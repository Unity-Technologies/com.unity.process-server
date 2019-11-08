using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpoiledCat.Logging;

namespace BaseTests
{

	public class TestThreadSynchronizationContext : SynchronizationContext
	{
		private readonly ConcurrentQueue<PostData> priorityQueue = new ConcurrentQueue<PostData>();
		private readonly ConcurrentQueue<PostData> queue = new ConcurrentQueue<PostData>();
		private readonly Task task;
		private readonly CancellationToken token;
		private int threadId;

		public TestThreadSynchronizationContext(CancellationToken token)
		{
			this.token = token;
			task = new Task(Start, token, TaskCreationOptions.LongRunning);
			task.Start();
		}

		public override void Post(SendOrPostCallback d, object state)
		{
			var data = new PostData { Completion = new ManualResetEventSlim(), Callback = d, State = state };
			queue.Enqueue(data);
		}

		public override void Send(SendOrPostCallback d, object state)
		{
			if (Thread.CurrentThread.ManagedThreadId == threadId)
			{
				d(state);
			}
			else
			{
				var data = new PostData { Completion = new ManualResetEventSlim(), Callback = d, State = state };
				priorityQueue.Enqueue(data);
				data.Completion.Wait(token);
			}
		}

		public void Pump()
		{
			PostData data;
			if (priorityQueue.TryDequeue(out data))
			{
				data.Run();
				data.Completion.Set();
			}
			if (queue.TryDequeue(out data))
			{
				data.Run();
				data.Completion.Set();
			}
		}

		private void Start()
		{
			SetSynchronizationContext(this);

			threadId = Thread.CurrentThread.ManagedThreadId;

			var lastTime = DateTime.Now.Ticks;
			var wait = new ManualResetEventSlim(false);
			var ticksPerFrame = TimeSpan.TicksPerMillisecond * 10;
			var count = 0;
			var secondStart = DateTime.Now.Ticks;

			while (!token.IsCancellationRequested)
			{
				var current = DateTime.Now.Ticks;
				count++;
				if (current - secondStart > TimeSpan.TicksPerMillisecond * 1000)
				{
					//Console.WriteLine(String.Format("FPS {0}", count));
					count = 0;
					secondStart = current;
				}

				Pump();

				lastTime = DateTime.Now.Ticks;
				long waitTime = (current + ticksPerFrame - lastTime) / TimeSpan.TicksPerMillisecond;
				if (waitTime > 0 && waitTime < int.MaxValue)
				{
					try
					{
						wait.Wait((int)waitTime, token);
					}
					catch {}
				}
			}
		}

		struct PostData
		{
			public ManualResetEventSlim Completion;
			public SendOrPostCallback Callback;
			public object State;

			public void Run()
			{
				if (Completion.IsSet)
					return;

				try
				{
					Callback(State);
				}
				catch { }
				Completion.Set();
			}
		}
	}
}

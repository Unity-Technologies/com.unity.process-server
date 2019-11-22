using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Unity.Editor.Tasks;
using Unity.Editor.Tasks.Extensions;
using Unity.Editor.Tasks.Logging;
using NUnit.Framework;

namespace BaseTests
{
	public partial class BaseTest
	{
		protected const int Timeout = 30000;
		protected const int RandomSeed = 120938;

		protected void StartTrackTime(Stopwatch watch, ILogging logger, string message = "")
		{
			if (!string.IsNullOrEmpty(message))
				logger.Trace(message);
			watch.Reset();
			watch.Start();
		}

		protected void StopTrackTimeAndLog(Stopwatch watch, ILogging logger)
		{
			watch.Stop();
			logger.Trace($"Time: {watch.ElapsedMilliseconds}");
		}

		protected ActionTask GetTask(ITaskManager taskManager, TaskAffinity affinity, int id, Action<int> body)
		{
			return new ActionTask(taskManager, _ => body(id)) { Affinity = affinity };
		}

		protected static IEnumerable<object> StartAndWaitForCompletion(params ITask[] tasks)
		{
			foreach (var task in tasks) task.Start();
			while (!tasks.All(x => x.Task.IsCompleted)) yield return null;
		}

		protected static IEnumerable<object> StartAndWaitForCompletion(IEnumerable<ITask> tasks)
		{
			foreach (var task in tasks) task.Start();
			while (!tasks.All(x => x.Task.IsCompleted)) yield return null;
		}

		protected static IEnumerable<object> WaitForCompletion(params ITask[] tasks)
		{
			while (!tasks.All(x => x.Task.IsCompleted)) yield return null;
		}

		protected static IEnumerable<object> WaitForCompletion(IEnumerable<ITask> tasks)
		{
			while (!tasks.All(x => x.Task.IsCompleted)) yield return null;
		}

		protected static IEnumerable<object> WaitForCompletion(IEnumerable<Task> tasks)
		{
			while (!tasks.All(x => x.IsCompleted)) yield return null;
		}

		protected static IEnumerable<object> WaitForCompletion(params Task[] tasks)
		{
			while (!tasks.All(x => x.IsCompleted)) yield return null;
		}
	}


	public static class TestExtensions
	{
		public static void Matches(this IEnumerable actual, IEnumerable expected)
		{
			CollectionAssert.AreEqual(expected, actual, $"{Environment.NewLine}expected:{expected.Join()}{Environment.NewLine}actual  :{actual.Join()}{Environment.NewLine}");
		}

		public static void Matches<T>(this IEnumerable<T> actual, IEnumerable<T> expected)
		{
			CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray(), $"{Environment.NewLine}expected:{expected.Join()}{Environment.NewLine}actual  :{actual.Join()}{Environment.NewLine}");
		}
	}

	static class KeyValuePair
	{
		public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value)
		{
			return new KeyValuePair<TKey, TValue>(key, value);
		}
	}

}

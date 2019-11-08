using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Threading;
using SpoiledCat.Logging;
using SpoiledCat.Threading;

namespace BaseTests
{
	public partial class BaseTest : IDisposable
	{
		public const bool TracingEnabled = false;

		public BaseTest()
		{
			LogHelper.LogAdapter = new NUnitLogAdapter();
			LogHelper.TracingEnabled = TracingEnabled;

			var syncContext = new TestThreadSynchronizationContext(default(CancellationToken));
			TaskManager = new TaskManager().Initialize(syncContext);

			LogHelper.Trace($"Starting test fixture. Main thread is {TaskManager.UIThread}");
		}

		public void Dispose()
		{
			TaskManager?.Dispose();
		}

		protected void StartTest(out Stopwatch watch, out ILogging logger, out ITaskManager taskManager, [CallerMemberName] string testName = "test")
		{
			logger = new LogFacade(testName, new NUnitLogAdapter(), TracingEnabled);
			watch = new Stopwatch();

			taskManager = TaskManager;

			logger.Trace("START");
			watch.Start();
		}

		protected void RunTest(Func<IEnumerator> testMethodToRun)
		{
			var test = testMethodToRun();
			while (test.MoveNext())
			{}
		}
	}
}

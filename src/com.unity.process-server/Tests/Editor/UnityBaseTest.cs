using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unity.Editor.Tasks;
using Unity.Editor.Tasks.Logging;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace BaseTests
{
	using System;

	// Unity does not support async/await tests, but it does
	// have a special type of test with a [CustomUnityTest] attribute
	// which mimicks a coroutine in EditMode. This attribute is
	// defined here so the tests can be compiled without
	// referencing Unity, and nunit on the command line
	// outside of Unity can execute the tests. Basically I don't
	// want to keep two copies of all the tests.
	public class CustomUnityTestAttribute : UnityTestAttribute
	{ }


	public partial class BaseTest : IDisposable
	{
		private LogAdapterBase existingLogger;
		private bool existingTracing;

		public BaseTest()
		{
			// set up the logger so it doesn't write exceptions to the unity log, the test runner doesn't like it
			existingLogger = LogHelper.LogAdapter;
			existingTracing = LogHelper.TracingEnabled;
			LogHelper.TracingEnabled = false;
			LogHelper.LogAdapter = new NullLogAdapter();
		}

		public void Dispose()
		{
			LogHelper.LogAdapter = existingLogger;
			LogHelper.TracingEnabled = existingTracing;
		}

		protected void StartTest(out Stopwatch watch, out ILogging logger, out ITaskManager taskManager, [CallerMemberName] string testName = "test")
		{
			taskManager = new TaskManager().Initialize();

			Debug.Log($"Starting test fixture. Main thread is {taskManager.UIThread}");

			logger = new LogFacade(testName, new UnityLogAdapter(), true);
			watch = new Stopwatch();

			logger.Trace("START");
			watch.Start();
		}

		protected void StopTest(Stopwatch watch, ILogging logger, ITaskManager taskManager)
		{
			watch.Stop();
			logger.Trace($"END:{watch.ElapsedMilliseconds}ms");
			taskManager.Dispose();
		}
	}
}

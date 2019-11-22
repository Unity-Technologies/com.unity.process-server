using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Threading;
using Unity.Editor.Tasks.Logging;
using Unity.Editor.Tasks;
using System.Threading.Tasks;

namespace BaseTests
{
    using SpoiledCat.Utilities;
    using Unity.Editor.ProcessServer.Internal.IO;

    public partial class BaseTest
	{
		public const bool TracingEnabled = false;

		public BaseTest()
		{
			LogHelper.LogAdapter = new NUnitLogAdapter();
			LogHelper.TracingEnabled = TracingEnabled;
		}

		internal TestData StartTest([CallerMemberName] string testName = "test") => new TestData(testName);

        internal class TestData : IDisposable
        {
            public readonly Stopwatch Watch;
            public readonly ILogging Logger;
            public readonly ITaskManager TaskManager;
            public readonly SPath TestPath;
            public readonly IEnvironment Environment;
            public readonly IProcessManager ProcessManager;
            public readonly string TestName;

            public TestData(string testName)
            {
                TestName = testName;
                Logger = new LogFacade(TestName, new NUnitLogAdapter(), TracingEnabled);
                Watch = new Stopwatch();
                TaskManager = new TaskManager();
                try
                {
                    TaskManager.Initialize();
                }
                catch
                {
                    // we're on the nunit sync context, which can't be used to create a task scheduler
                    // so use a different context as the main thread. The test won't run on the main nunit thread
                    TaskManager.Initialize(new MainThreadSynchronizationContext(TaskManager.Token));
                }

                TestPath = SPath.CreateTempDirectory(testName);
                Environment = new UnityEnvironment(testName);
                InitializeEnvironment();
                ProcessManager = new ProcessManager(Environment);

                Logger.Trace($"START {testName}");
                Watch.Start();
            }

            private void InitializeEnvironment()
            {
                var projectPath = TestPath.Combine("project").EnsureDirectoryExists();
                SPath unityPath, unityContentsPath;

                if (Environment.IsWindows)
                {
                    unityPath = unityContentsPath = TestPath.Combine("unity").EnsureDirectoryExists();
                }
                else
                {
                    unityPath = CurrentExecutionDirectory;

                    while (!unityPath.IsEmpty && !unityPath.DirectoryExists(".Editor"))
                        unityPath = unityPath.Parent;

                    if (!unityPath.IsEmpty)
                    {
                        unityPath = unityPath.Combine(".Editor");
                        unityContentsPath = unityPath.Combine("Data");
                    }
                    else
                    {
                        unityPath = unityContentsPath = TestPath.Combine("unity").EnsureDirectoryExists();
                        var ziphelper = new ZipHelper();
                        ziphelper.Extract(CurrentExecutionDirectory.Combine("Resources", "MonoBleedingEdge.zip"),
                            unityPath.Combine("MonoBleedingEdge").EnsureDirectoryExists(),
                            TaskManager.Token,
                            (_, __) => { },
                            (_, __, ___) => true);
                    }
                }

                Environment.Initialize(projectPath, "2019.2", unityPath, unityContentsPath);
            }


            public void Dispose()
            {
                Watch.Stop();
                Logger.Trace($"STOP {TestName} :{Watch.ElapsedMilliseconds}ms");
                ProcessManager.Dispose();
                TaskManager.Dispose();
                if (SynchronizationContext.Current is IMainThreadSynchronizationContext ourContext)
                    ourContext.Dispose();
            }

            internal SPath? testApp;

            internal SPath CurrentExecutionDirectory => System.Reflection.Assembly.GetExecutingAssembly().Location.ToSPath().Parent;

            internal SPath TestApp
            {
                get
                {
                    if (!testApp.HasValue)
                        testApp = CurrentExecutionDirectory.Combine("Helper.CommandLine.exe");
                    return testApp.Value;
                }
            }
        }

        protected async Task RunTest(Func<IEnumerator> testMethodToRun)
		{
			var scheduler = ThreadingHelper.GetUIScheduler(new ThreadSynchronizationContext(default));
			var taskStart = new Task<IEnumerator>(testMethodToRun);
			taskStart.Start(scheduler);
			var e = await RunOn(testMethodToRun, scheduler);
			while (await RunOn(s => ((IEnumerator)s).MoveNext(), e, scheduler))
			{ }
		}

		private Task<T> RunOn<T>(Func<T> method, TaskScheduler scheduler)
		{
			return Task<T>.Factory.StartNew(method, CancellationToken.None, TaskCreationOptions.None, scheduler);
		}

		private Task<T> RunOn<T>(Func<object, T> method, object state, TaskScheduler scheduler)
		{
			return Task<T>.Factory.StartNew(method, state, CancellationToken.None, TaskCreationOptions.None, scheduler);
		}
    }
}

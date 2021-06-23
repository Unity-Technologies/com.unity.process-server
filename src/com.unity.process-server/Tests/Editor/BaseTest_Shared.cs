using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.Editor.Tasks;
using Unity.ProcessServer.Internal.IO;

namespace BaseTests
{
    using System.Runtime.CompilerServices;
    using Unity.ProcessServer;
    using Unity.Editor.Tasks.Extensions;

    interface ILogging
    {
        bool TracingEnabled { get; set; }
        void Info(string message, params object[] objects);
        void Warn(string message, params object[] objects);
        void Error(string message, params object[] objects);
        void Trace(string message, params object[] objects);
    }

    class ServerConfiguration : Unity.ProcessServer.Server.ServerConfiguration, IRpcProcessConfiguration
    {
        public const string ProcessExecutableName = "Unity.ProcessServer.exe";

        public ServerConfiguration(string processServerDirectory)
        {
            ExecutablePath = processServerDirectory.ToSPath().Combine(ProcessExecutableName);
        }

        public string ExecutablePath { get; set; }
		public string RemoteProcessId { get; set; }
    }

    internal class TestData : IDisposable
    {
        public readonly Stopwatch Watch;
        public readonly ILogging Logger;
        public readonly SPath TestPath;
        public readonly string TestName;
        public readonly ITaskManager TaskManager;
        public readonly IEnvironment Environment;
        public readonly IProcessManager ProcessManager;
        public readonly ServerConfiguration Configuration;

        private MainThreadSynchronizationContext ourContext;

        public TestData(string testName, ILogging logger, string serverDirectory)
        {
            TestName = testName;
            Logger = logger;
            Watch = new Stopwatch();
            TestPath = SPath.CreateTempDirectory(testName);
            TaskManager = new TaskManager();

            try
            {
                TaskManager.Initialize();
            }
            catch
            {
                // we're on the nunit sync context, which can't be used to create a task scheduler
                // so use a different context as the main thread. The test won't run on the main nunit thread
                ourContext = new MainThreadSynchronizationContext(TaskManager.Token);
                TaskManager.Initialize(ourContext);
            }

            Environment = new UnityEnvironment(testName);
            InitializeEnvironment();
            ProcessManager = new ProcessManager(Environment);
            Configuration = new ServerConfiguration(serverDirectory);

            Logger.Trace($"START {testName}");
            Watch.Start();
        }

        private void InitializeEnvironment()
        {
            var projectPath = TestPath.Combine("project").EnsureDirectoryExists();

#if UNITY_EDITOR
			Environment.Initialize(projectPath, TheEnvironment.instance.Environment.UnityVersion, TheEnvironment.instance.Environment.UnityApplication, TheEnvironment.instance.Environment.UnityApplicationContents);
#else

            SPath unityPath, unityContentsPath;
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
                unityPath = unityContentsPath = SPath.Default;
            }

            Environment.Initialize(projectPath, "2019.2", unityPath, unityContentsPath);
#endif
        }

        public void Dispose()
        {
            Watch.Stop();
            ProcessManager.Dispose();
            TaskManager.Dispose();
            ourContext?.Dispose();
            Logger.Trace($"STOP {TestName} :{Watch.ElapsedMilliseconds}ms");
        }

        internal SPath CurrentExecutionDirectory => System.Reflection.Assembly.GetExecutingAssembly().Location.ToSPath().Parent;
    }


    public partial class BaseTest
	{
		protected const int Timeout = 30000;
		protected const int RandomSeed = 120938;

        internal TestData StartTest([CallerMemberName] string testName = "test") => new TestData(testName, new NUnitLogger(testName), ServerDirectory);

        internal void StartTrackTime(Stopwatch watch, ILogging logger, string message = "")
		{
			if (!string.IsNullOrEmpty(message))
				logger.Trace(message);
			watch.Reset();
			watch.Start();
		}

        internal void StopTrackTimeAndLog(Stopwatch watch, ILogging logger)
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

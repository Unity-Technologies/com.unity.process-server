using NUnit.Framework;
using System.Threading.Tasks;
using Unity.Editor.ProcessServer;
using Unity.Editor.ProcessServer.Internal.IO;
using Unity.Editor.Tasks;

namespace BaseTests
{
    using System;
    using System.Collections;
    using System.Threading;
    using Unity.Editor.ProcessServer.Interfaces;

    public partial class ProcessServerTests : BaseTest
    {
        [CustomUnityTest]
        public IEnumerator CanRun()
        {
            using (var test = StartTest())
            {
                var task = new IpcServerTask(test.TaskManager, test.ProcessManager,
                        new ServerConfiguration(ServerDirectory), CancellationToken.None) { Affinity = TaskAffinity.None }
                    .RegisterRemoteTarget<IServer>();

                foreach (var frame in WaitForCompletion(task.StartAwait())) yield return frame;

                var client = task.Result;
                Assert.NotNull(client);

                var expected = 0;
                var actual = client.Configuration.Port;

                var stopTask = client.GetRemoteTarget<IServer>().Stop();
                foreach (var frame in WaitForCompletion(stopTask)) yield return frame;

                Assert.Greater(actual, expected);
            }
        }

        [CustomUnityTest]
        public IEnumerator CanStartAndShutdown()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment,
                new ServerConfiguration(ServerDirectory)))
            {
                var connectTask = processServer.Connect();
                foreach (var frame in WaitForCompletion(connectTask)) yield return frame;

                var shutdownTask = processServer.Shutdown();
                var wait = Task.WhenAny(shutdownTask, Task.Delay(2000));
                foreach (var frame in WaitForCompletion(wait)) yield return frame;

                Assert.True(wait.Result == shutdownTask, "Server did not shutdown on time");
            }
        }

        [CustomUnityTest]
        public IEnumerator CanExecuteProcessRemotely()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment,
                new ServerConfiguration(ServerDirectory)))
            {
                var task = processServer.NewDotNetProcess(TestApp, "-d done",
                    outputProcessor: new FirstNonNullLineOutputProcessor<string>())
                                        .Start();

                var timeout = Task.Delay(4000);
                var wait = Task.WhenAny(task.Task, timeout);

                foreach (var frame in WaitForCompletion(wait)) yield return frame;

                Assert.True(wait.Result != timeout, "The process did not complete on time");
                Assert.AreEqual("done", task.Result);
            }
        }

        [CustomUnityTest]
        public IEnumerator CanKeepDotNetProcessAlive()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment,
                new ServerConfiguration(ServerDirectory)))
            {
                var restartCount = 0;
                var restarted = new TaskCompletionSource<ProcessRestartReason>();

                processServer.ProcessManager.OnProcessRestart += (sender, args) => {
                    restartCount++;
                    if (restartCount == 2)
                        restarted.TrySetResult(args.Reason);
                };

                var task = processServer.NewDotNetProcess(TestApp, "-s 200", new ProcessOptions(MonitorOptions.KeepAlive),
                    t => t.Detach()
                    ).Start();

                var timeout = Task.Delay(4000);
                var wait = Task.WhenAny(task.Task, timeout);
                foreach (var frame in WaitForCompletion(wait)) yield return frame;
                Assert.True(wait.Result != timeout, "Detach did not happen on time");

                timeout = Task.Delay(1000);
                wait = Task.WhenAny(task.Task, timeout);
                foreach (var frame in WaitForCompletion(wait)) yield return frame;
                Assert.True(wait.Result != timeout, "Restart did not happen on time");
            }
        }

        [CustomUnityTest]
        public IEnumerator CanExecuteAndRestartProcess()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment,
                new ServerConfiguration(ServerDirectory)))
            {
                processServer.ConnectSync();

                var task = new ProcessTask<string>(test.TaskManager,
                            test.ProcessManager.DefaultProcessEnvironment,
                            TestApp, "-s 100", new SimpleOutputProcessor()) { Affinity = TaskAffinity.None };
                task.Configure(processServer.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive, true));

                var restartCount = 0;

                var restarted = new TaskCompletionSource<ProcessRestartReason>();
                processServer.ProcessManager.OnProcessRestart += (sender, args) => {
                    restartCount++;
                    if (restartCount == 2)
                        restarted.TrySetResult(args.Reason);
                };

                task.Start();

                var timeout = Task.Delay(4000);
                var wait = Task.WhenAny(restarted.Task, timeout);
                foreach (var frame in WaitForCompletion(wait)) yield return frame;

                Assert.True(wait.Result != timeout, "Restart did not happen on time");

                task.Detach();

                Assert.AreEqual(ProcessRestartReason.KeepAlive, restarted.Task.Result);
            }
        }

        class TestProcessServer : Unity.Editor.ProcessServer.ProcessServer, IDisposable
        {
            public TestProcessServer(ITaskManager taskManager,
                IEnvironment environment,
                IProcessServerConfiguration configuration)
                : base(taskManager, environment, configuration)
            {
            }

            protected override void Dispose(bool disposing)
            {
                if (ShuttingDown)
                {
                    base.Dispose(disposing);
                    return;
                }
                try
                {
                    ShutdownSync();
                }
                catch
                {}
            }
        }
    }
}

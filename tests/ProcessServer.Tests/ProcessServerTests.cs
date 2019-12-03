using NUnit.Framework;

namespace BaseTests
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Unity.Editor.ProcessServer;
    using Unity.Editor.ProcessServer.Extensions;
    using Unity.Editor.ProcessServer.Interfaces;
    using Unity.Editor.ProcessServer.Internal.IO;
    using Unity.Editor.Tasks;

    [TestFixture]
    public class ProcessServerTests : BaseTest
    {
        internal SPath TestAssemblyLocation => System.Reflection.Assembly.GetExecutingAssembly().Location.ToSPath().Parent;

        class ServerConfiguration : Unity.Ipc.Configuration, IProcessServerConfiguration
        {
            public const string ProcessExecutableName = "processserver.exe";

            public ServerConfiguration(string processServerDirectory)
            {
                ExecutablePath = processServerDirectory.ToSPath().Combine(ProcessExecutableName);
            }

            public string ExecutablePath { get; set; }
        }

        [Test]
        public async Task CanRun()
        {
            using (var test = StartTest())
            {
                var task = new ProcessManagerTask(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration(TestAssemblyLocation));

                var port = await task.StartAwait();

                var process = task.Process;
                process.Kill();
                process.WaitForExit(500);
                process.Close();

                Assert.Greater(port, 40000);
            }
        }

        [Test]
        public async Task CanStartAndShutdown()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString())))
            {
                processServer.Connect();
                processServer.Stop();
                var shutdown = await processServer.Completion.Task;
                Assert.True(shutdown, "Server did not shutdown on time");
            }
        }

        [Test]
        public async Task CanExecuteProcessRemotely()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString()) ))
            {
                processServer.Connect();

                var ret = await new ProcessTask<string>(test.TaskManager,
                                    test.ProcessManager.DefaultProcessEnvironment,
                                    test.TestApp, "-d done",
                                    outputProcessor: new FirstNonNullLineOutputProcessor<string>())
                                .Configure(processServer)
                                .StartAwait().Timeout(3000, "The process did not finish on time");

                Assert.AreEqual("done", ret);

                processServer.Stop();
                await processServer.Completion.Task.Timeout(100, "The server did not stop on time");
            }
        }

        [Test]
        public async Task CanExecuteAndRestartProcess()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString())))
            {
                processServer.Connect();

                var task = new ProcessTask<string>(test.TaskManager,
                            test.ProcessManager.DefaultProcessEnvironment,
                            test.TestApp, "-s 100", new SimpleOutputProcessor())
                    .Configure(processServer, new ProcessOptions(MonitorOptions.KeepAlive, true));

                var restartCount = 0;

                var restarted = new TaskCompletionSource<ProcessRestartReason>();
                processServer.OnProcessRestart += (sender, args) => {
                    restartCount++;
                    if (restartCount == 2)
                        restarted.TrySetResult(args.Reason);
                };

                task.Start();

                var reason = await restarted.Task.Timeout(3000, "Restart did not happen on time");

                task.Detach();

                Assert.AreEqual(ProcessRestartReason.KeepAlive, reason);

                processServer.Stop();
                await processServer.Completion.Task.Timeout(100, "The server did not stop on time");
            }
        }

        class TestProcessServer : ProcessServer
        {
            private Process process;

            public TestProcessServer(ITaskManager taskManager,
                IProcessManager processManager,
                IEnvironment environment,
                IProcessServerConfiguration configuration)
                : base(taskManager, processManager, environment, configuration)
            {}

            protected override ITask<int> RunProcessServer(string pathToServerExecutable)
            {
                var task = base.RunProcessServer(pathToServerExecutable);
                process = ((IProcessTask<int>)task).Wrapper.Process;
                return task;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.Close();
                    }
                }
                catch
                {}
            }
        }
    }
}

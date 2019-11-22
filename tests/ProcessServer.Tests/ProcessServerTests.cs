using NUnit.Framework;

namespace BaseTests
{
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Unity.Editor.ProcessServer;
    using Unity.Editor.ProcessServer.Interfaces;
    using Unity.Editor.ProcessServer.Internal.IO;
    using Unity.Editor.Tasks;

    [TestFixture]
    public class ProcessServerTests : BaseTest
    {
        internal SPath TestAssemblyLocation => System.Reflection.Assembly.GetExecutingAssembly().Location.ToSPath().Parent;

        class ServerConfiguration : Unity.Ipc.Configuration, IProcessServerConfiguration
        {

        }

        [Test]
        public async Task CanRun()
        {
            using (var test = StartTest())
            {
                var task = new ProcessManagerTask(test.TaskManager, test.ProcessManager, TestAssemblyLocation, test.Environment.UnityProjectPath);

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
            using (var processServer = new TestProcessServer(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration()))
            {
                processServer.Initialize(TestAssemblyLocation.ToString());

                var shutdown = await processServer.ShutdownServer().StartAwait();
                Assert.True(shutdown, "Server did not shutdown on time");
            }
        }

        [Test]
        public async Task CanExecuteProcessRemotely()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration() ))
            {
                processServer.Initialize(TestAssemblyLocation.ToString());

                var ret = await new RemoteProcessTask<string>(test.TaskManager,
                                    test.ProcessManager.DefaultProcessEnvironment,
                                    processServer, test.TestApp, "-d done",
                                    outputProcessor: new FirstNonNullLineOutputProcessor<string>())
                                .Configure(test.ProcessManager)
                                .StartAwait();

                Assert.AreEqual("done", ret);

                var shutdown = await processServer.ShutdownServer().StartAwait();
                Assert.True(shutdown, "Server did not shutdown on time");
            }
        }

        [Test]
        public async Task CanExecuteAndRestartProcess()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration()))
            {
                processServer.Initialize(TestAssemblyLocation.ToString());

                var task = new RemoteProcessTask(test.TaskManager,
                            test.ProcessManager.DefaultProcessEnvironment,
                            processServer, test.TestApp, "-l 10 -d 1 -s 100",
                            new ProcessOptions(MonitorOptions.KeepAlive, true))
                        .Configure(test.ProcessManager);

                task.OnOutput += s => {
                    if (s == "1")
                    {
                        task.Detach();
                    }
                };

                var restarted = new TaskCompletionSource<ProcessRestartReason>();
                processServer.OnProcessRestart += (sender, args) => {
                    restarted.SetResult(args.Reason);
                };

                var ret = await task.StartAwait();
                var reason = await restarted.Task;

                Assert.AreEqual(ProcessRestartReason.KeepAlive, reason);

                var shutdown = await processServer.ShutdownServer().StartAwait();
                Assert.True(shutdown, "Server did not shutdown on time");
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

            protected override Process RunProcessServer(string pathToServerExecutable)
            {
                process = base.RunProcessServer(pathToServerExecutable);
                return process;
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

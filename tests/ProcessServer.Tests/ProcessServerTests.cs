using NUnit.Framework;
using NSubstitute;
using System.Threading.Tasks;
using Unity.Editor.ProcessServer;
using Unity.Editor.ProcessServer.Interfaces;
using Unity.Editor.ProcessServer.Internal.IO;
using Unity.Editor.Tasks;
using Unity.Ipc;
using Unity.Editor.Tasks.Extensions;
using System.Diagnostics;

namespace BaseTests
{

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
                using (var task = new ProcessManagerTask(test.TaskManager, test.ProcessManager, test.Environment, new ServerConfiguration(TestAssemblyLocation)))
                {
                    var port = await task.StartAwait();
                    Assert.Greater(port, 40000);

                    var id = task.ProcessId;
                    var p = Process.GetProcessById(id);
                    p.Kill();
                    p.WaitForExit();
                    p.Close();
                }
            }
        }

        [Test]
        public async Task CanStartAndShutdown()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString())))
            {
                processServer.Connect();
                processServer.Stop();
                var shutdown = processServer.Completion.WaitOne(1000);
                Assert.True(shutdown, "Server did not shutdown on time");
            }
        }

        [Test]
        public async Task CanExecuteProcessRemotely()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString()) ))
            {
                processServer.Connect();

                var ret = await new ProcessTask<string>(test.TaskManager,
                                    test.ProcessManager.DefaultProcessEnvironment,
                                    TestApp, "-d done",
                                    outputProcessor: new FirstNonNullLineOutputProcessor<string>())
                                .Configure(processServer.ProcessManager)
                                .StartAwait().Timeout(10000, "The process did not finish on time");

                Assert.AreEqual("done", ret);

                processServer.Stop();
                Assert.True(processServer.Completion.WaitOne(100), "The server did not stop on time");
            }
        }

        [Test]
        public async Task Server_CanRestartProcess()
        {
            using (var test = StartTest())
            {
                var runner = new Unity.Editor.ProcessServer.Server.ProcessRunner(test.TaskManager, test.ProcessManager,
                    test.ProcessManager.DefaultProcessEnvironment, test.Environment, null, null);

                var notifications = Substitute.For<IServerNotifications>();
                var client = Substitute.For<IRequestContext>();
                client.GetRemoteTarget<IServerNotifications>().Returns(notifications);

                string id = runner.Prepare(client, "where", "git", new ProcessOptions { MonitorOptions = MonitorOptions.KeepAlive });

                var task = runner.RunProcess(id);

                await task.Task;

                await Task.Delay(100);
                await notifications.Received().ProcessRestarting(runner.GetProcess(id), ProcessRestartReason.KeepAlive);

                await runner.Stop(id).Task;                
            }
        }

        [Test]
        public async Task CanExecuteAndRestartProcess()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString())))
            {
                processServer.Connect();

                try
                {
                    var task = new ProcessTask<string>(test.TaskManager,
                                test.ProcessManager.DefaultProcessEnvironment,
                                TestApp, "-s 100", new SimpleOutputProcessor())
                        .Configure(processServer.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive, true));

                    var restartCount = 0;

                    var restarted = new TaskCompletionSource<ProcessRestartReason>();
                    processServer.ProcessManager.OnProcessRestart += (sender, args) => {
                        restartCount++;
                        if (restartCount == 2)
                            restarted.TrySetResult(args.Reason);
                    };

                    task.Start();

                    var reason = await restarted.Task.Timeout(60000, "Restart did not happen on time");

                    task.Detach();

                    Assert.AreEqual(ProcessRestartReason.KeepAlive, reason);
                }
                finally
                {
                    processServer.Stop();
                    Assert.True(processServer.Completion.WaitOne(100), "The server did not stop on time");
                }
            }
        }

        class TestProcessServer : ProcessServer
        {
            private BaseProcessWrapper process;

            public TestProcessServer(ITaskManager taskManager,
                IEnvironment environment,
                IProcessServerConfiguration configuration)
                : base(taskManager, environment, configuration)
            {}

            protected override ITask<int> RunProcessServer(string pathToServerExecutable)
            {
                var task = base.RunProcessServer(pathToServerExecutable);
                process = ((IProcessTask)task).Wrapper;
                return task;
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try
                {
                    if (!process.HasExited)
                    {
                        process.Stop();
                        process.Dispose();
                    }
                }
                catch
                {}
            }
        }
    }
}

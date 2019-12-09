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
    using System;
    using System.Threading;
    using Microsoft.Extensions.Logging;
    using Unity.Editor.ProcessServer.Server;

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

                var task = new IpcServerTask(test.TaskManager, test.ProcessManager, new ServerConfiguration(TestAssemblyLocation), CancellationToken.None)
                    .RegisterRemoteTarget<IServer>();

                var client = await task.StartAwait();
                Assert.Greater(client.Configuration.Port, 40000);

                await client.GetRemoteTarget<IServer>().Stop();
            }
        }

        [Test]
        public async Task CanStartAndShutdown()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString())))
            {
                await processServer.Connect();
                await processServer.Shutdown();
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
                var task = new DotNetProcessTask<string>(test.TaskManager,
                                    test.ProcessManager.DefaultProcessEnvironment,
                                    test.Environment,
                                    TestApp, "-d done",
                                    outputProcessor: new FirstNonNullLineOutputProcessor<string>());

                task.Configure(processServer.ProcessManager);
                task.OnOutput += s => test.Logger.Info(s);
                var ret = await task.StartAwait().Timeout(10000, "The process did not finish on time");


                Assert.AreEqual("done", ret);
            }
        }

        [Test]
        public async Task CanKeepDotNetProcessAlive()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString())))
            {
                var ret = new DotNetProcessTask(test.TaskManager,
                                    processServer.ProcessManager,
                                    TestApp, "-s 200");

                ret.Configure(processServer.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive));
                ret.OnStartProcess += _ => ret.Detach();

                var restartCount = 0;
                var restarted = new TaskCompletionSource<ProcessRestartReason>();

                processServer.ProcessManager.OnProcessRestart += (sender, args) => {
                    restartCount++;
                    if (restartCount == 2)
                        restarted.TrySetResult(args.Reason);
                };

                await ret.StartAwait().Timeout(2000, "Detach did not happen on time");

                var reason = await restarted.Task.Timeout(1000, "Restart did not happen on time");
            }
        }

        [Test]
        public async Task Server_CanRestartProcess()
        {
            using (var test = StartTest())
            {
                using (var runner = new ProcessRunner(test.TaskManager, test.ProcessManager,
                    test.ProcessManager.DefaultProcessEnvironment, Substitute.For<ILogger<ProcessRunner>>()))
                {

                    var notifications = Substitute.For<IServerNotifications>();
                    var client = Substitute.For<IRequestContext>();
                    client.GetRemoteTarget<IServerNotifications>().Returns(notifications);

                    string id = runner.Prepare(client, "where", "git", new ProcessOptions { MonitorOptions = MonitorOptions.KeepAlive });

                    var task = runner.RunProcess(id);

                    await task.Task;

                    await Task.Delay(100);
                    await notifications.Received().ProcessRestarting(runner.GetProcess(id), ProcessRestartReason.KeepAlive);

                    await runner.StopProcess(id).Task;
                }
            }
        }

        [Test]
        public async Task CanExecuteAndRestartProcess()
        {
            using (var test = StartTest())
            using (var processServer = new TestProcessServer(test.TaskManager, test.Environment, new ServerConfiguration(TestAssemblyLocation.ToString())))
            {
                var task = new ProcessTask<string>(test.TaskManager,
                            test.ProcessManager.DefaultProcessEnvironment,
                            TestApp, "-s 100", new SimpleOutputProcessor());
                task.Configure(processServer.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive, true));

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
        }

        class TestProcessServer : Unity.Editor.ProcessServer.ProcessServer, IDisposable
        {
            public TestProcessServer(ITaskManager taskManager,
                IEnvironment environment,
                IProcessServerConfiguration configuration)
                : base(taskManager, environment, configuration)
            {}

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
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

using NUnit.Framework;
using System.Threading.Tasks;

namespace BaseTests
{
    using Microsoft.Extensions.Logging;
    using NSubstitute;
    using Unity.ProcessServer.Interfaces;
    using Unity.ProcessServer.Server;
    using Unity.Ipc;

    public partial class ProcessServerTests : BaseTest
    {
        [Test]
        public async Task CanRun_()
        {
            await RunTest(CanRun);
        }

        [Test]
        public async Task CanStartAndShutdown_()
        {
            await RunTest(CanStartAndShutdown);
        }

        [Test]
        public async Task CanExecuteProcessRemotely_()
        {
            await RunTest(CanExecuteProcessRemotely);
        }

        [Test]
        public async Task CanKeepDotNetProcessAlive_()
        {
            await RunTest(CanKeepDotNetProcessAlive);
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

                    await runner.RunProcess(id).Task;

                    await Task.Delay(100);
                    await notifications.Received().ProcessRestarting(runner.GetProcess(id), ProcessRestartReason.KeepAlive);
                    await runner.StopProcess(id).Task;
                }
            }
        }

        [Test]
        public async Task CanExecuteAndRestartProcess_()
        {
            await RunTest(CanExecuteAndRestartProcess);
        }
    }
}

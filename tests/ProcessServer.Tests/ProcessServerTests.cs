using NUnit.Framework;
using System.Threading.Tasks;

namespace BaseTests
{
    using Microsoft.Extensions.Logging;
    using NSubstitute;
    using Unity.ProcessServer.Interfaces;
    using Unity.ProcessServer.Server;
    using Unity.Rpc;

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
        public async Task CanReplay_()
        {
	        await RunTest(CanReplay);
        }


        [Test]
        public async Task CanReconnectToKeepAliveProcess_()
        {
	        await RunTest(CanReconnectToKeepAliveProcess);
        }

        [Test]
        public async Task CanStopKeepAliveProcessManually_()
        {
	        await RunTest(CanStopKeepAliveProcessManually);
        }

        [Test]
        public async Task Server_CanRestartProcess()
        {
            using (var test = StartTest())
            {
                using (var runner = new ProcessRunner(test.TaskManager, test.ProcessManager,
                    test.ProcessManager.DefaultProcessEnvironment, test.Configuration, Substitute.For<ILogger<ProcessRunner>>()))
                {

                    var notifications = Substitute.For<IServerNotifications>();
                    var client = Substitute.For<IRequestContext>();
                    client.GetRemoteTarget<IServerNotifications>().Returns(notifications);

                    string id = runner.Prepare(client, "where", "git", new ProcessOptions { MonitorOptions = MonitorOptions.KeepAlive }, null, test.Configuration.AccessToken);

                    await runner.RunProcess(id, test.Configuration.AccessToken).Task;

                    await Task.Delay(100);
                    await notifications.Received().ProcessRestarting(Arg.Any<RpcProcess>(), ProcessRestartReason.KeepAlive);
                    await runner.StopProcess(id, test.Configuration.AccessToken).Task;
                }
            }
        }

        [Test]
        public async Task CanExecuteAndRestartProcess_()
        {
            await RunTest(CanExecuteAndRestartProcess);
        }

        [Test]
        public async Task CanValidateAccessToken_()
        {
            await RunTest(CanValidateAccessToken);
        }
    }
}

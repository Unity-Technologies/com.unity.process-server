using System;

namespace ClientTestApp
{
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Unity.Editor.ProcessServer;
    using Unity.Editor.ProcessServer.Interfaces;
    using Unity.Editor.Tasks;
    using SpoiledCat.SimpleIO;
    using System.Reflection;

    class ServerConfiguration : Unity.Ipc.Configuration, IProcessServerConfiguration
    {
        public const string ProcessExecutableName = "processserver.exe";

        public ServerConfiguration(SPath processServerDirectory)
        {
            ExecutablePath = processServerDirectory.Combine(ProcessExecutableName);
        }

        public string ExecutablePath { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            var buildDir = Assembly.GetExecutingAssembly().Location.ToSPath().Parent;
            while (!buildDir.IsEmpty && buildDir.FileNameWithoutExtension != "build")
            {
                buildDir = buildDir.Parent;
            }

            buildDir = buildDir.Combine("packages/com.unity.process-server/Server~");

            var server = await ProcessServer.Get(configuration: new ServerConfiguration(buildDir));

            Console.WriteLine("Got server, hit any key to continue");
            Console.ReadLine();
            var ret = new DotNetProcessTask(server.TaskManager,
                server.Environment,
                "../../Helper.CommandLine/Debug/net471/Helper.CommandLine.exe", "-s 200");

            try
            {
                var restarted = new TaskCompletionSource<ProcessRestartReason>();

                ret.Configure(server.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive));
                ret.OnStartProcess += _ => ret.Detach();
                ret.OnEnd += (_, __, ___, ____) => restarted.TrySetResult(ProcessRestartReason.FirstStart);

                var restartCount = 0;

                server.ProcessManager.OnProcessRestart += (sender, e) => {
                    ret.Detach();
                    restartCount++;
                    if (restartCount == 2)
                        restarted.TrySetResult(e.Reason);
                };

                ret.Start();

                var reason = await restarted.Task;

                Console.WriteLine("restarted");
                Console.ReadLine();

                //ret = new DotNetProcessTask(server.TaskManager,
                //    server.Environment,
                //    "../../Helper.CommandLine/Debug/net471/Helper.CommandLine.exe", "-s 200");
                //ret.Configure(server.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive));

                ret = new DotNetProcessTask(server.TaskManager,
                    server.ProcessManager,
                    "../../Helper.CommandLine/Debug/net471/Helper.CommandLine.exe", "-d 1");

                ret.OnOutput += line => { Debugger.Break(); };

                await ret.StartAwait();

                Console.ReadLine();
            }
            finally
            {
                ret.Dispose();
                server.Stop();
            }
        }
    }
}

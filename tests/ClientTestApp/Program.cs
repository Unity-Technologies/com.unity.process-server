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

            var server = ProcessServer.Get(configuration: new ServerConfiguration(buildDir));

            var app = buildDir.Combine("tests/bin/Helper.CommandLine/Debug/net471/Helper.CommandLine.ex");

            Console.WriteLine("Got server, hit any key to continue");
            Console.ReadLine();

            var ret = new DotNetProcessTask(server.TaskManager,
                server.Environment,
                app, "-s 200") { Affinity = TaskAffinity.None };

            var restarted = new TaskCompletionSource<ProcessRestartReason>();

            ret.Configure(server.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive));
            ret.OnStartProcess += _ => ret.Detach();

            var restartCount = 0;

            server.ProcessManager.OnProcessRestart += (sender, e) => {
                ret.Detach();
                restartCount++;
                if (restartCount == 2)
                    restarted.TrySetResult(e.Reason);
            };


            var done = await Task.WhenAny(restarted.Task, ret.Finally((_, __) => { }).Start().Task);

            if (ret.Successful)
                Console.WriteLine($"restarted {restartCount} times");
            else
                Console.WriteLine($"process failed {ret.Exception}");

            Console.WriteLine("Running app with data");
            Console.ReadLine();

            //ret = new DotNetProcessTask(server.TaskManager,
            //    server.Environment,
            //    "../../Helper.CommandLine/Debug/net471/Helper.CommandLine.exe", "-s 200");
            //ret.Configure(server.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive));

            ret = new DotNetProcessTask(server.TaskManager,
                server.ProcessManager,
                app, "-d 1") { Affinity = TaskAffinity.None };

            ret.OnOutput += line => { Debugger.Break(); };

            var data = await ret.Finally((_, __, r) => r).Start().Task;

            if (!ret.Successful)
                Console.WriteLine($"process failed {ret.Exception}");
            else
                Console.WriteLine($"data: {data}");

            Console.WriteLine("Press any key to exit");
            Console.ReadLine();

            ret.Dispose();
            await server.Stop();
        }
    }
}

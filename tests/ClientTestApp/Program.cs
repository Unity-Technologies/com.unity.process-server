using System;

namespace ClientTestApp
{
	using System.Collections.Generic;
	using System.Diagnostics;
    using System.Threading.Tasks;
    using Unity.ProcessServer;
    using Unity.ProcessServer.Interfaces;
    using Unity.Editor.Tasks;
    using SpoiledCat.SimpleIO;
    using System.Reflection;

    class ServerConfiguration : Unity.Rpc.Configuration, IRpcProcessConfiguration
    {
        public const string ProcessExecutableName = "Unity.ProcessServer.exe";

        public ServerConfiguration(SPath processServerDirectory)
        {
            ExecutablePath = processServerDirectory.Combine(ProcessExecutableName);
        }

        public string ExecutablePath { get; set; }
		public string RemoteProcessId { get; set; }
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

            var app = buildDir.Combine("tests/bin/Helper.CommandLine/Debug/net471/Helper.CommandLine.exe");
            if (!app.FileExists())
                app = buildDir.Combine("tests/bin/Helper.CommandLine/Release/net471/Helper.CommandLine.exe");
            buildDir = buildDir.Combine("packages/com.unity.process-server/Server~");

            var server = ProcessServer.Get(configuration: new ServerConfiguration(buildDir));
            await server.Connect();

            Console.WriteLine("Got server, hit any key to continue");
            Console.ReadLine();

            //var ret = new DotNetProcessTask(server.TaskManager,
            // server.Environment,
            // app, "-d done") { Affinity = TaskAffinity.None };
            //ret.Configure(server.ProcessManager);

            //ret.OnStartProcess += _ => { Console.WriteLine("OnStartProcess"); };
            //ret.OnEndProcess += _ => { Console.WriteLine("OnEndProcess"); };
            //ret.OnOutput += s => { Console.WriteLine(s); };

            //await ret.StartAwait();

            //var ret = new DotNetProcessTask(server.TaskManager,
            //    server.Environment,
            //    app, "-s 200") { Affinity = TaskAffinity.None };

            //var restarted = new TaskCompletionSource<ProcessRestartReason>();

            //ret.Configure(server.ProcessManager, new ProcessOptions(MonitorOptions.KeepAlive));
            //ret.OnStartProcess += _ => ret.Detach();

            //var restartCount = 0;

            //server.ProcessManager.OnProcessRestart += (sender, e) => {
            //    ret.Detach();
            //    restartCount++;
            //    if (restartCount == 2)
            //        restarted.TrySetResult(e.Reason);
            //};


            //var done = await Task.WhenAny(restarted.Task, ret.Finally((_, __) => { }).Start().Task);

            //if (ret.Successful)
            //    Console.WriteLine($"restarted {restartCount} times");
            //else
            //    Console.WriteLine($"process failed {ret.Exception}");

            //Console.WriteLine("Running app with data");
            //Console.ReadLine();

            //ret = new DotNetProcessTask(server.TaskManager,
            //    server.ProcessManager,
            //    app, "-d 1") { Affinity = TaskAffinity.None };

            //ret.OnStartProcess += (a1) => {
            //    Console.WriteLine($"OnStartProcess {a1}");
            //};
            //ret.OnOutput += line => { Console.WriteLine(line); };
            //ret.OnEndProcess += (a1) => {
            //    Console.WriteLine($"OnEndProcess {a1}");
            //};

            //var data = await ret.Finally((_, __, r) => r).Start().Task;

            //if (!ret.Successful)
            //    Console.WriteLine($"process failed {ret.Exception}");
            //else
            //    Console.WriteLine($"data: {data}");


            //Console.WriteLine("Reconnecting to keep alive");
            //Console.ReadLine();

            var expectedId = 0;
            var expectedOutput = new List<string>();
            var actualId = 0;
            var actualOutput = new List<string>();

            var task = server.NewDotNetProcess(app, "-d done -b", new ProcessOptions(MonitorOptions.KeepAlive),
             onStart: t => {
                 Console.WriteLine($"onStart 1 called with pid {t.ProcessId}");
                 expectedId = t.ProcessId;
             },
             onOutput: (t, s) => {
                 Console.WriteLine($"OnOutput 1 {t.ProcessId} {s}");
                 expectedOutput.Add(s);
                 t.Detach();
             });

            await task.StartAwait();

            task.Dispose();

            task = server.NewDotNetProcess(app, "-d done -b", new ProcessOptions(MonitorOptions.KeepAlive),
                onStart: t => {
                    Console.WriteLine($"onStart 2 called with pid {t.ProcessId}");
                    actualId = t.ProcessId;
                },
                onOutput: (t, s) => {
                    Console.WriteLine($"OnOutput 2 {t.ProcessId} {s}");
                    actualOutput.Add(s);
                    t.Detach();
                });

            await task.StartAwait();

            Console.WriteLine("Press any key to exit");
            Console.ReadLine();

            //ret.Dispose();
            await server.Shutdown();
        }
    }
}

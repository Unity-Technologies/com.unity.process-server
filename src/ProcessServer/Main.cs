﻿using System.Threading;
using System.Threading.Tasks;
using System;

namespace Unity.ProcessServer.Server
{
    using System.Diagnostics;
    using Editor.Tasks;
    using Interfaces;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Rpc.Hosted;
    using Rpc.Hosted.Extensions;
    using Serilog;
    using Serilog.Core;
    using Serilog.Events;
    using SpoiledCat.Extensions.Configuration;

    static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // monitoring when the ipc host shuts down
            var exiting = new ManualResetEventSlim();

            var configuration = GetConfiguration(args);

            if (configuration.Debug)
            {
                if (!Debugger.IsAttached)
                    Debugger.Launch();
                else
                    Debugger.Break();
            }

            var taskManager = new TaskManager();
            var syncContext = new ThreadSynchronizationContext(taskManager.Token);
            taskManager.Initialize(syncContext);

            var environment = new UnityEnvironment("Process Manager")
                .Initialize(configuration.ProjectPath, configuration.UnityVersion,
                    configuration.UnityApplicationPath, configuration.UnityContentsPath);


            var host = new RpcHostedServer(configuration)
                       .AddRemoteProxy<IServerNotifications>()
                       .AddRemoteProxy<IProcessNotifications>()
                       .AddLocalTarget<ProcessServer.Implementation>()
                       .AddLocalScoped<ProcessRunner.Implementation>()
                       ;

            host.Stopping(s => {
                    s.GetService<ProcessRunner>().Shutdown();
                    s.GetService<ProcessServer>().Shutdown();
                    exiting.Set();
                })
                .ClientConnecting(s => {

                    // keep track of clients so we can broadcast notifications to them
                    s.GetService<ProcessServer>().ClientConnecting(s.GetRequestContext());

                })
                .ClientDisconnecting((s, disconnected) => {
                    s.GetService<ProcessRunner>().ClientDisconnecting(s.GetRequestContext());
                    s.GetService<ProcessServer>().ClientDisconnecting(s.GetRequestContext());
                });

            // set up a logger
            var logLevelSwitch = new LoggingLevelSwitch { MinimumLevel = LogEventLevel.Debug };
            host.UseSerilog((context, config) =>
                config.MinimumLevel.ControlledBy(logLevelSwitch)
                      .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                      .Enrich.FromLogContext()
                      .WriteTo.Console());

            host.ConfigureServices(services => {

	            // register the log switch so it can be retrieved and changed by any code
	            services.AddSingleton(logLevelSwitch);

	            // make the configuration available to everyone
	            services.AddSingleton(configuration);
	            services.AddSingleton<ITaskManager>(taskManager);
	            services.AddSingleton<IEnvironment>(environment);
	            services.AddSingleton<IProcessEnvironment>(s => s.GetService<IProcessManager>().DefaultProcessEnvironment);
	            services.AddSingleton<IProcessManager, ProcessManager>();
	            services.AddSingleton<ProcessRunner>();
	            services.AddSingleton<ProcessServer>();

            });

            host.UseConsoleLifetime();

            await host.Start();

            Console.WriteLine($"Port:{host.Rpc.Configuration.Port}");

            try
            {
                await host.Run();
            } catch {}

            // wait until all stop events have completed
            exiting.Wait();

            syncContext.Dispose();

            return 0;
        }

        private const string AppSettingsFile = "processserver";
        private static ServerConfiguration GetConfiguration(string[] args)
        {
            // get the -projectPath up front so we know where to find the json/yaml files for loading the rest of the configurations
            var projectPath = new ConfigurationBuilder().AddExtendedCommandLine(args).Build().GetValue<string>("projectpath");

            // merge settings from a yaml file and command line arguments into the configuration object
            var builder = new ConfigurationBuilder()
                .AddYamlFile(System.IO.Path.Combine(projectPath, "Cache", $"{AppSettingsFile}.settings"), optional: true, reloadOnChange: false)
                .AddExtendedCommandLine(args);

            var conf = builder.Build().Get<ServerConfiguration>();

            return conf;
        }
    }
}

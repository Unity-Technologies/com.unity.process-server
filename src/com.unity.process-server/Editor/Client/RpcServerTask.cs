namespace Unity.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Interfaces;
    using Rpc;
    using Internal.IO;

    public class RpcServerTask : TaskBase<RpcClient>
    {
        private readonly IProcessManager processManager;
        private readonly IRpcProcessConfiguration configuration;
        private readonly string workingDir;
        private readonly ProcessOptions options;
        private readonly IEnvironment environment;
        private readonly IProcessEnvironment processEnvironment;
        private readonly List<Type> remoteRpcTargets;
        private readonly List<object> localRpcTargets;
        private readonly string executable;
        private readonly string arguments;
        private readonly IOutputProcessor<int> portProcessor;
        private readonly int expectedPort;

        public RpcServerTask(ITaskManager taskManager,
            IProcessManager processManager,
            IRpcProcessConfiguration configuration,
            string workingDir = default,
            ProcessOptions options = default,
            PortOutputProcessor processor = null,
            List<Type> remoteRpcTargets = null, List<object> localRpcTargets = null,
            CancellationToken token = default)
            : this(taskManager, processManager, processManager.DefaultProcessEnvironment,
                processManager.DefaultProcessEnvironment.Environment,
                configuration, workingDir, options, processor, remoteRpcTargets, localRpcTargets, token)
        {}

        public RpcServerTask(ITaskManager taskManager,
            IProcessManager processManager,
            IProcessEnvironment processEnvironment,
            IEnvironment environment,
            IRpcProcessConfiguration configuration,
            string workingDir = default,
            ProcessOptions options = default,
            PortOutputProcessor processor = null,
            List<Type> remoteRpcTargets = null, List<object> localRpcTargets = null,
            CancellationToken token = default)
            : base(taskManager, token)
        {
            this.expectedPort = configuration.Port;
            this.processManager = processManager;
            this.configuration = configuration;
            this.workingDir = workingDir;
            this.options = options;
            this.processEnvironment = processManager.DefaultProcessEnvironment;
            this.environment = processEnvironment.Environment;
            this.remoteRpcTargets = remoteRpcTargets ?? new List<Type>();
            this.localRpcTargets = localRpcTargets ?? new List<object>();
            executable = configuration.ExecutablePath;
            arguments = CreateArguments(environment, configuration);

            portProcessor = processor ?? new PortOutputProcessor();
        }

        public RpcServerTask RegisterRemoteTarget<T>()
        {
            remoteRpcTargets.Add(typeof(T));
            return this;
        }

        public RpcServerTask RegisterLocalTarget(object instance)
        {
            localRpcTargets.Add(instance);
            return this;
        }

        private static string CreateArguments(IEnvironment environment, IRpcProcessConfiguration configuration)
        {
            var args = new List<string>();
            args.Add("-projectPath");
            args.Add(environment.UnityProjectPath.ToSPath().InQuotes());
            args.Add("-pid");
            args.Add(System.Diagnostics.Process.GetCurrentProcess().Id.ToString());
            if (!string.IsNullOrEmpty(configuration.AccessToken))
            {
                args.Add("-accessToken");
                args.Add(configuration.AccessToken);
            }
            return string.Join(" ", args);
        }

        protected override RpcClient RunWithReturn(bool success)
        {
            var result = base.RunWithReturn(success);
            try
            {
                var retries = 2;
                var port = expectedPort;
                while (result == null && retries > 0)
                {
                    IProcessTask<int> processTask = null;

                    try
                    {
                        var ipcTask = new TPLTask<int, RpcClient>(TaskManager, ConnectToServer) { Affinity = TaskAffinity.None };

                        if (port > 0)
                        {
                            ipcTask.PreviousResult = port;
                        }
                        else
                        {
                            // run the server process, we don't know which port it's on
                            processTask = SetupServerProcess();
                            ipcTask = processTask.Then(ipcTask);
                        }

                        ipcTask.StartSync(Token);
                        result = ipcTask.Result;
                    }
                    catch (Exception ex)
                    {
                        if (ex is AggregateException)
                        {
                            if (processTask != null && !processTask.Successful)
                            {
                                ex = processTask.Exception;
                            }
                        }
                        retries--;
                        processTask?.Stop();
                        processTask?.Dispose();
                        processTask = null;
                        port = 0;
                        result = default;
                        if (retries == 0)
                            ex.Rethrow();
                    }
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogException(ex);
#endif
                if (!RaiseFaultHandlers(ex))
                    Exception.Rethrow();
            }
            return result;
        }

        private IProcessTask<int> SetupServerProcess()
        {
            var task = new DotNetProcessTask<int>(TaskManager, processEnvironment, environment,
                executable, arguments, portProcessor) { Affinity = TaskAffinity.None };

            if (processManager is IRemoteProcessManager remoteProcessManager)
            {
                task.Configure(remoteProcessManager, options, workingDir);
                ((RemoteProcessWrapper)task.Wrapper).OnProcessPrepared += RpcServerTask_OnProcessPrepared;
            }
            else
                task.Configure(processManager, workingDir);

            // server returned the port, detach the process
            task.OnOutput += _ => task.Detach();
            return task;
        }

        private void RpcServerTask_OnProcessPrepared(RemoteProcessWrapper wrapper, RpcProcess process)
        {
            configuration.RemoteProcessId = process.Id;
            wrapper.OnProcessPrepared -= RpcServerTask_OnProcessPrepared;
        }

        private async Task<RpcClient> ConnectToServer(int port)
        {
            var client = new RpcClient(new Configuration { Port = port }, Token);
            foreach (var type in remoteRpcTargets)
                client.RegisterRemoteTarget(type);
            foreach (var inst in localRpcTargets)
                client.RegisterLocalTarget(inst);
            await client.Start();
            return client;
        }
    }
}

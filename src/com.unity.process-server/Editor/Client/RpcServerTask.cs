namespace Unity.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Rpc;
    using Internal.IO;

    public class RpcServerTask : TaskBase<RpcClient>
    {
        private readonly IProcessManager processManager;
        private readonly string workingDir;
        private readonly IEnvironment environment;
        private readonly IProcessEnvironment processEnvironment;
        private readonly List<Type> remoteRpcTargets;
        private readonly List<object> localRpcTargets;
        private readonly string executable;
        private readonly string arguments;
        private readonly IOutputProcessor<int> portProcessor;
        private readonly int expectedPort;

        private readonly AutoResetEvent signal = new AutoResetEvent(false);

        public RpcServerTask(ITaskManager taskManager,
                    IProcessManager processManager,
                    IProcessServerConfiguration configuration,
                    string workingDir = default,
                    List<Type> remoteRpcTargets = null, List<object> localRpcTargets = null,
                    CancellationToken token = default)
            : this(taskManager, processManager, processManager.DefaultProcessEnvironment, processManager.DefaultProcessEnvironment.Environment,
                configuration, workingDir, remoteRpcTargets, localRpcTargets, token)
        {}

        public RpcServerTask(ITaskManager taskManager,
                    IProcessManager processManager,
                    IProcessEnvironment processEnvironment,
                    IEnvironment environment,
                    IProcessServerConfiguration configuration,
					string workingDir = default,
                    List<Type> remoteRpcTargets = null, List<object> localRpcTargets = null,
                    CancellationToken token = default)
            : base(taskManager, token)
        {
            this.expectedPort = configuration.Port;
            this.processManager = processManager;
            this.workingDir = workingDir;
            this.processEnvironment = processManager.DefaultProcessEnvironment;
            this.environment = processEnvironment.Environment;
            this.remoteRpcTargets = remoteRpcTargets ?? new List<Type>();
            this.localRpcTargets = localRpcTargets ?? new List<object>();
            executable = configuration.ExecutablePath;
            arguments = CreateArguments(environment);
            portProcessor = new BaseOutputProcessor<int>((string line, out int result) => {
                result = default;
                if (!(line?.StartsWith("Port:") ?? false)) return false;
                result = int.Parse(line.Substring(5));
                return true;
            });
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

        private static string CreateArguments(IEnvironment environment)
        {
            var args = new StringBuilder();
            args.Append("-projectPath ");
            args.Append(environment.UnityProjectPath.ToSPath().InQuotes());
            return args.ToString();
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
            var task = new DotNetProcessTask<int>(TaskManager, processManager,
                processEnvironment, environment, executable, arguments,
                portProcessor, workingDir);

            // server returned the port, detach the process
            task.OnOutput += _ => task.Detach();
            return task;
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

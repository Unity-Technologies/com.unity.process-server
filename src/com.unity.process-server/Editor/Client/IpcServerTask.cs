namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Ipc;
    using Tasks;
    using Unity.Editor.ProcessServer.Internal.IO;

    public class IpcServerTask : TaskBase<IpcClient>
    {
        private readonly IProcessManager processManager;
        private readonly IEnvironment environment;
        private readonly IProcessEnvironment processEnvironment;
        private readonly List<Type> remoteIpcTargets;
        private readonly List<object> localIpcTargets;
        private int port;
        private string executable;
        private string arguments;
        private IOutputProcessor<int> portProcessor;

        private readonly AutoResetEvent signal = new AutoResetEvent(false);

        public IpcServerTask(ITaskManager taskManager,
                    IProcessManager processManager,
                    IProcessServerConfiguration configuration,
                    CancellationToken token,
                    List<Type> remoteIpcTargets = null, List<object> localIpcTargets = null)
            : this(taskManager, processManager, processManager.DefaultProcessEnvironment, processManager.DefaultProcessEnvironment.Environment,
                configuration, token, remoteIpcTargets, localIpcTargets)
        {}

        public IpcServerTask(ITaskManager taskManager,
                    IProcessManager processManager,
                    IProcessEnvironment processEnvironment,
                    IEnvironment environment,
                    IProcessServerConfiguration configuration,
                    CancellationToken token,
                    List<Type> remoteIpcTargets = null, List<object> localIpcTargets = null)
            : base(taskManager, token)
        {
            this.processManager = processManager;
            this.processEnvironment = processManager.DefaultProcessEnvironment;
            this.environment = processEnvironment.Environment;
            this.remoteIpcTargets = remoteIpcTargets ?? new List<Type>();
            this.localIpcTargets = localIpcTargets ?? new List<object>();
            port = configuration.Port;
            executable = configuration.ExecutablePath;
            arguments = CreateArguments(environment);
            portProcessor = new BaseOutputProcessor<int>((string line, out int result) => {
                result = default;
                if (!(line?.StartsWith("Port:") ?? false)) return false;
                result = int.Parse(line.Substring(5));
                return true;
            });
        }

        public IpcServerTask RegisterRemoteTarget<T>()
        {
            remoteIpcTargets.Add(typeof(T));
            return this;
        }

        public IpcServerTask RegisterLocalTarget(object instance)
        {
            localIpcTargets.Add(instance);
            return this;
        }

        private static string CreateArguments(IEnvironment environment)
        {
            var args = new StringBuilder();
            args.Append("-projectPath ");
            args.Append(environment.UnityProjectPath.ToSPath().InQuotes());
            return args.ToString();
        }

        protected override IpcClient RunWithReturn(bool success)
        {
            var result = base.RunWithReturn(success);
            try
            {
                int retries = 2;

                while (result == null && retries > 0)
                {
                    try
                    {
                        retries--;
                        IProcessTask<int> processTask = null;

                        var ipcTask = new TPLTask<int, IpcClient>(TaskManager, ConnectToServer) { Affinity = TaskAffinity.None };

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

                        // connect to ipc server
                        var t = ipcTask.Finally((s, ex, ret) => {
                            if (!s && ex != null) ex.Rethrow();
                            return ret;
                        }).Start();

                        if (!t.Task.Wait(10000))
                        {
                            processTask?.Stop();
                            processTask = null;
                            throw new TimeoutException("Could not connect to IPC server.");
                        }

                        result = ipcTask.Result;
                    }
                    catch (Exception ex)
                    {
#if UNITY_EDITOR
                        UnityEngine.Debug.LogException(ex);
#endif
                        port = 0;
                        result = default;
                        if (retries == 0)
                            throw;
                    }
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR
                UnityEngine.Debug.LogException(ex);
#endif
                if (!RaiseFaultHandlers(ex))
                    ex.Rethrow();
            }
            return result;
        }

        private IProcessTask<int> SetupServerProcess()
        {
            var task = new DotNetProcessTask<int>(TaskManager, processManager,
                processEnvironment, environment, executable, arguments,
                portProcessor);

            task.OnOutput += s => {
#if UNITY_EDITOR
                UnityEngine.Debug.Log($"Port:{s}");
#endif
                task.Detach();
            };
            task.OnEnd += (_, __, ___, ____) => {
#if UNITY_EDITOR
                UnityEngine.Debug.Log($"Process running");
#endif
            };
            return task;
        }

        private async Task<IpcClient> ConnectToServer(int port)
        {
#if UNITY_EDITOR
            UnityEngine.Debug.Log($"Connecting to port {port}");
#endif
            var client = new IpcClient(new Configuration { Port = port }, Token);
            foreach (var type in remoteIpcTargets)
                client.RegisterRemoteTarget(type);
            foreach (var inst in localIpcTargets)
                client.RegisterLocalTarget(inst);
            await client.Start();
#if UNITY_EDITOR
            UnityEngine.Debug.Log($"Connected to port {port}");
#endif
            return client;
        }

    }
}

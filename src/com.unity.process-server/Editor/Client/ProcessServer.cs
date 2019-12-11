namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using Ipc;
    using Tasks;
    using Unity.Editor.Tasks.Extensions;

    public interface IProcessServer
    {
        IProcessServer Connect();
        void Stop();

        IServer Server { get; }
        IProcessRunner ProcessRunner { get; }

        ITaskManager TaskManager { get; }
        IRemoteProcessManager ProcessManager { get; }
        IEnvironment Environment { get; }
        IProcessServerConfiguration Configuration { get; }
    }

    public interface IProcessServerConfiguration
    {
        /// <summary>
        /// Pass this to <see cref="ProcessServer.Get(ITaskManager, IEnvironment, IProcessServerConfiguration)"/>
        /// so the client can read and write the port information when connecting.
        /// </summary>
        int Port { get; set; }
        /// <summary>
        /// The path to the process server executable.
        /// </summary>
        string ExecutablePath { get; }
    }

    public class ProcessServer : IProcessServer
    {
        private static ProcessServer instance;

        private readonly bool ownsTaskManager;
        private readonly ServerNotifications notifications;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ManualResetEventSlim stopSignal = new ManualResetEventSlim(false);
        private readonly ManualResetEvent completionHandle = new ManualResetEvent(false);

        private MainThreadSynchronizationContext ourContext;

        private IpcClient ipcClient;

        private IProcessManager localProcessManager;
        private RemoteProcessManager processManager;
        private bool shuttingDown;

        public ProcessServer(ITaskManager taskManager,
            IEnvironment environment,
            IProcessServerConfiguration configuration)
        {
            Environment = environment;
            Configuration = configuration;

            ownsTaskManager = taskManager == null;

            if (ownsTaskManager)
            {
                TaskManager = GetTaskManager();
            }
            else
            {
                TaskManager = taskManager;
                TaskManager.Token.Register(Shutdown);
            }

            localProcessManager = new ProcessManager(Environment);
            processManager = new RemoteProcessManager(localProcessManager.DefaultProcessEnvironment, cts.Token);
            notifications = new ServerNotifications(this);


#if UNITY_EDITOR
            UnityEditor.EditorApplication.quitting += Stop;
#endif
        }

        public static IProcessServer Get(ITaskManager taskManager = null,
            IEnvironment environment = null,
            IProcessServerConfiguration configuration = null)
        {
            if (instance?.disposed ?? false)
                instance = null;

            if (instance == null)
            {
                var inst = new ProcessServer(taskManager, environment ?? TheEnvironment.instance.Environment, configuration ?? ApplicationConfiguration.Instance);
                instance = inst;
            }
            return instance.Connect();
        }

        public IProcessServer Connect()
        {
            if (disposed || shuttingDown) return null;

            if (ipcClient != null)
            {
                return this;
            }

            var task = SetupIpcTask().Start();
            if (!task.Task.Wait(5000, cts.Token))
            {
                return null;
            }

            ipcClient = task.Result;
            ipcClient.Disconnected(e => {
                processManager.ConnectToServer(null);
                ipcClient = null;
            });

            processManager.ConnectToServer(this);
            Configuration.Port = ipcClient.Configuration.Port;

            return this;
        }

        public void Stop()
        {
            if (disposed) return;
            Shutdown();
            Completion.WaitOne(500);
        }

        private ITaskManager GetTaskManager()
        {
            var taskManager = new TaskManager();
            try
            {
                taskManager.Initialize();
            }
            catch
            {
                ourContext = new MainThreadSynchronizationContext(cts.Token);
                taskManager.Initialize(ourContext);
            }
            return taskManager;
        }

        private ITask<IpcClient> SetupIpcTask()
        {
            return new IpcServerTask(TaskManager, localProcessManager,
                                       localProcessManager.DefaultProcessEnvironment,
                                       Environment, Configuration, cts.Token) { Affinity = TaskAffinity.None }
                                   .RegisterRemoteTarget<IServer>()
                                   .RegisterRemoteTarget<IProcessRunner>()
                                   .RegisterLocalTarget(notifications)
                                   .RegisterLocalTarget(processManager.ProcessNotifications)
                                   .Finally((success, exception, ret) => {
                                       if (!success)
                                       {
#if UNITY_EDITOR
                                           UnityEngine.Debug.LogException(ex);
#endif
                                           return default;
                                       }
                                       return ret;
                                   });
        }

        private void Shutdown()
        {
            if (shuttingDown)
            {
                Completion.WaitOne(500);
                return;
            }
            shuttingDown = true;

            new Thread(() => {
                try
                {
                    Server.Stop().FireAndForget();
                    stopSignal.Wait(300);
                }
                catch {}
                cts.Cancel();
                Dispose();
            }).Start();
        }

        private void ServerStopping() => stopSignal.Set();

        private void ProcessRestarting(IpcProcess process, ProcessRestartReason reason)
        {
            processManager.RaiseProcessRestart(process, reason);
        }

        private bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;

            if (disposing)
            {
                if (!shuttingDown)
                {
                    Shutdown();
                    stopSignal.Wait(300);
                }

                try
                {
                    localProcessManager?.Dispose();
                    ProcessManager?.Dispose();
                    ipcClient?.Dispose();
                    if (ownsTaskManager)
                    {
                        TaskManager?.Dispose();
                        ourContext?.Dispose();
                    }
                }
                finally
                {
                    completionHandle.Set();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public ITaskManager TaskManager { get; }
        public IEnvironment Environment { get; }
        public IRemoteProcessManager ProcessManager => processManager;
        public IProcessServerConfiguration Configuration { get; }

        public IServer Server => ipcClient?.GetRemoteTarget<IServer>();
        public IProcessRunner ProcessRunner => ipcClient?.GetRemoteTarget<IProcessRunner>();

        public WaitHandle Completion => completionHandle;

        class ServerNotifications : IServerNotifications
        {
            private readonly ProcessServer server;

            public ServerNotifications(ProcessServer server)
            {
                this.server = server;
            }

            public Task ServerStopping()
            {
                server.ServerStopping();
                return Task.CompletedTask;
            }

            public Task ProcessRestarting(IpcProcess process, ProcessRestartReason reason)
            {
                server.ProcessRestarting(process, reason);
                return Task.CompletedTask;
            }
        }
    }
}

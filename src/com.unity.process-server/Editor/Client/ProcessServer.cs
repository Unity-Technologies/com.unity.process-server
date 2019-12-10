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
        Task<IProcessServer> Connect();
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

        private IpcClient ipcClient;

        private IProcessManager localProcessManager;
        private RemoteProcessManager processManager;
        private MainThreadSynchronizationContext ourContext;
        private bool shuttingDown;

        public static async Task<IProcessServer> Get(ITaskManager taskManager = null,
            IEnvironment environment = null,
            IProcessServerConfiguration configuration = null)
        {
            if (instance != null && instance.disposed)
            {
                instance = null;
            }
            if (instance == null)
            {
                var inst = new ProcessServer(taskManager, environment ?? TheEnvironment.instance.Environment, configuration ?? ApplicationConfiguration.Instance);
                instance = inst;
            }
            return await instance.Connect();
        }

        public ProcessServer(ITaskManager taskManager,
            IEnvironment environment,
            IProcessServerConfiguration configuration)
        {
            Environment = environment;
            Configuration = configuration;

            ownsTaskManager = taskManager == null;

            if (ownsTaskManager)
                taskManager = new TaskManager();

            TaskManager = taskManager;
            TaskManager.Token.Register(Shutdown);

            localProcessManager = new ProcessManager(Environment);
            processManager = new RemoteProcessManager(localProcessManager.DefaultProcessEnvironment, cts.Token);
            notifications = new ServerNotifications(this);


#if UNITY_EDITOR
            UnityEditor.EditorApplication.quitting += Stop;
#endif
        }

        public void Stop()
        {
            if (disposed) return;
            Shutdown();
            Completion.WaitOne(500);
        }

        public async Task<IProcessServer> Connect()
        {
            if (disposed || shuttingDown) return null;

            EnsureInitialized();

            if (ipcClient != null)
            {
                return this;
            }

            ipcClient = await SetupIpcTask().StartAwait();

            ipcClient.Disconnected(e => {
                processManager.ConnectToServer(null);
                var c = ipcClient;
                ipcClient = null;
                if (disposed || shuttingDown) return;

                new ActionTask<IpcClient>(TaskManager, cts.Token, client => client.Dispose()) { PreviousResult = c, Affinity = TaskAffinity.None }
                    .Then(SetupIpcTask())
                    .Then(x => ipcClient = x, TaskAffinity.None)
                    .Start();
            });

            processManager.ConnectToServer(ipcClient.GetRemoteTarget<IProcessRunner>());
            Configuration.Port = ipcClient.Configuration.Port;

            return this;
        }

        private IpcServerTask SetupIpcTask()
        {
            return new IpcServerTask(TaskManager, localProcessManager,
                                       localProcessManager.DefaultProcessEnvironment,
                                       Environment, Configuration, cts.Token) { Affinity = TaskAffinity.None }
                                   .RegisterRemoteTarget<IServer>()
                                   .RegisterRemoteTarget<IProcessRunner>()
                                   .RegisterLocalTarget(notifications)
                                   .RegisterLocalTarget(processManager.ProcessNotifications);
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

        private void EnsureInitialized()
        {
            if (TaskManager.UIScheduler == null)
            {
                try
                {
                    TaskManager.Initialize();
                }
                catch
                {
                    if (ownsTaskManager)
                    {
                        ourContext = new MainThreadSynchronizationContext(TaskManager.Token);
                        TaskManager.Initialize(ourContext);
                    }
                    else
                        throw;
                }
            }
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
                    if (ownsTaskManager)
                    {
                        TaskManager?.Dispose();
                        ourContext?.Dispose();
                    }

                    localProcessManager?.Dispose();
                    ProcessManager?.Dispose();
                    ipcClient?.Dispose();
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

        public IServer Server => ipcClient.GetRemoteTarget<IServer>();
        public IProcessRunner ProcessRunner => ipcClient.GetRemoteTarget<IProcessRunner>();

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

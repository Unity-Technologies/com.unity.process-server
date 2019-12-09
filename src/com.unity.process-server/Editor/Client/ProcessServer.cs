namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using Ipc;
    using Tasks;
    using Unity.Editor.ProcessServer.Extensions;
    using Unity.Editor.Tasks.Extensions;

    public interface IProcessServer
    {
        IProcessServer ConnectSync();
        Task<IProcessServer> Connect();
        void ShutdownSync();
        Task Shutdown();

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
        private const int ConnectionTimeout = 5000;

        private readonly bool ownsTaskManager;
        private readonly ServerNotifications notifications;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly CancellationTokenSource externalCts;
        private readonly ManualResetEventSlim stopSignal = new ManualResetEventSlim(false);
        private readonly ManualResetEvent completionHandle = new ManualResetEvent(false);
        private readonly Task completionTask;

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
            completionTask = completionHandle.ToTask();

            ownsTaskManager = taskManager == null;

            if (ownsTaskManager)
            {
                taskManager = new TaskManager();
                try
                {
                    taskManager.Initialize();
                }
                catch
                {
                    ourContext = new MainThreadSynchronizationContext(cts.Token);
                    taskManager.Initialize(ourContext);
                }
            }
            else
            {
                externalCts = CancellationTokenSource.CreateLinkedTokenSource(taskManager.Token);
                externalCts.Token.Register(Dispose);
            }

            TaskManager = taskManager;

            if (configuration == null)
                configuration = new ApplicationConfigurationWrapper(TaskManager, ApplicationConfiguration.Instance);

            Configuration = configuration;

            localProcessManager = new ProcessManager(Environment);
            processManager = new RemoteProcessManager(this, localProcessManager.DefaultProcessEnvironment, cts.Token);
            notifications = new ServerNotifications(this);


#if UNITY_EDITOR
            UnityEditor.EditorApplication.quitting += ShutdownSync;
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
                var inst = new ProcessServer(taskManager, environment ?? TheEnvironment.instance.Environment, configuration);
                instance = inst;
            }
            return instance;
        }

        public static void Stop()
        {
            if (instance?.disposed ?? true)
                return;
            instance.ShutdownSync();
        }

        public IProcessServer ConnectSync() => InternalConnect();

        public async Task<IProcessServer> Connect()
        {
            if (disposed || shuttingDown) return null;
            if (ipcClient != null) return this;

            ipcClient = await SetupIpcTask().StartAwait(ConnectionTimeout, "Timeout connecting to process server", cts.Token);

            Configure();
            return this;
        }

        private IProcessServer InternalConnect()
        {
            if (disposed || shuttingDown) return null;
            if (ipcClient != null) return this;

            var task = SetupIpcTask().StartAwait(ConnectionTimeout, "Timeout connecting to process server", cts.Token);
            task.Wait();
            ipcClient = task.Result;

            Configure();
            return this;
        }

        private void Configure()
        {
            ipcClient.Disconnected(e => {
                ipcClient = null;
            });

            Configuration.Port = ipcClient.Configuration.Port;
        }

        public Task Shutdown()
        {
            if (disposed) return Task.CompletedTask;
            RequestShutdown();
            return completionTask;
        }

        public void ShutdownSync()
        {
            if (disposed) return;
            RequestShutdown();
            Completion.WaitOne(500);
        }

        private void RequestShutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;

            new Thread(() => {
                try
                {
                    Server.Stop().FireAndForget();
                    stopSignal.Wait(1000);
                }
                finally
                {
                    Dispose();
                }
            }).Start();
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
                                           return default;
                                       }
                                       return ret;
                                   });
        }

        private void ServerStopping() => stopSignal.Set();
        private void ProcessRestarting(IpcProcess process, ProcessRestartReason reason) => processManager.RaiseProcessRestart(process, reason);

        private bool disposed;
        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;

            if (disposing)
            {
                try
                {
                    externalCts?.Dispose();
                    cts.Cancel();
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

        /// <summary>
        /// Class that makes sure we always write the port information in the main thread, because it might be
        /// a scriptable singleton
        /// </summary>
        class ApplicationConfigurationWrapper : IProcessServerConfiguration
        {
            private readonly ITaskManager taskManager;
            private readonly IProcessServerConfiguration other;
            private int? port;

            public ApplicationConfigurationWrapper(ITaskManager taskManager, IProcessServerConfiguration other)
            {
                this.taskManager = taskManager;
                this.other = other;
            }

            public int Port
            {
                get
                {
                    return port.HasValue ? port.Value : other.Port;
                }
                set
                {
                    if (taskManager.InUIThread)
                    {
                        other.Port = value;
                    }
                    else
                    {
                        port = value;
                        taskManager.RunInUI(() => {
                            other.Port = port.Value;
                            port = null;
                        });
                    }
                }
            }

            public string ExecutablePath => other.ExecutablePath;
        }
    }

    namespace Extensions
    {
        internal static class WaitHandleExtensions
        {
            public static Task ToTask(this WaitHandle waitHandle)
            {
                var tcs = new TaskCompletionSource<object>();

                // Registering callback to wait till WaitHandle changes its state

                ThreadPool.RegisterWaitForSingleObject(
                    waitObject: waitHandle,
                    callBack: (o, timeout) => { tcs.SetResult(null); },
                    state: null,
                    timeout: TimeSpan.FromMilliseconds(-1),
                    executeOnlyOnce: true);

                return tcs.Task;
            }

            public static async Task<T> StartAwait<T>(this ITask<T> task, int timeout, string timeoutMessage, CancellationToken token)
            {
                var t = Task.Delay(timeout, token);
                var r = await Task.WhenAny(task.StartAwait(), t).ConfigureAwait(false);
                if (r == t)
                    throw new TimeoutException(timeoutMessage);
                return task.Result;
            }
        }
    }
}

namespace Unity.ProcessServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Interfaces;
    using Rpc;
    using Extensions;
    using Unity.Editor.Tasks.Extensions;

    /// <summary>
    /// Client interface of the process server.
    /// </summary>
    public interface IProcessServer
    {
        IProcessServer ConnectSync();
        Task<IProcessServer> Connect();
        void ShutdownSync();
        Task Shutdown();

        /// <summary>
        /// Runs a native process on the process server, returning all the output when the process is done.
        /// All callbacks run in the UI thread.
        /// </summary>
        IProcessTask<string> NewNativeProcess(string executable, string arguments,
            ProcessOptions options = default,
            Action<IProcessTask<string>> onStart = null,
            Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None);

        /// <summary>
        /// Runs a .net process on the process server. The process will be run natively on .net if on Windows
        /// or Unity's mono if not on Windows, returning all the output when the process is done.
        /// All callbacks run in the UI thread.
        /// </summary>
        IProcessTask<string> NewDotNetProcess(string executable, string arguments,
            ProcessOptions options = default,
            Action<IProcessTask<string>> onStart = null,
            Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None);

        /// <summary>
        /// Runs a process on the process server, using Unity's Mono, returning all the output when the process is done.
        /// All callbacks run in the UI thread.
        /// </summary>
        IProcessTask<string> NewMonoProcess(string executable, string arguments,
            ProcessOptions options = default,
            Action<IProcessTask<string>> onStart = null,
            Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None);

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
        private readonly MainThreadSynchronizationContext ourContext;
        private readonly IProcessManager localProcessManager;
        private readonly RemoteProcessManager processManager;

        private RpcClient ipcClient;
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

            var task = await SetupRpcTask().StartAwait(ConnectionTimeout, "Timeout connecting to process server", cts.Token).ConfigureAwait(false);
            task.Exception?.Rethrow();
            Configure();
            return this;
        }

        public async Task Shutdown()
        {
            if (disposed) return;
            RequestShutdown();
            await completionTask.ConfigureAwait(false);
        }

        public void ShutdownSync()
        {
            if (disposed) return;
            RequestShutdown();
            Completion.WaitOne(500);
        }

        public IProcessTask<string> NewNativeProcess(string executable, string arguments, ProcessOptions options = default,
            Action<IProcessTask<string>> onStart = null, Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None)
        {
            return ConfigureProcess(
                new NativeProcessTask<string>(TaskManager, localProcessManager.DefaultProcessEnvironment,
                    executable, arguments,
                    outputProcessor ?? new SimpleOutputProcessor())
                    { Affinity = affinity },
                options, onStart, onOutput, onEnd);
        }

        public IProcessTask<string> NewDotNetProcess(string executable, string arguments, ProcessOptions options = default,
            Action<IProcessTask<string>> onStart = null, Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None)
        {
            return ConfigureProcess(
                    new DotNetProcessTask<string>(TaskManager, localProcessManager.DefaultProcessEnvironment, Environment,
                        executable, arguments,
                        outputProcessor ?? new SimpleOutputProcessor()) { Affinity = affinity },
                    options, onStart, onOutput, onEnd);
        }

        public IProcessTask<string> NewMonoProcess(string executable, string arguments, ProcessOptions options = default,
            Action<IProcessTask<string>> onStart = null, Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None)
        {
            return ConfigureProcess(
                    new MonoProcessTask<string>(TaskManager, localProcessManager.DefaultProcessEnvironment, Environment,
                        executable, arguments,
                        outputProcessor ?? new SimpleOutputProcessor()) { Affinity = affinity },
                    options, onStart, onOutput, onEnd);
        }

        private IProcessTask<string> ConfigureProcess(IProcessTask<string> task, ProcessOptions options,
            Action<IProcessTask<string>> onStart,
            Action<IProcessTask<string>, string> onOutput,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null
            )
        {
            task.Configure(ProcessManager, options);

            if (onStart != null)
            {
                task.OnStartProcess += _ => {
                    if (TaskManager.InUIThread)
                        onStart(task);
                    else
                        TaskManager.RunInUI(() => onStart(task));
                };
            }

            if (onOutput != null)
            {
                task.OnOutput += line => {
                    if (TaskManager.InUIThread)
                        onOutput(task, line);
                    else
                        TaskManager.RunInUI(() => onOutput(task, line));
                };
            }

            if (onEnd != null)
            {
                task.OnEnd += (t, ret, success, exception) => {
                    if (TaskManager.InUIThread)
                        onEnd(t as IProcessTask<string>, ret, success, exception);
                    else
                        TaskManager.RunInUI(() => onEnd(t as IProcessTask<string>, ret, success, exception));
                };
            }
            return task;
        }


        private IProcessServer InternalConnect()
        {
            if (disposed || shuttingDown) return null;
            if (ipcClient != null) return this;

            var task = SetupRpcTask().StartAwait(ConnectionTimeout, "Timeout connecting to process server", cts.Token);
            task.Wait();
            task.Result?.Exception?.Rethrow();
            ipcClient = task.Result?.Result;

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

        private void RequestShutdown()
        {
            if (shuttingDown) return;
            shuttingDown = true;

            var startedShutdown = new ManualResetEventSlim();
            ThreadPool.QueueUserWorkItem(started => {
                try
                {
                    ((ManualResetEventSlim)started).Set();
                    Server.Stop().FireAndForget();
                    stopSignal.Wait(1000);
                }
                finally
                {
                    Dispose();
                }
            }, startedShutdown);
            startedShutdown.Wait();
        }

        private ITask<RpcClient> SetupRpcTask()
        {
            return new RpcServerTask(TaskManager, localProcessManager,
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
        private void ProcessRestarting(RpcProcess process, ProcessRestartReason reason) => processManager.RaiseProcessRestart(process, reason);

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
        protected bool ShuttingDown => shuttingDown;

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

            public Task ProcessRestarting(RpcProcess process, ProcessRestartReason reason)
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

            public static async Task<ITask<T>> StartAwait<T>(this ITask<T> task, int timeout, string timeoutMessage, CancellationToken token)
            {
                var t = Task.Delay(timeout, token);
                var r = await Task.WhenAny(task.StartAwait(), t).ConfigureAwait(false);
                if (r == t)
                    throw new TimeoutException(timeoutMessage);
                return task;
            }
        }
    }
}

﻿namespace Unity.ProcessServer
{
    using System;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Interfaces;
    using Extensions;
    using Rpc;
    using Unity.Editor.Tasks.Extensions;

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
            IRpcProcessConfiguration configuration)
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
            {
	            configuration = new ApplicationConfigurationWrapper(TaskManager, ApplicationConfiguration.instance);
	            // read the executable path up front so it gets serialized if it needs to, while we're on the main thread
	            var _ = configuration.ExecutablePath;
            }

            if (string.IsNullOrEmpty(configuration.AccessToken))
                configuration.AccessToken = GenerateAccessToken();

            Configuration = configuration;

            localProcessManager = new ProcessManager(Environment);
            processManager = new RemoteProcessManager(this, localProcessManager.DefaultProcessEnvironment, cts.Token);
            notifications = new ServerNotifications(this);

#if UNITY_EDITOR
            UnityEditor.EditorApplication.quitting += ShutdownSync;
#endif
        }

        private static string GenerateAccessToken()
        {
            var randomValues = new byte[16];
            using (var provider = new RNGCryptoServiceProvider())
                provider.GetBytes(randomValues);
            return BitConverter.ToString(randomValues).Replace("-", string.Empty);
        }

        public static IProcessServer Get(ITaskManager taskManager = null,
            IEnvironment environment = null,
            IRpcProcessConfiguration configuration = null)
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
            ipcClient = task.Result;
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

        public IProcessTask<string> NewNativeProcess(string executable, string arguments,
	        ProcessOptions options = default,
	        string workingDir = default,
            Action<IProcessTask<string>> onStart = null,
	        Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None,
            CancellationToken token = default
	        )
        {
            return ConfigureProcess(
                new NativeProcessTask<string>(TaskManager, localProcessManager.DefaultProcessEnvironment,
                    executable, arguments,
                    outputProcessor ?? new StringOutputProcessor(), token)
                    { Affinity = affinity },
                options, workingDir, onStart, onOutput, onEnd);
        }

        public IProcessTask<string> NewDotNetProcess(string executable, string arguments,
	        ProcessOptions options = default,
	        string workingDir = default,
            Action<IProcessTask<string>> onStart = null,
	        Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None,
	        CancellationToken token = default
	        )
        {
            return ConfigureProcess(
                    new DotNetProcessTask<string>(TaskManager, localProcessManager.DefaultProcessEnvironment, Environment,
                        executable, arguments,
                        outputProcessor ?? new StringOutputProcessor()) { Affinity = affinity },
                    options, workingDir, onStart, onOutput, onEnd);
        }

        public IProcessTask<string> NewMonoProcess(string executable, string arguments, ProcessOptions options = default,
	        string workingDir = default,
            Action<IProcessTask<string>> onStart = null,
	        Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None,
	        CancellationToken token = default
			)
        {
            return ConfigureProcess(
                    new MonoProcessTask<string>(TaskManager, localProcessManager.DefaultProcessEnvironment, Environment,
                        executable, arguments,
                        outputProcessor ?? new StringOutputProcessor()) { Affinity = affinity },
                    options, workingDir, onStart, onOutput, onEnd);
        }

        private IProcessTask<string> ConfigureProcess(IProcessTask<string> task, ProcessOptions options,
	        string workingDir,
            Action<IProcessTask<string>> onStart,
            Action<IProcessTask<string>, string> onOutput,
            Action<IProcessTask<string>, string, bool, Exception> onEnd
            )
        {
            task.Configure(ProcessManager, options, workingDir);

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
                    if (Server != null)
                    {
                        Server.Stop(Configuration.AccessToken).FireAndForget();
                        stopSignal.Wait(1000);
                    }
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
                                       Environment, Configuration, token: cts.Token) { Affinity = TaskAffinity.None }
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
        public IRpcProcessConfiguration Configuration { get; }

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
        class ApplicationConfigurationWrapper : IRpcProcessConfiguration
        {
            private readonly ITaskManager taskManager;
            private readonly IRpcProcessConfiguration other;
            private int? port;
            private string accessToken;

            public ApplicationConfigurationWrapper(ITaskManager taskManager, IRpcProcessConfiguration other)
            {
                this.taskManager = taskManager;
                this.other = other;
            }

            public int Port
            {
	            get => port.HasValue ? port.Value : other.Port; 
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

            public string AccessToken
            {
                get => accessToken ?? other.AccessToken;
                set
                {
                    if (taskManager.InUIThread)
                    {
                        other.AccessToken = value;
                    }
                    else
                    {
                        accessToken = value;
                        taskManager.RunInUI(() => {
                            other.AccessToken = accessToken;
                            accessToken = null;
                        });
                    }
                }
            }

            public string ExecutablePath => other.ExecutablePath;

            string IRpcProcessConfiguration.RemoteProcessId { get => string.Empty; set {} }
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

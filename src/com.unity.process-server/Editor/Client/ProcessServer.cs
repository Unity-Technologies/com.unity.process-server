namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using Ipc;
    using Tasks;
    using Unity.Editor.ProcessServer.Internal.IO;
    
    public interface IProcessServer : IRemoteProcessManager, IDisposable
    {
        IServer Server { get; }
        IProcessRunner ProcessRunner { get; }
        IProcessServer Connect();
        ITaskManager TaskManager { get; }
        IProcessManager ProcessManager { get; }
        IEnvironment Environment { get; }
        IProcessServerConfiguration Configuration { get; }
    }

    public interface IProcessServerConfiguration
    {
        int Port { get; set; }
        string ExecutablePath { get; set; }
    }

    public class RemoteProcessEnvironment : IProcessEnvironment
    {
        private readonly IProcessEnvironment localProcessEnvironment;

        public RemoteProcessEnvironment(IProcessEnvironment localProcessEnvironment)
        {
            this.localProcessEnvironment = localProcessEnvironment;
        }

        public void Configure(ProcessStartInfo psi, string workingDirectory = null)
        {
            localProcessEnvironment.Configure(psi, workingDirectory);
        }

        public IEnvironment Environment => localProcessEnvironment.Environment;
    }

    public interface IRemoteProcessManager : IProcessManager
    {
        T Configure<T>(T processTask, ProcessOptions options = default, string workingDirectory = null)
            where T : IProcessTask;
    }

    public class ProcessServer : IProcessServer
    {
        private static ProcessServer instance;

        private readonly bool ownsTaskManager;
        private readonly bool ownsProcessManager;
        private readonly Notifications notifications;

        private ManualResetEventSlim stopSignal;
        private IpcClient ipcClient;
        private Dictionary<string, SynchronizationContextTaskScheduler> processes = new Dictionary<string, SynchronizationContextTaskScheduler>();
        private Dictionary<string, RemoteProcessWrapper> wrappers = new Dictionary<string, RemoteProcessWrapper>();

        public event EventHandler<IpcProcessRestartEventArgs> OnProcessRestart;

        public ProcessServer(ITaskManager taskManager,
            IProcessManager processManager,
            IEnvironment environment,
            IProcessServerConfiguration configuration)
        {
            ownsTaskManager = taskManager == null;
            ownsProcessManager = processManager == null;

            if (ownsTaskManager)
                taskManager = new TaskManager().Initialize();

            if (ownsProcessManager)
                processManager = new ProcessManager(environment);

            TaskManager = taskManager;
            ProcessManager = processManager;
            Environment = environment;

            Configuration = configuration;
            notifications = new Notifications(this);

            Completion = new TPLTask<bool>(TaskManager, async () => {
                stopSignal = new ManualResetEventSlim(false);
                await Server.Stop();
                return stopSignal.Wait(500, TaskManager.Token);
            }) { Affinity = TaskAffinity.None };

#if UNITY_EDITOR
            EditorApplication.quitting += () => {
                Stop();
                Completion.Task.Wait();
                Dispose();
            };
#endif
        }

        public static IProcessServer Get(ITaskManager taskManager = null, IProcessManager processManager = null)
        {
            if (instance == null)
            {
                var inst = new ProcessServer(taskManager, processManager,
                    TheEnvironment.instance.Environment, ApplicationConfiguration.Instance);
                instance = inst;
            }
            instance.EnsureInitialized();
            return instance.Connect();
        }

        private void EnsureInitialized()
        {
            if (TaskManager.UIScheduler == null)
                TaskManager.Initialize();
        }

        public IProcessServer Connect()
        {
            var port = Configuration.Port;

            int retries = 2;

            while (ipcClient == null && retries > 0)
            {
                try
                {
                    var task = new TPLTask<IpcClient>(TaskManager, async () =>
                        {
                            // we don't know where the server is, start a new one
                            if (port == 0)
                            {
                                port = RunProcessServer(Configuration.ExecutablePath).RunSynchronously();
                            }

                            return await ConnectToServer(port);
                        });

                    task.StartAwait().Wait(TaskManager.Token);
                    ipcClient = task.Result;
                }
                catch
                {
                    // can't connect to server, try launching it again
                    port = 0;
                    retries--;
                    ipcClient = null;
                }
            }

            if (ipcClient == null)
                throw new NotReadyException("Could not connect to process server.");
            Configuration.Port = port;
            return this;
        }

        public T Configure<T>(T processTask, ProcessOptions options, string workingDirectory = null)
            where T : IProcessTask
        {
            var startInfo = new ProcessStartInfo {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.FileName = processTask.ProcessName.ToSPath().ToString();
            startInfo.Arguments = processTask.ProcessArguments;

            processTask.ProcessEnvironment.Configure(startInfo, workingDirectory);
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            processTask.Configure(this, process);
            if (processTask.Wrapper is RemoteProcessWrapper wrapper)
            {
                wrapper.Configure(options);
                wrapper.OnProcessPrepared += (processWrapper, ipcProcess) => {
                    wrappers.Add(ipcProcess.Id, processWrapper);
                    processes.Add(ipcProcess.Id, new SynchronizationContextTaskScheduler(new ThreadSynchronizationContext(TaskManager.Token)));
                };
            }

            return processTask;
        }

        T IProcessManager.Configure<T>(T processTask, string workingDirectory) => Configure(processTask, new ProcessOptions());

        public BaseProcessWrapper WrapProcess(string taskName,
            Process process,
            IOutputProcessor outputProcessor,
            Action onStart,
            Action onEnd,
            Action<Exception, string> onError,
            CancellationToken token)
        {
            return new RemoteProcessWrapper(this, process, outputProcessor, onStart, onEnd, onError, token);
        }

        public void Stop()
        {
            Completion.Start();
        }

        private async Task<IpcClient> ConnectToServer(int port)
        {
            var client = new IpcClient(new Configuration { Port = port }, TaskManager.Token);
            client.RegisterRemoteTarget<IServer>();
            client.RegisterRemoteTarget<IProcessRunner>();
            client.RegisterLocalTarget(notifications);
            await client.Start();
            return client;
        }

        protected virtual ITask<int> RunProcessServer(string pathToServerExecutable)
        {
            return new ProcessManagerTask(TaskManager, ProcessManager, Environment, Configuration);
        }

        private void ServerStopping() => stopSignal.Set();

        private void RaiseProcessOnStart(IpcProcess process)
        {
            if (!wrappers.TryGetValue(process.Id, out var wrapper) || !processes.TryGetValue(process.Id, out var scheduler))
                throw new InvalidOperationException($"OnStart for process {process.Id} was called but there's no record of it in the process list.");

            scheduler.Schedule(s => wrapper.OnProcessStart(), null, TaskManager.Token);
        }

        private void RaiseOnProcessEnd(IpcProcess process, bool success, Exception ex, string errors)
        {
            if (!wrappers.TryGetValue(process.Id, out var wrapper) || !processes.TryGetValue(process.Id, out var scheduler))
                throw new InvalidOperationException($"OnEnd for process {process.Id} was called but there's no record of it in the process list.");

            var task = new Task(s => wrapper.OnProcessEnd((IpcProcessEndEventArgs)s), new IpcProcessEndEventArgs(process, success, ex, errors),
                TaskManager.Token, TaskCreationOptions.None);

            task.ContinueWith((_, __) => {
                // process is done and it won't be restarted, cleanup
                if (process.ProcessOptions.MonitorOptions != MonitorOptions.KeepAlive)
                {
                    lock(processes)
                    {
                        if (wrappers.ContainsKey(process.Id))
                        {
                            processes.Remove(process.Id);
                            wrappers.Remove(process.Id);
                            scheduler.Dispose();
                            ((ThreadSynchronizationContext)scheduler.Context).Dispose();
                        }
                    }
                }
            }, null, TaskManager.Token, TaskContinuationOptions.None, TaskManager.GetScheduler(TaskAffinity.None));
            task.Start(scheduler);
        }

        private void RaiseProcessOnError(IpcProcess process, string errors)
        {
            if (!wrappers.TryGetValue(process.Id, out var wrapper) || !processes.TryGetValue(process.Id, out var scheduler))
                throw new InvalidOperationException($"OnError for process {process.Id} was called but there's no record of it in the process list.");

            scheduler.Schedule(s => wrapper.OnProcessError((IpcProcessErrorEventArgs)s), new IpcProcessErrorEventArgs(process, errors), TaskManager.Token);
        }

        private void RaiseProcessOnOutput(IpcProcess process, string line)
        {
            if (!wrappers.TryGetValue(process.Id, out var wrapper) || !processes.TryGetValue(process.Id, out var scheduler))
                throw new InvalidOperationException($"OnError for process {process.Id} but there's no record of it in the process list.");

            scheduler.Schedule(s => wrapper.OnProcessOutput((IpcProcessOutputEventArgs)s), new IpcProcessOutputEventArgs(process, line), TaskManager.Token);
        }

        private void RaiseProcessRestart(IpcProcess process, ProcessRestartReason reason)
        {
            if (!processes.TryGetValue(process.Id, out var scheduler))
                throw new InvalidOperationException($"OnRestart for process {process.Id} but there's no record of it in the process list.");

            scheduler.Schedule(s => OnProcessRestart?.Invoke(this, (IpcProcessRestartEventArgs)s), new IpcProcessRestartEventArgs(process, reason), TaskManager.Token);
        }

        private bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            disposed = true;

            if (disposing)
            {
                RemoteProcessWrapper[] wraps;
                SynchronizationContextTaskScheduler[] procs;
                lock(processes)
                {
                    wraps = wrappers.Values.ToArray();
                    procs = processes.Values.ToArray();
                    wrappers.Clear();
                    processes.Clear();
                }

                foreach (var w in wraps) w.Detach();

                if (ownsTaskManager)
                    TaskManager?.Dispose();

                if (ownsProcessManager)
                    ProcessManager?.Dispose();

                foreach (var p in procs)
                {
                    p.Dispose();
                    ((IDisposable)p.Context).Dispose();
                }

                ipcClient.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public ITaskManager TaskManager { get; }
        public IProcessManager ProcessManager { get; }
        public IProcessServerConfiguration Configuration { get; }
        public IServer Server => ipcClient.GetRemoteTarget<IServer>();
        public IProcessRunner ProcessRunner => ipcClient.GetRemoteTarget<IProcessRunner>();
        public ITask<bool> Completion { get; }
        public IProcessEnvironment DefaultProcessEnvironment { get; }
        public IEnvironment Environment { get; }

        class Notifications : IProcessNotifications, IServerNotifications
        {
            private readonly ProcessServer server;

            public Notifications(ProcessServer server)
            {
                this.server = server;
            }

            public async Task ProcessOnEnd(IpcProcess process, bool success, Exception ex, string errors) =>
                server.RaiseOnProcessEnd(process, success, ex, errors);

            public async Task ProcessOnError(IpcProcess process, string errors) => server.RaiseProcessOnError(process, errors);

            public async Task ProcessOnOutput(IpcProcess process, string line) =>
                server.RaiseProcessOnOutput(process, line);

            public async Task ProcessOnStart(IpcProcess process) => server.RaiseProcessOnStart(process);

            public async Task ServerStopping() => server.ServerStopping();
            public async Task ProcessRestarting(IpcProcess process, ProcessRestartReason reason) => server.RaiseProcessRestart(process, reason);
        }
    }
}

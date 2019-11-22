namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using Ipc;
    using Tasks;

    public interface IProcessServer : IDisposable
    {
        IServer Server { get; }
        IProcessRunner ProcessRunner { get; }
        event EventHandler<IpcProcessEventArgs> OnProcessStart;
        event EventHandler<IpcProcessEndEventArgs> OnProcessEnd;
        event EventHandler<IpcProcessOutputEventArgs> OnProcessOutput;
        event EventHandler<IpcProcessErrorEventArgs> OnProcessError;
    }

    public interface IProcessServerConfiguration
    {
        int Port { get; set; }
    }

    public class ProcessServer : IProcessServer
    {
        private static IProcessServer instance;

        private readonly IEnvironment environment;
        private readonly bool ownsTaskManager;
        private readonly bool ownsProcessManager;
        private readonly Notifications notifications;

        private ManualResetEventSlim stopSignal;
        private IpcClient ipcClient;

        public event EventHandler<IpcProcessEventArgs> OnProcessStart;
        public event EventHandler<IpcProcessEndEventArgs> OnProcessEnd;
        public event EventHandler<IpcProcessOutputEventArgs> OnProcessOutput;
        public event EventHandler<IpcProcessErrorEventArgs> OnProcessError;
        public event EventHandler<IpcProcessRestartEventArgs> OnProcessRestart;

        public ProcessServer(ITaskManager taskManager,
            IProcessManager processManager,
            IEnvironment environment,
            IProcessServerConfiguration configuration)
        {
            instance = this;

            ownsTaskManager = taskManager == null;
            ownsProcessManager = processManager == null;

            if (ownsTaskManager)
                taskManager = new TaskManager().Initialize();
            if (ownsProcessManager)
                processManager = new ProcessManager(environment);

            TaskManager = taskManager;
            ProcessManager = processManager;

            Configuration = configuration;
            this.environment = environment;
            notifications = new Notifications(this);
        }

        public static IProcessServer Get(ITaskManager taskManager = null, IProcessManager processManager = null)
        {
            if (instance == null)
            {
                var inst = new ProcessServer(taskManager, processManager,
                    TheEnvironment.instance.Environment, ApplicationConfiguration.Instance);
                inst.Initialize(Path.GetFullPath("Packages/com.unity.process-server/Server~"));
                instance = inst;
            }
            return instance;
        }

        public void Initialize(string pathToServerExecutable)
        {
            // we don't know where the server is, start a new one
            if (Configuration.Port == 0)
            {
                RunProcessServer(pathToServerExecutable);
            }
            ConnectToServer();
        }

        private void ConnectToServer()
        {
            var token = new CancellationTokenSource(500);
            ipcClient = new IpcClient(new Configuration { Port = Configuration.Port }, TaskManager.Token);
            ipcClient.RegisterRemoteTarget<IServer>();
            ipcClient.RegisterRemoteTarget<IProcessRunner>();
            ipcClient.RegisterLocalTarget(notifications);
            ipcClient.Start().Wait(token.Token);
        }

        protected virtual Process RunProcessServer(string pathToServerExecutable)
        {
            var token = new CancellationTokenSource(500);
            TaskManager.Token.Register(() => token.Cancel());
            var task = new ProcessManagerTask(TaskManager, ProcessManager, pathToServerExecutable, environment.UnityProjectPath);
            Configuration.Port = task.StartSync(token.Token);
            return task.Process;
        }

        public ITask<bool> ShutdownServer()
        {
            return new TPLTask<bool>(TaskManager, async () => {
                stopSignal = new ManualResetEventSlim(false);
                await Server.Stop();
                return stopSignal.Wait(500, TaskManager.Token);
            }) { Affinity = TaskAffinity.None };
        }

        private void ServerStopping()
        {
            stopSignal.Set();
        }

        private void RaiseOnProcessEnd(IpcProcess process, bool success, Exception ex, string errors) =>
            OnProcessEnd?.Invoke(this, new IpcProcessEndEventArgs(process, success, ex, errors));

        private void RaiseProcessOnError(IpcProcess process, string errors) =>
            OnProcessError?.Invoke(this, new IpcProcessErrorEventArgs(process, errors));

        private void RaiseProcessOnOutput(IpcProcess process, string line) =>
            OnProcessOutput?.Invoke(this, new IpcProcessOutputEventArgs(process, line));

        private void RaiseProcessOnStart(IpcProcess process) =>
            OnProcessStart?.Invoke(this, new IpcProcessEventArgs(process));

        private void RaiseProcessRestart(IpcProcess process, ProcessRestartReason reason) =>
            OnProcessRestart?.Invoke(this, new IpcProcessRestartEventArgs(process, reason));

        private bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                if (ownsTaskManager)
                    TaskManager?.Dispose();

                if (ownsProcessManager)
                    ProcessManager?.Dispose();
                ipcClient.Dispose();
                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }


        protected ITaskManager TaskManager { get; }
        protected IProcessManager ProcessManager { get; }
        public IProcessServerConfiguration Configuration { get; }
        public IServer Server => ipcClient.GetRemoteTarget<IServer>();
        public IProcessRunner ProcessRunner => ipcClient.GetRemoteTarget<IProcessRunner>();

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

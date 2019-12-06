namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using Ipc;
    using Tasks;
    using Unity.Editor.ProcessServer.Internal.IO;
    using Unity.Editor.Tasks.Extensions;

    public interface IProcessServer : IDisposable
    {
        IProcessServer Connect();

        IServer Server { get; }
        IProcessRunner ProcessRunner { get; }

        ITaskManager TaskManager { get; }
        IRemoteProcessManager ProcessManager { get; }
        IEnvironment Environment { get; }
        IProcessServerConfiguration Configuration { get; }
    }

    public interface IProcessServerConfiguration
    {
        int Port { get; set; }
        string ExecutablePath { get; set; }
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

        public static IProcessServer Get(ITaskManager taskManager = null)
        {
            if (instance == null)
            {
                var inst = new ProcessServer(taskManager, TheEnvironment.instance.Environment, ApplicationConfiguration.Instance);
                instance = inst;
            }
            instance.EnsureInitialized();
            return instance.Connect();
        }

        public ProcessServer(ITaskManager taskManager,
            IEnvironment environment,
            IProcessServerConfiguration configuration,
            IProcessEnvironment processEnvironment = null)
        {
            cts.Token.Register(Dispose);

            Environment = environment;
            Configuration = configuration;

            ownsTaskManager = taskManager == null;
            
            if (ownsTaskManager)
                taskManager = new TaskManager().Initialize();

            TaskManager = taskManager;
            TaskManager.Token.Register(cts.Cancel);

            if (processEnvironment == null)
            {
                localProcessManager = new ProcessManager(Environment);
                processEnvironment = localProcessManager.DefaultProcessEnvironment;
            }

            processManager = new RemoteProcessManager(processEnvironment, cts.Token);
            notifications = new ServerNotifications(this);

            
#if UNITY_EDITOR
            UnityEditor.EditorApplication.quitting += () => {
                Stop();
                Completion.WaitOne(500);
                Dispose();
            };
#endif
        }

        private void Shutdown()
        {
            new Thread(() => {
                try
                {
                    Server.Stop().FireAndForget();
                    var ret = stopSignal.Wait(300);
                    Dispose();
                }
                finally
                {
                    completionHandle.Set();
                }
            }).Start();
        }

        private void EnsureInitialized()
        {
            if (TaskManager.UIScheduler == null)
                TaskManager.Initialize();
        }

        public void Stop()
        {
            Shutdown();
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

                    task.StartAwait().Wait(cts.Token);
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
            processManager.ConnectToServer(ipcClient.GetRemoteTarget<IProcessRunner>());
            return this;
        }

        private async Task<IpcClient> ConnectToServer(int port)
        {
            var client = new IpcClient(new Configuration { Port = port }, cts.Token);
            client.RegisterRemoteTarget<IServer>();
            client.RegisterRemoteTarget<IProcessRunner>();
            client.RegisterLocalTarget(notifications);
            client.RegisterLocalTarget(processManager.ProcessNotifications);
            await client.Start();
            return client;
        }

        protected virtual ITask<int> RunProcessServer(string pathToServerExecutable)
        {
            if (localProcessManager == null)
                localProcessManager = new ProcessManager(Environment);
            return new ProcessManagerTask(TaskManager, localProcessManager, Environment, Configuration);
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
                if (ownsTaskManager)
                    TaskManager?.Dispose();

                localProcessManager?.Dispose();
                ProcessManager?.Dispose();

                ipcClient.Dispose();
                completionHandle.Set();
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

            public async Task ServerStopping() => server.ServerStopping();
            public async Task ProcessRestarting(IpcProcess process, ProcessRestartReason reason) => server.ProcessRestarting(process, reason);
        }
    }
}

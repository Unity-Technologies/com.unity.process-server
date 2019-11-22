namespace Unity.Editor.ProcessServer.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Extensions;
    using Interfaces;
    using Ipc;
    using Ipc.Hosted;
    using Ipc.Hosted.Extensions;
    using Microsoft.Extensions.Hosting;
    using SpoiledCat.SimpleIO;
    using Tasks;

    namespace Extensions
    {
        public static class Extensions
        {
            public static void AddOrUpdate<Key, Val>(this Dictionary<Key, Val> list, Key key, Val value)
            {
                if (list.ContainsKey(key))
                    list[key] = value;
                else
                    list.Add(key, value);
            }
        }
    }


    public class ProcessRunner
    {
        private readonly ITaskManager taskManager;
        private readonly IProcessManager processManager;
        private readonly IProcessEnvironment processEnvironment;
        private readonly IEnvironment environment;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private readonly Dictionary<string, IpcProcess> processes = new Dictionary<string, IpcProcess>();
        private readonly Dictionary<string, ITask> tasks = new Dictionary<string, ITask>();
        private readonly Dictionary<string, IRequestContext> clients = new Dictionary<string, IRequestContext>();

        public ProcessRunner(ITaskManager taskManager,
            IProcessManager processManager,
            IProcessEnvironment processEnvironment,
            IEnvironment environment,
            IpcHostedServer host,
            IHostApplicationLifetime app)
        {
            this.taskManager = taskManager;
            this.processManager = processManager;
            this.processEnvironment = processEnvironment;
            this.environment = environment;
            host.OnClientDisconnect += (provider, args) => {
                var c = provider.GetRequestContext();
                var reqs = clients.Keys.Where(x => clients[x] == c).ToArray();
                foreach (var key in reqs)
                {
                    clients.Remove(key);
                }
            };

            app.ApplicationStopping.Register(cts.Cancel);
        }

        public string PrepareProcess(IRequestContext client, string executable, string args,
            ProcessOptions options, string workingDirectory = null)
        {
            if (options.UseProjectPath)
            {
                var parsed = new SpoiledCat.Extensions.Configuration.ExtendedCommandLineConfigurationProvider(args.Split(' '), null);
                parsed.Load();
                if (!parsed.TryGet("projectpath", out _))
                {
                    args += $" -projectPath {environment.UnityProjectPath}";
                }
            }

            workingDirectory = workingDirectory ?? environment.UnityProjectPath;

            string id = Guid.NewGuid().ToString();
            var process = new IpcProcess(id, 0, executable, args, workingDirectory, options);

            processes.AddOrUpdate(id, process);

            return SetupProcess(client, process);
        }

        private string SetupProcess(IRequestContext client, IpcProcess process)
        {
            var id = process.Id;
            var outputProcessor = new RaiseAndDiscardOutputProcessor();
            var task = new ProcessTask<string>(taskManager, processEnvironment, process.Executable, process.Arguments, outputProcessor);

            tasks.AddOrUpdate(id, task);
            clients.AddOrUpdate(id, client);

            processManager.Configure(task, process.WorkingDirectory.ToSPath());

            task.OnStartProcess += async p => await RaiseOnProcessStart(id, p.ProcessId);
            task.OnEnd += async (t, _, success, ex) => await RaiseOnProcessEnd(id, success, ex, t.Errors);
            task.OnErrorData += async e => await RaiseOnProcessError(id, e);
            outputProcessor.OnEntry += async line => await RaiseOnProcessOutput(id, line);

            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive)
            {
                // restart the process with the same arguments when it stops
                var restartTask = new ActionTask(task.TaskManager, () => RestartProcess(client, process)) { Affinity = task.Affinity };
                task.Then(restartTask, TaskRunOptions.OnAlways);
            }
            else
            {
                task.Finally((_, __, ___) => {
                    tasks.Remove(id);
                    clients.Remove(id);
                    processes.Remove(id);
                });
            }

            return id;
        }

        private void RestartProcess(IRequestContext client, IpcProcess process)
        {
            if (cts.IsCancellationRequested)
                return;

            ProcessRestartReason reason = ProcessRestartReason.UserInitiated;
            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive)
                reason = ProcessRestartReason.KeepAlive;

            client.GetRemoteTarget<IServerNotifications>().ProcessRestarting(process, reason);
            SetupProcess(client, process);
            RunProcess(process.Id);
        }

        public void RunProcess(string id)
        {
            GetTask(id).Start();
        }

        public void AddOrUpdateTask(string id, ITask task)
        {
            if (tasks.ContainsKey(id))
                tasks[id] = task;
            else
                tasks.Add(id, task);
        }

        public ITask GetTask(string id)
        {
            if (tasks.TryGetValue(id, out var task))
            {
                return task;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        public IpcProcess GetProcess(string id)
        {
            if (processes.TryGetValue(id, out var process))
            {
                return process;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        private IpcProcess UpdateProcess(string id, int processId)
        {
            if (processes.TryGetValue(id, out var process))
            {
                var newp = new IpcProcess(id, processId, process.Executable, process.Arguments, process.WorkingDirectory, process.ProcessOptions);
                processes[id] = newp;
                return newp;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        private async Task RaiseOnProcessStart(string id, int processId)
        {
            if (clients.TryGetValue(id, out var client))
            {
                var notifications = client.GetRemoteTarget<IProcessNotifications>();
                await notifications.ProcessOnStart(UpdateProcess(id, processId));
            }
        }

        private async Task RaiseOnProcessOutput(string id, string line)
        {
            if (clients.TryGetValue(id, out var client))
            {
                var notifications = client.GetRemoteTarget<IProcessNotifications>();
                await notifications.ProcessOnOutput(GetProcess(id), line);
            }
        }

        private async Task RaiseOnProcessEnd(string id, bool success, Exception ex, string errors)
        {
            if (clients.TryGetValue(id, out var client))
            {
                var notifications = client.GetRemoteTarget<IProcessNotifications>();
                await notifications.ProcessOnEnd(GetProcess(id), success, ex, errors);
            }
        }

        private async Task RaiseOnProcessError(string id, string error)
        {
            if (clients.TryGetValue(id, out var client))
            {
                var notifications = client.GetRemoteTarget<IProcessNotifications>();
                await notifications.ProcessOnError(GetProcess(id), error);
            }
        }
    }

    public class ProcessRunnerImplementation : IProcessRunner
    {
        private readonly ProcessRunner owner;
        private readonly IRequestContext client;

        public ProcessRunnerImplementation(ProcessRunner owner, IRequestContext client)
        {
            this.owner = owner;
            this.client = client;
        }

        public Task<IpcProcess> PrepareProcess(string executable, string args, string workingDirectory, ProcessOptions options)
        {
            var id = owner.PrepareProcess(client, executable, args, options, workingDirectory);
            return Task.FromResult(owner.GetProcess(id));
        }

        public Task<IpcProcess> PrepareProcess(string executable, string args, ProcessOptions options)
        {
            var id = owner.PrepareProcess(client, executable, args, options);
            return Task.FromResult(owner.GetProcess(id));
        }

        public Task RunProcess(IpcProcess process)
        {
            owner.RunProcess(process.Id);
            return Task.CompletedTask;
        }
    }
}

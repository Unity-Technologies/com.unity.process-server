namespace Unity.Editor.ProcessServer.Server
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
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
        private readonly Dictionary<string, IProcessTask> tasks = new Dictionary<string, IProcessTask>();
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

            if (host != null)
            {
                host.OnClientDisconnect += (provider, args) => {
                    var c = provider.GetRequestContext();
                    var reqs = clients.Keys.Where(x => clients[x] == c).ToArray();
                    foreach (var key in reqs)
                    {
                        clients.Remove(key);
                    }
                };
            }

            app?.ApplicationStopping.Register(cts.Cancel);
        }

        public string Prepare(IRequestContext client, string executable, string arguments, ProcessOptions options, string workingDir = null)
        {
            var outputProcessor = new RaiseAndDiscardOutputProcessor();
            var task = new ProcessTask<string>(taskManager, processEnvironment, executable, arguments, outputProcessor: outputProcessor)
                .Configure(processManager, workingDir);

            var startInfo = ProcessInfo.FromStartInfo(task.Wrapper.StartInfo);

            var id = startInfo.GetId();
            var process = new IpcProcess(id, startInfo, options);

            processes.AddOrUpdate(id, process);
            tasks.AddOrUpdate(id, task);
            clients.AddOrUpdate(id, client);

            HookupProcessHandlers(client, process, id, outputProcessor, task);

            return id;
        }

        public string Prepare(IRequestContext client, ProcessInfo startInfo, ProcessOptions options)
        {
            var id = startInfo.GetId();
            var process = new IpcProcess(id, startInfo, options);
            processes.AddOrUpdate(id, process);
            return SetupProcess(client, process);
        }

        private string SetupProcess(IRequestContext client, IpcProcess process)
        {
            var id = process.Id;

            var outputProcessor = new RaiseAndDiscardOutputProcessor();
            var task = new ProcessTask<string>(taskManager, processEnvironment, outputProcessor: outputProcessor);
            processManager.Configure(task, process.StartInfo.ToStartInfo());

            tasks.AddOrUpdate(id, task);
            clients.AddOrUpdate(id, client);

            HookupProcessHandlers(client, process, id, outputProcessor, task);

            return id;
        }

        private void HookupProcessHandlers(IRequestContext client, IpcProcess process, string id, RaiseAndDiscardOutputProcessor outputProcessor, ProcessTask<string> task)
        {
            task.OnStartProcess += async p => await RaiseOnProcessStart(id, p.ProcessId);
            task.OnEnd += async (t, _, success, ex) => await RaiseOnProcessEnd(id, success, ex, t.Errors);
            task.OnErrorData += async e => await RaiseOnProcessError(id, e);
            outputProcessor.OnEntry += async line => await RaiseOnProcessOutput(id, line);

            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive)
            {
                // restart the process with the same arguments when it stops
                var restartTask = new ActionTask(task.TaskManager, () => {
                    // if process was not manually stopped by the user
                    if (processes.ContainsKey(id))
                        RestartProcess(client, id);
                }) { Affinity = task.Affinity };
                task.Then(restartTask, TaskRunOptions.OnAlways);
            }
            else
            {
                task.Finally((_, __, ___) => {
                    tasks.Remove(id);
                    clients.Remove(id);
                    if (processes.ContainsKey(id))
                        processes.Remove(id);
                });
            }
        }

        private void RestartProcess(IRequestContext client, string id)
        {
            if (cts.IsCancellationRequested)
                return;

            var process = GetProcess(id);
            ProcessRestartReason reason = ProcessRestartReason.UserInitiated;
            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive)
                reason = ProcessRestartReason.KeepAlive;

            client.GetRemoteTarget<IServerNotifications>().ProcessRestarting(process, reason);
            SetupProcess(client, process);
            RunProcess(process.Id);
        }

        public IProcessTask RunProcess(string id)
        {
            return GetTask(id).Start();
        }

        public IProcessTask Stop(string id)
        {
            var task = GetTask(id);
            task.Stop();
            return task;
        }

        public void AddOrUpdateTask(string id, IProcessTask task)
        {
            if (tasks.ContainsKey(id))
                tasks[id] = task;
            else
                tasks.Add(id, task);
        }

        public IProcessTask GetTask(string id)
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
                var newp = new IpcProcess(id, process.StartInfo, process.ProcessOptions);
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

        public Task<IpcProcess> Prepare(string executable, string args, string workingDirectory, ProcessOptions options)
        {
            var id = owner.Prepare(client, executable, args, options, workingDirectory);
            return Task.FromResult(owner.GetProcess(id));
        }

        public Task<IpcProcess> Prepare(string executable, string args, ProcessOptions options)
        {
            var id = owner.Prepare(client, executable, args, options);
            return Task.FromResult(owner.GetProcess(id));
        }

        public Task<IpcProcess> Prepare(ProcessInfo startInfo, ProcessOptions options)
        {
            var id = owner.Prepare(client, startInfo, options);
            return Task.FromResult(owner.GetProcess(id));
        }

        public Task Run(IpcProcess process)
        {
            owner.RunProcess(process.Id);
            return Task.CompletedTask;
        }

        public Task Stop(IpcProcess process)
        {
            owner.Stop(process.Id);
            return Task.CompletedTask;
        }
    }
}

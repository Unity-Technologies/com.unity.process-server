namespace Unity.ProcessServer.Server
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Interfaces;
    using Microsoft.Extensions.Logging;
    using Rpc;

    public class ProcessRunner : IDisposable
    {
        private readonly ITaskManager taskManager;
        private readonly IProcessManager processManager;
        private readonly IProcessEnvironment processEnvironment;
        private readonly ILogger<ProcessRunner> logger;
        private readonly string accessToken;
        private readonly CancellationTokenSource cts;

        private ConcurrentDictionary<string, ContextData> processes = new ConcurrentDictionary<string, ContextData>();

        public ProcessRunner(ITaskManager taskManager,
            IProcessManager processManager,
            IProcessEnvironment processEnvironment,
            ServerConfiguration serverConfiguration,
            ILogger<ProcessRunner> logger)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(taskManager.Token);

            this.taskManager = taskManager;
            this.processManager = processManager;
            this.processEnvironment = processEnvironment;
            this.logger = logger;
            this.accessToken = serverConfiguration.AccessToken;
        }

        public void ClientDisconnecting(IRequestContext context)
        {
            if (cts.IsCancellationRequested) return;

            var reqs = processes.Where(x => x.Value.Client == context).Select(x => x.Key).ToArray();
            foreach (var key in reqs)
            {
                RemoveClient(key);
            }
        }

        public void Shutdown()
        {
            cts.Cancel();
        }

        public string Prepare(IRequestContext client, string executable, string arguments, ProcessOptions options,
            string workingDir, string accessToken)
        {
            if (accessToken != this.accessToken)
                throw new UnauthorizedAccessException();
            if (cts.IsCancellationRequested) return null;

            var outputProcessor = new RaiseUntilDetachOutputProcess();
            var task = new NativeProcessListTask<string>(taskManager, processEnvironment, executable, arguments,
                    outputProcessor: outputProcessor, cts.Token)
                .Configure(processManager, workingDir);

            var startInfo = ProcessInfo.FromStartInfo(task.Wrapper.StartInfo);
            var id = options.MonitorOptions == MonitorOptions.KeepAlive ? startInfo.GetId() : Guid.NewGuid().ToString();

            SetupProcess(client, new RpcProcess(id, startInfo, options), task, outputProcessor);

            return id;
        }

        public string Prepare(IRequestContext client, ProcessInfo startInfo, ProcessOptions options, string accessToken)
        {
            if (accessToken != this.accessToken)
                throw new UnauthorizedAccessException();
            if (cts.IsCancellationRequested) return null;

            var id = options.MonitorOptions == MonitorOptions.KeepAlive ? startInfo.GetId() : Guid.NewGuid().ToString();
            SetupProcess(client, new RpcProcess(id, startInfo, options));
            return id;
        }

        public IProcessTask RunProcess(string id, string accessToken)
        {
            if (accessToken != this.accessToken)
                throw new UnauthorizedAccessException();
            if (cts.IsCancellationRequested) return null;

            var task = GetTask(id);
            if (task.Task.Status != TaskStatus.Created)
            {
                ReplayEvents(id);
                return task;
            }

            try
            {
                task.Start();
            }
            catch (TaskCanceledException)
            {
                // we're shutting down
            }
            return task;
        }

        public IProcessTask StopProcess(string id, string accessToken)
        {
            if (accessToken != this.accessToken)
                throw new UnauthorizedAccessException();
            if (cts.IsCancellationRequested) return null;
            if (!processes.TryGetValue(id, out var data)) return null;

            data.ManuallyStopped = true;
            processes[id] = data;
            var task = data.Task;
            try
            {
                task?.Stop();
            }
            catch (TaskCanceledException)
            {
                // we're shutting down
            }

            return task;
        }

        public void Detach(string id, string accessToken)
        {
            if (accessToken != this.accessToken)
                throw new UnauthorizedAccessException();
            if (cts.IsCancellationRequested) return;

            if (processes.TryGetValue(id, out var data))
            {
                data.OutputProcessor.Detach();
            }
        }

        private string SetupProcess(IRequestContext client, RpcProcess process,
            IProcessTask<string, List<string>> task = null, RaiseUntilDetachOutputProcess outputProcessor = null)
        {
            var id = process.Id;
            ContextData data = default;

            var processExists = processes.TryGetValue(process.Id, out data);

            if (task == null && (!processExists || data.Task.IsCompleted))
            {
                outputProcessor = new RaiseUntilDetachOutputProcess();
                task = new ProcessTaskWithListOutput<string>(taskManager, processEnvironment,
                    outputProcessor: outputProcessor, token: cts.Token);
                processManager.Configure(task, process.StartInfo.ToStartInfo());
            }
            else if (task != null)
            {
                task.Dispose();
                task = null;
            }

            data = processes.GetOrAdd(id, data);

            data.Process = process;
            data.Client = client;
            if (task != null)
            {
                data.Task = task;
                data.OutputProcessor = outputProcessor;
            }

            if (data.Notifications == null)
                data.Notifications =
                    new SynchronizationContextTaskScheduler(new ThreadSynchronizationContext(cts.Token));

            processes[id] = data;

            HookupProcessHandlers(data);

            return id;
        }

        private void HookupProcessHandlers(ContextData context)
        {
            if (cts.IsCancellationRequested) return;

            var task = context.Task;
            var process = context.Process;
            var id = process.Id;

            task.OnStartProcess += p => RaiseOnProcessStart(id, p.ProcessId);
            task.OnEnd += (t, _, success, ex) => RaiseOnProcessEnd(id, success, ex, t.Errors);
            task.OnErrorData += e => RaiseOnProcessError(id, e);
            task.OnData += line => RaiseOnProcessOutput(id, line);

            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive && !context.ManuallyStopped)
            {
                // restart the process with the same arguments when it stops
                var restartTask = new ActionTask(task.TaskManager, (_, ex) => {
                    if (processes.TryGetValue(id, out var data))
                    {
                        if (data.Process.ProcessOptions.MonitorOptions != MonitorOptions.KeepAlive ||
                            data.ManuallyStopped)
                        {
                            processes.TryRemove(id, out var _);
                        }
                        else
                        {
                            RestartProcess(id);
                        }
                    }
                }, token: cts.Token) { Affinity = task.Affinity };

                task.Then(restartTask, TaskRunOptions.OnAlways);
            }
            else
            {
                task.Finally((_, __, ___) => { processes.TryRemove(id, out var _); });
            }
        }

        private void RestartProcess(string id)
        {
            if (cts.IsCancellationRequested) return;

            var client = GetClient(id);
            var process = GetProcess(id);

            ProcessRestartReason reason = ProcessRestartReason.UserInitiated;

            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive)
                reason = ProcessRestartReason.KeepAlive;

            try
            {
                client.GetRemoteTarget<IServerNotifications>().ProcessRestarting(process, reason);
            }
            catch (SocketException)
            {
                // client is gone, oh well
            }
            catch (TaskCanceledException)
            {
                // we're shutting down
                return;
            }
            catch (Exception ex)
            {
                // something else is wrong
                logger.LogError(ex, nameof(RestartProcess));
            }

            SetupProcess(client, process);
            RunProcess(process.Id, accessToken);
        }

        private void ReplayEvents(string id)
        {
            if (processes.TryGetValue(id, out var context))
            {
                var task = context.Task;
                if (task.Task.Status == TaskStatus.Running || task.Task.Status == TaskStatus.RanToCompletion ||
                    task.Task.Status == TaskStatus.Faulted)
                {
                    RaiseOnProcessStart(id, task.ProcessId);

                    var entries = context.OutputProcessor.Result?.ToArray();
                    foreach (var entry in entries)
                    {
                        RaiseOnProcessOutput(id, entry);
                    }

                    if (task.Task.Status == TaskStatus.RanToCompletion || task.Task.Status == TaskStatus.Faulted)
                    {
                        RaiseOnProcessEnd(id, task.Successful, task.Exception, task.Errors);
                    }
                }
            }
        }

        private void RemoveClient(string id)
        {
            if (processes.TryGetValue(id, out var data))
            {
                var scheduler = data.Notifications;
                scheduler.Dispose();
                ((IDisposable)scheduler.Context).Dispose();
                data.Client = null;
                data.Notifications = null;
                processes[id] = data;
            }
        }

        private IProcessTask GetTask(string id)
        {
            if (processes.TryGetValue(id, out var data))
            {
                return data.Task;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        public RpcProcess GetProcess(string id)
        {
            if (processes.TryGetValue(id, out var data))
            {
                return data.Process;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        private IRequestContext GetClient(string id)
        {
            if (processes.TryGetValue(id, out var data))
            {
                return data.Client;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        private void UpdateProcessId(string id, int processId)
        {
            if (processes.TryGetValue(id, out var data))
            {
                var process = data.Process;
                process.ProcessId = processId;
                data.Process = process;
                processes[id] = data;
            }
        }

        private RpcProcess UpdateMonitorOptions(string id, MonitorOptions options)
        {
            if (processes.TryGetValue(id, out var data))
            {
                var process = data.Process;
                var opts = process.ProcessOptions;
                opts.MonitorOptions = options;
                process.ProcessOptions = opts;
                data.Process = process;
                processes[id] = data;
                return process;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        private void RaiseOnProcessStart(string id, int processId)
        {
            if (cts.IsCancellationRequested) return;

            UpdateProcessId(id, processId);

            if (processes.TryGetValue(id, out var context) && context.Client != null)
            {
                taskManager
                    .WithAsync(async ctx => {
                        var client = ctx.Client;

                        var data = new NotificationData(
                            client.GetRemoteTarget<IProcessNotifications>(),
                            RpcProcessEventArgs.Get(ctx.Process));

                        await data.notifications.ProcessOnStart(data.StartArgs);
                        return 0;
                    }, context, TaskAffinity.Custom)
                    .Start(context.Notifications);
            }
        }

        private void RaiseOnProcessOutput(string id, string line)
        {
            if (cts.IsCancellationRequested) return;

            if (processes.TryGetValue(id, out var context) && context.Client != null)
            {
                taskManager
                    .WithAsync(async ctx => {
                        var data = new NotificationData(ctx.Client.GetRemoteTarget<IProcessNotifications>(),
                            RpcProcessOutputEventArgs.Get(ctx.Process, line));
                        await data.notifications.ProcessOnOutput(data.OutputArgs);
                        return 0;
                    }, context, TaskAffinity.Custom)
                    .Start(context.Notifications);
            }
        }

        private void RaiseOnProcessEnd(string id, bool success, Exception ex, string errors)
        {
            if (cts.IsCancellationRequested) return;

            if (processes.TryGetValue(id, out var context) && context.Client != null)
            {
                if (!success && ex != null && ex is ProcessException pe && pe.InnerException is Win32Exception)
                {
                    // don't keep alive this process, it's not starting up correctly
                    UpdateMonitorOptions(id, MonitorOptions.None);
                }

                taskManager
                    .WithAsync(async ctx => {
                            var data = new NotificationData(ctx.Client.GetRemoteTarget<IProcessNotifications>(),
                                RpcProcessEndEventArgs.Get(ctx.Process, success, ex.GetExceptionMessage(),
                                    (ex as ProcessException)?.ErrorCode ?? 0, ex?.GetType().ToString() ?? string.Empty,
                                    errors));

                            await data.notifications.ProcessOnEnd(data.EndArgs);
                            return 0;
                        },
                        context, TaskAffinity.Custom)
                    .Start(context.Notifications);
            }
        }

        private void RaiseOnProcessError(string id, string error)
        {
            if (cts.IsCancellationRequested) return;

            if (processes.TryGetValue(id, out var context) && context.Client != null)
            {

                taskManager
                    .WithAsync(async ctx => {
                        var data = new NotificationData(ctx.Client.GetRemoteTarget<IProcessNotifications>(),
                            RpcProcessErrorEventArgs.Get(ctx.Process, error));
                        await data.notifications.ProcessOnError(data.ErrorArgs);
                        return 0;
                    }, context, TaskAffinity.Custom)
                    .Start(context.Notifications);
            }
        }

        private bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;
            if (disposing)
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }

                disposed = true;

                SynchronizationContextTaskScheduler[] n;
                lock(processes)
                {
                    n = processes.Values.Select(x => x.Notifications).ToArray();
                    processes.Clear();
                }

                foreach (var p in n)
                {
                    p.Dispose();
                    ((IDisposable)p.Context).Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        struct NotificationData
        {
            private object args;
            public IProcessNotifications notifications;

            public RpcProcessEndEventArgs EndArgs => (RpcProcessEndEventArgs)args;
            public RpcProcessOutputEventArgs OutputArgs => (RpcProcessOutputEventArgs)args;
            public RpcProcessErrorEventArgs ErrorArgs => (RpcProcessErrorEventArgs)args;
            public RpcProcessEventArgs StartArgs => (RpcProcessEventArgs)args;

            public NotificationData(IProcessNotifications notifications, RpcProcessEndEventArgs args)
            {
                this.notifications = notifications;
                this.args = args;
            }

            public NotificationData(IProcessNotifications notifications, RpcProcessOutputEventArgs args)
            {
                this.notifications = notifications;
                this.args = args;
            }

            public NotificationData(IProcessNotifications notifications, RpcProcessErrorEventArgs args)
            {
                this.notifications = notifications;
                this.args = args;
            }

            public NotificationData(IProcessNotifications notifications, RpcProcessEventArgs args)
            {
                this.notifications = notifications;
                this.args = args;
            }
        }

        public class Implementation : IProcessRunner
        {
            private readonly ProcessRunner owner;
            private readonly IRequestContext client;

            public Implementation(ProcessRunner owner, IRequestContext client)
            {
                this.owner = owner;
                this.client = client;
            }

            public Task<RpcProcess> Prepare(string executable, string args, string workingDirectory,
                ProcessOptions options, string accessToken)
            {
                var id = owner.Prepare(client, executable, args, options, workingDirectory, accessToken);
                return Task.FromResult(owner.GetProcess(id));
            }

            public Task<RpcProcess> Prepare(string executable, string args, ProcessOptions options, string accessToken)
            {
                var id = owner.Prepare(client, executable, args, options, null, accessToken);
                return Task.FromResult(owner.GetProcess(id));
            }

            public Task<RpcProcess> Prepare(ProcessInfo startInfo, ProcessOptions options, string accessToken)
            {
                var id = owner.Prepare(client, startInfo, options, accessToken);
                return Task.FromResult(owner.GetProcess(id));
            }

            public Task Run(RpcProcess process, string accessToken)
            {
                owner.RunProcess(process.Id, accessToken);
                return Task.CompletedTask;
            }

            public Task Stop(RpcProcess process, string accessToken)
            {
                owner.StopProcess(process.Id, accessToken);
                return Task.CompletedTask;
            }

            public Task Detach(RpcProcess process, string accessToken)
            {
                owner.Detach(process.Id, accessToken);
                return Task.CompletedTask;
            }

            public Task Run(string id, string accessToken)
            {
                owner.RunProcess(id, accessToken);
                return Task.CompletedTask;
            }

            public Task Stop(string id, string accessToken)
            {
                owner.StopProcess(id, accessToken);
                return Task.CompletedTask;
            }

            public Task Detach(string id, string accessToken)
            {
                owner.Detach(id, accessToken);
                return Task.CompletedTask;
            }
        }

        private struct ContextData
        {
            public RpcProcess Process;
            public IProcessTask<string, List<string>> Task;
            public IRequestContext Client;
            public RaiseUntilDetachOutputProcess OutputProcessor;
            public SynchronizationContextTaskScheduler Notifications;
            public bool ManuallyStopped;
        }
    }
}

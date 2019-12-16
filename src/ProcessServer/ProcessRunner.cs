namespace Unity.ProcessServer.Server
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Extensions;
    using Interfaces;
    using Microsoft.Extensions.Logging;
    using Rpc;

    namespace Extensions
    {
        internal static class Extensions
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

    public class ProcessRunner : IDisposable
    {
        private readonly ITaskManager taskManager;
        private readonly IProcessManager processManager;
        private readonly IProcessEnvironment processEnvironment;
        private readonly ILogger<ProcessRunner> logger;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private Dictionary<string, RpcProcess> processes = new Dictionary<string, RpcProcess>();
        private Dictionary<string, IProcessTask> tasks = new Dictionary<string, IProcessTask>();
        private Dictionary<string, IRequestContext> clients = new Dictionary<string, IRequestContext>();
        private readonly Dictionary<string, SynchronizationContextTaskScheduler> notifications = new Dictionary<string, SynchronizationContextTaskScheduler>();

        public ProcessRunner(ITaskManager taskManager,
            IProcessManager processManager,
            IProcessEnvironment processEnvironment,
            ILogger<ProcessRunner> logger)
        {
            this.taskManager = taskManager;
            this.processManager = processManager;
            this.processEnvironment = processEnvironment;
            this.logger = logger;

        }

        public void ClientDisconnecting(IRequestContext context)
        {
            var reqs = clients.Keys.Where(x => clients[x] == context).ToArray();
            foreach (var key in reqs)
            {
                clients.Remove(key);
                if (notifications.TryGetValue(key, out var scheduler))
                {
                    notifications.Remove(key);
                    scheduler.Dispose();
                    ((IDisposable)scheduler.Context).Dispose();
                }
            }
        }

        public void Shutdown()
        {
            cts.Cancel();
        }

        public string Prepare(IRequestContext client, string executable, string arguments, ProcessOptions options,
            string workingDir = null)
        {
            var outputProcessor = new RaiseAndDiscardOutputProcessor();
            var task = new ProcessTask<string>(taskManager, cts.Token, processEnvironment, executable, arguments,
                    outputProcessor: outputProcessor)
                .Configure(processManager, workingDir);

            var startInfo = ProcessInfo.FromStartInfo(task.Wrapper.StartInfo);

            var id = startInfo.GetId();
            var process = new RpcProcess(id, startInfo, options);

            notifications.Add(id, new SynchronizationContextTaskScheduler(new ThreadSynchronizationContext(cts.Token)));
            processes.AddOrUpdate(id, process);
            tasks.AddOrUpdate(id, task);
            clients.AddOrUpdate(id, client);

            HookupProcessHandlers(client, process, id, outputProcessor, task);

            return id;
        }

        public string Prepare(IRequestContext client, ProcessInfo startInfo, ProcessOptions options)
        {
            var id = startInfo.GetId();
            var process = new RpcProcess(id, startInfo, options);
            notifications.Add(id, new SynchronizationContextTaskScheduler(new ThreadSynchronizationContext(cts.Token)));
            processes.AddOrUpdate(id, process);
            return SetupProcess(client, process);
        }

        private string SetupProcess(IRequestContext client, RpcProcess process)
        {
            var id = process.Id;

            var outputProcessor = new RaiseAndDiscardOutputProcessor();
            var task = new ProcessTask<string>(taskManager, cts.Token, processEnvironment,
                outputProcessor: outputProcessor);
            processManager.Configure(task, process.StartInfo.ToStartInfo());

            tasks.AddOrUpdate(id, task);
            clients.AddOrUpdate(id, client);

            HookupProcessHandlers(client, process, id, outputProcessor, task);

            return id;
        }

        private void HookupProcessHandlers(IRequestContext client, RpcProcess process, string id,
            RaiseAndDiscardOutputProcessor outputProcessor, ProcessTask<string> task)
        {
            if (cts.IsCancellationRequested) return;

            task.OnStartProcess += p => RaiseOnProcessStart(id, p.ProcessId);
            task.OnEnd += (t, _, success, ex) => RaiseOnProcessEnd(id, success, ex, t.Errors);
            task.OnErrorData += e => RaiseOnProcessError(id, e);
            outputProcessor.OnEntry += line => RaiseOnProcessOutput(id, line);

            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive)
            {
                // restart the process with the same arguments when it stops
                var restartTask = new ActionTask(task.TaskManager, cts.Token, (_, ex) => {
                    if (processes.ContainsKey(id))
                    {
                        var p = GetProcess(id);
                        if (p.ProcessOptions.MonitorOptions != MonitorOptions.KeepAlive)
                        {
                            // if process was not manually stopped by the user
                            tasks.Remove(id);
                            clients.Remove(id);
                            if (processes.ContainsKey(id))
                                processes.Remove(id);
                            return;
                        }
                        RestartProcess(client, id);
                    }
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
            if (cts.IsCancellationRequested) return;

            var process = GetProcess(id);
            ProcessRestartReason reason = ProcessRestartReason.UserInitiated;
            if (process.ProcessOptions.MonitorOptions == MonitorOptions.KeepAlive)
                reason = ProcessRestartReason.KeepAlive;

            try
            {
                client.GetRemoteTarget<IServerNotifications>().ProcessRestarting(process, reason);
            }
            catch(SocketException ex)
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
            RunProcess(process.Id);
        }

        public IProcessTask RunProcess(string id)
        {
            if (cts.IsCancellationRequested) return null;
            var task = GetTask(id);
            try
            {
                task?.Start();
            }
            catch (TaskCanceledException)
            {
                // we're shutting down
            }
            return task;
        }

        public IProcessTask StopProcess(string id)
        {
            if (cts.IsCancellationRequested) return null;

            var process = GetProcess(id);
            process.ProcessOptions.MonitorOptions = MonitorOptions.None;

            var task = GetTask(id);
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

        public RpcProcess GetProcess(string id)
        {
            if (processes.TryGetValue(id, out var process))
            {
                return process;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
        }

        private RpcProcess UpdateMonitorOptions(string id, MonitorOptions options)
        {
            if (processes.TryGetValue(id, out var process))
            {
                var opts = process.ProcessOptions;
                opts.MonitorOptions = options;
                process.ProcessOptions = opts;
                processes[id] = process;
                return process;
            }
            throw new InvalidOperationException("Cannot find process with id " + id);
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

        private void RaiseOnProcessStart(string id, int processId)
        {
            if (cts.IsCancellationRequested) return;
            if (clients.TryGetValue(id, out var client))
            {
                if (!notifications.TryGetValue(id, out var scheduler))
                {
                    logger.LogError(
                        $"OnStart for process {id} was called but there's no record of it in the notifications list.");
                    return;
                }

                taskManager
                    .WithAsync(async data => {
                            await data.notifications.ProcessOnStart(data.StartArgs);
                            return 0;
                        },
                        new NotificationData(client.GetRemoteTarget<IProcessNotifications>(),
                            RpcProcessEventArgs.Get(GetProcess(id))), TaskAffinity.Custom)
                    .Start(scheduler);
            }
        }

        private void RaiseOnProcessOutput(string id, string line)
        {
            if (cts.IsCancellationRequested) return;
            if (clients.TryGetValue(id, out var client))
            {
                if (!notifications.TryGetValue(id, out var scheduler))
                {
                    logger.LogError(
                        $"OnOutput for process {id} was called but there's no record of it in the notifications list.");
                    return;
                }

                taskManager
                    .WithAsync(async data => {
                            await data.notifications.ProcessOnOutput(data.OutputArgs);
                            return 0;
                        },
                        new NotificationData(client.GetRemoteTarget<IProcessNotifications>(),
                            RpcProcessOutputEventArgs.Get(GetProcess(id), line)), TaskAffinity.Custom)
                    .Start(scheduler);
            }
        }

        private void RaiseOnProcessEnd(string id, bool success, Exception ex, string errors)
        {
            if (cts.IsCancellationRequested) return;
            if (clients.TryGetValue(id, out var client))
            {
                if (!success && ex != null && ex is ProcessException pe && pe.InnerException is Win32Exception)
                {
                    // don't keep alive this process, it's not starting up correctly
                    UpdateMonitorOptions(id, MonitorOptions.None);
                }

                if (!notifications.TryGetValue(id, out var scheduler))
                {
                    logger.LogError(
                        $"OnEnd for process {id} was called but there's no record of it in the notifications list.");
                    return;
                }

                taskManager
                    .WithAsync(async data => {
                            await data.notifications.ProcessOnEnd(data.EndArgs);
                            return 0;
                        }, new NotificationData(client.GetRemoteTarget<IProcessNotifications>(),
                            RpcProcessEndEventArgs.Get(GetProcess(id), success, ex.GetExceptionMessage(),
                                (ex as ProcessException)?.ErrorCode ?? 0, ex?.GetType().ToString() ?? string.Empty, errors)),
                        TaskAffinity.Custom)
                    .Start(scheduler);
            }
        }

        private void RaiseOnProcessError(string id, string error)
        {
            if (cts.IsCancellationRequested) return;
            if (clients.TryGetValue(id, out var client))
            {
                if (!notifications.TryGetValue(id, out var scheduler))
                {
                    logger.LogError(
                        $"OnEnd for process {id} was called but there's no record of it in the notifications list.");
                    return;
                }

                taskManager
                    .WithAsync(async data => {
                            await data.notifications.ProcessOnError(data.ErrorArgs);
                            return 0;
                        },
                        new NotificationData(client.GetRemoteTarget<IProcessNotifications>(),
                            RpcProcessErrorEventArgs.Get(GetProcess(id), error)), TaskAffinity.Custom)
                    .Start(scheduler);
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
                clients = null;
                processes = null;
                tasks = null;

                SynchronizationContextTaskScheduler[] n;
                lock (notifications)
                {
                    n = notifications.Values.ToArray();
                    notifications.Clear();
                }

                foreach (var p in n)
                {
                    p.Dispose();
                    ((IDisposable)p.Context).Dispose();
                }

                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
                ProcessOptions options)
            {
                var id = owner.Prepare(client, executable, args, options, workingDirectory);
                return Task.FromResult(owner.GetProcess(id));
            }

            public Task<RpcProcess> Prepare(string executable, string args, ProcessOptions options)
            {
                var id = owner.Prepare(client, executable, args, options);
                return Task.FromResult(owner.GetProcess(id));
            }

            public Task<RpcProcess> Prepare(ProcessInfo startInfo, ProcessOptions options)
            {
                var id = owner.Prepare(client, startInfo, options);
                return Task.FromResult(owner.GetProcess(id));
            }

            public Task Run(RpcProcess process)
            {
                owner.RunProcess(process.Id);
                return Task.CompletedTask;
            }

            public Task Stop(RpcProcess process)
            {
                owner.StopProcess(process.Id);
                return Task.CompletedTask;
            }
        }
    }

    internal static class ProcessTaskExtensions
    {
        public static string GetExceptionMessage(this Exception ex)
        {
            if (ex == null) return string.Empty;
            var message = GetExceptionMessageShort(ex);

            message += Environment.NewLine + "=======";

            var caller = Environment.StackTrace;
            var stack = caller.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            message += Environment.NewLine + string.Join(Environment.NewLine, stack.Skip(1).SkipWhile(x => x.Contains(nameof(GetExceptionMessage)) || x.Contains("LogFacade")).ToArray());
            return message;
        }

        public static string GetExceptionMessageShort(this Exception ex)
        {
            if (ex == null) return string.Empty;
            var message = ex.ToString();
            var inner = ex.InnerException;
            while (inner != null)
            {
                message += Environment.NewLine + inner.ToString();
                inner = inner.InnerException;
            }
            return message;
        }

        internal static void Schedule(this SynchronizationContextTaskScheduler scheduler, ITaskManager taskManager, Func<Task> action)
        {
            taskManager.WithAsync(action, TaskAffinity.Custom).Start(scheduler);
        }
    }
}

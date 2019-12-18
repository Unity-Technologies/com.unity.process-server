namespace Unity.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using Unity.Editor.Tasks;
    using Unity.ProcessServer.Internal.IO;

    public interface IRemoteProcessManager : IProcessManager
    {
        T Configure<T>(T processTask, ProcessOptions options = default, string workingDirectory = null)
            where T : IProcessTask;
        T Configure<T>(T processTask, ProcessStartInfo startInfo, ProcessOptions options = default)
            where T : IProcessTask;

        event EventHandler<RpcProcessRestartEventArgs> OnProcessRestart;
    }

    class RemoteProcessEnvironment : IProcessEnvironment
    {
        private readonly IProcessEnvironment localProcessEnvironment;

        public RemoteProcessEnvironment(IProcessEnvironment localProcessEnvironment)
        {
            this.localProcessEnvironment = localProcessEnvironment;
        }

        public void Configure(ProcessStartInfo psi)
        {
            localProcessEnvironment.Configure(psi);
        }

        public IEnvironment Environment => localProcessEnvironment.Environment;
    }

    class RemoteProcessManager : IRemoteProcessManager
    {
        private readonly Dictionary<string, SynchronizationContextTaskScheduler> schedulers = new Dictionary<string, SynchronizationContextTaskScheduler>();
        private readonly Dictionary<string, List<RemoteProcessWrapper>> wrappers = new Dictionary<string, List<RemoteProcessWrapper>>();
        private readonly CancellationTokenSource cts;
        private readonly IProcessServer server;
        public event EventHandler<RpcProcessRestartEventArgs> OnProcessRestart;

        public RemoteProcessManager(IProcessServer server, IProcessEnvironment environment, CancellationToken token)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            this.server = server;
            DefaultProcessEnvironment = environment;
            ProcessNotifications = new Notifications(this);
        }

        public T Configure<T>(T processTask, ProcessStartInfo startInfo, ProcessOptions options = default) where T : IProcessTask
        {
            processTask.Configure(this, startInfo);
            if (processTask.Wrapper is RemoteProcessWrapper wrapper)
            {
                wrapper.Configure(options);
                wrapper.OnProcessPrepared += (processWrapper, ipcProcess) => {
	                if (!wrappers.ContainsKey(ipcProcess.Id))
	                {
		                wrappers.Add(ipcProcess.Id, new List<RemoteProcessWrapper>());
		                schedulers.Add(ipcProcess.Id, new SynchronizationContextTaskScheduler(new ThreadSynchronizationContext(cts.Token)));
	                }
	                wrappers[ipcProcess.Id].Add(processWrapper);
                };
            }
            return processTask;
        }

        public T Configure<T>(T processTask, ProcessOptions options = default, string workingDirectory = null) where T : IProcessTask
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
            startInfo.WorkingDirectory = workingDirectory;

            return Configure(processTask, startInfo, options);
        }

        public T Configure<T>(T processTask, string workingDirectory = null) where T : IProcessTask => Configure(processTask, default, workingDirectory);

        public T Configure<T>(T processTask, ProcessStartInfo startInfo) where T : IProcessTask => Configure(processTask, startInfo, default);

        public BaseProcessWrapper WrapProcess(string taskName, ProcessStartInfo startInfo, IOutputProcessor outputProcessor,
            Action onStart, Action onEnd, Action<Exception, string> onError,
            CancellationToken token)
        {
            return new RemoteProcessWrapper(server, startInfo, outputProcessor, onStart, onEnd, onError, token);
        }

        public void Stop()
        {
            Dispose();
        }

        private void RaiseProcessOnStart(RpcProcessEventArgs args)
        {
            if (!wrappers.TryGetValue(args.Process.Id, out var list) || !schedulers.TryGetValue(args.Process.Id, out var scheduler))
                throw new InvalidOperationException($"OnStart for process {args.Process.Id} was called but there's no record of it in the process list.");

            foreach (var wrapper in list)
            {
	            scheduler.Schedule(s => wrapper.OnProcessStart((RpcProcessEventArgs)s), args, cts.Token);
            }
        }

        private void RaiseOnProcessEnd(RpcProcessEndEventArgs args)
        {
            if (!wrappers.TryGetValue(args.Process.Id, out var list) || !schedulers.TryGetValue(args.Process.Id, out var scheduler))
                throw new InvalidOperationException($"OnEnd for process {args.Process.Id} was called but there's no record of it in the process list.");

            foreach (var wrapper in list)
            {
	            var task = new Task(s => wrapper.OnProcessEnd((RpcProcessEndEventArgs)s), args,
		            cts.Token, TaskCreationOptions.None);

	            if (args.Process.ProcessOptions.MonitorOptions != MonitorOptions.KeepAlive)
	            {
		            task.ContinueWith((_, __) => {
			            lock(schedulers)
			            {
				            if (wrappers.ContainsKey(args.Process.Id))
				            {
					            schedulers.Remove(args.Process.Id);
					            wrappers.Remove(args.Process.Id);
					            scheduler.Dispose();
					            ((ThreadSynchronizationContext)scheduler.Context).Dispose();
				            }
			            }
		            }, null, cts.Token, TaskContinuationOptions.None, TaskScheduler.Default);
	            }
	            task.Start(scheduler);
            }
        }

        private void RaiseProcessOnError(RpcProcessErrorEventArgs args)
        {
            if (!wrappers.TryGetValue(args.Process.Id, out var list) || !schedulers.TryGetValue(args.Process.Id, out var scheduler))
                throw new InvalidOperationException($"OnError for process {args.Process.Id} was called but there's no record of it in the process list.");

            foreach (var wrapper in list)
            {
	            scheduler.Schedule(s => wrapper.OnProcessError((RpcProcessErrorEventArgs)s), args, cts.Token);
            }
        }

        private void RaiseProcessOnOutput(RpcProcessOutputEventArgs args)
        {
            if (!wrappers.TryGetValue(args.Process.Id, out var list) || !schedulers.TryGetValue(args.Process.Id, out var scheduler))
                throw new InvalidOperationException($"OnOutput for process {args.Process.Id} was called but there's no record of it in the process list.");

            foreach (var wrapper in list)
            {
	            scheduler.Schedule(s => wrapper.OnProcessOutput((RpcProcessOutputEventArgs)s), args, cts.Token);
            }
        }

        internal void RaiseProcessRestart(RpcProcess process, ProcessRestartReason reason)
        {
            if (!wrappers.TryGetValue(process.Id, out var list) || !schedulers.TryGetValue(process.Id, out var scheduler))
                throw new InvalidOperationException($"OnRestart for process {process.Id} was called but there's no record of it in the process list.");

            foreach (var wrapper in list)
            {
	            scheduler.Schedule(s => OnProcessRestart?.Invoke(this, (RpcProcessRestartEventArgs)s),
		            new RpcProcessRestartEventArgs(process, reason), cts.Token);
            }
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
                lock (schedulers)
                {
	                wraps = wrappers.Values.SelectMany(x => x).ToArray();
                    procs = schedulers.Values.ToArray();
                    wrappers.Clear();
                    schedulers.Clear();
                }

                foreach (var w in wraps) w.Detach();
                foreach (var p in procs)
                {
                    p.Dispose();
                    ((IDisposable)p.Context).Dispose();
                }
                cts.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public IProcessEnvironment DefaultProcessEnvironment { get; }
        public IProcessNotifications ProcessNotifications { get; }

        class Notifications : IProcessNotifications
        {
            private readonly RemoteProcessManager manager;

            public Notifications(RemoteProcessManager manager)
            {
                this.manager = manager;
            }

            public Task ProcessOnEnd(RpcProcessEndEventArgs args)
            {
                manager.RaiseOnProcessEnd(args);
                return Task.CompletedTask;
            }

            public Task ProcessOnError(RpcProcessErrorEventArgs args)
            {
                manager.RaiseProcessOnError(args);
                return Task.CompletedTask;
            }

            public Task ProcessOnOutput(RpcProcessOutputEventArgs args)
            {
                manager.RaiseProcessOnOutput(args);
                return Task.CompletedTask;
            }

            public Task ProcessOnStart(RpcProcessEventArgs args)
            {
                manager.RaiseProcessOnStart(args);
                return Task.CompletedTask;
            }
        }
    }
}

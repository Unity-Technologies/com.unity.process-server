namespace Unity.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using Editor.Tasks;
    using Interfaces;

    public class RemoteProcessWrapper : BaseProcessWrapper
    {
        public event Action<RemoteProcessWrapper, RpcProcess> OnProcessPrepared;

        private IProcessServer server;
        private readonly IOutputProcessor outputProcessor;

        private Action onStart;
        private Action onEnd;
        private Action<Exception, string> onError;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private RpcProcess remoteProcess;
        private Exception thrownException = null;
        private readonly List<string> errors = new List<string>();
        private bool detached = false;

        public RemoteProcessWrapper(
            IProcessServer server,
            ProcessStartInfo startInfo,
            IOutputProcessor outputProcessor,
            Action onStart,
            Action onEnd,
            Action<Exception, string> onError,
            CancellationToken token)
            : base(startInfo)
        {
            this.server = server;
            this.outputProcessor = outputProcessor;
            this.onStart = onStart;
            this.onEnd = onEnd;
            this.onError = onError;
            token.Register(cts.Cancel);
        }

        public void Configure(ProcessOptions options)
        {
            ProcessOptions = options;
        }

        private IProcessRunner GetRunner()
        {
            var runner = server.ProcessRunner;
            if (runner == null)
            {
                server = server.ConnectSync();
                runner = server?.ProcessRunner;
            }
            return runner;
        }

        public override void Run()
        {
            try
            {
                var runner = GetRunner();

                var task = runner.Prepare(ProcessInfo.FromStartInfo(StartInfo), ProcessOptions);

                task.Wait(cts.Token);

                remoteProcess = task.Result;

                OnProcessPrepared?.Invoke(this, remoteProcess);

                runner.Run(remoteProcess).Wait(cts.Token);

                cts.Token.WaitHandle.WaitOne();
            }
            catch (Exception ex)
            {
                thrownException = new ProcessException(-99, ex.Message, ex);
            }

            HasExited = true;

            if (thrownException != null || errors.Count > 0)
                RaiseOnError(thrownException, string.Join(Environment.NewLine, errors.ToArray()));

            RaiseOnEnd();
        }

        public override void Stop(bool dontWait = false)
        {
            var runner = GetRunner();
            if (runner != null)
            {
                var task = runner.Stop(remoteProcess);
                if (!dontWait)
                {
                    try
                    {
                        task.Wait(cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Do nothing.
                    }
                }
            }
            Dispose();
        }

        public override void Detach()
        {
            if (!detached)
            {
                var runner = GetRunner();
                runner.Detach(remoteProcess);
                detached = true;
                Dispose();
            }
        }

        public void OnProcessStart(RpcProcessEventArgs args)
        {
            ProcessId = args.Process.ProcessId;
            RaiseOnStart();
        }

        public void OnProcessError(RpcProcessErrorEventArgs e)
        {
            if (disposed) return;
            errors.Add(e.Errors);
        }

        public void OnProcessOutput(RpcProcessOutputEventArgs e)
        {
            if (disposed) return;
            outputProcessor.Process(e.Data);
        }

        public void OnProcessEnd(RpcProcessEndEventArgs e)
        {
            if (disposed) return;

            if (!e.Successful)
                thrownException = !string.IsNullOrEmpty(e.Exception) ? new ProcessException(e.Exception) : null;

            // the task is completed if the process server isn't going to restart it, we can finish up
            if (e.Process.ProcessOptions.MonitorOptions != MonitorOptions.KeepAlive)
                cts.Cancel();
        }

        private void RaiseOnStart()
        {
            onStart?.Invoke();
        }

        private void RaiseOnEnd()
        {
            onEnd?.Invoke();
        }

        private void RaiseOnError(Exception ex, string errors)
        {
            onError?.Invoke(ex, errors);
        }

        private bool disposed;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposed) return;
            disposed = true;

            if (disposing)
            {
                OnProcessPrepared = null;
                cts.Cancel();
                cts.Dispose();
            }

        }

        public ProcessOptions ProcessOptions { get; private set; }
    }
}

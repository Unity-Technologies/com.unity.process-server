namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using Interfaces;
    using Tasks;


    public class RemoteProcessWrapper : BaseProcessWrapper
    {
        public event Action<RemoteProcessWrapper, IpcProcess> OnProcessPrepared;

        private readonly IProcessRunner runner;
        private readonly IOutputProcessor outputProcessor;

        private Action onStart;
        private Action onEnd;
        private Action<Exception, string> onError;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private IpcProcess remoteProcess;
        private Exception thrownException = null;
        private readonly List<string> errors = new List<string>();
        private bool detached = false;

        public RemoteProcessWrapper(
            IProcessRunner runner,
            ProcessStartInfo startInfo,
            IOutputProcessor outputProcessor,
            Action onStart,
            Action onEnd,
            Action<Exception, string> onError,
            CancellationToken token)
            : base(startInfo)
        {
            this.runner = runner;
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

        public override void Run()
        {
            try
            {
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
            var task = runner.Stop(remoteProcess);
            if (!dontWait)
                task.Wait(cts.Token);
            Dispose();
        }

        public override void Detach()
        {
            if (!detached)
            {
                detached = true;
                Dispose();
            }
        }

        public void OnProcessStart()
        {
            RaiseOnStart();
        }

        public void OnProcessError(IpcProcessErrorEventArgs e)
        {
            errors.Add(e.Errors);
        }

        public void OnProcessOutput(IpcProcessOutputEventArgs e)
        {
            outputProcessor.Process(e.Data);
        }

        public void OnProcessEnd(IpcProcessEndEventArgs e)
        {
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

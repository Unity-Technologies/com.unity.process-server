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
        private readonly IProcessServer processServer;
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
            IProcessServer processServer,
            Process process,
            IOutputProcessor outputProcessor,
            Action onStart,
            Action onEnd,
            Action<Exception, string> onError,
            CancellationToken token)
            : base(process)
        {
            this.processServer = processServer;
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
            var runner = processServer.ProcessRunner;

            try
            {
                var task = runner.Prepare(Process.StartInfo.FileName,
                    Process.StartInfo.Arguments, Process.StartInfo.WorkingDirectory, ProcessOptions);

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

            if (thrownException != null || errors.Count > 0)
                RaiseOnError(thrownException, string.Join(Environment.NewLine, errors.ToArray()));

            RaiseOnEnd();
        }

        public override void Stop(bool dontWait = false)
        {
            Cleanup();
            cts.Cancel();
        }

        public override void Detach()
        {
            if (!detached)
            {
                detached = true;
                cts.Cancel();
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
                thrownException = e.Exception;

            // the task is completed if the process server isn't going to restart it
            if (ProcessOptions.MonitorOptions != MonitorOptions.KeepAlive)
                Stop();
        }

        private void Cleanup()
        {
            RaiseOnEnd();
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

        public ProcessOptions ProcessOptions { get; private set; }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposed) return;

            if (disposing)
            {
                disposed = true;
            }

        }
    }
}

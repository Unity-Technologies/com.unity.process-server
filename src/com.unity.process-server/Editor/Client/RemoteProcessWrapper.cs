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
            ProcessOptions processOptions,
            IOutputProcessor outputProcessor,
            Action onStart,
            Action onEnd,
            Action<Exception, string> onError,
            CancellationToken token)
            : base(process)
        {
            this.processServer = processServer;
            ProcessOptions = processOptions;
            this.outputProcessor = outputProcessor;
            this.onStart = onStart;
            this.onEnd = onEnd;
            this.onError = onError;
            token.Register(cts.Cancel);
        }

        public override void Run()
        {
            var runner = processServer.ProcessRunner;

            var task = runner.PrepareProcess(Process.StartInfo.FileName,
                Process.StartInfo.Arguments, Process.StartInfo.WorkingDirectory, ProcessOptions);
            task.Wait(cts.Token);
            remoteProcess = task.Result;

            processServer.OnProcessStart += OnProcessStart;
            processServer.OnProcessEnd += OnProcessEnd;
            processServer.OnProcessOutput += OnProcessOutput;
            processServer.OnProcessError += OnProcessError;

            runner.RunProcess(remoteProcess).Wait(cts.Token);

            cts.Token.WaitHandle.WaitOne();

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

        private void OnProcessStart(object sender, IpcProcessEventArgs e)
        {
            if (e.Process.Id != remoteProcess.Id)
                return;

            RaiseOnStart();
        }

        private void OnProcessError(object sender, IpcProcessErrorEventArgs e)
        {
            if (e.Process.Id != remoteProcess.Id)
                return;

            errors.Add(e.Errors);
        }

        private void OnProcessOutput(object sender, IpcProcessOutputEventArgs e)
        {
            if (e.Process.Id != remoteProcess.Id)
                return;

            outputProcessor.Process(e.Data);
        }

        private void OnProcessEnd(object sender, IpcProcessEndEventArgs e)
        {
            if (e.Process.Id != remoteProcess.Id)
                return;

            if (!e.Successful)
                thrownException = e.Exception;

            Stop();
        }

        private void Cleanup()
        {
            processServer.OnProcessStart -= OnProcessStart;
            processServer.OnProcessEnd -= OnProcessEnd;
            processServer.OnProcessOutput -= OnProcessOutput;
            processServer.OnProcessError -= OnProcessError;

            RaiseOnEnd();
        }

        private void RaiseOnStart()
        {
            onStart?.Invoke();
            onStart = null;
        }

        private void RaiseOnEnd()
        {
            onEnd?.Invoke();
            onEnd = null;
        }

        private void RaiseOnError(Exception ex, string errors)
        {
            onError?.Invoke(ex, errors);
            onError = null;
        }


        private bool disposed;

        public ProcessOptions ProcessOptions { get; }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposed) return;

            if (disposing)
            {
                processServer.OnProcessStart -= OnProcessStart;
                processServer.OnProcessEnd -= OnProcessEnd;
                processServer.OnProcessOutput -= OnProcessOutput;
                processServer.OnProcessError -= OnProcessError;
                disposed = true;
            }

        }
    }
}

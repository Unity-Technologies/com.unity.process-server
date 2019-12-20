namespace Unity.ProcessServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Interfaces;

    /// <summary>
    /// Client interface of the process server.
    /// </summary>
    public interface IProcessServer
    {
        IProcessServer ConnectSync();
        Task<IProcessServer> Connect();
        void ShutdownSync();
        Task Shutdown();

        /// <summary>
        /// Runs a native process on the process server, returning all the output when the process is done.
        /// All callbacks run in the UI thread.
        /// </summary>
        IProcessTask<string> NewNativeProcess(string executable,
            string arguments,
            ProcessOptions options = default,
            string workingDir = default,
            Action<IProcessTask<string>> onStart = null,
            Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None,
            CancellationToken token = default);

        /// <summary>
        /// Runs a .net process on the process server. The process will be run natively on .net if on Windows
        /// or Unity's mono if not on Windows, returning all the output when the process is done.
        /// All callbacks run in the UI thread.
        /// </summary>
        IProcessTask<string> NewDotNetProcess(string executable,
            string arguments,
            ProcessOptions options = default,
            string workingDir = default,
            Action<IProcessTask<string>> onStart = null,
            Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None,
            CancellationToken token = default);

        /// <summary>
        /// Runs a process on the process server, using Unity's Mono, returning all the output when the process is done.
        /// All callbacks run in the UI thread.
        /// </summary>
        IProcessTask<string> NewMonoProcess(string executable,
            string arguments,
            ProcessOptions options = default,
            string workingDir = default,
            Action<IProcessTask<string>> onStart = null,
            Action<IProcessTask<string>, string> onOutput = null,
            Action<IProcessTask<string>, string, bool, Exception> onEnd = null,
            IOutputProcessor<string> outputProcessor = null,
            TaskAffinity affinity = TaskAffinity.None,
            CancellationToken token = default);

        IServer Server { get; }
        IProcessRunner ProcessRunner { get; }

        ITaskManager TaskManager { get; }
        IRemoteProcessManager ProcessManager { get; }
        IEnvironment Environment { get; }
        IRpcProcessConfiguration Configuration { get; }
    }
}

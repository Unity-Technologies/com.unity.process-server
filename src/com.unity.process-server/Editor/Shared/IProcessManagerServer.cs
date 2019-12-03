using System;
using System.Threading.Tasks;

namespace Unity.Editor.ProcessServer.Interfaces
{
    public interface IServer
    {
        Task Stop();
    }

    public interface IProcessRunner
    {
        Task<IpcProcess> Prepare(string executable, string args, string workingDirectory, ProcessOptions options);
        Task<IpcProcess> Prepare(string executable, string args, ProcessOptions options);
        Task Run(IpcProcess process);

        /// <summary>
        /// Stop the process if it's not set to be kept alive, otherwise keep it running but detach
        /// all handlers to it.
        /// </summary>
        /// <param name="process"></param>
        /// <returns></returns>
        Task Detach(IpcProcess process);
    }

    public enum MonitorOptions
    {
        None,
        KeepAlive
    }

    public struct ProcessOptions
    {
        public MonitorOptions MonitorOptions;
        public bool UseProjectPath;

        public ProcessOptions(MonitorOptions monitorOptions, bool useProjectPath)
        {
            MonitorOptions = monitorOptions;
            UseProjectPath = useProjectPath;
        }

        public ProcessOptions(MonitorOptions monitorOptions)
        {
            MonitorOptions = monitorOptions;
            UseProjectPath = false;
        }

        public ProcessOptions(bool useProjectPath)
        {
            MonitorOptions = default;
            UseProjectPath = useProjectPath;
        }

    }

    public interface IServerNotifications
    {
        /// <summary>
        /// This is the last thing to be called on connected clients before the server shuts down.
        /// </summary>
        Task ServerStopping();

        Task ProcessRestarting(IpcProcess process, ProcessRestartReason reason);
    }

    public interface IProcessNotifications
    {
        Task ProcessOnStart(IpcProcess process);
        Task ProcessOnEnd(IpcProcess process, bool success, Exception ex, string errors);
        Task ProcessOnOutput(IpcProcess process, string line);
        Task ProcessOnError(IpcProcess process, string errors);
    }

    public struct IpcProcess
    {
        public string Id;
        public int ProcessId;
        public string Executable;
        public string Arguments;
        public string WorkingDirectory;
        public ProcessOptions ProcessOptions;

        public IpcProcess(string id, int processId, string executable, string arguments,
            string workingDirectory, ProcessOptions processOptions)
        {
            Id = id;
            ProcessId = processId;
            Executable = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            ProcessOptions = processOptions;
        }
    }

    public struct IpcProcessEventArgs
    {
        public IpcProcess Process;

        public IpcProcessEventArgs(IpcProcess process)
        {
            Process = process;
        }
    }

    public struct IpcProcessErrorEventArgs
    {
        public IpcProcess Process;
        public string Errors;

        public IpcProcessErrorEventArgs(IpcProcess process, string errors)
        {
            Process = process;
            Errors = errors;
        }
    }

    public struct IpcProcessOutputEventArgs
    {
        public IpcProcess Process;
        public string Data;

        public IpcProcessOutputEventArgs(IpcProcess process, string data)
        {
            Process = process;
            Data = data;
        }
    }

    public struct IpcProcessEndEventArgs
    {
        public IpcProcess Process;
        public bool Successful;
        public Exception Exception;
        public string Errors;

        public IpcProcessEndEventArgs(IpcProcess process, bool success, Exception exception, string errors)
        {
            Process = process;
            Successful = success;
            Exception = exception;
            Errors = errors;
        }
    }

    public struct IpcProcessRestartEventArgs
    {
        public IpcProcess Process;
        public ProcessRestartReason Reason;

        public IpcProcessRestartEventArgs(IpcProcess process, ProcessRestartReason reason)
        {
            Process = process;
            Reason = reason;
        }
    }

    public enum ProcessRestartReason
    {
        FirstStart,
        UserInitiated,
        KeepAlive
    }

}

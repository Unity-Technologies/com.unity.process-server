using System;
using System.Threading.Tasks;

namespace Unity.Editor.ProcessServer.Interfaces
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    public interface IServer
    {
        Task Stop();
    }

    public interface IProcessRunner
    {
        Task<IpcProcess> Prepare(string executable, string args, string workingDirectory, ProcessOptions options);
        Task<IpcProcess> Prepare(string executable, string args, ProcessOptions options);
        Task<IpcProcess> Prepare(ProcessInfo startInfo, ProcessOptions options);
        Task Run(IpcProcess process);
        Task Stop(IpcProcess process);
    }

    public enum MonitorOptions
    {
        None,
        KeepAlive
    }

    public struct ProcessInfo
    {
        public string WorkingDirectory;
        public string FileName;
        public bool UseShellExecute;
        public bool RedirectStandardError;
        public bool RedirectStandardOutput;
        public bool RedirectStandardInput;
        public Dictionary<string, string> Environment;
        public bool CreateNoWindow;
        public string Arguments;
        public ProcessWindowStyle WindowStyle;

        public string GetId()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("{0}-{1}-{2}-{3}-{4}{5}{6}{7}{8}{9}",
                WorkingDirectory?.ToUpperInvariant().GetHashCode() ?? 0,
                FileName?.ToUpperInvariant().GetHashCode() ?? 0,
                Arguments?.ToUpperInvariant().GetHashCode() ?? 0,
                Environment.GetHashCode(),
                UseShellExecute.GetHashCode(),
                RedirectStandardError.GetHashCode(),
                RedirectStandardOutput.GetHashCode(),
                RedirectStandardInput.GetHashCode(),
                CreateNoWindow.GetHashCode(),
                WindowStyle.GetHashCode());

            return sb.ToString();
        }

        public static ProcessInfo FromStartInfo(ProcessStartInfo startInfo)
        {
            return new ProcessInfo {
                WorkingDirectory = startInfo.WorkingDirectory,
                FileName = startInfo.FileName,
                UseShellExecute = startInfo.UseShellExecute,
                RedirectStandardError = startInfo.RedirectStandardError,
                RedirectStandardOutput = startInfo.RedirectStandardOutput,
                RedirectStandardInput = startInfo.RedirectStandardInput,
                Environment = new Dictionary<string, string>(startInfo.Environment),
                CreateNoWindow = startInfo.CreateNoWindow,
                Arguments = startInfo.Arguments,
                WindowStyle = startInfo.WindowStyle,
            };
        }

        public ProcessStartInfo ToStartInfo()
        {
            var startInfo = new ProcessStartInfo {
                WorkingDirectory = WorkingDirectory,
                FileName = FileName,
                UseShellExecute = UseShellExecute,
                RedirectStandardError = RedirectStandardError,
                RedirectStandardOutput = RedirectStandardOutput,
                RedirectStandardInput = RedirectStandardInput,
                CreateNoWindow = CreateNoWindow,
                Arguments = Arguments,
                WindowStyle = WindowStyle,
            };
            foreach (var k in Environment)
            {
                if (!startInfo.Environment.ContainsKey(k.Key))
                    startInfo.Environment.Add(k);
                else
                {
                    startInfo.Environment.Remove(k.Key);
                    startInfo.Environment.Add(k);
                }
            }
            return startInfo;
        }
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
        public ProcessInfo StartInfo;
        public ProcessOptions ProcessOptions;

        public IpcProcess(string id, ProcessInfo startInfo, ProcessOptions processOptions)
        {
            Id = id;
            StartInfo = startInfo;
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

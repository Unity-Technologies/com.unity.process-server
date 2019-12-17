using System;
using System.Threading.Tasks;

namespace Unity.ProcessServer.Interfaces
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;

    public interface IServer
    {
        Task Stop();
    }

    public interface IProcessRunner
    {
        Task<RpcProcess> Prepare(string executable, string args, string workingDirectory, ProcessOptions options);
        Task<RpcProcess> Prepare(string executable, string args, ProcessOptions options);
        Task<RpcProcess> Prepare(ProcessInfo startInfo, ProcessOptions options);
        Task Run(RpcProcess process);
        Task Stop(RpcProcess process);
        Task Detach(RpcProcess process);
    }

    public enum MonitorOptions
    {
        None,
        KeepAlive
    }

    [Serializable]
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

    [Serializable]
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

        Task ProcessRestarting(RpcProcess process, ProcessRestartReason reason);
    }

    public interface IProcessNotifications
    {
        Task ProcessOnStart(RpcProcessEventArgs args);
        Task ProcessOnEnd(RpcProcessEndEventArgs args);
        Task ProcessOnOutput(RpcProcessOutputEventArgs args);
        Task ProcessOnError(RpcProcessErrorEventArgs args);
    }

    [Serializable]
    public struct RpcProcess
    {
        public string Id;
        public ProcessInfo StartInfo;
        public ProcessOptions ProcessOptions;
        public int ProcessId;

        public RpcProcess(string id, ProcessInfo startInfo, ProcessOptions processOptions)
        {
            Id = id;
            StartInfo = startInfo;
            ProcessOptions = processOptions;
            ProcessId = 0;
        }
    }

    [Serializable]
    public struct RpcProcessEventArgs
    {
        public RpcProcess Process;

        public static RpcProcessEventArgs Get(RpcProcess process)
        {
            return new RpcProcessEventArgs {
                Process = process,
            };
        }
    }

    [Serializable]
    public struct RpcProcessErrorEventArgs
    {
        public RpcProcess Process;
        public string Errors;

        public static RpcProcessErrorEventArgs Get(RpcProcess process, string errors)
        {
            return new RpcProcessErrorEventArgs {
                Process = process,
                Errors = errors,
            };
        }
    }

    [Serializable]
    public struct RpcProcessOutputEventArgs
    {
        public RpcProcess Process;
        public string Data;

        public static RpcProcessOutputEventArgs Get(RpcProcess process, string data)
        {
            return new RpcProcessOutputEventArgs { Process = process, Data = data, };
        }
    }

    [Serializable]
    public struct RpcProcessEndEventArgs
    {
        public RpcProcess Process;
        public bool Successful;
        public string Exception;
        public string Errors;
        public int ErrorCode;
        public string ExceptionType;

        public static RpcProcessEndEventArgs Get(RpcProcess process, bool success, string exception, int errorCode, string exceptionType, string errors)
        {
            return new RpcProcessEndEventArgs {
                Process = process,
                Successful = success,
                Exception = exception,
                Errors = errors,
                ErrorCode = errorCode,
                ExceptionType = exceptionType,
            };
        }
    }

    [Serializable]
    public struct RpcProcessRestartEventArgs
    {
        public RpcProcess Process;
        public ProcessRestartReason Reason;

        public RpcProcessRestartEventArgs(RpcProcess process, ProcessRestartReason reason)
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

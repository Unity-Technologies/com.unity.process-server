namespace Unity.Editor.ProcessServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Interfaces;
    using Tasks;

    public static class ProcessTaskExtensions
    {
        public static T Configure<T>(this T task, IRemoteProcessManager processManager, ProcessOptions options = default, string workingDirectory = null)
            where T : IProcessTask
        {
            return processManager.Configure(task, options, workingDirectory);
        }

        //public static T Configure<T>(this T task, IProcessServer processServer, ProcessOptions options, string workingDirectory = null)
        //    where T : IProcessTask
        //{
        //    if (processServer is IRemoteProcessManager processManager)
        //        return processManager.Configure(task, options, workingDirectory);
        //    throw new InvalidOperationException($"{nameof(processServer)} is not a IRemoteProcessManager.");
        //}

        internal static void Schedule(this SynchronizationContextTaskScheduler scheduler, Action<object> action, object state, CancellationToken token)
        {
            Task.Factory.StartNew(action, state, token, TaskCreationOptions.None, scheduler);
        }
    }

    namespace Extensions
    {
        public static class TaskExtensions
        {
            public static async Task<T> Timeout<T>(this Task<T> task, int timeout, string message, CancellationToken token = default)
            {
                var ret = await Task.WhenAny(task, Task.Delay(timeout, token));
                if (ret != task)
                    throw new TimeoutException(message);
                return await task;
            }
        }

        public static class StringExtensions
        {
            public static string Quote(this string str)
            {
                if (str == null) return "\"\"";
                if (!str.StartsWith("\"")) str = "\"" + str;
                if (!str.EndsWith("\"")) str += "\"";
                return str;
            }
    }
    }
}

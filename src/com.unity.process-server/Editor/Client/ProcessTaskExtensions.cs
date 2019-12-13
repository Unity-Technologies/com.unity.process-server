namespace Unity.ProcessServer
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Editor.Tasks;
    using Interfaces;

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
}

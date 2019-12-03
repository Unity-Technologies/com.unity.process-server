//namespace Unity.Editor.ProcessServer
//{
//    using System;
//    using System.Collections.Generic;
//    using System.Diagnostics;
//    using System.Threading;
//    using Interfaces;
//    using Tasks;

//    public class RemoteProcessTask : ProcessTask<string>
//    {
//        private readonly IProcessServer processServer;
//        public ProcessOptions ProcessOptions { get; set; }

//        public RemoteProcessTask(ITaskManager taskManager,
//            IProcessEnvironment processEnvironment,
//            IProcessServer processServer,
//            string executable = null,
//            string arguments = null,
//            ProcessOptions options = default,
//            IOutputProcessor<string> outputProcessor = null)
//            : base(taskManager, processEnvironment, executable, arguments, outputProcessor ?? new SimpleOutputProcessor())
//        {
//            this.processServer = processServer;
//            this.ProcessOptions = options;
//        }

//        protected override BaseProcessWrapper GetWrapper(string taskName,
//            Process process,
//            IOutputProcessor outputProcessor,
//            bool longRunning,
//            Action onStart,
//            Action onEnd,
//            Action<Exception, string> onError,
//            CancellationToken token)
//        {
//            return new RemoteProcessWrapper(processServer, process, ProcessOptions, outputProcessor, onStart, onEnd, onError, token);
//        }
//    }


//    public class RemoteProcessTask<T> : ProcessTask<T>
//    {
//        private readonly IProcessServer processServer;
//        public ProcessOptions ProcessOptions { get; set; }

//        public RemoteProcessTask(ITaskManager taskManager,
//            IProcessEnvironment processEnvironment,
//            IProcessServer processServer,
//            string executable = null,
//            string arguments = null,
//            ProcessOptions options = default,
//            IOutputProcessor<T> outputProcessor = null)
//            : base(taskManager, processEnvironment, executable, arguments, outputProcessor)
//        {
//            this.processServer = processServer;
//            this.ProcessOptions = options;
//        }

//        protected override BaseProcessWrapper GetWrapper(string taskName,
//            Process process,
//            IOutputProcessor outputProcessor,
//            bool longRunning,
//            Action onStart,
//            Action onEnd,
//            Action<Exception, string> onError,
//            CancellationToken token)
//        {
//            return new RemoteProcessWrapper(processServer, process, ProcessOptions, outputProcessor, onStart, onEnd, onError, token);
//        }
//    }

//    public class RemoteProcessTask<T, ListT> : ProcessTaskWithListOutput<T>
//    {
//        private readonly IProcessServer processServer;
//        public ProcessOptions ProcessOptions { get; set; }

//        public RemoteProcessTask(ITaskManager taskManager,
//            IProcessEnvironment processEnvironment,
//            IProcessServer processServer,
//            string executable = null,
//            string arguments = null,
//            ProcessOptions options = default,
//            IOutputProcessor<T, List<T>> outputProcessor = null)
//            : base(taskManager, processEnvironment, executable, arguments, outputProcessor)
//        {
//            this.processServer = processServer;
//            this.ProcessOptions = options;
//        }

//        protected override BaseProcessWrapper GetWrapper(string taskName,
//            Process process,
//            IOutputProcessor<T, List<T>> outputProcessor,
//            bool longRunning,
//            Action onStart,
//            Action onEnd,
//            Action<Exception, string> onError,
//            CancellationToken token)
//        {
//            return new RemoteProcessWrapper(processServer, process, ProcessOptions, outputProcessor, onStart, onEnd, onError, token);
//        }
//    }
//}

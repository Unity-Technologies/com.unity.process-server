namespace Unity.Editor.ProcessServer
{
    using System;
    using Interfaces;
    using Internal.IO;
    using Unity.Editor.Tasks;

    class DotNetProcessTask : ProcessTask<string>
    {
        private readonly SPath executable;
        private readonly string arguments;

        public DotNetProcessTask(ITaskManager taskManager,
            IProcessManager processManager,
            string executable,
            string arguments,
            IProcessEnvironment processEnvironment = null,
            string workingDirectory = null)
            : base(taskManager, processEnvironment ?? processManager.DefaultProcessEnvironment, outputProcessor: new SimpleOutputProcessor())
        {
            if (ProcessEnvironment.Environment.IsWindows)
            {
                this.executable = executable.ToSPath();
                this.arguments = arguments;
            }
            else
            {
                this.arguments = executable + " " + arguments;
                this.executable = ProcessEnvironment.Environment.UnityApplicationContents.ToSPath()
                                                    .Combine("MonoBleedingEdge", "bin", "mono" + ProcessEnvironment.Environment.ExecutableExtension);
            }

            processManager.Configure(this, workingDirectory);
        }

        public override string ProcessName => executable;
        public override string ProcessArguments => arguments;
    }

    public class DotNetProcessTask<T> : ProcessTask<T>
    {
        private readonly Func<IProcessTask<T>, string, bool> isMatch;
        private readonly Func<IProcessTask<T>, string, T> processor;
        private readonly SPath executable;
        private readonly string arguments;

        public DotNetProcessTask(ITaskManager taskManager,
            IProcessManager processManager,
            string executable,
            string arguments,
            Func<IProcessTask<T>, string, bool> isMatch,
            Func<IProcessTask<T>, string, T> processor,
            IProcessEnvironment processEnvironment = null,
            string workingDirectory = null)
            : base(taskManager, processEnvironment ?? processManager.DefaultProcessEnvironment)
        {
            this.isMatch = isMatch;
            this.processor = processor;
            if (ProcessEnvironment.Environment.IsWindows)
            {
                this.executable = executable.ToSPath();
                this.arguments = arguments;
            }
            else
            {
                this.arguments = executable + " " + arguments;
                this.executable = ProcessEnvironment.Environment.UnityApplicationContents.ToSPath()
                                                    .Combine("MonoBleedingEdge", "bin", "mono" + ProcessEnvironment.Environment.ExecutableExtension);
            }

            processManager.Configure(this, workingDirectory);
        }

        public DotNetProcessTask(ITaskManager taskManager,
            IProcessManager processManager,
            string executable,
            string arguments,
            IOutputProcessor<T> outputProcessor,
            IProcessEnvironment processEnvironment = null,
            string workingDirectory = null)
            : base(taskManager, processEnvironment ?? processManager.DefaultProcessEnvironment, outputProcessor: outputProcessor)
        {
            if (ProcessEnvironment.Environment.IsWindows)
            {
                this.executable = executable.ToSPath();
                this.arguments = arguments;
            }
            else
            {
                this.arguments = executable + " " + arguments;
                this.executable = ProcessEnvironment.Environment.UnityApplicationContents.ToSPath()
                                                    .Combine("MonoBleedingEdge", "bin", "mono" + ProcessEnvironment.Environment.ExecutableExtension);
            }

            processManager.Configure(this, workingDirectory);
        }

        public DotNetProcessTask(ITaskManager taskManager,
            IProcessManager processManager,
            string executable,
            string arguments,
            IProcessEnvironment processEnvironment = null,
            string workingDirectory = null)
            : base(taskManager, processEnvironment ?? processManager.DefaultProcessEnvironment)
        {
            if (ProcessEnvironment.Environment.IsWindows)
            {
                this.executable = executable.ToSPath();
                this.arguments = arguments;
            }
            else
            {
                this.arguments = executable + " " + arguments;
                this.executable = ProcessEnvironment.Environment.UnityApplicationContents.ToSPath()
                                                    .Combine("MonoBleedingEdge", "bin", "mono" + ProcessEnvironment.Environment.ExecutableExtension);
            }

            processManager.Configure(this, workingDirectory);
        }

        protected override void ConfigureOutputProcessor()
        {
            if (OutputProcessor == null && processor != null)
            {
                OutputProcessor = new BaseOutputProcessor<T>((string line, out T result) => {
                    result = default(T);
                    if (!isMatch(this, line)) return false;
                    result = processor(this, line);
                    return true;
                });
            }

            base.ConfigureOutputProcessor();
        }

        public override string ProcessName => executable;
        public override string ProcessArguments => arguments;
    }
}

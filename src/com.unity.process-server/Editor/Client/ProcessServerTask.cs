namespace Unity.Editor.ProcessServer
{
    using System;
    using System.IO;
    using System.Text;
    using Tasks;
    using Extensions;

    public class ProcessManagerTask : DotNetProcessTask<int>
    {
        public ProcessManagerTask(ITaskManager taskManager,
            IProcessManager processManager,
            IEnvironment environment,
            IProcessServerConfiguration configuration)
            : base(taskManager, processManager, configuration.ExecutablePath, CreateArguments(environment))

        {
            Affinity = TaskAffinity.LongRunning;
            LongRunning = true;
        }

        private static string CreateArguments(IEnvironment environment)
        {
            var args = new StringBuilder();
            args.Append("-projectPath ");
            args.Append(environment.UnityProjectPath.Quote());
            args.Append(" -unityPath ");
            args.Append(environment.UnityApplicationContents.Quote());
            return args.ToString();
        }

        protected override void ConfigureOutputProcessor()
        {
            OutputProcessor = new BaseOutputProcessor<int>((string line, out int result) => {
                result = default;
                if (!(line?.StartsWith("Port:") ?? false)) return false;
                result = int.Parse(line.Substring(5));
                return true;
            });
            OutputProcessor.OnEntry += OnGotPort;

            base.ConfigureOutputProcessor();
        }

        private void OnGotPort(int port)
        {
            Detach();
            OutputProcessor.OnEntry -= OnGotPort;
        }
   }
}

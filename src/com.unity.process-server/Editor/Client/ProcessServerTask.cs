namespace Unity.Editor.ProcessServer
{
    using System.Text;
    using Tasks;
    using Unity.Editor.ProcessServer.Internal.IO;

    public class ProcessManagerTask : DotNetProcessTask<int>
    {
        public ProcessManagerTask(ITaskManager taskManager,
            IProcessManager processManager,
            IEnvironment environment,
            IProcessServerConfiguration configuration)
            : base(taskManager, processManager, configuration.ExecutablePath, CreateArguments(environment), outputProcessor: null)

        {
            Affinity = TaskAffinity.LongRunning;
            LongRunning = true;
        }

        private static string CreateArguments(IEnvironment environment)
        {
            var args = new StringBuilder();
            args.Append("-projectPath ");
            args.Append(environment.UnityProjectPath.ToSPath().InQuotes());
            args.Append(" -unityPath ");
            args.Append(environment.UnityApplicationContents.ToSPath().InQuotes());
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

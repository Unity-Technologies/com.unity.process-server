namespace Unity.Editor.ProcessServer
{
    using System;
    using System.IO;
    using Tasks;

    public class ProcessManagerTask : DotNetProcessTask<int>
    {
        private const string ProcessManagerExe = "processserver.exe";

        public ProcessManagerTask(ITaskManager taskManager,
            IProcessManager processManager,
            string baseLocation,
            string projectPath)
            : base(taskManager, processManager, Path.Combine(baseLocation, ProcessManagerExe), $"-projectPath {projectPath} -debug")

        {
            Affinity = TaskAffinity.LongRunning;
            LongRunning = true;
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

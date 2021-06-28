namespace Unity.ProcessServer.Server
{
    using Rpc;

    public class ServerConfiguration : Configuration
    {
        public bool Debug { get; set; }
        public string ProjectPath { get; set; }
        public string UnityVersion { get; set; }
        public string UnityApplicationPath { get; set; }
        public string UnityContentsPath { get; set; }
        public int Pid { get; set; }
        public string AccessToken { get; set; }
    }
}

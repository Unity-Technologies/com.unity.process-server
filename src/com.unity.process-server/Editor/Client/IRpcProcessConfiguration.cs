namespace Unity.ProcessServer
{
    public interface IRpcProcessConfiguration
    {
        /// <summary>
        /// Pass this to <see cref="ProcessServer.Get"/>
        /// so the client can read and write the port information when connecting, or to a <see cref="RpcServerTask"/>
        /// if you want to run your own rpc server process.
        /// </summary>
        int Port { get; set; }

        /// <summary>
        /// Access token required to invoke remote procedure calls.
        /// </summary>
        string AccessToken { get; set; }

        /// <summary>
        /// The path to the process server executable.
        /// </summary>
        string ExecutablePath { get; }

        /// <summary>
        /// The id of the remote process, set when the process is controlled by the process server. This id is constant
        /// even if the process restarts.
        /// </summary>
        string RemoteProcessId { get; set; }
    }
}

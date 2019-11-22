namespace Unity.Editor.ProcessServer.Server
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Interfaces;
    using Ipc;
    using Microsoft.Extensions.Hosting;

    public enum NotificationType
    {
        None,
        Shutdown
    }

    public class ProcessServer : IServer
    {
        private readonly IHostApplicationLifetime app;

        private TaskCompletionSource<bool> stopped = new TaskCompletionSource<bool>();

        private Dictionary<string, IRequestContext> clients = new Dictionary<string, IRequestContext>();

        public ProcessServer(IHostApplicationLifetime app)
        {
            this.app = app;
        }

        internal void ClientConnecting(IRequestContext context)
        {
            clients.Add(context.Id, context);
        }

        internal void ClientDisconnecting(IRequestContext context)
        {
            if (clients.ContainsKey(context.Id))
            {
                clients.Remove(context.Id);
            }
        }

        public Task Stop()
        {
            app.StopApplication();
            app.ApplicationStopping.Register(() => stopped.TrySetResult(true));
            return stopped.Task;
        }

        internal void NotifyClients(NotificationType type)
        {
            switch (type)
            {
                case NotificationType.Shutdown:
                    Parallel.ForEach(clients.Values, client => {
                        client.GetRemoteTarget<IServerNotifications>().ServerStopping().Wait(100);
                    });
                    break;
            }
        }
    }
}

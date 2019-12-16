namespace Unity.ProcessServer.Server
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Interfaces;
    using Rpc;
    using Microsoft.Extensions.Hosting;

    public enum NotificationType
    {
        None,
        Shutdown
    }

    public class ProcessServer
    {
        private readonly IHostApplicationLifetime app;
        private readonly TaskCompletionSource<bool> stopped = new TaskCompletionSource<bool>();
        private readonly Dictionary<string, IRequestContext> clients = new Dictionary<string, IRequestContext>();
        private bool stopping = false;

        public ProcessServer(IHostApplicationLifetime app)
        {
            this.app = app;
            app.ApplicationStopping.Register(() => stopped.TrySetResult(true));
        }

        public void ClientConnecting(IRequestContext context)
        {
            clients.Add(context.Id, context);
        }

        public void ClientDisconnecting(IRequestContext context)
        {
            if (clients.ContainsKey(context.Id))
            {
                clients.Remove(context.Id);
            }
        }

        public Task Stop()
        {
            if (stopping) return stopped.Task;
            stopping = true;
            app.StopApplication();
            return stopped.Task;
        }

        public void Shutdown()
        {
            NotifyClients(NotificationType.Shutdown);
        }

        public void NotifyClients(NotificationType type)
        {
            switch (type)
            {
                case NotificationType.Shutdown:
                    Parallel.ForEach(clients.Values, client => {
                        try
                        {
                            client.GetRemoteTarget<IServerNotifications>().ServerStopping().Wait(100);
                        }
                        // really don't care about clients being disconnected
                        catch {}
                    });
                    break;
            }
        }

        public class Implementation : IServer
        {
            private readonly ProcessServer owner;

            public Implementation(ProcessServer owner)
            {
                this.owner = owner;
            }
            public Task Stop()
            {
                return owner.Stop();
            }
        }
    }
}

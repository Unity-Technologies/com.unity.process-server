﻿namespace Unity.ProcessServer.Server
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Threading;
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
		private readonly int parentPID;
        private readonly string accessToken;
		private readonly Timer parentCheckTimer;
		private readonly TimeSpan checkInterval = TimeSpan.FromSeconds(30);

        public ProcessServer(IHostApplicationLifetime app, ServerConfiguration configuration)
        {
	        parentPID = configuration.Pid;
            accessToken = configuration.AccessToken;
	        this.app = app;

	        app.ApplicationStopping.Register(() => stopped.TrySetResult(true));

	        if (parentPID > 0)
	        {
		        parentCheckTimer = new Timer(ShutdownIfParentIsGone, null, checkInterval, checkInterval);
	        }
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

        public Task Stop(string accessToken)
        {
            if (accessToken != this.accessToken)
                throw new UnauthorizedAccessException();
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

        private void ShutdownIfParentIsGone(object _)
        {
	        var runningProcesses = Process.GetProcesses();
	        if (!runningProcesses.Any(x => x.Id == parentPID))
	        {
		        parentCheckTimer.Dispose();
		        app.StopApplication();
	        }
        }

        public class Implementation : IServer
        {
            private readonly ProcessServer owner;

            public Implementation(ProcessServer owner)
            {
                this.owner = owner;
            }
            public Task Stop(string accessToken)
            {
                return owner.Stop(accessToken);
            }
        }
    }
}

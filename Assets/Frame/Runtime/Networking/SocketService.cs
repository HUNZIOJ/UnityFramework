using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Frame.Core;

namespace Frame.Networking
{
    public sealed class SocketService : GameModuleBase, ISocketService
    {
        private readonly List<ISocketClient> clients = new List<ISocketClient>();

        public override int Priority
        {
            get { return -90; }
        }

        public IReadOnlyList<ISocketClient> Clients
        {
            get { return clients; }
        }

        public int ActiveConnectionCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < clients.Count; i++)
                {
                    if (clients[i] != null && clients[i].IsConnected)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<ISocketService>(this);
            Context.Services.Register(this);
        }

        public ISocketClient CreateClient(SocketClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            SocketClient client = new SocketClient(options);
            clients.Add(client);
            return client;
        }

        public ISocketClient CreateTcpClient(string host, int port, Action<SocketClientOptions> configure = null)
        {
            SocketClientOptions options = SocketClientOptions.Tcp(host, port);
            if (configure != null)
            {
                configure(options);
            }

            return CreateClient(options);
        }

        public ISocketClient CreateWebSocketClient(string url, Action<SocketClientOptions> configure = null)
        {
            SocketClientOptions options = SocketClientOptions.WebSocket(url);
            if (configure != null)
            {
                configure(options);
            }

            return CreateClient(options);
        }

        public bool RemoveClient(ISocketClient client, bool disconnect = true)
        {
            if (client == null)
            {
                return false;
            }

            bool removed = clients.Remove(client);
            if (!removed)
            {
                return false;
            }

            if (disconnect)
            {
                client.Dispose();
            }

            return true;
        }

        public async UniTask DisconnectAllAsync()
        {
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i] != null)
                {
                    await clients[i].DisconnectAsync(SocketDisconnectReason.Shutdown);
                }
            }
        }

        protected override void OnShutdown()
        {
            for (int i = 0; i < clients.Count; i++)
            {
                if (clients[i] != null)
                {
                    clients[i].Dispose();
                }
            }

            clients.Clear();
        }
    }
}

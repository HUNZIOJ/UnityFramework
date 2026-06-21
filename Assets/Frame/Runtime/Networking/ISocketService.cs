using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Frame.Networking
{
    public interface ISocketService
    {
        IReadOnlyList<ISocketClient> Clients { get; }

        int ActiveConnectionCount { get; }

        ISocketClient CreateClient(SocketClientOptions options);

        ISocketClient CreateTcpClient(string host, int port, Action<SocketClientOptions> configure = null);

        ISocketClient CreateWebSocketClient(string url, Action<SocketClientOptions> configure = null);

        bool RemoveClient(ISocketClient client, bool disconnect = true);

        UniTask DisconnectAllAsync();
    }
}

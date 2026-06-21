using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Frame.Networking
{
    public interface ISocketClient : IDisposable
    {
        event Action<ISocketClient, SocketClientState, SocketClientState> StateChanged;

        event Action<ISocketClient> Connected;

        event Action<ISocketClient, SocketDisconnectInfo> Disconnected;

        event Action<ISocketClient, int> Reconnecting;

        event Action<ISocketClient, SocketMessage> MessageReceived;

        event Action<ISocketClient, Exception> Error;

        string Id { get; }

        SocketClientOptions Options { get; }

        SocketClientState State { get; }

        bool IsConnected { get; }

        SocketClientMetrics Metrics { get; }

        UniTask<bool> ConnectAsync(CancellationToken cancellationToken = default(CancellationToken));

        UniTask DisconnectAsync(SocketDisconnectReason reason = SocketDisconnectReason.Local, string error = null);

        bool Send(SocketMessage message);

        bool Send(byte[] data);

        bool SendText(string text);

        void ClearMetrics();
    }
}

using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Frame.Networking
{
    internal interface IWebSocketConnection : IDisposable
    {
        bool IsOpen { get; }

        UniTask ConnectAsync(CancellationToken cancellationToken);

        UniTask<SocketMessage> ReceiveAsync(CancellationToken cancellationToken);

        UniTask SendAsync(SocketMessage message, CancellationToken cancellationToken);

        UniTask CloseAsync(SocketDisconnectReason reason);

        void Abort();
    }

    internal static class WebSocketConnectionFactory
    {
        public static IWebSocketConnection Create(SocketClientOptions options)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new NativeWebSocketConnection(options);
#else
            return new SystemNetWebSocketConnection(options);
#endif
        }
    }
}

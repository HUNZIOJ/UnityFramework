#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Frame.Networking
{
    internal sealed class SystemNetWebSocketConnection : IWebSocketConnection
    {
        private readonly SocketClientOptions options;
        private ClientWebSocket socket;
        private bool disposed;

        public SystemNetWebSocketConnection(SocketClientOptions options)
        {
            this.options = options ?? throw new ArgumentNullException("options");
        }

        public bool IsOpen
        {
            get { return socket != null && socket.State == WebSocketState.Open; }
        }

        public async UniTask ConnectAsync(CancellationToken cancellationToken)
        {
            ClientWebSocket client = new ClientWebSocket();
            try
            {
                if (options.WebSocketHeaders != null)
                {
                    foreach (var header in options.WebSocketHeaders)
                    {
                        if (!string.IsNullOrWhiteSpace(header.Key))
                        {
                            client.Options.SetRequestHeader(header.Key, header.Value);
                        }
                    }
                }

                if (options.WebSocketSubProtocols != null)
                {
                    for (int i = 0; i < options.WebSocketSubProtocols.Count; i++)
                    {
                        string protocol = options.WebSocketSubProtocols[i];
                        if (!string.IsNullOrWhiteSpace(protocol))
                        {
                            client.Options.AddSubProtocol(protocol);
                        }
                    }
                }

                client.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(options.ConnectTimeoutMilliseconds);
                    await client.ConnectAsync(new Uri(options.Url), timeout.Token);
                }

                socket = client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public async UniTask<SocketMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (socket == null)
            {
                throw new IOException("WebSocket is not available.");
            }

            byte[] readBuffer = new byte[options.ReceiveBufferSize];
            using (MemoryStream messageStream = new MemoryStream())
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(readBuffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    if (result.Count > 0)
                    {
                        messageStream.Write(readBuffer, 0, result.Count);
                    }

                    if (messageStream.Length > options.MaxMessageSizeBytes)
                    {
                        throw new InvalidOperationException("WebSocket message exceeds max size: " + messageStream.Length);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    byte[] payload = messageStream.ToArray();
                    SocketMessageKind kind = result.MessageType == WebSocketMessageType.Text ? SocketMessageKind.Text : SocketMessageKind.Binary;
                    return SocketMessage.WrapUnsafe(payload, kind);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        public async UniTask SendAsync(SocketMessage message, CancellationToken cancellationToken)
        {
            if (socket == null || socket.State != WebSocketState.Open)
            {
                throw new IOException("WebSocket is not open.");
            }

            WebSocketMessageType type = message.Kind == SocketMessageKind.Text ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
            await socket.SendAsync(new ArraySegment<byte>(message.Data), type, true, cancellationToken);
        }

        public async UniTask CloseAsync(SocketDisconnectReason reason)
        {
            if (socket == null)
            {
                return;
            }

            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                using (CancellationTokenSource closeTimeout = new CancellationTokenSource(1000))
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason.ToString(), closeTimeout.Token);
                }
            }
        }

        public void Abort()
        {
            try
            {
                socket?.Abort();
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                socket?.Dispose();
            }
            catch
            {
            }

            socket = null;
        }
    }
}
#endif

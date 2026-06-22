#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NativeWebSocket;

namespace Frame.Networking
{
    internal sealed class NativeWebSocketConnection : IWebSocketConnection
    {
        private readonly SocketClientOptions options;
        private readonly ConcurrentQueue<SocketMessage> receiveQueue = new ConcurrentQueue<SocketMessage>();
        private readonly SemaphoreSlim receiveSignal = new SemaphoreSlim(0);
        private WebSocket socket;
        private Exception error;
        private bool opened;
        private bool closed;
        private bool disposed;

        public NativeWebSocketConnection(SocketClientOptions options)
        {
            this.options = options ?? throw new ArgumentNullException("options");
        }

        public bool IsOpen
        {
            get { return socket != null && socket.State == WebSocketState.Open; }
        }

        public async UniTask ConnectAsync(CancellationToken cancellationToken)
        {
            socket = CreateSocket();
            socket.OnOpen += HandleOpen;
            socket.OnMessage += HandleMessage;
            socket.OnError += HandleError;
            socket.OnClose += HandleClose;

            try
            {
                await socket.Connect();

                DateTime deadline = DateTime.UtcNow.AddMilliseconds(options.ConnectTimeoutMilliseconds);
                while (!opened)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (error != null)
                    {
                        throw error;
                    }

                    if (closed)
                    {
                        throw new IOException("NativeWebSocket closed before opening.");
                    }

                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new TimeoutException("NativeWebSocket connect timed out: " + options.Url);
                    }

                    await UniTask.Delay(10, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, cancellationToken);
                }
            }
            catch
            {
                Abort();
                throw;
            }
        }

        public async UniTask<SocketMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                SocketMessage message;
                if (receiveQueue.TryDequeue(out message))
                {
                    return message;
                }

                if (error != null)
                {
                    throw error;
                }

                if (closed)
                {
                    return null;
                }

                await receiveSignal.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        public async UniTask SendAsync(SocketMessage message, CancellationToken cancellationToken)
        {
            if (socket == null || socket.State != WebSocketState.Open)
            {
                throw new IOException("NativeWebSocket is not open.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (message.Kind == SocketMessageKind.Text)
            {
                await socket.SendText(message.Text);
            }
            else
            {
                await socket.Send(message.Data);
            }
        }

        public async UniTask CloseAsync(SocketDisconnectReason reason)
        {
            if (socket == null || socket.State == WebSocketState.Closed)
            {
                return;
            }

            await socket.Close(WebSocketCloseCode.Normal, reason.ToString());
        }

        public void Abort()
        {
            try
            {
                if (socket != null && socket.State != WebSocketState.Closed)
                {
                    _ = socket.Close(WebSocketCloseCode.Away, "Abort");
                }
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
            if (socket != null)
            {
                Abort();
                socket.OnOpen -= HandleOpen;
                socket.OnMessage -= HandleMessage;
                socket.OnError -= HandleError;
                socket.OnClose -= HandleClose;
            }

            receiveSignal.Dispose();
            socket = null;
        }

        private WebSocket CreateSocket()
        {
            Dictionary<string, string> headers = options.WebSocketHeaders;
            List<string> subProtocols = options.WebSocketSubProtocols;
            if (subProtocols != null && subProtocols.Count > 0)
            {
                return new WebSocket(options.Url, subProtocols, headers);
            }

            return new WebSocket(options.Url, headers);
        }

        private void HandleOpen()
        {
            if (disposed)
            {
                return;
            }

            opened = true;
        }

        private void HandleMessage(byte[] data)
        {
            if (disposed)
            {
                return;
            }

            if (data != null && data.Length > options.MaxMessageSizeBytes)
            {
                error = new InvalidOperationException("NativeWebSocket message exceeds max size: " + data.Length);
                ReleaseReceiveSignal();
                return;
            }

            SocketMessageKind kind = options.WebGlWebSocketReceiveKind;
            receiveQueue.Enqueue(SocketMessage.WrapUnsafe(data ?? new byte[0], kind));
            ReleaseReceiveSignal();
        }

        private void HandleError(string errorMessage)
        {
            if (disposed)
            {
                return;
            }

            error = new IOException(string.IsNullOrWhiteSpace(errorMessage) ? "NativeWebSocket error." : errorMessage);
            ReleaseReceiveSignal();
        }

        private void HandleClose(WebSocketCloseCode closeCode)
        {
            if (disposed)
            {
                return;
            }

            closed = true;
            ReleaseReceiveSignal();
        }

        private void ReleaseReceiveSignal()
        {
            try
            {
                receiveSignal.Release();
            }
            catch
            {
            }
        }
    }
}
#endif

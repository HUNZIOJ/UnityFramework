using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Frame.Core;

namespace Frame.Networking
{
    public sealed class SocketClient : ISocketClient
    {
        private readonly object stateLock = new object();
        private readonly object transportLock = new object();
        private readonly SemaphoreSlim connectGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim sendSignal = new SemaphoreSlim(0);
        private readonly ConcurrentQueue<SocketMessage> sendQueue = new ConcurrentQueue<SocketMessage>();
        private readonly SocketClientMetrics metrics = new SocketClientMetrics();
        private readonly SocketClientOptions options;
        private readonly ISocketMessageCodec codec;

        private CancellationTokenSource connectionCancellation;
        private CancellationTokenSource reconnectCancellation;
        private TcpClient tcpClient;
        private Stream tcpStream;
        private IWebSocketConnection webSocket;
        private SocketReceiveBuffer receiveBuffer;
        private SocketClientState state = SocketClientState.Disconnected;
        private bool disposed;
        private bool localDisconnectRequested;
        private int handlingFailure;
        private long lastReceiveTicks;
        private long lastSendTicks;

        public SocketClient(SocketClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            this.options = options.Clone();
            this.options.Validate();
            codec = this.options.ResolveCodec();
            Id = string.IsNullOrWhiteSpace(this.options.Id) ? Guid.NewGuid().ToString("N") : this.options.Id;
        }

        public event Action<ISocketClient, SocketClientState, SocketClientState> StateChanged;

        public event Action<ISocketClient> Connected;

        public event Action<ISocketClient, SocketDisconnectInfo> Disconnected;

        public event Action<ISocketClient, int> Reconnecting;

        public event Action<ISocketClient, SocketMessage> MessageReceived;

        public event Action<ISocketClient, Exception> Error;

        public string Id { get; private set; }

        public SocketClientOptions Options
        {
            get { return options; }
        }

        public SocketClientState State
        {
            get
            {
                lock (stateLock)
                {
                    return state;
                }
            }
        }

        public bool IsConnected
        {
            get { return State == SocketClientState.Connected; }
        }

        public SocketClientMetrics Metrics
        {
            get { return metrics; }
        }

        public async UniTask<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            await connectGate.WaitAsync(cancellationToken);
            try
            {
                if (disposed)
                {
                    return false;
                }

                SocketClientState current = State;
                if (current == SocketClientState.Connected || current == SocketClientState.Connecting || current == SocketClientState.Reconnecting)
                {
                    return current == SocketClientState.Connected;
                }

                localDisconnectRequested = false;
                Interlocked.Exchange(ref handlingFailure, 0);
                SetState(SocketClientState.Connecting);
                ResetConnectionCancellation();

                CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(connectionCancellation.Token, cancellationToken);
                try
                {
                    await ConnectTransportAsync(linked.Token);
                    MarkActivity();
                    SetState(SocketClientState.Connected);
                    RaiseConnected();
                    StartBackgroundLoops(connectionCancellation.Token);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    await CloseTransportAsync(SocketDisconnectReason.Canceled);
                    SetState(SocketClientState.Disconnected);
                    RaiseDisconnected(new SocketDisconnectInfo(SocketDisconnectReason.Canceled, "Connection canceled."));
                    return false;
                }
                catch (Exception exception)
                {
                    await CloseTransportAsync(SocketDisconnectReason.Error);
                    SetState(SocketClientState.Disconnected);
                    RaiseError(exception);
                    RaiseDisconnected(new SocketDisconnectInfo(SocketDisconnectReason.Error, exception.Message));
                    return false;
                }
                finally
                {
                    linked.Dispose();
                }
            }
            finally
            {
                connectGate.Release();
            }
        }

        public async UniTask DisconnectAsync(SocketDisconnectReason reason = SocketDisconnectReason.Local, string error = null)
        {
            localDisconnectRequested = true;
            Interlocked.Exchange(ref handlingFailure, 0);
            CancelConnection();
            CancelReconnect();
            SetState(SocketClientState.Disconnecting);
            if (options.ClearSendQueueOnDisconnect)
            {
                ClearSendQueue();
            }

            await CloseTransportAsync(reason);
            SetState(SocketClientState.Disconnected);
            RaiseDisconnected(new SocketDisconnectInfo(reason, error));
        }

        public bool Send(SocketMessage message)
        {
            if (message == null || disposed || State != SocketClientState.Connected)
            {
                return false;
            }

            if (options.SendQueueLimit > 0 && sendQueue.Count >= options.SendQueueLimit)
            {
                lock (metrics)
                {
                    metrics.DroppedMessages++;
                }

                return false;
            }

            sendQueue.Enqueue(message.Clone());
            sendSignal.Release();
            return true;
        }

        public bool Send(byte[] data)
        {
            return Send(SocketMessage.Binary(data));
        }

        public bool SendText(string text)
        {
            return Send(SocketMessage.TextMessage(text));
        }

        public void ClearMetrics()
        {
            lock (metrics)
            {
                metrics.Clear();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            localDisconnectRequested = true;
            CancelConnection();
            CancelReconnect();
            CloseTransportSync();
            ClearSendQueue();
            StateChanged = null;
            Connected = null;
            Disconnected = null;
            Reconnecting = null;
            MessageReceived = null;
            Error = null;
            connectionCancellation?.Dispose();
            connectionCancellation = null;
            reconnectCancellation?.Dispose();
            reconnectCancellation = null;
            SetState(SocketClientState.Disconnected);
        }

        private async UniTask ConnectTransportAsync(CancellationToken cancellationToken)
        {
            if (options.Transport == SocketTransportType.WebSocket)
            {
                await ConnectWebSocketAsync(cancellationToken);
                return;
            }

            await ConnectTcpAsync(cancellationToken);
        }

        private async UniTask ConnectTcpAsync(CancellationToken cancellationToken)
        {
            TcpClient client = new TcpClient
            {
                NoDelay = options.NoDelay
            };
            try
            {
                Task connectTask = client.ConnectAsync(options.Host, options.Port);
                Task timeoutTask = Task.Delay(options.ConnectTimeoutMilliseconds, cancellationToken);
                Task completed = await Task.WhenAny(connectTask, timeoutTask);
                if (!ReferenceEquals(completed, connectTask))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException("TCP socket connect timed out: " + options.Host + ":" + options.Port);
                }

                await connectTask;
                Stream stream = client.GetStream();
                if (options.UseTls)
                {
                    SslStream sslStream = new SslStream(stream, false, options.CertificateValidationCallback);
                    string tlsHost = string.IsNullOrWhiteSpace(options.TlsHostName) ? options.Host : options.TlsHostName;
                    Task handshakeTask = sslStream.AuthenticateAsClientAsync(tlsHost);
                    Task handshakeTimeoutTask = Task.Delay(options.ConnectTimeoutMilliseconds, cancellationToken);
                    Task handshakeCompleted = await Task.WhenAny(handshakeTask, handshakeTimeoutTask);
                    if (!ReferenceEquals(handshakeCompleted, handshakeTask))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("TCP TLS handshake timed out: " + tlsHost);
                    }

                    await handshakeTask;
                    stream = sslStream;
                }

                lock (transportLock)
                {
                    tcpClient = client;
                    tcpStream = stream;
                    webSocket = null;
                    receiveBuffer = new SocketReceiveBuffer(options.ReceiveBufferSize);
                }
            }
            catch
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }

                throw;
            }
        }

        private async UniTask ConnectWebSocketAsync(CancellationToken cancellationToken)
        {
            IWebSocketConnection socket = WebSocketConnectionFactory.Create(options);
            try
            {
                await socket.ConnectAsync(cancellationToken);

                lock (transportLock)
                {
                    webSocket = socket;
                    tcpClient = null;
                    tcpStream = null;
                    receiveBuffer = null;
                }
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private void StartBackgroundLoops(CancellationToken cancellationToken)
        {
            if (sendQueue.Count > 0)
            {
                sendSignal.Release();
            }

            SendLoopAsync(cancellationToken).Forget();
            ReceiveLoopAsync(cancellationToken).Forget();
            if (options.HeartbeatIntervalSeconds > 0f && options.HeartbeatPayload != null)
            {
                HeartbeatLoopAsync(cancellationToken).Forget();
            }
        }

        private async UniTaskVoid SendLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await sendSignal.WaitAsync(cancellationToken);
                    while (!cancellationToken.IsCancellationRequested && sendQueue.TryDequeue(out SocketMessage message))
                    {
                        if (State != SocketClientState.Connected)
                        {
                            return;
                        }

                        await SendTransportAsync(message, cancellationToken);
                        MarkSent(message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                RaiseError(exception);
                HandleConnectionFailure(SocketDisconnectReason.Error, exception.Message);
            }
        }

        private async UniTaskVoid ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            SocketDisconnectReason reason = SocketDisconnectReason.Remote;
            string error = null;
            try
            {
                if (options.Transport == SocketTransportType.WebSocket)
                {
                    await ReceiveWebSocketLoopAsync(cancellationToken);
                }
                else
                {
                    await ReceiveTcpLoopAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                reason = SocketDisconnectReason.Canceled;
            }
            catch (Exception exception)
            {
                reason = SocketDisconnectReason.Error;
                error = exception.Message;
                RaiseError(exception);
            }

            if (!localDisconnectRequested && !disposed && reason != SocketDisconnectReason.Canceled)
            {
                HandleConnectionFailure(reason, error);
            }
        }

        private async UniTask ReceiveTcpLoopAsync(CancellationToken cancellationToken)
        {
            byte[] readBuffer = new byte[options.ReceiveBufferSize];
            while (!cancellationToken.IsCancellationRequested)
            {
                Stream stream;
                SocketReceiveBuffer socketBuffer;
                lock (transportLock)
                {
                    stream = tcpStream;
                    socketBuffer = receiveBuffer;
                }

                if (stream == null || socketBuffer == null)
                {
                    throw new IOException("TCP socket stream is not available.");
                }

                int read = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
                if (read <= 0)
                {
                    return;
                }

                MarkReceivedActivity();
                socketBuffer.Append(readBuffer, read);
                SocketMessage message;
                while (codec.TryDecode(socketBuffer, out message))
                {
                    MarkReceivedMessage(message);
                    RaiseMessage(message);
                }
            }
        }

        private async UniTask ReceiveWebSocketLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IWebSocketConnection socket;
                lock (transportLock)
                {
                    socket = webSocket;
                }

                if (socket == null)
                {
                    throw new IOException("WebSocket is not available.");
                }

                SocketMessage message = await socket.ReceiveAsync(cancellationToken);
                if (message == null)
                {
                    return;
                }

                MarkReceivedActivity();
                MarkReceivedMessage(message);
                RaiseMessage(message);
            }
        }

        private async UniTask SendTransportAsync(SocketMessage message, CancellationToken cancellationToken)
        {
            if (options.Transport == SocketTransportType.WebSocket)
            {
                IWebSocketConnection socket;
                lock (transportLock)
                {
                    socket = webSocket;
                }

                if (socket == null || !socket.IsOpen)
                {
                    throw new IOException("WebSocket is not open.");
                }

                await socket.SendAsync(message, cancellationToken);
                MarkSentActivity();
                return;
            }

            Stream stream;
            lock (transportLock)
            {
                stream = tcpStream;
            }

            if (stream == null)
            {
                throw new IOException("TCP socket stream is not available.");
            }

            byte[] frame = codec.Encode(message);
            await stream.WriteAsync(frame, 0, frame.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            MarkSentActivity();
        }

        private async UniTaskVoid HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            SocketMessage heartbeat = new SocketMessage(options.HeartbeatPayload, options.HeartbeatKind);
            int intervalMs = Math.Max(1, (int)Math.Ceiling(options.HeartbeatIntervalSeconds * 1000f));
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(intervalMs, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, cancellationToken);
                    if (State != SocketClientState.Connected)
                    {
                        continue;
                    }

                    if (options.HeartbeatTimeoutSeconds > 0f)
                    {
                        TimeSpan idle = DateTime.UtcNow - new DateTime(Interlocked.Read(ref lastReceiveTicks), DateTimeKind.Utc);
                        if (idle.TotalSeconds > options.HeartbeatTimeoutSeconds)
                        {
                            HandleConnectionFailure(SocketDisconnectReason.Timeout, "Socket heartbeat timed out.");
                            return;
                        }
                    }

                    Send(heartbeat);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    RaiseError(exception);
                }
            }
        }

        private void HandleConnectionFailure(SocketDisconnectReason reason, string error)
        {
            if (Interlocked.CompareExchange(ref handlingFailure, 1, 0) != 0)
            {
                return;
            }

            HandleConnectionFailureAsync(reason, error).Forget();
        }

        private async UniTaskVoid HandleConnectionFailureAsync(SocketDisconnectReason reason, string error)
        {
            CancelConnection();
            await CloseTransportAsync(reason);
            if (options.ClearSendQueueOnDisconnect)
            {
                ClearSendQueue();
            }

            SetState(SocketClientState.Disconnected);
            RaiseDisconnected(new SocketDisconnectInfo(reason, error));

            if (disposed || localDisconnectRequested || !options.AutoReconnect)
            {
                Interlocked.Exchange(ref handlingFailure, 0);
                return;
            }

            await ReconnectLoopAsync();
        }

        private async UniTask ReconnectLoopAsync()
        {
            ResetReconnectCancellation();
            CancellationTokenSource reconnectSource = reconnectCancellation;
            CancellationToken reconnectToken = reconnectSource.Token;
            SetState(SocketClientState.Reconnecting);
            int attempt = 0;
            float delay = options.ReconnectInitialDelaySeconds;
            try
            {
                while (!disposed && !localDisconnectRequested && !reconnectToken.IsCancellationRequested)
                {
                    if (options.MaxReconnectAttempts >= 0 && attempt >= options.MaxReconnectAttempts)
                    {
                        SetState(SocketClientState.Disconnected);
                        RaiseDisconnected(new SocketDisconnectInfo(SocketDisconnectReason.ReconnectFailed, "Socket reconnect attempts exhausted."));
                        Interlocked.Exchange(ref handlingFailure, 0);
                        return;
                    }

                    attempt++;
                    lock (metrics)
                    {
                        metrics.ReconnectAttempts++;
                    }

                    RaiseReconnecting(attempt);
                    int delayMs = Math.Max(1, (int)Math.Ceiling(delay * 1000f));
                    try
                    {
                        await UniTask.Delay(delayMs, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, reconnectToken);
                    }
                    catch (OperationCanceledException)
                    {
                        SetState(SocketClientState.Disconnected);
                        Interlocked.Exchange(ref handlingFailure, 0);
                        return;
                    }

                    if (disposed || localDisconnectRequested || reconnectToken.IsCancellationRequested)
                    {
                        SetState(SocketClientState.Disconnected);
                        Interlocked.Exchange(ref handlingFailure, 0);
                        return;
                    }

                    try
                    {
                        ResetConnectionCancellation();
                        await ConnectTransportAsync(connectionCancellation.Token);
                        MarkActivity();
                        SetState(SocketClientState.Connected);
                        RaiseConnected();
                        Interlocked.Exchange(ref handlingFailure, 0);
                        StartBackgroundLoops(connectionCancellation.Token);
                        return;
                    }
                    catch (Exception exception)
                    {
                        RaiseError(exception);
                        await CloseTransportAsync(SocketDisconnectReason.Error);
                        delay = Math.Min(options.ReconnectMaxDelaySeconds, delay * 2f);
                    }
                }

                SetState(SocketClientState.Disconnected);
                Interlocked.Exchange(ref handlingFailure, 0);
            }
            finally
            {
                ClearReconnectCancellation(reconnectSource);
            }
        }

        private async UniTask CloseTransportAsync(SocketDisconnectReason reason)
        {
            TcpClient client;
            Stream stream;
            IWebSocketConnection socket;
            lock (transportLock)
            {
                client = tcpClient;
                stream = tcpStream;
                socket = webSocket;
                tcpClient = null;
                tcpStream = null;
                webSocket = null;
                receiveBuffer = null;
            }

            if (socket != null)
            {
                try
                {
                    await socket.CloseAsync(reason);
                }
                catch
                {
                }
                finally
                {
                    socket.Dispose();
                }
            }

            if (stream != null)
            {
                try
                {
                    stream.Dispose();
                }
                catch
                {
                }
            }

            if (client != null)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }
        }

        private void CloseTransportSync()
        {
            TcpClient client;
            Stream stream;
            IWebSocketConnection socket;
            lock (transportLock)
            {
                client = tcpClient;
                stream = tcpStream;
                socket = webSocket;
                tcpClient = null;
                tcpStream = null;
                webSocket = null;
                receiveBuffer = null;
            }

            try { stream?.Dispose(); } catch { }
            try { client?.Close(); } catch { }
            try { socket?.Abort(); socket?.Dispose(); } catch { }
        }

        private void ResetConnectionCancellation()
        {
            CancellationTokenSource old = connectionCancellation;
            connectionCancellation = new CancellationTokenSource();
            if (old != null)
            {
                try
                {
                    old.Cancel();
                }
                catch
                {
                }

                old.Dispose();
            }
        }

        private void ResetReconnectCancellation()
        {
            CancellationTokenSource old = reconnectCancellation;
            reconnectCancellation = new CancellationTokenSource();
            if (old != null)
            {
                try
                {
                    old.Cancel();
                }
                catch
                {
                }

                old.Dispose();
            }
        }

        private void CancelReconnect()
        {
            CancellationTokenSource cts = reconnectCancellation;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        private void ClearReconnectCancellation(CancellationTokenSource source)
        {
            if (source == null || !ReferenceEquals(reconnectCancellation, source))
            {
                return;
            }

            reconnectCancellation = null;
            source.Dispose();
        }

        private void CancelConnection()
        {
            CancellationTokenSource cts = connectionCancellation;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        private void ClearSendQueue()
        {
            SocketMessage ignored;
            while (sendQueue.TryDequeue(out ignored))
            {
            }
        }

        private void SetState(SocketClientState next)
        {
            SocketClientState previous;
            lock (stateLock)
            {
                if (state == next)
                {
                    return;
                }

                previous = state;
                state = next;
            }

            Action<ISocketClient, SocketClientState, SocketClientState> handler = StateChanged;
            if (handler != null)
            {
                Dispatch(() => handler(this, previous, next));
            }
        }

        private void RaiseConnected()
        {
            Action<ISocketClient> handler = Connected;
            if (handler != null)
            {
                Dispatch(() => handler(this));
            }
        }

        private void RaiseDisconnected(SocketDisconnectInfo info)
        {
            Action<ISocketClient, SocketDisconnectInfo> handler = Disconnected;
            if (handler != null)
            {
                Dispatch(() => handler(this, info));
            }
        }

        private void RaiseReconnecting(int attempt)
        {
            Action<ISocketClient, int> handler = Reconnecting;
            if (handler != null)
            {
                Dispatch(() => handler(this, attempt));
            }
        }

        private void RaiseMessage(SocketMessage message)
        {
            Action<ISocketClient, SocketMessage> handler = MessageReceived;
            if (handler != null)
            {
                Dispatch(() => handler(this, message));
            }
        }

        private void RaiseError(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            Action<ISocketClient, Exception> handler = Error;
            if (handler != null)
            {
                Dispatch(() => handler(this, exception));
            }
        }

        private static void Dispatch(Action action)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                if (PlayerLoopHelper.IsMainThread)
                {
                    InvokeSafely(action);
                }
                else
                {
                    PlayerLoopHelper.AddContinuation(PlayerLoopTiming.Update, () => InvokeSafely(action));
                }
            }
            catch
            {
                InvokeSafely(action);
            }
        }

        private static void InvokeSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private void MarkActivity()
        {
            long now = DateTime.UtcNow.Ticks;
            Interlocked.Exchange(ref lastReceiveTicks, now);
            Interlocked.Exchange(ref lastSendTicks, now);
        }

        private void MarkReceivedActivity()
        {
            Interlocked.Exchange(ref lastReceiveTicks, DateTime.UtcNow.Ticks);
        }

        private void MarkSentActivity()
        {
            Interlocked.Exchange(ref lastSendTicks, DateTime.UtcNow.Ticks);
        }

        private void MarkSent(SocketMessage message)
        {
            lock (metrics)
            {
                metrics.SentMessages++;
                metrics.SentBytes += message == null ? 0 : message.Count;
            }
        }

        private void MarkReceivedMessage(SocketMessage message)
        {
            lock (metrics)
            {
                metrics.ReceivedMessages++;
                metrics.ReceivedBytes += message == null ? 0 : message.Count;
            }
        }
    }
}

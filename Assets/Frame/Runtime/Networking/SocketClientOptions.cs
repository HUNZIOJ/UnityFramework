using System;
using System.Collections.Generic;
using System.Net.Security;

namespace Frame.Networking
{
    public sealed class SocketClientOptions
    {
        public string Id;
        public SocketTransportType Transport = SocketTransportType.Tcp;
        public string Host;
        public int Port;
        public string Url;
        public bool UseTls;
        public string TlsHostName;
        public RemoteCertificateValidationCallback CertificateValidationCallback;
        public bool NoDelay = true;
        public int ConnectTimeoutMilliseconds = 10000;
        public int ReceiveBufferSize = 8192;
        public int MaxMessageSizeBytes = 1024 * 1024;
        public int SendQueueLimit = 1024;
        public bool ClearSendQueueOnDisconnect = true;
        public bool AutoReconnect = true;
        public int MaxReconnectAttempts = -1;
        public float ReconnectInitialDelaySeconds = 1f;
        public float ReconnectMaxDelaySeconds = 30f;
        public float HeartbeatIntervalSeconds = 0f;
        public float HeartbeatTimeoutSeconds = 0f;
        public byte[] HeartbeatPayload;
        public SocketMessageKind HeartbeatKind = SocketMessageKind.Binary;
        public ISocketMessageCodec Codec;
        public Dictionary<string, string> WebSocketHeaders;
        public List<string> WebSocketSubProtocols;

        public static SocketClientOptions Tcp(string host, int port)
        {
            return new SocketClientOptions
            {
                Transport = SocketTransportType.Tcp,
                Host = host,
                Port = port
            };
        }

        public static SocketClientOptions WebSocket(string url)
        {
            return new SocketClientOptions
            {
                Transport = SocketTransportType.WebSocket,
                Url = url
            };
        }

        public SocketClientOptions Clone()
        {
            SocketClientOptions clone = (SocketClientOptions)MemberwiseClone();
            clone.HeartbeatPayload = HeartbeatPayload == null ? null : Copy(HeartbeatPayload);
            clone.WebSocketHeaders = WebSocketHeaders == null
                ? null
                : new Dictionary<string, string>(WebSocketHeaders, StringComparer.OrdinalIgnoreCase);
            clone.WebSocketSubProtocols = WebSocketSubProtocols == null
                ? null
                : new List<string>(WebSocketSubProtocols);
            return clone;
        }

        internal ISocketMessageCodec ResolveCodec()
        {
            return Codec ?? new LengthPrefixedSocketCodec(MaxMessageSizeBytes);
        }

        internal void Validate()
        {
            ConnectTimeoutMilliseconds = Math.Max(1, ConnectTimeoutMilliseconds);
            ReceiveBufferSize = Math.Max(256, ReceiveBufferSize);
            MaxMessageSizeBytes = Math.Max(1, MaxMessageSizeBytes);
            ReconnectInitialDelaySeconds = Math.Max(0.01f, ReconnectInitialDelaySeconds);
            ReconnectMaxDelaySeconds = Math.Max(ReconnectInitialDelaySeconds, ReconnectMaxDelaySeconds);

            if (Transport == SocketTransportType.Tcp)
            {
                if (string.IsNullOrWhiteSpace(Host))
                {
                    throw new ArgumentException("TCP socket host is required.", "Host");
                }

                if (Port <= 0 || Port > 65535)
                {
                    throw new ArgumentOutOfRangeException("Port", "TCP socket port must be between 1 and 65535.");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(Url))
            {
                throw new ArgumentException("WebSocket url is required.", "Url");
            }

            Uri uri;
            if (!Uri.TryCreate(Url, UriKind.Absolute, out uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            {
                throw new ArgumentException("WebSocket url must be an absolute ws:// or wss:// uri.", "Url");
            }
        }

        private static byte[] Copy(byte[] data)
        {
            byte[] copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            return copy;
        }
    }
}

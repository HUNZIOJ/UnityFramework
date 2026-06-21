namespace Frame.Networking
{
    public sealed class SocketClientMetrics
    {
        public long SentMessages { get; internal set; }

        public long ReceivedMessages { get; internal set; }

        public long SentBytes { get; internal set; }

        public long ReceivedBytes { get; internal set; }

        public int ReconnectAttempts { get; internal set; }

        public int DroppedMessages { get; internal set; }

        public void Clear()
        {
            SentMessages = 0;
            ReceivedMessages = 0;
            SentBytes = 0;
            ReceivedBytes = 0;
            ReconnectAttempts = 0;
            DroppedMessages = 0;
        }
    }
}

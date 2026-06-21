namespace Frame.Networking
{
    public sealed class SocketDisconnectInfo
    {
        public SocketDisconnectInfo(SocketDisconnectReason reason, string error = null)
        {
            Reason = reason;
            Error = error;
        }

        public SocketDisconnectReason Reason { get; private set; }

        public string Error { get; private set; }
    }
}

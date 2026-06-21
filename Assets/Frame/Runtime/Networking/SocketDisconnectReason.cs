namespace Frame.Networking
{
    public enum SocketDisconnectReason
    {
        None = 0,
        Local = 1,
        Remote = 2,
        Error = 3,
        Timeout = 4,
        Canceled = 5,
        ReconnectFailed = 6,
        Shutdown = 7
    }
}

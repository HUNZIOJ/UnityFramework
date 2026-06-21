namespace Frame.Networking
{
    public enum SocketClientState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Reconnecting = 3,
        Disconnecting = 4
    }
}

namespace Frame.Networking
{
    public interface ISocketMessageCodec
    {
        byte[] Encode(SocketMessage message);

        bool TryDecode(SocketReceiveBuffer buffer, out SocketMessage message);
    }
}

using System;
using System.Buffers.Binary;

namespace Frame.Networking
{
    public sealed class LengthPrefixedSocketCodec : ISocketMessageCodec
    {
        public LengthPrefixedSocketCodec(int maxPayloadBytes = 1024 * 1024)
        {
            MaxPayloadBytes = Math.Max(1, maxPayloadBytes);
        }

        public int MaxPayloadBytes { get; private set; }

        public byte[] Encode(SocketMessage message)
        {
            byte[] payload = message == null || message.Data == null ? new byte[0] : message.Data;
            if (payload.Length > MaxPayloadBytes)
            {
                throw new InvalidOperationException("Socket payload exceeds max size: " + payload.Length);
            }

            byte[] frame = new byte[payload.Length + 4];
            BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
            if (payload.Length > 0)
            {
                Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
            }

            return frame;
        }

        public bool TryDecode(SocketReceiveBuffer buffer, out SocketMessage message)
        {
            message = null;
            if (buffer == null || !buffer.TryPeekInt32BigEndian(out int length))
            {
                return false;
            }

            if (length < 0 || length > MaxPayloadBytes)
            {
                throw new InvalidOperationException("Invalid socket payload length: " + length);
            }

            if (buffer.Count < length + 4)
            {
                return false;
            }

            buffer.Discard(4);
            byte[] payload;
            if (!buffer.TryRead(length, out payload))
            {
                return false;
            }

            message = SocketMessage.WrapUnsafe(payload, SocketMessageKind.Binary);
            return true;
        }
    }
}

using System;

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
            WriteInt32BigEndian(frame, 0, payload.Length);
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

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xff);
            buffer[offset + 1] = (byte)((value >> 16) & 0xff);
            buffer[offset + 2] = (byte)((value >> 8) & 0xff);
            buffer[offset + 3] = (byte)(value & 0xff);
        }
    }
}

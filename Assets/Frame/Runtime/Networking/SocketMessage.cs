using System;
using System.Text;

namespace Frame.Networking
{
    public sealed class SocketMessage
    {
        private static readonly byte[] EmptyData = new byte[0];

        public SocketMessage(byte[] data, SocketMessageKind kind = SocketMessageKind.Binary)
        {
            Data = data == null || data.Length == 0 ? EmptyData : Copy(data);
            Kind = kind;
        }

        public byte[] Data { get; private set; }

        public SocketMessageKind Kind { get; private set; }

        public int Count
        {
            get { return Data == null ? 0 : Data.Length; }
        }

        public string Text
        {
            get { return Encoding.UTF8.GetString(Data ?? EmptyData); }
        }

        public static SocketMessage Binary(byte[] data)
        {
            return new SocketMessage(data, SocketMessageKind.Binary);
        }

        public static SocketMessage TextMessage(string text)
        {
            return new SocketMessage(Encoding.UTF8.GetBytes(text ?? string.Empty), SocketMessageKind.Text);
        }

        internal SocketMessage Clone()
        {
            return new SocketMessage(Data, Kind);
        }

        internal static SocketMessage WrapUnsafe(byte[] data, SocketMessageKind kind)
        {
            SocketMessage message = new SocketMessage(null, kind);
            message.Data = data ?? EmptyData;
            return message;
        }

        private static byte[] Copy(byte[] data)
        {
            byte[] copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            return copy;
        }
    }
}

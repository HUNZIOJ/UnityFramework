using System;

namespace Frame.Networking
{
    public sealed class SocketReceiveBuffer
    {
        private byte[] buffer;
        private int offset;
        private int count;

        public SocketReceiveBuffer(int capacity = 8192)
        {
            buffer = new byte[Math.Max(1, capacity)];
        }

        public int Count
        {
            get { return count; }
        }

        public void Append(byte[] data, int length)
        {
            if (data == null || length <= 0)
            {
                return;
            }

            EnsureWritable(length);
            Buffer.BlockCopy(data, 0, buffer, offset + count, length);
            count += length;
        }

        public bool TryPeekInt32BigEndian(out int value)
        {
            value = 0;
            if (count < 4)
            {
                return false;
            }

            int index = offset;
            value = (buffer[index] << 24)
                | (buffer[index + 1] << 16)
                | (buffer[index + 2] << 8)
                | buffer[index + 3];
            return true;
        }

        public bool TryRead(int length, out byte[] data)
        {
            data = null;
            if (length < 0 || count < length)
            {
                return false;
            }

            data = new byte[length];
            if (length > 0)
            {
                Buffer.BlockCopy(buffer, offset, data, 0, length);
            }

            Discard(length);
            return true;
        }

        public void Discard(int length)
        {
            if (length <= 0)
            {
                return;
            }

            if (length >= count)
            {
                offset = 0;
                count = 0;
                return;
            }

            offset += length;
            count -= length;
        }

        public void Clear()
        {
            offset = 0;
            count = 0;
        }

        private void EnsureWritable(int length)
        {
            int required = offset + count + length;
            if (required <= buffer.Length)
            {
                return;
            }

            if (count + length <= buffer.Length)
            {
                Buffer.BlockCopy(buffer, offset, buffer, 0, count);
                offset = 0;
                return;
            }

            int capacity = buffer.Length;
            while (capacity < count + length)
            {
                capacity *= 2;
            }

            byte[] next = new byte[capacity];
            if (count > 0)
            {
                Buffer.BlockCopy(buffer, offset, next, 0, count);
            }

            buffer = next;
            offset = 0;
        }
    }
}

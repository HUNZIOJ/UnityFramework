using System;

namespace Frame.Core
{
    public sealed class FrameException : Exception
    {
        public FrameException(string message)
            : base(message)
        {
        }

        public FrameException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

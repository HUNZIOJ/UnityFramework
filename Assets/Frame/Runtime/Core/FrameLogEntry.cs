using System;

namespace Frame.Core
{
    public sealed class FrameLogEntry
    {
        public FrameLogEntry(FrameLogLevel level, string message, string formattedMessage, Exception exception = null)
        {
            Level = level;
            Message = message;
            FormattedMessage = formattedMessage;
            Exception = exception;
            UtcTicks = DateTime.UtcNow.Ticks;
        }

        public FrameLogLevel Level { get; private set; }

        public string Message { get; private set; }

        public string FormattedMessage { get; private set; }

        public Exception Exception { get; private set; }

        public long UtcTicks { get; private set; }

        public DateTime UtcTime
        {
            get { return new DateTime(UtcTicks, DateTimeKind.Utc); }
        }
    }
}

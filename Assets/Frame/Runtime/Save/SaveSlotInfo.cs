using System;

namespace Frame.Save
{
    [Serializable]
    public sealed class SaveSlotInfo
    {
        public string Slot;
        public string Path;
        public long LastWriteUtcTicks;
        public long SizeBytes;

        public DateTime LastWriteUtc
        {
            get { return new DateTime(LastWriteUtcTicks, DateTimeKind.Utc); }
        }
    }
}

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
        public bool HasMetadata;
        public int DataVersion;
        public bool Encrypted;
        public string SerializerExtension;

        public DateTime LastWriteUtc
        {
            get { return new DateTime(LastWriteUtcTicks, DateTimeKind.Utc); }
        }
    }
}

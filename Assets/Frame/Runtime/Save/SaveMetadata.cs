using System;

namespace Frame.Save
{
    [Serializable]
    public sealed class SaveMetadata
    {
        public int FormatVersion = 1;
        public string Slot;
        public string SerializerExtension;
        public int DataVersion;
        public long SavedAtUtcTicks;
        public long PayloadSizeBytes;
        public string PayloadSha256;
        public bool Encrypted;

        public DateTime SavedAtUtc
        {
            get { return SavedAtUtcTicks <= 0 ? DateTime.MinValue : new DateTime(SavedAtUtcTicks, DateTimeKind.Utc); }
        }
    }
}

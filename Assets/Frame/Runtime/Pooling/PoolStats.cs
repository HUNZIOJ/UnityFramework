using System;

namespace Frame.Pooling
{
    [Serializable]
    public sealed class PoolStats
    {
        public string Key;
        public int MaxSize;
        public int CountActive;
        public int CountInactive;
        public int CountTotal;
        public int CreatedCount;
        public int DestroyedCount;
        public int GetCount;
        public int ReleaseCount;
    }
}

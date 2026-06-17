using System;

namespace Frame.Diagnostics
{
    [Serializable]
    public sealed class DiagnosticsSnapshot
    {
        public int FrameCount;
        public float RealtimeSinceStartup;
        public float UnscaledTime;
        public float DeltaTime;
        public float AverageFps;
        public long ManagedMemoryBytes;
        public long TotalAllocatedMemoryBytes;
        public int BufferedLogCount;
        public int WarningCount;
        public int ErrorCount;
        public int ExceptionCount;
    }
}

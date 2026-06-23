namespace Frame.YooAsset
{
    public sealed class YooAssetResourceUpdateResult
    {
        public bool Success { get; internal set; }

        public bool IsSupported { get; internal set; }

        public bool UpdateNeeded { get; internal set; }

        public string PackageName { get; internal set; }

        public string LocalVersion { get; internal set; }

        public string RemoteVersion { get; internal set; }

        public int TotalDownloadCount { get; internal set; }

        public long TotalDownloadBytes { get; internal set; }

        public string Error { get; internal set; }

        public YooAssetResourceUpdatePhase Phase { get; internal set; }

        public static YooAssetResourceUpdateResult Completed(
            string packageName,
            bool isSupported,
            bool updateNeeded,
            string localVersion,
            string remoteVersion,
            int totalDownloadCount,
            long totalDownloadBytes)
        {
            return new YooAssetResourceUpdateResult
            {
                Success = true,
                IsSupported = isSupported,
                UpdateNeeded = updateNeeded,
                PackageName = packageName,
                LocalVersion = localVersion,
                RemoteVersion = remoteVersion,
                TotalDownloadCount = totalDownloadCount,
                TotalDownloadBytes = totalDownloadBytes,
                Phase = isSupported ? YooAssetResourceUpdatePhase.Completed : YooAssetResourceUpdatePhase.Unsupported
            };
        }

        public static YooAssetResourceUpdateResult Failed(string packageName, YooAssetResourceUpdatePhase phase, string error)
        {
            return new YooAssetResourceUpdateResult
            {
                Success = false,
                IsSupported = true,
                PackageName = packageName,
                Error = error,
                Phase = phase
            };
        }
    }
}

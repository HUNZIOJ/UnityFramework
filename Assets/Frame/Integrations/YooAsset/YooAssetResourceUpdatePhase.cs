namespace Frame.YooAsset
{
    public enum YooAssetResourceUpdatePhase
    {
        None = 0,
        CheckVersion = 1,
        LoadManifest = 2,
        CreateDownloader = 3,
        Downloading = 4,
        ClearCache = 5,
        Completed = 6,
        Failed = 7,
        Canceled = 8,
        Unsupported = 9
    }
}

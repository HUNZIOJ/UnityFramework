using System;
using UnityEngine;

namespace Frame.YooAsset
{
    public sealed class YooAssetResourceUpdateOperation : CustomYieldInstruction
    {
        private Action cancelAction;

        public override bool keepWaiting
        {
            get { return !IsDone; }
        }

        public bool IsDone { get; private set; }

        public bool IsCanceled { get; private set; }

        public bool Success
        {
            get { return Result != null && Result.Success; }
        }

        public bool IsSupported
        {
            get { return Result == null || Result.IsSupported; }
        }

        public YooAssetResourceUpdatePhase Phase { get; private set; }

        public float Progress { get; private set; }

        public string PackageName { get; private set; }

        public string LocalVersion { get; private set; }

        public string RemoteVersion { get; private set; }

        public int TotalDownloadCount { get; private set; }

        public long TotalDownloadBytes { get; private set; }

        public int CurrentDownloadCount { get; private set; }

        public long CurrentDownloadBytes { get; private set; }

        public string Error { get; private set; }

        public YooAssetResourceUpdateResult Result { get; private set; }

        public void Cancel()
        {
            if (IsDone || IsCanceled)
            {
                return;
            }

            IsCanceled = true;
            cancelAction?.Invoke();
        }

        internal void SetCancelAction(Action action)
        {
            cancelAction = action;
        }

        internal void SetPhase(YooAssetResourceUpdatePhase phase, float progress)
        {
            Phase = phase;
            Progress = Mathf.Clamp01(progress);
        }

        internal void SetPackage(string packageName)
        {
            PackageName = packageName;
        }

        internal void SetVersions(string localVersion, string remoteVersion)
        {
            LocalVersion = localVersion;
            RemoteVersion = remoteVersion;
        }

        internal void SetDownloadTotals(int totalCount, long totalBytes)
        {
            TotalDownloadCount = Mathf.Max(0, totalCount);
            TotalDownloadBytes = Math.Max(0, totalBytes);
        }

        internal void SetDownloadProgress(float progress, int currentCount, long currentBytes)
        {
            Progress = Mathf.Clamp01(progress);
            CurrentDownloadCount = Mathf.Max(0, currentCount);
            CurrentDownloadBytes = Math.Max(0, currentBytes);
        }

        internal void Complete(YooAssetResourceUpdateResult result)
        {
            Result = result;
            Error = result == null ? null : result.Error;
            if (result != null)
            {
                PackageName = result.PackageName;
                LocalVersion = result.LocalVersion;
                RemoteVersion = result.RemoteVersion;
                TotalDownloadCount = result.TotalDownloadCount;
                TotalDownloadBytes = result.TotalDownloadBytes;
                Phase = result.Phase;
                Progress = result.Success ? 1f : Progress;
            }

            cancelAction = null;
            IsDone = true;
        }

        internal void CompleteCanceled(string packageName)
        {
            Error = "Canceled.";
            Result = YooAssetResourceUpdateResult.Failed(packageName, YooAssetResourceUpdatePhase.Canceled, Error);
            Phase = YooAssetResourceUpdatePhase.Canceled;
            cancelAction = null;
            IsDone = true;
        }
    }
}

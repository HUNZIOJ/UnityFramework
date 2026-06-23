using System;
using Cysharp.Threading.Tasks;
using Frame.Core;
using YooAssetRuntime = YooAsset;

namespace Frame.YooAsset
{
    public sealed class YooAssetResourceUpdateService : GameModuleBase
    {
        public override int Priority
        {
            get { return -550; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register(this);
        }

        public YooAssetResourceUpdateOperation CheckForUpdates(YooAssetResourceUpdateOptions options = null, Action<YooAssetResourceUpdateResult> completed = null)
        {
            YooAssetResourceUpdateOperation operation = CreateOperation(options, out YooAssetResourceUpdateOptions normalized);
            CheckOrUpdateAsync(operation, normalized, false, completed).Forget();
            return operation;
        }

        public YooAssetResourceUpdateOperation Update(YooAssetResourceUpdateOptions options = null, Action<YooAssetResourceUpdateResult> completed = null)
        {
            YooAssetResourceUpdateOperation operation = CreateOperation(options, out YooAssetResourceUpdateOptions normalized);
            CheckOrUpdateAsync(operation, normalized, true, completed).Forget();
            return operation;
        }

        public YooAssetResourceUpdateOperation ClearCache(YooAssetResourceUpdateOptions options = null, Action<YooAssetResourceUpdateResult> completed = null)
        {
            YooAssetResourceUpdateOperation operation = CreateOperation(options, out YooAssetResourceUpdateOptions normalized);
            ClearCacheAsync(operation, normalized, completed).Forget();
            return operation;
        }

        private YooAssetResourceUpdateOperation CreateOperation(YooAssetResourceUpdateOptions options, out YooAssetResourceUpdateOptions normalized)
        {
            normalized = (options ?? new YooAssetResourceUpdateOptions()).Normalize(Context.Settings.YooAssetPackageName, Context.Settings.YooAssetDownloadMaxConcurrency);
            YooAssetResourceUpdateOperation operation = new YooAssetResourceUpdateOperation();
            operation.SetPackage(normalized.PackageName);
            operation.SetPhase(YooAssetResourceUpdatePhase.None, 0f);
            return operation;
        }

        private async UniTaskVoid CheckOrUpdateAsync(YooAssetResourceUpdateOperation operation, YooAssetResourceUpdateOptions options, bool download, Action<YooAssetResourceUpdateResult> completed)
        {
            try
            {
                if (!TryGetPackage(options.PackageName, out YooAssetRuntime.ResourcePackage package, out string error))
                {
                    Complete(operation, YooAssetResourceUpdateResult.Failed(options.PackageName, YooAssetResourceUpdatePhase.Failed, error), completed);
                    return;
                }

                string localVersion = TryGetPackageVersion(package);
                operation.SetVersions(localVersion, null);

                if (!SupportsRemoteUpdate())
                {
                    YooAssetResourceUpdateResult unsupported = YooAssetResourceUpdateResult.Completed(options.PackageName, false, false, localVersion, localVersion, 0, 0);
                    Complete(operation, unsupported, completed);
                    return;
                }

                operation.SetPhase(YooAssetResourceUpdatePhase.CheckVersion, 0.05f);
                YooAssetRuntime.RequestPackageVersionOperation versionOperation =
                    package.RequestPackageVersionAsync(new YooAssetRuntime.RequestPackageVersionOptions(options.AppendTimeTicks, options.TimeoutSeconds));

                await WaitYooOperation(versionOperation, operation, YooAssetResourceUpdatePhase.CheckVersion, 0.05f, 0.25f);
                if (operation.IsCanceled)
                {
                    operation.CompleteCanceled(options.PackageName);
                    InvokeCompleted(completed, operation.Result);
                    return;
                }

                if (versionOperation.Status != YooAssetRuntime.EOperationStatus.Succeeded)
                {
                    Complete(operation, YooAssetResourceUpdateResult.Failed(options.PackageName, YooAssetResourceUpdatePhase.CheckVersion, versionOperation.Error), completed);
                    return;
                }

                string remoteVersion = versionOperation.PackageVersion;
                bool updateNeeded = !string.Equals(localVersion, remoteVersion, StringComparison.Ordinal);
                operation.SetVersions(localVersion, remoteVersion);
                if (!download)
                {
                    YooAssetResourceUpdateResult result = YooAssetResourceUpdateResult.Completed(options.PackageName, true, updateNeeded, localVersion, remoteVersion, 0, 0);
                    Complete(operation, result, completed);
                    return;
                }

                operation.SetPhase(YooAssetResourceUpdatePhase.LoadManifest, 0.25f);
                YooAssetRuntime.LoadPackageManifestOperation manifestOperation =
                    package.LoadPackageManifestAsync(new YooAssetRuntime.LoadPackageManifestOptions(remoteVersion, options.TimeoutSeconds));

                await WaitYooOperation(manifestOperation, operation, YooAssetResourceUpdatePhase.LoadManifest, 0.25f, 0.45f);
                if (operation.IsCanceled)
                {
                    operation.CompleteCanceled(options.PackageName);
                    InvokeCompleted(completed, operation.Result);
                    return;
                }

                if (manifestOperation.Status != YooAssetRuntime.EOperationStatus.Succeeded)
                {
                    Complete(operation, YooAssetResourceUpdateResult.Failed(options.PackageName, YooAssetResourceUpdatePhase.LoadManifest, manifestOperation.Error), completed);
                    return;
                }

                operation.SetPhase(YooAssetResourceUpdatePhase.CreateDownloader, 0.45f);
                YooAssetRuntime.ResourceDownloaderOperation downloader = CreateDownloader(package, options);
                operation.SetDownloadTotals(downloader.TotalDownloadCount, downloader.TotalDownloadBytes);
                operation.SetCancelAction(downloader.CancelDownload);
                downloader.DownloadProgressChanged += args =>
                {
                    operation.SetDownloadProgress(
                        0.45f + args.Progress * 0.55f,
                        args.CurrentDownloadCount,
                        args.CurrentDownloadBytes);
                };

                if (downloader.TotalDownloadCount > 0)
                {
                    operation.SetPhase(YooAssetResourceUpdatePhase.Downloading, 0.45f);
                }

                downloader.StartDownload();
                while (!downloader.IsDone)
                {
                    if (operation.IsCanceled)
                    {
                        downloader.CancelDownload();
                        operation.CompleteCanceled(options.PackageName);
                        InvokeCompleted(completed, operation.Result);
                        return;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                if (downloader.Status != YooAssetRuntime.EOperationStatus.Succeeded)
                {
                    Complete(operation, YooAssetResourceUpdateResult.Failed(options.PackageName, YooAssetResourceUpdatePhase.Downloading, downloader.Error), completed);
                    return;
                }

                if (options.ClearUnusedCacheAfterUpdate)
                {
                    await RunClearCache(package, options, operation, 0.95f, 1f);
                    if (operation.IsCanceled)
                    {
                        operation.CompleteCanceled(options.PackageName);
                        InvokeCompleted(completed, operation.Result);
                        return;
                    }
                }

                YooAssetResourceUpdateResult completedResult = YooAssetResourceUpdateResult.Completed(
                    options.PackageName,
                    true,
                    updateNeeded || downloader.TotalDownloadCount > 0,
                    localVersion,
                    remoteVersion,
                    downloader.TotalDownloadCount,
                    downloader.TotalDownloadBytes);
                Complete(operation, completedResult, completed);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                Complete(operation, YooAssetResourceUpdateResult.Failed(options.PackageName, YooAssetResourceUpdatePhase.Failed, exception.Message), completed);
            }
        }

        private async UniTaskVoid ClearCacheAsync(YooAssetResourceUpdateOperation operation, YooAssetResourceUpdateOptions options, Action<YooAssetResourceUpdateResult> completed)
        {
            try
            {
                if (!TryGetPackage(options.PackageName, out YooAssetRuntime.ResourcePackage package, out string error))
                {
                    Complete(operation, YooAssetResourceUpdateResult.Failed(options.PackageName, YooAssetResourceUpdatePhase.ClearCache, error), completed);
                    return;
                }

                string localVersion = TryGetPackageVersion(package);
                operation.SetVersions(localVersion, localVersion);
                await RunClearCache(package, options, operation, 0f, 1f);
                if (operation.IsCanceled)
                {
                    operation.CompleteCanceled(options.PackageName);
                    InvokeCompleted(completed, operation.Result);
                    return;
                }

                YooAssetResourceUpdateResult result = YooAssetResourceUpdateResult.Completed(options.PackageName, true, false, localVersion, localVersion, 0, 0);
                Complete(operation, result, completed);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                Complete(operation, YooAssetResourceUpdateResult.Failed(options.PackageName, YooAssetResourceUpdatePhase.ClearCache, exception.Message), completed);
            }
        }

        private async UniTask RunClearCache(YooAssetRuntime.ResourcePackage package, YooAssetResourceUpdateOptions options, YooAssetResourceUpdateOperation operation, float startProgress, float endProgress)
        {
            operation.SetPhase(YooAssetResourceUpdatePhase.ClearCache, startProgress);
            string clearMethod = options.Tags == null || options.Tags.Length == 0
                ? YooAssetRuntime.ClearCacheMethods.ClearUnusedBundleFiles
                : YooAssetRuntime.ClearCacheMethods.ClearBundleFilesByTags;
            object clearParameter = options.Tags == null || options.Tags.Length == 0 ? null : options.Tags;
            YooAssetRuntime.ClearCacheOperation clearOperation = package.ClearCacheAsync(new YooAssetRuntime.ClearCacheOptions(clearMethod, clearParameter));
            await WaitYooOperation(clearOperation, operation, YooAssetResourceUpdatePhase.ClearCache, startProgress, endProgress);
            if (clearOperation.Status != YooAssetRuntime.EOperationStatus.Succeeded && !operation.IsCanceled)
            {
                throw new FrameException("YooAsset cache clear failed: " + clearOperation.Error);
            }
        }

        private static YooAssetRuntime.ResourceDownloaderOperation CreateDownloader(YooAssetRuntime.ResourcePackage package, YooAssetResourceUpdateOptions options)
        {
            YooAssetRuntime.ResourceDownloaderOptions downloaderOptions = options.Tags == null || options.Tags.Length == 0
                ? new YooAssetRuntime.ResourceDownloaderOptions(options.DownloadMaxConcurrency, options.DownloadRetryCount)
                : new YooAssetRuntime.ResourceDownloaderOptions(options.Tags, options.DownloadMaxConcurrency, options.DownloadRetryCount);
            return package.CreateResourceDownloader(downloaderOptions);
        }

        private static async UniTask WaitYooOperation(YooAssetRuntime.AsyncOperationBase yooOperation, YooAssetResourceUpdateOperation operation, YooAssetResourceUpdatePhase phase, float startProgress, float endProgress)
        {
            while (!yooOperation.IsDone)
            {
                if (operation.IsCanceled)
                {
                    break;
                }

                float progress = startProgress + (endProgress - startProgress) * yooOperation.Progress;
                operation.SetPhase(phase, progress);
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
        }

        private bool TryGetPackage(string packageName, out YooAssetRuntime.ResourcePackage package, out string error)
        {
            package = null;
            error = null;

            if (!YooAssetRuntime.YooAssets.IsInitialized)
            {
                error = "YooAssets is not initialized.";
                return false;
            }

            if (!YooAssetRuntime.YooAssets.TryGetPackage(packageName, out package) || package == null)
            {
                error = "YooAsset package is not created: " + packageName;
                return false;
            }

            if (package.InitializeStatus != YooAssetRuntime.EOperationStatus.Succeeded)
            {
                error = "YooAsset package is not initialized: " + packageName;
                return false;
            }

            return true;
        }

        private bool SupportsRemoteUpdate()
        {
            return Context.Settings.YooAssetPlayMode == Frame.Assets.YooAssetPlayMode.Host ||
                   Context.Settings.YooAssetPlayMode == Frame.Assets.YooAssetPlayMode.Web;
        }

        private static string TryGetPackageVersion(YooAssetRuntime.ResourcePackage package)
        {
            try
            {
                return package.GetPackageVersion();
            }
            catch
            {
                return null;
            }
        }

        private static void Complete(YooAssetResourceUpdateOperation operation, YooAssetResourceUpdateResult result, Action<YooAssetResourceUpdateResult> completed)
        {
            operation.Complete(result);
            InvokeCompleted(completed, result);
        }

        private static void InvokeCompleted(Action<YooAssetResourceUpdateResult> completed, YooAssetResourceUpdateResult result)
        {
            if (completed == null)
            {
                return;
            }

            try
            {
                completed(result);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Frame.Assets;
using Frame.Core;
using UnityEngine;
using Object = UnityEngine.Object;
using YooAssetRuntime = YooAsset;

namespace Frame.YooAsset
{
    public sealed class YooAssetAssetService : GameModuleBase, IAssetService
    {
        private readonly Dictionary<string, YooAssetEntry> cache = new Dictionary<string, YooAssetEntry>();
        private YooAssetRuntime.ResourcePackage package;
        private bool ownsYooAssets;
        private bool ownsPackage;
        private bool packageReady;
        private bool packageInitializing;
        private CancellationTokenSource initializeCancellation;

        public override int Priority
        {
            get { return -600; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IAssetService>(this);
            Context.Services.Register(this);
            initializeCancellation = new CancellationTokenSource();
            InitializePackageAsync(initializeCancellation.Token).Forget();
        }

        public AssetHandle<T> Load<T>(string path) where T : Object
        {
            AssetHandle<T> handle;
            TryLoadInternal(path, true, out handle);
            return handle;
        }

        public bool TryLoad<T>(string path, out AssetHandle<T> handle) where T : Object
        {
            return TryLoadInternal(path, false, out handle);
        }

        public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object
        {
            path = NormalizeLocation(path);
            AssetRequest<T> request = new AssetRequest<T>();

            YooAssetEntry entry;
            if (cache.TryGetValue(path, out entry) && entry.Asset != null)
            {
                T cachedAsset = entry.Asset as T;
                if (cachedAsset == null)
                {
                    AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                    CompleteRequest(request, empty, completed, "YooAsset asset type mismatch: " + path);
                    return request;
                }

                entry.RefCount++;
                AssetHandle<T> handle = new AssetHandle<T>(this, path, cachedAsset);
                CompleteRequest(request, handle, completed);
                return request;
            }

            LoadAsyncTask(path, request, completed).Forget();
            return request;
        }

        public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)
        {
            AssetHandle<GameObject> handle = Load<GameObject>(path);
            if (!handle.IsValid)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(handle.Asset, parent, worldPositionStays);
            if (instance == null)
            {
                handle.Release();
                return null;
            }

            AssetInstanceLease lease = instance.GetComponent<AssetInstanceLease>();
            if (lease == null)
            {
                lease = instance.AddComponent<AssetInstanceLease>();
            }

            lease.Bind(handle);
            return instance;
        }

        public bool IsLoaded(string path)
        {
            path = NormalizeLocation(path);
            YooAssetEntry entry;
            return cache.TryGetValue(path, out entry) && entry.Asset != null;
        }

        public int GetReferenceCount(string path)
        {
            path = NormalizeLocation(path);
            YooAssetEntry entry;
            return cache.TryGetValue(path, out entry) ? entry.RefCount : 0;
        }

        public List<AssetStats> GetLoadedAssetStats()
        {
            List<AssetStats> stats = new List<AssetStats>();
            foreach (KeyValuePair<string, YooAssetEntry> pair in cache)
            {
                if (pair.Value.Asset == null)
                {
                    continue;
                }

                stats.Add(new AssetStats
                {
                    Path = pair.Key,
                    TypeName = pair.Value.Asset.GetType().Name,
                    ReferenceCount = pair.Value.RefCount,
                    IsLoaded = true
                });
            }

            stats.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return stats;
        }

        public void Release(string path)
        {
            path = NormalizeLocation(path);
            YooAssetEntry entry;
            if (!cache.TryGetValue(path, out entry))
            {
                return;
            }

            entry.RefCount--;
            if (entry.RefCount > 0)
            {
                return;
            }

            ReleaseYooHandle(entry.Handle);
            cache.Remove(path);
        }

        public void ReleaseAll()
        {
            foreach (KeyValuePair<string, YooAssetEntry> pair in cache)
            {
                ReleaseYooHandle(pair.Value.Handle);
            }

            cache.Clear();
        }

        public void UnloadUnusedAssets()
        {
            if (package != null && packageReady)
            {
                YooAssetRuntime.UnloadUnusedAssetsOperation operation = package.UnloadUnusedAssetsAsync();
                operation.WaitForCompletion();
            }

            Resources.UnloadUnusedAssets();
        }

        protected override void OnShutdown()
        {
            if (initializeCancellation != null)
            {
                initializeCancellation.Cancel();
                initializeCancellation.Dispose();
                initializeCancellation = null;
            }

            packageInitializing = false;
            ReleaseAll();

            if (ownsPackage && package != null && package.InitializeStatus != YooAssetRuntime.EOperationStatus.None)
            {
                YooAssetRuntime.DestroyPackageOperation operation = package.DestroyPackageAsync();
                operation.WaitForCompletion();

                try
                {
                    YooAssetRuntime.YooAssets.RemovePackage(package.PackageName);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }
            }

            package = null;
            packageReady = false;
            ownsPackage = false;

            if (ownsYooAssets && YooAssetRuntime.YooAssets.IsInitialized)
            {
                YooAssetRuntime.YooAssets.Destroy();
            }

            ownsYooAssets = false;
        }

        private async UniTaskVoid LoadAsyncTask<T>(string path, AssetRequest<T> request, Action<AssetHandle<T>> completed) where T : Object
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, empty, completed, "YooAsset location is empty.");
                return;
            }

            if (!await WaitForPackageReadyAsync(request))
            {
                AssetHandle<T> failed = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, failed, completed, request.IsCanceled ? "Request canceled." : "YooAsset package is not ready.");
                return;
            }

            YooAssetRuntime.AssetHandle yooHandle;
            try
            {
                yooHandle = package.LoadAssetAsync<T>(path);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                AssetHandle<T> failed = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, failed, completed, exception.Message);
                return;
            }

            while (!yooHandle.IsDone)
            {
                request.SetProgress(yooHandle.Progress);
                if (request.IsCanceled)
                {
                    ReleaseYooHandle(yooHandle);
                    AssetHandle<T> canceled = new AssetHandle<T>(this, path, null);
                    CompleteRequest(request, canceled, completed, "Request canceled.");
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (request.IsCanceled)
            {
                ReleaseYooHandle(yooHandle);
                AssetHandle<T> canceled = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, canceled, completed, "Request canceled.");
                return;
            }

            T asset = GetLoadedAsset<T>(path, yooHandle, "YooAsset asset not found async");
            string error = null;
            if (asset == null)
            {
                error = string.IsNullOrWhiteSpace(yooHandle.Error) ? "YooAsset asset not found: " + path : yooHandle.Error;
                ReleaseYooHandle(yooHandle);
            }
            else
            {
                cache[path] = new YooAssetEntry
                {
                    Asset = asset,
                    Handle = yooHandle,
                    RefCount = 1
                };
            }

            AssetHandle<T> handle = new AssetHandle<T>(this, path, asset);
            CompleteRequest(request, handle, completed, error);
        }

        private async UniTaskVoid InitializePackageAsync(CancellationToken cancellationToken)
        {
            packageInitializing = true;
            try
            {
                FrameSettings settings = Context.Settings;
                string packageName = settings.YooAssetPackageName;

                if (!YooAssetRuntime.YooAssets.IsInitialized)
                {
                    YooAssetRuntime.YooAssets.Initialize();
                    ownsYooAssets = true;
                }

                if (!YooAssetRuntime.YooAssets.TryGetPackage(packageName, out package))
                {
                    package = YooAssetRuntime.YooAssets.CreatePackage(packageName);
                    ownsPackage = true;
                }

                while (package.InitializeStatus == YooAssetRuntime.EOperationStatus.Processing)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                if (package.InitializeStatus != YooAssetRuntime.EOperationStatus.Succeeded)
                {
                    YooAssetRuntime.InitializePackageOptions options = CreateInitializeOptions(settings);
                    YooAssetRuntime.InitializePackageOperation initializeOperation = package.InitializePackageAsync(options);
                    await WaitYooOperation(initializeOperation, cancellationToken);
                    if (initializeOperation.Status != YooAssetRuntime.EOperationStatus.Succeeded)
                    {
                        packageReady = false;
                        FrameLog.Warning("YooAsset package initialization failed: " + packageName + " error=" + initializeOperation.Error);
                        return;
                    }
                }

                if (package.PackageValid)
                {
                    packageReady = true;
                    FrameLog.Info("YooAsset package initialized: " + packageName + " version=" + package.GetPackageVersion() + " mode=" + settings.YooAssetPlayMode);
                    return;
                }

                YooAssetRuntime.RequestPackageVersionOperation versionOperation = package.RequestPackageVersionAsync(
                    new YooAssetRuntime.RequestPackageVersionOptions(true, 60));
                await WaitYooOperation(versionOperation, cancellationToken);
                if (versionOperation.Status != YooAssetRuntime.EOperationStatus.Succeeded)
                {
                    packageReady = false;
                    FrameLog.Warning("YooAsset package version request failed: " + packageName + " error=" + versionOperation.Error);
                    return;
                }

                YooAssetRuntime.LoadPackageManifestOperation manifestOperation = package.LoadPackageManifestAsync(
                    new YooAssetRuntime.LoadPackageManifestOptions(versionOperation.PackageVersion, 60));
                await WaitYooOperation(manifestOperation, cancellationToken);
                if (manifestOperation.Status == YooAssetRuntime.EOperationStatus.Succeeded)
                {
                    packageReady = true;
                    FrameLog.Info("YooAsset package initialized: " + packageName + " version=" + versionOperation.PackageVersion + " mode=" + settings.YooAssetPlayMode);
                }
                else
                {
                    packageReady = false;
                    FrameLog.Warning("YooAsset package manifest load failed: " + packageName + " error=" + manifestOperation.Error);
                }
            }
            catch (OperationCanceledException)
            {
                packageReady = false;
            }
            catch (Exception exception)
            {
                packageReady = false;
                FrameLog.Exception(exception);
            }
            finally
            {
                packageInitializing = false;
            }
        }

        private bool EnsurePackageReady(bool logWarnings = true)
        {
            if (packageReady && package != null)
            {
                return true;
            }

            if (logWarnings)
            {
                FrameLog.Warning(packageInitializing ? "YooAsset package is still initializing." : "YooAsset package is not ready.");
            }

            return false;
        }

        private async UniTask<bool> WaitForPackageReadyAsync<T>(AssetRequest<T> request) where T : Object
        {
            while (packageInitializing)
            {
                if (request.IsCanceled)
                {
                    return false;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            return EnsurePackageReady();
        }

        private static async UniTask WaitYooOperation(YooAssetRuntime.AsyncOperationBase operation, CancellationToken cancellationToken)
        {
            while (!operation.IsDone)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private static YooAssetRuntime.InitializePackageOptions CreateInitializeOptions(FrameSettings settings)
        {
            YooAssetRuntime.InitializePackageOptions options;
            switch (settings.YooAssetPlayMode)
            {
                case YooAssetPlayMode.EditorSimulate:
#if UNITY_EDITOR
                    string packageName = settings.YooAssetPackageName;
                    string editorPackageRoot = settings.YooAssetEditorPackageRoot;
                    if (string.IsNullOrWhiteSpace(editorPackageRoot))
                    {
                        YooAssetRuntime.PackageBuildResult buildResult = YooAssetRuntime.EditorSimulateBuildInvoker.Build(
                            packageName,
                            (int)YooAssetRuntime.EBundleType.VirtualAssetBundle);
                        editorPackageRoot = buildResult.PackageRootDirectory;
                    }

                    YooAssetRuntime.EditorSimulateModeOptions editorOptions = new YooAssetRuntime.EditorSimulateModeOptions();
                    editorOptions.EditorFileSystemParameters = YooAssetRuntime.FileSystemParameters.CreateDefaultEditorFileSystemParameters(editorPackageRoot);
                    options = editorOptions;
                    break;
#else
                    FrameLog.Warning("YooAsset EditorSimulate mode is editor-only. Falling back to Offline mode.");
                    options = CreateOfflineOptions(settings);
                    break;
#endif

                case YooAssetPlayMode.Host:
                    options = CreateHostOptions(settings);
                    break;

                case YooAssetPlayMode.Web:
                    options = CreateWebOptions(settings);
                    break;

                default:
                    options = CreateOfflineOptions(settings);
                    break;
            }

            options.AutoUnloadBundleWhenUnused = true;
            return options;
        }

        private static YooAssetRuntime.OfflinePlayModeOptions CreateOfflineOptions(FrameSettings settings)
        {
            YooAssetRuntime.OfflinePlayModeOptions options = new YooAssetRuntime.OfflinePlayModeOptions();
            options.BuiltinFileSystemParameters = CreateBuiltinFileSystemParameters(settings);
            return options;
        }

        private static YooAssetRuntime.HostPlayModeOptions CreateHostOptions(FrameSettings settings)
        {
            YooAssetRemoteService remoteService = new YooAssetRemoteService(settings.YooAssetDefaultHostServer, settings.YooAssetFallbackHostServer);
            YooAssetRuntime.HostPlayModeOptions options = new YooAssetRuntime.HostPlayModeOptions();
            options.BuiltinFileSystemParameters = CreateBuiltinFileSystemParameters(settings);
            options.BuiltinFileSystemParameters.AddParameter(YooAssetRuntime.EFileSystemParameter.CopyBuiltinPackageManifest, true);
            options.CacheFileSystemParameters = YooAssetRuntime.FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService);
            AddDownloadParameters(options.CacheFileSystemParameters, settings);
            return options;
        }

        private static YooAssetRuntime.WebPlayModeOptions CreateWebOptions(FrameSettings settings)
        {
            YooAssetRemoteService remoteService = new YooAssetRemoteService(settings.YooAssetDefaultHostServer, settings.YooAssetFallbackHostServer);
            YooAssetRuntime.WebPlayModeOptions options = new YooAssetRuntime.WebPlayModeOptions();
            options.WebServerFileSystemParameters = YooAssetRuntime.FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
            options.WebNetworkFileSystemParameters = YooAssetRuntime.FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(remoteService);
            AddDownloadParameters(options.WebNetworkFileSystemParameters, settings);
            return options;
        }

        private static YooAssetRuntime.FileSystemParameters CreateBuiltinFileSystemParameters(FrameSettings settings)
        {
            string packageRoot = settings.YooAssetBuiltinPackageRoot;
            return string.IsNullOrWhiteSpace(packageRoot)
                ? YooAssetRuntime.FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                : YooAssetRuntime.FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(packageRoot);
        }

        private static void AddDownloadParameters(YooAssetRuntime.FileSystemParameters parameters, FrameSettings settings)
        {
            parameters.AddParameter(YooAssetRuntime.EFileSystemParameter.DownloadMaxConcurrency, settings.YooAssetDownloadMaxConcurrency);
            parameters.AddParameter(YooAssetRuntime.EFileSystemParameter.DownloadMaxRequestPerFrame, settings.YooAssetDownloadMaxRequestPerFrame);
            parameters.AddParameter(YooAssetRuntime.EFileSystemParameter.DownloadWatchdogTimeout, settings.YooAssetDownloadWatchdogTimeout);
        }

        private static T GetLoadedAsset<T>(string path, YooAssetRuntime.AssetHandle yooHandle, string logPrefix, bool logWarnings = true) where T : Object
        {
            if (yooHandle == null || yooHandle.Status != YooAssetRuntime.EOperationStatus.Succeeded)
            {
                if (logWarnings)
                {
                    string error = yooHandle == null || string.IsNullOrWhiteSpace(yooHandle.Error) ? "Unknown error." : yooHandle.Error;
                    FrameLog.Warning(logPrefix + ": " + path + " type=" + typeof(T).Name + " error=" + error);
                }

                return null;
            }

            T asset = yooHandle.GetAssetObject<T>();
            if (asset == null && logWarnings)
            {
                FrameLog.Warning("YooAsset asset type mismatch or null: " + path + " expected=" + typeof(T).Name);
            }

            return asset;
        }

        private bool TryLoadInternal<T>(string path, bool logWarnings, out AssetHandle<T> handle) where T : Object
        {
            path = NormalizeLocation(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                if (logWarnings)
                {
                    FrameLog.Warning("YooAsset location is empty.");
                }

                handle = new AssetHandle<T>(this, path, null);
                return false;
            }

            if (!EnsurePackageReady(logWarnings))
            {
                handle = new AssetHandle<T>(this, path, null);
                return false;
            }

            YooAssetEntry entry;
            if (cache.TryGetValue(path, out entry) && entry.Asset != null)
            {
                T cachedAsset = entry.Asset as T;
                if (cachedAsset == null)
                {
                    if (logWarnings)
                    {
                        FrameLog.Warning("YooAsset asset type mismatch: " + path + " expected=" + typeof(T).Name);
                    }

                    handle = new AssetHandle<T>(this, path, null);
                    return false;
                }

                entry.RefCount++;
                handle = new AssetHandle<T>(this, path, cachedAsset);
                return true;
            }

            YooAssetRuntime.AssetHandle yooHandle = null;
            try
            {
                yooHandle = package.LoadAssetSync<T>(path);
            }
            catch (Exception exception)
            {
                if (logWarnings)
                {
                    FrameLog.Exception(exception);
                }
            }

            T asset = GetLoadedAsset<T>(path, yooHandle, "YooAsset asset not found", logWarnings);
            if (asset == null)
            {
                ReleaseYooHandle(yooHandle);
                handle = new AssetHandle<T>(this, path, null);
                return false;
            }

            cache[path] = new YooAssetEntry
            {
                Asset = asset,
                Handle = yooHandle,
                RefCount = 1
            };

            handle = new AssetHandle<T>(this, path, asset);
            return true;
        }

        private static void CompleteRequest<T>(AssetRequest<T> request, AssetHandle<T> handle, Action<AssetHandle<T>> completed, string error = null) where T : Object
        {
            request.Complete(handle, error);
            if (completed == null)
            {
                return;
            }

            try
            {
                completed(handle);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private static void ReleaseYooHandle(YooAssetRuntime.AssetHandle yooHandle)
        {
            if (yooHandle != null && yooHandle.IsValid)
            {
                yooHandle.Release();
            }
        }

        private static string NormalizeLocation(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
        }

        private sealed class YooAssetEntry
        {
            public Object Asset;
            public YooAssetRuntime.AssetHandle Handle;
            public int RefCount;
        }

        private sealed class YooAssetRemoteService : YooAssetRuntime.IRemoteService
        {
            private readonly string defaultHostServer;
            private readonly string fallbackHostServer;

            public YooAssetRemoteService(string defaultHostServer, string fallbackHostServer)
            {
                this.defaultHostServer = NormalizeHost(defaultHostServer);
                this.fallbackHostServer = NormalizeHost(fallbackHostServer);
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                List<string> urls = new List<string>(2);
                if (!string.IsNullOrWhiteSpace(defaultHostServer))
                {
                    urls.Add(CombineUrl(defaultHostServer, fileName));
                }

                if (!string.IsNullOrWhiteSpace(fallbackHostServer))
                {
                    urls.Add(CombineUrl(fallbackHostServer, fileName));
                }

                if (urls.Count == 0)
                {
                    urls.Add(fileName);
                }

                return urls;
            }

            private static string NormalizeHost(string host)
            {
                return string.IsNullOrWhiteSpace(host) ? string.Empty : host.Trim().TrimEnd('/');
            }

            private static string CombineUrl(string host, string fileName)
            {
                return host + "/" + (string.IsNullOrWhiteSpace(fileName) ? string.Empty : fileName.TrimStart('/'));
            }
        }
    }
}

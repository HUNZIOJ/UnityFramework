using System;
using System.Collections.Generic;
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

        public override int Priority
        {
            get { return -600; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IAssetService>(this);
            Context.Services.Register(this);
            try
            {
                InitializePackage();
            }
            catch (Exception exception)
            {
                packageReady = false;
                FrameLog.Exception(exception);
            }
        }

        public AssetHandle<T> Load<T>(string path) where T : Object
        {
            path = NormalizeLocation(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                FrameLog.Warning("YooAsset location is empty.");
                return new AssetHandle<T>(this, path, null);
            }

            if (!EnsurePackageReady())
            {
                return new AssetHandle<T>(this, path, null);
            }

            YooAssetEntry entry;
            if (cache.TryGetValue(path, out entry) && entry.Asset != null)
            {
                T cachedAsset = entry.Asset as T;
                if (cachedAsset == null)
                {
                    FrameLog.Warning("YooAsset asset type mismatch: " + path + " expected=" + typeof(T).Name);
                    return new AssetHandle<T>(this, path, null);
                }

                entry.RefCount++;
                return new AssetHandle<T>(this, path, cachedAsset);
            }

            YooAssetRuntime.AssetHandle yooHandle = null;
            try
            {
                yooHandle = package.LoadAssetSync<T>(path);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }

            T asset = GetLoadedAsset<T>(path, yooHandle, "YooAsset asset not found");
            if (asset == null)
            {
                ReleaseYooHandle(yooHandle);
                return new AssetHandle<T>(this, path, null);
            }

            cache[path] = new YooAssetEntry
            {
                Asset = asset,
                Handle = yooHandle,
                RefCount = 1
            };

            return new AssetHandle<T>(this, path, asset);
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

            if (!EnsurePackageReady())
            {
                AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, empty, completed, "YooAsset package is not ready.");
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

        private void InitializePackage()
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

            if (package.InitializeStatus == YooAssetRuntime.EOperationStatus.Succeeded)
            {
                packageReady = true;
                return;
            }

            YooAssetRuntime.InitializePackageOptions options = CreateInitializeOptions(settings);
            YooAssetRuntime.InitializePackageOperation operation = package.InitializePackageAsync(options);
            operation.WaitForCompletion();

            if (operation.Status == YooAssetRuntime.EOperationStatus.Succeeded)
            {
                packageReady = true;
                FrameLog.Info("YooAsset package initialized: " + packageName + " mode=" + settings.YooAssetPlayMode);
            }
            else
            {
                packageReady = false;
                FrameLog.Warning("YooAsset package initialization failed: " + packageName + " error=" + operation.Error);
            }
        }

        private bool EnsurePackageReady()
        {
            if (packageReady && package != null)
            {
                return true;
            }

            FrameLog.Warning("YooAsset package is not ready.");
            return false;
        }

        private static YooAssetRuntime.InitializePackageOptions CreateInitializeOptions(FrameSettings settings)
        {
            YooAssetRuntime.InitializePackageOptions options;
            switch (settings.YooAssetPlayMode)
            {
                case YooAssetPlayMode.EditorSimulate:
#if UNITY_EDITOR
                    YooAssetRuntime.EditorSimulateModeOptions editorOptions = new YooAssetRuntime.EditorSimulateModeOptions();
                    editorOptions.EditorFileSystemParameters = YooAssetRuntime.FileSystemParameters.CreateDefaultEditorFileSystemParameters(settings.YooAssetEditorPackageRoot);
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

        private static T GetLoadedAsset<T>(string path, YooAssetRuntime.AssetHandle yooHandle, string logPrefix) where T : Object
        {
            if (yooHandle == null || yooHandle.Status != YooAssetRuntime.EOperationStatus.Succeeded)
            {
                string error = yooHandle == null || string.IsNullOrWhiteSpace(yooHandle.Error) ? "Unknown error." : yooHandle.Error;
                FrameLog.Warning(logPrefix + ": " + path + " type=" + typeof(T).Name + " error=" + error);
                return null;
            }

            T asset = yooHandle.GetAssetObject<T>();
            if (asset == null)
            {
                FrameLog.Warning("YooAsset asset type mismatch or null: " + path + " expected=" + typeof(T).Name);
            }

            return asset;
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

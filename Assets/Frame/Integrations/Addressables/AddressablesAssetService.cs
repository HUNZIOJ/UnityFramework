using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Frame.Assets;
using Frame.Core;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;
using UnityAddressables = UnityEngine.AddressableAssets.Addressables;

namespace Frame.Addressables
{
    public sealed class AddressablesAssetService : GameModuleBase, IAssetService
    {
        private readonly Dictionary<string, AddressablesAssetEntry> cache = new Dictionary<string, AddressablesAssetEntry>();
        private bool initialized;

        public override int Priority
        {
            get { return -600; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IAssetService>(this);
            Context.Services.Register(this);
            EnsureInitialized();
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
            path = NormalizeAddress(path);
            AssetRequest<T> request = new AssetRequest<T>();

            AddressablesAssetEntry entry;
            if (cache.TryGetValue(path, out entry) && entry.Asset != null)
            {
                T cachedAsset = entry.Asset as T;
                if (cachedAsset == null)
                {
                    AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                    CompleteRequest(request, empty, completed, "Addressables asset type mismatch: " + path);
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
            path = NormalizeAddress(path);
            AddressablesAssetEntry entry;
            return cache.TryGetValue(path, out entry) && entry.Asset != null;
        }

        public int GetReferenceCount(string path)
        {
            path = NormalizeAddress(path);
            AddressablesAssetEntry entry;
            return cache.TryGetValue(path, out entry) ? entry.RefCount : 0;
        }

        public List<AssetStats> GetLoadedAssetStats()
        {
            List<AssetStats> stats = new List<AssetStats>();
            foreach (KeyValuePair<string, AddressablesAssetEntry> pair in cache)
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
            path = NormalizeAddress(path);
            AddressablesAssetEntry entry;
            if (!cache.TryGetValue(path, out entry))
            {
                return;
            }

            entry.RefCount--;
            if (entry.RefCount > 0)
            {
                return;
            }

            ReleaseOperation(entry.Handle);
            cache.Remove(path);
        }

        public void ReleaseAll()
        {
            foreach (KeyValuePair<string, AddressablesAssetEntry> pair in cache)
            {
                ReleaseOperation(pair.Value.Handle);
            }

            cache.Clear();
        }

        public void UnloadUnusedAssets()
        {
            Resources.UnloadUnusedAssets();
        }

        protected override void OnShutdown()
        {
            ReleaseAll();
        }

        private async UniTaskVoid LoadAsyncTask<T>(string path, AssetRequest<T> request, Action<AssetHandle<T>> completed) where T : Object
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, empty, completed, "Addressables address is empty.");
                return;
            }

            EnsureInitialized();

            AsyncOperationHandle<T> operation;
            try
            {
                operation = UnityAddressables.LoadAssetAsync<T>(path);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                AssetHandle<T> failed = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, failed, completed, exception.Message);
                return;
            }

            while (!operation.IsDone)
            {
                request.SetProgress(operation.PercentComplete);
                if (request.IsCanceled)
                {
                    ReleaseOperation(operation);
                    AssetHandle<T> canceled = new AssetHandle<T>(this, path, null);
                    CompleteRequest(request, canceled, completed, "Request canceled.");
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (request.IsCanceled)
            {
                ReleaseOperation(operation);
                AssetHandle<T> canceled = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, canceled, completed, "Request canceled.");
                return;
            }

            T asset = operation.Status == AsyncOperationStatus.Succeeded ? operation.Result : null;
            string error = null;
            if (asset == null)
            {
                error = operation.OperationException == null ? "Addressables asset not found: " + path : operation.OperationException.Message;
                FrameLog.Warning("Addressables asset not found async: " + path + " type=" + typeof(T).Name + " error=" + error);
                ReleaseOperation(operation);
            }
            else
            {
                cache[path] = new AddressablesAssetEntry
                {
                    Asset = asset,
                    Handle = operation,
                    RefCount = 1
                };
            }

            AssetHandle<T> handle = new AssetHandle<T>(this, path, asset);
            CompleteRequest(request, handle, completed, error);
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            try
            {
                var operation = UnityAddressables.InitializeAsync();
                operation.WaitForCompletion();
                if (operation.Status != AsyncOperationStatus.Succeeded)
                {
                    string error = operation.OperationException == null ? "Unknown error." : operation.OperationException.Message;
                    FrameLog.Warning("Addressables initialization failed: " + error);
                }
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }

            initialized = true;
        }

        private bool TryLoadInternal<T>(string path, bool logWarnings, out AssetHandle<T> handle) where T : Object
        {
            path = NormalizeAddress(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                if (logWarnings)
                {
                    FrameLog.Warning("Addressables address is empty.");
                }

                handle = new AssetHandle<T>(this, path, null);
                return false;
            }

            EnsureInitialized();

            AddressablesAssetEntry entry;
            if (cache.TryGetValue(path, out entry) && entry.Asset != null)
            {
                T cachedAsset = entry.Asset as T;
                if (cachedAsset == null)
                {
                    if (logWarnings)
                    {
                        FrameLog.Warning("Addressables asset type mismatch: " + path + " expected=" + typeof(T).Name);
                    }

                    handle = new AssetHandle<T>(this, path, null);
                    return false;
                }

                entry.RefCount++;
                handle = new AssetHandle<T>(this, path, cachedAsset);
                return true;
            }

            AsyncOperationHandle<T> operation = default(AsyncOperationHandle<T>);
            T asset = null;
            try
            {
                operation = UnityAddressables.LoadAssetAsync<T>(path);
                asset = operation.WaitForCompletion();
            }
            catch (Exception exception)
            {
                if (logWarnings)
                {
                    FrameLog.Exception(exception);
                }

                ReleaseOperation(operation);
                handle = new AssetHandle<T>(this, path, null);
                return false;
            }

            if (operation.Status != AsyncOperationStatus.Succeeded || asset == null)
            {
                if (logWarnings)
                {
                    string error = operation.OperationException == null ? "Unknown error." : operation.OperationException.Message;
                    FrameLog.Warning("Addressables asset not found: " + path + " type=" + typeof(T).Name + " error=" + error);
                }

                ReleaseOperation(operation);
                handle = new AssetHandle<T>(this, path, null);
                return false;
            }

            cache[path] = new AddressablesAssetEntry
            {
                Asset = asset,
                Handle = operation,
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

        private static void ReleaseOperation<T>(AsyncOperationHandle<T> operation)
        {
            if (operation.IsValid())
            {
                UnityAddressables.Release(operation);
            }
        }

        private static void ReleaseOperation(AsyncOperationHandle operation)
        {
            if (operation.IsValid())
            {
                UnityAddressables.Release(operation);
            }
        }

        private static string NormalizeAddress(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
        }

        private sealed class AddressablesAssetEntry
        {
            public Object Asset;
            public AsyncOperationHandle Handle;
            public int RefCount;
        }
    }
}

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Frame.Core;
using Frame.Utilities;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Assets
{
    public sealed class ResourcesAssetService : GameModuleBase, IAssetService
    {
        private readonly Dictionary<string, Object> cache = new Dictionary<string, Object>();
        private readonly Dictionary<string, int> refCounts = new Dictionary<string, int>();

        public override int Priority
        {
            get { return -600; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IAssetService>(this);
            Context.Services.Register(this);
        }

        public AssetHandle<T> Load<T>(string path) where T : Object
        {
            path = FramePathUtility.NormalizeResourcesPath(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                FrameLog.Warning("Resources path is empty.");
                return new AssetHandle<T>(this, path, null);
            }

            Object asset;
            if (!cache.TryGetValue(path, out asset) || asset == null)
            {
                asset = Resources.Load<T>(path);
                if (asset == null)
                {
                    FrameLog.Warning("Resources asset not found: " + path + " type=" + typeof(T).Name);
                    return new AssetHandle<T>(this, path, null);
                }

                cache[path] = asset;
            }

            T typedAsset = asset as T;
            if (typedAsset == null)
            {
                FrameLog.Warning("Resources asset type mismatch: " + path + " expected=" + typeof(T).Name);
                return new AssetHandle<T>(this, path, null);
            }

            AddRef(path);
            return new AssetHandle<T>(this, path, typedAsset);
        }

        public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object
        {
            path = FramePathUtility.NormalizeResourcesPath(path);
            AssetRequest<T> request = new AssetRequest<T>();

            Object cached;
            if (cache.TryGetValue(path, out cached) && cached != null)
            {
                T typedCached = cached as T;
                if (typedCached == null)
                {
                    FrameLog.Warning("Resources asset type mismatch async: " + path + " expected=" + typeof(T).Name);
                    AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                    CompleteRequest(request, empty, completed, "Resources asset type mismatch: " + path);
                    return request;
                }

                AddRef(path);
                AssetHandle<T> handle = new AssetHandle<T>(this, path, typedCached);
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
            path = FramePathUtility.NormalizeResourcesPath(path);
            Object asset;
            return cache.TryGetValue(path, out asset) && asset != null;
        }

        public int GetReferenceCount(string path)
        {
            path = FramePathUtility.NormalizeResourcesPath(path);
            int count;
            return refCounts.TryGetValue(path, out count) ? count : 0;
        }

        public List<AssetStats> GetLoadedAssetStats()
        {
            List<AssetStats> stats = new List<AssetStats>();
            foreach (KeyValuePair<string, Object> pair in cache)
            {
                Object asset = pair.Value;
                if (asset == null)
                {
                    continue;
                }

                int count;
                refCounts.TryGetValue(pair.Key, out count);
                stats.Add(new AssetStats
                {
                    Path = pair.Key,
                    TypeName = asset.GetType().Name,
                    ReferenceCount = count,
                    IsLoaded = true
                });
            }

            stats.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return stats;
        }

        public void Release(string path)
        {
            path = FramePathUtility.NormalizeResourcesPath(path);
            int count;
            if (!refCounts.TryGetValue(path, out count))
            {
                return;
            }

            count--;
            if (count <= 0)
            {
                refCounts.Remove(path);
                cache.Remove(path);
            }
            else
            {
                refCounts[path] = count;
            }
        }

        public void ReleaseAll()
        {
            refCounts.Clear();
            cache.Clear();
        }

        public void UnloadUnusedAssets()
        {
            Resources.UnloadUnusedAssets();
        }

        protected override void OnShutdown()
        {
            cache.Clear();
            refCounts.Clear();
            Resources.UnloadUnusedAssets();
        }

        private async UniTaskVoid LoadAsyncTask<T>(string path, AssetRequest<T> request, Action<AssetHandle<T>> completed) where T : Object
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, empty, completed, "Resources path is empty.");

                return;
            }

            await UniTask.Yield(PlayerLoopTiming.Update);
            if (request.IsCanceled)
            {
                AssetHandle<T> canceled = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, canceled, completed, "Request canceled.");
                return;
            }

            ResourceRequest resourceRequest = Resources.LoadAsync<T>(path);
            while (!resourceRequest.isDone)
            {
                request.SetProgress(resourceRequest.progress);
                if (request.IsCanceled)
                {
                    AssetHandle<T> canceled = new AssetHandle<T>(this, path, null);
                    CompleteRequest(request, canceled, completed, "Request canceled.");
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            if (request.IsCanceled)
            {
                AssetHandle<T> canceled = new AssetHandle<T>(this, path, null);
                CompleteRequest(request, canceled, completed, "Request canceled.");
                return;
            }

            T asset = resourceRequest.asset as T;
            string error = null;
            if (asset == null)
            {
                error = "Resources asset not found: " + path;
                FrameLog.Warning("Resources asset not found async: " + path + " type=" + typeof(T).Name);
            }
            else
            {
                cache[path] = asset;
                AddRef(path);
            }

            AssetHandle<T> handle = new AssetHandle<T>(this, path, asset);
            CompleteRequest(request, handle, completed, error);
        }

        private static void CompleteRequest<T>(AssetRequest<T> request, AssetHandle<T> handle, Action<AssetHandle<T>> completed, string error = null) where T : Object
        {
            request.Complete(handle, error);
            if (completed != null)
            {
                try
                {
                    completed(handle);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }
            }
        }

        private void AddRef(string path)
        {
            int count;
            refCounts.TryGetValue(path, out count);
            refCounts[path] = count + 1;
        }
    }
}

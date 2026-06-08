using System;
using System.Collections;
using System.Collections.Generic;
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

            AddRef(path);
            T typedAsset = asset as T;
            if (typedAsset == null)
            {
                FrameLog.Warning("Resources asset type mismatch: " + path + " expected=" + typeof(T).Name);
            }

            return new AssetHandle<T>(this, path, typedAsset);
        }

        public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object
        {
            path = FramePathUtility.NormalizeResourcesPath(path);
            AssetRequest<T> request = new AssetRequest<T>();

            Object cached;
            if (cache.TryGetValue(path, out cached) && cached != null)
            {
                AddRef(path);
                AssetHandle<T> handle = new AssetHandle<T>(this, path, cached as T);
                request.Complete(handle);
                if (completed != null)
                {
                    completed(handle);
                }

                return request;
            }

            Context.Coroutines.Run(LoadAsyncRoutine(path, request, completed));
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
            handle.Release();
            return instance;
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

        private IEnumerator LoadAsyncRoutine<T>(string path, AssetRequest<T> request, Action<AssetHandle<T>> completed) where T : Object
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                AssetHandle<T> empty = new AssetHandle<T>(this, path, null);
                request.Complete(empty);
                if (completed != null)
                {
                    completed(empty);
                }

                yield break;
            }

            ResourceRequest resourceRequest = Resources.LoadAsync<T>(path);
            yield return resourceRequest;

            T asset = resourceRequest.asset as T;
            if (asset == null)
            {
                FrameLog.Warning("Resources asset not found async: " + path + " type=" + typeof(T).Name);
            }
            else
            {
                cache[path] = asset;
                AddRef(path);
            }

            AssetHandle<T> handle = new AssetHandle<T>(this, path, asset);
            request.Complete(handle);
            if (completed != null)
            {
                completed(handle);
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

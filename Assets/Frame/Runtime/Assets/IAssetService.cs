using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Assets
{
    public interface IAssetService
    {
        AssetHandle<T> Load<T>(string path) where T : Object;

        AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object;

        GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false);

        void Release(string path);

        void UnloadUnusedAssets();
    }
}

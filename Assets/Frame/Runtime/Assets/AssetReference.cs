using Frame.Utilities;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Assets
{
    [System.Serializable]
    public struct AssetReference<T> where T : Object
    {
        [SerializeField] private string resourcesPath;

        public AssetReference(string resourcesPath)
        {
            this.resourcesPath = FramePathUtility.NormalizeResourcesPath(resourcesPath);
        }

        public string ResourcesPath
        {
            get { return FramePathUtility.NormalizeResourcesPath(resourcesPath); }
        }

        public bool IsValid
        {
            get { return !string.IsNullOrWhiteSpace(ResourcesPath); }
        }

        public AssetHandle<T> Load(IAssetService assetService)
        {
            return assetService.Load<T>(ResourcesPath);
        }

        public AssetRequest<T> LoadAsync(IAssetService assetService, Action<AssetHandle<T>> completed = null)
        {
            return assetService.LoadAsync(ResourcesPath, completed);
        }

        public override string ToString()
        {
            return ResourcesPath;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace Frame.Assets
{
    [System.Serializable]
    public struct AssetReference<T> where T : Object
    {
        [FormerlySerializedAs("resourcesPath")]
        [SerializeField] private string path;

        public AssetReference(string path)
        {
            this.path = NormalizeLocation(path);
        }

        public string Path
        {
            get { return NormalizeLocation(path); }
        }

        public string ResourcesPath
        {
            get { return Path; }
        }

        public bool IsValid
        {
            get { return !string.IsNullOrWhiteSpace(Path); }
        }

        public AssetHandle<T> Load(IAssetService assetService)
        {
            return assetService.Load<T>(Path);
        }

        public AssetRequest<T> LoadAsync(IAssetService assetService, Action<AssetHandle<T>> completed = null)
        {
            return assetService.LoadAsync(Path, completed);
        }

        public override string ToString()
        {
            return Path;
        }

        private static string NormalizeLocation(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\\', '/').Trim();
        }
    }
}

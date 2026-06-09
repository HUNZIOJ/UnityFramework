using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Assets
{
    public sealed class AssetHandle<T> : IDisposable where T : Object
    {
        private IAssetService owner;

        internal AssetHandle(IAssetService owner, string path, T asset)
        {
            this.owner = owner;
            Path = path;
            Asset = asset;
        }

        public string Path
        {
            get;
            private set;
        }

        public T Asset
        {
            get;
            private set;
        }

        public bool IsValid
        {
            get { return Asset != null; }
        }

        public void Release()
        {
            Dispose();
        }

        public void Dispose()
        {
            IAssetService service = owner;
            if (service == null || Asset == null || string.IsNullOrWhiteSpace(Path))
            {
                owner = null;
                return;
            }

            owner = null;
            service.Release(Path);
        }
    }
}

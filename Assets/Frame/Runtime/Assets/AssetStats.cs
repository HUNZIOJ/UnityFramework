using System;

namespace Frame.Assets
{
    [Serializable]
    public sealed class AssetStats
    {
        public string Path;
        public string TypeName;
        public int ReferenceCount;
        public bool IsLoaded;
    }
}

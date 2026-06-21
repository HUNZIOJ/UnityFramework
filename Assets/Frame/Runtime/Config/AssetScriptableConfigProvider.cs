using Frame.Assets;
using Frame.Utilities;

namespace Frame.Config
{
    public sealed class AssetScriptableConfigProvider : IConfigProvider
    {
        private readonly IAssetService assets;
        private readonly string rootPath;

        public AssetScriptableConfigProvider(IAssetService assets, string rootPath = "Configs")
        {
            this.assets = assets;
            this.rootPath = FramePathUtility.NormalizeResourcesPath(rootPath);
        }

        public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
        {
            config = null;
            if (assets == null || !typeof(ScriptableConfig).IsAssignableFrom(typeof(TConfig)))
            {
                return false;
            }

            AssetHandle<ScriptableConfig> handle;
            if (!assets.TryLoad<ScriptableConfig>(ResolvePath(key), out handle))
            {
                return false;
            }

            try
            {
                config = handle.Asset as TConfig;
                return config != null;
            }
            finally
            {
                handle.Release();
            }
        }

        private string ResolvePath(string key)
        {
            string path = string.IsNullOrWhiteSpace(rootPath) ? key : rootPath + "/" + key;
            return FramePathUtility.NormalizeResourcesPath(path);
        }
    }
}

using Frame.Assets;

namespace Frame.Config
{
    public sealed class AssetScriptableConfigProvider : IConfigProvider
    {
        private readonly IAssetService assets;
        private readonly string rootPath;

        public AssetScriptableConfigProvider(IAssetService assets, string rootPath = "Configs")
        {
            this.assets = assets;
            this.rootPath = NormalizeLocation(rootPath).Trim('/');
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
            return NormalizeLocation(path);
        }

        private static string NormalizeLocation(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\\', '/').Trim();
        }
    }
}

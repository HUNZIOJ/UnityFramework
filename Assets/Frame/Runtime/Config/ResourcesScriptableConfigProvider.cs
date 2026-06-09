using Frame.Utilities;
using UnityEngine;

namespace Frame.Config
{
    public sealed class ResourcesScriptableConfigProvider : IConfigProvider
    {
        private readonly ScriptableConfigProvider provider = new ScriptableConfigProvider();

        public ResourcesScriptableConfigProvider(string rootPath = "Configs")
        {
            string path = FramePathUtility.NormalizeResourcesPath(rootPath);
            ScriptableObject[] assets = Resources.LoadAll<ScriptableObject>(path);
            for (int i = 0; i < assets.Length; i++)
            {
                ScriptableConfig config = assets[i] as ScriptableConfig;
                if (config != null)
                {
                    provider.Register(config);
                }
            }
        }

        public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
        {
            return provider.TryLoad(key, out config);
        }
    }
}

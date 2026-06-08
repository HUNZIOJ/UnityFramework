using System.Collections.Generic;

namespace Frame.Config
{
    public sealed class ScriptableConfigProvider : IConfigProvider
    {
        private readonly Dictionary<string, ScriptableConfig> configs = new Dictionary<string, ScriptableConfig>();

        public void Register(ScriptableConfig config)
        {
            if (config == null)
            {
                return;
            }

            configs[config.Id] = config;
        }

        public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
        {
            ScriptableConfig value;
            if (configs.TryGetValue(key, out value))
            {
                config = value as TConfig;
                return config != null;
            }

            config = null;
            return false;
        }

        public void Clear()
        {
            configs.Clear();
        }
    }
}

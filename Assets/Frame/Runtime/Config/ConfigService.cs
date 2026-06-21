using System;
using System.Collections.Generic;
using Frame.Assets;
using Frame.Core;

namespace Frame.Config
{
    public sealed class ConfigService : GameModuleBase, IConfigService
    {
        private readonly List<IConfigProvider> providers = new List<IConfigProvider>();
        private readonly Dictionary<string, object> cache = new Dictionary<string, object>();

        public bool CacheEnabled { get; set; } = true;

        public override int Priority
        {
            get { return -200; }
        }

        protected override void OnInitialize()
        {
            IAssetService assets;
            if (Context.Services.TryResolve(out assets))
            {
                providers.Add(new AssetScriptableConfigProvider(assets));
                providers.Add(new AssetJsonConfigProvider(assets));
            }

            Context.Services.Register<IConfigService>(this);
            Context.Services.Register(this);
        }

        public void RegisterProvider(IConfigProvider provider)
        {
            if (provider != null && !providers.Contains(provider))
            {
                providers.Insert(0, provider);
                SubscribeProvider(provider);
                ClearCache();
            }
        }

        public bool UnregisterProvider(IConfigProvider provider)
        {
            if (provider == null)
            {
                return false;
            }

            bool removed = providers.Remove(provider);
            if (removed)
            {
                UnsubscribeProvider(provider);
                ClearCache();
            }

            return removed;
        }

        public void ClearCache()
        {
            cache.Clear();
        }

        public TConfig Load<TConfig>(string key) where TConfig : class
        {
            TConfig config;
            if (TryLoad(key, out config))
            {
                return config;
            }

            FrameLog.Warning("Config not found: " + key + " type=" + typeof(TConfig).Name);
            return null;
        }

        public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
        {
            string cacheKey = GetCacheKey<TConfig>(key);
            object cached;
            if (CacheEnabled && cache.TryGetValue(cacheKey, out cached))
            {
                config = cached as TConfig;
                return config != null;
            }

            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i].TryLoad(key, out config))
                {
                    if (!ValidateConfig(key, config))
                    {
                        config = null;
                        return false;
                    }

                    if (CacheEnabled)
                    {
                        cache[cacheKey] = config;
                    }

                    return true;
                }
            }

            config = null;
            return false;
        }

        protected override void OnShutdown()
        {
            for (int i = 0; i < providers.Count; i++)
            {
                UnsubscribeProvider(providers[i]);
            }

            providers.Clear();
            cache.Clear();
            CacheEnabled = true;
        }

        private void SubscribeProvider(IConfigProvider provider)
        {
            IConfigChangeNotifier notifier = provider as IConfigChangeNotifier;
            if (notifier != null)
            {
                notifier.Changed -= OnProviderChanged;
                notifier.Changed += OnProviderChanged;
            }
        }

        private void UnsubscribeProvider(IConfigProvider provider)
        {
            IConfigChangeNotifier notifier = provider as IConfigChangeNotifier;
            if (notifier != null)
            {
                notifier.Changed -= OnProviderChanged;
            }
        }

        private void OnProviderChanged()
        {
            ClearCache();
        }

        private static string GetCacheKey<TConfig>(string key)
        {
            return typeof(TConfig).FullName + ":" + key;
        }

        private static bool ValidateConfig<TConfig>(string key, TConfig config) where TConfig : class
        {
            IConfigValidator validator = config as IConfigValidator;
            if (validator == null)
            {
                return true;
            }

            string error;
            if (validator.Validate(out error))
            {
                return true;
            }

            FrameLog.Warning("Config validation failed: " + key + " type=" + typeof(TConfig).Name + " error=" + error);
            return false;
        }
    }
}

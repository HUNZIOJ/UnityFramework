using System.Collections.Generic;
using Frame.Core;

namespace Frame.Config
{
    public sealed class ConfigService : GameModuleBase, IConfigService
    {
        private readonly List<IConfigProvider> providers = new List<IConfigProvider>();

        public override int Priority
        {
            get { return -200; }
        }

        protected override void OnInitialize()
        {
            providers.Add(new ResourcesScriptableConfigProvider());
            providers.Add(new ResourcesJsonConfigProvider());
            Context.Services.Register<IConfigService>(this);
            Context.Services.Register(this);
        }

        public void RegisterProvider(IConfigProvider provider)
        {
            if (provider != null && !providers.Contains(provider))
            {
                providers.Insert(0, provider);
            }
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
            for (int i = 0; i < providers.Count; i++)
            {
                if (providers[i].TryLoad(key, out config))
                {
                    return true;
                }
            }

            config = null;
            return false;
        }

        protected override void OnShutdown()
        {
            providers.Clear();
        }
    }
}

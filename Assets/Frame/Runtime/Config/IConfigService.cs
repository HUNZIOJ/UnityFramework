namespace Frame.Config
{
    public interface IConfigService
    {
        bool CacheEnabled { get; set; }

        void RegisterProvider(IConfigProvider provider);

        bool UnregisterProvider(IConfigProvider provider);

        void ClearCache();

        TConfig Load<TConfig>(string key) where TConfig : class;

        bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class;
    }
}

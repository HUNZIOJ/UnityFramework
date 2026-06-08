namespace Frame.Config
{
    public interface IConfigService
    {
        void RegisterProvider(IConfigProvider provider);

        TConfig Load<TConfig>(string key) where TConfig : class;

        bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class;
    }
}

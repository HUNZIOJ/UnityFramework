namespace Frame.Config
{
    public interface IConfigProvider
    {
        bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class;
    }
}

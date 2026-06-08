namespace Frame.Localization
{
    public interface ILocalizationService
    {
        string CurrentLocale { get; }

        void SetLocale(string locale);

        void AddTable(LocalizedTextTable table);

        string Translate(string key, string fallback = null);
    }
}

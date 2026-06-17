using System;
using System.Collections.Generic;

namespace Frame.Localization
{
    public interface ILocalizationService
    {
        event Action<string> LocaleChanged;

        string CurrentLocale { get; }

        string FallbackLocale { get; set; }

        IReadOnlyCollection<string> MissingKeys { get; }

        void SetLocale(string locale);

        void AddTable(LocalizedTextTable table);

        bool RemoveTable(LocalizedTextTable table);

        void ClearTables();

        void ClearMissingKeys();

        bool TryTranslate(string key, out string value);

        string Translate(string key, string fallback = null, params object[] args);
    }
}

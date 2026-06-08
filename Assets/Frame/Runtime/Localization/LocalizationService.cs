using System.Collections.Generic;
using Frame.Core;

namespace Frame.Localization
{
    public sealed class LocalizationService : GameModuleBase, ILocalizationService
    {
        private readonly Dictionary<string, LocalizedTextTable> tables = new Dictionary<string, LocalizedTextTable>();
        private string currentLocale = "en";

        public string CurrentLocale
        {
            get { return currentLocale; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<ILocalizationService>(this);
            Context.Services.Register(this);
        }

        public void SetLocale(string locale)
        {
            if (!string.IsNullOrWhiteSpace(locale))
            {
                currentLocale = locale;
            }
        }

        public void AddTable(LocalizedTextTable table)
        {
            if (table != null)
            {
                tables[table.Locale] = table;
            }
        }

        public string Translate(string key, string fallback = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            LocalizedTextTable table;
            string value;
            if (tables.TryGetValue(currentLocale, out table) && table.TryGet(key, out value))
            {
                return value;
            }

            return fallback == null ? key : fallback;
        }

        protected override void OnShutdown()
        {
            tables.Clear();
            currentLocale = "en";
        }
    }
}

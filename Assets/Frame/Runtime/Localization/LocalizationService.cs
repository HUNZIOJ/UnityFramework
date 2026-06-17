using System;
using System.Collections.Generic;
using Frame.Core;

namespace Frame.Localization
{
    public sealed class LocalizationService : GameModuleBase, ILocalizationService
    {
        private readonly List<LocalizedTextTable> tables = new List<LocalizedTextTable>();
        private readonly HashSet<string> missingKeys = new HashSet<string>();
        private string currentLocale = "en";
        private string fallbackLocale = "en";

        public event Action<string> LocaleChanged;

        public string CurrentLocale
        {
            get { return currentLocale; }
        }

        public string FallbackLocale
        {
            get { return fallbackLocale; }
            set { fallbackLocale = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        public IReadOnlyCollection<string> MissingKeys
        {
            get { return missingKeys; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<ILocalizationService>(this);
            Context.Services.Register(this);
        }

        public void SetLocale(string locale)
        {
            locale = string.IsNullOrWhiteSpace(locale) ? null : locale.Trim();
            if (string.IsNullOrWhiteSpace(locale) || currentLocale == locale)
            {
                return;
            }

            currentLocale = locale;
            Action<string> handler = LocaleChanged;
            if (handler != null)
            {
                try
                {
                    handler(currentLocale);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }
            }
        }

        public void AddTable(LocalizedTextTable table)
        {
            if (table == null)
            {
                return;
            }

            tables.Remove(table);
            tables.Add(table);
        }

        public bool RemoveTable(LocalizedTextTable table)
        {
            return table != null && tables.Remove(table);
        }

        public void ClearTables()
        {
            tables.Clear();
        }

        public void ClearMissingKeys()
        {
            missingKeys.Clear();
        }

        public bool TryTranslate(string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            if (TryTranslate(currentLocale, key, out value))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(fallbackLocale) && fallbackLocale != currentLocale)
            {
                return TryTranslate(fallbackLocale, key, out value);
            }

            return false;
        }

        public string Translate(string key, string fallback = null, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return ApplyFormat(fallback, args);
            }

            string value;
            if (TryTranslate(key, out value))
            {
                return ApplyFormat(value, args);
            }

            missingKeys.Add(key);
            return ApplyFormat(fallback == null ? key : fallback, args);
        }

        protected override void OnShutdown()
        {
            tables.Clear();
            missingKeys.Clear();
            currentLocale = "en";
            fallbackLocale = "en";
            LocaleChanged = null;
        }

        private bool TryTranslate(string locale, string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(locale))
            {
                return false;
            }

            for (int i = tables.Count - 1; i >= 0; i--)
            {
                if (tables[i] != null && tables[i].TryGet(locale, key, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ApplyFormat(string template, object[] args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(template, args);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                return template;
            }
        }
    }
}

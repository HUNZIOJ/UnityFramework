using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Frame.Localization
{
    [CreateAssetMenu(menuName = "Frame/Localized Spreadsheet Table", fileName = "LocalizedTextTable")]
    public sealed class LocalizedTextTable : ScriptableObject
    {
        [Header("Spreadsheet Source")]
        [SerializeField] private TextAsset source;
        [SerializeField] private string delimiter = ",";

        [Header("Manual Fallback")]
        [SerializeField] private List<string> locales = new List<string> { "en" };
        [SerializeField] private List<Entry> entries = new List<Entry>();

        private readonly List<string> availableLocales = new List<string>();
        private Dictionary<string, Dictionary<string, string>> lookup;
        private bool lookupDirty = true;

        public string Locale
        {
            get { return Locales.Count > 0 ? Locales[0] : "en"; }
        }

        public IReadOnlyList<string> Locales
        {
            get
            {
                EnsureLookup();
                return availableLocales;
            }
        }

        public bool TryGet(string locale, string key, out string value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            EnsureLookup();
            Dictionary<string, string> localeLookup;
            return lookup.TryGetValue(locale, out localeLookup) && localeLookup.TryGetValue(key, out value);
        }

        public bool TryGet(string key, out string value)
        {
            return TryGet(Locale, key, out value);
        }

        public bool ContainsLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                return false;
            }

            EnsureLookup();
            return lookup.ContainsKey(locale);
        }

        public void Clear()
        {
            source = null;
            locales.Clear();
            entries.Clear();
            MarkDirty();
        }

        public void SetLocales(params string[] localeCodes)
        {
            locales.Clear();
            if (localeCodes != null)
            {
                for (int i = 0; i < localeCodes.Length; i++)
                {
                    AddLocaleIfMissing(CleanKey(localeCodes[i]));
                }
            }

            NormalizeEntryValueCounts();
            MarkDirty();
        }

        public void SetValue(string key, string locale, string value)
        {
            key = CleanKey(key);
            locale = CleanKey(locale);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(locale))
            {
                return;
            }

            int localeIndex = AddLocaleIfMissing(locale);
            Entry entry = FindOrCreateEntry(key);
            EnsureValueCount(entry.Values, locales.Count);
            entry.Values[localeIndex] = value ?? string.Empty;
            MarkDirty();
        }

        public void ImportCsv(string csv)
        {
            ImportDelimitedText(csv, ',');
        }

        public void ImportTsv(string tsv)
        {
            ImportDelimitedText(tsv, '\t');
        }

        public void ImportDelimitedText(string text, char textDelimiter)
        {
            source = null;
            delimiter = textDelimiter == '\t' ? "\\t" : textDelimiter.ToString();
            locales.Clear();
            entries.Clear();

            List<List<string>> rows = ParseDelimited(text, textDelimiter);
            ApplyRows(rows);
            MarkDirty();
        }

        public void SetSource(TextAsset textAsset, string textDelimiter = ",")
        {
            source = textAsset;
            delimiter = string.IsNullOrEmpty(textDelimiter) ? "," : textDelimiter;
            MarkDirty();
        }

        private void OnEnable()
        {
            lookupDirty = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            lookupDirty = true;
        }
#endif

        private void EnsureLookup()
        {
            if (!lookupDirty && lookup != null)
            {
                return;
            }

            if (lookup == null)
            {
                lookup = new Dictionary<string, Dictionary<string, string>>();
            }
            else
            {
                lookup.Clear();
            }

            availableLocales.Clear();
            if (source != null && !string.IsNullOrEmpty(source.text))
            {
                BuildLookupFromRows(ParseDelimited(source.text, ResolveDelimiter()));
            }
            else
            {
                BuildLookupFromSerializedEntries();
            }

            lookupDirty = false;
        }

        private void BuildLookupFromSerializedEntries()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                for (int j = 0; j < locales.Count; j++)
                {
                    string locale = CleanKey(locales[j]);
                    if (string.IsNullOrWhiteSpace(locale))
                    {
                        continue;
                    }

                    string value = j < entry.Values.Count ? entry.Values[j] : string.Empty;
                    AddLookupValue(locale, entry.Key, value);
                }
            }
        }

        private void BuildLookupFromRows(List<List<string>> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            List<string> header = rows[0];
            if (header.Count < 2)
            {
                return;
            }

            List<string> rowLocales = new List<string>();
            for (int i = 1; i < header.Count; i++)
            {
                rowLocales.Add(CleanKey(header[i]));
            }

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.Count == 0)
                {
                    continue;
                }

                string key = CleanKey(row[0]);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                for (int columnIndex = 1; columnIndex < header.Count; columnIndex++)
                {
                    string locale = rowLocales[columnIndex - 1];
                    if (string.IsNullOrWhiteSpace(locale))
                    {
                        continue;
                    }

                    string value = columnIndex < row.Count ? row[columnIndex] : string.Empty;
                    AddLookupValue(locale, key, value);
                }
            }
        }

        private void ApplyRows(List<List<string>> rows)
        {
            if (rows.Count == 0 || rows[0].Count < 2)
            {
                return;
            }

            List<string> header = rows[0];
            for (int i = 1; i < header.Count; i++)
            {
                AddLocaleIfMissing(CleanKey(header[i]));
            }

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.Count == 0)
                {
                    continue;
                }

                string key = CleanKey(row[0]);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                Entry entry = FindOrCreateEntry(key);
                EnsureValueCount(entry.Values, locales.Count);
                for (int columnIndex = 1; columnIndex < row.Count && columnIndex <= locales.Count; columnIndex++)
                {
                    entry.Values[columnIndex - 1] = row[columnIndex];
                }
            }
        }

        private void AddLookupValue(string locale, string key, string value)
        {
            locale = CleanKey(locale);
            key = CleanKey(key);
            if (string.IsNullOrWhiteSpace(locale) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            Dictionary<string, string> localeLookup;
            if (!lookup.TryGetValue(locale, out localeLookup))
            {
                localeLookup = new Dictionary<string, string>();
                lookup.Add(locale, localeLookup);
                availableLocales.Add(locale);
            }

            localeLookup[key] = value ?? string.Empty;
        }

        private int AddLocaleIfMissing(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                return -1;
            }

            for (int i = 0; i < locales.Count; i++)
            {
                if (locales[i] == locale)
                {
                    return i;
                }
            }

            locales.Add(locale);
            return locales.Count - 1;
        }

        private Entry FindOrCreateEntry(string key)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].Key == key)
                {
                    return entries[i];
                }
            }

            Entry entry = new Entry { Key = key };
            entries.Add(entry);
            return entry;
        }

        private void NormalizeEntryValueCounts()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null)
                {
                    EnsureValueCount(entries[i].Values, locales.Count);
                }
            }
        }

        private static void EnsureValueCount(List<string> values, int count)
        {
            while (values.Count < count)
            {
                values.Add(string.Empty);
            }
        }

        private char ResolveDelimiter()
        {
            if (delimiter == "\\t")
            {
                return '\t';
            }

            return string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        }

        private void MarkDirty()
        {
            lookupDirty = true;
        }

        private static string CleanKey(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            return value.Trim().TrimStart('\uFEFF');
        }

        private static List<List<string>> ParseDelimited(string text, char textDelimiter)
        {
            List<List<string>> rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text))
            {
                return rows;
            }

            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '"')
                {
                    if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (character == textDelimiter && !inQuotes)
                {
                    row.Add(field.ToString());
                    field.Length = 0;
                    continue;
                }

                if ((character == '\r' || character == '\n') && !inQuotes)
                {
                    if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    row.Add(field.ToString());
                    field.Length = 0;
                    AddRowIfNotEmpty(rows, row);
                    row = new List<string>();
                    continue;
                }

                field.Append(character);
            }

            row.Add(field.ToString());
            AddRowIfNotEmpty(rows, row);
            return rows;
        }

        private static void AddRowIfNotEmpty(List<List<string>> rows, List<string> row)
        {
            for (int i = 0; i < row.Count; i++)
            {
                if (!string.IsNullOrEmpty(row[i]))
                {
                    rows.Add(row);
                    return;
                }
            }
        }

        [Serializable]
        private sealed class Entry
        {
            public string Key = "";
            public List<string> Values = new List<string>();
        }
    }
}

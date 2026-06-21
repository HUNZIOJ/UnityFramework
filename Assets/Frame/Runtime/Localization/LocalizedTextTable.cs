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
        [SerializeField, HideInInspector] private string importedText = "";

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
            importedText = "";
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
            importedText = text ?? string.Empty;
            MarkDirty();
        }

        public void SetSource(TextAsset textAsset, string textDelimiter = ",")
        {
            source = textAsset;
            importedText = "";
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
            string tableText = source != null && !string.IsNullOrEmpty(source.text) ? source.text : importedText;
            if (!string.IsNullOrEmpty(tableText))
            {
                BuildLookupFromRows(ParseDelimited(tableText, ResolveDelimiter()));
            }

            lookupDirty = false;
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
    }
}

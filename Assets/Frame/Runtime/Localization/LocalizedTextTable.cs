using System;
using System.Collections.Generic;
using UnityEngine;

namespace Frame.Localization
{
    [CreateAssetMenu(menuName = "Frame/Localized Text Table", fileName = "LocalizedTextTable")]
    public sealed class LocalizedTextTable : ScriptableObject
    {
        [SerializeField] private string locale = "en";
        [SerializeField] private List<Entry> entries = new List<Entry>();

        public string Locale
        {
            get { return string.IsNullOrWhiteSpace(locale) ? "en" : locale; }
        }

        public bool TryGet(string key, out string value)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Key == key)
                {
                    value = entries[i].Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        [Serializable]
        private sealed class Entry
        {
            public string Key = "";
            [TextArea] public string Value = "";
        }
    }
}

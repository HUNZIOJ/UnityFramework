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

        private Dictionary<string, string> lookup;
        private bool lookupDirty = true;

        public string Locale
        {
            get { return string.IsNullOrWhiteSpace(locale) ? "en" : locale; }
        }

        public bool TryGet(string key, out string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                value = null;
                return false;
            }

            EnsureLookup();
            return lookup.TryGetValue(key, out value);
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
                lookup = new Dictionary<string, string>();
            }
            else
            {
                lookup.Clear();
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                lookup[entry.Key] = entry.Value;
            }

            lookupDirty = false;
        }

        [Serializable]
        private sealed class Entry
        {
            public string Key = "";
            [TextArea] public string Value = "";
        }
    }
}

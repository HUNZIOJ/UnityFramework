using System;
using System.Collections.Generic;
using Frame.Core;
using Frame.Utilities;
using Newtonsoft.Json;

namespace Frame.Config
{
    public sealed class RuntimeJsonConfigProvider : IConfigProvider, IConfigChangeNotifier
    {
        private readonly Dictionary<string, string> jsonByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public event Action Changed;

        public int Count
        {
            get { return jsonByKey.Count; }
        }

        public IEnumerable<string> Keys
        {
            get { return jsonByKey.Keys; }
        }

        public void SetJson(string key, string json)
        {
            string normalizedKey = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                throw new ArgumentException("Config key is required.", "key");
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                Remove(normalizedKey);
                return;
            }

            jsonByKey[normalizedKey] = json;
            RaiseChanged();
        }

        public void Set<TConfig>(string key, TConfig config) where TConfig : class
        {
            if (config == null)
            {
                Remove(key);
                return;
            }

            SetJson(key, JsonConvert.SerializeObject(config));
        }

        public bool Remove(string key)
        {
            string normalizedKey = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                return false;
            }

            bool removed = jsonByKey.Remove(normalizedKey);
            if (removed)
            {
                RaiseChanged();
            }

            return removed;
        }

        public void Clear()
        {
            if (jsonByKey.Count == 0)
            {
                return;
            }

            jsonByKey.Clear();
            RaiseChanged();
        }

        public bool Contains(string key)
        {
            string normalizedKey = NormalizeKey(key);
            return !string.IsNullOrWhiteSpace(normalizedKey) && jsonByKey.ContainsKey(normalizedKey);
        }

        public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
        {
            string normalizedKey = NormalizeKey(key);
            string json;
            if (string.IsNullOrWhiteSpace(normalizedKey) || !jsonByKey.TryGetValue(normalizedKey, out json))
            {
                config = null;
                return false;
            }

            try
            {
                config = JsonConvert.DeserializeObject<TConfig>(json);
                return config != null;
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                config = null;
                return false;
            }
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private static string NormalizeKey(string key)
        {
            return FramePathUtility.NormalizeResourcesPath(key);
        }
    }
}

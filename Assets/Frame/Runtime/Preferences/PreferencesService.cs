using System;
using Frame.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace Frame.Preferences
{
    public sealed class PreferencesService : GameModuleBase, IPreferencesService
    {
        public event Action<string> Changed;

        public override int Priority
        {
            get { return -850; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IPreferencesService>(this);
            Context.Services.Register(this);
        }

        public bool HasKey(string key)
        {
            return !string.IsNullOrWhiteSpace(key) && PlayerPrefs.HasKey(key);
        }

        public int GetInt(string key, int fallback = 0)
        {
            return HasKey(key) ? PlayerPrefs.GetInt(key, fallback) : fallback;
        }

        public void SetInt(string key, int value)
        {
            ValidateKey(key);
            PlayerPrefs.SetInt(key, value);
            RaiseChanged(key);
        }

        public float GetFloat(string key, float fallback = 0f)
        {
            return HasKey(key) ? PlayerPrefs.GetFloat(key, fallback) : fallback;
        }

        public void SetFloat(string key, float value)
        {
            ValidateKey(key);
            PlayerPrefs.SetFloat(key, value);
            RaiseChanged(key);
        }

        public string GetString(string key, string fallback = null)
        {
            return HasKey(key) ? PlayerPrefs.GetString(key, fallback) : fallback;
        }

        public void SetString(string key, string value)
        {
            ValidateKey(key);
            PlayerPrefs.SetString(key, value ?? string.Empty);
            RaiseChanged(key);
        }

        public bool GetBool(string key, bool fallback = false)
        {
            return GetInt(key, fallback ? 1 : 0) != 0;
        }

        public void SetBool(string key, bool value)
        {
            SetInt(key, value ? 1 : 0);
        }

        public TData GetJson<TData>(string key, TData fallback = default(TData))
        {
            TData value;
            return TryGetJson(key, out value) ? value : fallback;
        }

        public bool TryGetJson<TData>(string key, out TData value)
        {
            string json = GetString(key, null);
            if (string.IsNullOrWhiteSpace(json))
            {
                value = default(TData);
                return false;
            }

            try
            {
                value = JsonConvert.DeserializeObject<TData>(json);
                return value != null;
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                value = default(TData);
                return false;
            }
        }

        public void SetJson<TData>(string key, TData value)
        {
            ValidateKey(key);
            string json = JsonConvert.SerializeObject(value);
            PlayerPrefs.SetString(key, json);
            RaiseChanged(key);
        }

        public bool DeleteKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !PlayerPrefs.HasKey(key))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(key);
            RaiseChanged(key);
            return true;
        }

        public void Save()
        {
            PlayerPrefs.Save();
        }

        protected override void OnShutdown()
        {
            Save();
            Changed = null;
        }

        private void RaiseChanged(string key)
        {
            Action<string> handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(key);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Preference key is required.", "key");
            }
        }
    }
}

using System;

namespace Frame.Preferences
{
    public interface IPreferencesService
    {
        event Action<string> Changed;

        bool HasKey(string key);

        int GetInt(string key, int fallback = 0);

        void SetInt(string key, int value);

        float GetFloat(string key, float fallback = 0f);

        void SetFloat(string key, float value);

        string GetString(string key, string fallback = null);

        void SetString(string key, string value);

        bool GetBool(string key, bool fallback = false);

        void SetBool(string key, bool value);

        TData GetJson<TData>(string key, TData fallback = default(TData));

        bool TryGetJson<TData>(string key, out TData value);

        void SetJson<TData>(string key, TData value);

        bool DeleteKey(string key);

        void Save();
    }
}

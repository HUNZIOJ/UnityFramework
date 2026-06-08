using UnityEngine;

namespace Frame.Save
{
    public sealed class JsonUtilitySaveSerializer : ISaveSerializer
    {
        public string Serialize<TData>(TData data)
        {
            return JsonUtility.ToJson(data, true);
        }

        public TData Deserialize<TData>(string text)
        {
            return JsonUtility.FromJson<TData>(text);
        }
    }
}

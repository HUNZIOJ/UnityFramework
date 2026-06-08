using System;
using Frame.Core;
using Frame.Utilities;
using Newtonsoft.Json;
using UnityEngine;

namespace Frame.Config
{
    public sealed class ResourcesJsonConfigProvider : IConfigProvider
    {
        private readonly string rootPath;

        public ResourcesJsonConfigProvider(string rootPath = "Configs")
        {
            this.rootPath = FramePathUtility.NormalizeResourcesPath(rootPath);
        }

        public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
        {
            string path = string.IsNullOrWhiteSpace(rootPath) ? key : rootPath + "/" + key;
            path = FramePathUtility.NormalizeResourcesPath(path);
            TextAsset textAsset = Resources.Load<TextAsset>(path);
            if (textAsset == null)
            {
                config = null;
                return false;
            }

            try
            {
                config = JsonConvert.DeserializeObject<TConfig>(textAsset.text);
                return config != null;
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                config = null;
                return false;
            }
        }
    }
}

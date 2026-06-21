using System;
using Frame.Assets;
using Frame.Core;
using Frame.Utilities;
using Newtonsoft.Json;
using UnityEngine;

namespace Frame.Config
{
    public sealed class AssetJsonConfigProvider : IConfigProvider
    {
        private readonly IAssetService assets;
        private readonly string rootPath;

        public AssetJsonConfigProvider(IAssetService assets, string rootPath = "Configs")
        {
            this.assets = assets;
            this.rootPath = FramePathUtility.NormalizeResourcesPath(rootPath);
        }

        public bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class
        {
            config = null;
            if (assets == null)
            {
                return false;
            }

            string path = ResolvePath(key);
            AssetHandle<TextAsset> handle;
            if (!assets.TryLoad<TextAsset>(path, out handle))
            {
                return false;
            }

            try
            {
                TextAsset textAsset = handle.Asset;
                if (textAsset == null)
                {
                    return false;
                }

                config = JsonConvert.DeserializeObject<TConfig>(textAsset.text);
                return config != null;
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                config = null;
                return false;
            }
            finally
            {
                handle.Release();
            }
        }

        private string ResolvePath(string key)
        {
            string path = string.IsNullOrWhiteSpace(rootPath) ? key : rootPath + "/" + key;
            return FramePathUtility.NormalizeResourcesPath(path);
        }
    }
}

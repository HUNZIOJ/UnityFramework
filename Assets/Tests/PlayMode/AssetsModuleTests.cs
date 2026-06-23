using System;
using System.Collections.Generic;
using Frame.Assets;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Tests.PlayMode
{
    public sealed class AssetsModuleTests
    {
        [Test]
        public void AssetReference_PreservesYooAssetLocation()
        {
            AssetReference<TextAsset> reference = new AssetReference<TextAsset>("Assets/Game/Resources/FrameTests/TextAsset.txt");

            Assert.IsTrue(reference.IsValid);
            Assert.AreEqual("Assets/Game/Resources/FrameTests/TextAsset.txt", reference.Path);
            Assert.AreEqual(reference.Path, reference.ResourcesPath);
            Assert.AreEqual(reference.Path, reference.ToString());
        }

        [Test]
        public void AssetReference_NormalizesSlashesAndPassesLocationToService()
        {
            RecordingAssetService service = new RecordingAssetService();
            AssetReference<TextAsset> reference = new AssetReference<TextAsset>(" UI\\Main.prefab ");

            reference.Load(service);
            reference.LoadAsync(service);

            Assert.AreEqual("UI/Main.prefab", reference.Path);
            CollectionAssert.AreEqual(new[] { "UI/Main.prefab", "UI/Main.prefab" }, service.RequestedPaths);
        }

        private sealed class RecordingAssetService : IAssetService
        {
            public readonly List<string> RequestedPaths = new List<string>();

            public AssetHandle<T> Load<T>(string path) where T : Object
            {
                RequestedPaths.Add(path);
                return null;
            }

            public bool TryLoad<T>(string path, out AssetHandle<T> handle) where T : Object
            {
                RequestedPaths.Add(path);
                handle = null;
                return false;
            }

            public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object
            {
                RequestedPaths.Add(path);
                return null;
            }

            public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)
            {
                RequestedPaths.Add(path);
                return null;
            }

            public bool IsLoaded(string path)
            {
                return false;
            }

            public int GetReferenceCount(string path)
            {
                return 0;
            }

            public List<AssetStats> GetLoadedAssetStats()
            {
                return new List<AssetStats>();
            }

            public void Release(string path)
            {
            }

            public void ReleaseAll()
            {
            }

            public void UnloadUnusedAssets()
            {
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using Frame.Assets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class AssetsModuleTests
    {
        [UnityTest]
        public IEnumerator ResourcesAssetService_LoadsReleasesAndLoadsAsyncResources()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                ResourcesAssetService service = fixture.Initialize(new ResourcesAssetService());

                AssetHandle<TextAsset> handle = service.Load<TextAsset>("FrameTests/TextAsset");
                Assert.IsTrue(handle.IsValid);
                Assert.AreEqual("Frame test resource.", handle.Asset.text.Trim());
                Assert.IsTrue(service.IsLoaded("FrameTests/TextAsset"));
                Assert.AreEqual(1, service.GetReferenceCount("FrameTests/TextAsset"));
                handle.Release();
                handle.Release();
                Assert.IsFalse(service.IsLoaded("FrameTests/TextAsset"));
                Assert.AreEqual(0, service.GetReferenceCount("FrameTests/TextAsset"));

                AssetRequest<TextAsset> request = service.LoadAsync<TextAsset>("FrameTests/TextAsset");
                yield return request;

                Assert.IsTrue(request.IsDone);
                Assert.IsTrue(request.Success);
                Assert.AreEqual(1f, request.Progress);
                Assert.IsNull(request.Error);
                Assert.IsNotNull(request.Asset);
                request.Handle.Release();

                service.UnloadUnusedAssets();
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator AssetReference_NormalizesPathAndLoadsThroughService()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                ResourcesAssetService service = fixture.Initialize(new ResourcesAssetService());
                AssetReference<TextAsset> reference = new AssetReference<TextAsset>("Assets/Tests/PlayMode/Resources/FrameTests/TextAsset.txt");

                Assert.IsTrue(reference.IsValid);
                Assert.AreEqual("FrameTests/TextAsset", reference.ResourcesPath);

                using (AssetHandle<TextAsset> handle = reference.Load(service))
                {
                    Assert.IsTrue(handle.IsValid);
                }

                AssetRequest<TextAsset> request = reference.LoadAsync(service);
                yield return request;

                Assert.IsTrue(request.Success);
                request.Handle.Release();

                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator ResourcesAssetService_InvalidPathsCompleteWithInvalidHandles()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                ResourcesAssetService service = fixture.Initialize(new ResourcesAssetService());

                LogAssert.Expect(LogType.Warning, "[Frame] Resources asset not found: missing_asset type=TextAsset");
                using (AssetHandle<TextAsset> handle = service.Load<TextAsset>("missing_asset"))
                {
                    Assert.IsFalse(handle.IsValid);
                }

                AssetRequest<TextAsset> request = service.LoadAsync<TextAsset>("");
                yield return request;

                Assert.IsTrue(request.IsDone);
                Assert.IsFalse(request.Success);
                Assert.AreEqual("Resources path is empty.", request.Error);
                Assert.IsNull(request.Asset);
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator ResourcesAssetService_CanCancelAsyncRequest()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                ResourcesAssetService service = fixture.Initialize(new ResourcesAssetService());

                AssetRequest<TextAsset> request = service.LoadAsync<TextAsset>("FrameTests/TextAsset");
                request.Cancel();
                yield return request;

                Assert.IsTrue(request.IsDone);
                Assert.IsTrue(request.IsCanceled);
                Assert.IsFalse(request.Success);
                Assert.AreEqual("Request canceled.", request.Error);
                Assert.IsFalse(service.IsLoaded("FrameTests/TextAsset"));
                Assert.AreEqual(0, service.GetReferenceCount("FrameTests/TextAsset"));

                service.Shutdown();
            }
        }

        [Test]
        public void ResourcesAssetService_ReleaseAllClearsCacheAndReferenceCounts()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                ResourcesAssetService service = fixture.Initialize(new ResourcesAssetService());
                AssetHandle<TextAsset> first = service.Load<TextAsset>("FrameTests/TextAsset");
                AssetHandle<TextAsset> second = service.Load<TextAsset>("FrameTests/TextAsset");

                Assert.AreEqual(2, service.GetReferenceCount("FrameTests/TextAsset"));
                service.ReleaseAll();
                Assert.IsFalse(service.IsLoaded("FrameTests/TextAsset"));
                Assert.AreEqual(0, service.GetReferenceCount("FrameTests/TextAsset"));

                first.Release();
                second.Release();
                service.Shutdown();
            }
        }

        [Test]
        public void ResourcesAssetService_ReturnsLoadedAssetStatsSnapshot()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                ResourcesAssetService service = fixture.Initialize(new ResourcesAssetService());
                AssetHandle<TextAsset> first = service.Load<TextAsset>("FrameTests/TextAsset");
                AssetHandle<TextAsset> second = service.Load<TextAsset>("FrameTests/TextAsset");

                List<AssetStats> stats = service.GetLoadedAssetStats();
                Assert.AreEqual(1, stats.Count);
                Assert.AreEqual("FrameTests/TextAsset", stats[0].Path);
                Assert.AreEqual("TextAsset", stats[0].TypeName);
                Assert.AreEqual(2, stats[0].ReferenceCount);
                Assert.IsTrue(stats[0].IsLoaded);

                first.Release();
                second.Release();
                Assert.AreEqual(0, service.GetLoadedAssetStats().Count);
                service.Shutdown();
            }
        }
    }
}

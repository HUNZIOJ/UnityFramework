using System.Collections;
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
                handle.Release();
                handle.Release();

                AssetRequest<TextAsset> request = service.LoadAsync<TextAsset>("FrameTests/TextAsset");
                yield return request;

                Assert.IsTrue(request.IsDone);
                Assert.IsNotNull(request.Asset);
                request.Handle.Release();

                service.UnloadUnusedAssets();
                service.Shutdown();
            }
        }

        [Test]
        public void AssetReference_NormalizesPathAndLoadsThroughService()
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
                Assert.IsNull(request.Asset);
                service.Shutdown();
            }
        }
    }
}

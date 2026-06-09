using Frame.Assets;
using Frame.UI;
using NUnit.Framework;
using UnityEngine;

namespace Frame.Tests.PlayMode
{
    public sealed class UIModuleTests
    {
        [Test]
        public void UIService_CreatesRootLayersOpensCachesClosesAndDestroysPanels()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                FakeAssetService assets = new FakeAssetService();
                fixture.Services.Register<IAssetService>(assets);
                UIService service = fixture.Initialize(new UIService());

                Assert.IsNotNull(service.Root);
                Assert.IsNotNull(service.Root.GetLayer(UILayer.Normal));
                Assert.IsNotNull(service.Root.GetLayer(UILayer.Popup));

                TestPanel panel = service.Open<TestPanel>("UI/TestPanel", UILayer.Popup, "args", cache: true);
                Assert.IsNotNull(panel);
                Assert.IsTrue(panel.IsOpen);
                Assert.AreEqual(1, panel.CreatedCount);
                Assert.AreEqual(1, panel.OpenedCount);
                Assert.AreEqual("args", panel.LastArgs);
                Assert.AreEqual(UILayer.Popup, panel.Context.Layer);

                TestPanel cached = service.Open<TestPanel>("UI/TestPanel", UILayer.Popup, "second", cache: true);
                Assert.AreSame(panel, cached);
                Assert.AreEqual(2, panel.OpenedCount);
                Assert.AreEqual("second", panel.LastArgs);

                service.Close(panel);
                Assert.IsFalse(panel.IsOpen);
                Assert.AreEqual(1, panel.ClosedCount);

                service.Close(panel, destroy: true);
                Assert.AreEqual(1, panel.DisposedCount);

                TestPanel noCacheA = service.Open<TestPanel>("UI/TestPanel", UILayer.Normal, null, cache: false);
                TestPanel noCacheB = service.Open<TestPanel>("UI/TestPanel", UILayer.Normal, null, cache: false);
                Assert.AreNotSame(noCacheA, noCacheB);

                service.CloseTop(destroy: true);
                service.CloseAll(destroy: true);
                service.Shutdown();
            }
        }

        [Test]
        public void SafeAreaFitter_AddsRectTransformAndAppliesSafeAreaAnchors()
        {
            GameObject go = new GameObject("SafeArea", typeof(RectTransform), typeof(SafeAreaFitter));
            try
            {
                SafeAreaFitter fitter = go.GetComponent<SafeAreaFitter>();
                Assert.IsNotNull(fitter);
                RectTransform rect = go.GetComponent<RectTransform>();
                Assert.GreaterOrEqual(rect.anchorMin.x, 0f);
                Assert.LessOrEqual(rect.anchorMax.x, 1f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class FakeAssetService : IAssetService
        {
            public AssetHandle<T> Load<T>(string path) where T : Object
            {
                throw new System.NotSupportedException();
            }

            public AssetRequest<T> LoadAsync<T>(string path, System.Action<AssetHandle<T>> completed = null) where T : Object
            {
                return new AssetRequest<T>();
            }

            public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)
            {
                GameObject go = new GameObject(path, typeof(RectTransform), typeof(TestPanel));
                go.transform.SetParent(parent, worldPositionStays);
                return go;
            }

            public void Release(string path)
            {
            }

            public void UnloadUnusedAssets()
            {
            }
        }

        public sealed class TestPanel : UIPanelBase
        {
            public int CreatedCount;
            public int OpenedCount;
            public int ClosedCount;
            public int DisposedCount;
            public object LastArgs;

            protected override void OnCreate()
            {
                CreatedCount++;
            }

            protected override void OnOpen(object args)
            {
                OpenedCount++;
                LastArgs = args;
            }

            protected override void OnClose()
            {
                ClosedCount++;
            }

            protected override void OnDispose()
            {
                DisposedCount++;
            }
        }
    }
}

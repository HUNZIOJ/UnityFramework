using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Frame.Assets;
using Frame.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
        public void UIService_RoutesModalPanelsAndBackClosesTopAllowedPanel()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                FakeAssetService assets = new FakeAssetService();
                fixture.Services.Register<IAssetService>(assets);
                UIService service = fixture.Initialize(new UIService());

                service.RegisterRoute<TypedPanel>("settings", "UI/TypedPanel", UILayer.Popup, cache: true, modal: true, closeOnBackdrop: true);
                TypedArgs args = new TypedArgs { Title = "Settings", Value = 12 };

                TypedPanel panel = service.OpenRoute<TypedPanel, TypedArgs>("settings", args);

                Assert.IsNotNull(panel);
                Assert.IsTrue(panel.IsOpen);
                Assert.AreEqual("settings", panel.Context.Route);
                Assert.AreEqual("Settings", panel.LastArgs.Title);
                Assert.AreEqual(12, panel.LastArgs.Value);
                Assert.IsTrue(panel.Context.IsModal);
                Assert.IsNotNull(panel.Context.ModalBlocker);

                Assert.IsTrue(service.Back());

                Assert.IsFalse(panel.IsOpen);
                Assert.IsNull(panel.Context.ModalBlocker);
            }
        }

        [Test]
        public void UIService_OpenAsyncCompletesPanelRequest()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                FakeAssetService assets = new FakeAssetService();
                fixture.Services.Register<IAssetService>(assets);
                UIService service = fixture.Initialize(new UIService());

                UIPanelRequest<TestPanel> request = service.OpenAsync<TestPanel>("UI/TestPanel", UILayer.Normal, "async", cache: false);

                Assert.IsTrue(request.IsDone);
                Assert.IsTrue(request.Success);
                Assert.IsNotNull(request.Panel);
                Assert.AreEqual("async", request.Panel.LastArgs);
            }
        }

        [Test]
        public void UIService_QueuedRoutesOpenSequentiallyAndCanBeCleared()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                FakeAssetService assets = new FakeAssetService();
                fixture.Services.Register<IAssetService>(assets);
                UIService service = fixture.Initialize(new UIService());
                service.RegisterRoute<TestPanel>("reward", "UI/TestPanel", UILayer.Popup, cache: false, modal: true);

                UIPanelRequest<TestPanel> first = service.EnqueueRoute<TestPanel>("reward", "first");
                Assert.IsTrue(first.IsDone);
                Assert.IsTrue(first.Success);
                Assert.AreEqual("first", first.Panel.LastArgs);

                UIPanelRequest<TestPanel> second = service.EnqueueRoute<TestPanel>("reward", "second");
                Assert.IsFalse(second.IsDone);
                Assert.AreEqual(1, service.QueuedPanelCount);

                service.Close(first.Panel, destroy: true);

                Assert.IsTrue(second.IsDone);
                Assert.IsTrue(second.Success);
                Assert.AreEqual("second", second.Panel.LastArgs);
                Assert.AreEqual(0, service.QueuedPanelCount);

                UIPanelRequest<TestPanel> third = service.EnqueueRoute<TestPanel>("reward", "third");
                Assert.IsFalse(third.IsDone);
                service.ClearQueuedPanels();

                Assert.IsTrue(third.IsDone);
                Assert.IsFalse(third.Success);
                Assert.AreEqual("UI panel queue was cleared.", third.Error);

                service.Close(second.Panel, destroy: true);
                service.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator UIService_FadeTransitionRunsForOpenAndClose()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                FakeAssetService assets = new FakeAssetService();
                fixture.Services.Register<IAssetService>(assets);
                UIService service = fixture.Initialize(new UIService());
                UIOpenOptions options = UIOpenOptions.Default();
                options.Transition = new UIFadeTransition(0f);

                TestPanel panel = service.Open<TestPanel>("UI/TestPanel", options);
                yield return null;

                CanvasGroup canvasGroup = panel.GetComponent<CanvasGroup>();
                Assert.IsNotNull(canvasGroup);
                Assert.AreEqual(1f, canvasGroup.alpha);

                service.Close(panel);
                yield return null;

                Assert.IsFalse(panel.gameObject.activeSelf);
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
                GameObject prefab = CreatePrefab(path);
                return CreateHandle(path, prefab as T);
            }

            public AssetRequest<T> LoadAsync<T>(string path, System.Action<AssetHandle<T>> completed = null) where T : Object
            {
                AssetRequest<T> request = new AssetRequest<T>();
                AssetHandle<T> handle = Load<T>(path);
                CompleteRequest(request, handle);
                if (completed != null)
                {
                    completed(handle);
                }

                return request;
            }

            public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)
            {
                GameObject go = CreatePrefab(path);
                go.transform.SetParent(parent, worldPositionStays);
                return go;
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

            private static GameObject CreatePrefab(string path)
            {
                if (path.Contains("Typed"))
                {
                    return new GameObject(path, typeof(RectTransform), typeof(TypedPanel));
                }

                return new GameObject(path, typeof(RectTransform), typeof(TestPanel));
            }

            private AssetHandle<T> CreateHandle<T>(string path, T asset) where T : Object
            {
                ConstructorInfo constructor = typeof(AssetHandle<T>).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(IAssetService), typeof(string), typeof(T) }, null);
                return (AssetHandle<T>)constructor.Invoke(new object[] { this, path, asset });
            }

            private static void CompleteRequest<T>(AssetRequest<T> request, AssetHandle<T> handle) where T : Object
            {
                MethodInfo method = typeof(AssetRequest<T>).GetMethod("Complete", BindingFlags.Instance | BindingFlags.NonPublic);
                method.Invoke(request, new object[] { handle, null });
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

        public sealed class TypedArgs
        {
            public string Title;
            public int Value;
        }

        public sealed class TypedPanel : UIPanelBase<TypedArgs>
        {
            public TypedArgs LastArgs;

            protected override void OnOpen(TypedArgs args)
            {
                LastArgs = args;
            }
        }
    }
}

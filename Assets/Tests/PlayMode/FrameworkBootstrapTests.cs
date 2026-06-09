using System.Collections;
using Frame.Assets;
using Frame.Audio;
using Frame.Config;
using Frame.Core;
using Frame.Events;
using Frame.Localization;
using Frame.Networking;
using Frame.Pooling;
using Frame.Save;
using Frame.Scenes;
using Frame.Timing;
using Frame.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class FrameworkBootstrapTests
    {
        [UnitySetUp]
        public IEnumerator CleanupBefore()
        {
            yield return CleanupFramework();
        }

        [UnityTearDown]
        public IEnumerator CleanupAfter()
        {
            yield return CleanupFramework();
        }

        [UnityTest]
        public IEnumerator Framework_EnsureInitializesDefaultServicesAndShutdownClearsState()
        {
            FrameSettings settings = ScriptableObject.CreateInstance<FrameSettings>();
            GameEntry entry = GameEntry.Ensure(settings);
            yield return null;

            Assert.IsNotNull(entry);
            Assert.IsTrue(Framework.IsInitialized);
            Assert.IsTrue(Framework.TryResolve(out IEventBus _));
            Assert.IsTrue(Framework.TryResolve(out ITimerService _));
            Assert.IsTrue(Framework.TryResolve(out IPoolService _));
            Assert.IsTrue(Framework.TryResolve(out IAssetService _));
            Assert.IsTrue(Framework.TryResolve(out ISceneService _));
            Assert.IsTrue(Framework.TryResolve(out IUIService _));
            Assert.IsTrue(Framework.TryResolve(out IAudioService _));
            Assert.IsTrue(Framework.TryResolve(out ISaveService _));
            Assert.IsTrue(Framework.TryResolve(out IConfigService _));
            Assert.IsTrue(Framework.TryResolve(out IHttpService _));
            Assert.IsTrue(Framework.TryResolve(out ILocalizationService _));

            Framework.Start();
            Framework.Update(0.016f, 0.016f);
            Framework.FixedUpdate(0.02f, 0.02f);
            Framework.LateUpdate(0.016f, 0.016f);
            Framework.OnApplicationPause(false);
            Framework.OnApplicationFocus(true);
            Framework.Shutdown();

            Assert.IsFalse(Framework.IsInitialized);
            Assert.IsFalse(Framework.TryResolve(out IEventBus _));
            Object.Destroy(settings);
        }

        private static IEnumerator CleanupFramework()
        {
            if (Framework.IsInitialized)
            {
                Framework.Shutdown();
            }

            if (GameEntry.Instance != null)
            {
                Object.Destroy(GameEntry.Instance.gameObject);
            }

            yield return null;
        }
    }
}

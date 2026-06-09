using System.Collections;
using Frame.DOTween;
using Frame.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Frame.Tests.PlayMode
{
    public sealed class TweeningModuleTests
    {
        [UnityTest]
        public IEnumerator DOTweenTweenService_TweensValuesTransformsScaleFadeAndKills()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                DOTweenTweenService service = fixture.Initialize(new DOTweenTweenService());
                Assert.IsTrue(service.IsAvailable);

                float value = 0f;
                bool completed = false;
                ITweenHandle valueHandle = service.To(() => value, x => value = x, 1f, 0.01f, new TweenOptions
                {
                    Ease = TweenEase.Linear,
                    IgnoreTimeScale = true,
                    Completed = () => completed = true
                });

                yield return new WaitForSecondsRealtime(0.05f);
                Assert.IsTrue(valueHandle.IsActive || completed);
                Assert.AreEqual(1f, value, 0.01f);
                Assert.IsTrue(completed);

                GameObject go = new GameObject("TweenTarget");
                CanvasGroup group = go.AddComponent<CanvasGroup>();
                service.Move(go.transform, Vector3.one, 0.01f, local: true);
                service.Scale(go.transform, Vector3.one * 2f, 0.01f);
                service.Fade(group, 0.25f, 0.01f);
                yield return new WaitForSecondsRealtime(0.05f);

                Assert.AreEqual(Vector3.one, go.transform.localPosition);
                Assert.AreEqual(Vector3.one * 2f, go.transform.localScale);
                Assert.AreEqual(0.25f, group.alpha, 0.01f);

                service.Move(go.transform, Vector3.zero, 1f, local: true);
                Assert.GreaterOrEqual(service.Kill(go.transform), 0);
                service.KillAll();

                Object.Destroy(go);
                service.Shutdown();
            }
        }

        [Test]
        public void DOTweenTweenService_InvalidTweenInputsReturnSafeHandle()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                DOTweenTweenService service = fixture.Initialize(new DOTweenTweenService());
                ITweenHandle handle = service.To(null, value => { }, 1f, 0.1f);

                Assert.IsFalse(handle.IsActive);
                Assert.IsFalse(handle.IsPlaying);
                AssertEx.DoesNotThrowTwice(() => handle.Play());
                AssertEx.DoesNotThrowTwice(() => handle.Pause());
                AssertEx.DoesNotThrowTwice(() => handle.Kill());
                Assert.AreSame(handle, handle.OnComplete(() => { }));

                service.Shutdown();
            }
        }

        private static class AssertEx
        {
            public static void DoesNotThrowTwice(System.Action action)
            {
                Assert.DoesNotThrow(() => action());
                Assert.DoesNotThrow(() => action());
            }
        }
    }
}

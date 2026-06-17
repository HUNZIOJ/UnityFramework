using Frame.Lifecycle;
using NUnit.Framework;

namespace Frame.Tests.EditMode
{
    public sealed class LifecycleModuleTests
    {
        [Test]
        public void LifecycleService_TracksPauseFocusAndQuitEvents()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                LifecycleService service = fixture.Initialize(new LifecycleService());
                int pauseCount = 0;
                int focusCount = 0;
                int quitCount = 0;
                bool targetFocus = !service.HasFocus;

                service.PauseChanged += paused =>
                {
                    pauseCount++;
                    Assert.IsTrue(paused);
                };
                service.FocusChanged += focused =>
                {
                    focusCount++;
                    Assert.AreEqual(targetFocus, focused);
                };
                service.Quitting += () => quitCount++;

                service.OnApplicationPause(true);
                service.OnApplicationPause(true);
                service.OnApplicationFocus(targetFocus);
                service.OnApplicationFocus(targetFocus);
                service.OnApplicationQuit();
                service.OnApplicationQuit();

                Assert.IsTrue(service.IsPaused);
                Assert.AreEqual(targetFocus, service.HasFocus);
                Assert.IsTrue(service.IsQuitting);
                Assert.AreEqual(1, pauseCount);
                Assert.AreEqual(1, focusCount);
                Assert.AreEqual(1, quitCount);
            }
        }
    }
}

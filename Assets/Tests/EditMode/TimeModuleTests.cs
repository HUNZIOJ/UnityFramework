using Frame.Timing;
using NUnit.Framework;

namespace Frame.Tests.EditMode
{
    public sealed class TimeModuleTests
    {
        [Test]
        public void TimerService_DelayFiresAfterElapsedTime()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                TimerService timers = fixture.Initialize(new TimerService());
                int count = 0;

                TimerHandle handle = timers.Delay(1f, () => count++);
                timers.Update(0.5f, 0.5f);
                Assert.AreEqual(0, count);

                timers.Update(0.5f, 0.5f);

                Assert.AreEqual(1, count);
                Assert.IsFalse(handle.IsValid);
            }
        }

        [Test]
        public void TimerService_RepeatHonorsRepeatCount()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                TimerService timers = fixture.Initialize(new TimerService());
                int count = 0;

                timers.Repeat(1f, () => count++, repeatCount: 2);
                timers.Update(1f, 1f);
                timers.Update(1f, 1f);
                timers.Update(1f, 1f);

                Assert.AreEqual(2, count);
            }
        }

        [Test]
        public void TimerService_CancelAndCancelOwnerPreventCallbacks()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                TimerService timers = fixture.Initialize(new TimerService());
                object owner = new object();
                int count = 0;

                TimerHandle handle = timers.Delay(1f, () => count++);
                handle.Cancel();
                timers.Delay(1f, () => count++, owner: owner);
                timers.CancelOwner(owner);
                timers.Update(1f, 1f);

                Assert.AreEqual(0, count);
            }
        }

        [Test]
        public void TimerService_CancelOwnerDuringCallbackDoesNotKeepCompletedTimers()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                TimerService timers = fixture.Initialize(new TimerService());
                object owner = new object();
                int count = 0;

                timers.Delay(0f, () => count++);
                timers.Delay(0f, () =>
                {
                    count++;
                    timers.CancelOwner(owner);
                });
                timers.Delay(1f, () => count++, owner: owner);

                timers.Update(0f, 0f);

                Assert.AreEqual(2, count);
                Assert.AreEqual(0, timers.ActiveTimerCount);
            }
        }

        [Test]
        public void TimerService_UnscaledTimersUseUnscaledDelta()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                TimerService timers = fixture.Initialize(new TimerService());
                int count = 0;

                timers.Delay(1f, () => count++, unscaled: true);
                timers.Update(0f, 1f);

                Assert.AreEqual(1, count);
            }
        }

        [Test]
        public void TimerService_NextFrameFiresOnNextUpdateAndPauseStopsUpdates()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                TimerService timers = fixture.Initialize(new TimerService());
                int count = 0;

                timers.NextFrame(() => count++);
                timers.OnApplicationPause(true);
                timers.Update(1f, 1f);
                Assert.AreEqual(0, count);

                timers.OnApplicationPause(false);
                timers.Update(0f, 0f);

                Assert.AreEqual(1, count);
            }
        }

        [Test]
        public void TimerService_ReportsActiveScaledUnscaledAndPausedCounts()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                TimerService timers = fixture.Initialize(new TimerService());
                object owner = new object();

                Assert.AreEqual(0, timers.ActiveTimerCount);
                Assert.AreEqual(0, timers.ScaledTimerCount);
                Assert.AreEqual(0, timers.UnscaledTimerCount);
                Assert.IsFalse(timers.IsPaused);

                timers.Delay(1f, () => { });
                timers.Repeat(1f, () => { }, unscaled: true, owner: owner);

                Assert.AreEqual(2, timers.ActiveTimerCount);
                Assert.AreEqual(1, timers.ScaledTimerCount);
                Assert.AreEqual(1, timers.UnscaledTimerCount);

                timers.OnApplicationPause(true);
                Assert.IsTrue(timers.IsPaused);

                timers.CancelOwner(owner);

                Assert.AreEqual(1, timers.ActiveTimerCount);
                Assert.AreEqual(1, timers.ScaledTimerCount);
                Assert.AreEqual(0, timers.UnscaledTimerCount);

                timers.OnApplicationPause(false);
                timers.Update(1f, 1f);

                Assert.AreEqual(0, timers.ActiveTimerCount);
            }
        }
    }
}

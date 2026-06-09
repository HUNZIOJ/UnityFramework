using System;
using Frame.Events;
using NUnit.Framework;

namespace Frame.Tests.EditMode
{
    public sealed class EventsModuleTests
    {
        [Test]
        public void EventBus_PublishesTypedEventsAndDisposesSubscriptions()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                EventBus bus = fixture.Initialize(new EventBus());
                int received = 0;

                IDisposable subscription = bus.Subscribe<TestEvent>(evt => received += evt.Value);
                bus.Publish(new TestEvent { Value = 3 });
                subscription.Dispose();
                bus.Publish(new TestEvent { Value = 3 });

                Assert.AreEqual(3, received);
            }
        }

        [Test]
        public void EventBus_OnceSubscriptionRunsOnlyOnce()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                EventBus bus = fixture.Initialize(new EventBus());
                int count = 0;

                bus.Subscribe<TestEvent>(_ => count++, once: true);
                bus.Publish(new TestEvent());
                bus.Publish(new TestEvent());

                Assert.AreEqual(1, count);
            }
        }

        [Test]
        public void EventBus_UnsubscribeOwnerRemovesMatchingSubscriptions()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                EventBus bus = fixture.Initialize(new EventBus());
                object owner = new object();
                int ownerCount = 0;
                int otherCount = 0;

                bus.Subscribe<TestEvent>(_ => ownerCount++, owner);
                bus.Subscribe<TestEvent>(_ => otherCount++);
                bus.UnsubscribeOwner(owner);
                bus.Publish(new TestEvent());

                Assert.AreEqual(0, ownerCount);
                Assert.AreEqual(1, otherCount);
            }
        }

        [Test]
        public void EventBus_HandlerExceptionsDoNotStopOtherHandlers()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                EventBus bus = fixture.Initialize(new EventBus());
                int count = 0;

                bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("handler"));
                bus.Subscribe<TestEvent>(_ => count++);
                Assert.DoesNotThrow(() => AssertEx.WithFrameLogsOff(() => bus.Publish(new TestEvent())));

                Assert.AreEqual(1, count);
            }
        }

        [Test]
        public void EventBus_ClearRemovesAllSubscriptions()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                EventBus bus = fixture.Initialize(new EventBus());
                int count = 0;

                bus.Subscribe<TestEvent>(_ => count++);
                bus.Clear();
                bus.Publish(new TestEvent());

                Assert.AreEqual(0, count);
            }
        }

        private struct TestEvent
        {
            public int Value;
        }
    }
}

using System;

namespace Frame.Events
{
    public sealed class EventSubscription : IDisposable
    {
        private EventBus owner;
        private readonly int id;

        internal EventSubscription(EventBus owner, int id)
        {
            this.owner = owner;
            this.id = id;
        }

        public void Dispose()
        {
            EventBus bus = owner;
            if (bus == null)
            {
                return;
            }

            owner = null;
            bus.Unsubscribe(id);
        }
    }
}

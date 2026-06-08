using System;

namespace Frame.Events
{
    public interface IEventBus
    {
        IDisposable Subscribe<TEvent>(Action<TEvent> handler, object owner = null, bool once = false);

        void Publish<TEvent>(TEvent gameEvent);

        void UnsubscribeOwner(object owner);

        void Clear();
    }
}

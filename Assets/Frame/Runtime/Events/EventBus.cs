using System;
using System.Collections.Generic;
using Frame.Core;

namespace Frame.Events
{
    public sealed class EventBus : GameModuleBase, IEventBus
    {
        private readonly Dictionary<Type, List<Subscription>> subscriptions = new Dictionary<Type, List<Subscription>>();
        private int nextId = 1;

        public override int Priority
        {
            get { return -900; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IEventBus>(this);
            Context.Services.Register(this);
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler, object owner = null, bool once = false)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            Type eventType = typeof(TEvent);
            List<Subscription> list;
            if (!subscriptions.TryGetValue(eventType, out list))
            {
                list = new List<Subscription>();
                subscriptions.Add(eventType, list);
            }

            int id = nextId++;
            list.Add(new Subscription(id, owner, handler, once));
            return new EventSubscription(this, id);
        }

        public void Publish<TEvent>(TEvent gameEvent)
        {
            Type eventType = typeof(TEvent);
            List<Subscription> list;
            if (!subscriptions.TryGetValue(eventType, out list) || list.Count == 0)
            {
                return;
            }

            Subscription[] snapshot = list.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                Subscription subscription = snapshot[i];
                if (!subscription.Active)
                {
                    continue;
                }

                Action<TEvent> handler = subscription.Handler as Action<TEvent>;
                if (handler == null)
                {
                    continue;
                }

                try
                {
                    handler(gameEvent);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }

                if (subscription.Once)
                {
                    Unsubscribe(subscription.Id);
                }
            }
        }

        public void Unsubscribe(int id)
        {
            foreach (List<Subscription> list in subscriptions.Values)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Id == id)
                    {
                        list[i].Active = false;
                        list.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        public void UnsubscribeOwner(object owner)
        {
            if (owner == null)
            {
                return;
            }

            foreach (List<Subscription> list in subscriptions.Values)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(list[i].Owner, owner))
                    {
                        list[i].Active = false;
                        list.RemoveAt(i);
                    }
                }
            }
        }

        public void Clear()
        {
            subscriptions.Clear();
        }

        protected override void OnShutdown()
        {
            Clear();
        }

        private sealed class Subscription
        {
            public Subscription(int id, object owner, Delegate handler, bool once)
            {
                Id = id;
                Owner = owner;
                Handler = handler;
                Once = once;
                Active = true;
            }

            public int Id;
            public object Owner;
            public Delegate Handler;
            public bool Once;
            public bool Active;
        }
    }
}

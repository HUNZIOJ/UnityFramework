using System;

namespace Frame.Core
{
    public abstract class Singleton<T> where T : Singleton<T>
    {
        private static readonly object SyncRoot = new object();
        private static T instance;

        public static T Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                lock (SyncRoot)
                {
                    if (instance == null)
                    {
                        T created = (T)Activator.CreateInstance(typeof(T), true);
                        created.OnSingletonInitialize();
                        instance = created;
                    }

                    return instance;
                }
            }
        }

        public static bool HasInstance
        {
            get { return instance != null; }
        }

        public static void ReleaseInstance()
        {
            T current;
            lock (SyncRoot)
            {
                current = instance;
                instance = null;
            }

            if (current != null)
            {
                current.OnSingletonRelease();
            }
        }

        protected virtual void OnSingletonInitialize()
        {
        }

        protected virtual void OnSingletonRelease()
        {
        }
    }
}

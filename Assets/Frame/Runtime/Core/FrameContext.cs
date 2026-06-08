using UnityEngine;

namespace Frame.Core
{
    public sealed class FrameContext
    {
        public FrameContext(GameEntry entry, FrameSettings settings, ServiceRegistry services, Transform root, CoroutineRunner coroutines)
        {
            Entry = entry;
            Settings = settings;
            Services = services;
            Root = root;
            Coroutines = coroutines;
        }

        public GameEntry Entry
        {
            get;
            private set;
        }

        public FrameSettings Settings
        {
            get;
            private set;
        }

        public ServiceRegistry Services
        {
            get;
            private set;
        }

        public Transform Root
        {
            get;
            private set;
        }

        public CoroutineRunner Coroutines
        {
            get;
            private set;
        }
    }
}

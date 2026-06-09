using UnityEngine;

namespace Frame.Core
{
    public sealed class FrameContext
    {
        public FrameContext(GameEntry entry, FrameSettings settings, ServiceRegistry services, Transform root)
        {
            Entry = entry;
            Settings = settings;
            Services = services;
            Root = root;
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
    }
}

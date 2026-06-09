using System;
using Frame.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frame.Tests.PlayMode
{
    internal sealed class FramePlayModeTestFixture : IDisposable
    {
        private readonly GameObject root;

        public FramePlayModeTestFixture(string name = "FramePlayModeTest")
        {
            root = new GameObject(name);
            Settings = ScriptableObject.CreateInstance<FrameSettings>();
            Services = new ServiceRegistry();
            Context = new FrameContext(Entry, Settings, Services, root.transform);
        }

        public GameEntry Entry { get; private set; }

        public FrameSettings Settings { get; private set; }

        public ServiceRegistry Services { get; private set; }

        public FrameContext Context { get; private set; }

        public TModule Initialize<TModule>(TModule module) where TModule : GameModuleBase
        {
            module.Initialize(Context);
            return module;
        }

        public void Dispose()
        {
            if (root != null)
            {
                Object.Destroy(root);
            }

            if (Settings != null)
            {
                Object.Destroy(Settings);
            }
        }
    }
}

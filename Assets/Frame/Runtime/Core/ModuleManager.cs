using System;
using System.Collections.Generic;

namespace Frame.Core
{
    public sealed class ModuleManager
    {
        private readonly List<IFrameModule> modules = new List<IFrameModule>();
        private readonly Dictionary<Type, IFrameModule> moduleByType = new Dictionary<Type, IFrameModule>();

        public IReadOnlyList<IFrameModule> Modules
        {
            get { return modules; }
        }

        public void Add(IFrameModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException("module");
            }

            Type type = module.GetType();
            if (moduleByType.ContainsKey(type))
            {
                throw new FrameException("Module already registered: " + type.FullName);
            }

            modules.Add(module);
            moduleByType[type] = module;
            modules.Sort(CompareModulePriority);
        }

        public bool TryGet<TModule>(out TModule module) where TModule : class, IFrameModule
        {
            IFrameModule value;
            if (moduleByType.TryGetValue(typeof(TModule), out value))
            {
                module = value as TModule;
                return module != null;
            }

            module = null;
            return false;
        }

        public TModule Get<TModule>() where TModule : class, IFrameModule
        {
            TModule module;
            if (TryGet(out module))
            {
                return module;
            }

            throw new FrameException("Module is not registered: " + typeof(TModule).FullName);
        }

        public void InitializeAll(FrameContext context)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].Initialize(context);
                FrameLog.Debug("Initialized module: " + modules[i].Name);
            }
        }

        public void StartAll()
        {
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].Start();
            }
        }

        public void UpdateAll(float deltaTime, float unscaledDeltaTime)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].Update(deltaTime, unscaledDeltaTime);
            }
        }

        public void FixedUpdateAll(float fixedDeltaTime, float fixedUnscaledDeltaTime)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].FixedUpdate(fixedDeltaTime, fixedUnscaledDeltaTime);
            }
        }

        public void LateUpdateAll(float deltaTime, float unscaledDeltaTime)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].LateUpdate(deltaTime, unscaledDeltaTime);
            }
        }

        public void PauseAll(bool paused)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].OnApplicationPause(paused);
            }
        }

        public void FocusAll(bool focused)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].OnApplicationFocus(focused);
            }
        }

        public void ShutdownAll()
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                modules[i].Shutdown();
            }

            modules.Clear();
            moduleByType.Clear();
        }

        private static int CompareModulePriority(IFrameModule a, IFrameModule b)
        {
            return a.Priority.CompareTo(b.Priority);
        }
    }
}

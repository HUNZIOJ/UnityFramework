using Frame.Assets;
using Frame.Audio;
using Frame.Config;
using Frame.Events;
using Frame.Input;
using Frame.Localization;
using Frame.Networking;
using Frame.Pooling;
using Frame.Save;
using Frame.Scenes;
using Frame.Timing;
using Frame.UI;
using System;
using System.Reflection;
using UnityEngine;

namespace Frame.Core
{
    public static class Framework
    {
        private static bool isStarted;
        private static FrameContext context;
        private static ServiceRegistry services;
        private static ModuleManager modules;

        public static bool IsInitialized
        {
            get;
            private set;
        }

        public static FrameContext Context
        {
            get { return context; }
        }

        public static ServiceRegistry Services
        {
            get { return services; }
        }

        public static ModuleManager Modules
        {
            get { return modules; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            isStarted = false;
            IsInitialized = false;
            context = null;
            services = null;
            modules = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            FrameSettings settings = FrameSettings.LoadOrDefault();
            if (settings.AutoCreateGameEntry)
            {
                GameEntry.Ensure(settings);
            }
        }

        public static void Initialize(GameEntry entry, FrameSettings settings)
        {
            if (IsInitialized)
            {
                return;
            }

            if (entry == null)
            {
                throw new FrameException("GameEntry is required to initialize Framework.");
            }

            settings = settings == null ? FrameSettings.LoadOrDefault() : settings;
            FrameLog.Configure(settings);
            ApplyApplicationSettings(settings);

            services = new ServiceRegistry();
            modules = new ModuleManager();

            context = new FrameContext(entry, settings, services, entry.transform);
            services.Register(settings);
            services.Register(services);

            try
            {
                RegisterDefaultModules(settings);
                RegisterInstalledModules(settings);
                modules.InitializeAll(context);

                IsInitialized = true;
                FrameLog.Info("Framework initialized.");
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                CleanupFailedInitialization();
                throw new FrameException("Framework initialization failed.", exception);
            }
        }

        public static void Start()
        {
            if (!IsInitialized || isStarted)
            {
                return;
            }

            isStarted = true;
            modules.StartAll();
        }

        public static void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (IsInitialized)
            {
                modules.UpdateAll(deltaTime, unscaledDeltaTime);
            }
        }

        public static void FixedUpdate(float fixedDeltaTime, float fixedUnscaledDeltaTime)
        {
            if (IsInitialized)
            {
                modules.FixedUpdateAll(fixedDeltaTime, fixedUnscaledDeltaTime);
            }
        }

        public static void LateUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (IsInitialized)
            {
                modules.LateUpdateAll(deltaTime, unscaledDeltaTime);
            }
        }

        public static void OnApplicationPause(bool paused)
        {
            if (IsInitialized)
            {
                modules.PauseAll(paused);
            }
        }

        public static void OnApplicationFocus(bool focused)
        {
            if (IsInitialized)
            {
                modules.FocusAll(focused);
            }
        }

        public static void Shutdown()
        {
            if (!IsInitialized)
            {
                return;
            }

            modules.ShutdownAll();
            services.Clear();

            isStarted = false;
            IsInitialized = false;
            context = null;
            modules = null;
            services = null;
            FrameLog.Info("Framework shutdown.");
        }

        public static TService Resolve<TService>() where TService : class
        {
            if (!IsInitialized)
            {
                throw new FrameException("Framework is not initialized.");
            }

            return services.Resolve<TService>();
        }

        public static bool TryResolve<TService>(out TService service) where TService : class
        {
            if (!IsInitialized)
            {
                service = null;
                return false;
            }

            return services.TryResolve(out service);
        }

        private static void ApplyApplicationSettings(FrameSettings settings)
        {
            Application.runInBackground = settings.RunInBackground;
            if (settings.TargetFrameRate != 0)
            {
                Application.targetFrameRate = settings.TargetFrameRate;
            }
        }

        private static void RegisterDefaultModules(FrameSettings settings)
        {
            if (settings.EnableEventBus)
            {
                modules.Add(new EventBus());
            }

            if (settings.EnableTimerService)
            {
                modules.Add(new TimerService());
            }

            if (settings.EnablePoolService)
            {
                modules.Add(new PoolService());
            }

            if (settings.EnableAssetService)
            {
                modules.Add(new ResourcesAssetService());
            }

            if (settings.EnableSceneService)
            {
                modules.Add(new SceneService());
            }

            if (settings.EnableUIService)
            {
                modules.Add(new UIService());
            }

            if (settings.EnableAudioService)
            {
                modules.Add(new AudioService());
            }

            if (settings.EnableSaveService)
            {
                modules.Add(new SaveService());
            }

            if (settings.EnableConfigService)
            {
                modules.Add(new ConfigService());
            }

            if (settings.EnableInputService)
            {
                modules.Add(new InputService());
            }

            if (settings.EnableHttpService)
            {
                modules.Add(new HttpService());
            }

            if (settings.EnableLocalizationService)
            {
                modules.Add(new LocalizationService());
            }
        }

        private static void RegisterInstalledModules(FrameSettings settings)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types = GetLoadableTypes(assemblies[i]);
                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];
                    if (type == null || type.IsAbstract || type.IsInterface || !typeof(IFrameModuleInstaller).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    try
                    {
                        IFrameModuleInstaller installer = Activator.CreateInstance(type) as IFrameModuleInstaller;
                        if (installer != null)
                        {
                            installer.Install(modules, settings);
                        }
                    }
                    catch (Exception exception)
                    {
                        FrameLog.Exception(exception);
                    }
                }
            }
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
            {
                return Array.Empty<Type>();
            }

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types;
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                return Array.Empty<Type>();
            }
        }

        private static void CleanupFailedInitialization()
        {
            if (modules != null)
            {
                modules.ShutdownAll();
            }

            if (services != null)
            {
                services.Clear();
            }

            isStarted = false;
            IsInitialized = false;
            context = null;
            modules = null;
            services = null;
        }
    }
}

using UnityEngine;

namespace Frame.Core
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GameEntry : MonoSingleton<GameEntry>
    {
        [SerializeField] private FrameSettings settings;
        [SerializeField] private bool initializeOnAwake = true;

        public FrameSettings Settings
        {
            get { return settings; }
        }

        protected override bool UseDontDestroyOnLoad
        {
            get { return settings != null && settings.UseDontDestroyOnLoad; }
        }

        public static GameEntry Ensure(FrameSettings frameSettings)
        {
            if (Instance != null)
            {
                return Instance;
            }

            GameEntry existing = FindExistingInstance();
            if (existing != null)
            {
                existing.UseSettings(frameSettings);
                return existing;
            }

            GameObject go = new GameObject("Frame");
            go.SetActive(false);
            GameEntry entry = go.AddComponent<GameEntry>();
            entry.UseSettings(frameSettings);
            go.SetActive(true);
            return entry;
        }

        internal void UseSettings(FrameSettings frameSettings)
        {
            if (settings == null)
            {
                settings = frameSettings;
            }
        }

        protected override void OnSingletonAwake()
        {
            if (settings == null)
            {
                settings = FrameSettings.LoadOrDefault();
            }

            if (initializeOnAwake)
            {
                Framework.Initialize(this, settings);
            }
        }

        private void Start()
        {
            Framework.Start();
        }

        private void Update()
        {
            Framework.Update(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            Framework.FixedUpdate(Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
        }

        private void LateUpdate()
        {
            Framework.LateUpdate(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            Framework.OnApplicationPause(pauseStatus);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            Framework.OnApplicationFocus(hasFocus);
        }

        protected override void OnSingletonApplicationQuit()
        {
            Framework.OnApplicationQuit();
            Framework.Shutdown();
        }

        protected override void OnSingletonDestroyed()
        {
            if (!IsApplicationQuitting && Framework.IsInitialized)
            {
                Framework.Shutdown();
            }
        }
    }
}

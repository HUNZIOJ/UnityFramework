using UnityEngine;

namespace Frame.Core
{
    [DefaultExecutionOrder(-10000)]
    public sealed class GameEntry : MonoBehaviour
    {
        private static GameEntry instance;

        [SerializeField] private FrameSettings settings;
        [SerializeField] private bool initializeOnAwake = true;

        private bool isQuitting;

        public static GameEntry Instance
        {
            get { return instance; }
        }

        public FrameSettings Settings
        {
            get { return settings; }
        }

        public static GameEntry Ensure(FrameSettings frameSettings)
        {
            if (instance != null)
            {
                return instance;
            }

            GameEntry existing = FindExistingEntry();
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

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            if (settings == null)
            {
                settings = FrameSettings.LoadOrDefault();
            }

            if (settings.UseDontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
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

        private void OnApplicationQuit()
        {
            isQuitting = true;
            Framework.Shutdown();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                if (!isQuitting && Framework.IsInitialized)
                {
                    Framework.Shutdown();
                }

                instance = null;
            }
        }

        private static GameEntry FindExistingEntry()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindAnyObjectByType<GameEntry>(FindObjectsInactive.Include);
#else
            return Object.FindObjectOfType<GameEntry>(true);
#endif
        }
    }
}

using UnityEngine;

namespace Frame.Core
{
    [CreateAssetMenu(menuName = "Frame/Frame Settings", fileName = "FrameSettings")]
    public sealed class FrameSettings : ScriptableObject
    {
        public const string ResourcesPath = "Frame/FrameSettings";

        [Header("Bootstrap")]
        [SerializeField] private bool autoCreateGameEntry = true;
        [SerializeField] private bool dontDestroyOnLoad = true;
        [SerializeField] private bool runInBackground = true;
        [SerializeField] private int targetFrameRate = -1;

        [Header("Logging")]
        [SerializeField] private bool enableLogs = true;
        [SerializeField] private FrameLogLevel minimumLogLevel = FrameLogLevel.Info;

        [Header("Modules")]
        [SerializeField] private bool enableEventBus = true;
        [SerializeField] private bool enableTimerService = true;
        [SerializeField] private bool enablePoolService = true;
        [SerializeField] private bool enableAssetService = true;
        [SerializeField] private bool enableSceneService = true;
        [SerializeField] private bool enableUIService = true;
        [SerializeField] private bool enableAudioService = true;
        [SerializeField] private bool enableTweenService = true;
        [SerializeField] private bool enableSaveService = true;
        [SerializeField] private bool enableConfigService = true;
        [SerializeField] private bool enableInputService = true;
        [SerializeField] private bool enableHttpService = true;
        [SerializeField] private bool enableLocalizationService = true;

        [Header("UI")]
        [SerializeField] private string uiRootName = "UIRoot";
        [SerializeField] private int uiReferenceWidth = 1920;
        [SerializeField] private int uiReferenceHeight = 1080;
        [SerializeField] private float uiMatchWidthOrHeight = 0.5f;

        [Header("Audio")]
        [SerializeField] private int audioSourcePoolSize = 16;

        [Header("Save")]
        [SerializeField] private string saveFolderName = "Saves";

        [Header("Pooling")]
        [SerializeField] private int defaultGameObjectPoolMaxSize = 128;

        public bool AutoCreateGameEntry
        {
            get { return autoCreateGameEntry; }
        }

        public bool UseDontDestroyOnLoad
        {
            get { return dontDestroyOnLoad; }
        }

        public bool RunInBackground
        {
            get { return runInBackground; }
        }

        public int TargetFrameRate
        {
            get { return targetFrameRate; }
        }

        public bool EnableLogs
        {
            get { return enableLogs; }
        }

        public FrameLogLevel MinimumLogLevel
        {
            get { return minimumLogLevel; }
        }

        public bool EnableEventBus
        {
            get { return enableEventBus; }
        }

        public bool EnableTimerService
        {
            get { return enableTimerService; }
        }

        public bool EnablePoolService
        {
            get { return enablePoolService; }
        }

        public bool EnableAssetService
        {
            get { return enableAssetService; }
        }

        public bool EnableSceneService
        {
            get { return enableSceneService; }
        }

        public bool EnableUIService
        {
            get { return enableUIService; }
        }

        public bool EnableAudioService
        {
            get { return enableAudioService; }
        }

        public bool EnableTweenService
        {
            get { return enableTweenService; }
        }

        public bool EnableSaveService
        {
            get { return enableSaveService; }
        }

        public bool EnableConfigService
        {
            get { return enableConfigService; }
        }

        public bool EnableInputService
        {
            get { return enableInputService; }
        }

        public bool EnableHttpService
        {
            get { return enableHttpService; }
        }

        public bool EnableLocalizationService
        {
            get { return enableLocalizationService; }
        }

        public string UIRootName
        {
            get { return string.IsNullOrWhiteSpace(uiRootName) ? "UIRoot" : uiRootName; }
        }

        public Vector2 UIReferenceResolution
        {
            get { return new Vector2(Mathf.Max(1, uiReferenceWidth), Mathf.Max(1, uiReferenceHeight)); }
        }

        public float UIMatchWidthOrHeight
        {
            get { return Mathf.Clamp01(uiMatchWidthOrHeight); }
        }

        public int AudioSourcePoolSize
        {
            get { return Mathf.Max(1, audioSourcePoolSize); }
        }

        public string SaveFolderName
        {
            get { return string.IsNullOrWhiteSpace(saveFolderName) ? "Saves" : saveFolderName; }
        }

        public int DefaultGameObjectPoolMaxSize
        {
            get { return Mathf.Max(1, defaultGameObjectPoolMaxSize); }
        }

        public static FrameSettings LoadOrDefault()
        {
            FrameSettings settings = Resources.Load<FrameSettings>(ResourcesPath);
            if (settings != null)
            {
                return settings;
            }

            settings = CreateInstance<FrameSettings>();
            settings.name = "Runtime FrameSettings";
            return settings;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            uiReferenceWidth = Mathf.Max(1, uiReferenceWidth);
            uiReferenceHeight = Mathf.Max(1, uiReferenceHeight);
            uiMatchWidthOrHeight = Mathf.Clamp01(uiMatchWidthOrHeight);
            audioSourcePoolSize = Mathf.Max(1, audioSourcePoolSize);
            defaultGameObjectPoolMaxSize = Mathf.Max(1, defaultGameObjectPoolMaxSize);
        }
#endif
    }
}

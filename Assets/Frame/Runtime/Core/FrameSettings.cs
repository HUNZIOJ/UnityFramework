using Frame.Assets;
using Frame.Audio;
using UnityEngine;
using UnityEngine.Audio;

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

        [Header("Diagnostics")]
        [SerializeField] private bool enableRuntimeDiagnosticsOverlay = false;
        [SerializeField] private bool runtimeDiagnosticsOverlayVisibleOnStart = false;
        [SerializeField] private KeyCode runtimeDiagnosticsOverlayToggleKey = KeyCode.BackQuote;

        [Header("Modules")]
        [SerializeField] private bool enableDiagnosticsService = true;
        [SerializeField] private bool enableEventBus = true;
        [SerializeField] private bool enableLifecycleService = true;
        [SerializeField] private bool enableTimerService = true;
        [SerializeField] private bool enablePreferencesService = true;
        [SerializeField] private bool enablePoolService = true;
        [SerializeField] private bool enableAssetService = true;
        [SerializeField] private bool enableSceneService = true;
        [SerializeField] private bool enableUIService = true;
        [SerializeField] private bool enableGuideService = true;
        [SerializeField] private bool enableAudioService = true;
        [SerializeField] private bool enableTweenService = true;
        [SerializeField] private bool enableSaveService = true;
        [SerializeField] private bool enableConfigService = true;
        [SerializeField] private bool enableInputService = true;
        [SerializeField] private bool enableHttpService = true;
        [SerializeField] private bool enableSocketService = true;
        [SerializeField] private bool enableLocalizationService = true;
        [SerializeField] private bool enableResourceUpdateService = true;
        [SerializeField] private bool enableHotUpdateService = true;

        [Header("YooAsset")]
        [SerializeField] private string yooAssetPackageName = "DefaultPackage";
        [SerializeField] private YooAssetPlayMode yooAssetPlayMode = YooAssetPlayMode.EditorSimulate;
        [SerializeField] private string yooAssetEditorPackageRoot = string.Empty;
        [SerializeField] private string yooAssetBuiltinPackageRoot = string.Empty;
        [SerializeField] private string yooAssetDefaultHostServer = string.Empty;
        [SerializeField] private string yooAssetFallbackHostServer = string.Empty;
        [SerializeField] private int yooAssetDownloadMaxConcurrency = 5;
        [SerializeField] private int yooAssetDownloadMaxRequestPerFrame = 1;
        [SerializeField] private int yooAssetDownloadWatchdogTimeout = 10;

        [Header("UI")]
        [SerializeField] private string uiRootName = "UIRoot";
        [SerializeField] private int uiReferenceWidth = 1920;
        [SerializeField] private int uiReferenceHeight = 1080;
        [SerializeField] private float uiMatchWidthOrHeight = 0.5f;

        [Header("Audio")]
        [SerializeField] private int audioSourcePoolSize = 16;
        [SerializeField] private AudioMixerGroup masterAudioMixerGroup = null;
        [SerializeField] private AudioMixerGroup musicAudioMixerGroup = null;
        [SerializeField] private AudioMixerGroup sfxAudioMixerGroup = null;
        [SerializeField] private AudioMixerGroup uiAudioMixerGroup = null;
        [SerializeField] private AudioMixerGroup ambientAudioMixerGroup = null;
        [SerializeField] private string masterAudioMixerVolumeParameter = "MasterVolume";
        [SerializeField] private string musicAudioMixerVolumeParameter = "MusicVolume";
        [SerializeField] private string sfxAudioMixerVolumeParameter = "SfxVolume";
        [SerializeField] private string uiAudioMixerVolumeParameter = "UIVolume";
        [SerializeField] private string ambientAudioMixerVolumeParameter = "AmbientVolume";

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

        public bool EnableRuntimeDiagnosticsOverlay
        {
            get { return enableRuntimeDiagnosticsOverlay; }
        }

        public bool RuntimeDiagnosticsOverlayVisibleOnStart
        {
            get { return runtimeDiagnosticsOverlayVisibleOnStart; }
        }

        public KeyCode RuntimeDiagnosticsOverlayToggleKey
        {
            get { return runtimeDiagnosticsOverlayToggleKey; }
        }

        public bool EnableDiagnosticsService
        {
            get { return enableDiagnosticsService; }
        }

        public bool EnableEventBus
        {
            get { return enableEventBus; }
        }

        public bool EnableLifecycleService
        {
            get { return enableLifecycleService; }
        }

        public bool EnableTimerService
        {
            get { return enableTimerService; }
        }

        public bool EnablePreferencesService
        {
            get { return enablePreferencesService; }
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

        public bool EnableGuideService
        {
            get { return enableGuideService; }
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

        public bool EnableSocketService
        {
            get { return enableSocketService; }
        }

        public bool EnableLocalizationService
        {
            get { return enableLocalizationService; }
        }

        public bool EnableResourceUpdateService
        {
            get { return enableResourceUpdateService; }
        }

        public bool EnableHotUpdateService
        {
            get { return enableHotUpdateService; }
        }

        public string YooAssetPackageName
        {
            get { return string.IsNullOrWhiteSpace(yooAssetPackageName) ? "DefaultPackage" : yooAssetPackageName.Trim(); }
        }

        public YooAssetPlayMode YooAssetPlayMode
        {
            get { return yooAssetPlayMode; }
        }

        public string YooAssetEditorPackageRoot
        {
            get { return string.IsNullOrWhiteSpace(yooAssetEditorPackageRoot) ? string.Empty : yooAssetEditorPackageRoot.Trim(); }
        }

        public string YooAssetBuiltinPackageRoot
        {
            get { return string.IsNullOrWhiteSpace(yooAssetBuiltinPackageRoot) ? string.Empty : yooAssetBuiltinPackageRoot.Trim(); }
        }

        public string YooAssetDefaultHostServer
        {
            get { return string.IsNullOrWhiteSpace(yooAssetDefaultHostServer) ? string.Empty : yooAssetDefaultHostServer.Trim(); }
        }

        public string YooAssetFallbackHostServer
        {
            get { return string.IsNullOrWhiteSpace(yooAssetFallbackHostServer) ? string.Empty : yooAssetFallbackHostServer.Trim(); }
        }

        public int YooAssetDownloadMaxConcurrency
        {
            get { return Mathf.Max(1, yooAssetDownloadMaxConcurrency); }
        }

        public int YooAssetDownloadMaxRequestPerFrame
        {
            get { return Mathf.Max(1, yooAssetDownloadMaxRequestPerFrame); }
        }

        public int YooAssetDownloadWatchdogTimeout
        {
            get { return Mathf.Max(1, yooAssetDownloadWatchdogTimeout); }
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

        public AudioMixerGroup GetAudioMixerGroup(AudioCategory category)
        {
            AudioMixerGroup group = GetAssignedAudioMixerGroup(category);
            return group != null ? group : masterAudioMixerGroup;
        }

        public AudioMixerGroup GetAssignedAudioMixerGroup(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.Music:
                    return musicAudioMixerGroup;
                case AudioCategory.Sfx:
                    return sfxAudioMixerGroup;
                case AudioCategory.UI:
                    return uiAudioMixerGroup;
                case AudioCategory.Ambient:
                    return ambientAudioMixerGroup;
                default:
                    return masterAudioMixerGroup;
            }
        }

        public string GetAudioMixerVolumeParameter(AudioCategory category)
        {
            switch (category)
            {
                case AudioCategory.Music:
                    return musicAudioMixerVolumeParameter;
                case AudioCategory.Sfx:
                    return sfxAudioMixerVolumeParameter;
                case AudioCategory.UI:
                    return uiAudioMixerVolumeParameter;
                case AudioCategory.Ambient:
                    return ambientAudioMixerVolumeParameter;
                default:
                    return masterAudioMixerVolumeParameter;
            }
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
            yooAssetDownloadMaxConcurrency = Mathf.Max(1, yooAssetDownloadMaxConcurrency);
            yooAssetDownloadMaxRequestPerFrame = Mathf.Max(1, yooAssetDownloadMaxRequestPerFrame);
            yooAssetDownloadWatchdogTimeout = Mathf.Max(1, yooAssetDownloadWatchdogTimeout);
        }
#endif
    }
}

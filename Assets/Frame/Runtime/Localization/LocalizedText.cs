using Frame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Localization
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Text))]
    [AddComponentMenu("Frame/Localization/Localized Text")]
    public sealed class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string key;
        [SerializeField] private string fallback;
        [SerializeField] private Text target;

        private ILocalizationService localization;

        public string Key
        {
            get { return key; }
        }

        public string Fallback
        {
            get { return fallback; }
        }

        public Text Target
        {
            get
            {
                ResolveTarget();
                return target;
            }
        }

        public void SetKey(string localizationKey)
        {
            key = localizationKey;
            Refresh();
        }

        public void SetFallback(string fallbackText)
        {
            fallback = fallbackText;
            Refresh();
        }

        public void Bind(ILocalizationService service)
        {
            if (ReferenceEquals(localization, service))
            {
                Refresh();
                return;
            }

            Unbind();
            localization = service;
            if (localization != null)
            {
                localization.LocaleChanged += OnLocaleChanged;
            }

            Refresh();
        }

        public void Unbind()
        {
            if (localization != null)
            {
                localization.LocaleChanged -= OnLocaleChanged;
                localization = null;
            }
        }

        public void Refresh()
        {
            ResolveTarget();
            if (target == null)
            {
                return;
            }

            if (localization == null && !TryBindFromFramework())
            {
                target.text = GetFallbackText();
                return;
            }

            string resolvedFallback = string.IsNullOrEmpty(fallback) ? null : fallback;
            target.text = localization.Translate(key, resolvedFallback) ?? string.Empty;
        }

        private void Awake()
        {
            ResolveTarget();
        }

        private void OnEnable()
        {
            TryBindFromFramework();
            Refresh();
        }

        private void Start()
        {
            TryBindFromFramework();
            Refresh();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void Reset()
        {
            target = GetComponent<Text>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (target == null)
            {
                target = GetComponent<Text>();
            }

            if (Application.isPlaying && isActiveAndEnabled)
            {
                Refresh();
            }
        }
#endif

        private bool TryBindFromFramework()
        {
            if (localization != null)
            {
                return true;
            }

            ILocalizationService service;
            if (Framework.TryResolve(out service))
            {
                Bind(service);
                return true;
            }

            return false;
        }

        private void ResolveTarget()
        {
            if (target == null)
            {
                target = GetComponent<Text>();
            }
        }

        private void OnLocaleChanged(string locale)
        {
            Refresh();
        }

        private string GetFallbackText()
        {
            if (!string.IsNullOrEmpty(fallback))
            {
                return fallback;
            }

            return string.IsNullOrEmpty(key) ? string.Empty : key;
        }
    }
}

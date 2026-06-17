using System.Collections;
using UnityEngine;

namespace Frame.UI
{
    public sealed class UIFadeTransition : IUITransition
    {
        public UIFadeTransition(float duration = 0.18f, bool unscaledTime = true)
        {
            OpenDuration = duration;
            CloseDuration = duration;
            UseUnscaledTime = unscaledTime;
        }

        public float OpenDuration { get; set; }

        public float CloseDuration { get; set; }

        public bool UseUnscaledTime { get; set; }

        public IEnumerator PlayOpen(UIPanelBase panel)
        {
            yield return Fade(panel, 0f, 1f, OpenDuration);
        }

        public IEnumerator PlayClose(UIPanelBase panel)
        {
            yield return Fade(panel, 1f, 0f, CloseDuration);
        }

        private IEnumerator Fade(UIPanelBase panel, float from, float to, float duration)
        {
            if (panel == null)
            {
                yield break;
            }

            CanvasGroup canvasGroup = panel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = panel.gameObject.AddComponent<CanvasGroup>();
            }

            if (duration <= 0f)
            {
                canvasGroup.alpha = to;
                yield break;
            }

            float elapsed = 0f;
            canvasGroup.alpha = from;
            while (elapsed < duration && panel != null)
            {
                elapsed += UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (panel != null)
            {
                canvasGroup.alpha = to;
            }
        }
    }
}

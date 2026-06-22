using Cysharp.Threading.Tasks;
using Frame.Core;
using Frame.Tweening;
using UnityEngine;

namespace Frame.UI
{
    public sealed class UIFadeTransition : IUITransition
    {
        private readonly ITweenService tweenService;

        public UIFadeTransition(
            float duration = 0.18f, 
            bool unscaledTime = true, 
            TweenEase ease = TweenEase.OutQuad,
            AnimationCurve openCurve = null,
            AnimationCurve closeCurve = null)
        {
            OpenDuration = duration;
            CloseDuration = duration;
            UseUnscaledTime = unscaledTime;
            OpenEase = ease;
            CloseEase = ease;
            OpenCurve = openCurve;
            CloseCurve = closeCurve;
            
            Framework.TryResolve(out tweenService);
        }

        public float OpenDuration { get; set; }
        public float CloseDuration { get; set; }
        public bool UseUnscaledTime { get; set; }
        public TweenEase OpenEase { get; set; }
        public TweenEase CloseEase { get; set; }
        public AnimationCurve OpenCurve { get; set; }
        public AnimationCurve CloseCurve { get; set; }

        public UniTask PlayOpen(UIPanelBase panel)
        {
            return Fade(panel, 0f, 1f, OpenDuration, OpenEase, OpenCurve);
        }

        public UniTask PlayClose(UIPanelBase panel)
        {
            return Fade(panel, 1f, 0f, CloseDuration, CloseEase, CloseCurve);
        }

        private async UniTask Fade(UIPanelBase panel, float from, float to, float duration, TweenEase ease, AnimationCurve curve)
        {
            if (panel == null)
            {
                return;
            }

            CanvasGroup canvasGroup = panel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = panel.gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.alpha = from;

            if (duration <= 0f)
            {
                canvasGroup.alpha = to;
                return;
            }

            if (tweenService != null && tweenService.IsAvailable)
            {
                bool completed = false;
                ITweenHandle handle = tweenService.Fade(canvasGroup, to, duration, new TweenOptions
                {
                    Ease = ease,
                    EaseCurve = curve,
                    IgnoreTimeScale = UseUnscaledTime,
                    Target = panel,
                    Completed = () => completed = true
                });

                if (handle != null && handle.IsActive)
                {
                    await UniTask.WaitWhile(() => panel != null && handle.IsActive && !completed);
                }
            }

            if (panel != null)
            {
                canvasGroup.alpha = to;
            }
        }
    }
}

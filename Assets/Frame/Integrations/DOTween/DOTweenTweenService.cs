using System;
using DG.Tweening;
using Frame.Core;
using Frame.Tweening;
using UnityEngine;

namespace Frame.DOTween
{
    public sealed class DOTweenTweenService : GameModuleBase, ITweenService
    {
        public override int Priority
        {
            get { return -250; }
        }

        public bool IsAvailable
        {
            get { return true; }
        }

        protected override void OnInitialize()
        {
            DG.Tweening.DOTween.Init(false, true, LogBehaviour.ErrorsOnly);
            Context.Services.Register<ITweenService>(this);
            Context.Services.Register(this);
        }

        public ITweenHandle To(Func<float> getter, Action<float> setter, float endValue, float duration, TweenOptions options = null)
        {
            if (getter == null || setter == null)
            {
                return new DOTweenTweenHandle(null);
            }

            Tween tween = DG.Tweening.DOTween.To(getter.Invoke, setter.Invoke, endValue, Mathf.Max(0f, duration));
            ApplyOptions(tween, options);
            return new DOTweenTweenHandle(tween);
        }

        public ITweenHandle Move(Transform target, Vector3 endValue, float duration, bool local = false, TweenOptions options = null)
        {
            if (target == null)
            {
                return new DOTweenTweenHandle(null);
            }

            Tween tween = DG.Tweening.DOTween.To(
                () => local ? target.localPosition : target.position,
                value =>
                {
                    if (local)
                    {
                        target.localPosition = value;
                    }
                    else
                    {
                        target.position = value;
                    }
                },
                endValue,
                Mathf.Max(0f, duration));

            tween.SetTarget(target);
            ApplyOptions(tween, options);
            return new DOTweenTweenHandle(tween);
        }

        public ITweenHandle Scale(Transform target, Vector3 endValue, float duration, TweenOptions options = null)
        {
            if (target == null)
            {
                return new DOTweenTweenHandle(null);
            }

            Tween tween = DG.Tweening.DOTween.To(() => target.localScale, value => target.localScale = value, endValue, Mathf.Max(0f, duration));
            tween.SetTarget(target);
            ApplyOptions(tween, options);
            return new DOTweenTweenHandle(tween);
        }

        public ITweenHandle Fade(CanvasGroup target, float endValue, float duration, TweenOptions options = null)
        {
            if (target == null)
            {
                return new DOTweenTweenHandle(null);
            }

            Tween tween = target.DOFade(Mathf.Clamp01(endValue), Mathf.Max(0f, duration));
            tween.SetTarget(target);
            ApplyOptions(tween, options);
            return new DOTweenTweenHandle(tween);
        }

        public int Kill(object target, bool complete = false)
        {
            return DG.Tweening.DOTween.Kill(target, complete);
        }

        public void KillAll(bool complete = false)
        {
            DG.Tweening.DOTween.KillAll(complete);
        }

        protected override void OnShutdown()
        {
            KillAll(false);
        }

        private static void ApplyOptions(Tween tween, TweenOptions options)
        {
            if (tween == null)
            {
                return;
            }

            TweenOptions resolved = options ?? new TweenOptions();
            if (resolved.EaseCurve != null)
            {
                tween.SetEase(resolved.EaseCurve);
            }
            else
            {
                tween.SetEase(MapEase(resolved.Ease));
            }

            tween.SetUpdate(resolved.IgnoreTimeScale);

            if (resolved.Target != null)
            {
                tween.SetTarget(resolved.Target);
            }

            if (resolved.Completed != null)
            {
                tween.OnComplete(() => resolved.Completed());
            }
        }

        private static Ease MapEase(TweenEase ease)
        {
            switch (ease)
            {
                case TweenEase.Linear:
                    return Ease.Linear;
                case TweenEase.InQuad:
                    return Ease.InQuad;
                case TweenEase.InOutQuad:
                    return Ease.InOutQuad;
                case TweenEase.InCubic:
                    return Ease.InCubic;
                case TweenEase.OutCubic:
                    return Ease.OutCubic;
                case TweenEase.InOutCubic:
                    return Ease.InOutCubic;
                case TweenEase.InBack:
                    return Ease.InBack;
                case TweenEase.OutBack:
                    return Ease.OutBack;
                case TweenEase.InOutBack:
                    return Ease.InOutBack;
                default:
                    return Ease.OutQuad;
            }
        }
    }
}

using System;
using DG.Tweening;
using Frame.Tweening;

namespace Frame.DOTween
{
    public sealed class DOTweenTweenHandle : ITweenHandle
    {
        private Tween tween;

        public DOTweenTweenHandle(Tween tween)
        {
            this.tween = tween;
        }

        public bool IsActive
        {
            get { return tween != null && tween.IsActive(); }
        }

        public bool IsPlaying
        {
            get { return tween != null && tween.IsPlaying(); }
        }

        public void Play()
        {
            if (tween != null)
            {
                tween.Play();
            }
        }

        public void Pause()
        {
            if (tween != null)
            {
                tween.Pause();
            }
        }

        public void Kill(bool complete = false)
        {
            if (tween != null)
            {
                tween.Kill(complete);
                tween = null;
            }
        }

        public ITweenHandle OnComplete(Action callback)
        {
            if (tween != null && callback != null)
            {
                tween.OnComplete(() => callback());
            }

            return this;
        }
    }
}

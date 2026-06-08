using System;

namespace Frame.Tweening
{
    public sealed class TweenOptions
    {
        public TweenEase Ease = TweenEase.OutQuad;
        public bool IgnoreTimeScale;
        public object Target;
        public Action Completed;
    }
}

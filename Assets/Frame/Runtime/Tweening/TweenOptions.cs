using System;
using UnityEngine;

namespace Frame.Tweening
{
    public sealed class TweenOptions
    {
        public TweenEase Ease = TweenEase.OutQuad;
        public AnimationCurve EaseCurve;
        public bool IgnoreTimeScale;
        public object Target;
        public Action Completed;
    }
}

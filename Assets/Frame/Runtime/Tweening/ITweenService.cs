using System;
using UnityEngine;

namespace Frame.Tweening
{
    public interface ITweenService
    {
        bool IsAvailable { get; }

        ITweenHandle To(Func<float> getter, Action<float> setter, float endValue, float duration, TweenOptions options = null);

        ITweenHandle Move(Transform target, Vector3 endValue, float duration, bool local = false, TweenOptions options = null);

        ITweenHandle Scale(Transform target, Vector3 endValue, float duration, TweenOptions options = null);

        ITweenHandle Fade(CanvasGroup target, float endValue, float duration, TweenOptions options = null);

        int Kill(object target, bool complete = false);

        void KillAll(bool complete = false);
    }
}

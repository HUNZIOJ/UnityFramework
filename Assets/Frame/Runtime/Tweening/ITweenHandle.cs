using System;

namespace Frame.Tweening
{
    public interface ITweenHandle
    {
        bool IsActive { get; }

        bool IsPlaying { get; }

        void Play();

        void Pause();

        void Kill(bool complete = false);

        ITweenHandle OnComplete(Action callback);
    }
}

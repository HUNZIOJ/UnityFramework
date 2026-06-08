using System;

namespace Frame.Timing
{
    public interface ITimerService
    {
        TimerHandle Delay(float seconds, Action callback, bool unscaled = false, object owner = null);

        TimerHandle Repeat(float interval, Action callback, int repeatCount = -1, bool unscaled = false, object owner = null);

        TimerHandle NextFrame(Action callback, object owner = null);

        bool Contains(int id);

        bool Cancel(int id);

        void CancelOwner(object owner);
    }
}

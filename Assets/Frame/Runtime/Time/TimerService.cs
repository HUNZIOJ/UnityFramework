using System;
using System.Collections.Generic;
using Frame.Core;

namespace Frame.Timing
{
    public sealed class TimerService : GameModuleBase, ITimerService
    {
        private readonly Dictionary<int, TimerTask> timers = new Dictionary<int, TimerTask>();
        private readonly List<int> completedBuffer = new List<int>();
        private readonly List<int> ownerCancelBuffer = new List<int>();
        private readonly List<int> updateBuffer = new List<int>();
        private int nextId = 1;
        private bool paused;

        public override int Priority
        {
            get { return -800; }
        }

        public int ActiveTimerCount
        {
            get { return timers.Count; }
        }

        public int ScaledTimerCount
        {
            get { return CountTimers(unscaled: false); }
        }

        public int UnscaledTimerCount
        {
            get { return CountTimers(unscaled: true); }
        }

        public bool IsPaused
        {
            get { return paused; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<ITimerService>(this);
            Context.Services.Register(this);
        }

        public TimerHandle Delay(float seconds, Action callback, bool unscaled = false, object owner = null)
        {
            return Schedule(seconds, 0f, 0, callback, unscaled, owner);
        }

        public TimerHandle Repeat(float interval, Action callback, int repeatCount = -1, bool unscaled = false, object owner = null)
        {
            return Schedule(interval, interval, repeatCount, callback, unscaled, owner);
        }

        public TimerHandle NextFrame(Action callback, object owner = null)
        {
            return Schedule(0f, 0f, 0, callback, true, owner);
        }

        public bool Contains(int id)
        {
            return timers.ContainsKey(id);
        }

        public bool Cancel(int id)
        {
            return timers.Remove(id);
        }

        public void CancelOwner(object owner)
        {
            if (owner == null)
            {
                return;
            }

            ownerCancelBuffer.Clear();
            foreach (KeyValuePair<int, TimerTask> pair in timers)
            {
                if (ReferenceEquals(pair.Value.Owner, owner))
                {
                    ownerCancelBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < ownerCancelBuffer.Count; i++)
            {
                timers.Remove(ownerCancelBuffer[i]);
            }
        }

        public override void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (paused || timers.Count == 0)
            {
                return;
            }

            completedBuffer.Clear();
            updateBuffer.Clear();
            foreach (int key in timers.Keys)
            {
                updateBuffer.Add(key);
            }

            for (int i = 0; i < updateBuffer.Count; i++)
            {
                int id = updateBuffer[i];
                TimerTask timer;
                if (!timers.TryGetValue(id, out timer))
                {
                    continue;
                }

                float delta = timer.Unscaled ? unscaledDeltaTime : deltaTime;
                timer.Remaining -= delta;
                if (timer.Remaining > 0f)
                {
                    continue;
                }

                try
                {
                    timer.Callback();
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                }

                if (timer.Interval > 0f && (timer.RepeatCount < 0 || timer.CompletedCount + 1 < timer.RepeatCount))
                {
                    timer.CompletedCount++;
                    timer.Remaining += timer.Interval;
                }
                else
                {
                    completedBuffer.Add(id);
                }
            }

            for (int i = 0; i < completedBuffer.Count; i++)
            {
                timers.Remove(completedBuffer[i]);
            }
        }

        public override void OnApplicationPause(bool paused)
        {
            this.paused = paused;
        }

        protected override void OnShutdown()
        {
            timers.Clear();
            completedBuffer.Clear();
            ownerCancelBuffer.Clear();
            updateBuffer.Clear();
            paused = false;
            nextId = 1;
        }

        private TimerHandle Schedule(float delay, float interval, int repeatCount, Action callback, bool unscaled, object owner)
        {
            if (callback == null)
            {
                throw new ArgumentNullException("callback");
            }

            int id = nextId++;
            timers.Add(id, new TimerTask
            {
                Remaining = Math.Max(0f, delay),
                Interval = Math.Max(0f, interval),
                RepeatCount = repeatCount,
                Callback = callback,
                Unscaled = unscaled,
                Owner = owner
            });

            return new TimerHandle(this, id);
        }

        private int CountTimers(bool unscaled)
        {
            int count = 0;
            foreach (TimerTask timer in timers.Values)
            {
                if (timer.Unscaled == unscaled)
                {
                    count++;
                }
            }

            return count;
        }

        private sealed class TimerTask
        {
            public float Remaining;
            public float Interval;
            public int RepeatCount;
            public int CompletedCount;
            public Action Callback;
            public bool Unscaled;
            public object Owner;
        }
    }
}

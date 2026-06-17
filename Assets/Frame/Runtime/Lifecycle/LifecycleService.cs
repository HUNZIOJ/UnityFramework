using System;
using Frame.Core;
using UnityEngine;

namespace Frame.Lifecycle
{
    public sealed class LifecycleService : GameModuleBase, ILifecycleService
    {
        public event Action<bool> PauseChanged;

        public event Action<bool> FocusChanged;

        public event Action Quitting;

        public override int Priority
        {
            get { return -950; }
        }

        public bool IsPaused
        {
            get;
            private set;
        }

        public bool HasFocus
        {
            get;
            private set;
        }

        public bool IsQuitting
        {
            get;
            private set;
        }

        protected override void OnInitialize()
        {
            HasFocus = Application.isFocused;
            Context.Services.Register<ILifecycleService>(this);
            Context.Services.Register(this);
        }

        public override void OnApplicationPause(bool paused)
        {
            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            Invoke(PauseChanged, paused);
        }

        public override void OnApplicationFocus(bool focused)
        {
            if (HasFocus == focused)
            {
                return;
            }

            HasFocus = focused;
            Invoke(FocusChanged, focused);
        }

        public override void OnApplicationQuit()
        {
            if (IsQuitting)
            {
                return;
            }

            IsQuitting = true;
            Action handler = Quitting;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler();
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }

        protected override void OnShutdown()
        {
            PauseChanged = null;
            FocusChanged = null;
            Quitting = null;
        }

        private static void Invoke(Action<bool> handler, bool value)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(value);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
            }
        }
    }
}

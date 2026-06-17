using System;

namespace Frame.Lifecycle
{
    public interface ILifecycleService
    {
        event Action<bool> PauseChanged;

        event Action<bool> FocusChanged;

        event Action Quitting;

        bool IsPaused { get; }

        bool HasFocus { get; }

        bool IsQuitting { get; }
    }
}

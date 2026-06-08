using System;

namespace Frame.Utilities
{
    public sealed class DisposableAction : IDisposable
    {
        private Action onDispose;

        public DisposableAction(Action onDispose)
        {
            this.onDispose = onDispose;
        }

        public void Dispose()
        {
            Action action = onDispose;
            if (action == null)
            {
                return;
            }

            onDispose = null;
            action();
        }
    }
}

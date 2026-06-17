using System;

namespace Frame.UI
{
    public abstract class UIPanelBase<TArgs> : UIPanelBase
    {
        protected sealed override void OnOpen(object args)
        {
            if (args == null)
            {
                OnOpen(default(TArgs));
                return;
            }

            if (!(args is TArgs))
            {
                throw new ArgumentException("UI panel args type mismatch. Expected: " + typeof(TArgs).FullName, "args");
            }

            OnOpen((TArgs)args);
        }

        protected virtual void OnOpen(TArgs args)
        {
        }
    }
}

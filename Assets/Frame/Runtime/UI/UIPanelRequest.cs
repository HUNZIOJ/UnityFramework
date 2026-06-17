using UnityEngine;

namespace Frame.UI
{
    public sealed class UIPanelRequest<TPanel> : CustomYieldInstruction where TPanel : UIPanelBase
    {
        public override bool keepWaiting
        {
            get { return !IsDone; }
        }

        public bool IsDone { get; private set; }

        public bool Success
        {
            get { return Panel != null; }
        }

        public TPanel Panel { get; private set; }

        public string Error { get; private set; }

        internal void Complete(TPanel panel, string error = null)
        {
            Panel = panel;
            Error = error;
            IsDone = true;
        }
    }
}

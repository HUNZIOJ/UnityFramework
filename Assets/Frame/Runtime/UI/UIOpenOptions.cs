using UnityEngine;

namespace Frame.UI
{
    public sealed class UIOpenOptions
    {
        public UILayer Layer { get; set; } = UILayer.Normal;

        public bool Cache { get; set; } = true;

        public bool Modal { get; set; }

        public bool CloseOnBackdrop { get; set; }

        public bool AllowBack { get; set; } = true;

        public Color ModalColor { get; set; } = new Color(0f, 0f, 0f, 0.55f);

        public IUITransition Transition { get; set; }

        public static UIOpenOptions Default()
        {
            return new UIOpenOptions();
        }

        public UIOpenOptions Clone()
        {
            return new UIOpenOptions
            {
                Layer = Layer,
                Cache = Cache,
                Modal = Modal,
                CloseOnBackdrop = CloseOnBackdrop,
                AllowBack = AllowBack,
                ModalColor = ModalColor,
                Transition = Transition
            };
        }
    }
}

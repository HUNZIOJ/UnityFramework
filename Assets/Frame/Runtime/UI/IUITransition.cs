using System.Collections;

namespace Frame.UI
{
    public interface IUITransition
    {
        IEnumerator PlayOpen(UIPanelBase panel);

        IEnumerator PlayClose(UIPanelBase panel);
    }
}

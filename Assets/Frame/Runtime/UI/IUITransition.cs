using Cysharp.Threading.Tasks;

namespace Frame.UI
{
    public interface IUITransition
    {
        UniTask PlayOpen(UIPanelBase panel);

        UniTask PlayClose(UIPanelBase panel);
    }
}

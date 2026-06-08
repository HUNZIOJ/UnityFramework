namespace Frame.UI
{
    public interface IUIService
    {
        UIRoot Root { get; }

        TPanel Open<TPanel>(string resourcesPath, UILayer layer = UILayer.Normal, object args = null, bool cache = true)
            where TPanel : UIPanelBase;

        void Close(UIPanelBase panel, bool destroy = false);

        void CloseTop(bool destroy = false);

        void CloseAll(bool destroy = false);
    }
}

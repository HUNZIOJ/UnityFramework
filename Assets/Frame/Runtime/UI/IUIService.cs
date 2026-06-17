namespace Frame.UI
{
    public interface IUIService
    {
        UIRoot Root { get; }

        int QueuedPanelCount { get; }

        TPanel Open<TPanel>(string resourcesPath, UILayer layer = UILayer.Normal, object args = null, bool cache = true)
            where TPanel : UIPanelBase;

        TPanel Open<TPanel>(string resourcesPath, UIOpenOptions options, object args = null)
            where TPanel : UIPanelBase;

        TPanel Open<TPanel, TArgs>(string resourcesPath, TArgs args, UILayer layer = UILayer.Normal, bool cache = true)
            where TPanel : UIPanelBase<TArgs>;

        UIPanelRequest<TPanel> OpenAsync<TPanel>(string resourcesPath, UILayer layer = UILayer.Normal, object args = null, bool cache = true)
            where TPanel : UIPanelBase;

        UIPanelRequest<TPanel> OpenAsync<TPanel>(string resourcesPath, UIOpenOptions options, object args = null)
            where TPanel : UIPanelBase;

        void RegisterRoute<TPanel>(string route, string resourcesPath, UILayer layer = UILayer.Normal, bool cache = true, bool modal = false, bool closeOnBackdrop = false, bool allowBack = true, IUITransition transition = null)
            where TPanel : UIPanelBase;

        void RegisterRoute(UIRoute route);

        bool UnregisterRoute(string route);

        bool HasRoute(string route);

        UIPanelBase OpenRoute(string route, object args = null);

        TPanel OpenRoute<TPanel>(string route, object args = null)
            where TPanel : UIPanelBase;

        TPanel OpenRoute<TPanel, TArgs>(string route, TArgs args)
            where TPanel : UIPanelBase<TArgs>;

        UIPanelRequest<TPanel> OpenRouteAsync<TPanel>(string route, object args = null)
            where TPanel : UIPanelBase;

        UIPanelRequest<UIPanelBase> EnqueueRoute(string route, object args = null);

        UIPanelRequest<TPanel> EnqueueRoute<TPanel>(string route, object args = null)
            where TPanel : UIPanelBase;

        UIPanelRequest<TPanel> EnqueueRoute<TPanel, TArgs>(string route, TArgs args)
            where TPanel : UIPanelBase<TArgs>;

        void ClearQueuedPanels();

        void Close(UIPanelBase panel, bool destroy = false);

        void CloseTop(bool destroy = false);

        void CloseAll(bool destroy = false);

        bool Back(bool destroy = false);
    }
}

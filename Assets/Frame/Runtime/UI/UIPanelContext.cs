namespace Frame.UI
{
    public sealed class UIPanelContext
    {
        public UIPanelContext(UIService service, string assetPath, UILayer layer, object args)
        {
            Service = service;
            AssetPath = assetPath;
            Layer = layer;
            Args = args;
        }

        public UIService Service
        {
            get;
            private set;
        }

        public string AssetPath
        {
            get;
            private set;
        }

        public UILayer Layer
        {
            get;
            private set;
        }

        public object Args
        {
            get;
            private set;
        }
    }
}

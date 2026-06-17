namespace Frame.UI
{
    public sealed class UIPanelContext
    {
        public UIPanelContext(UIService service, string route, string assetPath, UIOpenOptions options, object args)
        {
            Service = service;
            Route = route;
            AssetPath = assetPath;
            Options = options == null ? UIOpenOptions.Default() : options.Clone();
            Args = args;
        }

        public UIService Service
        {
            get;
            private set;
        }

        public string Route
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
            get { return Options.Layer; }
        }

        public object Args
        {
            get;
            private set;
        }

        public UIOpenOptions Options
        {
            get;
            private set;
        }

        public bool IsModal
        {
            get { return Options != null && Options.Modal; }
        }

        public bool AllowBack
        {
            get { return Options == null || Options.AllowBack; }
        }

        public UnityEngine.GameObject ModalBlocker
        {
            get;
            private set;
        }

        public TArgs GetArgs<TArgs>()
        {
            if (Args == null)
            {
                return default(TArgs);
            }

            return (TArgs)Args;
        }

        internal void Update(string route, string assetPath, UIOpenOptions options, object args)
        {
            Route = route;
            AssetPath = assetPath;
            Options = options == null ? UIOpenOptions.Default() : options.Clone();
            Args = args;
        }

        internal void SetModalBlocker(UnityEngine.GameObject blocker)
        {
            ModalBlocker = blocker;
        }
    }
}

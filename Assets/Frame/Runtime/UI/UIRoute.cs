using System;

namespace Frame.UI
{
    public sealed class UIRoute
    {
        public UIRoute(string route, string resourcesPath, Type panelType, UIOpenOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                throw new ArgumentException("UI route is required.", "route");
            }

            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                throw new ArgumentException("UI route resources path is required.", "resourcesPath");
            }

            if (panelType == null || !typeof(UIPanelBase).IsAssignableFrom(panelType))
            {
                throw new ArgumentException("UI route panel type must inherit UIPanelBase.", "panelType");
            }

            Route = route;
            ResourcesPath = resourcesPath;
            PanelType = panelType;
            Options = options == null ? UIOpenOptions.Default() : options.Clone();
        }

        public string Route { get; private set; }

        public string ResourcesPath { get; private set; }

        public Type PanelType { get; private set; }

        public UIOpenOptions Options { get; private set; }
    }
}

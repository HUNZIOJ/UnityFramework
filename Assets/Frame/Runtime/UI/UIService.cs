using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Frame.Assets;
using Frame.Core;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Frame.UI
{
    public sealed class UIService : GameModuleBase, IUIService
    {
        private readonly Dictionary<string, UIPanelBase> cachedPanels = new Dictionary<string, UIPanelBase>();
        private readonly Dictionary<string, UIRoute> routes = new Dictionary<string, UIRoute>();
        private readonly Queue<QueuedPanelOpen> queuedPanels = new Queue<QueuedPanelOpen>();
        private readonly List<UIPanelBase> stack = new List<UIPanelBase>();
        private IAssetService assets;
        private UIRoot root;
        private UIPanelBase queuedActivePanel;
        private bool suppressQueuedOpen;

        public override int Priority
        {
            get { return -400; }
        }

        public UIRoot Root
        {
            get { return root; }
        }

        public int QueuedPanelCount
        {
            get { return queuedPanels.Count; }
        }

        protected override void OnInitialize()
        {
            Context.Services.TryResolve(out assets);
            CreateRoot();
            Context.Services.Register<IUIService>(this);
            Context.Services.Register(this);
        }

        public TPanel Open<TPanel>(string resourcesPath, UILayer layer = UILayer.Normal, object args = null, bool cache = true)
            where TPanel : UIPanelBase
        {
            UIOpenOptions options = UIOpenOptions.Default();
            options.Layer = layer;
            options.Cache = cache;
            return Open<TPanel>(resourcesPath, options, args);
        }

        public TPanel Open<TPanel>(string resourcesPath, UIOpenOptions options, object args = null)
            where TPanel : UIPanelBase
        {
            return OpenInternal(typeof(TPanel), null, resourcesPath, options, args) as TPanel;
        }

        public TPanel Open<TPanel, TArgs>(string resourcesPath, TArgs args, UILayer layer = UILayer.Normal, bool cache = true)
            where TPanel : UIPanelBase<TArgs>
        {
            return Open<TPanel>(resourcesPath, layer, args, cache);
        }

        public UIPanelRequest<TPanel> OpenAsync<TPanel>(string resourcesPath, UILayer layer = UILayer.Normal, object args = null, bool cache = true)
            where TPanel : UIPanelBase
        {
            UIOpenOptions options = UIOpenOptions.Default();
            options.Layer = layer;
            options.Cache = cache;
            return OpenAsync<TPanel>(resourcesPath, options, args);
        }

        public UIPanelRequest<TPanel> OpenAsync<TPanel>(string resourcesPath, UIOpenOptions options, object args = null)
            where TPanel : UIPanelBase
        {
            UIPanelRequest<TPanel> request = new UIPanelRequest<TPanel>();
            OpenInternalAsync(typeof(TPanel), null, resourcesPath, options, args, request);
            return request;
        }

        public void RegisterRoute<TPanel>(string route, string resourcesPath, UILayer layer = UILayer.Normal, bool cache = true, bool modal = false, bool closeOnBackdrop = false, bool allowBack = true, IUITransition transition = null)
            where TPanel : UIPanelBase
        {
            UIOpenOptions options = UIOpenOptions.Default();
            options.Layer = layer;
            options.Cache = cache;
            options.Modal = modal;
            options.CloseOnBackdrop = closeOnBackdrop;
            options.AllowBack = allowBack;
            options.Transition = transition;
            RegisterRoute(new UIRoute(route, resourcesPath, typeof(TPanel), options));
        }

        public void RegisterRoute(UIRoute route)
        {
            if (route == null)
            {
                throw new ArgumentNullException("route");
            }

            routes[route.Route] = route;
        }

        public bool UnregisterRoute(string route)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                return false;
            }

            return routes.Remove(route);
        }

        public bool HasRoute(string route)
        {
            return !string.IsNullOrWhiteSpace(route) && routes.ContainsKey(route);
        }

        public UIPanelBase OpenRoute(string route, object args = null)
        {
            UIRoute uiRoute = GetRoute(route);
            return OpenInternal(uiRoute.PanelType, uiRoute.Route, uiRoute.ResourcesPath, uiRoute.Options, args);
        }

        public TPanel OpenRoute<TPanel>(string route, object args = null)
            where TPanel : UIPanelBase
        {
            UIRoute uiRoute = GetRoute(route);
            ValidateRoutePanelType<TPanel>(uiRoute);
            return OpenInternal(typeof(TPanel), uiRoute.Route, uiRoute.ResourcesPath, uiRoute.Options, args) as TPanel;
        }

        public TPanel OpenRoute<TPanel, TArgs>(string route, TArgs args)
            where TPanel : UIPanelBase<TArgs>
        {
            return OpenRoute<TPanel>(route, args);
        }

        public UIPanelRequest<TPanel> OpenRouteAsync<TPanel>(string route, object args = null)
            where TPanel : UIPanelBase
        {
            UIRoute uiRoute = GetRoute(route);
            ValidateRoutePanelType<TPanel>(uiRoute);
            UIPanelRequest<TPanel> request = new UIPanelRequest<TPanel>();
            OpenInternalAsync(typeof(TPanel), uiRoute.Route, uiRoute.ResourcesPath, uiRoute.Options, args, request);
            return request;
        }

        public UIPanelRequest<UIPanelBase> EnqueueRoute(string route, object args = null)
        {
            UIRoute uiRoute = GetRoute(route);
            UIPanelRequest<UIPanelBase> request = new UIPanelRequest<UIPanelBase>();
            queuedPanels.Enqueue(new QueuedPanelOpen(uiRoute.PanelType, route, args, request));
            OpenNextQueuedPanel();
            return request;
        }

        public UIPanelRequest<TPanel> EnqueueRoute<TPanel>(string route, object args = null)
            where TPanel : UIPanelBase
        {
            UIRoute uiRoute = GetRoute(route);
            ValidateRoutePanelType(typeof(TPanel), uiRoute);
            UIPanelRequest<TPanel> request = new UIPanelRequest<TPanel>();
            queuedPanels.Enqueue(new QueuedPanelOpen<TPanel>(typeof(TPanel), route, args, request));
            OpenNextQueuedPanel();
            return request;
        }

        public UIPanelRequest<TPanel> EnqueueRoute<TPanel, TArgs>(string route, TArgs args)
            where TPanel : UIPanelBase<TArgs>
        {
            return EnqueueRoute<TPanel>(route, args);
        }

        public void ClearQueuedPanels()
        {
            while (queuedPanels.Count > 0)
            {
                queuedPanels.Dequeue().Complete(null, "UI panel queue was cleared.");
            }
        }

        public void Close(UIPanelBase panel, bool destroy = false)
        {
            CloseInternal(panel, destroy, false);
        }

        public void CloseTop(bool destroy = false)
        {
            if (stack.Count == 0)
            {
                return;
            }

            Close(stack[stack.Count - 1], destroy);
        }

        public void CloseAll(bool destroy = false)
        {
            ClearQueuedPanels();
            suppressQueuedOpen = true;
            try
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    Close(stack[i], destroy);
                }
            }
            finally
            {
                suppressQueuedOpen = false;
            }
        }

        public bool Back(bool destroy = false)
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                UIPanelBase panel = stack[i];
                if (panel != null && panel.Context != null && panel.Context.AllowBack)
                {
                    Close(panel, destroy);
                    return true;
                }
            }

            return false;
        }

        protected override void OnShutdown()
        {
            suppressQueuedOpen = true;
            ClearQueuedPanels();
            CloseAllImmediate(true);
            cachedPanels.Clear();
            routes.Clear();
            stack.Clear();
            queuedActivePanel = null;
            suppressQueuedOpen = false;
            root = null;
            assets = null;
        }

        private UIPanelBase OpenInternal(Type panelType, string route, string resourcesPath, UIOpenOptions options, object args)
        {
            ValidateOpen(panelType, resourcesPath);
            UIOpenOptions resolvedOptions = ResolveOptions(options);
            string cacheKey = GetCacheKey(route, resourcesPath);

            UIPanelBase cachedPanel;
            if (resolvedOptions.Cache && cachedPanels.TryGetValue(cacheKey, out cachedPanel) && cachedPanel != null)
            {
                if (!panelType.IsInstanceOfType(cachedPanel))
                {
                    FrameLog.Warning("Cached UI panel type mismatch: " + cacheKey + " expected=" + panelType.Name);
                    return null;
                }

                try
                {
                    cachedPanel.Context.Update(route, resourcesPath, resolvedOptions, args);
                    PrepareModalBlocker(cachedPanel, resolvedOptions);
                    cachedPanel.InternalOpen(args);
                    BringToTop(cachedPanel);
                    PlayOpenTransition(cachedPanel, resolvedOptions);
                    return cachedPanel;
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                    RemoveModalBlocker(cachedPanel);
                    cachedPanel.InternalClose();
                    return null;
                }
            }

            GameObject instance = assets == null ? null : assets.Instantiate(resourcesPath, root.GetLayer(resolvedOptions.Layer), false);
            if (instance == null)
            {
                FrameLog.Warning("Failed to open UI: " + resourcesPath);
                return null;
            }

            return CreatePanelFromInstance(panelType, route, resourcesPath, resolvedOptions, args, cacheKey, instance);
        }

        private void OpenInternalAsync<TPanel>(Type panelType, string route, string resourcesPath, UIOpenOptions options, object args, UIPanelRequest<TPanel> request)
            where TPanel : UIPanelBase
        {
            ValidateOpen(panelType, resourcesPath);
            UIOpenOptions resolvedOptions = ResolveOptions(options);
            string cacheKey = GetCacheKey(route, resourcesPath);

            UIPanelBase cachedPanel;
            if (resolvedOptions.Cache && cachedPanels.TryGetValue(cacheKey, out cachedPanel) && cachedPanel != null)
            {
                UIPanelBase panel = OpenInternal(panelType, route, resourcesPath, resolvedOptions, args);
                request.Complete(panel as TPanel, panel == null ? "Cached UI panel type mismatch." : null);
                return;
            }

            if (assets == null)
            {
                request.Complete(null, "Asset service is not available.");
                return;
            }

            assets.LoadAsync<GameObject>(resourcesPath, handle =>
            {
                try
                {
                    if (handle == null || !handle.IsValid)
                    {
                        request.Complete(null, "Failed to load UI asset: " + resourcesPath);
                        return;
                    }

                    GameObject instance = Object.Instantiate(handle.Asset, root.GetLayer(resolvedOptions.Layer), false);
                    handle.Release();
                    UIPanelBase panel = CreatePanelFromInstance(panelType, route, resourcesPath, resolvedOptions, args, cacheKey, instance);
                    request.Complete(panel as TPanel, panel == null ? "Failed to create UI panel: " + resourcesPath : null);
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                    request.Complete(null, exception.Message);
                }
            });
        }

        private UIPanelBase CreatePanelFromInstance(Type panelType, string route, string resourcesPath, UIOpenOptions options, object args, string cacheKey, GameObject instance)
        {
            UIPanelBase panel = instance.GetComponent(panelType) as UIPanelBase;
            if (panel == null)
            {
                Object.Destroy(instance);
                FrameLog.Warning("UI prefab does not contain panel component: " + resourcesPath + " type=" + panelType.Name);
                return null;
            }

            RectTransform rect = instance.GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = instance.AddComponent<RectTransform>();
            }

            rect.SetParent(root.GetLayer(options.Layer), false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            try
            {
                panel.InternalCreate(new UIPanelContext(this, route, resourcesPath, options, args));
                PrepareModalBlocker(panel, options);
                panel.InternalOpen(args);
            }
            catch (Exception exception)
            {
                FrameLog.Exception(exception);
                RemoveModalBlocker(panel);
                panel.InternalClose(false);
                panel.InternalDispose();
                Object.Destroy(instance);
                return null;
            }

            stack.Add(panel);
            BringToTop(panel);

            if (options.Cache)
            {
                cachedPanels[cacheKey] = panel;
            }

            PlayOpenTransition(panel, options);
            return panel;
        }

        private void CloseInternal(UIPanelBase panel, bool destroy, bool immediate)
        {
            if (panel == null)
            {
                return;
            }

            UIOpenOptions options = panel.Context == null ? UIOpenOptions.Default() : panel.Context.Options;
            stack.Remove(panel);
            RemoveModalBlocker(panel);
            bool wasOpen = panel.InternalClose(false);
            if (!wasOpen && !destroy)
            {
                return;
            }

            if (!immediate && options != null && options.Transition != null && root != null && panel.gameObject.activeInHierarchy)
            {
                CloseWithTransition(panel, destroy, options.Transition).Forget();
                return;
            }

            FinishClose(panel, destroy);
        }

        private async UniTaskVoid CloseWithTransition(UIPanelBase panel, bool destroy, IUITransition transition)
        {
            await transition.PlayClose(panel);
            FinishClose(panel, destroy);
        }

        private void FinishClose(UIPanelBase panel, bool destroy)
        {
            if (panel == null)
            {
                return;
            }

            bool wasQueuedActive = panel == queuedActivePanel;
            panel.InternalSetClosed();
            if (destroy)
            {
                RemoveCached(panel);
                panel.InternalDispose();
                Object.Destroy(panel.gameObject);
            }

            if (wasQueuedActive)
            {
                queuedActivePanel = null;
                if (!suppressQueuedOpen)
                {
                    OpenNextQueuedPanel();
                }
            }
        }

        private void CloseAllImmediate(bool destroy)
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                CloseInternal(stack[i], destroy, true);
            }
        }

        private void CreateRoot()
        {
            GameObject go = new GameObject(Context.Settings.UIRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(Context.Root, false);
            root = go.GetComponent<UIRoot>();
            if (root == null)
            {
                root = go.AddComponent<UIRoot>();
            }

            root.Initialize(Context.Settings);
        }

        private void PrepareModalBlocker(UIPanelBase panel, UIOpenOptions options)
        {
            RemoveModalBlocker(panel);
            if (panel == null || panel.Context == null || options == null || !options.Modal)
            {
                return;
            }

            RectTransform layer = root.GetLayer(options.Layer);
            GameObject blocker = new GameObject(panel.name + "_ModalBlocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = blocker.GetComponent<RectTransform>();
            rect.SetParent(layer, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = blocker.GetComponent<Image>();
            image.color = options.ModalColor;
            image.raycastTarget = true;

            if (options.CloseOnBackdrop)
            {
                Button button = blocker.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => Close(panel));
            }

            panel.Context.SetModalBlocker(blocker);
        }

        private void RemoveModalBlocker(UIPanelBase panel)
        {
            if (panel == null || panel.Context == null || panel.Context.ModalBlocker == null)
            {
                return;
            }

            Object.Destroy(panel.Context.ModalBlocker);
            panel.Context.SetModalBlocker(null);
        }

        private void BringToTop(UIPanelBase panel)
        {
            stack.Remove(panel);
            stack.Add(panel);
            panel.transform.SetAsLastSibling();
        }

        private void PlayOpenTransition(UIPanelBase panel, UIOpenOptions options)
        {
            if (panel == null || options == null || options.Transition == null || root == null || !panel.gameObject.activeInHierarchy)
            {
                return;
            }

            options.Transition.PlayOpen(panel).Forget();
        }

        private void RemoveCached(UIPanelBase panel)
        {
            string removeKey = null;
            foreach (KeyValuePair<string, UIPanelBase> pair in cachedPanels)
            {
                if (pair.Value == panel)
                {
                    removeKey = pair.Key;
                    break;
                }
            }

            if (removeKey != null)
            {
                cachedPanels.Remove(removeKey);
            }
        }

        private UIRoute GetRoute(string route)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                throw new FrameException("UI route is empty.");
            }

            UIRoute uiRoute;
            if (!routes.TryGetValue(route, out uiRoute))
            {
                throw new FrameException("UI route is not registered: " + route);
            }

            return uiRoute;
        }

        private static void ValidateRoutePanelType<TPanel>(UIRoute route) where TPanel : UIPanelBase
        {
            ValidateRoutePanelType(typeof(TPanel), route);
        }

        private static void ValidateRoutePanelType(Type expectedType, UIRoute route)
        {
            if (expectedType == null || expectedType == typeof(UIPanelBase))
            {
                return;
            }

            if (!expectedType.IsAssignableFrom(route.PanelType) && route.PanelType != expectedType)
            {
                throw new FrameException("UI route panel type mismatch: " + route.Route);
            }
        }

        private static void ValidateOpen(Type panelType, string resourcesPath)
        {
            if (panelType == null || !typeof(UIPanelBase).IsAssignableFrom(panelType))
            {
                throw new FrameException("UI panel type must inherit UIPanelBase.");
            }

            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                throw new FrameException("UI resources path is empty.");
            }
        }

        private static UIOpenOptions ResolveOptions(UIOpenOptions options)
        {
            return options == null ? UIOpenOptions.Default() : options.Clone();
        }

        private static string GetCacheKey(string route, string resourcesPath)
        {
            return string.IsNullOrWhiteSpace(route) ? resourcesPath : route;
        }

        private void OpenNextQueuedPanel()
        {
            if (queuedActivePanel != null && queuedActivePanel.IsOpen)
            {
                return;
            }

            while (queuedPanels.Count > 0)
            {
                QueuedPanelOpen queued = queuedPanels.Dequeue();
                try
                {
                    UIRoute uiRoute = GetRoute(queued.Route);
                    ValidateRoutePanelType(queued.PanelType, uiRoute);
                    UIPanelBase panel = OpenInternal(queued.PanelType, uiRoute.Route, uiRoute.ResourcesPath, uiRoute.Options, queued.Args);
                    if (panel == null)
                    {
                        queued.Complete(null, "Failed to open queued UI route: " + queued.Route);
                        continue;
                    }

                    queuedActivePanel = panel;
                    queued.Complete(panel, null);
                    return;
                }
                catch (Exception exception)
                {
                    FrameLog.Exception(exception);
                    queued.Complete(null, exception.Message);
                }
            }
        }

        private class QueuedPanelOpen
        {
            private readonly UIPanelRequest<UIPanelBase> request;

            public QueuedPanelOpen(Type panelType, string route, object args, UIPanelRequest<UIPanelBase> request)
            {
                PanelType = panelType;
                Route = route;
                Args = args;
                this.request = request;
            }

            public Type PanelType { get; private set; }

            public string Route { get; private set; }

            public object Args { get; private set; }

            public virtual void Complete(UIPanelBase panel, string error)
            {
                request.Complete(panel, error);
            }
        }

        private sealed class QueuedPanelOpen<TPanel> : QueuedPanelOpen where TPanel : UIPanelBase
        {
            private readonly UIPanelRequest<TPanel> request;

            public QueuedPanelOpen(Type panelType, string route, object args, UIPanelRequest<TPanel> request)
                : base(panelType, route, args, null)
            {
                this.request = request;
            }

            public override void Complete(UIPanelBase panel, string error)
            {
                request.Complete(panel as TPanel, error);
            }
        }
    }
}

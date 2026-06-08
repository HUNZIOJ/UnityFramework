using System.Collections.Generic;
using Frame.Assets;
using Frame.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.UI
{
    public sealed class UIService : GameModuleBase, IUIService
    {
        private readonly Dictionary<string, UIPanelBase> cachedPanels = new Dictionary<string, UIPanelBase>();
        private readonly List<UIPanelBase> stack = new List<UIPanelBase>();
        private IAssetService assets;
        private UIRoot root;

        public override int Priority
        {
            get { return -400; }
        }

        public UIRoot Root
        {
            get { return root; }
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
            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                throw new FrameException("UI resources path is empty.");
            }

            UIPanelBase panel;
            if (cache && cachedPanels.TryGetValue(resourcesPath, out panel) && panel != null)
            {
                panel.InternalOpen(args);
                BringToTop(panel);
                return panel as TPanel;
            }

            GameObject instance = assets == null ? null : assets.Instantiate(resourcesPath, root.GetLayer(layer), false);
            if (instance == null)
            {
                FrameLog.Warning("Failed to open UI: " + resourcesPath);
                return null;
            }

            TPanel typedPanel = instance.GetComponent<TPanel>();
            if (typedPanel == null)
            {
                Object.Destroy(instance);
                FrameLog.Warning("UI prefab does not contain panel component: " + resourcesPath + " type=" + typeof(TPanel).Name);
                return null;
            }

            RectTransform rect = instance.GetComponent<RectTransform>();
            rect.SetParent(root.GetLayer(layer), false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            typedPanel.InternalCreate(new UIPanelContext(this, resourcesPath, layer, args));
            typedPanel.InternalOpen(args);
            stack.Add(typedPanel);

            if (cache)
            {
                cachedPanels[resourcesPath] = typedPanel;
            }

            return typedPanel;
        }

        public void Close(UIPanelBase panel, bool destroy = false)
        {
            if (panel == null)
            {
                return;
            }

            panel.InternalClose();
            stack.Remove(panel);

            if (destroy)
            {
                RemoveCached(panel);
                panel.InternalDispose();
                Object.Destroy(panel.gameObject);
            }
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
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                Close(stack[i], destroy);
            }
        }

        protected override void OnShutdown()
        {
            CloseAll(true);
            cachedPanels.Clear();
            stack.Clear();
            root = null;
            assets = null;
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

        private void BringToTop(UIPanelBase panel)
        {
            stack.Remove(panel);
            stack.Add(panel);
            panel.transform.SetAsLastSibling();
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
    }
}

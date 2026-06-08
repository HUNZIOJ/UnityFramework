using System.Collections.Generic;
using Frame.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Frame.UI
{
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    [RequireComponent(typeof(GraphicRaycaster))]
    public sealed class UIRoot : MonoBehaviour
    {
        private readonly Dictionary<UILayer, RectTransform> layers = new Dictionary<UILayer, RectTransform>();

        public Canvas Canvas
        {
            get;
            private set;
        }

        public void Initialize(FrameSettings settings)
        {
            Canvas = GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 0;

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = settings.UIReferenceResolution;
            scaler.matchWidthOrHeight = settings.UIMatchWidthOrHeight;

            GetComponent<GraphicRaycaster>();
            EnsureEventSystem();
            EnsureAllLayers();
        }

        public RectTransform GetLayer(UILayer layer)
        {
            RectTransform transform;
            if (layers.TryGetValue(layer, out transform) && transform != null)
            {
                return transform;
            }

            GameObject go = new GameObject(layer.ToString(), typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(base.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Canvas layerCanvas = go.GetComponent<Canvas>();
            layerCanvas.overrideSorting = true;
            layerCanvas.sortingOrder = (int)layer;

            layers[layer] = rect;
            return rect;
        }

        private void EnsureAllLayers()
        {
            GetLayer(UILayer.Background);
            GetLayer(UILayer.Normal);
            GetLayer(UILayer.Popup);
            GetLayer(UILayer.Tips);
            GetLayer(UILayer.Loading);
            GetLayer(UILayer.System);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
#else
            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            Object.DontDestroyOnLoad(go);
        }
    }
}

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Frame.Core;
using Frame.Preferences;
using UnityEngine;
using UnityEngine.UI;

namespace Frame.Runtime.Guide
{
    /// <summary>
    /// 引导系统核心服务层。
    /// 负责状态流转、遮罩生成、动态等待 UI 目标出现。
    /// </summary>
    public sealed class GuideService : GameModuleBase, IGuideService
    {
        public override int Priority
        {
            get { return -350; }
        }

        public bool IsGuiding { get; private set; }

        private UIGuideMask currentMask;
        private RectTransform currentDialogue;
        private Text currentDialogueText;
        private GameObject currentDialoguePrefab;
        private CancellationTokenSource guideCts;
        private Action onGuideComplete;
        private string expectedCustomEventName;
        private IPreferencesService preferences;
        
        // 用于拦截 CustomEvent 和 AutoNext 的信号
        private UniTaskCompletionSource waitSignalSource;

        protected override void OnInitialize()
        {
            Context.Services.TryResolve(out preferences);
            Context.Services.Register<IGuideService>(this);
            Context.Services.Register(this);
        }

        protected override void OnShutdown()
        {
            StopGuide();
        }

        public void StartGuide(GuideConfig config, Action onComplete = null)
        {
            if (IsGuiding)
            {
                Debug.LogWarning("[Guide] 当前已有引导正在进行中，请先停止。");
                return;
            }

            if (config == null || config.Steps.Count == 0) return;

            if (ShouldPersist(config) && IsGuideCompleted(config.GuideGroupId))
            {
                onComplete?.Invoke();
                return;
            }

            IsGuiding = true;
            onGuideComplete = onComplete;
            guideCts = new CancellationTokenSource();

            // 启动异步引导流
            RunGuideFlowAsync(config, guideCts.Token).Forget();
        }

        public void StopGuide()
        {
            if (!IsGuiding) return;

            guideCts?.Cancel();
            guideCts?.Dispose();
            guideCts = null;
            waitSignalSource = null;
            expectedCustomEventName = null;

            Cleanup();
            IsGuiding = false;
        }

        public void NotifyCustomEvent(string eventName)
        {
            if (!IsGuiding || waitSignalSource == null) return;

            if (!string.IsNullOrEmpty(expectedCustomEventName) &&
                !string.Equals(expectedCustomEventName, eventName, StringComparison.Ordinal))
            {
                return;
            }

            waitSignalSource.TrySetResult();
        }

        public int GetGuideProgress(int guideGroupId)
        {
            if (!IsValidGuideGroupId(guideGroupId))
            {
                return 0;
            }

            return Mathf.Max(0, GetInt(GetProgressKey(guideGroupId), 0));
        }

        public bool IsGuideCompleted(int guideGroupId)
        {
            return IsValidGuideGroupId(guideGroupId) && GetBool(GetCompletedKey(guideGroupId), false);
        }

        public void ResetGuideProgress(int guideGroupId)
        {
            if (!IsValidGuideGroupId(guideGroupId))
            {
                return;
            }

            DeleteKey(GetProgressKey(guideGroupId));
            DeleteKey(GetCompletedKey(guideGroupId));
            SavePreferences();
        }

        private async UniTaskVoid RunGuideFlowAsync(GuideConfig config, CancellationToken token)
        {
            try
            {
                CreateGuideMask();

                int startIndex = GetStartStepIndex(config);
                for (int i = startIndex; i < config.Steps.Count; i++)
                {
                    SaveGuideProgress(config, i);
                    GuideStep step = config.Steps[i];
                    await ProcessStepAsync(step, config.DialoguePrefab, token);
                    SaveGuideProgress(config, i + 1);
                }

                // 全部步骤完成
                MarkGuideCompleted(config);
                onGuideComplete?.Invoke();
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[Guide] 引导流程被强行中断。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Guide] 引导流程发生异常: {ex}");
            }
            finally
            {
                StopGuide();
            }
        }

        private async UniTask ProcessStepAsync(GuideStep step, GameObject dialoguePrefab, CancellationToken token)
        {
            GuideTarget target = null;
            Vector2 dialogueAnchor = Vector2.zero;

            // 1. 动态寻址：如果需要高亮目标，死等这个 UI 目标出现（比如等玩家打开背包界面）
            if (!string.IsNullOrEmpty(step.TargetId))
            {
                currentMask.ClearTarget(); // 寻找期间黑屏等待，拦截乱点
                
                // 【核心亮点】利用 UniTask 的极简轮询等待目标出现，彻底解耦
                await UniTask.WaitUntil(() => GuideTarget.GetTarget(step.TargetId) != null, cancellationToken: token);
                target = GuideTarget.GetTarget(step.TargetId);

                // 2. 挖孔定位
                RectTransform targetRect = target.RectTransform;
                // 计算在 Canvas 根节点下的相对坐标 (假设 Mask 也是铺满根 Canvas 的)
                Vector2 center = GetCenterPositionInCanvas(targetRect, currentMask.rectTransform);
                Vector2 size = targetRect.rect.size + step.Padding;
                dialogueAnchor = center;

                currentMask.SetTarget(center, size, step.MaskShape, step.CornerRadius);
            }
            else
            {
                currentMask.ClearTarget();
            }

            ShowDialogue(step, dialoguePrefab, dialogueAnchor);

            // 4. 等待触发完成条件
            waitSignalSource = new UniTaskCompletionSource();

            try
            {
                if (step.TriggerType == GuideTriggerType.ClickTarget && target != null)
                {
                    // 等待真正的目标按钮被点击。由于点击穿透了镂空遮罩，目标本身的 Button 会响应。
                    Button btn = target.GetComponent<Button>();
                    if (btn != null)
                    {
                        var listener = btn.onClick.GetAsyncEventHandler(token);
                        await listener.OnInvokeAsync();
                    }
                    else
                    {
                        Debug.LogWarning($"[Guide] 目标 {step.TargetId} 没有 Button 组件，无法监听点击！直接跳过。");
                        await UniTask.Delay(500, cancellationToken: token);
                    }
                }
                else if (step.TriggerType == GuideTriggerType.AutoNext)
                {
                    // 等待全局点击任意屏幕位置
                    Button maskBtn = CreateAutoNextButton();
                    try
                    {
                        var listener = maskBtn.onClick.GetAsyncEventHandler(token);
                        await listener.OnInvokeAsync();
                    }
                    finally
                    {
                        if (maskBtn != null)
                        {
                            UnityEngine.Object.Destroy(maskBtn.gameObject);
                        }
                    }
                }
                else if (step.TriggerType == GuideTriggerType.CustomEvent)
                {
                    // 等待外界调用 NotifyCustomEvent
                    expectedCustomEventName = step.CustomEventName;
                    await waitSignalSource.Task.AttachExternalCancellation(token);
                }
            }
            finally
            {
                HideDialogue();
                expectedCustomEventName = null;
                waitSignalSource = null;
            }
        }

        private void ShowDialogue(GuideStep step, GameObject dialoguePrefab, Vector2 anchor)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.DialogueText))
            {
                HideDialogue();
                return;
            }

            EnsureDialogue(dialoguePrefab);
            currentDialogue.anchoredPosition = anchor + step.DialogueOffset;
            currentDialogueText.text = step.DialogueText;
            currentDialogue.gameObject.SetActive(true);
            currentDialogue.SetAsLastSibling();
        }

        private void EnsureDialogue(GameObject dialoguePrefab)
        {
            if (currentDialogue != null && currentDialoguePrefab == dialoguePrefab) return;

            DestroyDialogue();

            if (dialoguePrefab != null)
            {
                CreateDialogueFromPrefab(dialoguePrefab);
            }
            else
            {
                CreateDefaultDialogue();
            }

            currentDialoguePrefab = dialoguePrefab;
        }

        private void CreateDefaultDialogue()
        {
            GameObject dialogueObj = new GameObject("GuideDialogue");
            dialogueObj.transform.SetParent(currentMask.transform.parent, false);
            currentDialogue = dialogueObj.AddComponent<RectTransform>();
            currentDialogue.sizeDelta = new Vector2(420f, 120f);

            Image background = dialogueObj.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.85f);
            background.raycastTarget = false;

            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(dialogueObj.transform, false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 14f);
            textRect.offsetMax = new Vector2(-18f, -14f);

            currentDialogueText = textObj.AddComponent<Text>();
            currentDialogueText.font = GetDefaultFont();
            currentDialogueText.fontSize = 26;
            currentDialogueText.color = Color.white;
            currentDialogueText.alignment = TextAnchor.MiddleCenter;
            currentDialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
            currentDialogueText.verticalOverflow = VerticalWrapMode.Truncate;
            currentDialogueText.raycastTarget = false;
        }

        private void CreateDialogueFromPrefab(GameObject dialoguePrefab)
        {
            GameObject dialogueObj = UnityEngine.Object.Instantiate(dialoguePrefab, currentMask.transform.parent, false);
            dialogueObj.name = "GuideDialogue";

            currentDialogue = dialogueObj.GetComponent<RectTransform>();
            if (currentDialogue == null)
            {
                currentDialogue = dialogueObj.AddComponent<RectTransform>();
            }

            currentDialogueText = dialogueObj.GetComponentInChildren<Text>(true);
            if (currentDialogueText == null)
            {
                CreateDefaultDialogueText(dialogueObj.transform);
            }
        }

        private void CreateDefaultDialogueText(Transform parent)
        {
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(parent, false);
            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 14f);
            textRect.offsetMax = new Vector2(-18f, -14f);

            currentDialogueText = textObj.AddComponent<Text>();
            currentDialogueText.font = GetDefaultFont();
            currentDialogueText.fontSize = 26;
            currentDialogueText.color = Color.white;
            currentDialogueText.alignment = TextAnchor.MiddleCenter;
            currentDialogueText.horizontalOverflow = HorizontalWrapMode.Wrap;
            currentDialogueText.verticalOverflow = VerticalWrapMode.Truncate;
            currentDialogueText.raycastTarget = false;
        }

        private void HideDialogue()
        {
            if (currentDialogue != null)
            {
                currentDialogue.gameObject.SetActive(false);
            }
        }

        private void DestroyDialogue()
        {
            if (currentDialogue != null)
            {
                UnityEngine.Object.Destroy(currentDialogue.gameObject);
                currentDialogue = null;
                currentDialogueText = null;
                currentDialoguePrefab = null;
            }
        }

        private static Font GetDefaultFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return font;
        }

        private Button CreateAutoNextButton()
        {
            GameObject clickObj = new GameObject("GuideAutoNextClickLayer");
            clickObj.transform.SetParent(currentMask.transform.parent, false);
            clickObj.transform.SetAsLastSibling();

            RectTransform rect = clickObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = clickObj.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;

            Button button = clickObj.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = image;
            return button;
        }

        private void CreateGuideMask()
        {
            if (currentMask != null) return;

            // 动态创建一个最高层级的 Canvas
            GameObject maskObj = new GameObject("GuideMaskLayer");
            UnityEngine.Object.DontDestroyOnLoad(maskObj);

            Canvas canvas = maskObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000; // 极高的层级，确保在所有 UI 之上

            CanvasScaler scaler = maskObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            maskObj.AddComponent<GraphicRaycaster>();

            // 添加核心的遮罩组件
            GameObject maskContent = new GameObject("UIGuideMask");
            maskContent.transform.SetParent(maskObj.transform, false);
            currentMask = maskContent.AddComponent<UIGuideMask>();
            currentMask.color = new Color(0, 0, 0, 0.7f); // 半透明黑幕
            
            // 铺满全屏
            currentMask.rectTransform.anchorMin = Vector2.zero;
            currentMask.rectTransform.anchorMax = Vector2.one;
            currentMask.rectTransform.offsetMin = Vector2.zero;
            currentMask.rectTransform.offsetMax = Vector2.zero;
        }

        private void Cleanup()
        {
            if (currentMask != null && currentMask.transform.parent != null)
            {
                UnityEngine.Object.Destroy(currentMask.transform.parent.gameObject);
                currentMask = null;
                currentDialogue = null;
                currentDialogueText = null;
                currentDialoguePrefab = null;
            }
        }

        private int GetStartStepIndex(GuideConfig config)
        {
            if (!ShouldPersist(config))
            {
                return 0;
            }

            return Mathf.Clamp(GetGuideProgress(config.GuideGroupId), 0, config.Steps.Count);
        }

        private void SaveGuideProgress(GuideConfig config, int stepIndex)
        {
            if (!ShouldPersist(config))
            {
                return;
            }

            SetInt(GetProgressKey(config.GuideGroupId), Mathf.Max(0, stepIndex));
            SetBool(GetCompletedKey(config.GuideGroupId), false);
            SavePreferences();
        }

        private void MarkGuideCompleted(GuideConfig config)
        {
            if (!ShouldPersist(config))
            {
                return;
            }

            SetInt(GetProgressKey(config.GuideGroupId), config.Steps.Count);
            SetBool(GetCompletedKey(config.GuideGroupId), true);
            SavePreferences();
        }

        private static bool ShouldPersist(GuideConfig config)
        {
            return config != null && config.PersistProgress && IsValidGuideGroupId(config.GuideGroupId);
        }

        private static bool IsValidGuideGroupId(int guideGroupId)
        {
            return guideGroupId > 0;
        }

        private static string GetProgressKey(int guideGroupId)
        {
            return $"Frame.Guide.{guideGroupId}.Progress";
        }

        private static string GetCompletedKey(int guideGroupId)
        {
            return $"Frame.Guide.{guideGroupId}.Completed";
        }

        private int GetInt(string key, int fallback)
        {
            return preferences != null ? preferences.GetInt(key, fallback) : PlayerPrefs.GetInt(key, fallback);
        }

        private void SetInt(string key, int value)
        {
            if (preferences != null)
            {
                preferences.SetInt(key, value);
            }
            else
            {
                PlayerPrefs.SetInt(key, value);
            }
        }

        private bool GetBool(string key, bool fallback)
        {
            return preferences != null ? preferences.GetBool(key, fallback) : PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0;
        }

        private void SetBool(string key, bool value)
        {
            if (preferences != null)
            {
                preferences.SetBool(key, value);
            }
            else
            {
                PlayerPrefs.SetInt(key, value ? 1 : 0);
            }
        }

        private void DeleteKey(string key)
        {
            if (preferences != null)
            {
                preferences.DeleteKey(key);
            }
            else
            {
                PlayerPrefs.DeleteKey(key);
            }
        }

        private void SavePreferences()
        {
            if (preferences != null)
            {
                preferences.Save();
            }
            else
            {
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// 坐标系转换工具：求目标UI在Mask所属Canvas下的坐标
        /// </summary>
        private Vector2 GetCenterPositionInCanvas(RectTransform target, RectTransform maskTransform)
        {
            // 将目标的中心点转换为世界坐标
            Vector3 worldPos = target.TransformPoint(target.rect.center);
            // 将世界坐标转换到 Mask 的本地坐标系中
            RectTransformUtility.ScreenPointToLocalPointInRectangle(maskTransform, RectTransformUtility.WorldToScreenPoint(null, worldPos), null, out Vector2 localPos);
            return localPos;
        }
    }
}

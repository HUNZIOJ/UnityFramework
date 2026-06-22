using System.Collections;
using Frame.Core;
using Frame.Runtime.Guide;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Frame.Tests.PlayMode
{
    public sealed class GuideModuleTests
    {
        [Test]
        public void GuideService_RegistersInterfaceAndImplementation()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                GuideService service = fixture.Initialize(new GuideService());

                Assert.IsTrue(fixture.Services.TryResolve(out IGuideService guideService));
                Assert.AreSame(service, guideService);
                Assert.IsTrue(fixture.Services.TryResolve(out GuideService concreteService));
                Assert.AreSame(service, concreteService);

                service.Shutdown();
            }
        }

        [Test]
        public void UIGuideMask_RaycastMatchesConfiguredShape()
        {
            GameObject go = new GameObject("GuideMaskTest", typeof(RectTransform), typeof(UIGuideMask));
            try
            {
                RectTransform rect = go.GetComponent<RectTransform>();
                rect.position = Vector3.zero;
                rect.sizeDelta = new Vector2(400f, 400f);

                UIGuideMask mask = go.GetComponent<UIGuideMask>();
                Vector2 center = new Vector2(100f, 100f);

                mask.SetTarget(center, new Vector2(100f, 60f), GuideMaskShape.Rectangle);
                AssertPassesThrough(mask, center);
                AssertBlocks(mask, new Vector2(151f, 100f));

                mask.SetTarget(center, new Vector2(100f, 60f), GuideMaskShape.Circle);
                AssertPassesThrough(mask, new Vector2(149f, 100f));
                AssertBlocks(mask, new Vector2(151f, 100f));

                mask.SetTarget(center, new Vector2(100f, 60f), GuideMaskShape.Ellipse);
                AssertPassesThrough(mask, new Vector2(149f, 100f));
                AssertBlocks(mask, new Vector2(100f, 131f));

                mask.SetTarget(center, new Vector2(100f, 60f), GuideMaskShape.RoundedRectangle, 20f);
                AssertPassesThrough(mask, center);
                AssertPassesThrough(mask, new Vector2(70f, 125f));
                AssertBlocks(mask, new Vector2(55f, 125f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator GuideService_CustomEventRequiresMatchingEventName()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                GuideService service = fixture.Initialize(new GuideService());
                GuideConfig config = ScriptableObject.CreateInstance<GuideConfig>();
                bool completed = false;

                try
                {
                    config.Steps.Add(new GuideStep
                    {
                        TriggerType = GuideTriggerType.CustomEvent,
                        CustomEventName = "expected_event"
                    });

                    service.StartGuide(config, () => completed = true);
                    yield return null;

                    Assert.IsTrue(service.IsGuiding);
                    service.NotifyCustomEvent("wrong_event");
                    yield return null;

                    Assert.IsFalse(completed);
                    Assert.IsTrue(service.IsGuiding);

                    service.NotifyCustomEvent("expected_event");
                    yield return null;

                    Assert.IsTrue(completed);
                    Assert.IsFalse(service.IsGuiding);
                }
                finally
                {
                    service.Shutdown();
                    Object.Destroy(config);
                }
            }
        }

        [UnityTest]
        public IEnumerator GuideService_AutoNextCreatesClickLayerAndCompletesOnClick()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                GuideService service = fixture.Initialize(new GuideService());
                GuideConfig config = ScriptableObject.CreateInstance<GuideConfig>();
                bool completed = false;

                try
                {
                    config.Steps.Add(new GuideStep
                    {
                        TriggerType = GuideTriggerType.AutoNext,
                        DialogueText = "Tap anywhere to continue",
                        DialogueOffset = new Vector2(12f, -24f)
                    });

                    service.StartGuide(config, () => completed = true);
                    yield return null;

                    GameObject dialogue = GameObject.Find("GuideDialogue");
                    Assert.IsNotNull(dialogue);
                    Assert.IsTrue(dialogue.activeSelf);
                    Assert.AreEqual(new Vector2(12f, -24f), dialogue.GetComponent<RectTransform>().anchoredPosition);
                    Assert.AreEqual("Tap anywhere to continue", dialogue.GetComponentInChildren<Text>().text);

                    GameObject clickLayer = GameObject.Find("GuideAutoNextClickLayer");
                    Assert.IsNotNull(clickLayer);
                    Button button = clickLayer.GetComponent<Button>();
                    Assert.IsNotNull(button);

                    button.onClick.Invoke();
                    yield return null;

                    Assert.IsTrue(completed);
                    Assert.IsFalse(service.IsGuiding);
                }
                finally
                {
                    service.Shutdown();
                    Object.Destroy(config);
                }
            }
        }

        [UnityTest]
        public IEnumerator GuideService_UsesConfiguredDialoguePrefabWhenProvided()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                GuideService service = fixture.Initialize(new GuideService());
                GuideConfig config = ScriptableObject.CreateInstance<GuideConfig>();
                GameObject prefab = CreateDialoguePrefab();
                bool completed = false;

                try
                {
                    config.DialoguePrefab = prefab;
                    config.Steps.Add(new GuideStep
                    {
                        TriggerType = GuideTriggerType.AutoNext,
                        DialogueText = "Custom dialogue",
                        DialogueOffset = new Vector2(4f, 8f)
                    });

                    service.StartGuide(config, () => completed = true);
                    yield return null;

                    GameObject dialogue = GameObject.Find("GuideDialogue");
                    Assert.IsNotNull(dialogue);
                    Assert.AreEqual(new Vector2(4f, 8f), dialogue.GetComponent<RectTransform>().anchoredPosition);
                    Assert.AreEqual(new Color(0.25f, 0.5f, 0.75f, 1f), dialogue.GetComponent<Image>().color);
                    Assert.AreEqual("Custom dialogue", dialogue.GetComponentInChildren<Text>().text);

                    dialogue.GetComponentInParent<Canvas>().GetComponentInChildren<Button>().onClick.Invoke();
                    yield return null;

                    Assert.IsTrue(completed);
                }
                finally
                {
                    service.Shutdown();
                    Object.Destroy(config);
                    Object.Destroy(prefab);
                }
            }
        }

        [UnityTest]
        public IEnumerator GuideService_PersistsProgressAndResumesFromSavedStep()
        {
            using (FramePlayModeTestFixture fixture = new FramePlayModeTestFixture())
            {
                const int guideGroupId = 91001;
                GuideService service = fixture.Initialize(new GuideService());
                GuideConfig config = ScriptableObject.CreateInstance<GuideConfig>();
                bool completed = false;

                try
                {
                    service.ResetGuideProgress(guideGroupId);
                    config.GuideGroupId = guideGroupId;
                    config.Steps.Add(new GuideStep
                    {
                        TriggerType = GuideTriggerType.AutoNext,
                        DialogueText = "Step 1"
                    });
                    config.Steps.Add(new GuideStep
                    {
                        TriggerType = GuideTriggerType.AutoNext,
                        DialogueText = "Step 2"
                    });

                    service.StartGuide(config);
                    yield return null;

                    Assert.AreEqual(0, service.GetGuideProgress(guideGroupId));
                    Assert.AreEqual("Step 1", GameObject.Find("GuideDialogue").GetComponentInChildren<Text>().text);

                    GameObject.Find("GuideAutoNextClickLayer").GetComponent<Button>().onClick.Invoke();
                    yield return null;

                    Assert.AreEqual(1, service.GetGuideProgress(guideGroupId));
                    service.StopGuide();
                    yield return null;

                    service.StartGuide(config, () => completed = true);
                    yield return null;

                    Assert.AreEqual("Step 2", GameObject.Find("GuideDialogue").GetComponentInChildren<Text>().text);
                    GameObject.Find("GuideAutoNextClickLayer").GetComponent<Button>().onClick.Invoke();
                    yield return null;

                    Assert.IsTrue(completed);
                    Assert.AreEqual(2, service.GetGuideProgress(guideGroupId));
                    Assert.IsTrue(service.IsGuideCompleted(guideGroupId));
                }
                finally
                {
                    service.ResetGuideProgress(guideGroupId);
                    service.Shutdown();
                    Object.Destroy(config);
                }
            }
        }

        private static void AssertPassesThrough(UIGuideMask mask, Vector2 screenPoint)
        {
            Assert.IsFalse(mask.IsRaycastLocationValid(screenPoint, null));
        }

        private static void AssertBlocks(UIGuideMask mask, Vector2 screenPoint)
        {
            Assert.IsTrue(mask.IsRaycastLocationValid(screenPoint, null));
        }

        private static GameObject CreateDialoguePrefab()
        {
            GameObject prefab = new GameObject("CustomGuideDialoguePrefab", typeof(RectTransform), typeof(Image));
            prefab.GetComponent<RectTransform>().sizeDelta = new Vector2(500f, 140f);
            prefab.GetComponent<Image>().color = new Color(0.25f, 0.5f, 0.75f, 1f);

            GameObject textObj = new GameObject("CustomText", typeof(RectTransform), typeof(Text));
            textObj.transform.SetParent(prefab.transform, false);
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textObj.GetComponent<Text>().font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return prefab;
        }
    }
}

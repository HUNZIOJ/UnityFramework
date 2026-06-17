using Frame.Input;
using NUnit.Framework;
using System;
using System.Reflection;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Frame.Tests.EditMode
{
    public sealed class InputModuleTests
    {
        [Test]
        public void InputService_SetContextChangesCurrentContext()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                InputService service = fixture.Initialize(new InputService());

                service.SetContext(InputContext.UI);
                Assert.AreEqual(InputContext.UI, service.CurrentContext);

                service.SetContext(InputContext.Disabled);
                Assert.AreEqual(InputContext.Disabled, service.CurrentContext);
            }
        }

        [Test]
        public void InputService_PushContextRestoresPreviousContext()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                InputService service = fixture.Initialize(new InputService());

                service.SetContext(InputContext.Gameplay);
                IDisposable uiScope = service.PushContext(InputContext.UI);
                Assert.AreEqual(InputContext.UI, service.CurrentContext);
                Assert.AreEqual(1, service.ContextStackDepth);

                IDisposable disabledScope = service.PushContext(InputContext.Disabled);
                Assert.AreEqual(InputContext.Disabled, service.CurrentContext);
                Assert.AreEqual(2, service.ContextStackDepth);

                disabledScope.Dispose();
                Assert.AreEqual(InputContext.UI, service.CurrentContext);
                Assert.AreEqual(1, service.ContextStackDepth);

                uiScope.Dispose();
                Assert.AreEqual(InputContext.Gameplay, service.CurrentContext);
                Assert.AreEqual(0, service.ContextStackDepth);
                Assert.IsFalse(service.PopContext());
            }
        }

#if ENABLE_INPUT_SYSTEM
        [Test]
        public void InputService_InputSystemMethodsAreSafeWithoutActions()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                InputService service = fixture.Initialize(new InputService());

                service.SetContext(InputContext.Gameplay);

                MethodInfo findAction = typeof(InputService).GetMethod("FindAction");
                MethodInfo wasPressed = typeof(InputService).GetMethod("WasPressedThisFrame");
                MethodInfo readVector2 = typeof(InputService).GetMethod("ReadVector2");
                MethodInfo setActions = typeof(InputService).GetMethod("SetActions");
                MethodInfo saveOverrides = typeof(InputService).GetMethod("SaveBindingOverridesAsJson");

                Assert.IsNotNull(findAction);
                Assert.IsNotNull(wasPressed);
                Assert.IsNotNull(readVector2);
                Assert.IsNotNull(setActions);
                Assert.IsNotNull(saveOverrides);

                setActions.Invoke(service, new object[] { null });
                Assert.IsNull(findAction.Invoke(service, new object[] { "Missing" }));
                Assert.IsFalse((bool)wasPressed.Invoke(service, new object[] { "Missing" }));
                Assert.AreEqual(Vector2.zero, (Vector2)readVector2.Invoke(service, new object[] { "Missing" }));
                Assert.AreEqual(string.Empty, saveOverrides.Invoke(service, null));

                service.SetContext(InputContext.Disabled);
                Assert.AreEqual(InputContext.Disabled, service.CurrentContext);
            }
        }

        [Test]
        public void InputService_GameplayContextEnablesPlayerActionMap()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                InputService service = fixture.Initialize(new InputService());
                InputActionAsset actions = ScriptableObject.CreateInstance<InputActionAsset>();
                try
                {
                    InputActionMap player = actions.AddActionMap("Player");
                    player.AddAction("Move", InputActionType.Value, "<Gamepad>/leftStick");

                    InputActionMap ui = actions.AddActionMap("UI");
                    ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");

                    service.SetActions(actions);
                    service.SetContext(InputContext.Gameplay);

                    Assert.IsTrue(player.enabled);
                    Assert.IsFalse(ui.enabled);

                    service.SetContext(InputContext.UI);

                    Assert.IsFalse(player.enabled);
                    Assert.IsTrue(ui.enabled);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(actions);
                }
            }
        }

        [Test]
        public void InputService_SavesLoadsAndClearsBindingOverrides()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                InputService service = fixture.Initialize(new InputService());
                InputActionAsset actions = ScriptableObject.CreateInstance<InputActionAsset>();
                try
                {
                    InputActionMap player = actions.AddActionMap("Player");
                    InputAction jump = player.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");

                    service.SetActions(actions);

                    Assert.IsFalse(service.ApplyBindingOverride("Missing", 0, "<Keyboard>/enter"));
                    Assert.IsFalse(service.ApplyBindingOverride("Jump", -1, "<Keyboard>/enter"));
                    Assert.IsFalse(service.ApplyBindingOverride("Jump", 99, "<Keyboard>/enter"));
                    Assert.IsFalse(service.ApplyBindingOverride("Jump", 0, ""));

                    Assert.IsTrue(service.ApplyBindingOverride("Jump", 0, "<Keyboard>/enter"));
                    Assert.AreEqual("<Keyboard>/enter", jump.bindings[0].overridePath);

                    string json = service.SaveBindingOverridesAsJson();
                    Assert.IsNotEmpty(json);

                    service.ClearBindingOverrides();
                    Assert.IsNull(jump.bindings[0].overridePath);

                    service.LoadBindingOverridesFromJson(json);
                    Assert.AreEqual("<Keyboard>/enter", jump.bindings[0].overridePath);

                    Assert.IsTrue(service.ClearBindingOverride("Jump", 0));
                    Assert.IsNull(jump.bindings[0].overridePath);

                    service.LoadBindingOverridesFromJson(json);
                    service.LoadBindingOverridesFromJson(string.Empty);
                    Assert.IsNull(jump.bindings[0].overridePath);

                    Assert.IsFalse(service.ClearBindingOverride("Jump", 99));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(actions);
                }
            }
        }
#else
        [Test]
        public void InputService_LegacyMethodsAreSafeToCall()
        {
            using (FrameTestFixture fixture = new FrameTestFixture())
            {
                InputService service = fixture.Initialize(new InputService());
                service.SetContext(InputContext.Disabled);

                Assert.IsFalse(service.GetKey(KeyCode.Space));
                Assert.IsFalse(service.GetKeyDown(KeyCode.Space));
            }
        }
#endif
    }
}

using Frame.Input;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;

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

                Assert.IsNotNull(findAction);
                Assert.IsNotNull(wasPressed);
                Assert.IsNotNull(readVector2);
                Assert.IsNotNull(setActions);

                setActions.Invoke(service, new object[] { null });
                Assert.IsNull(findAction.Invoke(service, new object[] { "Missing" }));
                Assert.IsFalse((bool)wasPressed.Invoke(service, new object[] { "Missing" }));
                Assert.AreEqual(Vector2.zero, (Vector2)readVector2.Invoke(service, new object[] { "Missing" }));

                service.SetContext(InputContext.Disabled);
                Assert.AreEqual(InputContext.Disabled, service.CurrentContext);
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

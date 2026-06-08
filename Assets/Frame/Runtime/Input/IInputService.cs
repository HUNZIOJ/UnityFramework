using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Frame.Input
{
    public interface IInputService
    {
        InputContext CurrentContext { get; }

        void SetContext(InputContext context);

#if ENABLE_INPUT_SYSTEM
        void SetActions(InputActionAsset actionAsset);

        InputAction FindAction(string actionName);

        bool WasPressedThisFrame(string actionName);

        Vector2 ReadVector2(string actionName);
#else
        bool GetKey(KeyCode key);

        bool GetKeyDown(KeyCode key);
#endif
    }
}

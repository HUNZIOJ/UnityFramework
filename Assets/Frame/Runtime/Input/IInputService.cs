using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Frame.Input
{
    public interface IInputService
    {
        InputContext CurrentContext { get; }

        int ContextStackDepth { get; }

        void SetContext(InputContext context);

        IDisposable PushContext(InputContext context);

        bool PopContext();

#if ENABLE_INPUT_SYSTEM
        void SetActions(InputActionAsset actionAsset);

        InputAction FindAction(string actionName);

        bool WasPressedThisFrame(string actionName);

        Vector2 ReadVector2(string actionName);

        bool ApplyBindingOverride(string actionName, int bindingIndex, string overridePath);

        bool ClearBindingOverride(string actionName, int bindingIndex);

        void ClearBindingOverrides();

        string SaveBindingOverridesAsJson();

        void LoadBindingOverridesFromJson(string json, bool removeExisting = true);
#else
        bool GetKey(KeyCode key);

        bool GetKeyDown(KeyCode key);
#endif
    }
}

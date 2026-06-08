using Frame.Core;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Frame.Input
{
    public sealed class InputService : GameModuleBase, IInputService
    {
        private InputContext currentContext = InputContext.Gameplay;

#if ENABLE_INPUT_SYSTEM
        private InputActionAsset actions;
#endif

        public InputContext CurrentContext
        {
            get { return currentContext; }
        }

        protected override void OnInitialize()
        {
            Context.Services.Register<IInputService>(this);
            Context.Services.Register(this);
        }

        public void SetContext(InputContext context)
        {
            currentContext = context;
#if ENABLE_INPUT_SYSTEM
            ApplyActionMapState();
#endif
        }

#if ENABLE_INPUT_SYSTEM
        public void SetActions(InputActionAsset actionAsset)
        {
            if (actions != null)
            {
                actions.Disable();
            }

            actions = actionAsset;
            ApplyActionMapState();
        }

        public InputAction FindAction(string actionName)
        {
            if (actions == null || string.IsNullOrWhiteSpace(actionName))
            {
                return null;
            }

            return actions.FindAction(actionName, false);
        }

        public bool WasPressedThisFrame(string actionName)
        {
            InputAction action = FindAction(actionName);
            return action != null && action.WasPressedThisFrame();
        }

        public Vector2 ReadVector2(string actionName)
        {
            InputAction action = FindAction(actionName);
            return action == null ? Vector2.zero : action.ReadValue<Vector2>();
        }

        private void ApplyActionMapState()
        {
            if (actions == null)
            {
                return;
            }

            if (currentContext == InputContext.Disabled)
            {
                actions.Disable();
                return;
            }

            actions.Enable();
            for (int i = 0; i < actions.actionMaps.Count; i++)
            {
                InputActionMap map = actions.actionMaps[i];
                bool shouldEnable =
                    (currentContext == InputContext.Gameplay && map.name == "Gameplay") ||
                    (currentContext == InputContext.UI && map.name == "UI");

                if (shouldEnable)
                {
                    map.Enable();
                }
                else
                {
                    map.Disable();
                }
            }
        }
#else
        public bool GetKey(KeyCode key)
        {
            return currentContext != InputContext.Disabled && UnityEngine.Input.GetKey(key);
        }

        public bool GetKeyDown(KeyCode key)
        {
            return currentContext != InputContext.Disabled && UnityEngine.Input.GetKeyDown(key);
        }
#endif

        protected override void OnShutdown()
        {
#if ENABLE_INPUT_SYSTEM
            if (actions != null)
            {
                actions.Disable();
                actions = null;
            }
#endif
        }
    }
}

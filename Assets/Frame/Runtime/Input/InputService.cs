using System;
using System.Collections.Generic;
using Frame.Core;
using Frame.Utilities;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Frame.Input
{
    public sealed class InputService : GameModuleBase, IInputService
    {
        private const string GameplayActionMapName = "Player";
        private const string UIActionMapName = "UI";

        private readonly Stack<InputContext> contextStack = new Stack<InputContext>();
        private InputContext currentContext = InputContext.Gameplay;

#if ENABLE_INPUT_SYSTEM
        private InputActionAsset actions;
#endif

        public InputContext CurrentContext
        {
            get { return currentContext; }
        }

        public int ContextStackDepth
        {
            get { return contextStack.Count; }
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

        public IDisposable PushContext(InputContext context)
        {
            contextStack.Push(currentContext);
            SetContext(context);
            return new DisposableAction(() => PopContext());
        }

        public bool PopContext()
        {
            if (contextStack.Count == 0)
            {
                return false;
            }

            SetContext(contextStack.Pop());
            return true;
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

        public bool ApplyBindingOverride(string actionName, int bindingIndex, string overridePath)
        {
            InputAction action = FindAction(actionName);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count || string.IsNullOrWhiteSpace(overridePath))
            {
                return false;
            }

            action.ApplyBindingOverride(bindingIndex, overridePath);
            return true;
        }

        public bool ClearBindingOverride(string actionName, int bindingIndex)
        {
            InputAction action = FindAction(actionName);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                return false;
            }

            action.RemoveBindingOverride(bindingIndex);
            return true;
        }

        public void ClearBindingOverrides()
        {
            if (actions != null)
            {
                actions.RemoveAllBindingOverrides();
            }
        }

        public string SaveBindingOverridesAsJson()
        {
            return actions == null ? string.Empty : actions.SaveBindingOverridesAsJson();
        }

        public void LoadBindingOverridesFromJson(string json, bool removeExisting = true)
        {
            if (actions == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                if (removeExisting)
                {
                    actions.RemoveAllBindingOverrides();
                }

                return;
            }

            actions.LoadBindingOverridesFromJson(json, removeExisting);
            ApplyActionMapState();
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
                    (currentContext == InputContext.Gameplay && map.name == GameplayActionMapName) ||
                    (currentContext == InputContext.UI && map.name == UIActionMapName);

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
            contextStack.Clear();
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

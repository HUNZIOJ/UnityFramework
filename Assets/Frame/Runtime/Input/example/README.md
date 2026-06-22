# Input 模块使用示例

Input 模块统一管理输入上下文。启用 Unity Input System 时支持 `InputActionAsset`、Action 查询、绑定覆盖保存/加载；未启用时回退到 Legacy `KeyCode`。

## 命名空间

```csharp
using Frame.Core;
using Frame.Input;
using UnityEngine;
```

Input System 相关代码需要：

```csharp
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
```

## 获取服务

```csharp
IInputService input = Framework.Resolve<IInputService>();
```

## 输入上下文

```csharp
input.SetContext(InputContext.Gameplay);
input.SetContext(InputContext.UI);
input.SetContext(InputContext.Disabled);

InputContext current = input.CurrentContext;
```

可用上下文：

- `Disabled`: 禁用输入。
- `Gameplay`: 启用名为 `Player` 的 Action Map。
- `UI`: 启用名为 `UI` 的 Action Map。

## 临时压栈上下文

适合打开 UI、暂停菜单、剧情对话等临时切换。

```csharp
IDisposable scope = input.PushContext(InputContext.UI);

try
{
    OpenPauseMenu();
}
finally
{
    scope.Dispose();
}
```

手动弹出：

```csharp
bool restored = input.PopContext();
int depth = input.ContextStackDepth;
```

## Input System 用法

```csharp
#if ENABLE_INPUT_SYSTEM
[SerializeField] private InputActionAsset actions;

public void InitializeInput()
{
    IInputService input = Framework.Resolve<IInputService>();
    input.SetActions(actions);
    input.SetContext(InputContext.Gameplay);
}
#endif
```

读取按钮：

```csharp
#if ENABLE_INPUT_SYSTEM
if (input.WasPressedThisFrame("Jump"))
{
    Jump();
}
#endif
```

读取方向：

```csharp
#if ENABLE_INPUT_SYSTEM
Vector2 move = input.ReadVector2("Move");
#endif
```

查找 Action：

```csharp
#if ENABLE_INPUT_SYSTEM
InputAction action = input.FindAction("Attack");
if (action != null)
{
    // 可读取 action.phase、bindings 等。
}
#endif
```

## 绑定覆盖

```csharp
#if ENABLE_INPUT_SYSTEM
bool applied = input.ApplyBindingOverride(
    actionName: "Jump",
    bindingIndex: 0,
    overridePath: "<Keyboard>/space");

bool cleared = input.ClearBindingOverride("Jump", 0);
input.ClearBindingOverrides();
#endif
```

保存和加载覆盖：

```csharp
#if ENABLE_INPUT_SYSTEM
string json = input.SaveBindingOverridesAsJson();
Framework.Resolve<IPreferencesService>().SetString("input.bindings", json);

string savedJson = Framework.Resolve<IPreferencesService>().GetString("input.bindings", "");
input.LoadBindingOverridesFromJson(savedJson, removeExisting: true);
#endif
```

## Legacy Input 用法

未启用 Input System 时可使用：

```csharp
#if !ENABLE_INPUT_SYSTEM
if (input.GetKeyDown(KeyCode.Space))
{
    Jump();
}

if (input.GetKey(KeyCode.LeftShift))
{
    Sprint();
}
#endif
```

当上下文是 `Disabled` 时，`GetKey` 和 `GetKeyDown` 会返回 `false`。

## UI 打开时切换上下文

```csharp
public sealed class PauseFlow
{
    private IDisposable inputScope;

    public void Open()
    {
        inputScope = Framework.Resolve<IInputService>().PushContext(InputContext.UI);
        Framework.Resolve<IUIService>().OpenRoute("pause");
    }

    public void Close()
    {
        inputScope?.Dispose();
        inputScope = null;
    }
}
```

## 注意事项

- Input System 下 Action Map 名称固定识别为 `Player` 和 `UI`。
- `SetActions` 会禁用旧的 `InputActionAsset`，再应用新上下文。
- `LoadBindingOverridesFromJson` 传空字符串且 `removeExisting=true` 时会清空覆盖。
- 推荐把输入重绑定 JSON 保存到 Preferences 模块。

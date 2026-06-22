# Editor 模块使用示例

Editor 模块提供 Unity 菜单和项目校验工具，帮助创建 `FrameSettings`、创建场景入口、打开 README，以及在本地或 CI 中验证框架依赖和资源约定。

## 命名空间

```csharp
using Frame.Editor;
```

该模块只在 Editor 程序集中可用，不能被 Runtime 代码引用。

## 菜单入口

Unity 顶部菜单 `Frame` 提供：

- `Frame/Create Default Frame Settings`
- `Frame/Create GameEntry In Scene`
- `Frame/Open README`
- `Frame/Validate Project`

## 创建 FrameSettings

菜单执行 `Frame/Create Default Frame Settings` 会确保目录存在，并创建：

```text
Assets/Frame/Resources/Frame/FrameSettings.asset
```

代码调用：

```csharp
FrameMenuItems.CreateDefaultSettings();
```

如果资产已存在，会选中并 ping 该资产。

## 创建 GameEntry

菜单执行 `Frame/Create GameEntry In Scene`：

- 当前场景已有 `GameEntry` 时选中它。
- 没有时创建名为 `Frame` 的 GameObject 并挂载 `GameEntry`。
- 使用 Undo 注册创建操作。
- 标记当前场景 dirty。

代码调用：

```csharp
FrameMenuItems.CreateGameEntryInScene();
```

## 打开 README

```csharp
FrameMenuItems.OpenReadme();
```

会打开：

```text
Assets/Frame/README.md
```

## 本地项目校验

菜单执行 `Frame/Validate Project`，或代码调用：

```csharp
FrameMenuItems.ValidateProject();
```

校验内容包括：

- `FrameSettings` 是否存在。
- UI 参考分辨率、音频池大小、GameObject 池默认大小是否有效。
- 当前场景 `GameEntry` 数量。
- Build Settings 场景是否存在且至少一个启用。
- 必要包是否存在：Newtonsoft Json、Input System、UniTask、Addressables、YooAsset。
- `Frame.Runtime.asmdef` 是否引用 `UnityEngine.UI`、`Unity.InputSystem`、`UniTask`。
- DOTween、Addressables、YooAsset 集成 asmdef 是否存在。
- Resources 路径是否重复。
- `Resources/UI/*.prefab` 是否包含 `UIPanelBase`。
- `Resources/Configs/*.json` 是否为合法 JSON。

## 读取校验报告

```csharp
FrameMenuItems.ValidationReport report = FrameMenuItems.RunProjectValidation(logDetails: false);

if (!report.Passed)
{
    foreach (FrameMenuItems.ValidationMessage message in report.Messages)
    {
        Debug.Log(message.Type + ": " + message.Message);
    }
}
```

报告字段：

- `Messages`
- `Errors`
- `Warnings`
- `Passed`
- `ExitCode`

## CI 校验

批处理模式调用：

```powershell
& "D:\UnityEditor\6000.4.8f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "E:\UnityProject\Framework" `
  -executeMethod Frame.Editor.FrameMenuItems.ValidateProjectForCI `
  -quit
```

`ValidateProjectForCI` 在 batchmode 下会调用 `EditorApplication.Exit(report.ExitCode)`：

- `0`: 没有 error。
- `1`: 存在 error。

非 batchmode 下如果校验失败，会抛 `InvalidOperationException`。

## 自定义编辑器工具中复用校验

```csharp
using UnityEditor;
using UnityEngine;

public static class CustomBuildValidator
{
    [MenuItem("Game/Validate Before Build")]
    public static void ValidateBeforeBuild()
    {
        FrameMenuItems.ValidationReport report = FrameMenuItems.RunProjectValidation();
        if (!report.Passed)
        {
            throw new System.InvalidOperationException("Frame validation failed.");
        }

        Debug.Log("Frame validation passed.");
    }
}
```

## 注意事项

- Editor 模块依赖 `UnityEditor`，不要放进 Runtime asmdef。
- `ValidateResources` 会跳过 `.meta`、`.cs`、`.asmdef`。
- Resources key 会去掉扩展名并以 `/Resources/` 后的路径为准。
- 校验 warning 不会导致 CI 失败，error 才会返回非 0。

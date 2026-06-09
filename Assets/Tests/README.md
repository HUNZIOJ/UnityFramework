# Frame Tests

本目录使用 Unity Test Framework 覆盖 `Assets/Frame` 框架模块。

## Test Assemblies

```text
Assets/Tests/EditMode/Frame.EditMode.Tests.asmdef
Assets/Tests/PlayMode/Frame.PlayMode.Tests.asmdef
```

EditMode 测试主要覆盖纯逻辑和无需真实帧循环的模块。PlayMode 测试覆盖依赖 Unity 对象、协程、场景、UGUI、音频和 DOTween 的模块。

## Coverage Map

| Module | Test Script | Mode |
| --- | --- | --- |
| Core | `CoreModuleTests.cs`, `FrameworkBootstrapTests.cs` | EditMode + PlayMode |
| Events | `EventsModuleTests.cs` | EditMode |
| Time | `TimeModuleTests.cs` | EditMode |
| Pooling | `PoolingModuleTests.cs`, `PoolingPlayModeTests.cs` | EditMode + PlayMode |
| Assets | `AssetsModuleTests.cs` | PlayMode |
| Scenes | `ScenesModuleTests.cs` | PlayMode |
| UI | `UIModuleTests.cs` | PlayMode |
| Audio | `AudioModuleTests.cs` | PlayMode |
| Tweening / DOTween | `TweeningModuleTests.cs` | PlayMode |
| Config | `ConfigModuleTests.cs` | EditMode |
| Save | `SaveModuleTests.cs` | EditMode |
| Input | `InputModuleTests.cs` | EditMode |
| Networking | `NetworkingModuleTests.cs` | PlayMode |
| Localization | `LocalizationModuleTests.cs` | EditMode |
| StateMachine | `StateMachineModuleTests.cs` | EditMode |
| Utilities | `UtilitiesModuleTests.cs` | EditMode |

## What Is Tested

- 服务注册、解析、释放、模块优先级、生命周期顺序和初始化失败清理。
- 事件订阅、一次性订阅、owner 批量解绑、异常隔离和清空订阅。
- 延迟计时、循环计时、取消、owner 取消、非缩放时间和暂停行为。
- C# 对象池、GameObject 池、PoolService、预热、归还、清理和 `IPoolable` 回调。
- Resources 同步加载、异步加载、路径归一化、无效路径和资源句柄释放。
- UI root、UI 层、面板创建、打开、缓存、关闭、销毁和安全区组件。
- 音量分组、静音、音乐、音效、AudioCue 播放。
- JSON 配置、ScriptableObject 配置、自定义 provider 优先级和缺失配置。
- JSON 存档、备份恢复、自定义 serializer、slot 校验、列表和删除。
- InputContext 和 InputSystem/Legacy 条件接口。
- HTTP 空 URL、取消、失败响应、重试和回调异常隔离。
- 本地化表、语言切换、fallback、缺失 key 和字典缓存。
- 通用状态机的进入、Tick、退出、重复切换、缺失状态和清理。
- 路径工具和一次性释放工具。
- DOTween 值动画、Transform 移动、缩放、CanvasGroup 淡入淡出、Kill 和空句柄安全性。

## Running Tests

在 Unity Editor 中打开 `Window/Test Runner`：

1. 选择 `EditMode`，运行 `Frame.EditMode.Tests`。
2. 选择 `PlayMode`，运行 `Frame.PlayMode.Tests`。

命令行运行需要本机 Unity 可执行文件路径。例如：

```powershell
& "D:\UnityEditor\6000.4.8f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "E:\UnityProject\Framework" `
  -runTests `
  -testPlatform EditMode `
  -testResults "Temp\FrameEditModeResults.xml" `
  -quit
```

PlayMode：

```powershell
& "D:\UnityEditor\6000.4.8f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "E:\UnityProject\Framework" `
  -runTests `
  -testPlatform PlayMode `
  -testResults "Temp\FramePlayModeResults.xml" `
  -quit
```

## Notes

- `NetworkingModuleTests` 不依赖外部网络服务，只测试空 URL、取消和本地不可连接端口的失败路径。
- `ScenesModuleTests` 使用 Build Settings 中已有的 `Assets/Scenes/SampleScene.unity`。
- `AssetsModuleTests` 使用 `Assets/Tests/PlayMode/Resources/FrameTests/TextAsset.txt`。
- `ConfigModuleTests` 使用 `Assets/Tests/EditMode/Resources/Configs/FrameTests/item.json`。
- 当前命令行验证使用 Unity 生成的 csproj 编译测试程序集；完整测试执行仍应通过 Unity Test Runner 运行。

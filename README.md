# Framework

一个轻量、原生依赖优先的 Unity 游戏开发框架底座。适合空项目启动、中小型项目开发，也可作为商业项目早期的基础框架。

- **Unity 版本**：6000.5.0f1（URP）
- **渲染管线**：Universal Render Pipeline (URP 17.5.0)
- **核心依赖**：Input System 1.19、Newtonsoft.Json 3.2、Addressables 3.1、YooAsset、HybridCLR、NativeWebSocket、DOTween（第三方目录）

框架代码与业务代码严格分层：`Assets/Frame` 只放可复用框架，`Assets/Game` 放具体游戏业务。业务层统一通过接口（`IEventBus`、`ITimerService`、`IAssetService`、`IUIService`、`IGuideService`、`ISaveService`、`IHttpService`、`ISocketService` 等）使用框架，不依赖具体实现。

> 更深入的实现细节和生产可用性评估见 [Assets/Frame/FRAMEWORK_DEEP_DIVE.md](Assets/Frame/FRAMEWORK_DEEP_DIVE.md)，项目目录约定见 [Assets/PROJECT_STRUCTURE.md](Assets/PROJECT_STRUCTURE.md)。

---

## 目录

- [快速开始](#快速开始)
- [项目结构](#项目结构)
- [启动方式](#启动方式)
- [模块总览](#模块总览)
- [模块详解](#模块详解)
  - [Core 核心底座](#core-核心底座)
  - [Lifecycle 生命周期](#lifecycle-生命周期)
  - [Events 事件总线](#events-事件总线)
  - [Time 定时器](#time-定时器)
  - [Pooling 对象池](#pooling-对象池)
  - [Assets 资源加载](#assets-资源加载)
  - [Scenes 场景管理](#scenes-场景管理)
  - [UI 用户界面](#ui-用户界面)
  - [Guide 新手引导](#guide-新手引导)
  - [Audio 音频](#audio-音频)
  - [Tweening 补间动画](#tweening-补间动画)
  - [Config 配置](#config-配置)
  - [Save 存档](#save-存档)
  - [Preferences 偏好设置](#preferences-偏好设置)
  - [Input 输入](#input-输入)
  - [Networking 网络](#networking-网络)
  - [Localization 多语言](#localization-多语言)
  - [StateMachine 状态机](#statemachine-状态机)
  - [Diagnostics 诊断](#diagnostics-诊断)
  - [Utilities / Utility 工具](#utilities--utility-工具)
  - [Integrations 集成层](#integrations-集成层)
  - [Editor 编辑器工具](#editor-编辑器工具)
  - [Tests 测试](#tests-测试)
- [编辑器工具与 CI](#编辑器工具与-ci)
- [常用服务接口](#常用服务接口)
- [基础示例](#基础示例)

---

## 快速开始

1. 用 Unity 6000.5.0f1 打开本仓库。
2. 首次打开会自动在 `BeforeSceneLoad` 阶段创建 `Frame` 对象并按默认 `FrameSettings` 初始化框架。
3. 如需自定义配置：菜单 `Frame/Create Default Frame Settings` 生成 `Assets/Frame/Resources/Frame/FrameSettings.asset`，在面板中开启 / 关闭各模块。
4. 菜单 `Frame/Create GameEntry In Scene` 在场景中创建入口对象。
5. 业务代码通过 `Framework.Resolve<T>()` 获取服务接口：

```csharp
using Frame.Core;
using Frame.Events;
using Frame.Timing;

IEventBus events = Framework.Resolve<IEventBus>();
ITimerService timers = Framework.Resolve<ITimerService>();
```

## 项目结构

```text
Framework/
├─ Assets/
│  ├─ Frame/                 # 通用框架，只放可复用底座、适配层、示例和编辑器工具
│  │  ├─ Runtime/            # 运行时程序集 Frame.Runtime
│  │  ├─ Editor/             # 编辑器程序集 Frame.Editor（仅 Editor）
│  │  ├─ Integrations/       # 可选集成（Addressables / YooAsset / DOTween）
│  │  ├─ Samples/            # 调用示例
│  │  ├─ README.md           # 框架说明
│  │  └─ FRAMEWORK_DEEP_DIVE.md
│  ├─ Game/                  # 业务代码、业务配置、业务 UI、业务 prefab
│  ├─ Art/                   # 美术资源（Materials/Models/Textures/Animations/VFX）
│  ├─ Audio/                 # 音频资源（Music/SFX/Voice）
│  ├─ Scenes/                # 场景资源
│  ├─ Settings/              # URP 与项目模板设置
│  ├─ Tests/                 # EditMode / PlayMode 测试
│  ├─ ThirdParty/            # 第三方插件或 SDK（如 DOTween）
│  └─ PROJECT_STRUCTURE.md   # 目录约定
├─ Packages/                 # Unity 包清单（manifest.json）
├─ ProjectSettings/          # Unity 项目设置
└─ HybridCLRData/            # HybridCLR 热更新数据
```

### 分层规则

- `Assets/Frame` 不写具体游戏业务逻辑。
- `Assets/Game` 可以依赖 `Frame.Runtime`。
- `Assets/Frame/Runtime` 不能依赖 `UnityEditor`。
- `Assets/Frame/Editor` 只放编辑器脚本，由 `Frame.Editor.asmdef` 限制到 Editor 平台。
- Resources 路径统一使用 `/`，不带扩展名。
- UI prefab 建议放在 `Resources/UI`；JSON 配置建议放在 `Resources/Configs`。

## 启动方式

默认在 `BeforeSceneLoad` 阶段自动创建 `Frame` 对象并初始化框架。若要显式控制启动：

1. 菜单 `Frame/Create Default Frame Settings`，生成 `Assets/Frame/Resources/Frame/FrameSettings.asset`。
2. 菜单 `Frame/Create GameEntry In Scene`，在场景中创建入口。
3. 在 `FrameSettings` 中按需关闭模块。

## 模块总览

| 模块 | 命名空间 | 接口 | 说明 |
| --- | --- | --- | --- |
| Core | `Frame.Core` | `Framework` | 启动入口、模块生命周期、服务注册、日志 |
| Lifecycle | `Frame.Lifecycle` | `ILifecycleService` | 应用暂停、焦点、退出事件 |
| Events | `Frame.Events` | `IEventBus` | 类型安全事件总线 |
| Time | `Frame.Timing` | `ITimerService` | Update 驱动定时器 |
| Pooling | `Frame.Pooling` | `IPoolService` | 对象池 / GameObject 池 |
| Assets | `Frame.Assets` | `IAssetService` | 资源加载抽象（Resources / Addressables / YooAsset） |
| Scenes | `Frame.Scenes` | `ISceneService` | SceneManager 封装 |
| UI | `Frame.UI` | `IUIService` | UGUI 分层、路由、返回栈、模态 |
| Guide | `Frame.Runtime.Guide` | `IGuideService` | 新手引导、镂空遮罩 |
| Audio | `Frame.Audio` | `IAudioService` | BGM / SFX / 分组音量 |
| Tweening | `Frame.Tweening` | `ITweenService` | 补间动画抽象 |
| Config | `Frame.Config` | `IConfigService` | JSON / ScriptableObject 配置 |
| Save | `Frame.Save` | `ISaveService` | 本地存档（JSON / 二进制 / AES） |
| Preferences | `Frame.Preferences` | `IPreferencesService` | PlayerPrefs 偏好设置 |
| Input | `Frame.Input` | `IInputService` | InputSystem / Legacy 适配 |
| Networking | `Frame.Networking` | `IHttpService` / `ISocketService` | HTTP + TCP/WebSocket 长连接 |
| Localization | `Frame.Localization` | `ILocalizationService` | Excel/CSV 多语言文本表 |
| StateMachine | `Frame.StateMachine` | — | 通用状态机 |
| Diagnostics | `Frame.Diagnostics` | `IDiagnosticsService` | 日志缓冲、运行时指标、调试面板 |
| Utilities | `Frame.Utilities` | — | 路径 / 释放等通用工具 |
| Utility | `Frame.Utility` | — | 数学 / 几何工具（贝塞尔等） |

## 模块详解

每个模块在 `example/README.md` 下提供了完整的用法示例，下面是各模块的简介与入口链接。

### Core 核心底座

`Frame.Core` —— 框架底座，负责启动入口、模块生命周期、服务注册和日志。

- `Framework`：全局静态门面，暴露 `Resolve<T>()`、`Context`、`Services`、`Modules`。
- `GameEntry`：Unity 生命周期入口，驱动模块 `Tick`、暂停、焦点、退出。
- `ModuleManager`：按优先级初始化、按倒序销毁模块。
- `ServiceRegistry`：轻量服务定位器。
- `IFrameModuleInstaller`：可选集成（Addressables / YooAsset / DOTween）的安装扩展点。
- `FrameSettings`：ScriptableObject 配置，控制启用哪些模块。
- `FrameLog`：统一日志入口，支持级别过滤与缓冲。

📖 用法示例：[Assets/Frame/Runtime/Core/example/README.md](Assets/Frame/Runtime/Core/example/README.md)

### Lifecycle 生命周期

`Frame.Lifecycle` —— 对业务暴露应用暂停 / 恢复、焦点获得 / 失去、退出前事件，避免每个业务对象各自挂 Unity 生命周期回调。常用于切后台暂停战斗、退出前同步存档。

📖 用法示例：[Assets/Frame/Runtime/Lifecycle/example/README.md](Assets/Frame/Runtime/Lifecycle/example/README.md)

### Events 事件总线

`Frame.Events` —— 类型安全事件总线，支持 `once` 一次性订阅、按 `owner` 批量解绑、单个处理器异常隔离。适合 UI / 系统间解耦。

📖 用法示例：[Assets/Frame/Runtime/Events/example/README.md](Assets/Frame/Runtime/Events/example/README.md)

### Time 定时器

`Frame.Timing` —— 基于 Update 驱动的定时器服务，支持缩放 / 非缩放延迟、循环、按 `owner` 批量取消、运行时计时器统计。

📖 用法示例：[Assets/Frame/Runtime/Time/example/README.md](Assets/Frame/Runtime/Time/example/README.md)

### Pooling 对象池

`Frame.Pooling` —— `ObjectPool<T>` 面向纯 C# 对象，`GameObjectPool` 面向 prefab 实例，支持预热、容量上限、`IPoolable` 生命周期回调、运行时统计。

📖 用法示例：[Assets/Frame/Runtime/Pooling/example/README.md](Assets/Frame/Runtime/Pooling/example/README.md)

### Assets 资源加载

`Frame.Assets` —— 资源加载抽象层，默认 `Resources` 实现，可通过 `FrameSettings.AssetServiceBackend` 切换到 Addressables 或 YooAsset。统一暴露 `IAssetService`、引用计数、异步请求状态 / 取消、资源诊断。资源句柄 `AssetHandle` 使用 `Dispose` 释放。

📖 用法示例：[Assets/Frame/Runtime/Assets/example/README.md](Assets/Frame/Runtime/Assets/example/README.md)

### Scenes 场景管理

`Frame.Scenes` —— 封装同步 / 异步加载、叠加加载、卸载、Build Settings 校验、加载状态 / 事件、手动激活和进度回调。

📖 用法示例：[Assets/Frame/Runtime/Scenes/example/README.md](Assets/Frame/Runtime/Scenes/example/README.md)

### UI 用户界面

`Frame.UI` —— 自动创建 UGUI root，内置分层、`UIPanelBase` 生命周期、路由、返回栈、模态遮罩、弹窗队列、异步打开、强类型参数和淡入淡出动画扩展点。`SafeAreaFitter` 组件适配全面屏安全区。

📖 用法示例：[Assets/Frame/Runtime/UI/example/README.md](Assets/Frame/Runtime/UI/example/README.md)

### Guide 新手引导

`Frame.Runtime.Guide` —— 基于 `GuideConfig` 和 `GuideTarget` 的新手引导服务，支持矩形 / 圆角矩形 / 圆形 / 椭圆镂空遮罩，支持点击目标、点击任意处、自定义事件三类推进方式，可配置自定义提示框 prefab，按 `GuideGroupId` 持久化步骤进度。

📖 用法示例：[Assets/Frame/Runtime/Guide/example/README.md](Assets/Frame/Runtime/Guide/example/README.md)

### Audio 音频

`Frame.Audio` —— BGM 淡入淡出、SFX source 池、Master / Music / Sfx / UI / Ambient 分组音量。

📖 用法示例：[Assets/Frame/Runtime/Audio/example/README.md](Assets/Frame/Runtime/Audio/example/README.md)

### Tweening 补间动画

`Frame.Tweening` —— 业务依赖 `ITweenService`，当前通过 DOTween 适配层实现，见 [Integrations/DOTween](#dotween)。

📖 用法示例：[Assets/Frame/Runtime/Tweening/example/README.md](Assets/Frame/Runtime/Tweening/example/README.md)

### Config 配置

`Frame.Config` —— 默认通过 `IAssetService` 读取 `Configs` 下的 JSON / ScriptableObject 配置，支持运行时 JSON 覆盖、读取缓存、多 provider 优先级和 `IConfigValidator` 校验。

📖 用法示例：[Assets/Frame/Runtime/Config/example/README.md](Assets/Frame/Runtime/Config/example/README.md)

### Save 存档

`Frame.Save` —— 基于 `Application.persistentDataPath`，默认 Newtonsoft.Json，支持二进制 serializer、AES 加密、多 slot、列表、删除、metadata 校验、备份恢复、版本迁移、自定义 serializer。同步与异步 API 并存。

📖 用法示例：[Assets/Frame/Runtime/Save/example/README.md](Assets/Frame/Runtime/Save/example/README.md)

### Preferences 偏好设置

`Frame.Preferences` —— 基于 `PlayerPrefs` 的轻量用户设置服务，支持 int / float / string / bool / JSON、变更事件和显式 `Save()`。

📖 用法示例：[Assets/Frame/Runtime/Preferences/example/README.md](Assets/Frame/Runtime/Preferences/example/README.md)

### Input 输入

`Frame.Input` —— 项目启用 InputSystem 时可挂接 `InputActionAsset`，支持输入上下文栈和绑定覆盖保存 / 加载；否则保留 Legacy `KeyCode` 读取。

📖 用法示例：[Assets/Frame/Runtime/Input/example/README.md](Assets/Frame/Runtime/Input/example/README.md)

### Networking 网络

`Frame.Networking` —— 基于 `UnityWebRequest` 的 GET / POST / PUT / DELETE（`IHttpService`），以及独立的 `SocketService` 长连接模块；支持 TCP、WebSocket、长度前缀编解码、连接状态事件、发送队列、心跳、自动重连和收发指标。

📖 用法示例：[Assets/Frame/Runtime/Networking/example/README.md](Assets/Frame/Runtime/Networking/example/README.md)

### Localization 多语言

`Frame.Localization` —— 轻量多语言文本表，支持 Excel 导出 CSV / TSV 的 key + 多语言列结构、fallback locale、格式化文本、缺失 key 统计，并提供 UGUI `LocalizedText` 自动刷新组件。目录下有 `ExampleLocalization.csv/.xlsx` 作为表结构参考。

📖 用法示例：[Assets/Frame/Runtime/Localization/example/README.md](Assets/Frame/Runtime/Localization/example/README.md)

### StateMachine 状态机

`Frame.StateMachine` —— 通用状态机，支持状态、转换、条件、参数（bool / int / float / trigger）、图层与状态切换事件。

📖 用法示例：[Assets/Frame/Runtime/StateMachine/example/README.md](Assets/Frame/Runtime/StateMachine/example/README.md)

### Diagnostics 诊断

`Frame.Diagnostics` —— 运行时日志缓冲、日志事件、日志文件落盘（`FileLogSink`）、FPS / 内存 / 错误计数快照和可选 IMGUI 调试面板（`RuntimeDiagnosticsOverlay`）。在 `FrameSettings` 中开启 Overlay，默认反引号键（`` ` ``）切换显示。

📖 用法示例：[Assets/Frame/Runtime/Diagnostics/example/README.md](Assets/Frame/Runtime/Diagnostics/example/README.md)

### Utilities / Utility 工具

- `Frame.Utilities`：框架内部通用工具 —— `FramePathUtility` 路径处理、`DisposableAction` 作用域式释放。

  📖 [Assets/Frame/Runtime/Utilities/example/README.md](Assets/Frame/Runtime/Utilities/example/README.md)

- `Frame.Utility`：业务通用数学 / 几何工具 —— `BezierUtility` 贝塞尔曲线。

  📖 [Assets/Frame/Runtime/Utility/example/README.md](Assets/Frame/Runtime/Utility/example/README.md)

### Integrations 集成层

`Assets/Frame/Integrations` 下是可选的程序集，通过 `IFrameModuleInstaller` 在框架初始化时按 `FrameSettings` 注册：

- **Addressables**（`Frame.Addressables`）：把 Unity Addressables 适配成 `IAssetService`。

  📖 [Assets/Frame/Integrations/Addressables/example/README.md](Assets/Frame/Integrations/Addressables/example/README.md)

- **YooAsset**（`Frame.YooAsset`）：把 YooAsset 适配成 `IAssetService`。

  📖 [Assets/Frame/Integrations/YooAsset/example/README.md](Assets/Frame/Integrations/YooAsset/example/README.md)

- **DOTween**（`Frame.DOTween`）：DOTween 对 `ITweenService` 的实现，注册 `DOTweenTweenService`。

  📖 [Assets/Frame/Integrations/DOTween/example/README.md](Assets/Frame/Integrations/DOTween/example/README.md)

<a id="dotween"></a>

### Editor 编辑器工具

`Frame.Editor`（仅 Editor）—— Unity 菜单和项目校验工具：创建 `FrameSettings`、创建场景入口、打开 README、`Validate Project` 校验。

📖 用法示例：[Assets/Frame/Editor/example/README.md](Assets/Frame/Editor/example/README.md)

### Tests 测试

`Assets/Tests` —— EditMode 与 PlayMode 测试，覆盖框架核心模块。

📖 [Assets/Tests/README.md](Assets/Tests/README.md)

## 编辑器工具与 CI

Unity 顶部菜单 `Frame` 提供：

- `Create Default Frame Settings`：生成 `Assets/Frame/Resources/Frame/FrameSettings.asset`。
- `Create GameEntry In Scene`：在场景中创建入口。
- `Open README`：打开框架说明。
- `Validate Project`：校验项目。

构建前建议执行 `Frame/Validate Project`。当前校验项包括：FrameSettings、GameEntry 数量、Build Settings 场景、关键包依赖、Runtime asmdef 引用、DOTween 集成资源、Resources 路径冲突、Resources/UI prefab 面板组件、Resources/Configs JSON 格式。

CI 中调用同一套校验入口，存在错误时 Unity 以非 0 退出码结束（警告不会让流水线失败）：

```powershell
& "D:\UnityEditor\6000.5.0f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "E:\UnityProject\Framework" `
  -executeMethod Frame.Editor.FrameMenuItems.ValidateProjectForCI `
  -quit
```

运行时排查可在 `FrameSettings` 中打开 Runtime Diagnostics Overlay，默认反引号键（`` ` ``）切换显示，面板展示 FPS / 内存、生命周期状态、HTTP 指标、Socket 连接指标、计时器数量、场景加载状态、资源引用计数、对象池统计和最近日志。

## 常用服务接口

业务代码建议优先依赖接口：

| 接口 | 获取方式 | 用途 |
| --- | --- | --- |
| `IEventBus` | `Framework.Resolve<IEventBus>()` | 发布 / 订阅事件 |
| `ITimerService` | `Framework.Resolve<ITimerService>()` | 延迟 / 循环定时器 |
| `IPoolService` | `Framework.Resolve<IPoolService>()` | 对象池 |
| `IAssetService` | `Framework.Resolve<IAssetService>()` | 资源加载 |
| `ISceneService` | `Framework.Resolve<ISceneService>()` | 场景加载 |
| `IUIService` | `Framework.Resolve<IUIService>()` | UI 打开 / 关闭 / 路由 |
| `IGuideService` | `Framework.Resolve<IGuideService>()` | 新手引导 |
| `IAudioService` | `Framework.Resolve<IAudioService>()` | 音频播放 / 音量 |
| `ITweenService` | `Framework.Resolve<ITweenService>()` | 补间动画 |
| `IConfigService` | `Framework.Resolve<IConfigService>()` | 配置读取 |
| `ISaveService` | `Framework.Resolve<ISaveService>()` | 本地存档 |
| `IPreferencesService` | `Framework.Resolve<IPreferencesService>()` | 偏好设置 |
| `IInputService` | `Framework.Resolve<IInputService>()` | 输入 / 上下文栈 |
| `IHttpService` | `Framework.Resolve<IHttpService>()` | HTTP 请求 |
| `ISocketService` | `Framework.Resolve<ISocketService>()` | TCP / WebSocket 长连接 |
| `ILocalizationService` | `Framework.Resolve<ILocalizationService>()` | 多语言 |
| `ILifecycleService` | `Framework.Resolve<ILifecycleService>()` | 应用生命周期事件 |
| `IDiagnosticsService` | `Framework.Resolve<IDiagnosticsService>()` | 诊断 / 日志 |

## 基础示例

事件：

```csharp
public struct PlayerLevelChanged { public int Level; }

Framework.Resolve<IEventBus>().Publish(new PlayerLevelChanged { Level = 3 });
```

定时器：

```csharp
TimerHandle handle = Framework.Resolve<ITimerService>()
    .Delay(1f, () => Debug.Log("delay"), unscaled: true, owner: this);
```

UI：

```csharp
Framework.Resolve<IUIService>().Open<MainMenuPanel>("UI/MainMenu", UILayer.Normal);
```

新手引导：

```csharp
Framework.Resolve<IGuideService>().StartGuide(tutorialGuideConfig);
```

输入上下文：

```csharp
IDisposable scope = Framework.Resolve<IInputService>().PushContext(InputContext.UI);
Framework.Resolve<IUIService>().OpenRoute<ShopPanel>("shop");
// 面板关闭或流程结束时调用 scope.Dispose() 恢复之前的输入上下文。
```

存档：

```csharp
Framework.Resolve<ISaveService>().Save("slot_1", saveData, dataVersion: 2);
await Framework.Resolve<ISaveService>().SaveAsync("slot_1", saveData, dataVersion: 2);
```

偏好设置：

```csharp
IPreferencesService pref = Framework.Resolve<IPreferencesService>();
pref.SetFloat("audio.music", 0.8f);
pref.SetString("locale", "zh");
pref.Save();
```

HTTP：

```csharp
IHttpService http = Framework.Resolve<IHttpService>();
http.BaseUrl = "https://example.com/api";
http.SetBearerToken("access-token");
http.ResponseParser = new EnvelopeHttpResponseParser();

http.GetJson<VersionResponse>("version", response =>
{
    Debug.Log(response.Success ? response.Value.Version : response.Error);
});
```

Socket：

```csharp
ISocketService sockets = Framework.Resolve<ISocketService>();
ISocketClient client = sockets.CreateWebSocketClient("wss://example.com/realtime", options =>
{
    options.AutoReconnect = true;
    options.HeartbeatIntervalSeconds = 10f;
    options.HeartbeatTimeoutSeconds = 30f;
    options.HeartbeatPayload = System.Text.Encoding.UTF8.GetBytes("ping");
});

client.MessageReceived += (socket, message) => Debug.Log(message.Text);
await client.ConnectAsync();
client.SendText("hello");
```

---

## License

本项目基于 [MIT License](LICENSE) 开源。

Copyright (c) 2026 oujie

简而言之：你可以自由使用、复制、修改、合并、发布、分发甚至商用本项目，只需在副本中保留原始版权声明和本许可声明即可。本项目按 "现状" 提供，不附带任何担保。详见 [LICENSE](LICENSE) 全文。

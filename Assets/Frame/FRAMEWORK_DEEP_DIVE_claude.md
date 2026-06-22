# Frame 框架深度解析（Claude 版）

> 本文档（`FRAMEWORK_DEEP_DIVE_claude.md`）面向准备阅读源码、二次开发、或在真实项目中深度使用 `Assets/Frame` 框架的工程师。它在 `README.md` 的基础上进一步下沉到**每个文件、每个类型、每个方法与属性**的实现细节，并补充了大量**流转逻辑（control flow）**、**设计意图（why）** 与**踩坑点（gotchas）**。
>
> 与 `FRAMEWORK_DEEP_DIVE.md`（codex 版）相比，本文档：
> - 以「流转逻辑 + 逐成员表 + 设计意图」三段式覆盖每个模块；
> - 增加了**模块间端到端协作链路**章节（启动链、UI 打开链、资源加载与释放链、存档链、场景切换链）；
> - 增加了**扩展指南**（自定义模块 / 自定义资源后端 / 自定义序列化器 / 自定义网络协议解析器 / 自定义 UI 过渡）；
> - 对每个公开与关键私有成员，尽量给出**精确签名**与**调用来源**。

---

## 目录

**基础**
1. [阅读指引](#1-阅读指引)
2. [程序集（asmdef）结构](#2-程序集asmdef结构)
3. [总体架构](#3-总体架构)
4. [Core 模块](#4-core-模块)

**基础/辅助模块**
5. [Diagnostics 模块](#5-diagnostics-模块)
6. [Lifecycle 模块](#6-lifecycle-模块)
7. [Events 模块](#7-events-模块)
8. [Time 模块](#8-time-模块)
9. [Preferences 模块](#9-preferences-模块)
10. [Pooling 模块](#10-pooling-模块)
11. [StateMachine 模块](#11-statemachine-模块)
12. [Utilities 模块](#12-utilities-模块)

**业务常用模块**
13. [Assets 模块](#13-assets-模块)
14. [Scenes 模块](#14-scenes-模块)
15. [Audio 模块](#15-audio-模块)
16. [UI 模块](#16-ui-模块)
17. [Save 模块](#17-save-模块)
18. [Config 模块](#18-config-模块)
19. [Networking 模块](#19-networking-模块)
20. [Input 模块](#20-input-模块)
21. [Localization 模块](#21-localization-模块)
22. [Tweening 模块](#22-tweening-模块)

**集成层与工具**
23. [Integrations 集成层](#23-integrations-集成层)
24. [Editor 编辑器工具](#24-editor-编辑器工具)
25. [Samples 示例](#25-samples-示例)

**横向串联与扩展**
26. [模块间端到端协作链路](#26-模块间端到端协作链路)
27. [扩展指南](#27-扩展指南)
28. [生产化检查表（速查）](#28-生产化检查表速查)
29. [附录：模块、接口、Priority 速查表](#29-附录模块接口priority-速查表)

---

## 1. 阅读指引

推荐阅读顺序：

1. **第 3 章 总体架构**：先建立「模块化服务注册 + 静态门面 + Unity 生命周期转发」的整体心智模型。
2. **第 4 章 Core 模块**：理解启动、模块生命周期调度、服务定位、日志，这是所有其它模块的地基。
3. **常用业务模块**：Assets → Scenes → UI → Audio → Save → Config → Networking → Input。
4. **辅助/基础模块**：Diagnostics、Lifecycle、Events、Time、Preferences、Pooling、Localization、StateMachine、Tweening、Utilities。
5. **集成层与工具**：Integrations（Addressables / YooAsset / DOTween）、Editor、Samples。
6. **第 X 章 模块间协作链路** 与 **扩展指南**：把零散模块串成完整业务流程。

约定：

- 业务代码应放在 `Assets/Game` 或 `Assets/Scripts/Game`，通过 `Framework.Resolve<TService>()` 获取框架服务，**不要把业务逻辑写进 `Assets/Frame`**。
- 业务层应**优先依赖接口**（`IUIService`、`ISaveService`、`IHttpService`、`ISocketService` 等），只有确实需要实现类特有能力时才解析具体类型，以降低替换实现的成本。

---

## 2. 程序集（asmdef）结构

| 程序集 | 位置 | 作用 | 关键依赖 |
| --- | --- | --- | --- |
| `Frame.Runtime` | `Assets/Frame/Runtime` | 框架运行时主程序集，包含 Core 与全部内置模块 | UniTask、Newtonsoft.Json、Unity.InputSystem |
| `Frame.Addressables` | `Assets/Frame/Integrations/Addressables` | Addressables 后端实现，独立程序集，按需启用 | `Frame.Runtime`、Addressables |
| `Frame.YooAsset` | `Assets/Frame/Integrations/YooAsset` | YooAsset 后端实现，独立程序集，按需启用 | `Frame.Runtime`、YooAsset |
| DOTween 集成 | `Assets/Frame/Integrations/DOTween` | DOTween 对 `ITweenService` 的适配 | `Frame.Runtime`、DOTween |
| `Frame.Editor` | `Assets/Frame/Editor` | 编辑器菜单与项目校验工具 | `Frame.Runtime`、UnityEditor |

`Frame.Runtime/Core/AssemblyInfo.cs` 通过 `[assembly: InternalsVisibleTo("Frame.Addressables")]` 和 `[assembly: InternalsVisibleTo("Frame.YooAsset")]` 把内部成员（如 `AssetHandle` 的 internal 构造函数、`AssetInstanceLease` 等）开放给两个资源集成程序集，使集成层能够构造框架资源句柄而不必把这些构造函数变成 public。

> **设计意图**：把 Addressables / YooAsset / DOTween 拆成独立程序集，意味着「不安装对应第三方包的项目」不会因为缺少类型而编译失败——只要不引用对应集成程序集即可。`Frame.Runtime` 本身只依赖 Resources（Unity 内置）作为默认资源后端。

---

## 3. 总体架构

框架采用**「模块化服务注册」**设计，四个核心角色：

- **`GameEntry`**：Unity 场景中的 `MonoBehaviour` 入口组件，**唯一**职责是把 Unity 的生命周期回调（`Awake/Start/Update/FixedUpdate/LateUpdate/OnApplicationPause/Focus/Quit`）转发给静态门面 `Framework`。它本身不实现任何业务。
- **`Framework`**：静态门面（`static class`），负责自动启动、初始化、关闭、服务解析、默认模块注册和扩展模块扫描。它是业务层最常用的入口（`Framework.Resolve<T>()`）。
- **`ModuleManager`**：模块容器，按 `Priority` 升序初始化与 Update，按倒序 Shutdown。
- **`ServiceRegistry`**：轻量服务容器（`Dictionary<Type, object>`），按类型注册与解析。

每个具体能力都是一个继承 `GameModuleBase` 的**模块**，在 `OnInitialize()` 中把自己注册进 `ServiceRegistry`。业务层只依赖接口。

### 3.1 模块初始化优先级表

`Priority` 数值越小越早初始化、越晚关闭（Shutdown 为倒序）。下表是内置模块的优先级（取自各模块源码的 `Priority` override）：

| 模块 | 实现类型 | Priority | 说明 |
| --- | --- | ---: | --- |
| Diagnostics | `DiagnosticsService` | **-1000** | 最早初始化，以便统计后续模块初始化期间产生的日志 |
| Lifecycle | `LifecycleService` | **-950** | 记录暂停/焦点/退出状态 |
| Events | `EventBus` | **-900** | 类型安全事件总线，供其它模块初始化时即可解析 |
| Preferences | `PreferencesService` | **-850** | `PlayerPrefs` 偏好读写 |
| Time | `TimerService` | **-800** | Update 驱动定时器 |
| Pooling | `PoolService` | **-700** | GameObject 对象池管理 |
| Assets | `ResourcesAssetService` / `AddressablesAssetService` / `YooAssetAssetService` | **-600** | 资源加载与引用计数，后端由 `FrameSettings.AssetServiceBackend` 决定 |
| Scenes | `SceneService` | **-500** | SceneManager 封装 |
| UI | `UIService` | **-400** | UGUI root、路由、栈、弹窗 |
| Audio | `AudioService` | **-300** | 音源池、BGM、音效 |
| DOTween | `DOTweenTweenService` | **-250** | DOTween 适配（通过 installer 安装，不在默认注册表中） |
| Config | `ConfigService` | **-200** | 配置 Provider 链 |
| Save | `SaveService` | **-100** | 本地存档 |
| Input | `InputService` | **0** | 输入上下文与绑定 |
| Networking | `SocketService` / `HttpService` | **-90 / 0** | TCP/WebSocket 长连接和 HTTP 请求 |
| Localization | `LocalizationService` | **0** | 本地化文本 |

> **注意**：Priority 相同的模块（Input / HTTP / Localization 都是 0），其相对顺序取决于 `List.Sort` 的实现（非稳定排序）以及它们加入 `ModuleManager` 的顺序。业务**不应**依赖同优先级模块之间的相对初始化顺序。如果模块 A 在 `OnInitialize()` 中需要解析模块 B，应保证 B 的 Priority 严格小于 A。`SocketService` 的 Priority 是 -90，会早于默认 0 优先级模块初始化。

### 3.2 启动流转（自动启动路径）

```
[Unity 引擎] 
   │ RuntimeInitializeOnLoadMethod(SubsystemRegistration)
   ▼
Framework.ResetStatics()                 // 清空所有静态状态，适配「关闭 Domain Reload」的 Enter Play Mode
   │ RuntimeInitializeOnLoadMethod(BeforeSceneLoad)
   ▼
Framework.AutoBootstrap()
   │  FrameSettings.LoadOrDefault()       // 从 Resources/Frame/FrameSettings 加载，缺失则 CreateInstance 兜底
   │  if (settings.AutoCreateGameEntry)
   ▼
GameEntry.Ensure(settings)               // 查找/创建名为 "Frame" 的 GameObject 并挂 GameEntry
   │  (先 SetActive(false) → AddComponent → UseSettings → SetActive(true))
   ▼
GameEntry.Awake() → OnSingletonAwake()
   │  settings 兜底 → if (initializeOnAwake)
   ▼
Framework.Initialize(this, settings)
   │  1. FrameLog.Configure(settings)             // 应用日志开关与最低等级
   │  2. ApplyApplicationSettings(settings)       // runInBackground / targetFrameRate
   │  3. new ServiceRegistry() / new ModuleManager()
   │  4. new FrameContext(entry, settings, services, entry.transform)
   │  5. services.Register(settings); services.Register(services)
   │  6. RegisterDefaultModules(settings)         // 按开关 modules.Add(new XxxService())
   │  7. RegisterInstalledModules(settings)       // 反射扫描 IFrameModuleInstaller（如 DOTween）
   │  8. modules.InitializeAll(context)           // 按 Priority 升序 Initialize
   │  9. IsInitialized = true
   │ 10. CreateRuntimeDiagnosticsOverlay(...)     // 可选 IMGUI 诊断面板
   │ (任一步抛异常 → FrameLog.Exception → CleanupFailedInitialization → 抛 FrameException)
   ▼
GameEntry.Start()   → Framework.Start()   → modules.StartAll()
GameEntry.Update()  → Framework.Update()  → modules.UpdateAll()
   ... 每帧转发 ...
GameEntry.OnApplicationQuit (OnSingletonApplicationQuit)
   → Framework.OnApplicationQuit() → modules.ApplicationQuitAll()   // 先通知模块退出
   → Framework.Shutdown()          → modules.ShutdownAll() (倒序) → services.Clear()
```

**手动启动路径**：菜单 `Frame/Create GameEntry In Scene` 在场景中放置 `GameEntry`，在 Inspector 指定 `FrameSettings`，并把 `FrameSettings.AutoCreateGameEntry` 关闭以避免重复入口；如果想完全手动控制初始化时机，可把 `GameEntry.initializeOnAwake` 设为 false，再在合适的时机自行调用 `Framework.Initialize(entry, settings)`。

### 3.3 失败清理（半初始化保护）

`Framework.Initialize()` 的步骤 6~10 全部包在 `try/catch` 中。任一模块 `OnInitialize()` 抛异常时：

1. `FrameLog.Exception(exception)` 记录原始异常；
2. `CleanupFailedInitialization()`：若 `modules != null` 则 `ShutdownAll()`（倒序关闭**已成功初始化**的模块），若 `services != null` 则 `Clear()`（释放实现了 `IDisposable` 的服务），随后清空全部静态引用并把 `IsInitialized` 置回 false；
3. 抛出 `new FrameException("Framework initialization failed.", exception)`，保留 inner exception。

此外，`GameModuleBase.Initialize()` 内部也有一层保护：单个模块的 `OnInitialize()` 中途失败时，会 `try { OnShutdown(); } finally { context = null; IsInitialized = false; }` 后再 `throw`，让模块自身的局部资源（已注册的事件、已创建的 GameObject）有机会回滚。

> **设计意图**：两层清理保证「框架要么完整初始化，要么完全不保留半初始化状态」，避免出现「某些服务已注册、某些没注册」的不一致状态导致后续 `Resolve` 行为诡异。

### 3.4 服务解析

```csharp
using Frame.Core;
using Frame.UI;
using Frame.Save;

// 强制解析：服务不存在时抛 FrameException
IUIService ui = Framework.Resolve<IUIService>();
ISaveService save = Framework.Resolve<ISaveService>();

// 尝试解析：用于可选模块、可能被 FrameSettings 关闭的服务
if (Framework.TryResolve(out IUIService optionalUi))
{
    optionalUi.CloseAll();
}
```

`Framework.Resolve<T>()` 在未初始化时抛 `FrameException`；`TryResolve<T>()` 在未初始化时返回 false 并把 out 参数置 null。**当某模块在 `FrameSettings` 中被关闭后，对应接口的 `Resolve<T>()` 会失败**，因此对可选依赖应使用 `TryResolve`。

---

## 4. Core 模块

Core 是框架的启动与生命周期基础设施，不承载具体业务。它的核心设计是**把 Unity 不可控、不可排序、难以测试的生命周期，转换成可排序、可测试的模块生命周期**。

### 4.1 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `FrameSettings` | 全局配置资源（ScriptableObject） | 控制自动启动、DontDestroyOnLoad、日志等级、运行时诊断、模块开关、资源后端、UI 分辨率、音频池与 Mixer、存档目录、池默认容量 |
| `Framework` | 框架静态门面 | `Initialize`/`Shutdown`/生命周期转发/`Resolve`/`TryResolve`，注册默认与扩展模块，失败清理 |
| `GameEntry` | Unity 入口组件 | 继承 `MonoSingleton<GameEntry>`，`DefaultExecutionOrder(-10000)` 保证最早执行，转发 Unity 生命周期 |
| `FrameContext` | 模块初始化上下文 | 只读持有 `Entry`/`Settings`/`Services`/`Root` |
| `ServiceRegistry` | 服务容器 | `Dictionary<Type, object>` 注册解析；`Clear()` 释放 `IDisposable` 且去重避免重复 Dispose |
| `ModuleManager` | 模块管理器 | 添加/按优先级排序/初始化/Start/Update/Pause/Focus/Quit/倒序 Shutdown |
| `IFrameModule` | 模块生命周期协议 | 定义 Name/Priority/IsInitialized 与全部生命周期回调 |
| `GameModuleBase` | 模块推荐基类 | 模板方法 `OnInitialize`/`OnShutdown`，封装初始化状态与失败回滚 |
| `IFrameModuleInstaller` | 外部模块安装入口 | 供独立程序集（DOTween 等）自动把模块加入 `ModuleManager` |
| `FrameLog` | 框架日志入口 | 受 `EnableLogs`/`MinimumLogLevel` 控制，维护环形缓冲并触发 `EntryWritten` |
| `FrameLogEntry` | 单条日志数据 | 等级/原始消息/格式化消息/异常/UTC ticks |
| `FrameLogLevel` | 日志等级枚举 | `Trace=0`…`Error=4`、`Off=5` |
| `FrameException` | 框架异常类型 | 区分框架级错误与业务异常，支持 inner exception |
| `Singleton<T>` | 纯 C# 懒加载单例基类 | 双检锁 + `Activator.CreateInstance(true)` + 初始化/释放钩子 |
| `MonoSingleton<T>` | MonoBehaviour 单例基类 | 查找/创建场景对象、处理重复实例、退出标记、可选 DontDestroyOnLoad |

### 4.2 `FrameSettings.cs`

全局配置资产，标注 `[CreateAssetMenu(menuName = "Frame/Frame Settings")]`，默认放在 `Resources/Frame/FrameSettings.asset`。所有序列化字段都通过**只读属性**暴露，并在 getter 中做兜底/范围限制（防止 Inspector 填入非法值导致运行时崩溃）。

| 成员 | 默认值 | 作用 | 实现/注意点 |
| --- | --- | --- | --- |
| `ResourcesPath`（const） | `"Frame/FrameSettings"` | 配置在 Resources 下的相对路径 | `LoadOrDefault()` 用它加载 |
| `AutoCreateGameEntry` | true | 是否在 `BeforeSceneLoad` 自动创建入口 | 关闭后需手动放置 `GameEntry` |
| `UseDontDestroyOnLoad` | true | 入口对象是否跨场景保留 | `GameEntry.UseDontDestroyOnLoad` 读取 |
| `RunInBackground` | true | `Application.runInBackground` | 初始化时应用 |
| `TargetFrameRate` | -1 | 目标帧率 | **为 0 时不改 Unity 当前值**；非 0 时设置 `Application.targetFrameRate`（-1 表示平台默认/不限制） |
| `EnableLogs` | true | 是否启用 `FrameLog` 输出 | 不拦截业务自己的 `Debug.Log` |
| `MinimumLogLevel` | `Info` | 最低输出等级 | `level < minimumLevel` 的日志被丢弃 |
| `EnableRuntimeDiagnosticsOverlay` | false | 是否创建 IMGUI 诊断面板 | 面板在模块初始化之后创建 |
| `RuntimeDiagnosticsOverlayVisibleOnStart` | false | 面板启动时是否可见 | — |
| `RuntimeDiagnosticsOverlayToggleKey` | `KeyCode.BackQuote`（反引号） | 切换面板显示的按键 | 设为 `None` 可禁用快捷键 |
| `EnableDiagnosticsService` … `EnableLocalizationService` | 均 true | 16 个模块开关 | `RegisterDefaultModules()` 逐个判断；注意 `EnableTweenService` 由 DOTween installer 读取，不在默认注册逻辑里 |
| `AssetServiceBackend` | `Resources` | 资源后端选择 | 只有 `Resources` 在 `RegisterDefaultModules` 内注册；Addressables/YooAsset 由各自 installer 注册 |
| `YooAssetPackageName` | `"DefaultPackage"` | YooAsset 包名 | getter 做 trim + 空白兜底 |
| `YooAssetPlayMode` | `EditorSimulate` | YooAsset 运行模式 | 见 Assets 模块 |
| `YooAssetEditorPackageRoot`/`YooAssetBuiltinPackageRoot` | 空 | YooAsset 包根目录覆盖 | getter trim + 空白兜底 |
| `YooAssetDefaultHostServer`/`YooAssetFallbackHostServer` | 空 | Host/Web 模式远端 URL | getter trim |
| `YooAssetDownloadMaxConcurrency` | 5 | 下载并发 | getter `Mathf.Max(1, …)` |
| `YooAssetDownloadMaxRequestPerFrame` | 1 | 每帧请求数 | getter `Mathf.Max(1, …)` |
| `YooAssetDownloadWatchdogTimeout` | 10 | 下载看门狗超时 | getter `Mathf.Max(1, …)` |
| `UIRootName` | `"UIRoot"` | UI 根节点名 | 空白时返回 `"UIRoot"` |
| `UIReferenceResolution` | (1920,1080) | CanvasScaler 参考分辨率 | getter `Mathf.Max(1, …)` 防止 0/负数 |
| `UIMatchWidthOrHeight` | 0.5 | CanvasScaler match | getter `Mathf.Clamp01` |
| `AudioSourcePoolSize` | 16 | SFX 音源池大小 | getter `Mathf.Max(1, …)` |
| `GetAudioMixerGroup(category)` | — | 取最终 MixerGroup | 分类组缺失时回退 master 组 |
| `GetAssignedAudioMixerGroup(category)` | — | 取分类显式配置的组 | switch Music/Sfx/UI/Ambient，默认 master |
| `GetAudioMixerVolumeParameter(category)` | — | 取音量 exposed 参数名 | 默认 `MasterVolume`/`MusicVolume`/… |
| `SaveFolderName` | `"Saves"` | 存档子目录名 | 空白时返回 `"Saves"` |
| `DefaultGameObjectPoolMaxSize` | 128 | GameObject 池默认容量 | getter `Mathf.Max(1, …)` |
| `LoadOrDefault()`（static） | — | 加载或创建运行时配置 | 优先 `Resources.Load`，找不到则 `CreateInstance` 并命名 `"Runtime FrameSettings"`，**此时全部使用源码默认值** |
| `OnValidate()`（`#if UNITY_EDITOR`） | — | Inspector 改值后修正非法数值 | 限制 UI 尺寸、match、音频池、池容量、YooAsset 下载参数；运行时 build 不含此方法 |

> **踩坑点**：`GetAudioMixerGroup` 与 `GetAssignedAudioMixerGroup` 的区别在于「是否回退到 master」。`AudioService` 给音源分配 MixerGroup 时用前者（保证至少受 master 控制），而判断「某分类是否单独配了 mixer」时用后者。

### 4.3 `Framework.cs`（静态门面）

| 成员 | 签名 | 作用 | 实现/注意点 |
| --- | --- | --- | --- |
| `IsInitialized` | `static bool { get; private set; }` | 框架是否初始化完成 | 初始化成功置 true；shutdown/失败清理置 false |
| `Context` | `static FrameContext` | 当前上下文 | 未初始化时为 null |
| `Services` | `static ServiceRegistry` | 当前服务容器 | 推荐用 `Resolve<T>()` 而非直接操作 |
| `Modules` | `static ModuleManager` | 当前模块管理器 | 调试/高级扩展用 |
| `ResetStatics()` | `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)] private static` | Enter Play Mode 前重置静态 | 清空 `isStarted`/`IsInitialized`/`context`/`services`/`modules`，适配关闭 Domain Reload |
| `AutoBootstrap()` | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] private static` | 场景加载前自动建入口 | `LoadOrDefault()` → 若 `AutoCreateGameEntry` 则 `GameEntry.Ensure(settings)` |
| `Initialize(entry, settings)` | `static void` | 初始化框架 | 见 3.2/3.3；防重复、校验 entry、补默认 settings、配置日志与 Application、建容器与上下文、注册默认与扩展模块、初始化、建诊断面板、失败清理并抛 `FrameException` |
| `Start()` | `static void` | 启动 Start 阶段 | 未初始化或已 started 则返回；置 `isStarted` 并 `modules.StartAll()` |
| `Update/FixedUpdate/LateUpdate(...)` | `static void` | 每帧转发 | 仅在 `IsInitialized` 时转发给 `ModuleManager` 对应方法 |
| `OnApplicationPause/Focus/Quit(...)` | `static void` | 应用事件转发 | 仅初始化后调用 `PauseAll/FocusAll/ApplicationQuitAll` |
| `Shutdown()` | `static void` | 关闭框架 | 未初始化返回；`modules.ShutdownAll()`（倒序）→ `services.Clear()` → 重置静态 → 写 shutdown 日志 |
| `Resolve<TService>()` | `static TService where TService:class` | 强制解析 | 未初始化抛 `FrameException`，否则转发 `services.Resolve` |
| `TryResolve<TService>(out)` | `static bool where TService:class` | 尝试解析 | 未初始化返回 false 并置 null |
| `ApplyApplicationSettings(settings)` | `private static` | 应用 Unity 级设置 | `runInBackground`；`TargetFrameRate != 0` 时设 `targetFrameRate` |
| `RegisterDefaultModules(settings)` | `private static` | 注册内置模块 | 按开关 `modules.Add(new XxxService())`；Asset 仅在 `Resources` 后端时注册 `ResourcesAssetService` |
| `RegisterInstalledModules(settings)` | `private static` | 注册外部模块 | 遍历 AppDomain 程序集 → `GetLoadableTypes` → 筛选非抽象/非接口/实现 `IFrameModuleInstaller` → 无参构造 → `Install(modules, settings)`；单个 installer 异常仅写日志不中断 |
| `CreateRuntimeDiagnosticsOverlay(entry, settings)` | `private static` | 建诊断面板 | entry/settings 为空或未启用则返回；否则 `RuntimeDiagnosticsOverlay.Ensure(...)` |
| `GetLoadableTypes(assembly)` | `private static Type[]` | 安全读取程序集类型 | 动态程序集返回空；`ReflectionTypeLoadException` 返回可加载的 `Types`；其它异常写日志返回空 |
| `CleanupFailedInitialization()` | `private static` | 失败清理 | `ShutdownAll` + `Clear` + 重置静态 |

> **设计意图**：`RegisterInstalledModules` 用反射扫描所有程序集，使得「装了某集成包就自动接入对应模块」成为可能，集成层无需修改 Core 代码。代价是启动时一次性反射扫描全部程序集类型；`GetLoadableTypes` 的容错保证某个第三方程序集类型加载失败不会拖垮整个框架启动。

### 4.4 `GameEntry.cs`

`[DefaultExecutionOrder(-10000)]` 确保它的 `Update` 早于绝大多数业务脚本——这样模块的每帧逻辑先于业务执行。继承 `MonoSingleton<GameEntry>`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `settings`（`[SerializeField]`） | 入口使用的配置 | 手动场景入口在 Inspector 指定 |
| `initializeOnAwake`（`[SerializeField]`，默认 true） | 是否在 Awake 自动初始化 | 想手动控制初始化时机时设 false |
| `Settings` | 暴露 settings | — |
| `UseDontDestroyOnLoad`（override） | 是否跨场景 | `settings != null && settings.UseDontDestroyOnLoad` |
| `Ensure(frameSettings)`（static） | 确保场景存在入口 | `Instance` 存在直接返回；否则 `FindExistingInstance` 补 settings；都没有则**先 `SetActive(false)` → `AddComponent` → `UseSettings` → `SetActive(true)`** |
| `UseSettings(frameSettings)`（internal） | 设置配置 | 仅当 `settings == null` 才赋值，避免覆盖手动指定 |
| `OnSingletonAwake()`（override） | Awake 钩子 | settings 兜底 `LoadOrDefault()`；`initializeOnAwake` 为 true 时 `Framework.Initialize(this, settings)` |
| `Start/Update/FixedUpdate/LateUpdate()` | 转发 | 分别调用 `Framework.Start/Update/FixedUpdate/LateUpdate`，传入对应 `Time.*` |
| `OnApplicationPause/Focus(bool)` | 转发 | 调用 `Framework.OnApplicationPause/Focus` |
| `OnSingletonApplicationQuit()`（override） | 退出流程 | 先 `Framework.OnApplicationQuit()` 通知模块，再 `Framework.Shutdown()` 倒序释放 |
| `OnSingletonDestroyed()`（override） | 非退出销毁保护 | 若非应用退出且 `Framework.IsInitialized`，则 `Framework.Shutdown()` |

> **踩坑点**：`Ensure` 里「先 inactive 再 AddComponent，最后 active」是为了**避免 `Awake` 在 settings 赋值之前触发**——如果直接在 active 对象上 `AddComponent<GameEntry>()`，Unity 会立刻调用 `Awake`，而此时 `settings` 还没通过 `UseSettings` 注入。

### 4.5 `ModuleManager.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Modules` | `IReadOnlyList<IFrameModule>` | 返回内部 list 只读视图，供调试 |
| `Add(module)` | 添加模块 | 空值抛 `ArgumentNullException`；**用具体类型去重**（同一实现类型重复注册抛 `FrameException`）；加入 list 与 `moduleByType` 字典；`modules.Sort(CompareModulePriority)` |
| `TryGet<TModule>(out)` | 按具体类型取模块 | `typeof(TModule)` 查字典并 `as` 转换 |
| `Get<TModule>()` | 按具体类型取模块 | 失败抛 `FrameException` |
| `InitializeAll(context)` | 初始化全部 | 按已排序顺序 `Initialize(context)`，每个成功后 `FrameLog.Debug("Initialized module: ...")` |
| `StartAll()` | 正序 Start | Unity Start 阶段 |
| `UpdateAll/FixedUpdateAll/LateUpdateAll(...)` | 正序每帧调度 | 直接 for 循环转发 |
| `PauseAll/FocusAll/ApplicationQuitAll(...)` | 正序转发应用事件 | — |
| `ShutdownAll()` | 关闭全部 | **从尾部倒序** `Shutdown()`，再清空 list 与字典 |
| `CompareModulePriority(a, b)`（static） | 排序比较器 | `a.Priority.CompareTo(b.Priority)` |

> **设计意图**：倒序 Shutdown 保证「后初始化（依赖别人）的模块先关闭」，减少关闭期间访问已释放服务的风险。每次 `Add` 都重新 `Sort`，因此插入顺序无关紧要——只看 Priority。

### 4.6 `ServiceRegistry.cs`

轻量服务容器，只按类型注册/解析，**不做构造函数注入、作用域管理或线程安全**。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Register<TService>(service)` | 注册 | 空值抛异常；`services[typeof(TService)] = service` 覆盖保存 |
| `TryResolve<TService>(out)` | 尝试解析 | 查字典 + `as TService` |
| `Resolve<TService>()` | 强制解析 | 失败抛 `FrameException("Service is not registered: ...")` |
| `Unregister<TService>()` | 移除注册 | 按类型 key 删除 |
| `Clear()` | 清空并释放 | 遍历 values，对实现 `IDisposable` 的服务 `Dispose()`；用 `disposed` 列表 + `ReferenceEquals` **去重**，避免同一实例以「接口 + 实现」两个 key 注册时被 Dispose 两次；最后清空字典 |

> **踩坑点**：模块通常会 `Register<IXxxService>(this)` **和** `Register(this)`（注册自身具体类型）两次——这是同一个对象的两个 key。`Clear()` 的去重逻辑就是为这种「一对象多 key」场景设计的。

### 4.7 `GameModuleBase.cs`

模块推荐基类。大多数模块只需 override `OnInitialize()`，必要时 override `OnShutdown()` 和 Update 系列。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Name`（virtual） | 模块名 | 默认 `GetType().Name` |
| `Priority`（virtual） | 优先级 | 默认 0，内置模块 override |
| `IsInitialized` | 初始化标记 | `Initialize` 成功置 true，`Shutdown` 后置 false |
| `Context`（protected） | 当前上下文 | 初始化时保存，关闭后清空 |
| `Initialize(frameContext)` | 模板初始化 | 防重复；保存 context；`try { OnInitialize(); IsInitialized = true; } catch { try OnShutdown finally 清空 context/IsInitialized; throw; }` |
| `Start/Update/FixedUpdate/LateUpdate(...)`（virtual 空） | 默认空生命周期 | 需要时 override |
| `OnApplicationPause/Focus/Quit(...)`（virtual 空） | 默认空应用事件 | 需要时 override |
| `Shutdown()` | 模板关闭 | 未初始化直接返回；`OnShutdown()`；清 `IsInitialized` 与 context |
| `OnInitialize()`（protected virtual 空） | 初始化钩子 | 注册服务、建资源、订阅事件 |
| `OnShutdown()`（protected virtual 空） | 关闭钩子 | 反注册、释放资源 |

### 4.8 日志体系：`FrameLog` / `FrameLogEntry` / `FrameLogLevel`

**`FrameLogLevel`**（枚举，按数值从低到高）：`Trace=0`、`Debug=1`、`Info=2`、`Warning=3`、`Error=4`、`Off=5`。`Off` 用作 `MinimumLogLevel` 时会让 `Write()` 直接返回（关闭所有日志）。

**`FrameLogEntry`**（一条结构化日志）：构造时记录 `Level`、`Message`（原始）、`FormattedMessage`（带 `[Frame]` 前缀）、`Exception`（可空）、`UtcTicks`（`DateTime.UtcNow.Ticks`）。`UtcTime` getter 用 `new DateTime(UtcTicks, DateTimeKind.Utc)` 转换。所有属性 `private set`。

**`FrameLog`**（静态日志入口）：

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `DefaultMaxBufferedEntries`（const） | 256 | 默认环形缓冲容量 |
| `EntryWritten`（event） | 日志写入事件 | 在日志入缓冲后触发；`DiagnosticsService` 与 `FileLogSink` 订阅它 |
| `BufferedEntries` | 最近日志只读视图 | 返回内部 `List` 引用，**非线程安全** |
| `MaxBufferedEntries` | 缓冲容量 | setter `Mathf.Max(0, value)` 后 `TrimBuffer()`；设 0 关闭缓冲但事件仍触发 |
| `Configure(settings)` | 应用日志配置 | settings 为空恢复默认（启用 + Info）；否则读 `EnableLogs`/`MinimumLogLevel` |
| `ClearBufferedEntries()` | 清空缓冲 | `bufferedEntries.Clear()` |
| `Trace/Debug/Info/Warning/Error(message)` | 等级快捷入口 | 全部转发 `Write(level, message)` |
| `Exception(exception)` | 写异常日志 | 检查开关与 `minimumLevel > Error`；构造 `FrameLogEntry(Error,…,exception)` 并 `Publish`；随后 `Debug.LogException`（异常非空）或 `Debug.LogError` |
| `Write(level, message)` | 通用写入 | 过滤 `!enabled \|\| level < minimumLevel \|\| minimumLevel==Off`；拼 `[Frame] message`；`Publish` 后按等级调用 Unity `LogError/LogWarning/Log` |
| `Publish(entry)`（private） | 内部发布 | 空值保护；按容量入缓冲并 `TrimBuffer`；取 `EntryWritten` 委托快照后触发；**捕获订阅者异常**写 `Debug.LogException` |
| `TrimBuffer()`（private） | 裁剪缓冲 | `<=0` 清空；否则 `RemoveRange(0, overflow)` 删最旧 |

> **流转**：业务/框架调用 `FrameLog.Info(...)` → `Write` 过滤 → `Publish` 入缓冲 + 触发 `EntryWritten` → `DiagnosticsService` 统计计数并转发 `LogReceived`，`FileLogSink` 写文件 → `Write` 再把消息送进 Unity Console。**一条日志同时进入：内存缓冲、订阅者、Unity Console**。

### 4.9 单例基类：`Singleton<T>` / `MonoSingleton<T>`

**`Singleton<T>`**（纯 C# 懒加载单例）：`Instance` 用双重检查锁 + `Activator.CreateInstance(typeof(T), true)`（`true` 允许调用私有/受保护构造）创建，创建后调 `OnSingletonInitialize()`。`HasInstance` 判断是否已创建。`ReleaseInstance()` 在锁内取出并置空，锁外调 `OnSingletonRelease()`。子类构造函数必须能被反射创建。

**`MonoSingleton<T>`**（MonoBehaviour 单例）：

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Instance` | 当前实例 | **不自动创建**，避免隐藏副作用 |
| `HasInstance` | 是否已有实例 | — |
| `IsApplicationQuitting`（protected） | 是否正在退出 | `OnApplicationQuit` 设置静态标记 |
| `UseDontDestroyOnLoad`（protected virtual） | 是否跨场景 | 默认 false |
| `GetOrCreate()`（static） | 获取或创建 | 静态实例 → `FindExistingInstance` → 新建 GameObject + AddComponent |
| `FindExistingInstance()`（static protected） | 查场景实例 | Unity 2023+ `FindAnyObjectByType(Include)`，旧版 `FindObjectOfType<T>(true)`，均含 inactive |
| `Awake()`（virtual） | 注册单例 | 已有其它实例 → `OnDuplicateInstance` + `Destroy(gameObject)`；否则设 instance、`OnSingletonAwake()`、必要时 `DontDestroyOnLoad` |
| `OnApplicationQuit()`（virtual） | 记录退出 | 置 `isApplicationQuitting = true` 后 `OnSingletonApplicationQuit()` |
| `OnDestroy()`（virtual） | 清理引用 | **仅当前对象是 instance 时**才 `OnSingletonDestroyed()` 并置空（重复实例被销毁不会误清真正实例） |
| `OnSingletonAwake/ApplicationQuit/Destroyed()`、`OnDuplicateInstance(current)`（protected virtual 空） | 钩子 | 子类 override，不要直接 override `Awake` 等 |

---

## 5. Diagnostics 模块

Diagnostics（诊断）模块负责把框架运行时的健康状况集中暴露出来：它统计 FPS、托管/已分配内存、缓冲日志数量以及 Warning/Error/Exception 计数；提供运行时快照 `DiagnosticsSnapshot` 供 UI 或上报系统读取；可以把 `FrameLog` 写入的日志转发出去（`LogReceived` 事件）或落地到磁盘文件（`FileLogSink`，带自动滚动备份）；并提供一个基于 IMGUI 的运行时叠加面板 `RuntimeDiagnosticsOverlay`，把各个子系统（HTTP、Socket、Lifecycle、Timer、Scene、Asset、Pool、日志）的状态实时画在屏幕上。该模块本身不存储日志，而是依赖 `Frame.Core.FrameLog` 的静态环形缓冲与 `EntryWritten` 事件作为唯一日志数据源。它的 `Priority` 为 `-1000`，是框架中优先级最高（数值最小、最先初始化、最先 `Update`）的模块，确保诊断能力在其它模块之前就绪。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IDiagnosticsService` | 诊断服务对外接口 | 暴露 `LogReceived` 事件、`Logs` 只读列表、`CaptureSnapshot`、`WriteLogsToFile`、`ClearLogs` |
| `DiagnosticsService` | 诊断服务实现，`GameModuleBase` 子类 | `Priority = -1000`；统计计数、采样 FPS、转发日志、管理文件日志 Sink |
| `DiagnosticsSnapshot` | 一次性运行时指标快照（`[Serializable]` 普通数据类） | 11 个公共字段，全部为值类型，便于序列化/上报 |
| `FileLogSink` | 把 `FrameLog` 日志追加写入文件的 `IDisposable` Sink | 自带 `lock` 线程安全、按 `MaxBytes` 滚动到 `.bak` 备份、UTF-8 编码 |
| `RuntimeDiagnosticsOverlay` | 屏幕上的 IMGUI 诊断叠加面板（`MonoBehaviour`） | 按键切换显隐、定时刷新快照、聚合展示 8 个子系统状态 |

### `IDiagnosticsService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action<FrameLogEntry> LogReceived` | 每写入一条日志触发一次，携带 `FrameLogEntry` | 由实现类在 `FrameLog.EntryWritten` 回调中转发 |
| `IReadOnlyList<FrameLogEntry> Logs { get; }` | 返回当前缓冲的日志列表 | 实现直接返回 `FrameLog.BufferedEntries`，是只读视图 |
| `DiagnosticsSnapshot CaptureSnapshot()` | 抓取当前运行时指标快照 | 每次调用都 new 一个新对象 |
| `IDisposable WriteLogsToFile(string filePath, long maxBytes = 1048576)` | 开始把日志写入指定文件，返回停止写入的句柄 | 默认 `maxBytes = 1048576`（即 1 MiB） |
| `void ClearLogs()` | 清空缓冲日志并把计数归零 | 实现会调用 `FrameLog.ClearBufferedEntries()` |

### `DiagnosticsService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private int warningCount` | Warning 级别日志累计计数 | 由 `Count` 维护，`ClearLogs`/`OnShutdown` 归零 |
| `private int errorCount` | Error 及以上级别日志累计计数 | `entry.Level >= FrameLogLevel.Error` 时自增 |
| `private int exceptionCount` | 带异常对象的日志累计计数 | `entry.Exception != null` 时自增 |
| `private float sampleElapsed` | FPS 采样窗口已累计的非缩放时间 | 在 `Update` 中累加 `unscaledDeltaTime` |
| `private int sampleFrames` | FPS 采样窗口已累计的帧数 | 在 `Update` 中自增 |
| `private float averageFps` | 最近一次采样窗口算出的平均 FPS | 写入快照的 `AverageFps` |
| `private readonly List<IDisposable> logSinks` | 已创建的文件日志 Sink 集合 | `new List<IDisposable>()`；`OnShutdown` 中统一释放 |
| `event Action<FrameLogEntry> LogReceived` | 实现接口的日志转发事件 | 在 `OnLogEntryWritten` 中以异常保护方式触发 |
| `override int Priority { get; }` | 模块优先级 | 固定返回 `-1000`（框架内最高优先级） |
| `IReadOnlyList<FrameLogEntry> Logs { get; }` | 缓冲日志列表 | 直接返回 `FrameLog.BufferedEntries` |
| `protected override void OnInitialize()` | 模块初始化 | 调用 `RecalculateLogCounts()` 回填计数 → 订阅 `FrameLog.EntryWritten += OnLogEntryWritten` → 把自身分别按 `IDiagnosticsService` 与具体类型注册到 `Context.Services` |
| `public override void Update(float deltaTime, float unscaledDeltaTime)` | 每帧采样 FPS | `sampleElapsed += unscaledDeltaTime; sampleFrames++;` 当 `sampleElapsed >= 0.5f` 时 `averageFps = sampleFrames / sampleElapsed` 并重置采样窗口（即每 0.5 秒刷新一次平均 FPS） |
| `DiagnosticsSnapshot CaptureSnapshot()` | 构造快照 | 取 `Time.frameCount`/`Time.realtimeSinceStartup`/`Time.unscaledTime`/`Time.deltaTime`、`averageFps`、`GC.GetTotalMemory(false)`、`Profiler.GetTotalAllocatedMemoryLong()`、`FrameLog.BufferedEntries.Count`、三个计数 |
| `IDisposable WriteLogsToFile(string filePath, long maxBytes = 1048576)` | 创建并登记文件 Sink | `new FileLogSink(filePath, maxBytes)` 加入 `logSinks`；返回的 `DisposableAction` 会 `sink.Dispose()` 并从列表移除（`DisposableAction` 来自 `Frame.Utilities`） |
| `void ClearLogs()` | 清日志+清计数 | `FrameLog.ClearBufferedEntries()`，然后 `warningCount/errorCount/exceptionCount = 0` |
| `protected override void OnShutdown()` | 模块关闭清理 | 退订 `FrameLog.EntryWritten -= OnLogEntryWritten`；调用 `DisposeLogSinks()`；把三个计数、`sampleElapsed`、`sampleFrames`、`averageFps` 全部归零 |
| `private void DisposeLogSinks()` | 释放所有文件 Sink | 倒序遍历 `logSinks`，逐个 `Dispose()`，捕获异常并 `UnityEngine.Debug.LogException(exception)`；最后 `logSinks.Clear()` |
| `private void OnLogEntryWritten(FrameLogEntry entry)` | `FrameLog.EntryWritten` 的回调 | 先 `Count(entry)` 统计，再把 `LogReceived` 拷贝到局部变量 `handler` 后调用；调用包在 `try/catch` 中，异常用 `UnityEngine.Debug.LogException(exception)` 输出（避免再次进入 FrameLog 造成递归） |
| `private void RecalculateLogCounts()` | 根据现有缓冲重算计数 | 先把三个计数清零，再遍历 `FrameLog.BufferedEntries` 逐条 `Count`；用于初始化时回填历史日志计数 |
| `private void Count(FrameLogEntry entry)` | 单条日志计数逻辑 | `entry == null` 直接返回；`Level == Warning` → `warningCount++`；否则 `Level >= Error` → `errorCount++`；`Exception != null` → `exceptionCount++`（注意 Warning 与 Error 是 `else if` 互斥，但 Exception 计数独立判断） |

### `DiagnosticsSnapshot.cs`

> 标注 `[Serializable]` 的 `sealed` 数据类，全部为公共字段（无属性、无方法）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public int FrameCount` | 帧序号 | 来自 `Time.frameCount` |
| `public float RealtimeSinceStartup` | 自启动起的真实时间（秒） | 来自 `Time.realtimeSinceStartup` |
| `public float UnscaledTime` | 非缩放时间（秒） | 来自 `Time.unscaledTime` |
| `public float DeltaTime` | 当前帧间隔（秒） | 来自 `Time.deltaTime` |
| `public float AverageFps` | 平均 FPS | 来自 `DiagnosticsService.averageFps`（0.5s 窗口） |
| `public long ManagedMemoryBytes` | 托管堆占用字节 | 来自 `GC.GetTotalMemory(false)` |
| `public long TotalAllocatedMemoryBytes` | 已分配内存字节 | 来自 `Profiler.GetTotalAllocatedMemoryLong()` |
| `public int BufferedLogCount` | 当前缓冲日志条数 | 来自 `FrameLog.BufferedEntries.Count` |
| `public int WarningCount` | Warning 计数 | 累计值快照 |
| `public int ErrorCount` | Error 及以上计数 | 累计值快照 |
| `public int ExceptionCount` | 异常计数 | 累计值快照 |

### `FileLogSink.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private const long DefaultMaxBytes = 1024 * 1024` | 默认最大文件字节数 | 等于 1 MiB（1048576） |
| `private readonly object syncRoot` | 写文件的互斥锁 | `new object()`，保证多线程写入安全 |
| `private bool disposed` | 是否已释放 | 双重检查：进 `lock` 前后各判一次 |
| `FileLogSink(string filePath, long maxBytes = DefaultMaxBytes)` | 构造函数 | `filePath` 为空白时抛 `ArgumentException("Log file path is required.", nameof(filePath))`；`FilePath = Path.GetFullPath(filePath)`；`MaxBytes = Math.Max(1, maxBytes)`（下限 1 字节）；若目录非空则 `Directory.CreateDirectory(directory)`；最后订阅 `FrameLog.EntryWritten += OnEntryWritten` |
| `string FilePath { get; private set; }` | 日志文件绝对路径 | 构造时确定 |
| `long MaxBytes { get; private set; }` | 触发滚动的字节阈值 | 至少为 1 |
| `string BackupFilePath { get; }` | 备份文件路径 | 返回 `FilePath + ".bak"` |
| `void Dispose()` | 停止写入 | 已 `disposed` 则直接返回；否则置 `disposed = true` 并退订 `FrameLog.EntryWritten -= OnEntryWritten` |
| `private void OnEntryWritten(FrameLogEntry entry)` | 日志写入回调 | `disposed` 或 `entry == null` 直接返回；`Format(entry)` 后进入 `lock(syncRoot)`，再次检查 `disposed`，调用 `RotateIfNeeded(Encoding.UTF8.GetByteCount(line))`，然后 `File.AppendAllText(FilePath, line, Encoding.UTF8)`；整体 `try/catch`，异常 `Debug.LogException(exception)` |
| `private void RotateIfNeeded(int pendingBytes)` | 按需滚动文件 | 文件不存在直接返回；若 `fileInfo.Length + pendingBytes <= MaxBytes` 不滚动；否则先删除已有 `.bak`，再 `File.Move(FilePath, BackupFilePath)`（只保留一份备份，旧文件变为 `.bak`，原路径将由后续 `AppendAllText` 重新创建） |
| `private static string Format(FrameLogEntry entry)` | 把日志格式化为一行 | 优先用 `entry.FormattedMessage`，为空则用 `entry.Message`；格式串 `"{0:o} [{1}] {2}{3}{4}"`，用 `CultureInfo.InvariantCulture`、`entry.UtcTime`（ISO 8601 round-trip "o" 格式）、`entry.Level`、`Sanitize(message)`、异常文本、`Environment.NewLine` |
| `private static string FormatException(Exception exception)` | 格式化异常文本 | `null` 返回空串；否则返回 `" | {类型全名}: {Sanitize(异常消息)}"` |
| `private static string Sanitize(string value)` | 转义换行符 | 空串返回空串；否则把 `\r` 替换为 `\\r`、`\n` 替换为 `\\n`，保证一条日志恒为单行 |

### `RuntimeDiagnosticsOverlay.cs`

> 标注 `[DisallowMultipleComponent]` 与 `[AddComponentMenu("Frame/Diagnostics/Runtime Diagnostics Overlay")]` 的 `sealed MonoBehaviour`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `[SerializeField] private bool visible` | 当前是否显示面板 | 通过 `Visible` 属性与 `toggleKey` 切换 |
| `[SerializeField] private KeyCode toggleKey = KeyCode.BackQuote` | 切换显隐的按键 | 默认反引号键 `` ` `` |
| `[SerializeField] private int maxLogLines = 12` | 日志区最多显示行数 | 默认 12 |
| `[SerializeField] private float width = 520f` | 面板宽度 | 默认 520，绘制时被 `Mathf.Clamp` 到 `[280, Screen.width-20]` |
| `[SerializeField] private float refreshInterval = 0.25f` | 快照刷新间隔（秒） | 默认 0.25；刷新时下限 `Mathf.Max(0.05f, refreshInterval)` |
| `private IDiagnosticsService diagnostics` 等 8 个服务字段 | 缓存解析到的子系统服务 | `diagnostics/lifecycle/http/sockets/assets/pools/scenes/timers`，全部通过 `Framework.TryResolve` 填充 |
| `private Vector2 scroll` | 滚动视图位置 | `OnGUI` 中 `BeginScrollView` 使用 |
| `private float nextRefreshTime` | 下次允许刷新的非缩放时间点 | 节流刷新用 |
| `private DiagnosticsSnapshot snapshot` | 当前快照 | 可能为 `null`（服务不可用时） |
| `private List<PoolStats> poolStats` | 对象池统计列表 | `new List<PoolStats>()`，来自 `pools.GetAllGameObjectPoolStats()` |
| `private List<AssetStats> assetStats` | 资源统计列表 | `new List<AssetStats>()`，来自 `assets.GetLoadedAssetStats()` |
| `bool Visible { get; set; }` | 显隐属性 | 读写 `visible` 字段 |
| `KeyCode ToggleKey { get; set; }` | 切换键属性 | 读写 `toggleKey` 字段 |
| `static RuntimeDiagnosticsOverlay Ensure(Transform parent, bool visibleAtStart, KeyCode toggleKey)` | 确保父节点下存在唯一 Overlay 实例 | 先 `parent.GetComponentInChildren<RuntimeDiagnosticsOverlay>(true)` 查找（`parent==null` 时返回 null）；找到则 `Configure` 后返回；否则 `new GameObject("RuntimeDiagnosticsOverlay", typeof(RuntimeDiagnosticsOverlay))`，有父则 `SetParent(parent, false)`，再 `Configure` 返回 |
| `void Configure(bool visibleAtStart, KeyCode key)` | 配置并立即刷新 | 设置 `visible`、`toggleKey`，调用 `RefreshServices()` 与 `RefreshSnapshot(true)` |
| `private void Update()` | 每帧检测切换键并按需刷新 | `toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey)` 时翻转 `visible`；`visible` 时 `RefreshSnapshot(false)`（受节流） |
| `private void OnEnable()` | 启用时刷新 | `RefreshServices()` + `RefreshSnapshot(true)` |
| `private void OnGUI()` | 绘制面板 | `!visible` 直接返回；`panelWidth = Mathf.Clamp(width, 280f, Mathf.Max(280f, Screen.width-20f))`；在窗口区域内依次调用 `DrawSnapshot/DrawLifecycle/DrawHttp/DrawSockets/DrawTimers/DrawScenes/DrawAssets/DrawPools/DrawLogs`（注意 `OnGUI` 内调用顺序与方法定义顺序不同） |
| `private void DrawSnapshot()` | 画 Runtime 区块 | `snapshot==null` 时显示 "Diagnostics service is not available."；否则显示 Frame、FPS（`"0.0"`）、Managed/Allocated Memory（经 `FormatBytes`）、Logs/Warnings/Errors/Exceptions 计数 |
| `private void DrawHttp()` | 画 HTTP 区块 | `http==null` 显示不可用；否则显示 `ActiveRequestCount/StartedRequestCount/CompletedRequestCount/FailedRequestCount` |
| `private void DrawSockets()` | 画 Sockets 区块 | `sockets==null` 显示不可用；否则显示 `Clients/ActiveConnectionCount`，并逐个打印 `Id/Transport/State/SentMessages/ReceivedMessages/DroppedMessages` |
| `private void DrawLifecycle()` | 画 Lifecycle 区块 | `lifecycle==null` 显示不可用；否则显示 `IsPaused/HasFocus/IsQuitting` |
| `private void DrawPools()` | 画 GameObject Pools 区块 | `pools==null` 显示不可用；`poolStats.Count==0` 显示 "No pools."；否则逐项打印 `Key/CountActive/CountInactive/CreatedCount/DestroyedCount`（跳过 null 项） |
| `private void DrawTimers()` | 画 Timers 区块 | `timers==null` 显示不可用；否则显示 `ActiveTimerCount`、`ScaledTimerCount`、`UnscaledTimerCount`，`IsPaused` 为真时追加 " paused" |
| `private void DrawScenes()` | 画 Scenes 区块 | `scenes==null` 显示不可用；显示活动场景名（无效时 "none"）；`CurrentOperation==null` 时只显示 `IsLoading`，否则附带场景名、`NormalizedProgress`（`"0%"`）、`IsReadyToActivate` |
| `private void DrawAssets()` | 画 Assets 区块 | `assets==null` 显示不可用；`assetStats.Count==0` 显示 "No loaded assets."；否则逐项打印 `Path/ReferenceCount/TypeName`（跳过 null） |
| `private void DrawLogs()` | 画 Recent Logs 区块 | `diagnostics==null` 显示不可用；取 `diagnostics.Logs`，起点 `Mathf.Max(0, logs.Count - Mathf.Max(1, maxLogLines))`，从该起点打印到末尾，每行 `[Level] Message`（跳过 null 项） |
| `private void RefreshServices()` | 重新解析全部服务 | 连续 `Framework.TryResolve(out ...)` 8 次 |
| `private void RefreshSnapshot(bool force)` | 刷新快照与统计列表（带节流） | 非强制且 `Time.unscaledTime < nextRefreshTime` 直接返回；否则 `nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval)`，`RefreshServices()`，再取 `snapshot`/`assetStats`/`poolStats`（服务为 null 时用空列表/null） |
| `private static string FormatBytes(long bytes)` | 字节转 MB 字符串 | `const float mb = 1024f*1024f`，返回 `(bytes/mb).ToString("0.0") + " MB"` |

### 流转逻辑

1. 日志写入链路：业务代码或框架调用 `FrameLog.Info/Warning/Error/Exception(...)` → `FrameLog` 把条目写入其静态环形缓冲并触发静态事件 `EntryWritten(FrameLogEntry)`。`DiagnosticsService.OnInitialize` 已订阅该事件，于是 `OnLogEntryWritten` 被调用：先 `Count(entry)` 更新 Warning/Error/Exception 计数，再以异常保护方式触发本服务的 `LogReceived` 事件，订阅者（如上报系统、自定义 UI）即可收到。
2. 文件落地链路：调用 `WriteLogsToFile` 时另外创建一个 `FileLogSink`，它**独立**订阅同一个 `FrameLog.EntryWritten`。因此一条日志会同时触达 `DiagnosticsService.OnLogEntryWritten` 与每个 `FileLogSink.OnEntryWritten`。`FileLogSink` 在 `lock(syncRoot)` 内先 `RotateIfNeeded`（若写入后会超过 `MaxBytes`，把现有文件移动为 `.bak`），再 `File.AppendAllText` 追加单行 UTF-8 文本。两个回调都不会回写 `FrameLog`（异常一律走 `UnityEngine.Debug`），避免事件递归。
3. FPS 采样：`DiagnosticsService.Update` 每帧累加 `unscaledDeltaTime` 与帧数，凑满 0.5 秒窗口就算一次 `averageFps` 并清零窗口；该值仅在 `CaptureSnapshot` 时被读取写入快照。
4. Overlay 数据流：`RuntimeDiagnosticsOverlay` 不直接监听日志，而是定时（默认每 0.25s，受 `nextRefreshTime` 节流）调用 `RefreshSnapshot`：`Framework.TryResolve` 重新解析 8 个服务，`diagnostics.CaptureSnapshot()` 拿快照，`assets.GetLoadedAssetStats()`/`pools.GetAllGameObjectPoolStats()` 拿列表；`OnGUI` 把这些缓存数据画出来，Socket 区块直接读取 `ISocketService.Clients` 和各 client metrics，日志区则实时从 `diagnostics.Logs` 取最后 `maxLogLines` 行。任一服务解析不到时，对应区块显示 "... is not available." 而不会抛异常。

### 使用示例

```csharp
using System;
using Frame.Core;
using Frame.Diagnostics;
using UnityEngine;

public sealed class DiagnosticsSample : MonoBehaviour
{
    private IDisposable fileSink;
    private IDiagnosticsService diagnostics;

    private void Start()
    {
        if (!Framework.TryResolve(out diagnostics))
        {
            return;
        }

        // 把日志落地到持久化目录，单文件上限 2 MiB，超出后滚动为 .bak
        fileSink = diagnostics.WriteLogsToFile(
            System.IO.Path.Combine(Application.persistentDataPath, "frame.log"),
            maxBytes: 2 * 1024 * 1024);

        // 订阅日志，用于自定义上报
        diagnostics.LogReceived += OnLog;

        // 在玩家头上挂一个运行时叠加面板，反引号键切换显隐
        RuntimeDiagnosticsOverlay.Ensure(transform, visibleAtStart: false, KeyCode.BackQuote);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            DiagnosticsSnapshot snap = diagnostics.CaptureSnapshot();
            Debug.LogFormat("FPS={0:0.0} 内存={1}MB 错误={2}",
                snap.AverageFps,
                snap.ManagedMemoryBytes / 1048576f,
                snap.ErrorCount);
        }
    }

    private void OnLog(FrameLogEntry entry)
    {
        if (entry.Level >= FrameLogLevel.Error)
        {
            // 例如：把错误推送到崩溃上报后台
        }
    }

    private void OnDestroy()
    {
        if (diagnostics != null)
        {
            diagnostics.LogReceived -= OnLog;
        }

        // Dispose 句柄会停止文件写入并从 logSinks 中移除该 sink
        fileSink?.Dispose();
    }
}
```

### 设计意图与踩坑点

- **单一日志源**：所有日志都来自 `FrameLog`，`DiagnosticsService.Logs` 只是 `FrameLog.BufferedEntries` 的只读视图，避免双份缓冲与不一致。`ClearLogs` 会真正清空 `FrameLog` 的缓冲（影响所有读取者），不是只清自己的统计。
- **计数互斥但异常独立**：`Count` 中 Warning 与 Error 是 `else if`，同一条日志只会进 Warning 或 Error 其中之一；而 `exceptionCount` 是独立判断，一条带异常的 Error 会同时让 `errorCount` 和 `exceptionCount` 自增。
- **回调防递归与防崩溃**：`OnLogEntryWritten`、`FileLogSink.OnEntryWritten`、`DisposeLogSinks` 捕获异常后都走 `UnityEngine.Debug.LogException`，绝不调用 `FrameLog`，防止"日志写入触发日志"的无限递归。
- **`LogReceived` 拷到局部变量**：触发前先 `Action<FrameLogEntry> handler = LogReceived;`，避免多线程/回调中退订导致空引用。
- **FileLogSink 线程安全**：用 `lock(syncRoot)` + `disposed` 双重检查，支持非主线程写日志；但 `File.AppendAllText` 每条都开关文件句柄，高频日志下有 IO 开销，`MaxBytes` 下限被强制为 1。
- **滚动只保留一份备份**：`RotateIfNeeded` 删除旧 `.bak` 后把当前文件改名为 `.bak`，因此最多保留"当前 + 上一份"，更早的历史会丢失。
- **快照是瞬时值**：`CaptureSnapshot` 每次 new 新对象，`AverageFps` 是 0.5s 窗口的结果而非瞬时帧率；`Time.*` 读取必须在主线程。
- **Overlay 用 IMGUI 且自解析服务**：`OnGUI` 有一定开销，面板默认隐藏；它通过 `Framework.TryResolve` 弱依赖各服务，缺失任一服务都能优雅降级显示 "not available"。`Ensure` 配合 `[DisallowMultipleComponent]` 保证同一父节点下不重复创建。
- **优先级最高（-1000）**：诊断最先初始化、最先 `Update`，确保后续模块出问题时诊断已经在监听日志。

---

## 6. Lifecycle 模块

Lifecycle（生命周期）模块把 Unity 应用级的暂停、焦点、退出三类回调（`OnApplicationPause`/`OnApplicationFocus`/`OnApplicationQuit`）收敛成一个可订阅的服务，让业务无需各自挂 `MonoBehaviour` 去监听这些事件。它维护三个布尔状态（`IsPaused`/`HasFocus`/`IsQuitting`）并在状态发生变化时触发对应事件（`PauseChanged`/`FocusChanged`/`Quitting`）。其它模块（典型如 `TimerService`）也会响应同样的 Unity 回调，但 Lifecycle 模块本身只负责状态广播。`Priority` 为 `-950`，仅次于 Diagnostics，保证生命周期状态在大多数业务模块之前就绪。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ILifecycleService` | 生命周期服务接口 | 暴露 `PauseChanged`/`FocusChanged`/`Quitting` 三事件与 `IsPaused`/`HasFocus`/`IsQuitting` 三只读属性 |
| `LifecycleService` | 实现，`GameModuleBase` 子类 | `Priority = -950`；在去抖（状态变化才触发）基础上以异常保护方式广播事件 |

### `ILifecycleService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action<bool> PauseChanged` | 暂停状态变化事件，参数为新的 `paused` 值 | 仅在值变化时触发 |
| `event Action<bool> FocusChanged` | 焦点状态变化事件，参数为新的 `focused` 值 | 仅在值变化时触发 |
| `event Action Quitting` | 应用退出事件 | 仅触发一次 |
| `bool IsPaused { get; }` | 当前是否暂停 | 只读 |
| `bool HasFocus { get; }` | 当前是否有焦点 | 只读 |
| `bool IsQuitting { get; }` | 是否正在退出 | 只读 |

### `LifecycleService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action<bool> PauseChanged` | 暂停变化事件 | `OnShutdown` 中置 null |
| `event Action<bool> FocusChanged` | 焦点变化事件 | `OnShutdown` 中置 null |
| `event Action Quitting` | 退出事件 | `OnShutdown` 中置 null |
| `override int Priority { get; }` | 模块优先级 | 固定返回 `-950` |
| `bool IsPaused { get; private set; }` | 暂停状态 | 自动属性，仅内部可写 |
| `bool HasFocus { get; private set; }` | 焦点状态 | 自动属性，仅内部可写；初值由 `OnInitialize` 设定 |
| `bool IsQuitting { get; private set; }` | 退出状态 | 自动属性，仅内部可写 |
| `protected override void OnInitialize()` | 初始化 | `HasFocus = Application.isFocused;` 取当前焦点作为初值；把自身按 `ILifecycleService` 与具体类型注册到 `Context.Services` |
| `public override void OnApplicationPause(bool paused)` | Unity 暂停回调 | 若 `IsPaused == paused` 直接返回（去抖）；否则更新 `IsPaused = paused`，调用 `Invoke(PauseChanged, paused)` |
| `public override void OnApplicationFocus(bool focused)` | Unity 焦点回调 | 若 `HasFocus == focused` 直接返回；否则更新 `HasFocus = focused`，调用 `Invoke(FocusChanged, focused)` |
| `public override void OnApplicationQuit()` | Unity 退出回调 | 若已 `IsQuitting` 直接返回（只触发一次）；否则置 `IsQuitting = true`；把 `Quitting` 拷到局部 `handler`，为 null 则返回，否则 `try{ handler(); } catch(Exception e){ FrameLog.Exception(e); }` |
| `protected override void OnShutdown()` | 关闭清理 | 把 `PauseChanged`/`FocusChanged`/`Quitting` 三事件全部置 null，断开所有订阅 |
| `private static void Invoke(Action<bool> handler, bool value)` | 带异常保护地触发 `Action<bool>` | `handler == null` 直接返回；否则 `try{ handler(value); } catch(Exception e){ FrameLog.Exception(e); }` |

### 流转逻辑

Unity 引擎在应用被切到后台/恢复、获得/失去焦点、即将退出时分别调用框架驱动器转发的 `OnApplicationPause(bool)`/`OnApplicationFocus(bool)`/`OnApplicationQuit()`（这些在 `GameModuleBase` 中是可重写的虚方法，由框架的 Runner 统一分发到所有模块）。`LifecycleService` 重写这三者：每个回调先做"状态是否真的改变"的去抖判断，只有变化时才更新内部状态并通过 `Invoke`/直接调用广播事件。所有订阅者回调都被包在 `try/catch` 中，异常通过 `FrameLog.Exception` 记录而不会中断后续订阅者或冒泡到引擎。`Quitting` 通过 `IsQuitting` 守卫确保整个生命周期只触发一次。注意 `HasFocus` 在初始化时用 `Application.isFocused` 取真实初值，因此即便启动后没有焦点变化也能反映正确状态。

### 使用示例

```csharp
using Frame.Core;
using Frame.Lifecycle;
using UnityEngine;

public sealed class SaveOnPause : MonoBehaviour
{
    private ILifecycleService lifecycle;

    private void OnEnable()
    {
        if (!Framework.TryResolve(out lifecycle))
        {
            return;
        }

        lifecycle.PauseChanged += OnPauseChanged;
        lifecycle.Quitting += OnQuitting;
    }

    private void OnDisable()
    {
        if (lifecycle != null)
        {
            lifecycle.PauseChanged -= OnPauseChanged;
            lifecycle.Quitting -= OnQuitting;
        }
    }

    private void OnPauseChanged(bool paused)
    {
        if (paused)
        {
            // 切后台：立即落盘，移动端尤其重要（进程可能被系统回收）
            SaveSystem.Flush();
        }
    }

    private void OnQuitting()
    {
        SaveSystem.Flush();
    }
}
```

### 设计意图与踩坑点

- **状态去抖**：三个回调都先比较"新旧是否相同"，避免引擎重复回调导致事件被多次触发；订阅者无需自己做防抖。
- **异常隔离**：所有事件触发都经 `try/catch` + `FrameLog.Exception`，单个订阅者抛异常不会影响其它订阅者，也不会把异常抛回引擎（这点与 `Quitting` 时尤其关键，退出阶段抛异常可能导致存档逻辑被跳过）。
- **`Quitting` 只触发一次**：`IsQuitting` 守卫保证语义稳定；订阅者应把退出处理写成幂等的。
- **`HasFocus` 初值取自 `Application.isFocused`**：编辑器/某些平台下首帧焦点状态不一定为 true，初始化时主动同步可避免首次 `FocusChanged` 误判。
- **`OnShutdown` 清空事件**：模块关闭时把三个事件置 null，防止悬挂订阅造成内存泄漏，但订阅者仍应在自身 `OnDisable/OnDestroy` 主动退订。
- **与 TimerService 的关系**：暂停时 `LifecycleService` 只是广播 `PauseChanged`，真正"暂停计时"是 `TimerService.OnApplicationPause` 自己实现的；两者监听的是同一个引擎回调，互不依赖。

---

## 7. Events 模块

Events（事件总线）模块提供一个进程内的强类型发布/订阅总线 `IEventBus`，让模块之间以"事件类型"为契约解耦通信，而无需互相持有引用。订阅以泛型事件类型 `TEvent` 为键，支持指定 `owner`（便于批量退订）和 `once`（触发一次即自动退订）；订阅返回 `IDisposable`（`EventSubscription`），`Dispose` 即退订。发布时对订阅列表做快照，允许在回调内部安全地订阅/退订。`Priority` 为 `-900`。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IEventBus` | 事件总线接口 | `Subscribe<TEvent>`、`Publish<TEvent>`、`UnsubscribeOwner`、`Clear` |
| `EventBus` | 实现，`GameModuleBase` 子类 | `Priority = -900`；按 `Type` 分桶存订阅，发布走数组快照，`int` 自增 ID |
| `EventBus.Subscription`（私有嵌套类） | 单条订阅记录 | 字段 `Id/Owner/Handler/Once/Active`，`Handler` 存为 `Delegate` |
| `EventSubscription` | 订阅句柄，`IDisposable` | `Dispose` 调用 `EventBus.Unsubscribe(id)`，一次性 |

### `IEventBus.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `IDisposable Subscribe<TEvent>(Action<TEvent> handler, object owner = null, bool once = false)` | 订阅某类型事件，返回退订句柄 | `owner` 默认 null，`once` 默认 false |
| `void Publish<TEvent>(TEvent gameEvent)` | 发布事件 | 同步派发给所有当前活跃订阅者 |
| `void UnsubscribeOwner(object owner)` | 退订某 owner 的所有订阅 | `owner==null` 时无操作 |
| `void Clear()` | 清空所有订阅 | — |

### `EventBus.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<Type, List<Subscription>> subscriptions` | 事件类型到订阅列表的映射 | `new Dictionary<...>()`，每种 `TEvent` 一个桶 |
| `private int nextId = 1` | 订阅 ID 自增源 | 从 1 开始（`TimerHandle.IsValid` 等约定中 id>0 才有效） |
| `override int Priority { get; }` | 模块优先级 | 固定返回 `-900` |
| `protected override void OnInitialize()` | 初始化 | 把自身按 `IEventBus` 与具体类型注册到 `Context.Services` |
| `IDisposable Subscribe<TEvent>(Action<TEvent> handler, object owner = null, bool once = false)` | 订阅 | `handler==null` 抛 `ArgumentNullException("handler")`；按 `typeof(TEvent)` 取/建列表；`id = nextId++`；`list.Add(new Subscription(id, owner, handler, once))`；返回 `new EventSubscription(this, id)` |
| `void Publish<TEvent>(TEvent gameEvent)` | 发布 | 取 `typeof(TEvent)` 的列表，不存在或为空直接返回；`Subscription[] snapshot = list.ToArray()` 做快照后遍历：跳过 `!Active`；`subscription.Handler as Action<TEvent>` 转型，为 null 跳过；`try{ handler(gameEvent); } catch(Exception e){ FrameLog.Exception(e); }`；若 `Once` 则 `Unsubscribe(subscription.Id)` |
| `void Unsubscribe(int id)` | 按 ID 退订（公共方法，供句柄调用） | 遍历所有桶，倒序找到匹配 `Id` 的项，置 `Active = false` 并 `RemoveAt` 后立即 `return`（只移除第一个匹配项） |
| `void UnsubscribeOwner(object owner)` | 按 owner 批量退订 | `owner==null` 直接返回；遍历所有桶，倒序对 `ReferenceEquals(list[i].Owner, owner)` 的项置 `Active=false` 并 `RemoveAt`（移除全部匹配项） |
| `void Clear()` | 清空 | `subscriptions.Clear()`（不逐个置 Active=false） |
| `protected override void OnShutdown()` | 关闭清理 | 调用 `Clear()` |
| `private sealed class Subscription` | 订阅记录嵌套类 | 见下 |
| `Subscription(int id, object owner, Delegate handler, bool once)` | 订阅记录构造 | 赋值 `Id/Owner/Handler/Once`，并 `Active = true` |
| `Subscription.Id`（`public int`） | 订阅唯一 ID | — |
| `Subscription.Owner`（`public object`） | 拥有者，用于批量退订 | 可为 null |
| `Subscription.Handler`（`public Delegate`） | 回调委托 | 存为 `Delegate`，派发时再 `as Action<TEvent>` |
| `Subscription.Once`（`public bool`） | 是否触发一次后退订 | — |
| `Subscription.Active`（`public bool`） | 是否仍活跃 | 退订时先置 false 再移除；发布快照遍历时据此跳过已退订项 |

### `EventSubscription.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private EventBus owner` | 所属总线引用 | `Dispose` 后置 null |
| `private readonly int id` | 对应订阅 ID | 构造时确定 |
| `internal EventSubscription(EventBus owner, int id)` | 构造函数 | 仅程序集内可见，由 `EventBus.Subscribe` 创建 |
| `public void Dispose()` | 退订 | 把 `owner` 拷到局部 `bus`，为 null 直接返回；置 `owner = null`（保证幂等，重复 Dispose 无副作用），再 `bus.Unsubscribe(id)` |

### 流转逻辑

订阅时 `Subscribe<TEvent>` 以 `typeof(TEvent)` 为键找到（或创建）一个 `List<Subscription>`，分配自增 `id`，把 `handler` 以 `Delegate` 形式存入 `Subscription` 并标记 `Active=true`，返回一次性的 `EventSubscription` 句柄。发布时 `Publish<TEvent>` 关键在于先 `list.ToArray()` 拍下快照再遍历——这样订阅者回调内即使调用 `Subscribe`/`Unsubscribe`/`UnsubscribeOwner` 改动原列表，也不会破坏当前正在进行的迭代；遍历中通过 `Active` 标志跳过已在本次发布期间被退订的项（退订是"先置 Active=false 再 RemoveAt"，正是为了让快照里的引用仍能据此跳过）。每个回调用 `try/catch` 包裹，异常走 `FrameLog.Exception`，单个订阅者抛异常不影响其它订阅者。`once` 订阅在其回调执行后立即 `Unsubscribe`。退订路径有三条：句柄 `Dispose`（→`Unsubscribe(id)`，移除单个）、`UnsubscribeOwner(owner)`（移除该 owner 全部）、`Clear`/`OnShutdown`（整桶清空）。`nextId` 单调自增，不复用 ID，避免句柄误退订到后续新订阅。

### 使用示例

```csharp
using System;
using Frame.Core;
using Frame.Events;
using UnityEngine;

// 事件就是普通数据类型
public readonly struct PlayerDied
{
    public readonly int PlayerId;
    public PlayerDied(int playerId) { PlayerId = playerId; }
}

public sealed class ScoreBoard : MonoBehaviour
{
    private IEventBus bus;
    private IDisposable handle;

    private void OnEnable()
    {
        if (!Framework.TryResolve(out bus))
        {
            return;
        }

        // 持续订阅，并用 this 作为 owner 便于批量退订
        handle = bus.Subscribe<PlayerDied>(OnPlayerDied, owner: this);

        // 只关心第一次开局事件，触发一次后自动退订
        bus.Subscribe<GameStarted>(_ => Debug.Log("首局开始"), once: true);
    }

    private void OnPlayerDied(PlayerDied e)
    {
        Debug.Log("玩家死亡: " + e.PlayerId);
    }

    private void OnDisable()
    {
        // 方式一：Dispose 单个句柄
        handle?.Dispose();
        // 方式二：按 owner 一次性退订该对象的所有订阅
        bus?.UnsubscribeOwner(this);
    }

    // 任意位置发布
    public void KillPlayer(int id) => bus.Publish(new PlayerDied(id));
}

public readonly struct GameStarted { }
```

### 设计意图与踩坑点

- **发布期快照（核心设计）**：`Publish` 用 `list.ToArray()` 后遍历，使得"在回调里订阅/退订"安全无异常；代价是每次发布有一次数组分配（高频事件下需留意 GC）。
- **`Active` 标志的意义**：退订时先置 `Active=false` 再从原列表移除——快照里仍持有旧引用，靠 `Active` 才能在本轮发布中正确跳过刚退订的订阅者。
- **类型即契约，无继承约束**：`TEvent` 可以是任意类型（推荐用不可变 `struct`/`readonly struct` 减少分配）；按 `typeof(TEvent)` 精确匹配，不支持基类/接口的多态派发。
- **`Unsubscribe(id)` 只移除第一个匹配**：因 ID 唯一，找到即 `return`；`UnsubscribeOwner` 则移除全部匹配，二者语义不同。
- **`EventSubscription.Dispose` 幂等**：先置 `owner=null` 再调用，重复 Dispose 安全。
- **`Clear`/`OnShutdown` 不置 Active=false**：直接清字典即可，但若此时正处于某次 `Publish` 的快照遍历中，已拍下的快照仍会继续执行（这种交叉场景较罕见，需注意）。
- **同步派发、单线程假设**：没有任何锁，`subscriptions` 与 `nextId` 假定在主线程访问；跨线程发布/订阅不安全。
- **`once` 在回调后退订**：回调执行期间该订阅仍 `Active`，若回调内对同类型再次 `Publish` 会重入（需避免无限递归）。

---

## 8. Time 模块

Time（定时器）模块提供基于帧更新（`Update`）的轻量定时器服务 `ITimerService`：支持一次性延时 `Delay`、周期重复 `Repeat`（可限次或无限）、下一帧执行 `NextFrame`；每个定时器可选择使用缩放时间或非缩放时间（`unscaled`），可绑定 `owner` 以便批量取消。调度返回值类型 `TimerHandle`（`struct`）可查询有效性并取消。服务还统计活跃/缩放/非缩放定时器数量，并在应用暂停时整体停摆。`Priority` 为 `-800`。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ITimerService` | 定时器服务接口 | 计数属性 + `Delay`/`Repeat`/`NextFrame`/`Contains`/`Cancel`/`CancelOwner` |
| `TimerService` | 实现，`GameModuleBase` 子类 | `Priority = -800`；字典存定时器，`Update` 用键快照推进，暂停时停摆 |
| `TimerService.TimerTask`（私有嵌套类） | 单个定时器状态 | 字段 `Remaining/Interval/RepeatCount/CompletedCount/Callback/Unscaled/Owner` |
| `TimerHandle`（`struct`） | 定时器句柄 | `Id`/`IsValid`/`Cancel()`，值类型、`internal` 构造 |

### `ITimerService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `int ActiveTimerCount { get; }` | 活跃定时器总数 | — |
| `int ScaledTimerCount { get; }` | 使用缩放时间的定时器数 | — |
| `int UnscaledTimerCount { get; }` | 使用非缩放时间的定时器数 | — |
| `bool IsPaused { get; }` | 是否暂停 | — |
| `TimerHandle Delay(float seconds, Action callback, bool unscaled = false, object owner = null)` | 延时一次性回调 | 默认缩放时间、无 owner |
| `TimerHandle Repeat(float interval, Action callback, int repeatCount = -1, bool unscaled = false, object owner = null)` | 周期回调 | `repeatCount` 默认 `-1`（无限） |
| `TimerHandle NextFrame(Action callback, object owner = null)` | 下一帧执行一次 | 强制使用非缩放时间 |
| `bool Contains(int id)` | 是否存在该 ID 的定时器 | — |
| `bool Cancel(int id)` | 取消指定 ID | 返回是否确实移除 |
| `void CancelOwner(object owner)` | 取消某 owner 的全部定时器 | `owner==null` 无操作 |

### `TimerService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<int, TimerTask> timers` | ID→定时器任务映射 | `new Dictionary<int, TimerTask>()` |
| `private readonly List<int> completedBuffer` | 本帧完成需移除的 ID 缓冲 | 复用列表，`Update` 开头 `Clear` |
| `private readonly List<int> ownerCancelBuffer` | `CancelOwner` 待移除 ID 缓冲 | 复用列表 |
| `private readonly List<int> updateBuffer` | `Update` 时的键快照缓冲 | 复用列表，避免遍历中改字典 |
| `private int nextId = 1` | 定时器 ID 自增源 | 从 1 开始（`TimerHandle.IsValid` 要求 id>0） |
| `private bool paused` | 是否暂停 | 由 `OnApplicationPause` 设置 |
| `override int Priority { get; }` | 模块优先级 | 固定返回 `-800` |
| `int ActiveTimerCount { get; }` | 活跃数 | 返回 `timers.Count` |
| `int ScaledTimerCount { get; }` | 缩放定时器数 | 返回 `CountTimers(unscaled: false)` |
| `int UnscaledTimerCount { get; }` | 非缩放定时器数 | 返回 `CountTimers(unscaled: true)` |
| `bool IsPaused { get; }` | 暂停状态 | 返回 `paused` |
| `protected override void OnInitialize()` | 初始化 | 把自身按 `ITimerService` 与具体类型注册到 `Context.Services` |
| `TimerHandle Delay(float seconds, Action callback, bool unscaled = false, object owner = null)` | 延时 | `Schedule(seconds, 0f, 0, callback, unscaled, owner)`（interval=0、repeatCount=0 → 一次性） |
| `TimerHandle Repeat(float interval, Action callback, int repeatCount = -1, bool unscaled = false, object owner = null)` | 周期 | `Schedule(interval, interval, repeatCount, callback, unscaled, owner)`（首次也等 `interval` 后触发） |
| `TimerHandle NextFrame(Action callback, object owner = null)` | 下一帧 | `Schedule(0f, 0f, 0, callback, true, owner)`（delay=0、强制 `unscaled=true`） |
| `bool Contains(int id)` | 存在性 | `timers.ContainsKey(id)` |
| `bool Cancel(int id)` | 取消 | `timers.Remove(id)`，返回是否移除成功 |
| `void CancelOwner(object owner)` | 批量取消 | `owner==null` 返回；清 `ownerCancelBuffer`，遍历 `timers` 收集 `ReferenceEquals(pair.Value.Owner, owner)` 的键，再逐个 `timers.Remove`（先收集后删除，避免迭代中改字典） |
| `public override void Update(float deltaTime, float unscaledDeltaTime)` | 每帧推进 | 见下方流转逻辑；`paused` 或 `timers.Count==0` 时直接返回 |
| `public override void OnApplicationPause(bool paused)` | Unity 暂停回调 | `this.paused = paused`（暂停时 `Update` 直接返回，定时器整体停摆） |
| `protected override void OnShutdown()` | 关闭清理 | `timers.Clear()`、清三个缓冲列表、`paused = false`、`nextId = 1` |
| `private TimerHandle Schedule(float delay, float interval, int repeatCount, Action callback, bool unscaled, object owner)` | 统一调度入口 | `callback==null` 抛 `ArgumentNullException("callback")`；`id = nextId++`；构造 `TimerTask{ Remaining=Math.Max(0f,delay), Interval=Math.Max(0f,interval), RepeatCount=repeatCount, Callback=callback, Unscaled=unscaled, Owner=owner }` 加入字典；返回 `new TimerHandle(this, id)` |
| `private int CountTimers(bool unscaled)` | 按缩放属性计数 | 遍历 `timers.Values`，`timer.Unscaled == unscaled` 累加 |
| `private sealed class TimerTask` | 定时器状态嵌套类 | 见下 |
| `TimerTask.Remaining`（`public float`） | 距下次触发的剩余时间 | 每帧减 delta |
| `TimerTask.Interval`（`public float`） | 重复间隔；0 表示一次性 | — |
| `TimerTask.RepeatCount`（`public int`） | 重复次数；<0 表示无限 | — |
| `TimerTask.CompletedCount`（`public int`） | 已完成触发次数 | 用于判断是否到达 `RepeatCount` |
| `TimerTask.Callback`（`public Action`） | 到点回调 | 非空（`Schedule` 已校验） |
| `TimerTask.Unscaled`（`public bool`） | 是否用非缩放时间 | 决定取 `unscaledDeltaTime` 还是 `deltaTime` |
| `TimerTask.Owner`（`public object`） | 拥有者 | 供 `CancelOwner` 用 |

### `TimerHandle.cs`

> 值类型 `struct`，无字段初始化默认即"无效句柄"（`service==null`）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly TimerService service` | 关联服务 | 构造时注入；默认句柄为 null |
| `private readonly int id` | 定时器 ID | 构造时确定 |
| `internal TimerHandle(TimerService service, int id)` | 构造函数 | 仅程序集内可见，由 `Schedule` 创建 |
| `int Id { get; }` | 定时器 ID | 返回 `id` |
| `bool IsValid { get; }` | 句柄是否仍指向活跃定时器 | `service != null && id > 0 && service.Contains(id)`（已完成/已取消的定时器返回 false） |
| `void Cancel()` | 取消该定时器 | `service != null` 时 `service.Cancel(id)`；默认句柄上调用安全无操作 |

### 流转逻辑

调度走统一的 `Schedule`：校验回调非空，分配自增 `id`，把 `delay`/`interval` 经 `Math.Max(0f, ...)` 钳为非负后放入 `TimerTask` 并存进字典，返回 `TimerHandle`。每帧框架调用 `Update(deltaTime, unscaledDeltaTime)`：若 `paused` 或无定时器立即返回；否则先 `completedBuffer.Clear()` 与 `updateBuffer.Clear()`，把当前 `timers.Keys` 全部拷入 `updateBuffer` 形成**键快照**，再遍历快照——这样回调里若 `Schedule`/`Cancel` 改动 `timers` 也不会破坏迭代（遍历前会用 `timers.TryGetValue` 重查，已被取消的 ID 会被跳过）。对每个仍存在的定时器：按 `Unscaled` 选择 `unscaledDeltaTime`/`deltaTime` 扣减 `Remaining`，未到 0 则跳过；到点则 `try{ Callback(); } catch(Exception e){ FrameLog.Exception(e); }`。触发后判断是否继续：当 `Interval > 0` 且（`RepeatCount < 0` 无限 或 `CompletedCount + 1 < RepeatCount`）时，`CompletedCount++` 并 `Remaining += Interval`（累加而非重置，减少长期漂移）；否则把该 ID 记入 `completedBuffer`。遍历结束后统一从字典移除 `completedBuffer` 中的 ID。暂停由 `OnApplicationPause(true)` 把 `paused` 置真，`Update` 据此整体停摆——剩余时间被"冻结"，恢复后从原 `Remaining` 继续，不会在暂停期间累计推进。`NextFrame` 是 delay=0 的非缩放一次性定时器：调度当帧不执行，下一帧 `Update` 中 `Remaining -= delta` 后立即 <=0 触发。

### 使用示例

```csharp
using System;
using Frame.Core;
using Frame.Timing;
using UnityEngine;

public sealed class AbilitySystem : MonoBehaviour
{
    private ITimerService timers;
    private TimerHandle cooldown;

    private void Start()
    {
        if (!Framework.TryResolve(out timers))
        {
            return;
        }

        // 3 秒后执行一次（使用缩放时间，受 Time.timeScale 影响）
        timers.Delay(3f, () => Debug.Log("延时到达"), owner: this);

        // 每 1 秒回血一次，重复 5 次
        timers.Repeat(1f, RegenTick, repeatCount: 5, owner: this);

        // 下一帧再做初始化（避开当前帧的脚本执行顺序问题）
        timers.NextFrame(() => Debug.Log("下一帧"), owner: this);

        // 用非缩放时间做 UI 冷却，暂停游戏(timeScale=0)时仍照常计时？
        // 注意：unscaled 不受 timeScale 影响，但仍受应用级暂停影响
        cooldown = timers.Repeat(0.5f, () => { }, repeatCount: -1, unscaled: true, owner: this);
    }

    private void RegenTick() => Debug.Log("回血");

    private void OnDestroy()
    {
        // 单个取消
        cooldown.Cancel();
        // 或一次性取消本对象的所有定时器
        timers?.CancelOwner(this);
    }
}
```

### 设计意图与踩坑点

- **键快照遍历（核心）**：`Update` 先把 `timers.Keys` 拷进 `updateBuffer` 再遍历，并在循环内用 `TryGetValue` 重查，使得回调内 `Schedule`/`Cancel`/`CancelOwner` 修改字典都安全；新建的定时器不会在创建当帧被遍历到（下一帧才参与）。
- **三个缓冲列表复用**：`completedBuffer`/`ownerCancelBuffer`/`updateBuffer` 都是成员字段并复用，避免每帧分配，降低 GC（但也意味着 `TimerService` 非线程安全）。
- **`Remaining += Interval` 而非重置**：周期定时器累加间隔而不是从当前帧重新计时，能抵消单帧误差、减少长期漂移；但若回调耗时超过一个间隔，多次到点会在后续帧"补触发"。
- **暂停语义**：`paused` 时 `Update` 整体返回，所有定时器（含 `unscaled`）一并冻结。注意这里的暂停是**应用级**（`OnApplicationPause`），与 Unity 的 `Time.timeScale` 无关；`unscaled` 定时器不受 `timeScale` 影响，但仍受应用级暂停影响。
- **`Delay` 用缩放时间、`NextFrame` 强制非缩放**：`Delay`/`Repeat` 默认 `unscaled=false`（受 `timeScale` 影响，`timeScale=0` 时永不触发）；`NextFrame` 写死 `unscaled=true`，保证即便 `timeScale=0` 也能在下一帧执行。
- **`TimerHandle` 是 struct**：默认值即无效句柄，`Cancel()`/`IsValid` 对默认句柄安全；`IsValid` 通过 `service.Contains(id)` 反映真实状态（完成或取消后变 false）。
- **`Cancel` 返回值有意义**：基于 `Dictionary.Remove` 的返回值，可据此判断是否真的取消了一个存在的定时器。
- **回调异常隔离**：单个定时器回调抛异常被 `FrameLog.Exception` 捕获，不影响同帧其它定时器；但该一次性定时器若因异常未走到"重复判断"之外的逻辑——实际上异常发生在 `Callback()` 调用处，之后仍会正常进入重复/完成判断（异常被吞），需注意回调内部幂等。
- **`nextId` 不复用**：单调自增，`OnShutdown` 才重置为 1，避免句柄误命中后续新定时器。

## 9. Preferences 模块

Preferences 模块对 Unity 内置的 `PlayerPrefs` 做了一层服务化封装，统一了键值存取、变更通知与 JSON 序列化能力。它把散落在各处的 `PlayerPrefs.GetXxx/SetXxx` 调用收敛到一个可注入的服务接口 `IPreferencesService` 中，额外提供了 `bool` 类型支持（基于 int 模拟）、基于 Newtonsoft.Json 的对象持久化（`GetJson`/`SetJson`/`TryGetJson`），以及统一的 `Changed` 变更事件，使设置变更可以被 UI 或其他系统响应。该模块作为 `GameModuleBase` 子类，优先级 `Priority = -850`（在框架早期初始化阶段就绪，供后续模块读取配置）。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IPreferencesService`（interface，`Frame.Preferences`） | 偏好设置服务的契约：键存取、删除、保存、JSON 存取、变更事件 | 所有写操作均触发 `Changed`；`GetJson`/`TryGetJson`/`SetJson` 为泛型方法 |
| `PreferencesService`（sealed class，`Frame.Preferences`） | `IPreferencesService` 的实现，继承 `GameModuleBase` | `Priority = -850`；底层用 `PlayerPrefs`；`bool` 用 int 0/1 模拟；JSON 用 `JsonConvert` |

### `IPreferencesService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action<string> Changed` | 任意键发生变更（写入或删除）时触发，参数为受影响的 key | 仅声明事件契约，实际触发逻辑在实现类的 `RaiseChanged` 中 |
| `bool HasKey(string key)` | 判断指定 key 是否存在 | — |
| `int GetInt(string key, int fallback = 0)` | 读取 int，默认回退值 `0` | — |
| `void SetInt(string key, int value)` | 写入 int | — |
| `float GetFloat(string key, float fallback = 0f)` | 读取 float，默认回退值 `0f` | — |
| `void SetFloat(string key, float value)` | 写入 float | — |
| `string GetString(string key, string fallback = null)` | 读取 string，默认回退值 `null` | — |
| `void SetString(string key, string value)` | 写入 string | — |
| `bool GetBool(string key, bool fallback = false)` | 读取 bool，默认回退值 `false` | 约定：底层以 int 形式存储 |
| `void SetBool(string key, bool value)` | 写入 bool | 约定：底层以 int 形式存储 |
| `TData GetJson<TData>(string key, TData fallback = default(TData))` | 读取并反序列化为 `TData`，失败回退 `fallback` | 泛型方法，`TData` 无约束 |
| `bool TryGetJson<TData>(string key, out TData value)` | 尝试读取并反序列化，成功返回 `true` | `out` 参数返回结果 |
| `void SetJson<TData>(string key, TData value)` | 序列化对象并写入 | 泛型方法 |
| `bool DeleteKey(string key)` | 删除指定 key，删除成功返回 `true` | — |
| `void Save()` | 将内存中的偏好刷盘 | 对应 `PlayerPrefs.Save()` |

### `PreferencesService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public event Action<string> Changed` | 变更事件实现 | 在 `OnShutdown` 中置 `null` 以解除引用 |
| `public override int Priority { get { return -850; } }` | 模块优先级 | 固定值 `-850` |
| `protected override void OnInitialize()` | 初始化：注册服务 | 同时 `Context.Services.Register<IPreferencesService>(this)` 与 `Context.Services.Register(this)`（按接口与具体类型各注册一次） |
| `public bool HasKey(string key)` | 判断 key 是否存在 | 返回 `!string.IsNullOrWhiteSpace(key) && PlayerPrefs.HasKey(key)`；空白 key 直接判为不存在 |
| `public int GetInt(string key, int fallback = 0)` | 读取 int | `HasKey(key) ? PlayerPrefs.GetInt(key, fallback) : fallback`，key 不存在时返回 `fallback` |
| `public void SetInt(string key, int value)` | 写入 int | 先 `ValidateKey(key)`，再 `PlayerPrefs.SetInt`，最后 `RaiseChanged(key)` |
| `public float GetFloat(string key, float fallback = 0f)` | 读取 float | `HasKey(key) ? PlayerPrefs.GetFloat(key, fallback) : fallback` |
| `public void SetFloat(string key, float value)` | 写入 float | `ValidateKey` → `PlayerPrefs.SetFloat` → `RaiseChanged` |
| `public string GetString(string key, string fallback = null)` | 读取 string | `HasKey(key) ? PlayerPrefs.GetString(key, fallback) : fallback` |
| `public void SetString(string key, string value)` | 写入 string | `ValidateKey` → `PlayerPrefs.SetString(key, value ?? string.Empty)`（null 转空串）→ `RaiseChanged` |
| `public bool GetBool(string key, bool fallback = false)` | 读取 bool | 复用 `GetInt(key, fallback ? 1 : 0) != 0`，非零即 `true` |
| `public void SetBool(string key, bool value)` | 写入 bool | 复用 `SetInt(key, value ? 1 : 0)`，因此也会触发一次 `RaiseChanged` |
| `public TData GetJson<TData>(string key, TData fallback = default(TData))` | 读取 JSON 对象 | 内部调用 `TryGetJson`，成功返回值，否则返回 `fallback` |
| `public bool TryGetJson<TData>(string key, out TData value)` | 尝试读取 JSON | 先 `GetString(key, null)`，若空白则 `value = default; return false`；否则 `JsonConvert.DeserializeObject<TData>(json)`，返回 `value != null`；异常时 `FrameLog.Exception(exception)`、`value = default`、返回 `false` |
| `public void SetJson<TData>(string key, TData value)` | 写入 JSON | `ValidateKey` → `JsonConvert.SerializeObject(value)` → `PlayerPrefs.SetString(key, json)` → `RaiseChanged`。注意此处直接调 `PlayerPrefs.SetString` 而非 `SetString`，故 value 为 null 时序列化结果可能是字符串 `"null"` |
| `public bool DeleteKey(string key)` | 删除 key | 若 `string.IsNullOrWhiteSpace(key) || !PlayerPrefs.HasKey(key)` 返回 `false`；否则 `PlayerPrefs.DeleteKey(key)` → `RaiseChanged(key)` → 返回 `true` |
| `public void Save()` | 刷盘 | `PlayerPrefs.Save()` |
| `protected override void OnShutdown()` | 关闭：保存并清理 | 先 `Save()`，再 `Changed = null` |
| `private void RaiseChanged(string key)` | 触发变更事件 | 取本地副本 `handler`；为 null 直接返回；调用包裹在 try/catch 中，异常用 `FrameLog.Exception(exception)` 记录，避免订阅者异常中断写流程 |
| `private static void ValidateKey(string key)` | 校验 key 非空白 | 空白时抛 `ArgumentException("Preference key is required.", "key")` |

### 流转逻辑

- **读取流程**：所有 `GetXxx` 先经 `HasKey` 判断键是否存在（且 key 非空白），不存在则直接返回 `fallback`，避免对不存在键返回 `PlayerPrefs` 内部默认值的歧义。
- **写入流程**：所有写操作（`SetInt/SetFloat/SetString/SetJson`）统一遵循 `ValidateKey → PlayerPrefs.SetXxx → RaiseChanged(key)` 三段式。`SetBool` 复用 `SetInt`、`DeleteKey` 在删除后也调用 `RaiseChanged`，因此这两条路径同样会发出变更通知。
- **bool 编码**：bool 不直接存储，写入时 `true→1 / false→0`，读取时 `!=0 ? true : false`，与 int 共用同一存储槽。
- **JSON 流程**：`SetJson` 用 `JsonConvert.SerializeObject` 序列化为字符串后写入；`GetJson` 委托 `TryGetJson`，后者读出字符串、空白判否、反序列化并以 `value != null` 作为成功判据，异常被吞掉并记录日志后返回失败。
- **事件安全**：`RaiseChanged` 对订阅者回调做 try/catch 隔离，单个订阅者抛异常不会影响 `PlayerPrefs` 写入或后续逻辑。
- **生命周期**：`OnInitialize` 把自身按接口和具体类型注册到 `Context.Services`；`OnShutdown` 先刷盘再清空事件订阅。

### 使用示例

```csharp
var prefs = Context.Services.Resolve<IPreferencesService>();

// 监听变更（如设置界面同步）
prefs.Changed += key => Debug.Log($"偏好已变更: {key}");

// 基础类型
prefs.SetInt("HighScore", 1200);
int score = prefs.GetInt("HighScore");           // 1200
prefs.SetBool("MusicEnabled", true);
bool music = prefs.GetBool("MusicEnabled", true); // true

// 对象持久化
var settings = new GameOptions { Volume = 0.8f, Difficulty = 2 };
prefs.SetJson("GameOptions", settings);

if (prefs.TryGetJson<GameOptions>("GameOptions", out var loaded))
{
    Debug.Log(loaded.Volume);
}

// 带回退的读取
var opt = prefs.GetJson<GameOptions>("Missing", new GameOptions());

// 删除与持久化
if (prefs.DeleteKey("HighScore")) { /* 删除成功 */ }
prefs.Save();
```

### 设计意图与踩坑点

- `HasKey` 把空白 key 视为不存在，因此对空白 key 的 `GetXxx` 永远返回 `fallback`，而 `SetXxx`（经 `ValidateKey`）会直接抛 `ArgumentException`，二者行为不对称：读宽松、写严格。
- `SetString` 会把 `null` 值替换为 `string.Empty` 再存储，但 `SetJson` 走的是 `PlayerPrefs.SetString(key, json)`（不经 `SetString` 的 null 兜底），对 `null` 对象会序列化成字符串 `"null"`，后续 `TryGetJson` 反序列化得到 `null` 并返回 `false`。
- `TryGetJson` 用 `value != null` 作为成功判据：若 `TData` 是值类型，反序列化结果不为 null 恒成立；若反序列化出 `null`（如存储内容为 `"null"`）则视为失败。
- JSON 依赖 Newtonsoft.Json（`using Newtonsoft.Json`），反序列化异常被捕获并通过 `FrameLog.Exception` 记录，不向调用方抛出。
- `Changed` 事件在 `OnShutdown` 被置 `null`，模块关闭后不再回调；`OnShutdown` 同时会 `Save()` 一次确保落盘。
- `PlayerPrefs` 的写入并非立即落盘，需调用 `Save()`（或等待 Unity 在退出时自动保存）；本服务的 `SetXxx` 不自动 `Save`。

---

## 10. Pooling 模块

Pooling 模块提供两类对象池：面向 `GameObject` 的 `GameObjectPool`（带激活/失活、父节点管理、`IPoolable` 生命周期回调）与面向纯 C# 对象的泛型 `ObjectPool<T>`（带工厂、`onGet/onRelease/onDestroy` 钩子、`IResettablePoolItem` 重置）。`PoolService`（`GameModuleBase`，`Priority = -700`）则统一管理一组按 key 索引的 `GameObjectPool`，在场景中创建名为 "Pools" 的根节点并为每个池建立独立子节点用于挂载失活对象。两套池均使用 `Stack` 存放空闲对象、`HashSet` 防止重复归还（double-release），并通过 `PoolStats` 暴露运行时统计。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IPoolService`（interface，`Frame.Pooling`） | GameObject 对象池服务契约：创建、获取、Spawn/Despawn、统计、清理 | 仅管理 `GameObjectPool` |
| `PoolService`（sealed class，`Frame.Pooling`） | `IPoolService` 实现，继承 `GameModuleBase` | `Priority = -700`；建 "Pools" 根节点；用 `Context.Settings.DefaultGameObjectPoolMaxSize` 作 maxSize 兜底 |
| `GameObjectPool`（sealed class，`Frame.Pooling`） | 单个 GameObject 池：实例化、激活、归还、预热、清理 | `Stack<GameObject>` + `HashSet<GameObject>`；触发 `IPoolable` 回调 |
| `ObjectPool<T>`（sealed class，`Frame.Pooling`，`where T : class`） | 单个纯 C# 对象池 | 工厂 + 钩子 + `IResettablePoolItem`；`MaxSize` 默认 `128` |
| `IPoolable`（interface，`Frame.Pooling`） | GameObject 池对象生命周期回调 | `OnSpawned` / `OnDespawned` |
| `IResettablePoolItem`（interface，`Frame.Pooling`） | `ObjectPool<T>` 归还前重置回调 | `ResetForPool` |
| `PoolStats`（sealed class，`[Serializable]`，`Frame.Pooling`） | 池运行时统计快照 | 全为 public 字段，可被 Unity 序列化展示 |

### `IPoolService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `GameObjectPool CreateGameObjectPool(string key, GameObject prefab, int maxSize = -1, int prewarm = 0)` | 创建（或返回已存在的）GameObject 池 | `maxSize` 默认 `-1`（表示用配置兜底）；`prewarm` 默认 `0` |
| `bool TryGetGameObjectPool(string key, out GameObjectPool pool)` | 尝试按 key 取池 | — |
| `GameObject Spawn(string key, Transform parent = null)` | 从指定池取一个实例 | `parent` 默认 `null` |
| `void Despawn(string key, GameObject instance)` | 归还实例到指定池 | — |
| `PoolStats GetGameObjectPoolStats(string key)` | 取单个池统计 | 池不存在时返回 `null` |
| `List<PoolStats> GetAllGameObjectPoolStats()` | 取所有池统计 | — |
| `void ClearGameObjectPool(string key)` | 清理单个池的空闲对象 | — |
| `void ClearAllGameObjectPools()` | 清理所有池的空闲对象 | — |

### `PoolService.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<string, GameObjectPool> gameObjectPools` | key→池 映射表 | 初始化为空字典 |
| `private Transform poolRoot` | 所有池子节点的父节点（"Pools"） | 在 `OnInitialize` 创建，`OnShutdown` 销毁 |
| `public override int Priority { get { return -700; } }` | 模块优先级 | 固定值 `-700` |
| `protected override void OnInitialize()` | 初始化 | `new GameObject("Pools")` → `SetParent(Context.Root, false)` → 记为 `poolRoot`；按 `IPoolService` 与具体类型各注册一次 |
| `public GameObjectPool CreateGameObjectPool(string key, GameObject prefab, int maxSize = -1, int prewarm = 0)` | 创建/复用池 | key 空白时回退为 `prefab.name`（prefab 为 null 时回退 `"Pool"`）；`prefab == null` 抛 `FrameException("Prefab is required to create a GameObjectPool.")`；已存在同 key 直接返回旧池；为池新建子 GameObject(key) 挂到 `poolRoot`；`resolvedMaxSize = maxSize > 0 ? maxSize : Context.Settings.DefaultGameObjectPoolMaxSize`；`prewarm > 0` 时调 `pool.Prewarm(prewarm)`；最后加入字典 |
| `public bool TryGetGameObjectPool(string key, out GameObjectPool pool)` | 查池 | 直接转发 `gameObjectPools.TryGetValue` |
| `public GameObject Spawn(string key, Transform parent = null)` | 取实例 | 池不存在抛 `FrameException("GameObject pool is not registered: " + key)`；否则 `pool.Get(parent)` |
| `public void Despawn(string key, GameObject instance)` | 归还实例 | 池存在则 `pool.Release(instance)`；池不存在且 `instance != null` 则 `Object.Destroy(instance)`（兜底销毁，防泄漏） |
| `public PoolStats GetGameObjectPoolStats(string key)` | 单池统计 | 存在则 `pool.GetStats(key)`，否则 `null` |
| `public List<PoolStats> GetAllGameObjectPoolStats()` | 全池统计 | 遍历字典，对每个 `pair.Value.GetStats(pair.Key)` 收集成 `List<PoolStats>` |
| `public void ClearGameObjectPool(string key)` | 清理单池 | 存在则 `pool.Clear()` |
| `public void ClearAllGameObjectPools()` | 清理全池 | 遍历 `gameObjectPools.Values` 逐个 `Clear()` |
| `protected override void OnShutdown()` | 关闭 | `ClearAllGameObjectPools()` → `gameObjectPools.Clear()` → 若 `poolRoot != null` 则 `Object.Destroy(poolRoot.gameObject)` → `poolRoot = null` |

### `GameObjectPool.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly GameObject prefab` | 实例化用的预制体 | 构造时注入 |
| `private readonly Transform parent` | 失活实例挂载的默认父节点 | 构造时注入（由 `PoolService` 提供的 per-pool 子节点） |
| `private readonly int maxSize` | 空闲池容量上限 | 构造时 `Mathf.Max(1, maxSize)`，至少为 1 |
| `private readonly Stack<GameObject> inactive` | 空闲实例栈（LIFO） | — |
| `private readonly HashSet<GameObject> inPool` | 当前已在池中的实例集合 | 用于 O(1) 去重，防 double-release |
| `private int countActive` | 当前激活中的实例数 | — |
| `private int createdCount` | 累计创建（Instantiate）次数 | — |
| `private int destroyedCount` | 累计销毁次数 | — |
| `private int getCount` | 累计 Get 次数 | — |
| `private int releaseCount` | 累计 Release 次数 | — |
| `public GameObjectPool(GameObject prefab, Transform parent, int maxSize)` | 构造函数 | 赋值三字段，`maxSize` 经 `Mathf.Max(1, maxSize)` 规整 |
| `public int CountInactive { get { return inactive.Count; } }` | 当前空闲实例数 | — |
| `public int CountActive { get { return countActive; } }` | 当前激活实例数 | — |
| `public GameObject Get(Transform newParent = null)` | 取一个实例 | 空闲栈非空则 `inactive.Pop()`，否则 `Object.Instantiate(prefab)` 并 `createdCount++`；随后 `inPool.Remove(instance)`、`countActive++`、`getCount++`；`SetParent(newParent ?? parent, false)`；`SetActive(true)`；用 `GetComponentsInChildren<IPoolable>(true)`（含未激活）取所有 `IPoolable` 并逐个 `OnSpawned()`；返回实例 |
| `public void Release(GameObject instance)` | 归还实例 | `instance == null` 直接返回；`inPool.Contains(instance)` 直接返回（防重复归还）；`countActive > 0` 时 `countActive--`；`releaseCount++`；遍历 `IPoolable` 逐个 `OnDespawned()`；若 `inactive.Count >= maxSize` 则 `Object.Destroy(instance)` 且 `destroyedCount++` 后返回（超容直接销毁）；否则 `SetActive(false)` → `SetParent(parent, false)` → `inactive.Push(instance)` → `inPool.Add(instance)` |
| `public void Prewarm(int count)` | 预热生成 count 个空闲实例 | 循环 `Object.Instantiate(prefab, parent, false)` → `createdCount++` → `SetActive(false)` → `inactive.Push` → `inPool.Add`。注意不触发 `IPoolable` 回调，也不检查 `maxSize` |
| `public void Clear()` | 销毁所有空闲实例 | `while inactive.Count > 0`：`Pop()`，非 null 则 `Object.Destroy` 且 `destroyedCount++`；最后 `inPool.Clear()`。注意只销毁空闲对象，不影响 `countActive` 计数 |
| `public PoolStats GetStats(string key = null)` | 生成统计快照 | 填充 `Key/MaxSize/CountActive/CountInactive/CountTotal(=countActive+CountInactive)/CreatedCount/DestroyedCount/GetCount/ReleaseCount` |

### `ObjectPool.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `sealed class ObjectPool<T> where T : class` | 纯 C# 对象池，泛型 `T` 约束为引用类型 | — |
| `private readonly Func<T> factory` | 对象工厂 | 不可为 null |
| `private readonly Action<T> onGet` | Get 时回调 | 可为 null |
| `private readonly Action<T> onRelease` | Release 时回调 | 可为 null |
| `private readonly Action<T> onDestroy` | 销毁时回调 | 可为 null |
| `private readonly Stack<T> inactive` | 空闲对象栈 | — |
| `private readonly HashSet<T> inPool` | 在池对象集合，防 double-release | — |
| `private int countActive / createdCount / destroyedCount / getCount / releaseCount` | 运行时计数 | 与 `GameObjectPool` 语义一致 |
| `public ObjectPool(Func<T> factory, Action<T> onGet = null, Action<T> onRelease = null, Action<T> onDestroy = null, int maxSize = 128)` | 构造函数 | `factory == null` 抛 `ArgumentNullException("factory")`；`MaxSize = Math.Max(1, maxSize)`；`maxSize` 默认 `128` |
| `public int MaxSize { get; private set; }` | 空闲池容量上限 | 构造时设定，外部只读 |
| `public int CountInactive { get { return inactive.Count; } }` | 空闲对象数 | — |
| `public int CountActive { get { return countActive; } }` | 激活对象数 | — |
| `public T Get()` | 取一个对象 | 空闲栈非空则 `Pop()` 并 `inPool.Remove(item)`，否则 `factory()` 且 `createdCount++`；`countActive++`、`getCount++`；`onGet?.Invoke(item)`；返回 |
| `public void Release(T item)` | 归还对象 | `item == null \|\| inPool.Contains(item)` 直接返回；若 `item is IResettablePoolItem` 则调 `ResetForPool()`；`countActive > 0` 时 `countActive--`；`releaseCount++`；`onRelease?.Invoke(item)`；若 `inactive.Count >= MaxSize` 则 `onDestroy?.Invoke(item)`、`destroyedCount++` 后返回；否则 `inactive.Push(item)` + `inPool.Add(item)` |
| `public void Prewarm(int count)` | 预热 | 循环 `factory()` → `createdCount++` → `Release(item)`。注意：经 `Release` 入池，会触发 `IResettablePoolItem.ResetForPool` 与 `onRelease`，且受 `MaxSize` 限制；`Release` 内再 `releaseCount++` |
| `public PoolStats GetStats(string key = null)` | 统计快照 | 同 `GameObjectPool.GetStats` 结构，`MaxSize` 取属性 `MaxSize` |
| `public void Clear()` | 清空空闲对象 | 若 `onDestroy != null`：`while inactive.Count > 0`：`onDestroy(inactive.Pop())` 且 `destroyedCount++`；否则 `destroyedCount += inactive.Count`（不调销毁）；最后 `inactive.Clear()` + `inPool.Clear()` |

### `IPoolable.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `void OnSpawned()` | GameObject 从池取出并激活后回调 | 由 `GameObjectPool.Get` 通过 `GetComponentsInChildren<IPoolable>(true)` 逐个触发 |
| `void OnDespawned()` | GameObject 归还时回调 | 由 `GameObjectPool.Release` 在失活/销毁前逐个触发 |

### `IResettablePoolItem.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `void ResetForPool()` | `ObjectPool<T>` 对象归还前的重置钩子 | 源码注释：「专门用来给 objectpool 对应的池对象来实现的接口」。仅 `ObjectPool<T>.Release` 中通过 `item as IResettablePoolItem` 判断并调用 |

### `PoolStats.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `[Serializable] sealed class PoolStats` | 池统计数据快照 | 可被 Unity 序列化/Inspector 展示 |
| `public string Key` | 池标识 | — |
| `public int MaxSize` | 空闲容量上限 | — |
| `public int CountActive` | 激活数 | — |
| `public int CountInactive` | 空闲数 | — |
| `public int CountTotal` | 总数（active+inactive） | 由 `GetStats` 计算填充 |
| `public int CreatedCount` | 累计创建数 | — |
| `public int DestroyedCount` | 累计销毁数 | — |
| `public int GetCount` | 累计取出数 | — |
| `public int ReleaseCount` | 累计归还数 | — |

### 流转逻辑

- **PoolService 节点结构**：`OnInitialize` 创建名为 "Pools" 的 GameObject 作为 `poolRoot`，挂到 `Context.Root` 下。每次 `CreateGameObjectPool` 又新建一个名为 `key` 的子 GameObject 作为该池的 `parent`，挂到 `poolRoot` 下——即「Pools/<key>」两级层级，失活对象统一回收到对应子节点下，便于在 Hierarchy 中观察。
- **Stack + HashSet 防 double-release**：两套池都用 `Stack` 存空闲对象（LIFO，复用最近归还者，缓存局部性好），用 `HashSet`（`inPool`）记录「当前在池中」的对象。`Release` 首先 `inPool.Contains` 判断，若已在池中则直接返回，从而防止同一对象被重复归还导致栈中出现重复引用；`Get` 取出后立即 `inPool.Remove`。
- **GameObjectPool.Get 流程**：优先复用空闲栈对象，空则 `Instantiate` 新建（`createdCount++`）→ 计数更新 → 重设父节点（优先 `newParent`，否则默认 `parent`）→ 激活 → 调用所有子层级 `IPoolable.OnSpawned()`（`GetComponentsInChildren<IPoolable>(true)` 的 `true` 表示包含未激活子物体）。
- **GameObjectPool.Release 流程**：null/重复归还提前返回 → 计数更新 → 先调 `IPoolable.OnDespawned()` → 再判容量：空闲已满（`inactive.Count >= maxSize`）则直接 `Destroy`（`destroyedCount++`），否则失活、复位父节点为默认 `parent`、入栈并登记到 `inPool`。
- **ObjectPool.Release 流程**：与 GameObjectPool 类似，但额外在计数前调用 `IResettablePoolItem.ResetForPool`（若实现）；超容时走 `onDestroy` 回调而非物理销毁。
- **预热差异**：`GameObjectPool.Prewarm` 直接实例化并压栈，不触发回调、不检查 maxSize；`ObjectPool<T>.Prewarm` 走 `Release`，因此会触发 `ResetForPool`/`onRelease` 且受 maxSize 约束。
- **Clear 差异**：`GameObjectPool.Clear` 物理 `Destroy` 所有空闲对象并清空 `inPool`，但不触碰激活中的对象、不复位 `countActive`；`ObjectPool<T>.Clear` 仅在有 `onDestroy` 时逐个回调，否则只累加 `destroyedCount`，最后清空栈与集合。
- **Despawn 兜底**：`PoolService.Despawn` 在 key 未注册且实例非 null 时直接 `Object.Destroy(instance)`，避免无主实例泄漏。
- **关闭流程**：`OnShutdown` 先 `ClearAllGameObjectPools`（销毁所有空闲对象）→ 清空字典 → 销毁 `poolRoot`（连带销毁所有 per-pool 子节点及其下尚未取出的对象）→ 置 `poolRoot = null`。

### 使用示例

```csharp
// GameObject 池：通过服务管理
var pools = Context.Services.Resolve<IPoolService>();
pools.CreateGameObjectPool("Bullet", bulletPrefab, maxSize: 64, prewarm: 16);

GameObject bullet = pools.Spawn("Bullet", firePoint);   // 复用或新建，触发 OnSpawned
// ... 命中后归还
pools.Despawn("Bullet", bullet);                         // 触发 OnDespawned，回收或销毁

PoolStats stats = pools.GetGameObjectPoolStats("Bullet");
Debug.Log($"激活 {stats.CountActive} / 空闲 {stats.CountInactive}");

// 让池对象响应生命周期
public sealed class Bullet : MonoBehaviour, IPoolable
{
    public void OnSpawned()  { /* 重置速度、特效 */ }
    public void OnDespawned(){ /* 停止拖尾、清理状态 */ }
}

// 纯 C# 对象池
var sbPool = new ObjectPool<StringBuilder>(
    factory: () => new StringBuilder(),
    onRelease: sb => sb.Clear(),
    maxSize: 32);

StringBuilder sb = sbPool.Get();
sb.Append("hello");
sbPool.Release(sb);   // 若实现 IResettablePoolItem 还会调用 ResetForPool

// 可重置对象
public sealed class Damage : IResettablePoolItem
{
    public int Value;
    public void ResetForPool() => Value = 0;
}
```

### 设计意图与踩坑点

- `GameObjectPool.Clear()` 与 `ObjectPool<T>.Clear()` 只销毁/释放**空闲**对象，已取出（active）的对象不受影响，因此 `Clear` 后 `CountActive` 不归零；要彻底回收需先把所有活动对象 `Release`/`Despawn`。
- `maxSize` 在 `GameObjectPool` 构造与 `ObjectPool<T>` 构造中都经 `Max(1, ...)` 规整，**最小为 1**；`ObjectPool<T>` 的 `maxSize` 默认 `128`。
- `PoolService.CreateGameObjectPool` 的 `maxSize` 仅在 `> 0` 时生效，否则（含默认 `-1`）回退到 `Context.Settings.DefaultGameObjectPoolMaxSize`；注意 `0` 也会触发回退。
- `CreateGameObjectPool` 对同一 key 幂等：已存在则直接返回旧池，传入的新 `prefab/maxSize/prewarm` 会被忽略。
- key 为空白时会被替换为 `prefab.name`（prefab 为 null 则为 `"Pool"`），但 prefab 为 null 时随后会抛 `FrameException`，故空白 key + 空 prefab 实际走不到取 `"Pool"` 的分支。
- double-release 防护依赖 `inPool` 集合；但若对同一个未在池中的实例调用两次 `Release`，第一次会把它入池，第二次因 `inPool.Contains` 返回而安全。
- `GameObjectPool.Prewarm` 不触发 `IPoolable.OnSpawned/OnDespawned`，预热出的对象处于失活状态，首次 `Get` 才会触发 `OnSpawned`。
- `Get` 使用 `GetComponentsInChildren<IPoolable>(true)`（含未激活子物体），每次取出都会分配数组并遍历子层级，高频路径需注意 GC 与性能。
- `PoolStats` 是值快照（每次 `GetStats` 新建），不会随池实时联动。
- `ObjectPool<T>.Clear` 在没有 `onDestroy` 时只累加 `destroyedCount` 而不做任何实际清理动作（对象交由 GC），这只是统计意义上的「销毁」。

---

## 11. StateMachine 模块

StateMachine 模块是一个 Type 标识的有限状态机实现。状态实现 `IState` 或继承 `StateBase`，状态机注册时直接使用状态实例的运行时 `Type` 作为唯一标识；切换可用 `Change<TState>()` 或 `Change(typeof(TState))`。当前版本参考 Unity Animator Controller 的核心结构：Controller 持有参数和 Layer，Layer 持有状态图，状态节点可挂子状态机；转换支持条件、Trigger、Any State、Exit Time、优先级和转换时长元数据。它不直接播放动画，但适合角色 AI、UI 流程、战斗阶段、技能/动作逻辑等需要分层状态的场景。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `StateChangeContext`（struct） | 进入状态的上下文 | Machine、LayerName、From、To、Parameter、HasFrom、HasParameter |
| `StateTransitionContext`（struct） | 转换完成上下文 | From、To、Transition、LayerName、Parameter |
| `IState`（interface） | 单个状态的生命周期契约 | `Enter(context)/Tick(float)/Exit`，状态类型即标识 |
| `StateBase`（abstract class） | 可选便利基类 | 空虚方法 + `Machine` 反向引用 |
| `StateParameterSet` | Animator 风格参数表 | Float、Int、Bool、Trigger |
| `StateCondition` | 转换条件 | If/IfNot/Greater/Less/Equal/NotEqual |
| `StateTransition` | 转换规则 | 条件、Any State、Exit Time、Duration、Priority |
| `StateNode` | 状态节点 | 状态实例、StateType、Length、Speed、Time、子状态机 |
| `StateGraph` | 状态图/子状态机 | 状态集合、Entry、普通转换、Any State |
| `StateMachineLayer` | 并行 Layer | 根状态图、当前激活路径、独立转换队列 |
| `StateMachine`（sealed class） | Controller 入口 | 参数、Layer、事件中心集成、Tick/Clear |


### `IState.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `interface IState` | 状态接口 | 不再要求状态自己返回 id |
| 运行时 `Type` | 唯一标识 | `Add` 时取状态实例的 `GetType()` 作为字典 key |
| `void Enter(StateChangeContext context)` | 进入状态 | 参数和来源状态统一从 context 读取 |
| `void Tick(float deltaTime)` | 每帧/每步驱动 | — |
| `void Exit()` | 离开状态 | — |

### `StateChangeContext` / `StateBase.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Machine` / `LayerName` | 来源状态机和 Layer | 便于状态内部按层查询或打日志 |
| `From` / `To` | 来源和目标状态类型 | 首次进入时 `HasFrom=false` |
| `Parameter` / `HasParameter` | `Change<TState>(parameter)` 或 `Change(type, parameter)` 传入的数据 | 参数类型由具体状态约定 |
| `TryGetParameter<T>()` | 类型安全读取参数 | 类型不匹配返回 false |
| `abstract StateBase` | 便利基类 | 实现 `IState`，空虚 `Enter/Tick/Exit`，含 `Machine` 反向引用（`Add` 时由机器注入）便于状态内部 `Machine.Change(...)` |

### 参数 / 转换 / 图 / Layer

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `StateParameterSet.SetFloat/SetInt/SetBool/SetTrigger` | 写参数 | 不存在时按类型自动创建；同名不同类型抛异常 |
| `StateCondition.If/IfNot/Greater/Less/Equal/NotEqual` | 条件判断 | 转换条件全部满足才触发 |
| `StateTransition.When/WhenAll` | 添加条件 | 链式配置 |
| `StateTransition.WithExitTime(float)` | 等待源状态 normalized time | `StateNode.Length` 决定 normalized time |
| `StateNode.CreateChildMachine()` | 创建子状态机 | 父状态进入后自动进入子状态机 Entry |
| `StateGraph.EntryStateType` | 默认进入状态 | 第一个 AddState 会自动成为 Entry |
| `StateGraph.AddAnyTransition(to)` | Any State 转换 | 当前图内任意激活状态都可触发 |
| `StateMachine.AddLayer(name)` | 创建并行 Layer | 每个 Layer 独立保存当前状态和转换队列 |

### `StateMachine.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `StateMachine()` | 默认构造 | 创建 Base Layer 和参数表 |
| `StateMachine(IEventBus eventBus)` | 事件中心构造 | eventBus 可为空 |
| `Parameters` | 参数表 | Animator 风格 Float/Int/Bool/Trigger |
| `BaseLayer` / `Layers` | Layer 管理 | Base Layer 始终存在 |
| `StateEntered/StateExited/StateChanged/Transitioned` | 本地事件 | 不依赖事件中心 |
| `BindTrigger<TEvent>(trigger)` | 事件驱动 Trigger | 收到 EventBus 事件后自动 SetTrigger |
| `CurrentState` / `CurrentStateType` | Base Layer 当前状态 /类型 | 多 Layer 时其他层从 `GetLayer(name)` 查询 |
| `Add/AddTransition/AddAnyTransition` | Base Layer 便捷入口 | 完整配置可直接访问 `BaseLayer.Root` |
| `Start/Tick/Change/Clear` | 生命周期 | `Tick` 驱动所有启用 Layer |

### 流转逻辑

- **注册**：`Add` 以状态实例的运行时 `Type` 为 key 注册到 Base Layer；子状态机通过 `StateNode.CreateChildMachine().AddState(...)` 注册；同一 Layer 内状态类型必须唯一。
- **自动进入**：`Start()` 或首次 `Tick()` 会让每个启用 Layer 进入其根状态图 Entry；如果 Entry 节点有子状态机，会继续进入子状态机 Entry。
- **转换求值**：每帧先 Tick 当前激活路径上的所有状态，再按优先级和注册顺序查找命中的普通转换/Any State 转换。条件全部满足且 Exit Time 达到后才切换；Trigger 命中后自动消耗。
- **层级切换**：切换时计算当前路径和目标路径的公共前缀，先从叶子向上 Exit，再从公共父级向目标叶子 Enter。
- **事件中心**：有 `IEventBus` 时，进入、退出、转换会分别发布 `StateMachineStateEntered`、`StateMachineStateExited`、`StateMachineTransitioned`。
- **异常处理**：Enter/Tick/Exit/StateChanged 抛出的异常不被吞掉，按原异常向外抛出。
- **清理（Clear）**：当前状态路径先 `Exit`，清空状态图、Layer 和参数，并重建空 Base Layer。

### 使用示例

```csharp
// 继承 StateBase 只重写需要的钩子；可在状态内部用 Machine 请求切换
public sealed class IdleState : StateBase
{
    public override void Enter(StateChangeContext context) { /* 播放待机动画 */ }
    public override void Tick(float dt) { /* 检测玩家 */ }
}

var fsm = new StateMachine();
StateNode locomotion = fsm.Add(new LocomotionState());
StateGraph locomotionGraph = locomotion.CreateChildMachine();
locomotionGraph.AddState(new IdleState()).WithLength(1f);
locomotionGraph.AddState(new PatrolState());
locomotionGraph.AddTransition<IdleState, PatrolState>()
    .When(StateCondition.Greater("Speed", 0.1f));

fsm.Add(new ChaseState());
fsm.Add(new DeadState());
fsm.AddAnyTransition<DeadState>().When(StateCondition.Trigger("Dead"));

fsm.StateChanged += context => FrameLog.Info($"{context.From} -> {context.To}");

fsm.Start();                              // 进入 Locomotion，再进入子状态机 Idle
void Update() => fsm.Tick(Time.deltaTime);// 转发 Tick，自动评估转换

fsm.Change<ChaseState>(target);             // target 在 ChaseState.Enter(context) 里读取
fsm.Clear();                                // 当前状态 Exit，并清空所有注册
```

### 设计意图与踩坑点

- **参数入口统一**：需要进入参数时直接使用 `Change<TState>(parameter)` 或 `Change(type, parameter)`，目标状态在 `Enter(context)` 中读取，不再通过额外接口拆一次生命周期。
- **状态类型唯一性**：同一 Layer 内状态类型必须唯一；子状态机不是命名空间隔离区，这是为了让转换目标保持简单明确。
- **自切换被短路**：对当前状态再次 `Change` 返回 `true` 但不触发 `Exit`/`Enter`，不能用它来「重置」状态。
- **防重入而非禁止重入**：在 `Enter/Exit/Tick` 内调用 `Change` 是安全的——请求会排队，在当前转换或当前 Tick 结束后按 FIFO 应用，不会产生递归 Exit/Enter 栈，也不会破坏激活路径遍历。
- **异常不静默**：状态回调异常按原异常向外抛出；如果 `Enter` 中途失败，状态实现仍应自行保证内部数据一致。
- **不接管 Unity 生命周期**：状态机不持有更新循环，需调用方在 `Update` 调 `Tick`。

---

## 12. Utilities 模块

Utilities 模块收纳了与具体业务无关的通用小工具：`DisposableAction` 把任意 `Action` 包装成 `IDisposable`，便于配合 `using` 语句做「作用域结束自动执行」的清理/还原；`FramePathUtility` 提供路径相关的静态辅助方法，主要用于把各种来源的路径规范化为 `Resources.Load` 可用的相对路径，以及把字符串净化为合法文件名。两者均无外部依赖（仅用 `System` / `System.IO`）。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `DisposableAction`（sealed class，`Frame.Utilities`，实现 `IDisposable`） | 把 `Action` 包装为可释放对象，`Dispose` 时执行一次 | 仅执行一次（执行后置 null） |
| `FramePathUtility`（static class，`Frame.Utilities`） | 路径规范化与文件名净化的静态工具 | `NormalizeResourcesPath` / `SanitizeFileName` |

### `DisposableAction.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `sealed class DisposableAction : IDisposable` | Action 到 IDisposable 的适配器 | — |
| `private Action onDispose` | 待执行的回调 | 执行后被置 `null` |
| `public DisposableAction(Action onDispose)` | 构造，注入回调 | 不做 null 校验（构造时允许传 null） |
| `public void Dispose()` | 执行一次回调 | 取本地副本 `action = onDispose`；若为 null 直接返回；先 `onDispose = null`（先清后调，保证幂等/只执行一次），再 `action()` |

### `FramePathUtility.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `static class FramePathUtility` | 路径工具静态类 | — |
| `public static string NormalizeResourcesPath(string path)` | 把路径规范化为 `Resources` 相对路径 | 见下方分步说明；空白输入返回 `string.Empty` |
| `public static string SanitizeFileName(string fileName)` | 把字符串净化为合法文件名 | 空白返回 `"default"`；遍历 `Path.GetInvalidFileNameChars()` 把每个非法字符替换为 `'_'` |

**`NormalizeResourcesPath(string path)` 分步逻辑：**
1. 若 `string.IsNullOrWhiteSpace(path)`，直接返回 `string.Empty`。
2. `path = path.Replace('\\', '/').Trim()`：把反斜杠统一替换为正斜杠，并去除首尾空白。
3. `string extension = Path.GetExtension(path)`：取扩展名；若非空（`!string.IsNullOrEmpty(extension)`），则 `path = path.Substring(0, path.Length - extension.Length)` 去掉扩展名（含点号）。
4. 定位 `const string resourcesToken = "/Resources/"`：用 `path.IndexOf(resourcesToken, StringComparison.OrdinalIgnoreCase)`（忽略大小写）查找；若 `resourcesIndex >= 0`，则 `path = path.Substring(resourcesIndex + resourcesToken.Length)`，即截取 "/Resources/" 之后的部分（去掉前缀直到并包含该 token）。
5. 返回处理后的 `path`。

**`SanitizeFileName(string fileName)` 分步逻辑：**
1. 若 `string.IsNullOrWhiteSpace(fileName)`，返回 `"default"`。
2. 取 `Path.GetInvalidFileNameChars()` 得到非法字符数组。
3. 遍历该数组，对每个非法字符执行 `fileName = fileName.Replace(invalid[i], '_')`，将其替换为下划线。
4. 返回净化后的 `fileName`。

### 流转逻辑

- **DisposableAction 包裹流程**：构造时把 `Action` 存入 `onDispose` 字段。`Dispose` 时先把字段读到局部变量 `action`，若为 null 立即返回；否则**先**把 `onDispose` 置 null **再**执行 `action()`。这种「先清空后调用」的顺序保证了即便重复 `Dispose`（如 `using` 正常退出 + 手动调用）也只会执行一次回调，具备幂等性。
- **NormalizeResourcesPath 数据流**：原始路径 →（统一分隔符 + Trim）→（剥离扩展名）→（截断到 "/Resources/" 之后）→ 规范化结果。三步串行、逐步收窄，最终得到不带扩展名、不含 Resources 前缀、使用正斜杠的相对路径，正好契合 `Resources.Load("子路径/资源名")` 的输入要求。
- **SanitizeFileName 数据流**：原始字符串 →（空白兜底为 "default"）→（逐个替换系统级非法文件名字符为 '_'）→ 合法文件名。

### 使用示例

```csharp
// DisposableAction：作用域结束自动还原
Cursor.visible = false;
using (new DisposableAction(() => Cursor.visible = true))
{
    // 这段作用域内光标隐藏，退出 using 时自动恢复
    DoSomethingModal();
}

// 也可作为事件订阅的「取消令牌」
service.Changed += handler;
IDisposable unsub = new DisposableAction(() => service.Changed -= handler);
// ... 不再需要时
unsub.Dispose();   // 只会退订一次

// 路径规范化：用于 Resources.Load
string p1 = FramePathUtility.NormalizeResourcesPath(@"Assets\Game\Resources\UI\MainMenu.prefab");
// => "UI/MainMenu"
var prefab = Resources.Load<GameObject>(p1);

string p2 = FramePathUtility.NormalizeResourcesPath("Icons/Hero.png"); // 无 Resources 段
// => "Icons/Hero"（仅剥离扩展名）

// 文件名净化
string safe = FramePathUtility.SanitizeFileName("save:2026/06/15?.json");
// => 非法字符（: / ? 等）被替换为 '_'
```

### 设计意图与踩坑点

- `DisposableAction` 构造**不做 null 校验**，传入 null 时 `Dispose` 是安全的空操作（局部副本为 null 即返回）。
- `DisposableAction.Dispose` 的「先置 null 再执行」保证回调**至多执行一次**；不要依赖它执行多次。
- `NormalizeResourcesPath` 会**剥离扩展名**：`Path.GetExtension` 识别到的扩展名（含点）被去除——若资源名本身含点（如版本号 `config.v2`），`.v2` 会被误当作扩展名剥掉，需谨慎。
- `NormalizeResourcesPath` 的 "/Resources/" 查找**忽略大小写**（`OrdinalIgnoreCase`），且要求路径中存在带前后斜杠的完整 token；若路径以 "Resources/" 开头但前面没有斜杠，则不会被截断（不匹配 "/Resources/"）。
- `NormalizeResourcesPath` 把反斜杠统一为正斜杠并 `Trim`，适配 Windows/编辑器路径混用的场景；空白输入返回 `string.Empty` 而非 null。
- `SanitizeFileName` 对空白返回固定字符串 `"default"`；非法字符集合来自 `Path.GetInvalidFileNameChars()`（平台相关），仅替换文件名非法字符，**不处理**路径分隔符以外的目录语义，也不限制长度。

## 13. Assets 模块

Assets 模块为框架提供统一的资源加载抽象。所有上层逻辑通过 `IAssetService` 接口加载、实例化与释放资源，而不直接依赖某个具体的资源管线。框架内置三种后端实现：默认的 `ResourcesAssetService`（基于 Unity 的 `Resources` API，位于 Runtime 程序集 `Frame.Assets` 命名空间），以及两个集成层实现 `AddressablesAssetService`（`Frame.Addressables` 程序集）与 `YooAssetAssetService`（`Frame.YooAsset` 程序集）。后两者在另一篇文档中详述，本文聚焦运行时核心类型与枚举，但会在「流转逻辑」中引用三种后端的统一流程。

三种后端共享同一套语义：

- **引用计数（reference counting）**：每次成功加载某个 `path` 都会令该路径的引用计数 +1，每次 `Release` 都会 -1，归零时才真正从缓存移除并卸载底层句柄。`ResourcesAssetService` 用一对 `Dictionary`（`cache`/`refCounts`）实现，Addressables/YooAsset 用每个条目内置的 `RefCount` 字段实现。
- **异步请求（async requests）**：`LoadAsync<T>` 返回 `AssetRequest<T>`，它继承自 Unity 的 `CustomYieldInstruction`，可在协程中 `yield return`；内部由 UniTask 驱动（`UniTaskVoid` + `Forget()`）。
- **实例租约（instance leases）**：`Instantiate` 会在生成出来的 `GameObject` 上挂一个 `AssetInstanceLease` 组件，把对应的 `AssetHandle`（实现了 `IDisposable`）绑定到该组件；当实例被销毁时自动调用 `Dispose`，把引用计数归还后端，避免依赖资源被过早卸载。

`AssetHandle<T>` 的构造函数是 `internal` 的；Runtime 程序集通过 `Frame.Runtime/Core/AssemblyInfo.cs` 中的 `[assembly: InternalsVisibleTo("Frame.Addressables")]` 与 `[assembly: InternalsVisibleTo("Frame.YooAsset")]` 把内部成员开放给两个集成程序集，使集成层能够构造框架句柄而无须把构造函数公开。每个后端都是一个继承 `GameModuleBase` 的模块，在 `OnInitialize` 中把自身注册到 `Context.Services`（既注册为 `IAssetService` 接口，也注册为具体类型）。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IAssetService`（接口） | 资源服务统一抽象，定义同步/异步加载、实例化、引用计数查询、释放与卸载等能力 | 三种后端共同实现；上层只依赖此接口 |
| `ResourcesAssetService`（密封类） | 基于 `UnityEngine.Resources` 的默认后端，同时是 `GameModuleBase` 模块 | `Priority = -600`；用 `cache` + `refCounts` 两个 `Dictionary<string,…>` 管理；缓存键只为路径 |
| `AssetHandle<T>`（密封泛型类） | 一次加载结果的句柄，持有资源引用与归属服务，负责释放 | 实现 `IDisposable`；构造函数 `internal`；`IsValid => Asset != null`；二次释放保护 |
| `AssetRequest<T>`（密封泛型类） | 异步加载请求对象，可在协程中等待，携带进度/错误/结果 | 继承 `CustomYieldInstruction`；`keepWaiting => !IsDone`；`Cancel` 协作式取消 |
| `AssetReference<T>`（可序列化结构） | 可在 Inspector 中序列化的资源引用（保存 Resources 路径），封装加载入口 | `[System.Serializable]`；构造与 getter 都做路径归一化 |
| `AssetStats`（可序列化类） | 诊断快照数据，描述某条已加载资源的路径/类型/引用计数/加载状态 | `[Serializable]`；纯数据 DTO，公共字段 |
| `AssetServiceBackend`（枚举） | 标识使用哪种资源后端：`Resources` / `Addressables` / `YooAsset` | 数值 0/1/2，供配置选择后端 |
| `AssetInstanceLease`（密封 MonoBehaviour） | 实例租约组件，把句柄生命周期绑定到 `GameObject`，销毁时自动释放 | `[DisallowMultipleComponent]`；持有一个 `IDisposable` |
| `YooAssetPlayMode`（枚举） | YooAsset 运行模式：编辑器模拟 / 离线 / 联机 / Web | 数值 0/1/2/3；仅 YooAsset 后端使用 |

---

### `IAssetService.cs`

`namespace Frame.Assets` 中的资源服务接口，所有后端必须实现。`Object` 为 `UnityEngine.Object` 的别名（`using Object = UnityEngine.Object;`）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `AssetHandle<T> Load<T>(string path) where T : Object` | 同步加载指定路径的资源，返回句柄 | 失败时返回 `Asset == null` 的句柄（`IsValid` 为 false），不抛异常；成功会令引用计数 +1 |
| `bool TryLoad<T>(string path, out AssetHandle<T> handle) where T : Object` | 静默尝试加载指定路径的资源 | 成功返回有效 handle 并令引用计数 +1；失败返回 false 且不打印缺失资源 warning，适合 provider fallback 链路 |
| `AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object` | 异步加载资源，返回可在协程中 `yield return` 的请求；可选完成回调 | 缓存命中时同步完成；未命中时启动 UniTask 任务异步加载；`completed` 在完成时被调用 |
| `GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)` | 加载并实例化 `GameObject`，自动绑定实例租约 | 失败返回 `null`；成功会在实例上挂 `AssetInstanceLease` |
| `bool IsLoaded(string path)` | 查询某路径是否已加载且资源未被销毁 | 内部对 `path` 做归一化后查缓存 |
| `int GetReferenceCount(string path)` | 查询某路径当前引用计数 | 未加载返回 0 |
| `List<AssetStats> GetLoadedAssetStats()` | 返回所有已加载资源的诊断快照列表 | 列表按 `Path` 序数排序（`string.CompareOrdinal`） |
| `void Release(string path)` | 对某路径释放一次引用，归零时真正卸载 | 与每次成功加载配对调用 |
| `void ReleaseAll()` | 释放并清空所有已加载资源 | 用于场景切换/重置 |
| `void UnloadUnusedAssets()` | 触发底层卸载未使用的资源 | Resources 后端直接调用 `Resources.UnloadUnusedAssets()` |

---

### `ResourcesAssetService.cs`

`public sealed class ResourcesAssetService : GameModuleBase, IAssetService`，位于 `namespace Frame.Assets`。默认资源后端，依赖 `Cysharp.Threading.Tasks`（UniTask）、`Frame.Core`、`Frame.Utilities`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<string, Object> cache` | 路径→资源对象缓存 | 键为归一化后的 Resources 路径；值为 `UnityEngine.Object` |
| `private readonly Dictionary<string, int> refCounts` | 路径→引用计数 | 键与 `cache` 同步；归零时移除键 |
| `public override int Priority { get { return -600; } }` | 模块优先级 | 固定返回 `-600`，使资源服务很早初始化 |
| `protected override void OnInitialize()` | 模块初始化 | 执行 `Context.Services.Register<IAssetService>(this);` 与 `Context.Services.Register(this);` 两次注册（接口 + 具体类型） |
| `public AssetHandle<T> Load<T>(string path)` | 同步加载 | 见下方流程；路径归一化→空判断→缓存命中判断→`Resources.Load<T>`→类型校验→`AddRef`→返回句柄。失败均返回 `new AssetHandle<T>(this, path, null)` |
| `public bool TryLoad<T>(string path, out AssetHandle<T> handle)` | 静默同步尝试加载 | 复用内部加载流程但不打印缺失资源 warning；成功同样 `AddRef` 并返回 true，失败返回 false |
| `public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null)` | 异步加载 | 先归一化路径并 `new AssetRequest<T>()`；缓存命中（且 `cached != null`）时做类型校验后 `AddRef` 并通过 `CompleteRequest` 同步完成；未命中则 `LoadAsyncTask(path, request, completed).Forget();` 后立即返回 request |
| `public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)` | 加载并实例化预制体 | `Load<GameObject>` →`!handle.IsValid` 返回 `null`；`Object.Instantiate`；若实例为 `null` 则 `handle.Release()` 返回 `null`；否则 `GetComponent`/`AddComponent<AssetInstanceLease>` 后 `lease.Bind(handle)` |
| `public bool IsLoaded(string path)` | 是否已加载 | 归一化后 `cache.TryGetValue` 且 `asset != null` |
| `public int GetReferenceCount(string path)` | 引用计数查询 | 归一化后查 `refCounts`，无键返回 0 |
| `public List<AssetStats> GetLoadedAssetStats()` | 诊断快照 | 遍历 `cache`，跳过 `asset == null` 的项；从 `refCounts` 取计数（无键则为 0）；填充 `Path`/`TypeName`(=`asset.GetType().Name`)/`ReferenceCount`/`IsLoaded=true`；最后按 `Path` 序数排序 |
| `public void Release(string path)` | 释放一次引用 | 归一化后若 `refCounts` 无键直接返回；否则 `count--`，`count <= 0` 时同时 `refCounts.Remove` 与 `cache.Remove`，否则写回 `refCounts[path]=count` |
| `public void ReleaseAll()` | 释放全部 | `refCounts.Clear()` + `cache.Clear()`（不调用 `UnloadUnusedAssets`） |
| `public void UnloadUnusedAssets()` | 卸载未使用资源 | 直接 `Resources.UnloadUnusedAssets()` |
| `protected override void OnShutdown()` | 模块关闭 | `cache.Clear()` + `refCounts.Clear()` + `Resources.UnloadUnusedAssets()` |
| `private async UniTaskVoid LoadAsyncTask<T>(string path, AssetRequest<T> request, Action<AssetHandle<T>> completed)` | 异步加载协程主体 | 见下方异步流程；空路径直接以 `"Resources path is empty."` 完成；`await UniTask.Yield(PlayerLoopTiming.Update)`；多处 `request.IsCanceled` 检查→以 `"Request canceled."` 完成；`Resources.LoadAsync<T>` 轮询 `isDone` 并 `SetProgress(resourceRequest.progress)`；成功则 `cache[path]=asset` + `AddRef(path)`，失败 error=`"Resources asset not found: " + path` |
| `private static void CompleteRequest<T>(AssetRequest<T> request, AssetHandle<T> handle, Action<AssetHandle<T>> completed, string error = null)` | 完成请求并触发回调 | `request.Complete(handle, error)`；若 `completed != null` 则 `try { completed(handle); } catch (Exception exception) { FrameLog.Exception(exception); }`——回调异常被吞掉并记录，不影响请求本身 |
| `private void AddRef(string path)` | 引用计数 +1 | `refCounts.TryGetValue(path, out count)` 后 `refCounts[path] = count + 1`（无键时 `count` 为 0，结果为 1） |

精确日志字符串：

- 空路径：`FrameLog.Warning("Resources path is empty.")`
- 同步未找到：`FrameLog.Warning("Resources asset not found: " + path + " type=" + typeof(T).Name)`
- 同步类型不匹配：`FrameLog.Warning("Resources asset type mismatch: " + path + " expected=" + typeof(T).Name)`
- 异步缓存命中但类型不匹配：`FrameLog.Warning("Resources asset type mismatch async: " + path + " expected=" + typeof(T).Name)`（request 的 error 为 `"Resources asset type mismatch: " + path`）
- 异步未找到：`FrameLog.Warning("Resources asset not found async: " + path + " type=" + typeof(T).Name)`（request 的 error 为 `"Resources asset not found: " + path`）
- 取消：request 的 error 为 `"Request canceled."`

---

### `AssetHandle.cs`

`public sealed class AssetHandle<T> : IDisposable where T : Object`，位于 `namespace Frame.Assets`。一次加载结果的轻量句柄，持有资源与归属服务的引用。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private IAssetService owner` | 归属的资源服务 | 释放后置 `null`，作为「已释放」标记 |
| `internal AssetHandle(IAssetService owner, string path, T asset)` | 构造函数 | **internal**：仅 Runtime 程序集与通过 `InternalsVisibleTo` 授权的 `Frame.Addressables`/`Frame.YooAsset` 可构造；赋值 `owner`/`Path`/`Asset` |
| `public string Path { get; private set; }` | 资源路径（归一化后） | 由构造函数设置 |
| `public T Asset { get; private set; }` | 资源对象 | 加载失败时为 `null` |
| `public bool IsValid { get { return Asset != null; } }` | 句柄是否有效 | 仅判断 `Asset != null` |
| `public void Release()` | 释放句柄 | 直接调用 `Dispose()` |
| `public void Dispose()` | 释放并归还引用计数 | 取本地变量 `service = owner`；若 `service == null` 或 `Asset == null` 或 `Path` 空白，则把 `owner = null` 后直接返回（不调用 Release）；否则先 `owner = null` 再 `service.Release(Path)`。**双重释放保护**：首次释放后 `owner` 已为 `null`，再次调用会在前置判断中直接返回，不会重复 `Release` |

---

### `AssetRequest.cs`

`public sealed class AssetRequest<T> : CustomYieldInstruction where T : Object`，位于 `namespace Frame.Assets`。异步加载请求对象。因继承 Unity 的 `CustomYieldInstruction`，可在协程里 `yield return request;` 等待完成。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public override bool keepWaiting { get { return !IsDone; } }` | 协程是否继续等待 | 完成前 `IsDone == false` → `keepWaiting == true`；完成后 `keepWaiting == false`，协程恢复 |
| `public bool IsDone { get; private set; }` | 是否已完成 | 仅由内部 `Complete` 置 true |
| `public bool IsCanceled { get; private set; }` | 是否被取消 | 由 `Cancel()` 置 true |
| `public bool Success { get { return Handle != null && Handle.IsValid; } }` | 是否成功 | 需 `Handle` 非空且 `Handle.IsValid`（即资源非空） |
| `public float Progress { get; private set; }` | 加载进度 [0,1] | 由内部 `SetProgress` 写入，`Complete` 时置 `1f` |
| `public string Error { get; private set; }` | 错误信息 | 成功为 `null`；失败/取消为对应错误串 |
| `public AssetHandle<T> Handle { get; private set; }` | 完成后的句柄 | 由 `Complete` 写入 |
| `public T Asset { get { return Handle == null ? null : Handle.Asset; } }` | 便捷访问资源 | `Handle` 为空时返回 `null` |
| `public void Cancel()` | 协作式取消 | 若 `IsDone` 直接返回；否则置 `IsCanceled = true`。注意：仅设置标志，真正中止由后端的加载任务在下一次 `IsCanceled` 检查时执行 |
| `internal void SetProgress(float progress)` | 设置进度 | `Progress = Mathf.Clamp01(progress)`，钳制到 [0,1]。**internal**，仅后端调用 |
| `internal void Complete(AssetHandle<T> handle, string error = null)` | 标记完成 | 顺序：`Handle = handle; Error = error; Progress = 1f; IsDone = true;`。**internal**，仅后端调用 |

---

### `AssetReference.cs`

`[System.Serializable] public struct AssetReference<T> where T : Object`，位于 `namespace Frame.Assets`。可在 Inspector 序列化保存的资源引用（保存 Resources 路径）。依赖 `Frame.Utilities`（`FramePathUtility`）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `[SerializeField] private string resourcesPath` | 序列化保存的原始路径 | 私有字段，受 Unity 序列化 |
| `public AssetReference(string resourcesPath)` | 构造函数 | 存入时即 `FramePathUtility.NormalizeResourcesPath(resourcesPath)` 归一化 |
| `public string ResourcesPath { get { return FramePathUtility.NormalizeResourcesPath(resourcesPath); } }` | 取归一化路径 | **每次 getter 都重新归一化**，因此即使 Inspector 里手填了带扩展名/带 `/Resources/` 前缀的值也能正确处理 |
| `public bool IsValid { get { return !string.IsNullOrWhiteSpace(ResourcesPath); } }` | 是否有有效路径 | 基于归一化后的路径判空 |
| `public AssetHandle<T> Load(IAssetService assetService)` | 同步加载 | 转发 `assetService.Load<T>(ResourcesPath)` |
| `public AssetRequest<T> LoadAsync(IAssetService assetService, Action<AssetHandle<T>> completed = null)` | 异步加载 | 转发 `assetService.LoadAsync(ResourcesPath, completed)` |
| `public override string ToString()` | 字符串表示 | 返回 `ResourcesPath`（归一化后） |

---

### `AssetStats.cs`

`[Serializable] public sealed class AssetStats`，位于 `namespace Frame.Assets`。诊断用的纯数据快照（DTO），由 `GetLoadedAssetStats()` 填充。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public string Path` | 资源路径 | 公共字段；填的是缓存键（归一化路径） |
| `public string TypeName` | 资源运行时类型名 | 来自 `asset.GetType().Name` |
| `public int ReferenceCount` | 当前引用计数 | 来自后端引用计数表 |
| `public bool IsLoaded` | 是否已加载 | 快照生成时恒为 `true`（仅收集已加载项） |

---

### `AssetServiceBackend.cs`

`public enum AssetServiceBackend`，位于 `namespace Frame.Assets`。用于配置选择资源后端。

| 值 | 数值 | 含义 |
| --- | --- | --- |
| `Resources` | `0` | 使用 `ResourcesAssetService`（默认，基于 Unity `Resources`） |
| `Addressables` | `1` | 使用 `AddressablesAssetService`（Addressables 集成） |
| `YooAsset` | `2` | 使用 `YooAssetAssetService`（YooAsset 集成） |

---

### `AssetInstanceLease.cs`

`[DisallowMultipleComponent] public sealed class AssetInstanceLease : MonoBehaviour`，位于 `namespace Frame.Assets`。挂在实例化出来的 `GameObject` 上，把句柄（`IDisposable`）生命周期绑定到该对象，销毁时自动释放。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private IDisposable lease` | 持有的可释放对象 | 通常是 `AssetHandle<GameObject>` |
| `public void Bind(IDisposable disposable)` | 绑定/替换租约 | 若 `ReferenceEquals(lease, disposable)` 则直接返回（重复绑定同一对象无副作用）；否则记下 `previous = lease`，写入新 `lease = disposable`，再 `previous?.Dispose()`——替换旧租约会释放旧句柄 |
| `private void OnDestroy()` | 对象销毁回调 | 取 `disposable = lease`，先置 `lease = null`，再 `disposable?.Dispose()`——实例被销毁时自动归还引用计数 |

---

### `YooAssetPlayMode.cs`

`public enum YooAssetPlayMode`，位于 `namespace Frame.Assets`。仅 YooAsset 后端用于选择运行模式。

| 值 | 数值 | 含义 |
| --- | --- | --- |
| `EditorSimulate` | `0` | 编辑器模拟模式（仅编辑器有效；运行时构建会回退到 Offline 并告警 `"YooAsset EditorSimulate mode is editor-only. Falling back to Offline mode."`） |
| `Offline` | `1` | 离线模式（仅使用内置文件系统，不联网下载） |
| `Host` | `2` | 联机/主机模式（带远端服务器与缓存沙盒文件系统，支持下载） |
| `Web` | `3` | Web 模式（WebGL 等，使用 Web 文件系统） |

---

### 流转逻辑

下文以 `ResourcesAssetService` 为主线，Addressables/YooAsset 在对应步骤的差异以括注说明。

#### 1. 同步加载 `Load<T>`

1. **路径归一化**：`path = FramePathUtility.NormalizeResourcesPath(path)`。归一化将反斜杠转正斜杠、`Trim`、去掉扩展名，并把 `/Resources/`（大小写不敏感）之前的部分截掉（只保留 Resources 之后的相对路径）。空路径归一化为 `string.Empty`。（Addressables/YooAsset 只做 `Replace('\\','/').Trim()`，不裁剪扩展名或 Resources 前缀。）
2. **空判断**：若归一化后路径为空白，`FrameLog.Warning("Resources path is empty.")` 并返回 `new AssetHandle<T>(this, path, null)`（无效句柄，**不**增加引用计数）。
3. **缓存检查**：`cache.TryGetValue(path, out asset)`。若未命中或 `asset == null`（资源被销毁），则真正加载：`asset = Resources.Load<T>(path)`。
   - 若加载返回 `null`：`FrameLog.Warning("Resources asset not found: " + path + " type=" + typeof(T).Name)`，返回无效句柄（不计数）。
   - 否则写入 `cache[path] = asset`。
4. **类型校验**：`T typedAsset = asset as T`。若 `typedAsset == null`（缓存中存的对象与请求类型不符），`FrameLog.Warning("Resources asset type mismatch: " + path + " expected=" + typeof(T).Name)`，返回无效句柄（**不**计数）。注意：缓存键只含路径不含类型，因此用错类型请求同一路径会触发此分支。
5. **引用 +1**：`AddRef(path)`（`refCounts[path] = count + 1`）。
6. **返回句柄**：`new AssetHandle<T>(this, path, typedAsset)`，`IsValid == true`。

#### 2. 异步加载 `LoadAsync<T>`

1. 先 `path = FramePathUtility.NormalizeResourcesPath(path)`，再 `new AssetRequest<T>()`。
2. **缓存命中即同步完成**：`cache.TryGetValue(path, out cached) && cached != null` 时：
   - 类型不符：`FrameLog.Warning("Resources asset type mismatch async: …")`，构造无效句柄，`CompleteRequest(request, empty, completed, "Resources asset type mismatch: " + path)`，立即返回 request（**已 `IsDone`**）。
   - 类型相符：`AddRef(path)` → `CompleteRequest(request, handle, completed)`（无 error），立即返回 request。
   此时返回的 request 已经 `IsDone == true`，协程 `yield return` 会立刻继续（同步完成）。
3. **缓存未命中**：`LoadAsyncTask(path, request, completed).Forget();` 启动一个 `UniTaskVoid` 任务（`Forget()` 表示「即发即忘」，不 await 也不抛出未观察异常），随后立即返回尚未完成的 request。
4. **`LoadAsyncTask` 执行体**：
   - 空路径：以 error=`"Resources path is empty."` 完成后返回。
   - `await UniTask.Yield(PlayerLoopTiming.Update)` 让出一帧。让出后检查 `request.IsCanceled`，若已取消则以 error=`"Request canceled."` 完成后返回。
   - `ResourceRequest resourceRequest = Resources.LoadAsync<T>(path)`，进入 `while (!resourceRequest.isDone)` 轮询：每次循环 `request.SetProgress(resourceRequest.progress)` 更新进度；检查 `IsCanceled`（取消则以 `"Request canceled."` 完成返回）；再 `await UniTask.Yield(PlayerLoopTiming.Update)`。（Addressables 用 `operation.PercentComplete`，YooAsset 用 `yooHandle.Progress`。）
   - 循环结束后再次检查 `IsCanceled`（取消则完成返回）。
   - `T asset = resourceRequest.asset as T`：若为 `null` 则 error=`"Resources asset not found: " + path` 并告警；否则 `cache[path] = asset; AddRef(path);`。
   - 最后构造 `AssetHandle<T>(this, path, asset)` 并 `CompleteRequest(request, handle, completed, error)`。
5. **`CompleteRequest` 回调机制**：先 `request.Complete(handle, error)`（设置 Handle/Error，Progress=1，IsDone=true），随后若 `completed != null` 则在 `try/catch` 中调用 `completed(handle)`，回调里抛出的异常被 `FrameLog.Exception` 记录后吞掉，不影响 request 状态。

**取消的协作式语义**：`request.Cancel()` 仅在 `!IsDone` 时把 `IsCanceled` 置 true；真正的中止发生在 `LoadAsyncTask` 的多个检查点（让出后、轮询每帧、循环结束后）。因此取消不是立即的，最坏需等到下一帧任务恢复执行才生效，且取消后资源**不会**被加入缓存、引用计数**不会**增加。Addressables/YooAsset 在取消分支还会额外 `ReleaseOperation`/`ReleaseYooHandle` 把已开始的底层句柄释放掉。

#### 3. 释放流程 `Release`

1. 用户对 `AssetHandle` 调 `Release()` 或 `Dispose()`（`Release()` 内部即 `Dispose()`）。
2. `Dispose()` 取 `service = owner`；若 `service`/`Asset`/`Path` 任一无效则把 `owner` 置 `null` 后返回（**不**调用 `Release`，避免对无效句柄归还计数）；否则先把 `owner = null` 再 `service.Release(Path)`。
3. `ResourcesAssetService.Release(path)`：归一化路径→若 `refCounts` 无该键直接返回→`count--`→`count <= 0` 时 `refCounts.Remove(path)` 且 `cache.Remove(path)`（真正卸载缓存），否则 `refCounts[path] = count` 写回。
4. **双重释放保护**：句柄首次释放后 `owner` 已为 `null`，二次 `Dispose` 会命中前置判断直接返回，不会重复扣减引用计数。

#### 4. 实例化流程 `Instantiate`

1. `Load<GameObject>(path)`：同步加载预制体（引用计数 +1）。
2. 若 `!handle.IsValid` 直接返回 `null`（此分支下 `Load` 已是失败路径，未计数）。
3. `Object.Instantiate(handle.Asset, parent, worldPositionStays)` 生成实例。
4. 若实例为 `null`：`handle.Release()` 回滚引用计数，返回 `null`。
5. 取/挂 `AssetInstanceLease`：`instance.GetComponent<AssetInstanceLease>()`，没有则 `AddComponent`。
6. `lease.Bind(handle)`：把句柄绑定到组件。
7. 实例被 `Destroy` 时，`AssetInstanceLease.OnDestroy` 调用 `handle.Dispose()`，自动 `Release` 归还引用计数——从而预制体（及 Addressables/YooAsset 的依赖 bundle）在所有实例销毁后才被卸载。

#### 5. 引用计数语义与诊断快照

- 每次成功 `Load`/缓存命中的 `LoadAsync`/`Instantiate` 都使引用计数 +1；每次 `Release`/句柄 `Dispose`/实例销毁使其 -1；归零时移除缓存条目。`Instantiate` 出的每个实例各持一份引用，互不影响。
- `GetReferenceCount(path)` 归一化后查 `refCounts`，未加载返回 0。
- `GetLoadedAssetStats()` 遍历缓存，跳过空资源，逐项产出 `AssetStats { Path, TypeName=运行时类型名, ReferenceCount=当前计数, IsLoaded=true }`，并按 `Path` 序数排序返回，便于在调试面板中稳定展示。
- `ReleaseAll()` 一次清空全部缓存与计数（Addressables/YooAsset 还会逐条释放底层句柄）；`UnloadUnusedAssets()` 触发底层卸载（Resources 直接调 `Resources.UnloadUnusedAssets()`，YooAsset 还会先 `package.UnloadUnusedAssetsAsync().WaitForCompletion()`）。

---

### 使用示例

同步加载（建议用 `using` 块自动释放，离开作用域即 `Dispose` 归还计数）：

```csharp
IAssetService assets = Context.Services.Get<IAssetService>();

using (AssetHandle<Texture2D> handle = assets.Load<Texture2D>("UI/Icons/coin"))
{
    if (handle.IsValid)
    {
        Texture2D tex = handle.Asset;
        // 使用 tex …
    }
} // 离开 using 自动 Dispose → Release，计数 -1
```

异步加载（在协程中 `yield return` 等待请求完成）：

```csharp
IEnumerator LoadRoutine(IAssetService assets)
{
    AssetRequest<GameObject> request = assets.LoadAsync<GameObject>("Prefabs/Enemy");
    yield return request; // keepWaiting => !IsDone

    if (request.Success)
    {
        GameObject prefab = request.Asset;       // == request.Handle.Asset
        // …
        request.Handle.Release();                 // 用完归还计数
    }
    else
    {
        Debug.LogWarning("加载失败: " + request.Error);
    }
}
```

带完成回调的异步加载：

```csharp
assets.LoadAsync<AudioClip>("Audio/bgm", handle =>
{
    if (handle.IsValid) { /* 播放 handle.Asset */ }
    // 回调内抛异常会被框架 try/catch 并记录，不会中断请求
});
```

通过 `AssetReference<T>`（可在 Inspector 中配置路径）加载：

```csharp
[SerializeField] private AssetReference<Sprite> iconRef;

void Use(IAssetService assets)
{
    if (iconRef.IsValid)
    {
        AssetHandle<Sprite> handle = iconRef.Load(assets);
        // 或 iconRef.LoadAsync(assets, h => { … });
    }
}
```

取消进行中的异步请求：

```csharp
AssetRequest<GameObject> request = assets.LoadAsync<GameObject>("Prefabs/Boss");
// …某些条件下放弃加载
request.Cancel(); // 协作式：下一帧任务检查点生效，资源不入缓存、计数不增加
```

诊断 API：

```csharp
int n = assets.GetReferenceCount("Prefabs/Enemy");      // 当前引用计数
List<AssetStats> stats = assets.GetLoadedAssetStats();   // 已加载资源快照（按 Path 排序）
foreach (AssetStats s in stats)
    Debug.Log($"{s.Path} [{s.TypeName}] refs={s.ReferenceCount} loaded={s.IsLoaded}");

assets.UnloadUnusedAssets();  // 触发底层卸载未引用资源
assets.ReleaseAll();          // 清空全部缓存与计数（如场景切换时）
```

实例化（自动租约，销毁即释放）：

```csharp
GameObject enemy = assets.Instantiate("Prefabs/Enemy", transform);
// enemy 上已挂 AssetInstanceLease；Destroy(enemy) 时自动 Release，无需手动管理句柄
```

---

### 设计意图与踩坑点

- **缓存键只含路径、不含类型**：`cache`/`refCounts` 都以归一化路径为键。若先以类型 A 加载某路径、后以不兼容的类型 B 请求同一路径，会命中缓存里的 A 对象但 `as B` 失败，落入「type mismatch」分支返回无效句柄且**不增加计数**——此时 B 的调用方拿不到资源，但 A 的引用计数也不会被这次失败请求影响。务必保证同一路径在全局使用一致的资源类型。
- **类型不匹配/未找到不抛异常**：所有失败路径都返回 `IsValid == false` 的句柄（异步则 request 的 `Success == false` 且 `Error` 非空），调用方必须显式检查 `IsValid`/`Success`，否则会拿到 `null` 的 `Asset`。
- **实例租约存在的意义**：`AssetInstanceLease` 把句柄生命周期绑定到实例 `GameObject`。对 Addressables/YooAsset 而言，资源句柄对应底层 bundle/依赖，过早 `Release` 会卸载依赖导致实例材质/网格丢失（变粉红/不可见）；租约确保实例存活期间引用计数始终 ≥1，销毁时才归还，从根本上避免「实例还在用、依赖已被卸载」的悬挂问题。`Bind` 同一对象幂等、替换旧租约会释放旧句柄；`[DisallowMultipleComponent]` 保证一个实例只有一个租约。
- **`NormalizeResourcesPath` 归一化规则**：反斜杠→正斜杠、`Trim`、去扩展名、截掉 `/Resources/`（大小写不敏感）之前的部分。因此 `"Assets/Game/Resources/UI/coin.png"`、`"UI/coin"`、`"ui\\coin"` 等都会归一化到 `"UI/coin"`（注意大小写仍区分，`Resources.Load` 本身对大小写敏感）。`AssetReference<T>` 的 getter 每次都重新归一化，所以即使序列化里存了未归一化的值也安全。Addressables/YooAsset 的路径**不**走此归一化（只做斜杠替换与 Trim），其地址/location 需按各自管线的约定填写。
- **何时不该用 Resources 后端**：Unity 的 `Resources` 目录会全部打进包体、增大首包体积并拖慢启动（构建期需扫描整目录），且不支持热更/远端下载、不便做按需加载与依赖管理。对中大型项目或需要热更的场景，应切换到 `Addressables` 或 `YooAsset` 后端（通过 `AssetServiceBackend` 配置）；`Resources` 后端定位为默认/兜底与小型/原型项目使用。
- **取消是协作式、不是抢占式**：`Cancel()` 只置标志，已发起的 `Resources.LoadAsync`/Addressables/YooAsset 操作要等任务恢复到检查点才真正中止；取消后不会写缓存、不会增加计数，集成后端还会释放已开始的底层句柄。
- **句柄释放要与加载配对**：除 `Instantiate`（由租约托管）外，每次成功 `Load`/缓存命中的 `LoadAsync` 都使计数 +1，必须有对应的 `handle.Release()`/`Dispose()`（推荐 `using`），否则资源永不卸载造成泄漏。`AssetHandle.Dispose` 自带双重释放保护，重复调用安全。

## 14. Scenes 模块

Scenes 模块封装了 Unity `UnityEngine.SceneManagement` 的同步/异步场景加载、卸载、激活与构建设置校验。核心服务 `SceneService` 继承自 `GameModuleBase` 并实现 `ISceneService`，`Priority` 固定返回 `-500`（数值越小越早初始化，因此它属于较早初始化的模块）。模块在 `OnInitialize` 中把自身分别以 `ISceneService` 接口和具体类型注册进 `Context.Services`，不创建任何 GameObject、不读取 `Context.Settings`。异步加载使用 UniTask（`UniTaskVoid` + `PlayerLoopTiming.Update`）逐帧驱动进度，并通过 `SceneLoadOperation`（继承 `CustomYieldInstruction`）把 Unity 的 `AsyncOperation` 包装成可被 `yield return`、可手动激活的句柄。所有对外事件回调与用户回调都用 try/catch 隔离异常（统一交给 `FrameLog.Exception`），保证单个监听器抛错不会中断加载流程。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ISceneService` | 场景服务对外接口 | 暴露 3 个事件、3 个只读属性、6 个方法；同步 `Load` 默认 `LoadSceneMode.Single` + `validateInBuildSettings=true` |
| `SceneService` | `ISceneService` 的唯一实现，`GameModuleBase` 模块 | `Priority = -500`；维护 `activeOperations` 列表与 `CurrentOperation`；异步加载基于 UniTask 逐帧追踪 |
| `SceneLoadArgs` | 异步加载参数对象（`[Serializable]`、`sealed`） | 公开字段含默认值：`Mode=Single`、`ActivateOnLoad=true`、`AllowConcurrentLoads=false`、`ValidateInBuildSettings=true`、`SetActiveOnComplete=false`，以及 `Progress`/`Completed` 回调 |
| `SceneLoadOperation` | 包装 `AsyncOperation` 的加载句柄（继承 `CustomYieldInstruction`） | 可 `yield return`；`NormalizedProgress` 用 `progress/0.9f` 归一化；`IsReadyToActivate` / `Activate()` 支持手动激活 |

### `ISceneService.cs`

接口定义，命名空间 `Frame.Scenes`，引用 `System`、`UnityEngine`、`UnityEngine.SceneManagement`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action<SceneLoadOperation> LoadStarted` | 异步加载启动时触发 | 在 `LoadAsync` 内、`TrackLoadAsync` 启动前发布，参数为包装好的 `SceneLoadOperation` |
| `event Action<SceneLoadOperation, float> LoadProgress` | 加载进度变化时触发 | 由 `TrackLoadAsync` 每帧调用 `NotifyProgress` 发布，progress 为归一化值（最后一次为 `1f`） |
| `event Action<SceneLoadOperation> LoadCompleted` | 加载完成时触发 | 由 `TrackLoadAsync` 在完成后调用 `NotifyCompleted` 发布 |
| `Scene ActiveScene { get; }` | 当前激活场景 | 实现直接返回 `SceneManager.GetActiveScene()` |
| `bool IsLoading { get; }` | 是否有异步加载在进行 | 实现会先调用 `RemoveCompletedOperations()` 清理已完成项，再判断 `activeOperations.Count > 0` |
| `SceneLoadOperation CurrentOperation { get; }` | 最近一次/当前的加载操作 | 实现为 `{ get; private set; }`，完成后会回退到列表末尾项或置空 |
| `void Load(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, bool validateInBuildSettings = true)` | 同步加载场景 | 默认单场景模式、默认校验构建设置 |
| `SceneLoadOperation LoadAsync(SceneLoadArgs args)` | 异步加载场景 | 返回可等待/可手动激活的操作句柄 |
| `AsyncOperation UnloadAsync(string sceneName)` | 异步卸载场景 | 名称空或场景未加载时返回 `null` |
| `bool IsSceneLoaded(string sceneName)` | 判断场景是否已加载 | 支持名称或路径匹配（大小写不敏感） |
| `bool IsSceneInBuildSettings(string sceneName)` | 判断场景是否在 Build Settings 中 | 同时按路径与文件名（去扩展名）匹配 |
| `bool SetActiveScene(string sceneName)` | 将已加载场景设为激活场景 | 找不到或未加载返回 `false` |

### `SceneService.cs`

`public sealed class SceneService : GameModuleBase, ISceneService`，命名空间 `Frame.Scenes`，引用 `Cysharp.Threading.Tasks`、`System`、`System.Collections.Generic`、`System.IO`、`Frame.Core`、`UnityEngine`、`UnityEngine.SceneManagement`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly List<SceneLoadOperation> activeOperations` | 跟踪进行中的加载操作 | 初始化为空 `List`；`LoadAsync` 添加、`TrackLoadAsync` 的 `finally` 移除、`RemoveCompletedOperations` 清理 |
| `public event Action<SceneLoadOperation> LoadStarted` | 见接口 | `OnShutdown` 中置 `null` |
| `public event Action<SceneLoadOperation, float> LoadProgress` | 见接口 | `OnShutdown` 中置 `null` |
| `public event Action<SceneLoadOperation> LoadCompleted` | 见接口 | `OnShutdown` 中置 `null` |
| `public override int Priority { get; }` | 模块初始化优先级 | 固定返回 `-500` |
| `public Scene ActiveScene { get; }` | 当前激活场景 | 返回 `SceneManager.GetActiveScene()` |
| `public bool IsLoading { get; }` | 是否正在加载 | 先 `RemoveCompletedOperations()`，再返回 `activeOperations.Count > 0`（有清理副作用的 getter） |
| `public SceneLoadOperation CurrentOperation { get; private set; }` | 当前操作 | 自动属性，私有 setter |
| `protected override void OnInitialize()` | 模块初始化 | 仅 `Context.Services.Register<ISceneService>(this)` 与 `Context.Services.Register(this)`；不建 GameObject、不读 Settings |
| `public void Load(string sceneName, LoadSceneMode mode = LoadSceneMode.Single, bool validateInBuildSettings = true)` | 同步加载 | 先 `ValidateSceneName` 再 `ValidateSceneCanLoad`，最后 `SceneManager.LoadScene(sceneName, mode)` |
| `public SceneLoadOperation LoadAsync(SceneLoadArgs args)` | 异步加载 | 见下方流转逻辑；返回包装后的 `SceneLoadOperation` |
| `public AsyncOperation UnloadAsync(string sceneName)` | 异步卸载 | 名称空 → 返回 `null`；`!IsSceneLoaded` → 返回 `null`；否则 `SceneManager.UnloadSceneAsync(sceneName)` |
| `public bool IsSceneLoaded(string sceneName)` | 场景是否已加载 | 名称空返回 `false`；遍历 `SceneManager.sceneCount`，用 `SceneMatches` 且 `scene.isLoaded` |
| `public bool IsSceneInBuildSettings(string sceneName)` | 是否在构建设置 | 名称空返回 `false`；遍历 `SceneManager.sceneCountInBuildSettings`，路径用 `NormalizeScenePath` 比较，名称用 `Path.GetFileNameWithoutExtension` 比较，均 `OrdinalIgnoreCase` |
| `public bool SetActiveScene(string sceneName)` | 设为激活场景 | 名称空返回 `false`；遍历已加载场景，匹配且 `isLoaded` 时 `SceneManager.SetActiveScene(scene)` |
| `protected override void OnShutdown()` | 模块关闭 | `activeOperations.Clear()`、`CurrentOperation = null`、三个事件全部置 `null` |
| `private async UniTaskVoid TrackLoadAsync(SceneLoadArgs args, SceneLoadOperation operation)` | 逐帧追踪加载 | 见下方流转逻辑；`try/catch(Exception)` → `FrameLog.Exception`，`finally` 中移除操作并回退 `CurrentOperation` |
| `private void NotifyProgress(SceneLoadArgs args, SceneLoadOperation operation, float progress)` | 通知进度 | 若 `args.Progress != null` 则 try/catch 调用之，随后 `PublishLoadProgress` |
| `private void NotifyCompleted(SceneLoadArgs args, SceneLoadOperation operation, Scene scene)` | 通知完成 | 若 `args.Completed != null` 则 try/catch 调用之（传入 `Scene`），随后 `PublishLoadCompleted` |
| `private void PublishLoadStarted(SceneLoadOperation operation)` | 发布 `LoadStarted` | handler 为空提前返回，否则 try/catch 调用 |
| `private void PublishLoadProgress(SceneLoadOperation operation, float progress)` | 发布 `LoadProgress` | handler 为空提前返回，否则 try/catch 调用 |
| `private void PublishLoadCompleted(SceneLoadOperation operation)` | 发布 `LoadCompleted` | handler 为空提前返回，否则 try/catch 调用 |
| `private void RemoveCompletedOperations()` | 清理已完成/空操作 | 倒序遍历，`activeOperations[i] == null \|\| activeOperations[i].IsDone` 时移除 |
| `private void ValidateSceneCanLoad(string sceneName, bool validateInBuildSettings)` | 校验可加载 | `validateInBuildSettings && !IsSceneInBuildSettings(sceneName)` 时抛 `FrameException("Scene is not included in Build Settings: " + sceneName)` |
| `private static void ValidateSceneName(string sceneName)` | 校验名称非空 | `string.IsNullOrWhiteSpace` 时抛 `FrameException("Scene name is empty.")` |
| `private static bool SceneMatches(Scene scene, string sceneName, string normalizedScenePath)` | 场景匹配 | `!scene.IsValid()` → `false`；名称 `OrdinalIgnoreCase` 相等 → `true`；否则比较归一化后的 `scene.path` |
| `private static string NormalizeScenePath(string sceneNameOrPath)` | 路径归一化 | 空白返回 `string.Empty`，否则把 `'\\'` 替换为 `'/'` |

异常消息字符串（精确）：
- 名称为空：`"Scene name is empty."`
- `LoadAsync` 参数缺失：`"SceneLoadArgs.SceneName is required."`（条件 `args == null || string.IsNullOrWhiteSpace(args.SceneName)`）
- 已有加载在进行：`"A scene load is already running: " + currentName`（`currentName` 取 `CurrentOperation?.SceneName`，为 `null` 时用 `"unknown"`）
- 启动失败：`"Failed to start scene load: " + args.SceneName`（`SceneManager.LoadSceneAsync` 返回 `null` 时）
- 不在构建设置：`"Scene is not included in Build Settings: " + sceneName`

### `SceneLoadArgs.cs`

`[Serializable] public sealed class SceneLoadArgs`，命名空间 `Frame.Scenes`，引用 `System`、`UnityEngine.SceneManagement`。全部为公开字段（无属性封装），适合在 Inspector 序列化或代码内联构造。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public string SceneName` | 目标场景名/路径 | 无默认值（`null`）；`LoadAsync` 中为空白时抛异常 |
| `public LoadSceneMode Mode = LoadSceneMode.Single` | 加载模式 | 默认 `LoadSceneMode.Single`，传给 `SceneManager.LoadSceneAsync` |
| `public bool ActivateOnLoad = true` | 加载完成后是否自动激活场景 | 默认 `true`；映射到 `AsyncOperation.allowSceneActivation`。设 `false` 可实现"加载到 90% 后等待手动激活" |
| `public bool AllowConcurrentLoads = false` | 是否允许并发加载 | 默认 `false`；为 `false` 且 `IsLoading` 时 `LoadAsync` 抛异常 |
| `public bool ValidateInBuildSettings = true` | 是否校验构建设置 | 默认 `true`；传给 `ValidateSceneCanLoad` |
| `public bool SetActiveOnComplete = false` | 完成后是否设为激活场景 | 默认 `false`；为 `true` 且 `loadedScene.IsValid() && isLoaded` 时由 `TrackLoadAsync` 调 `SceneManager.SetActiveScene` |
| `public Action<float> Progress` | 进度回调 | 可空；由 `NotifyProgress` 在 try/catch 内调用，参数为归一化进度 |
| `public Action<Scene> Completed` | 完成回调 | 可空；由 `NotifyCompleted` 在 try/catch 内调用，参数为加载到的 `Scene` |

### `SceneLoadOperation.cs`

`public sealed class SceneLoadOperation : CustomYieldInstruction`，命名空间 `Frame.Scenes`，引用 `UnityEngine`、`UnityEngine.SceneManagement`。因继承 `CustomYieldInstruction` 可直接 `yield return` 于协程。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly AsyncOperation operation` | 底层 Unity 异步操作 | 构造注入，可能为 `null`（多处 getter 对 `null` 做兜底） |
| `private readonly string sceneName` | 场景名 | 构造注入 |
| `public SceneLoadOperation(string sceneName, AsyncOperation operation)` | 构造函数 | 仅赋值字段 |
| `public string SceneName { get; }` | 场景名 | 返回 `sceneName` |
| `public override bool keepWaiting { get; }` | `CustomYieldInstruction` 协程等待条件 | `operation != null && !operation.isDone`，控制 `yield return` 何时结束 |
| `public float Progress { get; }` | 原始进度 | `operation == null ? 1f : operation.progress`（注意：`allowSceneActivation=false` 时上限约 `0.9`） |
| `public float NormalizedProgress { get; }` | 归一化进度 [0,1] | `operation == null` → `1f`；`operation.isDone` → `1f`；否则 `Mathf.Clamp01(operation.progress / 0.9f)`（魔数 `0.9f`，对应 Unity 异步加载在禁用激活时停在 0.9） |
| `public bool IsDone { get; }` | 是否完成 | `operation == null \|\| operation.isDone` |
| `public bool IsReadyToActivate { get; }` | 是否可手动激活 | `operation != null && !operation.allowSceneActivation && operation.progress >= 0.9f`（进度达 0.9 且尚未允许激活） |
| `public bool AllowSceneActivation { get; set; }` | 是否允许激活 | get：`operation == null \|\| operation.allowSceneActivation`；set：`operation != null` 时写入 `operation.allowSceneActivation` |
| `public Scene LoadedScene { get; }` | 加载到的场景 | 返回 `SceneManager.GetSceneByName(sceneName)` |
| `public AsyncOperation Operation { get; }` | 底层操作 | 直接暴露 `operation` |
| `public void Activate()` | 手动触发激活 | 等价于 `AllowSceneActivation = true` |

### 流转逻辑

同步加载 `Load(sceneName, mode, validateInBuildSettings)`：
1. `ValidateSceneName(sceneName)`：名称空白 → 抛 `FrameException("Scene name is empty.")`。
2. `ValidateSceneCanLoad(sceneName, validateInBuildSettings)`：当 `validateInBuildSettings=true` 且场景不在构建设置中 → 抛 `FrameException("Scene is not included in Build Settings: ...")`。
3. 调用 `SceneManager.LoadScene(sceneName, mode)`（阻塞式同步加载，无操作句柄、不发布事件）。

异步加载 `LoadAsync(args)`：
1. 参数校验：`args == null || string.IsNullOrWhiteSpace(args.SceneName)` → 抛 `FrameException("SceneLoadArgs.SceneName is required.")`。
2. 构建设置校验：`ValidateSceneCanLoad(args.SceneName, args.ValidateInBuildSettings)`。
3. 并发守卫：`!args.AllowConcurrentLoads && IsLoading`（注意 `IsLoading` 会先清理已完成操作）时，取 `CurrentOperation?.SceneName`（空则 `"unknown"`），抛 `FrameException("A scene load is already running: " + currentName)`。
4. 调用 `SceneManager.LoadSceneAsync(args.SceneName, args.Mode)`；返回 `null` → 抛 `FrameException("Failed to start scene load: ...")`。
5. 设置 `operation.allowSceneActivation = args.ActivateOnLoad`。
6. 包装：`new SceneLoadOperation(args.SceneName, operation)` → 加入 `activeOperations` → 设为 `CurrentOperation`。
7. 发布 `LoadStarted`（`PublishLoadStarted`）。
8. 启动逐帧追踪：`TrackLoadAsync(args, wrapped).Forget()`（UniTask，即发即忘）。
9. 返回 `wrapped`。

逐帧追踪 `TrackLoadAsync`（`async UniTaskVoid`）：
1. `while (!operation.IsDone)`：每帧 `NotifyProgress(args, operation, operation.NormalizedProgress)`，然后 `await UniTask.Yield(PlayerLoopTiming.Update)`。`NormalizedProgress = progress / 0.9f` 经 `Clamp01`。
2. 循环结束后再发一次 `NotifyProgress(args, operation, 1f)`（确保进度落到 1）。
3. 取 `operation.LoadedScene`；若 `args.SetActiveOnComplete && loadedScene.IsValid() && loadedScene.isLoaded` → `SceneManager.SetActiveScene(loadedScene)`。
4. `NotifyCompleted(args, operation, loadedScene)`（先 `args.Completed`，再发布 `LoadCompleted`）。
5. `catch (Exception)` → `FrameLog.Exception(exception)`（异常不外泄）。
6. `finally`：`activeOperations.Remove(operation)`；若被移除的是 `CurrentOperation`，则把 `CurrentOperation` 回退为列表末尾项（`activeOperations[Count-1]`），列表空则置 `null`。

手动激活：当 `args.ActivateOnLoad=false` 时，底层 `AsyncOperation.allowSceneActivation=false`，进度会停在约 `0.9`，`IsDone` 永远为 `false`，因此 `TrackLoadAsync` 的循环会持续。此时 `IsReadyToActivate`（`progress >= 0.9f && !allowSceneActivation`）为 `true`，业务可在合适时机调用 `operation.Activate()`（或 `AllowSceneActivation = true`）放行，进度才会跑到 1 并完成。

事件异常隔离：所有 `PublishLoadStarted/Progress/Completed` 与用户回调 `args.Progress/args.Completed` 均包在 try/catch 内，单个监听器抛错只会被 `FrameLog.Exception` 记录，不影响其余监听器与加载流程。

清理：`RemoveCompletedOperations` 倒序移除 `null` 或 `IsDone` 的操作，被 `IsLoading` getter 间接触发；`OnShutdown` 清空列表、置空 `CurrentOperation`、解绑全部事件。

### 使用示例

```csharp
using Frame.Core;
using Frame.Scenes;
using UnityEngine.SceneManagement;

public sealed class LevelLoader
{
    private readonly ISceneService scenes;

    public LevelLoader(ISceneService scenes)
    {
        this.scenes = scenes;
    }

    // 1) 简单同步加载（默认 Single 模式 + 校验构建设置）
    public void LoadMenu()
    {
        scenes.Load("MainMenu");
    }

    // 2) 异步加载并显示进度条，完成后设为激活场景
    public SceneLoadOperation LoadLevelAsync()
    {
        var args = new SceneLoadArgs
        {
            SceneName = "Level_01",
            Mode = LoadSceneMode.Single,
            ActivateOnLoad = true,
            SetActiveOnComplete = true,
            Progress = p => UnityEngine.Debug.Log($"加载进度 {p:P0}"),
            Completed = scene => UnityEngine.Debug.Log($"完成: {scene.name}")
        };
        return scenes.LoadAsync(args);
    }

    // 3) 加载到 90% 后等待玩家点击再激活（无缝过场）
    public SceneLoadOperation PreloadThenActivateOnTap()
    {
        var op = scenes.LoadAsync(new SceneLoadArgs
        {
            SceneName = "Level_02",
            ActivateOnLoad = false // 进度会停在 0.9
        });
        // 之后在某处轮询/事件中:
        // if (op.IsReadyToActivate) op.Activate();
        return op;
    }

    // 4) 附加场景叠加与卸载
    public void ToggleOverlay()
    {
        if (!scenes.IsSceneLoaded("UIOverlay"))
        {
            scenes.LoadAsync(new SceneLoadArgs { SceneName = "UIOverlay", Mode = LoadSceneMode.Additive });
        }
        else
        {
            scenes.UnloadAsync("UIOverlay");
        }
    }
}
```

### 设计意图与踩坑点

- `IsLoading` 是带副作用的属性（会调用 `RemoveCompletedOperations`），不要假定它纯只读；它也是并发守卫的判定基础。
- 同步 `Load` 不产生 `SceneLoadOperation`、不发布任何事件；只有 `LoadAsync` 走事件 + 进度通道。需要进度/回调请用异步重载。
- `NormalizedProgress` 用魔数 `0.9f` 归一化，是为了对齐 Unity"禁用激活时进度停在 0.9"的语义；UI 进度条应使用 `NormalizedProgress` 而非原始 `Progress`，否则进度条会卡在 90%。
- `ActivateOnLoad=false` 会导致 `IsDone` 永不为 `true`、`TrackLoadAsync` 循环不结束，必须主动 `Activate()`，否则该操作会一直占用 `activeOperations` 并阻塞后续非并发加载。
- 并发加载默认禁止（`AllowConcurrentLoads=false`）；附加（Additive）多场景同时加载需显式置 `true`，否则第二个 `LoadAsync` 会抛 `"A scene load is already running"`。
- 构建设置校验默认开启，运行期通过 Addressables/AssetBundle 动态加入的场景需把 `ValidateInBuildSettings`/`validateInBuildSettings` 置 `false`，否则误判为"不在构建设置"。
- 场景匹配 `SceneMatches` 同时支持名称与路径（路径会把 `\` 归一为 `/`，全部 `OrdinalIgnoreCase`），可用短名也可用完整路径。
- `LoadedScene` 通过 `SceneManager.GetSceneByName(sceneName)` 取，若 `sceneName` 传的是路径而非纯名称，可能取不到目标场景对象，进而影响 `SetActiveOnComplete` 生效——建议异步加载时 `SceneName` 用纯场景名。
- 事件与回调全程异常隔离（`FrameLog.Exception`），但这也意味着监听器里的错误会被"吞掉"只记日志，调试时需留意日志而非异常崩溃。
- `OnShutdown` 只清理列表与事件，不会强制卸载已加载场景；模块关闭不等于场景卸载。

---

## 15. Audio 模块

Audio 模块提供音乐（Music）、音效（SFX/UI/Ambient）的统一播放、分类音量、静音、淡入淡出与 `AudioMixer` 集成。核心服务 `AudioService` 继承 `GameModuleBase` 并实现 `IAudioService`，`Priority` 固定返回 `-300`。`OnInitialize` 在 `Context.Root` 下创建名为 `"Audio"` 的根节点，初始化分类音量（全部 `1f`），把音量应用到 `AudioMixer`，并按 `Context.Settings.AudioSourcePoolSize` 预热一个 `AudioSource` 对象池，最后注册服务。播放统一走对象池：从池里取空闲（未激活）的 `AudioSource`，循环音乐用 `Play()`、一次性音效用 `PlayOneShot` 并在剪辑时长后异步归还。淡入淡出、延时归还均使用 UniTask（`UniTaskVoid` + `CancellationToken` + `PlayerLoopTiming.Update`），不使用协程。每次播放返回一个 `AudioPlaybackHandle` 句柄，可运行期调音量/音调或停止。`AudioMixer` 音量采用线性→分贝转换（`Mathf.Log10(value) * 20f`，下限 `-80f`）。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `AudioCategory` | 音频分类枚举 | 5 个值：`Master`、`Music`、`Sfx`、`UI`、`Ambient`（按声明顺序，底层 int 0..4） |
| `AudioCue` | 可配置的音频资源（`ScriptableObject`） | `[CreateAssetMenu(menuName="Frame/Audio Cue", fileName="AudioCue")]`；字段经 Clamp 暴露 |
| `AudioPlaybackHandle` | 单次播放句柄（`sealed class`） | 内部构造，封装 `AudioSource`、音量(Clamp01)/音调(Clamp 0.1~3)、`Stop()`、`Refresh()` |
| `IAudioService` | 音频服务接口 | `CurrentMusic` + 音量/静音 + `PlayMusic/StopMusic` + `PlayCue/PlayOneShot`（及其 `Handle` 版本） |
| `AudioService` | `IAudioService` 实现，`GameModuleBase` 模块 | `Priority = -300`；`Audio` 根节点 + `AudioSource` 池 + `AudioMixer` dB 集成；UniTask 驱动淡入淡出与归还 |

### `AudioCategory.cs`

`public enum AudioCategory`，命名空间 `Frame.Audio`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Master` | 主分类 | 底层值 `0`；静音 `muted` 仅作用于 `Master`（应用时 volume 取 `0f`） |
| `Music` | 音乐分类 | 底层值 `1`；`PlayMusic`/循环音乐使用 |
| `Sfx` | 音效分类 | 底层值 `2`；`PlayOneShot` 默认分类 |
| `UI` | UI 音效分类 | 底层值 `3` |
| `Ambient` | 环境音分类 | 底层值 `4` |

### `AudioCue.cs`

`[CreateAssetMenu(menuName = "Frame/Audio Cue", fileName = "AudioCue")] public sealed class AudioCue : ScriptableObject`，命名空间 `Frame.Audio`，引用 `UnityEngine`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `[SerializeField] private AudioClip clip = null` | 音频剪辑 | 序列化字段，默认 `null` |
| `[SerializeField] private AudioCategory category = AudioCategory.Sfx` | 分类 | 默认 `Sfx` |
| `[SerializeField] private float volume = 1f` | 音量 | 默认 `1f` |
| `[SerializeField] private float pitch = 1f` | 音调 | 默认 `1f` |
| `[SerializeField] private bool loop = false` | 是否循环 | 默认 `false` |
| `public AudioClip Clip { get; }` | 暴露剪辑 | 返回 `clip` |
| `public AudioCategory Category { get; }` | 暴露分类 | 返回 `category` |
| `public float Volume { get; }` | 暴露音量 | 返回 `Mathf.Clamp01(volume)`（getter 内夹紧） |
| `public float Pitch { get; }` | 暴露音调 | 返回 `Mathf.Clamp(pitch, 0.1f, 3f)`（getter 内夹紧） |
| `public bool Loop { get; }` | 暴露循环标记 | 返回 `loop` |

### `AudioPlaybackHandle.cs`（位于 `IAudioService.cs` 文件中）

`public sealed class AudioPlaybackHandle`，命名空间 `Frame.Audio`。构造函数为 `internal`，只能由 `AudioService` 创建。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Action<AudioPlaybackHandle> stopAction` | 停止回调 | 指向 `AudioService.StopPlayback` |
| `private readonly Action<AudioPlaybackHandle> refreshAction` | 刷新回调 | 指向 `AudioService.RefreshPlayback` |
| `private AudioSource source` | 关联音源 | `Invalidate()` 时置 `null` |
| `private float volume` | 当前音量 | 构造时 `Mathf.Clamp01(volume)` |
| `private float pitch` | 当前音调 | 构造时 `Mathf.Clamp(pitch, 0.1f, 3f)` |
| `private bool valid` | 句柄是否有效 | 构造时 `source != null` |
| `internal AudioPlaybackHandle(AudioSource source, AudioCategory category, float volume, float pitch, Action<AudioPlaybackHandle> stopAction, Action<AudioPlaybackHandle> refreshAction)` | 构造函数 | 夹紧音量/音调，记录回调，设置 `Category`，`valid = source != null` |
| `public AudioSource Source { get; }` | 关联音源 | `valid ? source : null` |
| `public AudioCategory Category { get; private set; }` | 分类 | 构造时赋值，私有 setter |
| `public float Volume { get; set; }` | 音量 | get 返回 `volume`；set 经 `Mathf.Clamp01` 后调 `Refresh()`（写回 `source.volume`） |
| `public float Pitch { get; set; }` | 音调 | get 返回 `pitch`；set 经 `Mathf.Clamp(value, 0.1f, 3f)`，若 `valid && source != null` 直接写 `source.pitch` |
| `public bool IsValid { get; }` | 是否有效 | `valid && source != null` |
| `public bool IsPlaying { get; }` | 是否在播放 | `IsValid && source.isPlaying` |
| `public void Stop()` | 停止播放 | `valid && stopAction != null` 时调用 `stopAction(this)`（即 `StopPlayback`） |
| `internal void Invalidate()` | 作废句柄 | `valid = false; source = null;`（由 `AudioService` 归还/关闭时调用） |
| `internal void Refresh()` | 刷新到音源 | `valid && refreshAction != null` 时调用 `refreshAction(this)`（即 `RefreshPlayback`） |

### `IAudioService.cs`（接口部分）

`public interface IAudioService`，命名空间 `Frame.Audio`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `AudioPlaybackHandle CurrentMusic { get; }` | 当前音乐句柄 | 实现返回 `musicHandle`，可能为 `null` |
| `void SetVolume(AudioCategory category, float volume)` | 设置分类音量 | 实现 `Clamp01` 后存入字典并应用到 Mixer |
| `float GetVolume(AudioCategory category)` | 读取分类音量 | 字典无值时返回 `1f` |
| `void SetMuted(bool muted)` | 设置静音 | 实现记录 `muted` 并重新应用 `Master` 音量 |
| `void PlayMusic(AudioClip clip, float fadeSeconds = 0f, float volume = 1f)` | 播放音乐（循环） | 默认无淡入、音量 1；会先停掉当前音乐 |
| `void StopMusic(float fadeSeconds = 0f)` | 停止音乐 | 默认立即停止；`fadeSeconds>0` 则淡出后停 |
| `AudioSource PlayCue(AudioCue cue, Vector3 position = default)` | 按 Cue 播放，返回音源 | Music 分类转 `PlayMusic`，否则转 `PlayOneShot` |
| `AudioSource PlayOneShot(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default)` | 播放一次性音效，返回音源 | 内部调 `PlayOneShotHandle` 取其 `Source` |
| `AudioPlaybackHandle PlayCueHandle(AudioCue cue, Vector3 position = default)` | 按 Cue 播放，返回句柄 | Music 分类按 `cue.Loop` 决定是否循环，并写入 `musicHandle` |
| `AudioPlaybackHandle PlayOneShotHandle(AudioClip clip, AudioCategory category = AudioCategory.Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default)` | 播放一次性音效，返回句柄 | 内部调 `PlayClipHandle(..., loop:false)` |

### `AudioService.cs`

`public sealed class AudioService : GameModuleBase, IAudioService`，命名空间 `Frame.Audio`，引用 `System.Collections.Generic`、`System.Threading`、`Cysharp.Threading.Tasks`、`Frame.Core`、`UnityEngine`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<AudioCategory, float> mixerVolumes` | 分类音量表 | `SetDefaultVolumes` 初始化 5 项为 `1f` |
| `private readonly List<AudioSource> sourcePool` | `AudioSource` 对象池 | `Prewarm` 预创建；不足时 `GetFreeSource` 追加 `"Audio_Extra"` |
| `private readonly Dictionary<AudioSource, ActivePlayback> activePlaybacks` | 进行中的播放表 | 键为 `AudioSource`，值含 `Handle` 与 `ReturnCancellation` |
| `private Transform audioRoot` | `"Audio"` 根节点 | `OnInitialize` 创建于 `Context.Root` 下，`OnShutdown` 销毁 |
| `private AudioPlaybackHandle musicHandle` | 当前音乐句柄 | 对应 `CurrentMusic` |
| `private CancellationTokenSource musicFadeCancellation` | 当前淡入淡出取消源 | 同一时刻只允许一个淡变 |
| `private bool muted` | 静音标记 | 仅影响 `Master` 应用音量 |
| `private sealed class ActivePlayback` | 播放上下文 | 字段：`AudioPlaybackHandle Handle`、`CancellationTokenSource ReturnCancellation` |
| `public override int Priority { get; }` | 模块优先级 | 固定返回 `-300` |
| `public AudioPlaybackHandle CurrentMusic { get; }` | 当前音乐句柄 | 返回 `musicHandle` |
| `protected override void OnInitialize()` | 模块初始化 | 见下方流转逻辑；建 `"Audio"` 根、默认音量、应用 Mixer、`Prewarm(Context.Settings.AudioSourcePoolSize)`、注册服务 |
| `public void SetVolume(AudioCategory category, float volume)` | 设分类音量 | `mixerVolumes[category] = Mathf.Clamp01(volume)`，再 `ApplyMixerVolume(category)` |
| `public float GetVolume(AudioCategory category)` | 取分类音量 | `TryGetValue` 命中返回值，否则返回 `1f` |
| `public void SetMuted(bool muted)` | 设静音 | 记录 `this.muted`，调用 `ApplyMixerVolume(AudioCategory.Master)` |
| `public void PlayMusic(AudioClip clip, float fadeSeconds = 0f, float volume = 1f)` | 播放音乐 | `clip==null` 直接返回；先 `StopMusic()`；`PlayClipHandle(..., Music, loop:true)`；`fadeSeconds>0` 时把音量设 0 再 `StartMusicFade` 到 `Clamp01(volume)`（`stopWhenComplete:false`） |
| `public void StopMusic(float fadeSeconds = 0f)` | 停止音乐 | `musicHandle!=null` 时：`fadeSeconds>0 && handle.IsValid` → `StartMusicFade(handle, 0f, fadeSeconds, stopWhenComplete:true)` 后返回；否则 `CancelMusicFade()` + `handle.Stop()` + `musicHandle=null` |
| `public AudioSource PlayCue(AudioCue cue, Vector3 position = default)` | 按 Cue 播放 | `cue==null \|\| cue.Clip==null` → `null`；`Music` 分类 → `PlayMusic(cue.Clip, 0f, cue.Volume)` 并返回 `musicHandle?.Source`；否则 `PlayOneShot(cue.Clip, cue.Category, cue.Volume, cue.Pitch, position)` |
| `public AudioSource PlayOneShot(AudioClip clip, AudioCategory category = Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default)` | 一次性音效 | 调 `PlayOneShotHandle`，返回 `handle?.Source` |
| `public AudioPlaybackHandle PlayCueHandle(AudioCue cue, Vector3 position = default)` | 按 Cue 播放（句柄） | `cue==null \|\| cue.Clip==null` → `null`；`Music` 分类 → `StopMusic()` 后 `PlayClipHandle(cue.Clip, Music, cue.Volume, cue.Pitch, position, cue.Loop)` 并写 `musicHandle`；否则 `PlayOneShotHandle(...)` |
| `public AudioPlaybackHandle PlayOneShotHandle(AudioClip clip, AudioCategory category = Sfx, float volume = 1f, float pitch = 1f, Vector3 position = default)` | 一次性音效（句柄） | 直接 `PlayClipHandle(clip, category, volume, pitch, position, loop:false)` |
| `private AudioPlaybackHandle PlayClipHandle(AudioClip clip, AudioCategory category, float volume, float pitch, Vector3 position, bool loop)` | 播放核心 | 见下方流转逻辑；取空闲音源、设位置/音调(Clamp 0.1~3)/loop/clip、应用 MixerGroup、建句柄、入表、`Refresh`；循环 `Play()`，否则 `PlayOneShot(clip)` + 启动延时归还 |
| `protected override void OnShutdown()` | 模块关闭 | `CancelMusicFade()`；停掉并禁用池中所有音源；遍历 `activePlaybacks` 取消归还任务并 `Invalidate` 句柄；清空 3 个集合；销毁 `audioRoot`；`musicHandle=null` |
| `private void SetDefaultVolumes()` | 初始化默认音量 | `Master/Music/Sfx/UI/Ambient` 全部置 `1f` |
| `private void RefreshPlayback(AudioPlaybackHandle handle)` | 句柄→音源同步 | 句柄无效或音源空则返回；否则 `source.pitch = handle.Pitch; source.volume = handle.Volume`（注意：此处写入的是句柄音量，分类/主音量由 Mixer 单独控制） |
| `private void StopPlayback(AudioPlaybackHandle handle)` | 句柄停止回调 | 句柄无效返回；`ReturnSource(handle.Source, stopReturnRoutine:true)`；若 `handle==musicHandle` 则 `CancelMusicFade()` + `musicHandle=null` |
| `private void StartMusicFade(AudioPlaybackHandle handle, float targetVolume, float seconds, bool stopWhenComplete)` | 启动淡变 | 句柄无效返回；`CancelMusicFade()` 后新建 `CancellationTokenSource` 存入 `musicFadeCancellation`，`FadeMusicAsync(...).Forget()` |
| `private async UniTaskVoid FadeMusicAsync(AudioPlaybackHandle handle, float targetVolume, float seconds, bool stopWhenComplete, CancellationTokenSource cancellation)` | 淡变协作体 | `duration=Max(0.001,seconds)`，按 `Time.unscaledDeltaTime` 累加，`handle.Volume = Lerp(start, target, elapsed/duration)`，每帧 `await UniTask.Yield(Update, token)`；结束置 `target`，`stopWhenComplete` 则 `handle.Stop()`；`catch OperationCanceledException` 静默；`finally` 若仍是当前淡变则清理并 `Dispose` |
| `private void CancelMusicFade()` | 取消淡变 | 取出 `musicFadeCancellation`，为空返回；否则置 `null` 后 `Cancel()` + `Dispose()` |
| `private void ApplyMixerGroup(AudioSource source, AudioCategory category)` | 设置音源输出组 | `source/Context/Settings` 非空时 `source.outputAudioMixerGroup = Context.Settings.GetAudioMixerGroup(category)` |
| `private void ApplyAllMixerVolumes()` | 应用全部分类音量 | 依次 `ApplyMixerVolume` 5 个分类 |
| `private void ApplyMixerVolume(AudioCategory category)` | 应用单个分类音量到 Mixer | `Context/Settings` 空返回；取 `GetAssignedAudioMixerGroup(category)` 与 `GetAudioMixerVolumeParameter(category)`，组/混音器/参数任一无效则返回；`volume = (category==Master && muted) ? 0f : GetVolume(category)`；`group.audioMixer.SetFloat(parameter, LinearToDecibels(volume))` |
| `private static float LinearToDecibels(float value)` | 线性→分贝 | `value <= 0.0001f` → `-80f`；否则 `Mathf.Log10(value) * 20f` |
| `private void Prewarm(int count)` | 预热对象池 | 循环 `count` 次 `CreateSource("Audio_" + i)`，禁用后加入 `sourcePool` |
| `private AudioSource GetFreeSource()` | 取空闲音源 | 遍历池找 `!activeSelf` 者，激活并返回；无空闲则 `CreateSource("Audio_Extra")` 加入池并返回（不预先禁用，故返回时已激活） |
| `private AudioSource CreateSource(string name)` | 创建音源 | 新建带 `AudioSource` 的 GameObject，挂到 `audioRoot ?? Context.Root`；`playOnAwake=false`、`spatialBlend=0f`（2D） |
| `private async UniTaskVoid ReturnWhenFinishedAsync(AudioSource source, float seconds, AudioPlaybackHandle handle, CancellationToken cancellationToken)` | 延时归还 | `milliseconds = Max(0, CeilToInt(seconds*1000))`；`UniTask.Delay(ms, UnscaledDeltaTime, Update, token)`；`catch OperationCanceledException` 直接 return；延时后若该 source 仍映射同一 handle 则 `ReturnSource(source, stopReturnRoutine:false)` |
| `private void ReturnSource(AudioSource source, bool stopReturnRoutine)` | 归还音源到池 | `source==null` 返回；若有 `ActivePlayback`：处理 `ReturnCancellation`（`stopReturnRoutine` 时 `Cancel()`，再 `Dispose()` 置 `null`）、`Handle.Invalidate()`、从表移除；最后 `Stop()` + `clip=null` + `loop=false` + `gameObject.SetActive(false)` |

### 流转逻辑

初始化 `OnInitialize`：
1. `new GameObject("Audio")`，`SetParent(Context.Root, false)`，记为 `audioRoot`。
2. `SetDefaultVolumes()`：`mixerVolumes` 中 `Master/Music/Sfx/UI/Ambient` 全置 `1f`。
3. `ApplyAllMixerVolumes()`：把这 5 个分类音量经 `LinearToDecibels` 写入各自的 `AudioMixer` 参数（仅当分类配置了 group/parameter 时）。
4. `Prewarm(Context.Settings.AudioSourcePoolSize)`：按设置预创建 N 个 `AudioSource`（`playOnAwake=false`、`spatialBlend=0f` 即 2D），创建后立即禁用 GameObject 放入 `sourcePool`。
5. `Context.Services.Register<IAudioService>(this)` + `Context.Services.Register(this)`。

分类音量模型与最终音量：分类音量存于 `mixerVolumes`（默认全 `1f`，经 `Clamp01`）。最终音量并非在 `AudioSource.volume` 上做 `master * category` 的乘法——`RefreshPlayback` 只把"句柄自身音量"`handle.Volume` 写到 `source.volume`；分类与主音量是通过 `AudioMixer` 实现的：每个分类音量（`Master` 在 `muted` 时取 `0f`）经 `LinearToDecibels` 转成 dB，由 `ApplyMixerVolume` 写入对应 Mixer 组的暴露参数，最终由 `AudioMixer` 的组层级（Master 作为父总线）叠加完成"master × category"的衰减。若分类未配置 Mixer 组/参数，则该分类的音量调节不会生效（音源仍以句柄音量直接播放）。

`AudioMixer` 集成：
- `ApplyMixerGroup`：播放时把 `source.outputAudioMixerGroup = Context.Settings.GetAudioMixerGroup(category)`，将音源路由到分类对应的混音组。
- `ApplyMixerVolume`：取 `Context.Settings.GetAssignedAudioMixerGroup(category)` 与 `Context.Settings.GetAudioMixerVolumeParameter(category)`；当组、`audioMixer`、参数名都有效时，`SetFloat(parameter, LinearToDecibels(volume))`。
- dB 转换公式（精确）：`LinearToDecibels(value)` = `value <= 0.0001f ? -80f : Mathf.Log10(value) * 20f`。

`PlayMusic(clip, fadeSeconds, volume)` 淡入：`clip==null` 直接返回 → `StopMusic()`（无淡出）→ `PlayClipHandle(clip, Music, volume, pitch:1f, position:Zero, loop:true)` 得到 `musicHandle` → 若 `fadeSeconds>0`，先 `musicHandle.Volume = 0f`，再 `StartMusicFade(musicHandle, Clamp01(volume), fadeSeconds, stopWhenComplete:false)` 实现从 0 渐入到目标音量。

`StopMusic(fadeSeconds)` 淡出：`musicHandle!=null` 时，若 `fadeSeconds>0 && handle.IsValid` → `StartMusicFade(handle, 0f, fadeSeconds, stopWhenComplete:true)`（淡出到 0 后 `Stop()`）并提前 return；否则 `CancelMusicFade()` + `handle.Stop()` + `musicHandle=null`。

淡变实现（UniTask，非协程）：`StartMusicFade` 先 `CancelMusicFade()` 保证唯一性，新建 `CancellationTokenSource` 存入 `musicFadeCancellation`，`FadeMusicAsync(...).Forget()`。`FadeMusicAsync` 用 `duration = Max(0.001f, seconds)`，每帧 `elapsed += Time.unscaledDeltaTime`（不受 `Time.timeScale` 影响），`handle.Volume = Lerp(startVolume, targetVolume, Clamp01(elapsed/duration))`，`await UniTask.Yield(PlayerLoopTiming.Update, token)`；完成后写入目标音量并按 `stopWhenComplete` 决定是否 `Stop()`；`OperationCanceledException` 被静默捕获；`finally` 仅当 `musicFadeCancellation` 仍是本次 cancellation 时清空并 `Dispose`。

`PlayOneShot` / 一次性音效归还：`PlayOneShot` → `PlayOneShotHandle` → `PlayClipHandle(..., loop:false)`。在 `PlayClipHandle` 中：`GetFreeSource()` 取空闲音源 → 设 `transform.position`、`pitch=Clamp(pitch,0.1,3)`、`loop=false`、`clip=null`（非循环不预设 clip）→ `ApplyMixerGroup` → 建 `AudioPlaybackHandle`（回调 `StopPlayback`/`RefreshPlayback`）→ 写入 `activePlaybacks` → `RefreshPlayback`（写 volume/pitch）→ 非循环分支：`source.PlayOneShot(clip)`，新建 `ReturnCancellation`，`playbackSeconds = clip.length / Max(0.01f, source.pitch)`，`ReturnWhenFinishedAsync(source, playbackSeconds, handle, token).Forget()`。`ReturnWhenFinishedAsync` 用 `UniTask.Delay(CeilToInt(seconds*1000)ms, UnscaledDeltaTime, Update, token)` 等待剪辑播放时长，到期若该音源仍映射同一句柄则 `ReturnSource(source, stopReturnRoutine:false)` 归还。被取消时静默 return。

`PlayCue` / `PlayCueHandle` 使用 `AudioCue`：先判 `cue==null || cue.Clip==null`。`Music` 分类时——`PlayCue` 走 `PlayMusic(cue.Clip, 0f, cue.Volume)`（强制非循环淡入、loop 由 `PlayMusic` 固定为 true）；`PlayCueHandle` 走 `StopMusic()` + `PlayClipHandle(..., loop:cue.Loop)` 并写 `musicHandle`（尊重 `cue.Loop`）。非 Music 分类时均转 `PlayOneShot`/`PlayOneShotHandle`。`AudioCue.Volume`/`Pitch` 在 getter 内已 Clamp。

`AudioPlaybackHandle`：每次播放返回，封装单次播放的运行期控制。`Volume`（Clamp01，set 触发 `Refresh()` 回写 `source.volume`）、`Pitch`（Clamp 0.1~3，set 直接写 `source.pitch`）、`IsValid`、`IsPlaying`、`Stop()`（触发 `StopPlayback` → `ReturnSource(stopReturnRoutine:true)`）。归还/关闭时句柄被 `Invalidate()`，之后 `Source` 返回 `null`、`IsValid=false`。

`CurrentMusic`：返回 `musicHandle`，即最近一次 `PlayMusic`/`PlayCueHandle(Music)` 产生的句柄；`StopMusic` 完成（或 `StopPlayback` 命中 musicHandle）后置 `null`。

`OnShutdown`：`CancelMusicFade()` → 停掉并禁用池内全部音源（`Stop`/`clip=null`/`SetActive(false)`）→ 遍历 `activePlaybacks` 取消并释放各自 `ReturnCancellation`、`Handle.Invalidate()` → 清空 `activePlaybacks`/`sourcePool`/`mixerVolumes` → 销毁 `audioRoot` GameObject → `musicHandle=null`。

### 使用示例

```csharp
using Frame.Audio;
using UnityEngine;

public sealed class AudioExample
{
    private readonly IAudioService audio;

    public AudioExample(IAudioService audio)
    {
        this.audio = audio;
    }

    public void Run(AudioClip bgm, AudioClip clickSfx, AudioCue explosionCue)
    {
        // 分类音量与静音（写入 AudioMixer 暴露参数）
        audio.SetVolume(AudioCategory.Master, 0.8f);
        audio.SetVolume(AudioCategory.Music, 0.6f);
        audio.SetMuted(false);

        // 2 秒淡入的循环背景音乐
        audio.PlayMusic(bgm, fadeSeconds: 2f, volume: 1f);

        // 一次性 UI 音效，返回句柄可调音
        AudioPlaybackHandle click = audio.PlayOneShotHandle(
            clickSfx, AudioCategory.UI, volume: 0.9f, pitch: 1.1f);
        if (click != null && click.IsValid)
        {
            click.Volume = 0.5f; // 运行期改音量
        }

        // 用 AudioCue 在世界坐标播放（Music 分类会自动当作背景音乐）
        audio.PlayCue(explosionCue, new Vector3(3f, 0f, 0f));

        // 1.5 秒淡出并停止当前音乐
        audio.StopMusic(fadeSeconds: 1.5f);

        // 读取当前音乐句柄
        AudioPlaybackHandle current = audio.CurrentMusic;
        Debug.Log(current != null && current.IsPlaying ? "音乐播放中" : "无音乐");
    }
}
```

### 设计意图与踩坑点

- 最终音量并非在 `AudioSource.volume` 上做 master×category 相乘：`source.volume` 只承载"句柄音量"，分类/主音量靠 `AudioMixer` 组层级实现。因此必须在 `FrameSettings` 为分类配置好 Mixer 组与暴露参数（`GetAssignedAudioMixerGroup`/`GetAudioMixerVolumeParameter`），`SetVolume`/`SetMuted` 才会生效；否则只有句柄级音量起作用。
- 静音 `muted` 仅作用于 `Master` 分类（`ApplyMixerVolume` 中 `category==Master && muted` 时取 `0f`）；其它分类不读 `muted`，是通过 Master 总线统一压到 `-80dB`（`LinearToDecibels(0)`）实现整体静音。
- dB 转换下限是 `-80f`（`value<=0.0001f`），即"几乎为 0"就视作静音，而非 `Mathf` 负无穷。
- 对象池：非循环音效靠 `ReturnWhenFinishedAsync` 在"剪辑时长 / pitch"后归还（用 `UnscaledDeltaTime`，不受时间缩放影响）。若 pitch 在播放中被改大，归还时间不会重新计算（仍按播放时的 pitch 估算），极端情况下可能提前/滞后归还。
- `GetFreeSource` 在池满时创建 `"Audio_Extra"` 且不预先禁用（直接返回已激活的音源），池会无界增长——高并发音效场景应把 `AudioSourcePoolSize` 设足够大以减少额外分配。
- 循环音源（音乐）走 `Play()`，clip 设在 `source.clip`；非循环走 `source.PlayOneShot(clip)`，此时 `source.clip` 被置 `null`。两者归还/失效路径不同：循环音乐需 `Stop()`/`StopMusic` 显式停止，否则永远占用一个音源。
- 同一时刻只允许一个音乐淡变（`musicFadeCancellation`）：新淡变会 `CancelMusicFade()` 取消上一个；`PlayMusic`/`StopMusic` 反复快速调用是安全的（旧淡变被取消并 `Dispose`）。
- `PlayCue`（非 Handle 版）对 Music 分类强制 `PlayMusic`（循环、无淡入），会忽略 `cue.Loop`；若需要尊重 `cue.Loop`（如非循环的音乐型片段），应使用 `PlayCueHandle`。
- 所有异步流程（淡变、延时归还）均为 UniTask `UniTaskVoid` + `Forget()`，配合 `CancellationToken` 取消；`OperationCanceledException` 被静默吞掉，调试时取消不会报错。
- `AudioSource` 默认 `spatialBlend=0f`（2D），`PlayClipHandle` 虽设置了 `transform.position`，但 2D 音源下位置不产生空间衰减；需要 3D 空间音效需在创建后另行调整 `spatialBlend`（当前框架未暴露该配置）。
- `OnShutdown` 会销毁整个 `"Audio"` 根节点并作废所有句柄，关闭后持有的旧 `AudioPlaybackHandle` 将 `IsValid=false`，继续使用应先判空/判有效。

## 16. UI 模块

UI 模块是整个框架中**体量最大、状态机最复杂**的模块，基于 Unity 内置的 **UGUI（uGUI / `UnityEngine.UI`）** 实现。它围绕一个**单一 Canvas 根节点（`UIRoot`）** 组织出多个**分层（`UILayer`，每层一个独立 `Canvas` + `GraphicRaycaster` + `overrideSorting`）**，在各层之上托管**面板（`UIPanelBase` / `UIPanelBase<TArgs>`）的完整生命周期**（`OnCreate` 一次性创建 → `OnOpen` 每次打开 → `OnClose` 关闭 → `OnDispose` 销毁）。

模块对外提供一套丰富的能力：

- **路由（`UIRoute`）**：把「字符串 route 名 → 预制体路径 + 面板类型 + 打开选项」绑定起来，业务层只需记 route 名即可打开面板；
- **后退栈（back stack）**：以 `List<UIPanelBase> stack` 维护打开顺序，`Back()` 从栈顶向下找到第一个 `AllowBack=true` 的面板并关闭，实现「物理返回键 / 手势返回」语义；
- **模态遮罩（modal mask）**：模态面板会在其所在层插入一张全屏 `Image` 遮罩（`ModalColor`），可选 `CloseOnBackdrop`（点击遮罩关闭）；
- **弹窗队列（popup queue）**：`EnqueueRoute` 把弹窗排队，**同一时刻只有一个排队面板处于活动状态**，前一个关闭后自动弹出下一个；
- **异步打开**：`OpenAsync` / `OpenRouteAsync` / `EnqueueRoute` 返回可被 `yield return` 的 `UIPanelRequest<TPanel>`（继承 `CustomYieldInstruction`）；
- **强类型参数**：`UIPanelBase<TArgs>` 在 `OnOpen` 中把 `object args` 安全地转成 `TArgs`，类型不匹配则抛 `ArgumentException`；
- **打开/关闭过渡（transition）**：通过 `IUITransition` 抽象，内置淡入淡出 `UIFadeTransition`；安装 `ITweenService` 时优先使用补间服务，否则使用协程 fallback。

`UIService` 继承 `GameModuleBase`，`Priority = -400`（在 Assets 等基础模块之后初始化），在 `OnInitialize` 中用 `Context.Services.TryResolve(out assets)` **可选地**解析 `IAssetService`（若资源模块被禁用则 `assets` 为 `null`，此时同步与异步打开都会失败并打印警告），随后创建 `UIRoot` 并把自身注册为 `IUIService` 与 `UIService` 两个服务。

---

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IUIService` | UI 模块对外接口 | 定义 `Open/OpenAsync/Route/EnqueueRoute/Close/Back` 等全部能力；业务层应优先依赖此接口 |
| `UIService`（`sealed class : GameModuleBase, IUIService`） | UI 模块实现 | `Priority=-400`；持有缓存字典、路由表、弹窗队列、后退栈；约 30 个成员 |
| `UIRoot`（`sealed class : MonoBehaviour`） | UI 根节点 | 持有主 `Canvas`（`ScreenSpaceOverlay`）；按需创建每个 `UILayer` 的子 Canvas；保证 `EventSystem` 存在 |
| `UILayer`（`enum`） | UI 分层枚举 | 枚举值即各层 `Canvas.sortingOrder`：Background=0 / Normal=100 / Popup=200 / Tips=300 / Loading=400 / System=500 |
| `UIPanelBase`（`abstract class : MonoBehaviour`） | 面板基类（非泛型） | 维护 `created`/`IsOpen` 状态；`Internal*` 方法供 `UIService` 调用；四个 `protected virtual` 生命周期钩子 |
| `UIPanelBase<TArgs>`（`abstract class : UIPanelBase`） | 强类型参数面板基类 | `sealed override OnOpen(object)` 做类型校验并转发到 `OnOpen(TArgs)` |
| `UIPanelContext`（`sealed class`） | 面板上下文 | 持有 `Service/Route/AssetPath/Options/Args/ModalBlocker`；提供 `Layer/IsModal/AllowBack/GetArgs<T>` |
| `UIOpenOptions`（`sealed class`） | 打开选项 | 默认值：Layer=Normal、Cache=true、Modal=false、CloseOnBackdrop=false、AllowBack=true、ModalColor=(0,0,0,0.55)；提供 `Default()`/`Clone()` |
| `UIRoute`（`sealed class`） | 路由定义 | 不可变；构造时校验 route/path/panelType；内部 Clone 选项 |
| `UIPanelRequest<TPanel>`（`sealed class : CustomYieldInstruction`） | 异步打开句柄 | `keepWaiting=!IsDone`；暴露 `IsDone/Success/Panel/Error`；`internal Complete` 由服务调用 |
| `IUITransition`（`interface`） | 过渡动画抽象 | `IEnumerator PlayOpen/PlayClose(UIPanelBase)` |
| `UIFadeTransition`（`sealed class : IUITransition`） | 淡入淡出过渡 | 默认 duration=0.18，unscaledTime=true，ease=OutQuad；操作 `CanvasGroup.alpha` |
| `SafeAreaFitter`（`sealed class : MonoBehaviour`） | 安全区适配 | 每帧检测 `Screen.safeArea` 变化并重设锚点 |
| `UIService.QueuedPanelOpen`（私有嵌套 `class`） | 弹窗队列项（非泛型） | 持有 `PanelType/Route/Args` 与 `UIPanelRequest<UIPanelBase>`；`virtual Complete` |
| `UIService.QueuedPanelOpen<TPanel>`（私有嵌套 `sealed class`） | 弹窗队列项（强类型） | 继承非泛型版；持有 `UIPanelRequest<TPanel>`；`override Complete` 做 `panel as TPanel` 下转 |

---

### `IUIService.cs`

UI 模块对外契约。所有方法均围绕「打开 / 路由 / 队列 / 关闭」展开。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `UIRoot Root { get; }` | 暴露 UI 根节点 | 只读；实现返回内部 `root` 字段 |
| `int QueuedPanelCount { get; }` | 当前排队（尚未打开）的弹窗数量 | 实现返回 `queuedPanels.Count`，不含已激活的 `queuedActivePanel` |
| `TPanel Open<TPanel>(string resourcesPath, UILayer layer = UILayer.Normal, object args = null, bool cache = true)` where `TPanel:UIPanelBase` | 同步打开（层 + 缓存简化重载） | 内部组装 `UIOpenOptions` 后转发到 options 重载 |
| `TPanel Open<TPanel>(string resourcesPath, UIOpenOptions options, object args = null)` where `TPanel:UIPanelBase` | 同步打开（完整选项） | 核心入口，调用 `OpenInternal`，失败返回 `null` |
| `TPanel Open<TPanel, TArgs>(string resourcesPath, TArgs args, UILayer layer = UILayer.Normal, bool cache = true)` where `TPanel:UIPanelBase<TArgs>` | 强类型参数同步打开 | 转发到非泛型参数版 `Open<TPanel>`，`args` 装箱传入 |
| `UIPanelRequest<TPanel> OpenAsync<TPanel>(string resourcesPath, UILayer layer = UILayer.Normal, object args = null, bool cache = true)` where `TPanel:UIPanelBase` | 异步打开（简化重载） | 组装选项后转发 options 异步重载 |
| `UIPanelRequest<TPanel> OpenAsync<TPanel>(string resourcesPath, UIOpenOptions options, object args = null)` where `TPanel:UIPanelBase` | 异步打开（完整选项） | 立即返回 `UIPanelRequest`，可 `yield return` |
| `void RegisterRoute<TPanel>(string route, string resourcesPath, UILayer layer=Normal, bool cache=true, bool modal=false, bool closeOnBackdrop=false, bool allowBack=true, IUITransition transition=null)` where `TPanel:UIPanelBase` | 注册路由（参数式） | 内部把零散参数组装成 `UIOpenOptions` 并 `new UIRoute(...)` 注册 |
| `void RegisterRoute(UIRoute route)` | 注册路由（对象式） | `route==null` 抛 `ArgumentNullException`；按 `route.Route` 覆盖写入字典 |
| `bool UnregisterRoute(string route)` | 注销路由 | route 空白返回 false；否则返回字典 `Remove` 结果 |
| `bool HasRoute(string route)` | 是否已注册路由 | route 非空白且字典含键才为 true |
| `UIPanelBase OpenRoute(string route, object args = null)` | 同步打开路由（弱类型返回） | 取路由后用 `route.PanelType` 打开，不做类型校验 |
| `TPanel OpenRoute<TPanel>(string route, object args = null)` where `TPanel:UIPanelBase` | 同步打开路由（强类型返回） | 先 `ValidateRoutePanelType<TPanel>`，再以 `typeof(TPanel)` 打开 |
| `TPanel OpenRoute<TPanel, TArgs>(string route, TArgs args)` where `TPanel:UIPanelBase<TArgs>` | 强类型参数 + 强类型面板打开路由 | 转发到 `OpenRoute<TPanel>`，`args` 装箱 |
| `UIPanelRequest<TPanel> OpenRouteAsync<TPanel>(string route, object args=null)` where `TPanel:UIPanelBase` | 异步打开路由 | 校验类型后走 `OpenInternalAsync` |
| `UIPanelRequest<UIPanelBase> EnqueueRoute(string route, object args=null)` | 入队弹窗（弱类型） | 用 route 的 `PanelType`；入队后立即尝试 `OpenNextQueuedPanel` |
| `UIPanelRequest<TPanel> EnqueueRoute<TPanel>(string route, object args=null)` where `TPanel:UIPanelBase` | 入队弹窗（强类型） | 入队前 `ValidateRoutePanelType`；用泛型队列项 |
| `UIPanelRequest<TPanel> EnqueueRoute<TPanel, TArgs>(string route, TArgs args)` where `TPanel:UIPanelBase<TArgs>` | 入队弹窗（强类型参数） | 转发到 `EnqueueRoute<TPanel>` |
| `void ClearQueuedPanels()` | 清空弹窗队列 | 逐个 `Dequeue().Complete(null, "UI panel queue was cleared.")` |
| `void Close(UIPanelBase panel, bool destroy = false)` | 关闭指定面板 | 转发 `CloseInternal(panel, destroy, immediate:false)` |
| `void CloseTop(bool destroy = false)` | 关闭栈顶面板 | 栈空直接返回；否则 `Close(stack[^1])` |
| `void CloseAll(bool destroy = false)` | 关闭所有面板 | 先清队列、置 `suppressQueuedOpen`，倒序逐个关闭 |
| `bool Back(bool destroy = false)` | 返回（关闭最上层允许返回的面板） | 从栈顶向下找首个 `Context.AllowBack` 的面板关闭，成功返回 true，否则 false |

---

### `UIService.cs`

模块实现。下表覆盖**全部字段、属性、公开方法、私有辅助方法与两个嵌套队列项类**。

#### 字段

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Dictionary<string, UIPanelBase> cachedPanels` | 缓存面板字典 | key 为 cacheKey（route 优先，否则 resourcesPath）；只有 `options.Cache==true` 才写入 |
| `Dictionary<string, UIRoute> routes` | 路由表 | key 为 route 字符串 |
| `Queue<QueuedPanelOpen> queuedPanels` | 弹窗等待队列 | FIFO；不含已激活弹窗 |
| `List<UIPanelBase> stack` | 后退栈 / 打开顺序栈 | 末尾为最上层；`BringToTop` 维护 |
| `IAssetService assets` | 资源服务引用 | `OnInitialize` 中 `TryResolve`，可能为 `null` |
| `UIRoot root` | UI 根节点 | `CreateRoot()` 创建 |
| `UIPanelBase queuedActivePanel` | 当前活动的排队弹窗 | 同一时刻至多一个；关闭后触发下一个 |
| `bool suppressQueuedOpen` | 抑制队列推进标志 | `CloseAll`/`OnShutdown` 期间置 true，避免关闭过程反复弹下一个 |

#### 属性

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `override int Priority { get { return -400; } }` | 模块初始化优先级 | **固定 `-400`**；数值小→早初始化、晚关闭 |
| `UIRoot Root { get; }` | 暴露根节点 | 返回 `root` |
| `int QueuedPanelCount { get; }` | 排队数量 | 返回 `queuedPanels.Count` |

#### 生命周期 override

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `protected override void OnInitialize()` | 模块初始化 | `Context.Services.TryResolve(out assets)`（可选）→ `CreateRoot()` → 注册 `IUIService` 与 `UIService` 两个服务 |
| `protected override void OnShutdown()` | 模块关闭 | 置 `suppressQueuedOpen=true` → `ClearQueuedPanels()` → `CloseAllImmediate(true)`（无过渡、销毁）→ 清空 `cachedPanels/routes/stack`、置空 `queuedActivePanel/root/assets`，复位 `suppressQueuedOpen` |

#### 公开打开/路由/队列/关闭方法

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Open<TPanel>(path, layer, args, cache)` | 同步打开简化重载 | `UIOpenOptions.Default()` + 设置 Layer/Cache，转发 options 重载 |
| `Open<TPanel>(path, options, args)` | 同步打开核心 | `OpenInternal(typeof(TPanel), route:null, path, options, args) as TPanel` |
| `Open<TPanel, TArgs>(path, args, layer, cache)` | 强类型参数同步打开 | 转发到 `Open<TPanel>(path, layer, args, cache)` |
| `OpenAsync<TPanel>(path, layer, args, cache)` | 异步打开简化重载 | 组装选项后转发 options 异步重载 |
| `OpenAsync<TPanel>(path, options, args)` | 异步打开核心 | `new UIPanelRequest<TPanel>()` → `OpenInternalAsync(...)` → 立即返回 request |
| `RegisterRoute<TPanel>(...)` | 参数式注册路由 | 组装 `UIOpenOptions`（含 Modal/CloseOnBackdrop/AllowBack/Transition）→ `RegisterRoute(new UIRoute(...))` |
| `RegisterRoute(UIRoute route)` | 对象式注册路由 | `route==null` → `ArgumentNullException("route")`；`routes[route.Route]=route` 覆盖写入 |
| `UnregisterRoute(string route)` | 注销路由 | 空白返回 false；否则 `routes.Remove(route)` |
| `HasRoute(string route)` | 是否含路由 | `!IsNullOrWhiteSpace(route) && routes.ContainsKey(route)` |
| `OpenRoute(route, args)` | 同步打开路由（弱类型） | `GetRoute(route)` → `OpenInternal(uiRoute.PanelType, uiRoute.Route, uiRoute.ResourcesPath, uiRoute.Options, args)` |
| `OpenRoute<TPanel>(route, args)` | 同步打开路由（强类型返回） | `GetRoute` → `ValidateRoutePanelType<TPanel>` → `OpenInternal(typeof(TPanel),...) as TPanel` |
| `OpenRoute<TPanel, TArgs>(route, args)` | 强类型参数路由打开 | 转发 `OpenRoute<TPanel>` |
| `OpenRouteAsync<TPanel>(route, args)` | 异步打开路由 | `GetRoute` → `ValidateRoutePanelType<TPanel>` → `OpenInternalAsync` → 返回 request |
| `EnqueueRoute(route, args)` | 入队弹窗（弱类型） | `GetRoute` → `new QueuedPanelOpen(uiRoute.PanelType, route, args, request)` 入队 → `OpenNextQueuedPanel()` |
| `EnqueueRoute<TPanel>(route, args)` | 入队弹窗（强类型） | `GetRoute` → `ValidateRoutePanelType(typeof(TPanel), uiRoute)` → 入队 `QueuedPanelOpen<TPanel>` → `OpenNextQueuedPanel()` |
| `EnqueueRoute<TPanel, TArgs>(route, args)` | 入队弹窗（强类型参数） | 转发 `EnqueueRoute<TPanel>` |
| `ClearQueuedPanels()` | 清空队列 | `while (queuedPanels.Count>0) Dequeue().Complete(null, "UI panel queue was cleared.")` |
| `Close(panel, destroy)` | 关闭面板 | `CloseInternal(panel, destroy, immediate:false)` |
| `CloseTop(destroy)` | 关闭栈顶 | 栈空 return；否则 `Close(stack[stack.Count-1], destroy)` |
| `CloseAll(destroy)` | 关闭所有 | `ClearQueuedPanels()` → `suppressQueuedOpen=true` → 倒序 `Close(stack[i], destroy)` → `finally` 复位标志 |
| `Back(destroy)` | 返回 | 从栈顶向下遍历，找首个 `panel!=null && panel.Context!=null && panel.Context.AllowBack`，`Close` 并返回 true；遍历完返回 false |

#### 私有辅助方法

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `UIPanelBase OpenInternal(Type panelType, string route, string resourcesPath, UIOpenOptions options, object args)` | 同步打开总流程 | `ValidateOpen` → `ResolveOptions`（Clone） → `GetCacheKey`；**缓存命中**则校验类型、`Context.Update`、`PrepareModalBlocker`、`InternalOpen`、`BringToTop`、`PlayOpenTransition`，异常时打日志并 `RemoveModalBlocker`+`InternalClose`；**未命中**则 `assets.Instantiate(path, layer, false)`，失败打印 `"Failed to open UI: "+path` 并返回 null，否则 `CreatePanelFromInstance` |
| `void OpenInternalAsync<TPanel>(Type, route, path, options, args, UIPanelRequest<TPanel> request)` | 异步打开总流程 | 校验 + ResolveOptions + cacheKey；**缓存命中**直接走同步 `OpenInternal` 并 `request.Complete(panel as TPanel, mismatch?"Cached UI panel type mismatch.":null)`；`assets==null` → `Complete(null,"Asset service is not available.")`；否则 `assets.LoadAsync<GameObject>`：句柄无效 → `"Failed to load UI asset: "+path`；有效则 `Object.Instantiate(handle.Asset, layer, false)`、`handle.Release()`、`CreatePanelFromInstance`、`Complete`；回调内异常 `FrameLog.Exception` 并 `Complete(null, exception.Message)` |
| `UIPanelBase CreatePanelFromInstance(Type panelType, route, path, options, args, cacheKey, GameObject instance)` | 从实例创建面板 | `instance.GetComponent(panelType) as UIPanelBase`，缺组件则 `Destroy(instance)` + 警告 `"UI prefab does not contain panel component: "+path+" type="+name` 返回 null；取/补 `RectTransform`，`SetParent(layer,false)` 并拉伸为全屏（anchorMin=0/anchorMax=1/offset=0）；`try`：`InternalCreate(new UIPanelContext(...))`→`PrepareModalBlocker`→`InternalOpen(args)`，异常则 `RemoveModalBlocker`+`InternalClose(false)`+`InternalDispose`+`Destroy` 返回 null；成功后 `stack.Add`、`BringToTop`，`options.Cache` 时写 `cachedPanels[cacheKey]`，最后 `PlayOpenTransition` 并返回 panel |
| `void CloseInternal(UIPanelBase panel, bool destroy, bool immediate)` | 关闭总流程 | `panel==null` return；取 `options`（无 Context 用 Default）；`stack.Remove`、`RemoveModalBlocker`；`wasOpen = InternalClose(false)`（**不停用 GO**）；若未打开且不销毁直接 return；非 immediate 且有 `Transition` 且 root 存在且 GO 在层级中激活 → `root.StartCoroutine(CloseWithTransition(...))` 并 return；否则 `FinishClose` |
| `IEnumerator CloseWithTransition(UIPanelBase panel, bool destroy, IUITransition transition)` | 带过渡关闭协程 | `yield return transition.PlayClose(panel)` → `FinishClose(panel, destroy)` |
| `void FinishClose(UIPanelBase panel, bool destroy)` | 收尾关闭 | `panel==null` return；记录 `wasQueuedActive = panel==queuedActivePanel`；`InternalSetClosed()`（停用 GO）；若 destroy → `RemoveCached`+`InternalDispose`+`Destroy(panel.gameObject)`；若关的是活动排队弹窗 → 置 `queuedActivePanel=null`，未抑制时 `OpenNextQueuedPanel()` |
| `void CloseAllImmediate(bool destroy)` | 立即关闭所有（无过渡） | 倒序 `CloseInternal(stack[i], destroy, immediate:true)`；供 `OnShutdown` 调用 |
| `void CreateRoot()` | 创建 UI 根节点 | `new GameObject(Context.Settings.UIRootName, RectTransform, Canvas, CanvasScaler, GraphicRaycaster)` → `SetParent(Context.Root,false)` → 取/补 `UIRoot` 组件 → `root.Initialize(Context.Settings)` |
| `void PrepareModalBlocker(UIPanelBase panel, UIOpenOptions options)` | 准备模态遮罩 | 先 `RemoveModalBlocker`（幂等）；若非模态/参数空则返回；在 `options.Layer` 层新建 `name+"_ModalBlocker"`（RectTransform+CanvasRenderer+Image），全屏拉伸；`image.color=options.ModalColor`、`raycastTarget=true`；若 `CloseOnBackdrop` 则加 `Button`（`transition=None`，`onClick → Close(panel)`）；`panel.Context.SetModalBlocker(blocker)` |
| `void RemoveModalBlocker(UIPanelBase panel)` | 移除模态遮罩 | panel/Context/ModalBlocker 任一为空则返回；`Destroy(ModalBlocker)` 并 `SetModalBlocker(null)` |
| `void BringToTop(UIPanelBase panel)` | 置顶面板 | `stack.Remove(panel)` + `stack.Add(panel)`（移到栈尾）+ `panel.transform.SetAsLastSibling()`（同层渲染最后/最上） |
| `void PlayOpenTransition(UIPanelBase panel, UIOpenOptions options)` | 播放打开过渡 | panel/options/Transition/root 任一为空或 GO 未激活则返回；否则 `root.StartCoroutine(options.Transition.PlayOpen(panel))` |
| `void RemoveCached(UIPanelBase panel)` | 从缓存移除面板 | 遍历 `cachedPanels` 找 value==panel 的 key 并 `Remove`（按值反查，O(n)） |
| `UIRoute GetRoute(string route)` | 取路由（带校验） | route 空白 → `FrameException("UI route is empty.")`；字典无键 → `FrameException("UI route is not registered: "+route)` |
| `static void ValidateRoutePanelType<TPanel>(UIRoute route)` | 路由类型校验（泛型入口） | 转发到 `ValidateRoutePanelType(typeof(TPanel), route)` |
| `static void ValidateRoutePanelType(Type expectedType, UIRoute route)` | 路由类型校验 | `expectedType==null` 或 `==typeof(UIPanelBase)` 时跳过；否则若 `!IsAssignableFrom(route.PanelType) && route.PanelType!=expectedType` → `FrameException("UI route panel type mismatch: "+route.Route)` |
| `static void ValidateOpen(Type panelType, string resourcesPath)` | 打开前参数校验 | `panelType` 为空或不继承 `UIPanelBase` → `FrameException("UI panel type must inherit UIPanelBase.")`；path 空白 → `FrameException("UI resources path is empty.")` |
| `static UIOpenOptions ResolveOptions(UIOpenOptions options)` | 选项归一化 | `options==null ? Default() : options.Clone()`——**始终返回副本**，避免外部对象被内部修改 |
| `static string GetCacheKey(string route, string resourcesPath)` | 计算缓存键 | route 非空白用 route，否则用 resourcesPath |
| `void OpenNextQueuedPanel()` | 推进弹窗队列 | 若 `queuedActivePanel!=null && queuedActivePanel.IsOpen` 直接返回（已有活动弹窗）；`while` 队列非空：`Dequeue` → `GetRoute`+`ValidateRoutePanelType`+`OpenInternal`；打开失败 `Complete(null,"Failed to open queued UI route: "+route)` 继续下一个；成功则置 `queuedActivePanel=panel`、`Complete(panel,null)` 并 return；异常 `FrameLog.Exception`+`Complete(null, exception.Message)` |

#### 嵌套队列项类型

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private class QueuedPanelOpen` | 弱类型队列项 | 字段 `request:UIPanelRequest<UIPanelBase>`；构造接收 `(Type panelType, string route, object args, request)`；属性 `PanelType/Route/Args`（private set）；`virtual void Complete(UIPanelBase panel, string error)` → `request.Complete(panel, error)` |
| `private sealed class QueuedPanelOpen<TPanel> : QueuedPanelOpen` where `TPanel:UIPanelBase` | 强类型队列项 | 自带 `request:UIPanelRequest<TPanel>`；构造 `base(panelType, route, args, null)`（基类 request 传 null）；`override Complete` → `request.Complete(panel as TPanel, error)`（下转失败则 Panel 为 null） |

---

### `UIRoot.cs`

UI 根节点 MonoBehaviour，`[RequireComponent]` 强制带 `Canvas`/`CanvasScaler`/`GraphicRaycaster`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `readonly Dictionary<UILayer, RectTransform> layers` | 各层缓存 | 懒创建，`GetLayer` 维护 |
| `Canvas Canvas { get; private set; }` | 主 Canvas | `Initialize` 中赋值 |
| `void Initialize(FrameSettings settings)` | 初始化根节点 | 取 `Canvas` 并设 `renderMode=ScreenSpaceOverlay`、`sortingOrder=0`；`CanvasScaler` 设 `ScaleWithScreenSize`、`referenceResolution=settings.UIReferenceResolution`、`matchWidthOrHeight=settings.UIMatchWidthOrHeight`；`GetComponent<GraphicRaycaster>()`（确保存在）；`EnsureEventSystem()`；`EnsureAllLayers()` |
| `RectTransform GetLayer(UILayer layer)` | 获取/创建某层 | 字典命中且非空直接返回；否则 `new GameObject(layer.ToString(), RectTransform, Canvas, GraphicRaycaster)`，`SetParent(this.transform,false)`，全屏拉伸；子 Canvas 设 `overrideSorting=true`、`sortingOrder=(int)layer`；写入字典并返回 |
| `void EnsureAllLayers()` | 预创建全部层 | 依次 `GetLayer(Background/Normal/Popup/Tips/Loading/System)`，保证渲染顺序与 sibling 顺序一致 |
| `static void EnsureEventSystem()` | 确保 EventSystem | 若 `EventSystem.current!=null` 直接返回；否则按编译宏创建：`ENABLE_INPUT_SYSTEM` 时用 `EventSystem`+`InputSystemUIInputModule`，否则用 `EventSystem`+`StandaloneInputModule`；新建对象 `DontDestroyOnLoad` |

---

### `UILayer.cs`

UI 分层枚举，**枚举整型值直接用作各层子 Canvas 的 `sortingOrder`**（在 `UIRoot.GetLayer` 中 `sortingOrder=(int)layer`），数值越大越靠上层（越后渲染、越先接收射线）。

| 枚举值 | sortingOrder | 作用 |
| --- | --- | --- |
| `Background` | `0` | 最底层背景（场景底图、3D 摄像机叠加界面等） |
| `Normal` | `100` | 常规主界面（默认层，`UILayer.Normal` 为多数 API 的默认值） |
| `Popup` | `200` | 弹窗层（对话框、确认框，弹窗队列默认目标） |
| `Tips` | `300` | 提示/飘字层（toast、tooltip） |
| `Loading` | `400` | 加载界面层（覆盖普通弹窗） |
| `System` | `500` | 系统级最高层（断线重连、强制更新、全局错误弹窗） |

---

### `UIPanelBase.cs`

非泛型面板基类，`[RequireComponent(typeof(RectTransform))]`。`Internal*` 方法均为 `internal`，仅供 `UIService` 驱动；业务侧只重写四个 `protected virtual` 钩子。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private bool created` | 是否已创建标志 | 保证 `OnCreate` 只执行一次 |
| `UIPanelContext Context { get; private set; }` | 面板上下文 | `InternalCreate` 赋值，`InternalDispose` 置 null |
| `bool IsOpen { get; private set; }` | 是否处于打开态 | `InternalOpen` 置 true，`InternalClose` 置 false |
| `internal void InternalCreate(UIPanelContext context)` | 内部创建 | 赋 `Context`；若 `!created` 则置 `created=true` 并调用 `OnCreate()`（一次性） |
| `internal void InternalOpen(object args)` | 内部打开 | `IsOpen=true`，`gameObject.SetActive(true)`，调用 `OnOpen(args)`（每次打开都执行） |
| `internal bool InternalClose(bool deactivate = true)` | 内部关闭 | 若 `!IsOpen` 返回 false；否则 `IsOpen=false`、`OnClose()`，`deactivate` 时 `SetActive(false)`，返回 true（**`UIService` 关闭时传 `false` 以便过渡期间保持显示**） |
| `internal void InternalSetClosed()` | 内部置为已关闭 | `gameObject.SetActive(false)`；过渡结束后由 `FinishClose` 调用真正停用 |
| `internal void InternalDispose()` | 内部销毁 | 调用 `OnDispose()` 并把 `Context=null` |
| `public void Close(bool destroy = false)` | 面板自关闭 | `Context!=null` 时 `Context.Service.Close(this, destroy)` |
| `protected virtual void OnCreate()` | 钩子：首次创建 | **每个 cacheKey 仅一次**；适合一次性绑定引用、查找子节点 |
| `protected virtual void OnOpen(object args)` | 钩子：每次打开 | 每次 Open 都触发；适合刷新数据、播放进入动画 |
| `protected virtual void OnClose()` | 钩子：每次关闭 | 每次关闭触发；适合停止协程、保存临时状态 |
| `protected virtual void OnDispose()` | 钩子：销毁前 | 仅 `destroy=true` 关闭时触发；适合释放外部资源 |

---

### `UIPanelBaseGeneric.cs`

强类型参数面板基类 `UIPanelBase<TArgs> : UIPanelBase`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `protected sealed override void OnOpen(object args)` | 重写并密封基类 `OnOpen` | `args==null` → `OnOpen(default(TArgs))`；`!(args is TArgs)` → `throw new ArgumentException("UI panel args type mismatch. Expected: "+typeof(TArgs).FullName, "args")`；否则 `OnOpen((TArgs)args)`。**`sealed` 阻止子类再覆盖 `object` 版本** |
| `protected virtual void OnOpen(TArgs args)` | 钩子：强类型打开参数 | 子类重写此版本即可拿到已转型的 `TArgs`，无需自己拆箱与判空 |

---

### `UIPanelContext.cs`

面板上下文 `sealed class`，封装一次打开所需的全部信息，由 `UIService` 在创建/复用时构造或 `Update`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `UIPanelContext(UIService service, string route, string assetPath, UIOpenOptions options, object args)` | 构造 | `Options` 取 `options==null?Default():options.Clone()`（**始终 Clone**） |
| `UIService Service { get; private set; }` | 所属服务 | 供 `panel.Close()` 回调 |
| `string Route { get; private set; }` | 路由名 | 直接打开（非路由）时为 null |
| `string AssetPath { get; private set; }` | 资源路径 | 预制体 resourcesPath |
| `UILayer Layer { get; }` | 所在层 | 返回 `Options.Layer` |
| `object Args { get; private set; }` | 打开参数（装箱） | 由 `GetArgs<T>` 取回 |
| `UIOpenOptions Options { get; private set; }` | 打开选项 | 上下文持有的副本 |
| `bool IsModal { get; }` | 是否模态 | `Options!=null && Options.Modal` |
| `bool AllowBack { get; }` | 是否允许返回 | `Options==null || Options.AllowBack`（默认允许）；`Back()` 据此筛选 |
| `GameObject ModalBlocker { get; private set; }` | 模态遮罩对象 | 由 `SetModalBlocker` 维护 |
| `TArgs GetArgs<TArgs>()` | 取强类型参数 | `Args==null` 返回 `default(TArgs)`，否则 `(TArgs)Args`（类型不符会抛 `InvalidCastException`） |
| `internal void Update(string route, string assetPath, UIOpenOptions options, object args)` | 复用时更新上下文 | 缓存命中重开时刷新 Route/AssetPath/Options(Clone)/Args |
| `internal void SetModalBlocker(GameObject blocker)` | 设置遮罩引用 | 由 `PrepareModalBlocker/RemoveModalBlocker` 调用 |

---

### `UIOpenOptions.cs`

打开选项 `sealed class`，所有字段为自动属性并带 C# 默认值初始化器。

| 成员 | 作用 | 实现/注意点（含默认值） |
| --- | --- | --- |
| `UILayer Layer { get; set; }` | 目标层 | 默认 `UILayer.Normal` |
| `bool Cache { get; set; }` | 是否缓存复用 | 默认 `true`；false 则每次新建、关闭不入缓存 |
| `bool Modal { get; set; }` | 是否模态 | 默认 `false` |
| `bool CloseOnBackdrop { get; set; }` | 点击遮罩关闭 | 默认 `false`；仅 Modal 时遮罩存在才有意义 |
| `bool AllowBack { get; set; }` | 是否参与 `Back()` | 默认 `true` |
| `Color ModalColor { get; set; }` | 遮罩颜色 | 默认 `new Color(0,0,0,0.55f)`（半透明黑） |
| `IUITransition Transition { get; set; }` | 打开/关闭过渡 | 默认 `null`（无过渡） |
| `static UIOpenOptions Default()` | 默认选项工厂 | `return new UIOpenOptions()`（全用默认值） |
| `UIOpenOptions Clone()` | 浅拷贝 | 复制全部 7 个字段（含 `Transition` 引用，过渡对象本身不深拷贝） |

---

### `UIRoute.cs`

路由定义 `sealed class`，构造后**不可变**（属性 private set）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `UIRoute(string route, string resourcesPath, Type panelType, UIOpenOptions options = null)` | 构造 | route 空白 → `ArgumentException("UI route is required.","route")`；path 空白 → `ArgumentException("UI route resources path is required.","resourcesPath")`；`panelType` 为空或不继承 `UIPanelBase` → `ArgumentException("UI route panel type must inherit UIPanelBase.","panelType")`；`Options` 取 `options==null?Default():options.Clone()`（**始终 Clone**） |
| `string Route { get; private set; }` | 路由名 | 路由表 key |
| `string ResourcesPath { get; private set; }` | 资源路径 | 预制体路径 |
| `Type PanelType { get; private set; }` | 面板类型 | 打开时实例化的目标组件类型 |
| `UIOpenOptions Options { get; private set; }` | 默认打开选项 | 该路由的预设选项副本 |

---

### `UIPanelRequest.cs`

异步打开句柄 `UIPanelRequest<TPanel> : CustomYieldInstruction`，可直接 `yield return`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `override bool keepWaiting { get; }` | 协程是否继续等待 | 返回 `!IsDone`；`yield return request` 时协程挂起直到完成 |
| `bool IsDone { get; private set; }` | 是否已完成 | `Complete` 置 true |
| `bool Success { get; }` | 是否成功 | `Panel != null` |
| `TPanel Panel { get; private set; }` | 结果面板 | 失败为 null |
| `string Error { get; private set; }` | 错误信息 | 失败时填充，成功为 null |
| `internal void Complete(TPanel panel, string error = null)` | 完成请求 | 设 `Panel/Error/IsDone=true`；由服务与队列项调用 |

---

### `IUITransition.cs`

过渡动画抽象接口。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `IEnumerator PlayOpen(UIPanelBase panel)` | 打开过渡协程 | 由 `PlayOpenTransition` 经 `root.StartCoroutine` 驱动 |
| `IEnumerator PlayClose(UIPanelBase panel)` | 关闭过渡协程 | 由 `CloseWithTransition` 先 `yield` 再 `FinishClose` |

---

### `UIFadeTransition.cs`

内置淡入淡出过渡 `sealed class : IUITransition`，操作面板上的 `CanvasGroup.alpha`。它会优先通过 `Framework.TryResolve<ITweenService>` 使用补间服务；没有补间服务时用内置协程按同一套 `TweenEase`/`AnimationCurve` 计算。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `UIFadeTransition(float duration = 0.18f, bool unscaledTime = true, TweenEase ease = TweenEase.OutQuad)` | 构造 | 把 `duration` 同时赋给 `OpenDuration` 与 `CloseDuration`，`UseUnscaledTime=unscaledTime`，打开/关闭 ease 同步设置 |
| `float OpenDuration { get; set; }` | 淡入时长 | 单位秒 |
| `float CloseDuration { get; set; }` | 淡出时长 | 单位秒 |
| `bool UseUnscaledTime { get; set; }` | 是否用不受时间缩放影响的时间 | 默认 true，保证 `Time.timeScale=0`（暂停）时 UI 过渡仍正常 |
| `IEnumerator PlayOpen(UIPanelBase panel)` | 淡入 | `Fade(panel, 0f, 1f, OpenDuration)` |
| `IEnumerator PlayClose(UIPanelBase panel)` | 淡出 | `Fade(panel, 1f, 0f, CloseDuration)` |
| `private IEnumerator Fade(UIPanelBase panel, float from, float to, float duration)` | 通用渐变 | panel 为 null 直接 `yield break`；取/补 `CanvasGroup`；`duration<=0` 直接设 `alpha=to`；否则按 `UseUnscaledTime?unscaledDeltaTime:deltaTime` 累加，`alpha=Mathf.Lerp(from,to,Clamp01(elapsed/duration))`，每帧 `yield return null`，循环中 `panel` 被销毁则中断，结束后若 panel 仍存在则 `alpha=to` |

---

### `SafeAreaFitter.cs`

刘海屏 / 异形屏安全区适配 `sealed class : MonoBehaviour`，`[RequireComponent(typeof(RectTransform))]`。与 `UIService` 无直接耦合，可挂在任意需要避让安全区的面板根节点。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private RectTransform rectTransform` | 自身 RectTransform | `Awake` 缓存 |
| `private Rect lastSafeArea` | 上次安全区 | 用于变化检测 |
| `private void Awake()` | 初始化 | 缓存 `rectTransform` 并立即 `Apply()` |
| `private void Update()` | 每帧检测 | `lastSafeArea != Screen.safeArea` 才 `Apply()`（避免每帧重算锚点） |
| `private void Apply()` | 应用安全区 | 记录 `Screen.safeArea`；用安全区 `position`/`position+size` 除以 `Screen.width/height` 归一化为 `anchorMin/anchorMax`，使 RectTransform 收缩到安全区内 |

---

### 流转逻辑

#### (a) 打开面板流程（同步 `OpenInternal`）

1. **校验**：`ValidateOpen(panelType, resourcesPath)`——`panelType` 必须非空且继承 `UIPanelBase`，否则抛 `FrameException("UI panel type must inherit UIPanelBase.")`；`resourcesPath` 不能空白，否则抛 `FrameException("UI resources path is empty.")`。
2. **克隆选项**：`resolvedOptions = ResolveOptions(options)`——`options==null` 取 `Default()`，否则 `options.Clone()`。**之后所有逻辑都用这份副本**，外部对象不会被内部修改。
3. **计算缓存键**：`cacheKey = GetCacheKey(route, resourcesPath)`——有 route 用 route，否则用 resourcesPath。
4. **缓存命中分支**：当 `resolvedOptions.Cache==true` 且 `cachedPanels[cacheKey]` 存在且非空：
   - 若缓存对象 `!panelType.IsInstanceOfType(cachedPanel)`：打印 `"Cached UI panel type mismatch: "+cacheKey+" expected="+panelType.Name` 并返回 null。
   - 否则 `try`：`Context.Update(route, path, resolvedOptions, args)`（刷新上下文）→ `PrepareModalBlocker` → `cachedPanel.InternalOpen(args)`（重新激活 + `OnOpen`，**不再触发 `OnCreate`**）→ `BringToTop` → `PlayOpenTransition` → 返回缓存面板。
   - `catch`：`FrameLog.Exception` → `RemoveModalBlocker` → `InternalClose()` → 返回 null。
5. **加载分支（未命中）**：`instance = assets==null ? null : assets.Instantiate(resourcesPath, root.GetLayer(layer), false)`。`assets` 为 null 或实例化失败 → 打印 `"Failed to open UI: "+resourcesPath` 返回 null。
6. **从实例创建（`CreatePanelFromInstance`）**：
   - `instance.GetComponent(panelType) as UIPanelBase`，**取不到面板组件**则 `Destroy(instance)` + 警告 `"UI prefab does not contain panel component: "+path+" type="+panelType.Name` 返回 null。
   - 取/补 `RectTransform`，`SetParent(layer,false)` 并**全屏拉伸**（`anchorMin=zero`、`anchorMax=one`、`offsetMin/offsetMax=zero`）。
   - `try`：`InternalCreate(new UIPanelContext(this,route,path,options,args))`（首次触发 `OnCreate`）→ `PrepareModalBlocker` → `InternalOpen(args)`（激活 + `OnOpen`）。异常则回滚：`RemoveModalBlocker` + `InternalClose(false)` + `InternalDispose` + `Destroy(instance)`，返回 null。
   - `stack.Add(panel)` → `BringToTop(panel)`（移到栈尾 + `SetAsLastSibling`）。
   - 若 `options.Cache` → `cachedPanels[cacheKey]=panel`。
   - `PlayOpenTransition(panel, options)`（有 Transition 且 GO 激活才 `root.StartCoroutine(Transition.PlayOpen)`）。
   - 返回 panel。

#### (b) 关闭面板流程（`CloseInternal` → `FinishClose`）

1. `panel==null` 直接返回。
2. 取 `options`（无 Context 用 `Default()`）。
3. `stack.Remove(panel)` 从后退栈移除；`RemoveModalBlocker(panel)` 销毁遮罩。
4. `wasOpen = panel.InternalClose(false)`——**传 `deactivate=false`**：触发 `OnClose`、置 `IsOpen=false`，但**不立即停用 GameObject**（为关闭过渡保留可见性）。若面板本就未打开（`wasOpen==false`）且不销毁，直接返回。
5. **过渡判定**：非 `immediate` 且 `options.Transition!=null` 且 `root!=null` 且 `panel.gameObject.activeInHierarchy` → `root.StartCoroutine(CloseWithTransition(panel,destroy,Transition))`，协程内先 `yield return Transition.PlayClose(panel)` 再 `FinishClose`；否则**直接 `FinishClose`**。
6. **`FinishClose`**：记录 `wasQueuedActive = panel==queuedActivePanel` → `InternalSetClosed()`（真正 `SetActive(false)`）→ 若 `destroy`：`RemoveCached`（按值反查移除缓存）+ `InternalDispose`（`OnDispose` + 清 Context）+ `Object.Destroy(panel.gameObject)`。
7. **推进弹窗队列**：若关闭的是当前活动排队弹窗，置 `queuedActivePanel=null`，且未处于 `suppressQueuedOpen` 时调用 `OpenNextQueuedPanel()` 弹出下一个。

#### (c) 后退栈（`Back`）

- `stack` 末尾为最上层。`Back()` 从 `stack.Count-1` 向 0 遍历，找到首个满足 `panel!=null && panel.Context!=null && panel.Context.AllowBack` 的面板，调用 `Close(panel, destroy)` 并返回 `true`。
- 若遍历完没有可返回面板（全部 `AllowBack=false` 或栈空），返回 `false`。`AllowBack` 来源于 `Options.AllowBack`（默认 true）。

#### (d) 模态遮罩（`PrepareModalBlocker`）

- 先 `RemoveModalBlocker`（幂等，避免重复遮罩）。若 `!options.Modal` 或参数不全则不创建。
- 在 `options.Layer` 对应层新建 `{panelName}_ModalBlocker`，挂 `RectTransform`+`CanvasRenderer`+`Image`，**全屏拉伸**。`image.color=options.ModalColor`（默认半透明黑 (0,0,0,0.55)），`raycastTarget=true`——**拦截穿透点击**。
- 若 `CloseOnBackdrop`：额外加 `Button`（`transition=Selectable.Transition.None`，无视觉反馈），`onClick` 监听 `() => Close(panel)`，实现点击遮罩关闭面板。
- 遮罩对象引用存入 `panel.Context.SetModalBlocker(blocker)`，关闭时由 `RemoveModalBlocker` 销毁。
- **注意层级顺序**：遮罩与面板在同一层，遮罩先于面板创建（在 `InternalCreate` 之后、`BringToTop` 之前 prepare），随后 `BringToTop` 把**面板**移到 sibling 最后，使面板渲染在遮罩之上、遮罩遮住其下方内容。

#### (e) 弹窗队列（`EnqueueRoute` → `OpenNextQueuedPanel`）

- `EnqueueRoute*` 先 `GetRoute` 校验路由（与类型），构造 `QueuedPanelOpen`（弱类型）或 `QueuedPanelOpen<TPanel>`（强类型）入队，再立即 `OpenNextQueuedPanel()`。
- `OpenNextQueuedPanel`：**若已有活动弹窗且仍 `IsOpen`（`queuedActivePanel.IsOpen`）则直接返回**——保证同一时刻只有一个排队弹窗活动。否则循环 `Dequeue`：取路由 → 校验类型 → `OpenInternal`。打开失败用 `Complete(null,"Failed to open queued UI route: "+route)` 并继续下一个；成功则置 `queuedActivePanel=panel`、`Complete(panel,null)` 并 return（停止循环，等这个关闭后再弹下一个）；异常则 `Complete(null, exception.Message)` 继续。
- 活动弹窗关闭时，`FinishClose` 检测到 `wasQueuedActive` → 置空 `queuedActivePanel` → `OpenNextQueuedPanel` 弹出下一个，形成串行播放链。
- `ClearQueuedPanels()` 清空**等待队列**（不含已激活弹窗），每项 `Complete(null,"UI panel queue was cleared.")`。`QueuedPanelCount` 仅反映等待队列长度。

#### (f) 异步打开（`OpenInternalAsync`，返回可 yield 的 `UIPanelRequest`）

- `OpenAsync*`/`OpenRouteAsync*` 立即 `new UIPanelRequest<TPanel>()` 并返回，**实际加载在后台进行**。请求继承 `CustomYieldInstruction`，协程 `yield return request` 会挂起到 `IsDone`。
- `OpenInternalAsync`：校验 + ResolveOptions + cacheKey。
  - **缓存命中**：直接走同步 `OpenInternal` 并 `request.Complete(panel as TPanel, panel==null?"Cached UI panel type mismatch.":null)`。
  - `assets==null`：`Complete(null,"Asset service is not available.")`。
  - 否则 `assets.LoadAsync<GameObject>(path, handle => {...})`：句柄无效（`handle==null || !handle.IsValid`）→ `Complete(null,"Failed to load UI asset: "+path)`；有效则 `Object.Instantiate(handle.Asset, layer, false)` → `handle.Release()` → `CreatePanelFromInstance` → `Complete(panel as TPanel, panel==null?"Failed to create UI panel: "+path:null)`；回调内异常 `FrameLog.Exception` + `Complete(null, exception.Message)`。
- 完成后业务可读 `request.Success`/`request.Panel`/`request.Error`。

---

### 使用示例

```csharp
using Frame.Core;
using Frame.UI;
using UnityEngine;

// 1) 基础面板（弱类型参数）
public sealed class MainMenuPanel : UIPanelBase
{
    protected override void OnCreate()
    {
        // 仅首次创建执行一次：绑定子节点、注册按钮
    }

    protected override void OnOpen(object args)
    {
        // 每次打开执行：刷新数据
    }

    protected override void OnClose() { /* 停止协程等 */ }
    protected override void OnDispose() { /* 释放外部资源 */ }
}

// 2) 强类型参数面板
public struct ShopArgs { public int CategoryId; }

public sealed class ShopPanel : UIPanelBase<ShopArgs>
{
    protected override void OnOpen(ShopArgs args)
    {
        Debug.Log("打开商店分类: " + args.CategoryId);
    }
}

public static class UIDemo
{
    public static void Direct()
    {
        IUIService ui = Framework.Resolve<IUIService>();

        // 3) 直接打开（同步）
        ui.Open<MainMenuPanel>("UI/MainMenu", UILayer.Normal);
        ui.Open<ShopPanel, ShopArgs>("UI/Shop", new ShopArgs { CategoryId = 3 });

        // 完整选项：模态 + 点击遮罩关闭 + 淡入淡出
        var options = UIOpenOptions.Default();
        options.Layer = UILayer.Popup;
        options.Modal = true;
        options.CloseOnBackdrop = true;
        options.Transition = new UIFadeTransition(0.2f, true);
        ui.Open<MainMenuPanel>("UI/Confirm", options);
    }

    public static void Routes()
    {
        IUIService ui = Framework.Resolve<IUIService>();

        // 4) 注册路由 + 打开路由
        ui.RegisterRoute<ShopPanel>(
            route: "shop",
            resourcesPath: "UI/Shop",
            layer: UILayer.Normal,
            cache: true,
            modal: false,
            transition: new UIFadeTransition());

        ui.OpenRoute<ShopPanel, ShopArgs>("shop", new ShopArgs { CategoryId = 1 });

        // 5) 入队弹窗（串行播放，前一个关闭后自动弹下一个）
        ui.RegisterRoute<MainMenuPanel>("dailyReward", "UI/DailyReward", UILayer.Popup, modal: true);
        ui.RegisterRoute<MainMenuPanel>("signIn", "UI/SignIn", UILayer.Popup, modal: true);
        ui.EnqueueRoute("dailyReward");
        ui.EnqueueRoute("signIn"); // 排在 dailyReward 关闭后
    }

    // 6) 异步打开 + yield 等待
    public static System.Collections.IEnumerator OpenAsyncCoroutine()
    {
        IUIService ui = Framework.Resolve<IUIService>();
        UIPanelRequest<ShopPanel> request = ui.OpenAsync<ShopPanel>("UI/Shop", UILayer.Normal);
        yield return request; // 挂起直到加载/实例化完成
        if (request.Success)
        {
            ShopPanel panel = request.Panel;
            // ...
        }
        else
        {
            Debug.LogWarning("打开失败: " + request.Error);
        }
    }
}
```

---

### 设计意图与踩坑点

- **为什么 `UIOpenOptions` 总被 Clone**：`ResolveOptions`、`UIRoute` 构造、`UIPanelContext` 构造与 `Update` 都对传入的 options 调用 `Clone()`。这样外部传进来的同一个 options 对象在多次打开/复用之间不会被内部相互影响，也避免业务侧后续修改原对象波及已打开面板的上下文。注意 `Clone()` 是**浅拷贝**：`Transition` 是引用复制，多个面板可能共享同一过渡实例（过渡是无状态的协程工厂，通常安全）。
- **为什么每层有独立 Canvas + `overrideSorting`**：`UIRoot.GetLayer` 为每层建子 `Canvas` 并设 `overrideSorting=true`、`sortingOrder=(int)layer`。这让各层渲染顺序**只由 `UILayer` 数值决定**（Background<Normal<Popup<Tips<Loading<System），与父 Canvas 的合批/排序解耦；每层带独立 `GraphicRaycaster`，射线命中也按层独立处理。层内的相对顺序则由 sibling 顺序控制（`BringToTop` 用 `SetAsLastSibling`）。
- **缓存键碰撞风险**：`GetCacheKey` 优先用 route、否则用 `resourcesPath`。这意味着**同一预制体路径在不同层/不同选项下打开会命中同一缓存项**（key 不含 layer/options）。命中时若 `panelType` 与缓存对象类型不符会被检测（打印 `"Cached UI panel type mismatch"` 返回 null），但**层与选项不一致不会报错**——缓存复用会沿用上次实例所在层（实际由 `Context.Update` 刷新 options，但已实例化对象的父层不会被重新 SetParent）。若需要在不同上下文复用同一预制体，建议用不同 route 名或关闭缓存（`cache:false`）。
- **面板类型校验分两道**：打开前 `ValidateOpen` 保证 `panelType` 继承 `UIPanelBase`；路由打开/入队时 `ValidateRoutePanelType` 保证请求的 `TPanel` 与 `route.PanelType` 兼容（`expectedType==UIPanelBase` 时跳过，弱类型 `OpenRoute(string)` 不校验）；运行时 `CreatePanelFromInstance` 再用 `GetComponent(panelType)` 实际确认预制体挂了对应组件，三道防线层层兜底。
- **EventSystem 的创建（输入系统分支）**：`EnsureEventSystem` 仅在 `EventSystem.current==null` 时创建，并按编译宏选择输入模块——定义了 `ENABLE_INPUT_SYSTEM` 用 `InputSystemUIInputModule`（新输入系统），否则用 `StandaloneInputModule`（旧输入系统）。新建的 EventSystem 走 `DontDestroyOnLoad`，跨场景常驻。**踩坑**：若项目同时启用新旧输入系统但宏配置不当，可能出现 UI 点击无响应；另外场景里若已有 EventSystem，框架不会重复创建。
- **`OnCreate`（一次性）vs `OnOpen`（每次）的生命周期顺序**：`InternalCreate` 用 `created` 标志保证 `OnCreate` **每个缓存实例仅一次**，随后每次打开（含缓存复用重开）都走 `InternalOpen` → `OnOpen`。因此**一次性初始化（查找子节点、绑定事件）放 `OnCreate`，数据刷新放 `OnOpen`**。缓存复用时不会再 `OnCreate`，若把刷新逻辑误放在 `OnCreate` 会导致第二次打开数据不更新。
- **关闭与过渡的两段式**：`InternalClose(false)` 先触发 `OnClose` 但保留 GO 激活以便播放关闭过渡；过渡协程结束（或无过渡直接）才 `InternalSetClosed()` 真正停用，`destroy` 时再 `OnDispose`+`Destroy`。`OnDispose` 仅在销毁路径触发——**非销毁关闭（默认 `destroy=false`）只停用不释放，面板留在缓存等待复用**。
- **`assets` 可空的连锁反应**：资源模块被禁用时 `TryResolve` 失败，`assets==null`。此时同步打开打印 `"Failed to open UI: ..."` 返回 null，异步打开 `Complete(null,"Asset service is not available.")`。UI 模块本身仍能创建 Root 与层，但无法加载任何预制体面板。
- **`suppressQueuedOpen` 的作用**：`CloseAll`/`OnShutdown` 期间批量关闭会逐个触发 `FinishClose`，若不抑制，每关掉一个活动弹窗就会尝试弹下一个，与「全部关闭」意图冲突。该标志在批量操作期间临时关闭队列自动推进。

## 17. Save 模块

Save 模块提供基于本地文件的存档系统，存档根目录为 `Application.persistentDataPath/<SaveFolderName>`（`SaveFolderName` 取自 `Context.Settings.SaveFolderName`，在 `OnInitialize` 时通过 `Path.Combine` 拼接并 `Directory.CreateDirectory` 确保存在）。核心能力：

- **可插拔序列化器**：默认 `NewtonsoftSaveSerializer`（JSON，`.json`），可切换为 `BinarySaveSerializer`（DataContract 二进制 XML，`.bin`），或自定义实现 `ISaveSerializer` / 继承 `TextSaveSerializer`。存档文件扩展名由"当前序列化器"的 `FileExtension` 动态决定。
- **可选 AES 加密**：注入 `AesSaveEncryptor` 后，写入前对字节流加密、读取后解密；不注入则明文。加密标志记录在元数据 `Encrypted` 中。
- **多槽位（multi-slot）**：以 `slot` 字符串为键，每个槽位对应一个独立文件（文件名经 `FramePathUtility.SanitizeFileName` 清洗非法字符）。
- **元数据 + SHA-256 校验**：每个存档写入同名 `.meta` 伴随文件（JSON，`JsonUtility` 序列化），记录格式版本、数据版本、序列化器扩展名、加密标志、负载字节数、SHA-256 摘要。读取时做大小 + 摘要校验，检测篡改/损坏。
- **原子写入 + 备份**：先写 `.tmp` 临时文件，若正式文件已存在则将旧文件原子替换为 `.bak` 备份（`File.Replace`），否则直接 `File.Move`；元数据同样走 `.tmp` → 正式的替换流程。读取失败时自动回退到 `.bak`。
- **版本迁移链**：通过 `RegisterMigration<TData>(SaveMigration<TData>)` 注册迁移函数，加载时若存档 `DataVersion` 低于已注册迁移可处理的版本，则按 `FromVersion → ToVersion` 链式逐级升级数据对象。
- **同步 + 异步双 API**：`Save`/`TryLoad`/`Load` 与 `SaveAsync`/`TryLoadAsync`/`LoadAsync`（基于 `System.IO` 的 `*Async` + `CancellationToken`）。

模块为 `SaveService : GameModuleBase, ISaveService`，`Priority = -100`（数值较小，相对其他模块较早初始化），在 `OnInitialize` 中将自身注册为 `ISaveService` 与具体类型 `SaveService` 两份服务到 `Context.Services`。

> 命名空间：`Frame.Save`。依赖 `Frame.Core`（`GameModuleBase`、`FrameLog`）、`Frame.Utilities`（`FramePathUtility`）、`Newtonsoft.Json`、`UnityEngine`（`Application.persistentDataPath`、`JsonUtility`）。

---

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ISaveService` | 存档服务公开接口 | 同步/异步 Save、Load、TryLoad、Delete、Exists、ListSlots、TryGetMetadata、GetPath，以及序列化器/加密器/迁移配置入口。 |
| `SaveService` | 接口实现，`GameModuleBase` 模块 | `Priority = -100`；管理 `saveRoot`、当前 `serializer`、`encryptor`、按类型分组的 `migrations`；负责原子写、备份回退、校验、迁移链。 |
| `ISaveSerializer` | 序列化器接口 | `string FileExtension`、`byte[] Serialize<TData>(TData)`、`TData Deserialize<TData>(byte[])`。所有持久化都以 `byte[]` 为最终载体。 |
| `TextSaveSerializer` | 文本序列化器抽象基类 | 实现 `ISaveSerializer`，把"文本 ↔ 字节"用 `TextEncoding`（默认 UTF-8）桥接；子类只需实现 `SerializeToText`/`DeserializeFromText`。 |
| `NewtonsoftSaveSerializer` | 默认 JSON 序列化器 | 继承 `TextSaveSerializer`，`FileExtension = ".json"`，使用 Newtonsoft.Json + 预设 `JsonSerializerSettings`（缩进、字符串枚举、允许非公有默认构造等）。 |
| `BinarySaveSerializer` | 二进制序列化器 | 直接实现 `ISaveSerializer`，`FileExtension = ".bin"`，基于 `DataContractSerializer` + `XmlDictionaryWriter/Reader` 二进制格式，默认 `PreserveObjectReferences = true`。 |
| `ISaveEncryptor` | 加密器接口 | `byte[] Encrypt(byte[])`、`byte[] Decrypt(byte[])`，作用于序列化后的字节流。 |
| `AesSaveEncryptor` | AES 加密实现 | 6 字节魔数头 `{70,83,65,69,83,1}`（"FSAES"+1），每次随机 IV 并前置写入，passphrase 经 SHA-256 派生密钥，密钥长度归一化到 16/24/32 字节。 |
| `ISaveVersionedData` | 数据自带版本号接口 | 单只读属性 `int SaveVersion`；让数据对象自身声明版本，无需在 `Save` 调用处显式传 `dataVersion`。 |
| `SaveMigration<TData>` | 单步迁移描述 | 携带 `FromVersion`、`ToVersion`、迁移委托；构造期校验 `toVersion > fromVersion` 且委托非空；`Apply(data)` 执行升级。 |
| `SaveMetadata` | 存档元数据（`.meta` 内容） | `[Serializable]` 类，字段：`FormatVersion`、`Slot`、`SerializerExtension`、`DataVersion`、`SavedAtUtcTicks`、`PayloadSizeBytes`、`PayloadSha256`、`Encrypted`；只读属性 `SavedAtUtc`。 |
| `SaveSlotInfo` | 槽位枚举条目 | `[Serializable]` 类，字段：`Slot`、`Path`、`LastWriteUtcTicks`、`SizeBytes`、`HasMetadata`、`DataVersion`、`Encrypted`、`SerializerExtension`；只读属性 `LastWriteUtc`。 |
| `SaveLoadResult<TData>` | 异步加载结果 | `readonly struct`，字段 `bool Success`、`TData Data`；用于 `TryLoadAsync` 返回值（结构体无法用 `out` 参数返回，故用它承载）。 |

---

### `ISaveService.cs`

存档服务对外契约。所有方法均为泛型方法以 `TData` 承载具体存档数据类型。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `void SetSerializer(ISaveSerializer serializer)` | 设置当前序列化器 | 影响后续读写以及文件扩展名。 |
| `void SetEncryptor(ISaveEncryptor encryptor)` | 设置加密器 | 传 `null` 等于关闭加密。 |
| `void RegisterMigration<TData>(SaveMigration<TData> migration)` | 注册某数据类型的一步迁移 | 类型参数 `TData` 为存档数据类型；按 `FromVersion` 排序累积成迁移链。 |
| `void ClearMigrations<TData>()` | 清空某类型的全部迁移 | 类型参数 `TData`；移除该类型的迁移列表。 |
| `bool Exists(string slot)` | 判断槽位主存档文件是否存在 | 检查 `GetPath(slot)` 对应文件；不检查 `.bak`。 |
| `void Save<TData>(string slot, TData data)` | 同步保存（自动解析版本） | 版本号由 `data` 是否实现 `ISaveVersionedData` 决定，否则为 0。 |
| `void Save<TData>(string slot, TData data, int dataVersion)` | 同步保存（显式版本） | 显式指定写入元数据的 `DataVersion`。 |
| `Task SaveAsync<TData>(string slot, TData data, CancellationToken = default)` | 异步保存（自动解析版本） | 内部转调显式版本重载。 |
| `Task SaveAsync<TData>(string slot, TData data, int dataVersion, CancellationToken = default)` | 异步保存（显式版本） | `async` 实现，使用 `File.WriteAllBytesAsync`/`File.WriteAllTextAsync` + token。 |
| `bool TryLoad<TData>(string slot, out TData data)` | 同步尝试加载 | 成功返回 `true` 并经 `out` 返回数据；主文件失败自动回退 `.bak`。 |
| `Task<SaveLoadResult<TData>> TryLoadAsync<TData>(string slot, CancellationToken = default)` | 异步尝试加载 | 返回 `SaveLoadResult<TData>`（结构体）；主文件失败回退 `.bak`。 |
| `TData Load<TData>(string slot, TData fallback = default)` | 同步加载（带兜底值） | 失败返回 `fallback`；底层调 `TryLoad`。 |
| `Task<TData> LoadAsync<TData>(string slot, TData fallback = default, CancellationToken = default)` | 异步加载（带兜底值） | 失败返回 `fallback`；底层调 `TryLoadAsync`。 |
| `bool Delete(string slot)` | 删除槽位 | 删除主文件、`.bak`、主 `.meta`、`.bak.meta`；任一被删返回 `true`。 |
| `List<SaveSlotInfo> ListSlots()` | 枚举所有槽位 | 按"当前序列化器扩展名"在 `saveRoot` 顶层匹配文件；附带读取各自 `.meta`。 |
| `bool TryGetMetadata(string slot, out SaveMetadata metadata)` | 读取槽位元数据 | 读取主文件对应 `.meta`；无 `.meta` 返回 `false`。 |
| `string GetPath(string slot)` | 计算槽位主文件绝对路径 | `saveRoot + SanitizeFileName(slot) + 当前序列化器扩展名`。 |

---

### `SaveService.cs`

`public sealed class SaveService : GameModuleBase, ISaveService`。

**常量与字段**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `const string TempExtension = ".tmp"` | 临时文件后缀 | 原子写入的中间文件后缀。 |
| `const string BackupExtension = ".bak"` | 备份文件后缀 | 旧版本被替换后保存为 `.bak`，读取失败时回退源。 |
| `const string MetadataExtension = ".meta"` | 元数据文件后缀 | 伴随每个存档文件（路径 = 主文件路径 + `.meta`）。 |
| `const int MetadataFormatVersion = 1` | 元数据格式版本 | 写入 `SaveMetadata.FormatVersion`。 |
| `Dictionary<Type, List<object>> migrations` | 按数据类型分组的迁移链 | value 为 `List<object>`，元素实际为 `SaveMigration<TData>`，使用时强转。 |
| `ISaveSerializer serializer` | 当前序列化器 | `OnInitialize` 初始化为 `NewtonsoftSaveSerializer`。 |
| `ISaveEncryptor encryptor` | 当前加密器 | 默认 `null`（不加密）。 |
| `string saveRoot` | 存档根目录绝对路径 | `Path.Combine(Application.persistentDataPath, Context.Settings.SaveFolderName)`。 |

**生命周期 / 属性**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `override int Priority { get { return -100; } }` | 模块优先级 | 固定 `-100`。 |
| `override void OnInitialize()` | 初始化 | 建 `NewtonsoftSaveSerializer`；拼 `saveRoot` 并 `Directory.CreateDirectory`；注册 `Context.Services.Register<ISaveService>(this)` 与 `Context.Services.Register(this)`。 |
| `override void OnShutdown()` | 关闭清理 | `migrations.Clear()`；`serializer`、`encryptor`、`saveRoot` 置 `null`。 |

**配置类公开方法**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `void SetSerializer(ISaveSerializer serializer)` | 切换序列化器 | 仅当传入非 `null` 才覆盖；传 `null` 静默忽略（保留旧序列化器）。 |
| `void SetEncryptor(ISaveEncryptor encryptor)` | 切换加密器 | 直接赋值，允许传 `null` 以关闭加密。 |
| `void RegisterMigration<TData>(SaveMigration<TData> migration)` | 注册迁移 | `migration == null` 抛 `ArgumentNullException("migration")`；按 `typeof(TData)` 取/建列表，`Add` 后用 `list.Sort` 按 `FromVersion` 升序排序（比较器内将 `object` 强转回 `SaveMigration<TData>`）。 |
| `void ClearMigrations<TData>()` | 清空该类型迁移 | `migrations.Remove(typeof(TData))`。 |

**存在性 / 路径**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool Exists(string slot)` | 主文件是否存在 | 先 `ValidateSlot(slot)`，再 `File.Exists(GetPath(slot))`。 |
| `string GetPath(string slot)` | 槽位主文件路径 | `ValidateSlot`；`fileName = FramePathUtility.SanitizeFileName(slot) + GetSerializerFileExtension()`；`Path.Combine(saveRoot, fileName)`。 |

**保存（同步）**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `void Save<TData>(string slot, TData data)` | 保存（自动版本） | 转调 `Save(slot, data, ResolveDataVersion(data))`。 |
| `void Save<TData>(string slot, TData data, int dataVersion)` | 保存（显式版本） | 计算 `path/tempPath/tempMetadataPath/backupPath`；`serializer.Serialize(data)` → 若 `encryptor != null` 则 `Encrypt`；`CreateMetadata`；`File.WriteAllBytes(tempPath)` + `WriteMetadata(tempMetadataPath)`；若主文件已存在 → `BackupMetadata` + `File.Replace(tempPath, path, backupPath, true)`，否则 `File.Move(tempPath, path)`；最后 `ReplaceMetadata(tempMetadataPath, 主.meta)`；`catch` 块删除残留 `.tmp`/`.tmp.meta` 后 `throw`（重抛原异常）。 |

**保存（异步）**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Task SaveAsync<TData>(string slot, TData data, CancellationToken = default)` | 异步保存（自动版本） | 转调显式版本重载（同步返回该 `Task`）。 |
| `async Task SaveAsync<TData>(string slot, TData data, int dataVersion, CancellationToken = default)` | 异步保存（显式版本） | 流程同同步版，但用 `await File.WriteAllBytesAsync` / `await WriteMetadataAsync`；序列化前后及写完后均 `cancellationToken.ThrowIfCancellationRequested()`；`catch` 同样清理 `.tmp`/`.tmp.meta` 并重抛。 |

**加载**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool TryLoad<TData>(string slot, out TData data)` | 同步尝试加载 | `ValidateSlot`；先 `TryLoadFromPath(path)`，失败再 `TryLoadFromPath(path + ".bak")`。 |
| `async Task<SaveLoadResult<TData>> TryLoadAsync<TData>(string slot, CancellationToken = default)` | 异步尝试加载 | `ValidateSlot`；先 `TryLoadFromPathAsync(path)`，`result.Success` 则返回，否则 `await TryLoadFromPathAsync(path + ".bak")`。 |
| `TData Load<TData>(string slot, TData fallback = default)` | 同步加载兜底 | `TryLoad` 成功返回 `data`，否则 `fallback`。 |
| `async Task<TData> LoadAsync<TData>(string slot, TData fallback = default, CancellationToken = default)` | 异步加载兜底 | `await TryLoadAsync`，`Success` 返回 `result.Data`，否则 `fallback`。 |

**删除 / 枚举 / 元数据**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool Delete(string slot)` | 删除槽位全部文件 | `ValidateSlot`；逐一存在性检查并删除：主文件、`.bak`、`GetMetadataPath(主)`、`GetMetadataPath(.bak)`；任何一个被删则 `deleted = true`。 |
| `List<SaveSlotInfo> ListSlots()` | 枚举槽位 | `saveRoot` 不存在则返回空列表；`Directory.GetFiles(saveRoot, "*" + GetSerializerFileExtension(), TopDirectoryOnly)`；每个文件构造 `SaveSlotInfo`（`Slot = GetFileNameWithoutExtension`、`Path`、`LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks`、`SizeBytes = info.Length`）；若 `TryReadMetadata` 成功，则把 `result[last]`（引用类型，取出即原对象）的 `HasMetadata/DataVersion/Encrypted/SerializerExtension` 写回。注意：只列出"当前序列化器扩展名"匹配的文件。 |
| `bool TryGetMetadata(string slot, out SaveMetadata metadata)` | 读元数据 | `ValidateSlot`；转调 `TryReadMetadata(GetPath(slot))`。 |

**私有辅助方法**

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool TryLoadFromPath<TData>(string path, out TData data)` | 从指定路径同步加载 | 路径空白或不存在返回 `false`；读字节；`metadataFileExists = File.Exists(.meta)`，`hasMetadata = TryReadMetadata`；若 `.meta` 文件存在但解析失败 → `false`（防部分损坏）；有元数据则 `ValidatePayload`（大小+SHA-256）；解密判定 `shouldDecrypt = hasMetadata ? metadata.Encrypted : encryptor != null`，需解密但 `encryptor==null` 抛 `InvalidOperationException("Save data is encrypted but no save encryptor is configured.")`；`serializer.Deserialize<TData>`；`ApplyMigrations(data, hasMetadata ? metadata.DataVersion : 0)`；`catch` 写 `FrameLog.Exception` 后返回 `false`。 |
| `async Task<SaveLoadResult<TData>> TryLoadFromPathAsync<TData>(string path, CancellationToken)` | 从指定路径异步加载 | 与同步版逻辑等价，但用 `await File.ReadAllBytesAsync` / `await File.ReadAllTextAsync`，元数据用 `JsonUtility.FromJson<SaveMetadata>` 解析；元数据存在但解析为 `null` → 返回失败结果；`OperationCanceledException` 直接重抛，其他异常写日志返回 `new SaveLoadResult<TData>(false, default)`。 |
| `SaveMetadata CreateMetadata(string slot, byte[] bytes, int dataVersion)` | 构造元数据 | `FormatVersion = MetadataFormatVersion`；`Slot = slot`；`SerializerExtension = GetSerializerFileExtension()`；`DataVersion = Math.Max(0, dataVersion)`；`SavedAtUtcTicks = DateTime.UtcNow.Ticks`；`PayloadSizeBytes = bytes?.Length ?? 0`；`PayloadSha256 = ComputeSha256(bytes)`；`Encrypted = encryptor != null`。 |
| `TData ApplyMigrations<TData>(TData data, int dataVersion)` | 执行迁移链 | 无该类型迁移或空列表则原样返回；`currentVersion = Math.Max(0, dataVersion)`；`do/while` 循环：每轮线性扫描列表找 `FromVersion == currentVersion` 的迁移，命中则 `data = migration.Apply(data)`、`currentVersion = migration.ToVersion`、`migrated = true; break`；直到一轮无命中。 |
| `void BackupMetadata(string path, string backupPath)` | 备份元数据 | 主 `.meta` 存在则 `File.Copy(主.meta, .bak.meta, true)`；否则若 `.bak.meta` 存在则删除（保持与主存档一致）。 |
| `static void ReplaceMetadata(string tempMetadataPath, string metadataPath)` | 落地元数据 | 目标 `.meta` 存在先删除，再 `File.Move(temp.meta → .meta)`。 |
| `static Task WriteMetadataAsync(string metadataPath, SaveMetadata metadata, CancellationToken)` | 异步写元数据 | `JsonUtility.ToJson(metadata, true)`（缩进）→ `File.WriteAllTextAsync(..., Encoding.UTF8, token)`。 |
| `static void WriteMetadata(string metadataPath, SaveMetadata metadata)` | 同步写元数据 | `JsonUtility.ToJson(metadata, true)` → `File.WriteAllText(..., Encoding.UTF8)`。 |
| `bool TryReadMetadata(string path, out SaveMetadata metadata)` | 读元数据 | `.meta` 不存在返回 `false`；`File.ReadAllText(..., UTF8)` + `JsonUtility.FromJson<SaveMetadata>`，非 `null` 即成功；异常写 `FrameLog.Exception` 返回 `false`。 |
| `static bool ValidatePayload(string path, byte[] bytes, SaveMetadata metadata)` | 校验负载 | 元数据为空或 `PayloadSha256` 空白 → 视为通过（`true`）；大小不符 → `FrameLog.Warning("Save payload size mismatch: " + path)` + `false`；SHA-256 不符（`OrdinalIgnoreCase`）→ `FrameLog.Warning("Save payload checksum mismatch: " + path)` + `false`。 |
| `static int ResolveDataVersion<TData>(TData data)` | 解析数据版本 | `data as ISaveVersionedData`，为 `null` 返回 `0`，否则 `Math.Max(0, versioned.SaveVersion)`。 |
| `static string GetMetadataPath(string path)` | 元数据路径 | `path + ".meta"`。 |
| `static string ComputeSha256(byte[] bytes)` | 计算 SHA-256 | `SHA256.Create().ComputeHash(bytes ?? Array.Empty<byte>())`，逐字节 `ToString("x2")` 拼成小写十六进制字符串。 |
| `string GetSerializerFileExtension()` | 当前序列化器扩展名 | `serializer?.FileExtension`；为空白回退 `".save"`；`Trim()` 后若不以 `.` 开头则补 `.`。 |
| `static void ValidateSlot(string slot)` | 校验 slot | 空/空白抛 `ArgumentException("Save slot is required.", "slot")`。 |

---

### `ISaveSerializer.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `string FileExtension { get; }` | 文件扩展名 | 决定存档文件与 `ListSlots` 匹配的后缀（如 `.json`/`.bin`）。 |
| `byte[] Serialize<TData>(TData data)` | 序列化为字节 | 类型参数 `TData` 为数据类型；返回值为最终写盘字节流。 |
| `TData Deserialize<TData>(byte[] bytes)` | 从字节反序列化 | 类型参数 `TData`；从字节还原对象。 |

---

### `TextSaveSerializer.cs`

`public abstract class TextSaveSerializer : ISaveSerializer`，把"文本表示"与"字节流"解耦的抽象基类。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `abstract string FileExtension { get; }` | 文件扩展名 | 由具体子类提供。 |
| `virtual Encoding TextEncoding { get; }` | 文本编码 | 默认 `Encoding.UTF8`，子类可重写。 |
| `byte[] Serialize<TData>(TData data)` | 文本→字节 | `SerializeToText(data)` 后 `TextEncoding.GetBytes(text ?? string.Empty)`（`null` 文本按空串处理）。 |
| `TData Deserialize<TData>(byte[] bytes)` | 字节→文本→对象 | `bytes` 为空或长度 0 时 `text = string.Empty`，否则 `TextEncoding.GetString(bytes)`，再 `DeserializeFromText<TData>(text)`。 |
| `protected abstract string SerializeToText<TData>(TData data)` | 对象→文本 | 子类实现具体文本格式。 |
| `protected abstract TData DeserializeFromText<TData>(string text)` | 文本→对象 | 子类实现解析。 |

---

### `NewtonsoftSaveSerializer.cs`

`public sealed class NewtonsoftSaveSerializer : TextSaveSerializer`，框架默认序列化器。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `readonly JsonSerializerSettings settings` | JSON 设置 | 构造时确定，序列化/反序列化共用。 |
| `NewtonsoftSaveSerializer()` | 默认构造 | 委托 `: this(CreateDefaultSettings())`。 |
| `NewtonsoftSaveSerializer(JsonSerializerSettings settings)` | 自定义设置构造 | 传 `null` 时回退 `CreateDefaultSettings()`。 |
| `override string FileExtension { get { return ".json"; } }` | 扩展名 | 固定 `.json`。 |
| `protected override string SerializeToText<TData>(TData data)` | 对象→JSON 文本 | `JsonConvert.SerializeObject(data, Formatting.Indented, settings)`（带缩进）。 |
| `protected override TData DeserializeFromText<TData>(string text)` | JSON 文本→对象 | `JsonConvert.DeserializeObject<TData>(text, settings)`。 |
| `static JsonSerializerSettings CreateDefaultSettings()` | 默认设置工厂 | `ContractResolver = new DefaultContractResolver()`；`ConstructorHandling = AllowNonPublicDefaultConstructor`；`DefaultValueHandling = Include`；`MissingMemberHandling = Ignore`；`NullValueHandling = Include`；`ObjectCreationHandling = Replace`；`ReferenceLoopHandling = Ignore`；`TypeNameHandling = None`；并 `Converters.Add(new StringEnumConverter())`（枚举序列化为字符串）。 |

---

### `BinarySaveSerializer.cs`

`public sealed class BinarySaveSerializer : ISaveSerializer`，基于 DataContract 的二进制序列化（直接实现接口，不继承 `TextSaveSerializer`）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `readonly DataContractSerializerSettings settings` | DataContract 设置 | 构造时确定。 |
| `BinarySaveSerializer()` | 默认构造 | 委托 `: this(CreateDefaultSettings())`。 |
| `BinarySaveSerializer(DataContractSerializerSettings settings)` | 自定义设置构造 | 传 `null` 时回退 `CreateDefaultSettings()`。 |
| `string FileExtension { get { return ".bin"; } }` | 扩展名 | 固定 `.bin`。 |
| `byte[] Serialize<TData>(TData data)` | 对象→二进制 | `CreateSerializer(typeof(TData))`；`MemoryStream` + `XmlDictionaryWriter.CreateBinaryWriter`；`serializer.WriteObject(writer, data)`；返回 `stream.ToArray()`。 |
| `TData Deserialize<TData>(byte[] bytes)` | 二进制→对象 | `bytes` 为 `null` 或空抛 `ArgumentException("Binary save data is empty.", "bytes")`；`MemoryStream(bytes)` + `XmlDictionaryReader.CreateBinaryReader(stream, XmlDictionaryReaderQuotas.Max)`；`(TData)serializer.ReadObject(reader)`。 |
| `DataContractSerializer CreateSerializer(Type type)` | 构造序列化器 | `new DataContractSerializer(type, settings)`。 |
| `static DataContractSerializerSettings CreateDefaultSettings()` | 默认设置 | `PreserveObjectReferences = true`（保留对象引用，可处理共享/循环引用）。 |

---

### `ISaveEncryptor.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `byte[] Encrypt(byte[] bytes)` | 加密 | 作用于序列化后的字节流。 |
| `byte[] Decrypt(byte[] bytes)` | 解密 | 还原为明文字节流。 |

---

### `AesSaveEncryptor.cs`

`public sealed class AesSaveEncryptor : ISaveEncryptor`，AES 对称加密（CBC + PKCS7，`Aes.Create()` 默认模式）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `static readonly byte[] Header = {70,83,65,69,83,1}` | 魔数头 | 6 字节：`"FSAES"`（F=70,S=83,A=65,E=69,S=83）+ 版本字节 `1`；密文最前面写入，用于解密时校验。 |
| `readonly byte[] key` | AES 密钥 | 经 `NormalizeKey` 归一化为合法长度（16/24/32 字节）。 |
| `AesSaveEncryptor(string passphrase)` | 口令构造 | 委托 `: this(HashPassphrase(passphrase))`（口令 → SHA-256 → 32 字节密钥）。 |
| `AesSaveEncryptor(byte[] key)` | 密钥构造 | `key` 为 `null` 或空抛 `ArgumentException("Encryption key is required.", "key")`；存 `NormalizeKey(key)`。 |
| `byte[] Encrypt(byte[] bytes)` | 加密 | `bytes` 为 `null` 当作空数组；`Aes.Create()` 设 `Key`，`GenerateIV()` 随机 IV；输出流先写 `Header`，再写 1 字节 `IV 长度`，再写 IV 本体，随后用 `CryptoStream(... CreateEncryptor(), Write)` 写入密文；返回 `output.ToArray()`。**输出布局：`Header(6) | ivLen(1) | IV(ivLen) | 密文`**。 |
| `byte[] Decrypt(byte[] bytes)` | 解密 | 长度 `<= Header.Length + 1` 抛 `InvalidDataException("Encrypted save data is invalid.")`；逐字节比对 `Header`，不符抛 `InvalidDataException("Encrypted save data header is invalid.")`；读 1 字节得 `ivLength`，若 `<=0` 或 `bytes.Length <= offset+ivLength` 抛 `InvalidDataException("Encrypted save data IV is invalid.")`；`Buffer.BlockCopy` 取出 IV；`Aes.Create()` 设 `Key`/`IV`，用 `CryptoStream(input, CreateDecryptor(), Read)` `CopyTo` 输出流；返回明文字节。 |
| `static byte[] HashPassphrase(string passphrase)` | 口令派生密钥 | 口令空抛 `ArgumentException("Encryption passphrase is required.", "passphrase")`；`SHA256.ComputeHash(UTF8 字节)` 得 32 字节密钥（注意：直接 SHA-256，无盐、无 PBKDF2/Rfc2898 迭代）。 |
| `static byte[] NormalizeKey(byte[] key)` | 密钥长度归一 | 长度为 16/24/32 直接复制使用；否则 `SHA256.ComputeHash(key)` 压成 32 字节。 |

> 加密参数要点（精确）：IV 每次加密随机生成且明文前置存储；密钥从口令派生用**裸 SHA-256**（**非** Rfc2898/PBKDF2，**无固定盐**）；加密模式为 `Aes.Create()` 默认（CBC、PKCS7 填充）。

---

### `ISaveVersionedData.cs`

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `int SaveVersion { get; }` | 数据自带版本号 | 数据类实现后，`Save<TData>(slot, data)` 无版本重载会经 `ResolveDataVersion` 自动取此值（`Math.Max(0, ...)`）。 |

---

### `SaveMigration.cs`

`public sealed class SaveMigration<TData>`，描述一步版本迁移。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `readonly Func<TData, TData> migrate` | 迁移委托 | 接收旧数据返回新数据。 |
| `SaveMigration(int fromVersion, int toVersion, Func<TData, TData> migrate)` | 构造 | `toVersion <= fromVersion` 抛 `ArgumentException("Save migration target version must be greater than source version.", "toVersion")`；`migrate == null` 抛 `ArgumentNullException("migrate")`；初始化 `FromVersion/ToVersion/migrate`。 |
| `int FromVersion { get; private set; }` | 源版本 | 链按此值升序排序、匹配。 |
| `int ToVersion { get; private set; }` | 目标版本 | 迁移后 `currentVersion` 更新为此值。 |
| `TData Apply(TData data)` | 执行迁移 | `return migrate(data)`。 |

---

### `SaveMetadata.cs`

`[Serializable] public sealed class SaveMetadata`，`.meta` 文件 JSON 内容（`JsonUtility` 序列化）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `int FormatVersion = 1` | 元数据格式版本 | 写入时取 `MetadataFormatVersion`（当前 1）。 |
| `string Slot` | 槽位名 | 原始 `slot` 字符串。 |
| `string SerializerExtension` | 序列化器扩展名 | 写入时的 `GetSerializerFileExtension()`（如 `.json`）。 |
| `int DataVersion` | 数据版本 | `Math.Max(0, dataVersion)`；迁移链以此为起点。 |
| `long SavedAtUtcTicks` | 保存时间 Ticks | `DateTime.UtcNow.Ticks`。 |
| `long PayloadSizeBytes` | 负载字节数 | 加密后（若有）实际写盘字节长度。 |
| `string PayloadSha256` | 负载 SHA-256 | 小写十六进制；加载时大小写不敏感比对。 |
| `bool Encrypted` | 是否加密 | `encryptor != null` 时为 `true`。 |
| `DateTime SavedAtUtc { get; }` | 保存时间（UTC） | `SavedAtUtcTicks <= 0` 返回 `DateTime.MinValue`，否则 `new DateTime(ticks, DateTimeKind.Utc)`。 |

---

### `SaveSlotInfo.cs`

`[Serializable] public sealed class SaveSlotInfo`，`ListSlots` 返回的条目（**类**，引用类型）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `string Slot` | 槽位名 | `Path.GetFileNameWithoutExtension(file)`。 |
| `string Path` | 文件绝对路径 | 主存档文件路径。 |
| `long LastWriteUtcTicks` | 最后写入 Ticks | `FileInfo.LastWriteTimeUtc.Ticks`。 |
| `long SizeBytes` | 文件大小 | `FileInfo.Length`。 |
| `bool HasMetadata` | 是否有元数据 | 成功读取 `.meta` 时置 `true`。 |
| `int DataVersion` | 数据版本 | 来自 `.meta`（无则默认 0）。 |
| `bool Encrypted` | 是否加密 | 来自 `.meta`。 |
| `string SerializerExtension` | 序列化器扩展名 | 来自 `.meta`。 |
| `DateTime LastWriteUtc { get; }` | 最后写入（UTC） | `new DateTime(LastWriteUtcTicks, DateTimeKind.Utc)`。 |

---

### `SaveLoadResult.cs`

`public readonly struct SaveLoadResult<TData>`，异步加载结果载体（因结构体不能作 `out` 返回，用作 `TryLoadAsync` 返回值）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `SaveLoadResult(bool success, TData data)` | 构造 | 初始化两只读属性。 |
| `bool Success { get; }` | 是否成功 | 加载成功为 `true`。 |
| `TData Data { get; }` | 数据 | 失败时为 `default(TData)`。 |

---

### 流转逻辑

#### 1. 保存流程（Save / SaveAsync）

1. **解析版本**：无 `dataVersion` 重载经 `ResolveDataVersion(data)`：若数据实现 `ISaveVersionedData` 取 `Math.Max(0, SaveVersion)`，否则 `0`。
2. **计算路径**：`path = GetPath(slot)`（`saveRoot + SanitizeFileName(slot) + 当前序列化器扩展名`）；`tempPath = path + ".tmp"`；`tempMetadataPath = path + ".meta" + ".tmp"`；`backupPath = path + ".bak"`。
3. **序列化**：`bytes = serializer.Serialize(data)`。
4. **可选加密**：`if (encryptor != null) bytes = encryptor.Encrypt(bytes)`（AES：`Header | ivLen | IV | 密文`）。
5. **构造元数据**：`CreateMetadata(slot, bytes, dataVersion)` —— 记录 `FormatVersion=1`、`Slot`、`SerializerExtension`、`DataVersion=Max(0,dataVersion)`、`SavedAtUtcTicks`、`PayloadSizeBytes=bytes.Length`、`PayloadSha256=ComputeSha256(bytes)`、`Encrypted=encryptor!=null`。SHA-256 是对**加密后**的最终字节计算的。
6. **写临时文件**：同步 `File.WriteAllBytes(tempPath, bytes)` + `WriteMetadata(tempMetadataPath)`；异步 `await File.WriteAllBytesAsync(...)` + `await WriteMetadataAsync(...)`（元数据用 `JsonUtility.ToJson(metadata, true)`，UTF-8）。
7. **原子落地**：
   - 若 `path` 已存在：`BackupMetadata(path, backupPath)`（把主 `.meta` 复制成 `.bak.meta`，或在主 `.meta` 不存在时删除旧 `.bak.meta`），再 `File.Replace(tempPath, path, backupPath, true)` —— 旧主文件原子变为 `.bak`，临时文件成为新主文件。
   - 若 `path` 不存在：`File.Move(tempPath, path)`。
   - 然后 `ReplaceMetadata(tempMetadataPath, 主.meta)`：删旧主 `.meta` 后 `File.Move(temp.meta → 主.meta)`。
8. **失败回滚**：`catch` 块删除残留 `tempPath` / `tempMetadataPath` 后重抛原异常（不吞异常）。异步版在序列化前/写完后多处 `ThrowIfCancellationRequested`。

#### 2. 加载流程（TryLoad / TryLoadAsync / Load / LoadAsync）

1. **校验 slot**：`ValidateSlot` —— 空白抛 `ArgumentException("Save slot is required.", "slot")`。
2. **先主文件**：`TryLoadFromPath(path)` / `TryLoadFromPathAsync(path)`。
3. **读字节 + 元数据探测**：
   - 文件不存在/路径空白 → 失败。
   - 读 `bytes`；`metadataFileExists = File.Exists(.meta)`，`hasMetadata = 解析 .meta 成功`。
   - **若 `.meta` 存在却解析失败 → 失败**（视为损坏，防止误读半截存档）。
   - **旧存档（无 `.meta`）仍可加载**：`hasMetadata == false` 时跳过校验，按 `dataVersion = 0` 处理。
4. **校验负载**（仅 `hasMetadata`）：`ValidatePayload` —— 比对 `PayloadSizeBytes` 与实际长度、`PayloadSha256` 与重算值（`OrdinalIgnoreCase`）；不符记 `FrameLog.Warning` 并失败。`PayloadSha256` 为空白时视为通过。
5. **可选解密**：`shouldDecrypt = hasMetadata ? metadata.Encrypted : (encryptor != null)`。需解密但 `encryptor == null` → 抛 `InvalidOperationException("Save data is encrypted but no save encryptor is configured.")`；否则 `bytes = encryptor.Decrypt(bytes)`（AES 校验 `Header`/IV，失败抛 `InvalidDataException`）。
6. **反序列化**：`data = serializer.Deserialize<TData>(bytes)`。
7. **迁移链**：`data = ApplyMigrations(data, hasMetadata ? metadata.DataVersion : 0)`（详见第 6 节）。
8. **异常处理**：同步版 `catch` 写 `FrameLog.Exception` 返回 `false`；异步版 `OperationCanceledException` 重抛、其他异常写日志返回 `Success=false`。
9. **回退 .bak**：主文件加载失败时，自动对 `path + ".bak"` 重复上述流程（同步 `TryLoadFromPath(backupPath)`，异步 `await TryLoadFromPathAsync(backupPath)`）。
10. **兜底值**：`Load`/`LoadAsync` 在最终失败时返回 `fallback`。

#### 3. 异步 Save/Load 说明

异步路径基于 `System.IO` 的 `File.WriteAllBytesAsync` / `File.WriteAllTextAsync` / `File.ReadAllBytesAsync` / `File.ReadAllTextAsync` 与 `Task`/`async`-`await`，全程传递 `CancellationToken`（默认 `default`）。返回类型为 `Task` / `Task<SaveLoadResult<TData>>` / `Task<TData>`，可与 UniTask 互操作（`await` 兼容）。取消时 `SaveAsync` 抛 `OperationCanceledException` 并清理临时文件；`TryLoadFromPathAsync` 捕获并重抛 `OperationCanceledException`（不降级为"加载失败"）。异步加载的元数据用 `JsonUtility.FromJson<SaveMetadata>` 解析（与同步路径一致）。

#### 4. 槽位枚举与元数据查询

- `ListSlots()`：仅匹配 `saveRoot` 顶层中后缀等于"**当前**序列化器扩展名"的文件（切换序列化器后会改变可见集合）。每条 `SaveSlotInfo` 先填文件信息（`Slot`/`Path`/`LastWriteUtcTicks`/`SizeBytes`），再读 `.meta` 补 `HasMetadata`/`DataVersion`/`Encrypted`/`SerializerExtension`。由于 `SaveSlotInfo` 是引用类型，`result[result.Count - 1]` 取出的就是列表中的同一对象，赋值即生效。
- `TryGetMetadata(slot, out metadata)`：读取主文件对应 `.meta`；无 `.meta`（含旧存档）返回 `false`。
- `Delete(slot)`：删除主文件、`.bak`、主 `.meta`、`.bak.meta` 四类中存在者，任一被删返回 `true`。
- **文件扩展名由当前序列化器决定**：`GetSerializerFileExtension()` 取 `serializer.FileExtension`，空白回退 `.save`，并保证以 `.` 开头。

#### 5. 路径与文件名

`GetPath(slot) = Path.Combine(saveRoot, FramePathUtility.SanitizeFileName(slot) + 扩展名)`。`SanitizeFileName` 将 `Path.GetInvalidFileNameChars()` 中字符替换为 `_`，空白名回退 `"default"`。元数据路径恒为"主文件路径 + `.meta`"，临时/备份为"主文件路径 + `.tmp`/`.bak`"。

#### 6. 迁移机制

- **注册**：`RegisterMigration<TData>(new SaveMigration<TData>(fromVersion, toVersion, func))`。同类型多步迁移累积进同一列表，每次注册后按 `FromVersion` **升序排序**。
- **应用**（`ApplyMigrations`）：`currentVersion = Max(0, 存档DataVersion)`；`do/while` 循环每轮线性查找 `FromVersion == currentVersion` 的迁移，命中即 `Apply` 并把 `currentVersion` 推进到该迁移的 `ToVersion`、`break` 重新开始下一轮；直到某轮无任何命中。因此迁移可"跨级跳"（如 0→2、2→5），只要链能从存档版本连续接力即可。
- **触发条件**：仅当存档 `DataVersion` 小于（实际是"等于某迁移 `FromVersion`"）已注册迁移可处理的起点时才会逐级升级；不匹配则原样返回。`ClearMigrations<TData>()` 清空该类型迁移。

#### 7. 异常边界

- 空/空白 `slot` → `ArgumentException("Save slot is required.", "slot")`（`Exists`/`Save` 经 `GetPath`、`TryLoad`/`TryLoadAsync`/`TryGetMetadata` 直接 `ValidateSlot`）。
- 加密但无加密器 → `InvalidOperationException(...)`（加载时）。
- 二进制反序列化空字节 → `ArgumentException("Binary save data is empty.", "bytes")`。
- AES 密文非法/头错/IV 错 → `InvalidDataException(...)`。
- 迁移参数非法 → 构造期 `ArgumentException`/`ArgumentNullException`。
- 保存失败统一清理临时文件并重抛；加载失败统一降级为"返回 false / fallback"（取消除外）。

---

### 使用示例

```csharp
using System.Threading;
using System.Threading.Tasks;
using Frame.Core;
using Frame.Save;

// 1) 定义存档数据类（可选实现 ISaveVersionedData 自带版本号）
[System.Serializable]
public class PlayerSave : ISaveVersionedData
{
    public string Name;
    public int Level;
    public int Gold;

    // 数据自带版本号；无版本重载的 Save 会自动采用它
    public int SaveVersion { get { return 2; } }
}

public class SaveSample
{
    private readonly ISaveService save = GameContext.Current.Services.Resolve<ISaveService>();

    // 2) 同步保存——自动版本（取 ISaveVersionedData.SaveVersion = 2）
    public void SaveAuto()
    {
        var data = new PlayerSave { Name = "Hero", Level = 10, Gold = 999 };
        save.Save("player", data);                  // 写 player.json + player.json.meta
    }

    // 2b) 同步保存——显式版本（覆盖自动解析，写入 DataVersion = 3）
    public void SaveExplicit()
    {
        var data = new PlayerSave { Name = "Hero", Level = 11, Gold = 1200 };
        save.Save("player", data, 3);
    }

    // 3) 异步保存（可取消）
    public async Task SaveAsyncSample(CancellationToken token)
    {
        var data = new PlayerSave { Name = "Hero", Level = 12, Gold = 1500 };
        await save.SaveAsync("player", data, token);            // 自动版本
        await save.SaveAsync("player", data, 4, token);         // 显式版本
    }

    // 4) 同步加载
    public void LoadSample()
    {
        if (save.TryLoad("player", out PlayerSave data))
        {
            FrameLog.Info($"Loaded {data.Name} Lv.{data.Level}");
        }

        // 带兜底
        PlayerSave loaded = save.Load("player", new PlayerSave { Name = "Guest" });
    }

    // 5) 异步加载——SaveLoadResult
    public async Task LoadAsyncSample(CancellationToken token)
    {
        SaveLoadResult<PlayerSave> result = await save.TryLoadAsync<PlayerSave>("player", token);
        if (result.Success)
        {
            FrameLog.Info($"Loaded {result.Data.Name}");
        }

        PlayerSave loaded = await save.LoadAsync("player", new PlayerSave(), token);
    }

    // 6) 枚举槽位 + 查元数据
    public void EnumerateSample()
    {
        foreach (SaveSlotInfo slot in save.ListSlots())
        {
            FrameLog.Info($"{slot.Slot} v{slot.DataVersion} enc={slot.Encrypted} " +
                          $"size={slot.SizeBytes} time={slot.LastWriteUtc}");
        }

        if (save.TryGetMetadata("player", out SaveMetadata meta))
        {
            FrameLog.Info($"sha256={meta.PayloadSha256} savedAt={meta.SavedAtUtc}");
        }
    }

    // 7) 切换为二进制序列化器（注意：之后 ListSlots 只看 .bin）
    public void UseBinary()
    {
        save.SetSerializer(new BinarySaveSerializer());
    }

    // 8) 启用 AES 加密（口令派生密钥 = SHA-256(口令)）
    public void UseEncryption()
    {
        save.SetEncryptor(new AesSaveEncryptor("my-secret-passphrase"));
        // 或使用原始密钥：new AesSaveEncryptor(keyBytes16or24or32)
    }

    // 9) 注册迁移链（v0 -> v1 -> v2）
    public void RegisterMigrations()
    {
        save.RegisterMigration(new SaveMigration<PlayerSave>(0, 1, d =>
        {
            if (d != null && d.Name == null) d.Name = "Unknown";
            return d;
        }));
        save.RegisterMigration(new SaveMigration<PlayerSave>(1, 2, d =>
        {
            if (d != null && d.Gold < 0) d.Gold = 0;
            return d;
        }));
        // 需要时清空：save.ClearMigrations<PlayerSave>();
    }
}
```

> 服务获取方式以项目实际容器 API 为准（此处 `GameContext.Current.Services.Resolve<ISaveService>()` 仅示意）；`SaveService` 在初始化时已注册 `ISaveService` 与 `SaveService` 两份服务。

---

### 设计意图与踩坑点

- **原子写入（.tmp → 正式）**：始终先把新数据写到 `.tmp`，再用 `File.Replace`/`File.Move` 一步换上，避免写入中途崩溃/断电导致正式存档被截断或半写。元数据 `.meta` 同样走 `.tmp` 落地，保证"数据 + 元数据"尽量一致。
- **.bak 备份存在的理由**：`File.Replace(tempPath, path, backupPath, true)` 会把被替换掉的旧主文件原子保存为 `.bak`。当新存档损坏（SHA-256/大小不符、解密失败、反序列化抛错）时，加载流程自动回退到 `.bak`，最大化挽救上一份可用存档。`BackupMetadata` 也同步维护 `.bak.meta`。
- **SHA-256 防篡改/防损坏**：元数据记录加密后字节的大小与 SHA-256，加载时重算比对（大小写不敏感）。可检测外部篡改和磁盘损坏；但它是完整性校验**不是**认证——SHA-256 与 `.meta` 同为明文，攻击者可同时改数据与 `.meta`，并非防作弊的安全边界。`PayloadSha256` 为空时跳过校验。
- **AES 密钥管理警告**：`AesSaveEncryptor` 用**裸 SHA-256(口令)** 派生密钥（**无 PBKDF2/Rfc2898 迭代、无盐**），抗暴力/字典攻击能力弱；IV 随机且明文前置（合理），但密钥/口令一旦硬编码在客户端即可被逆向提取。请勿把 AES 当作强反作弊手段，敏感校验应放服务端；切换/丢失密钥会导致旧密文无法解密（解密时会因 `Header` 校验或填充错误抛 `InvalidDataException`/加密异常，进而加载失败回退）。
- **ISaveVersionedData vs 显式 dataVersion**：数据类实现 `ISaveVersionedData` 后，无版本重载会自动取 `SaveVersion`；也可调用显式 `dataVersion` 重载覆盖（适合不便改数据类、或运行期决定版本的场景）。两者都会被 `Math.Max(0, ...)` 钳到非负。无元数据的旧存档加载时按 `DataVersion = 0` 进入迁移链。
- **切换序列化器影响可见性**：`ListSlots`/`GetPath`/`Exists` 都依赖"当前序列化器扩展名"。运行期 `SetSerializer` 后，旧扩展名的存档不会出现在 `ListSlots` 中，也无法用 `Load` 读到（路径扩展名变了）。建议全局固定一种序列化器，或做好迁移。
- **迁移链是"逐级接力"**：迁移按 `FromVersion == currentVersion` 匹配并推进到 `ToVersion`，链必须从存档版本起连续可达，否则在断点处停止（剩余升级不会发生）。注册顺序无关（内部按 `FromVersion` 排序），但区间不能有歧义重叠（同一 `FromVersion` 多条会取列表中先匹配到的一条）。
- **未覆盖云存档**：本模块仅处理本地 `persistentDataPath` 文件读写，**不包含**云同步/多端合并/冲突解决/在线备份。如需云存档需在其上自建上传/下载与冲突策略。
- **其它注意**：`SetSerializer(null)` 会被静默忽略（保留旧序列化器）；`SaveLoadResult<TData>` 是 `readonly struct`，故异步用返回值而非 `out`；`OnShutdown` 后再调用服务会因字段被置 `null` 出错，应在模块生命周期内使用。

## 18. Config 模块

Config 模块提供一个**多 Provider 链 + 缓存 + 校验**的配置加载体系。核心入口是 `ConfigService`（继承 `GameModuleBase`，`Priority = -200`，在初始化时把自身注册到 `Context.Services`）。配置通过 `Load<TConfig>(key)` / `TryLoad<TConfig>(key, out config)` 泛型方法按字符串 `key` + 目标类型读取，底层逐个询问已注册的 `IConfigProvider`。默认本地配置通过当前 `IAssetService` 加载：`AssetScriptableConfigProvider` 按路径读取 `ScriptableConfig`，`AssetJsonConfigProvider` 按路径读取 JSON `TextAsset`；运行时可写的远端/覆盖 JSON 配置由 `RuntimeJsonConfigProvider` 提供。配置对象可选实现 `IConfigValidator`，加载后立即校验；校验失败则视为加载失败。JSON 反序列化使用 Newtonsoft.Json (`JsonConvert`)。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IConfigService` | 配置服务对外接口 | `CacheEnabled`、`RegisterProvider`/`UnregisterProvider`、`ClearCache`、`Load<T>`/`TryLoad<T>` |
| `ConfigService` | 服务实现（`GameModuleBase`） | `Priority = -200`；Provider 链 + 缓存字典；`RegisterProvider` 插入链首并清缓存；订阅 `IConfigChangeNotifier`；加载后跑 `IConfigValidator` |
| `IConfigProvider` | 单个配置来源抽象 | 只有 `TryLoad<TConfig>(key, out config)` 一个泛型方法 |
| `IConfigChangeNotifier` | 配置来源变更通知 | 暴露 `event Action Changed`；`ConfigService` 订阅后在变更时 `ClearCache` |
| `IConfigValidator` | 配置自校验接口 | `bool Validate(out string error)`；由配置对象自身实现 |
| `AssetJsonConfigProvider` | 通过 `IAssetService` 读 JSON 文本资产 | 默认根目录 `Configs`；`assets.TryLoad<TextAsset>` + `JsonConvert.DeserializeObject` |
| `AssetScriptableConfigProvider` | 通过 `IAssetService` 读 `ScriptableConfig` 资产 | 默认根目录 `Configs`；按 `Configs/{key}` 路径加载单个资产 |
| `RuntimeJsonConfigProvider` | 运行时可写的 JSON 覆盖层 | 实现 `IConfigChangeNotifier`；`SetJson`/`Set<T>`/`Remove`/`Clear`；变更触发 `Changed` |
| `ScriptableConfig` | `ScriptableObject` 配置基类 | 暴露 `Id`（空则回退为资产 `name`） |
| `ScriptableConfigProvider` | 内存中按 `Id` 索引的 `ScriptableConfig` 字典 | `Register`/`TryLoad`/`Clear`；适合手动注册或测试注入 |

---

### `IConfigService.cs`

定义配置服务对外契约，命名空间 `Frame.Config`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool CacheEnabled { get; set; }` | 是否启用按 `(key, type)` 的内存缓存 | 由实现持有，默认 `true` |
| `void RegisterProvider(IConfigProvider provider)` | 注册一个配置来源 | 实现中插入链首（最高优先级）并清缓存 |
| `bool UnregisterProvider(IConfigProvider provider)` | 注销配置来源 | 返回是否真的移除了 |
| `void ClearCache()` | 清空缓存 | 用于强制下次重新加载 |
| `TConfig Load<TConfig>(string key) where TConfig : class` | 加载配置，失败返回 `null` | 失败时打印 `FrameLog.Warning("Config not found: ...")` |
| `bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class` | 尝试加载，成功返回 `true` | 不打印 warning，失败 `config = null` |

---

### `ConfigService.cs`

`IConfigService` 的实现，`sealed`，继承 `GameModuleBase`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly List<IConfigProvider> providers` | Provider 链 | 索引 0 优先级最高；遍历顺序即优先级顺序 |
| `private readonly Dictionary<string, object> cache` | `(类型全名:key) → 配置对象` 缓存 | key 由 `GetCacheKey<T>` 生成 |
| `bool CacheEnabled { get; set; } = true` | 缓存开关 | 默认开启 |
| `override int Priority => -200` | 模块优先级 | 较早初始化（数值越小越靠前） |
| `protected override void OnInitialize()` | 初始化钩子 | 若 `Context.Services.TryResolve(out IAssetService assets)` 成功，则依次加入 `AssetScriptableConfigProvider`、`AssetJsonConfigProvider`；随后 `Context.Services.Register<IConfigService>(this)` 与 `Register(this)`。注意：默认链中 ScriptableObject Provider 在前、JSON Provider 在后 |
| `void RegisterProvider(IConfigProvider provider)` | 注册外部 Provider | 仅当 `provider != null && !providers.Contains(provider)` 时：`providers.Insert(0, provider)`（**插入链首=最高优先级**）→ `SubscribeProvider` → `ClearCache()` |
| `bool UnregisterProvider(IConfigProvider provider)` | 注销 Provider | `provider == null` 直接返回 `false`；`providers.Remove`；若移除成功则 `UnsubscribeProvider` + `ClearCache()`；返回移除结果 |
| `void ClearCache()` | 清缓存 | `cache.Clear()` |
| `TConfig Load<TConfig>(string key)` | 加载或返回 `null` | 调 `TryLoad`；失败打印 `FrameLog.Warning("Config not found: " + key + " type=" + typeof(TConfig).Name)` 并返回 `null` |
| `bool TryLoad<TConfig>(string key, out TConfig config)` | 核心加载逻辑 | ① 算 `cacheKey`；②若 `CacheEnabled` 且命中缓存，`config = cached as TConfig`，返回 `config != null`；③否则按顺序遍历 `providers`，第一个 `TryLoad` 成功者：先 `ValidateConfig`，失败则 `config = null; return false`；成功则在 `CacheEnabled` 时写缓存并 `return true`；④全部未命中 `config = null; return false` |
| `protected override void OnShutdown()` | 关闭钩子 | 逐个 `UnsubscribeProvider`，`providers.Clear()`，`cache.Clear()`，`CacheEnabled = true`（复位） |
| `private void SubscribeProvider(IConfigProvider provider)` | 订阅变更 | 若 `provider is IConfigChangeNotifier`，先 `-=` 再 `+= OnProviderChanged`（防重复订阅） |
| `private void UnsubscribeProvider(IConfigProvider provider)` | 取消订阅 | 若是 `IConfigChangeNotifier` 则 `Changed -= OnProviderChanged` |
| `private void OnProviderChanged()` | 变更回调 | 直接 `ClearCache()` |
| `private static string GetCacheKey<TConfig>(string key)` | 生成缓存 key | `typeof(TConfig).FullName + ":" + key`（类型 + key 联合唯一） |
| `private static bool ValidateConfig<TConfig>(string key, TConfig config)` | 加载后校验 | 若 `config is IConfigValidator validator`：调 `validator.Validate(out error)`，`true` 通过；`false` 时打印 `FrameLog.Warning("Config validation failed: ...")` 并返回 `false`。配置未实现校验接口则直接通过（返回 `true`） |

---

### `IConfigProvider.cs`

单个配置来源的抽象，命名空间 `Frame.Config`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool TryLoad<TConfig>(string key, out TConfig config) where TConfig : class` | 按 key 尝试加载配置 | 唯一成员；成功 `true` 并填充 `config`，失败 `false` 且 `config = null`。泛型约束 `class` |

---

### `IConfigChangeNotifier.cs`

可变更配置来源的通知接口，命名空间 `Frame.Config`（`using System;`）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action Changed` | 配置发生变化时触发 | `ConfigService` 订阅后在回调里 `ClearCache()`，实现“写后即时失效缓存” |

---

### `IConfigValidator.cs`

配置对象自校验接口，命名空间 `Frame.Config`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool Validate(out string error)` | 校验自身合法性 | 返回 `true` 合法；`false` 时通过 `out error` 给出原因。由配置对象（POCO 或 `ScriptableConfig`）自行实现；`ConfigService` 在加载后调用 |

---

### `AssetJsonConfigProvider.cs`

通过 `IAssetService` 加载 JSON 文本资产（`TextAsset`）并反序列化，`sealed`，实现 `IConfigProvider`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly IAssetService assets` | 资源服务引用 | 构造时传入；为空时加载直接失败 |
| `private readonly string rootPath` | 配置根目录 | 构造时用 `FramePathUtility.NormalizeResourcesPath` 归一化保存，默认 `"Configs"` |
| `AssetJsonConfigProvider(IAssetService assets, string rootPath = "Configs")` | 构造函数 | 绑定当前资源后端；Resources 后端下逻辑路径 `Configs/game` 对应 `Resources/Configs/game.json` |
| `bool TryLoad<TConfig>(string key, out TConfig config)` | 加载并反序列化 | ① 拼路径：`rootPath` 为空则 `path = key`，否则 `rootPath + "/" + key`，再 `NormalizeResourcesPath`；② `assets.TryLoad<TextAsset>(path, out handle)`，失败则 `config = null; return false`；③ `try { config = JsonConvert.DeserializeObject<TConfig>(handle.Asset.text); return config != null; }`；④ 异常时 `FrameLog.Exception(exception)`，`config = null; return false`；⑤ `finally` 释放 `AssetHandle` |

---

### `AssetScriptableConfigProvider.cs`

通过 `IAssetService` 加载单个 `ScriptableConfig` 资产，`sealed`，实现 `IConfigProvider`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly IAssetService assets` | 资源服务引用 | 构造时传入；为空时加载直接失败 |
| `private readonly string rootPath` | 配置根目录 | 构造时用 `FramePathUtility.NormalizeResourcesPath` 归一化保存，默认 `"Configs"` |
| `AssetScriptableConfigProvider(IAssetService assets, string rootPath = "Configs")` | 构造函数 | 绑定当前资源后端 |
| `bool TryLoad<TConfig>(string key, out TConfig config)` | 加载 Scriptable 配置 | 仅当 `TConfig` 派生自 `ScriptableConfig` 时尝试；调用 `assets.TryLoad<ScriptableConfig>(rootPath/key, out handle)` 并 `handle.Asset as TConfig`；最后释放 `AssetHandle`。key 是资源路径，不再按 `ScriptableConfig.Id` 全量扫描 |

---

### `RuntimeJsonConfigProvider.cs`

运行时可写的 JSON 覆盖/远端配置来源，`sealed`，实现 `IConfigProvider` 与 `IConfigChangeNotifier`。键大小写不敏感（`StringComparer.OrdinalIgnoreCase`）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<string, string> jsonByKey` | `key → json 文本` | 不区分大小写 |
| `event Action Changed` | 变更通知 | 实现 `IConfigChangeNotifier`；任何写操作都会 `RaiseChanged` |
| `int Count { get; }` | 当前条目数 | `jsonByKey.Count` |
| `IEnumerable<string> Keys { get; }` | 当前所有 key | `jsonByKey.Keys` |
| `void SetJson(string key, string json)` | 写入/更新一条 JSON | `NormalizeKey(key)`；归一化后为空抛 `ArgumentException("Config key is required.", "key")`；若 `json` 空白则改为 `Remove(normalizedKey)` 并返回；否则写入字典并 `RaiseChanged()` |
| `void Set<TConfig>(string key, TConfig config) where TConfig : class` | 写入强类型对象 | `config == null` 时改为 `Remove(key)`；否则 `SetJson(key, JsonConvert.SerializeObject(config))` |
| `bool Remove(string key)` | 删除一条 | `NormalizeKey`，空白返回 `false`；`jsonByKey.Remove`，成功则 `RaiseChanged()`；返回是否删除 |
| `void Clear()` | 清空全部 | 若已空直接返回（不触发）；否则 `jsonByKey.Clear()` + `RaiseChanged()` |
| `bool Contains(string key)` | 是否存在某 key | `NormalizeKey` 非空白且 `ContainsKey` |
| `bool TryLoad<TConfig>(string key, out TConfig config)` | 反序列化读取 | `NormalizeKey`；归一化为空白或字典无此 key 则 `config = null; return false`；否则 `try { config = JsonConvert.DeserializeObject<TConfig>(json); return config != null; }`，异常 `FrameLog.Exception` + `config = null; return false` |
| `private void RaiseChanged()` | 触发变更事件 | 捕获 `Changed` 到本地变量，`null` 直接返回；`try { handler(); } catch { FrameLog.Exception }`（隔离订阅者异常） |
| `private static string NormalizeKey(string key)` | 归一化 key | `FramePathUtility.NormalizeResourcesPath(key)`（与 key 大小写无关查询配合） |

---

### `ScriptableConfig.cs`

`ScriptableObject` 配置的抽象基类，命名空间 `Frame.Config`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `[SerializeField] private string id = ""` | 序列化的配置 Id | Inspector 可填；默认空串 |
| `string Id { get; }` | 对外暴露的标识 | `string.IsNullOrWhiteSpace(id) ? name : id`：未填时**回退为资产文件名 `name`** |

---

### `ScriptableConfigProvider.cs`

内存中按 `Id` 索引 `ScriptableConfig` 的 Provider，`sealed`，实现 `IConfigProvider`。适合单独手动注册、测试注入或运行时维护。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<string, ScriptableConfig> configs` | `Id → 配置资产` | 普通字典（大小写敏感，使用 `ScriptableConfig.Id` 为键） |
| `void Register(ScriptableConfig config)` | 注册一条配置 | `config == null` 直接返回；否则 `configs[config.Id] = config`（同 Id 覆盖） |
| `bool TryLoad<TConfig>(string key, out TConfig config)` | 按 key 查询 | `configs.TryGetValue(key, out value)` 命中后 `config = value as TConfig`，返回 `config != null`（类型不匹配返回 `false`）；未命中 `config = null; return false` |
| `void Clear()` | 清空索引 | `configs.Clear()` |

---

### 流转逻辑

1. **默认 Provider 链构建**：`ConfigService.OnInitialize` 先尝试解析 `IAssetService`。若资源服务可用，按顺序加入两个内置 Provider —— 先 `AssetScriptableConfigProvider`（根目录 `Configs`，按 key 路径加载单个 `ScriptableConfig`），后 `AssetJsonConfigProvider`（根目录 `Configs`，通过资源服务加载 `TextAsset` + Newtonsoft 反序列化）。链表遍历顺序即优先级，索引 0 最高，故默认情况下 `ScriptableObject` 配置优先于 JSON 文本配置。
2. **注册自定义 Provider**：`RegisterProvider` 用 `providers.Insert(0, provider)` **插入链首**，因此后注册者优先级更高（典型用法：注册 `RuntimeJsonConfigProvider` 作为远端/热更覆盖层，覆盖本地资产配置）。每次注册都会 `ClearCache()`，并在 Provider 实现 `IConfigChangeNotifier` 时订阅其 `Changed`。
3. **加载与缓存**：`Load<T>`/`TryLoad<T>` 先按 `GetCacheKey = typeof(T).FullName + ":" + key` 查缓存（仅 `CacheEnabled` 时）。未命中则按优先级遍历 Provider，第一个 `TryLoad` 成功的来源胜出。
4. **加载后校验**：命中后调用 `ValidateConfig`。若配置对象实现了 `IConfigValidator`，执行 `Validate(out error)`；**校验失败则整体加载失败**（`config = null`、`TryLoad` 返回 `false`，并打印 `FrameLog.Warning("Config validation failed: ...")`），且**不写入缓存**。未实现校验接口的配置直接通过。
5. **缓存写入**：校验通过且 `CacheEnabled` 时，把对象写入 `cache[cacheKey]`，后续相同 `(类型,key)` 直接命中。
6. **变更失效**：实现 `IConfigChangeNotifier` 的 Provider（如 `RuntimeJsonConfigProvider`）在 `SetJson`/`Set`/`Remove`/`Clear` 等写操作后触发 `Changed`，`ConfigService.OnProviderChanged` 收到后 `ClearCache()`，保证下次加载读到最新值。
7. **关闭复位**：`OnShutdown` 取消所有订阅、清空 Provider 链与缓存，并把 `CacheEnabled` 复位为 `true`。

### 使用示例

```csharp
using Frame.Config;

// 1) 通过 Context 取得配置服务（模块已自动注册）
IConfigService configs = Context.Services.Resolve<IConfigService>();

// 2) 加载 Configs/game（Resources 后端下对应 Resources/Configs/game.json）
GameSettings settings = configs.Load<GameSettings>("game");
if (settings != null)
{
    Debug.Log(settings.PlayerName);
}

// 或者用 TryLoad 避免 warning
if (configs.TryLoad<GameSettings>("game", out GameSettings s))
{
    // ...
}

// 3) 注册运行时覆盖层（从服务器拉到 JSON 后写入，自动失效缓存）
var runtime = new RuntimeJsonConfigProvider();
configs.RegisterProvider(runtime);            // 插入链首，优先级最高
runtime.SetJson("game", downloadedJsonText);  // 触发 Changed -> ClearCache
GameSettings hot = configs.Load<GameSettings>("game"); // 读到覆盖后的值

// 4) ScriptableObject 配置：定义并通过 AssetScriptableConfigProvider 按路径加载
public sealed class EnemyConfig : ScriptableConfig
{
    public int hp;
}
// 资产放在配置根目录下；Resources 后端示例路径为 Resources/Configs/Enemies/Goblin.asset
EnemyConfig enemy = configs.Load<EnemyConfig>("Enemies/Goblin");

// 5) 自校验配置
public sealed class GameSettings : IConfigValidator
{
    public string PlayerName;
    public bool Validate(out string error)
    {
        if (string.IsNullOrEmpty(PlayerName)) { error = "PlayerName required"; return false; }
        error = null; return true;
    }
}
```

### 设计意图与踩坑点

- **链首插入 = 高优先级**：`RegisterProvider` 永远 `Insert(0, ...)`。要让远端/热更配置覆盖本地资源，直接注册即可，无需手动排序；但反过来，多次注册会让最后注册者优先级最高，注意注册顺序。
- **默认链顺序固定**：内置链是 `[AssetScriptableConfigProvider, AssetJsonConfigProvider]`，即同名 key 下 `ScriptableObject` 配置会先命中。若希望 JSON 优先，需用 `RegisterProvider` 重新注册一个 JSON Provider 到链首。
- **缓存键含完整类型名**：`GetCacheKey` 用 `typeof(T).FullName`，同一 key 不同目标类型互不污染；但也意味着用错误类型加载同一 key 不会命中已有缓存。
- **校验失败=加载失败且不缓存**：`IConfigValidator` 返回 `false` 时配置被丢弃（返回 `null`），适合做“坏配置直接当作不存在”的强约束；修好配置后由于未缓存会自然重试。
- **`RuntimeJsonConfigProvider` 写空 = 删除**：`SetJson(key, 空白)` 与 `Set(key, null)` 都等价于 `Remove`，谨防误把空字符串当作“清空字段”。
- **键规则**：`RuntimeJsonConfigProvider` 用 `OrdinalIgnoreCase` 且 key 会经 `NormalizeResourcesPath`（去扩展名、剥 `/Resources/` 前缀）；`AssetJsonConfigProvider` 和 `AssetScriptableConfigProvider` 也会规范化 root/key。不同资产后端仍可能有自己的大小写和地址规则。
- **ScriptableConfig 不再全量扫描**：默认 `AssetScriptableConfigProvider` 按路径加载单个资产，不会 `LoadAll` 扫描目录。若想按 `ScriptableConfig.Id` 查询，需要手动维护并注册 `ScriptableConfigProvider`。
- **异常被吞为加载失败**：所有 Provider 的反序列化异常都走 `FrameLog.Exception` 并返回 `false`，不会向上抛——排查问题需看日志。
- **`ScriptableConfig.Id` 回退资产名**：未在 Inspector 填 `id` 时 key 就是资产文件名，重命名资产会改变其 key。

---

## 19. Networking 模块

Networking 模块包含两个独立服务：`HttpService` 封装基于 `UnityWebRequest` 的 HTTP 客户端；`SocketService` 管理 TCP Socket 和 WebSocket 长连接。`HttpService`（继承 `GameModuleBase`，`Priority = 0`）底层用 **UniTask**（`Cysharp.Threading.Tasks`）异步发送，JSON 序列化/反序列化用 Newtonsoft.Json。它提供便捷方法 `Get`/`PostJson` 与强类型 `GetJson<T>`/`PostJson<Req,Resp>`/`SendJson<T>`；强类型解析委托给可插拔的 `IHttpResponseParser`（默认裸 JSON 的 `JsonHttpResponseParser`，以及解析 `{code,message,data,success}` 信封的 `EnvelopeHttpResponseParser`）。每次请求返回 `HttpRequestHandle`（可 `yield` 等待，可 `Cancel`）。服务维护 `BaseUrl`（相对路径自动拼接）、默认请求头（含 Bearer Token）与一组指标计数器，并通过 `RequestStarted`/`RequestCompleted` 事件对外广播。`SocketService`（`Priority = -90`）负责创建多个 `ISocketClient`，每个 client 负责连接、收发、心跳、自动重连、状态事件和指标。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IHttpService` | HTTP 服务对外接口 | 事件、`BaseUrl`、`ResponseParser`、默认头、4 个计数器、便捷/强类型发送方法 |
| `HttpService` | 服务实现（`GameModuleBase`） | `Priority = 0`；`UnityWebRequest` + `UniTask`；重试、超时、头合并、URL 解析、指标与事件 |
| `HttpRequest` | 请求描述（可变字段） | `Url`/`Method`/`Body`/`ContentType`/`TimeoutSeconds`/`Retries`/`RetryDelaySeconds`/`Headers`，含默认值 |
| `HttpResponse` | 响应基类 | `Success`/`StatusCode`/`Text`/`Data`/`Error`/`ErrorCode`/`Message` |
| `HttpResponse<TData>` | 强类型响应 | 增加 `Value`；静态 `From`/`CreateFromBase` 工厂 |
| `HttpMethod` | HTTP 动词枚举 | `Get`、`Post`、`Put`、`Delete` |
| `HttpRequestHandle` | 请求句柄 | `CustomYieldInstruction`；`IsDone`/`IsCanceled`/`Response`/`Cancel()` |
| `IHttpResponseParser` | 响应解析器接口 | `HttpResponse<TData> Parse<TData>(HttpResponse response)` |
| `JsonHttpResponseParser` | 裸 JSON 解析器 | 单例 `Instance`；按 `string`/`byte[]`/对象分支反序列化 |
| `EnvelopeHttpResponseParser` | 信封解析器 | 解析 `{success,code,message,data}`；字段名与成功码可配置 |
| `ISocketService` | Socket 服务对外接口 | 创建 TCP/WebSocket client、管理 client 列表、全部断开 |
| `SocketService` | Socket 服务实现（`GameModuleBase`） | `Priority = -90`；注册服务、创建/移除 client、关闭时释放连接 |
| `ISocketClient` | 单个长连接接口 | Connect/Disconnect/Send、状态、生命周期事件、收发指标 |
| `SocketClient` | TCP/WebSocket 客户端实现 | `TcpClient`/`ClientWebSocket`、send/receive loop、心跳、重连、主线程事件派发 |
| `SocketClientOptions` | 长连接配置 | endpoint、TLS、buffer、queue、reconnect、heartbeat、codec、WebSocket headers/subprotocols |
| `SocketMessage` | 收发消息 | payload + text/binary 类型 |
| `ISocketMessageCodec` / `LengthPrefixedSocketCodec` | TCP 编解码 | 默认 4 字节大端长度前缀，解决 TCP 粘包/拆包 |
| `SocketClientMetrics` | 长连接指标 | sent/received bytes/messages、reconnect attempts、dropped messages |

---

### `IHttpService.cs`

HTTP 服务对外契约，命名空间 `Frame.Networking`（`using System; using System.Collections.Generic;`）。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action<HttpRequest> RequestStarted` | 请求开始事件 | 在 `BeginRequest` 中触发 |
| `event Action<HttpRequest, HttpResponse> RequestCompleted` | 请求完成事件 | 在 `FinishRequest` 中触发，含最终响应 |
| `string BaseUrl { get; set; }` | 基础 URL | 相对 URL 会与之拼接 |
| `IHttpResponseParser ResponseParser { get; set; }` | 强类型响应解析器 | 为 `null` 时回退 `JsonHttpResponseParser.Instance` |
| `IReadOnlyDictionary<string, string> DefaultHeaders { get; }` | 只读默认头视图 | 每个请求都会注入这些头 |
| `int ActiveRequestCount { get; }` | 当前进行中请求数 | 诊断指标 |
| `int StartedRequestCount { get; }` | 累计发起数 | 诊断指标 |
| `int CompletedRequestCount { get; }` | 累计完成数 | 诊断指标 |
| `int FailedRequestCount { get; }` | 累计失败数 | 诊断指标 |
| `void ClearMetrics()` | 清零计数器 | 不清 `ActiveRequestCount` |
| `void SetDefaultHeader(string name, string value)` | 设置/删除默认头 | `value == null` 表示删除 |
| `bool RemoveDefaultHeader(string name)` | 删除默认头 | 返回是否删除 |
| `void ClearDefaultHeaders()` | 清空默认头 | — |
| `void SetBearerToken(string token)` | 设置/清除 Bearer | 空 token 删除 `Authorization` |
| `HttpRequestHandle Get(string url, Action<HttpResponse> completed)` | GET 便捷方法 | 构造 GET 请求并 `Send` |
| `HttpRequestHandle GetJson<TResponse>(string url, Action<HttpResponse<TResponse>> completed)` | 强类型 GET | 走 `SendJson` |
| `HttpRequestHandle PostJson(string url, string json, Action<HttpResponse> completed)` | POST 原始 JSON 字符串 | `ContentType = "application/json"` |
| `HttpRequestHandle PostJson<TRequest, TResponse>(string url, TRequest body, Action<HttpResponse<TResponse>> completed)` | 强类型 POST | 序列化 body + 强类型解析响应 |
| `HttpRequestHandle Send(HttpRequest request, Action<HttpResponse> completed)` | 通用发送 | 返回可 `yield`/`Cancel` 的句柄 |
| `HttpRequestHandle SendJson<TResponse>(HttpRequest request, Action<HttpResponse<TResponse>> completed)` | 通用强类型发送 | 完成回调中用 `ResponseParser` 转换 |

---

### `HttpService.cs`

`IHttpService` 的实现，`sealed`，继承 `GameModuleBase`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<string, string> defaultHeaders` | 默认请求头 | `StringComparer.OrdinalIgnoreCase`（头名大小写不敏感） |
| `private int activeRequestCount / startedRequestCount / completedRequestCount / failedRequestCount` | 指标计数后端字段 | 经 `BeginRequest`/`FinishRequest` 维护 |
| `event Action<HttpRequest> RequestStarted` | 开始事件 | — |
| `event Action<HttpRequest, HttpResponse> RequestCompleted` | 完成事件 | — |
| `string BaseUrl { get; set; }` | 基础 URL | 自动属性 |
| `IHttpResponseParser ResponseParser { get; set; }` | 解析器 | 自动属性，默认 `null` |
| `IReadOnlyDictionary<string, string> DefaultHeaders { get; }` | 默认头只读视图 | 返回 `defaultHeaders` |
| `int ActiveRequestCount/StartedRequestCount/CompletedRequestCount/FailedRequestCount { get; }` | 指标只读属性 | 返回对应私有字段 |
| `protected override void OnInitialize()` | 初始化 | `Context.Services.Register<IHttpService>(this)` 与 `Register(this)` |
| `void ClearMetrics()` | 清零累计指标 | 仅重置 `started/completed/failed`，**不动 `activeRequestCount`** |
| `void SetDefaultHeader(string name, string value)` | 设/删默认头 | `name` 空白抛 `ArgumentException("HTTP header name is required.", "name")`；`value == null` 则 `Remove(name)`；否则写入 |
| `bool RemoveDefaultHeader(string name)` | 删默认头 | `name` 非空白且 `defaultHeaders.Remove(name)` |
| `void ClearDefaultHeaders()` | 清空默认头 | `defaultHeaders.Clear()` |
| `void SetBearerToken(string token)` | 设/清 Bearer | token 空白 → `RemoveDefaultHeader("Authorization")`；否则 `SetDefaultHeader("Authorization", "Bearer " + token)` |
| `HttpRequestHandle Get(string url, Action<HttpResponse> completed)` | GET 便捷 | `Send(new HttpRequest { Url = url, Method = HttpMethod.Get }, completed)` |
| `HttpRequestHandle GetJson<TResponse>(string url, ...)` | 强类型 GET | `SendJson(new HttpRequest { Url = url, Method = Get }, completed)` |
| `HttpRequestHandle PostJson(string url, string json, ...)` | POST 字符串 | `Send(new HttpRequest { Url, Method = Post, Body = json, ContentType = "application/json" }, completed)` |
| `HttpRequestHandle PostJson<TRequest, TResponse>(string url, TRequest body, ...)` | 强类型 POST | `json = JsonConvert.SerializeObject(body)`，再 `SendJson(... Method = Post, Body = json, ContentType="application/json" ...)` |
| `HttpRequestHandle Send(HttpRequest request, Action<HttpResponse> completed)` | 通用发送入口 | 新建 `HttpRequestHandle`；`SendAsync(PrepareRequest(request), completed, handle).Forget()`（fire-and-forget UniTask）；立即返回 handle |
| `HttpRequestHandle SendJson<TResponse>(HttpRequest request, ...)` | 通用强类型发送 | 调 `Send`，在其 completed 回调里 `HttpResponse<TResponse>.From(response, ResponseParser)` 转强类型后回调 `completed`（`completed != null` 时） |
| `protected override void OnShutdown()` | 关闭复位 | 清默认头、`BaseUrl = null`、`ResponseParser = null`、四个计数器归零、两事件置 `null` |
| `private async UniTaskVoid SendAsync(HttpRequest request, Action<HttpResponse> completed, HttpRequestHandle handle)` | 核心发送协程 | 见下方“流转逻辑”详解：`BeginRequest` → 空 URL 短路 → 重试循环（创建 `UnityWebRequest`、`Attach`、设超时与头、`SendWebRequest().ToUniTask()`、`CreateResponse`、`Detach`、成功跳出）→ 重试间隔 `UniTask.Delay`(UnscaledDeltaTime) → 取消/空响应兜底 → `FinishRequest` + `Complete` |
| `private static UnityWebRequest CreateUnityRequest(HttpRequest request)` | 按方法建 `UnityWebRequest` | `Post`→`CreateUploadRequest(kHttpVerbPOST,...)`；`Put`→`CreateUploadRequest(kHttpVerbPUT,...)`；`Delete`→`UnityWebRequest.Delete(url)`；默认（Get）→`UnityWebRequest.Get(url)` |
| `private static UnityWebRequest CreateUploadRequest(string method, HttpRequest request)` | 建带 body 的上传请求 | `new UnityWebRequest(url, method)`；`Encoding.UTF8.GetBytes(Body ?? "")`；`UploadHandlerRaw` + `DownloadHandlerBuffer`；`SetRequestHeader("Content-Type", ContentType 空白时回退 "application/json")` |
| `private static HttpResponse CreateResponse(UnityWebRequest webRequest)` | 从 `UnityWebRequest` 构造响应 | `webRequest == null` → 失败响应 `Error = "Request failed."`；否则 `Success = result == Result.Success`、`StatusCode = responseCode`、`Text/Data` 取 `downloadHandler`（为 null 时为 null）、`Error = webRequest.error` |
| `private HttpRequest PrepareRequest(HttpRequest source)` | 复制请求并注入头/解析 URL | `source == null` 返回 `null`；否则深拷贝字段，`Url = ResolveUrl(source.Url)`；先把 `defaultHeaders` 全部写入 `request.Headers`，**再用 `source.Headers` 覆盖**（每请求头优先于默认头） |
| `private string ResolveUrl(string url)` | 相对 URL 拼接 | 若 `url` 空白、或 `BaseUrl` 空白、或 `url` 已是绝对 URI（`Uri.IsWellFormedUriString(url, Absolute)`）→ 原样返回；否则 `BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/')` |
| `private void BeginRequest(HttpRequest request)` | 请求开始统计与事件 | `activeRequestCount++`、`startedRequestCount++`；触发 `RequestStarted`（`try/catch` 隔离订阅者异常，异常走 `FrameLog.Exception`） |
| `private void FinishRequest(HttpRequest request, HttpResponse response)` | 请求结束统计与事件 | `activeRequestCount = Max(0, active-1)`、`completedRequestCount++`；`response == null || !Success` 时 `failedRequestCount++`；触发 `RequestCompleted`（异常隔离） |
| `private static void Complete(HttpRequestHandle handle, Action<HttpResponse> completed, HttpResponse response)` | 收尾回调 | `handle != null` 时 `handle.Complete(response)`（置 `IsDone`）；`completed != null` 时调用，并 `try/catch` 隔离回调异常（`FrameLog.Exception`） |

---

### `HttpRequest.cs`

请求描述对象，`sealed`，公开可变字段（带默认值），命名空间 `Frame.Networking`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `string Url` | 目标 URL | 可为相对路径（配合 `BaseUrl`） |
| `HttpMethod Method = HttpMethod.Get` | 请求方法 | 默认 `Get` |
| `string Body` | 请求体 | POST/PUT 时使用；默认 `null`（编码时按空串处理） |
| `string ContentType = "application/json"` | 内容类型 | 默认 `application/json` |
| `int TimeoutSeconds = 15` | 超时（秒） | 默认 **15**；实际设置时取 `Max(1, TimeoutSeconds)` |
| `int Retries = 0` | 重试次数 | 默认 **0**；总尝试次数 = `Max(0, Retries) + 1` |
| `float RetryDelaySeconds = 0.25f` | 重试间隔（秒） | 默认 **0.25**；`> 0` 时在尝试之间等待 |
| `Dictionary<string, string> Headers` | 每请求头 | `StringComparer.OrdinalIgnoreCase`，默认空；会覆盖同名默认头 |

---

### `HttpResponse.cs`

响应基类与泛型派生类，命名空间 `Frame.Networking`。

`HttpResponse`（基类）：

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool Success` | 是否成功 | 由传输结果或协议解析共同决定 |
| `long StatusCode` | HTTP 状态码 | 取自 `responseCode` |
| `string Text` | 文本响应体 | `downloadHandler.text` |
| `byte[] Data` | 二进制响应体 | `downloadHandler.data` |
| `string Error` | 错误描述 | 传输错误或解析错误信息 |
| `string ErrorCode` | 业务错误码 | 由信封解析器填充（`code` 字段） |
| `string Message` | 业务消息 | 由信封解析器填充（`message` 字段） |

`HttpResponse<TData>`（`sealed`，继承自 `HttpResponse`）：

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `TData Value` | 强类型解析结果 | 由解析器填充 |
| `static HttpResponse<TData> From(HttpResponse response)` | 用默认解析器转换 | `JsonHttpResponseParser.Instance.Parse<TData>(response)` |
| `static HttpResponse<TData> From(HttpResponse response, IHttpResponseParser parser)` | 用指定解析器转换 | `parser == null` 时回退默认 `From`；否则 `parser.Parse<TData>(response)` |
| `internal static HttpResponse<TData> CreateFromBase(HttpResponse response)` | 从基类拷贝出强类型壳 | 逐字段复制；`response == null` 时 `Success=false`、`Error = "Response is null."`、其余字段安全取默认；**不填 `Value`**（由解析器后续填） |

---

### `HttpMethod.cs`

HTTP 动词枚举，命名空间 `Frame.Networking`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Get` | GET（值 0） | `CreateUnityRequest` 默认分支 → `UnityWebRequest.Get` |
| `Post` | POST（值 1） | → `CreateUploadRequest(kHttpVerbPOST, ...)` |
| `Put` | PUT（值 2） | → `CreateUploadRequest(kHttpVerbPUT, ...)` |
| `Delete` | DELETE（值 3） | → `UnityWebRequest.Delete(url)`（不带 body） |

---

### `HttpRequestHandle.cs`

请求句柄，`sealed`，继承 `CustomYieldInstruction`（可在协程中 `yield return`），命名空间 `Frame.Networking`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private UnityWebRequest webRequest` | 当前底层请求引用 | 由 `Attach`/`Detach`/`Complete` 维护 |
| `override bool keepWaiting { get; }` | 协程是否继续等待 | `return !IsDone` |
| `bool IsDone { get; private set; }` | 是否已完成 | `Complete` 时置 `true` |
| `bool IsCanceled { get; private set; }` | 是否被取消 | `Cancel` 时置 `true` |
| `HttpResponse Response { get; private set; }` | 最终响应 | `Complete` 时赋值 |
| `void Cancel()` | 取消请求 | 已 `IsDone` 直接返回；否则置 `IsCanceled = true`，若 `webRequest != null` 调 `webRequest.Abort()` |
| `internal void Attach(UnityWebRequest request)` | 绑定底层请求 | 记录 `webRequest`；若**已被提前取消**且非空则立即 `Abort()`（处理“发送前已 Cancel”竞态） |
| `internal void Detach(UnityWebRequest request)` | 解绑底层请求 | 仅当 `ReferenceEquals(webRequest, request)` 时把 `webRequest = null`（避免误清下一次重试的请求） |
| `internal void Complete(HttpResponse response)` | 标记完成 | `Response = response`；`webRequest = null`；`IsDone = true` |

---

### `IHttpResponseParser.cs`

响应解析器接口，命名空间 `Frame.Networking`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `HttpResponse<TData> Parse<TData>(HttpResponse response)` | 把基础响应解析为强类型 | 唯一成员；实现需先 `CreateFromBase` 再填 `Value`，并可改写 `Success/Error/ErrorCode/Message` |

---

### `JsonHttpResponseParser.cs`

裸 JSON 解析器（响应体本身就是目标对象的 JSON），`sealed`，实现 `IHttpResponseParser`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `static readonly JsonHttpResponseParser Instance` | 全局单例 | 默认解析器，被 `HttpResponse<TData>.From` 复用 |
| `HttpResponse<TData> Parse<TData>(HttpResponse response)` | 解析逻辑 | ① `CreateFromBase`；②若 `!Success` 直接返回；③`TData == string` → `Value = Text`；④`TData == byte[]` → `Value = Data`；⑤`Text` 空白直接返回（`Value` 保持默认）；⑥否则 `try { Value = JsonConvert.DeserializeObject<TData>(Text); }`，异常时 `FrameLog.Exception` + `Success = false`、`Error = "Failed to parse response JSON: " + ex.Message` |

---

### `EnvelopeHttpResponseParser.cs`

信封解析器，解析形如 `{success, code, message, data}` 的统一返回结构，`sealed`，实现 `IHttpResponseParser`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly HashSet<string> successCodes` | 成功码集合 | `OrdinalIgnoreCase`，默认含 `"0"`、`"200"`、`"OK"`、`"Success"` |
| `string SuccessField = "success"` | 成功标志字段名 | 默认 `"success"` |
| `string CodeField = "code"` | 业务码字段名 | 默认 `"code"` |
| `string MessageField = "message"` | 消息字段名 | 默认 `"message"` |
| `string DataField = "data"` | 数据字段名 | 默认 `"data"`；为空白时把**整个 root** 当作 data |
| `bool TreatMissingSuccessFieldAsSuccess = true` | 无 success 字段时是否算成功 | 默认 `true` |
| `bool TreatMissingCodeAsSuccess = true` | 无 code 时是否算成功 | 默认 `true`（两者**同时**为 true 才在两字段都缺失时判成功） |
| `ISet<string> SuccessCodes { get; }` | 成功码集合只读访问 | 返回 `successCodes`，可外部增删 |
| `HttpResponse<TData> Parse<TData>(HttpResponse response)` | 信封解析主流程 | 见下；非信封结构/解析失败时回退 `JsonHttpResponseParser.Instance.Parse<TData>` |
| `private bool HasEnvelopeFields(JObject root)` | 判断是否信封 | 只要含 `SuccessField`/`CodeField`/`MessageField`/`DataField` 任一字段即认为是信封 |
| `private static bool HasField(JObject root, string fieldName)` | 字段存在性 | `root != null && fieldName 非空白 && root[fieldName] != null` |
| `private bool ResolveProtocolSuccess(JObject root, string code)` | 计算协议层成功 | 见下方“流转逻辑” |
| `private static string ReadString(JObject root, string fieldName)` | 安全读字符串字段 | 缺失/Null 返回 `null`；`String` 类型取值，否则 `ToString(Formatting.None)` |

`Parse<TData>` 详细流程：① `CreateFromBase`，若传输层 `!Success` 直接返回；②`Text` 空白直接返回；③`JObject.Parse(Text)` 失败（异常）则回退裸 JSON 解析器；④若 `!HasEnvelopeFields` 同样回退裸解析器；⑤读 `code = ReadString(CodeField)`、`message = ReadString(MessageField)`，算 `protocolSuccess = ResolveProtocolSuccess(root, code)`；⑥写 `typed.ErrorCode = code`、`typed.Message = message`、`typed.Success = protocolSuccess`；⑦若 `!protocolSuccess`，`Error = message 空白 ? "API error: " + code : message`，返回；⑧取 `data`（`DataField` 空白时为整个 `root`，否则 `root[DataField]`），为 null/JTokenType.Null 直接返回；⑨按 `TData` 分支提取：`string`（data 为 String 取值，否则 `ToString(Formatting.None)`）、`byte[]`（`JsonConvert.DeserializeObject<TData>(data.ToString())`）、其它（`data.ToObject<TData>()`）；异常时 `FrameLog.Exception` + `Success = false`、`Error = "Failed to parse response envelope data: " + ex.Message`。

`ResolveProtocolSuccess` 逻辑：取 `successToken = root[SuccessField]`（`SuccessField` 空白则视为 null）；若非空且非 Null：`Boolean` 类型直接取 bool；否则取其字符串，`bool.TryParse` 成功用该 bool，否则 `successCodes.Contains(successText)`。若无有效 success 字段：当 `code` 非空白时 `successCodes.Contains(code)`；两者都缺时返回 `TreatMissingSuccessFieldAsSuccess && TreatMissingCodeAsSuccess`。

---

### Socket 长连接关键类型

#### `ISocketService.cs` / `SocketService.cs`

`SocketService` 继承 `GameModuleBase` 并实现 `ISocketService`。它在 `OnInitialize` 中注册 `ISocketService` 和自身，`Priority = -90`，比默认 0 优先级模块更早可用。服务内部持有 `List<ISocketClient>`，提供 `CreateClient(SocketClientOptions)`、`CreateTcpClient(host, port, configure)`、`CreateWebSocketClient(url, configure)`、`RemoveClient(client, disconnect)` 和 `DisconnectAllAsync()`。`OnShutdown` 会 dispose 所有 client 并清空列表。

#### `SocketClientOptions.cs`

`SocketClientOptions` 是连接配置对象。TCP 使用 `Host/Port`，WebSocket 使用 `Url`；可配置 `UseTls/TlsHostName/CertificateValidationCallback`、`ConnectTimeoutMilliseconds`、`ReceiveBufferSize`、`MaxMessageSizeBytes`、`SendQueueLimit`、`ClearSendQueueOnDisconnect`、`AutoReconnect`、`MaxReconnectAttempts`、重连延迟、心跳 payload、`Codec`、WebSocket headers 和 sub-protocols。`Tcp(host, port)` 与 `WebSocket(url)` 是工厂方法。`Validate()` 会校验 TCP host/port 或 WebSocket `ws://`/`wss://` URL，并修正最小缓冲、超时和延迟。

#### `ISocketClient.cs` / `SocketClient.cs`

`ISocketClient` 暴露 `StateChanged`、`Connected`、`Disconnected`、`Reconnecting`、`MessageReceived`、`Error` 事件，以及 `ConnectAsync`、`DisconnectAsync`、`Send`、`SendText`、`ClearMetrics`。`SocketClient` 是具体实现：TCP 走 `TcpClient`/`NetworkStream`，可选 `SslStream`；WebSocket 走 `ClientWebSocket`。连接成功后启动 send loop、receive loop 和可选 heartbeat loop。所有对外事件会通过 UniTask PlayerLoop 派发回 Unity 主线程，订阅者异常会被 `FrameLog.Exception` 捕获。

#### TCP 编解码

TCP 是字节流，没有天然消息边界，因此框架默认使用 `LengthPrefixedSocketCodec`：每个包前 4 字节大端 int 表示 payload 长度，后面跟 payload。`SocketReceiveBuffer` 保存未消费字节，`TryDecode` 在数据不足时返回 false，长度非法或超过最大值时抛异常。项目协议如果不是长度前缀，可以实现 `ISocketMessageCodec` 并赋给 `SocketClientOptions.Codec`。

#### WebSocket

WebSocket 使用 `ClientWebSocket` 的帧边界，不走 TCP codec。接收时会把分片合并到 `EndOfMessage`，按 `WebSocketMessageType.Text/Binary` 转为 `SocketMessageKind.Text/Binary`。WebGL 非 Editor 构建不能使用 .NET `ClientWebSocket`，需要浏览器 WebSocket bridge 或单独 transport。

---

### 流转逻辑

1. **便捷与强类型入口**：`Get`/`PostJson(string json)` 走非泛型 `Send` 回调 `HttpResponse`；`GetJson<T>`/`PostJson<Req,Resp>`/`SendJson<T>` 走 `SendJson`，在完成回调中用 `HttpResponse<TResponse>.From(response, ResponseParser)` 转强类型（`ResponseParser` 为 `null` 时回退 `JsonHttpResponseParser.Instance`）。`PostJson<Req,Resp>` 先 `JsonConvert.SerializeObject(body)` 得到 body。
2. **`Send` → `SendAsync`**：`Send` 立即返回 `HttpRequestHandle`，把 `PrepareRequest(request)` 后的请求交给 `SendAsync(...).Forget()`（UniTask fire-and-forget）。
3. **`PrepareRequest`（头合并 + URL 解析）**：深拷贝原请求字段；`Url = ResolveUrl(source.Url)`；先把所有 `defaultHeaders` 写入 `request.Headers`，**再用 `source.Headers` 覆盖同名项**——即每请求头优先于默认头。
4. **`ResolveUrl`（BaseUrl 拼接）**：`url` 空白、或 `BaseUrl` 空白、或 `url` 已是绝对 URI → 原样返回；否则 `BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/')`（确保只有一个 `/`）。
5. **`BeginRequest`**：`activeRequestCount++`、`startedRequestCount++`，触发 `RequestStarted`（异常隔离）。
6. **空 URL 短路**：`request == null` 或 `Url` 空白 → 直接构造 `Success=false, Error="Url is empty."`，`FinishRequest` + `Complete` 返回。
7. **重试循环**：`attempts = Max(0, Retries) + 1`，`for i in [0, attempts)`：若 `handle.IsCanceled` 则 break；`using (var webRequest = CreateUnityRequest(request))`：`handle.Attach(webRequest)`；在 try 中设 `timeout = Max(1, TimeoutSeconds)` 并逐个 `SetRequestHeader`（跳过空白 key），设置头时若异常则 `shouldSend=false` 且 `Error = ex.Message`；若 `shouldSend` 则 `await webRequest.SendWebRequest().ToUniTask()`，正常或捕获 `UnityWebRequestException` 后都用 `CreateResponse(webRequest)` 得到响应；随后 `handle.Detach(webRequest)`；若 `finalResponse.Success` 则 break。若不是最后一次且 `RetryDelaySeconds > 0`，`await UniTask.Delay(Ceil(RetryDelaySeconds*1000), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update)` 后重试。
8. **异常与兜底**：整个循环外层 try/catch，未取消时异常记 `FrameLog.Exception` 并置 `Error = ex.Message`。循环后：若 `handle.IsCanceled` → 覆盖为 `Success=false, Error="Request canceled."`；否则若 `finalResponse == null` → `Success=false, Error="Request failed."`。
9. **`CreateResponse`**：`Success = result == Result.Success`，`StatusCode = responseCode`，`Text/Data` 取自 `downloadHandler`（null 安全），`Error = webRequest.error`。
10. **`FinishRequest`**：`activeRequestCount = Max(0, active-1)`、`completedRequestCount++`；响应为 null 或不成功则 `failedRequestCount++`；触发 `RequestCompleted`（异常隔离）。
11. **`Complete`**：`handle.Complete(response)`（置 `Response`、`IsDone=true`、清 `webRequest`）；再调用业务 `completed` 回调，**用 try/catch 隔离回调异常**（不影响计数与句柄状态）。
12. **取消（`Cancel`）**：`HttpRequestHandle.Cancel()` 置 `IsCanceled=true` 并 `webRequest.Abort()`；`SendAsync` 在每轮循环开头与结束兜底处检测 `IsCanceled`，最终响应统一为 `Success=false, Error="Request canceled."`。`Attach` 还处理“发送前已取消”的竞态（绑定即 Abort）。
13. **指标用途**：`ActiveRequestCount`/`StartedRequestCount`/`CompletedRequestCount`/`FailedRequestCount` 与 `RequestStarted`/`RequestCompleted` 事件供诊断 Overlay 实时展示；`ClearMetrics` 只清累计三项，不动 active。
14. **Socket 创建**：业务通过 `ISocketService.CreateTcpClient` 或 `CreateWebSocketClient` 构造 `SocketClientOptions`，可在 configure 回调里设置重连、心跳、headers、codec 等参数；`SocketService.CreateClient` 创建 `SocketClient` 并加入 `Clients`。
15. **Socket 连接**：`SocketClient.ConnectAsync` 用 `connectGate` 防并发连接，按 transport 进入 `ConnectTcpAsync` 或 `ConnectWebSocketAsync`；成功后置 `Connected`、派发 `Connected` 事件并启动 send/receive/heartbeat loop。
16. **Socket 发送**：`SendText`/`Send(byte[])` 会包装为 `SocketMessage` 并入队；send loop 被 `sendSignal` 唤醒后，TCP 用 codec 编成 frame 写入 stream，WebSocket 用 `ClientWebSocket.SendAsync` 发送文本或二进制帧。
17. **Socket 接收**：TCP receive loop 持续读 stream，追加到 `SocketReceiveBuffer` 并循环 `TryDecode`；WebSocket receive loop 合并分片直到完整消息。收到消息后更新 metrics，并通过 PlayerLoop 派发 `MessageReceived`。
18. **Socket 心跳和重连**：heartbeat loop 定时发送配置的 payload，并在超过 `HeartbeatTimeoutSeconds` 未收到任何消息时触发 timeout。非主动断开导致的错误会进入 `HandleConnectionFailureAsync`，关闭 transport、派发 `Disconnected`，再按指数退避进入 `ReconnectLoopAsync`；主动 `DisconnectAsync` 或 `Dispose` 不触发重连。

### 使用示例

```csharp
using Frame.Networking;

IHttpService http = Context.Services.Resolve<IHttpService>();
http.BaseUrl = "https://api.example.com";
http.SetBearerToken("eyJhbGci...");           // 注入 Authorization: Bearer ...
http.ResponseParser = new EnvelopeHttpResponseParser(); // 切换为信封解析

// 1) 简单 GET（相对路径自动拼 BaseUrl）
http.Get("/ping", resp =>
{
    Debug.Log(resp.Success ? resp.Text : resp.Error);
});

// 2) 强类型 GET，信封 data 反序列化为 UserDto
http.GetJson<UserDto>("/users/1", resp =>
{
    if (resp.Success) Debug.Log(resp.Value.Name);
    else Debug.LogWarning($"{resp.ErrorCode} {resp.Message} {resp.Error}");
});

// 3) 强类型 POST
http.PostJson<LoginReq, LoginResp>("/login", new LoginReq { user = "a", pwd = "b" }, resp =>
{
    if (resp.Success) http.SetBearerToken(resp.Value.token);
});

// 4) 完整自定义请求 + 重试 + 协程等待 + 取消
var req = new HttpRequest
{
    Url = "/upload",
    Method = HttpMethod.Post,
    Body = "{\"k\":1}",
    ContentType = "application/json",
    TimeoutSeconds = 30,
    Retries = 2,
    RetryDelaySeconds = 0.5f
};
req.Headers["X-Trace"] = "abc";   // 每请求头覆盖同名默认头
HttpRequestHandle handle = http.Send(req, resp => { /* ... */ });

IEnumerator Wait()
{
    yield return handle;          // CustomYieldInstruction
    Debug.Log(handle.Response.StatusCode);
}
// 需要时取消：handle.Cancel(); // 最终 resp.Error == "Request canceled."
```

Socket 示例：

```csharp
ISocketService sockets = Framework.Resolve<ISocketService>();

ISocketClient tcp = sockets.CreateTcpClient("127.0.0.1", 9000, options =>
{
    options.AutoReconnect = true;
    options.HeartbeatIntervalSeconds = 10f;
    options.HeartbeatTimeoutSeconds = 30f;
    options.HeartbeatPayload = System.Text.Encoding.UTF8.GetBytes("ping");
});
tcp.MessageReceived += (socket, message) => Debug.Log(message.Text);
await tcp.ConnectAsync();
tcp.SendText("hello");

ISocketClient ws = sockets.CreateWebSocketClient("wss://example.com/realtime", options =>
{
    options.WebSocketHeaders = new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer " + accessToken
    };
});
await ws.ConnectAsync();
ws.SendText("{\"op\":\"join\"}");
```

### 设计意图与踩坑点

- **每请求头优先于默认头**：`PrepareRequest` 先写默认头再用 `source.Headers` 覆盖，所以同名头以请求级为准。`defaultHeaders` 与 `HttpRequest.Headers` 都用 `OrdinalIgnoreCase`，头名大小写不影响覆盖。
- **`Content-Type` 设置点不同**：上传请求（POST/PUT）在 `CreateUploadRequest` 内**显式 `SetRequestHeader("Content-Type", ...)`**（空白回退 `application/json`），而 GET/DELETE 不带 body 也就不设。若你在 `Headers` 里再放 `Content-Type`，循环里 `SetRequestHeader` 会再次设置/覆盖。
- **`BaseUrl` 仅对相对 URL 生效**：绝对 URL（`Uri.IsWellFormedUriString(..., Absolute)`）原样发送；拼接只做一次斜杠规整，不解析 `..` 等相对段。
- **重试只在“非成功”时进行**：`finalResponse.Success` 为 true 即跳出；4xx/5xx 因 `result != Success` 也会触发重试，注意可能放大对服务端的压力。`attempts = Retries + 1`，`Retries=0` 表示只发 1 次。
- **超时下限 1 秒、间隔用未缩放时间**：`timeout = Max(1, TimeoutSeconds)`；重试 `UniTask.Delay` 用 `DelayType.UnscaledDeltaTime`，不受 `Time.timeScale` 影响（暂停游戏也会重试）。
- **取消语义固定**：无论在何处取消，最终响应都被强制为 `Success=false, Error="Request canceled."`；`Attach` 处理了“句柄已取消但请求刚创建”的竞态（绑定即 `Abort`）。
- **回调异常被隔离**：业务 `completed`、`RequestStarted`、`RequestCompleted` 的异常都被 `try/catch` + `FrameLog.Exception` 吞掉，不会中断请求生命周期或污染计数器——排障要看日志。
- **解析器回退链**：`SendJson` 用 `ResponseParser`（null 回退 `JsonHttpResponseParser.Instance`）。`EnvelopeHttpResponseParser` 在“响应不是合法 JSON 对象”或“不含任何信封字段”时**自动回退**裸 JSON 解析，因此对普通 JSON 接口也安全。
- **信封成功判定较宽松**：默认成功码集合含 `0/200/OK/Success`（大小写不敏感），且两标志字段都缺失时（`TreatMissing*AsSuccess` 均为 true）判成功；若后端用其它码表示成功，需要往 `SuccessCodes` 里补充或调字段名。
- **`string`/`byte[]` 走特殊分支**：两个解析器对 `TData == string` / `byte[]` 直接取 `Text`/`Data` 而非反序列化，避免对纯文本/二进制再套一层 JSON。信封解析器里 `string` 还会在 data 非字符串节点时做 `ToString(Formatting.None)`。
- **`ClearMetrics` 不清 active**：只清累计三项；若想完整复位需走 `OnShutdown`（模块关闭）。
- **`DataField` 留空 = 整包即数据**：`EnvelopeHttpResponseParser.DataField` 设为空白时把整个 root 当 data，适配“成功标志和数据混在同层”的协议。
- **TCP 必须有消息边界**：默认 `LengthPrefixedSocketCodec` 解决粘包/拆包；如果服务端不是 4 字节大端长度前缀，必须替换 `SocketClientOptions.Codec`。
- **Socket 回调回到主线程**：`SocketClient` 的外部事件通过 UniTask PlayerLoop 派发，适合直接更新 Unity 对象；订阅者异常同样会被日志捕获。
- **主动断开不重连**：`DisconnectAsync` 和 `Dispose` 会标记 local disconnect，不进入自动重连。网络异常、远端关闭、心跳超时才会触发重连流程。
- **WebGL transport 另算**：WebGL 非 Editor 不能使用 .NET `TcpClient`/`ClientWebSocket`；需要浏览器 WebSocket bridge 或单独实现。

## 20. Input 模块

Input 模块为框架提供「上下文驱动」的输入抽象层。它通过 `InputContext` 枚举在 *Gameplay / UI / Disabled* 三种模式之间切换，并支持上下文栈（Push/Pop），从而让弹窗、菜单等临时界面在打开时压入 UI 上下文、关闭时自动恢复。模块本身用 `#if ENABLE_INPUT_SYSTEM` 条件编译，将「新版 Input System」与「旧版 `UnityEngine.Input`」两套实现彻底分开：两套实现暴露的接口成员完全不同（见下文），但上下文管理逻辑在两套分支中共享。

> 注意：本项目 `ProjectSettings` 的 `activeInputHandler=2`（Both，新旧输入系统同时启用）。当工程启用了 Input System 包时，编译符号 `ENABLE_INPUT_SYSTEM` 被定义，因此实际生效的是 Input System 分支。`InputService` 继承自 `GameModuleBase`，在 `OnInitialize` 中把自身注册到 `Context.Services`，其模块优先级 `Priority = 0`（未在源码中重写 `Priority`，使用 `GameModuleBase` 默认值 0）。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `InputContext`（enum） | 输入上下文模式 | 三个值，顺序为 `Disabled=0, Gameplay=1, UI=2`；`Disabled` 禁用一切输入 |
| `IInputService`（interface） | 输入服务契约 | 成员集合随 `ENABLE_INPUT_SYSTEM` 而变；上下文相关成员两套分支共有 |
| `InputService`（class，sealed） | 输入服务实现 | `GameModuleBase` 子类，`Priority=0`；维护 `currentContext` 与 `contextStack` |

### `InputContext.cs`

枚举 `InputContext`，命名空间 `Frame.Input`。无显式赋值，按声明顺序取默认整型值。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Disabled` | 禁用输入 | 整型值 `0`（第一个成员）。处于此上下文时：Input System 分支调用 `actions.Disable()`；旧版分支 `GetKey/GetKeyDown` 直接返回 `false` |
| `Gameplay` | 玩法上下文 | 整型值 `1`。Input System 分支下启用名为 `"Player"` 的 ActionMap（常量 `GameplayActionMapName`） |
| `UI` | 界面上下文 | 整型值 `2`。Input System 分支下启用名为 `"UI"` 的 ActionMap（常量 `UIActionMapName`） |

> `InputService` 字段 `currentContext` 的初始值为 `InputContext.Gameplay`（默认进入玩法上下文）。

### `IInputService.cs`

接口 `IInputService`，命名空间 `Frame.Input`。成员分为「始终存在」「仅 Input System」「仅旧版」三组。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `InputContext CurrentContext { get; }` | 当前上下文（只读属性） | **始终存在**（不在任何 `#if` 内） |
| `int ContextStackDepth { get; }` | 上下文栈深度（只读属性） | **始终存在** |
| `void SetContext(InputContext context)` | 直接设置当前上下文 | **始终存在** |
| `IDisposable PushContext(InputContext context)` | 压栈并切换上下文，返回 `IDisposable`，`Dispose` 时弹栈恢复 | **始终存在** |
| `bool PopContext()` | 弹栈恢复上一上下文，栈空返回 `false` | **始终存在** |
| `void SetActions(InputActionAsset actionAsset)` | 绑定 `InputActionAsset` | **仅 `#if ENABLE_INPUT_SYSTEM`** |
| `InputAction FindAction(string actionName)` | 按名查找 `InputAction` | **仅 Input System** |
| `bool WasPressedThisFrame(string actionName)` | 当前帧是否按下该 Action | **仅 Input System** |
| `Vector2 ReadVector2(string actionName)` | 读取 Action 的 `Vector2` 值 | **仅 Input System** |
| `bool ApplyBindingOverride(string actionName, int bindingIndex, string overridePath)` | 对指定绑定索引应用重映射路径 | **仅 Input System** |
| `bool ClearBindingOverride(string actionName, int bindingIndex)` | 清除指定绑定索引的重映射 | **仅 Input System** |
| `void ClearBindingOverrides()` | 清除全部重映射 | **仅 Input System** |
| `string SaveBindingOverridesAsJson()` | 将全部重映射序列化为 JSON 字符串 | **仅 Input System** |
| `void LoadBindingOverridesFromJson(string json, bool removeExisting = true)` | 从 JSON 加载重映射，默认先移除现有重映射 | **仅 Input System**；默认参数 `removeExisting = true` |
| `bool GetKey(KeyCode key)` | 该键是否处于按下状态 | **仅 `#else`（旧版 `UnityEngine.Input`）** |
| `bool GetKeyDown(KeyCode key)` | 该键是否在当前帧按下 | **仅旧版** |

### `InputService.cs`

类 `InputService : GameModuleBase, IInputService`（`sealed`），命名空间 `Frame.Input`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `const string GameplayActionMapName = "Player"` | 玩法 ActionMap 名 | **私有常量**。注意：尽管枚举叫 `Gameplay`，对应的 ActionMap 名是 `"Player"` |
| `const string UIActionMapName = "UI"` | 界面 ActionMap 名 | 私有常量 |
| `Stack<InputContext> contextStack` | 上下文栈 | `readonly` 私有字段，构造时初始化 |
| `InputContext currentContext` | 当前上下文 | 私有字段，初始值 `InputContext.Gameplay` |
| `InputActionAsset actions` | 当前绑定的动作资源 | **仅 `#if ENABLE_INPUT_SYSTEM`** 字段 |
| `InputContext CurrentContext { get; }` | 返回 `currentContext` | 始终存在 |
| `int ContextStackDepth { get; }` | 返回 `contextStack.Count` | 始终存在 |
| `protected override void OnInitialize()` | 模块初始化 | 注册 `Context.Services.Register<IInputService>(this)` 与 `Context.Services.Register(this)`（按具体类型再注册一次） |
| `void SetContext(InputContext context)` | 设置 `currentContext` | Input System 分支下额外调用 `ApplyActionMapState()`（旧版分支无此调用） |
| `IDisposable PushContext(InputContext context)` | 压入当前上下文并切换 | `contextStack.Push(currentContext)` → `SetContext(context)` → 返回 `new DisposableAction(() => PopContext())`（`Frame.Utilities` 提供）。`Dispose` 即弹栈恢复 |
| `bool PopContext()` | 弹栈恢复 | 栈空返回 `false`；否则 `SetContext(contextStack.Pop())` 后返回 `true` |
| `void SetActions(InputActionAsset actionAsset)` | 替换动作资源 | **仅 Input System**。若已有 `actions` 先 `Disable()`，再赋值并 `ApplyActionMapState()` |
| `InputAction FindAction(string actionName)` | 查找 Action | **仅 Input System**。`actions==null` 或名称空白返回 `null`；否则 `actions.FindAction(actionName, false)`（第二参 `throwIfNotFound=false`） |
| `bool WasPressedThisFrame(string actionName)` | 帧内按下判定 | **仅 Input System**。`action != null && action.WasPressedThisFrame()` |
| `Vector2 ReadVector2(string actionName)` | 读取二维向量 | **仅 Input System**。未找到返回 `Vector2.zero`，否则 `action.ReadValue<Vector2>()` |
| `bool ApplyBindingOverride(string actionName, int bindingIndex, string overridePath)` | 应用重映射 | **仅 Input System**。校验 `action!=null`、`0 <= bindingIndex < action.bindings.Count`、`overridePath` 非空白，任一失败返回 `false`；通过则 `action.ApplyBindingOverride(bindingIndex, overridePath)` 返回 `true` |
| `bool ClearBindingOverride(string actionName, int bindingIndex)` | 清除单个重映射 | **仅 Input System**。同样校验索引范围；通过则 `action.RemoveBindingOverride(bindingIndex)` 返回 `true` |
| `void ClearBindingOverrides()` | 清除全部重映射 | **仅 Input System**。`actions != null` 时 `actions.RemoveAllBindingOverrides()` |
| `string SaveBindingOverridesAsJson()` | 导出重映射 | **仅 Input System**。`actions==null` 返回 `string.Empty`，否则 `actions.SaveBindingOverridesAsJson()` |
| `void LoadBindingOverridesFromJson(string json, bool removeExisting = true)` | 导入重映射 | **仅 Input System**。`actions==null` 直接返回；`json` 空白时：若 `removeExisting` 则 `RemoveAllBindingOverrides()` 后返回；否则 `actions.LoadBindingOverridesFromJson(json, removeExisting)` 并 `ApplyActionMapState()` |
| `void ApplyActionMapState()` | 根据上下文启停 ActionMap | **仅 Input System**，私有。`actions==null` 直接返回；`Disabled` 上下文调用 `actions.Disable()` 返回；否则先 `actions.Enable()`，再遍历 `actionMaps`，仅当（`Gameplay` 且 `map.name=="Player"`）或（`UI` 且 `map.name=="UI"`）时 `map.Enable()`，其余 `map.Disable()` |
| `bool GetKey(KeyCode key)` | 旧版按键持续状态 | **仅 `#else`**。`currentContext != Disabled && UnityEngine.Input.GetKey(key)` |
| `bool GetKeyDown(KeyCode key)` | 旧版按键当前帧按下 | **仅 `#else`**。`currentContext != Disabled && UnityEngine.Input.GetKeyDown(key)` |
| `protected override void OnShutdown()` | 模块关闭 | `contextStack.Clear()`；Input System 分支下若 `actions!=null` 则 `Disable()` 并置 `null` |

### 流转逻辑

**条件编译两套分支（关键）**

- 当定义 `ENABLE_INPUT_SYSTEM`（即工程引入 Input System 包，本项目 `activeInputHandler=2` Both 时成立）：
  - 接口/实现暴露 `SetActions / FindAction / WasPressedThisFrame / ReadVector2 / ApplyBindingOverride / ClearBindingOverride / ClearBindingOverrides / SaveBindingOverridesAsJson / LoadBindingOverridesFromJson`。
  - **不存在** `GetKey / GetKeyDown`。
- 否则（`#else`，仅旧版 `UnityEngine.Input`）：
  - 仅暴露 `GetKey / GetKeyDown`。
  - **不存在** 上述所有 Input System 方法及 `actions` 字段。
- 两套分支**共有**：`CurrentContext / ContextStackDepth / SetContext / PushContext / PopContext`，以及 `OnInitialize / OnShutdown / contextStack / currentContext`。

**Input System 完整流转**

1. `SetActions(asset)` 绑定 `InputActionAsset`；若已有旧资源先 `Disable()`。
2. `SetContext` / `PushContext` 改变 `currentContext` 后调用 `ApplyActionMapState()`：
   - `Disabled` → 整个 asset `Disable()`。
   - `Gameplay` → 启用 asset，并只 `Enable()` 名为 `"Player"` 的 ActionMap，其余 `Disable()`。
   - `UI` → 启用 asset，并只 `Enable()` 名为 `"UI"` 的 ActionMap，其余 `Disable()`。
3. 运行时查询：`WasPressedThisFrame(name)`（帧内边沿）、`ReadVector2(name)`（移动/瞄准等连续量）。
4. 按键重映射：`ApplyBindingOverride(name, index, path)` 改键 → `SaveBindingOverridesAsJson()` 持久化（可写入存档/`PlayerPrefs`） → 下次启动 `LoadBindingOverridesFromJson(json)` 还原（默认 `removeExisting=true` 先清空旧重映射，加载后重新 `ApplyActionMapState()`） → `ClearBindingOverride(name,index)` / `ClearBindingOverrides()` 恢复默认。

**旧版流转**

- 仅靠 `GetKey/GetKeyDown`，二者都先判断 `currentContext != InputContext.Disabled`，因此把上下文切到 `Disabled` 即可一键屏蔽全部旧版按键查询；`Gameplay` 与 `UI` 在旧版分支下行为相同（无 ActionMap 概念，不做区分）。

**上下文栈**

- `PushContext(ctx)`：当前上下文入栈、切到 `ctx`，返回 `IDisposable`；`using` 作用域结束或显式 `Dispose()` 时自动 `PopContext()`。
- `PopContext()`：栈非空时恢复栈顶上下文返回 `true`，栈空返回 `false`。
- `SetContext`：不经过栈，直接设置（不会影响已压栈的内容）。
- `OnShutdown` 会清空栈，Input System 分支同时禁用并释放 `actions`。

### 使用示例

```csharp
using Frame.Core;
using Frame.Input;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputExample : MonoBehaviour
{
    private IInputService input;

#if ENABLE_INPUT_SYSTEM
    [SerializeField] private InputActionAsset actionAsset;
#endif

    private void Start()
    {
        Framework.TryResolve(out input);

#if ENABLE_INPUT_SYSTEM
        var svc = (InputService)input;
        svc.SetActions(actionAsset);          // 绑定动作资源
        input.SetContext(InputContext.Gameplay); // 启用 "Player" ActionMap

        // 从存档恢复改键
        string saved = PlayerPrefs.GetString("rebind", string.Empty);
        svc.LoadBindingOverridesFromJson(saved);
#endif
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var svc = (InputService)input;
        Vector2 move = svc.ReadVector2("Move");
        if (svc.WasPressedThisFrame("Jump"))
        {
            Debug.Log("Jump pressed");
        }
#else
        if (input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("Jump pressed (legacy)");
        }
#endif
    }

    // 打开暂停菜单：压入 UI 上下文，关闭时自动恢复
    public void OpenPauseMenu()
    {
        using (input.PushContext(InputContext.UI))
        {
            // ... 菜单交互期间，Gameplay ActionMap 被禁用、UI ActionMap 被启用 ...
        } // Dispose -> 自动 PopContext，恢复 Gameplay
    }

#if ENABLE_INPUT_SYSTEM
    // 改键并持久化
    public void Rebind(string action, int index, string path)
    {
        var svc = (InputService)input;
        if (svc.ApplyBindingOverride(action, index, path))
        {
            PlayerPrefs.SetString("rebind", svc.SaveBindingOverridesAsJson());
        }
    }
#endif
}
```

### 设计意图与踩坑点

- **`Gameplay` 枚举名 ≠ ActionMap 名**：玩法上下文对应的 ActionMap 必须命名为 `"Player"`（常量 `GameplayActionMapName`），UI 对应 `"UI"`。若你的 `InputActionAsset` 里 ActionMap 命名不同，`ApplyActionMapState` 不会启用任何匹配的 Map，导致输入「全部失灵」。这是最常见的坑。
- **重映射相关方法仅存在于 Input System 分支**：在纯旧版工程（未定义 `ENABLE_INPUT_SYSTEM`）下调用 `SaveBindingOverridesAsJson` 等会编译失败——它们根本不在接口里。编写跨分支代码时务必用 `#if ENABLE_INPUT_SYSTEM` 包裹。
- **`Disabled` 的行为不对称**：Input System 分支下 `Disabled` 调用 `actions.Disable()`（连 Action 回调都不会触发）；旧版分支下 `Disabled` 只是让 `GetKey/GetKeyDown` 返回 `false`（但 `UnityEngine.Input` 本身仍在采集）。
- **`PushContext` 必须配合 `using` 或显式 `Dispose`**：返回的 `IDisposable` 是恢复上下文的唯一可靠手段；如果忘记释放又手动 `SetContext`，栈中残留的上下文会在后续 `PopContext` 时错误地「弹回」旧值。
- **`SetContext` 不进栈**：它与 `PushContext` 是两条独立路径。混用时注意：`SetContext` 改的是当前值，不影响已压栈的历史，可能造成 Pop 后回到非预期上下文。
- **`LoadBindingOverridesFromJson` 空 JSON 的语义**：传入空白字符串时，若 `removeExisting=true` 会清空所有重映射（相当于复位），这点在「恢复默认」场景下可被利用，但若用于「不修改」目的会误清空。
- **`OnInitialize` 双重注册**：同时按接口与具体类型注册，方便需要调用 Input System 专有方法（如 `SetActions`）的代码通过具体类型 `InputService` 解析。
- **模块 `Priority=0`**：未重写优先级，初始化顺序由框架默认决定；若有模块依赖 Input 已就绪，需自行排序。

---

## 21. Localization 模块

Localization 模块提供运行时多语言文本服务。核心由 `LocalizationService`（`GameModuleBase` 子类，`Priority=0`）、数据载体 `LocalizedTextTable`（`ScriptableObject`，支持表格/CSV/TSV 导入）、UGUI 组件 `LocalizedText`（自动刷新 `UnityEngine.UI.Text`）三部分组成。服务维护「当前语言 + 回退语言」「多张文本表（后加入者覆盖先加入者）」「缺失键追踪」，并在切换语言时广播事件。

> `LocalizationService` 在 `OnInitialize` 中注册到 `Context.Services`；未重写 `Priority`，使用 `GameModuleBase` 默认值 `Priority = 0`。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ILocalizationService`（interface） | 本地化服务契约 | `LocaleChanged` 事件、`Translate`（带 `string.Format` 参数）、`MissingKeys` 追踪 |
| `LocalizationService`（class，sealed） | 本地化服务实现 | 默认 `currentLocale="en"`、`fallbackLocale="en"`；表列表倒序查表实现「后覆盖先」 |
| `LocalizedTextTable`（ScriptableObject，sealed） | 文本数据表 | `key` 列 + 多 `locale` 列；支持 `TextAsset` 源或导入的 CSV/TSV 文本；惰性构建 `locale->key->value` 缓存 |
| `LocalizedText`（MonoBehaviour，sealed） | UGUI 本地化文本组件 | 挂在带 `Text` 的对象上，启用/语言变更时自动刷新 |

### `ILocalizationService.cs`

接口 `ILocalizationService`，命名空间 `Frame.Localization`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `event Action<string> LocaleChanged` | 语言变更事件，参数为新 locale | 仅在 `SetLocale` 实际改变语言时触发 |
| `string CurrentLocale { get; }` | 当前语言（只读） | 默认 `"en"` |
| `string FallbackLocale { get; set; }` | 回退语言（可读写） | 默认 `"en"`；setter 对空白值置 `null`，否则 `Trim()` |
| `IReadOnlyCollection<string> MissingKeys { get; }` | 已记录的缺失键集合（只读） | 由 `Translate` 在查不到键时填充 |
| `void SetLocale(string locale)` | 切换当前语言 | 空白或与当前相同则不动作 |
| `void AddTable(LocalizedTextTable table)` | 添加文本表 | 先移除同引用再加入末尾（保证置于最高优先级） |
| `bool RemoveTable(LocalizedTextTable table)` | 移除文本表 | 移除成功返回 `true` |
| `void ClearTables()` | 清空全部文本表 | — |
| `void ClearMissingKeys()` | 清空缺失键记录 | — |
| `bool TryTranslate(string key, out string value)` | 尝试翻译 | 先当前语言，再回退语言；找不到返回 `false` 且 `value=null`（不记录缺失键） |
| `string Translate(string key, string fallback = null, params object[] args)` | 翻译并格式化 | 找不到则记录缺失键并返回 `fallback`（为 `null` 时返回 `key` 本身）；`args` 走 `string.Format` |

### `LocalizationService.cs`

类 `LocalizationService : GameModuleBase, ILocalizationService`（`sealed`），命名空间 `Frame.Localization`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `List<LocalizedTextTable> tables` | 文本表列表 | `readonly` 私有；倒序遍历实现「后加入覆盖先加入」 |
| `HashSet<string> missingKeys` | 缺失键集合 | `readonly` 私有；去重 |
| `string currentLocale` | 当前语言 | 初始 `"en"` |
| `string fallbackLocale` | 回退语言 | 初始 `"en"` |
| `event Action<string> LocaleChanged` | 语言变更事件 | `OnShutdown` 中置 `null` |
| `string CurrentLocale { get; }` | 返回 `currentLocale` | — |
| `string FallbackLocale { get; set; }` | 回退语言属性 | setter：`string.IsNullOrWhiteSpace(value) ? null : value.Trim()` |
| `IReadOnlyCollection<string> MissingKeys { get; }` | 返回 `missingKeys` | 直接返回内部集合引用（只读视图） |
| `protected override void OnInitialize()` | 模块初始化 | 注册 `ILocalizationService` 与具体类型两次 |
| `void SetLocale(string locale)` | 切换语言 | 先 `Trim`/空白判 `null`；空白或与 `currentLocale` 相同直接返回；否则更新并触发 `LocaleChanged`，事件回调异常被 `FrameLog.Exception` 捕获（不外抛） |
| `void AddTable(LocalizedTextTable table)` | 添加表 | `null` 忽略；`tables.Remove(table)` 后 `tables.Add(table)`（去重并置末尾=最高优先级） |
| `bool RemoveTable(LocalizedTextTable table)` | 移除表 | `table != null && tables.Remove(table)` |
| `void ClearTables()` | 清空表 | `tables.Clear()` |
| `void ClearMissingKeys()` | 清空缺失键 | `missingKeys.Clear()` |
| `bool TryTranslate(string key, out string value)`（public） | 公开尝试翻译 | key 空白返回 `false`；先 `TryTranslate(currentLocale,...)`；失败且 `fallbackLocale` 非空白且 ≠ `currentLocale` 时再查回退语言 |
| `string Translate(string key, string fallback = null, params object[] args)` | 翻译 + 格式化 | key 空白返回 `ApplyFormat(fallback, args)`；命中走 `ApplyFormat(value, args)`；未命中 `missingKeys.Add(key)` 并返回 `ApplyFormat(fallback ?? key, args)`（fallback 为 null 用 key） |
| `protected override void OnShutdown()` | 模块关闭 | 清空 `tables`、`missingKeys`；`currentLocale`/`fallbackLocale` 复位 `"en"`；`LocaleChanged = null` |
| `bool TryTranslate(string locale, string key, out string value)`（private） | 按指定语言查表 | locale 空白返回 `false`；**倒序**遍历 `tables`（`i = Count-1 → 0`），首个 `tables[i].TryGet(locale,key,out value)` 成功即返回（实现后表覆盖前表） |
| `static string ApplyFormat(string template, object[] args)` | 安全格式化 | `template` 空或 `args` 空/零长时原样返回；否则 `string.Format(template, args)`，异常被 `FrameLog.Exception` 捕获并返回原 `template` |

### `LocalizedTextTable.cs`

类 `LocalizedTextTable : ScriptableObject`（`sealed`），`[CreateAssetMenu(menuName = "Frame/Localized Spreadsheet Table", fileName = "LocalizedTextTable")]`，命名空间 `Frame.Localization`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `TextAsset source` | 表格源文件 | `[SerializeField]`，`[Header("Spreadsheet Source")]`；优先使用 Excel 导出的 CSV/TSV `TextAsset` |
| `string delimiter = ","` | 分隔符 | `[SerializeField]`，默认 `","`；TSV 以字面 `"\\t"` 存储（两字符反斜杠+t），由 `ResolveDelimiter` 还原为 `'\t'` |
| `string importedText` | 导入的表格文本 | `[SerializeField, HideInInspector]`；`ImportCsv`/`ImportTsv` 写入，Inspector 不直接编辑 |
| `List<string> availableLocales` | 缓存可用语言 | `readonly` 私有；随 `EnsureLookup` 重建 |
| `Dictionary<string, Dictionary<string,string>> lookup` | 缓存 `locale->key->value` | 私有；惰性构建，命中查询 O(1) |
| `bool lookupDirty = true` | 缓存脏标记 | 私有；初值 `true` 强制首次构建 |
| `string Locale { get; }` | 主语言 | `Locales.Count > 0 ? Locales[0] : "en"`（无则 `"en"`） |
| `IReadOnlyList<string> Locales { get; }` | 可用语言列表 | getter 先 `EnsureLookup()` 再返回 `availableLocales` |
| `bool TryGet(string locale, string key, out string value)` | 按语言+键查值 | locale/key 空白返回 `false`；`EnsureLookup()` 后两级字典查找 |
| `bool TryGet(string key, out string value)` | 用主语言查值 | 转调 `TryGet(Locale, key, out value)` |
| `bool ContainsLocale(string locale)` | 是否含该语言 | 空白返回 `false`；`EnsureLookup()` 后 `lookup.ContainsKey(locale)` |
| `void Clear()` | 清空全部 | 置 `source=null`、清 `importedText`、`MarkDirty()` |
| `void ImportCsv(string csv)` | 导入 CSV | 转调 `ImportDelimitedText(csv, ',')`（逗号分隔） |
| `void ImportTsv(string tsv)` | 导入 TSV | 转调 `ImportDelimitedText(tsv, '\t')`（制表符分隔） |
| `void ImportDelimitedText(string text, char textDelimiter)` | 通用分隔文本导入 | 置 `source=null`；`delimiter` 记为 `"\\t"`（制表符）或字符本身；保存原始 `importedText`；`MarkDirty()` |
| `void SetSource(TextAsset textAsset, string textDelimiter = ",")` | 设置表格源 | `source=textAsset`；清 `importedText`；`delimiter` 空则 `","` 否则取传入值；`MarkDirty()` |
| `void OnEnable()` | 启用回调 | 私有；置 `lookupDirty=true` |
| `void OnValidate()` | 编辑器校验 | 私有，`#if UNITY_EDITOR`；置 `lookupDirty=true` |
| `void EnsureLookup()` | 构建/复用缓存 | 私有；不脏且 `lookup!=null` 直接返回；否则新建/清空字典、清 `availableLocales`；从 `source.text` 或 `importedText` 解析表格；末尾置 `lookupDirty=false` |
| `void BuildLookupFromRows(List<List<string>> rows)` | 从表格行建缓存 | 私有；空行集返回；**首行为表头**，列数 <2 返回；表头第 1 列为 key 列，第 2 列起为各 locale（`CleanKey`）；从第 2 行起：第 0 列为 key（空白跳过），按列写入 `AddLookupValue(locale, key, value)`，缺列用 `""` |
| `void AddLookupValue(string locale, string key, string value)` | 写入缓存项 | 私有；`CleanKey` locale/key，空白忽略；不存在的 locale 新建子字典并加入 `availableLocales`；`localeLookup[key] = value ?? ""` |
| `char ResolveDelimiter()` | 解析存储的分隔符 | 私有；`delimiter == "\\t"` 返回 `'\t'`；空返回 `','`；否则取 `delimiter[0]` |
| `void MarkDirty()` | 标记缓存脏 | 私有；`lookupDirty = true` |
| `static string CleanKey(string value)` | 清洗键/值 | 私有；`null` 返回 `""`；否则 `Trim()` 后 `TrimStart('﻿')`（去 BOM） |
| `static List<List<string>> ParseDelimited(string text, char textDelimiter)` | 解析分隔文本 | 私有；支持 RFC4180 风格引号：`"` 切换引号态、连续 `""` 转义为字面 `"`；引号外遇分隔符切字段、遇 `\r`/`\n`（含 `\r\n`）切行；末尾收尾行；经 `AddRowIfNotEmpty` 过滤 |
| `static void AddRowIfNotEmpty(List<List<string>> rows, List<string> row)` | 过滤空行 | 私有；任一字段非空才加入（全空行被丢弃） |

### `LocalizedText.cs`

类 `LocalizedText : MonoBehaviour`（`sealed`），特性 `[DisallowMultipleComponent]`、`[RequireComponent(typeof(Text))]`、`[AddComponentMenu("Frame/Localization/Localized Text")]`，命名空间 `Frame.Localization`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `string key` | 本地化键 | `[SerializeField]` |
| `string fallback` | 回退文本 | `[SerializeField]` |
| `Text target` | 目标 UGUI Text | `[SerializeField]`；`RequireComponent` 保证存在 |
| `ILocalizationService localization` | 绑定的服务 | 私有，运行时解析 |
| `string Key { get; }` | 返回 `key` | — |
| `string Fallback { get; }` | 返回 `fallback` | — |
| `Text Target { get; }` | 返回 `target` | getter 内先 `ResolveTarget()` |
| `void SetKey(string localizationKey)` | 设置键并刷新 | 赋值后 `Refresh()` |
| `void SetFallback(string fallbackText)` | 设置回退并刷新 | 赋值后 `Refresh()` |
| `void Bind(ILocalizationService service)` | 绑定服务 | 若与现有同引用仅 `Refresh()`；否则 `Unbind()` 后赋值并订阅 `LocaleChanged += OnLocaleChanged`，最后 `Refresh()` |
| `void Unbind()` | 解绑服务 | 若有绑定则 `LocaleChanged -= OnLocaleChanged` 并置 `null` |
| `void Refresh()` | 刷新文本 | `ResolveTarget()`；`target==null` 返回；未绑定且 `TryBindFromFramework()` 失败 → `target.text = GetFallbackText()`；否则 `target.text = localization.Translate(key, fallback为空则null) ?? ""` |
| `void Awake()` | 初始化 | 私有；`ResolveTarget()` |
| `void OnEnable()` | 启用刷新 | 私有；`TryBindFromFramework()` + `Refresh()`（启用即自动刷新） |
| `void Start()` | 启动刷新 | 私有；`TryBindFromFramework()` + `Refresh()` |
| `void OnDisable()` | 禁用解绑 | 私有；`Unbind()`（避免禁用对象仍响应语言变更） |
| `void Reset()` | 编辑器重置 | 私有；`target = GetComponent<Text>()` |
| `void OnValidate()` | 编辑器校验 | 私有，`#if UNITY_EDITOR`；补 `target`，运行中且激活则 `Refresh()` |
| `bool TryBindFromFramework()` | 从框架解析服务 | 私有；已绑定返回 `true`；否则 `Framework.TryResolve(out service)` 成功则 `Bind` 返回 `true`，失败返回 `false` |
| `void ResolveTarget()` | 解析目标 Text | 私有；`target==null` 时 `GetComponent<Text>()` |
| `void OnLocaleChanged(string locale)` | 语言变更回调 | 私有；调用 `Refresh()` |
| `string GetFallbackText()` | 取兜底文本 | 私有；`fallback` 非空返回 `fallback`，否则 `key` 非空返回 `key`，再否则 `""` |

### 流转逻辑

**语言与回退**

- `currentLocale` 默认 `"en"`，`FallbackLocale` 默认 `"en"`（setter 对空白置 `null`）。
- 翻译查找顺序：当前语言 → 回退语言（仅当回退语言非空白且与当前不同）。`TryTranslate` 找不到返回 `false`（不记缺失键）；`Translate` 找不到才记入 `MissingKeys` 并返回 `fallback`（为 `null` 时退回 `key` 本身）。
- `SetLocale` 只有在语言确实改变时才触发 `LocaleChanged`，回调异常被吞并记日志（不会中断其他订阅者）。

**多表覆盖**

- `AddTable` 把表去重后追加到列表末尾；`TryTranslate(locale,...)` **倒序**遍历表，因此**后加入的表覆盖先加入的同键值**。`RemoveTable/ClearTables` 管理表集合。

**LocalizedTextTable 解析与缓存**

- 数据来源是表格文本：优先使用 `source`（TextAsset）内容；没有绑定 source 时使用 `ImportCsv`/`ImportTsv` 保存的 `importedText`。两者都会按 `ResolveDelimiter()` 解析。
- 表格格式：**首行为表头**，第 0 列是 key 列，第 2 列（索引 1）起每列对应一个 locale；数据行第 0 列为 key，其余列与表头 locale 对齐；缺失单元格补空串。
- `ParseDelimited` 支持带引号字段（`"` 切换引号态、`""` 转义引号），并能处理 `\r\n`/`\r`/`\n` 行尾；全空行被丢弃。
- 缓存 `lookup`（`locale -> key -> value`）惰性构建：`lookupDirty` 为 `true` 时（首次、`OnEnable`、`OnValidate`、各写操作 `MarkDirty` 后）重建，之后查询 O(1)。CSV 与 TSV 的唯一区别是分隔符：CSV `','`，TSV `'\t'`（在 `delimiter` 字段里以字面 `"\\t"` 存储）。

**LocalizedText 自动刷新**

- `OnEnable`/`Start` 时 `TryBindFromFramework()`（经 `Framework.TryResolve` 解析 `ILocalizationService`）并 `Refresh()`，因此**组件一启用就显示当前语言文本**。
- 绑定后订阅 `LocaleChanged`，**语言切换时自动 `Refresh()`**。`OnDisable` 解绑，避免隐藏对象继续响应。
- 解析失败（框架未就绪）时退化为显示 `GetFallbackText()`（fallback 优先，其次 key，再次空串）。

### 使用示例

```csharp
using Frame.Core;
using Frame.Localization;
using UnityEngine;

public sealed class LocalizationExample : MonoBehaviour
{
    [SerializeField] private LocalizedTextTable table; // 资源里配置好 key/locale 列

    private void Start()
    {
        ILocalizationService loc;
        if (!Framework.TryResolve(out loc))
        {
            return;
        }

        loc.FallbackLocale = "en";     // 回退语言（默认就是 "en"）
        loc.AddTable(table);            // 后加入的表优先级最高
        loc.SetLocale("zh");            // 切换语言 -> 触发 LocaleChanged，LocalizedText 自动刷新

        // 直接翻译，带 string.Format 参数
        string hp = loc.Translate("ui.hp", fallback: "HP: {0}", 100); // -> "生命值: 100"（命中时）

        // 监听语言变化
        loc.LocaleChanged += newLocale => Debug.Log($"locale -> {newLocale}");

        // 缺失键诊断
        loc.Translate("nonexistent.key"); // 记入 MissingKeys，返回 key 本身
        foreach (string missing in loc.MissingKeys)
        {
            Debug.LogWarning($"Missing localization: {missing}");
        }
        loc.ClearMissingKeys();
    }
}
```

运行时从 CSV/TSV 字符串构建表：

```csharp
var table = ScriptableObject.CreateInstance<LocalizedTextTable>();
table.ImportCsv("key,en,zh\nui.ok,OK,确定\nui.cancel,Cancel,取消");
// 或： table.ImportTsv("key\ten\tzh\nui.ok\tOK\t确定");

string value;
if (table.TryGet("zh", "ui.ok", out value))
{
    Debug.Log(value); // 确定
}
```

### 设计意图与踩坑点

- **表优先级是「后加入覆盖先加入」**：`AddTable` 把表移到末尾、查表倒序遍历。若想让某张「补丁表」覆盖基础表，最后再 `AddTable` 它即可。重复 `AddTable` 同一引用不会产生重复（会先移除）。
- **`TryTranslate` 不记缺失键，`Translate` 才记**：做纯探测（不希望污染 `MissingKeys`）时用 `TryTranslate`；面向 UI 显示用 `Translate`，以便收集缺失键做翻译补全。
- **`MissingKeys` 返回内部集合引用**：它是只读视图但底层就是那个 `HashSet`，不要假设它是快照；并发遍历时若同时发生 `Translate` 可能引发集合修改问题（单线程 Unity 主线程下通常无碍）。
- **TSV 分隔符的字面存储**：`delimiter` 字段对制表符存的是两字符字符串 `"\\t"`，而非真正的制表符；`ResolveDelimiter` 负责还原。手改 asset 的 `delimiter` 字段时要遵循这一约定，否则解析会按错误分隔符切列。
- **CSV 引号与 BOM**：`ParseDelimited` 处理了引号转义和 `\r\n`；`CleanKey` 会去掉 UTF-8 BOM（`﻿`）和首尾空白。导出含逗号/换行的文本请用双引号包裹。
- **表头列数必须 ≥2**：`BuildLookupFromRows` 对列数 <2 的表头直接放弃（至少要有 key 列 + 1 个 locale 列）。
- **`LocalizedText` 依赖 `UnityEngine.UI.Text`（旧版 UGUI）**：`RequireComponent(typeof(Text))`，不支持 TextMeshPro。需要 TMP 时要另写组件。
- **`LocalizedText` 禁用即解绑**：`OnDisable` 会 `Unbind`，因此被禁用的对象在语言切换后不会自动更新，重新启用时 `OnEnable` 才会重新绑定并刷新。
- **`Translate` 的 fallback 语义**：未命中且 `fallback==null` 时返回的是 **key 本身**（便于在界面上一眼看出哪个键没翻译），而非空串。
- **模块 `Priority=0`**：与 Input 一样未重写优先级；`LocalizedText` 通过 `Framework.TryResolve` 容错获取服务，因此即便服务尚未就绪组件也不会报错，只会先显示兜底文本。

---

## 22. Tweening 模块

> 重要：Runtime 层**只有抽象**——`ITweenService`、`ITweenHandle`、枚举 `TweenEase`、配置类 `TweenOptions`。**实际实现是 DOTween**，位于 `Integrations`（`DOTweenTweenService`，由 `DOTweenModuleInstaller` 在 `FrameSettings.EnableTweenService` 为真时安装），单独文档说明。本节只讲抽象本身：方法签名、配置字段、枚举值、句柄能力。这样设计让业务代码只依赖 `Frame.Tweening` 抽象，可在不引入 DOTween 时降级（`IsAvailable` 为 `false`）或替换为其他补间后端。

### 类型总览

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ITweenService`（interface） | 补间服务契约 | `IsAvailable` 探测后端是否就绪；提供 `To/Move/Scale/Fade` 与 `Kill/KillAll` |
| `ITweenHandle`（interface） | 单个补间句柄 | `IsActive`/`IsPlaying`、`Play/Pause/Kill`、链式 `OnComplete` |
| `TweenEase`（enum） | 缓动曲线 | 10 个值（Linear + Quad/Cubic/Back 的 In/Out/InOut） |
| `TweenOptions`（class，sealed） | 补间可选配置 | `Ease`（默认 `OutQuad`）、`EaseCurve`、`IgnoreTimeScale`、`Target`、`Completed` |

### `TweenEase.cs`

枚举 `TweenEase`，命名空间 `Frame.Tweening`。无显式赋值，按声明顺序取整型值 0..9。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `Linear` | 线性 | 值 `0` |
| `InQuad` | 二次缓入 | 值 `1` |
| `OutQuad` | 二次缓出 | 值 `2`（`TweenOptions.Ease` 默认） |
| `InOutQuad` | 二次缓入缓出 | 值 `3` |
| `InCubic` | 三次缓入 | 值 `4` |
| `OutCubic` | 三次缓出 | 值 `5` |
| `InOutCubic` | 三次缓入缓出 | 值 `6` |
| `InBack` | 回弹缓入 | 值 `7` |
| `OutBack` | 回弹缓出 | 值 `8` |
| `InOutBack` | 回弹缓入缓出 | 值 `9` |

### `TweenOptions.cs`

类 `TweenOptions`（`sealed`），命名空间 `Frame.Tweening`。所有字段为 public，可在构造后直接赋值。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `TweenEase Ease = TweenEase.OutQuad` | 缓动曲线 | 默认 `OutQuad` |
| `bool IgnoreTimeScale` | 是否忽略 `Time.timeScale` | 默认 `false`（即受时间缩放影响） |
| `object Target` | 关联目标对象 | 默认 `null`；供 `ITweenService.Kill(object target, ...)` 按目标批量杀补间 |
| `Action Completed` | 完成回调 | 默认 `null`；补间正常结束时调用 |

> 注意：源码中 `TweenOptions` **不存在** `Loops`、`Delay`、`LoopType` 等字段——本抽象只暴露 `Ease / EaseCurve / IgnoreTimeScale / Target / Completed`。

### `ITweenHandle.cs`

接口 `ITweenHandle`，命名空间 `Frame.Tweening`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool IsActive { get; }` | 补间是否仍存活（未被杀死/未完成销毁） | 只读 |
| `bool IsPlaying { get; }` | 补间是否正在播放 | 只读 |
| `void Play()` | 播放/恢复 | — |
| `void Pause()` | 暂停 | — |
| `void Kill(bool complete = false)` | 杀死补间 | 默认 `complete=false`（不跳到终值）；`true` 则跳到终值再杀 |
| `ITweenHandle OnComplete(Action callback)` | 注册完成回调（链式） | 返回自身以便链式调用 |

### `ITweenService.cs`

接口 `ITweenService`，命名空间 `Frame.Tweening`。所有带 `TweenOptions` 的方法该参数默认 `null`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `bool IsAvailable { get; }` | 后端是否可用 | 只读；DOTween 未安装/未启用时为 `false`，调用方应据此降级 |
| `ITweenHandle To(Func<float> getter, Action<float> setter, float endValue, float duration, TweenOptions options = null)` | 通用浮点补间 | 通过 getter/setter 驱动任意 `float`；`endValue` 终值，`duration` 时长 |
| `ITweenHandle Move(Transform target, Vector3 endValue, float duration, bool local = false, TweenOptions options = null)` | 位移补间 | `local=false` 走世界坐标，`true` 走本地坐标 |
| `ITweenHandle Scale(Transform target, Vector3 endValue, float duration, TweenOptions options = null)` | 缩放补间 | 终值为 `localScale` 目标 |
| `ITweenHandle Fade(CanvasGroup target, float endValue, float duration, TweenOptions options = null)` | 透明度补间 | 作用于 `CanvasGroup.alpha`，`endValue` 一般取 0..1 |
| `int Kill(object target, bool complete = false)` | 按目标杀补间 | 返回被杀数量；`complete=true` 杀前跳终值 |
| `void KillAll(bool complete = false)` | 杀全部补间 | `complete=true` 全部跳终值 |

### 流转逻辑

**抽象与实现分层**

- Runtime 仅定义契约：`ITweenService`/`ITweenHandle`/`TweenEase`/`TweenOptions`。
- 实现在 `Integrations` 的 `DOTweenTweenService`，由 `DOTweenModuleInstaller` 安装，**仅当 `FrameSettings.EnableTweenService` 为真**时注册到 `Context.Services`。
- 调用方先 `Framework.TryResolve(out ITweenService tween)`，再用 `tween.IsAvailable` 判断后端是否就绪；若不可用则跳过补间或直接设终值。

**典型调用链**

1. 通过 `Move/Scale/Fade/To` 之一发起补间，传入可选 `TweenOptions`（设置 `Ease`、是否 `IgnoreTimeScale`、`Target`、`Completed` 回调）。
2. 方法返回 `ITweenHandle`：可 `Pause/Play` 控制播放、`OnComplete` 追加回调、查询 `IsActive/IsPlaying`。
3. 结束/打断：`handle.Kill(complete)` 杀单个；`service.Kill(target, complete)` 按 `target`（与 `TweenOptions.Target` 或方法目标对象关联）批量杀；`service.KillAll(complete)` 清场。
4. `complete=true` 表示杀之前先把属性跳到 `endValue`（保证视觉到位），`false` 则原地停。

**TweenOptions 与缓动**

- `Ease` 选 `TweenEase` 十值之一（默认 `OutQuad`）；`IgnoreTimeScale=true` 时补间不受 `Time.timeScale` 影响（适合暂停菜单动画）；`Completed` 与 `ITweenHandle.OnComplete` 都是完成回调来源。

### 使用示例

```csharp
using System;
using Frame.Core;
using Frame.Tweening;
using UnityEngine;

public sealed class TweenExample : MonoBehaviour
{
    [SerializeField] private Transform mover;
    [SerializeField] private CanvasGroup panel;

    private ITweenService tween;

    private void Start()
    {
        if (!Framework.TryResolve(out tween) || !tween.IsAvailable)
        {
            // 后端不可用（DOTween 未启用），直接设终态降级
            if (panel != null) panel.alpha = 1f;
            return;
        }

        var options = new TweenOptions
        {
            Ease = TweenEase.InOutCubic,
            IgnoreTimeScale = true,       // 暂停时仍播放
            Target = this,                 // 便于按目标批量 Kill
            Completed = () => Debug.Log("move done")
        };

        // 世界坐标位移
        ITweenHandle h = tween.Move(mover, new Vector3(5f, 0f, 0f), 1.0f, local: false, options);
        h.OnComplete(() => Debug.Log("chained complete"));

        // 面板淡入
        tween.Fade(panel, 1f, 0.5f);

        // 通用浮点补间（例如驱动一个进度值）
        float progress = 0f;
        tween.To(() => progress, v => progress = v, 1f, 2f,
            new TweenOptions { Ease = TweenEase.Linear });
    }

    private void OnDisable()
    {
        if (tween != null && tween.IsAvailable)
        {
            tween.Kill(this, complete: true); // 杀掉本对象关联的补间并跳终值
        }
    }
}
```

### 设计意图与踩坑点

- **务必先判 `IsAvailable`**：补间是可选能力（`FrameSettings.EnableTweenService` 控制安装）。不判直接调用，在后端缺失时实现可能返回不可用句柄或空操作，导致动画静默不生效。正确做法是 `IsAvailable==false` 时直接设终态。
- **抽象不含循环/延迟**：`TweenOptions` 只有 `Ease/EaseCurve/IgnoreTimeScale/Target/Completed`，**没有 `Loops`、`Delay`、`LoopType`**。需要循环或延迟必须自行用句柄编排或改用 DOTween 实现层（会破坏抽象隔离，谨慎）。
- **`Target` 是批量 Kill 的钥匙**：在 `TweenOptions.Target` 设好关联对象后，`Kill(target)` 才能按对象精确清理；否则只能靠 `KillAll` 或单句柄 `Kill`。
- **`complete` 参数的语义**：`Kill(true)`/`KillAll(true)` 会先把属性跳到 `endValue` 再终止（避免动画停在半途的视觉突兀）；`false` 则保持当前值。切场景/关面板时按需选择。
- **两路完成回调**：`TweenOptions.Completed` 与 `ITweenHandle.OnComplete` 都能挂完成回调；混用时注意两者都会触发，避免重复执行副作用。
- **`Fade` 仅作用于 `CanvasGroup`**：要淡入淡出的 UI 需要挂 `CanvasGroup`；单独的 `Image`/`Text` 颜色透明度不在此抽象内（可用 `To` 自行驱动）。
- **依赖 `Framework.TryResolve` 解析**：补间服务从 `Context.Services` 取，业务代码不应直接 `new` 任何实现，保持对 `ITweenService` 抽象的依赖以便替换后端。


本篇文档覆盖 Frame 框架的三个外围层：

- **Integrations 集成层**（`Assets/Frame/Integrations/`）：把第三方库（Addressables / YooAsset / DOTween）适配为框架内部的服务抽象。每个集成都位于**独立的程序集**（`Frame.Addressables` / `Frame.YooAsset` / `Frame.DOTween`），并通过实现 `IFrameModuleInstaller` 在启动时被反射扫描装配。
- **Editor 编辑器工具**（`Assets/Frame/Editor/`）：`Frame` 菜单下的创建、打开与工程校验工具，含 CI 校验入口。
- **Samples 示例**（`Assets/Frame/Samples/`）：一个最小可运行的学习示例 `FrameDemoController`。

> 关键前提（已核实）：`Frame.Runtime` 通过 `InternalsVisibleTo` 把内部成员暴露给 `Frame.Addressables` 与 `Frame.YooAsset`，因此这两个集成可以调用 `AssetHandle<T>` 的 **internal 构造函数**（`new AssetHandle<T>(IAssetService owner, string path, T asset)`）直接构造句柄。
> `IFrameModuleInstaller.Install(ModuleManager modules, FrameSettings settings)` 会在框架启动时被反射枚举并逐一调用——每个 Installer 自行判断「当前 `FrameSettings` 配置是否需要我」，满足条件才 `modules.Add(...)` 注册对应服务模块。
> `ITweenService` / `ITweenHandle` / `TweenEase` / `TweenOptions` 定义在 `Frame.Runtime` 的 `Frame.Tweening` 命名空间中，DOTween 集成只是它的一种实现。

---

## 23. Integrations 集成层

集成层的统一设计模式：

1. **服务类**继承 `GameModuleBase` 并实现框架接口（`IAssetService` 或 `ITweenService`），在 `OnInitialize` 中把自己注册进 `Context.Services`，在 `OnShutdown` 中释放底层资源。
2. **Installer 类**实现 `IFrameModuleInstaller`，只在 `FrameSettings` 满足精确条件时才把服务模块加入 `ModuleManager`。
3. **句柄包装**：框架对外暴露统一的 `AssetHandle<T>` / `ITweenHandle`，集成内部用一个 Entry/Handle 包装类持有底层库的真实句柄（`AsyncOperationHandle` / `YooAsset.AssetHandle` / `Tween`），并维护**引用计数**。

下面逐文件、逐成员展开。

### Addressables 集成

把 Unity 官方 **Addressables** 包适配为 `IAssetService`。整个集成位于程序集 `Frame.Addressables`，命名空间 `Frame.Addressables`。源码使用 `using UnityAddressables = UnityEngine.AddressableAssets.Addressables;` 别名，以避免与命名空间 `Frame.Addressables` 冲突。

#### `AddressablesAssetService.cs`

`public sealed class AddressablesAssetService : GameModuleBase, IAssetService`。内部用 `Dictionary<string, AddressablesAssetEntry> cache` 缓存已加载资源，`bool initialized` 标记 Addressables 系统是否已初始化。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<string, AddressablesAssetEntry> cache` | 地址 → 缓存条目映射 | key 为规范化后的地址；value 持有底层句柄、资源对象、引用计数 |
| `private bool initialized` | 标记 Addressables 是否已 `InitializeAsync` | 仅初始化一次，由 `EnsureInitialized` 守护 |
| `public override int Priority { get { return -600; } }` | 模块初始化优先级 | 与 YooAsset 后端一致为 **-600**（负值越小越早初始化，资源服务是最底层依赖之一，需要先于 UI/Config 等模块就绪） |
| `protected override void OnInitialize()` | 模块初始化回调 | 依次 `Context.Services.Register<IAssetService>(this)`（按接口注册）+ `Context.Services.Register(this)`（按具体类型注册），随后调用 `EnsureInitialized()` 立即初始化 Addressables 系统 |
| `public AssetHandle<T> Load<T>(string path) where T : Object` | 同步加载资源 | ① `NormalizeAddress` 规范化路径；空地址 → 警告并返回无效句柄；② `EnsureInitialized`；③ 命中缓存且类型匹配 → `RefCount++` 并返回；类型不匹配 → 警告并返回无效句柄；④ 未命中 → `UnityAddressables.LoadAssetAsync<T>(path)` 后 `operation.WaitForCompletion()` **阻塞**取结果；⑤ 失败（状态非 `Succeeded` 或 asset 为 null）→ 取 `OperationException.Message`、`ReleaseOperation(operation)` 释放句柄、返回无效句柄；⑥ 成功 → 写入缓存（`RefCount = 1`）并返回有效句柄 |
| `public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object` | 异步加载资源 | 先 `NormalizeAddress`、新建 `AssetRequest<T>`；命中缓存且类型匹配 → `RefCount++` 后 `CompleteRequest` 立即完成；类型不匹配 → 以错误信息立即完成；未命中 → `LoadAsyncTask(...).Forget()` 启动 UniTask 协程并立刻返回 request（供 `await` 或回调） |
| `public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)` | 加载并实例化预制体 | `Load<GameObject>(path)`；句柄无效 → 返回 null；`Object.Instantiate(handle.Asset, parent, worldPositionStays)`；实例为空 → `handle.Release()` 回滚引用计数并返回 null；否则给实例挂 `AssetInstanceLease`（无则 `AddComponent`）并 `lease.Bind(handle)`——实例销毁时自动 `Release`，实现「实例生命周期 = 资源引用生命周期」 |
| `public bool IsLoaded(string path)` | 查询资源是否已加载 | 规范化后查缓存，条目存在且 `Asset != null` 才返回 true |
| `public int GetReferenceCount(string path)` | 查询引用计数 | 命中返回 `entry.RefCount`，否则 0 |
| `public List<AssetStats> GetLoadedAssetStats()` | 导出加载统计 | 遍历缓存跳过空资源，生成 `AssetStats { Path, TypeName, ReferenceCount, IsLoaded = true }`，并按 `Path` 做 `CompareOrdinal` 排序，便于调试面板展示 |
| `public void Release(string path)` | 释放一次引用 | 规范化后查缓存；不存在直接返回；`RefCount--`；仍 `> 0` 则返回；**降到 0** 时 `ReleaseOperation(entry.Handle)`（`UnityAddressables.Release`）并从缓存移除 |
| `public void ReleaseAll()` | 释放全部资源 | 遍历缓存对每个 `Handle` 调 `ReleaseOperation`，最后 `cache.Clear()` |
| `public void UnloadUnusedAssets()` | 卸载未使用资源 | 直接 `Resources.UnloadUnusedAssets()`（Addressables 后端这里只做 Unity 引擎层卸载，不额外操作 bundle） |
| `protected override void OnShutdown()` | 模块关闭回调 | 调用 `ReleaseAll()` 释放所有句柄 |
| `private async UniTaskVoid LoadAsyncTask<T>(string path, AssetRequest<T> request, Action<AssetHandle<T>> completed) where T : Object` | 异步加载主体协程 | 空地址 → 立即以错误完成；`EnsureInitialized`；`LoadAssetAsync` 抛异常 → 记日志并以异常消息完成；循环 `while (!operation.IsDone)` 内每帧 `request.SetProgress(operation.PercentComplete)` 上报进度，并检测 `request.IsCanceled`（取消则释放句柄、以 "Request canceled." 完成），每帧 `await UniTask.Yield(PlayerLoopTiming.Update)`；完成后再次检查取消；取 `operation.Result`（仅 `Succeeded`），为空则取错误信息并释放句柄，非空则写缓存（`RefCount = 1`）；最后 `CompleteRequest` |
| `private void EnsureInitialized()` | 确保 Addressables 已初始化 | 若 `initialized` 已为 true 直接返回；否则 `UnityAddressables.InitializeAsync()` + `WaitForCompletion()` **同步等待**；失败记录警告（取 `OperationException`）；异常记录到 `FrameLog.Exception`；**无论成功失败最终都置 `initialized = true`**（避免反复重试初始化） |
| `private static void CompleteRequest<T>(AssetRequest<T> request, AssetHandle<T> handle, Action<AssetHandle<T>> completed, string error = null)` | 统一完成请求并回调 | `request.Complete(handle, error)`；若有 `completed` 回调则 try/catch 调用，异常写入 `FrameLog.Exception`（保证回调异常不破坏加载流程） |
| `private static void ReleaseOperation<T>(AsyncOperationHandle<T> operation)` | 释放泛型句柄 | 仅当 `operation.IsValid()` 时 `UnityAddressables.Release(operation)` |
| `private static void ReleaseOperation(AsyncOperationHandle operation)` | 释放非泛型句柄 | 同上，针对缓存里以非泛型 `AsyncOperationHandle` 存储的句柄 |
| `private static string NormalizeAddress(string path)` | 规范化地址 | 空白 → `string.Empty`；否则 `Replace('\\','/').Trim()`（统一斜杠、去首尾空白） |
| `private sealed class AddressablesAssetEntry` | 缓存条目 | 字段：`Object Asset`（资源对象）、`AsyncOperationHandle Handle`（**非泛型**底层句柄，用于释放）、`int RefCount`（引用计数） |

#### `AddressablesAssetModuleInstaller.cs`

`public sealed class AddressablesAssetModuleInstaller : IFrameModuleInstaller`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public void Install(ModuleManager modules, FrameSettings settings)` | 条件注册 Addressables 资源服务 | **当且仅当** `settings != null` 且 `settings.EnableAssetService == true` 且 `settings.AssetServiceBackend == AssetServiceBackend.Addressables` 时，执行 `modules.Add(new AddressablesAssetService())`；任一条件不满足直接 `return`，不注册 |

**流转逻辑**：框架启动时反射扫描所有 `IFrameModuleInstaller` 并调用其 `Install`。`AddressablesAssetModuleInstaller` 只在「启用资源服务且后端选定为 Addressables」时把 `AddressablesAssetService` 注册为模块。模块 `OnInitialize` 时把自身注册为 `IAssetService`（同时按接口与具体类型注册）并立刻调用 `EnsureInitialized()` 完成 Addressables 系统初始化。运行期 `Load/LoadAsync` 通过 `cache` 把框架 `AssetHandle<T>` 映射到底层 `AsyncOperationHandle`：同地址多次加载只递增 `RefCount` 复用句柄；`Release` 把计数减到 0 时才真正 `Addressables.Release`。同步路径用 `WaitForCompletion()` 阻塞，异步路径用 `PercentComplete` 逐帧上报进度并支持取消。

### YooAsset 集成

把国产热更资源框架 **YooAsset** 适配为 `IAssetService`。程序集 `Frame.YooAsset`，命名空间 `Frame.YooAsset`，源码用别名 `using YooAssetRuntime = YooAsset;` 引用底层库。该后端支持 YooAsset 的四种运行模式（编辑器模拟 / 单机离线 / 联机热更 / WebGL）。

#### `YooAssetAssetService.cs`

`public sealed class YooAssetAssetService : GameModuleBase, IAssetService`。字段：`Dictionary<string, YooAssetEntry> cache`、`ResourcePackage package`（资源包）、`bool ownsYooAssets`（是否由本服务初始化了 `YooAssets` 全局）、`bool ownsPackage`（是否由本服务创建了 package）、`bool packageReady`（package 是否初始化完成）。`ownsXxx` 标志确保关闭时**只销毁自己创建的对象**，避免误销毁外部已有实例。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly Dictionary<string, YooAssetEntry> cache` | 位置 → 缓存条目 | 同 Addressables，持有底层 `AssetHandle`、资源对象、引用计数 |
| `private YooAssetRuntime.ResourcePackage package` | 当前资源包 | 由 `InitializePackage` 创建或获取 |
| `private bool ownsYooAssets` | 是否本服务初始化了 YooAssets 全局 | 仅当本服务调用 `YooAssets.Initialize()` 时为 true |
| `private bool ownsPackage` | 是否本服务创建了该 package | 仅当本服务 `CreatePackage` 时为 true |
| `private bool packageReady` | package 是否就绪 | 初始化成功置 true，失败/异常置 false |
| `public override int Priority { get { return -600; } }` | 模块优先级 | 与 Addressables 后端一致 **-600** |
| `protected override void OnInitialize()` | 模块初始化 | `Context.Services.Register<IAssetService>(this)` + `Context.Services.Register(this)`；随后 try/catch 调用 `InitializePackage()`，异常时置 `packageReady = false` 并 `FrameLog.Exception`（初始化失败不会抛出，后续加载会安全返回无效句柄） |
| `public AssetHandle<T> Load<T>(string path) where T : Object` | 同步加载 | `NormalizeLocation`；空位置 → 警告 + 无效句柄；`EnsurePackageReady()` 为 false → 无效句柄；命中缓存且类型匹配 → `RefCount++` 返回，类型不匹配 → 警告 + 无效句柄；未命中 → try/catch `package.LoadAssetSync<T>(path)`；`GetLoadedAsset<T>` 取对象，失败则 `ReleaseYooHandle` 并返回无效句柄；成功写缓存（`RefCount = 1`）并返回 |
| `public AssetRequest<T> LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null) where T : Object` | 异步加载 | `NormalizeLocation` + 新建 request；命中缓存类型匹配 → `RefCount++` 后 `CompleteRequest`，类型不匹配 → 错误完成；未命中 → `LoadAsyncTask(...).Forget()` 并返回 request |
| `public GameObject Instantiate(string path, Transform parent = null, bool worldPositionStays = false)` | 加载并实例化 | 逻辑与 Addressables 完全一致：`Load<GameObject>` → `Object.Instantiate` → 挂 `AssetInstanceLease` 并 `Bind(handle)`，让实例销毁联动 `Release` |
| `public bool IsLoaded(string path)` | 是否已加载 | 规范化后查缓存，条目存在且 `Asset != null` |
| `public int GetReferenceCount(string path)` | 引用计数查询 | 命中返回 `RefCount`，否则 0 |
| `public List<AssetStats> GetLoadedAssetStats()` | 导出加载统计 | 同 Addressables：遍历缓存生成 `AssetStats` 并按 `Path` `CompareOrdinal` 排序 |
| `public void Release(string path)` | 释放一次引用 | 规范化查缓存；不存在返回；`RefCount--`；仍 `>0` 返回；降到 0 → `ReleaseYooHandle(entry.Handle)`（`yooHandle.Release()`）并移除缓存 |
| `public void ReleaseAll()` | 释放全部 | 遍历缓存 `ReleaseYooHandle`，`cache.Clear()` |
| `public void UnloadUnusedAssets()` | 卸载未使用资源 | 若 `package != null && packageReady`：`package.UnloadUnusedAssetsAsync()` + `WaitForCompletion()`（**同步等待** YooAsset bundle 卸载）；随后再 `Resources.UnloadUnusedAssets()` |
| `protected override void OnShutdown()` | 模块关闭 | ① `ReleaseAll()`；② 若 `ownsPackage && package != null && package.InitializeStatus != EOperationStatus.None`：`package.DestroyPackageAsync()` + `WaitForCompletion()`，再 try/catch `YooAssets.RemovePackage(package.PackageName)`；③ 复位 `package = null / packageReady = false / ownsPackage = false`；④ 若 `ownsYooAssets && YooAssets.IsInitialized`：`YooAssets.Destroy()`；⑤ `ownsYooAssets = false`。**只销毁自己创建的资源** |
| `private async UniTaskVoid LoadAsyncTask<T>(string path, AssetRequest<T> request, Action<AssetHandle<T>> completed) where T : Object` | 异步加载主体 | 空位置 → 错误完成；`EnsurePackageReady()` 为 false → 以 "YooAsset package is not ready." 完成；try/catch `package.LoadAssetAsync<T>(path)`，异常以消息完成；`while (!yooHandle.IsDone)` 内 `request.SetProgress(yooHandle.Progress)` 上报、检测取消（取消则 `ReleaseYooHandle` 并完成）、`await UniTask.Yield(PlayerLoopTiming.Update)`；完成后再查取消；`GetLoadedAsset<T>` 取对象，失败取 `yooHandle.Error`（空则用默认文案）并 `ReleaseYooHandle`，成功写缓存；最后 `CompleteRequest` |
| `private void InitializePackage()` | 初始化资源包 | 读 `Context.Settings`，取 `settings.YooAssetPackageName`；若 `!YooAssets.IsInitialized` → `YooAssets.Initialize()` 并置 `ownsYooAssets = true`；`TryGetPackage(packageName, out package)` 失败 → `CreatePackage(packageName)` 并置 `ownsPackage = true`；若 `package.InitializeStatus == Succeeded`（已被外部初始化）→ 直接 `packageReady = true` 返回；否则用 `CreateInitializeOptions(settings)` 构造参数 → `package.InitializePackageAsync(options)` + `WaitForCompletion()`；成功置 `packageReady = true` 并 `FrameLog.Info`（含 packageName 与 `YooAssetPlayMode`），失败置 false 并 `FrameLog.Warning`（含 `operation.Error`） |
| `private bool EnsurePackageReady()` | 检查 package 就绪 | `packageReady && package != null` → true；否则 `FrameLog.Warning("YooAsset package is not ready.")` 并返回 false |
| `private static YooAssetRuntime.InitializePackageOptions CreateInitializeOptions(FrameSettings settings)` | 按运行模式构造初始化参数 | `switch (settings.YooAssetPlayMode)`：**`EditorSimulate`** → 仅 `UNITY_EDITOR` 下用 `EditorSimulateModeOptions` + `FileSystemParameters.CreateDefaultEditorFileSystemParameters(settings.YooAssetEditorPackageRoot)`，非编辑器下警告并回退 `CreateOfflineOptions`；**`Host`** → `CreateHostOptions`；**`Web`** → `CreateWebOptions`；**default（含 `Offline`）** → `CreateOfflineOptions`。最后统一设 `options.AutoUnloadBundleWhenUnused = true` |
| `private static YooAssetRuntime.OfflinePlayModeOptions CreateOfflineOptions(FrameSettings settings)` | 离线模式参数 | `OfflinePlayModeOptions`，`BuiltinFileSystemParameters = CreateBuiltinFileSystemParameters(settings)`（只读内置资源，无远端下载） |
| `private static YooAssetRuntime.HostPlayModeOptions CreateHostOptions(FrameSettings settings)` | 联机热更模式参数 | 用 `YooAssetRemoteService(settings.YooAssetDefaultHostServer, settings.YooAssetFallbackHostServer)` 构造远端服务；`HostPlayModeOptions` 设 `BuiltinFileSystemParameters`（并 `AddParameter(CopyBuiltinPackageManifest, true)`）+ `CacheFileSystemParameters = CreateDefaultSandboxFileSystemParameters(remoteService)`；调用 `AddDownloadParameters` 注入下载并发等参数 |
| `private static YooAssetRuntime.WebPlayModeOptions CreateWebOptions(FrameSettings settings)` | WebGL 模式参数 | 同样用 `YooAssetRemoteService`；`WebPlayModeOptions` 设 `WebServerFileSystemParameters = CreateDefaultWebServerFileSystemParameters()` + `WebNetworkFileSystemParameters = CreateDefaultWebNetworkFileSystemParameters(remoteService)`，并对网络文件系统 `AddDownloadParameters` |
| `private static YooAssetRuntime.FileSystemParameters CreateBuiltinFileSystemParameters(FrameSettings settings)` | 内置文件系统参数 | 取 `settings.YooAssetBuiltinPackageRoot`；为空 → `CreateDefaultBuiltinFileSystemParameters()`，否则带根目录的重载 |
| `private static void AddDownloadParameters(YooAssetRuntime.FileSystemParameters parameters, FrameSettings settings)` | 注入下载参数 | `AddParameter` 三项：`DownloadMaxConcurrency = settings.YooAssetDownloadMaxConcurrency`、`DownloadMaxRequestPerFrame = settings.YooAssetDownloadMaxRequestPerFrame`、`DownloadWatchdogTimeout = settings.YooAssetDownloadWatchdogTimeout` |
| `private static T GetLoadedAsset<T>(string path, YooAssetRuntime.AssetHandle yooHandle, string logPrefix) where T : Object` | 从底层句柄取对象 | `yooHandle == null` 或 `Status != Succeeded` → 取 `Error` 警告（含 logPrefix/path/type）并返回 null；否则 `yooHandle.GetAssetObject<T>()`，为空再警告类型不匹配 |
| `private static void CompleteRequest<T>(...)` | 统一完成请求并回调 | 与 Addressables 同：`request.Complete(handle, error)` + try/catch 回调，异常写日志 |
| `private static void ReleaseYooHandle(YooAssetRuntime.AssetHandle yooHandle)` | 释放底层句柄 | 仅当 `yooHandle != null && yooHandle.IsValid` 时 `yooHandle.Release()` |
| `private static string NormalizeLocation(string path)` | 规范化资源位置 | 空白 → `string.Empty`；否则 `Replace('\\','/').Trim()` |
| `private sealed class YooAssetEntry` | 缓存条目 | 字段：`Object Asset`、`YooAssetRuntime.AssetHandle Handle`、`int RefCount` |
| `private sealed class YooAssetRemoteService : YooAssetRuntime.IRemoteService` | 远端 URL 解析器 | 见下表 |

`YooAssetRemoteService`（内部类，实现 YooAsset 的 `IRemoteService`，用于 Host/Web 模式拼接下载 URL）：

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly string defaultHostServer` | 主下载服务器 | 构造时经 `NormalizeHost` 规范化 |
| `private readonly string fallbackHostServer` | 备用下载服务器 | 同上 |
| `public YooAssetRemoteService(string defaultHostServer, string fallbackHostServer)` | 构造 | 两个地址各自 `NormalizeHost` 后保存 |
| `public IReadOnlyList<string> GetRemoteUrls(string fileName)` | 返回该文件的候选下载 URL 列表 | 容量为 2 的列表；`defaultHostServer` 非空 → `Add(CombineUrl(default, fileName))`；`fallbackHostServer` 非空 → 追加备用；若两者都空导致列表为空 → `Add(fileName)`（兜底，按原始文件名）；YooAsset 会按顺序尝试主→备 |
| `private static string NormalizeHost(string host)` | 规范化服务器地址 | 空白 → `string.Empty`，否则 `Trim().TrimEnd('/')`（去尾部斜杠） |
| `private static string CombineUrl(string host, string fileName)` | 拼接完整 URL | `host + "/" + fileName.TrimStart('/')`（避免双斜杠） |

#### `YooAssetModuleInstaller.cs`

`public sealed class YooAssetModuleInstaller : IFrameModuleInstaller`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public void Install(ModuleManager modules, FrameSettings settings)` | 条件注册 YooAsset 资源服务 | **当且仅当** `settings != null` 且 `settings.EnableAssetService == true` 且 `settings.AssetServiceBackend == AssetServiceBackend.YooAsset` 时 `modules.Add(new YooAssetAssetService())`；否则 `return` 不注册 |

**流转逻辑**：与 Addressables 后端互斥——`AssetServiceBackend` 是枚举（`Resources=0 / Addressables=1 / YooAsset=2`），同一时刻只会有一个资源后端被注册。Installer 通过 `settings.AssetServiceBackend == AssetServiceBackend.YooAsset` 判定接管资源服务。服务 `OnInitialize` 时注册接口并 `InitializePackage`：根据 `settings.YooAssetPlayMode` 选择 `EditorSimulate/Offline/Host/Web` 模式构造 `InitializePackageOptions`，再异步初始化 package 并 `WaitForCompletion` 同步等待就绪。`Load/LoadAsync` 把框架 `AssetHandle<T>` 映射到底层 `YooAsset.AssetHandle`，复用同位置缓存并以 `RefCount` 计数，降到 0 才 `yooHandle.Release()`。关闭时凭 `ownsPackage` / `ownsYooAssets` 标志**只销毁自己创建的 package 与全局**，对外部既有实例零侵入。

### DOTween 集成

把著名补间库 **DOTween** 适配为 `ITweenService`。程序集 `Frame.DOTween`，命名空间 `Frame.DOTween`。源码用 `DG.Tweening.DOTween` 全限定名调用底层静态 API（因为命名空间 `Frame.DOTween` 与类型名 `DOTween` 会冲突）。

#### `DOTweenTweenService.cs`

`public sealed class DOTweenTweenService : GameModuleBase, ITweenService`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public override int Priority { get { return -250; } }` | 模块优先级 | **-250**（晚于资源服务 -600，但仍属较早初始化的基础服务） |
| `public bool IsAvailable { get { return true; } }` | 补间服务是否可用 | DOTween 集成已装配即恒为 true（`ITweenService` 契约属性） |
| `protected override void OnInitialize()` | 模块初始化 | `DG.Tweening.DOTween.Init(false, true, LogBehaviour.ErrorsOnly)`（参数：`recycleAllByDefault=false`、`useSafeMode=true`、日志仅错误）；随后 `Context.Services.Register<ITweenService>(this)` + `Context.Services.Register(this)` |
| `public ITweenHandle To(Func<float> getter, Action<float> setter, float endValue, float duration, TweenOptions options = null)` | 通用浮点补间 | `getter`/`setter` 任一为 null → 返回包装 null 的 `DOTweenTweenHandle`（空安全句柄）；否则 `DG.Tweening.DOTween.To(getter.Invoke, setter.Invoke, endValue, Mathf.Max(0f, duration))`（duration 钳为非负），`ApplyOptions` 后包装返回 |
| `public ITweenHandle Move(Transform target, Vector3 endValue, float duration, bool local = false, TweenOptions options = null)` | 位移补间 | `target == null` → 空句柄；否则用 `DOTween.To` 自定义 getter/setter：`local` 为 true 读写 `localPosition`，否则读写 `position`；`tween.SetTarget(target)`（绑定目标供 `Kill(target)`）→ `ApplyOptions` → 包装返回 |
| `public ITweenHandle Scale(Transform target, Vector3 endValue, float duration, TweenOptions options = null)` | 缩放补间 | `target == null` → 空句柄；否则 `DOTween.To(() => target.localScale, v => target.localScale = v, endValue, Mathf.Max(0f,duration))` → `SetTarget(target)` → `ApplyOptions` → 包装 |
| `public ITweenHandle Fade(CanvasGroup target, float endValue, float duration, TweenOptions options = null)` | 透明度补间 | `target == null` → 空句柄；否则直接调用 `target.DOFade(Clamp01(endValue), Max(0,duration))` → `SetTarget(target)` → `ApplyOptions` → 包装 |
| `public int Kill(object target, bool complete = false)` | 杀掉指定目标的所有补间 | 直接 `DG.Tweening.DOTween.Kill(target, complete)`，返回被杀数量；`complete=true` 表示先跳到终点再杀 |
| `public void KillAll(bool complete = false)` | 杀掉所有补间 | `DG.Tweening.DOTween.KillAll(complete)` |
| `protected override void OnShutdown()` | 模块关闭 | `KillAll(false)`（关闭时不补完，直接清掉所有补间） |
| `private static void ApplyOptions(Tween tween, TweenOptions options)` | 应用补间选项 | `tween == null` 直接返回；`options ?? new TweenOptions()` 取默认；`EaseCurve` 非空时 `SetEase(AnimationCurve)`，否则映射 `TweenEase`；设置 `SetUpdate`、`SetTarget`、`OnComplete` |
| `private static Ease MapEase(TweenEase ease)` | 框架缓动枚举 → DOTween `Ease` | `switch` 映射（见下） |

`MapEase` 的精确映射（`Frame.Tweening.TweenEase` → `DG.Tweening.Ease`）：

| `TweenEase` | DOTween `Ease` |
| --- | --- |
| `Linear` | `Ease.Linear` |
| `InQuad` | `Ease.InQuad` |
| `InOutQuad` | `Ease.InOutQuad` |
| `InCubic` | `Ease.InCubic` |
| `OutCubic` | `Ease.OutCubic` |
| `InOutCubic` | `Ease.InOutCubic` |
| `InBack` | `Ease.InBack` |
| `OutBack` | `Ease.OutBack` |
| `InOutBack` | `Ease.InOutBack` |
| `OutQuad`（及任何未显式列出的值，`default` 分支） | `Ease.OutQuad` |

> 注意：`TweenEase.OutQuad` 在 `switch` 里**没有独立 case**，它落入 `default` 分支返回 `Ease.OutQuad`——结果正确，且任何未来新增/未覆盖的枚举值也会安全回退为 `OutQuad`。

#### `DOTweenTweenHandle.cs`

`public sealed class DOTweenTweenHandle : ITweenHandle`。一个对 DOTween `Tween`（`Tweener` 的基类）的轻量包装，对外暴露统一的 `ITweenHandle` 契约。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private Tween tween` | 被包装的底层补间 | 可为 null（空安全句柄）；`Kill` 后置 null |
| `public DOTweenTweenHandle(Tween tween)` | 构造 | 保存传入的 `Tween`（可能为 null） |
| `public bool IsActive { get; }` | 是否仍激活 | `tween != null && tween.IsActive()` |
| `public bool IsPlaying { get; }` | 是否正在播放 | `tween != null && tween.IsPlaying()` |
| `public void Play()` | 播放/恢复 | `tween` 非空才 `tween.Play()` |
| `public void Pause()` | 暂停 | `tween` 非空才 `tween.Pause()` |
| `public void Kill(bool complete = false)` | 杀掉补间 | `tween` 非空才 `tween.Kill(complete)` 并 `tween = null`（防止重复 Kill 与悬空引用） |
| `public ITweenHandle OnComplete(Action callback)` | 注册完成回调（链式） | `tween != null && callback != null` 才 `tween.OnComplete(() => callback())`；**始终返回 this** 以支持链式调用 |

#### `DOTweenModuleInstaller.cs`

`public sealed class DOTweenModuleInstaller : IFrameModuleInstaller`。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public void Install(ModuleManager modules, FrameSettings settings)` | 条件注册补间服务 | **当且仅当** `settings != null` 且 `settings.EnableTweenService == true` 时 `modules.Add(new DOTweenTweenService())`；否则不注册 |

**流转逻辑**：与资源后端不同，补间集成只看一个开关 `settings.EnableTweenService`（没有「后端枚举」之分）。Installer 在该开关开启时注册 `DOTweenTweenService`。服务 `OnInitialize` 先 `DOTween.Init` 初始化底层库再注册 `ITweenService`。业务代码通过 `ITweenService` 的 `Move/Scale/Fade/To` 创建补间，框架统一返回 `ITweenHandle`，内部实为 `DOTweenTweenHandle` 包装的 DOTween `Tween`；`TweenOptions`（`Ease`/`EaseCurve`/`IgnoreTimeScale`/`Target`/`Completed`）经 `ApplyOptions` 一次性翻译为 DOTween 的 `SetEase`/`SetUpdate`/`SetTarget`/`OnComplete`。关闭时 `KillAll(false)` 清理全部补间。

> 编辑器校验提示：若启用了补间服务但缺少 `Assets/ThirdParty/DOTween/DOTween.dll`，`Frame/Validate Project` 会发出警告（见下文 Editor 部分）。

---

## 24. Editor 编辑器工具

文件：`Assets/Frame/Editor/FrameMenuItems.cs`，`public static class FrameMenuItems`（命名空间 `Frame.Editor`）。提供 `Frame` 顶部菜单下的工具，以及供 CI 调用的工程校验入口。常量 `SettingsAssetPath = "Assets/Frame/Resources/Frame/FrameSettings.asset"` 是 `FrameSettings` 资产的约定路径。

### 菜单项方法

| 方法 / `MenuItem` | 作用 | 实现/注意点 |
| --- | --- | --- |
| `[MenuItem("Frame/Create Default Frame Settings")]` `public static void CreateDefaultSettings()` | 创建/定位默认 FrameSettings 资产 | 依次 `EnsureFolder` 确保 `Assets/Frame`、`Assets/Frame/Resources`、`Assets/Frame/Resources/Frame` 三级目录存在；`LoadAssetAtPath<FrameSettings>(SettingsAssetPath)`，不存在则 `ScriptableObject.CreateInstance<FrameSettings>()` + `CreateAsset` + `SaveAssets`；最后 `Selection.activeObject = settings` 并 `EditorGUIUtility.PingObject` 在 Project 窗口高亮 |
| `[MenuItem("Frame/Create GameEntry In Scene")]` `public static void CreateGameEntryInScene()` | 在当前场景创建 GameEntry | 先 `FindAnyObjectByType<GameEntry>(FindObjectsInactive.Include)`，若已存在 → 选中并 ping，**不重复创建**；否则 `new GameObject("Frame", typeof(GameEntry))`，`Undo.RegisterCreatedObjectUndo`（可撤销），选中新对象，`EditorSceneManager.MarkSceneDirty` 标记场景已修改 |
| `[MenuItem("Frame/Open README")]` `public static void OpenReadme()` | 打开框架 README | `LoadAssetAtPath<Object>("Assets/Frame/README.md")`，非空则 `AssetDatabase.OpenAsset`（用系统/默认编辑器打开） |
| `[MenuItem("Frame/Validate Project")]` `public static void ValidateProject()` | 菜单触发工程校验 | `RunProjectValidation()` 跑全部检查 → `LogValidationSummary` 输出汇总到 Console |

### 校验入口与公共方法

| 方法 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public static ValidationReport RunProjectValidation(bool logDetails = true)` | 执行全部校验，返回报告 | 新建 `ValidationReport(logDetails)`，依序调用六个校验：`ValidateSettings` → `ValidateGameEntry` → `ValidateBuildScenes` → `ValidateRuntimeDependencies` → `ValidateIntegrations` → `ValidateResources`，返回报告 |
| `public static void ValidateProjectForCI()` | CI 校验入口 | 跑 `RunProjectValidation()` + `LogValidationSummary`；**若 `Application.isBatchMode`（批处理/无头 CI）→ `EditorApplication.Exit(report.ExitCode)`**（通过/失败分别退出码 0/1）并返回；否则（编辑器内手动调用）若 `!report.Passed` 则 `throw new InvalidOperationException(...)`（含 Errors/Warnings 数） |
| `private static void LogValidationSummary(ValidationReport report)` | 输出汇总日志 | `Errors > 0` → `Debug.LogError("[Frame] Project validation failed. ...")`；否则 `Warnings > 0` → `Debug.LogWarning(...)`；否则 `Debug.Log("[Frame] Project validation passed.")` |

### 各校验项的真实检查内容

**`ValidateSettings(report)`**（FrameSettings 资产自检）：
- `LoadAssetAtPath<FrameSettings>(SettingsAssetPath)`，**为 null → Warning**（"FrameSettings asset not found. Use Frame/Create Default Frame Settings." 然后 return，跳过后续设置检查）。
- `settings.UIReferenceResolution.x <= 0 || .y <= 0` → **Error**（UI 参考分辨率非法）。
- `settings.AudioSourcePoolSize <= 0` → **Error**（音频源池大小非法）。
- `settings.DefaultGameObjectPoolMaxSize <= 0` → **Error**（默认 GameObject 池上限非法）。

**`ValidateGameEntry(report)`**（场景内 GameEntry 数量）：
- 用 `FindObjectsByType<GameEntry>(FindObjectsInactive.Include)`（`UNITY_2023_1_OR_NEWER`）或旧版 `FindObjectsOfType<GameEntry>(true)` 枚举（含未激活）。
- 数量 `== 0` → **Info**（"No GameEntry found... Auto bootstrap can create one before scene load."，仅信息，不算错误）。
- 数量 `> 1` → **Error**（"Multiple GameEntry instances found in current scene: N"）。

**`ValidateBuildScenes(report)`**（Build Settings 场景）：
- `EditorBuildSettings.scenes` 为 null 或长度 0 → **Warning**（"Build Settings has no scenes."，return）。
- 遍历场景：对每个 **enabled** 的场景，标记 `hasEnabledScene = true`，并 `File.Exists(path)` 检查文件存在，缺失 → **Error**（"Enabled build scene is missing: path"）。
- 若有场景但**没有任何 enabled** → **Warning**（"Build Settings has scenes, but none are enabled."）。

**`ValidateRuntimeDependencies(report)`**（包依赖 + asmdef 引用）：
- `ValidatePackage` 逐项检查五个包是否存在，缺失为 **Error**：
  - `com.unity.nuget.newtonsoft-json`（Newtonsoft JSON serializer）
  - `com.unity.inputsystem`（Input service asmdef reference）
  - `com.cysharp.unitask`（async services）
  - `com.unity.addressables`（Addressables asset service integration）
  - `com.tuyoogame.yooasset`（YooAsset asset service integration）
- 读取 `Assets/Frame/Frame.Runtime.asmdef`：文件为空/缺失 → **Error**（"Frame.Runtime.asmdef is missing." 并 return）。
- 否则用 `ValidateAsmdefReference` 检查 asmdef 文本是否含三个引用字符串，缺失为 **Error**：`"UnityEngine.UI"`、`"Unity.InputSystem"`、`"UniTask"`。

**`ValidateIntegrations(report)`**（集成产物存在性，均为 **Warning**）：
- `Assets/ThirdParty/DOTween/DOTween.dll` 缺失 → Warning（"DOTween.dll not found. Disable TweenService or import DOTween before using tween integration."）。
- `Assets/Frame/Integrations/DOTween/Frame.DOTween.asmdef` 缺失 → Warning（"Frame DOTween integration asmdef not found."）。
- `Assets/Frame/Integrations/Addressables/Frame.Addressables.asmdef` 缺失 → Warning（"...AssetServiceBackend.Addressables will not be available."）。
- `Assets/Frame/Integrations/YooAsset/Frame.YooAsset.asmdef` 缺失 → Warning（"...AssetServiceBackend.YooAsset will not be available."）。

**`ValidateResources(report)`**（Resources 路径冲突 + 资产格式）：
- `Directory.Exists("Assets")` 为 false → **Error**（"Assets folder is missing." 并 return）。
- `Directory.GetFiles("Assets", "*", AllDirectories)` 遍历所有文件（路径统一为 `/`）。
- `ShouldSkipResourceValidation` 跳过 `.meta`、`.cs`、`.asmdef` 与空路径。
- `TryGetResourcesKey` 仅处理路径中含 `/Resources/` 的文件，取 `/Resources/` 之后、**去扩展名**的相对路径作为 `resourcesKey`（即 `Resources.Load` 的逻辑键）。
- 用 `Dictionary<string,string>(OrdinalIgnoreCase)` 记录已见键：**同一逻辑键出现两次 → Warning**（"Duplicate Resources path 'key': existing and assetPath"，提示 `Resources.Load` 路径冲突会随机命中其一）。
- 随后对每个资源调用 `ValidateResourceAsset`。

**`ValidateResourceAsset(report, assetPath, resourcesKey)`**（按子目录的专项校验）：
- 若 `resourcesKey` 以 **`UI/`** 开头且扩展名为 **`.prefab`**：`LoadAssetAtPath<GameObject>`，加载失败 → **Error**（"UI prefab could not be loaded: ..."）；加载成功但 `GetComponentInChildren<UIPanelBase>(true)` 为 null（自身或子节点无面板组件）→ **Warning**（"Resources UI prefab has no UIPanelBase component: ..."）。
- 若 `resourcesKey` 以 **`Configs/`** 开头且扩展名为 **`.json`**：`JToken.Parse(File.ReadAllText(...))` 尝试解析；异常 → **Error**（"Invalid JSON config: ... error=..."，即配置 JSON 格式非法）。

### 校验辅助方法

| 方法 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private static void ValidatePackage(ValidationReport report, string packageName, string purpose)` | 校验单个包存在 | `PackageExists` 为 false → `report.Error("Required package missing for {purpose}: {packageName}")` |
| `private static bool PackageExists(string packageName)` | 判断包是否安装 | 三种命中方式任一即 true：① `Packages/manifest.json` 文本含 `"packageName"`；② 存在本地目录 `Packages/{packageName}`（嵌入包）；③ `Library/PackageCache` 下存在 `{packageName}@*` 目录 |
| `private static void ValidateAsmdefReference(ValidationReport report, string asmdef, string reference)` | 校验 asmdef 含某引用 | asmdef 文本不含 `"reference"` → `report.Error(... missing reference: ...)` |
| `private static string ReadTextAsset(string path)` | 读文本文件 | `File.Exists` 则 `ReadAllText`，否则 `string.Empty` |
| `private static bool TryGetResourcesKey(string assetPath, out string key)` | 提取 Resources 逻辑键 | 找 `"/Resources/"` 标记（忽略大小写），不含 → false；含则截取其后子串并去掉扩展名长度，空白键返回 false |
| `private static bool ShouldSkipResourceValidation(string assetPath)` | 是否跳过该文件 | 空白、或扩展名为 `.meta`/`.cs`/`.asmdef` → true |
| `private static void EnsureFolder(string path)` | 递归创建目录 | `AssetDatabase.IsValidFolder` 已存在则返回；否则取父目录递归 `EnsureFolder` 后 `AssetDatabase.CreateFolder(parent, name)` |

### 报告类型

`public sealed class ValidationReport`：聚合校验结果。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `private readonly bool logDetails` | 是否实时打印每条消息 | 构造参数（默认 true） |
| `private readonly List<ValidationMessage> messages` | 全部消息 | 通过 `Messages` 只读暴露 |
| `public ValidationReport(bool logDetails = true)` | 构造 | 保存 `logDetails` |
| `public IReadOnlyList<ValidationMessage> Messages { get; }` | 消息列表 | 只读 |
| `public int Errors { get; private set; }` | 错误计数 | 每次 `Error` 自增 |
| `public int Warnings { get; private set; }` | 警告计数 | 每次 `Warning` 自增 |
| `public bool Passed { get { return Errors == 0; } }` | 是否通过 | **仅看 Errors**，Warnings 不影响通过 |
| `public int ExitCode { get { return Passed ? 0 : 1; } }` | CI 退出码 | 通过 0 / 失败 1 |
| `public void Error(string message)` | 记一条错误 | `Errors++` + `Add(LogType.Error, ...)` |
| `public void Warning(string message)` | 记一条警告 | `Warnings++` + `Add(LogType.Warning, ...)` |
| `public void Info(string message)` | 记一条信息 | `Add(LogType.Log, ...)`（不计入错误/警告） |
| `private void Add(LogType type, string message)` | 入列并按需打印 | 加入 `messages`；若 `logDetails` 则按 `Error/Warning/Log` 分别 `Debug.LogError/LogWarning/Log`（均带 `[Frame] ` 前缀） |

`public sealed class ValidationMessage`：单条消息。

| 成员 | 作用 | 实现/注意点 |
| --- | --- | --- |
| `public ValidationMessage(LogType type, string message)` | 构造 | 保存类型与文本 |
| `public LogType Type { get; private set; }` | 消息级别 | `LogType.Error/Warning/Log` |
| `public string Message { get; private set; }` | 消息文本 | 原始文案（不含 `[Frame]` 前缀） |

**CI 退出码行为小结**：在批处理模式（`Application.isBatchMode`，典型 CI 无头运行 Unity）下，`ValidateProjectForCI` 用 `EditorApplication.Exit(report.ExitCode)` 终止进程——有任何 **Error** 退出码为 1（构建/流水线判失败），无 Error（即使有 Warning）退出码为 0。非批处理模式（开发者在编辑器里手动调用）则改为在失败时抛 `InvalidOperationException`。CI 脚本通常用 `-batchmode -executeMethod Frame.Editor.FrameMenuItems.ValidateProjectForCI` 调用。

---

## 25. Samples 示例

文件：`Assets/Frame/Samples/Scripts/FrameDemoController.cs`，`public sealed class FrameDemoController : MonoBehaviour`（命名空间 `Frame.Samples`）。这是一个**最小可运行学习示例**，演示如何在普通 `MonoBehaviour` 里通过 `Framework` 门面解析并使用框架的三个核心服务：**事件总线（EventBus）**、**定时器（TimerService）**、**存档（SaveService）**，并展示正确的订阅/资源释放生命周期。

字段：
- `private IDisposable eventSubscription`：事件订阅句柄，用于在销毁时退订。
- `private TimerHandle timer`：定时器句柄，用于在销毁时取消。

逐方法走读：

| 方法 | 作用 | 学习要点 |
| --- | --- | --- |
| `private void Start()` | 演示主流程 | ① 先 `if (!Framework.IsInitialized) return;`——**防御性检查**，框架未就绪就不做任何事；② `Framework.Resolve<IEventBus>()` 解析事件总线，`eventBus.Subscribe<DemoEvent>(OnDemoEvent, this)` 订阅并把返回的 `IDisposable` 存入 `eventSubscription`（注意第二参 `this` 作为订阅者归属，便于框架按宿主管理生命周期）；③ `eventBus.Publish(new DemoEvent { Message = "Frame demo event published." })` 立即发布一条事件（会同步回调到刚注册的 `OnDemoEvent`）；④ `Framework.Resolve<TimerService>()` 解析定时器服务，`timers.Delay(1f, SaveDemoData, true, this)` 注册一个 1 秒后触发的延时回调（参数 `true` 与 `this` 分别表示「受 TimeScale 影响/绑定宿主」一类语义，宿主销毁随之失效），返回的 `TimerHandle` 存入 `timer` |
| `private void OnDestroy()` | 清理订阅与定时器 | `eventSubscription != null` → `Dispose()` 退订并置 null（**演示必须显式退订事件，避免悬挂回调**）；`timer.Cancel()` 取消定时器（`TimerHandle` 为结构体句柄，`Cancel` 幂等安全） |
| `private void OnDemoEvent(DemoEvent demoEvent)` | 事件处理器 | 简单 `Debug.Log(demoEvent.Message)`，演示订阅回调签名 |
| `private void SaveDemoData()` | 定时器回调：写存档 | 用 **`Framework.TryResolve(out saveService)`**（`TryResolve` 而非 `Resolve`，演示「服务可能未启用时的安全解析」——解析失败直接 return 不抛异常）；成功则 `saveService.Save("demo", new DemoSaveData { PlayerName = "Player", Level = 1, SavedAt = DateTime.UtcNow.ToString("O") })`，以键 `"demo"` 存一份可序列化数据（ISO-8601 时间戳） |

内部类型：

| 类型 | 作用 | 说明 |
| --- | --- | --- |
| `private struct DemoEvent { public string Message; }` | 示例事件 | 值类型事件，符合 EventBus 推荐的轻量事件模式 |
| `[Serializable] private sealed class DemoSaveData { public string PlayerName; public int Level; public string SavedAt; }` | 示例存档数据 | `[Serializable]` + 公共字段，可被 SaveService 序列化 |

**作为学习参考的关键启示**：
1. 永远先判 `Framework.IsInitialized` 再用框架。
2. 用 `Framework.Resolve<T>()` 拿「必有」的服务，用 `Framework.TryResolve(out ...)` 拿「可能未启用」的服务（存档/补间等可在 `FrameSettings` 里关闭）。
3. 订阅时把 `IDisposable` 存好，`OnDestroy` 里 `Dispose`；定时器把 `TimerHandle` 存好，`OnDestroy` 里 `Cancel`——这是避免回调泄漏与「对象已销毁仍被回调」的标准范式。

---

## 26. 模块间端到端协作链路

前面各章按「模块」纵向拆解。本章反过来，按**业务场景**横向把多个模块串成完整链路，帮助理解框架在真实流程中的协作方式。

### 链路 1：框架启动到第一个面板

```
Unity BeforeSceneLoad
  → Framework.AutoBootstrap() → GameEntry.Ensure(settings)
  → GameEntry.Awake → Framework.Initialize()
      → RegisterDefaultModules（按开关 new 各 Service）
      → RegisterInstalledModules（反射安装 DOTweenTweenService 等）
      → ModuleManager.InitializeAll（按 Priority 升序）
          -1000 DiagnosticsService.OnInitialize  → 订阅 FrameLog.EntryWritten
          -950  LifecycleService.OnInitialize     → 读 Application.isFocused
          -900  EventBus.OnInitialize
          ...
          -600  ResourcesAssetService.OnInitialize → 注册 IAssetService
          -500  SceneService.OnInitialize
          -400  UIService.OnInitialize            → TryResolve<IAssetService>() + CreateRoot()
          -300  AudioService.OnInitialize          → 建 "Audio" 根 + 预热音源池
          ...
  → GameEntry.Start → Framework.Start → modules.StartAll
  → 业务脚本（在某个 MonoBehaviour.Start 里）:
        IUIService ui = Framework.Resolve<IUIService>();
        ui.Open<MainMenuPanel>("UI/MainMenu", UILayer.Normal);
            → UIService.OpenInternal
                → assets.Instantiate("UI/MainMenu")   // 走 IAssetService
                    → ResourcesAssetService.Load<GameObject> → AddRef → AssetHandle
                    → Instantiate + 绑定 AssetInstanceLease
                → GetComponent<MainMenuPanel>
                → InternalCreate(OnCreate) / InternalOpen(OnOpen)
                → push stack / BringToTop
```

**关键协作点**：UIService 依赖 IAssetService 加载 prefab。如果在 `FrameSettings` 里关闭了 AssetService（`EnableAssetService=false`），UIService 的 `TryResolve<IAssetService>()` 会拿到 null，后续 `Open` 会失败。这就是「同优先级无依赖、跨优先级需保证顺序」原则的体现：UIService（-400）晚于 AssetService（-600），所以初始化时一定能解析到资源服务。

### 链路 2：资源加载与释放的引用计数闭环

```
assets.Load<Sprite>("Icons/Sword")
  → NormalizeResourcesPath("Icons/Sword")        // 去扩展名/反斜杠/Resources 前缀
  → cache miss → Resources.Load<Sprite> → cache["Icons/Sword"]=sprite
  → AddRef → refCounts["Icons/Sword"]=1
  → return AssetHandle<Sprite>(owner=service, path, asset)

handle.Release()  (或 using 块结束触发 Dispose)
  → service.Release("Icons/Sword")
  → refCounts["Icons/Sword"]-- → 0
  → 移除 refCounts 与 cache 条目（注意：移除缓存 ≠ 立即卸载，真正卸载交给 Unity）
```

对于 prefab 实例：`Instantiate` 在实例上挂 `AssetInstanceLease`，lease 持有加载句柄；实例 `OnDestroy` 时自动 `Dispose` 句柄，从而把「实例生命周期」与「资源引用计数」绑定。这对 Addressables/YooAsset 尤其重要——避免实例还活着、底层 Bundle 依赖却被提前 Release。

诊断时可用 `assets.GetReferenceCount(path)` 与 `assets.GetLoadedAssetStats()`（被 RuntimeDiagnosticsOverlay 的 Assets 面板读取）定位「只 Load 不 Release」造成的引用泄漏。

### 链路 3：场景切换 + Loading UI + 资源清理

```
1. ui.Open<LoadingPanel>(..., UILayer.Loading)   // AllowBack=false，返回键不关
2. scenes.LoadAsync(new SceneLoadArgs {
       SceneName="Battle", ActivateOnLoad=false,   // 停在 0.9 等手动激活
       SetActiveOnComplete=true,
       Progress = p => loadingBar.value = p })       // p 已按 0..0.9 归一化为 0..1
   → SceneService 发布 LoadStarted → 业务/Overlay 可感知
   → TrackLoadAsync 每帧 NotifyProgress
3. 等 operation.IsReadyToActivate（progress>=0.9 && !done）
4. （可选）assets.ReleaseAll() / UnloadUnusedAssets() 清理旧场景资源
5. operation.Activate()  → 场景激活 → SetActiveScene("Battle")
   → TrackLoadAsync 发布 LoadCompleted → ui.Close(LoadingPanel)
```

`SceneService` 默认 `AllowConcurrentLoads=false`，重复发起加载会抛 `FrameException`，避免两个 Loading 流程互相覆盖。

### 链路 4：存档的原子写入与生命周期联动

```
lifecycle.PauseChanged += paused => { if (paused) save.Save("autosave", data); }
  // 移动端切后台 → GameEntry.OnApplicationPause → Framework → ModuleManager.PauseAll
  //   → LifecycleService 仅在状态真正变化时触发 PauseChanged
  //   → TimerService.OnApplicationPause 同时把所有定时器暂停

save.Save("autosave", data, dataVersion: 2)
  → serializer.Serialize(data)            // 默认 NewtonsoftSaveSerializer → .json
  → encryptor?.Encrypt(payload)           // 可选 AES：Header + IV + 密文
  → 写 autosave.json.tmp
  → 若 autosave.json 已存在 → 移动到 autosave.json.bak
  → 移动 .tmp → autosave.json             // 原子替换
  → 写 autosave.json.meta（格式版本/数据版本/序列化器扩展名/是否加密/大小/SHA-256）

save.TryLoad<T>("autosave", out data)
  → 读 .meta 校验 SHA-256 与版本 → 解密 → 反序列化
  → dataVersion < 当前 → 沿 SaveMigration 链迁移
  → 任一步失败 → 回退读 .bak
```

### 链路 5：输入上下文栈 + UI 弹窗

```csharp
public sealed class ShopPanel : UIPanelBase
{
    private IDisposable inputScope;

    protected override void OnOpen(object args)
    {
        // 打开商店时把输入切到 UI 上下文，屏蔽 Gameplay 操作
        inputScope = Framework.Resolve<IInputService>().PushContext(InputContext.UI);
    }

    protected override void OnClose()
    {
        // 关闭时 Dispose，自动恢复到之前的输入上下文（栈式恢复）
        inputScope?.Dispose();
        inputScope = null;
    }
}
```

`PushContext` 返回 `IDisposable`，把当前上下文压栈并切到新上下文；`Dispose` 时弹栈恢复。这让「过场/对话/Loading/弹窗」等临时输入屏蔽不需要业务手动记忆上一个上下文。

---

## 27. 扩展指南

框架的扩展点都建立在 Core 的几个约定上：**模块继承 `GameModuleBase`、在 `OnInitialize` 注册接口、通过 `IFrameModuleInstaller` 自动安装、业务依赖接口**。

### 扩展 1：自定义业务模块

```csharp
using Frame.Core;

public interface IQuestService
{
    void Accept(int questId);
}

public sealed class QuestService : GameModuleBase, IQuestService
{
    // Priority 要大于它依赖的服务。比如依赖 EventBus(-900)/SaveService(-100)，设 10 即可。
    public override int Priority => 10;

    protected override void OnInitialize()
    {
        // 在这里解析依赖（此时低 Priority 模块已初始化完成）
        var events = Context.Services.Resolve<IEventBus>();
        // 注册自己：接口 + 具体类型
        Context.Services.Register<IQuestService>(this);
        Context.Services.Register(this);
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        // 每帧逻辑，无需自己挂 MonoBehaviour
    }

    protected override void OnShutdown()
    {
        // 反订阅、释放资源
    }

    public void Accept(int questId) { /* ... */ }
}
```

把它接入框架有两种方式：

**方式 A——业务自行注册**（不改框架）：写一个 installer 放在你自己的程序集里：

```csharp
public sealed class GameModuleInstaller : IFrameModuleInstaller
{
    public void Install(ModuleManager modules, FrameSettings settings)
    {
        modules.Add(new QuestService());
    }
}
```

`Framework.RegisterInstalledModules` 会在启动时反射扫描所有程序集中实现 `IFrameModuleInstaller` 的类型并调用 `Install`。**installer 必须有无参构造函数，且不应在构造里依赖场景对象。**

**方式 B——直接改 `Framework.RegisterDefaultModules`**：不推荐，会污染框架层。

### 扩展 2：自定义资源后端

框架已内置 Resources、Addressables、YooAsset 三种后端，统一实现 `IAssetService`。若要接入第四种（例如自研 Bundle 系统）：

1. 在独立程序集实现 `IAssetService`（参考 `AddressablesAssetService`）。该程序集需引用 `Frame.Runtime`，并在 `Frame.Runtime/Core/AssemblyInfo.cs` 追加 `[assembly: InternalsVisibleTo("你的程序集名")]`——因为 `AssetHandle<T>` 的构造函数是 internal。
2. 实现一个 `IFrameModuleInstaller`，仅当 `settings.AssetServiceBackend == 你的枚举值` 时 `modules.Add(new YourAssetService())`。
3. 在 `AssetServiceBackend` 枚举里增加你的后端值，并在 `FrameSettings` Inspector 中选择它。

注意 Resources 后端是在 `Framework.RegisterDefaultModules` 内直接注册的（只在 `AssetServiceBackend.Resources` 时），其它后端走 installer——这样不安装对应第三方包的项目不会编译失败。

### 扩展 3：自定义存档序列化器 / 加密器

```csharp
// 序列化器：实现 ISaveSerializer（FileExtension + Serialize/Deserialize）
public sealed class MessagePackSaveSerializer : ISaveSerializer
{
    public string FileExtension => ".mp";
    public byte[] Serialize<T>(T data) { /* ... */ }
    public T Deserialize<T>(byte[] bytes) { /* ... */ }
}

ISaveService save = Framework.Resolve<ISaveService>();
save.SetSerializer(new MessagePackSaveSerializer());     // 切换后存档扩展名变为 .mp
save.SetEncryptor(new AesSaveEncryptor("project-key"));  // 叠加 AES 加密
```

切换序列化器后，**新存档的扩展名由当前序列化器的 `FileExtension` 决定**；读取旧存档时要保证序列化器与写入时一致（metadata 里记录了 `SerializerExtension` 可用于判断）。

### 扩展 4：自定义网络协议解析器

后端协议若不是裸 JSON、也不是 `{ code, message, data, success }` envelope，可实现 `IHttpResponseParser`：

```csharp
public sealed class MyProtocolParser : IHttpResponseParser
{
    public bool TryParse<T>(string json, out T value, out string errorCode, out string message)
    {
        // 按你的协议解析，返回是否业务成功 + 业务错误码 + 提示
    }
}

IHttpService http = Framework.Resolve<IHttpService>();
http.ResponseParser = new MyProtocolParser();
// 之后所有 GetJson<T>/PostJson<Req,Resp>/SendJson<T> 都走你的协议
```

> 具体接口签名以 `IHttpResponseParser.cs` 源码为准（见 Networking 章节）。`EnvelopeHttpResponseParser` 是现成参考实现，字段名 `CodeField/MessageField/DataField/SuccessField` 可配置。

TCP 长连接如果服务端协议不是默认 4 字节大端长度前缀，可实现 `ISocketMessageCodec` 并赋值给 `SocketClientOptions.Codec`。业务 RPC、可靠消息、ack、断线补偿和消息去重建议放在项目网络门面层，而不是直接塞进通用 `SocketService`。

### 扩展 5：自定义 UI 过渡动画

实现 `IUITransition` 即可替换默认的 `UIFadeTransition`：

```csharp
public sealed class UIScaleTransition : IUITransition
{
    public IEnumerator PlayOpen(UIPanelBase panel)  { /* 协程：从小到大 */ }
    public IEnumerator PlayClose(UIPanelBase panel) { /* 协程：从大到小 */ }
}

ui.RegisterRoute<ShopPanel>("shop", "UI/Shop", UILayer.Popup,
    transition: new UIScaleTransition());
```

过渡通过 `UIRoot.StartCoroutine` 驱动：打开动画不阻塞 `Open` 返回；关闭动画会在面板真正隐藏/销毁前 `yield` 等待 `PlayClose` 完成。

### 扩展 6：自定义配置 Provider（远程/灰度）

```csharp
RuntimeJsonConfigProvider remote = new RuntimeJsonConfigProvider();
IConfigService configs = Framework.Resolve<IConfigService>();
configs.RegisterProvider(remote);          // 插入到 provider 链最前面，优先级最高
remote.SetJson("Items/sword_001", remoteJson);  // 远程覆盖

// 若 provider 实现 IConfigChangeNotifier，变更时 ConfigService 自动清缓存
ItemConfig overridden = configs.Load<ItemConfig>("Items/sword_001");
```

`RegisterProvider` 把新 provider **插到链头**，因此它的配置会覆盖默认 `AssetScriptableConfigProvider` / `AssetJsonConfigProvider`。配置对象实现 `IConfigValidator` 时，加载后会自动校验，失败返回 null/false 并写框架 warning。

---

## 28. 生产化检查表（速查）

> 这里只做上线前快速核对。

- **启动**：明确自动启动 vs 场景入口，避免多个 `GameEntry`。
- **模块开关**：`FrameSettings` 里关掉用不到的模块；对可选依赖统一用 `TryResolve`。
- **日志**：Release 包把 `MinimumLogLevel` 调到 `Warning`/`Error`；按需开 `FileLogSink` 并限制大小。
- **资源**：中大型项目从 `Resources` 切到 Addressables/YooAsset，并补打包/远程发布/版本/回滚流程；`IAssetService` 不负责完整热更流程。
- **UI**：制定 prefab 路径规范、层级、弹窗栈、遮罩、动画与销毁策略；面板里订阅的事件/计时器在 `OnClose`/`OnDispose` 清理。
- **存档**：确认版本迁移、metadata 校验、AES 密钥管理、云同步策略；大存档用异步接口。
- **网络**：确认统一 `ResponseParser`、鉴权刷新、错误码动作映射、重试与埋点；长连接项目还要确认 Socket 心跳、重连、消息 ack、断线补偿和 WebGL transport 策略。
- **配置**：构建前校验引用与重复 id；远程配置补版本/灰度/签名/回滚。
- **本地化**：导出缺失 key 清单（`MissingKeys`）；确认地区 fallback、复数、性别规则。
- **输入**：确认目标平台输入设备、ActionMap、重绑定保存、UI 焦点。
- **诊断**：保留最近日志、FPS、内存、关键错误计数，方便真机排查；正式包通过隐藏入口或编译开关控制 Overlay 可见性。
- **测试 / CI**：核心模块（EventBus/TimerService/SaveService/ConfigService/ObjectPool）补 EditMode/PlayMode 测试；CI 接入 `Frame.Editor.FrameMenuItems.ValidateProjectForCI`（校验错误返回非 0 退出码）。

---

## 29. 附录：模块、接口、Priority 速查表

| 模块 | 服务接口 | 实现类 | Priority | 命名空间 |
| --- | --- | --- | ---: | --- |
| Diagnostics | `IDiagnosticsService` | `DiagnosticsService` | -1000 | `Frame.Diagnostics` |
| Lifecycle | `ILifecycleService` | `LifecycleService` | -950 | `Frame.Lifecycle` |
| Events | `IEventBus` | `EventBus` | -900 | `Frame.Events` |
| Preferences | `IPreferencesService` | `PreferencesService` | -850 | `Frame.Preferences` |
| Time | `ITimerService` | `TimerService` | -800 | `Frame.Timing` |
| Pooling | `IPoolService` | `PoolService` | -700 | `Frame.Pooling` |
| Assets | `IAssetService` | `ResourcesAssetService` / `AddressablesAssetService` / `YooAssetAssetService` | -600 | `Frame.Assets` |
| Scenes | `ISceneService` | `SceneService` | -500 | `Frame.Scenes` |
| UI | `IUIService` | `UIService` | -400 | `Frame.UI` |
| Audio | `IAudioService` | `AudioService` | -300 | `Frame.Audio` |
| Tweening | `ITweenService` | `DOTweenTweenService`（集成层） | -250 | `Frame.Tweening` |
| Config | `IConfigService` | `ConfigService` | -200 | `Frame.Config` |
| Save | `ISaveService` | `SaveService` | -100 | `Frame.Save` |
| Input | `IInputService` | `InputService` | 0 | `Frame.Input` |
| Networking | `ISocketService` / `IHttpService` | `SocketService` / `HttpService` | -90 / 0 | `Frame.Networking` |
| Localization | `ILocalizationService` | `LocalizationService` | 0 | `Frame.Localization` |

> Tweening 的实现 `DOTweenTweenService` 由 `DOTweenModuleInstaller` 在 `FrameSettings.EnableTweenService` 为 true 时安装，不在 `Framework.RegisterDefaultModules` 的默认注册表中；Addressables/YooAsset 后端同理由各自 installer 在对应 `AssetServiceBackend` 下安装。

---

*本文档由源码逐文件解析生成，所有 Priority、默认值、常量、错误信息字符串均取自 `Assets/Frame` 实际源码。如源码后续变更，请以源码为准。*

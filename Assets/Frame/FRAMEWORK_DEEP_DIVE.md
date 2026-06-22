# Frame Framework Deep Dive

本文档用于解释 `Assets/Frame` 框架的整体设计、每个模块的使用方式、每个公开类型的职责，以及关键实现方式。它比 `README.md` 更偏向源码阅读和二次开发参考。

## 阅读方式

建议按下面顺序理解：

1. 先看 Core 模块，理解启动、模块生命周期和服务注册。
2. 再看业务常用模块：Assets、UI、Scenes、Audio、Save、Config、Networking、Input。
3. 最后看辅助模块：Diagnostics、Lifecycle、Events、Time、Preferences、Pooling、Localization、StateMachine、Tweening、Utilities、Editor。

业务代码建议放在 `Assets/Game` 或 `Assets/Scripts/Game`，通过 `Framework.Resolve<TService>()` 获取框架服务，不建议把业务逻辑写进 `Assets/Frame`。

## 总体架构

框架采用“模块化服务注册”设计：

- `GameEntry` 是 Unity 场景中的入口组件，负责把 Unity 生命周期转发给框架。
- `Framework` 是静态门面，负责自动启动、初始化、关闭、服务解析和默认模块注册。
- `ModuleManager` 管理所有 `IFrameModule`，按 `Priority` 从小到大初始化，关闭时倒序执行。
- 每个服务模块继承 `GameModuleBase`，在 `OnInitialize()` 中把自己注册到 `ServiceRegistry`。
- 业务层只依赖接口，例如 `IUIService`、`ISaveService`、`IHttpService`、`ISocketService`，降低替换实现的成本。

初始化顺序由模块优先级决定：

| 模块 | 实现类型 | Priority | 说明 |
| --- | --- | ---: | --- |
| Diagnostics | `DiagnosticsService` | -1000 | 最早接入日志和性能采样 |
| Lifecycle | `LifecycleService` | -950 | 记录暂停、焦点、退出状态 |
| Events | `EventBus` | -900 | 类型安全事件总线 |
| Preferences | `PreferencesService` | -850 | PlayerPrefs 偏好读写 |
| Time | `TimerService` | -800 | Update 驱动定时器 |
| Pooling | `PoolService` | -700 | GameObject 池管理 |
| Assets | `ResourcesAssetService` / `AddressablesAssetService` / `YooAssetAssetService` | -600 | 资源加载和引用计数，后端由 `FrameSettings.AssetServiceBackend` 决定 |
| Scenes | `SceneService` | -500 | SceneManager 封装 |
| UI | `UIService` | -400 | UGUI root、路由、栈、弹窗 |
| Audio | `AudioService` | -300 | 音源池、BGM、音效 |
| DOTween | `DOTweenTweenService` | -250 | DOTween 适配 |
| Config | `ConfigService` | -200 | 配置 Provider 链 |
| Save | `SaveService` | -100 | 本地存档 |
| Input | `InputService` | 0 | 输入上下文和绑定 |
| Networking | `SocketService` / `HttpService` | -90 / 0 | TCP/WebSocket 长连接和 HTTP 请求 |
| Localization | `LocalizationService` | 0 | 本地化文本 |

常用服务解析方式：

```csharp
using Frame.Core;
using Frame.UI;
using Frame.Save;

IUIService ui = Framework.Resolve<IUIService>();
ISaveService save = Framework.Resolve<ISaveService>();

if (Framework.TryResolve(out IUIService optionalUi))
{
    optionalUi.CloseAll();
}
```

## Core 模块

Core 是框架的启动和生命周期基础。它不承载具体业务能力，而是提供“框架如何存在、模块如何运行、服务如何被找到”的基础设施。

### 使用方式

默认使用自动启动：

1. 在 Unity 菜单执行 `Frame/Create Default Frame Settings`。
2. `FrameSettings` 会保存到 `Assets/Frame/Resources/Frame/FrameSettings.asset`。
3. 运行游戏时 `Framework.AutoBootstrap()` 在场景加载前执行。
4. 如果 `AutoCreateGameEntry` 为 true，框架会自动创建名为 `Frame` 的 `GameObject` 并挂载 `GameEntry`。

手动入口方式：

1. 在场景中执行 `Frame/Create GameEntry In Scene`。
2. 把 `FrameSettings` 指定到 `GameEntry`。
3. 可以关闭 `FrameSettings.AutoCreateGameEntry`，避免重复入口。

创建自定义模块：

```csharp
using Frame.Core;

public sealed class QuestService : GameModuleBase
{
    public override int Priority => 10;

    protected override void OnInitialize()
    {
        Context.Services.Register(this);
    }

    public override void Update(float deltaTime, float unscaledDeltaTime)
    {
        // 每帧逻辑
    }
}
```

如果要让外部程序集自动安装模块，实现 `IFrameModuleInstaller`：

```csharp
using Frame.Core;

public sealed class GameModuleInstaller : IFrameModuleInstaller
{
    public void Install(ModuleManager modules, FrameSettings settings)
    {
        modules.Add(new QuestService());
    }
}
```

### 设计和实现

Core 的核心设计是把 Unity 的不可控生命周期转换成可排序、可测试的模块生命周期。`GameEntry` 只负责接收 Unity 回调，`Framework` 只负责协调模块，具体能力都在各模块内部。

初始化流程：

1. `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 清理静态状态，适配 Unity Enter Play Mode。
2. `RuntimeInitializeOnLoadMethod(BeforeSceneLoad)` 调用 `FrameSettings.LoadOrDefault()`。
3. `GameEntry.Ensure(settings)` 查找或创建入口对象。
4. `GameEntry.Awake()` 调用 `Framework.Initialize(this, settings)`。
5. `Framework.Initialize()` 创建 `ServiceRegistry`、`ModuleManager`、`FrameContext`。
6. `RegisterDefaultModules()` 根据 `FrameSettings` 开关添加内置模块。
7. `RegisterInstalledModules()` 扫描所有程序集里实现 `IFrameModuleInstaller` 的类型，并调用 `Install()`。
8. `ModuleManager.InitializeAll()` 按 `Priority` 初始化模块。
9. `GameEntry.Start/Update/FixedUpdate/LateUpdate/OnApplicationPause/OnApplicationFocus/OnApplicationQuit` 转发给 `Framework`。
10. `Framework.Shutdown()` 倒序关闭模块并清空服务容器。

如果某个模块初始化失败，`Framework.Initialize()` 会捕获异常，调用 `CleanupFailedInitialization()` 关闭已初始化模块并清空服务，最后抛出带 inner exception 的 `FrameException`。这样不会留下半初始化状态。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `FrameSettings` | 全局配置资源 | 控制自动启动、DontDestroyOnLoad、日志等级、运行时诊断、模块开关、UI 分辨率、音频池大小、存档目录、池默认大小 |
| `Framework` | 框架静态门面 | 提供 `Initialize`、`Shutdown`、生命周期转发、`Resolve<T>`、`TryResolve<T>`，并注册默认模块和扩展模块 |
| `GameEntry` | Unity 入口组件 | 继承 `MonoSingleton<GameEntry>`，接收 Unity 生命周期并转发给 `Framework` |
| `FrameContext` | 模块初始化上下文 | 包含 `Entry`、`Settings`、`Services`、`Root`，模块通过它访问框架环境 |
| `ServiceRegistry` | 服务容器 | 用 `Dictionary<Type, object>` 按类型注册和解析服务，`Clear()` 时会释放实现 `IDisposable` 的服务 |
| `ModuleManager` | 模块管理器 | 添加模块、按优先级排序、初始化、Start、Update、Pause、Focus、Quit、倒序 Shutdown |
| `IFrameModule` | 模块生命周期接口 | 定义模块名称、优先级、初始化状态和所有生命周期回调 |
| `GameModuleBase` | 模块基类 | 提供模板方法 `OnInitialize/OnShutdown`，封装 `IsInitialized` 和失败清理 |
| `IFrameModuleInstaller` | 外部模块安装入口 | 供 DOTween 等独立程序集自动把模块加入 `ModuleManager` |
| `FrameLog` | 框架日志入口 | 受 `FrameSettings.EnableLogs` 和 `MinimumLogLevel` 控制，维护最近日志缓冲并触发 `EntryWritten` |
| `FrameLogEntry` | 单条日志数据 | 保存等级、原始消息、格式化消息、异常、UTC 时间 |
| `FrameLogLevel` | 日志等级枚举 | `Trace`、`Debug`、`Info`、`Warning`、`Error`、`Off` |
| `FrameException` | 框架异常类型 | 用于抛出明确的框架级错误 |
| `Singleton<T>` | 普通 C# 单例基类 | 延迟创建，支持 `ReleaseInstance()` 和初始化/释放钩子 |
| `MonoSingleton<T>` | MonoBehaviour 单例基类 | 查找或创建场景对象，处理重复实例、退出状态和可选 DontDestroyOnLoad |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Core` 的实际源码文件展开。Core 是所有模块的启动、调度、服务定位和生命周期基础，后续模块的 `OnInitialize()`、`Context.Services.Register(...)`、`Framework.Resolve<T>()` 都依赖这里的约定。

#### `FrameContext.cs`

`FrameContext` 是模块初始化期间传入的只读上下文对象。它本身不包含业务逻辑，只把框架运行环境集中交给模块。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `FrameContext(GameEntry entry, FrameSettings settings, ServiceRegistry services, Transform root)` | 构造上下文 | 直接把入口组件、配置、服务容器、根 Transform 保存到只读属性 | 只在 `Framework.Initialize()` 中创建。自定义模块不要自己 new，应该在 `GameModuleBase.Context` 里读取 |
| `Entry` | 当前框架入口组件 | 保存 `GameEntry` 引用 | 需要挂载子节点、启动协程或访问入口对象时使用 |
| `Settings` | 当前配置资源 | 保存 `FrameSettings` 引用 | 模块读取开关、UI、音频、存档目录等配置时使用 |
| `Services` | 服务注册表 | 保存 `ServiceRegistry` 引用 | 模块在 `OnInitialize()` 中注册接口和实现，例如 `Register<IUIService>(this)` |
| `Root` | 框架根节点 | 保存 `entry.transform` | 创建模块内部 GameObject 时建议挂到这里，便于统一管理 |

#### `FrameException.cs`

`FrameException` 是框架内部主动抛出的异常类型，用来区分“业务异常”和“框架状态错误”。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `FrameException(string message)` | 抛出普通框架异常 | 继承 `System.Exception` 并把 message 传给基类 | 用于缺少 `GameEntry`、服务未注册、模块重复注册等明确错误 |
| `FrameException(string message, Exception innerException)` | 抛出带内部异常的框架异常 | 把原始异常作为 inner exception 保存 | `Framework.Initialize()` 初始化失败时使用，保留真实失败原因 |

#### `FrameLogLevel.cs`

`FrameLogLevel` 定义日志过滤等级。枚举值从低到高排列，因此 `FrameLog.Write()` 可以用 `level < minimumLevel` 做过滤。

| 枚举值 | 含义 | 使用建议 |
| --- | --- | --- |
| `Trace` | 最细粒度追踪日志 | 高频、临时排查问题时使用 |
| `Debug` | 调试日志 | 开发期模块初始化、状态切换可使用 |
| `Info` | 普通信息 | 默认最低等级，适合关键流程日志 |
| `Warning` | 警告 | 可恢复但需要关注的问题 |
| `Error` | 错误 | 失败、异常、不可忽略问题 |
| `Off` | 关闭日志 | `MinimumLogLevel` 设为 `Off` 后 `Write()` 直接返回 |

#### `FrameLogEntry.cs`

`FrameLogEntry` 是一条框架日志的结构化数据。Diagnostics、文件日志、运行时面板都读这个对象，而不是重新解析 Unity Console 文本。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `FrameLogEntry(FrameLogLevel level, string message, string formattedMessage, Exception exception = null)` | 创建日志条目 | 保存等级、原始消息、带 `[Frame]` 前缀的格式化消息、异常对象，并记录 `DateTime.UtcNow.Ticks` | 只由 `FrameLog` 创建。文件日志用 UTC 时间保证跨时区可读 |
| `Level` | 日志等级 | 构造后私有 set | 用于筛选 Warning/Error/Exception |
| `Message` | 原始业务消息 | 构造后私有 set | 运行时面板显示时使用，避免重复 `[Frame]` 前缀 |
| `FormattedMessage` | 实际输出到 Unity Console 的消息 | 构造后私有 set | 文件日志优先写它，保持与 Console 一致 |
| `Exception` | 关联异常 | 可为空 | `DiagnosticsService` 用它统计 exception 数量 |
| `UtcTicks` | UTC ticks | 构造时写入 | 可序列化、可转换成 `DateTime` |
| `UtcTime` | UTC 时间 | 每次 getter 用 `new DateTime(UtcTicks, DateTimeKind.Utc)` 转换 | 文件日志用 `o` 格式输出 ISO 时间 |

#### `FrameLog.cs`

`FrameLog` 是框架日志统一入口。它同时负责三件事：按配置过滤日志、写 Unity Console、维护最近日志缓冲并发布 `EntryWritten` 事件。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `EntryWritten` | 日志写入事件 | `Publish()` 在日志入缓冲后触发 | `DiagnosticsService` 和 `FileLogSink` 通过它接收全部框架日志 |
| `BufferedEntries` | 最近日志缓冲 | 返回内部 `List<FrameLogEntry>` 的只读视图 | 只读引用，不要假设线程安全；主要给诊断面板读取 |
| `MaxBufferedEntries` | 最大缓冲条数 | setter 用 `Mathf.Max(0, value)` 限制并调用 `TrimBuffer()` | 设为 0 会清空并关闭内存缓冲，但事件仍会发布 |
| `Configure(FrameSettings settings)` | 应用日志配置 | `settings == null` 时恢复默认开启和 `Info` 等级，否则读取 `EnableLogs` 与 `MinimumLogLevel` | `Framework.Initialize()` 早期调用。测试里可传 null 重置日志状态 |
| `ClearBufferedEntries()` | 清空内存日志 | 直接 `bufferedEntries.Clear()` | `DiagnosticsService.ClearLogs()` 调用它，同时重置计数 |
| `Trace/Debug/Info/Warning/Error(string message)` | 不同等级日志快捷入口 | 全部转发到 `Write(level, message)` | 业务层统一用这些方法，避免直接依赖 Unity `Debug` |
| `Exception(Exception exception)` | 写异常日志 | 检查日志开关和最低等级，构造 `FrameLogEntry(Error, ...)`，然后调用 `Debug.LogException` 或 `Debug.LogError` | 异常对象会保存在 `FrameLogEntry.Exception` 中，诊断统计会把它计入 `ExceptionCount` |
| `Write(FrameLogLevel level, string message)` | 通用日志写入 | 根据 `enabled`、`minimumLevel`、`Off` 过滤；生成 `[Frame] message`；发布后按等级调用 Unity `Log/Warning/Error` | 所有非异常日志都会走这里；`Warning` 和 `Error` 会进入 Unity Console 对应等级 |
| `Publish(FrameLogEntry entry)` | 内部发布流程 | 空值保护；按 `MaxBufferedEntries` 加入缓冲并裁剪；取事件委托快照后触发；捕获订阅者异常并写 Unity exception | 事件订阅者抛异常不会打断日志系统 |
| `TrimBuffer()` | 裁剪内存缓冲 | `maxBufferedEntries <= 0` 时清空；否则计算溢出数量并 `RemoveRange(0, overflow)` 删除最旧日志 | 缓冲保留最近 N 条，适合运行时面板显示 |

#### `FrameSettings.cs`

`FrameSettings` 是框架的全局配置资产，默认放在 `Resources/Frame/FrameSettings.asset`。它通过只读属性暴露序列化字段，并在 getter 中做必要的兜底和范围限制。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `ResourcesPath` | 默认配置资源路径 | 常量 `"Frame/FrameSettings"` | `FrameSettings.LoadOrDefault()` 用它从 Resources 加载 |
| Bootstrap 属性 | 控制启动行为 | `AutoCreateGameEntry`、`UseDontDestroyOnLoad`、`RunInBackground`、`TargetFrameRate` 直接读取字段 | `AutoCreateGameEntry` 为 true 时会在场景加载前自动创建 `GameEntry` |
| Logging 属性 | 控制日志 | `EnableLogs` 和 `MinimumLogLevel` 由 `FrameLog.Configure()` 使用 | 只影响 `FrameLog`，不直接拦截业务自己的 `Debug.Log` |
| Diagnostics 属性 | 控制运行时面板 | `EnableRuntimeDiagnosticsOverlay`、`RuntimeDiagnosticsOverlayVisibleOnStart`、`RuntimeDiagnosticsOverlayToggleKey` | 面板创建发生在模块初始化之后 |
| Module 开关属性 | 控制默认模块是否注册 | `EnableDiagnosticsService` 到 `EnableLocalizationService` 分别被 `Framework.RegisterDefaultModules()` 判断 | 关闭模块后对应接口 `Framework.Resolve<T>()` 会失败，应使用 `TryResolve` 处理可选依赖 |
| `UIRootName` | UI 根节点名称 | 空白时返回 `"UIRoot"` | `UIService` 创建 root 时使用 |
| `UIReferenceResolution` | UI 参考分辨率 | 用 `Mathf.Max(1, width/height)` 兜底 | 防止 CanvasScaler 收到 0 或负数 |
| `UIMatchWidthOrHeight` | CanvasScaler 匹配值 | `Mathf.Clamp01` 限制 0 到 1 | 0 偏宽度，1 偏高度 |
| `AudioSourcePoolSize` | 音频源池大小 | 最小值限制为 1 | `AudioService` 预热 AudioSource 池使用 |
| `GetAudioMixerGroup(AudioCategory category)` | 获取最终 MixerGroup | 先取分类专用组，缺失时回退到 master | 音效分类没有独立 mixer 时仍可受 master 控制 |
| `GetAssignedAudioMixerGroup(AudioCategory category)` | 获取分类显式配置的 MixerGroup | switch 按 `Music/Sfx/UI/Ambient` 返回字段，默认返回 master | 只想知道“是否配置了分类专用组”时用它 |
| `GetAudioMixerVolumeParameter(AudioCategory category)` | 获取音量参数名 | switch 按分类返回不同 exposed parameter 名称 | `AudioService` 设置 mixer dB 值时使用 |
| `SaveFolderName` | 存档目录名 | 空白时返回 `"Saves"` | `SaveService` 与路径工具组合成实际持久化目录 |
| `DefaultGameObjectPoolMaxSize` | GameObject 池默认容量 | 最小值限制为 1 | `PoolService.CreateGameObjectPool()` 传负数时用这个默认值 |
| `LoadOrDefault()` | 加载或创建运行时配置 | 优先 `Resources.Load<FrameSettings>(ResourcesPath)`，找不到时 `CreateInstance<FrameSettings>()` 并命名为 `"Runtime FrameSettings"` | 没有创建配置资产时框架仍可运行，但会使用源码默认值 |
| `OnValidate()` | 编辑器内修正非法数值 | 只在 `UNITY_EDITOR` 编译；限制 UI 尺寸、match、音频池、默认池大小 | 修改 Inspector 后自动清理明显非法值，运行时 build 不包含这个方法 |

#### `IFrameModule.cs`

`IFrameModule` 定义模块生命周期协议。所有模块都被 `ModuleManager` 通过这个接口统一调度。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Name` | 模块名称 | 通常由 `GameModuleBase` 返回类型名 | 日志和调试信息使用 |
| `Priority` | 初始化和 Update 顺序 | 数值越小越早初始化，越晚关闭 | 依赖基础服务的模块应设置更大的 priority |
| `IsInitialized` | 初始化状态 | 由实现维护 | `GameModuleBase` 已提供默认状态管理 |
| `Initialize(FrameContext context)` | 初始化模块 | 由 `ModuleManager.InitializeAll()` 调用 | 模块应在这里注册服务、创建内部对象 |
| `Start()` | Unity Start 阶段 | `GameEntry.Start()` 转发后调用 | 适合依赖所有模块都完成初始化后的逻辑 |
| `Update/FixedUpdate/LateUpdate(...)` | 每帧调度 | 参数由 `GameEntry` 从 Unity Time 转发 | 模块不需要自己挂 MonoBehaviour 也能获得生命周期 |
| `OnApplicationPause/OnApplicationFocus/OnApplicationQuit(...)` | 应用生命周期事件 | `GameEntry` 通过 `Framework` 和 `ModuleManager` 转发 | 适合保存状态、暂停计时、发出事件 |
| `Shutdown()` | 关闭模块 | `ModuleManager.ShutdownAll()` 倒序调用 | 必须释放事件订阅、GameObject、句柄、文件等资源 |

#### `GameModuleBase.cs`

`GameModuleBase` 是模块实现的推荐基类，把初始化状态、上下文保存、失败清理封装好。大多数服务只需要 override `OnInitialize()`、必要时 override `OnShutdown()` 和 Update 类方法。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Name` | 默认模块名 | 返回 `GetType().Name` | 特殊日志名可 override |
| `Priority` | 默认优先级 | 返回 0 | 内置模块通过 override 调整顺序 |
| `IsInitialized` | 初始化标记 | `Initialize()` 成功后置 true，`Shutdown()` 后置 false | 外部只读 |
| `Context` | 当前模块上下文 | 初始化时保存，关闭后清空 | 只在模块生命周期内有效 |
| `Initialize(FrameContext frameContext)` | 模板初始化流程 | 防重复；保存 context；调用 `OnInitialize()`；成功后置 `IsInitialized = true`；失败时尝试 `OnShutdown()` 并清空状态后重新抛出 | 自定义模块不要 override 它，override `OnInitialize()` 即可 |
| `Start/Update/FixedUpdate/LateUpdate(...)` | 默认空生命周期 | virtual 空实现 | 需要每帧逻辑时 override |
| `OnApplicationPause/OnApplicationFocus/OnApplicationQuit(...)` | 默认空应用事件 | virtual 空实现 | 需要响应 pause/focus/quit 时 override |
| `Shutdown()` | 模板关闭流程 | 未初始化直接返回；调用 `OnShutdown()`；清空初始化状态和 context | 自定义清理写在 `OnShutdown()` |
| `OnInitialize()` | 初始化钩子 | protected virtual 空实现 | 注册服务、创建资源、订阅事件放这里 |
| `OnShutdown()` | 关闭钩子 | protected virtual 空实现 | 反注册事件、释放资源放这里 |

#### `IFrameModuleInstaller.cs`

`IFrameModuleInstaller` 是外部程序集扩展模块的安装入口。框架启动时会扫描 AppDomain 中所有实现类并调用它。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Install(ModuleManager modules, FrameSettings settings)` | 向框架安装模块 | 由 `Framework.RegisterInstalledModules()` 通过反射创建 installer 后调用 | DOTween 集成就通过它添加 `DOTweenTweenService`。Installer 应保持无参构造，内部根据 `settings` 判断是否安装 |

#### `ModuleManager.cs`

`ModuleManager` 管理所有 `IFrameModule`。它只关心模块生命周期，不关心具体业务。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Modules` | 当前模块列表 | 返回内部 list 的只读视图 | 调试和诊断使用，不建议业务直接遍历调用 |
| `Add(IFrameModule module)` | 添加模块 | 空值抛 `ArgumentNullException`；用具体类型去重；加入 list 和字典；按 priority 排序 | 同一实现类型只能注册一次。多个模块 priority 相同则按当前排序结果执行 |
| `TryGet<TModule>(out TModule module)` | 尝试按具体模块类型获取 | 用 `typeof(TModule)` 查字典并安全转换 | 获取实现类而不是服务接口时使用 |
| `Get<TModule>()` | 按具体模块类型获取 | `TryGet` 成功返回，失败抛 `FrameException` | 适合内部确认模块必然存在的场景 |
| `InitializeAll(FrameContext context)` | 初始化全部模块 | 按已排序顺序依次 `Initialize(context)`，每个成功后写 Debug 日志 | 某个模块抛异常会中断，外层 `Framework.Initialize()` 负责清理 |
| `StartAll()` | 调用全部模块 Start | 正序遍历 | 发生在 Unity `Start` 阶段 |
| `UpdateAll(float deltaTime, float unscaledDeltaTime)` | 调用全部模块 Update | 正序遍历 | `TimerService`、`DiagnosticsService` 等依赖每帧推进 |
| `FixedUpdateAll(...)` | 调用全部模块 FixedUpdate | 正序遍历 | 给物理或固定步长模块预留 |
| `LateUpdateAll(...)` | 调用全部模块 LateUpdate | 正序遍历 | 给需要晚于 Update 的模块预留 |
| `PauseAll(bool paused)` | 转发暂停事件 | 正序遍历调用 `OnApplicationPause` | `TimerService` 会据此暂停 |
| `FocusAll(bool focused)` | 转发焦点事件 | 正序遍历调用 `OnApplicationFocus` | `LifecycleService` 会更新焦点状态 |
| `ApplicationQuitAll()` | 转发退出事件 | 正序遍历调用 `OnApplicationQuit` | 退出事件先通知，再由 `ShutdownAll()` 做资源释放 |
| `ShutdownAll()` | 关闭全部模块 | 从 list 尾部倒序 `Shutdown()`，再清空 list 和字典 | 倒序保证后初始化的模块先释放，减少依赖已释放服务 |
| `CompareModulePriority(IFrameModule a, IFrameModule b)` | 排序比较器 | 返回 `a.Priority.CompareTo(b.Priority)` | priority 小的先初始化 |

#### `ServiceRegistry.cs`

`ServiceRegistry` 是轻量服务容器，只按类型做注册和解析，不做构造注入。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Register<TService>(TService service)` | 注册服务实例 | 空值抛 `ArgumentNullException`；用 `typeof(TService)` 作为 key 覆盖保存 | 模块通常同时注册接口和自身实现，例如 `Register<IEventBus>(this)` 与 `Register(this)` |
| `TryResolve<TService>(out TService service)` | 尝试解析服务 | 查字典并 `as TService` 转换，失败返回 false | 可选模块或可能被关闭的服务应使用它 |
| `Resolve<TService>()` | 强制解析服务 | `TryResolve` 成功返回，失败抛 `FrameException` | 业务确定服务启用时使用 |
| `Unregister<TService>()` | 移除注册 | 按类型 key 删除 | 少量动态服务可使用，内置模块主要在 shutdown 时统一 clear |
| `Clear()` | 清空服务容器并释放 disposable | 遍历 values，对实现 `IDisposable` 的服务调用 `Dispose()`；用 `disposed` list 避免同一实例以接口和实现注册时重复 Dispose；最后清空字典 | 模块同时注册多个类型时不会重复释放同一个对象 |

#### `Singleton.cs`

`Singleton<T>` 是非 MonoBehaviour 的懒加载单例基类。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Instance` | 获取或创建单例 | 双重检查锁；用 `Activator.CreateInstance(typeof(T), true)` 调私有/受保护构造；调用 `OnSingletonInitialize()` 后赋值 | 适合纯 C# 管理器。构造函数必须可被反射创建 |
| `HasInstance` | 是否已创建 | 判断静态 `instance != null` | 想避免触发创建时使用 |
| `ReleaseInstance()` | 释放单例引用 | lock 内取出并置空，lock 外调用 `OnSingletonRelease()` | 适合测试或手动生命周期管理 |
| `OnSingletonInitialize()` | 初始化钩子 | protected virtual 空实现 | 子类在第一次创建时初始化资源 |
| `OnSingletonRelease()` | 释放钩子 | protected virtual 空实现 | 子类释放资源、取消订阅 |

#### `MonoSingleton.cs`

`MonoSingleton<T>` 是 MonoBehaviour 单例基类，处理场景查找、自动创建、重复实例销毁和应用退出状态。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Instance` | 当前实例 | 返回静态 `instance` | 不会自动创建，避免隐藏场景副作用 |
| `HasInstance` | 是否已有实例 | 判断静态 `instance != null` | 查询状态用 |
| `IsApplicationQuitting` | 是否正在退出 | `OnApplicationQuit()` 设置静态标记 | `GameEntry` 用它避免退出时重复 shutdown |
| `UseDontDestroyOnLoad` | 是否跨场景保留 | 默认 false，子类 override | `GameEntry` 根据 `FrameSettings.UseDontDestroyOnLoad` 返回 |
| `GetOrCreate()` | 获取或创建实例 | 先用静态实例，再查场景现有对象，最后创建新 GameObject 并 AddComponent | 需要确保单例存在时使用 |
| `FindExistingInstance()` | 查找场景实例 | Unity 2023+ 用 `FindAnyObjectByType(...Include)`，旧版本用 `FindObjectOfType<T>(true)` | 会包含 inactive 对象 |
| `Awake()` | 注册单例 | 如果已有其他实例，调用 `OnDuplicateInstance()` 并销毁当前 GameObject；否则设置 instance、调用 `OnSingletonAwake()`，必要时 `DontDestroyOnLoad` | 子类不要直接 override `Awake()`，应 override 钩子 |
| `OnApplicationQuit()` | 记录退出状态 | 设置 `isApplicationQuitting = true` 后调用 `OnSingletonApplicationQuit()` | 子类退出逻辑写钩子 |
| `OnDestroy()` | 清理实例引用 | 只有当前对象是 instance 时才调用 `OnSingletonDestroyed()` 并置空 | 重复实例被销毁时不会误清空真正实例 |
| `OnSingletonAwake()` | Awake 钩子 | protected virtual 空实现 | `GameEntry` 在这里初始化框架 |
| `OnSingletonApplicationQuit()` | 退出钩子 | protected virtual 空实现 | `GameEntry` 在这里转发 quit 并 shutdown |
| `OnSingletonDestroyed()` | 销毁钩子 | protected virtual 空实现 | `GameEntry` 在非退出销毁时 shutdown |
| `OnDuplicateInstance(T current)` | 重复实例钩子 | protected virtual 空实现 | 可在重复对象被销毁前做日志或迁移 |

#### `GameEntry.cs`

`GameEntry` 是 Unity 场景和纯 C# 框架之间的桥。它只转发生命周期，不直接实现具体模块功能。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Settings` | 当前入口使用的配置 | 返回序列化字段 `settings` | 手动场景入口可在 Inspector 指定 |
| `UseDontDestroyOnLoad` | 是否跨场景保留 | override `MonoSingleton`，读取 `settings.UseDontDestroyOnLoad` | settings 为空时不保留 |
| `Ensure(FrameSettings frameSettings)` | 确保场景中存在入口 | 若 `Instance` 存在直接返回；否则查找场景已有入口并补 settings；仍不存在则创建 inactive 的 `"Frame"` GameObject，挂 `GameEntry`，设置 settings 后激活 | `Framework.AutoBootstrap()` 自动入口使用它。先 inactive 再 AddComponent 可以避免 Awake 过早读取未赋值 settings |
| `UseSettings(FrameSettings frameSettings)` | 设置入口配置 | 只有当前 `settings == null` 才赋值 | 避免覆盖手动场景入口已经指定的配置 |
| `OnSingletonAwake()` | 入口 Awake 钩子 | 缺失 settings 时 `FrameSettings.LoadOrDefault()`；`initializeOnAwake` 为 true 时调用 `Framework.Initialize(this, settings)` | 手动控制初始化时可关闭 `initializeOnAwake` |
| `Start()` | 转发 Unity Start | 调用 `Framework.Start()` | 确保所有模块初始化后再进入 Start 阶段 |
| `Update()` | 转发 Unity Update | 传入 `Time.deltaTime` 和 `Time.unscaledDeltaTime` | 所有模块的非 MonoBehaviour 每帧逻辑来自这里 |
| `FixedUpdate()` | 转发 Unity FixedUpdate | 传入 `Time.fixedDeltaTime` 和 `Time.fixedUnscaledDeltaTime` | 供固定步长模块使用 |
| `LateUpdate()` | 转发 Unity LateUpdate | 传入当前帧 delta 和 unscaled delta | 供晚帧模块使用 |
| `OnApplicationPause(bool pauseStatus)` | 转发暂停事件 | 调用 `Framework.OnApplicationPause(pauseStatus)` | 移动端切后台会触发 |
| `OnApplicationFocus(bool hasFocus)` | 转发焦点事件 | 调用 `Framework.OnApplicationFocus(hasFocus)` | PC 窗口焦点变化会触发 |
| `OnSingletonApplicationQuit()` | 退出流程 | 先 `Framework.OnApplicationQuit()` 通知模块，再 `Framework.Shutdown()` 倒序释放 | 退出时的唯一主关闭路径 |
| `OnSingletonDestroyed()` | 非退出销毁保护 | 如果不是应用退出且框架已初始化，则调用 `Framework.Shutdown()` | 手动删除入口对象时避免框架静态状态残留 |

#### `Framework.cs`

`Framework` 是静态门面，负责自动启动、初始化、模块注册、生命周期转发、服务解析和失败清理。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `IsInitialized` | 框架是否初始化完成 | 初始化成功置 true，shutdown 或失败清理置 false | 业务可用于启动状态判断 |
| `Context` | 当前上下文 | 返回静态 `context` | 未初始化时为 null |
| `Services` | 当前服务容器 | 返回静态 `services` | 推荐业务使用 `Resolve<T>()` 而不是直接操作 |
| `Modules` | 当前模块管理器 | 返回静态 `modules` | 调试或高级扩展使用 |
| `ResetStatics()` | Enter Play Mode 前重置静态状态 | `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 调用，清空 started、initialized、context、services、modules | 适配 Unity 禁用 Domain Reload 的情况 |
| `AutoBootstrap()` | 场景加载前自动创建入口 | `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` 调用；加载 settings；如果 `AutoCreateGameEntry` 为 true 则 `GameEntry.Ensure(settings)` | 默认自动启动路径 |
| `Initialize(GameEntry entry, FrameSettings settings)` | 初始化框架 | 防重复；校验 entry；补默认 settings；配置日志和 Application；创建 `ServiceRegistry`、`ModuleManager`、`FrameContext`；注册 settings 与 services；注册默认模块和 installer 模块；初始化模块；创建诊断面板；失败时清理并抛 `FrameException` | 手动启动时调用它。模块初始化异常会被包成带 inner exception 的 `FrameException` |
| `Start()` | 启动模块 Start 阶段 | 未初始化或已 started 时返回；设置 `isStarted` 并 `modules.StartAll()` | 由 `GameEntry.Start()` 调用 |
| `Update/FixedUpdate/LateUpdate(...)` | 生命周期转发 | 初始化后分别调用 `ModuleManager` 对应方法 | 不初始化时静默忽略 |
| `OnApplicationPause/OnApplicationFocus/OnApplicationQuit(...)` | 应用事件转发 | 初始化后调用 `PauseAll/FocusAll/ApplicationQuitAll` | 真正资源释放在 `Shutdown()` |
| `Shutdown()` | 关闭框架 | 未初始化返回；倒序关闭模块；清空服务；重置静态状态；写 shutdown 日志 | 一般由 `GameEntry` 退出或销毁触发 |
| `Resolve<TService>()` | 强制解析服务 | 未初始化抛 `FrameException`，否则转发 `services.Resolve<TService>()` | 业务最常用入口 |
| `TryResolve<TService>(out TService service)` | 尝试解析服务 | 未初始化返回 false；否则转发 `services.TryResolve` | 可选模块、诊断面板、容错逻辑建议使用 |
| `ApplyApplicationSettings(FrameSettings settings)` | 应用 Unity 级设置 | 设置 `Application.runInBackground`；`TargetFrameRate != 0` 时设置 `Application.targetFrameRate` | target frame rate 为 0 表示不改 Unity 当前值 |
| `RegisterDefaultModules(FrameSettings settings)` | 注册内置模块 | 按 settings 的模块开关依次 `modules.Add(new XxxService())` | 这里决定内置模块是否存在；DOTween 不在这里，而是 installer 扩展 |
| `RegisterInstalledModules(FrameSettings settings)` | 注册外部模块 | 遍历当前 AppDomain 程序集，读取可加载类型；筛选非抽象、非接口、实现 `IFrameModuleInstaller` 的类型；无参创建并调用 `Install()`；异常写日志不中断其他 installer | 适合集成包自动接入。Installer 构造函数不应依赖场景对象 |
| `CreateRuntimeDiagnosticsOverlay(GameEntry entry, FrameSettings settings)` | 创建运行时诊断面板 | entry/settings 为空或未启用时返回；否则调用 `RuntimeDiagnosticsOverlay.Ensure(...)` | 放在模块初始化之后，面板可以立即解析服务 |
| `GetLoadableTypes(Assembly assembly)` | 安全读取程序集类型 | 动态程序集返回空；正常用 `assembly.GetTypes()`；`ReflectionTypeLoadException` 返回可加载的 `Types`；其他异常写日志后返回空 | 避免某个程序集类型加载失败导致整个框架启动失败 |
| `CleanupFailedInitialization()` | 初始化失败清理 | 如果 modules 存在则 `ShutdownAll()`；services 存在则 `Clear()`；清空静态状态 | 保证半初始化失败不会留下服务和模块引用 |

## Diagnostics 模块

Diagnostics 用于运行时排查问题，核心价值是把日志、FPS、内存、HTTP、定时器、场景、资源、对象池状态集中展示。

### 使用方式

在 `FrameSettings` 中启用：

- `EnableDiagnosticsService`：注册诊断服务。
- `EnableRuntimeDiagnosticsOverlay`：运行时生成 IMGUI 诊断面板。
- `RuntimeDiagnosticsOverlayToggleKey`：默认反引号键切换显示。

代码中写日志：

```csharp
using Frame.Core;

FrameLog.Info("Player login started.");
FrameLog.Warning("Config fallback used.");
```

写入文件日志：

```csharp
using Frame.Core;
using Frame.Diagnostics;

IDiagnosticsService diagnostics = Framework.Resolve<IDiagnosticsService>();
IDisposable sink = diagnostics.WriteLogsToFile("Logs/frame.log", 1024 * 1024);
```

### 设计和实现

`DiagnosticsService` 初始化时订阅 `FrameLog.EntryWritten`，因此所有通过 `FrameLog` 输出的日志都会进入诊断模块。它每 0.5 秒采样一次 FPS，使用 `GC.GetTotalMemory(false)` 和 `Profiler.GetTotalAllocatedMemoryLong()` 获取内存信息。

`RuntimeDiagnosticsOverlay` 是一个 `MonoBehaviour`，使用 IMGUI 绘制调试面板。它通过 `Framework.TryResolve` 动态获取各模块服务，如果某个服务被关闭，面板会显示该服务不可用。

`FileLogSink` 监听 `FrameLog.EntryWritten`，按行写入 UTF-8 文件，并在超过 `MaxBytes` 时把当前文件移动为 `.bak`。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IDiagnosticsService` | 诊断服务接口 | 暴露日志列表、快照捕获、文件日志、清空日志 |
| `DiagnosticsService` | 诊断服务实现 | 统计日志数量、异常数量、FPS、内存，注册为 `IDiagnosticsService` |
| `DiagnosticsSnapshot` | 诊断快照数据 | 保存帧号、运行时间、FPS、内存、日志数量、错误数量 |
| `RuntimeDiagnosticsOverlay` | 运行时 IMGUI 面板 | 展示 Runtime、Lifecycle、HTTP、Timers、Scenes、Assets、Pools、Logs |
| `FileLogSink` | 文件日志输出 | 订阅日志事件，写入文件，支持大小轮转和 `.bak` 备份 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Diagnostics` 的实际源码文件展开。Diagnostics 依赖 Core 的 `FrameLog`，但不会主动接管 Unity 原生日志，只记录通过 `FrameLog` 写出的框架日志。

#### `IDiagnosticsService.cs`

`IDiagnosticsService` 是诊断模块对外接口，业务层只依赖它即可获取快照、日志和文件落盘能力。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `LogReceived` | 新框架日志事件 | `DiagnosticsService` 收到 `FrameLog.EntryWritten` 后转发 | 适合自定义日志窗口、远程上传、测试监听 |
| `Logs` | 当前内存日志列表 | 返回 `FrameLog.BufferedEntries` | 只包含最近 N 条，N 由 `FrameLog.MaxBufferedEntries` 控制 |
| `CaptureSnapshot()` | 获取运行时诊断快照 | 实现类即时读取 Time、GC、Profiler 和日志计数 | 面板或调试命令按需调用，不会持久保存历史 |
| `WriteLogsToFile(string filePath, long maxBytes = 1048576)` | 开启文件日志 | 实现类创建 `FileLogSink` 并返回可 Dispose 的句柄 | 必须由业务显式调用才会写文件。相对路径会被 `Path.GetFullPath` 转成当前进程工作目录下的绝对路径 |
| `ClearLogs()` | 清空内存日志和计数 | 清空 `FrameLog` 缓冲并重置统计 | 不会删除已经写入的日志文件 |

#### `DiagnosticsSnapshot.cs`

`DiagnosticsSnapshot` 是可序列化的数据容器，没有方法，只保存一次采样结果。

| 字段 | 含义 | 来源 |
| --- | --- | --- |
| `FrameCount` | 当前 Unity 帧号 | `Time.frameCount` |
| `RealtimeSinceStartup` | 启动后的真实时间 | `Time.realtimeSinceStartup` |
| `UnscaledTime` | 不受 timeScale 影响的时间 | `Time.unscaledTime` |
| `DeltaTime` | 当前帧 delta | `Time.deltaTime` |
| `AverageFps` | 近似平均 FPS | `DiagnosticsService.Update()` 每 0.5 秒计算 |
| `ManagedMemoryBytes` | 托管堆内存 | `GC.GetTotalMemory(false)` |
| `TotalAllocatedMemoryBytes` | Unity Profiler 总分配内存 | `Profiler.GetTotalAllocatedMemoryLong()` |
| `BufferedLogCount` | 内存日志条数 | `FrameLog.BufferedEntries.Count` |
| `WarningCount` | Warning 日志累计数 | `DiagnosticsService.Count()` |
| `ErrorCount` | Error 及以上日志累计数 | `DiagnosticsService.Count()` |
| `ExceptionCount` | 带异常对象的日志累计数 | `FrameLogEntry.Exception != null` |

#### `DiagnosticsService.cs`

`DiagnosticsService` 是诊断服务实现，优先级 `-1000`，默认最早初始化。这样后续模块初始化期间写出的日志也能被诊断服务统计。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | override 返回 `-1000` | 确保尽早订阅 `FrameLog.EntryWritten` |
| `Logs` | 当前日志列表 | 返回 `FrameLog.BufferedEntries` | 与接口一致，是共享缓冲，不是复制 |
| `OnInitialize()` | 初始化诊断服务 | 先 `RecalculateLogCounts()` 统计已存在缓冲，再订阅 `FrameLog.EntryWritten`，最后注册 `IDiagnosticsService` 和自身实现 | 如果框架初始化前已有日志，计数不会丢 |
| `Update(float deltaTime, float unscaledDeltaTime)` | 采样 FPS | 累加 unscaled 时间和帧数，每 0.5 秒计算 `averageFps = sampleFrames / sampleElapsed` 后清零 | 使用 unscaled 时间，暂停 timeScale 不影响 FPS 统计 |
| `CaptureSnapshot()` | 生成快照 | new `DiagnosticsSnapshot` 并填充 Time、GC、Profiler、日志计数 | 快照是当下状态，不会自动刷新 |
| `WriteLogsToFile(string filePath, long maxBytes = 1048576)` | 创建文件日志 sink | new `FileLogSink(filePath, maxBytes)`，加入 `logSinks`，返回 `DisposableAction`，Dispose 时释放 sink 并从列表移除 | 这个方法不会自动选择路径。建议传 `Path.Combine(Application.persistentDataPath, "Logs/frame.log")` 这类稳定路径 |
| `ClearLogs()` | 清空日志统计 | 调用 `FrameLog.ClearBufferedEntries()`，warning/error/exception 计数归零 | 已创建的 `FileLogSink` 仍继续写后续日志 |
| `OnShutdown()` | 关闭诊断服务 | 取消订阅日志事件，释放所有文件 sink，重置计数和 FPS 采样字段 | 防止退出后继续持有文件句柄 |
| `DisposeLogSinks()` | 批量释放文件 sink | 从后向前遍历 `logSinks`，每个 Dispose 包 try/catch，最后 Clear | 单个 sink 释放失败不会阻断其他 sink |
| `OnLogEntryWritten(FrameLogEntry entry)` | 处理新日志 | 调用 `Count(entry)` 更新计数；取 `LogReceived` 委托快照并 try/catch 触发 | 订阅者异常只写 Unity exception，不影响诊断服务 |
| `RecalculateLogCounts()` | 从现有日志重建计数 | 清零计数后遍历 `FrameLog.BufferedEntries`，逐条 `Count()` | 初始化时使用，保证统计和缓冲一致 |
| `Count(FrameLogEntry entry)` | 统计单条日志 | null 直接返回；`Warning` 计入 warning；`Error` 及以上计入 error；存在 `Exception` 计入 exception | Error 日志带异常时会同时增加 error 和 exception |

#### `FileLogSink.cs`

`FileLogSink` 是文件日志写入器。它不属于 Unity 组件，只订阅 `FrameLog.EntryWritten`，把每条日志追加为 UTF-8 文本行。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `FileLogSink(string filePath, long maxBytes = DefaultMaxBytes)` | 创建文件日志输出 | 校验路径非空；`Path.GetFullPath(filePath)` 转绝对路径；`MaxBytes = Math.Max(1, maxBytes)`；创建目录；订阅 `FrameLog.EntryWritten` | 相对路径取决于当前进程工作目录。Unity Editor 通常是项目根目录，Player 建议使用 `Application.persistentDataPath` |
| `FilePath` | 日志文件绝对路径 | 构造时设置，私有 set | 可用于把最终路径显示给玩家或调试面板 |
| `MaxBytes` | 单文件大小上限 | 构造时限制最小 1 字节 | 达到上限后轮转 |
| `BackupFilePath` | 备份文件路径 | getter 返回 `FilePath + ".bak"` | 当前只保留一个 `.bak`，旧备份会被删除 |
| `Dispose()` | 停止写文件 | 防重复；设置 disposed；取消订阅 `FrameLog.EntryWritten` | 调用后不会再写后续日志，但不关闭已有文本内容 |
| `OnEntryWritten(FrameLogEntry entry)` | 写入单条日志 | disposed 或 null 返回；格式化文本；lock 内再次检查 disposed；按 pending bytes 轮转；`File.AppendAllText(FilePath, line, Encoding.UTF8)` 追加 | lock 保护同一个 sink 的并发写入；异常写 Unity exception |
| `RotateIfNeeded(int pendingBytes)` | 写入前检查轮转 | 文件不存在直接返回；如果现有长度加本行长度超过 `MaxBytes`，先删旧 `.bak`，再把当前文件 Move 到 `.bak` | 轮转发生在追加前，因此新日志会写入新的主文件 |
| `Format(FrameLogEntry entry)` | 格式化日志行 | 选择 `FormattedMessage`，没有则用 `Message`；拼接 UTC ISO 时间、等级、清理后的消息、异常摘要和换行 | 文件里每条日志一行，便于 grep/上传 |
| `FormatException(Exception exception)` | 格式化异常摘要 | null 返回空；否则输出 `" | FullTypeName: Message"` 并清理换行 | 当前只写异常类型和 message，不写 stack trace |
| `Sanitize(string value)` | 清理换行 | null/empty 返回空；把 `\r`、`\n` 替换为字面量 `\\r`、`\\n` | 保证一条日志不会破坏单行格式 |

#### `RuntimeDiagnosticsOverlay.cs`

`RuntimeDiagnosticsOverlay` 是运行时 IMGUI 面板。它只在启用 `FrameSettings.EnableRuntimeDiagnosticsOverlay` 后由 `Framework.CreateRuntimeDiagnosticsOverlay()` 创建，也可以手动挂载。

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Visible` | 面板显示状态 | 读写序列化字段 `visible` | 运行时可通过代码直接打开或关闭 |
| `ToggleKey` | 切换显示按键 | 读写序列化字段 `toggleKey` | 默认反引号；设为 `KeyCode.None` 可禁用快捷键 |
| `Ensure(Transform parent, bool visibleAtStart, KeyCode toggleKey)` | 确保面板存在 | 先在 parent 子节点查找已有 `RuntimeDiagnosticsOverlay`，找到则 Configure；否则创建 `RuntimeDiagnosticsOverlay` GameObject 并挂到 parent 下 | Framework 自动创建时 parent 是 `GameEntry.transform` |
| `Configure(bool visibleAtStart, KeyCode key)` | 设置初始状态 | 设置 visible 和 toggleKey，刷新服务引用和快照 | 适合复用已有 overlay 时更新配置 |
| `Update()` | 处理快捷键和刷新 | 检测 `UnityEngine.Input.GetKeyDown(toggleKey)` 切换 visible；显示时按间隔刷新快照 | 这里使用 Unity Legacy Input，不依赖框架 InputService，避免诊断面板被输入上下文禁用 |
| `OnEnable()` | 组件启用时刷新 | 调用 `RefreshServices()` 和强制 `RefreshSnapshot(true)` | 保证重新启用后数据立即有效 |
| `OnGUI()` | 绘制 IMGUI 面板 | visible false 直接返回；计算宽度；创建窗口和 scroll view；依次绘制 Runtime、Lifecycle、HTTP、Sockets、Timers、Scenes、Assets、Pools、Logs | 使用 IMGUI，主要面向调试，不是正式 UI |
| `DrawSnapshot()` | 绘制运行时摘要 | snapshot 为空显示不可用；否则显示帧号、FPS、内存、日志计数 | 数据来自 `IDiagnosticsService.CaptureSnapshot()` |
| `DrawHttp()` | 绘制 HTTP 统计 | http 为空显示不可用；否则显示 active/started/completed/failed | 依赖 `IHttpService` 启用 |
| `DrawSockets()` | 绘制 Socket 统计 | sockets 为空显示不可用；否则显示 clients/active，并逐个显示 transport/state/sent/received/dropped | 依赖 `ISocketService` 启用 |
| `DrawLifecycle()` | 绘制应用生命周期状态 | lifecycle 为空显示不可用；否则显示 paused/focus/quitting | 依赖 `ILifecycleService` 启用 |
| `DrawPools()` | 绘制对象池统计 | pools 为空显示不可用；无池显示 `No pools.`；否则遍历 `poolStats` 显示 active/inactive/created/destroyed | 数据缓存由 `RefreshSnapshot()` 更新 |
| `DrawTimers()` | 绘制定时器统计 | timers 为空显示不可用；显示 active、scaled、unscaled、paused | 依赖 `ITimerService` 启用 |
| `DrawScenes()` | 绘制场景状态 | scenes 为空显示不可用；显示 active scene；如果有 `CurrentOperation`，显示 scene、progress、ready | 依赖 `ISceneService` 启用 |
| `DrawAssets()` | 绘制资源引用统计 | assets 为空显示不可用；无加载资源显示 `No loaded assets.`；否则显示路径、引用计数、类型 | 依赖 `IAssetService` 启用 |
| `DrawLogs()` | 绘制最近日志 | diagnostics 为空显示不可用；从 `diagnostics.Logs` 中取最后 `maxLogLines` 条显示等级和原始消息 | 不显示 stack trace，适合快速看最近框架日志 |
| `RefreshServices()` | 重新解析可选服务 | 对 diagnostics、lifecycle、http、sockets、assets、pools、scenes、timers 分别 `Framework.TryResolve(out ...)` | 模块关闭或未启用时保持 null，绘制层显示不可用 |
| `RefreshSnapshot(bool force)` | 按间隔刷新缓存 | 非 force 且未到 `nextRefreshTime` 时返回；更新下次刷新时间；刷新服务；抓快照；读取 asset stats 和 pool stats | `refreshInterval` 最小按 0.05 秒处理，避免过高刷新 |
| `FormatBytes(long bytes)` | 格式化内存大小 | 除以 1024*1024 并保留一位小数，加 `" MB"` | 面板内部显示使用 |

## Lifecycle 模块

Lifecycle 负责把 Unity 的应用暂停、焦点变化、退出状态转换为可订阅服务。

### 使用方式

```csharp
using Frame.Core;
using Frame.Lifecycle;

ILifecycleService lifecycle = Framework.Resolve<ILifecycleService>();
lifecycle.PauseChanged += paused =>
{
    if (paused)
    {
        // 保存临时数据
    }
};
```

### 设计和实现

`GameEntry.OnApplicationPause`、`OnApplicationFocus`、`OnApplicationQuit` 会转发到 `Framework`，再由 `ModuleManager` 分发给所有模块。`LifecycleService` 保存最新状态并触发事件。事件回调异常会被 `FrameLog.Exception` 捕获，不会打断后续生命周期处理。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ILifecycleService` | 生命周期状态接口 | 暴露 `PauseChanged`、`FocusChanged`、`Quitting` 和三个状态属性 |
| `LifecycleService` | 生命周期实现 | 记录 `IsPaused`、`HasFocus`、`IsQuitting`，处理 Unity 生命周期转发 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Lifecycle` 的实际源码文件展开。Lifecycle 模块的职责很窄：把 Unity 回调转成可查询状态和可订阅事件。

#### `ILifecycleService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `PauseChanged` | 暂停状态变化事件 | `LifecycleService.OnApplicationPause()` 在状态真正变化时触发 | 适合暂停计时、保存临时进度、暂停音频 |
| `FocusChanged` | 焦点状态变化事件 | `LifecycleService.OnApplicationFocus()` 在状态真正变化时触发 | PC 或编辑器窗口失焦时可用 |
| `Quitting` | 应用退出事件 | `LifecycleService.OnApplicationQuit()` 首次退出时触发 | 适合保存最后状态，不适合做耗时异步流程 |
| `IsPaused` | 当前是否暂停 | 实现类保存最新 pause 状态 | 查询当前状态用 |
| `HasFocus` | 当前是否有焦点 | 初始化时读取 `Application.isFocused`，之后由 focus 回调更新 | 首帧即可得到初始焦点状态 |
| `IsQuitting` | 是否正在退出 | 首次 quit 后置 true | 避免重复执行退出逻辑 |

#### `LifecycleService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-950` | 仅晚于 Diagnostics，早于大多数业务模块 |
| `IsPaused/HasFocus/IsQuitting` | 状态属性 | public getter、private setter | 由 Unity 生命周期事件驱动，不需要业务手动设置 |
| `OnInitialize()` | 初始化服务 | 设置 `HasFocus = Application.isFocused`，注册 `ILifecycleService` 和自身实现 | 初始化时不会主动触发 `FocusChanged` |
| `OnApplicationPause(bool paused)` | 处理暂停变化 | 如果状态未变化直接返回；变化时更新 `IsPaused` 并 `Invoke(PauseChanged, paused)` | 避免重复 pause 事件导致业务重复保存 |
| `OnApplicationFocus(bool focused)` | 处理焦点变化 | 如果状态未变化直接返回；变化时更新 `HasFocus` 并 `Invoke(FocusChanged, focused)` | 和 pause 独立，平台上两者触发顺序不应强依赖 |
| `OnApplicationQuit()` | 处理退出 | 已退出则返回；置 `IsQuitting = true`；安全触发 `Quitting` | 订阅者异常会被 `FrameLog.Exception` 捕获 |
| `OnShutdown()` | 关闭服务 | 清空三个事件委托 | 防止旧订阅者跨框架重启残留 |
| `Invoke(Action<bool> handler, bool value)` | 安全触发 bool 事件 | null 返回；try/catch 调用 handler；异常写 `FrameLog.Exception` | 一个订阅者异常不会阻断框架生命周期 |

## Events 模块

Events 是类型安全事件总线，用于模块之间、业务系统之间低耦合通信。

### 使用方式

```csharp
using System;
using Frame.Core;
using Frame.Events;

public struct PlayerLevelUp
{
    public int Level;
}

IEventBus events = Framework.Resolve<IEventBus>();
IDisposable sub = events.Subscribe<PlayerLevelUp>(e =>
{
    FrameLog.Info("Level up: " + e.Level);
}, owner: this);

events.Publish(new PlayerLevelUp { Level = 10 });
sub.Dispose();
```

按 owner 批量退订：

```csharp
events.UnsubscribeOwner(this);
```

### 设计和实现

`EventBus` 内部用 `Dictionary<Type, List<Subscription>>` 保存订阅。发布事件时先复制订阅列表快照，避免回调中订阅或退订导致集合修改异常。单个事件回调抛异常会被捕获，不影响其他订阅者。`once` 订阅在触发后自动移除。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IEventBus` | 事件总线接口 | `Subscribe`、`Publish`、`UnsubscribeOwner`、`Clear` |
| `EventBus` | 事件总线实现 | 类型到订阅列表的映射，支持 owner、once、异常隔离 |
| `EventSubscription` | 订阅句柄 | 实现 `IDisposable`，调用 `Dispose()` 会退订对应 id |
| `Subscription` | `EventBus` 私有订阅记录 | 保存 id、owner、handler、once、active，发布时用快照读取 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Events` 的实际源码文件展开。Events 模块只负责进程内同步事件分发，不做队列持久化、跨线程派发或网络消息。

#### `IEventBus.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Subscribe<TEvent>(Action<TEvent> handler, object owner = null, bool once = false)` | 订阅某个事件类型 | 实现类按 `typeof(TEvent)` 存储 handler，返回 `IDisposable` 句柄 | 建议把 MonoBehaviour 或系统对象作为 owner，便于批量退订 |
| `Publish<TEvent>(TEvent gameEvent)` | 发布事件 | 同步调用当前事件类型的订阅者 | 事件类型精确匹配，不会自动派发给基类或接口订阅 |
| `UnsubscribeOwner(object owner)` | 按 owner 批量退订 | 实现类用 `ReferenceEquals` 匹配 owner | 对象销毁时调用可避免事件持有旧对象 |
| `Clear()` | 清空全部订阅 | 清空内部字典 | 框架关闭或测试重置时使用 |

#### `EventSubscription.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `EventSubscription(EventBus owner, int id)` | 创建订阅句柄 | internal 构造，保存 EventBus 和订阅 id | 只由 `EventBus.Subscribe()` 返回 |
| `Dispose()` | 退订 | 如果 owner 已为空直接返回；否则置空 owner，并调用 `bus.Unsubscribe(id)` | Dispose 可重复调用，第二次不会有副作用 |

#### `EventBus.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-900` | 早于多数业务模块，便于其他模块初始化时解析事件总线 |
| `OnInitialize()` | 注册服务 | 注册 `IEventBus` 和自身实现 | 业务通过 `Framework.Resolve<IEventBus>()` 使用 |
| `Subscribe<TEvent>(Action<TEvent> handler, object owner = null, bool once = false)` | 添加订阅 | handler 为空抛异常；按事件类型取或建列表；分配递增 id；保存 `Subscription(id, owner, handler, once)`；返回 `EventSubscription` | once 为 true 时第一次收到事件后自动退订 |
| `Publish<TEvent>(TEvent gameEvent)` | 同步发布事件 | 找不到列表或列表空直接返回；`list.ToArray()` 建快照；跳过 inactive；把 `Delegate` 转为 `Action<TEvent>` 后调用；异常写日志；once 订阅调用 `Unsubscribe(id)` | 快照避免回调中订阅/退订修改列表导致枚举异常 |
| `Unsubscribe(int id)` | 按订阅 id 退订 | 遍历所有事件类型列表，从后往前找 id；找到后置 `Active=false` 并移除 | public 但主要由 `EventSubscription.Dispose()` 内部使用 |
| `UnsubscribeOwner(object owner)` | 按 owner 退订 | owner 为空返回；遍历所有列表，从后往前移除 `ReferenceEquals(list[i].Owner, owner)` 的订阅 | 从后往前删避免索引错乱 |
| `Clear()` | 清空全部订阅 | `subscriptions.Clear()` | 不重置 `nextId`，id 继续递增 |
| `OnShutdown()` | 关闭服务 | 调用 `Clear()` | 防止事件持有旧对象引用 |
| `Subscription` | 私有订阅记录 | 保存 `Id`、`Owner`、`Handler`、`Once`、`Active` | `Active` 用于快照发布期间标记已失效订阅 |
| `Subscription(int id, object owner, Delegate handler, bool once)` | 创建记录 | 保存参数并设置 `Active = true` | handler 以 `Delegate` 保存，发布时按泛型事件类型转换 |

## Time 模块

Time 模块提供不依赖 MonoBehaviour 的定时器服务。

### 使用方式

```csharp
using Frame.Core;
using Frame.Timing;

ITimerService timers = Framework.Resolve<ITimerService>();

TimerHandle handle = timers.Delay(2f, () =>
{
    FrameLog.Info("2 seconds later.");
});

timers.Repeat(1f, () => FrameLog.Info("tick"), repeatCount: 5, unscaled: true, owner: this);

handle.Cancel();
timers.CancelOwner(this);
```

### 设计和实现

`TimerService.Update()` 每帧遍历定时器字典的 key 快照，避免回调中取消定时器造成集合修改。定时器可以选择 scaled time 或 unscaled time。应用暂停时 `OnApplicationPause` 会把服务置为暂停状态，暂停时不推进计时。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ITimerService` | 定时器接口 | 延迟、重复、下一帧、取消、按 owner 取消 |
| `TimerService` | 定时器实现 | Update 驱动，支持 scaled/unscaled、暂停、回调异常隔离 |
| `TimerHandle` | 定时器句柄 | 保存 id，可查询有效性并取消 |
| `TimerTask` | `TimerService` 私有任务数据 | 保存剩余时间、间隔、重复次数、回调、unscaled 标记和 owner |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Time` 的实际源码文件展开。Time 模块用 `GameEntry.Update()` 驱动，不会创建额外 MonoBehaviour。

#### `ITimerService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `ActiveTimerCount` | 活跃定时器数量 | 实现类返回字典数量 | 运行时诊断面板显示 |
| `ScaledTimerCount` | 使用 scaled delta 的定时器数量 | 实现类遍历任务统计 `Unscaled == false` | 受 `Time.timeScale` 影响 |
| `UnscaledTimerCount` | 使用 unscaled delta 的定时器数量 | 实现类遍历任务统计 `Unscaled == true` | 不受 `Time.timeScale` 影响 |
| `IsPaused` | 服务是否暂停 | `OnApplicationPause()` 更新 | 暂停时所有定时器都不推进 |
| `Delay(float seconds, Action callback, bool unscaled = false, object owner = null)` | 延迟执行一次 | 实现类调度单次任务 | seconds 小于 0 会被归零 |
| `Repeat(float interval, Action callback, int repeatCount = -1, bool unscaled = false, object owner = null)` | 重复执行 | 实现类调度带 interval 的任务 | `repeatCount < 0` 表示无限重复 |
| `NextFrame(Action callback, object owner = null)` | 下一帧执行 | 实现类调度 delay 0、unscaled true 的单次任务 | 当前帧创建后，会在后续 Update 轮询中触发 |
| `Contains(int id)` | 查询 id 是否存在 | 实现类查字典 | `TimerHandle.IsValid` 使用 |
| `Cancel(int id)` | 按 id 取消 | 实现类从字典删除 | 成功返回 true |
| `CancelOwner(object owner)` | 按 owner 批量取消 | 实现类用引用相等匹配 owner | MonoBehaviour 销毁时推荐调用 |

#### `TimerHandle.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `TimerHandle(TimerService service, int id)` | 创建句柄 | internal 构造，保存服务和 id | 只由 `TimerService` 返回 |
| `Id` | 定时器 id | 返回只读字段 | 可用于日志或手动 `Cancel(id)` |
| `IsValid` | 定时器是否仍有效 | service 非空、id 大于 0、且 service.Contains(id) | 定时器触发完成或被取消后会变 false |
| `Cancel()` | 取消定时器 | service 非空时调用 `service.Cancel(id)` | 可重复调用，已取消时无副作用 |

#### `TimerService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-800` | 早于大多数业务模块初始化 |
| `ActiveTimerCount` | 活跃定时器数量 | 返回 `timers.Count` | 包括 scaled 和 unscaled |
| `ScaledTimerCount/UnscaledTimerCount` | 分类数量 | 调用 `CountTimers(false/true)` | 每次 getter 都遍历当前任务 |
| `IsPaused` | 暂停状态 | 返回私有字段 `paused` | pause 时 Update 直接返回 |
| `OnInitialize()` | 注册服务 | 注册 `ITimerService` 和自身实现 | 业务通过接口使用 |
| `Delay(...)` | 延迟一次 | 调用 `Schedule(seconds, 0f, 0, callback, unscaled, owner)` | `RepeatCount=0` 配合 `Interval=0` 表示触发后移除 |
| `Repeat(...)` | 重复定时 | 调用 `Schedule(interval, interval, repeatCount, callback, unscaled, owner)` | interval 小于 0 会归零，0 间隔会在下一次 Update 后移除或高频触发风险，需要谨慎 |
| `NextFrame(...)` | 下一帧回调 | 调用 `Schedule(0f, 0f, 0, callback, true, owner)` | 使用 unscaled delta，避免 timeScale 为 0 时永不触发 |
| `Contains(int id)` | 查询任务 | `timers.ContainsKey(id)` | 句柄有效性判断 |
| `Cancel(int id)` | 删除任务 | `timers.Remove(id)` | 如果在回调中取消其他任务，Update 快照能安全处理 |
| `CancelOwner(object owner)` | 按 owner 删除任务 | owner 为空返回；遍历字典收集匹配 key 到 `removeBuffer`；再统一删除 | 不在 foreach 中直接删，避免集合修改异常 |
| `Update(float deltaTime, float unscaledDeltaTime)` | 推进定时器 | paused 或无任务返回；复制 `timers.Keys` 到 `updateBuffer`；逐个读取任务；根据 `Unscaled` 选择 delta；剩余时间减 delta；到期后 try/catch 调 callback；可重复任务增加 completed 并 `Remaining += Interval`，否则加入删除缓冲；最后统一删除 | 使用 key 快照，允许回调中取消定时器；回调异常不会阻断其他 timer |
| `OnApplicationPause(bool paused)` | 响应应用暂停 | 保存暂停状态 | 暂停期间 scaled/unscaled timer 都停止 |
| `OnShutdown()` | 关闭服务 | 清空 timers、缓冲，重置 paused 和 nextId | 框架重启后 id 从 1 重新开始 |
| `Schedule(float delay, float interval, int repeatCount, Action callback, bool unscaled, object owner)` | 内部调度 | callback 为空抛异常；分配 id；创建 `TimerTask`，delay/interval 用 `Math.Max(0f, ...)`；返回 `TimerHandle` | 所有公开创建方法都走这里，保证参数处理一致 |
| `CountTimers(bool unscaled)` | 统计分类数量 | 遍历 `timers.Values`，比较 `timer.Unscaled` | 只用于诊断统计 |
| `TimerTask` | 私有任务数据 | 保存 Remaining、Interval、RepeatCount、CompletedCount、Callback、Unscaled、Owner | `CompletedCount + 1 < RepeatCount` 控制有限重复次数 |

## Preferences 模块

Preferences 是轻量用户偏好设置服务，基于 `PlayerPrefs`。

### 使用方式

```csharp
using Frame.Core;
using Frame.Preferences;

IPreferencesService prefs = Framework.Resolve<IPreferencesService>();
prefs.SetFloat("audio.music", 0.8f);
prefs.SetBool("tutorial.done", true);
prefs.SetJson("graphics", new GraphicsOptions { Quality = 2 });
prefs.Save();
```

### 设计和实现

`PreferencesService` 直接封装 `PlayerPrefs` 的 int、float、string，并用 Newtonsoft JSON 支持对象读写。每次写入会触发 `Changed(key)`，模块关闭时会调用 `PlayerPrefs.Save()`。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IPreferencesService` | 偏好设置接口 | 基础类型、bool、JSON、删除、保存、Changed 事件 |
| `PreferencesService` | 偏好设置实现 | 基于 `PlayerPrefs`，bool 用 int 保存，JSON 用 Newtonsoft |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Preferences` 的实际源码文件展开。Preferences 模块适合保存设置、教程状态、音量、输入绑定等小数据，不适合大体积存档。

#### `IPreferencesService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Changed` | key 变化事件 | 实现类写入或删除后触发 | UI 设置页可订阅它刷新显示 |
| `HasKey(string key)` | 判断 key 是否存在 | 实现类转发 `PlayerPrefs.HasKey` 并过滤空 key | 空白 key 直接 false |
| `GetInt/SetInt` | int 读写 | 基于 PlayerPrefs int | 写入后触发 Changed |
| `GetFloat/SetFloat` | float 读写 | 基于 PlayerPrefs float | 写入后触发 Changed |
| `GetString/SetString` | string 读写 | 基于 PlayerPrefs string | null 字符串会写为空字符串 |
| `GetBool/SetBool` | bool 读写 | bool 用 1/0 int 表示 | 与 PlayerPrefs 原生能力兼容 |
| `GetJson<TData>` | 读取 JSON 对象 | 实现类调用 `TryGetJson`，失败返回 fallback | 适合小型配置或设置对象 |
| `TryGetJson<TData>` | 尝试读取 JSON 对象 | JSON 为空或反序列化失败返回 false | 失败时输出 `FrameLog.Exception` |
| `SetJson<TData>` | 写入 JSON 对象 | Newtonsoft 序列化为字符串后写入 | 对象需要可被 Newtonsoft 正常序列化 |
| `DeleteKey(string key)` | 删除 key | key 无效或不存在返回 false | 删除成功触发 Changed |
| `Save()` | 强制落盘 | 调用 PlayerPrefs.Save | 框架关闭时也会自动调用 |

#### `PreferencesService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-850` | 早于多数业务模块 |
| `OnInitialize()` | 注册服务 | 注册 `IPreferencesService` 和自身实现 | 业务通过接口使用 |
| `HasKey(string key)` | 查询 key | `!string.IsNullOrWhiteSpace(key) && PlayerPrefs.HasKey(key)` | 空 key 不抛异常，直接 false |
| `GetInt(string key, int fallback = 0)` | 读取 int | key 存在则 `PlayerPrefs.GetInt(key, fallback)`，否则 fallback | 读取不会触发 Changed |
| `SetInt(string key, int value)` | 写 int | `ValidateKey` 后 `PlayerPrefs.SetInt`，再 `RaiseChanged(key)` | 不自动 Save，Unity 会延迟落盘；可手动调用 Save |
| `GetFloat/SetFloat` | 读写 float | 与 int 同样流程，调用 `PlayerPrefs.GetFloat/SetFloat` | 写入触发 Changed |
| `GetString/SetString` | 读写 string | 读取时 fallback；写入时 null 转空字符串 | 写入触发 Changed |
| `GetBool(string key, bool fallback = false)` | 读取 bool | 调 `GetInt(key, fallback ? 1 : 0) != 0` | 任何非 0 都视为 true |
| `SetBool(string key, bool value)` | 写 bool | 调 `SetInt(key, value ? 1 : 0)` | Changed 事件由 SetInt 触发 |
| `GetJson<TData>(string key, TData fallback = default)` | 读取 JSON 或 fallback | 调 `TryGetJson`，成功返回 value，否则 fallback | 失败不会抛给调用方 |
| `TryGetJson<TData>(string key, out TData value)` | 尝试反序列化 | 先 `GetString(key, null)`；空白返回 false；用 `JsonConvert.DeserializeObject<TData>(json)`；结果非 null 才 true；异常记录并返回 false | 值类型反序列化会得到装箱后的默认值，不为 null 时返回 true |
| `SetJson<TData>(string key, TData value)` | 序列化并写入 | `ValidateKey` 后 `JsonConvert.SerializeObject(value)`，写入 string 并触发 Changed | 不做 schema 校验 |
| `DeleteKey(string key)` | 删除 key | 空白或不存在返回 false；存在则 `PlayerPrefs.DeleteKey`，触发 Changed，返回 true | 删除后仍需 Save 才能立即落盘 |
| `Save()` | 保存偏好 | 调 `PlayerPrefs.Save()` | 频繁调用可能有 I/O 成本，通常在设置确认或退出时调用 |
| `OnShutdown()` | 关闭服务 | 调 `Save()`，清空 Changed | 确保框架关闭时尽量落盘 |
| `RaiseChanged(string key)` | 安全触发变更事件 | 取委托快照；null 返回；try/catch 调用；异常写 `FrameLog.Exception` | 一个监听者异常不会阻断其他流程 |
| `ValidateKey(string key)` | 校验 key | 空白则抛 `ArgumentException("Preference key is required.", "key")` | 所有写操作都校验 key，读操作更宽松 |

## Pooling 模块

Pooling 提供 C# 对象池和 GameObject 池，减少频繁创建销毁。

### 使用方式

GameObject 池：

```csharp
using Frame.Core;
using Frame.Pooling;
using UnityEngine;

IPoolService pools = Framework.Resolve<IPoolService>();
pools.CreateGameObjectPool("Bullet", bulletPrefab, maxSize: 128, prewarm: 32);

GameObject bullet = pools.Spawn("Bullet");
pools.Despawn("Bullet", bullet);
```

C# 对象池：

```csharp
ObjectPool<List<int>> pool = new ObjectPool<List<int>>(
    factory: () => new List<int>(),
    onRelease: list => list.Clear(),
    maxSize: 32);

List<int> temp = pool.Get();
pool.Release(temp);
```

### 设计和实现

`PoolService` 在框架根节点下创建 `Pools` 节点，每个 GameObject 池创建一个子节点保存 inactive 实例。`GameObjectPool` 使用 `Stack<GameObject>` 存放 inactive 对象，使用 `HashSet<GameObject>` 防止重复回收。对象激活时调用所有子组件的 `IPoolable.OnSpawned()`，回收时调用 `OnDespawned()`。

`ObjectPool<T>` 面向普通 C# 对象，使用 factory 创建对象，支持 get/release/destroy 回调。如果对象实现 `IResettablePoolItem`，释放时会自动调用 `ResetForPool()`。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IPoolService` | GameObject 池服务接口 | 创建、获取、生成、回收、统计、清理 |
| `PoolService` | GameObject 池服务实现 | 管理多个命名 `GameObjectPool`，创建 `Pools` 根节点 |
| `GameObjectPool` | 单个 prefab 的对象池 | inactive 栈、重复回收保护、预热、统计 |
| `ObjectPool<T>` | 普通 C# 对象池 | factory、回调、最大容量、统计、清理 |
| `IPoolable` | GameObject 池生命周期接口 | `OnSpawned()` 和 `OnDespawned()` |
| `IResettablePoolItem` | C# 对象池重置接口 | `ResetForPool()` 在释放时调用 |
| `PoolStats` | 池统计数据 | key、容量、active、inactive、created、destroyed、get、release |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Pooling` 的实际源码文件展开。Pooling 分两层：`PoolService` 管理多个 GameObject 池，`ObjectPool<T>` 作为通用 C# 对象池可被业务单独使用。

#### `IPoolService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `CreateGameObjectPool(string key, GameObject prefab, int maxSize = -1, int prewarm = 0)` | 创建或获取命名 GameObject 池 | 实现类按 key 缓存 `GameObjectPool` | key 为空时实现会使用 prefab 名称或 `"Pool"` |
| `TryGetGameObjectPool(string key, out GameObjectPool pool)` | 查询池 | 实现类查字典 | 需要直接操作池对象时使用 |
| `Spawn(string key, Transform parent = null)` | 从池中取对象 | 实现类找到池后调用 `pool.Get(parent)` | key 未注册会抛 `FrameException` |
| `Despawn(string key, GameObject instance)` | 回收对象 | 实现类找到池则 Release，否则销毁 instance | key 写错会导致对象被销毁，业务应保持 key 常量化 |
| `GetGameObjectPoolStats(string key)` | 查询单池统计 | 实现类调用 `pool.GetStats(key)` | 不存在时返回 null |
| `GetAllGameObjectPoolStats()` | 查询全部池统计 | 实现类遍历池字典生成列表 | 运行时诊断面板使用 |
| `ClearGameObjectPool(string key)` | 清空单池 inactive 对象 | 实现类调用 `pool.Clear()` | active 对象不会被池主动销毁 |
| `ClearAllGameObjectPools()` | 清空全部池 inactive 对象 | 实现类遍历所有池 Clear | 框架关闭时调用 |

#### `IPoolable.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `OnSpawned()` | 对象从池中取出时回调 | `GameObjectPool.Get()` 查找所有子级 `IPoolable` 后调用 | 适合重置粒子、血量、碰撞器、计时器 |
| `OnDespawned()` | 对象回池前回调 | `GameObjectPool.Release()` 查找所有子级 `IPoolable` 后调用 | 适合停止特效、取消订阅、清理临时状态 |

#### `IResettablePoolItem.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `ResetForPool()` | C# 对象回池前重置 | `ObjectPool<T>.Release()` 中检测 `item as IResettablePoolItem` 后调用 | 适合 List 包装对象、临时数据对象清空字段 |

#### `PoolStats.cs`

`PoolStats` 是可序列化统计数据，没有方法。`GameObjectPool.GetStats()` 和 `ObjectPool<T>.GetStats()` 都返回它。

| 字段 | 含义 |
| --- | --- |
| `Key` | 池名称或调用方传入的统计 key |
| `MaxSize` | inactive 最大缓存数量 |
| `CountActive` | 当前已取出未回收数量 |
| `CountInactive` | 当前回池缓存数量 |
| `CountTotal` | active + inactive |
| `CreatedCount` | 累计创建对象数量 |
| `DestroyedCount` | 累计销毁对象数量 |
| `GetCount` | 累计取出次数 |
| `ReleaseCount` | 累计回收次数 |

#### `GameObjectPool.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `GameObjectPool(GameObject prefab, Transform parent, int maxSize)` | 创建单 prefab 池 | 保存 prefab、inactive 父节点；`maxSize` 用 `Mathf.Max(1, maxSize)` 兜底 | 一般由 `PoolService` 创建 |
| `CountInactive` | 当前缓存数量 | 返回 `inactive.Count` | 不包含 active |
| `CountActive` | 当前活跃数量 | 返回 `countActive` | 每次 Get 加一，Release 时最多减到 0 |
| `Get(Transform newParent = null)` | 获取实例 | inactive 有对象则 Pop，否则 Instantiate prefab 并增加 created；移出 `inPool`；增加 active/get 计数；设置 parent；激活对象；调用所有子级 `IPoolable.OnSpawned()` | 传 newParent 可把对象挂到业务节点下；不传则挂到池 parent |
| `Release(GameObject instance)` | 回收实例 | null 返回；如果 `inPool.Contains(instance)` 说明重复回收，直接返回；减少 active；调用 `OnDespawned()`；inactive 满则 Destroy 并增加 destroyed；否则 SetActive false、挂回 parent、压栈并加入 inPool | `inPool` 是重复回收保护核心 |
| `Prewarm(int count)` | 预创建 inactive 对象 | 循环 Instantiate prefab 到 parent，关闭激活，压入 inactive，加入 inPool，增加 created | 适合关卡开始前预热子弹、特效等 |
| `Clear()` | 清空 inactive 对象 | 不断 Pop inactive，非 null 则 Destroy 并增加 destroyed；最后清空 inPool | 不会处理已取出的 active 对象 |
| `GetStats(string key = null)` | 获取统计 | new `PoolStats` 填充 key、max、active/inactive、累计计数 | 诊断面板和性能调试使用 |

#### `ObjectPool.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `ObjectPool(Func<T> factory, Action<T> onGet = null, Action<T> onRelease = null, Action<T> onDestroy = null, int maxSize = 128)` | 创建 C# 对象池 | factory 为空抛异常；保存回调；`MaxSize = Math.Max(1, maxSize)` | T 必须是 class；适合非 UnityEngine.Object 的临时对象 |
| `MaxSize` | inactive 最大缓存数 | 构造时私有 set | 超过后释放对象时走 destroy 流程 |
| `CountInactive/CountActive` | 当前数量 | inactive 栈数量和 active 计数 | 统计用途 |
| `Get()` | 获取对象 | inactive 有对象则 Pop 并从 inPool 移除，否则调用 factory 并增加 created；增加 active/get；触发 `onGet` | factory 应返回非 null 对象，否则后续逻辑可能失真 |
| `Release(T item)` | 回收对象 | null 或已在 inPool 中直接返回；若实现 `IResettablePoolItem` 则先 `ResetForPool()`；减少 active；增加 release；触发 `onRelease`；inactive 满则触发 `onDestroy` 并增加 destroyed，否则压栈并加入 inPool | 同样用 `inPool` 防重复回收 |
| `Prewarm(int count)` | 预热对象 | 循环 factory 创建并增加 created，然后调用 `Release(item)` 放回池 | 因为走 Release，会触发 reset 和 onRelease |
| `GetStats(string key = null)` | 获取统计 | new `PoolStats` 填充当前和累计计数 | 与 GameObjectPool 统计结构一致 |
| `Clear()` | 清空 inactive 对象 | 如果有 onDestroy，逐个 Pop 并调用；否则只增加 destroyed 计数；最后清空 inactive 和 inPool | 不会处理 active 对象 |

#### `PoolService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-700` | 作为基础服务早期可用 |
| `OnInitialize()` | 初始化池根节点和服务 | 创建 `"Pools"` GameObject，挂到 `Context.Root` 下，保存 transform，注册 `IPoolService` 和自身实现 | 所有 GameObject 池的 inactive 节点会集中在这里 |
| `CreateGameObjectPool(...)` | 创建或返回现有池 | key 为空时用 prefab.name 或 `"Pool"`；prefab 为空抛 `FrameException`；已有 key 返回现有池；否则创建 key 子节点；maxSize 小于等于 0 时使用 `Context.Settings.DefaultGameObjectPoolMaxSize`；创建 `GameObjectPool` 并可预热 | 同 key 重复创建不会替换 prefab，会返回第一次创建的池 |
| `TryGetGameObjectPool(...)` | 查询池 | `gameObjectPools.TryGetValue` | 直接访问池对象时使用 |
| `Spawn(string key, Transform parent = null)` | 生成对象 | 找不到 key 抛 `FrameException`；找到后 `pool.Get(parent)` | 业务应该先创建池再 Spawn |
| `Despawn(string key, GameObject instance)` | 回收对象 | 找到池则 `pool.Release(instance)`；找不到且 instance 非空则 `Object.Destroy(instance)` | 未注册 key 的对象不会泄漏，会直接销毁 |
| `GetGameObjectPoolStats(string key)` | 单池统计 | 找到池返回 `pool.GetStats(key)`，否则 null | 可用于调试指定池 |
| `GetAllGameObjectPoolStats()` | 全部统计 | 遍历字典，调用每个池的 `GetStats(pair.Key)` | 诊断面板使用 |
| `ClearGameObjectPool(string key)` | 清空某池 inactive | 找到池则 `pool.Clear()` | 不删除池节点和字典记录 |
| `ClearAllGameObjectPools()` | 清空所有池 inactive | 遍历所有池 Clear | 不删除字典记录 |
| `OnShutdown()` | 关闭池服务 | 清空所有池；清空字典；销毁 poolRoot GameObject；置空 root | active 对象如果还在业务层，池不会单独追踪销毁 |

## Assets 模块

Assets 模块通过 `IAssetService` 统一提供同步加载、异步加载、实例化、缓存和引用计数。默认后端是 Unity `Resources`，也提供 Addressables 和 YooAsset 集成实现。

### 使用方式

同步加载：

```csharp
using Frame.Assets;
using Frame.Core;
using UnityEngine;

IAssetService assets = Framework.Resolve<IAssetService>();
using (AssetHandle<GameObject> handle = assets.Load<GameObject>("UI/MainMenu"))
{
    if (handle.IsValid)
    {
        GameObject prefab = handle.Asset;
    }
}
```

异步加载：

```csharp
AssetRequest<TextAsset> request = assets.LoadAsync<TextAsset>("Configs/items", handle =>
{
    if (handle.IsValid)
    {
        FrameLog.Info(handle.Asset.text);
        handle.Release();
    }
});

yield return request;
```

使用资源引用：

```csharp
AssetReference<GameObject> reference = new AssetReference<GameObject>("UI/Shop");
AssetRequest<GameObject> load = reference.LoadAsync(assets);
```

### 设计和实现

`ResourcesAssetService` 把路径先交给 `FramePathUtility.NormalizeResourcesPath()` 归一化，避免带扩展名、反斜杠、`Resources/` 前缀造成重复缓存。加载成功后把资源放入 `cache`，并在 `refCounts` 中增加引用计数。`AssetHandle<T>.Release()` 会回调服务的 `Release(path)`，引用计数归零时移除缓存。

`AddressablesAssetService` 使用 Addressables address 作为缓存 key。加载成功后保存 Addressables `AsyncOperationHandle`，引用计数归零时调用 `Addressables.Release(handle)`。

`YooAssetAssetService` 使用 YooAsset location 作为缓存 key。初始化 `FrameSettings.YooAssetPackageName` 指定的 package 后保存 YooAsset `AssetHandle`，引用计数归零时调用 YooAsset handle `Release()`。

异步加载会把底层加载进度写入 `AssetRequest<T>.Progress`。请求支持取消，取消后会以失败请求完成，并释放已经创建的底层加载句柄。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IAssetService` | 资源服务接口 | 加载、异步加载、实例化、引用计数、统计、释放 |
| `ResourcesAssetService` | Resources 实现 | 路径归一化、缓存、引用计数、异步请求、卸载未使用资源 |
| `AddressablesAssetService` | Addressables 实现 | address 缓存、Addressables handle 引用计数、异步进度和取消 |
| `YooAssetAssetService` | YooAsset 实现 | package 初始化、location 缓存、YooAsset handle 引用计数、Host/Web 远端 URL |
| `AssetHandle<T>` | 资源句柄 | 保存资源、路径和 owner，`Dispose/Release` 减引用 |
| `AssetRequest<T>` | 异步资源请求 | 可 `yield return`，有完成、取消、进度、错误、句柄 |
| `AssetReference<T>` | 可序列化资源引用值 | 封装 Resources 路径，提供 Load/LoadAsync |
| `AssetStats` | 资源统计数据 | 路径、类型名、引用计数、是否已加载 |
| `AssetInstanceLease` | 实例资源租约 | 挂到实例 GameObject 上，销毁时释放加载句柄 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Assets` 和 `Assets/Frame/Integrations/*` 的实际源码文件展开。核心接口在 Runtime 程序集内，Addressables/YooAsset 作为独立 integration 程序集安装。

#### `IAssetService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Load<T>(string path)` | 同步加载资源 | 实现类返回 `AssetHandle<T>` | 使用完应 `Release()` 或 `Dispose()` |
| `TryLoad<T>(string path, out AssetHandle<T> handle)` | 静默尝试加载资源 | 实现类成功时返回有效 handle，失败时返回 false | 适合 Config 等 fallback 链路；普通业务缺失资源需要告警时用 `Load<T>` |
| `LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null)` | 异步加载资源 | 实现类返回 `AssetRequest<T>`，可 yield 等待 | completed 异常会被框架日志捕获 |
| `Instantiate(string path, Transform parent = null, bool worldPositionStays = false)` | 加载并实例化 prefab | 实现类加载 GameObject 后 Instantiate，并给实例绑定 `AssetInstanceLease` | 返回的是实例，不是资源句柄；实例销毁时释放资源引用 |
| `IsLoaded(string path)` | 查询资源是否在缓存 | 实现类查缓存字典 | path 会先规范化 |
| `GetReferenceCount(string path)` | 查询引用计数 | 实现类查引用计数字典 | 只统计通过服务 Load 成功返回的 handle |
| `GetLoadedAssetStats()` | 获取当前缓存资源统计 | 实现类遍历缓存生成列表 | 诊断面板使用 |
| `Release(string path)` | 释放一次引用 | 实现类引用计数减一，归零后移除缓存 | 一般由 `AssetHandle.Dispose()` 调用 |
| `ReleaseAll()` | 清空全部缓存和引用计数 | 实现类清空字典 | 不会主动销毁已实例化 GameObject |
| `UnloadUnusedAssets()` | 请求 Unity 卸载未使用资源 | 实现类调用 `Resources.UnloadUnusedAssets()` | Unity 会异步处理，成本较高，不适合高频调用 |

#### `AssetHandle.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `AssetHandle(IAssetService owner, string path, T asset)` | 创建资源句柄 | internal 构造，保存 owner、Path、Asset | 只由资源服务创建 |
| `Path` | 资源路径 | 构造时设置，私有 set | Release 时用它回传给服务 |
| `Asset` | 实际资源对象 | 构造时设置，私有 set | 业务读取资源 |
| `IsValid` | 句柄是否有效 | 判断 `Asset != null` | 加载失败时 false |
| `Release()` | 释放句柄 | 调用 `Dispose()` | 语义化别名 |
| `Dispose()` | 释放一次资源引用 | owner 为空、Asset 为空或 Path 空白时只清空 owner；否则置空 owner 并调用 `service.Release(Path)` | 防重复释放：第一次释放后 owner 为 null |

#### `AssetReference.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `AssetReference(string resourcesPath)` | 创建引用 | 构造时用 `FramePathUtility.NormalizeResourcesPath` 保存路径 | 可在代码中创建，也可在 Inspector 序列化字段中填写 |
| `ResourcesPath` | 规范化后的 Resources 路径 | getter 每次再次规范化字段 | 支持用户填写 `Assets/.../Resources/...` 或带扩展名路径 |
| `IsValid` | 路径是否有效 | 判断规范化路径非空白 | 加载前可先判断 |
| `Load(IAssetService assetService)` | 同步加载 | 调用 `assetService.Load<T>(ResourcesPath)` | assetService 不做 null 检查，调用方应传有效服务 |
| `LoadAsync(IAssetService assetService, Action<AssetHandle<T>> completed = null)` | 异步加载 | 调用 `assetService.LoadAsync<T>(ResourcesPath, completed)` | 与服务接口返回相同 request |
| `ToString()` | 转文本 | 返回 `ResourcesPath` | 便于日志输出 |

#### `AssetRequest.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `keepWaiting` | Unity coroutine 等待条件 | override 返回 `!IsDone` | 可以 `yield return request` |
| `IsDone` | 请求是否完成 | `Complete()` 时置 true | 取消后也要等异步流程调用 Complete 才完成 |
| `IsCanceled` | 是否已请求取消 | `Cancel()` 置 true | 取消是协作式，底层 `Resources.LoadAsync` 不会真正 Abort |
| `Success` | 是否成功 | `Handle != null && Handle.IsValid` | 取消、失败、类型不匹配都 false |
| `Progress` | 进度 | `SetProgress()` 用 `Mathf.Clamp01` 设置，`Complete()` 置 1 | 来自 `ResourceRequest.progress` |
| `Error` | 错误信息 | `Complete(handle, error)` 设置 | 成功时通常为 null |
| `Handle` | 完成后的资源句柄 | `Complete()` 设置 | 成功后业务应释放 |
| `Asset` | 快捷访问资源 | `Handle == null ? null : Handle.Asset` | 不替代释放 handle |
| `Cancel()` | 请求取消 | 已完成直接返回，否则置 `IsCanceled = true` | 异步任务会在 yield 点检查并以 `"Request canceled."` 完成 |
| `SetProgress(float progress)` | 内部更新进度 | clamp 到 0 到 1 | 只由资源服务调用 |
| `Complete(AssetHandle<T> handle, string error = null)` | 内部完成请求 | 保存 handle/error，进度置 1，`IsDone = true` | 只由资源服务调用 |

#### `AssetStats.cs`

| 字段 | 含义 |
| --- | --- |
| `Path` | Resources 路径 |
| `TypeName` | 资源运行时类型名 |
| `ReferenceCount` | 当前引用计数 |
| `IsLoaded` | 当前是否加载 |

#### `AssetServiceBackend.cs`

| 枚举值 | 含义 |
| --- | --- |
| `Resources` | 使用内置 `ResourcesAssetService`，不需要额外包 |
| `Addressables` | 使用 `Frame.Addressables` 集成程序集里的 `AddressablesAssetService` |
| `YooAsset` | 使用 `Frame.YooAsset` 集成程序集里的 `YooAssetAssetService` |

#### `YooAssetPlayMode.cs`

| 枚举值 | 含义 |
| --- | --- |
| `EditorSimulate` | 编辑器模拟模式，依赖 YooAsset 编辑器构建出的模拟数据 |
| `Offline` | 离线模式，只使用首包内置资源 |
| `Host` | 联机模式，内置资源 + 远端资源 + 沙盒缓存 |
| `Web` | WebGL 模式，使用 WebServer/WebNetwork 文件系统 |

#### `AssetInstanceLease.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Bind(IDisposable disposable)` | 绑定资源句柄 | 保存新 disposable；如果已有旧 lease，先释放旧 lease | 资源服务实例化 prefab 后调用 |
| `OnDestroy()` | 实例销毁时释放资源引用 | 取出 lease，置空，再 `Dispose()` | 防止 Addressables/YooAsset 依赖在实例存活时被提前释放 |

#### `ResourcesAssetService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-600` | 早于 Scene/UI/Audio 等可能加载资源的模块 |
| `OnInitialize()` | 注册服务 | 注册 `IAssetService` 和自身实现 | 默认资源服务入口 |
| `Load<T>(string path)` | 同步加载 | 规范化 path；空路径写 warning 并返回空 handle；缓存 miss 时 `Resources.Load<T>`；找不到写 warning；缓存命中但类型不匹配写 warning；成功 `AddRef(path)` 并返回有效 handle | 缓存 key 只按 path，不按类型，因此同一路径用错类型会得到类型不匹配 |
| `TryLoad<T>(string path, out AssetHandle<T> handle)` | 静默同步尝试加载 | 复用 `TryLoadInternal(path, false, out handle)`；成功时同样 AddRef；失败时返回 false | 用于 provider/fallback 链路，避免 TryLoad 缺失资源时刷 warning |
| `LoadAsync<T>(string path, Action<AssetHandle<T>> completed = null)` | 异步加载 | 规范化 path 并创建 request；缓存命中则立即类型检查、AddRef、Complete；缓存未命中则启动 `LoadAsyncTask(...).Forget()` | 缓存命中时 request 会同步完成 |
| `Instantiate(string path, Transform parent = null, bool worldPositionStays = false)` | 加载并实例化 GameObject | `Load<GameObject>`；无效返回 null；成功 Instantiate；给实例绑定 `AssetInstanceLease` | 实例销毁时自动释放加载 handle |
| `IsLoaded(string path)` | 查询缓存 | 规范化 path；缓存存在且对象非 null 才 true | cache 里 null 会视为未加载 |
| `GetReferenceCount(string path)` | 查询引用计数 | 规范化 path；查不到返回 0 | 释放到 0 后 refCounts 会移除 |
| `GetLoadedAssetStats()` | 获取缓存统计 | 遍历 cache；跳过 null；读取 refCounts；创建 `AssetStats`；按 Path 排序 | 诊断面板显示当前缓存资源 |
| `Release(string path)` | 释放一次引用 | 规范化 path；没有 ref count 返回；count 减一；小于等于 0 时移除 refCounts 和 cache，否则写回 count | 移除 cache 不等于立刻卸载资源，真正卸载由 Unity 管理 |
| `ReleaseAll()` | 清空引用和缓存 | `refCounts.Clear()`、`cache.Clear()` | 适合切换大场景前手动清空 |
| `UnloadUnusedAssets()` | 请求 Unity 卸载未使用资源 | 调 `Resources.UnloadUnusedAssets()` | 可能产生卡顿，应选择加载间隙调用 |
| `OnShutdown()` | 关闭服务 | 清空 cache/refCounts，并调用 `Resources.UnloadUnusedAssets()` | 框架退出时释放资源服务持有的引用 |
| `LoadAsyncTask<T>(...)` | 异步加载流程 | 空 path 直接 Complete error；先 `UniTask.Yield(Update)` 给调用方取消机会；取消则返回空 handle；创建 `Resources.LoadAsync<T>`；循环更新 progress 并检查取消；完成后再次检查取消；asset 为空写 warning；成功写 cache 并 AddRef；最后 CompleteRequest | 使用 `UniTaskVoid` fire-and-forget，异常路径主要通过内部检查和日志处理 |
| `CompleteRequest<T>(...)` | 完成请求并回调 | 调 `request.Complete(handle, error)`；completed 非空时 try/catch 调用 | completed 抛异常不会中断异步任务 |
| `AddRef(string path)` | 增加引用计数 | 读取当前 count，写回 `count + 1` | 只在成功加载或缓存命中类型正确时调用 |

#### `AddressablesAssetService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-600` | 与默认资源服务一致 |
| `OnInitialize()` | 注册服务并初始化 Addressables | 注册 `IAssetService` 和自身实现；调用 `Addressables.InitializeAsync().WaitForCompletion()` | 使用前应先在 Addressables 窗口配置 address/group/profile |
| `Load<T>(string path)` | 同步加载 | path 只做 trim 和反斜杠替换；缓存命中 AddRef；缓存 miss 时 `Addressables.LoadAssetAsync<T>(path).WaitForCompletion()` | path 是 Addressables address，不是 Resources 路径 |
| `LoadAsync<T>(...)` | 异步加载 | 创建 Addressables handle，循环读取 `PercentComplete`，取消时 `Addressables.Release(handle)` | 完成后返回框架 `AssetHandle<T>` |
| `Instantiate(...)` | 实例化 prefab | `Load<GameObject>` 后 Unity Instantiate，并绑定 `AssetInstanceLease` | 实例销毁才释放资源引用 |
| `Release(string path)` | 释放一次引用 | 引用计数归零时调用 `Addressables.Release` 并移除缓存 | 不要绕过框架直接释放同一 handle |
| `ReleaseAll()` | 释放全部缓存 | 遍历缓存并 `Addressables.Release` | 适合切场景前清理 |
| `UnloadUnusedAssets()` | 请求 Unity 卸载未使用资源 | 调 `Resources.UnloadUnusedAssets()` | Addressables 的核心释放动作是 Release handle |

#### `YooAssetAssetService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-600` | 与默认资源服务一致 |
| `OnInitialize()` | 注册服务并初始化 YooAsset package | 如果 `YooAssets` 未初始化则调用 `YooAssets.Initialize()`；创建或获取 `FrameSettings.YooAssetPackageName`；按 `YooAssetPlayMode` 创建初始化参数并 `WaitForCompletion()` | 只负责 package 初始化，不负责完整补丁流程 |
| `Load<T>(string path)` | 同步加载 | path 只做 trim 和反斜杠替换；缓存命中 AddRef；缓存 miss 时 `package.LoadAssetSync<T>(path)` | path 是 YooAsset location |
| `LoadAsync<T>(...)` | 异步加载 | `package.LoadAssetAsync<T>(path)`；循环读取 `Progress`；取消时释放 YooAsset handle | 成功后保存 YooAsset handle 到缓存 |
| `Instantiate(...)` | 实例化 prefab | `Load<GameObject>` 后 Unity Instantiate，并绑定 `AssetInstanceLease` | 实例销毁才释放资源引用 |
| `Release(string path)` | 释放一次引用 | 引用计数归零时调用 YooAsset handle `Release()` | 配合 YooAsset 的包/Bundle 引用计数 |
| `ReleaseAll()` | 释放全部缓存 | 遍历缓存释放 YooAsset handle | 切场景前可显式调用 |
| `UnloadUnusedAssets()` | 回收未使用资源 | `package.UnloadUnusedAssetsAsync().WaitForCompletion()`，随后调用 `Resources.UnloadUnusedAssets()` | 不适合高频调用 |
| `OnShutdown()` | 关闭服务 | 释放缓存；如果 package/YooAssets 由服务创建，则销毁 package 和 YooAssets | 避免销毁外部已经存在的 package |
| `YooAssetRemoteService` | Host/Web 远端 URL 服务 | 使用 `YooAssetDefaultHostServer` 和 `YooAssetFallbackHostServer` 拼接文件 URL | 完整下载器和版本更新流程应由项目启动流程处理 |

## Scenes 模块

Scenes 模块封装 Unity `SceneManager`，提供同步加载、异步加载、Build Settings 校验、进度事件和手动激活。

### 使用方式

同步加载：

    Progress = p => FrameLog.Info("Loading: " + p)
});

yield return new WaitUntil(() => op.IsReadyToActivate);
op.Activate();
```

### 设计和实现

`SceneService.LoadAsync()` 会先校验场景名和 Build Settings。默认不允许并发加载，除非 `SceneLoadArgs.AllowConcurrentLoads` 为 true。服务把 Unity 的 `AsyncOperation` 包装成 `SceneLoadOperation`，并用 UniTask 每帧追踪进度、发布事件、调用回调。手动激活时，`AsyncOperation.allowSceneActivation` 保持 false，`SceneLoadOperation.IsReadyToActivate` 在进度到达 0.9 且未完成时为 true。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ISceneService` | 场景服务接口 | 加载、异步加载、卸载、状态查询、设置 active scene |
| `SceneService` | 场景服务实现 | Build Settings 校验、并发控制、事件发布、进度追踪 |
| `SceneLoadArgs` | 场景加载参数 | 场景名、模式、是否自动激活、是否允许并发、进度和完成回调 |
| `SceneLoadOperation` | 异步加载句柄 | 可 `yield return`，暴露进度、归一化进度、激活状态、LoadedScene |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Scenes` 的实际源码文件展开。Scenes 模块把 Unity `SceneManager` 的同步/异步 API 包装成统一服务，并为加载进度、完成事件和手动激活提供稳定入口。

#### `ISceneService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `LoadStarted` | 异步加载开始事件 | `SceneService.LoadAsync()` 创建 operation 后发布 | 适合显示 loading UI |
| `LoadProgress` | 异步加载进度事件 | `TrackLoadAsync()` 每帧发布 normalized progress | progress 已按 0 到 1 归一化 |
| `LoadCompleted` | 异步加载完成事件 | `TrackLoadAsync()` 完成后发布 | 发生在 `args.Completed` 之后 |
| `ActiveScene` | 当前 active scene | 实现类返回 `SceneManager.GetActiveScene()` | 用于诊断和业务查询 |
| `IsLoading` | 当前是否有加载操作 | 实现类清理已完成 operation 后判断列表数量 | 默认不允许并发加载时用于保护 |
| `CurrentOperation` | 当前加载操作 | 实现类保存最近 operation | 并发加载时代表最后一个仍活跃的 operation |
| `Load(string sceneName, LoadSceneMode mode = Single, bool validateInBuildSettings = true)` | 同步加载场景 | 实现类校验后调用 `SceneManager.LoadScene` | 默认校验 Build Settings |
| `LoadAsync(SceneLoadArgs args)` | 异步加载场景 | 实现类包装 Unity `AsyncOperation` | 返回 `SceneLoadOperation`，可 yield 等待或手动激活 |
| `UnloadAsync(string sceneName)` | 异步卸载场景 | 实现类确认已加载后调用 `SceneManager.UnloadSceneAsync` | sceneName 为空或未加载返回 null |
| `IsSceneLoaded(string sceneName)` | 查询场景是否已加载 | 实现类遍历 `SceneManager.sceneCount` | 支持按名称或路径匹配 |
| `IsSceneInBuildSettings(string sceneName)` | 查询是否在 Build Settings | 实现类遍历 build index 路径 | 支持按路径或文件名匹配 |
| `SetActiveScene(string sceneName)` | 设置 active scene | 实现类遍历已加载场景并调用 `SceneManager.SetActiveScene` | 未加载或无效返回 false |

#### `SceneLoadArgs.cs`

`SceneLoadArgs` 是异步加载参数对象，没有方法。它用字段而不是属性，便于 Inspector/序列化和对象初始化。

| 字段 | 作用 | 默认值和注意点 |
| --- | --- | --- |
| `SceneName` | 要加载的场景名或路径 | 必填，空白会抛 `FrameException` |
| `Mode` | 加载模式 | 默认 `LoadSceneMode.Single` |
| `ActivateOnLoad` | 是否加载完成后自动激活 | 默认 true；false 时 Unity 进度会停在 0.9，需调用 `SceneLoadOperation.Activate()` |
| `AllowConcurrentLoads` | 是否允许并发加载 | 默认 false；false 时已有加载会抛异常 |
| `ValidateInBuildSettings` | 是否校验 Build Settings | 默认 true；动态地址或特殊流程可关闭 |
| `SetActiveOnComplete` | 完成后是否设为 active scene | 默认 false；对 Additive 加载常用 |
| `Progress` | 单次请求进度回调 | `SceneService.NotifyProgress()` 安全调用 |
| `Completed` | 单次请求完成回调 | `SceneService.NotifyCompleted()` 安全调用，参数是 loaded scene |

#### `SceneLoadOperation.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `SceneLoadOperation(string sceneName, AsyncOperation operation)` | 包装 Unity operation | 保存 sceneName 和 AsyncOperation | 只由 `SceneService` 创建 |
| `SceneName` | 目标场景名 | 返回构造参数 | 日志和诊断使用 |
| `keepWaiting` | Coroutine 等待条件 | operation 非空且未 done 时 true | 可 `yield return operation` |
| `Progress` | Unity 原始进度 | operation 为空返回 1，否则返回 `operation.progress` | 手动激活时通常停在 0.9 |
| `NormalizedProgress` | 归一化进度 | done 或 operation 空返回 1，否则 `Mathf.Clamp01(operation.progress / 0.9f)` | 更适合 UI 进度条 |
| `IsDone` | 是否完成 | operation 为空或 `operation.isDone` | 用于轮询 |
| `IsReadyToActivate` | 是否等待手动激活 | operation 存在、`allowSceneActivation == false`、progress 大于等于 0.9 | loading UI 等待玩家确认时使用 |
| `AllowSceneActivation` | 是否允许激活 | getter 读 operation；setter 写 `operation.allowSceneActivation` | `Activate()` 是它的语义化快捷方法 |
| `LoadedScene` | 已加载场景对象 | `SceneManager.GetSceneByName(sceneName)` | 场景名不唯一时需注意 Unity 的查找行为 |
| `Operation` | 原始 AsyncOperation | 返回内部 operation | 高级场景可直接读 Unity 状态 |
| `Activate()` | 手动激活 | 设置 `AllowSceneActivation = true` | 仅当 `ActivateOnLoad=false` 时有实际意义 |

#### `SceneService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-500` | 早于 UI、Audio 等可能依赖场景状态的模块 |
| `ActiveScene` | 当前 active scene | 直接返回 `SceneManager.GetActiveScene()` | 每次 getter 都读取 Unity 当前状态 |
| `IsLoading` | 是否有活跃加载 | 先 `RemoveCompletedOperations()`，再判断 `activeOperations.Count > 0` | 访问这个属性会顺便清理已完成列表 |
| `CurrentOperation` | 当前 operation | LoadAsync 时设置，完成 finally 中更新或清空 | 并发时可能变成最后一个未完成 operation |
| `OnInitialize()` | 注册服务 | 注册 `ISceneService` 和自身实现 | 业务通过接口访问 |
| `Load(...)` | 同步加载 | `ValidateSceneName`、`ValidateSceneCanLoad` 后调用 `SceneManager.LoadScene(sceneName, mode)` | 同步加载会阻塞当前帧 |
| `LoadAsync(SceneLoadArgs args)` | 开始异步加载 | 校验 args 和 Build Settings；默认禁止并发；调用 `SceneManager.LoadSceneAsync`；设置 `allowSceneActivation`；包装 operation；加入 active list；设置 CurrentOperation；发布 started；启动 `TrackLoadAsync().Forget()` | 返回后加载追踪在 UniTask 中每帧推进 |
| `UnloadAsync(string sceneName)` | 卸载场景 | 空名返回 null；未加载返回 null；否则 `SceneManager.UnloadSceneAsync(sceneName)` | 不包装成自定义 operation |
| `IsSceneLoaded(string sceneName)` | 查询已加载场景 | 空名 false；规范化路径；遍历所有 loaded scene，用 `SceneMatches` 判断且 `scene.isLoaded` | 支持大小写不敏感匹配 |
| `IsSceneInBuildSettings(string sceneName)` | 查询 Build Settings | 空名 false；规范化输入；遍历 build settings scene path；先比完整路径，再比无扩展文件名 | 允许传 `"Battle"` 或 `"Assets/.../Battle.unity"` |
| `SetActiveScene(string sceneName)` | 设置 active scene | 空名 false；遍历已加载场景；匹配且 loaded 时调用 `SceneManager.SetActiveScene` | Additive 加载后可手动调用 |
| `OnShutdown()` | 关闭服务 | 清空 activeOperations、CurrentOperation 和三个事件 | 不主动卸载 Unity 场景 |
| `TrackLoadAsync(SceneLoadArgs args, SceneLoadOperation operation)` | 跟踪异步加载 | while 未完成时通知进度并 `UniTask.Yield(Update)`；完成后通知 1；按需 SetActiveScene；通知 completed；catch 记录异常；finally 移除 operation 并更新 CurrentOperation | 所有回调异常被隔离 |
| `NotifyProgress(...)` | 通知单次和全局进度 | 先安全调用 `args.Progress`，再 `PublishLoadProgress` | 单次回调和服务事件都会收到 |
| `NotifyCompleted(...)` | 通知单次和全局完成 | 先安全调用 `args.Completed(scene)`，再 `PublishLoadCompleted` | completed 回调异常不会阻断全局事件 |
| `PublishLoadStarted(...)` | 发布开始事件 | 取委托快照；null 返回；try/catch 调用 | 事件订阅者异常写日志 |
| `PublishLoadProgress(...)` | 发布进度事件 | 同上，传 operation 和 progress | progress 来自 `NormalizedProgress` |
| `PublishLoadCompleted(...)` | 发布完成事件 | 同上，传 operation | 发生在 operation 从 active list 移除之前 |
| `RemoveCompletedOperations()` | 清理完成 operation | 从后往前删除 null 或 `IsDone` 的 operation | 避免并发修改和索引错乱 |
| `ValidateSceneCanLoad(...)` | 校验是否可加载 | validate 为 true 且 `IsSceneInBuildSettings` false 时抛 `FrameException` | 同步/异步加载共用 |
| `ValidateSceneName(string sceneName)` | 校验场景名 | 空白抛 `FrameException("Scene name is empty.")` | 同步加载使用 |
| `SceneMatches(Scene scene, string sceneName, string normalizedScenePath)` | 判断场景是否匹配输入 | scene 无效 false；先按 scene.name 比，再按规范化 scene.path 比 | 用于已加载场景查询和 SetActive |
| `NormalizeScenePath(string sceneNameOrPath)` | 规范化路径 | 空白返回空字符串，否则把 `\` 替换为 `/` | 只处理分隔符，不去扩展名 |

## UI 模块

UI 模块基于 UGUI，覆盖常见项目需要的 UI 根节点、分层、面板生命周期、路由、返回栈、模态遮罩、弹窗队列、异步打开、开关动画和强类型参数。

### 使用方式

基础面板：

```csharp
using Frame.UI;

public sealed class MainMenuPanel : UIPanelBase
{
    protected override void OnCreate()
    {
        // 只在首次创建时调用
    }

    protected override void OnOpen(object args)
    {
        // 每次打开时调用
    }

    protected override void OnClose()
    {
        // 每次关闭时调用
    }
}
```

强类型参数面板：

```csharp
public sealed class ShopArgs
{
    public int Tab;
}

public sealed class ShopPanel : UIPanelBase<ShopArgs>
{
    protected override void OnOpen(ShopArgs args)
    {
        int tab = args == null ? 0 : args.Tab;
    }
}
```

直接打开：

```csharp
IUIService ui = Framework.Resolve<IUIService>();
ShopPanel panel = ui.Open<ShopPanel, ShopArgs>("UI/Shop", new ShopArgs { Tab = 2 });
```

注册路由、模态、动画、返回栈：

```csharp
ui.RegisterRoute<ShopPanel>(
    route: "shop",
    resourcesPath: "UI/Shop",
    layer: UILayer.Popup,
    cache: true,
    modal: true,
    closeOnBackdrop: true,
    allowBack: true,
    transition: new UIFadeTransition(0.15f));

ui.OpenRoute<ShopPanel, ShopArgs>("shop", new ShopArgs { Tab = 1 });
ui.Back();
```

弹窗队列：

```csharp
ui.EnqueueRoute<RewardPanel>("reward", rewardArgs);
ui.EnqueueRoute<NoticePanel>("notice", noticeArgs);
```

异步打开：

```csharp
UIPanelRequest<ShopPanel> request = ui.OpenRouteAsync<ShopPanel>("shop");
yield return request;

if (request.Success)
{
    request.Panel.Close();
}
```

### 设计和实现

`UIService.OnInitialize()` 会创建 `UIRoot`，并尝试解析 `IAssetService`。`UIRoot` 创建主 Canvas、CanvasScaler、GraphicRaycaster、EventSystem，并创建固定 UI 层。每个 `UILayer` 对应一个带独立 Canvas 的全屏 RectTransform，`sortingOrder` 等于枚举值。

打开面板流程：

1. 校验 panel 类型和 Resources 路径。
2. 复制 `UIOpenOptions`，避免外部修改影响当前打开。
3. 如果开启缓存并命中缓存，更新 `UIPanelContext`，重新打开面板。
4. 如果没有缓存，同步或异步加载 prefab。
5. 实例化 prefab，并查找指定的 `UIPanelBase` 子类组件。
6. 把 RectTransform 拉伸到目标层。
7. 创建 `UIPanelContext`，调用 `InternalCreate()` 和 `InternalOpen()`。
8. 如果是模态，创建全屏 `Image` 作为点击拦截遮罩。
9. 加入内部 stack，并把面板 transform 移到最上层。
10. 如果配置了 `IUITransition`，启动打开动画协程。

关闭面板流程：

1. 从 stack 移除。
2. 移除模态遮罩。
3. 调用 `OnClose()`。
4. 如果有关闭动画，协程等待 `PlayClose()`。
5. 如果 destroy 为 true，移除缓存、调用 `OnDispose()` 并销毁 GameObject。
6. 如果关闭的是队列当前面板，自动打开队列下一个。

返回栈：

- `Back()` 从 stack 顶部往下找第一个 `Context.AllowBack == true` 的面板。
- 找到后调用 `Close()`。
- 这允许 Loading、System 类面板设置 `AllowBack = false`，不被返回键关闭。

模态遮罩：

- `UIOpenOptions.Modal = true` 时，在同层创建全屏 `Image`。
- 遮罩颜色来自 `ModalColor`。
- `CloseOnBackdrop = true` 时给遮罩加 `Button`，点击后关闭面板。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IUIService` | UI 服务接口 | 打开、异步打开、路由、队列、关闭、返回 |
| `UIService` | UI 服务实现 | 缓存、路由表、返回栈、队列、模态遮罩、动画、资源加载 |
| `UIRoot` | UI 根节点 | 创建 Canvas、CanvasScaler、EventSystem 和所有 UI 层 |
| `UILayer` | UI 层级枚举 | `Background`、`Normal`、`Popup`、`Tips`、`Loading`、`System` |
| `UIPanelBase` | 面板基类 | 提供 `OnCreate`、`OnOpen(object)`、`OnClose`、`OnDispose` 生命周期 |
| `UIPanelBase<TArgs>` | 强类型参数面板基类 | 密封 object 版 `OnOpen`，校验参数类型后调用 `OnOpen(TArgs)` |
| `UIPanelContext` | 面板上下文 | 保存 service、route、assetPath、options、args、modal blocker |
| `UIOpenOptions` | 打开配置 | layer、cache、modal、closeOnBackdrop、allowBack、modalColor、transition |
| `UIRoute` | 路由定义 | route 名、Resources 路径、panel 类型、默认打开配置 |
| `UIPanelRequest<TPanel>` | 异步打开请求 | 可 `yield return`，有 Success、Panel、Error |
| `IUITransition` | UI 动画接口 | `PlayOpen` 和 `PlayClose` 返回 IEnumerator |
| `UIFadeTransition` | 默认淡入淡出动画 | 自动确保 CanvasGroup，支持 unscaled time，内部通过 `ITweenService` 桥接调用 DOTween 的 DOFade |
| `SafeAreaFitter` | 安全区适配组件 | 根据 `Screen.safeArea` 调整 RectTransform 锚点 |
| `QueuedPanelOpen` | `UIService` 私有队列项 | 保存 route、panel 类型、参数和请求句柄，队列弹窗关闭后继续打开下一个 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/UI` 的实际源码文件展开。UI 模块的核心是 `UIService`，其他类型分别承担面板生命周期、路由配置、打开参数、异步请求、层级 root、过渡动画和安全区适配。

#### `UILayer.cs`

| 枚举值 | sortingOrder | 用途 |
| --- | ---: | --- |
| `Background` | 0 | 背景层 |
| `Normal` | 100 | 普通页面 |
| `Popup` | 200 | 弹窗 |
| `Tips` | 300 | toast、tips |
| `Loading` | 400 | loading 遮罩 |
| `System` | 500 | 系统级 UI |

#### `UIOpenOptions.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Layer` | 打开到哪个 UI 层 | 默认 `UILayer.Normal` | 决定 parent 和 sortingOrder |
| `Cache` | 是否缓存面板实例 | 默认 true | true 时关闭不 destroy 可复用 |
| `Modal` | 是否创建模态遮罩 | 默认 false | 遮罩由 `UIService.PrepareModalBlocker()` 创建 |
| `CloseOnBackdrop` | 点击遮罩是否关闭 | 默认 false | 只有 Modal true 时有效 |
| `AllowBack` | 是否允许 Back 关闭 | 默认 true | Loading/System 可设 false |
| `ModalColor` | 遮罩颜色 | 默认半透明黑色 | 可按弹窗定制 |
| `Transition` | 打开/关闭动画 | `IUITransition` | 可传 `UIFadeTransition` 或自定义 |
| `Default()` | 创建默认配置 | `new UIOpenOptions()` | 避免调用方依赖构造细节 |
| `Clone()` | 复制配置 | 复制所有属性到新对象 | UIService 每次打开都会复制，防止外部后续修改影响已打开面板 |

#### `UIRoute.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `UIRoute(string route, string resourcesPath, Type panelType, UIOpenOptions options = null)` | 创建路由 | 校验 route、resourcesPath、panelType；panelType 必须继承 `UIPanelBase`；options 为空用默认，否则 Clone | 路由把业务名、资源路径、面板类型和默认打开参数绑定在一起 |
| `Route` | 路由名 | 构造后私有 set | `UIService.routes` 的 key |
| `ResourcesPath` | Resources prefab 路径 | 构造后私有 set | 交给 `IAssetService` 加载 |
| `PanelType` | 面板组件类型 | 构造后私有 set | 打开路由时校验和 GetComponent |
| `Options` | 默认打开配置 | 构造时 Clone | 避免外部修改原 options |

#### `UIPanelRequest.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `keepWaiting` | Coroutine 等待条件 | 返回 `!IsDone` | 可 `yield return request` |
| `IsDone` | 请求是否完成 | `Complete()` 设置 | 成功失败都会完成 |
| `Success` | 是否成功 | `Panel != null` | 失败时看 Error |
| `Panel` | 打开的面板 | `Complete(panel)` 设置 | 泛型类型由请求决定 |
| `Error` | 失败原因 | `Complete` 设置 | 异步加载失败、队列清理、类型错误等 |
| `Complete(TPanel panel, string error = null)` | 内部完成请求 | 保存 panel/error 并置 IsDone | 只由 UIService 调用 |

#### `UIPanelContext.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `UIPanelContext(UIService service, string route, string assetPath, UIOpenOptions options, object args)` | 创建上下文 | 保存 service、route、assetPath、args；options 为空默认，否则 Clone | `UIPanelBase.InternalCreate()` 保存它 |
| `Service` | 所属 UIService | 构造时保存 | 面板 `Close()` 通过它回调服务 |
| `Route/AssetPath` | 路由和资源路径 | 可由 `Update()` 更新 | 缓存面板重新打开时会更新 |
| `Layer` | 当前层 | 返回 `Options.Layer` | 只读快捷属性 |
| `Args` | 打开参数 | 构造或 Update 保存 | 可用 `GetArgs<T>()` 取强类型 |
| `Options` | 打开配置 | Clone 保存 | 不直接引用调用方 options |
| `IsModal/AllowBack` | 模态和返回行为 | 从 Options 推导 | 返回栈和遮罩逻辑使用 |
| `ModalBlocker` | 当前遮罩对象 | internal setter | UIService 创建和销毁 |
| `GetArgs<TArgs>()` | 获取强类型参数 | Args 为空返回 default，否则强转 | 类型不匹配会抛 InvalidCastException |
| `Update(...)` | 更新缓存面板上下文 | 替换 route、assetPath、options、args | 缓存命中重新打开时调用 |
| `SetModalBlocker(GameObject blocker)` | 保存遮罩引用 | internal 方法 | RemoveModalBlocker 使用 |

#### `UIPanelBase.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Context` | 面板上下文 | InternalCreate 设置，Dispose 清空 | 面板内可读取 route、args、options |
| `IsOpen` | 是否打开 | InternalOpen/Close 维护 | 关闭动画期间已为 false |
| `InternalCreate(UIPanelContext context)` | 内部创建 | 保存 Context；首次 created 时调用 `OnCreate()` | 只调用一次 |
| `InternalOpen(object args)` | 内部打开 | `IsOpen=true`，激活 GameObject，调用 `OnOpen(args)` | 每次打开都调用 |
| `InternalClose(bool deactivate = true)` | 内部关闭 | 未打开返回 false；置 IsOpen false；调用 `OnClose()`；按需 SetActive false | 返回是否真的从打开状态关闭 |
| `InternalSetClosed()` | 强制置关闭显示 | `gameObject.SetActive(false)` | 关闭动画后最终隐藏 |
| `InternalDispose()` | 内部释放 | 调 `OnDispose()`，清 Context | destroy 面板时使用 |
| `Close(bool destroy = false)` | 面板自关闭 | Context 非空时 `Context.Service.Close(this, destroy)` | 面板按钮可直接调用 |
| `OnCreate/OnOpen/OnClose/OnDispose` | 生命周期钩子 | protected virtual 空实现 | 业务面板 override |

#### `UIPanelBaseGeneric.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `OnOpen(object args)` | 密封 object 参数入口 | args 为空调用 `OnOpen(default(TArgs))`；类型不匹配抛 `ArgumentException`；类型正确转 TArgs | 防止强类型面板收到错误参数时静默失败 |
| `OnOpen(TArgs args)` | 强类型打开钩子 | protected virtual 空实现 | 业务面板 override 这个方法 |

#### `UIRoot.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Canvas` | 主 Canvas | Initialize 时获取并设置 | RenderMode 为 ScreenSpaceOverlay |
| `Initialize(FrameSettings settings)` | 初始化 root | 设置 Canvas、CanvasScaler 参考分辨率和 match；确保 GraphicRaycaster；EnsureEventSystem；EnsureAllLayers | `UIService.CreateRoot()` 调用 |
| `GetLayer(UILayer layer)` | 获取或创建层节点 | 字典命中返回；否则创建带 RectTransform/Canvas/GraphicRaycaster 的全屏节点，Canvas overrideSorting true，sortingOrder 为层枚举值 | 每个层独立 Canvas，便于排序 |
| `EnsureAllLayers()` | 创建默认层 | 依次 GetLayer 六个内置层 | 初始化时预建 |
| `EnsureEventSystem()` | 确保 EventSystem | 已存在返回；Input System 下创建 `InputSystemUIInputModule`，否则 `StandaloneInputModule`，并 DontDestroyOnLoad | 避免 UI 无法响应点击 |

#### `IUITransition.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `PlayOpen(UIPanelBase panel)` | 播放打开动画 | 返回 IEnumerator | UIService 用 root.StartCoroutine 执行 |
| `PlayClose(UIPanelBase panel)` | 播放关闭动画 | 返回 IEnumerator | CloseInternal 在 FinishClose 前等待 |

#### `UIFadeTransition.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `UIFadeTransition(float duration = 0.18f, bool unscaledTime = true, Ease ease = Ease.OutQuad)` | 创建淡入淡出动画 | OpenDuration/CloseDuration 都设为 duration，保存 UseUnscaledTime，open/close ease 同步设置 | 适合多数 UI，不受 timeScale 影响 |
| `OpenDuration/CloseDuration/UseUnscaledTime` | 动画参数 | 自动属性 | 可运行时调整 |
| `OpenEase/CloseEase` | 打开/关闭缓动 | 默认构造参数 ease | 用 DOTween 的 Ease |
| `OpenCurve/CloseCurve` | 打开/关闭自定义曲线 | AnimationCurve | 非空时优先于 TweenEase |
| `PlayOpen(UIPanelBase panel)` | 淡入 | `Fade(panel, 0, 1, OpenDuration, OpenEase, OpenCurve)` | 打开时调用 |
| `PlayClose(UIPanelBase panel)` | 淡出 | `Fade(panel, 1, 0, CloseDuration, CloseEase, CloseCurve)` | 关闭时调用 |
| `Fade(...)` | 淡入淡出实现 | panel 空 yield break；确保 CanvasGroup；duration <= 0 直接设 alpha；优先解析 ITweenService.Fade；无补间服务时线性协程插值；结束设目标 alpha | 自动添加 CanvasGroup |

#### `SafeAreaFitter.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Awake()` | 初始化 RectTransform | 获取 RectTransform 并 Apply | 组件要求 RectTransform |
| `Update()` | 监听安全区变化 | `lastSafeArea != Screen.safeArea` 时 Apply | 横竖屏或刘海区域变化时更新 |
| `Apply()` | 应用安全区锚点 | 把 safeArea position/size 转成 0 到 1 anchorMin/anchorMax | 只改 anchor，不改 offset |

#### `IUIService.cs`

`IUIService` 的公开方法都由 `UIService` 实现。同步打开返回面板实例，异步打开返回 `UIPanelRequest<TPanel>`，路由方法先查 `UIRoute` 再打开，队列方法保证一次只展示一个队列面板。

| 成员组 | 作用 | 注意点 |
| --- | --- | --- |
| `Open<TPanel>` / `Open<TPanel,TArgs>` | 按 Resources 路径直接打开 | 泛型参数必须继承 `UIPanelBase` |
| `OpenAsync<TPanel>` | 异步加载并打开 | 依赖 `IAssetService.LoadAsync<GameObject>` |
| `RegisterRoute` / `UnregisterRoute` / `HasRoute` | 管理路由表 | route 为空会失败或抛异常 |
| `OpenRoute` / `OpenRouteAsync` | 按 route 打开 | 会校验 route 注册类型和泛型类型 |
| `EnqueueRoute` | 队列弹窗 | 当前队列面板关闭后自动打开下一个 |
| `ClearQueuedPanels` | 清空队列 | 未打开的请求会以失败完成 |
| `Close` / `CloseTop` / `CloseAll` / `Back` | 关闭和返回 | Back 会找 stack 顶部第一个 AllowBack 面板 |

#### `UIService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-400` | 晚于 Asset/Scene，早于 Audio/Save 等 |
| `Root` / `QueuedPanelCount` | 查询状态 | 返回 root 和队列数量 | 诊断和业务可读 |
| `OnInitialize()` | 初始化 UI | 尝试解析 IAssetService；CreateRoot；注册 IUIService 和自身 | AssetService 关闭时异步/同步打开会失败 |
| `Open/OpenAsync/RegisterRoute/OpenRoute/EnqueueRoute` | 公开入口 | 构造 options/request/route 后转内部方法 | 详见接口说明 |
| `ClearQueuedPanels()` | 清空队列 | 逐个 Dequeue 并 Complete(null, `"UI panel queue was cleared."`) | 不关闭当前已打开的 queuedActivePanel |
| `Close/CloseTop/CloseAll/Back` | 关闭入口 | Close 转 `CloseInternal`；CloseAll 清队列并设置 suppressQueuedOpen；Back 从 stack 顶部找 AllowBack | CloseAll 避免关闭过程中自动开下一个队列 |
| `OnShutdown()` | 关闭 UI | 抑制队列；清队列；立即关闭并销毁全部；清缓存/路由/stack；清 root/assets | 不单独销毁 root，因为 root 在 Context.Root 下随框架对象销毁 |
| `OpenInternal(...)` | 同步打开核心 | ValidateOpen；Clone options；算 cacheKey；缓存命中则更新 Context、准备 modal、InternalOpen、BringToTop、播放动画；否则通过 assets.Instantiate 加载实例并 CreatePanelFromInstance | 缓存类型不匹配写 warning 并返回 null |
| `OpenInternalAsync(...)` | 异步打开核心 | 校验并解析 options/cacheKey；缓存命中复用 OpenInternal；assets 为空失败；否则 LoadAsync GameObject，回调中 Instantiate、Release handle、CreatePanelFromInstance、Complete request | 回调异常会记录并完成失败 |
| `CreatePanelFromInstance(...)` | 从 prefab 实例创建面板 | GetComponent 指定 panelType；缺失则 Destroy；确保 RectTransform 全屏；InternalCreate/PrepareModalBlocker/InternalOpen；加入 stack；BringToTop；按 cacheKey 缓存；播放打开动画 | OnCreate/OnOpen 抛异常时会清理实例 |
| `CloseInternal(...)` | 关闭核心 | null 返回；取 options；从 stack 移除；移除 modal；InternalClose(false)；未打开且不 destroy 返回；有关闭动画且非 immediate 时 StartCoroutine；否则 FinishClose | 动画期间 GameObject 保持 active，结束后隐藏 |
| `CloseWithTransition(...)` | 关闭动画协程 | yield `transition.PlayClose(panel)` 后 FinishClose | 只由 CloseInternal 启动 |
| `FinishClose(...)` | 完成关闭 | InternalSetClosed；destroy 时 RemoveCached、InternalDispose、Destroy；如果关闭的是 queuedActivePanel，清空并按需 OpenNextQueuedPanel | 队列推进在这里发生 |
| `CloseAllImmediate(bool destroy)` | 立即关闭全部 | 从 stack 后往前 CloseInternal(immediate true) | Shutdown 使用 |
| `CreateRoot()` | 创建 UI root | new GameObject，挂 RectTransform/Canvas/CanvasScaler/GraphicRaycaster，挂到 Context.Root，获取或添加 UIRoot 并 Initialize | 名称来自 `FrameSettings.UIRootName` |
| `PrepareModalBlocker(...)` | 创建模态遮罩 | 先移除旧遮罩；非 Modal 返回；同层创建全屏 Image，设置颜色和 raycast；CloseOnBackdrop 时加 Button 并监听 Close(panel)；保存到 Context | 遮罩和面板同层，随后 BringToTop 让面板在遮罩上方 |
| `RemoveModalBlocker(...)` | 移除遮罩 | Context/ModalBlocker 为空返回；Destroy blocker 并置 null | 关闭和重新打开缓存面板时使用 |
| `BringToTop(...)` | 面板置顶 | 从 stack 移除再 Add；transform.SetAsLastSibling | 保持返回栈顺序和层级一致 |
| `PlayOpenTransition(...)` | 播放打开动画 | panel/options/transition/root/active 任一无效则返回；StartCoroutine `Transition.PlayOpen` | 打开动画不阻塞 Open 返回 |
| `RemoveCached(...)` | 从缓存移除面板 | 遍历 cachedPanels 找 value 相同的 key 并删除 | destroy 面板时使用 |
| `GetRoute(string route)` | 获取路由 | route 空抛 `FrameException`；未注册抛 `FrameException` | 所有 route 打开入口共用 |
| `ValidateRoutePanelType(...)` | 校验路由面板类型 | expectedType 空或 UIPanelBase 时跳过；否则检查 expected 可赋值或相等 | 避免用错误泛型打开 route |
| `ValidateOpen(Type panelType, string resourcesPath)` | 校验打开参数 | panelType 必须继承 UIPanelBase；resourcesPath 非空 | 同步/异步都用 |
| `ResolveOptions(UIOpenOptions options)` | 复制打开配置 | null 返回默认，否则 Clone | 防止外部 options 被服务持有 |
| `GetCacheKey(string route, string resourcesPath)` | 生成缓存 key | route 非空用 route，否则 resourcesPath | route 面板和直接路径面板缓存隔离规则取决于 key |
| `OpenNextQueuedPanel()` | 推进队列 | 当前 queuedActivePanel 仍打开则返回；循环取队列项，查 route、校验类型、OpenInternal；失败则完成失败并继续下一个；成功设 queuedActivePanel 并完成请求 | 确保队列一次只有一个 active 面板 |
| `QueuedPanelOpen` | 队列项基类 | 保存 PanelType/Route/Args/request；Complete 转发到 `UIPanelRequest<UIPanelBase>` | 非泛型队列使用 |
| `QueuedPanelOpen<TPanel>` | 泛型队列项 | 保存泛型 request；Complete 时 `panel as TPanel` | 泛型队列请求使用 |

## Audio 模块

Audio 模块负责 BGM、音效、音量分组、AudioSource 池和 AudioMixer 音量参数。

### 使用方式

```csharp
using Frame.Audio;
using Frame.Core;

IAudioService audio = Framework.Resolve<IAudioService>();
audio.SetVolume(AudioCategory.Music, 0.8f);
audio.PlayMusic(bgmClip, fadeSeconds: 1f);

AudioPlaybackHandle handle = audio.PlayOneShotHandle(clickClip, AudioCategory.UI);
handle.Volume = 0.5f;
handle.Stop();
```

使用 `AudioCue`：

```csharp
audio.PlayCue(buttonCue);
```

### 设计和实现

`AudioService` 初始化时创建 `Audio` 根节点，按 `FrameSettings.AudioSourcePoolSize` 预热 `AudioSource`。播放音效时从池中取空闲 AudioSource，使用 `PlayOneShot()` 播放，并根据 clip 长度启动 UniTask 延迟回收。播放 BGM 时使用 loop 模式保存到 `CurrentMusic`，支持淡入淡出。淡入淡出用 `CancellationTokenSource` 管理，开始新的淡入淡出会取消旧任务。

音量分组通过 `FrameSettings.GetAudioMixerGroup(category)` 和 `GetAudioMixerVolumeParameter(category)` 连接 Unity `AudioMixer`。线性音量转换为分贝，0 音量映射为 -80dB。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IAudioService` | 音频服务接口 | 音量、静音、BGM、Cue、OneShot、播放句柄 |
| `AudioService` | 音频服务实现 | AudioSource 池、BGM 淡入淡出、音效自动回收、AudioMixer 参数 |
| `AudioPlaybackHandle` | 播放句柄 | 可读 Source、Category、Volume、Pitch、IsPlaying，可 Stop |
| `AudioCue` | 音频配置资源 | ScriptableObject，保存 clip、category、volume、pitch、loop |
| `AudioCategory` | 音频分类枚举 | `Master`、`Music`、`Sfx`、`UI`、`Ambient` |
| `ActivePlayback` | `AudioService` 私有播放记录 | 绑定播放句柄和自动回收的取消令牌，避免音源回收任务泄漏 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Audio` 的实际源码文件展开。Audio 模块通过 `AudioPlaybackHandle` 把外部可控播放和内部 AudioSource 池关联起来。

#### `AudioCategory.cs`

| 枚举值 | 含义 |
| --- | --- |
| `Master` | 主音量 |
| `Music` | 背景音乐 |
| `Sfx` | 游戏音效 |
| `UI` | UI 音效 |
| `Ambient` | 环境声 |

#### `AudioCue.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Clip` | 音频资源 | 返回序列化 AudioClip | cue 为空或 clip 空时播放返回 null |
| `Category` | 音频分类 | 默认 Sfx | Music 会走 BGM 播放逻辑 |
| `Volume` | cue 音量 | `Mathf.Clamp01(volume)` | Inspector 中越界会在 getter 兜底 |
| `Pitch` | cue 变调 | `Mathf.Clamp(pitch, 0.1f, 3f)` | 避免 0 或极端 pitch |
| `Loop` | 是否循环 | 返回序列化 bool | 只有 `PlayCueHandle` 的 Music 分支会传给 loop |

#### `IAudioService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `AudioPlaybackHandle(...)` | 创建播放句柄 | internal 构造，保存 source、category、volume、pitch、stop/refresh 回调并标记 valid | 只由 AudioService 创建 |
| `Source` | 当前 AudioSource | 句柄有效时返回 source，否则 null | 回收后不可继续使用 |
| `Category` | 播放分类 | 构造后私有 set | 统计或逻辑区分 |
| `Volume` | 播放音量 | setter clamp 0 到 1 并 Refresh | OneShot 播放后调音量只影响 source.volume，已开始的 PlayOneShot 实际混音行为需按 Unity 机制理解 |
| `Pitch` | 播放 pitch | setter clamp 0.1 到 3，并写 source.pitch | 对 loop 和后续 source 生效 |
| `IsValid` | 句柄是否仍有效 | valid 且 source 非空 | source 回池后 false |
| `IsPlaying` | 是否正在播放 | IsValid 且 `source.isPlaying` | OneShot 完成回收后 false |
| `Stop()` | 停止播放 | valid 且 stopAction 非空时回调 AudioService | 主动回收 source |
| `Invalidate()` | 内部失效 | valid false，source null | 回收和 shutdown 使用 |
| `Refresh()` | 内部刷新 | valid 且 refreshAction 非空时回调 | Volume setter 使用 |
| `IAudioService.CurrentMusic` | 当前 BGM 句柄 | 实现类返回 musicHandle | 没有音乐时 null |
| `SetVolume/GetVolume/SetMuted` | 音量控制 | 实现类写 mixerVolumes 并同步 AudioMixer | Muted 只作用 Master mixer 参数 |
| `PlayMusic/StopMusic` | BGM 控制 | 实现类 loop 播放并可淡入淡出 | 新 BGM 会先停止旧 BGM |
| `PlayCue/PlayCueHandle` | 播放 AudioCue | cue 为 Music 时走音乐逻辑，否则 one-shot | Handle 版本可停止或改参数 |
| `PlayOneShot/PlayOneShotHandle` | 播放单次音效 | 从 AudioSource 池取 source，结束后自动回收 | clip 空返回 null |

#### `AudioService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `ActivePlayback` | 内部播放记录 | 保存 Handle 和 ReturnCancellation | 用于自动回收任务取消 |
| `Priority` | 模块优先级 | 返回 `-300` | UI 之后，Save/Config 之前 |
| `CurrentMusic` | 当前音乐 | 返回 `musicHandle` | 可能已失效，使用前看 IsValid |
| `OnInitialize()` | 初始化音频服务 | 创建 `"Audio"` 节点挂到 Context.Root；SetDefaultVolumes；ApplyAllMixerVolumes；按 settings 预热 AudioSource；注册服务 | AudioMixer 参数在初始化时同步一次 |
| `SetVolume(AudioCategory category, float volume)` | 设置分类音量 | clamp 后写 `mixerVolumes`，调用 `ApplyMixerVolume(category)` | 只改变 mixer 参数，不遍历所有 source |
| `GetVolume(AudioCategory category)` | 读取分类音量 | 字典命中返回，否则 1 | 未设置分类默认满音量 |
| `SetMuted(bool muted)` | 设置静音 | 保存 muted，应用 Master 音量 | 静音通过 Master 参数写 0 线性音量实现 |
| `PlayMusic(AudioClip clip, float fadeSeconds = 0f, float volume = 1f)` | 播放 BGM | clip 空返回；StopMusic；`PlayClipHandle` Music loop；fade > 0 时先音量 0 再 StartMusicFade 到目标音量 | fade 播放使用 unscaled time |
| `StopMusic(float fadeSeconds = 0f)` | 停止 BGM | 有 musicHandle 时，fade > 0 且有效则淡出并完成后 Stop；否则 CancelMusicFade、Stop、清 musicHandle | 淡出期间 musicHandle 仍保留 |
| `PlayCue(AudioCue cue, Vector3 position = default)` | 播放 cue 并返回 source | cue/clip 空 null；Music 调 PlayMusic；其他调 PlayOneShot | 简单播放入口 |
| `PlayOneShot(...)` | 播放单次并返回 source | 调 PlayOneShotHandle，再取 handle.Source | 不保留 handle 时无法主动停止 |
| `PlayCueHandle(...)` | 播放 cue 并返回 handle | cue/clip 空 null；Music 分支 StopMusic 后 PlayClipHandle，使用 cue.Pitch 和 cue.Loop；其他走 OneShotHandle | 需要控制播放时用 |
| `PlayOneShotHandle(...)` | 播放单次并返回 handle | 调 `PlayClipHandle(..., loop:false)` | 自动延迟回收 |
| `PlayClipHandle(...)` | 播放核心 | clip 空 null；GetFreeSource；设置 position/pitch/loop/clip/mixer；创建 handle 和 ActivePlayback；RefreshPlayback；loop 则 Play；非 loop 则 PlayOneShot 并启动 ReturnWhenFinishedAsync | 非 loop 的 source.clip 为 null，使用 PlayOneShot 参数播放 |
| `OnShutdown()` | 关闭服务 | 取消音乐 fade；停止并隐藏所有 source；取消所有 ReturnCancellation；Invalidate handles；清字典/池/音量；销毁 audioRoot | 防止 UniTask 回收任务继续访问已释放 source |
| `SetDefaultVolumes()` | 初始化音量字典 | 五个分类都设 1 | OnInitialize 使用 |
| `RefreshPlayback(AudioPlaybackHandle handle)` | 同步句柄到 source | 无效返回；写 source.pitch/source.volume | handle.Volume setter 回调 |
| `StopPlayback(AudioPlaybackHandle handle)` | 停止某播放 | 无效返回；ReturnSource；如果是 musicHandle 则取消 fade 并清空 musicHandle | AudioPlaybackHandle.Stop 调用 |
| `StartMusicFade(...)` | 启动 BGM 淡入淡出 | handle 无效返回；CancelMusicFade；创建 CTS；保存并启动 FadeMusicAsync | 同时只允许一个音乐 fade |
| `FadeMusicAsync(...)` | 音乐淡入淡出任务 | duration 最小 0.001；先 Yield 一帧；循环 unscaledDeltaTime 插值 handle.Volume；结束设目标音量并可 Stop；取消异常吞掉；finally 如果仍是当前 CTS 则清空并 Dispose | 使用 CancellationTokenSource 控制竞态 |
| `CancelMusicFade()` | 取消当前 fade | CTS 为空返回；清字段、Cancel、Dispose | 开新 fade 或停止音乐时调用 |
| `ApplyMixerGroup(AudioSource source, AudioCategory category)` | 设置 source 输出组 | 从 settings 获取分类 mixer group，赋给 source.outputAudioMixerGroup | 没有配置时可能为 null |
| `ApplyAllMixerVolumes()` | 应用所有 mixer 音量 | 依次 Master/Music/Sfx/UI/Ambient | 初始化时调用 |
| `ApplyMixerVolume(AudioCategory category)` | 写 AudioMixer 参数 | Context/settings 无效返回；取显式 assigned group 和参数名；无 mixer 或参数空返回；Master 且 muted 时音量取 0，否则取分类音量；转 dB 后 SetFloat | 只有配置了对应 AudioMixerGroup 才会写参数 |
| `LinearToDecibels(float value)` | 线性音量转 dB | 小于等于 0.0001 返回 -80，否则 `Mathf.Log10(value) * 20` | Unity AudioMixer 常用映射 |
| `Prewarm(int count)` | 预热 source 池 | 循环 CreateSource，隐藏 GameObject，加入 sourcePool | count 来自 settings，最小 1 |
| `GetFreeSource()` | 获取可用 source | 找第一个 inactive source 并激活；没有则 CreateSource `"Audio_Extra"` 并加入池 | 池不限制最大数量 |
| `CreateSource(string name)` | 创建 AudioSource | new GameObject 挂 AudioSource；父节点 audioRoot 或 Context.Root；playOnAwake false，spatialBlend 0 | 默认 2D 音频 |
| `ReturnWhenFinishedAsync(...)` | 单次音效自动回收 | 按 clip length/pitch 转毫秒 unscaled delay；取消则返回；延迟后确认 activePlaybacks 中的 handle 仍匹配，再 ReturnSource | 避免 source 被复用后旧任务误回收 |
| `ReturnSource(AudioSource source, bool stopReturnRoutine)` | 回收 source | 找到 active playback 后按需取消并 Dispose ReturnCancellation；Invalidate handle；移除 active；Stop、清 clip/loop、SetActive false | stopReturnRoutine false 表示当前就是自动回收任务，不需要取消自己 |

## Tweening 和 DOTween 集成

Tweening 模块只定义抽象接口，DOTween 集成提供实际实现。这样业务代码可以依赖 `ITweenService`，以后替换动画库时不改业务层。

### 使用方式

```csharp
using Frame.Core;
using Frame.Tweening;

ITweenService tweens = Framework.Resolve<ITweenService>();
tweens.Move(transform, new Vector3(0, 2, 0), 0.3f, local: true, new TweenOptions
{
    Ease = TweenEase.OutBack,
    IgnoreTimeScale = true,
    Target = transform,
    Completed = () => FrameLog.Info("move done")
});
```

### 设计和实现

`DOTweenModuleInstaller` 在框架扫描外部模块安装器时执行。如果 `FrameSettings.EnableTweenService` 为 true，就把 `DOTweenTweenService` 添加到模块列表。`DOTweenTweenService.OnInitialize()` 调用 `DOTween.Init()`，并注册 `ITweenService`。

`DOTweenTweenHandle` 包装 DOTween 的 `Tween`，避免业务层直接依赖 DOTween 类型。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ITweenService` | 补间服务接口 | To、Move、Scale、Fade、Kill、KillAll |
| `ITweenHandle` | 补间句柄接口 | IsActive、IsPlaying、Play、Pause、Kill、OnComplete |
| `TweenOptions` | 补间配置 | Ease、EaseCurve、IgnoreTimeScale、Target、Completed |
| `TweenEase` | 缓动枚举 | Linear、Quad、Cubic、Back 系列 |
| `DOTweenModuleInstaller` | DOTween 模块安装器 | 根据设置把 DOTween 服务加入模块列表 |
| `DOTweenTweenService` | DOTween 实现 | 把接口调用映射到 DOTween API，负责 Ease 映射 |
| `DOTweenTweenHandle` | DOTween 句柄包装 | 包装 `Tween`，提供统一句柄操作 |
| `DOTweenUIFadeTransition` | DOTween UI 过渡 | 直接使用 `CanvasGroup.DOFade` 实现打开/关闭淡入淡出 |

### 源码级文件和方法详解

这一节覆盖 `Assets/Frame/Runtime/Tweening` 的抽象接口，以及 `Assets/Frame/Integrations/DOTween` 的实际实现。

#### `ITweenHandle.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `IsActive` | tween 是否仍有效 | 实现类映射到底层 tween 状态 | 被 Kill 后 false |
| `IsPlaying` | tween 是否正在播放 | 实现类映射到底层 tween 状态 | Pause 后 false |
| `Play()` | 播放 | 底层 tween Play | 可恢复暂停 tween |
| `Pause()` | 暂停 | 底层 tween Pause | 不销毁 tween |
| `Kill(bool complete = false)` | 终止 | complete true 时按底层库规则补完成状态 | DOTween 句柄会清空内部 tween 引用 |
| `OnComplete(Action callback)` | 注册完成回调 | 返回自身以便链式调用 | callback 为空时不处理 |

#### `ITweenService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `IsAvailable` | tween 服务是否可用 | DOTween 实现恒 true | 可用于可选集成判断 |
| `To(...)` | 任意 float 补间 | getter/setter 映射到底层 tween | 可补任意数值 |
| `Move(...)` | Transform 位置补间 | 支持 world/local position | target 为空返回无效 handle |
| `Scale(...)` | Transform scale 补间 | 补 localScale | target 为空返回无效 handle |
| `Fade(...)` | CanvasGroup alpha 补间 | endValue clamp 到 0 到 1 | UI 淡入淡出使用 |
| `Kill(object target, bool complete = false)` | 按 target 杀 tween | 底层 DOTween.Kill | 需要 options.Target 或 SetTarget 才能按业务对象杀 |
| `KillAll(bool complete = false)` | 杀全部 tween | 底层 DOTween.KillAll | Shutdown 使用 |

#### `TweenEase.cs`

| 枚举值 | 映射 |
| --- | --- |
| `Linear` | DOTween `Ease.Linear` |
| `InQuad/OutQuad/InOutQuad` | DOTween Quad 系列 |
| `InCubic/OutCubic/InOutCubic` | DOTween Cubic 系列 |
| `InBack/OutBack/InOutBack` | DOTween Back 系列 |

#### `TweenOptions.cs`

| 字段 | 作用 | 默认和注意点 |
| --- | --- | --- |
| `Ease` | 缓动类型 | 默认 `TweenEase.OutQuad` |
| `EaseCurve` | 自定义缓动曲线 | 非空时 DOTween SetEase(AnimationCurve) 优先于 Ease |
| `IgnoreTimeScale` | 是否忽略 Time.timeScale | DOTween `SetUpdate(bool)` |
| `Target` | 业务目标对象 | 用于 DOTween target 和 Kill |
| `Completed` | 完成回调 | ApplyOptions 绑定 OnComplete |

#### `DOTweenModuleInstaller.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Install(ModuleManager modules, FrameSettings settings)` | 安装 DOTween 服务模块 | settings 非空且 `EnableTweenService` true 时 `modules.Add(new DOTweenTweenService())` | 通过 `Framework.RegisterInstalledModules()` 反射调用 |

#### `DOTweenTweenHandle.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `DOTweenTweenHandle(Tween tween)` | 包装 DOTween Tween | 保存 tween 引用 | tween 可为 null，表示无效句柄 |
| `IsActive` | 是否 active | tween 非空且 `tween.IsActive()` | Kill 后 false |
| `IsPlaying` | 是否 playing | tween 非空且 `tween.IsPlaying()` | Pause 后 false |
| `Play()` | 播放 | tween 非空时 `tween.Play()` | 空句柄无副作用 |
| `Pause()` | 暂停 | tween 非空时 `tween.Pause()` | 空句柄无副作用 |
| `Kill(bool complete = false)` | 杀 tween | tween 非空时 `tween.Kill(complete)` 并置 null | 防重复 Kill |
| `OnComplete(Action callback)` | 注册完成回调 | tween 和 callback 非空时 `tween.OnComplete(() => callback())`，返回 this | 业务不接触 DOTween 类型 |

#### `DOTweenTweenService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Priority` | 模块优先级 | 返回 `-250` | Audio 之后，Config/Save 之前 |
| `IsAvailable` | 可用状态 | 返回 true | DOTween 集成程序集存在时可用 |
| `OnInitialize()` | 初始化 DOTween | `DOTween.Init(false, true, LogBehaviour.ErrorsOnly)`，注册 `ITweenService` 和自身实现 | 初始化时只输出错误级日志 |
| `To(...)` | float 补间 | getter 或 setter 空返回无效 handle；否则 `DOTween.To(getter.Invoke, setter.Invoke, endValue, max(0,duration))`，ApplyOptions | duration 小于 0 会按 0 处理 |
| `Move(...)` | 位置补间 | target 空无效；用 DOTween.To 读写 local/world position；先 `tween.SetTarget(target)`，再 ApplyOptions | options.Target 可覆盖 target |
| `Scale(...)` | 缩放补间 | target 空无效；读写 `target.localScale` | 常用于 UI 或 Transform 动画 |
| `Fade(...)` | CanvasGroup alpha 补间 | target 空无效；使用 DOTween `CanvasGroup.DOFade`；endValue clamp | UI 淡入淡出 |
| `Kill(object target, bool complete = false)` | 按 target 杀 tween | 返回 `DOTween.Kill(target, complete)` 数量 | 依赖 tween target |
| `KillAll(bool complete = false)` | 杀全部 tween | 调 `DOTween.KillAll(complete)` | 框架关闭时调用 |
| `OnShutdown()` | 关闭服务 | `KillAll(false)` | 防止 tween 继续运行 |
| `ApplyOptions(Tween tween, TweenOptions options)` | 应用配置 | tween 空返回；options 空创建默认；SetEase、SetUpdate；Target 非空则 SetTarget；Completed 非空则 OnComplete | 所有创建方法共用 |
| `MapEase(TweenEase ease)` | 映射缓动 | switch 转 DOTween Ease，默认 OutQuad | 抽象枚举和第三方库隔离 |

## Save 模块

Save 模块提供本地存档，包含序列化、加密、临时文件、备份、metadata、SHA256 校验、版本迁移和异步读写。

### 使用方式

定义数据：

```csharp
using System;
using Frame.Save;

[Serializable]
public sealed class PlayerSave : ISaveVersionedData
{
    public int SaveVersion => 2;
    public string Name;
    public int Level;
}
```

保存和读取：

```csharp
using Frame.Core;
using Frame.Save;

ISaveService save = Framework.Resolve<ISaveService>();
save.Save("player", new PlayerSave { Name = "A", Level = 3 });

PlayerSave data = save.Load("player", new PlayerSave());
```

异步读取：

```csharp
SaveLoadResult<PlayerSave> result = await save.TryLoadAsync<PlayerSave>("player");
if (result.Success)
{
    PlayerSave data = result.Data;
}
```

启用加密和迁移：

```csharp
save.SetEncryptor(new AesSaveEncryptor("project-secret"));

save.RegisterMigration(new SaveMigration<PlayerSave>(1, 2, oldData =>
{
    oldData.Level = Math.Max(1, oldData.Level);
    return oldData;
}));
```

### 设计和实现

默认序列化器是 `NewtonsoftSaveSerializer`，默认保存到 `Application.persistentDataPath/FrameSettings.SaveFolderName`。保存时流程如下：

1. 校验 slot。
2. 序列化数据。
3. 如果设置了 `ISaveEncryptor`，先加密 payload。
4. 创建 `SaveMetadata`，记录 slot、序列化扩展名、数据版本、UTC 时间、payload 大小、SHA256、是否加密。
5. 写入临时存档文件 `.tmp` 和临时 metadata 文件 `.meta.tmp`。
6. 如果旧存档存在，先复制旧 metadata，再用 `File.Replace(temp, path, backup, true)` 原子替换并生成 `.bak`。
7. 替换 metadata。
8. 如果过程中失败，删除临时文件。

读取时先尝试主文件，失败再尝试 `.bak`。如果 metadata 存在，会校验 payload 大小和 SHA256。如果 metadata 标记为加密但当前没有 encryptor，会抛出并记录异常。反序列化成功后按注册的迁移链从旧版本逐步迁移到新版本。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ISaveService` | 存档服务接口 | 设置序列化器/加密器、迁移、同步/异步保存读取、删除、列槽、metadata、路径 |
| `SaveService` | 存档实现 | 临时文件、备份、metadata、SHA256、加密、迁移、fallback |
| `ISaveSerializer` | 序列化器接口 | FileExtension、Serialize、Deserialize |
| `TextSaveSerializer` | 文本序列化器基类 | UTF-8 字节和文本互转，子类实现文本序列化 |
| `NewtonsoftSaveSerializer` | JSON 序列化器 | 默认实现，Indented JSON，支持 enum 字符串 |
| `BinarySaveSerializer` | 二进制序列化器 | 基于 `DataContractSerializer` 的 binary XML |
| `ISaveEncryptor` | 加密器接口 | Encrypt、Decrypt |
| `AesSaveEncryptor` | AES 加密器 | passphrase 或 key，写入自定义 header 和 IV |
| `ISaveVersionedData` | 版本化数据接口 | 数据类实现后保存时自动使用 `SaveVersion` |
| `SaveMigration<TData>` | 数据迁移规则 | fromVersion、toVersion、Func 迁移函数 |
| `SaveLoadResult<TData>` | 异步读取结果 | Success 和 Data |
| `SaveMetadata` | 存档 metadata | 格式版本、slot、扩展名、数据版本、保存时间、大小、SHA256、加密标记 |
| `SaveSlotInfo` | 存档槽信息 | 列槽时返回路径、大小、时间、metadata 状态 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Save` 的实际源码文件展开。Save 模块把“数据序列化”“可选加密”“原子替换”“备份恢复”“metadata 校验”“版本迁移”拆成独立类型，`SaveService` 负责把它们串起来。

#### `ISaveSerializer.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `FileExtension` | 存档文件扩展名 | 实现类返回 `.json`、`.bin` 等 | `SaveService.GetPath()` 使用 |
| `Serialize<TData>(TData data)` | 对象转 bytes | 实现类决定格式 | Save 前调用 |
| `Deserialize<TData>(byte[] bytes)` | bytes 转对象 | 实现类决定格式 | Load 时调用 |

#### `ISaveEncryptor.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Encrypt(byte[] bytes)` | 加密 payload | 实现类返回加密 bytes | serializer 之后、metadata 创建之前调用 |
| `Decrypt(byte[] bytes)` | 解密 payload | 实现类返回明文 bytes | deserialize 之前调用 |

#### `ISaveVersionedData.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `SaveVersion` | 数据版本 | SaveService 自动读取 | 实现后可省略 Save(slot, data, version) 的手动版本参数 |

#### `TextSaveSerializer.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `FileExtension` | 文本序列化扩展名 | 抽象属性 | 子类实现 |
| `TextEncoding` | 文本编码 | virtual，默认 UTF-8 | 子类可 override |
| `Serialize<TData>(TData data)` | 文本转 bytes | 调 `SerializeToText(data)`，null 文本转空字符串，按 TextEncoding.GetBytes | 子类只关心文本格式 |
| `Deserialize<TData>(byte[] bytes)` | bytes 转文本再反序列化 | null/空 bytes 转空字符串，否则按 TextEncoding.GetString，再 `DeserializeFromText<TData>` | JSON serializer 继承它 |
| `SerializeToText/DeserializeFromText` | 文本序列化抽象方法 | 子类实现 | 只处理 string 层 |

#### `NewtonsoftSaveSerializer.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| 构造函数 | 创建 JSON serializer | 无参构造使用 `CreateDefaultSettings()`；有参构造 settings 为空也用默认 | 默认 SaveService 使用它 |
| `FileExtension` | 扩展名 | 返回 `.json` | 存档文件默认 JSON |
| `SerializeToText<TData>` | JSON 序列化 | `JsonConvert.SerializeObject(data, Formatting.Indented, settings)` | 便于人工查看 |
| `DeserializeFromText<TData>` | JSON 反序列化 | `JsonConvert.DeserializeObject<TData>(text, settings)` | 缺失字段忽略 |
| `CreateDefaultSettings()` | 默认 JSON 配置 | DefaultContractResolver；允许非 public 默认构造；include 默认值和 null；Replace 对象创建；忽略循环引用；禁用 TypeNameHandling；添加 StringEnumConverter | 防止类型名注入，enum 保存为字符串 |

#### `BinarySaveSerializer.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| 构造函数 | 创建二进制 serializer | 无参用默认 settings；有参 settings 为空也用默认 | 适合 DataContractSerializer 支持的数据 |
| `FileExtension` | 扩展名 | 返回 `.bin` | 切换 serializer 后槽文件扩展名会变化 |
| `Serialize<TData>` | 二进制 XML 序列化 | DataContractSerializer 写入 XmlDictionaryWriter binary writer，再返回 MemoryStream bytes | 不是自定义裸二进制，而是 binary XML |
| `Deserialize<TData>` | 二进制 XML 反序列化 | bytes 空抛异常；创建 binary reader 并 ReadObject | 类型必须兼容 DataContractSerializer |
| `CreateSerializer(Type type)` | 创建 DataContractSerializer | 用保存的 settings | 内部复用 |
| `CreateDefaultSettings()` | 默认设置 | `PreserveObjectReferences = true` | 支持对象引用关系 |

#### `AesSaveEncryptor.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Header` | 加密文件头 | 固定 bytes `{70,83,65,69,83,1}` | 解密时校验格式 |
| `AesSaveEncryptor(string passphrase)` | 口令构造 | `HashPassphrase(passphrase)` 后转 key 构造 | 空口令抛异常 |
| `AesSaveEncryptor(byte[] key)` | key 构造 | key 空抛异常；`NormalizeKey` 转合法 AES key 长度 | 支持 16/24/32 字节或任意 bytes 哈希 |
| `Encrypt(byte[] bytes)` | AES 加密 | null 明文转空数组；Aes.Create；设置 key；生成 IV；写 Header、IV 长度、IV；CryptoStream 写密文；返回完整 bytes | 每次加密 IV 不同 |
| `Decrypt(byte[] bytes)` | AES 解密 | 校验长度、Header、IV 长度；取 IV；Aes.Create 设置 key/IV；CryptoStream 解密到 MemoryStream | header/IV 错误抛 InvalidDataException |
| `HashPassphrase(string passphrase)` | 口令转 key | 空抛异常；SHA256(UTF8 passphrase) | 得到 32 字节 key |
| `NormalizeKey(byte[] key)` | 规范化 key | 长度 16/24/32 直接复制；否则 SHA256(key) | 保证 AES key 合法 |

#### `SaveMigration.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `SaveMigration(int fromVersion, int toVersion, Func<TData,TData> migrate)` | 创建迁移规则 | toVersion 必须大于 fromVersion；migrate 不能为空；保存版本和函数 | 可注册多段迁移链 |
| `FromVersion/ToVersion` | 版本范围 | 构造后私有 set | SaveService 按 FromVersion 排序和匹配 |
| `Apply(TData data)` | 执行迁移 | 调 migrate(data) | 迁移异常会让读取失败并记录 |

#### `SaveLoadResult.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `SaveLoadResult(bool success, TData data)` | 创建读取结果 | 保存 Success/Data | 异步 TryLoad 返回 |
| `Success` | 是否读取成功 | 只读属性 | false 时 Data 为 default |
| `Data` | 读取数据 | 只读属性 | 调用方按 Success 判断 |

#### `SaveMetadata.cs`

| 字段/属性 | 含义 |
| --- | --- |
| `FormatVersion` | metadata 格式版本，当前 1 |
| `Slot` | 存档槽名 |
| `SerializerExtension` | serializer 扩展名 |
| `DataVersion` | 数据版本 |
| `SavedAtUtcTicks` / `SavedAtUtc` | UTC 保存时间 |
| `PayloadSizeBytes` | payload bytes 长度 |
| `PayloadSha256` | payload SHA256 |
| `Encrypted` | payload 是否加密 |

#### `SaveSlotInfo.cs`

| 字段/属性 | 含义 |
| --- | --- |
| `Slot` | 槽名 |
| `Path` | 文件路径 |
| `LastWriteUtcTicks` / `LastWriteUtc` | 文件最后写入 UTC 时间 |
| `SizeBytes` | 文件大小 |
| `HasMetadata` | 是否读取到 metadata |
| `DataVersion` | metadata 中的数据版本 |
| `Encrypted` | metadata 中的加密标记 |
| `SerializerExtension` | metadata 中的序列化扩展名 |

#### `ISaveService.cs`

`ISaveService` 是业务入口。同步方法适合小数据或编辑器工具，异步方法适合运行时避免卡顿。

| 成员组 | 作用 | 注意点 |
| --- | --- | --- |
| `SetSerializer/SetEncryptor` | 替换序列化器和加密器 | serializer 传 null 会被忽略，encryptor 可传 null 关闭加密 |
| `RegisterMigration/ClearMigrations` | 管理版本迁移 | 按 TData 类型隔离 |
| `Exists/GetPath` | 查询文件存在和路径 | slot 会校验并清理非法文件名 |
| `Save/SaveAsync` | 保存数据 | 可自动从 `ISaveVersionedData` 取版本，也可手动传 dataVersion |
| `TryLoad/TryLoadAsync/Load/LoadAsync` | 读取数据 | TryLoad 失败不抛，Load 失败返回 fallback |
| `Delete` | 删除主文件、备份和 metadata | 返回是否删除了任何文件 |
| `ListSlots` | 列出当前 serializer 扩展名的槽 | 切换 serializer 后只列当前扩展名 |
| `TryGetMetadata` | 读取 metadata | metadata 文件不存在或解析失败返回 false |

#### `SaveService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| 常量 | 临时/备份/metadata 约定 | `.tmp`、`.bak`、`.meta`，metadata version 1 | 文件布局由这里决定 |
| `Priority` | 模块优先级 | 返回 `-100` | Config 之前，默认模块之前 |
| `OnInitialize()` | 初始化服务 | 默认 serializer 为 `NewtonsoftSaveSerializer`；saveRoot 为 `Application.persistentDataPath/SaveFolderName`；创建目录；注册服务 | 保存路径稳定在持久化目录 |
| `SetSerializer(ISaveSerializer serializer)` | 替换序列化器 | serializer 非空才替换 | 会影响后续 GetPath 扩展名 |
| `SetEncryptor(ISaveEncryptor encryptor)` | 替换加密器 | 直接赋值，可为 null | 新保存文件 metadata.Encrypted 取决于当前 encryptor |
| `RegisterMigration<TData>` | 注册迁移 | migration 空抛异常；按 TData 类型取列表；加入后按 FromVersion 排序 | ApplyMigrations 会按当前版本一段段匹配 |
| `ClearMigrations<TData>()` | 清该类型迁移 | 从 migrations 字典移除 | 不影响其他类型 |
| `Exists(string slot)` | 文件是否存在 | ValidateSlot 后 `File.Exists(GetPath(slot))` | 只查当前 serializer 扩展名主文件 |
| `Save<TData>(slot,data)` | 同步保存自动版本 | 调 `ResolveDataVersion(data)` 后 Save | 数据实现 ISaveVersionedData 时自动取版本 |
| `Save<TData>(slot,data,dataVersion)` | 同步保存核心 | 计算 path/temp/meta/backup；序列化并可加密；CreateMetadata；写 temp payload 和 temp metadata；已有主文件时 BackupMetadata 并 `File.Replace`，否则 Move；ReplaceMetadata；失败删除临时文件并重抛 | 用临时文件和备份降低写坏风险 |
| `SaveAsync<TData>(...)` | 异步保存 | 同步流程的 async 版本，写文件用 `File.WriteAllBytesAsync` 和 `WriteMetadataAsync`，支持 cancellation | 取消或异常会删除临时文件 |
| `TryLoad<TData>(slot,out data)` | 同步读取 | 校验 slot；先 TryLoadFromPath 主文件，失败再读 `.bak` | 主文件坏时可 fallback 备份 |
| `TryLoadAsync<TData>` | 异步读取 | 主文件 async 读取失败再读备份 | OperationCanceledException 会向外抛 |
| `Load/LoadAsync` | 读取或 fallback | TryLoad 成功返回 data，否则 fallback | 简化业务调用 |
| `Delete(string slot)` | 删除槽 | 删除主文件、备份、主 metadata、备份 metadata，任一删除则 true | 不抛找不到文件 |
| `ListSlots()` | 列槽 | 目录不存在返回空；按当前 serializer 扩展名找文件；创建 SaveSlotInfo；若 TryReadMetadata 成功则补 metadata 信息 | 只列主文件，不列 `.bak` |
| `TryGetMetadata(string slot,out metadata)` | 读取 metadata | 校验 slot；TryReadMetadata(GetPath(slot)) | 不读备份 metadata |
| `GetPath(string slot)` | 获取槽路径 | ValidateSlot；`FramePathUtility.SanitizeFileName(slot) + extension`；Combine saveRoot | 防止非法文件名 |
| `OnShutdown()` | 关闭服务 | 清 migrations，serializer/encryptor/saveRoot 置 null | 不删除文件 |
| `TryLoadFromPath<TData>` | 同步路径读取 | 路径无效或不存在 false；读 bytes；metadata 文件存在但解析失败则 false；metadata 有效则 ValidatePayload；按 metadata.Encrypted 或当前 encryptor 判断是否解密；反序列化；ApplyMigrations | 捕获异常并 `FrameLog.Exception`，返回 false |
| `TryLoadFromPathAsync<TData>` | 异步路径读取 | async 版本；读 metadata 用 `File.ReadAllTextAsync` 和 JsonUtility；取消异常重抛，其他异常记录后返回失败 | 与同步路径保持同样校验 |
| `CreateMetadata(...)` | 创建 metadata | 填 format/slot/extension/version/UTC/size/SHA256/encrypted | dataVersion 小于 0 会变 0 |
| `ApplyMigrations<TData>` | 应用迁移链 | 找不到列表直接返回；currentVersion 从 metadata version 开始；循环找 FromVersion 等于 currentVersion 的第一条，Apply 后更新 currentVersion，直到无匹配 | 支持 1->2->3 链式迁移 |
| `BackupMetadata(path, backupPath)` | 备份 metadata | 主 metadata 存在则 Copy 到备份 metadata；否则如果旧备份 metadata 存在就删除 | 保持 payload 备份和 metadata 备份一致 |
| `ReplaceMetadata(tempMetadataPath, metadataPath)` | 替换 metadata | 目标存在先 Delete，再 Move temp | metadata 不用 File.Replace |
| `WriteMetadataAsync/WriteMetadata` | 写 metadata | `JsonUtility.ToJson(metadata, true)`，UTF-8 写入 | metadata 使用 Unity JsonUtility |
| `TryReadMetadata(string path,out metadata)` | 读取 metadata | path + `.meta` 不存在 false；读文本并 FromJson；异常记录后 false | path 是 payload 路径，不是 metadata 路径 |
| `ValidatePayload(...)` | 校验 payload | metadata 或 hash 空则 true；校验 size；计算 SHA256 比较；不匹配写 warning 并 false | 防止文件损坏或不完整写入 |
| `ResolveDataVersion<TData>` | 解析数据版本 | data as `ISaveVersionedData`，没有则 0，有则 Max(0, SaveVersion) | 自动 Save 版本入口 |
| `GetMetadataPath(string path)` | metadata 路径 | `path + ".meta"` | 主文件和备份都可套用 |
| `ComputeSha256(byte[] bytes)` | 计算 SHA256 字符串 | null 当空数组；逐字节 `x2` 小写 hex | metadata 校验使用 |
| `GetSerializerFileExtension()` | 当前扩展名 | serializer 或 extension 空时 `.save`；否则 trim，并确保以 `.` 开头 | 自定义 serializer 可返回 `json` 或 `.json` |
| `ValidateSlot(string slot)` | 校验槽名 | 空白抛 `ArgumentException` | 所有路径入口先校验 |

## Config 模块

Config 模块负责读取配置，支持多 Provider、优先级、缓存、运行时覆盖和配置校验。

### 使用方式

默认 JSON 配置通过 `IAssetService` 加载；Resources 后端对应路径：

```text
Assets/AnyFolder/Resources/Configs/items.json
```

```csharp
IConfigService configs = Framework.Resolve<IConfigService>();
ItemConfig item = configs.Load<ItemConfig>("items");
```

运行时覆盖配置：

```csharp
RuntimeJsonConfigProvider runtime = new RuntimeJsonConfigProvider();
runtime.Set("items", new ItemConfig());
configs.RegisterProvider(runtime);
```

配置校验：

```csharp
public sealed class ItemConfig : IConfigValidator
{
    public string Id;

    public bool Validate(out string error)
    {
        error = string.IsNullOrWhiteSpace(Id) ? "Id is empty." : null;
        return error == null;
    }
}
```

### 设计和实现

`ConfigService` 初始化时会尝试解析 `IAssetService`。如果资源服务可用，默认注册两个 Provider：

1. `AssetScriptableConfigProvider`：按 `Configs/{key}` 路径加载单个 `ScriptableConfig` 资产。
2. `AssetJsonConfigProvider`：按 `Configs/{key}` 路径加载 JSON `TextAsset` 并反序列化。

Config 模块不再直接调用 `Resources.Load` 或 `Resources.LoadAll`；具体资源来源由当前 `IAssetService` 后端决定。Resources 后端下，`Configs/items` 对应 `Resources/Configs/items.json`。

用户调用 `RegisterProvider(provider)` 时，新 Provider 会插入列表头部，因此优先级高于默认 Provider。开启缓存时，缓存 key 是 `typeof(TConfig).FullName + ":" + key`。如果 Provider 实现 `IConfigChangeNotifier`，ConfigService 会订阅 `Changed`，变更时自动清理缓存。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IConfigService` | 配置服务接口 | Provider 注册、缓存、Load、TryLoad |
| `ConfigService` | 配置服务实现 | Provider 链、优先级、缓存、校验、变更清缓存 |
| `IConfigProvider` | 配置来源接口 | 按 key 和类型尝试加载配置 |
| `IConfigValidator` | 配置校验接口 | 配置对象可自校验，失败会拒绝加载 |
| `IConfigChangeNotifier` | Provider 变更通知 | 运行时配置变化时触发缓存失效 |
| `RuntimeJsonConfigProvider` | 运行时 JSON Provider | 内存字典，可 SetJson、Set<T>、Remove、Clear，支持 Changed |
| `AssetJsonConfigProvider` | 资源服务 JSON Provider | 通过 `IAssetService` 加载 `TextAsset` 并 Newtonsoft 反序列化 |
| `AssetScriptableConfigProvider` | 资源服务 Scriptable Provider | 通过 `IAssetService` 按路径加载单个 `ScriptableConfig` |
| `ScriptableConfig` | ScriptableObject 配置基类 | 提供 `Id`，适合编辑器资产配置 |
| `ScriptableConfigProvider` | 手动注册 ScriptableConfig Provider | 用字典按 Id 查找配置 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Config` 的实际源码文件展开。Config 模块采用 Provider 链：越靠前的 Provider 优先级越高，`ConfigService` 负责缓存、校验和变更失效。

#### `IConfigService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `CacheEnabled` | 是否启用配置缓存 | 实现类在 TryLoad 时读写 cache | 关闭后每次都询问 Provider |
| `RegisterProvider(IConfigProvider provider)` | 注册配置来源 | 实现类插入 Provider 链头部 | 后注册的 Provider 优先级高 |
| `UnregisterProvider(IConfigProvider provider)` | 移除配置来源 | 实现类从 Provider 链移除并清缓存 | 返回是否实际移除 |
| `ClearCache()` | 清空缓存 | 实现类清空字典 | 热更新或测试重置时使用 |
| `Load<TConfig>(string key)` | 强语义读取配置 | 实现类调用 TryLoad，失败写 warning 并返回 null | 适合必需配置，但不会抛异常 |
| `TryLoad<TConfig>(string key, out TConfig config)` | 尝试读取配置 | 实现类按 Provider 顺序查找 | 适合 fallback 逻辑 |

#### `IConfigProvider.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `TryLoad<TConfig>(string key, out TConfig config)` | 从某个来源读取配置 | Provider 自己决定 key 和类型如何映射 | 返回 false 表示该 Provider 没有命中或解析失败 |

#### `IConfigChangeNotifier.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Changed` | Provider 变更事件 | `ConfigService.SubscribeProvider()` 订阅它 | Provider 数据改变时触发，可自动清空服务缓存 |

#### `IConfigValidator.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Validate(out string error)` | 配置自校验 | `ConfigService.ValidateConfig()` 在 Provider 命中后调用 | 返回 false 会拒绝加载并写 warning |

#### `ScriptableConfig.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Id` | Scriptable 配置 id | 序列化字段 `id` 为空时回退到 asset `name` | `ScriptableConfigProvider` 用它作为字典 key |

#### `ScriptableConfigProvider.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Register(ScriptableConfig config)` | 注册一个 ScriptableConfig | null 直接返回；按 `config.Id` 写入字典 | 同 Id 会覆盖旧配置 |
| `TryLoad<TConfig>(string key, out TConfig config)` | 按 key 查找配置 | 字典命中后 `as TConfig`，类型匹配才 true | 适合手动构造测试配置或运行时注入 |
| `Clear()` | 清空注册表 | `configs.Clear()` | 不销毁 ScriptableObject 资产 |

#### `AssetScriptableConfigProvider.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `AssetScriptableConfigProvider(IAssetService assets, string rootPath = "Configs")` | 创建 Scriptable Provider | 保存资源服务引用和规范化 rootPath | 默认逻辑根目录是 `Configs` |
| `TryLoad<TConfig>(string key, out TConfig config)` | 加载 Scriptable 配置 | 仅当 `TConfig` 派生自 `ScriptableConfig` 时尝试；通过 `assets.TryLoad<ScriptableConfig>(rootPath/key)` 加载并 `as TConfig` | key 是资源路径，不再通过 `Resources.LoadAll` 按 Id 全量扫描 |

#### `AssetJsonConfigProvider.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `AssetJsonConfigProvider(IAssetService assets, string rootPath = "Configs")` | 创建 JSON Provider | 保存资源服务引用和规范化 rootPath | 默认逻辑根目录是 `Configs` |
| `TryLoad<TConfig>(string key, out TConfig config)` | 加载 JSON 配置 | 拼接 rootPath/key 并规范化；`assets.TryLoad<TextAsset>`；找到后用 `JsonConvert.DeserializeObject<TConfig>`；异常写 `FrameLog.Exception` 并返回 false；最后释放 `AssetHandle` | key 不需要写 `.json` 扩展名；实际路径规则由当前 `IAssetService` 后端决定 |

#### `RuntimeJsonConfigProvider.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Changed` | 数据变化事件 | Set/Remove/Clear 后通过 `RaiseChanged()` 触发 | ConfigService 订阅后会自动清缓存 |
| `Count` | 当前配置数量 | 返回字典 Count | 调试用 |
| `Keys` | 当前 key 集合 | 返回 `jsonByKey.Keys` | 字典使用忽略大小写比较 |
| `SetJson(string key, string json)` | 设置原始 JSON | key 规范化后为空抛异常；json 为空则 Remove；否则写入字典并 RaiseChanged | 支持运行时覆盖默认资产配置 |
| `Set<TConfig>(string key, TConfig config)` | 设置对象配置 | config 为空则 Remove；否则序列化后 SetJson | 使用 Newtonsoft 默认序列化 |
| `Remove(string key)` | 删除配置 | 规范化 key；空 key false；字典删除成功后 RaiseChanged | 删除不存在 key 不触发 Changed |
| `Clear()` | 清空全部配置 | 字典空时返回；否则清空并 RaiseChanged | 避免空清理触发无意义事件 |
| `Contains(string key)` | 查询 key | 规范化 key 后查字典 | 空 key false |
| `TryLoad<TConfig>(string key, out TConfig config)` | 读取运行时 JSON | 规范化 key；查字典；反序列化；结果非 null true；异常写日志并 false | 作为高优先级 Provider 可覆盖默认配置 |
| `RaiseChanged()` | 安全触发变更 | 取委托快照，null 返回，try/catch 调用 | 订阅者异常写 `FrameLog.Exception` |
| `NormalizeKey(string key)` | 规范化 key | 调 `FramePathUtility.NormalizeResourcesPath(key)` | 与默认配置 key 规则保持一致 |

#### `ConfigService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `CacheEnabled` | 是否启用缓存 | 自动属性，默认 true | Shutdown 后重置 true |
| `Priority` | 模块优先级 | 返回 `-200` | 晚于资源、场景、UI、音频等基础模块，早于默认 0 模块 |
| `OnInitialize()` | 初始化 Provider 链 | 若可解析 `IAssetService`，添加 `AssetScriptableConfigProvider` 和 `AssetJsonConfigProvider`，再注册 `IConfigService` 和自身实现 | 默认 Scriptable Provider 优先于 JSON Provider；关闭资源服务时只保留外部注册 provider |
| `RegisterProvider(IConfigProvider provider)` | 注册高优先级 Provider | provider 非空且不重复时插入 index 0；订阅变更；清缓存 | 运行时覆盖配置通常用它 |
| `UnregisterProvider(IConfigProvider provider)` | 移除 Provider | null false；移除成功后取消订阅并清缓存 | 返回是否移除 |
| `ClearCache()` | 清空缓存 | `cache.Clear()` | Provider 变更和注册/移除时都会调用 |
| `Load<TConfig>(string key)` | 读取配置 | TryLoad 成功返回；失败写 warning 并返回 null | warning 包含 key 和类型名 |
| `TryLoad<TConfig>(string key, out TConfig config)` | Provider 链读取 | 生成 cacheKey；缓存命中则转换返回；否则顺序询问 providers；命中后 `ValidateConfig`；通过校验后按需写缓存 | 缓存 key 是 `typeof(TConfig).FullName + ":" + key` |
| `OnShutdown()` | 关闭服务 | 取消所有 Provider 订阅；清空 providers/cache；`CacheEnabled = true` | 防止运行时 Provider 事件残留 |
| `SubscribeProvider(IConfigProvider provider)` | 订阅变更通知 | 如果 provider 实现 `IConfigChangeNotifier`，先减再加 `OnProviderChanged` | 减再加避免重复订阅 |
| `UnsubscribeProvider(IConfigProvider provider)` | 取消订阅 | provider 是 notifier 时移除事件 | Shutdown 和 unregister 使用 |
| `OnProviderChanged()` | Provider 变更处理 | 调 `ClearCache()` | 不主动重载配置，等下次 Load |
| `GetCacheKey<TConfig>(string key)` | 生成缓存 key | 类型 FullName 加冒号和 key | 同 key 不同类型不会冲突 |
| `ValidateConfig<TConfig>(string key, TConfig config)` | 配置校验 | 如果 config 不实现 `IConfigValidator` 返回 true；否则调用 Validate；失败写 warning 并 false | 校验失败不会写入缓存 |

## Input 模块

Input 模块统一输入上下文，优先支持 Unity Input System，未启用时退回 Legacy Input。

### 使用方式

切换上下文：

```csharp
using Frame.Core;
using Frame.Input;

IInputService input = Framework.Resolve<IInputService>();
input.SetContext(InputContext.Gameplay);

using (input.PushContext(InputContext.UI))
{
    // 临时进入 UI 输入模式
}
```

Input System：

```csharp
input.SetActions(inputActionsAsset);

if (input.WasPressedThisFrame("Jump"))
{
    // jump
}

Vector2 move = input.ReadVector2("Move");
```

重绑定保存：

```csharp
input.ApplyBindingOverride("Jump", 0, "<Keyboard>/enter");
string json = input.SaveBindingOverridesAsJson();
prefs.SetString("input.bindings", json);

input.LoadBindingOverridesFromJson(prefs.GetString("input.bindings", ""));
```

### 设计和实现

`InputService` 有当前上下文和上下文栈。`PushContext()` 会保存当前上下文并返回 `DisposableAction`，Dispose 时自动 Pop，适合打开 UI、暂停菜单、对话框等临时输入场景。

启用 Input System 时，服务管理一个 `InputActionAsset`。上下文为 `Gameplay` 时启用名为 `Player` 的 ActionMap；上下文为 `UI` 时启用名为 `UI` 的 ActionMap；`Disabled` 时禁用所有输入。未启用 Input System 时，只提供 `GetKey` 和 `GetKeyDown`，并在 `Disabled` 时返回 false。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IInputService` | 输入服务接口 | 上下文、InputActionAsset、Action 查询、按键读取、重绑定保存加载 |
| `InputService` | 输入服务实现 | 上下文栈、ActionMap 启停、Legacy fallback、binding override |
| `InputContext` | 输入上下文枚举 | `Disabled`、`Gameplay`、`UI` |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Input` 的实际源码文件展开。Input 模块使用条件编译：定义 `ENABLE_INPUT_SYSTEM` 时走 Unity Input System，否则只保留 Legacy `UnityEngine.Input` 的按键查询。

#### `InputContext.cs`

| 枚举值 | 含义 | 使用建议 |
| --- | --- | --- |
| `Disabled` | 禁用输入 | 弹窗锁定、过场动画、暂停输入时使用 |
| `Gameplay` | 游戏玩法输入 | 默认上下文，Input System 下启用名为 `"Player"` 的 ActionMap |
| `UI` | UI 输入 | 打开菜单、背包、对话框时使用，Input System 下启用名为 `"UI"` 的 ActionMap |

#### `IInputService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `CurrentContext` | 当前输入上下文 | 实现类保存当前枚举值 | 读当前输入模式 |
| `ContextStackDepth` | 上下文栈深度 | 实现类返回栈 Count | 调试 Push/Pop 是否配对 |
| `SetContext(InputContext context)` | 直接切换上下文 | 实现类设置 current 并应用 ActionMap 状态 | 不会记录历史 |
| `PushContext(InputContext context)` | 临时切换上下文 | 实现类把当前上下文压栈，并返回 Dispose 自动 Pop 的句柄 | 推荐 `using` 包住 UI 临时模式 |
| `PopContext()` | 恢复上一个上下文 | 栈空返回 false | Push/Pop 不配对时可通过返回值发现 |
| `SetActions(InputActionAsset actionAsset)` | 设置 Input System 资产 | 仅 `ENABLE_INPUT_SYSTEM` 下存在 | 会禁用旧 actions，再应用当前上下文 |
| `FindAction/WasPressedThisFrame/ReadVector2` | 查询 Action | 仅 Input System 下存在 | actionName 空或未找到时返回 null/false/zero |
| `ApplyBindingOverride/ClearBindingOverride/ClearBindingOverrides` | 重绑定管理 | 仅 Input System 下存在 | bindingIndex 越界返回 false |
| `SaveBindingOverridesAsJson/LoadBindingOverridesFromJson` | 重绑定持久化 | 仅 Input System 下存在 | 可配合 Preferences 保存 |
| `GetKey/GetKeyDown` | Legacy Input 查询 | 未启用 Input System 时存在 | `Disabled` 上下文下返回 false |

#### `InputService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `GameplayActionMapName` | Gameplay map 名称 | 常量 `"Player"` | InputActionAsset 里 ActionMap 名必须匹配 |
| `UIActionMapName` | UI map 名称 | 常量 `"UI"` | UI 上下文只启用该 map |
| `CurrentContext` | 当前上下文 | 返回 `currentContext`，默认 Gameplay | 初始化后即可查询 |
| `ContextStackDepth` | 栈深度 | 返回 `contextStack.Count` | 检查 PushContext 是否释放 |
| `OnInitialize()` | 注册服务 | 注册 `IInputService` 和自身实现 | 业务通过接口访问 |
| `SetContext(InputContext context)` | 设置上下文 | 保存 context；Input System 下调用 `ApplyActionMapState()` | Legacy 下只影响 `GetKey/GetKeyDown` 是否被 Disabled 阻断 |
| `PushContext(InputContext context)` | 压栈并切换 | `contextStack.Push(currentContext)`；SetContext；返回 `DisposableAction(() => PopContext())` | `using (input.PushContext(InputContext.UI))` 结束时自动恢复 |
| `PopContext()` | 弹栈恢复 | 栈空 false；否则 SetContext(pop) 并 true | 多层 UI 可以嵌套恢复 |
| `SetActions(InputActionAsset actionAsset)` | 设置 actions | 旧 actions 非空先 Disable；保存新 asset；应用当前 ActionMap 状态 | 切换输入资产时避免旧输入继续响应 |
| `FindAction(string actionName)` | 查找 action | actions 为空或 actionName 空返回 null；否则 `actions.FindAction(actionName, false)` | false 表示找不到不抛异常 |
| `WasPressedThisFrame(string actionName)` | 判断本帧按下 | 找到 action 后调用 `WasPressedThisFrame()` | 适合按钮触发 |
| `ReadVector2(string actionName)` | 读取 Vector2 | 找不到返回 `Vector2.zero`，否则 `ReadValue<Vector2>()` | 适合移动/摇杆 |
| `ApplyBindingOverride(string actionName, int bindingIndex, string overridePath)` | 应用绑定覆盖 | action 不存在、index 越界、overridePath 空白都 false；否则 `action.ApplyBindingOverride` | 成功后需要保存 JSON 才能持久化 |
| `ClearBindingOverride(string actionName, int bindingIndex)` | 清除单个绑定覆盖 | action 不存在或 index 越界 false；否则 `RemoveBindingOverride` | 用于恢复某个键位 |
| `ClearBindingOverrides()` | 清除所有绑定覆盖 | actions 非空时 `RemoveAllBindingOverrides()` | 恢复默认键位 |
| `SaveBindingOverridesAsJson()` | 保存绑定覆盖 JSON | actions 为空返回空字符串，否则调用 Input System API | 可写入 `IPreferencesService.SetString` |
| `LoadBindingOverridesFromJson(string json, bool removeExisting = true)` | 加载绑定覆盖 | actions 为空返回；json 空白时按 removeExisting 决定是否清空；否则加载 JSON 并应用 ActionMap 状态 | 加载后会重新启停 ActionMap，保证上下文生效 |
| `ApplyActionMapState()` | 应用当前上下文到 ActionMap | actions 为空返回；Disabled 时 Disable 全部；否则先 Enable asset，再遍历 actionMaps，仅启用匹配 `"Player"` 或 `"UI"` 的 map，禁用其他 map | 这是 Input System 路径的核心 |
| `GetKey(KeyCode key)` | Legacy 按键按住 | 当前上下文不是 Disabled 且 `UnityEngine.Input.GetKey(key)` | 仅非 Input System 编译路径 |
| `GetKeyDown(KeyCode key)` | Legacy 本帧按下 | 当前上下文不是 Disabled 且 `UnityEngine.Input.GetKeyDown(key)` | 仅非 Input System 编译路径 |
| `OnShutdown()` | 关闭输入服务 | 清空上下文栈；Input System 下禁用 actions 并置 null | 防止退出后输入资产仍启用 |

## Networking 模块

Networking 模块分成两条线：`HttpService` 封装 UnityWebRequest，提供 BaseUrl、默认 Header、Bearer Token、JSON 请求、重试、取消、统计事件和响应解析器；`SocketService` 管理 TCP Socket 和 WebSocket 长连接，提供连接状态、发送队列、消息事件、心跳、自动重连和收发指标。

### 使用方式

```csharp
using Frame.Core;
using Frame.Networking;

IHttpService http = Framework.Resolve<IHttpService>();
http.BaseUrl = "https://api.example.com";
http.SetBearerToken("access-token");
http.ResponseParser = new EnvelopeHttpResponseParser();

HttpRequestHandle handle = http.GetJson<PlayerDto>("players/me", response =>
{
    if (response.Success)
    {
        PlayerDto player = response.Value;
    }
    else
    {
        FrameLog.Warning(response.ErrorCode + " " + response.Error);
    }
});

handle.Cancel();
```

自定义请求：

```csharp
http.SendJson<LoginResponse>(new HttpRequest
{
    Url = "login",
    Method = HttpMethod.Post,
    Body = "{\"name\":\"a\"}",
    Retries = 2,
    TimeoutSeconds = 10
}, OnLogin);
```

TCP 长连接：

```csharp
ISocketService sockets = Framework.Resolve<ISocketService>();
ISocketClient client = sockets.CreateTcpClient("127.0.0.1", 9000, options =>
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

WebSocket：

```csharp
ISocketClient realtime = sockets.CreateWebSocketClient("wss://example.com/realtime", options =>
{
    options.WebSocketHeaders = new Dictionary<string, string>
    {
        ["Authorization"] = "Bearer " + accessToken
    };
    options.WebSocketSubProtocols = new List<string> { "game.v1" };
});

await realtime.ConnectAsync();
realtime.SendText("{\"op\":\"join\"}");
```

### 设计和实现

`HttpService.Send()` 会复制请求对象，合并默认 headers 和请求 headers，并解析相对 URL。请求使用 UniTask 异步发送。失败时按 `Retries` 重试，重试间隔使用 unscaled time。所有请求会更新 `ActiveRequestCount`、`StartedRequestCount`、`CompletedRequestCount`、`FailedRequestCount`，并触发 `RequestStarted` 和 `RequestCompleted`。

`HttpRequestHandle` 保存当前 UnityWebRequest，取消时调用 Abort。请求结束后会 detach，避免句柄继续持有已释放的 request。

响应解析：

- `JsonHttpResponseParser`：直接把 response.Text 反序列化为 `TData`，`string` 和 `byte[]` 有特殊处理。
- `EnvelopeHttpResponseParser`：适配 `{ success, code, message, data }` 这类统一协议。协议失败会设置 `Success = false`、`ErrorCode`、`Message`、`Error`。
- 如果响应不是 envelope，`EnvelopeHttpResponseParser` 会回退到普通 JSON 解析。

Socket 设计：

- `SocketService` 只负责创建、持有和释放多个 `ISocketClient`，不把业务协议耦进框架。
- `SocketClient` 针对 TCP 使用 `TcpClient`/`NetworkStream`，可选 `SslStream`；针对 WebSocket 使用 `ClientWebSocket`。
- TCP 是字节流，默认用 `LengthPrefixedSocketCodec` 加 4 字节大端长度头，避免粘包/拆包；业务可实现 `ISocketMessageCodec` 替换。
- WebSocket 自带消息帧，发送和接收时保留文本/二进制类型。
- 发送侧使用队列和后台 send loop；队列上限由 `SocketClientOptions.SendQueueLimit` 控制，超限计入 `DroppedMessages`。
- 接收、发送、心跳和重连都由 UniTask 后台 loop 驱动，回调通过 UniTask PlayerLoop 回到 Unity 主线程。
- 自动重连使用初始延迟、最大延迟和最大次数控制；主动 `DisconnectAsync` 或 `Dispose` 不会触发重连。
- WebGL 平台不能使用 .NET `TcpClient`/`ClientWebSocket`，需要单独的浏览器 WebSocket transport。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `IHttpService` | HTTP 服务接口 | 请求事件、BaseUrl、Headers、统计、GET/POST/Send/SendJson |
| `HttpService` | HTTP 实现 | UnityWebRequest、UniTask、headers 合并、retry、cancel、metrics |
| `HttpRequest` | 请求数据 | Url、Method、Body、ContentType、Timeout、Retries、RetryDelay、Headers |
| `HttpRequestHandle` | 请求句柄 | 可 `yield return`，可 Cancel，保存 Response、IsDone、IsCanceled |
| `HttpResponse` | 基础响应 | Success、StatusCode、Text、Data、Error、ErrorCode、Message |
| `HttpResponse<TData>` | 泛型响应 | 增加 `Value`，支持从基础响应和 parser 创建 |
| `IHttpResponseParser` | 响应解析接口 | 把基础响应解析为泛型业务响应 |
| `JsonHttpResponseParser` | 普通 JSON 解析器 | JSON 反序列化，支持 string/byte[] |
| `EnvelopeHttpResponseParser` | 统一协议解析器 | success/code/message/data 字段，可配置成功码和字段名 |
| `HttpMethod` | HTTP 方法枚举 | `Get`、`Post`、`Put`、`Delete` |
| `ISocketService` | Socket 服务接口 | 创建 TCP/WebSocket client，管理 client 列表和全部断开 |
| `SocketService` | Socket 服务实现 | 注册服务、创建 `SocketClient`、关闭时释放连接 |
| `ISocketClient` | 单个长连接接口 | 连接/断开/发送、状态、事件、指标 |
| `SocketClient` | TCP/WebSocket 客户端实现 | send/receive loop、心跳、重连、主线程事件派发 |
| `SocketClientOptions` | 长连接配置 | transport、endpoint、TLS、buffer、queue、reconnect、heartbeat、WebSocket header |
| `SocketMessage` | 收发消息 | byte[] payload + 文本/二进制类型 |
| `ISocketMessageCodec` | TCP 编解码接口 | Encode/TryDecode，解决字节流边界 |
| `LengthPrefixedSocketCodec` | 默认 TCP 编解码器 | 4 字节大端长度头 + payload |
| `SocketClientMetrics` | 长连接指标 | sent/received bytes/messages、reconnect、dropped |
| `SocketDisconnectInfo` | 断开信息 | reason + error |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Networking` 的实际源码文件展开。HTTP 路径用 `UnityWebRequest` 发请求，用 UniTask 驱动异步，外层用 `HttpRequestHandle` 给 coroutine 和取消流程提供统一句柄；Socket 路径用 `SocketService` 管理 TCP/WebSocket client，每个 client 独立处理连接、收发、心跳和重连。

#### `HttpMethod.cs`

| 枚举值 | 含义 |
| --- | --- |
| `Get` | HTTP GET |
| `Post` | HTTP POST |
| `Put` | HTTP PUT |
| `Delete` | HTTP DELETE |

#### `HttpRequest.cs`

`HttpRequest` 是请求数据对象，没有方法。`HttpService.PrepareRequest()` 会复制它，并合并默认 headers 与单次请求 headers。

| 字段 | 作用 | 默认值和注意点 |
| --- | --- | --- |
| `Url` | 请求地址 | 可为相对路径，`BaseUrl` 非空时会被补全 |
| `Method` | HTTP 方法 | 默认 `HttpMethod.Get` |
| `Body` | 请求体字符串 | POST/PUT 时转 UTF-8 bytes |
| `ContentType` | 请求体类型 | 默认 `"application/json"` |
| `TimeoutSeconds` | UnityWebRequest 超时 | 默认 15，发送前最小限制为 1 |
| `Retries` | 失败重试次数 | 默认 0，实际 attempts 是 `Retries + 1` |
| `RetryDelaySeconds` | 重试间隔 | 默认 0.25 秒，使用 unscaled time |
| `Headers` | 单次请求 headers | 忽略大小写 key，覆盖默认 headers |

#### `HttpResponse.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `HttpResponse` 字段 | 基础响应数据 | 保存 Success、StatusCode、Text、Data、Error、ErrorCode、Message | `ErrorCode/Message` 主要由 envelope parser 填充 |
| `HttpResponse<TData>.Value` | 解析后的业务值 | Parser 成功时设置 | 只有泛型响应才有 |
| `HttpResponse<TData>.From(HttpResponse response)` | 用默认 JSON parser 转泛型 | 调 `JsonHttpResponseParser.Instance.Parse<TData>(response)` | 适合普通 JSON |
| `HttpResponse<TData>.From(HttpResponse response, IHttpResponseParser parser)` | 用指定 parser 转泛型 | parser 为空时回退默认 JSON parser | `HttpService.SendJson` 使用 `ResponseParser` |
| `CreateFromBase(HttpResponse response)` | 复制基础响应字段 | response 为 null 时构造失败响应，否则复制基础字段 | Parser 先创建 typed 再填 `Value` |

#### `HttpRequestHandle.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `keepWaiting` | Coroutine 等待条件 | 返回 `!IsDone` | 可 `yield return handle` |
| `IsDone` | 请求是否完成 | `Complete()` 时置 true | 取消也会以完成响应结束 |
| `IsCanceled` | 是否已请求取消 | `Cancel()` 置 true | 发出后会 Abort 当前 webRequest |
| `Response` | 完成响应 | `Complete(response)` 设置 | 请求完成后读取 |
| `Cancel()` | 取消请求 | 已完成返回；置 `IsCanceled = true`；当前 webRequest 非空时 Abort | 取消是协作式，最终响应 Error 为 `"Request canceled."` |
| `Attach(UnityWebRequest request)` | 绑定当前 request | 保存 webRequest；如果句柄已取消则立即 Abort | SendAsync 每次尝试开始时调用 |
| `Detach(UnityWebRequest request)` | 解绑当前 request | 只有引用相等才清空 | 避免旧 request 结束后清掉新 request |
| `Complete(HttpResponse response)` | 完成句柄 | 保存 Response，清空 webRequest，置 IsDone | 只由 HttpService 调用 |

#### `IHttpResponseParser.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Parse<TData>(HttpResponse response)` | 把基础响应转成泛型响应 | 具体 parser 决定 JSON 或 envelope 解析规则 | 自定义协议可实现这个接口并赋给 `IHttpService.ResponseParser` |

#### `JsonHttpResponseParser.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Instance` | 默认 parser 单例 | static readonly 实例 | 避免重复 new |
| `Parse<TData>(HttpResponse response)` | 普通 JSON 解析 | 先 `CreateFromBase`；基础响应失败直接返回；`TData == string` 返回 Text；`TData == byte[]` 返回 Data；Text 空直接返回；否则 Newtonsoft 反序列化；异常时 `Success=false` 并写 Error | 适合后端直接返回业务对象的接口 |

#### `EnvelopeHttpResponseParser.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `SuccessCodes` | 成功码集合 | 默认包含 `"0"`、`"200"`、`"OK"`、`"Success"`，忽略大小写 | 可按项目后端协议添加 |
| `SuccessField/CodeField/MessageField/DataField` | envelope 字段名 | 默认 `success/code/message/data` | 后端字段名不同可直接改 |
| `TreatMissingSuccessFieldAsSuccess` | 缺少 success 是否视为成功 | 默认 true | 和缺少 code 的策略共同决定 |
| `TreatMissingCodeAsSuccess` | 缺少 code 是否视为成功 | 默认 true | 两者都 true 且 success/code 都缺失时成功 |
| `Parse<TData>(HttpResponse response)` | envelope 解析 | 先复制基础响应；基础失败或 Text 空直接返回；JObject.Parse 失败则回退普通 JSON；没有 envelope 字段也回退普通 JSON；读取 code/message；计算协议成功；失败时填 ErrorCode/Message/Error；成功时读取 data 并按 TData 解析 | 兼容普通 JSON 和统一协议 |
| `HasEnvelopeFields(JObject root)` | 判断是否像 envelope | 检查 success/code/message/data 任一字段存在 | 避免误把普通对象当 envelope |
| `HasField(JObject root, string fieldName)` | 判断字段存在 | root 非空、字段名非空、token 非空 | 字段名可配置为空 |
| `ResolveProtocolSuccess(JObject root, string code)` | 判断业务协议成功 | 优先读 success 字段：bool 直接返回，字符串先 bool.Parse 再查 SuccessCodes；无 success 但有 code 时查 SuccessCodes；两者都无时按两个 TreatMissing 开关 | 支持 boolean、字符串和 code 成功码 |
| `ReadString(JObject root, string fieldName)` | 读取字段字符串 | root/fieldName 无效返回 null；token null 返回 null；字符串 token 返回 Value，否则 ToString(Formatting.None) | code/message 都用它 |

#### `IHttpService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `RequestStarted` | 请求开始事件 | `HttpService.BeginRequest()` 触发 | 诊断、loading 统计可订阅 |
| `RequestCompleted` | 请求完成事件 | `HttpService.FinishRequest()` 触发 | 成功、失败、取消都会触发 |
| `BaseUrl` | 请求基础地址 | 实现类 `ResolveUrl()` 合并相对 URL | URL 已是绝对地址时不拼接 |
| `ResponseParser` | 泛型响应解析器 | `SendJson` 使用 | null 时回退普通 JSON parser |
| `DefaultHeaders` | 默认 headers | 实现类维护只读字典视图 | 单次 request headers 可覆盖 |
| Metrics 属性 | 请求统计 | Active/Started/Completed/Failed | 运行时诊断面板显示 |
| `ClearMetrics()` | 清空历史统计 | 不清 active，只清 started/completed/failed | active 表示当前进行中 |
| Header 方法 | 管理默认 headers | Set/Remove/Clear/BearerToken | bearer token 写 Authorization |
| 快捷请求方法 | GET/POST/JSON 请求 | 构造 `HttpRequest` 后转发 Send/SendJson | 简化常见用法 |
| `Send/SendJson` | 发送自定义请求 | Send 返回基础响应，SendJson 返回泛型响应 | SendJson 会走 parser |

#### `HttpService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `DefaultHeaders` | 默认 headers | 返回 `defaultHeaders` 的只读接口 | 字典本身忽略 key 大小写 |
| Metrics 属性 | 请求统计 | 返回对应计数字段 | active 在请求开始加一，结束减一 |
| `OnInitialize()` | 注册服务 | 注册 `IHttpService` 和自身实现 | 业务通过接口访问 |
| `ClearMetrics()` | 清统计 | started/completed/failed 归零 | 不修改 active |
| `SetDefaultHeader(string name, string value)` | 设置默认 header | name 空抛异常；value null 删除；否则写字典 | 可用于认证、客户端版本等 |
| `RemoveDefaultHeader(string name)` | 删除默认 header | name 非空且 Remove 成功 true | bearer token 清理也走它 |
| `ClearDefaultHeaders()` | 清空默认 headers | 字典 Clear | 不影响已经发出的请求副本 |
| `SetBearerToken(string token)` | 设置 Authorization | token 空则删除 Authorization，否则写 `"Bearer " + token` | 常见登录态接口使用 |
| `Get/GetJson/PostJson/PostJson<TRequest,TResponse>` | 快捷请求 | 构造 `HttpRequest` 后调用 Send 或 SendJson；泛型 POST 会 Newtonsoft 序列化 body | completed 可为空 |
| `Send(HttpRequest request, Action<HttpResponse> completed)` | 发送基础请求 | 创建 handle；`PrepareRequest` 复制并解析 URL/headers；启动 `SendAsync(...).Forget()`；返回 handle | request 为空也会返回 handle，异步中完成失败响应 |
| `SendJson<TResponse>(...)` | 发送并解析泛型响应 | 调 Send，基础响应完成后用 `HttpResponse<TResponse>.From(response, ResponseParser)` 解析，再调用 completed | completed 抛异常会被 Complete 外层捕获 |
| `OnShutdown()` | 关闭服务 | 清 headers、BaseUrl、ResponseParser、metrics、事件 | 不主动 Abort 已发请求，因为没有集中句柄列表 |
| `SendAsync(...)` | 核心异步发送 | BeginRequest；URL 空直接失败；按 Retries 计算 attempts；每次创建 UnityWebRequest、Attach handle、设置 timeout/headers；发送并创建 response；失败且还有次数则按 unscaled delay 等待；捕获异常；取消时覆盖为取消响应；最后 FinishRequest 和 Complete | retry 只按 finalResponse.Success 判断，HTTP 错误和网络错误都会重试 |
| `CreateUnityRequest(HttpRequest request)` | 创建 UnityWebRequest | Method Post/Put 走 upload request；Delete 走 `UnityWebRequest.Delete`；默认 Get | DELETE 当前没有 body |
| `CreateUploadRequest(string method, HttpRequest request)` | 创建带 body 请求 | new UnityWebRequest；Body 转 UTF-8；设置 UploadHandlerRaw 和 DownloadHandlerBuffer；设置 Content-Type | POST/PUT 共用 |
| `CreateResponse(UnityWebRequest webRequest)` | 从 UnityWebRequest 创建响应 | null 返回失败；否则 Success 取 `webRequest.result == Success`，复制 responseCode/text/data/error | 只表示传输层成功，业务协议由 parser 判断 |
| `PrepareRequest(HttpRequest source)` | 复制并合并请求 | null 返回 null；复制所有字段；ResolveUrl；先拷默认 headers，再拷 source headers 覆盖 | 避免异步发送期间调用方修改原 request |
| `ResolveUrl(string url)` | 解析绝对 URL | url/BaseUrl 空或 url 已绝对时返回原 url；否则拼 `BaseUrl.TrimEnd('/') + "/" + url.TrimStart('/')` | 避免重复斜杠 |
| `BeginRequest(HttpRequest request)` | 开始统计和事件 | active、started 加一；安全触发 RequestStarted | 即使 request 为 null 也会统计一次开始 |
| `FinishRequest(HttpRequest request, HttpResponse response)` | 完成统计和事件 | active 最小到 0；completed 加一；response 失败则 failed 加一；安全触发 RequestCompleted | 取消也算 completed 和 failed |
| `Complete(HttpRequestHandle handle, Action<HttpResponse> completed, HttpResponse response)` | 完成句柄和回调 | handle 非空则 Complete；completed 非空则 try/catch 调用 | 回调异常写 `FrameLog.Exception` |

#### `SocketTransportType.cs`、`SocketClientState.cs`、`SocketDisconnectReason.cs`、`SocketMessageKind.cs`

| 类型 | 作用 |
| --- | --- |
| `SocketTransportType` | 区分 `Tcp` 和 `WebSocket` |
| `SocketClientState` | `Disconnected/Connecting/Connected/Disconnecting/Reconnecting` |
| `SocketDisconnectReason` | `Local/Remote/Error/Timeout/Canceled/ReconnectFailed/Shutdown` 等断开原因 |
| `SocketMessageKind` | 区分二进制和文本消息 |

#### `SocketMessage.cs`

| 成员 | 作用 | 实现方式和注意点 |
| --- | --- | --- |
| `Data` / `Kind` / `Count` | 消息 payload 和类型 | 构造时复制 byte[]，避免外部修改发送内容 |
| `Text` | UTF-8 文本视图 | 直接把 Data 按 UTF-8 解码 |
| `Binary(byte[] data)` | 创建二进制消息 | 对 data 做防御性复制 |
| `TextMessage(string text)` | 创建文本消息 | 使用 UTF-8 编码，null 视为空字符串 |
| `WrapUnsafe(byte[] data, SocketMessageKind kind)` | 内部快速包装 | 接收路径使用，调用方不应修改 payload |

#### `SocketReceiveBuffer.cs`

`SocketReceiveBuffer` 是 TCP 解码用的可增长缓冲。`Append()` 追加新读到的 bytes，`TryPeekInt32BigEndian()` 读取长度头，`TryRead()` 读取完整 payload，`Discard()` 丢弃已消费数据。它通过 offset/count 复用数组，空间不够时先尝试前移，再扩容。

#### `ISocketMessageCodec.cs` 和 `LengthPrefixedSocketCodec.cs`

| 成员 | 作用 | 实现方式和注意点 |
| --- | --- | --- |
| `Encode(SocketMessage message)` | 把消息编码成 TCP frame | 默认实现写 4 字节大端 payload 长度，再写 payload |
| `TryDecode(SocketReceiveBuffer buffer, out SocketMessage message)` | 从缓冲中尝试读完整消息 | 数据不足返回 false；长度非法或超过 `MaxPayloadBytes` 抛异常 |
| `MaxPayloadBytes` | 最大 payload 大小 | 默认 1 MB，可通过 `SocketClientOptions.MaxMessageSizeBytes` 调整 |

#### `SocketClientOptions.cs`

| 字段 | 作用 |
| --- | --- |
| `Transport`、`Host`、`Port`、`Url` | 连接类型和端点 |
| `UseTls`、`TlsHostName`、`CertificateValidationCallback` | TCP TLS 配置 |
| `NoDelay`、`ConnectTimeoutMilliseconds`、`ReceiveBufferSize`、`MaxMessageSizeBytes` | 连接和缓冲参数 |
| `SendQueueLimit`、`ClearSendQueueOnDisconnect` | 发送队列控制 |
| `AutoReconnect`、`MaxReconnectAttempts`、`ReconnectInitialDelaySeconds`、`ReconnectMaxDelaySeconds` | 自动重连策略 |
| `HeartbeatIntervalSeconds`、`HeartbeatTimeoutSeconds`、`HeartbeatPayload`、`HeartbeatKind` | 应用层心跳 |
| `Codec` | TCP 编解码器；为空时用 `LengthPrefixedSocketCodec` |
| `WebSocketHeaders`、`WebSocketSubProtocols` | WebSocket 握手参数 |

`Validate()` 会修正最小缓冲、超时和重连延迟，并校验 TCP host/port 或 WebSocket `ws://`/`wss://` URL。

#### `ISocketClient.cs`

| 成员 | 作用 |
| --- | --- |
| `StateChanged`、`Connected`、`Disconnected`、`Reconnecting`、`MessageReceived`、`Error` | 连接生命周期和消息事件 |
| `Id`、`Options`、`State`、`IsConnected`、`Metrics` | 基础状态和指标 |
| `ConnectAsync()` / `DisconnectAsync()` | 建立和关闭连接 |
| `Send(SocketMessage)`、`Send(byte[])`、`SendText(string)` | 入队发送消息 |
| `ClearMetrics()` | 清空历史指标 |

#### `SocketClient.cs`

`SocketClient` 是长连接核心实现。连接时根据 `SocketClientOptions.Transport` 选择 TCP 或 WebSocket，连接成功后启动 send loop、receive loop 和可选 heartbeat loop。

| 方法 | 作用 | 实现方式和注意点 |
| --- | --- | --- |
| `ConnectAsync()` | 建立连接 | 使用 `connectGate` 防并发连接；连接成功后切 `Connected` 并启动后台 loop |
| `DisconnectAsync()` | 主动断开 | 标记 local disconnect、取消连接 token、清发送队列、关闭底层 transport，不触发自动重连 |
| `Send(...)` | 入队发送 | 非 Connected 返回 false；队列满返回 false 并增加 dropped 指标 |
| `ConnectTcpAsync()` | TCP 连接 | `TcpClient.ConnectAsync` + timeout；可选 `SslStream.AuthenticateAsClientAsync` |
| `ConnectWebSocketAsync()` | WebSocket 连接 | 设置 headers/sub-protocol，`ClientWebSocket.ConnectAsync`；WebGL 非 Editor 抛平台不支持 |
| `SendLoopAsync()` | 后台发送 | 等待 `sendSignal`，按 transport 写 WebSocket frame 或 TCP codec frame |
| `ReceiveTcpLoopAsync()` | TCP 接收 | 读 stream 后追加到 `SocketReceiveBuffer`，循环 `codec.TryDecode` 抛出完整消息 |
| `ReceiveWebSocketLoopAsync()` | WebSocket 接收 | 合并分片直到 `EndOfMessage`，按 text/binary 创建 `SocketMessage` |
| `HeartbeatLoopAsync()` | 应用层心跳 | 定时发送 heartbeat payload；超过 `HeartbeatTimeoutSeconds` 未收到消息则按 timeout 断开 |
| `HandleConnectionFailureAsync()` | 异常断线处理 | 关闭 transport、派发 Disconnected；如果允许自动重连则进入 `ReconnectLoopAsync()` |
| `ReconnectLoopAsync()` | 自动重连 | 指数退避，受最大次数限制；成功后重新启动后台 loop |
| `Dispatch()` | 主线程事件派发 | 主线程直接执行，后台线程通过 `PlayerLoopHelper.AddContinuation` 回到 Update |

#### `ISocketService.cs` 和 `SocketService.cs`

| 成员 | 作用 | 实现方式和注意点 |
| --- | --- | --- |
| `Priority = -90` | 模块优先级 | 晚于 Save(-100)，早于默认 0 优先级模块 |
| `Clients` / `ActiveConnectionCount` | 客户端列表和活跃连接数 | Runtime Diagnostics Overlay 使用 |
| `OnInitialize()` | 注册服务 | 注册 `ISocketService` 和自身实现 |
| `CreateClient()` | 创建通用客户端 | 克隆并校验 options 后加入列表 |
| `CreateTcpClient()` / `CreateWebSocketClient()` | 便捷创建 | 构造默认 options 后执行 configure |
| `RemoveClient()` | 移除客户端 | 可选择同时 Dispose 连接 |
| `DisconnectAllAsync()` | 异步断开全部连接 | 模块关闭或业务退出登录时使用 |
| `OnShutdown()` | 模块关闭 | Dispose 所有 client 并清空列表 |

## Localization 模块

Localization 是轻量文本本地化模块，支持多表、fallback locale、格式化参数、缺失 key 记录和 UI 文本绑定。

### 使用方式

创建 `LocalizedTextTable`：

```csharp
LocalizedTextTable table = ScriptableObject.CreateInstance<LocalizedTextTable>();
table.ImportCsv("key,en,zh\nhello,Hello {0},你好 {0}");
```

使用服务：

```csharp
ILocalizationService loc = Framework.Resolve<ILocalizationService>();
loc.AddTable(table);
loc.FallbackLocale = "en";
loc.SetLocale("zh");

string text = loc.Translate("hello", null, "Oujie");
```

UI 绑定：

```csharp
LocalizedText localized = labelGameObject.GetComponent<LocalizedText>();
localized.SetKey("hello");
localized.Bind(loc);
```

### 设计和实现

`LocalizationService` 内部保存 table 列表。翻译时从后往前查找表，因此后添加的表优先级更高。当前语言查不到时查 fallback language。仍查不到时把 key 加入 `MissingKeys`，返回 fallback 或 key 本身。格式化使用 `string.Format`，异常会记录并返回原模板。

`LocalizedTextTable` 使用 CSV/TSV 表格文本作为数据源，第一列为 key，后续列为 locale。资源可以直接绑定 Excel 导出的 `TextAsset`，也可以用 `ImportCsv`/`ImportTsv` 导入整张表格文本。

表内部会构建 `Dictionary<locale, Dictionary<key, value>>` 缓存，避免每次翻译线性遍历。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `ILocalizationService` | 本地化服务接口 | 当前语言、fallback、缺失 key、表管理、翻译 |
| `LocalizationService` | 本地化实现 | 多表优先级、fallback、格式化、缺失 key、事件异常隔离 |
| `LocalizedTextTable` | 本地化文本表 | ScriptableObject，支持 CSV、TSV、TextAsset 源、字典缓存 |
| `LocalizedText` | UI 文本绑定组件 | 绑定 `UnityEngine.UI.Text`，语言变化时刷新 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Localization` 的实际源码文件展开。Localization 模块由三部分组成：服务负责多表优先级和 fallback，`LocalizedTextTable` 负责表数据和解析，`LocalizedText` 负责 UI Text 自动刷新。

#### `ILocalizationService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `LocaleChanged` | 当前语言变化事件 | `LocalizationService.SetLocale()` 触发 | UI 绑定组件订阅它刷新文本 |
| `CurrentLocale` | 当前语言 | 实现类保存字符串 | 默认 `"en"` |
| `FallbackLocale` | fallback 语言 | 实现类可读写，空白会变 null | 当前语言找不到 key 时使用 |
| `MissingKeys` | 缺失 key 集合 | 实现类保存 HashSet | 翻译失败时记录，便于测试和导出 |
| `SetLocale(string locale)` | 切换语言 | 实现类 trim 后判断变化 | 空白或相同语言不触发事件 |
| `AddTable/RemoveTable/ClearTables` | 管理文本表 | 实现类维护表列表 | 后添加表优先级更高 |
| `ClearMissingKeys()` | 清空缺失记录 | 清 HashSet | 切场景或重新测试前使用 |
| `TryTranslate(string key, out string value)` | 尝试翻译 | 先当前语言，再 fallback 语言 | 不记录 missing key |
| `Translate(string key, string fallback = null, params object[] args)` | 翻译并格式化 | 找不到时记录 missing key，返回 fallback 或 key | args 传给 `string.Format` |

#### `LocalizationService.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `CurrentLocale` | 当前语言 | 返回 `currentLocale` | 默认 `"en"` |
| `FallbackLocale` | fallback 语言 | setter 空白转 null，否则 Trim | 可关闭 fallback |
| `MissingKeys` | 缺失 key 集合 | 返回 HashSet 的只读接口 | 不保证顺序 |
| `OnInitialize()` | 注册服务 | 注册 `ILocalizationService` 和自身实现 | 业务通过接口访问 |
| `SetLocale(string locale)` | 切换语言 | 空白转 null；空或和 current 相同则返回；更新 current；安全触发 LocaleChanged | 订阅者异常写 `FrameLog.Exception` |
| `AddTable(LocalizedTextTable table)` | 添加文本表 | null 返回；先 Remove 再 Add | 重复添加会移动到最高优先级 |
| `RemoveTable(LocalizedTextTable table)` | 移除文本表 | table 非空且列表移除成功 true | 不销毁 ScriptableObject |
| `ClearTables()` | 清空表列表 | `tables.Clear()` | 不清 missing keys |
| `ClearMissingKeys()` | 清空缺失记录 | `missingKeys.Clear()` | 用于调试 |
| `TryTranslate(string key, out string value)` | 翻译不记录缺失 | key 空 false；先 `TryTranslate(currentLocale, key)`；失败且 fallback 有效且不同于当前语言，则试 fallback | 适合判断是否存在文案 |
| `Translate(string key, string fallback = null, params object[] args)` | 翻译并格式化 | key 空时格式化 fallback；TryTranslate 成功格式化 value；失败加入 missingKeys，格式化 fallback 或 key | string.Format 异常会记录并返回原模板 |
| `OnShutdown()` | 关闭服务 | 清表、清 missing、重置 locale/fallback、清事件 | 框架重启恢复默认 |
| `TryTranslate(string locale, string key, out string value)` | 内部按指定语言查表 | locale 空 false；从 `tables.Count - 1` 倒序查找，命中第一个返回 true | 后添加表覆盖前面的表 |
| `ApplyFormat(string template, object[] args)` | 格式化文本 | template 空或无 args 直接返回；否则 `string.Format`，异常写日志并返回 template | 避免格式错误打断 UI |

#### `LocalizedText.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Key` | 当前本地化 key | 返回序列化字段 | Inspector 或代码设置 |
| `Fallback` | 当前 fallback 文本 | 返回序列化字段 | key 缺失或服务不可用时显示 |
| `Target` | 绑定的 UI Text | getter 先 `ResolveTarget()` | 组件要求同对象有 `Text` |
| `SetKey(string localizationKey)` | 修改 key | 保存 key 并 Refresh | 动态切换文本 key 使用 |
| `SetFallback(string fallbackText)` | 修改 fallback | 保存 fallback 并 Refresh | 服务不可用时立即更新 |
| `Bind(ILocalizationService service)` | 绑定服务 | 如果已绑定同一服务则 Refresh；否则 Unbind，保存 service，订阅 LocaleChanged，再 Refresh | 可手动绑定测试服务 |
| `Unbind()` | 解绑服务 | 如果 localization 非空则取消订阅并置 null | OnDisable 调用，防止事件持有组件 |
| `Refresh()` | 刷新 UI 文本 | ResolveTarget；target 空返回；无服务且无法从 Framework 绑定时显示 fallback；有服务时 `localization.Translate(key, resolvedFallback)` 并写 target.text | fallback 空时传 null，Translate 缺失会返回 key |
| `Awake()` | 组件初始化 | ResolveTarget | 自动找到 Text |
| `OnEnable()` | 启用时绑定刷新 | TryBindFromFramework 后 Refresh | 支持运行时自动接入框架服务 |
| `Start()` | 启动后再次绑定刷新 | TryBindFromFramework 后 Refresh | 处理 OnEnable 时框架尚未初始化的情况 |
| `OnDisable()` | 禁用时解绑 | 调 Unbind | 防止隐藏对象继续订阅 LocaleChanged |
| `Reset()` | 编辑器重置 | `target = GetComponent<Text>()` | 添加组件时自动填充 |
| `OnValidate()` | 编辑器校验 | target 为空时补 Text；运行中且激活则 Refresh | 只在 Editor 编译 |
| `TryBindFromFramework()` | 从 Framework 自动解析服务 | 已绑定 true；`Framework.TryResolve(out service)` 成功则 Bind 并 true，否则 false | LocalizationService 关闭时会失败 |
| `ResolveTarget()` | 解析 Text 组件 | target 为空时 `GetComponent<Text>()` | 多处调用保证引用有效 |
| `OnLocaleChanged(string locale)` | 语言变化回调 | 调 Refresh | locale 参数当前未直接使用 |
| `GetFallbackText()` | 计算无服务 fallback | fallback 非空返回 fallback，否则 key 非空返回 key，否则空字符串 | 服务不可用时保证 UI 不显示 null |

#### `LocalizedTextTable.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `Locale` | 默认语言 | 返回 `Locales[0]`，没有则 `"en"` | `TryGet(key, out value)` 使用 |
| `Locales` | 可用语言列表 | `EnsureLookup()` 后返回 `availableLocales` | 如果绑定 source，会根据 source header 构建 |
| `TryGet(string locale, string key, out string value)` | 按语言和 key 查询 | locale/key 空 false；EnsureLookup；查 `lookup[locale][key]` | 查询是精确匹配，key 会在构建时 Clean |
| `TryGet(string key, out string value)` | 按默认语言查询 | 调 `TryGet(Locale, key, out value)` | 单语言表快速用 |
| `ContainsLocale(string locale)` | 是否包含语言 | locale 空 false；EnsureLookup；查 lookup key | UI 语言切换前可验证 |
| `Clear()` | 清空表数据 | source 置 null，清空导入文本，MarkDirty | 不销毁资产 |
| `ImportCsv(string csv)` | 导入 CSV | 调 `ImportDelimitedText(csv, ',')` | 支持引号字段 |
| `ImportTsv(string tsv)` | 导入 TSV | 调 `ImportDelimitedText(tsv, '\t')` | TSV delimiter 会保存为 `"\\t"` |
| `ImportDelimitedText(string text, char textDelimiter)` | 导入分隔文本 | 清 source、设置 delimiter、保存原始表格文本、MarkDirty | 下次查询时按表头解析 |
| `SetSource(TextAsset textAsset, string textDelimiter = ",")` | 绑定外部文本源 | 保存 source 和 delimiter，清空导入文本，MarkDirty | 用 Excel 导出的 CSV/TSV 作为表源 |
| `OnEnable()` | 启用时标脏 | `lookupDirty = true` | 资产加载后延迟重建 lookup |
| `OnValidate()` | 编辑器校验 | `lookupDirty = true` | Inspector 改动后刷新缓存 |
| `EnsureLookup()` | 确保查询字典有效 | 如果不脏且 lookup 非空返回；否则创建或清空 lookup，清 availableLocales；从 source 文本或导入文本 ParseDelimited 后 BuildLookupFromRows；最后 lookupDirty false | 所有查询入口都会调用 |
| `BuildLookupFromRows(List<List<string>> rows)` | 从表格行建 lookup | 第一行是 header，第一列 key，后续列 locale；逐行读取 key 和各语言 value，AddLookupValue | TextAsset 和导入文本共用 |
| `AddLookupValue(string locale, string key, string value)` | 写 lookup 值 | Clean locale/key；无效返回；locale 不存在则创建内层字典并加入 availableLocales；写 value 或空字符串 | 后写入同 key 会覆盖旧值 |
| `ResolveDelimiter()` | 解析 delimiter 字符 | delimiter 为 `"\\t"` 返回 tab；空返回逗号；否则取第一个字符 | 支持 Inspector 填 `\t` |
| `MarkDirty()` | 标记 lookup 失效 | `lookupDirty = true` | 下次查询重建 |
| `CleanKey(string value)` | 清理 key/locale | null 返回空；Trim 并去掉开头 BOM `\uFEFF` | 兼容带 BOM 的 CSV header |
| `ParseDelimited(string text, char textDelimiter)` | CSV/TSV 解析 | 空文本返回空 rows；逐字符解析引号、双引号转义、分隔符、换行；每行非空才加入 rows | 支持含分隔符或换行的 quoted field |
| `AddRowIfNotEmpty(List<List<string>> rows, List<string> row)` | 跳过空行 | 行内任一字段非空则加入 rows | 避免空行进入表 |

## StateMachine 模块

StateMachine 是通用状态机工具，不是框架服务模块。它参考 Unity Animator Controller 的核心概念组织：Controller 持有参数和 Layer，Layer 持有根状态图，状态节点可以继续挂子状态机；转换支持条件、Trigger、Any State、Exit Time、优先级和转换时长元数据。它不直接播放动画，但适合驱动角色 AI、UI 流程、战斗阶段、技能/动作逻辑等需要分层状态的场景。

### 使用方式

```csharp
using Frame.StateMachine;

StateMachine machine = new StateMachine();

machine.Add(new PrepareState()).WithLength(1f);
machine.Add(new FightingState());
machine.Add(new ResultState());
machine.Add(new HitState());

machine.AddTransition<PrepareState, FightingState>()
    .When(StateCondition.Greater("Ready", 0));
machine.AddAnyTransition<HitState>()
    .When(StateCondition.Trigger("Damaged"));

machine.StateChanged += context => FrameLog.Info($"FSM {context.LayerName}: {context.From} -> {context.To}");

machine.Start();                            // 进入 Base Layer 默认状态
machine.SetInt("Ready", 1);
machine.SetTrigger("Damaged");
machine.Tick(Time.deltaTime);              // 在 Update 里调用
machine.Change<ResultState>(resultData);   // 参数在 ResultState.Enter(context) 里读取
```

### 设计和实现

`StateMachine` 是控制器入口。它创建默认 `Base Layer`，提供 `Parameters` 参数表、`AddLayer` 多层、`Add/AddTransition/AddAnyTransition` 便捷入口，以及 `Start/Change/Tick/Clear` 生命周期。`Tick` 会驱动每个启用的 Layer；未启动的 Layer 会自动进入默认状态。

`StateMachineLayer` 管理一个根 `StateGraph` 和当前激活路径。路径可以是 `Grounded -> LocomotionIdle` 这种父状态 + 子状态的层级。切换到带子状态机的父节点时，会自动继续进入该子状态机的默认状态。切换到外部节点时，会按从叶子到父级的顺序退出，再进入目标路径。

状态注册时直接用状态实例的运行时 `Type` 作为唯一标识。同一 Layer 内一个状态类型只能注册一次；切换可用 `Change<TState>()` 或 `Change(typeof(TState))`，转换可用 `AddTransition<TFrom,TTo>()`，避免字符串 id 的拼写错误和重复维护。

`StateParameterSet` 保存 Float、Int、Bool、Trigger。`StateCondition` 对参数做判断，`StateTransition` 保存 From/To、条件、Exit Time、Duration、Priority 等信息。Trigger 命中转换后自动重置。`StateChangeContext` 是进入状态时的统一上下文，包含 Machine、LayerName、From、To、Parameter、HasFrom、HasParameter，并提供 `TryGetParameter<T>()`。

事件方面，本地可订阅 `StateEntered`、`StateExited`、`StateChanged`、`Transitioned`。构造时传入 `IEventBus` 后，还会发布 `StateMachineStateEntered`、`StateMachineStateExited`、`StateMachineTransitioned`。`BindTrigger<TEvent>("Trigger")` 可把事件中心里的业务事件转换成状态机 Trigger。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `StateChangeContext` | 状态进入上下文 | Machine、LayerName、From、To、Parameter、HasFrom、HasParameter、TryGetParameter |
| `StateTransitionContext` | 转换完成上下文 | From、To、Transition、LayerName、Parameter |
| `IState` | 状态基础接口 | Enter(context)、Tick、Exit；状态类型即标识 |
| `StateBase` | 可选便利基类 | 空虚方法 + `Machine` 反向引用，只重写需要的钩子 |
| `StateParameterSet` | Animator 风格参数表 | Float、Int、Bool、Trigger；Trigger 命中后消耗 |
| `StateCondition` | 转换条件 | If/IfNot/Greater/Less/Equal/NotEqual |
| `StateTransition` | 转换规则 | 条件、Any State、Exit Time、Duration、Priority |
| `StateNode` | 状态节点 | 状态实例、StateType、Length、Speed、当前 Time、子状态机 |
| `StateGraph` | 状态图/子状态机 | 状态集合、默认 Entry、普通转换、Any State 转换 |
| `StateMachineLayer` | 并行 Layer | 根状态图、当前激活路径、独立转换队列 |
| `StateMachine` | Controller 入口 | 参数、Layer、事件中心集成、Tick/Clear |


### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/StateMachine` 的实际源码文件展开。StateMachine 不是框架服务模块，不参与 `Framework` 初始化，只是一个通用工具。

#### `IState.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `IState` | 状态生命周期接口 | 不再要求状态自己返回 id | 注册时用状态实例的运行时 `Type` 作为唯一标识 |
| `Enter(context)` | 进入状态 | 状态机切换到该状态后调用 | 初始化动画、UI、计时器；需要参数时读取 `context.Parameter` |
| `Tick(float deltaTime)` | 状态更新 | 状态机 `Tick` 时转发给当前状态 | 调用方决定 deltaTime 来源 |
| `Exit()` | 退出状态 | 状态机离开当前状态或 Clear 时调用 | 释放状态内临时资源、取消事件订阅 |

#### 参数、条件、转换

| 成员 | 作用 | 使用方式和注意点 |
| --- | --- | --- |
| `StateParameterSet.SetFloat/SetInt/SetBool/SetTrigger` | 写参数 | 不存在时按类型自动创建；同名不同类型会抛异常 |
| `StateCondition.If/IfNot/Greater/Less/Equal/NotEqual` | 转换条件 | 条件全部满足才允许转换 |
| `StateTransition.When/WhenAll` | 添加条件 | 链式配置 |
| `WithExitTime(float)` | 等待源状态 normalized time | `StateNode.Length` 决定 normalized time；Length<=0 时 Tick 后视为 1 |
| `WithDuration(float)` | 转换时长元数据 | 当前不做动画混合，只用于事件/调试信息 |
| `Priority` / `CanTransitionToSelf` | 优先级和自切换 | Priority 数字越小越优先 |

#### 图、层、事件

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `StateNode.CreateChildMachine()` | 创建子状态机 | 父状态进入后自动进入子状态机 Entry |
| `StateGraph.EntryStateType` | 默认进入状态 | 未设置时第一个 AddState 自动作为 Entry |
| `StateGraph.AddAnyTransition(to)` | Any State 转换 | 当前图内任意激活状态都可触发 |
| `StateMachine.AddLayer(name)` | 创建并行 Layer | 每个 Layer 有独立当前状态和转换队列 |
| `StateEntered/StateExited/StateChanged/Transitioned` | 本地事件 | 不依赖事件中心 |
| `StateMachine(eventBus)` / `SetEventBus` | 事件中心集成 | 发布进入、退出、转换事件 |

## Utilities 模块

Utilities 放通用小工具，避免各模块重复实现。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `FramePathUtility` | 路径工具 | `NormalizeResourcesPath` 去扩展名、统一斜杠、裁剪 Resources 前缀；`SanitizeFileName` 清理非法文件名 |
| `DisposableAction` | Dispose 回调封装 | 用于 `InputService.PushContext()` 等“作用域结束自动恢复”场景 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Runtime/Utilities` 的实际源码文件展开。Utilities 里的类型没有模块生命周期，都是被其他模块直接调用的小工具。

#### `DisposableAction.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `DisposableAction(Action onDispose)` | 创建 Dispose 回调对象 | 保存传入 action | `InputService.PushContext()`、`DiagnosticsService.WriteLogsToFile()` 等返回它 |
| `Dispose()` | 执行一次回调 | 取出 `onDispose`；为空返回；先置空字段再调用 action | 防重复 Dispose，且 action 内再次 Dispose 不会递归执行 |

#### `FramePathUtility.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `NormalizeResourcesPath(string path)` | 规范化 Resources 路径 | 空白返回空；替换反斜杠为 `/` 并 Trim；去掉扩展名；如果包含 `"/Resources/"`，裁剪到 Resources 后面的相对路径 | 让 `Assets/UI/Resources/Foo.prefab`、`UI/Resources/Foo.prefab`、`Foo` 等形式尽量归一，避免资源缓存重复 key |
| `SanitizeFileName(string fileName)` | 清理文件名非法字符 | 空白返回 `"default"`；遍历 `Path.GetInvalidFileNameChars()`，把每个非法字符替换为 `_` | 存档槽位、配置文件名等落盘路径可用 |

## Editor 模块

Editor 模块提供 Unity 菜单和项目校验工具，只存在于编辑器程序集。

### 使用方式

Unity 顶部菜单 `Frame`：

- `Create Default Frame Settings`：创建 `Assets/Frame/Resources/Frame/FrameSettings.asset`。
- `Create GameEntry In Scene`：在当前场景创建入口对象。
- `Open README`：打开框架 README。
- `Validate Project`：执行项目校验并输出日志。

CI 命令：

```powershell
& "D:\UnityEditor\6000.4.8f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "E:\UnityProject\Framework" `
  -executeMethod Frame.Editor.FrameMenuItems.ValidateProjectForCI `
  -quit
```

### 设计和实现

`FrameMenuItems.ValidateProject()` 会执行一组编辑器校验：

- `FrameSettings` 是否存在以及关键数值是否合法。
- 当前场景 `GameEntry` 数量是否超过一个。
- Build Settings 是否有可用场景，启用场景路径是否存在。
- 关键包是否存在：Newtonsoft JSON、Input System、UniTask。
- `Frame.Runtime.asmdef` 是否引用 Unity UI、InputSystem、UniTask。
- DOTween 集成资源是否存在。
- `Resources` 路径是否冲突。
- `Resources/UI` prefab 是否包含 `UIPanelBase`。
- `Resources/Configs` JSON 是否能解析。

CI 入口 `ValidateProjectForCI()` 在 batchmode 下会按错误数量返回退出码：有错误返回 1，无错误返回 0。警告不会让 CI 失败。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `FrameMenuItems` | 编辑器菜单入口 | 创建设置、创建入口、打开 README、项目校验、CI 校验 |
| `ValidationReport` | 校验报告 | 记录 Errors、Warnings、Messages、Passed、ExitCode |
| `ValidationMessage` | 单条校验消息 | 保存 Unity `LogType` 和消息文本 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Editor` 的实际源码文件展开。Editor 代码只在 Unity 编辑器程序集编译，用来创建默认资源和做项目结构校验。

#### `FrameMenuItems.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `SettingsAssetPath` | 默认设置资产路径 | 常量 `Assets/Frame/Resources/Frame/FrameSettings.asset` | 与 `FrameSettings.ResourcesPath` 对应 |
| `CreateDefaultSettings()` | 菜单创建默认设置 | EnsureFolder 创建目录；尝试 LoadAsset；不存在则 CreateInstance 并 CreateAsset/SaveAssets；最后选中并 Ping | 菜单 `Frame/Create Default Frame Settings` |
| `CreateGameEntryInScene()` | 菜单创建场景入口 | 先查现有 GameEntry；存在则选中并 Ping；不存在则创建 `"Frame"` GameObject 挂 GameEntry，注册 Undo，标记场景 dirty | 避免重复入口 |
| `OpenReadme()` | 打开 README | LoadAssetAtPath `Assets/Frame/README.md`，存在则 OpenAsset | 菜单快速入口 |
| `ValidateProject()` | 编辑器校验入口 | RunProjectValidation 后 LogValidationSummary | 菜单 `Frame/Validate Project` |
| `RunProjectValidation(bool logDetails = true)` | 执行全部校验 | 创建 ValidationReport；依次 ValidateSettings、GameEntry、BuildScenes、RuntimeDependencies、Integrations、Resources；返回 report | CI 和菜单共用 |
| `ValidateProjectForCI()` | CI 入口 | 运行校验并输出摘要；batchmode 下 `EditorApplication.Exit(report.ExitCode)`；非 batch 且失败则抛异常 | Unity `-executeMethod` 使用 |
| `LogValidationSummary(ValidationReport report)` | 输出摘要 | errors > 0 输出 Error，warnings > 0 输出 Warning，否则 Log passed | 详细消息由 report.Add 决定 |
| `ValidateSettings(ValidationReport report)` | 校验 FrameSettings | 找不到 settings 给 warning；检查 UI 分辨率、AudioSourcePoolSize、DefaultGameObjectPoolMaxSize | getter 已兜底，主要防资源缺失 |
| `ValidateGameEntry(ValidationReport report)` | 校验场景入口数量 | Unity 2023+ 用 `FindObjectsByType`，旧版用 `FindObjectsOfType`；0 个给 info，多个给 error | 自动启动允许 0 个 |
| `ValidateBuildScenes(ValidationReport report)` | 校验 Build Settings | 无场景 warning；有 enabled 场景但文件不存在 error；没有 enabled scene warning | 防构建时场景缺失 |
| `ValidateRuntimeDependencies(ValidationReport report)` | 校验运行依赖 | 检查 Newtonsoft、Input System、UniTask、Addressables、YooAsset package；读取 Frame.Runtime.asmdef；校验 UnityEngine.UI、Unity.InputSystem、UniTask 引用 | asmdef 缺失直接 error 并返回 |
| `ValidateIntegrations(ValidationReport report)` | 校验可选集成 | DOTween.dll 缺失 warning；DOTween/Addressables/YooAsset asmdef 缺失 warning | 不作为 error，因为后端和 TweenService 可按项目选择 |
| `ValidateResources(ValidationReport report)` | 校验 Resources | 遍历 Assets 全部文件，跳过 meta/cs/asmdef；提取 Resources key；发现重复 key warning；对 UI prefab 和 Config JSON 做专项校验 | 可发现 Resources 路径冲突 |
| `ValidateResourceAsset(...)` | 校验单个 Resources 资产 | UI prefab 需能加载且包含 `UIPanelBase`，缺失面板给 warning；Configs JSON 用 `JToken.Parse` 校验，失败 error | 只针对 UI/ 和 Configs/ |
| `ValidatePackage(...)` | 校验 package | PackageExists false 则 error | 必需依赖使用 |
| `PackageExists(string packageName)` | 查包是否存在 | 先查 Packages/manifest.json 内容，再查 Packages/packageName 目录，再查 Library/PackageCache/package@* | 兼容嵌入包和缓存包 |
| `ValidateAsmdefReference(...)` | 校验 asmdef 引用 | 字符串 contains `"reference"`，缺失 error | 简单文本检查 |
| `ReadTextAsset(string path)` | 读文本 | File.Exists 则 ReadAllText，否则空 | 校验工具内部使用 |
| `TryGetResourcesKey(string assetPath, out string key)` | 提取 Resources key | 查 `"/Resources/"`；截取后去扩展名；空白 false | 用于重复 key 检查 |
| `ShouldSkipResourceValidation(string assetPath)` | 是否跳过资源校验 | 空路径 true；扩展名 meta/cs/asmdef true | 降低无关文件干扰 |
| `EnsureFolder(string path)` | 确保文件夹存在 | AssetDatabase.IsValidFolder 返回；递归确保 parent 后 CreateFolder | 创建默认 settings 目录用 |
| `ValidationReport` | 校验结果对象 | 保存 logDetails、messages、Errors、Warnings；Passed 为 Errors==0；ExitCode 为 0/1 | RunProjectValidation 返回它 |
| `ValidationReport.Error/Warning/Info` | 添加消息 | Error/Warning 增加计数，再 Add 对应 LogType；Info 只 Add | 详细日志受 logDetails 控制 |
| `ValidationReport.Add(...)` | 内部记录并输出 | 添加 ValidationMessage；logDetails false 返回；按 LogType 调 Debug.LogError/Warning/Log 并加 `[Frame]` 前缀 | 所有校验消息统一入口 |
| `ValidationMessage` | 单条消息 | 构造保存 Type/Message | 供测试或 CI 读取 |

## Samples 模块

Samples 用来展示框架基础使用，不应作为业务架构模板照搬。

### 类型职责

| 类型 | 作用 | 关键点 |
| --- | --- | --- |
| `FrameDemoController` | 简单示例组件 | 演示事件订阅/发布、定时器、存档保存 |
| `DemoEvent` | 示例私有事件数据 | 通过 `IEventBus` 发布和接收，包含 Message |
| `DemoSaveData` | 示例私有存档数据 | 演示 `SaveService.Save()` 保存普通可序列化对象 |

### 源码级文件和方法详解

这一节按 `Assets/Frame/Samples` 的实际源码文件展开。Samples 不是框架运行必需代码，只展示最小接入方式。

#### `FrameDemoController.cs`

| 成员 | 作用 | 实现方式 | 使用方式和注意点 |
| --- | --- | --- | --- |
| `eventSubscription` | 事件订阅句柄 | 保存 `IEventBus.Subscribe` 返回的 IDisposable | OnDestroy 释放 |
| `timer` | 定时器句柄 | 保存 `TimerService.Delay` 返回值 | OnDestroy Cancel |
| `Start()` | 示例启动 | 框架未初始化直接返回；Resolve IEventBus；订阅 DemoEvent；立即 Publish 示例事件；Resolve TimerService；1 秒后调用 SaveDemoData | 演示服务解析、事件和定时器 |
| `OnDestroy()` | 示例清理 | Dispose eventSubscription 并置空；timer.Cancel | 防止事件和定时器在对象销毁后回调 |
| `OnDemoEvent(DemoEvent demoEvent)` | 事件回调 | `Debug.Log(demoEvent.Message)` | 仅展示接收事件 |
| `SaveDemoData()` | 保存示例数据 | TryResolve SaveService；失败返回；保存 slot `"demo"`，写 PlayerName/Level/SavedAt | 使用 UTC ISO 时间字符串 |
| `DemoEvent` | 私有事件数据 | struct，只有 `Message` 字段 | 示例事件类型 |
| `DemoSaveData` | 私有存档数据 | `[Serializable]` class，字段 PlayerName/Level/SavedAt | Newtonsoft serializer 可保存 |

## 典型项目接入流程

1. 创建或确认 `FrameSettings`。
2. 在 `FrameSettings` 中按项目需要启用或关闭模块。
3. 建议创建 `Assets/Game`，并在业务程序集引用 `Frame.Runtime`。
4. UI prefab 放到 `Resources/UI`，面板脚本继承 `UIPanelBase` 或 `UIPanelBase<TArgs>`。
5. 配置文件放到 `Resources/Configs`，或在启动后注册自定义 `IConfigProvider`。
6. 存档数据实现普通可序列化 class，复杂项目实现 `ISaveVersionedData` 并注册迁移。
7. 网络层统一配置 HTTP `BaseUrl`、Headers、`ResponseParser`；需要长连接时通过 `ISocketService` 创建 TCP/WebSocket client，并在业务网络门面层绑定消息协议。
8. 输入层绑定 `InputActionAsset`，用 `PushContext` 管理 UI/Gameplay 输入切换。
9. 构建前运行 `Frame/Validate Project` 或 CI 入口。

## 常见扩展点

### 替换资源系统

当前默认 `ResourcesAssetService` 适合中小型项目和原型。大型项目可以在 `FrameSettings.AssetServiceBackend` 中切换到 `Addressables` 或 `YooAsset`，业务层继续依赖 `IAssetService`。需要注意，框架服务只负责加载和释放；Addressables Catalog 更新、YooAsset 版本清单请求、补丁下载、失败重试和 CDN 回滚属于项目级启动流程。

### 扩展网络协议

如果后端 envelope 字段不是 `success/code/message/data`，可以直接配置 `EnvelopeHttpResponseParser` 字段；如果协议更复杂，实现新的 `IHttpResponseParser` 并赋值给 `HttpService.ResponseParser`。TCP 长连接如果不是长度前缀协议，实现 `ISocketMessageCodec` 并赋值给 `SocketClientOptions.Codec`。

### 扩展配置来源

实现 `IConfigProvider` 可以接入远程配置、热更新配置、本地缓存配置。实现 `IConfigChangeNotifier` 后，配置变化会让 `ConfigService` 自动清理缓存。

### 扩展 UI 动画

实现 `IUITransition`，在 `RegisterRoute` 或 `UIOpenOptions` 中传入。动画只负责视觉表现，面板生命周期仍由 `UIService` 控制。

### 扩展模块

实现 `IFrameModuleInstaller` 后，框架启动时会扫描当前 AppDomain 的程序集并安装模块。适合独立程序集或可选插件。

## 当前边界

这套框架已经覆盖中小型 Unity 项目的基础开发场景，但以下能力仍建议按具体项目补充：

- 资源构建、远程发布、热更新、回滚和 CDN 监控流程。
- 账号系统、鉴权刷新、业务 RPC、可靠消息、消息去重和断线期间消息补偿。
- 云存档或跨设备同步。
- 大规模本地化导入导出和复数/性别规则。
- 平台真机测试、性能预算、自动化构建流水线。
- 项目级 UI 规范、资源命名规范、错误码规范。

框架层的原则是提供可替换接口和默认实现，不把项目特定策略硬编码到通用底座里。

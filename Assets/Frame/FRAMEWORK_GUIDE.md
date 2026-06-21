# Frame Framework Guide

本文档面向准备在真实项目中使用 `Assets/Frame` 的开发者，说明框架当前能力、生产可用性判断、模块实现方式、推荐用法、扩展方式和后续优化路线。需要逐类阅读源码设计时，参考 [FRAMEWORK_DEEP_DIVE.md](FRAMEWORK_DEEP_DIVE.md)。

## 结论

当前项目可以作为中小型 Unity 项目的通用开发底座，也适合作为商业项目早期的框架起点。它已经具备清晰的模块边界、自动启动、服务注册、生命周期、事件、定时器、资源、UI、音频、存档、配置、网络、输入、本地化、状态机和 DOTween 适配能力。

但它还不应被视为“开箱即用的完整生产级基础设施”。如果项目目标是长线运营、资源热更新、多语言大规模文本、复杂账号网络、强安全存档、多团队并行开发，仍需要继续补齐资源打包/发布流程、配置热更新、云存档或账号存档、统一网络协议、项目级测试覆盖和构建流水线。

推荐定位：

- 可直接用于原型、Demo、单机小游戏、小团队中小型项目。
- 可用于生产项目的第一版底座，但上线前需要按本文“生产化检查表”补齐项目特定能力。
- 不建议直接用于大型联网项目或强热更新项目，除非先完成资源、配置、网络和存档层的生产化扩展。

## 项目结构

```text
Assets/
  Frame/                         框架本体
    Frame.Runtime.asmdef          运行时程序集
    Runtime/Core                  启动、模块生命周期、服务注册、日志
    Runtime/Lifecycle             应用暂停、焦点和退出事件
    Runtime/Events                类型安全事件总线
    Runtime/Time                  Update 驱动定时器
    Runtime/Pooling               C# 对象池和 GameObject 池
    Runtime/Assets                资源服务接口和 Resources 默认实现
    Runtime/Scenes                SceneManager 封装
    Runtime/UI                    UGUI 根节点、分层、面板生命周期
    Runtime/Audio                 BGM、音效、音量分组
    Runtime/Tweening              补间动画抽象接口
    Runtime/Config                JSON 和 ScriptableObject 配置入口
    Runtime/Save                  JSON/二进制存档服务
    Runtime/Preferences           PlayerPrefs 用户偏好设置
    Runtime/Input                 InputSystem/Legacy 输入适配
    Runtime/Networking            UnityWebRequest HTTP、TCP Socket 和 WebSocket 封装
    Runtime/Localization          轻量本地化文本表
    Runtime/StateMachine          通用状态机
    Runtime/Utilities             路径、释放等工具
    Integrations/Addressables     Addressables 资源服务实现
    Integrations/YooAsset         YooAsset 资源服务实现
    Integrations/DOTween          DOTween 对 ITweenService 的实现
    Editor                        编辑器菜单和校验工具
    Samples                       使用示例
  ThirdParty/DOTween              DOTween 插件
  Packages/manifest.json          Unity 包依赖
  ProjectSettings/                Unity 项目设置
```

业务代码建议放在 `Assets/Game` 或 `Assets/Scripts/Game`，不要直接写进 `Assets/Frame`。框架层只保留可复用基础设施，业务层通过接口依赖框架服务。

## 启动流程

框架默认通过 `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)` 自动启动。

核心流程：

1. `Framework.AutoBootstrap()` 加载 `Resources/Frame/FrameSettings`，如果没有配置资源，则创建运行时默认配置。
2. 如果 `FrameSettings.AutoCreateGameEntry` 为 true，调用 `GameEntry.Ensure(settings)`。
3. `GameEntry.Awake()` 调用 `Framework.Initialize(this, settings)`。
4. `Framework.Initialize()` 创建 `ServiceRegistry`、`ModuleManager` 和 `FrameContext`。
5. `Framework` 根据 `FrameSettings` 注册默认模块，再扫描实现了 `IFrameModuleInstaller` 的扩展模块，例如 DOTween 适配模块。
6. `ModuleManager.InitializeAll()` 按模块 `Priority` 从小到大初始化。
7. `GameEntry.Start/Update/FixedUpdate/LateUpdate` 驱动所有模块生命周期。
8. `OnApplicationQuit` 或入口对象销毁时调用 `Framework.Shutdown()`，模块按倒序关闭。

显式启动方式：

1. Unity 菜单执行 `Frame/Create Default Frame Settings`。
2. Unity 菜单执行 `Frame/Create GameEntry In Scene`。
3. 在 `FrameSettings` 中关闭不需要的模块。

常用入口：

```csharp
using Frame.Core;
using Frame.Events;
using Frame.Timing;

IEventBus events = Framework.Resolve<IEventBus>();
ITimerService timers = Framework.Resolve<ITimerService>();
```

建议业务代码优先解析接口，例如 `IEventBus`、`ITimerService`、`IAssetService`，只有确实需要实现类特有能力时再解析具体服务。

## Core 模块

Core 是整个框架的根。主要类型：

- `GameEntry`：Unity 场景中的框架入口，负责把 Unity 生命周期转发给框架。
- `Framework`：静态门面，负责初始化、关闭、服务解析和模块注册。
- `ModuleManager`：管理模块列表，按优先级初始化，按倒序关闭。
- `GameModuleBase`：模块基类，封装 `IsInitialized` 和生命周期模板方法。
- `ServiceRegistry`：轻量服务容器，按类型注册和解析实例。
- `FrameSettings`：框架运行配置。
- `FrameLog`：统一日志入口，支持日志缓冲和 `EntryWritten` 事件。
- `FrameContext`：模块初始化时拿到的上下文，包含入口、设置、服务容器和根节点。

模块实现方式：

```csharp
public sealed class MyService : GameModuleBase, IMyService
{
    public override int Priority => 100;

    protected override void OnInitialize()
    {
        Context.Services.Register<IMyService>(this);
    }

    protected override void OnShutdown()
    {
        // 释放资源
    }
}
```

外部模块自动安装：

```csharp
public sealed class MyModuleInstaller : IFrameModuleInstaller
{
    public void Install(ModuleManager modules, FrameSettings settings)
    {
        modules.Add(new MyService());
    }
}
```

注意事项：

- `Priority` 越小越早初始化，关闭时顺序相反。
- 模块初始化失败时，框架会清理已初始化模块和服务，避免保留半初始化状态。
- `ServiceRegistry` 是主线程轻量容器，不是完整 DI 容器，不负责构造函数注入、作用域管理或线程安全。

## Lifecycle 模块

`LifecycleService` 把 Unity 的应用暂停、焦点变化和退出前回调统一暴露给业务层，适合保存临时状态、暂停网络轮询、处理后台恢复和释放临时资源。

实现方式：

- `GameEntry` 收到 `OnApplicationPause`、`OnApplicationFocus` 和 `OnApplicationQuit` 后转发给 `Framework`。
- `ModuleManager` 会把生命周期回调转发给所有模块。
- `LifecycleService` 记录 `IsPaused`、`HasFocus` 和 `IsQuitting`。
- `PauseChanged` 和 `FocusChanged` 只在状态真实变化时触发，避免重复回调。
- `Quitting` 只触发一次，适合做退出前保存或 flush。
- 回调异常会被 `FrameLog.Exception` 捕获，不会中断其它系统。

用法：

```csharp
ILifecycleService lifecycle = Framework.Resolve<ILifecycleService>();

lifecycle.PauseChanged += paused =>
{
    if (paused)
    {
        Framework.Resolve<ISaveService>().Save("autosave", data);
    }
};

lifecycle.FocusChanged += focused =>
{
    Debug.Log(focused ? "focus" : "blur");
};

lifecycle.Quitting += () =>
{
    Debug.Log("flush analytics");
};
```

生产建议：

- 移动端进入后台时尽量快速保存关键状态，避免依赖长时间后台执行。
- 网络轮询、语音、定位、广告和支付恢复通常应绑定 pause/focus，而不是散落在各个 MonoBehaviour。
- 退出事件不保证在所有平台都可靠触发，关键数据仍应在流程节点和后台切换时保存。

## Diagnostics 模块

`DiagnosticsService` 是轻量运行时诊断服务，默认随框架启动，可在 `FrameSettings` 中关闭。

实现方式：

- `FrameLog` 会把日志写入固定长度缓冲区，默认保留最近 256 条。
- `FrameLog.EntryWritten` 可订阅日志事件，适合接入运行时控制台、日志落盘、远程上报。
- `DiagnosticsService.Logs` 读取当前日志缓冲。
- `DiagnosticsService.CaptureSnapshot()` 返回帧数、运行时间、平均 FPS、托管内存、Unity 已分配内存、warning/error/exception 计数。
- `DiagnosticsService.WriteLogsToFile()` 可把框架日志写入本地文件，并在超过大小限制时轮转到 `.bak`。
- `DiagnosticsService.ClearLogs()` 清空日志缓冲和诊断计数。
- `RuntimeDiagnosticsOverlay` 是可选 IMGUI 调试面板，可在 `FrameSettings` 中启用，默认用反引号键切换显示，展示 FPS/内存、生命周期状态、HTTP 指标、计时器数量、场景加载状态、资源引用计数、对象池统计和最近日志。

用法：

```csharp
IDiagnosticsService diagnostics = Framework.Resolve<IDiagnosticsService>();
DiagnosticsSnapshot snapshot = diagnostics.CaptureSnapshot();

Debug.Log($"fps={snapshot.AverageFps} memory={snapshot.ManagedMemoryBytes}");

diagnostics.LogReceived += entry =>
{
    if (entry.Level >= FrameLogLevel.Error)
    {
        // 上报或写入本地文件
    }
};

IDisposable fileLog = diagnostics.WriteLogsToFile(
    Path.Combine(Application.persistentDataPath, "frame.log"),
    maxBytes: 1024 * 1024);

// 不再需要落盘时调用：
fileLog.Dispose();
```

生产建议：

- Release 包建议只上报 Warning/Error/Exception，并限制本地日志文件大小；本地文件可保留一份 `.bak` 供用户反馈时上传。
- 真机性能排查可启用 Runtime Diagnostics Overlay，正式包建议通过隐藏入口或编译开关控制可见性。
- 远程日志上报需要按项目补采样、脱敏、批量发送和崩溃前落盘。

## Events 模块

`EventBus` 提供类型安全的发布订阅。

实现方式：

- 内部用 `Dictionary<Type, List<Subscription>>` 按事件类型保存订阅。
- `Subscribe<TEvent>` 记录 handler、owner、once 和 id。
- `Publish<TEvent>` 发布时复制订阅列表快照，避免回调中增删订阅导致集合修改异常。
- 单个 handler 抛异常会被 `FrameLog.Exception` 捕获，不影响后续订阅者。
- `UnsubscribeOwner(owner)` 用于按界面、系统或对象批量解绑。

用法：

```csharp
public struct PlayerLevelChanged
{
    public int Level;
}

private IDisposable subscription;

private void OnEnable()
{
    var bus = Framework.Resolve<IEventBus>();
    subscription = bus.Subscribe<PlayerLevelChanged>(OnLevelChanged, this);
}

private void OnDisable()
{
    subscription?.Dispose();
    subscription = null;
}

private void OnLevelChanged(PlayerLevelChanged evt)
{
    Debug.Log(evt.Level);
}
```

发布事件：

```csharp
Framework.Resolve<IEventBus>().Publish(new PlayerLevelChanged { Level = 10 });
```

适合场景：

- UI 与系统解耦。
- 游戏状态变化通知。
- 轻量全局消息。

不适合场景：

- 高频逐帧数值同步。
- 跨线程事件。
- 需要严格顺序、回滚、持久化的领域事件。

## Time 模块

`TimerService` 提供 Update 驱动的定时任务。

实现方式：

- 内部用 `Dictionary<int, TimerTask>` 保存计时器。
- 每帧复制 key 到 `updateBuffer`，避免回调中取消计时器导致遍历异常。
- 支持 scaled time 和 unscaled time。
- 支持 owner 批量取消。
- 应用暂停时暂停所有计时器。
- `ActiveTimerCount`、`ScaledTimerCount`、`UnscaledTimerCount` 和 `IsPaused` 可用于运行时诊断和泄漏排查。

用法：

```csharp
ITimerService timers = Framework.Resolve<ITimerService>();

TimerHandle handle = timers.Delay(2f, () =>
{
    Debug.Log("2 seconds later");
}, unscaled: true, owner: this);

handle.Cancel();
```

循环计时：

```csharp
timers.Repeat(1f, Tick, repeatCount: -1, unscaled: false, owner: this);
```

下一帧执行：

```csharp
timers.NextFrame(RefreshLayout, this);
```

注意事项：

- 计时器在主线程执行。
- 不适合替代高精度物理计时。
- 对象销毁时应调用 `CancelOwner(this)` 或保存 `TimerHandle.Cancel()`。

## Assets 模块

资源服务统一通过 `IAssetService` 对外暴露，具体后端由 `FrameSettings.AssetServiceBackend` 决定：

- `Resources`：默认后端，适合原型、小项目、框架默认资源和少量内置资源。
- `Addressables`：Unity 官方资源系统，适合标准 Unity 工作流、本地/远程分组、Catalog 更新和中大型项目。
- `YooAsset`：面向 AssetBundle 热更管线的第三方框架，适合国内移动游戏、CDN 补丁、首包/沙盒缓存和更强的版本清单控制。

实现方式：

- `ResourcesAssetService` 路径通过 `FramePathUtility.NormalizeResourcesPath` 归一化，底层使用 `Resources.Load<T>` 和 `Resources.LoadAsync<T>`。
- `AddressablesAssetService` 使用 Addressables address 作为 key，底层持有 `AsyncOperationHandle`，引用计数归零时调用 `Addressables.Release`。
- `YooAssetAssetService` 使用 YooAsset location 作为 key，初始化指定 package 后持有 `YooAsset.AssetHandle`，引用计数归零时调用 YooAsset handle `Release()`。
- 三个后端都维护缓存和引用计数，`AssetHandle<T>.Release()` 会减少引用计数。
- `TryLoad<T>(path, out handle)` 用于静默尝试加载资源；普通业务需要缺失资源告警时继续使用 `Load<T>()`。
- `AssetRequest<T>` 暴露 `Success`、`Error`、`Progress` 和 `Cancel()`，可用于 Loading UI、超时策略和流程取消。
- `IsLoaded()`、`GetReferenceCount()`、`GetLoadedAssetStats()` 和 `ReleaseAll()` 可用于定位资源泄漏和切场景时清理资源。
- `Instantiate(path)` 会先加载 prefab，并在实例上挂 `AssetInstanceLease`，实例销毁时自动释放资源句柄，避免 Addressables/YooAsset 依赖被提前卸载。
- 回调异常会被捕获并写入框架日志。

资源路径规则：

- `Resources`：使用 `/`，不带扩展名，相对任意 `Resources` 目录。
- `Addressables`：填写 Addressables address；不要依赖 `Resources` 路径裁剪规则。
- `YooAsset`：填写 YooAsset location；通常来自 YooAsset 收集器配置或资源清单。

后端切换：

```csharp
// 推荐在 FrameSettings Inspector 中设置：
// Asset Service Backend = Resources / Addressables / YooAsset
```

YooAsset 额外配置：

- `YooAssetPackageName`：默认 `DefaultPackage`，必须和 YooAsset 构建配置里的 package 名一致。
- `YooAssetPlayMode`：`EditorSimulate` 用于编辑器模拟；`Offline` 用于首包内置资源；`Host` 用于内置资源 + 远端 CDN + 沙盒缓存；`Web` 用于 WebGL。
- `YooAssetDefaultHostServer` / `YooAssetFallbackHostServer`：Host/Web 模式下拼接远端文件 URL。
- `YooAssetDownloadMaxConcurrency`、`YooAssetDownloadMaxRequestPerFrame`、`YooAssetDownloadWatchdogTimeout`：传给 YooAsset 文件系统下载参数。

用法：

```csharp
IAssetService assets = Framework.Resolve<IAssetService>();

using (AssetHandle<Sprite> handle = assets.Load<Sprite>("Icons/Sword"))
{
    if (handle.IsValid)
    {
        icon.sprite = handle.Asset;
    }
}
```

异步加载：

```csharp
AssetRequest<GameObject> request = assets.LoadAsync<GameObject>("UI/MainMenu", handle =>
{
    if (handle.IsValid)
    {
        Instantiate(handle.Asset);
        handle.Release();
    }
});

yield return request;

if (!request.Success)
{
    Debug.LogError(request.Error);
}
```

取消异步加载：

```csharp
AssetRequest<AudioClip> request = assets.LoadAsync<AudioClip>("Audio/Bgm/Battle");
request.Cancel();
```

资源诊断：

```csharp
Debug.Log(assets.GetReferenceCount("Icons/Sword"));
foreach (AssetStats stats in assets.GetLoadedAssetStats())
{
    Debug.Log($"{stats.Path} refs={stats.ReferenceCount} type={stats.TypeName}");
}

assets.ReleaseAll();
assets.UnloadUnusedAssets();
```

生产建议：

- 小项目和 Demo 可以继续使用 `Resources`，但不要把大量正式资源长期放进 `Resources`。
- 标准 Unity 项目优先考虑 `Addressables`：官方维护、编辑器集成好，适合 Label、Catalog、本地/远程分组和常规 DLC。
- 国内手游或强热更项目优先考虑 `YooAsset`：版本清单、CDN、沙盒缓存、首包资源和补丁流程更贴近商业手游管线。
- `IAssetService` 只负责加载/释放资源，不负责完整热更流程。Addressables 的 Catalog 更新、YooAsset 的版本请求、清单更新、下载器和补丁 UI 应在启动/登录前的项目级流程中完成。
- 高频逐帧数据不要走通用资源服务；资源系统适合加载资产，不适合当作热路径对象查找表。

## UI 模块

UI 模块基于 UGUI，实现了根节点、层级、面板生命周期、路由、返回栈、模态遮罩、弹窗队列、异步打开、开关动画和强类型打开参数。

实现方式：

- `UIService.OnInitialize()` 自动创建 `UIRoot`。
- `UIRoot` 创建 `Background`、`Normal`、`Popup`、`Tips`、`Loading`、`System` 层。
- 每个层是一个带独立 Canvas 的 RectTransform，sortingOrder 等于 `UILayer` 枚举值。
- `UIService.Open<TPanel>()` 通过资源服务加载 prefab，挂到指定层。
- `UIService.RegisterRoute<TPanel>()` 可以把业务路由名映射到 prefab、层级、缓存、模态和动画配置。
- `UIService.OpenAsync<TPanel>()` 和 `OpenRouteAsync<TPanel>()` 返回 `UIPanelRequest<TPanel>`，可作为协程 yield 对象等待。
- `UIService.Back()` 会从顶层往下关闭第一个允许返回的面板。
- `UIService.EnqueueRoute<TPanel>()` 可把路由面板加入队列，当前队列面板关闭后再打开下一个，适合奖励弹窗、公告、确认框。
- 面板必须继承 `UIPanelBase`。
- 需要强类型参数时继承 `UIPanelBase<TArgs>`。
- `UIPanelBase` 生命周期为 `OnCreate`、`OnOpen`、`OnClose`、`OnDispose`。
- 默认支持缓存面板，关闭时隐藏，销毁时释放。
- `UIOpenOptions.Modal` 会自动创建拦截点击的模态遮罩，可配置点击遮罩关闭。
- `UIOpenOptions.Transition` 可接入 `IUITransition`，默认提供 `UIFadeTransition`。

面板示例：

```csharp
using Frame.UI;
using UnityEngine.UI;

public sealed class MainMenuPanel : UIPanelBase
{
    public Button StartButton;

    protected override void OnCreate()
    {
        StartButton.onClick.AddListener(OnStartClicked);
    }

    protected override void OnOpen(object args)
    {
        gameObject.SetActive(true);
    }

    protected override void OnClose()
    {
        // 停止动画、取消计时器、解绑临时事件
    }

    private void OnStartClicked()
    {
        Close();
    }
}
```

打开 UI：

```csharp
IUIService ui = Framework.Resolve<IUIService>();
ui.Open<MainMenuPanel>("UI/MainMenu", UILayer.Normal);
```

路由、模态、动画和强类型参数：

```csharp
public sealed class ShopArgs
{
    public string Tab;
}

public sealed class ShopPanel : UIPanelBase<ShopArgs>
{
    protected override void OnOpen(ShopArgs args)
    {
        // 根据 args.Tab 刷新界面
    }
}

ui.RegisterRoute<ShopPanel>(
    "shop",
    "UI/ShopPanel",
    UILayer.Popup,
    cache: true,
    modal: true,
    closeOnBackdrop: true,
    transition: new UIFadeTransition(0.2f));

ui.OpenRoute<ShopPanel, ShopArgs>("shop", new ShopArgs { Tab = "Weapon" });
```

异步打开：

```csharp
UIPanelRequest<ShopPanel> request = ui.OpenRouteAsync<ShopPanel>("shop", new ShopArgs { Tab = "Armor" });
yield return request;

if (request.Success)
{
    ShopPanel panel = request.Panel;
}
```

弹窗队列：

```csharp
UIPanelRequest<ShopPanel> queued = ui.EnqueueRoute<ShopPanel, ShopArgs>(
    "shop",
    new ShopArgs { Tab = "Reward" });

yield return queued;

if (!queued.Success)
{
    Debug.LogError(queued.Error);
}
```

关闭 UI：

```csharp
panel.Close();
ui.CloseTop();
ui.Back();
ui.CloseAll(destroy: true);
```

生产建议：

- 为 UI prefab 制定资源目录规范，例如 `Resources/UI/<Feature>/<PanelName>`。
- 面板里订阅事件或计时器时，用 `OnClose` 或 `OnDispose` 清理。
- 复杂转场编排、UI 焦点导航和输入屏蔽栈仍可在当前路由/返回栈/队列基础上继续扩展。

## Audio 模块

`AudioService` 提供音乐和音效播放。

实现方式：

- 初始化时创建 `Audio` 根节点。
- 单独创建一个循环播放的 `musicSource`。
- 预热一组 SFX `AudioSource`，空闲 source 会隐藏。
- 分组音量包括 `Master`、`Music`、`Sfx`、`UI`、`Ambient`。
- BGM 支持淡入淡出，`CurrentMusic` 可读取当前音乐播放句柄。
- OneShot 播放后通过协程按 clip 时长归还 source。

播放音乐：

```csharp
IAudioService audio = Framework.Resolve<IAudioService>();
audio.PlayMusic(bgmClip, fadeSeconds: 1f, volume: 0.8f);
audio.StopMusic(fadeSeconds: 0.5f);
```

播放音效：

```csharp
audio.PlayOneShot(clickClip, AudioCategory.UI);
```

使用 AudioCue：

```csharp
audio.PlayCue(cue);
```

生产建议：

- 需要把用户音量设置持久化。
- 移动端项目应控制同时播放音效数量。
- 大项目建议接入 AudioMixer、声音优先级、空间音频配置、音频资源异步加载和音频诊断统计。

## Save 模块

`SaveService` 使用 `Application.persistentDataPath/<SaveFolderName>` 保存 JSON 或二进制存档文件。

实现方式：

- 默认序列化器是 `NewtonsoftSaveSerializer`，输出 `.json`。
- 可切换到 `BinarySaveSerializer`，输出 `.bin` 二进制存档。
- 可通过 `SetEncryptor` 配置存档加密解密，例如 `AesSaveEncryptor`。
- 存档文件扩展名由当前 `ISaveSerializer.FileExtension` 决定。
- 保存时先写 `.tmp`，再替换正式文件。
- 如果正式文件已存在，使用 `.bak` 作为备份。
- 保存时会同步写入 `<存档文件>.meta`，记录格式版本、数据版本、序列化器扩展名、是否加密、payload 大小和 SHA-256。
- 读取时如果 metadata 校验失败或反序列化失败，会尝试读取 `.bak` 备份文件。
- 没有 metadata 的旧存档仍可按旧逻辑读取，便于平滑升级。
- 可注册 `SaveMigration<TData>`，读取旧版本数据后自动按版本链迁移到新结构。
- 空 slot 会抛出 `ArgumentException`，避免误写入默认档位。

数据结构示例：

```csharp
[Serializable]
public sealed class PlayerSaveData
{
    public int Version = 1;
    public string PlayerName;
    public int Level;
    public long SavedAtUtcTicks;
}
```

保存：

```csharp
ISaveService save = Framework.Resolve<ISaveService>();

save.Save("slot_1", new PlayerSaveData
{
    PlayerName = "Player",
    Level = 12,
    SavedAtUtcTicks = DateTime.UtcNow.Ticks
});

save.Save("slot_1", data, dataVersion: 2);
```

异步保存和读取：

```csharp
await save.SaveAsync("slot_1", data, dataVersion: 2);

SaveLoadResult<PlayerSaveData> result = await save.TryLoadAsync<PlayerSaveData>("slot_1");
if (result.Success)
{
    Debug.Log(result.Data.Level);
}
```

读取：

```csharp
if (save.TryLoad<PlayerSaveData>("slot_1", out var data))
{
    Debug.Log(data.Level);
}
```

列出存档：

```csharp
foreach (SaveSlotInfo slot in save.ListSlots())
{
    Debug.Log($"{slot.Slot} v{slot.DataVersion} {slot.SizeBytes}");
}
```

读取 metadata：

```csharp
if (save.TryGetMetadata("slot_1", out SaveMetadata metadata))
{
    Debug.Log($"{metadata.SerializerExtension} {metadata.PayloadSha256}");
}
```

二进制存档和加密：

```csharp
save.SetSerializer(new BinarySaveSerializer());
save.SetEncryptor(new AesSaveEncryptor("project-save-key"));
```

版本迁移：

```csharp
save.RegisterMigration(new SaveMigration<PlayerSaveData>(1, 2, oldData =>
{
    oldData.Version = 2;
    return oldData;
}));
```

生产建议：

- 建议存档结构实现 `ISaveVersionedData`，或保存时显式传入 `dataVersion`。
- 加密项目需要做好密钥管理、换钥和丢 key 后的降级策略。
- 云存档仍需按业务补冲突合并、设备覆盖确认和服务器校验。
- 移动端和主机平台应评估写入频率，大存档优先使用异步接口或后台队列。

## Preferences 模块

`PreferencesService` 用于保存轻量用户偏好，例如音量、语言、画质、开关状态和最后一次选择。它基于 Unity `PlayerPrefs`，不适合替代正式存档。

实现方式：

- 默认随框架启动，可在 `FrameSettings` 中关闭。
- 支持 `int`、`float`、`string`、`bool` 和 JSON 对象。
- `Changed` 事件会在某个 key 被写入或删除时触发，便于刷新设置界面。
- `Save()` 显式调用 `PlayerPrefs.Save()`，模块关闭时也会保存一次。
- 不提供全局 `DeleteAll`，避免误删同项目或同包名下的其它用户数据。

用法：

```csharp
IPreferencesService preferences = Framework.Resolve<IPreferencesService>();

preferences.SetFloat("audio.music", 0.8f);
preferences.SetBool("graphics.vsync", true);
preferences.SetString("locale", "zh");
preferences.Save();

float musicVolume = preferences.GetFloat("audio.music", 1f);
string locale = preferences.GetString("locale", "en");
```

JSON 设置：

```csharp
preferences.SetJson("graphics.options", options);

if (preferences.TryGetJson("graphics.options", out GraphicsOptions loaded))
{
    ApplyGraphics(loaded);
}
```

生产建议：

- 偏好设置适合保存用户设置，不适合保存角色进度、背包、关卡状态等关键数据。
- 建议业务 key 使用命名空间，例如 `audio.music`、`graphics.quality`、`input.scheme`。
- 需要云同步用户设置时，可在本服务之上增加账号维度同步层。

## Config 模块

`ConfigService` 统一从多个 provider 读取配置。默认配置资源通过当前 `IAssetService` 加载，不在 Config 模块内直接调用 `Resources.Load`。

实现方式：

- 如果已启用 `IAssetService`，默认注册 `AssetScriptableConfigProvider` 和 `AssetJsonConfigProvider`，根目录为逻辑路径 `Configs`。
- `AssetJsonConfigProvider` 读取 `Configs/{key}` 对应的 `TextAsset` 并用 Newtonsoft.Json 反序列化；Resources 后端下对应 `Resources/Configs/{key}.json`。
- `AssetScriptableConfigProvider` 只处理 `ScriptableConfig` 派生类型，按 `Configs/{key}` 路径加载单个资产，不再扫描整个 `Resources/Configs`。
- 外部可注册自定义 `IConfigProvider`。
- 新 provider 插入到列表开头，因此优先级高于默认 provider。
- `RuntimeJsonConfigProvider` 可在运行时注入 JSON 覆盖配置，适合远程配置、灰度配置和热更新覆盖。
- 实现 `IConfigChangeNotifier` 的 provider 变更时，`ConfigService` 会自动清空读取缓存。
- `CacheEnabled` 默认开启，同一 key 和类型的配置只会从 provider 读取一次。
- `ClearCache()` 可清空配置缓存，`RegisterProvider()` 和 `UnregisterProvider()` 会自动清缓存。
- `ScriptableConfigProvider` 可手动注册 ScriptableObject 配置。
- 配置对象实现 `IConfigValidator` 时，加载后会自动执行校验，失败时返回 null/false 并输出框架 warning。

JSON 配置示例：

```json
{
  "Id": "sword_001",
  "Name": "Iron Sword",
  "Attack": 12
}
```

读取：

```csharp
public sealed class ItemConfig
{
    public string Id;
    public string Name;
    public int Attack;
}

IConfigService configs = Framework.Resolve<IConfigService>();
ItemConfig sword = configs.Load<ItemConfig>("Items/sword_001");
configs.ClearCache();
```

运行时 JSON 覆盖：

```csharp
RuntimeJsonConfigProvider remoteConfigs = new RuntimeJsonConfigProvider();
configs.RegisterProvider(remoteConfigs);

remoteConfigs.SetJson("Items/sword_001", remoteJson);

ItemConfig overridden = configs.Load<ItemConfig>("Items/sword_001");
```

配置校验：

```csharp
public sealed class ValidatedItemConfig : IConfigValidator
{
    public string Id;
    public string Name;
    public int Attack;

    public bool Validate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            error = "Id is required.";
            return false;
        }

        error = null;
        return true;
    }
}
```

Resources 后端对应路径：

```text
Assets/Game/Resources/Configs/Items/sword_001.json
```

生产建议：

- 配置量大时建议在 Loading 阶段集中预加载关键表，避免首帧业务触发大量反序列化。
- 仍建议补编辑器/构建前配置校验工具，检查缺字段、重复 id、引用不存在。
- 联网项目可用 `RuntimeJsonConfigProvider` 承接远程 JSON，但仍需要版本号、灰度、签名校验和回滚机制。

## Networking 模块

Networking 模块包含两类能力：`HttpService` 负责短连接 HTTP 请求，`SocketService` 负责 TCP Socket 和 WebSocket 长连接。两者独立注册，分别由 `FrameSettings.EnableHttpService` 和 `FrameSettings.EnableSocketService` 控制。

HTTP 实现方式：

- `Get` 和 `PostJson` 是便捷入口。
- `Send(HttpRequest, completed)` 支持 method、body、content-type、headers、timeout、retries、retryDelay。
- `BaseUrl` 可配置 API 根地址，相对 URL 会自动拼接。
- `SetDefaultHeader()` 和 `SetBearerToken()` 可统一注入公共 header 和认证 token，单个 `HttpRequest.Headers` 可覆盖默认 header。
- `GetJson<TResponse>()`、`PostJson<TRequest, TResponse>()` 和 `SendJson<TResponse>()` 会把 JSON 响应解析成 `HttpResponse<TResponse>`。
- `ResponseParser` 可替换 typed JSON 的解析策略，默认是裸 JSON；`EnvelopeHttpResponseParser` 支持常见 `{ code, message, data, success }` 统一响应结构。
- `HttpResponse` 暴露 `ErrorCode` 和 `Message`，便于业务层统一处理后端协议错误。
- `HttpRequestHandle` 可作为协程 yield 对象，也可主动 `Cancel()`。
- 请求失败会按配置重试。
- 完成回调抛异常会被捕获，不会中断服务协程。
- 取消请求会返回 `Success=false` 和 `Error="Request canceled."`。
- `RequestStarted`、`RequestCompleted`、`ActiveRequestCount`、`StartedRequestCount`、`CompletedRequestCount`、`FailedRequestCount` 可接入诊断面板或网络调试日志。

GET：

```csharp
IHttpService http = Framework.Resolve<IHttpService>();
http.BaseUrl = "https://example.com/api";
http.SetBearerToken("access-token");

http.Get("version", response =>
{
    if (response.Success)
    {
        Debug.Log(response.Text);
    }
});
```

POST JSON：

```csharp
http.PostJson("https://example.com/api/login", "{\"name\":\"player\"}", response =>
{
    Debug.Log(response.StatusCode);
});
```

Typed JSON：

```csharp
http.PostJson<LoginRequest, LoginResponse>("login", request, response =>
{
    if (response.Success)
    {
        Debug.Log(response.Value.Token);
    }
    else
    {
        Debug.LogError(response.Error);
    }
});
```

统一响应 envelope：

```csharp
http.ResponseParser = new EnvelopeHttpResponseParser
{
    CodeField = "code",
    MessageField = "message",
    DataField = "data",
    SuccessField = "success"
};

http.GetJson<PlayerProfile>("profile", response =>
{
    if (!response.Success)
    {
        Debug.LogError($"{response.ErrorCode}: {response.Error}");
        return;
    }

    ApplyProfile(response.Value);
});
```

自定义请求：

```csharp
HttpRequest request = new HttpRequest
{
    Url = "https://example.com/api/items",
    Method = HttpMethod.Get,
    TimeoutSeconds = 10,
    Retries = 2,
    RetryDelaySeconds = 0.5f
};
request.Headers["Authorization"] = "Bearer token";

HttpRequestHandle handle = http.Send(request, OnCompleted);
```

HTTP 诊断：

```csharp
http.RequestCompleted += (request, response) =>
{
    Debug.Log($"{request.Url} {response.StatusCode} {response.Success}");
};

Debug.Log($"{http.ActiveRequestCount} active, {http.FailedRequestCount} failed");
```

Socket 长连接：

- `ISocketService.CreateTcpClient(host, port)` 创建 TCP 客户端，默认使用 4 字节大端长度前缀协议解决 TCP 粘包/拆包。
- `ISocketService.CreateWebSocketClient(url)` 创建 WebSocket 客户端，支持 `ws://` 和 `wss://`。
- `SocketClientOptions` 可配置 TLS、连接超时、接收缓冲、最大消息大小、发送队列上限、自动重连、心跳、WebSocket header 和 sub-protocol。
- `ISocketClient` 暴露 `StateChanged`、`Connected`、`Disconnected`、`Reconnecting`、`MessageReceived` 和 `Error` 事件。
- `SocketClientMetrics` 提供 sent/received/dropped/reconnect 计数，Runtime Diagnostics Overlay 会展示 Socket 客户端数量、活跃连接数和基础收发指标。
- 非 WebSocket 的 TCP 流必须有项目协议编解码器；默认 `LengthPrefixedSocketCodec` 适合二进制包或 UTF-8 文本包，业务可实现 `ISocketMessageCodec` 替换。

TCP 示例：

```csharp
ISocketService sockets = Framework.Resolve<ISocketService>();
ISocketClient client = sockets.CreateTcpClient("127.0.0.1", 9000, options =>
{
    options.AutoReconnect = true;
    options.MaxReconnectAttempts = -1;
    options.HeartbeatIntervalSeconds = 10f;
    options.HeartbeatTimeoutSeconds = 30f;
    options.HeartbeatPayload = System.Text.Encoding.UTF8.GetBytes("ping");
});

client.Connected += socket => Debug.Log("socket connected");
client.Disconnected += (socket, info) => Debug.Log($"socket closed: {info.Reason} {info.Error}");
client.MessageReceived += (socket, message) => Debug.Log(message.Text);

await client.ConnectAsync();
client.SendText("hello");
```

WebSocket 示例：

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
realtime.SendText("{\"op\":\"join\",\"room\":\"lobby\"}");
```

Socket 关闭：

```csharp
await client.DisconnectAsync();
sockets.RemoveClient(client);
```

生产建议：

- 需要按项目补鉴权刷新、环境切换、埋点和限流。
- 业务后端如果不是 `{ code, message, data }` 风格，可实现 `IHttpResponseParser` 接入项目协议。
- 错误码到用户提示、重登、重试或静默忽略的策略仍建议放在业务网络门面层。
- Socket 层只负责连接、收发、重连和基础编解码；登录态刷新、业务 ack、可靠消息、RPC 映射、消息去重和序列化协议仍应放在项目网络门面层。
- WebGL 平台不能使用 .NET `TcpClient`/`ClientWebSocket`，需要浏览器 WebSocket bridge 或单独的 WebGL transport 实现。

## Pooling 模块

框架提供两类池：

- `ObjectPool<T>`：纯 C# 对象池。
- `GameObjectPool`：prefab 实例池，由 `PoolService` 管理。

`ObjectPool<T>` 实现方式：

- 用 `Stack<T>` 保存 inactive 对象。
- 用 `HashSet<T>` 防止重复归还。
- 支持 `factory`、`onGet`、`onRelease`、`onDestroy` 回调。
- 如果对象实现 `IResettablePoolItem`，归还时自动调用 `ResetForPool()`。
- `GetStats()` 返回 `PoolStats`，包含 active、inactive、created、destroyed、get、release 等计数。

用法：

```csharp
ObjectPool<BulletData> pool = new ObjectPool<BulletData>(
    () => new BulletData(),
    onRelease: item => item.Reset(),
    maxSize: 256);

BulletData data = pool.Get();
pool.Release(data);

PoolStats stats = pool.GetStats("Bullets");
Debug.Log($"{stats.CountActive}/{stats.CountInactive}");
```

`GameObjectPool` 用法：

```csharp
IPoolService pools = Framework.Resolve<IPoolService>();
pools.CreateGameObjectPool("Bullet", bulletPrefab, maxSize: 200, prewarm: 50);

GameObject bullet = pools.Spawn("Bullet");
pools.Despawn("Bullet", bullet);

PoolStats bulletStats = pools.GetGameObjectPoolStats("Bullet");
pools.ClearGameObjectPool("Bullet");
```

如果 prefab 上的组件实现 `IPoolable`，对象取出和归还时会收到回调：

```csharp
public sealed class BulletView : MonoBehaviour, IPoolable
{
    public void OnSpawned() { }
    public void OnDespawned() { }
}
```

生产建议：

- 池化对象必须清理状态，尤其是动画、粒子、协程、TrailRenderer、事件订阅。
- 大量对象池建议把 `PoolStats` 接入 Diagnostics 面板，查看活跃数、缓存数、创建数和溢出销毁次数。
- `GameObjectPool.Clear()` 当前只销毁 inactive 对象，活跃对象需由业务归还或场景生命周期处理。

## Scenes 模块

`SceneService` 封装 Unity `SceneManager`。

实现方式：

- 同步加载调用 `SceneManager.LoadScene`，默认会先检查场景是否在 Build Settings 中。
- 异步加载调用 `LoadSceneAsync`，返回 `SceneLoadOperation`。
- `SceneLoadArgs.ValidateInBuildSettings` 默认开启，能在运行时更早发现漏加构建场景。
- `SceneLoadArgs.ActivateOnLoad=false` 时可等待加载到可激活状态，再调用 `SceneLoadOperation.Activate()`。
- `SceneLoadArgs.AllowConcurrentLoads=false` 默认阻止重复并发加载，避免多个 Loading 流程互相覆盖。
- `SceneLoadArgs.SetActiveOnComplete` 可在加载完成后把目标场景设为 active scene。
- `SceneService.IsLoading`、`CurrentOperation`、`LoadStarted`、`LoadProgress`、`LoadCompleted` 可用于驱动统一 Loading UI。
- `SceneLoadArgs.Progress` 每帧回调归一化进度。
- 完成后回调 `SceneLoadArgs.Completed`。

用法：

```csharp
ISceneService scenes = Framework.Resolve<ISceneService>();

scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "Battle",
    Mode = LoadSceneMode.Single,
    ActivateOnLoad = true,
    SetActiveOnComplete = true,
    Progress = p => loadingBar.value = p,
    Completed = scene => Debug.Log(scene.name)
});
```

手动激活：

```csharp
SceneLoadOperation operation = scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "Battle",
    ActivateOnLoad = false
});

while (!operation.IsReadyToActivate)
{
    await UniTask.Yield();
}

operation.Activate();
```

生产建议：

- Loading 场景建议和 UI 模块约定统一流程，统一使用 `IsLoading`、进度事件和手动激活点。
- 大项目通常需要场景切换状态机、资源预加载、黑屏/转场、失败回退。

## Input 模块

`InputService` 支持新 Input System 和 Legacy Input 的条件编译。

当前项目 `ProjectSettings` 中 `activeInputHandler` 为 2，表示 Both，新旧输入都启用。

实现方式：

- 启用 `ENABLE_INPUT_SYSTEM` 时，服务保存 `InputActionAsset`，按 `InputContext` 启用 `Gameplay` 或 `UI` ActionMap。
- 未启用 Input System 时，服务提供 `GetKey` 和 `GetKeyDown`。
- `InputContext.Disabled` 会关闭输入。
- `PushContext()` 会把当前上下文压栈并切换到新上下文，返回的 `IDisposable` 释放后自动恢复，适合弹窗、过场、Loading、剧情对话等临时输入屏蔽场景。
- `PopContext()` 可手动恢复上一个上下文，返回 false 表示当前没有可恢复的上下文。
- `ApplyBindingOverride()`、`SaveBindingOverridesAsJson()`、`LoadBindingOverridesFromJson()` 和 `ClearBindingOverrides()` 可用于设置界面的按键重绑定和持久化。

Input System 用法：

```csharp
IInputService input = Framework.Resolve<IInputService>();
input.SetActions(actionAsset);
input.SetContext(InputContext.Gameplay);

if (input.WasPressedThisFrame("Jump"))
{
    Jump();
}
```

绑定覆盖：

```csharp
input.ApplyBindingOverride("Jump", bindingIndex: 0, overridePath: "<Keyboard>/space");

string json = input.SaveBindingOverridesAsJson();
Framework.Resolve<IPreferencesService>().SetString("input.bindings", json);

string saved = Framework.Resolve<IPreferencesService>().GetString("input.bindings", string.Empty);
input.LoadBindingOverridesFromJson(saved);
```

临时切换输入上下文：

```csharp
public sealed class ShopPanel : UIPanelBase
{
    private IDisposable inputScope;

    protected override void OnOpen(object args)
    {
        inputScope = Framework.Resolve<IInputService>().PushContext(InputContext.UI);
    }

    protected override void OnClose()
    {
        inputScope?.Dispose();
        inputScope = null;
    }
}

IDisposable scope = input.PushContext(InputContext.Disabled);
// 播放不可打断过场
scope.Dispose();
```

Legacy 用法：

```csharp
if (input.GetKeyDown(KeyCode.Space))
{
    Jump();
}
```

生产建议：

- 当前 `Frame.Runtime.asmdef` 显式引用了 `Unity.InputSystem`，项目已安装该包时没有问题。
- 如果想让框架迁移到未安装 Input System 的项目，建议把 Input System 适配拆成独立 asmdef 或改用 version define。
- 大项目仍建议继续补设备切换、手柄提示图标、复合绑定交互 UI 和 UI 焦点规则。

## Tweening 模块

框架核心只定义 `ITweenService` 和 `ITweenHandle`，实际实现由 `Integrations/DOTween` 提供。

实现方式：

- `DOTweenModuleInstaller` 被 `Framework.RegisterInstalledModules()` 反射发现。
- 如果 `FrameSettings.EnableTweenService` 为 true，注册 `DOTweenTweenService`。
- `DOTweenTweenService` 把框架的 `TweenEase` 映射到 DOTween `Ease`。
- 业务层依赖 `ITweenService`，不直接依赖 DOTween 类型。

用法：

```csharp
ITweenService tweens = Framework.Resolve<ITweenService>();

tweens.Move(transform, new Vector3(0, 2, 0), 0.3f, local: true, new TweenOptions
{
    Ease = TweenEase.OutBack,
    IgnoreTimeScale = true,
    Completed = OnMoveCompleted
});
```

CanvasGroup 淡入：

```csharp
tweens.Fade(canvasGroup, 1f, 0.2f);
```

停止目标上的动画：

```csharp
tweens.Kill(transform);
```

生产建议：

- 建议确认 DOTween 插件 asmdef/reference 在目标项目中稳定可编译。
- UI 动画应统一封装进入面板基类或 UI 动画服务。
- 大项目需要补 sequence、颜色、旋转、路径动画等常用接口，避免业务层直接绕过抽象。

## Localization 模块

`LocalizationService` 提供轻量本地化。当前数据模型改为表格型，一张 `LocalizedTextTable` 可以包含多种语言，适合从 Excel 导出 CSV/TSV 后导入。

实现方式：

- 当前语言保存在 `currentLocale`。
- `FallbackLocale` 默认是 `en`，当前语言找不到 key 时会回退到 fallback locale。
- 外部通过 `AddTable(LocalizedTextTable)` 注册一张或多张多语言表，后注册的表可以覆盖先注册表中的同名 key。
- `RemoveTable()` 和 `ClearTables()` 可用于卸载活动、DLC 或远程语言包。
- `LocalizedTextTable` 支持 `key,en,zh,ja` 这种第一列 key、后续列为 locale 的表格结构。
- `LocalizedTextTable` 可以在 Inspector 中直接引用 Excel 导出的 CSV/TSV `TextAsset`，也可以通过 `SetSource`、`ImportCsv`、`ImportTsv` 导入整张表格文本。
- `LocalizedTextTable` 在运行时懒构建 `locale -> key -> value` 字典缓存，单表查找复杂度为 O(1)。
- `LocalizedText` 组件可挂在 UGUI `Text` 上，填写 key 后会在启用和语言切换时自动刷新文本。
- `Translate()` 支持 `string.Format` 风格参数。
- 缺失 key 时返回 fallback；fallback 为空则返回 key，并记录到 `MissingKeys`，可用 `ClearMissingKeys()` 清空。

用法：

CSV 示例：

```csv
key,en,zh
menu.start,Start,开始
menu.quit,Quit,退出
```

```csharp
ILocalizationService localization = Framework.Resolve<ILocalizationService>();
localization.AddTable(menuTextTable);
localization.SetLocale("en");

title.text = localization.Translate("menu.start", "Start");
score.text = localization.Translate("battle.score", "Score {0}", scoreValue);

foreach (string missingKey in localization.MissingKeys)
{
    Debug.LogWarning(missingKey);
}
```

UGUI 文本自动本地化：

1. 在带 `Text` 的 GameObject 上添加 `Frame/Localization/Localized Text`。
2. 在组件的 `Key` 字段填写 `menu.start`。
3. 代码调用 `localization.SetLocale("zh")` 后，组件会自动刷新成当前语言文本。

生产建议：

- 需要自动加载当前项目使用的本地化表。
- 缺失 key 可在测试或 QA 阶段导出，作为多语言验收清单。
- 多语言项目仍建议继续补复数、性别、地区 fallback 和 Excel 导入导出校验。
- 大型项目可直接接入 Unity Localization 包，同时保留 `ILocalizationService` 作为业务接口。

## StateMachine 模块

状态机是纯 C# 工具，不依赖 Unity 生命周期（需调用方在 `Update` 中手动驱动）。它参考 Unity Animator Controller 的组织方式，支持参数、条件转换、Any State、Exit Time、子状态机和多 Layer。

典型用法：

```csharp
// 状态实现 IState，或继承 StateBase 只重写需要的钩子
StateMachine machine = new StateMachine();
machine.Add(new IdleState()).WithLength(1f); // 按状态实例的运行时 Type 注册；Length 用于 Exit Time
machine.Add(runState);
machine.AddTransition<IdleState, RunState>()
    .When(StateCondition.Greater("Speed", 0.1f));
machine.AddAnyTransition<HitState>()
    .When(StateCondition.Trigger("Damaged"));

machine.Change<IdleState>();                // 也可 Change(typeof(IdleState))
machine.SetFloat("Speed", inputSpeed);
machine.SetTrigger("Damaged");
machine.Tick(Time.deltaTime);               // 在 Update 里调用
```

核心能力：

- **Animator 风格参数**：`SetFloat/SetInt/SetBool/SetTrigger`，转换用 `StateCondition` 判断，Trigger 在命中转换后自动消耗。
- **转换规则**：普通转换、`AddAnyTransition`、`WithExitTime`、`WithDuration`、优先级、可选自切换。
- **分层和嵌套**：`AddLayer("UpperBody")` 创建并行 Layer；`StateNode.CreateChildMachine()` 创建子状态机，进入父状态时自动进入子状态机默认状态。
- **类型作为状态标识**：注册状态时直接使用状态实例的运行时 `Type`，切换可用 `Change<TState>()` 或 `Change(typeof(TState))`，避免字符串拼写错误。
- **统一进入上下文**：`Change<TState>(parameter)` 会把 `StateChangeContext` 传给目标状态的 `Enter(context)`，包含 Machine、LayerName、From、To、Parameter 等信息。
- **事件中心集成**：构造时传入 `IEventBus` 后会发布 `StateMachineStateEntered`、`StateMachineStateExited`、`StateMachineTransitioned`；也可用 `BindTrigger<TEvent>("TriggerName")` 把事件转换成状态机 Trigger。
- **防重入**：在 `Enter/Exit/Tick` 内调用 `Change` 会被排队，不会递归执行切换或破坏当前 Tick 遍历。

适合：角色 AI、UI 流程、游戏阶段/关卡流程、局部玩法状态。


## Editor 工具

菜单位于 Unity 顶部 `Frame`：

- `Create Default Frame Settings`：创建默认配置资源。
- `Create GameEntry In Scene`：在当前场景创建入口。
- `Open README`：打开框架 README。
- `Validate Project`：检查 FrameSettings、GameEntry 数量、Build Settings 场景、关键包依赖、Runtime asmdef 引用、DOTween 集成资源、Resources 路径冲突、Resources/UI prefab 面板组件和 Resources/Configs JSON 格式；CI 可通过 `Frame.Editor.FrameMenuItems.ValidateProjectForCI` 调用同一套规则。

生产建议：

- `Validate Project` 已覆盖框架基础依赖、场景配置和常见 Resources 风险；项目上线前仍建议按业务补 UI 路由表、远程配置、平台能力和热更新清单检查。
- CI 构建前建议执行 `-executeMethod Frame.Editor.FrameMenuItems.ValidateProjectForCI`，校验错误会返回非 0 退出码，警告只记录日志。

## 生产化检查表

上线前建议逐项确认：

- 启动：明确使用自动启动还是场景入口启动，避免多个 `GameEntry`。
- 生命周期：暂停、恢复、失焦和退出前保存逻辑统一接入 `ILifecycleService`。
- 日志：Release 包关闭 Trace/Debug，保留关键 Error。
- 诊断：保留最近日志、FPS、内存和关键错误计数，方便真机排查。
- 服务：业务层优先依赖接口，不直接依赖具体实现。
- 资源：大项目不要长期依赖 `Resources`，应切换到 Addressables 或 YooAsset，并补齐打包、远程发布、版本和回滚流程。
- UI：制定 prefab 路径、层级、弹窗栈、遮罩、动画和销毁策略。
- 音频：保存用户音量设置，限制同屏音效数量。
- 偏好：音量、语言、画质和输入方案等轻量设置通过 Preferences 保存。
- 存档：确认版本迁移、metadata 校验、加密密钥和云同步策略。
- 网络：确认统一响应解析器、鉴权、错误码、重试策略和埋点。
- 配置：构建前校验配置引用和重复 id。
- 本地化：导出缺失 key 清单，确认地区 fallback、复数和性别规则。
- 输入：确认目标平台输入设备、action map、重绑定保存和 UI 焦点规则。
- 测试：核心模块、存档、配置、资源、UI、网络和输入都应有对应 EditMode/PlayMode 覆盖。
- 构建：增加 CI 或本地批处理编译校验。

## 已完成的优化

本次检查中已落地以下低风险改动：

- `Framework.Initialize` 增加失败清理，初始化失败时会关闭已初始化模块、清空服务并抛出带 inner exception 的 `FrameException`。
- `GameModuleBase.Initialize` 在模块 `OnInitialize` 中途失败时会尝试调用 `OnShutdown` 清理模块局部状态。
- `SaveService` 对空存档 slot 改为明确抛出 `ArgumentException`，避免静默写入 `default.json`。
- `HttpService` 跳过空 header key，配置请求失败时返回失败响应，完成回调异常会被框架日志捕获。
- `HttpRequestHandle` 增加请求 detach，避免句柄持有已经释放的 `UnityWebRequest`。
- `HttpService.ResponseParser` 和 `EnvelopeHttpResponseParser` 增加统一响应解析入口，避免业务层反复手写协议解析。
- `FrameMenuItems.ValidateProjectForCI` 增加 batchmode 入口，便于构建流水线复用编辑器项目校验并按错误退出码失败。
- `ResourcesAssetService` 的异步完成回调增加异常隔离，并修复缓存资源类型不匹配时的引用计数边界。
- `AssetHandle.Release` 对无效资源句柄变为空操作。
- `IAssetService.GetLoadedAssetStats()` 增加资源缓存快照，Runtime Diagnostics Overlay 会展示当前资源引用计数。
- `LocalizedTextTable` 增加运行时字典缓存，避免每次翻译线性遍历列表。

## 后续优化优先级

P0，上线前必须按项目情况处理：

- 增加基础测试，至少覆盖 `EventBus`、`TimerService`、`SaveService`、`ConfigService`、`ObjectPool`。
- 在项目流水线中接入 `Frame.Editor.FrameMenuItems.ValidateProjectForCI`。
- 给存档和配置加版本字段与迁移策略。
- 根据项目规模替换资源系统。

P1，商业项目强烈建议：

- 网络层按项目补鉴权刷新、错误码动作映射和请求日志。
- UI 层按项目制定面板命名、路由表、弹窗优先级、焦点导航和动画规范。
- 配置层按项目制定预加载清单、校验规则、灰度远程覆盖和回滚流程。
- 本地化补复数、性别、地区 fallback 和导入导出校验流程。

P2，规模扩大后处理：

- 拆分更多 asmdef，例如 `Frame.UI`、`Frame.Input`、`Frame.Audio`、`Frame.Networking`。
- 扩展运行时诊断面板，增加自定义业务页签、远程导出和线上开关能力。
- 深化 Addressables/YooAsset、Unity Localization、AudioMixer 等系统适配。

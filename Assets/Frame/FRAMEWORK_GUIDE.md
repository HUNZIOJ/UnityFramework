# Frame Framework Guide

本文档面向准备在真实项目中使用 `Assets/Frame` 的开发者，说明框架当前能力、生产可用性判断、模块实现方式、推荐用法、扩展方式和后续优化路线。

## 结论

当前项目可以作为中小型 Unity 项目的通用开发底座，也适合作为商业项目早期的框架起点。它已经具备清晰的模块边界、自动启动、服务注册、事件、定时器、资源、UI、音频、存档、配置、网络、输入、本地化、状态机和 DOTween 适配能力。

但它还不应被视为“开箱即用的完整生产级基础设施”。如果项目目标是长线运营、资源热更新、多语言大规模文本、复杂账号网络、强安全存档、多团队并行开发，仍需要继续补齐 Addressables/AssetBundle、配置热更新、存档版本迁移、统一网络协议、测试覆盖、诊断面板和构建流水线。

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
    Runtime/Events                类型安全事件总线
    Runtime/Time                  Update 驱动定时器
    Runtime/Pooling               C# 对象池和 GameObject 池
    Runtime/Assets                Resources 资源服务
    Runtime/Scenes                SceneManager 封装
    Runtime/UI                    UGUI 根节点、分层、面板生命周期
    Runtime/Audio                 BGM、音效、音量分组
    Runtime/Tweening              补间动画抽象接口
    Runtime/Config                JSON 和 ScriptableObject 配置入口
    Runtime/Save                  JSON 存档服务
    Runtime/Input                 InputSystem/Legacy 输入适配
    Runtime/Networking            UnityWebRequest HTTP 封装
    Runtime/Localization          轻量本地化文本表
    Runtime/StateMachine          通用状态机
    Runtime/Utilities             路径、释放等工具
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
4. `Framework.Initialize()` 创建 `ServiceRegistry`、`ModuleManager`、`CoroutineRunner` 和 `FrameContext`。
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
- `FrameLog`：统一日志入口。
- `FrameContext`：模块初始化时拿到的上下文，包含入口、设置、服务容器、根节点和协程运行器。

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

当前资源服务是 `ResourcesAssetService`，通过 `IAssetService` 对外暴露。

实现方式：

- 路径通过 `FramePathUtility.NormalizeResourcesPath` 归一化。
- 同步加载用 `Resources.Load<T>`。
- 异步加载用 `Resources.LoadAsync<T>` 和框架协程。
- 内部有 `cache` 和 `refCounts`，`AssetHandle<T>.Release()` 会减少引用计数。
- `Instantiate(path)` 会先加载 prefab，实例化后立刻释放资源句柄。
- 回调异常会被捕获并写入框架日志。

资源路径规则：

- 使用 `/`。
- 不带扩展名。
- 相对任意 `Resources` 目录。

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
```

生产建议：

- `Resources` 适合小项目和原型，不适合大型项目资源管理。
- 上线项目建议新增 `AddressablesAssetService` 或 `AssetBundleAssetService`，保持 `IAssetService` 不变。
- 需要补充资源依赖、引用泄漏检测、异步取消、加载优先级、资源分组和内存统计。

## UI 模块

UI 模块基于 UGUI，实现了根节点、层级和面板生命周期。

实现方式：

- `UIService.OnInitialize()` 自动创建 `UIRoot`。
- `UIRoot` 创建 `Background`、`Normal`、`Popup`、`Tips`、`Loading`、`System` 层。
- 每个层是一个带独立 Canvas 的 RectTransform，sortingOrder 等于 `UILayer` 枚举值。
- `UIService.Open<TPanel>()` 通过资源服务加载 prefab，挂到指定层。
- 面板必须继承 `UIPanelBase`。
- `UIPanelBase` 生命周期为 `OnCreate`、`OnOpen`、`OnClose`、`OnDispose`。
- 默认支持缓存面板，关闭时隐藏，销毁时释放。

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

关闭 UI：

```csharp
panel.Close();
ui.CloseTop();
ui.CloseAll(destroy: true);
```

生产建议：

- 为 UI prefab 制定资源目录规范，例如 `Resources/UI/<Feature>/<PanelName>`。
- 面板里订阅事件或计时器时，用 `OnClose` 或 `OnDispose` 清理。
- 大项目建议引入 UI 路由、面板栈规则、弹窗队列、遮罩策略、打开参数类型化、加载中状态和动画统一规范。

## Audio 模块

`AudioService` 提供音乐和音效播放。

实现方式：

- 初始化时创建 `Audio` 根节点。
- 单独创建一个循环播放的 `musicSource`。
- 预热一组 SFX `AudioSource`，空闲 source 会隐藏。
- 分组音量包括 `Master`、`Music`、`Sfx`、`UI`、`Ambient`。
- BGM 支持淡入淡出。
- OneShot 播放后通过协程按 clip 时长归还 source。

播放音乐：

```csharp
IAudioService audio = Framework.Resolve<IAudioService>();
audio.PlayMusic(bgmClip, fadeSeconds: 1f, volume: 0.8f);
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

`SaveService` 使用 `Application.persistentDataPath/<SaveFolderName>` 保存 JSON 文件。

实现方式：

- 默认序列化器是 `NewtonsoftSaveSerializer`。
- 存档文件扩展名是 `.json`。
- 保存时先写 `.tmp`，再替换正式文件。
- 如果正式文件已存在，使用 `.bak` 作为备份。
- 读取失败时会尝试读取备份文件。
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
    Debug.Log($"{slot.Slot} {slot.SizeBytes}");
}
```

自定义序列化：

```csharp
save.SetSerializer(new JsonUtilitySaveSerializer());
```

生产建议：

- 必须给存档结构加 `Version` 字段。
- 上线前应补迁移器、校验和、加密或混淆、云存档冲突处理。
- 移动端和主机平台应评估写入频率，避免频繁同步写文件造成卡顿。
- 大存档建议改为异步写入或后台队列。

## Config 模块

`ConfigService` 统一从多个 provider 读取配置。

实现方式：

- 默认注册 `ResourcesJsonConfigProvider`，根目录为 `Resources/Configs`。
- 外部可注册自定义 `IConfigProvider`。
- 新 provider 插入到列表开头，因此优先级高于默认 provider。
- `ScriptableConfigProvider` 可手动注册 ScriptableObject 配置。

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
```

对应路径：

```text
Assets/Game/Resources/Configs/Items/sword_001.json
```

生产建议：

- 配置量大时应增加缓存和预加载。
- 需要补配置校验工具，构建前检查缺字段、重复 id、引用不存在。
- 联网项目通常需要远程配置、版本号、灰度和回滚机制。

## Networking 模块

`HttpService` 是 `UnityWebRequest` 的轻量封装。

实现方式：

- `Get` 和 `PostJson` 是便捷入口。
- `Send(HttpRequest, completed)` 支持 method、body、content-type、headers、timeout、retries、retryDelay。
- `HttpRequestHandle` 可作为协程 yield 对象，也可主动 `Cancel()`。
- 请求失败会按配置重试。
- 完成回调抛异常会被捕获，不会中断服务协程。
- 取消请求会返回 `Success=false` 和 `Error="Request canceled."`。

GET：

```csharp
IHttpService http = Framework.Resolve<IHttpService>();

http.Get("https://example.com/api/version", response =>
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

生产建议：

- 需要统一错误码映射。
- 需要鉴权刷新、公共 header 注入、环境切换、请求日志、埋点和限流。
- 需要按业务协议封装响应模型，不建议业务层直接到处处理裸 JSON 字符串。
- 对长连接、WebSocket、RPC、可靠消息，本模块不覆盖。

## Pooling 模块

框架提供两类池：

- `ObjectPool<T>`：纯 C# 对象池。
- `GameObjectPool`：prefab 实例池，由 `PoolService` 管理。

`ObjectPool<T>` 实现方式：

- 用 `Stack<T>` 保存 inactive 对象。
- 用 `HashSet<T>` 防止重复归还。
- 支持 `factory`、`onGet`、`onRelease`、`onDestroy` 回调。
- 如果对象实现 `IResettablePoolItem`，归还时自动调用 `ResetForPool()`。

用法：

```csharp
ObjectPool<BulletData> pool = new ObjectPool<BulletData>(
    () => new BulletData(),
    onRelease: item => item.Reset(),
    maxSize: 256);

BulletData data = pool.Get();
pool.Release(data);
```

`GameObjectPool` 用法：

```csharp
IPoolService pools = Framework.Resolve<IPoolService>();
pools.CreateGameObjectPool("Bullet", bulletPrefab, maxSize: 200, prewarm: 50);

GameObject bullet = pools.Spawn("Bullet");
pools.Despawn("Bullet", bullet);
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
- 大量对象池建议增加统计面板，查看峰值、活跃数、溢出销毁次数。
- `GameObjectPool.Clear()` 当前只销毁 inactive 对象，活跃对象需由业务归还或场景生命周期处理。

## Scenes 模块

`SceneService` 封装 Unity `SceneManager`。

实现方式：

- 同步加载调用 `SceneManager.LoadScene`。
- 异步加载调用 `LoadSceneAsync`，返回 `SceneLoadOperation`。
- `SceneLoadArgs.Progress` 每帧回调进度。
- 完成后回调 `SceneLoadArgs.Completed`。

用法：

```csharp
ISceneService scenes = Framework.Resolve<ISceneService>();

scenes.LoadAsync(new SceneLoadArgs
{
    SceneName = "Battle",
    Mode = LoadSceneMode.Single,
    ActivateOnLoad = true,
    Progress = p => loadingBar.value = p,
    Completed = scene => Debug.Log(scene.name)
});
```

生产建议：

- 需要补场景名校验和 BuildSettings 检查。
- Loading 场景建议和 UI 模块约定统一流程。
- 大项目通常需要场景切换状态机、资源预加载、黑屏/转场、失败回退。

## Input 模块

`InputService` 支持新 Input System 和 Legacy Input 的条件编译。

当前项目 `ProjectSettings` 中 `activeInputHandler` 为 2，表示 Both，新旧输入都启用。

实现方式：

- 启用 `ENABLE_INPUT_SYSTEM` 时，服务保存 `InputActionAsset`，按 `InputContext` 启用 `Gameplay` 或 `UI` ActionMap。
- 未启用 Input System 时，服务提供 `GetKey` 和 `GetKeyDown`。
- `InputContext.Disabled` 会关闭输入。

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
- 大项目需要输入重绑定、设备切换、手柄提示图标、输入屏蔽栈和 UI 焦点规则。

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

`LocalizationService` 提供轻量本地化。

实现方式：

- 当前语言保存在 `currentLocale`。
- 外部通过 `AddTable(LocalizedTextTable)` 注册语言表。
- `LocalizedTextTable` 在运行时懒构建字典缓存，查找复杂度为 O(1)。
- 缺失 key 时返回 fallback；fallback 为空则返回 key。

用法：

```csharp
ILocalizationService localization = Framework.Resolve<ILocalizationService>();
localization.AddTable(englishTable);
localization.SetLocale("en");

title.text = localization.Translate("menu.start", "Start");
```

生产建议：

- 需要自动加载当前语言表。
- 需要缺失 key 统计和导出。
- 多语言项目建议支持参数格式化、复数、性别、地区 fallback 和表格导入导出。
- 大型项目可直接接入 Unity Localization 包，同时保留 `ILocalizationService` 作为业务接口。

## StateMachine 模块

状态机是纯 C# 工具，不依赖 Unity 生命周期。

典型用法：

```csharp
StateMachine<string> machine = new StateMachine<string>();
machine.Add("Idle", idleState);
machine.Add("Battle", battleState);
machine.Change("Idle");
machine.Update(Time.deltaTime);
```

适合：

- 角色状态。
- UI 流程。
- 局部玩法状态。

生产建议：

- 复杂项目可补状态切换条件、栈式状态机、异步进入/退出、状态切换日志。

## Editor 工具

菜单位于 Unity 顶部 `Frame`：

- `Create Default Frame Settings`：创建默认配置资源。
- `Create GameEntry In Scene`：在当前场景创建入口。
- `Open README`：打开框架 README。
- `Validate Project`：基础校验。

生产建议：

- `Validate Project` 目前只做基础检查，应扩展为构建前校验。
- 可补充 asmdef 引用校验、资源路径校验、配置完整性校验、场景 BuildSettings 校验、DOTween 安装校验。

## 生产化检查表

上线前建议逐项确认：

- 启动：明确使用自动启动还是场景入口启动，避免多个 `GameEntry`。
- 日志：Release 包关闭 Trace/Debug，保留关键 Error。
- 服务：业务层优先依赖接口，不直接依赖具体实现。
- 资源：大项目不要长期依赖 `Resources`，替换为 Addressables/AssetBundle。
- UI：制定 prefab 路径、层级、弹窗栈、遮罩、动画和销毁策略。
- 音频：保存用户音量设置，限制同屏音效数量。
- 存档：增加版本迁移、校验、加密或云同步。
- 网络：增加统一协议、鉴权、错误码、重试策略、埋点。
- 配置：构建前校验配置引用和重复 id。
- 本地化：统计缺失 key，支持导入导出和格式化。
- 输入：确认目标平台输入设备和 action map 规则。
- 测试：为 Core、Save、Config、Events、Timer、Pooling 增加 EditMode 测试。
- 构建：增加 CI 或本地批处理编译校验。

## 已完成的优化

本次检查中已落地以下低风险改动：

- `Framework.Initialize` 增加失败清理，初始化失败时会关闭已初始化模块、清空服务并抛出带 inner exception 的 `FrameException`。
- `GameModuleBase.Initialize` 在模块 `OnInitialize` 中途失败时会尝试调用 `OnShutdown` 清理模块局部状态。
- `SaveService` 对空存档 slot 改为明确抛出 `ArgumentException`，避免静默写入 `default.json`。
- `HttpService` 跳过空 header key，配置请求失败时返回失败响应，完成回调异常会被框架日志捕获。
- `HttpRequestHandle` 增加请求 detach，避免句柄持有已经释放的 `UnityWebRequest`。
- `ResourcesAssetService` 的异步完成回调增加异常隔离，并修复缓存资源类型不匹配时的引用计数边界。
- `AssetHandle.Release` 对无效资源句柄变为空操作。
- `LocalizedTextTable` 增加运行时字典缓存，避免每次翻译线性遍历列表。

## 后续优化优先级

P0，上线前必须按项目情况处理：

- 增加基础测试，至少覆盖 `EventBus`、`TimerService`、`SaveService`、`ConfigService`、`ObjectPool`。
- 增加构建前校验菜单或 CI 命令。
- 给存档和配置加版本字段与迁移策略。
- 根据项目规模替换资源系统。

P1，商业项目强烈建议：

- 网络层增加统一响应模型、鉴权刷新、错误码映射和请求日志。
- UI 层增加面板路由、弹窗队列和动画规范。
- 配置层增加缓存、预加载、校验和远程覆盖。
- 本地化增加缺失 key 统计、参数格式化和导入导出流程。

P2，规模扩大后处理：

- 拆分更多 asmdef，例如 `Frame.UI`、`Frame.Input`、`Frame.Audio`、`Frame.Networking`。
- 增加运行时诊断面板，展示服务状态、资源引用、对象池统计、网络请求、计时器数量。
- 增加 Addressables、Unity Localization、AudioMixer 等官方系统适配。


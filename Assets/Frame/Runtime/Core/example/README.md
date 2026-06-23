# Core 模块使用示例

Core 是框架的启动、模块生命周期、服务注册、日志和基础单例层。业务代码通常只需要使用 `Framework.Resolve<T>()` 获取服务；扩展框架时才需要实现 `IFrameModule` 或 `IFrameModuleInstaller`。

## 命名空间

```csharp
using Frame.Core;
```

## 自动启动

默认情况下，`Framework` 会在 `BeforeSceneLoad` 阶段读取 `Resources/Frame/FrameSettings`。如果 `FrameSettings.AutoCreateGameEntry` 为 `true`，会自动创建名为 `Frame` 的 `GameObject` 并挂载 `GameEntry`。

推荐流程：

1. Unity 菜单执行 `Frame/Create Default Frame Settings`。
2. 在 `Assets/Frame/Resources/Frame/FrameSettings.asset` 中启用需要的模块。
3. 业务代码在框架初始化后通过 `Framework.Resolve<T>()` 获取服务。

```csharp
using Frame.Core;
using Frame.Events;
using Frame.Timing;

public sealed class ExampleBootstrapUser
{
    public void UseServices()
    {
        if (!Framework.IsInitialized)
        {
            return;
        }

        IEventBus events = Framework.Resolve<IEventBus>();
        ITimerService timers = Framework.Resolve<ITimerService>();

        events.Publish(new GameReadyEvent());
        timers.Delay(1f, () => FrameLog.Info("one second later"));
    }

    private struct GameReadyEvent
    {
    }
}
```

## 手动创建 GameEntry

如果不希望自动创建入口，可以在 `FrameSettings` 中关闭 `AutoCreateGameEntry`，然后在首场景放置 `GameEntry`。

```csharp
using Frame.Core;
using UnityEngine;

public sealed class ManualEntryCreator : MonoBehaviour
{
    [SerializeField] private FrameSettings settings;

    private void Awake()
    {
        GameEntry.Ensure(settings);
    }
}
```

## 显式初始化和关闭

常规项目不需要手动调用这些生命周期方法，`GameEntry` 会转发 Unity 生命周期。测试或自定义宿主可以直接调用：

```csharp
Framework.Initialize(entry, settings);
Framework.Start();
Framework.Update(Time.deltaTime, Time.unscaledDeltaTime);
Framework.FixedUpdate(Time.fixedDeltaTime, Time.fixedUnscaledDeltaTime);
Framework.LateUpdate(Time.deltaTime, Time.unscaledDeltaTime);
Framework.OnApplicationPause(false);
Framework.OnApplicationFocus(true);
Framework.OnApplicationQuit();
Framework.Shutdown();
```

## 服务注册和解析

`ServiceRegistry` 是 Core 内部的轻量服务容器。模块初始化时会注册接口和自身类型，业务层优先依赖接口。

```csharp
ServiceRegistry services = new ServiceRegistry();
services.Register<IExampleService>(new ExampleService());

IExampleService service = services.Resolve<IExampleService>();

if (services.TryResolve<IExampleService>(out IExampleService optional))
{
    optional.Execute();
}

services.Unregister<IExampleService>();
services.Clear();
```

```csharp
public interface IExampleService
{
    void Execute();
}

public sealed class ExampleService : IExampleService
{
    public void Execute()
    {
    }
}
```

## 自定义模块

模块需要实现 `IFrameModule`。推荐继承 `GameModuleBase`，只重写需要的生命周期。

```csharp
using Frame.Core;

public interface IScoreService
{
    int Score { get; }
    void Add(int value);
}

public sealed class ScoreModule : GameModuleBase, IScoreService
{
    public override int Priority
    {
        get { return -50; }
    }

    public int Score { get; private set; }

    protected override void OnInitialize()
    {
        Context.Services.Register<IScoreService>(this);
        Context.Services.Register(this);
    }

    public void Add(int value)
    {
        Score += value;
    }

    protected override void OnShutdown()
    {
        Score = 0;
    }
}
```

`Priority` 越小越早初始化。关闭时按相反顺序执行 `Shutdown()`，因此底层服务通常使用更小的优先级。

## 自定义模块安装器

`Framework` 初始化时会扫描当前 AppDomain 中所有 `IFrameModuleInstaller` 实现。适合把可选模块按 `FrameSettings` 条件接入。

```csharp
using Frame.Core;

public sealed class ScoreModuleInstaller : IFrameModuleInstaller
{
    public void Install(ModuleManager modules, FrameSettings settings)
    {
        modules.Add(new ScoreModule());
    }
}
```

## ModuleManager 直接使用

测试和自定义宿主可以手动管理模块：

```csharp
ModuleManager modules = new ModuleManager();
modules.Add(new ScoreModule());

modules.InitializeAll(context);
modules.StartAll();
modules.UpdateAll(deltaTime, unscaledDeltaTime);

ScoreModule score = modules.Get<ScoreModule>();
modules.ShutdownAll();
```

## FrameSettings 常用配置

- `AutoCreateGameEntry`: 是否自动创建 `GameEntry`。
- `UseDontDestroyOnLoad`: `GameEntry` 是否跨场景保留。
- `RunInBackground`: 是否后台运行。
- `TargetFrameRate`: 目标帧率，`0` 表示不设置。
- `Enable*Service`: 控制默认模块是否注册。
- YooAsset 相关字段：包名、运行模式、内置包根、远端地址和下载参数。
- `EnableRuntimeDiagnosticsOverlay`: 是否启用运行时诊断面板。
- `UIReferenceResolution`、`UIMatchWidthOrHeight`: UGUI 根节点缩放配置。
- `AudioSourcePoolSize`、音频 Mixer 配置: 控制音频服务。
- `SaveFolderName`: 存档根目录名。
- `DefaultGameObjectPoolMaxSize`: GameObject 池默认最大数量。

## 日志

```csharp
FrameLog.Trace("trace");
FrameLog.Debug("debug");
FrameLog.Info("info");
FrameLog.Warning("warning");
FrameLog.Error("error");

try
{
    ThrowSomething();
}
catch (Exception exception)
{
    FrameLog.Exception(exception);
}
```

`FrameLog.EntryWritten` 可以监听所有框架日志，`FrameLog.BufferedEntries` 保存最近的日志，`FrameLog.MaxBufferedEntries` 控制缓冲长度。

## 单例基类

纯 C# 对象可继承 `Singleton<T>`：

```csharp
public sealed class GameModel : Singleton<GameModel>
{
    private GameModel()
    {
    }

    protected override void OnSingletonInitialize()
    {
    }
}
```

Unity 组件可继承 `MonoSingleton<T>`：

```csharp
public sealed class AudioListenerHost : MonoSingleton<AudioListenerHost>
{
    protected override bool UseDontDestroyOnLoad
    {
        get { return true; }
    }
}
```

## 注意事项

- 业务层不要直接修改 `Framework.Modules` 中的默认模块，优先通过接口使用服务。
- `Framework.Resolve<T>()` 在未初始化时会抛出 `FrameException`，可用 `TryResolve` 做可选依赖。
- 自定义模块在 `OnInitialize()` 中注册服务，在 `OnShutdown()` 中释放订阅、句柄和临时状态。
- `ServiceRegistry.Clear()` 会对已注册的 `IDisposable` 服务调用 `Dispose()`。

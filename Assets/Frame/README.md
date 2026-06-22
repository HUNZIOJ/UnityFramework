# Unity Frame

`Assets/Frame` 是一个轻量、原生依赖优先的 Unity 游戏开发框架底座。它适合空项目启动、中小型项目开发，也可以作为商业项目早期的基础框架。

完整说明、模块实现细节、生产可用性评估和使用示例见：

- [FRAMEWORK_DEEP_DIVE.md](FRAMEWORK_DEEP_DIVE.md)

## 当前结论

当前框架已经具备基础生产雏形，但不应直接等同于完整生产级基础设施。

适合直接使用：

- 原型、Demo、教学项目。
- 单机小游戏。
- 中小型 Unity 项目的第一版业务底座。

上线前建议补齐：

- 按项目选择 Addressables 或 YooAsset，并补齐对应的资源分组、构建和远程发布流程。
- 云存档、资源热更新、配置远程灰度和统一网络协议。
- 网络统一协议、鉴权刷新、错误码和埋点。
- 配置校验、热更新或远程配置。
- 面向项目业务的 UI 规范、资源规范和错误处理规范。
- 完整 CI 流水线接入、平台真机验证和项目级测试覆盖。

## 目录

```text
Assets/Frame
  Frame.Runtime.asmdef          运行时程序集
  Runtime/Core                  启动入口、模块生命周期、服务注册、日志
  Runtime/Lifecycle             应用暂停、焦点和退出事件
  Runtime/Events                类型安全事件总线
  Runtime/Time                  Update 驱动定时器
  Runtime/Pooling               普通对象池与 GameObject 池
  Runtime/Assets                资源服务接口、Resources 实现、异步请求、资源句柄
  Runtime/Scenes                SceneManager 封装
  Runtime/UI                    UGUI 根节点、分层、路由、返回栈、模态、动画、安全区
  Runtime/Audio                 BGM/SFX/UI 音频播放与分组音量
  Runtime/Tweening              补间动画抽象接口
  Runtime/Config                基于 IAssetService 的 JSON/ScriptableObject 配置入口
  Runtime/Save                  persistentDataPath JSON/二进制存档
  Runtime/Preferences           PlayerPrefs 用户偏好设置
  Runtime/Input                 InputSystem/Legacy 输入服务适配
  Runtime/Networking            UnityWebRequest HTTP、TCP Socket 和 WebSocket 封装
  Runtime/Localization          Excel/CSV 风格多语言文本表
  Runtime/StateMachine          通用状态机
  Runtime/Utilities             路径、释放等工具
  Integrations/Addressables     Addressables 对 IAssetService 的实现
  Integrations/YooAsset         YooAsset 对 IAssetService 的实现
  Integrations/DOTween          DOTween 对 ITweenService 的实现
  Editor                        编辑器菜单和校验工具
  Samples                       调用示例
```

## 启动方式

默认会在 `BeforeSceneLoad` 自动创建 `Frame` 对象并初始化框架。

如果想显式控制：

1. 菜单执行 `Frame/Create Default Frame Settings`，创建 `Assets/Frame/Resources/Frame/FrameSettings.asset`。
2. 菜单执行 `Frame/Create GameEntry In Scene`，在场景中创建入口。
3. 在 `FrameSettings` 中关闭不需要的模块。

## 常用入口

```csharp
using Frame.Core;
using Frame.Events;
using Frame.Timing;

IEventBus events = Framework.Resolve<IEventBus>();
ITimerService timers = Framework.Resolve<ITimerService>();
```

业务代码建议优先依赖接口，例如 `IEventBus`、`ITimerService`、`IAssetService`、`IUIService`、`ISaveService`、`IHttpService`、`ISocketService`。

## 模块概览

- `Core`：`GameEntry` 负责 Unity 生命周期，`Framework` 负责模块注册和服务解析，`ModuleManager` 按优先级初始化和倒序销毁模块。
- `Lifecycle`：对业务暴露应用暂停、焦点变化和退出前事件，避免业务对象各自挂 Unity 生命周期回调。
- `Events`：支持订阅、一次性订阅、按 owner 批量解绑，适合 UI/系统间解耦。
- `Diagnostics`：运行时日志缓冲、日志事件、日志文件落盘、FPS/内存/错误计数快照和可选 IMGUI 调试面板。
- `Time`：支持缩放/非缩放延迟、循环定时器、owner 批量取消和运行时计时器统计。
- `Pooling`：`ObjectPool<T>` 面向纯 C# 对象，`GameObjectPool` 面向 prefab 实例，支持预热、统计和清理。
- `Assets`：默认使用 `Resources`，也提供 Addressables 和 YooAsset 集成；通过 `FrameSettings.AssetServiceBackend` 切换，统一暴露 `IAssetService`、引用计数、异步请求状态/取消和资源诊断。
- `Scenes`：封装同步加载、异步加载、卸载、Build Settings 校验、加载状态/事件、手动激活和进度回调。
- `UI`：自动创建 UGUI root，内置分层、`UIPanelBase` 生命周期、路由、返回栈、模态遮罩、弹窗队列、异步打开、强类型参数和淡入淡出动画扩展点。
- `Audio`：BGM 淡入淡出、SFX source 池、Master/Music/Sfx/UI/Ambient 分组音量。
- `Tweening`：业务依赖 `ITweenService`，当前通过 DOTween 适配层实现。
- `Config`：默认通过 `IAssetService` 读取 `Configs` 下的 JSON 和 ScriptableObject 配置，支持运行时 JSON 覆盖、读取缓存、provider 优先级和 `IConfigValidator` 校验。
- `Save`：使用 `Application.persistentDataPath`，默认 Newtonsoft.Json，支持二进制 serializer、AES 加密、多 slot、列表、删除、metadata 校验、备份恢复、版本迁移、自定义 serializer。
- `Preferences`：基于 `PlayerPrefs` 的轻量用户设置服务，支持 int/float/string/bool/JSON、变更事件和显式保存。
- `Input`：项目启用 InputSystem 时可挂接 `InputActionAsset`，支持输入上下文栈和绑定覆盖保存/加载；否则保留 Legacy `KeyCode` 读取。
- `Networking`：基于 `UnityWebRequest` 的 GET/POST/PUT/DELETE，以及独立的 `SocketService` 长连接模块；支持 TCP、WebSocket、长度前缀编解码、连接状态事件、发送队列、心跳、自动重连和收发指标。
- `Localization`：轻量多语言文本表，支持 Excel 导出 CSV/TSV 的 key + 多语言列结构、fallback locale、格式化文本、缺失 key 统计，并提供 UGUI `LocalizedText` 自动刷新组件。

## 基础示例

事件：

```csharp
public struct PlayerLevelChanged
{
    public int Level;
}

Framework.Resolve<IEventBus>().Publish(new PlayerLevelChanged { Level = 3 });
```

定时器：

```csharp
TimerHandle handle = Framework.Resolve<ITimerService>()
    .Delay(1f, () => Debug.Log("delay"), unscaled: true, owner: this);
```

UI：

```csharp
Framework.Resolve<IUIService>()
    .Open<MainMenuPanel>("UI/MainMenu", UILayer.Normal);
```

输入上下文：

```csharp
IDisposable scope = Framework.Resolve<IInputService>().PushContext(InputContext.UI);
Framework.Resolve<IUIService>().OpenRoute<ShopPanel>("shop");
// 面板关闭或流程结束时调用 scope.Dispose() 恢复之前的输入上下文。
```

存档：

```csharp
Framework.Resolve<ISaveService>().Save("slot_1", saveData);
Framework.Resolve<ISaveService>().Save("slot_1", saveData, dataVersion: 2);
await Framework.Resolve<ISaveService>().SaveAsync("slot_1", saveData, dataVersion: 2);
```

偏好设置：

```csharp
IPreferencesService preferences = Framework.Resolve<IPreferencesService>();
preferences.SetFloat("audio.music", 0.8f);
preferences.SetString("locale", "zh");
preferences.Save();
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

## 编辑器工具

Unity 顶部菜单 `Frame` 提供：

- `Create Default Frame Settings`
- `Create GameEntry In Scene`
- `Open README`
- `Validate Project`

构建前建议执行 `Frame/Validate Project`；当前会检查 FrameSettings、GameEntry 数量、Build Settings 场景、关键包依赖、Runtime asmdef 引用、DOTween 集成资源、Resources 路径冲突、Resources/UI prefab 面板组件和 Resources/Configs JSON 格式。

CI 中可以调用同一套校验入口，存在错误时 Unity 会以非 0 退出码结束，警告不会让流水线失败：

```powershell
& "D:\UnityEditor\6000.4.8f1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "E:\UnityProject\Framework" `
  -executeMethod Frame.Editor.FrameMenuItems.ValidateProjectForCI `
  -quit
```

运行时排查可在 `FrameSettings` 中打开 Runtime Diagnostics Overlay，默认用反引号键切换显示，面板会展示 FPS/内存、生命周期状态、HTTP 指标、Socket 连接指标、计时器数量、场景加载状态、资源引用计数、对象池统计和最近日志。

# Unity Frame

`Assets/Frame` 是一个轻量、原生依赖优先的 Unity 游戏开发框架底座。它适合空项目启动、中小型项目开发，也可以作为商业项目早期的基础框架。

完整说明、模块实现细节、生产可用性评估和使用示例见：

- [FRAMEWORK_GUIDE.md](FRAMEWORK_GUIDE.md)

## 当前结论

当前框架已经具备基础生产雏形，但不应直接等同于完整生产级基础设施。

适合直接使用：

- 原型、Demo、教学项目。
- 单机小游戏。
- 中小型 Unity 项目的第一版业务底座。

上线前建议补齐：

- Addressables/AssetBundle 资源系统。
- 存档版本迁移、校验、加密或云存档。
- 网络统一协议、鉴权刷新、错误码和埋点。
- 配置校验、热更新或远程配置。
- UI 路由、弹窗队列、动画规范。
- EditMode/PlayMode 测试和构建前校验。

## 目录

```text
Assets/Frame
  Frame.Runtime.asmdef          运行时程序集
  Runtime/Core                  启动入口、模块生命周期、服务注册、日志
  Runtime/Events                类型安全事件总线
  Runtime/Time                  Update 驱动定时器
  Runtime/Pooling               普通对象池与 GameObject 池
  Runtime/Assets                Resources 资产加载、异步请求、资源句柄
  Runtime/Scenes                SceneManager 封装
  Runtime/UI                    UGUI 根节点、分层、面板生命周期、安全区
  Runtime/Audio                 BGM/SFX/UI 音频播放与分组音量
  Runtime/Tweening              补间动画抽象接口
  Runtime/Config                ScriptableObject/Resources JSON 配置入口
  Runtime/Save                  persistentDataPath JSON 存档
  Runtime/Input                 InputSystem/Legacy 输入服务适配
  Runtime/Networking            UnityWebRequest HTTP 封装
  Runtime/Localization          简易本地化表
  Runtime/StateMachine          通用状态机
  Runtime/Utilities             路径、释放等工具
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

业务代码建议优先依赖接口，例如 `IEventBus`、`ITimerService`、`IAssetService`、`IUIService`、`ISaveService`、`IHttpService`。

## 模块概览

- `Core`：`GameEntry` 负责 Unity 生命周期，`Framework` 负责模块注册和服务解析，`ModuleManager` 按优先级初始化和倒序销毁模块。
- `Events`：支持订阅、一次性订阅、按 owner 批量解绑，适合 UI/系统间解耦。
- `Time`：支持缩放/非缩放延迟、循环定时器、owner 批量取消。
- `Pooling`：`ObjectPool<T>` 面向纯 C# 对象，`GameObjectPool` 面向 prefab 实例。
- `Assets`：默认使用 `Resources`，路径会自动归一化。接口已预留，后续可替换为 Addressables 实现。
- `Scenes`：封装同步加载、异步加载、卸载和进度回调。
- `UI`：自动创建 UGUI root，内置 Background/Normal/Popup/Tips/Loading/System 层和 `UIPanelBase` 生命周期。
- `Audio`：BGM 淡入淡出、SFX source 池、Master/Music/Sfx/UI/Ambient 分组音量。
- `Tweening`：业务依赖 `ITweenService`，当前通过 DOTween 适配层实现。
- `Config`：支持 Resources JSON 和 ScriptableObject provider。
- `Save`：使用 `Application.persistentDataPath` 和 Newtonsoft.Json，支持多 slot、列表、删除、备份恢复、自定义 serializer。
- `Input`：项目启用 InputSystem 时可挂接 `InputActionAsset`，否则保留 Legacy `KeyCode` 读取。
- `Networking`：基于 `UnityWebRequest` 的 GET/POST/PUT/DELETE，支持 timeout、header、retry。
- `Localization`：轻量文本表，不强依赖 Unity Localization 包。

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

存档：

```csharp
Framework.Resolve<ISaveService>().Save("slot_1", saveData);
```

HTTP：

```csharp
Framework.Resolve<IHttpService>().Get("https://example.com/api/version", response =>
{
    Debug.Log(response.Success ? response.Text : response.Error);
});
```

## 编辑器工具

Unity 顶部菜单 `Frame` 提供：

- `Create Default Frame Settings`
- `Create GameEntry In Scene`
- `Open README`
- `Validate Project`

构建前建议执行 `Frame/Validate Project`，并根据项目需要扩展更多检查。

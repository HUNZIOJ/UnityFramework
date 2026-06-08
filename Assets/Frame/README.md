# Unity Frame

`Assets/Frame` 是一个轻量、原生依赖优先的 Unity 项目框架底座，适合在空项目里作为业务开发起点。当前工程使用 Unity 6000.4.8f1，框架默认不引入 Addressables、DI 容器或第三方网络库；存档序列化默认使用 Unity 官方 `com.unity.nuget.newtonsoft-json` 包，补间动画通过 DOTween 适配层提供。

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
  Runtime/Tweening              补间动画抽象接口，默认由 DOTween 适配层实现
  Runtime/Config                ScriptableObject/Resources JSON 配置入口
  Runtime/Save                  persistentDataPath JSON 存档，默认 Newtonsoft.Json serializer
  Runtime/Input                 InputSystem/Legacy 输入服务适配
  Runtime/Networking            UnityWebRequest HTTP 封装
  Runtime/Localization          简易本地化表
  Runtime/StateMachine          通用状态机
  Runtime/Utilities             路径、释放等工具
  Editor                        编辑器菜单和校验工具
  Samples                       调用示例
```

## 启动方式

默认会在 `BeforeSceneLoad` 自动创建 `Frame` 对象并初始化框架。如果想显式控制：

1. 菜单执行 `Frame/Create Default Frame Settings`，创建 `Assets/Frame/Resources/Frame/FrameSettings.asset`。
2. 菜单执行 `Frame/Create GameEntry In Scene`，在场景中创建入口。
3. 在 `FrameSettings` 中关闭不需要的模块。

## 常用入口

```csharp
using Frame.Core;
using Frame.Events;
using Frame.Timing;

IEventBus events = Framework.Resolve<IEventBus>();
TimerService timers = Framework.Resolve<TimerService>();
```

## 模块说明

- `Core`：`GameEntry` 负责 Unity 生命周期，`Framework` 负责模块注册和服务解析，`ModuleManager` 按优先级初始化和倒序销毁模块。
- `Events`：支持订阅、一次性订阅、按 owner 批量解绑，适合 UI/系统间解耦。
- `Time`：支持缩放/非缩放延迟、循环定时器、owner 批量取消。
- `Pooling`：`ObjectPool<T>` 面向纯 C# 对象，`GameObjectPool` 面向 prefab 实例。
- `Assets`：默认使用 `Resources`，路径会自动归一化。接口已经预留，后续可替换为 Addressables 实现。
- `Scenes`：封装同步加载、异步加载、卸载和进度回调。
- `UI`：自动创建 UGUI root，内置 Background/Normal/Popup/Tips/Loading/System 层和 `UIPanelBase` 生命周期。
- `Audio`：BGM 淡入淡出、SFX source 池、Master/Music/Sfx/UI/Ambient 分组音量。
- `Tweening`：业务依赖 `ITweenService`，当前通过 `Frame.DOTween` 适配 DOTween，核心不直接暴露第三方类型。
- `Config`：支持 Resources JSON 和 ScriptableObject provider，业务配置结构放业务层。
- `Save`：使用 `Application.persistentDataPath` 和 Newtonsoft.Json，支持多 slot、列表、删除、备份恢复、自定义 serializer。
- `Input`：项目启用 InputSystem 时可挂接 `InputActionAsset`，否则保留 Legacy `KeyCode` 读取。
- `Networking`：基于 `UnityWebRequest` 的 GET/POST/PUT/DELETE，支持 timeout、header、retry。
- `Localization`：轻量文本表，不强依赖 Unity Localization 包。

## 使用建议

- 业务代码不要直接写进 `Assets/Frame`，建议新建 `Assets/Game` 或 `Assets/Scripts/Game`。
- UI prefab 放到任意 `Resources` 子目录后，用 `UIService.Open<TPanel>("UI/MainMenu")` 打开。
- 资源路径使用 `/`，不带扩展名，且相对 `Resources`。
- 大项目建议后续把 UI/Input 等适配层拆成独立 asmdef，让 Core 更纯。
- 构建前执行 `Frame/Validate Project` 做基础检查。

## 生产化路线

- 服务解析优先依赖接口，例如 `IEventBus`、`ITimerService`、`IUIService`、`ISaveService`、`IHttpService`。
- 当前资源层默认是 `Resources` 实现，生产项目建议新增 Addressables/AssetBundle 实现并保持 `IAssetService` 不变。
- DOTween 已导入到 `Assets/ThirdParty/DOTween`，框架通过 `Assets/Frame/Integrations/DOTween` 注册 `ITweenService`。
- 存档已使用临时文件和备份文件降低写入中断风险；重要项目仍建议叠加版本号、迁移器、校验和或加密。
- HTTP 层已支持取消、超时、重试和重试间隔；线上项目通常还需要鉴权刷新、统一错误码、请求队列和埋点。

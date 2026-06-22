# UI 模块使用示例

UI 模块基于 UGUI，提供自动创建 `UIRoot`、分层、面板生命周期、资源加载、缓存、路由、返回栈、模态遮罩、队列打开和过渡动画。

## 命名空间

```csharp
using Frame.Core;
using Frame.UI;
using UnityEngine;
```

## 获取服务

```csharp
IUIService ui = Framework.Resolve<IUIService>();
```

## 创建面板脚本

```csharp
public sealed class MainMenuPanel : UIPanelBase
{
    protected override void OnCreate()
    {
        // 只在面板实例首次创建时调用一次。
    }

    protected override void OnOpen(object args)
    {
        // 每次打开都会调用。
    }

    protected override void OnClose()
    {
        // 每次关闭都会调用。
    }

    protected override void OnDispose()
    {
        // destroy=true 或服务关闭时调用。
    }
}
```

Prefab 要包含 `MainMenuPanel` 组件，并放到资源后端可加载的位置。Resources 后端示例：

```text
Assets/Game/Resources/UI/MainMenu.prefab
```

## 打开面板

```csharp
MainMenuPanel panel = ui.Open<MainMenuPanel>(
    resourcesPath: "UI/MainMenu",
    layer: UILayer.Normal,
    args: null,
    cache: true);
```

关闭：

```csharp
ui.Close(panel);
panel.Close();
ui.CloseTop();
ui.CloseAll();
```

`destroy=false` 时缓存面板会隐藏并保留实例；`destroy=true` 会销毁并从缓存移除。

## 强类型参数面板

```csharp
public sealed class ShopArgs
{
    public int TabIndex;
}

public sealed class ShopPanel : UIPanelBase<ShopArgs>
{
    protected override void OnOpen(ShopArgs args)
    {
        int tab = args == null ? 0 : args.TabIndex;
    }
}
```

打开：

```csharp
ui.Open<ShopPanel, ShopArgs>(
    "UI/Shop",
    new ShopArgs { TabIndex = 2 },
    UILayer.Normal,
    cache: true);
```

## UIOpenOptions

```csharp
UIOpenOptions options = new UIOpenOptions
{
    Layer = UILayer.Popup,
    Cache = true,
    Modal = true,
    CloseOnBackdrop = true,
    AllowBack = true,
    ModalColor = new Color(0f, 0f, 0f, 0.6f),
    Transition = new UIFadeTransition(duration: 0.18f)
};

ConfirmPanel panel = ui.Open<ConfirmPanel>("UI/Confirm", options, args: "Delete?");
```

可用层级：

- `Background`
- `Normal`
- `Popup`
- `Tips`
- `Loading`
- `System`

## 异步打开

```csharp
UIPanelRequest<ShopPanel> request = ui.OpenAsync<ShopPanel>(
    "UI/Shop",
    UILayer.Normal,
    args: null,
    cache: false);

yield return request;

if (request.Success)
{
    ShopPanel panel = request.Panel;
}
else
{
    Debug.LogWarning(request.Error);
}
```

## 路由

注册路由：

```csharp
ui.RegisterRoute<ShopPanel>(
    route: "shop",
    resourcesPath: "UI/Shop",
    layer: UILayer.Normal,
    cache: true,
    modal: false,
    closeOnBackdrop: false,
    allowBack: true,
    transition: new UIFadeTransition());
```

打开路由：

```csharp
ShopPanel shop = ui.OpenRoute<ShopPanel>("shop");
```

强类型参数：

```csharp
ShopPanel shop = ui.OpenRoute<ShopPanel, ShopArgs>(
    "shop",
    new ShopArgs { TabIndex = 1 });
```

检查和注销：

```csharp
bool hasRoute = ui.HasRoute("shop");
bool removed = ui.UnregisterRoute("shop");
```

也可以直接注册 `UIRoute`：

```csharp
ui.RegisterRoute(new UIRoute(
    "settings",
    "UI/Settings",
    typeof(SettingsPanel),
    new UIOpenOptions { Layer = UILayer.Popup, Modal = true }));
```

## 队列打开

队列适合公告、奖励弹窗等一次只展示一个的流程。

```csharp
ui.EnqueueRoute<RewardPanel, RewardArgs>("reward", rewardArgs);
ui.EnqueueRoute<RewardPanel, RewardArgs>("reward", nextRewardArgs);

int queued = ui.QueuedPanelCount;
ui.ClearQueuedPanels();
```

当前队列面板关闭后，下一项会自动打开。

## 返回栈

```csharp
bool handled = ui.Back();
```

`Back()` 会从顶层往下找 `AllowBack = true` 的面板并关闭它。

## 自定义过渡动画

```csharp
public sealed class InstantTransition : IUITransition
{
    public Cysharp.Threading.Tasks.UniTask PlayOpen(UIPanelBase panel)
    {
        panel.gameObject.SetActive(true);
        return Cysharp.Threading.Tasks.UniTask.CompletedTask;
    }

    public Cysharp.Threading.Tasks.UniTask PlayClose(UIPanelBase panel)
    {
        return Cysharp.Threading.Tasks.UniTask.CompletedTask;
    }
}
```

内置 `UIFadeTransition` 会优先使用 `ITweenService`，没有 Tween 服务时仍会设置最终 alpha。

## SafeAreaFitter

把 `SafeAreaFitter` 挂到需要适配刘海屏/安全区的 RectTransform 上，它会根据 `Screen.safeArea` 自动更新 anchors。

## 注意事项

- UI 模块依赖 `IAssetService` 实例化 prefab，确保资源模块已启用。
- UI prefab 必须带有对应 `UIPanelBase` 派生组件，否则打开会失败。
- 缓存 key 使用 route 或 resourcesPath。route 面板重复打开会复用同一个缓存实例。
- 模态遮罩会创建在面板同层级，`CloseOnBackdrop` 会点击遮罩关闭面板。
- `UIRoot` 会自动创建 EventSystem，InputSystem 启用时使用 `InputSystemUIInputModule`。

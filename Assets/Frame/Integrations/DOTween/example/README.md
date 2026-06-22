# DOTween 集成使用示例

DOTween 集成把 DOTween 适配成框架统一的 `ITweenService`。业务代码优先依赖 `Frame.Tweening`，避免直接绑定具体补间库。

## 前置条件

- 项目已导入 DOTween。
- 保留 `Assets/Frame/Integrations/DOTween/Frame.DOTween.asmdef`。
- `FrameSettings.EnableTweenService = true`。

`DOTweenModuleInstaller` 会在框架初始化时自动注册 `DOTweenTweenService`。

## 命名空间

业务代码：

```csharp
using Frame.Core;
using Frame.Tweening;
using UnityEngine;
```

手动访问集成实现：

```csharp
using Frame.DOTween;
```

## 使用 ITweenService

```csharp
ITweenService tweens = Framework.Resolve<ITweenService>();

ITweenHandle handle = tweens.Move(
    target: transform,
    endValue: new Vector3(0f, 2f, 0f),
    duration: 0.3f,
    local: true,
    options: new TweenOptions
    {
        Ease = TweenEase.OutBack,
        Target = transform,
        Completed = () => FrameLog.Info("move complete")
    });
```

## 数值、移动、缩放、淡入淡出

```csharp
float alpha = 0f;
tweens.To(() => alpha, value => alpha = value, 1f, 0.2f);

tweens.Move(transform, Vector3.zero, 0.2f, local: true);
tweens.Scale(transform, Vector3.one * 1.2f, 0.15f);
tweens.Fade(canvasGroup, 0f, 0.2f);
```

## 句柄控制

```csharp
handle.Pause();
handle.Play();
handle.OnComplete(() => FrameLog.Info("done"));
handle.Kill(complete: false);
```

`DOTweenTweenHandle` 会代理 DOTween `Tween` 的 `IsActive`、`IsPlaying`、`Play`、`Pause`、`Kill` 和 `OnComplete`。

## 停止目标补间

```csharp
tweens.Kill(transform);
tweens.Kill(canvasGroup, complete: true);
tweens.KillAll();
```

`DOTweenTweenService.OnShutdown()` 会调用 `KillAll(false)`。

## Ease 映射

`TweenEase` 会映射到 DOTween Ease：

- `Linear`
- `InQuad`
- `OutQuad`
- `InOutQuad`
- `InCubic`
- `OutCubic`
- `InOutCubic`
- `InBack`
- `OutBack`
- `InOutBack`

如果 `TweenOptions.EaseCurve` 不为空，会使用自定义曲线。

## UI 过渡

```csharp
UIOpenOptions options = new UIOpenOptions
{
    Layer = UILayer.Popup,
    Modal = true,
    Transition = new UIFadeTransition(duration: 0.18f, unscaledTime: true)
};

Framework.Resolve<IUIService>().Open<ConfirmPanel>("UI/Confirm", options);
```

`UIFadeTransition` 会调用 `ITweenService.Fade`。

## 手动注册

一般不需要手动注册。如果测试或自定义宿主需要：

```csharp
ModuleManager modules = new ModuleManager();
modules.Add(new DOTweenTweenService());
```

或：

```csharp
new DOTweenModuleInstaller().Install(modules, settings);
```

## 注意事项

- `DOTweenTweenService.OnInitialize()` 会调用 `DG.Tweening.DOTween.Init(false, true, LogBehaviour.ErrorsOnly)`。
- `Move` 和 `Scale` 会对 DOTween tween 设置目标为 Transform。
- `TweenOptions.Target` 不为空时会覆盖或补充目标，便于 `Kill(target)`。
- 如果业务需要 DOTween Sequence、Path 等高级能力，可以在业务层直接用 DOTween，但通用模块建议仍暴露 `ITweenService`。

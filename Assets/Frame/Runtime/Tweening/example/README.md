# Tweening 模块使用示例

Tweening 模块定义补间动画抽象接口。实际运行时实现由 DOTween 集成模块提供，业务层只依赖 `ITweenService` 和 `ITweenHandle`。

## 命名空间

```csharp
using Frame.Core;
using Frame.Tweening;
using UnityEngine;
```

## 获取服务

```csharp
ITweenService tweens = Framework.Resolve<ITweenService>();

if (!tweens.IsAvailable)
{
    return;
}
```

## 数值补间

```csharp
float value = 0f;

ITweenHandle handle = tweens.To(
    getter: () => value,
    setter: x => value = x,
    endValue: 1f,
    duration: 0.5f,
    options: new TweenOptions
    {
        Ease = TweenEase.OutQuad,
        IgnoreTimeScale = true,
        Completed = () => FrameLog.Info("done")
    });
```

## 移动 Transform

世界坐标：

```csharp
tweens.Move(transform, new Vector3(10f, 0f, 0f), 0.3f);
```

本地坐标：

```csharp
tweens.Move(
    target: transform,
    endValue: Vector3.zero,
    duration: 0.2f,
    local: true,
    options: new TweenOptions { Ease = TweenEase.InOutQuad });
```

## 缩放

```csharp
tweens.Scale(
    target: transform,
    endValue: Vector3.one * 1.2f,
    duration: 0.15f,
    options: new TweenOptions { Ease = TweenEase.OutBack });
```

## CanvasGroup 淡入淡出

```csharp
CanvasGroup canvasGroup = panel.GetComponent<CanvasGroup>();

tweens.Fade(
    target: canvasGroup,
    endValue: 1f,
    duration: 0.2f,
    options: new TweenOptions { IgnoreTimeScale = true });
```

## TweenOptions

```csharp
TweenOptions options = new TweenOptions
{
    Ease = TweenEase.OutCubic,
    EaseCurve = customCurve,
    IgnoreTimeScale = true,
    Target = gameObject,
    Completed = () => FrameLog.Info("complete")
};
```

`EaseCurve` 不为空时优先使用自定义曲线，否则使用 `Ease`。

可用 Ease：

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

## ITweenHandle

```csharp
ITweenHandle handle = tweens.Scale(transform, Vector3.one, 0.2f);

bool active = handle.IsActive;
bool playing = handle.IsPlaying;

handle.Pause();
handle.Play();
handle.OnComplete(() => FrameLog.Info("completed"));
handle.Kill(complete: false);
```

## 按目标停止

```csharp
tweens.Kill(transform);
tweens.Kill(gameObject, complete: true);
tweens.KillAll();
```

## 和 UI 过渡配合

```csharp
UIOpenOptions options = new UIOpenOptions
{
    Layer = UILayer.Popup,
    Modal = true,
    Transition = new UIFadeTransition(duration: 0.18f)
};

Framework.Resolve<IUIService>().Open<ConfirmPanel>("UI/Confirm", options);
```

`UIFadeTransition` 会通过 `ITweenService` 播放淡入淡出。

## 注意事项

- 默认工程通过 `Frame.DOTween` 集成注册 `ITweenService`。
- 如果未导入 DOTween 或关闭 `FrameSettings.EnableTweenService`，`ITweenService` 可能无法解析。
- 业务代码不要直接依赖 DOTween，除非你明确只支持该实现。
- 对临时对象创建的补间，销毁对象前调用 `Kill(target)` 可避免回调访问失效对象。

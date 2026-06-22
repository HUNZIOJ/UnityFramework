# Lifecycle 模块使用示例

Lifecycle 模块把 Unity 应用暂停、焦点变化和退出事件统一成服务事件，业务对象无需各自挂 MonoBehaviour 生命周期。

## 命名空间

```csharp
using Frame.Core;
using Frame.Lifecycle;
```

## 获取服务

```csharp
ILifecycleService lifecycle = Framework.Resolve<ILifecycleService>();
```

## 监听暂停和恢复

```csharp
public sealed class AutoSaveOnPause
{
    private ILifecycleService lifecycle;

    public void Start()
    {
        lifecycle = Framework.Resolve<ILifecycleService>();
        lifecycle.PauseChanged += OnPauseChanged;
    }

    public void Stop()
    {
        if (lifecycle != null)
        {
            lifecycle.PauseChanged -= OnPauseChanged;
        }
    }

    private void OnPauseChanged(bool paused)
    {
        if (paused)
        {
            SaveGame();
        }
    }

    private void SaveGame()
    {
    }
}
```

## 监听焦点变化

```csharp
lifecycle.FocusChanged += focused =>
{
    FrameLog.Info("focus=" + focused);
};

bool hasFocus = lifecycle.HasFocus;
```

## 监听退出

```csharp
lifecycle.Quitting += () =>
{
    FrameLog.Info("application is quitting");
};

if (lifecycle.IsQuitting)
{
    return;
}
```

## 查询当前状态

```csharp
bool paused = lifecycle.IsPaused;
bool focused = lifecycle.HasFocus;
bool quitting = lifecycle.IsQuitting;
```

## 注意事项

- `LifecycleService` 由 `GameEntry` 转发 Unity 生命周期，不需要业务对象自己实现 `OnApplicationPause`。
- 事件处理函数内部异常会被 `FrameLog.Exception` 记录。
- 模块关闭时会清空事件订阅。业务对象仍应在自身关闭时主动解绑。
- 如果某些平台暂停和焦点回调顺序不同，业务逻辑应同时检查 `IsPaused` 和 `HasFocus`。

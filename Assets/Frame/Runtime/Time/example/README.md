# Time 模块使用示例

Time 模块提供由框架 `Update` 驱动的计时器服务，支持延迟、重复、下一帧、缩放/非缩放时间和 owner 批量取消。

## 命名空间

```csharp
using Frame.Core;
using Frame.Timing;
```

## 获取服务

```csharp
ITimerService timers = Framework.Resolve<ITimerService>();
```

## 延迟执行

```csharp
TimerHandle handle = timers.Delay(1f, () =>
{
    FrameLog.Info("delay finished");
});

if (handle.IsValid)
{
    FrameLog.Info("timer id=" + handle.Id);
}
```

取消延迟：

```csharp
handle.Cancel();
```

## 使用非缩放时间

当 `Time.timeScale = 0` 时，普通计时器会暂停推进。传入 `unscaled: true` 可用于 UI、暂停界面和真实时间倒计时。

```csharp
timers.Delay(
    seconds: 0.5f,
    callback: () => FrameLog.Info("runs with unscaled time"),
    unscaled: true);
```

## 重复执行

`repeatCount = -1` 表示无限循环。

```csharp
TimerHandle heartbeat = timers.Repeat(
    interval: 5f,
    callback: () => SendHeartbeat(),
    repeatCount: -1,
    unscaled: true,
    owner: this);
```

固定次数重复：

```csharp
timers.Repeat(1f, () => FrameLog.Info("tick"), repeatCount: 3);
```

## 下一帧执行

```csharp
timers.NextFrame(() =>
{
    FrameLog.Info("called next Update");
});
```

## owner 批量取消

```csharp
public sealed class QuestPresenter
{
    public void Open()
    {
        ITimerService timers = Framework.Resolve<ITimerService>();
        timers.Delay(1f, Refresh, owner: this);
        timers.Repeat(10f, Refresh, owner: this);
    }

    public void Close()
    {
        Framework.Resolve<ITimerService>().CancelOwner(this);
    }

    private void Refresh()
    {
    }
}
```

## 通过 id 管理

```csharp
TimerHandle handle = timers.Delay(2f, DoSomething);

bool exists = timers.Contains(handle.Id);
bool canceled = timers.Cancel(handle.Id);
```

## 运行状态统计

```csharp
FrameLog.Info("active=" + timers.ActiveTimerCount);
FrameLog.Info("scaled=" + timers.ScaledTimerCount);
FrameLog.Info("unscaled=" + timers.UnscaledTimerCount);
FrameLog.Info("paused=" + timers.IsPaused);
```

`TimerService` 会监听应用暂停。应用暂停时 `IsPaused` 为 `true`，计时器不会推进。

## 注意事项

- 回调不能为空，否则会抛 `ArgumentNullException`。
- 回调抛异常会被 `FrameLog.Exception` 记录，不会中断其他计时器。
- `TimerHandle` 是值类型，只保存服务引用和 id；取消后 `IsValid` 会变成 `false`。
- 对象关闭或销毁时优先使用 owner 取消，避免回调访问已释放对象。

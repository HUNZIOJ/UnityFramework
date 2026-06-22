# Events 模块使用示例

Events 提供类型安全的事件总线，适合 UI、系统和业务对象之间解耦通信。

## 命名空间

```csharp
using Frame.Core;
using Frame.Events;
```

## 获取服务

```csharp
IEventBus eventBus = Framework.Resolve<IEventBus>();
```

## 定义和发布事件

事件可以是 `struct` 或 `class`。推荐使用不可变或只读字段，避免监听者之间互相修改。

```csharp
public struct PlayerLevelChanged
{
    public int OldLevel;
    public int NewLevel;
}

eventBus.Publish(new PlayerLevelChanged
{
    OldLevel = 2,
    NewLevel = 3
});
```

## 订阅和手动取消

```csharp
private IDisposable subscription;

public void Enable()
{
    IEventBus eventBus = Framework.Resolve<IEventBus>();
    subscription = eventBus.Subscribe<PlayerLevelChanged>(OnPlayerLevelChanged);
}

public void Disable()
{
    subscription?.Dispose();
    subscription = null;
}

private void OnPlayerLevelChanged(PlayerLevelChanged evt)
{
    FrameLog.Info("level: " + evt.OldLevel + " -> " + evt.NewLevel);
}
```

## 使用 owner 批量解绑

传入 `owner` 后，可以在对象销毁时一次性解绑该对象所有订阅。

```csharp
public sealed class BattleHud
{
    public void Open()
    {
        IEventBus eventBus = Framework.Resolve<IEventBus>();
        eventBus.Subscribe<PlayerLevelChanged>(OnPlayerLevelChanged, owner: this);
        eventBus.Subscribe<PlayerHpChanged>(OnPlayerHpChanged, owner: this);
    }

    public void Close()
    {
        Framework.Resolve<IEventBus>().UnsubscribeOwner(this);
    }

    private void OnPlayerLevelChanged(PlayerLevelChanged evt) { }
    private void OnPlayerHpChanged(PlayerHpChanged evt) { }
}

public struct PlayerHpChanged
{
    public int Current;
    public int Max;
}
```

## 只监听一次

```csharp
eventBus.Subscribe<TutorialCompleted>(
    evt => FrameLog.Info("tutorial completed"),
    once: true);

public struct TutorialCompleted
{
}
```

## 清空所有事件

```csharp
eventBus.Clear();
```

`Clear()` 会移除所有事件类型的所有订阅，通常只在测试、登出、热重载或模块关闭时使用。

## 异常处理

单个事件处理函数抛异常时，`EventBus` 会用 `FrameLog.Exception` 记录异常，并继续执行后续订阅者。

```csharp
eventBus.Subscribe<PlayerLevelChanged>(_ => throw new InvalidOperationException("bad handler"));
eventBus.Subscribe<PlayerLevelChanged>(_ => FrameLog.Info("still called"));

eventBus.Publish(new PlayerLevelChanged());
```

## 常见用法建议

- UI 打开时订阅，关闭时用 `Dispose()` 或 `UnsubscribeOwner(this)` 解绑。
- 事件只表达“已经发生的事实”，不要把事件处理当成强依赖调用链。
- 高频事件要谨慎发布，必要时合并或节流。
- 事件类型尽量放在业务模块的公开契约目录中，避免散落在 UI 类内部。

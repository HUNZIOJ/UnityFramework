# StateMachine 模块使用示例

StateMachine 模块提供纯 C# 有限状态机，支持状态生命周期、参数条件、Trigger、Any State、Exit Time、子状态机、多层状态机和事件总线联动。

## 命名空间

```csharp
using Frame.StateMachine;
```

如需事件总线：

```csharp
using Frame.Events;
```

## 定义状态

可以实现 `IState`，也可以继承 `StateBase`。

```csharp
public sealed class IdleState : StateBase
{
    public override void Enter(StateChangeContext context)
    {
        FrameLog.Info("enter idle");
    }

    public override void Tick(float deltaTime)
    {
    }

    public override void Exit()
    {
        FrameLog.Info("exit idle");
    }
}

public sealed class RunState : StateBase
{
}
```

`StateChangeContext` 提供：

- `Machine`
- `LayerName`
- `From`
- `To`
- `Parameter`
- `HasFrom`
- `HasParameter`
- `TryGetParameter<T>()`

## 创建状态机并切换

```csharp
StateMachine machine = new StateMachine();
machine.Add(new IdleState());
machine.Add(new RunState());

machine.Change<IdleState>();
machine.Tick(Time.deltaTime);

machine.Change<RunState>();
```

带参数切换：

```csharp
machine.Change<RunState>(new RunArgs { Speed = 3f });
```

状态读取参数：

```csharp
public sealed class RunState : StateBase
{
    public override void Enter(StateChangeContext context)
    {
        if (context.TryGetParameter<RunArgs>(out RunArgs args))
        {
            FrameLog.Info("speed=" + args.Speed);
        }
    }
}

public sealed class RunArgs
{
    public float Speed;
}
```

## Start 默认入口

第一个添加的状态会成为默认入口。可以调用 `Start()` 进入入口状态。

```csharp
machine.Add(new IdleState());
machine.Add(new RunState());

machine.Start();
```

`Start()` 也支持参数：

```csharp
machine.BaseLayer.Start(initialParameter);
```

## 参数

```csharp
machine.Parameters.AddFloat("Speed", 0f);
machine.Parameters.AddInt("Ammo", 30);
machine.Parameters.AddBool("Grounded", true);
machine.Parameters.AddTrigger("Attack");

machine.SetFloat("Speed", 1.2f);
machine.SetInt("Ammo", 10);
machine.SetBool("Grounded", false);
machine.SetTrigger("Attack");
machine.ResetTrigger("Attack");
```

读取：

```csharp
float speed = machine.Parameters.GetFloat("Speed");

if (machine.Parameters.TryGetBool("Grounded", out bool grounded))
{
}

bool has = machine.Parameters.Has("Ammo");
```

监听参数变化：

```csharp
machine.Parameters.Changed += changed =>
{
    FrameLog.Info(changed.Name + " " + changed.Type);
};
```

## 条件转移

```csharp
machine.AddTransition<IdleState, RunState>()
    .When(StateCondition.Greater("Speed", 0.1f));

machine.AddTransition<RunState, IdleState>()
    .When(StateCondition.Less("Speed", 0.1f));

machine.Change<IdleState>();
machine.SetFloat("Speed", 1f);
machine.Tick(Time.deltaTime);
```

条件工厂：

- `StateCondition.If("BoolOrTrigger")`
- `StateCondition.IfNot("Bool")`
- `StateCondition.Trigger("Trigger")`
- `StateCondition.Greater("FloatOrInt", threshold)`
- `StateCondition.Less("FloatOrInt", threshold)`
- `StateCondition.Equal("IntOrBool", value)`
- `StateCondition.NotEqual("IntOrBool", value)`

多个条件：

```csharp
machine.AddTransition<IdleState, RunState>()
    .WhenAll(
        StateCondition.If("Grounded"),
        StateCondition.Greater("Speed", 0.1f));
```

Trigger 命中转移后会自动重置。

## Any State 和 Exit Time

```csharp
machine.Add(new IdleState()).WithLength(1f);
machine.Add(new HitState());

machine.AddAnyTransition<HitState>()
    .WithExitTime(0.5f)
    .When(StateCondition.If("Damaged"));

machine.Change<IdleState>();
machine.SetBool("Damaged", true);
machine.Tick(0.25f); // 仍在 Idle
machine.Tick(0.25f); // 切到 Hit
```

`WithLength` 和 `WithSpeed` 会影响 `StateNode.NormalizedTime`。

## 状态内主动切换

继承 `StateBase` 后可以通过 `Machine` 访问状态机。

```csharp
public sealed class BootState : StateBase
{
    public override void Enter(StateChangeContext context)
    {
        Machine.Change<MainState>();
    }
}
```

状态机对 Enter/Tick 中的重入切换做了排队，避免递归切换。

## 子状态机

```csharp
StateNode grounded = machine.Add(new GroundedState());
StateGraph locomotion = grounded.CreateChildMachine();

locomotion.AddState(new IdleState());
locomotion.AddState(new RunState());
locomotion.AddTransition<IdleState, RunState>()
    .When(StateCondition.Greater("Speed", 0.1f));

machine.Add(new AirState());

machine.Change<GroundedState>();
```

进入父状态时会进入子图入口状态。`machine.CurrentStateType` 返回当前叶子状态。

## 多层状态机

适合角色下半身 locomotion 和上半身武器动作并行。

```csharp
StateMachine machine = new StateMachine();
machine.Add(new BaseIdleState());

StateMachineLayer upper = machine.AddLayer("UpperBody");
upper.AddState(new AimState());
upper.AddState(new ReloadState());
upper.AddTransition<AimState, ReloadState>()
    .When(StateCondition.Trigger("Reload"));

machine.Start();
machine.SetTrigger("Reload");
machine.Tick(Time.deltaTime);
```

层 API：

```csharp
StateMachineLayer layer = machine.GetLayer("UpperBody");
bool found = machine.TryGetLayer("UpperBody", out layer);
bool removed = machine.RemoveLayer("UpperBody");
```

## 事件总线联动

```csharp
IEventBus bus = Framework.Resolve<IEventBus>();
StateMachine machine = new StateMachine(bus);

machine.Add(new IdleState());
machine.Add(new HitState());
machine.AddTransition<IdleState, HitState>()
    .When(StateCondition.Trigger("Damaged"));

IDisposable binding = machine.BindTrigger<DamageEvent>("Damaged");

machine.Change<IdleState>();
bus.Publish(new DamageEvent());
machine.Tick(Time.deltaTime);
```

带过滤：

```csharp
machine.BindTrigger<DamageEvent>(
    "Damaged",
    evt => evt.Amount > 0);
```

## 状态机事件

```csharp
machine.StateEntered += context => FrameLog.Info("entered " + context.To);
machine.StateExited += evt => FrameLog.Info("exited " + evt.StateType);
machine.StateChanged += context => FrameLog.Info("changed " + context.From + " -> " + context.To);
machine.Transitioned += evt => FrameLog.Info("transitioned " + evt.From + " -> " + evt.To);
```

如果设置了 `EventBus`，状态机会发布：

- `StateMachineStateEntered`
- `StateMachineStateExited`
- `StateMachineTransitioned`

## 查询和清理

```csharp
bool hasIdle = machine.Has<IdleState>();

if (machine.TryGetState<IdleState>(out IdleState idle))
{
}

if (machine.TryGetNode<IdleState>(out StateNode node))
{
    FrameLog.Info("normalized=" + node.NormalizedTime);
}

machine.Clear();
```

## 注意事项

- 同一类型状态在同一图中只能添加一次。
- `Change<T>()` 目标不存在时返回 `false`。
- `Tick(deltaTime)` 由业务自己驱动，可放在 MonoBehaviour `Update`。
- 状态生命周期异常不会被吞掉，调用方应在合适边界处理。
- `StateTransition.Priority` 数值越小优先级越高，同优先级按创建顺序。

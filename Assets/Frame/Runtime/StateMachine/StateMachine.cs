using System;
using System.Collections.Generic;
using Frame.Events;

namespace Frame.StateMachine
{
    /// <summary>
    /// Animator 风格的分层有限状态机入口。
    /// </summary>
    /// <remarks>
    /// 它持有参数表和多个 Layer。每个 Layer 管理自己的状态图、当前状态路径和转换队列。
    /// 该状态机是纯 C# 工具，不自动挂接 Unity 生命周期，需要调用方主动调用 <see cref="Tick"/>。
    /// </remarks>
    public sealed class StateMachine
    {
        /// <summary>
        /// 默认 Layer 名称。所有便捷 Add、Change、Has 方法都代理到该 Layer。
        /// </summary>
        public const string DefaultLayerName = "Base Layer";

        /// <summary>
        /// 按创建顺序保存所有 Layer。
        /// </summary>
        private readonly List<StateMachineLayer> layers = new List<StateMachineLayer>();

        /// <summary>
        /// 按名称索引 Layer，便于通过 layerName 快速查找。
        /// </summary>
        private readonly Dictionary<string, StateMachineLayer> layersByName = new Dictionary<string, StateMachineLayer>();

        /// <summary>
        /// 创建没有事件总线集成的状态机。
        /// </summary>
        public StateMachine()
            : this(null)
        {
        }

        /// <summary>
        /// 创建状态机，并可选绑定事件总线。
        /// </summary>
        /// <param name="eventBus">事件总线；为空时只触发本地事件。</param>
        public StateMachine(IEventBus eventBus)
        {
            EventBus = eventBus;
            Parameters = new StateParameterSet();
            BaseLayer = CreateLayer(DefaultLayerName);
        }

        /// <summary>
        /// 任意状态进入后触发的本地事件。
        /// </summary>
        public event Action<StateChangeContext> StateEntered;

        /// <summary>
        /// 任意状态退出后触发的本地事件。
        /// </summary>
        public event Action<StateMachineStateExited> StateExited;

        /// <summary>
        /// 状态完成切换后触发的简化本地事件，兼容只关心 From/To 的使用场景。
        /// </summary>
        public event Action<StateChangeContext> StateChanged;

        /// <summary>
        /// 状态完成切换后触发的详细本地事件，包含触发转换规则。
        /// </summary>
        public event Action<StateTransitionContext> Transitioned;

        /// <summary>
        /// 全局参数表，供所有 Layer 的转换条件共同读取。
        /// </summary>
        public StateParameterSet Parameters { get; private set; }

        /// <summary>
        /// 默认 Layer。单层状态机一般只使用它。
        /// </summary>
        public StateMachineLayer BaseLayer { get; private set; }

        /// <summary>
        /// 可选事件总线。存在时进入、退出、转换会额外发布事件总线事件。
        /// </summary>
        public IEventBus EventBus { get; private set; }

        /// <summary>
        /// 所有 Layer 的只读列表。
        /// </summary>
        public IList<StateMachineLayer> Layers
        {
            get { return layers.AsReadOnly(); }
        }

        /// <summary>
        /// 默认 Layer 当前叶子状态实例。
        /// </summary>
        public IState CurrentState
        {
            get { return BaseLayer.CurrentState; }
        }

        /// <summary>
        /// 默认 Layer 当前叶子状态类型。
        /// </summary>
        public Type CurrentStateType
        {
            get { return BaseLayer.CurrentStateType; }
        }

        /// <summary>
        /// 默认 Layer 是否已经启动。
        /// </summary>
        public bool IsRunning
        {
            get { return BaseLayer.IsRunning; }
        }

        /// <summary>
        /// 任意 Layer 是否正在执行状态切换。
        /// </summary>
        public bool IsTransitioning
        {
            get
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    if (layers[i].IsTransitioning)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 所有 Layer 中注册的状态总数。
        /// </summary>
        public int StateCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < layers.Count; i++)
                {
                    count += layers[i].StateCount;
                }

                return count;
            }
        }

        /// <summary>
        /// 替换事件总线。替换前会取消以当前状态机为 Owner 的订阅。
        /// </summary>
        /// <param name="eventBus">新的事件总线；可为空。</param>
        public void SetEventBus(IEventBus eventBus)
        {
            if (EventBus != null && !ReferenceEquals(EventBus, eventBus))
            {
                EventBus.UnsubscribeOwner(this);
            }

            EventBus = eventBus;
        }

        /// <summary>
        /// 把事件总线中的业务事件绑定为状态机 Trigger。
        /// </summary>
        /// <typeparam name="TEvent">业务事件类型。</typeparam>
        /// <param name="triggerName">要设置的 Trigger 参数名。</param>
        /// <param name="predicate">可选过滤条件，返回 true 时才设置 Trigger。</param>
        /// <returns>事件订阅句柄。</returns>
        public IDisposable BindTrigger<TEvent>(string triggerName, Func<TEvent, bool> predicate = null)
        {
            if (EventBus == null)
            {
                throw new InvalidOperationException("An EventBus is required before binding events to triggers.");
            }

            return EventBus.Subscribe<TEvent>(
                evt =>
                {
                    if (predicate == null || predicate(evt))
                    {
                        Parameters.SetTrigger(triggerName);
                    }
                },
                this);
        }

        /// <summary>
        /// 添加一个新的并行 Layer。
        /// </summary>
        /// <param name="name">Layer 名称，必须唯一。</param>
        /// <returns>新创建的 Layer。</returns>
        public StateMachineLayer AddLayer(string name)
        {
            if (layersByName.ContainsKey(name))
            {
                throw new InvalidOperationException("State machine layer already exists: " + name);
            }

            return CreateLayer(name);
        }

        /// <summary>
        /// 移除指定 Layer。默认 Base Layer 不能被移除。
        /// </summary>
        /// <param name="name">Layer 名称。</param>
        /// <returns>成功移除时返回 true。</returns>
        public bool RemoveLayer(string name)
        {
            StateMachineLayer layer;
            if (!layersByName.TryGetValue(name, out layer))
            {
                return false;
            }

            if (ReferenceEquals(layer, BaseLayer))
            {
                return false;
            }

            layer.Clear();
            layersByName.Remove(name);
            layers.Remove(layer);
            return true;
        }

        /// <summary>
        /// 获取指定名称的 Layer；不存在时抛异常。
        /// </summary>
        public StateMachineLayer GetLayer(string name)
        {
            StateMachineLayer layer;
            if (!layersByName.TryGetValue(name, out layer))
            {
                throw new InvalidOperationException("State machine layer does not exist: " + name);
            }

            return layer;
        }

        /// <summary>
        /// 尝试获取指定名称的 Layer。
        /// </summary>
        public bool TryGetLayer(string name, out StateMachineLayer layer)
        {
            return layersByName.TryGetValue(name, out layer);
        }

        /// <summary>
        /// 向默认 Layer 添加状态。
        /// </summary>
        public StateNode Add(IState state)
        {
            return BaseLayer.AddState(state);
        }

        /// <summary>
        /// 向默认 Layer 添加普通转换。
        /// </summary>
        public StateTransition AddTransition(Type from, Type to)
        {
            return BaseLayer.AddTransition(from, to);
        }

        /// <summary>
        /// 向默认 Layer 添加普通转换。
        /// </summary>
        public StateTransition AddTransition<TFrom, TTo>()
            where TFrom : IState
            where TTo : IState
        {
            return BaseLayer.AddTransition<TFrom, TTo>();
        }

        /// <summary>
        /// 向默认 Layer 添加 Any State 转换。
        /// </summary>
        public StateTransition AddAnyTransition(Type to)
        {
            return BaseLayer.AddAnyTransition(to);
        }

        /// <summary>
        /// 向默认 Layer 添加 Any State 转换。
        /// </summary>
        public StateTransition AddAnyTransition<TTo>() where TTo : IState
        {
            return BaseLayer.AddAnyTransition<TTo>();
        }

        /// <summary>
        /// 检查默认 Layer 是否注册了指定状态类型。
        /// </summary>
        public bool Has(Type stateType)
        {
            return BaseLayer.Has(stateType);
        }

        /// <summary>
        /// 检查默认 Layer 是否注册了指定状态类型。
        /// </summary>
        public bool Has<TState>() where TState : IState
        {
            return BaseLayer.Has<TState>();
        }

        /// <summary>
        /// 尝试从默认 Layer 获取状态实例。
        /// </summary>
        public bool TryGetState(Type stateType, out IState state)
        {
            return BaseLayer.TryGetState(stateType, out state);
        }

        /// <summary>
        /// 尝试从默认 Layer 获取强类型状态实例。
        /// </summary>
        public bool TryGetState<TState>(out TState state) where TState : IState
        {
            return BaseLayer.TryGetState<TState>(out state);
        }

        /// <summary>
        /// 尝试从默认 Layer 获取状态节点。
        /// </summary>
        public bool TryGetNode(Type stateType, out StateNode node)
        {
            return BaseLayer.TryGetNode(stateType, out node);
        }

        /// <summary>
        /// 尝试从默认 Layer 获取状态节点。
        /// </summary>
        public bool TryGetNode<TState>(out StateNode node) where TState : IState
        {
            return BaseLayer.TryGetNode<TState>(out node);
        }

        /// <summary>
        /// 启动所有启用的 Layer，进入各自根状态图的 Entry 状态。
        /// </summary>
        /// <returns>至少有一个 Layer 成功启动时返回 true。</returns>
        public bool Start()
        {
            bool started = false;
            for (int i = 0; i < layers.Count; i++)
            {
                started |= layers[i].Start();
            }

            return started;
        }

        /// <summary>
        /// 切换默认 Layer 到指定状态。
        /// </summary>
        public bool Change(Type stateType)
        {
            return BaseLayer.Change(stateType);
        }

        /// <summary>
        /// 切换默认 Layer 到指定状态。
        /// </summary>
        public bool Change<TState>() where TState : IState
        {
            return BaseLayer.Change<TState>();
        }

        /// <summary>
        /// 切换默认 Layer 到指定状态，并传入业务参数。
        /// </summary>
        public bool Change(Type stateType, object parameter)
        {
            return BaseLayer.Change(stateType, parameter);
        }

        /// <summary>
        /// 切换默认 Layer 到指定状态，并传入业务参数。
        /// </summary>
        public bool Change<TState>(object parameter) where TState : IState
        {
            return BaseLayer.Change<TState>(parameter);
        }

        /// <summary>
        /// 切换指定 Layer 到指定状态。
        /// </summary>
        public bool Change(string layerName, Type stateType)
        {
            return GetLayer(layerName).Change(stateType);
        }

        /// <summary>
        /// 切换指定 Layer 到指定状态。
        /// </summary>
        public bool ChangeInLayer<TState>(string layerName) where TState : IState
        {
            return GetLayer(layerName).Change<TState>();
        }

        /// <summary>
        /// 切换指定 Layer 到指定状态，并传入业务参数。
        /// </summary>
        public bool Change(string layerName, Type stateType, object parameter)
        {
            return GetLayer(layerName).Change(stateType, parameter);
        }

        /// <summary>
        /// 切换指定 Layer 到指定状态，并传入业务参数。
        /// </summary>
        public bool ChangeInLayer<TState>(string layerName, object parameter) where TState : IState
        {
            return GetLayer(layerName).Change<TState>(parameter);
        }

        /// <summary>
        /// 驱动所有 Layer。未启动的启用 Layer 会在 Tick 中自动进入 Entry。
        /// </summary>
        /// <param name="deltaTime">由调用方传入的时间增量。</param>
        public void Tick(float deltaTime)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                layers[i].Tick(deltaTime);
            }
        }

        /// <summary>
        /// 清空状态机：取消事件总线订阅、退出并清空所有 Layer、清空参数，再创建新的默认 Layer。
        /// </summary>
        public void Clear()
        {
            if (EventBus != null)
            {
                EventBus.UnsubscribeOwner(this);
            }

            for (int i = 0; i < layers.Count; i++)
            {
                layers[i].Clear();
            }

            layers.Clear();
            layersByName.Clear();
            Parameters.Clear();
            BaseLayer = CreateLayer(DefaultLayerName);
        }

        /// <summary>
        /// 设置 Float 参数的便捷方法。
        /// </summary>
        public void SetFloat(string name, float value)
        {
            Parameters.SetFloat(name, value);
        }

        /// <summary>
        /// 设置 Int 参数的便捷方法。
        /// </summary>
        public void SetInt(string name, int value)
        {
            Parameters.SetInt(name, value);
        }

        /// <summary>
        /// 设置 Bool 参数的便捷方法。
        /// </summary>
        public void SetBool(string name, bool value)
        {
            Parameters.SetBool(name, value);
        }

        /// <summary>
        /// 设置 Trigger 参数的便捷方法。
        /// </summary>
        public void SetTrigger(string name)
        {
            Parameters.SetTrigger(name);
        }

        /// <summary>
        /// 重置 Trigger 参数的便捷方法。
        /// </summary>
        public void ResetTrigger(string name)
        {
            Parameters.ResetTrigger(name);
        }

        /// <summary>
        /// 通知状态进入，并同步发布本地事件和事件总线事件。
        /// </summary>
        internal void NotifyStateEntered(StateChangeContext context)
        {
            Action<StateChangeContext> handler = StateEntered;
            if (handler != null)
            {
                handler(context);
            }

            if (EventBus != null)
            {
                EventBus.Publish(new StateMachineStateEntered(context));
            }
        }

        /// <summary>
        /// 通知状态退出，并同步发布本地事件和事件总线事件。
        /// </summary>
        internal void NotifyStateExited(StateMachineLayer layer, Type stateType, Type to, bool hasTo)
        {
            StateMachineStateExited evt = new StateMachineStateExited(this, layer.Name, stateType, to, hasTo);

            Action<StateMachineStateExited> handler = StateExited;
            if (handler != null)
            {
                handler(evt);
            }

            if (EventBus != null)
            {
                EventBus.Publish(evt);
            }
        }

        /// <summary>
        /// 通知状态切换完成，并同步发布兼容事件、详细事件和事件总线事件。
        /// </summary>
        internal void NotifyTransitioned(StateTransitionContext context)
        {
            StateChangeContext changeContext = new StateChangeContext(
                this,
                context.LayerName,
                context.From,
                context.To,
                context.Parameter,
                context.HasFrom,
                context.HasParameter);

            Action<StateChangeContext> changedHandler = StateChanged;
            if (changedHandler != null)
            {
                changedHandler(changeContext);
            }

            Action<StateTransitionContext> transitionedHandler = Transitioned;
            if (transitionedHandler != null)
            {
                transitionedHandler(context);
            }

            if (EventBus != null)
            {
                EventBus.Publish(new StateMachineTransitioned(context));
            }
        }

        /// <summary>
        /// 创建 Layer 并加入索引。
        /// </summary>
        private StateMachineLayer CreateLayer(string name)
        {
            StateMachineLayer layer = new StateMachineLayer(this, name);
            layers.Add(layer);
            layersByName.Add(name, layer);
            return layer;
        }
    }
}

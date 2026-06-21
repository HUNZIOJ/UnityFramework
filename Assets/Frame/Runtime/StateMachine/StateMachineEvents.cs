using System;

namespace Frame.StateMachine
{
    /// <summary>
    /// 状态转换完成时传递给本地事件的上下文。
    /// </summary>
    /// <remarks>
    /// 它比 <see cref="StateChangeContext"/> 多了 <see cref="Transition"/> 字段，可以区分这次切换是
    /// 手动 Change 触发的，还是某条转换规则触发的。
    /// </remarks>
    public struct StateTransitionContext
    {
        /// <summary>
        /// 状态机内部创建转换上下文时使用。
        /// </summary>
        internal StateTransitionContext(
            StateMachine machine,
            string layerName,
            Type from,
            Type to,
            object parameter,
            bool hasFrom,
            bool hasParameter,
            StateTransition transition)
        {
            Machine = machine;
            LayerName = layerName;
            From = from;
            To = to;
            Parameter = parameter;
            HasFrom = hasFrom;
            HasParameter = hasParameter;
            Transition = transition;
        }

        /// <summary>
        /// 发生切换的状态机。
        /// </summary>
        public StateMachine Machine { get; private set; }

        /// <summary>
        /// 发生切换的 Layer 名称。
        /// </summary>
        public string LayerName { get; private set; }

        /// <summary>
        /// 切换前的叶子状态类型。首次启动时可能为空。
        /// </summary>
        public Type From { get; private set; }

        /// <summary>
        /// 切换后的最终叶子状态类型。
        /// </summary>
        public Type To { get; private set; }

        /// <summary>
        /// 本次切换携带的业务参数。
        /// </summary>
        public object Parameter { get; private set; }

        /// <summary>
        /// 是否存在有效来源状态。
        /// </summary>
        public bool HasFrom { get; private set; }

        /// <summary>
        /// 是否显式传入了业务参数。
        /// </summary>
        public bool HasParameter { get; private set; }

        /// <summary>
        /// 触发本次切换的转换规则。手动 Change 或启动 Entry 时为空。
        /// </summary>
        public StateTransition Transition { get; private set; }
    }

    /// <summary>
    /// 状态进入事件。构造状态机时传入 EventBus 后会发布该事件。
    /// </summary>
    public struct StateMachineStateEntered
    {
        /// <summary>
        /// 由状态进入上下文转换为事件数据。
        /// </summary>
        internal StateMachineStateEntered(StateChangeContext context)
        {
            Machine = context.Machine;
            LayerName = context.LayerName;
            From = context.From;
            StateType = context.To;
            Parameter = context.Parameter;
            HasFrom = context.HasFrom;
            HasParameter = context.HasParameter;
        }

        /// <summary>
        /// 发生进入事件的状态机。
        /// </summary>
        public StateMachine Machine { get; private set; }

        /// <summary>
        /// 发生进入事件的 Layer 名称。
        /// </summary>
        public string LayerName { get; private set; }

        /// <summary>
        /// 来源状态类型。首次进入时可能为空。
        /// </summary>
        public Type From { get; private set; }

        /// <summary>
        /// 刚进入的状态类型。
        /// </summary>
        public Type StateType { get; private set; }

        /// <summary>
        /// 进入状态时携带的业务参数。
        /// </summary>
        public object Parameter { get; private set; }

        /// <summary>
        /// 是否存在有效来源状态。
        /// </summary>
        public bool HasFrom { get; private set; }

        /// <summary>
        /// 是否显式传入了业务参数。
        /// </summary>
        public bool HasParameter { get; private set; }
    }

    /// <summary>
    /// 状态退出事件。构造状态机时传入 EventBus 后会发布该事件。
    /// </summary>
    public struct StateMachineStateExited
    {
        /// <summary>
        /// 状态机内部创建退出事件时使用。
        /// </summary>
        internal StateMachineStateExited(StateMachine machine, string layerName, Type stateType, Type to, bool hasTo)
        {
            Machine = machine;
            LayerName = layerName;
            StateType = stateType;
            To = to;
            HasTo = hasTo;
        }

        /// <summary>
        /// 发生退出事件的状态机。
        /// </summary>
        public StateMachine Machine { get; private set; }

        /// <summary>
        /// 发生退出事件的 Layer 名称。
        /// </summary>
        public string LayerName { get; private set; }

        /// <summary>
        /// 刚退出的状态类型。
        /// </summary>
        public Type StateType { get; private set; }

        /// <summary>
        /// 本次退出将要去往的最终目标状态类型。清理状态机时可能为空。
        /// </summary>
        public Type To { get; private set; }

        /// <summary>
        /// 是否存在有效目标状态。
        /// </summary>
        public bool HasTo { get; private set; }
    }

    /// <summary>
    /// 状态转换完成事件。构造状态机时传入 EventBus 后会发布该事件。
    /// </summary>
    public struct StateMachineTransitioned
    {
        /// <summary>
        /// 由转换上下文转换为事件数据。
        /// </summary>
        internal StateMachineTransitioned(StateTransitionContext context)
        {
            Machine = context.Machine;
            LayerName = context.LayerName;
            From = context.From;
            To = context.To;
            Parameter = context.Parameter;
            HasFrom = context.HasFrom;
            HasParameter = context.HasParameter;
            Transition = context.Transition;
        }

        /// <summary>
        /// 发生转换的状态机。
        /// </summary>
        public StateMachine Machine { get; private set; }

        /// <summary>
        /// 发生转换的 Layer 名称。
        /// </summary>
        public string LayerName { get; private set; }

        /// <summary>
        /// 切换前的叶子状态类型。
        /// </summary>
        public Type From { get; private set; }

        /// <summary>
        /// 切换后的最终叶子状态类型。
        /// </summary>
        public Type To { get; private set; }

        /// <summary>
        /// 本次切换携带的业务参数。
        /// </summary>
        public object Parameter { get; private set; }

        /// <summary>
        /// 是否存在有效来源状态。
        /// </summary>
        public bool HasFrom { get; private set; }

        /// <summary>
        /// 是否显式传入了业务参数。
        /// </summary>
        public bool HasParameter { get; private set; }

        /// <summary>
        /// 触发本次切换的转换规则。手动 Change 或启动 Entry 时为空。
        /// </summary>
        public StateTransition Transition { get; private set; }
    }
}

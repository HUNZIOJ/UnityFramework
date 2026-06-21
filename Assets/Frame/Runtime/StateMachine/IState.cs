using System;

namespace Frame.StateMachine
{
    /// <summary>
    /// 状态类型相关的内部工具。所有注册进有限状态机的类型都必须实现 <see cref="IState"/>。
    /// </summary>
    internal static class StateTypeUtility
    {
        /// <summary>
        /// 校验状态类型是否有效；用于公开 API 入参检查，避免把普通类当作状态注册或切换。
        /// </summary>
        /// <param name="stateType">要检查的状态类型。</param>
        /// <param name="parameterName">抛异常时使用的参数名。</param>
        public static void EnsureStateType(Type stateType, string parameterName)
        {
            if (stateType == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!typeof(IState).IsAssignableFrom(stateType))
            {
                throw new ArgumentException("State type must implement IState.", parameterName);
            }
        }

        /// <summary>
        /// 返回用于日志和异常信息的状态类型名称。
        /// </summary>
        /// <param name="stateType">状态类型，可以为空。</param>
        /// <returns>类型完整名；为空时返回空字符串。</returns>
        public static string GetDisplayName(Type stateType)
        {
            return stateType == null ? string.Empty : stateType.FullName;
        }
    }

    /// <summary>
    /// 状态进入时传递给 <see cref="IState.Enter"/> 的上下文。
    /// </summary>
    /// <remarks>
    /// 它记录本次切换属于哪台状态机、哪一层、从哪个状态来、进入哪个状态，以及调用
    /// <c>Change</c> 时额外传入的业务参数。
    /// </remarks>
    public struct StateChangeContext
    {
        /// <summary>
        /// 创建一个只知道目标状态的上下文；常用于手动构造测试数据。
        /// </summary>
        /// <param name="to">即将进入的目标状态类型。</param>
        public StateChangeContext(Type to)
            : this(null, null, null, to, null, false, false)
        {
        }

        /// <summary>
        /// 创建一个只包含来源状态和目标状态的上下文。
        /// </summary>
        /// <param name="from">离开的来源状态类型。</param>
        /// <param name="to">进入的目标状态类型。</param>
        public StateChangeContext(Type from, Type to)
            : this(null, null, from, to, null, true, false)
        {
        }

        /// <summary>
        /// 创建一个带业务参数的状态切换上下文。
        /// </summary>
        /// <param name="from">离开的来源状态类型。</param>
        /// <param name="to">进入的目标状态类型。</param>
        /// <param name="parameter">传给目标状态的业务参数。</param>
        public StateChangeContext(Type from, Type to, object parameter)
            : this(null, null, from, to, parameter, true, true)
        {
        }

        /// <summary>
        /// 状态机内部构造完整上下文时使用。
        /// </summary>
        internal StateChangeContext(
            StateMachine machine,
            string layerName,
            Type from,
            Type to,
            object parameter,
            bool hasFrom,
            bool hasParameter)
        {
            Machine = machine;
            LayerName = layerName;
            From = from;
            To = to;
            Parameter = parameter;
            HasFrom = hasFrom;
            HasParameter = hasParameter;
        }

        /// <summary>
        /// 发起本次切换的状态机实例；手动构造上下文时可能为空。
        /// </summary>
        public StateMachine Machine { get; private set; }

        /// <summary>
        /// 发起本次切换的 Layer 名称；用于区分多层状态机里的来源。
        /// </summary>
        public string LayerName { get; private set; }

        /// <summary>
        /// 本次切换离开的状态类型；没有来源状态时为空。
        /// </summary>
        public Type From { get; private set; }

        /// <summary>
        /// 本次切换进入的目标状态类型。
        /// </summary>
        public Type To { get; private set; }

        /// <summary>
        /// 调用 <c>Change</c> 时传入的业务参数；没有参数时为空。
        /// </summary>
        public object Parameter { get; private set; }

        /// <summary>
        /// 是否存在有效来源状态。首次进入 Entry 状态时通常为 false。
        /// </summary>
        public bool HasFrom { get; private set; }

        /// <summary>
        /// 本次切换是否显式传入了业务参数。注意参数值本身可以是 null。
        /// </summary>
        public bool HasParameter { get; private set; }

        /// <summary>
        /// 尝试按指定类型读取业务参数。
        /// </summary>
        /// <typeparam name="TParameter">期望的参数类型。</typeparam>
        /// <param name="parameter">成功时返回转换后的参数；失败时为默认值。</param>
        /// <returns>参数存在且类型匹配时返回 true。</returns>
        public bool TryGetParameter<TParameter>(out TParameter parameter)
        {
            if (Parameter is TParameter)
            {
                parameter = (TParameter)Parameter;
                return true;
            }

            parameter = default(TParameter);
            return false;
        }
    }

    /// <summary>
    /// 状态对象必须实现的最小生命周期接口。
    /// </summary>
    /// <remarks>
    /// 状态机只负责在合适的时机调用这些方法，不接管 Unity 的 MonoBehaviour 生命周期。
    /// 调用方通常在自己的 Update 中调用状态机的 Tick。
    /// </remarks>
    public interface IState
    {
        /// <summary>
        /// 状态被进入时调用一次，可在这里初始化状态内数据、播放动画或读取切换参数。
        /// </summary>
        /// <param name="context">本次状态切换的上下文。</param>
        void Enter(StateChangeContext context);

        /// <summary>
        /// 状态机每次 Tick 时转发到当前激活状态。
        /// </summary>
        /// <param name="deltaTime">由调用方传入的时间增量。</param>
        void Tick(float deltaTime);

        /// <summary>
        /// 状态被离开时调用一次，可在这里清理临时资源或取消事件订阅。
        /// </summary>
        void Exit();
    }
}

using System;
using System.Collections.Generic;

namespace Frame.StateMachine
{
    /// <summary>
    /// 状态图中的一个状态节点，是实际 <see cref="IState"/> 实例的运行时包装。
    /// </summary>
    /// <remarks>
    /// 节点保存状态实例、状态类型、显示名、计时信息、本状态发出的转换，以及可选的子状态机。
    /// 状态机切换时真正进入和退出的是节点里的 <see cref="State"/>。
    /// </remarks>
    public sealed class StateNode
    {
        /// <summary>
        /// 从本状态发出的普通转换列表。
        /// </summary>
        private readonly List<StateTransition> transitions = new List<StateTransition>();

        /// <summary>
        /// 本状态挂载的子状态机。存在时进入本状态后会继续进入子状态机 Entry。
        /// </summary>
        private StateGraph childMachine;

        /// <summary>
        /// 状态图内部创建节点时使用。
        /// </summary>
        internal StateNode(StateGraph owner, IState state)
        {
            Owner = owner;
            State = state;
            StateType = state.GetType();
            Speed = 1f;
        }

        /// <summary>
        /// 该节点所属的状态图。根图或子状态图都会通过它找到 Layer。
        /// </summary>
        public StateGraph Owner { get; private set; }

        /// <summary>
        /// 被包装的实际业务状态实例。
        /// </summary>
        public IState State { get; private set; }

        /// <summary>
        /// 状态实例的运行时类型，同一个 Layer 内必须唯一。
        /// </summary>
        public Type StateType { get; private set; }

        /// <summary>
        /// 节点显示名，默认是状态类型短名，可用于调试或编辑器展示。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 状态长度，用于计算 <see cref="NormalizedTime"/> 和 Exit Time。0 表示不按长度归一化。
        /// </summary>
        public float Length { get; set; }

        /// <summary>
        /// 状态计时速度。Tick 时 Time 会增加 deltaTime * Speed。
        /// </summary>
        public float Speed { get; set; }

        /// <summary>
        /// 当前状态已运行时间。每次进入该状态时重置为 0。
        /// </summary>
        public float Time { get; private set; }

        /// <summary>
        /// 归一化运行时间，用于 Exit Time 判断。Length 小于等于 0 时，运行过一次 Tick 后视为 1。
        /// </summary>
        public float NormalizedTime
        {
            get
            {
                if (Length <= 0f)
                {
                    return Time > 0f ? 1f : 0f;
                }

                return Time / Length;
            }
        }

        /// <summary>
        /// 本状态的子状态机；没有创建时为空。
        /// </summary>
        public StateGraph ChildMachine
        {
            get { return childMachine; }
        }

        /// <summary>
        /// 从本状态发出的只读转换列表。
        /// </summary>
        public IList<StateTransition> Transitions
        {
            get { return transitions.AsReadOnly(); }
        }

        /// <summary>
        /// 设置状态长度，负数会被钳制为 0。
        /// </summary>
        /// <param name="length">状态长度。</param>
        /// <returns>当前节点，便于链式调用。</returns>
        public StateNode WithLength(float length)
        {
            Length = length < 0f ? 0f : length;
            return this;
        }

        /// <summary>
        /// 设置状态计时速度。
        /// </summary>
        /// <param name="speed">计时速度。</param>
        /// <returns>当前节点，便于链式调用。</returns>
        public StateNode WithSpeed(float speed)
        {
            Speed = speed;
            return this;
        }

        /// <summary>
        /// 创建挂在当前状态下面的子状态机。
        /// </summary>
        /// <returns>新创建的子状态图。</returns>
        /// <exception cref="InvalidOperationException">当前状态已经有子状态机时抛出。</exception>
        public StateGraph CreateChildMachine()
        {
            if (childMachine != null)
            {
                throw new InvalidOperationException("State '" + StateTypeUtility.GetDisplayName(StateType) + "' already has a child state machine.");
            }

            childMachine = new StateGraph(Owner.Layer, this);
            return childMachine;
        }

        /// <summary>
        /// 获取已有子状态机；不存在时创建。
        /// </summary>
        /// <returns>当前状态的子状态图。</returns>
        public StateGraph GetOrCreateChildMachine()
        {
            return childMachine ?? CreateChildMachine();
        }

        /// <summary>
        /// 从当前状态添加一条到目标状态的转换。
        /// </summary>
        /// <param name="to">目标状态类型。</param>
        /// <returns>新创建的转换。</returns>
        public StateTransition AddTransition(Type to)
        {
            return Owner.AddTransition(StateType, to);
        }

        /// <summary>
        /// 从当前状态添加一条到目标状态的转换。
        /// </summary>
        /// <typeparam name="TTo">目标状态类型。</typeparam>
        /// <returns>新创建的转换。</returns>
        public StateTransition AddTransition<TTo>() where TTo : IState
        {
            return AddTransition(typeof(TTo));
        }

        /// <summary>
        /// 状态图创建转换后把它挂回来源节点。
        /// </summary>
        internal void AddTransitionInternal(StateTransition transition)
        {
            transitions.Add(transition);
        }

        /// <summary>
        /// 进入状态时重置运行时间。
        /// </summary>
        internal void ResetTime()
        {
            Time = 0f;
        }

        /// <summary>
        /// 推进状态运行时间。
        /// </summary>
        /// <param name="deltaTime">本次 Tick 的时间增量。</param>
        internal void Advance(float deltaTime)
        {
            Time += deltaTime * Speed;
            if (Time < 0f)
            {
                Time = 0f;
            }
        }
    }
}

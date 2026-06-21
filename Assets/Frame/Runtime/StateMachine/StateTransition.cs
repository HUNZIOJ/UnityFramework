using System;
using System.Collections.Generic;

namespace Frame.StateMachine
{
    /// <summary>
    /// 有限状态机中的一条状态转换规则。
    /// </summary>
    /// <remarks>
    /// 转换可以从普通状态出发，也可以是 Any State 转换。它会检查条件、Exit Time、自切换限制，
    /// 最终由 <see cref="StateMachineLayer"/> 选择优先级最高的一条执行。
    /// </remarks>
    public sealed class StateTransition
    {
        /// <summary>
        /// 本转换要求全部满足的条件列表。
        /// </summary>
        private readonly List<StateCondition> conditions = new List<StateCondition>();

        /// <summary>
        /// 状态图内部创建转换时使用。
        /// </summary>
        internal StateTransition(Type from, Type to, bool fromAny, int order)
        {
            From = from;
            To = to;
            FromAny = fromAny;
            Order = order;
            ExitTime = 1f;
            Duration = 0f;
        }

        /// <summary>
        /// 可选名称，仅用于调试、日志或编辑器展示，不参与匹配逻辑。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 来源状态类型。Any State 转换时为空。
        /// </summary>
        public Type From { get; private set; }

        /// <summary>
        /// 目标状态类型。
        /// </summary>
        public Type To { get; private set; }

        /// <summary>
        /// 是否为 Any State 转换。为 true 时不要求当前状态等于 <see cref="From"/>。
        /// </summary>
        public bool FromAny { get; private set; }

        /// <summary>
        /// 是否启用 Exit Time。启用后来源状态的 NormalizedTime 达到阈值才允许转换。
        /// </summary>
        public bool HasExitTime { get; set; }

        /// <summary>
        /// 退出时间阈值，使用来源状态的归一化时间。1 表示状态完整播放一次。
        /// </summary>
        public float ExitTime { get; set; }

        /// <summary>
        /// 转换持续时间元数据。当前状态机不做插值，只保存给上层动画或表现系统使用。
        /// </summary>
        public float Duration { get; set; }

        /// <summary>
        /// 是否允许目标状态就是当前叶子状态。默认不允许，避免条件持续满足时反复重进同一状态。
        /// </summary>
        public bool CanTransitionToSelf { get; set; }

        /// <summary>
        /// 转换优先级。数值越小优先级越高；优先级相同则按创建顺序决定。
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 创建顺序，用于同优先级转换的稳定排序。
        /// </summary>
        internal int Order { get; private set; }

        /// <summary>
        /// 内部可变条件列表，供状态机在命中转换后消费 Trigger。
        /// </summary>
        internal IList<StateCondition> ConditionList
        {
            get { return conditions; }
        }

        /// <summary>
        /// 只读条件列表。外部通过 <see cref="When"/> 或 <see cref="WhenAll"/> 添加条件。
        /// </summary>
        public IList<StateCondition> Conditions
        {
            get { return conditions.AsReadOnly(); }
        }

        /// <summary>
        /// 添加一条必须满足的转换条件。
        /// </summary>
        /// <param name="condition">要添加的条件。</param>
        /// <returns>当前转换，便于链式调用。</returns>
        public StateTransition When(StateCondition condition)
        {
            if (condition == null)
            {
                throw new ArgumentNullException("condition");
            }

            conditions.Add(condition);
            return this;
        }

        /// <summary>
        /// 一次添加多条必须同时满足的转换条件。
        /// </summary>
        /// <param name="transitionConditions">条件数组；为空时不做任何事。</param>
        /// <returns>当前转换，便于链式调用。</returns>
        public StateTransition WhenAll(params StateCondition[] transitionConditions)
        {
            if (transitionConditions == null)
            {
                return this;
            }

            for (int i = 0; i < transitionConditions.Length; i++)
            {
                When(transitionConditions[i]);
            }

            return this;
        }

        /// <summary>
        /// 启用 Exit Time，并设置来源状态归一化时间阈值。
        /// </summary>
        /// <param name="exitTime">退出时间阈值。</param>
        /// <returns>当前转换，便于链式调用。</returns>
        public StateTransition WithExitTime(float exitTime)
        {
            HasExitTime = true;
            ExitTime = exitTime;
            return this;
        }

        /// <summary>
        /// 设置转换持续时间元数据，负数会被钳制为 0。
        /// </summary>
        /// <param name="duration">转换持续时间。</param>
        /// <returns>当前转换，便于链式调用。</returns>
        public StateTransition WithDuration(float duration)
        {
            Duration = duration < 0f ? 0f : duration;
            return this;
        }

        /// <summary>
        /// 判断转换在当前参数和来源状态下是否满足。
        /// </summary>
        /// <param name="parameters">状态机参数表。</param>
        /// <param name="source">本次检测使用的来源节点。</param>
        /// <param name="currentLeafType">当前叶子状态类型，用于阻止默认自切换。</param>
        /// <returns>允许执行该转换时返回 true。</returns>
        internal bool IsMet(StateParameterSet parameters, StateNode source, Type currentLeafType)
        {
            if (!CanTransitionToSelf && To == currentLeafType)
            {
                return false;
            }

            if (HasExitTime)
            {
                if (source == null || source.NormalizedTime < ExitTime)
                {
                    return false;
                }
            }

            for (int i = 0; i < conditions.Count; i++)
            {
                if (!conditions[i].IsMet(parameters))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

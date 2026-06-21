using System;
using System.Collections.Generic;

namespace Frame.StateMachine
{
    /// <summary>
    /// 状态图，也就是一组状态节点和它们之间的转换关系。
    /// </summary>
    /// <remarks>
    /// 根状态图属于 Layer；子状态图挂在某个 <see cref="StateNode"/> 下。进入一个带子状态图的父状态时，
    /// 状态机会继续沿子状态图的 Entry 状态向下进入，形成“父状态 -> 子状态”的激活路径。
    /// </remarks>
    public sealed class StateGraph
    {
        /// <summary>
        /// 当前图内直接拥有的状态节点，按状态类型索引。
        /// </summary>
        private readonly Dictionary<Type, StateNode> states;

        /// <summary>
        /// 当前图的 Any State 转换列表。图内任意激活状态都可以触发这些转换。
        /// </summary>
        private readonly List<StateTransition> anyStateTransitions = new List<StateTransition>();

        /// <summary>
        /// 当前图是否已经配置默认入口状态。
        /// </summary>
        private bool hasEntryState;

        /// <summary>
        /// 当前图默认入口状态的类型。
        /// </summary>
        private Type entryStateType;

        /// <summary>
        /// Layer 或 StateNode 创建状态图时使用。
        /// </summary>
        internal StateGraph(StateMachineLayer layer, StateNode parentState)
        {
            Layer = layer;
            ParentState = parentState;
            states = new Dictionary<Type, StateNode>();
        }

        /// <summary>
        /// 当前状态图所属的 Layer。
        /// </summary>
        public StateMachineLayer Layer { get; private set; }

        /// <summary>
        /// 拥有这个子状态图的父状态。根状态图没有父状态，因此为空。
        /// </summary>
        public StateNode ParentState { get; private set; }

        /// <summary>
        /// 当前图直接包含的状态数量，不包含子状态图里的状态。
        /// </summary>
        public int StateCount
        {
            get { return states.Count; }
        }

        /// <summary>
        /// 当前图是否配置了 Entry 状态。
        /// </summary>
        public bool HasEntryState
        {
            get { return hasEntryState; }
        }

        /// <summary>
        /// 当前图默认入口状态类型。进入该图时会优先进入这个状态。
        /// </summary>
        public Type EntryStateType
        {
            get { return entryStateType; }
            set
            {
                StateTypeUtility.EnsureStateType(value, "value");
                if (!states.ContainsKey(value))
                {
                    throw new InvalidOperationException("Entry state does not exist in this graph: " + StateTypeUtility.GetDisplayName(value));
                }

                entryStateType = value;
                hasEntryState = true;
            }
        }

        /// <summary>
        /// 当前图直接包含的所有状态节点。
        /// </summary>
        public IEnumerable<StateNode> States
        {
            get { return states.Values; }
        }

        /// <summary>
        /// 当前图的只读 Any State 转换列表。
        /// </summary>
        public IList<StateTransition> AnyStateTransitions
        {
            get { return anyStateTransitions.AsReadOnly(); }
        }

        /// <summary>
        /// 向当前图添加状态。第一个添加的状态会自动成为 Entry 状态。
        /// </summary>
        /// <param name="state">业务状态实例。</param>
        /// <returns>包装该状态的节点。</returns>
        public StateNode AddState(IState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            Type stateType = state.GetType();
            StateNode existingInLayer;
            if (Layer.TryGetNode(stateType, out existingInLayer) && existingInLayer.Owner != this)
            {
                throw new InvalidOperationException("State type already exists in layer '" + Layer.Name + "': " + StateTypeUtility.GetDisplayName(stateType));
            }

            StateNode previous;
            if (states.TryGetValue(stateType, out previous))
            {
                Layer.UnregisterNode(previous);
            }

            StateNode node = new StateNode(this, state);
            node.Name = stateType.Name;
            states[stateType] = node;
            Layer.RegisterNode(node);

            StateBase stateBase = state as StateBase;
            if (stateBase != null)
            {
                stateBase.Machine = Layer.Owner;
            }

            if (!hasEntryState)
            {
                entryStateType = stateType;
                hasEntryState = true;
            }

            return node;
        }

        /// <summary>
        /// 检查当前图是否直接包含指定状态类型。
        /// </summary>
        public bool HasState(Type stateType)
        {
            StateTypeUtility.EnsureStateType(stateType, "stateType");
            return states.ContainsKey(stateType);
        }

        /// <summary>
        /// 检查当前图是否直接包含指定状态类型。
        /// </summary>
        public bool HasState<TState>() where TState : IState
        {
            return HasState(typeof(TState));
        }

        /// <summary>
        /// 尝试获取当前图内的状态节点。
        /// </summary>
        public bool TryGetState(Type stateType, out StateNode node)
        {
            StateTypeUtility.EnsureStateType(stateType, "stateType");
            return states.TryGetValue(stateType, out node);
        }

        /// <summary>
        /// 尝试获取当前图内的状态节点。
        /// </summary>
        public bool TryGetState<TState>(out StateNode node) where TState : IState
        {
            return TryGetState(typeof(TState), out node);
        }

        /// <summary>
        /// 获取当前图内的状态节点；不存在时抛异常。
        /// </summary>
        public StateNode GetState(Type stateType)
        {
            StateTypeUtility.EnsureStateType(stateType, "stateType");
            StateNode node;
            if (!states.TryGetValue(stateType, out node))
            {
                throw new InvalidOperationException("State does not exist in this graph: " + StateTypeUtility.GetDisplayName(stateType));
            }

            return node;
        }

        /// <summary>
        /// 添加从一个状态到另一个状态的普通转换。
        /// </summary>
        /// <param name="from">来源状态类型，必须在当前图中直接存在。</param>
        /// <param name="to">目标状态类型，可以在同一 Layer 的任意图中。</param>
        /// <returns>新创建的转换。</returns>
        public StateTransition AddTransition(Type from, Type to)
        {
            StateTypeUtility.EnsureStateType(to, "to");
            StateNode source = GetState(from);
            StateTransition transition = new StateTransition(from, to, false, Layer.NextTransitionOrder());
            source.AddTransitionInternal(transition);
            return transition;
        }

        /// <summary>
        /// 添加从一个状态到另一个状态的普通转换。
        /// </summary>
        public StateTransition AddTransition<TFrom, TTo>()
            where TFrom : IState
            where TTo : IState
        {
            return AddTransition(typeof(TFrom), typeof(TTo));
        }

        /// <summary>
        /// 添加 Any State 转换。当前图任意激活状态都可以通过它跳到目标状态。
        /// </summary>
        /// <param name="to">目标状态类型。</param>
        /// <returns>新创建的转换。</returns>
        public StateTransition AddAnyTransition(Type to)
        {
            StateTypeUtility.EnsureStateType(to, "to");
            StateTransition transition = new StateTransition(null, to, true, Layer.NextTransitionOrder());
            anyStateTransitions.Add(transition);
            return transition;
        }

        /// <summary>
        /// 添加 Any State 转换。
        /// </summary>
        public StateTransition AddAnyTransition<TTo>() where TTo : IState
        {
            return AddAnyTransition(typeof(TTo));
        }

        /// <summary>
        /// 尝试获取当前图的 Entry 节点。
        /// </summary>
        internal bool TryGetEntryNode(out StateNode node)
        {
            if (!hasEntryState)
            {
                node = null;
                return false;
            }

            return states.TryGetValue(entryStateType, out node);
        }

        /// <summary>
        /// 清空当前图及其转换，并从 Layer 全局索引中注销节点。
        /// </summary>
        internal void Clear()
        {
            foreach (StateNode node in states.Values)
            {
                Layer.UnregisterNode(node);
            }

            states.Clear();
            anyStateTransitions.Clear();
            hasEntryState = false;
            entryStateType = null;
        }
    }
}

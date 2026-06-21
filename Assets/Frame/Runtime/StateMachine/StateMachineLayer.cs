using System;
using System.Collections.Generic;

namespace Frame.StateMachine
{
    /// <summary>
    /// 状态机的一个并行 Layer。
    /// </summary>
    /// <remarks>
    /// 每个 Layer 拥有独立的根状态图、当前激活路径和转换队列。状态机 Tick 时会逐个驱动启用的 Layer。
    /// 例如 Base Layer 管角色全身动作，UpperBody Layer 可以单独管上半身攻击动作。
    /// </remarks>
    public sealed class StateMachineLayer
    {
        /// <summary>
        /// 本 Layer 内所有状态节点的全局索引，包含根图和子状态图中的节点。
        /// </summary>
        private readonly Dictionary<Type, StateNode> allStates;

        /// <summary>
        /// 当前激活路径，顺序为父状态到叶子状态，例如 Locomotion -> Idle。
        /// </summary>
        private readonly List<StateNode> currentPath = new List<StateNode>();

        /// <summary>
        /// Tick、Enter、Exit 过程中请求的切换会先进入队列，避免重入修改当前路径。
        /// </summary>
        private readonly Queue<PendingChange> pendingChanges = new Queue<PendingChange>();

        /// <summary>
        /// 转换创建顺序计数器，用于同优先级转换的稳定排序。
        /// </summary>
        private int nextTransitionOrder;

        /// <summary>
        /// 当前是否正在执行进入或退出流程。
        /// </summary>
        private bool isTransitioning;

        /// <summary>
        /// 当前是否正在调用激活状态的 Tick。
        /// </summary>
        private bool isTicking;

        /// <summary>
        /// 状态机内部创建 Layer 时使用。
        /// </summary>
        internal StateMachineLayer(StateMachine owner, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Layer name cannot be null or empty.", "name");
            }

            Owner = owner;
            Name = name;
            Enabled = true;
            allStates = new Dictionary<Type, StateNode>();
            Root = new StateGraph(this, null);
        }

        /// <summary>
        /// 拥有该 Layer 的状态机。
        /// </summary>
        public StateMachine Owner { get; private set; }

        /// <summary>
        /// Layer 名称。默认 Layer 名称为 <see cref="StateMachine.DefaultLayerName"/>。
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// 是否启用该 Layer。禁用后 Tick 不会驱动它，也不会自动进入 Entry。
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// 根状态图。通过它添加根层级状态和 Any State 转换。
        /// </summary>
        public StateGraph Root { get; private set; }

        /// <summary>
        /// 当前是否正在切换状态。
        /// </summary>
        public bool IsTransitioning
        {
            get { return isTransitioning; }
        }

        /// <summary>
        /// 当前 Layer 中注册的状态总数，包含子状态图里的状态。
        /// </summary>
        public int StateCount
        {
            get { return allStates.Count; }
        }

        /// <summary>
        /// 当前 Layer 是否已经进入了至少一个状态。
        /// </summary>
        public bool IsRunning
        {
            get { return currentPath.Count > 0; }
        }

        /// <summary>
        /// 当前激活路径的叶子节点，也就是最具体的当前状态。
        /// </summary>
        public StateNode CurrentNode
        {
            get { return currentPath.Count == 0 ? null : currentPath[currentPath.Count - 1]; }
        }

        /// <summary>
        /// 当前叶子状态实例。
        /// </summary>
        public IState CurrentState
        {
            get { return CurrentNode == null ? null : CurrentNode.State; }
        }

        /// <summary>
        /// 当前叶子状态类型。
        /// </summary>
        public Type CurrentStateType
        {
            get { return CurrentNode == null ? null : CurrentNode.StateType; }
        }

        /// <summary>
        /// 当前完整激活路径的只读视图，顺序为父状态到叶子状态。
        /// </summary>
        public IList<StateNode> ActivePath
        {
            get { return currentPath.AsReadOnly(); }
        }

        /// <summary>
        /// 向根状态图添加状态。
        /// </summary>
        public StateNode AddState(IState state)
        {
            return Root.AddState(state);
        }

        /// <summary>
        /// 向根状态图添加普通转换。
        /// </summary>
        public StateTransition AddTransition(Type from, Type to)
        {
            return Root.AddTransition(from, to);
        }

        /// <summary>
        /// 向根状态图添加普通转换。
        /// </summary>
        public StateTransition AddTransition<TFrom, TTo>()
            where TFrom : IState
            where TTo : IState
        {
            return Root.AddTransition<TFrom, TTo>();
        }

        /// <summary>
        /// 向根状态图添加 Any State 转换。
        /// </summary>
        public StateTransition AddAnyTransition(Type to)
        {
            return Root.AddAnyTransition(to);
        }

        /// <summary>
        /// 向根状态图添加 Any State 转换。
        /// </summary>
        public StateTransition AddAnyTransition<TTo>() where TTo : IState
        {
            return Root.AddAnyTransition<TTo>();
        }

        /// <summary>
        /// 检查当前 Layer 是否注册了指定状态类型。
        /// </summary>
        public bool Has(Type stateType)
        {
            StateTypeUtility.EnsureStateType(stateType, "stateType");
            return allStates.ContainsKey(stateType);
        }

        /// <summary>
        /// 检查当前 Layer 是否注册了指定状态类型。
        /// </summary>
        public bool Has<TState>() where TState : IState
        {
            return Has(typeof(TState));
        }

        /// <summary>
        /// 尝试获取当前 Layer 中的状态节点，包含子状态机里的节点。
        /// </summary>
        public bool TryGetNode(Type stateType, out StateNode node)
        {
            StateTypeUtility.EnsureStateType(stateType, "stateType");
            return allStates.TryGetValue(stateType, out node);
        }

        /// <summary>
        /// 尝试获取当前 Layer 中的状态节点，包含子状态机里的节点。
        /// </summary>
        public bool TryGetNode<TState>(out StateNode node) where TState : IState
        {
            return TryGetNode(typeof(TState), out node);
        }

        /// <summary>
        /// 尝试获取当前 Layer 中的状态实例。
        /// </summary>
        public bool TryGetState(Type stateType, out IState state)
        {
            StateNode node;
            if (!TryGetNode(stateType, out node))
            {
                state = null;
                return false;
            }

            state = node.State;
            return true;
        }

        /// <summary>
        /// 尝试获取当前 Layer 中的强类型状态实例。
        /// </summary>
        public bool TryGetState<TState>(out TState state) where TState : IState
        {
            IState raw;
            if (TryGetState(typeof(TState), out raw) && raw is TState)
            {
                state = (TState)raw;
                return true;
            }

            state = default(TState);
            return false;
        }

        /// <summary>
        /// 启动该 Layer，进入根状态图的 Entry 状态。
        /// </summary>
        /// <returns>成功进入状态时返回 true。</returns>
        public bool Start()
        {
            return Start(null, false);
        }

        /// <summary>
        /// 启动该 Layer，并把参数传给 Entry 状态。
        /// </summary>
        public bool Start(object parameter)
        {
            return Start(parameter, true);
        }

        /// <summary>
        /// 切换到指定状态。
        /// </summary>
        public bool Change(Type stateType)
        {
            return Change(stateType, null, false, null);
        }

        /// <summary>
        /// 切换到指定状态。
        /// </summary>
        public bool Change<TState>() where TState : IState
        {
            return Change(typeof(TState));
        }

        /// <summary>
        /// 切换到指定状态，并传入业务参数。
        /// </summary>
        public bool Change(Type stateType, object parameter)
        {
            return Change(stateType, parameter, true, null);
        }

        /// <summary>
        /// 切换到指定状态，并传入业务参数。
        /// </summary>
        public bool Change<TState>(object parameter) where TState : IState
        {
            return Change(typeof(TState), parameter);
        }

        /// <summary>
        /// 驱动当前激活路径里的所有状态，并在 Tick 后检查条件转换。
        /// </summary>
        /// <param name="deltaTime">由调用方传入的时间增量。</param>
        public void Tick(float deltaTime)
        {
            if (!Enabled)
            {
                return;
            }

            if (!IsRunning)
            {
                Start();
            }

            if (!IsRunning)
            {
                return;
            }

            isTicking = true;
            try
            {
                for (int i = 0; i < currentPath.Count; i++)
                {
                    StateNode node = currentPath[i];
                    node.Advance(deltaTime);
                    node.State.Tick(deltaTime);
                }
            }
            finally
            {
                isTicking = false;
            }

            if (DrainPendingChanges())
            {
                return;
            }

            StateNode transitionSource;
            StateTransition transition = FindTriggeredTransition(out transitionSource);
            if (transition == null)
            {
                return;
            }

            Owner.Parameters.ConsumeTriggers(transition.ConditionList);
            Change(transition.To, null, false, transition);
        }

        /// <summary>
        /// 退出当前状态路径、清空所有状态和转换，并重置 Layer 运行状态。
        /// </summary>
        public void Clear()
        {
            ExitCurrentPath(null, false);
            currentPath.Clear();
            pendingChanges.Clear();
            Root.Clear();
            allStates.Clear();
            isTransitioning = false;
            nextTransitionOrder = 0;
        }

        /// <summary>
        /// 生成下一条转换的创建顺序号。
        /// </summary>
        internal int NextTransitionOrder()
        {
            return nextTransitionOrder++;
        }

        /// <summary>
        /// 把节点注册到 Layer 全局索引，保证同一 Layer 内状态类型唯一。
        /// </summary>
        internal void RegisterNode(StateNode node)
        {
            StateNode existing;
            if (allStates.TryGetValue(node.StateType, out existing) && !ReferenceEquals(existing, node))
            {
                throw new InvalidOperationException("State type already exists in layer '" + Name + "': " + StateTypeUtility.GetDisplayName(node.StateType));
            }

            allStates[node.StateType] = node;
        }

        /// <summary>
        /// 从 Layer 全局索引中注销节点，并递归注销它的子状态机节点。
        /// </summary>
        internal void UnregisterNode(StateNode node)
        {
            if (node == null)
            {
                return;
            }

            if (node.ChildMachine != null)
            {
                foreach (StateNode child in node.ChildMachine.States)
                {
                    UnregisterNode(child);
                }
            }

            StateNode existing;
            if (allStates.TryGetValue(node.StateType, out existing) && ReferenceEquals(existing, node))
            {
                allStates.Remove(node.StateType);
            }

            StateBase stateBase = node.State as StateBase;
            if (stateBase != null && ReferenceEquals(stateBase.Machine, Owner))
            {
                stateBase.Machine = null;
            }
        }

        /// <summary>
        /// 根据根状态图 Entry 启动 Layer。
        /// </summary>
        private bool Start(object parameter, bool hasParameter)
        {
            StateNode entry;
            if (!Root.TryGetEntryNode(out entry))
            {
                return false;
            }

            return Change(entry.StateType, parameter, hasParameter, null);
        }

        /// <summary>
        /// 请求切换到指定状态。若当前正在 Tick 或切换，则延迟到安全时机执行。
        /// </summary>
        private bool Change(Type stateType, object parameter, bool hasParameter, StateTransition transition)
        {
            StateTypeUtility.EnsureStateType(stateType, "stateType");

            StateNode target;
            if (!allStates.TryGetValue(stateType, out target))
            {
                return false;
            }

            if (isTransitioning || isTicking)
            {
                pendingChanges.Enqueue(new PendingChange(stateType, parameter, hasParameter, transition));
                return true;
            }

            bool changed = ApplyChange(target, parameter, hasParameter, transition);
            DrainPendingChanges();
            return changed;
        }

        /// <summary>
        /// 立即执行状态切换：构建目标路径、退出旧路径差异部分、进入新路径差异部分。
        /// </summary>
        private bool ApplyChange(StateNode target, object parameter, bool hasParameter, StateTransition transition)
        {
            List<StateNode> targetPath = BuildTargetPath(target);
            if (targetPath.Count == 0)
            {
                return false;
            }

            StateNode currentLeaf = CurrentNode;
            StateNode targetLeaf = targetPath[targetPath.Count - 1];
            if (currentLeaf != null &&
                ReferenceEquals(currentLeaf, targetLeaf) &&
                (transition == null || !transition.CanTransitionToSelf))
            {
                return true;
            }

            Type fromType = CurrentStateType;
            bool hasFrom = IsRunning;
            Type finalTo = targetLeaf.StateType;
            int commonPrefix = CountCommonPrefix(currentPath, targetPath);
            if (currentLeaf != null &&
                ReferenceEquals(currentLeaf, targetLeaf) &&
                transition != null &&
                transition.CanTransitionToSelf &&
                commonPrefix > 0)
            {
                commonPrefix--;
            }

            isTransitioning = true;
            try
            {
                for (int i = currentPath.Count - 1; i >= commonPrefix; i--)
                {
                    StateNode node = currentPath[i];
                    node.State.Exit();
                    Owner.NotifyStateExited(this, node.StateType, finalTo, true);
                }

                if (currentPath.Count > commonPrefix)
                {
                    currentPath.RemoveRange(commonPrefix, currentPath.Count - commonPrefix);
                }

                for (int i = commonPrefix; i < targetPath.Count; i++)
                {
                    StateNode node = targetPath[i];
                    currentPath.Add(node);
                    node.ResetTime();

                    StateChangeContext enterContext = new StateChangeContext(
                        Owner,
                        Name,
                        fromType,
                        node.StateType,
                        parameter,
                        hasFrom,
                        hasParameter);
                    node.State.Enter(enterContext);
                    Owner.NotifyStateEntered(enterContext);
                }
            }
            finally
            {
                isTransitioning = false;
            }

            StateTransitionContext transitionContext = new StateTransitionContext(
                Owner,
                Name,
                fromType,
                finalTo,
                parameter,
                hasFrom,
                hasParameter,
                transition);
            Owner.NotifyTransitioned(transitionContext);
            return true;
        }

        /// <summary>
        /// 退出当前完整激活路径，顺序为叶子状态到父状态。
        /// </summary>
        private void ExitCurrentPath(Type to, bool hasTo)
        {
            if (currentPath.Count == 0)
            {
                return;
            }

            isTransitioning = true;
            try
            {
                for (int i = currentPath.Count - 1; i >= 0; i--)
                {
                    StateNode node = currentPath[i];
                    node.State.Exit();
                    Owner.NotifyStateExited(this, node.StateType, to, hasTo);
                }
            }
            finally
            {
                isTransitioning = false;
            }
        }

        /// <summary>
        /// 在当前激活路径上查找可触发的最佳转换。
        /// </summary>
        /// <remarks>
        /// 检查顺序从叶子状态向父状态回溯；普通转换和所属图的 Any State 转换都会参与竞争。
        /// 最终由 Priority 和创建顺序决定最佳转换。
        /// </remarks>
        private StateTransition FindTriggeredTransition(out StateNode source)
        {
            source = null;
            if (currentPath.Count == 0)
            {
                return null;
            }

            StateNode currentLeaf = CurrentNode;
            StateTransition best = null;
            StateNode bestSource = null;

            for (int i = currentPath.Count - 1; i >= 0; i--)
            {
                StateNode node = currentPath[i];
                EvaluateTransitions(node.Transitions, node, currentLeaf.StateType, ref best, ref bestSource);
                EvaluateTransitions(node.Owner.AnyStateTransitions, currentLeaf, currentLeaf.StateType, ref best, ref bestSource);
            }

            source = bestSource;
            return best;
        }

        /// <summary>
        /// 检查一组转换，把满足条件且优先级更高的转换记录为当前最佳。
        /// </summary>
        private void EvaluateTransitions(
            IList<StateTransition> transitions,
            StateNode source,
            Type currentLeafType,
            ref StateTransition best,
            ref StateNode bestSource)
        {
            for (int i = 0; i < transitions.Count; i++)
            {
                StateTransition transition = transitions[i];
                if (!allStates.ContainsKey(transition.To))
                {
                    continue;
                }

                if (!transition.IsMet(Owner.Parameters, source, currentLeafType))
                {
                    continue;
                }

                if (IsBetter(transition, best))
                {
                    best = transition;
                    bestSource = source;
                }
            }
        }

        /// <summary>
        /// 判断候选转换是否比当前转换优先级更高。
        /// </summary>
        private static bool IsBetter(StateTransition candidate, StateTransition current)
        {
            if (current == null)
            {
                return true;
            }

            if (candidate.Priority != current.Priority)
            {
                return candidate.Priority < current.Priority;
            }

            return candidate.Order < current.Order;
        }

        /// <summary>
        /// 构建进入目标节点需要激活的完整路径，并自动追加子状态机 Entry。
        /// </summary>
        private List<StateNode> BuildTargetPath(StateNode target)
        {
            List<StateNode> path = BuildPathToNode(target);
            AppendDefaultChildren(path);
            return path;
        }

        /// <summary>
        /// 从目标节点向上追溯父状态，得到根到目标节点的路径。
        /// </summary>
        private static List<StateNode> BuildPathToNode(StateNode target)
        {
            List<StateNode> reversed = new List<StateNode>();
            StateNode node = target;
            while (node != null)
            {
                reversed.Add(node);
                node = node.Owner.ParentState;
            }

            reversed.Reverse();
            return reversed;
        }

        /// <summary>
        /// 如果路径末尾节点有子状态机，则持续进入每一层子状态机的 Entry。
        /// </summary>
        private static void AppendDefaultChildren(List<StateNode> path)
        {
            if (path.Count == 0)
            {
                return;
            }

            StateNode node = path[path.Count - 1];
            while (node.ChildMachine != null)
            {
                StateNode childEntry;
                if (!node.ChildMachine.TryGetEntryNode(out childEntry))
                {
                    return;
                }

                path.Add(childEntry);
                node = childEntry;
            }
        }

        /// <summary>
        /// 计算两条路径从根开始有多少连续相同节点，用于最小化退出和进入范围。
        /// </summary>
        private static int CountCommonPrefix(List<StateNode> a, List<StateNode> b)
        {
            int count = 0;
            int max = Math.Min(a.Count, b.Count);
            while (count < max && ReferenceEquals(a[count], b[count]))
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// 依次执行延迟切换队列。
        /// </summary>
        /// <returns>至少执行了一次切换时返回 true。</returns>
        private bool DrainPendingChanges()
        {
            bool applied = false;
            while (pendingChanges.Count > 0 && !isTransitioning)
            {
                PendingChange pending = pendingChanges.Dequeue();
                StateNode target;
                if (allStates.TryGetValue(pending.StateType, out target))
                {
                    ApplyChange(target, pending.Parameter, pending.HasParameter, pending.Transition);
                    applied = true;
                }
            }

            return applied;
        }

        /// <summary>
        /// 延迟切换请求，保存调用 Change 时的目标状态、参数和触发转换。
        /// </summary>
        private struct PendingChange
        {
            /// <summary>
            /// 创建延迟切换请求。
            /// </summary>
            public PendingChange(Type stateType, object parameter, bool hasParameter, StateTransition transition)
            {
                StateType = stateType;
                Parameter = parameter;
                HasParameter = hasParameter;
                Transition = transition;
            }

            /// <summary>
            /// 要切换到的目标状态类型。
            /// </summary>
            public Type StateType;

            /// <summary>
            /// 切换参数。
            /// </summary>
            public object Parameter;

            /// <summary>
            /// 是否显式传入了切换参数。
            /// </summary>
            public bool HasParameter;

            /// <summary>
            /// 触发该切换的转换；手动 Change 时为空。
            /// </summary>
            public StateTransition Transition;
        }
    }
}

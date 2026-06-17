namespace Frame.StateMachine
{
    /// <summary>
    /// Optional convenience base class for states. Implements <see cref="IState{TStateId}"/>
    /// with empty virtual hooks so concrete states only override what they need, and gives
    /// the state a back-reference to its owning <see cref="StateMachine{TStateId}"/> (set by
    /// the machine when the state is added) so a state can request its own transitions.
    /// Using this base is entirely optional — any <see cref="IState{TStateId}"/> works.
    /// </summary>
    public abstract class StateBase<TStateId> : IState<TStateId>
    {
        public abstract TStateId Id { get; }

        /// <summary>The owning machine. Assigned by <c>StateMachine.Add</c>; null until then.</summary>
        public StateMachine<TStateId> Machine { get; internal set; }

        public virtual void Enter() { }

        public virtual void Tick(float deltaTime) { }

        public virtual void Exit() { }
    }
}

namespace Frame.StateMachine
{
    /// <summary>
    /// Optional capability: a state that wants to receive a payload object passed to
    /// <c>StateMachine.Change(id, payload)</c>. Invoked once, right after <c>Enter()</c>.
    /// States that do not need data simply do not implement this interface.
    /// </summary>
    public interface IPayloadState
    {
        void OnEnterWithPayload(object payload);
    }

    /// <summary>
    /// Optional capability: a state that wants a physics-step update. The owner must
    /// forward <c>StateMachine.FixedTick</c> from <c>MonoBehaviour.FixedUpdate</c>.
    /// </summary>
    public interface IFixedTickState
    {
        void FixedTick(float fixedDeltaTime);
    }

    /// <summary>
    /// Optional capability: a state that wants a late update, after all regular ticks.
    /// The owner must forward <c>StateMachine.LateTick</c> from <c>MonoBehaviour.LateUpdate</c>.
    /// </summary>
    public interface ILateTickState
    {
        void LateTick(float deltaTime);
    }
}

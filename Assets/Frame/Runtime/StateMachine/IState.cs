namespace Frame.StateMachine
{
    public interface IState<TStateId>
    {
        TStateId Id { get; }

        void Enter();

        void Tick(float deltaTime);

        void Exit();
    }
}

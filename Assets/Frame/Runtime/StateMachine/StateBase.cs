namespace Frame.StateMachine
{
    /// <summary>
    /// 状态基类，提供空的生命周期实现和状态机反向引用。
    /// </summary>
    /// <remarks>
    /// 业务状态可以直接实现 <see cref="IState"/>，也可以继承这个类，只重写自己需要的生命周期方法。
    /// 继承该类后，可以通过 <see cref="Machine"/> 在状态内部主动请求切换。
    /// </remarks>
    public abstract class StateBase : IState
    {
        /// <summary>
        /// 当前状态所属的状态机。状态注册到状态机后自动赋值，移除或清理后会恢复为 null。
        /// </summary>
        public StateMachine Machine { get; internal set; }

        /// <summary>
        /// 状态被进入时调用。默认什么都不做，子类按需重写。
        /// </summary>
        /// <param name="context">本次状态切换的上下文。</param>
        public virtual void Enter(StateChangeContext context) { }

        /// <summary>
        /// 状态机每帧或每次逻辑更新时调用。默认什么都不做，子类按需重写。
        /// </summary>
        /// <param name="deltaTime">由调用方传入的时间增量。</param>
        public virtual void Tick(float deltaTime) { }

        /// <summary>
        /// 状态被离开时调用。默认什么都不做，子类按需重写。
        /// </summary>
        public virtual void Exit() { }
    }
}

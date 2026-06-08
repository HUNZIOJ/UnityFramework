using System;
using System.Collections.Generic;

namespace Frame.StateMachine
{
    public sealed class StateMachine<TStateId>
    {
        private readonly Dictionary<TStateId, IState<TStateId>> states = new Dictionary<TStateId, IState<TStateId>>();

        public IState<TStateId> CurrentState
        {
            get;
            private set;
        }

        public TStateId CurrentId
        {
            get { return CurrentState == null ? default(TStateId) : CurrentState.Id; }
        }

        public void Add(IState<TStateId> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            states[state.Id] = state;
        }

        public bool Change(TStateId id)
        {
            IState<TStateId> next;
            if (!states.TryGetValue(id, out next))
            {
                return false;
            }

            if (ReferenceEquals(CurrentState, next))
            {
                return true;
            }

            if (CurrentState != null)
            {
                CurrentState.Exit();
            }

            CurrentState = next;
            CurrentState.Enter();
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (CurrentState != null)
            {
                CurrentState.Tick(deltaTime);
            }
        }

        public void Clear()
        {
            if (CurrentState != null)
            {
                CurrentState.Exit();
            }

            CurrentState = null;
            states.Clear();
        }
    }
}

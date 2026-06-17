using System;

namespace Frame.StateMachine
{
    /// <summary>
    /// Optional capability implemented by a state to veto transitions.
    /// <c>CanExit</c> is asked on the current state before leaving it;
    /// <c>CanEnter</c> is asked on the target state before entering it.
    /// Returning false aborts the transition (Change returns false).
    /// States that do not implement this are always allowed to enter/exit.
    /// </summary>
    public interface IStateGuard<TStateId>
    {
        bool CanExit(TStateId to);

        bool CanEnter(TStateId from);
    }

    /// <summary>
    /// A data-driven transition rule: when the machine is in <see cref="From"/> (or any
    /// state if <see cref="FromAny"/> is true) and <see cref="Condition"/> evaluates true,
    /// the machine moves to <see cref="To"/>. Evaluated in registration order on each Tick.
    /// </summary>
    public sealed class StateTransition<TStateId>
    {
        public StateTransition(TStateId from, TStateId to, Func<bool> condition, bool fromAny = false)
        {
            if (condition == null)
            {
                throw new ArgumentNullException("condition");
            }

            From = from;
            To = to;
            Condition = condition;
            FromAny = fromAny;
        }

        public TStateId From { get; private set; }

        public TStateId To { get; private set; }

        public Func<bool> Condition { get; private set; }

        public bool FromAny { get; private set; }
    }
}

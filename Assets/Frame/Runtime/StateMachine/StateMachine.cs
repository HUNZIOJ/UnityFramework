using System;
using System.Collections.Generic;

namespace Frame.StateMachine
{
    /// <summary>
    /// A generic finite state machine.
    ///
    /// Backward compatible with the original minimal API (<c>Add</c>, <c>Change</c>,
    /// <c>Tick</c>, <c>Clear</c>, <c>CurrentState</c>, <c>CurrentId</c>) — existing callers
    /// are unaffected. Production additions:
    ///   - payload passing on transitions (Change(id, payload) + IPayloadState)
    ///   - transition guards (IStateGuard: CanExit / CanEnter)
    ///   - data-driven transitions (AddTransition + automatic evaluation each Tick)
    ///   - "any state" transitions
    ///   - previous-state history and RevertToPrevious()
    ///   - re-entrant Change protection (transitions requested during Enter/Exit are queued)
    ///   - FixedTick / LateTick forwarding for states that opt in
    ///   - StateChanged event for observers (UI, audio, analytics)
    ///   - per-state exception isolation so one faulty Tick cannot kill the machine
    ///   - optional strict mode that throws instead of returning false on unknown ids
    /// </summary>
    public sealed class StateMachine<TStateId>
    {
        private readonly Dictionary<TStateId, IState<TStateId>> states;
        private readonly List<StateTransition<TStateId>> transitions = new List<StateTransition<TStateId>>();
        private readonly Queue<PendingChange> pendingChanges = new Queue<PendingChange>();

        private bool isTransitioning;
        private bool hasPrevious;
        private TStateId previousId;

        public StateMachine()
            : this(EqualityComparer<TStateId>.Default)
        {
        }

        /// <param name="comparer">
        /// Comparer for state ids. Pass a custom one (or none for the default) — supplying
        /// the default explicitly avoids per-lookup boxing/GC when TStateId is an enum.
        /// </param>
        /// <param name="strict">When true, transitions to unknown ids throw instead of returning false.</param>
        public StateMachine(IEqualityComparer<TStateId> comparer, bool strict = false)
        {
            states = new Dictionary<TStateId, IState<TStateId>>(comparer ?? EqualityComparer<TStateId>.Default);
            Strict = strict;
        }

        /// <summary>Raised after a state change completes, with (fromId, toId). Exceptions in handlers are isolated.</summary>
        public event Action<TStateId, TStateId> StateChanged;

        public IState<TStateId> CurrentState { get; private set; }

        public TStateId CurrentId
        {
            get { return CurrentState == null ? default(TStateId) : CurrentState.Id; }
        }

        public bool IsRunning
        {
            get { return CurrentState != null; }
        }

        /// <summary>True while a transition (Exit/Enter) is in progress. Change calls made here are queued.</summary>
        public bool IsTransitioning
        {
            get { return isTransitioning; }
        }

        public bool Strict { get; set; }

        public int StateCount
        {
            get { return states.Count; }
        }

        // ---- registration -------------------------------------------------

        public void Add(IState<TStateId> state)
        {
            if (state == null)
            {
                throw new ArgumentNullException("state");
            }

            states[state.Id] = state;

            StateBase<TStateId> withBackReference = state as StateBase<TStateId>;
            if (withBackReference != null)
            {
                withBackReference.Machine = this;
            }
        }

        public bool Has(TStateId id)
        {
            return states.ContainsKey(id);
        }

        public bool TryGetState(TStateId id, out IState<TStateId> state)
        {
            return states.TryGetValue(id, out state);
        }

        /// <summary>Register a conditional transition evaluated automatically on each Tick (in registration order).</summary>
        public void AddTransition(TStateId from, TStateId to, Func<bool> condition)
        {
            transitions.Add(new StateTransition<TStateId>(from, to, condition, false));
        }

        /// <summary>Register a transition that fires from ANY current state when the condition holds.</summary>
        public void AddAnyTransition(TStateId to, Func<bool> condition)
        {
            transitions.Add(new StateTransition<TStateId>(default(TStateId), to, condition, true));
        }

        // ---- transitions --------------------------------------------------

        public bool Change(TStateId id)
        {
            return Change(id, null);
        }

        /// <summary>
        /// Transition to <paramref name="id"/>, optionally handing <paramref name="payload"/> to the
        /// target state (if it implements <see cref="IPayloadState"/>). Returns false (or throws in
        /// strict mode) if the id is unknown or a guard vetoes the transition. If called while a
        /// transition is already running, the request is queued and applied after the current one settles.
        /// </summary>
        public bool Change(TStateId id, object payload)
        {
            IState<TStateId> next;
            if (!states.TryGetValue(id, out next))
            {
                if (Strict)
                {
                    throw new FrameStateMachineException("Unknown state id: " + id);
                }

                return false;
            }

            // Re-entrancy: a Change requested from inside Enter/Exit is deferred, not applied
            // mid-transition. This prevents recursive Exit/Enter stacks and surprising ordering.
            if (isTransitioning)
            {
                pendingChanges.Enqueue(new PendingChange(id, payload));
                return true;
            }

            return ApplyChange(next, payload);
        }

        private bool ApplyChange(IState<TStateId> next, object payload)
        {
            if (ReferenceEquals(CurrentState, next))
            {
                return true;
            }

            TStateId fromId = CurrentId;

            // Guards: current state may veto exit; target state may veto entry.
            IStateGuard<TStateId> exitGuard = CurrentState as IStateGuard<TStateId>;
            if (exitGuard != null && !exitGuard.CanExit(next.Id))
            {
                return false;
            }

            IStateGuard<TStateId> enterGuard = next as IStateGuard<TStateId>;
            if (enterGuard != null && !enterGuard.CanEnter(fromId))
            {
                return false;
            }

            isTransitioning = true;
            try
            {
                if (CurrentState != null)
                {
                    SafeExit(CurrentState);
                    hasPrevious = true;
                    previousId = fromId;
                }

                CurrentState = next;
                SafeEnter(CurrentState);

                IPayloadState payloadState = CurrentState as IPayloadState;
                if (payloadState != null)
                {
                    SafePayload(payloadState, payload);
                }
            }
            finally
            {
                isTransitioning = false;
            }

            RaiseStateChanged(fromId, next.Id);
            DrainPendingChanges();
            return true;
        }

        /// <summary>Return to the state active before the current one. False if there is no history.</summary>
        public bool RevertToPrevious()
        {
            if (!hasPrevious)
            {
                return false;
            }

            return Change(previousId);
        }

        public bool HasPrevious
        {
            get { return hasPrevious; }
        }

        // ---- updates ------------------------------------------------------

        public void Tick(float deltaTime)
        {
            // Evaluate data-driven transitions first; if one fires, the new state ticks this frame.
            EvaluateTransitions();

            if (CurrentState != null)
            {
                SafeTick(CurrentState, deltaTime);
            }
        }

        public void FixedTick(float fixedDeltaTime)
        {
            IFixedTickState fixedState = CurrentState as IFixedTickState;
            if (fixedState == null)
            {
                return;
            }

            try
            {
                fixedState.FixedTick(fixedDeltaTime);
            }
            catch (Exception exception)
            {
                throw Wrap(exception, "FixedTick");
            }
        }

        public void LateTick(float deltaTime)
        {
            ILateTickState lateState = CurrentState as ILateTickState;
            if (lateState == null)
            {
                return;
            }

            try
            {
                lateState.LateTick(deltaTime);
            }
            catch (Exception exception)
            {
                throw Wrap(exception, "LateTick");
            }
        }

        private void EvaluateTransitions()
        {
            if (transitions.Count == 0 || CurrentState == null || isTransitioning)
            {
                return;
            }

            for (int i = 0; i < transitions.Count; i++)
            {
                StateTransition<TStateId> transition = transitions[i];
                bool sourceMatches = transition.FromAny || states.Comparer.Equals(transition.From, CurrentId);
                if (!sourceMatches)
                {
                    continue;
                }

                bool conditionMet;
                try
                {
                    conditionMet = transition.Condition();
                }
                catch (Exception exception)
                {
                    throw Wrap(exception, "transition condition");
                }

                if (conditionMet && !states.Comparer.Equals(transition.To, CurrentId))
                {
                    Change(transition.To);
                    return; // re-evaluate next frame from the new state
                }
            }
        }

        // ---- teardown -----------------------------------------------------

        public void Clear()
        {
            if (CurrentState != null)
            {
                SafeExit(CurrentState);
            }

            CurrentState = null;
            hasPrevious = false;
            previousId = default(TStateId);
            pendingChanges.Clear();
            states.Clear();
            transitions.Clear();
            StateChanged = null;
        }

        // ---- internals ----------------------------------------------------

        private void DrainPendingChanges()
        {
            while (pendingChanges.Count > 0 && !isTransitioning)
            {
                PendingChange pending = pendingChanges.Dequeue();
                IState<TStateId> next;
                if (states.TryGetValue(pending.Id, out next))
                {
                    ApplyChange(next, pending.Payload);
                }
            }
        }

        private void RaiseStateChanged(TStateId fromId, TStateId toId)
        {
            Action<TStateId, TStateId> handler = StateChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(fromId, toId);
            }
            catch (Exception exception)
            {
                throw Wrap(exception, "StateChanged handler");
            }
        }

        private void SafeEnter(IState<TStateId> state)
        {
            try
            {
                state.Enter();
            }
            catch (Exception exception)
            {
                throw Wrap(exception, "Enter");
            }
        }

        private void SafeExit(IState<TStateId> state)
        {
            try
            {
                state.Exit();
            }
            catch (Exception exception)
            {
                throw Wrap(exception, "Exit");
            }
        }

        private void SafeTick(IState<TStateId> state, float deltaTime)
        {
            try
            {
                state.Tick(deltaTime);
            }
            catch (Exception exception)
            {
                throw Wrap(exception, "Tick");
            }
        }

        private static void SafePayload(IPayloadState state, object payload)
        {
            try
            {
                state.OnEnterWithPayload(payload);
            }
            catch (Exception exception)
            {
                throw Wrap(exception, "OnEnterWithPayload");
            }
        }

        private static FrameStateMachineException Wrap(Exception inner, string phase)
        {
            return new FrameStateMachineException("State machine failed during " + phase + ".", inner);
        }

        private struct PendingChange
        {
            public PendingChange(TStateId id, object payload)
            {
                Id = id;
                Payload = payload;
            }

            public TStateId Id { get; private set; }

            public object Payload { get; private set; }
        }
    }
}

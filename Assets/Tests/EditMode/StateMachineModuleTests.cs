using System;
using Frame.StateMachine;
using NUnit.Framework;

namespace Frame.Tests.EditMode
{
    public sealed class StateMachineModuleTests
    {
        [Test]
        public void StateMachine_AddChangeTickAndClearDriveStateLifecycle()
        {
            StateMachine<string> machine = new StateMachine<string>();
            TestState idle = new TestState("Idle");
            TestState battle = new TestState("Battle");

            machine.Add(idle);
            machine.Add(battle);

            Assert.IsTrue(machine.Change("Idle"));
            Assert.AreEqual("Idle", machine.CurrentId);
            Assert.AreEqual(1, idle.EnterCount);

            machine.Tick(0.5f);
            Assert.AreEqual(0.5f, idle.LastDelta);

            Assert.IsTrue(machine.Change("Battle"));
            Assert.AreEqual(1, idle.ExitCount);
            Assert.AreEqual(1, battle.EnterCount);

            Assert.IsTrue(machine.Change("Battle"));
            Assert.AreEqual(1, battle.EnterCount);

            Assert.IsFalse(machine.Change("Missing"));
            machine.Clear();

            Assert.IsNull(machine.CurrentState);
            Assert.AreEqual(1, battle.ExitCount);
        }

        [Test]
        public void StateMachine_StrictModeThrowsOnUnknownId()
        {
            StateMachine<string> machine = new StateMachine<string>(null, strict: true);
            Assert.Throws<FrameStateMachineException>(() => machine.Change("Nope"));
        }

        [Test]
        public void StateMachine_RaisesStateChangedWithFromAndTo()
        {
            StateMachine<string> machine = new StateMachine<string>();
            machine.Add(new TestState("A"));
            machine.Add(new TestState("B"));

            string observedFrom = null;
            string observedTo = null;
            machine.StateChanged += (from, to) => { observedFrom = from; observedTo = to; };

            machine.Change("A");
            Assert.AreEqual(null, observedFrom); // default(string) for the first entry
            Assert.AreEqual("A", observedTo);

            machine.Change("B");
            Assert.AreEqual("A", observedFrom);
            Assert.AreEqual("B", observedTo);
        }

        [Test]
        public void StateMachine_PayloadIsDeliveredToTargetState()
        {
            StateMachine<string> machine = new StateMachine<string>();
            PayloadState hurt = new PayloadState("Hurt");
            machine.Add(new TestState("Idle"));
            machine.Add(hurt);

            machine.Change("Idle");
            machine.Change("Hurt", 42);

            Assert.AreEqual(42, hurt.LastPayload);
        }

        [Test]
        public void StateMachine_GuardCanVetoExitAndEnter()
        {
            StateMachine<string> machine = new StateMachine<string>();
            GuardedState locked = new GuardedState("Locked") { AllowExit = false };
            GuardedState target = new GuardedState("Target") { AllowEnter = false };
            machine.Add(locked);
            machine.Add(target);
            machine.Add(new TestState("Free"));

            machine.Change("Locked");
            Assert.IsFalse(machine.Change("Free"), "exit guard should veto");
            Assert.AreEqual("Locked", machine.CurrentId);

            locked.AllowExit = true;
            Assert.IsFalse(machine.Change("Target"), "enter guard should veto");
            Assert.AreEqual("Locked", machine.CurrentId);

            Assert.IsTrue(machine.Change("Free"));
            Assert.AreEqual("Free", machine.CurrentId);
        }

        [Test]
        public void StateMachine_RevertToPreviousReturnsToPriorState()
        {
            StateMachine<string> machine = new StateMachine<string>();
            machine.Add(new TestState("Game"));
            machine.Add(new TestState("Pause"));

            Assert.IsFalse(machine.RevertToPrevious(), "no history yet");

            machine.Change("Game");
            machine.Change("Pause");
            Assert.IsTrue(machine.RevertToPrevious());
            Assert.AreEqual("Game", machine.CurrentId);
        }

        [Test]
        public void StateMachine_DataDrivenTransitionFiresOnTick()
        {
            StateMachine<string> machine = new StateMachine<string>();
            machine.Add(new TestState("Idle"));
            machine.Add(new TestState("Run"));

            bool shouldRun = false;
            machine.AddTransition("Idle", "Run", () => shouldRun);

            machine.Change("Idle");
            machine.Tick(0.1f);
            Assert.AreEqual("Idle", machine.CurrentId);

            shouldRun = true;
            machine.Tick(0.1f);
            Assert.AreEqual("Run", machine.CurrentId);
        }

        [Test]
        public void StateMachine_AnyTransitionFiresFromEveryState()
        {
            StateMachine<string> machine = new StateMachine<string>();
            machine.Add(new TestState("A"));
            machine.Add(new TestState("B"));
            machine.Add(new TestState("Death"));

            bool dead = false;
            machine.AddAnyTransition("Death", () => dead);

            machine.Change("B");
            dead = true;
            machine.Tick(0f);
            Assert.AreEqual("Death", machine.CurrentId);
        }

        [Test]
        public void StateMachine_ReentrantChangeIsQueuedNotRecursive()
        {
            StateMachine<string> machine = new StateMachine<string>();
            // Boot's Enter immediately requests a change to Main; it must be queued and applied
            // after Boot finishes entering, not recurse mid-transition.
            ReentrantState boot = new ReentrantState("Boot");
            boot.ChangeTo = "Main";
            machine.Add(boot); // Add wires up boot.Machine
            machine.Add(new TestState("Main"));

            machine.Change("Boot");

            Assert.AreEqual("Main", machine.CurrentId);
            Assert.AreEqual(1, boot.EnterCount);
            Assert.AreEqual(1, boot.ExitCount);
        }

        [Test]
        public void StateMachine_StateExceptionIsWrappedNotSwallowed()
        {
            StateMachine<string> machine = new StateMachine<string>();
            machine.Add(new ThrowingState("Bad"));

            FrameStateMachineException error = Assert.Throws<FrameStateMachineException>(() => machine.Change("Bad"));
            Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
        }

        // ---- test doubles -------------------------------------------------

        private sealed class TestState : IState<string>
        {
            public TestState(string id) { Id = id; }
            public string Id { get; private set; }
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }
            public float LastDelta { get; private set; }
            public void Enter() { EnterCount++; }
            public void Tick(float deltaTime) { LastDelta = deltaTime; }
            public void Exit() { ExitCount++; }
        }

        private sealed class PayloadState : IState<string>, IPayloadState
        {
            public PayloadState(string id) { Id = id; }
            public string Id { get; private set; }
            public object LastPayload { get; private set; }
            public void Enter() { }
            public void Tick(float deltaTime) { }
            public void Exit() { }
            public void OnEnterWithPayload(object payload) { LastPayload = payload; }
        }

        private sealed class GuardedState : IState<string>, IStateGuard<string>
        {
            public GuardedState(string id) { Id = id; }
            public string Id { get; private set; }
            public bool AllowExit = true;
            public bool AllowEnter = true;
            public void Enter() { }
            public void Tick(float deltaTime) { }
            public void Exit() { }
            public bool CanExit(string to) { return AllowExit; }
            public bool CanEnter(string from) { return AllowEnter; }
        }

        private sealed class ReentrantState : StateBase<string>
        {
            private readonly string id;
            public ReentrantState(string id) { this.id = id; }
            public override string Id { get { return id; } }
            public string ChangeTo;
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }
            public override void Enter()
            {
                EnterCount++;
                if (ChangeTo != null)
                {
                    Machine.Change(ChangeTo);
                }
            }
            public override void Exit() { ExitCount++; }
        }

        private sealed class ThrowingState : IState<string>
        {
            public ThrowingState(string id) { Id = id; }
            public string Id { get; private set; }
            public void Enter() { throw new InvalidOperationException("boom"); }
            public void Tick(float deltaTime) { }
            public void Exit() { }
        }
    }
}

using System;
using System.Collections.Generic;
using Frame.Events;
using Frame.StateMachine;
using NUnit.Framework;
using RuntimeStateMachine = Frame.StateMachine.StateMachine;

namespace Frame.Tests.EditMode
{
    public sealed class StateMachineModuleTests
    {
        [Test]
        public void StateMachine_AddChangeTickAndClearDriveStateLifecycle()
        {
            RuntimeStateMachine machine = new RuntimeStateMachine();
            IdleState idle = new IdleState();
            BattleState battle = new BattleState();

            machine.Add(idle);
            machine.Add(battle);

            Assert.IsTrue(machine.Change<IdleState>());
            Assert.AreEqual(typeof(IdleState), machine.CurrentStateType);
            Assert.AreEqual(1, idle.EnterCount);
            Assert.IsFalse(idle.LastContext.HasFrom);
            Assert.AreEqual("Base Layer", idle.LastContext.LayerName);

            machine.Tick(0.5f);
            Assert.AreEqual(0.5f, idle.LastDelta);

            Assert.IsTrue(machine.Change<BattleState>(7));
            Assert.AreEqual(1, idle.ExitCount);
            Assert.AreEqual(1, battle.EnterCount);
            Assert.IsTrue(battle.LastContext.HasFrom);
            Assert.AreEqual(typeof(IdleState), battle.LastContext.From);
            Assert.AreEqual(typeof(BattleState), battle.LastContext.To);
            Assert.AreEqual(7, battle.LastContext.Parameter);

            Assert.IsTrue(machine.Change<BattleState>());
            Assert.AreEqual(1, battle.EnterCount);

            Assert.IsFalse(machine.Change<MissingState>());

            machine.Clear();

            Assert.IsNull(machine.CurrentState);
            Assert.AreEqual(1, battle.ExitCount);
            Assert.AreEqual(0, machine.StateCount);
        }

        [Test]
        public void StateMachine_ParameterConditionsAndTriggersDriveTransitions()
        {
            RuntimeStateMachine machine = new RuntimeStateMachine();
            machine.Add(new IdleState());
            machine.Add(new RunState());
            machine.Add(new AttackState());

            machine.AddTransition<IdleState, RunState>().When(StateCondition.Greater("Speed", 0.1f));
            machine.AddTransition<RunState, AttackState>().When(StateCondition.Trigger("Attack"));

            machine.Change<IdleState>();
            machine.SetFloat("Speed", 0f);
            machine.Tick(0.1f);
            Assert.AreEqual(typeof(IdleState), machine.CurrentStateType);

            machine.SetFloat("Speed", 1f);
            machine.Tick(0.1f);
            Assert.AreEqual(typeof(RunState), machine.CurrentStateType);

            machine.SetTrigger("Attack");
            machine.Tick(0.1f);
            Assert.AreEqual(typeof(AttackState), machine.CurrentStateType);
            Assert.IsFalse(machine.Parameters.IsTriggerSet("Attack"));
        }

        [Test]
        public void StateMachine_AnyStateTransitionHonorsExitTime()
        {
            RuntimeStateMachine machine = new RuntimeStateMachine();
            machine.Add(new IdleState()).WithLength(1f);
            machine.Add(new HitState());
            machine.AddAnyTransition<HitState>()
                .WithExitTime(0.5f)
                .When(StateCondition.If("Damaged"));

            machine.Change<IdleState>();
            machine.SetBool("Damaged", true);

            machine.Tick(0.25f);
            Assert.AreEqual(typeof(IdleState), machine.CurrentStateType);

            machine.Tick(0.25f);
            Assert.AreEqual(typeof(HitState), machine.CurrentStateType);
        }

        [Test]
        public void StateMachine_ChildStateMachineEntersDefaultChildAndExitsHierarchy()
        {
            List<string> log = new List<string>();
            RuntimeStateMachine machine = new RuntimeStateMachine();
            StateNode grounded = machine.Add(new GroundedState(log));
            StateGraph locomotion = grounded.CreateChildMachine();
            locomotion.AddState(new IdleState(log));
            locomotion.AddState(new RunState(log));
            locomotion.AddTransition<IdleState, RunState>().When(StateCondition.Greater("Speed", 0.1f));
            machine.Add(new AirState(log));

            machine.Change<GroundedState>();

            Assert.AreEqual(typeof(IdleState), machine.CurrentStateType);
            Assert.AreEqual(2, machine.BaseLayer.ActivePath.Count);
            Assert.AreEqual(typeof(GroundedState), machine.BaseLayer.ActivePath[0].StateType);
            Assert.AreEqual(typeof(IdleState), machine.BaseLayer.ActivePath[1].StateType);

            machine.SetFloat("Speed", 1f);
            machine.Tick(0.1f);
            Assert.AreEqual(typeof(RunState), machine.CurrentStateType);
            Assert.AreEqual(typeof(GroundedState), machine.BaseLayer.ActivePath[0].StateType);
            Assert.AreEqual(typeof(RunState), machine.BaseLayer.ActivePath[1].StateType);

            machine.Change<AirState>();

            Assert.AreEqual(typeof(AirState), machine.CurrentStateType);
            CollectionAssert.AreEqual(
                new[]
                {
                    "Enter:Grounded",
                    "Enter:Idle",
                    "Exit:Idle",
                    "Enter:Run",
                    "Exit:Run",
                    "Exit:Grounded",
                    "Enter:Air"
                },
                log);
        }

        [Test]
        public void StateMachine_LayersRunIndependently()
        {
            RuntimeStateMachine machine = new RuntimeStateMachine();
            machine.Add(new BaseIdleState());

            StateMachineLayer upper = machine.AddLayer("UpperBody");
            upper.AddState(new AimState());
            upper.AddState(new ReloadState());
            upper.AddTransition<AimState, ReloadState>().When(StateCondition.Trigger("Reload"));

            machine.Start();
            Assert.AreEqual(typeof(BaseIdleState), machine.CurrentStateType);
            Assert.AreEqual(typeof(AimState), upper.CurrentStateType);

            machine.SetTrigger("Reload");
            machine.Tick(0.1f);

            Assert.AreEqual(typeof(BaseIdleState), machine.CurrentStateType);
            Assert.AreEqual(typeof(ReloadState), upper.CurrentStateType);
        }

        [Test]
        public void StateMachine_EventBusPublishesTransitionsAndCanDriveTriggers()
        {
            EventBus bus = new EventBus();
            RuntimeStateMachine machine = new RuntimeStateMachine(bus);
            machine.Add(new IdleState());
            machine.Add(new HitState());
            machine.AddTransition<IdleState, HitState>().When(StateCondition.Trigger("Damaged"));
            machine.BindTrigger<DamageEvent>("Damaged");

            int hitTransitions = 0;
            bus.Subscribe<StateMachineTransitioned>(evt =>
            {
                if (evt.To == typeof(HitState))
                {
                    hitTransitions++;
                }
            });

            machine.Change<IdleState>();
            bus.Publish(new DamageEvent());
            machine.Tick(0.1f);

            Assert.AreEqual(typeof(HitState), machine.CurrentStateType);
            Assert.AreEqual(1, hitTransitions);
        }

        [Test]
        public void StateMachine_ReentrantChangeIsQueuedNotRecursive()
        {
            RuntimeStateMachine machine = new RuntimeStateMachine();
            BootState boot = new BootState();
            boot.ChangeTo = typeof(MainState);
            machine.Add(boot);
            machine.Add(new MainState());

            machine.Change<BootState>();

            Assert.AreEqual(typeof(MainState), machine.CurrentStateType);
            Assert.AreEqual(1, boot.EnterCount);
            Assert.AreEqual(1, boot.ExitCount);
        }

        [Test]
        public void StateMachine_ChangeRequestedDuringTickIsAppliedAfterTick()
        {
            RuntimeStateMachine machine = new RuntimeStateMachine();
            ActiveState active = new ActiveState();
            active.ChangeTo = typeof(NextState);
            NextState next = new NextState();
            machine.Add(active);
            machine.Add(next);

            machine.Change<ActiveState>();
            machine.Tick(0.1f);

            Assert.AreEqual(typeof(NextState), machine.CurrentStateType);
            Assert.AreEqual(1, active.TickCount);
            Assert.AreEqual(1, active.ExitCount);
            Assert.AreEqual(1, next.EnterCount);
        }

        [Test]
        public void StateMachine_StateExceptionIsNotSwallowed()
        {
            RuntimeStateMachine machine = new RuntimeStateMachine();
            machine.Add(new ThrowingState());

            Assert.Throws<InvalidOperationException>(() => machine.Change<ThrowingState>());
        }

        private struct DamageEvent
        {
        }

        private abstract class TestState : StateBase
        {
            private readonly List<string> log;

            protected TestState(List<string> log = null)
            {
                this.log = log;
            }

            public int EnterCount { get; private set; }

            public int ExitCount { get; private set; }

            public float LastDelta { get; private set; }

            public StateChangeContext LastContext { get; private set; }

            public override void Enter(StateChangeContext context)
            {
                LastContext = context;
                EnterCount++;
                if (log != null)
                {
                    log.Add("Enter:" + Label);
                }
            }

            public override void Tick(float deltaTime)
            {
                LastDelta = deltaTime;
            }

            public override void Exit()
            {
                ExitCount++;
                if (log != null)
                {
                    log.Add("Exit:" + Label);
                }
            }

            private string Label
            {
                get
                {
                    string name = GetType().Name;
                    const string suffix = "State";
                    return name.EndsWith(suffix, StringComparison.Ordinal)
                        ? name.Substring(0, name.Length - suffix.Length)
                        : name;
                }
            }
        }

        private sealed class IdleState : TestState
        {
            public IdleState(List<string> log = null) : base(log) { }
        }

        private sealed class BattleState : TestState
        {
            public BattleState(List<string> log = null) : base(log) { }
        }

        private sealed class RunState : TestState
        {
            public RunState(List<string> log = null) : base(log) { }
        }

        private sealed class AttackState : TestState
        {
            public AttackState(List<string> log = null) : base(log) { }
        }

        private sealed class HitState : TestState
        {
            public HitState(List<string> log = null) : base(log) { }
        }

        private sealed class GroundedState : TestState
        {
            public GroundedState(List<string> log = null) : base(log) { }
        }

        private sealed class AirState : TestState
        {
            public AirState(List<string> log = null) : base(log) { }
        }

        private sealed class BaseIdleState : TestState
        {
        }

        private sealed class AimState : TestState
        {
        }

        private sealed class ReloadState : TestState
        {
        }

        private sealed class MainState : TestState
        {
        }

        private sealed class NextState : TestState
        {
        }

        private sealed class MissingState : TestState
        {
        }

        private sealed class BootState : StateBase
        {
            public Type ChangeTo;

            public int EnterCount { get; private set; }

            public int ExitCount { get; private set; }

            public override void Enter(StateChangeContext context)
            {
                EnterCount++;
                if (ChangeTo != null)
                {
                    Machine.Change(ChangeTo);
                }
            }

            public override void Exit()
            {
                ExitCount++;
            }
        }

        private sealed class ActiveState : StateBase
        {
            public Type ChangeTo;

            public int TickCount { get; private set; }

            public int ExitCount { get; private set; }

            public override void Tick(float deltaTime)
            {
                TickCount++;
                if (ChangeTo != null)
                {
                    Machine.Change(ChangeTo);
                }
            }

            public override void Exit()
            {
                ExitCount++;
            }
        }

        private sealed class ThrowingState : StateBase
        {
            public override void Enter(StateChangeContext context)
            {
                throw new InvalidOperationException("boom");
            }
        }
    }
}

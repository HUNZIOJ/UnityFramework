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

        private sealed class TestState : IState<string>
        {
            public TestState(string id)
            {
                Id = id;
            }

            public string Id { get; private set; }

            public int EnterCount { get; private set; }

            public int ExitCount { get; private set; }

            public float LastDelta { get; private set; }

            public void Enter() { EnterCount++; }

            public void Tick(float deltaTime) { LastDelta = deltaTime; }

            public void Exit() { ExitCount++; }
        }
    }
}

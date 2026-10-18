using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Game.Tests
{
    /// <summary>通过正式单人冲刺、移动和轮前恢复入口验证方向选择与持续位移。</summary>
    public sealed class ArcadeDashDirectionTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private SlayerMartAbilityFlowTests harness;
        private ArcadeCombatRuntime Runtime => harness.runtime;

        [SetUp]
        public void Setup()
        {
            harness = new SlayerMartAbilityFlowTests();
            harness.Setup();
            Runtime.Dashes = 2;
            Runtime.PlayerPosition = Vector2.zero;
        }

        private object Call(string name, params object[] args) =>
            typeof(ArcadeCombatSystem).GetMethod(name, Private).Invoke(harness.system, args);

        private Vector2 Step()
        {
            var before = Runtime.PlayerPosition;
            Call("TickPlayer", Runtime, .02f);
            return Runtime.PlayerPosition - before;
        }

        [TestCase(1f, 0f)]
        [TestCase(-1f, 0f)]
        [TestCase(0f, 1f)]
        [TestCase(0f, -1f)]
        public void StationaryDashMovesAlongCurrentFacing(float x, float y)
        {
            Runtime.AimDirection = new Vector2(x, y);
            Call("UseDash", Runtime);
            var delta = Step();
            Assert.That(Vector2.Dot(delta, Runtime.AimDirection), Is.GreaterThan(.01f));
            Assert.That(Runtime.Dashes, Is.EqualTo(1));
            Assert.That(Runtime.DashRemaining, Is.EqualTo(.28f).Within(.0001f));
            Assert.That(Runtime.PlayerInvulnerability, Is.EqualTo(.28f).Within(.0001f));
        }

        [Test]
        public void MoveInputWinsOverFacingAndUsesFullDashSpeed()
        {
            Runtime.MoveDirection = new Vector2(.3f, .4f);
            Runtime.AimDirection = Vector2.left;
            Call("UseDash", Runtime);
            var delta = Step();
            Assert.That(Vector2.Distance(delta.normalized, new Vector2(.6f, .8f)), Is.LessThan(.001f));
            var distance = delta.magnitude;
            Runtime.PlayerPosition = Vector2.zero;
            Runtime.DashRemaining = 0;
            Runtime.MoveDirection = new Vector2(.6f, .8f);
            Assert.That(distance / Step().magnitude, Is.EqualTo(2.4f).Within(.001f));
        }

        [Test]
        public void ReleasingOrChangingInputDoesNotRedirectActiveDash()
        {
            Runtime.MoveDirection = Vector2.right;
            Call("UseDash", Runtime);
            Runtime.MoveDirection = Vector2.zero;
            Runtime.AimDirection = Vector2.down;
            Assert.That(Step().x, Is.GreaterThan(.01f));
            Runtime.MoveDirection = Vector2.left;
            var delta = Step();
            Assert.That(delta.x, Is.GreaterThan(.01f));
            Assert.That(delta.y, Is.EqualTo(0).Within(.001f));
        }

        [Test]
        public void ExpiredDashReturnsToOrdinaryMovementInput()
        {
            Runtime.MoveDirection = Vector2.right;
            Call("UseDash", Runtime);
            Runtime.DashRemaining = 0;
            Runtime.MoveDirection = Vector2.zero;
            Assert.That(Step(), Is.EqualTo(Vector2.zero));
            Runtime.MoveDirection = Vector2.left;
            Assert.That(Step().x, Is.LessThan(0));
        }

        [Test]
        public void RepeatedDashDoesNotConsumeOrRedirectCurrentDash()
        {
            Runtime.MoveDirection = Vector2.right;
            Call("UseDash", Runtime);
            Runtime.MoveDirection = Vector2.left;
            Call("UseDash", Runtime);
            Assert.That(Runtime.Dashes, Is.EqualTo(1));
            Assert.That(Step().x, Is.GreaterThan(0));
        }

        [Test]
        public void RoundCheckpointRetainsDashDirectionIndependentOfFacing()
        {
            Runtime.MoveDirection = Vector2.right;
            Runtime.AimDirection = Vector2.up;
            Call("UseDash", Runtime);
            var checkpoint = Call("CaptureIslandCheckpoint", Runtime);
            Runtime.MoveDirection = Vector2.left;
            Runtime.DashRemaining = 0;
            Call("UseDash", Runtime);
            Call("RestoreIslandCheckpoint", Runtime, checkpoint);
            Runtime.MoveDirection = Vector2.zero;
            Assert.That(Step().x, Is.GreaterThan(.01f));
        }

        [Test]
        public void DashActionSamplesNewInputBeforeNormalFrameRead()
        {
            const string flag = "RUN_PLAYER_UPDATES_IN_EDIT_MODE";
            bool previous = (bool)typeof(InputSettings).GetMethod("IsFeatureEnabled", Private)
                .Invoke(InputSystem.settings, new object[] { flag });
            InputSystem.settings.SetInternalFeatureFlag(flag, true);
            var pad = InputSystem.AddDevice<Gamepad>();
            using var playerInput = new PlayerInputCtrl();
            try
            {
                playerInput.Enable();
                typeof(ArcadeCombatSystem).GetField("input", Private).SetValue(harness.system, playerInput);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = Vector2.right, rightStick = Vector2.up });
                InputSystem.Update();
                Assert.That(playerInput.MoveInput.x, Is.GreaterThan(.9f));
                // 不执行普通帧采样，保留上一帧相反方向，模拟本帧方向键与冲刺同时触发。
                Runtime.MoveDirection = Vector2.left;
                Call("OnHudAction", new ArcadeHudActionRequestedEvent(ArcadeHudActionKind.Dash));
                Assert.That(Runtime.DashDirection, Is.EqualTo(Vector2.right));
                Assert.That(Runtime.AimDirection, Is.EqualTo(Vector2.up));
            }
            finally
            {
                playerInput.Disable();
                InputSystem.RemoveDevice(pad);
                InputSystem.settings.SetInternalFeatureFlag(flag, previous);
            }
        }
    }
}

using System.Reflection;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using QFramework;
namespace Game.Tests
{
    /// <summary>单人原始手柄输入通过正式能力入口，阻断期间不缓冲技能。</summary>
    public sealed class SlayerMartSingleInputTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private SlayerMartAbilityFlowTests h;private PlayerInputCtrl input;private Gamepad pad;
        [SetUp]public void Setup(){h=new SlayerMartAbilityFlowTests();h.Setup();input=new PlayerInputCtrl();input.Enable();pad=InputSystem.AddDevice<Gamepad>();typeof(ArcadeCombatSystem).GetField("input",Private).SetValue(h.system,input);h.player.Owned.Add("slayermart.item.16");h.player.StormCharge=100;}
        [TearDown]public void Cleanup(){input.Dispose();InputSystem.RemoveDevice(pad);}
        private void Press(){InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.LeftShoulder).WithButton(GamepadButton.RightShoulder));InputSystem.Update();}
        private void Read(){var method=typeof(ArcadeCombatSystem).GetMethod("ReadFieldUpgradeInput",Private);Assert.That(method,Is.Not.Null);method.Invoke(h.system,new object[]{h.runtime});}
        [Test]public void SingleChordActivatesExactlyOnce(){Press();Read();Assert.That(h.player.StormRemaining,Is.EqualTo(10));Assert.That(h.player.StormCharge,Is.Zero);Read();Assert.That(h.player.StormRemaining,Is.EqualTo(10));}
        [Test]public void ClearedMenuInputCannotFireOnReturn(){Press();typeof(ArcadeCombatSystem).GetMethod("ClearArcadeInput",Private).Invoke(h.system,new object[]{h.runtime});Read();Assert.That(h.player.StormCharge,Is.EqualTo(100));Assert.That(h.player.StormRemaining,Is.Zero);}
        [Test]public void RespawnConsumesNoChargeAndQueuesNothing(){h.runtime.RespawnRemaining=1;Press();Read();h.runtime.RespawnRemaining=0;Read();Assert.That(h.player.StormCharge,Is.EqualTo(100));}
        [Test]public void SuccessfulActivationPublishesArmorAndCharge(){ArcadeCombatHudSnapshot snapshot=null;h.architecture.RegisterEvent<ArcadeCombatHudChangedEvent>(e=>snapshot=e.Snapshot);Press();Read();Assert.That(snapshot,Is.Not.Null);Assert.That(snapshot.HasStorm,Is.True);Assert.That(snapshot.StormCharge,Is.Zero);Assert.That(snapshot.StormSeconds,Is.EqualTo(10));Assert.That(snapshot.Plates,Is.EqualTo(2));Assert.That(snapshot.VestSlots,Is.EqualTo(2));}
    }
}

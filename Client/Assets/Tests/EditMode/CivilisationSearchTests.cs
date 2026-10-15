using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class CivilisationSearchTests
    {
        private CivilisationProductionTests support;
        private object search;
        [SetUp] public void SetUp()
        {
            support = new CivilisationProductionTests(); support.SetUp();
            var architecture = (Game)Game.Interface;
            architecture.RegisterModel(new UnitModel());
            architecture.RegisterSystem(new UnitSystem());
            architecture.RegisterSystem(new PlayerActionSystem());
            architecture.RegisterModel(new ExpeditionTaskModel());
            architecture.RegisterSystem(new ExpeditionTaskSystem());
            var type = typeof(Game).Assembly.GetType("Game.CivilisationSearchSystem");
            Assert.That(type, Is.Not.Null, "文明搜索规则尚未实现");
            search = Activator.CreateInstance(type);
            typeof(Architecture<Game>).GetMethod("RegisterSystem").MakeGenericMethod(type).Invoke(architecture, new[] { search });
            architecture.GetModel<MapSceneModel>().SetLoaded("map.another-day.civilisation-valley", null);
            var expeditionId = Guid.NewGuid().ToString("N");
            architecture.GetModel<ExpeditionModel>().BeginConfiguredExpedition(MapIds.CivilisationValley, expeditionId, false, 0, 0);
            Assert.That(architecture.GetSystem<ExpeditionTaskSystem>().TryBegin(MapIds.CivilisationValley, expeditionId,
                Array.Empty<ExpeditionTaskDefinition>(), out var bindingError), Is.True, bindingError);
            architecture.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData { ExpeditionId = expeditionId, MapId = MapIds.CivilisationValley });
            architecture.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.InExpedition);
            // 新格式要求显式建立初始快照；普通采集 Flush 不能代替新远征的创建事务。
            var save = architecture.GetSystem<SaveSystem>();
            var initial = architecture.GetModel<ExpeditionModel>().CaptureSave(expeditionId, DateTime.UtcNow);
            Assert.That(save.CaptureExpeditionSections(initial, out var initialError), Is.True, initialError);
            Assert.That(save.SaveExpedition(initial, out initialError), Is.True, initialError);
            architecture.GetModel<SaveModel>().SetExpedition(initial);
        }
        [TearDown] public void TearDown() => support?.TearDown();
        private void PlayerAt(Vector3 position, long instance = 1)
        {
            var units = Game.Interface.GetSystem<UnitSystem>();
            if (units.TryGet(1, out _)) units.TryUpdateTransform(1, position, Quaternion.identity);
            else Assert.That(units.TryRegister(new UnitSnapshot(1, instance, UnitType.Player, UnitSource.MapPlaced, "player.graybox.default", position, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
        }
        private bool Begin(long map, string point)
        {
            var args = new object[] { map, point, null };
            return (bool)search.GetType().GetMethod("TryBeginSearch").Invoke(search, args);
        }
        [Test] public void OldInstanceAndOutOfRangeCannotStartSearch()
        { PlayerAt(Vector3.zero); Assert.That(Begin(2, "camp.fire"), Is.False); Assert.That(Begin(1, "camp.fire"), Is.False); }
        [Test] public void MobileInteractionSnapshotSupportsEnvironmentPointsAndRejectsDisabledTargets()
        {
            var architecture = (Game)Game.Interface;
            architecture.RegisterSystem(new PlayerInteractionSystem());
            var system = architecture.GetSystem<PlayerInteractionSystem>();
            var target = new EnvironmentPoint(); system.Focus(target);
            var property = typeof(PlayerInteractionSystem).GetProperty("CanInteractWithFocusedTarget");
            Assert.That(property, Is.Not.Null, "手机交互仍仅支持 NPC");
            Assert.That((bool)property.GetValue(system), Is.True);
            Assert.That(system.CanInteractWithFocusedNpc, Is.False);
            target.Enabled = false;
            Assert.That((bool)property.GetValue(system), Is.False);
            system.Blur(target);
            Assert.That((bool)property.GetValue(system), Is.False);
        }
        private sealed class EnvironmentPoint : IPlayerInteractable
        {
            public bool Enabled = true;
            public string DisplayName => "point";
            public bool CanInteract => Enabled;
            public void SetFocused(bool focused) { }
            public void Interact() { }
        }
        [Test] public void CompletedSearchStoresSampleOnlyOnce()
        {
            PlayerAt(new Vector3(0, 0, 10)); Assert.That(Begin(1, "camp.fire"), Is.True);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3);
            var saved = Game.Interface.GetSystem<SaveSystem>().InspectExpedition();
            Assert.That(saved.Status, Is.EqualTo(SaveFileStatus.Valid), saved.Error);
            Assert.That(saved.Data.Sections["another-day.civilisation-expedition"]["technologies"].Values<string>(), Does.Contain("technology.dry-ration"));
            Assert.That(Begin(1, "camp.fire"), Is.False);
        }
        [Test] public void SearchReturnConversionAndProductionPersistAsOneJourney()
        {
            var architecture = Game.Interface;
            var save = architecture.GetSystem<SaveSystem>();
            var basis = new BaseSaveData();
            Assert.That(save.CaptureBaseSections(basis, out var error), Is.True, error);
            foreach (var point in new[] { ("camp.fire", new Vector3(0,0,10)), ("camp.food", new Vector3(-3,0,9)), ("camp.wood", new Vector3(4,0,11)) })
            {
                PlayerAt(point.Item2); Assert.That(Begin(1, point.Item1), Is.True);
                architecture.GetSystem<PlayerActionSystem>().TickForTests(3);
            }
            var journey = save.InspectExpedition();
            Assert.That(journey.Status, Is.EqualTo(SaveFileStatus.Valid), journey.Error);
            Assert.That(save.ApplyExpeditionSettlement(basis, journey.Data, false, out var repeated, out error), Is.True, error);
            Assert.That(repeated, Is.False);
            Assert.That(save.ApplyExpeditionSettlement(basis, journey.Data, false, out repeated, out error), Is.True, error);
            Assert.That(repeated, Is.True, "同一趟返程不能再次付款");
            Assert.That(save.SaveBase(basis, out error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(basis, out error), Is.True, error);
            architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Assert.That(architecture.GetModel<CivilisationTechnologyModel>().IsUnlocked("technology.dry-ration"), Is.True);
            Assert.That(architecture.GetModel<CurrencyModel>().Gold, Is.EqualTo(16));
            Assert.That(architecture.GetModel<PlayerBagModel>().Container.GetTotalAmount(29), Is.Zero);
            Assert.That(architecture.GetModel<PlayerBagModel>().Container.GetTotalAmount(9), Is.Zero);
            Assert.That(architecture.GetSystem<CivilisationProductionSystem>().TryProduce("production.dry-ration", 1, out error), Is.True, error);
            var persisted = save.InspectBase();
            Assert.That(persisted.Status, Is.EqualTo(SaveFileStatus.Valid), persisted.Error);
            Assert.That(persisted.Data.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.EqualTo(8));
            Assert.That(architecture.GetModel<PlayerBagModel>().Container.GetTotalAmount(30), Is.EqualTo(1));
        }
        [Test] public void InterruptedSearchDoesNotGrantSample()
        {
            PlayerAt(new Vector3(0, 0, 10)); Assert.That(Begin(1, "camp.fire"), Is.True);
            Game.Interface.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Damaged);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
        }
        [Test] public void MovingOutOfRangeBeforeCompletionDoesNotGrantSample()
        {
            PlayerAt(new Vector3(0, 0, 10)); Assert.That(Begin(1, "camp.fire"), Is.True);
            PlayerAt(Vector3.zero); Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3);
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Status, Is.EqualTo(SaveFileStatus.Missing));
        }
    }
}

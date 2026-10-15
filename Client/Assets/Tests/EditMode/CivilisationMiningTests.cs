using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过真实采集、装备、存档与结算验证采石；不修改正式场景或进入运行模式。</summary>
    public sealed class CivilisationMiningTests
    {
        private CivilisationHarvestTests support;
        private const string Rock = "rock.valley.1";
        private const int Stone = 31;
        private static readonly Vector3 RockPosition = new Vector3(8, 0, -9.5f);
        [SetUp] public void SetUp()
        {
            support = new CivilisationHarvestTests(); support.SetUp();
            Move(RockPosition);
        }
        [TearDown] public void TearDown() => support?.TearDown();
        private static void Move(Vector3 position) => Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, position, Quaternion.identity);
        private static void Equip(int item = 18, string slot = "Melee") => CivilisationHarvestTests.Equip(item, slot);
        private static bool Begin(out string error) => Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, Rock, out error);
        private static void Complete() => Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3.1f);
        private static int Amount => Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(Stone);

        [Test] public void CampPickaxeCanEquipAndMineStoneOnceWithSavedReceipt()
        {
            var architecture = (Game)Game.Interface;
            Move(new Vector3(-4, 0, 6));
            Assert.That(architecture.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, "camp.pickaxe", out var error), Is.True, error);
            Complete();
            var bag = architecture.GetModel<PlayerBagModel>().Container;
            var slot = bag.Slots.Single(p => p.Value.ItemId == 18).Key;
            Assert.That(architecture.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(slot, out error), Is.True, error);
            Move(RockPosition); Assert.That(Begin(out error), Is.True, error);
            Assert.That(architecture.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action), Is.True);
            Assert.That(action.Kind.ToString(), Is.EqualTo("MineStone"));
            Complete(); Assert.That(Amount, Is.Zero); Assert.That(support.Drops.Amount(Stone), Is.EqualTo(4));
            Assert.That(architecture.GetSystem<SaveSystem>().InspectExpedition().Data.Sections[
                CivilisationExpeditionModel.Section]["visited"].Values<string>(), Does.Contain(Rock));
            Assert.That(Begin(out _), Is.False); Complete(); Assert.That(support.Drops.Amount(Stone), Is.EqualTo(4));
        }
        [Test] public void MiningRejectsBagOnlyPickaxeAxeAndInactiveMeleeSlot()
        {
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(18, 1, 1, out _, out _), Is.True);
            Assert.That(Begin(out var error), Is.False); Assert.That(error, Is.EqualTo("civilisation.error.pickaxe"));
            Equip(17); Assert.That(Begin(out error), Is.False); Assert.That(error, Is.EqualTo("civilisation.error.pickaxe"));
            Equip(18, "Ranged"); Assert.That(Begin(out _), Is.False); Assert.That(Amount, Is.Zero);
        }
        [Test] public void UnequippingPickaxeBeforeCompletionLeavesRockAvailable()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error); Equip(0); Complete();
            Assert.That(Amount, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Rock), Is.False);
        }
        [Test] public void FullBagStillMinesRockAndLeavesStoneOnGround()
        {
            Equip(); var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(5, bag.Capacity - 1, 1, out _, out _), Is.True);
            Assert.That(Begin(out var error), Is.True, error); Complete();
            Assert.That(Amount, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Rock), Is.True);
            Assert.That(support.Drops.Amount(Stone), Is.EqualTo(4));
        }
        [Test] public void DamageInterruptsMiningAndAllowsRetry()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Damaged); Complete();
            Assert.That(Amount, Is.Zero); Assert.That(Begin(out error), Is.True, error); Complete(); Assert.That(support.Drops.Amount(Stone), Is.EqualTo(4));
        }
        [Test] public void MovingAwayDoesNotGrantStone()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error); Move(Vector3.zero); Complete();
            Assert.That(Amount, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Rock), Is.False);
        }
        [Test] public void DestroyModeCancelsMiningAndBlocksRestart()
        {
            Equip(); Assert.That(Begin(out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().BeginDestroyMode(out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            Assert.That(Begin(out _), Is.False); Complete(); Assert.That(Amount, Is.Zero);
            Game.Interface.GetSystem<TemporaryBuildingSystem>().CancelCurrentMode(); Assert.That(Begin(out error), Is.True, error);
        }
        [Test] public void SaveFailureRollsBackStoneAndRockConsumption()
        {
            Equip(); var root = Path.Combine(Path.GetTempPath(), "AnotherDayMiningFailure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "block");
            try
            {
                Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(blocked, "child"));
                Assert.That(Begin(out var error), Is.True, error); Complete();
                Assert.That(Amount, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(Rock), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }
        [TestCase(false, 8)] [TestCase(true, 0)]
        public void StoneUsesExistingReturnConversionAndDefeatLoss(bool defeated, int gold)
        {
            var basis = new BaseSaveData { Sections = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>
            { [CurrencyModel.SaveSectionId] = new JObject { ["schemaVersion"] = 1, ["gold"] = 0 } } };
            var journey = new ExpeditionSaveData { ExpeditionId = "0123456789abcdef0123456789abcdef", MapId = MapIds.CivilisationValley,
                Sections = new System.Collections.Generic.Dictionary<string, Newtonsoft.Json.Linq.JToken>
                { ["another-day.player-bag"] = new JObject { ["capacity"] = InventoryDefaults.PlayerBagCapacity, ["riskState"] = "InExpedition",
                    ["slots"] = new JArray(new JObject { ["index"] = 0, ["itemId"] = Stone, ["amount"] = 4 }) } } };
            Assert.That(CivilisationSettlementPolicy.TryApply(basis, journey, defeated, Game.Interface.GetUtility<IRuntimeConfigProvider>(), out var error), Is.True, error);
            Assert.That(basis.Sections[CurrencyModel.SaveSectionId]["gold"].Value<long>(), Is.EqualTo(gold));
            if (defeated) Assert.That(basis.Sections.ContainsKey("another-day.player-bag"), Is.False, "失败结算不能把远征石材带入基地");
            else Assert.That(basis.Sections["another-day.player-bag"]["slots"], Is.Empty);
        }
    }
}

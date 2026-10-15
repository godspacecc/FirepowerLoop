using System;
using System.IO;
using System.Reflection;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证有效近战挥击的资源进度、命中边界和原子结算；不进入运行模式。</summary>
    public sealed class CivilisationMeleeHarvestTests
    {
        private CivilisationHarvestTests support;
        private static readonly Vector3 Tree = new Vector3(16.96f, 0, 15.61f);
        private static readonly Vector3 Rock = new Vector3(8, 0, -9.5f);
        [SetUp] public void SetUp()
        {
            support = new CivilisationHarvestTests(); support.SetUp();
            Equip(5);
        }
        [TearDown] public void TearDown() => support?.TearDown();
        private static void Equip(int item, string slot = "Melee")
        {
            if (slot == "Ranged") item = 6;
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            if (item > 0 && !bag.Slots.Any(p => p.Value.ItemId == item))
                Assert.That(bag.TryAdd(item, 1, 1, out _, out _), Is.True);
            var index = item == 0 ? -1 : bag.Slots.First(p => p.Value.ItemId == item).Key;
            Game.Interface.GetModel<WeaponEquipmentModel>().Restore(new JObject
            { ["schemaVersion"] = 1, ["sourceContainerKind"] = item == 0 ? -1 : (int)InventoryContainerKind.PlayerBag, ["sourceSlotIndex"] = index, ["itemId"] = item });
        }
        private static bool Hit(Vector3 target, Vector3? direction = null, long instance = 1, float distance = .5f, int weapon = 5)
        {
            var origin = target - Vector3.forward * distance;
            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, origin, Quaternion.identity);
            var method = typeof(CivilisationSearchSystem).GetMethod("TryApplyMeleeHarvest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "近战挥击尚未接入资源采集");
            return (bool)method.Invoke(Game.Interface.GetSystem<CivilisationSearchSystem>(), new object[] { instance, origin, direction ?? Vector3.forward, weapon });
        }
        private static int Amount(int item) => Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(item);
        private int Dropped(int item) => support.Drops.Amount(item);
        private static void Finish(Vector3 target) { for (var i = 0; i < 20; i++) Hit(target); }

        [TestCase(false, 9, "tree.valley.5")]
        [TestCase(true, 31, "rock.valley.1")]
        public void SwordHitsAccumulateAndPersistExactlyOneReward(bool mining, int item, string id)
        {
            var target = mining ? Rock : Tree;
            Assert.That(Hit(target), Is.True);
            Assert.That(Amount(item), Is.Zero, "一次挥击不能跳过采集时长");
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(30);
            Assert.That(Amount(item), Is.Zero, "挥击采集不能靠等待自动完成");
            Finish(target);
            Assert.That(Amount(item), Is.Zero); Assert.That(Dropped(item), Is.EqualTo(4));
            Assert.That(Game.Interface.GetSystem<SaveSystem>().InspectExpedition().Data.Sections[CivilisationExpeditionModel.Section]["visited"].Values<string>(), Does.Contain(id));
            Assert.That(Hit(target), Is.False);
            Assert.That(Dropped(item), Is.EqualTo(4));
        }
        [Test] public void MissesOldMapAndInvalidWeaponDoNotAccumulate()
        {
            Assert.That(Hit(Tree, Vector3.back), Is.False);
            Assert.That(Hit(Tree, distance: 20), Is.False);
            Assert.That(Hit(Tree, instance: 2), Is.False);
            Assert.That(Hit(Tree, Vector3.zero), Is.False);
            Equip(0); Assert.That(Hit(Tree), Is.False);
            Equip(5, "Ranged"); Assert.That(Hit(Tree), Is.False);
            Equip(5); Assert.That(Hit(Tree), Is.True); Assert.That(Amount(9), Is.Zero);
        }
        [Test] public void FullBagStillCompletesMeleeHarvest()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(5, bag.Capacity - 1, 1, out _, out _), Is.True);
            Finish(Tree);
            Assert.That(Amount(9), Is.Zero);
            Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited("tree.valley.5"), Is.True);
            Assert.That(Dropped(9), Is.EqualTo(4));
        }
        [Test] public void SaveFailureRollsBackResourceAndReward()
        {
            var root = Path.Combine(Path.GetTempPath(), "AnotherDayMeleeHarvest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "block");
            try
            {
                Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(blocked, "child"));
                Finish(Rock);
                Assert.That(Amount(31), Is.Zero);
                Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited("rock.valley.1"), Is.False);
                Assert.That(Game.Interface.GetModel<LootModel>().Loot, Is.Empty);
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public void DestroyModeBlocksMeleeHarvest()
        {
            Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().BeginDestroyMode(out var error), Is.True, error);
            Assert.That(Hit(Tree), Is.False); Assert.That(Amount(9), Is.Zero);
        }
        [Test] public void OrdinarySearchPointCannotBeHarvestedBySword()
        {
            Assert.That(Hit(new Vector3(0, 0, 10)), Is.False);
            Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited("camp.fire"), Is.False);
        }
        [Test] public void MapReleaseDiscardsPartialMeleeProgress()
        {
            for (var i = 0; i < 6; i++) Hit(Tree);
            Assert.That(Amount(9), Is.Zero);
            Game.Interface.GetSystem<CivilisationSearchSystem>().ReleaseMap(1);
            Assert.That(Hit(Tree), Is.True); Assert.That(Amount(9), Is.Zero);
            Finish(Tree); Assert.That(Amount(9), Is.Zero); Assert.That(Dropped(9), Is.EqualTo(4));
        }
        [TestCase(17, false, 9, "tree.valley.5")]
        [TestCase(18, true, 31, "rock.valley.1")]
        public void OriginalToolInteractionStillCompletesOnce(int tool, bool mining, int item, string id)
        {
            Equip(tool);
            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, mining ? Rock : Tree, Quaternion.identity);
            Assert.That(Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, id, out var error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3.1f);
            Assert.That(Amount(item), Is.Zero); Assert.That(Dropped(item), Is.EqualTo(4));
            Assert.That(Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, id, out _), Is.False);
        }
        [Test] public void AttackInputRespectsCooldownAndHarvestsThroughWeaponSystem()
        {
            var architecture = (Game)Game.Interface;
            var port = new HarvestPort(Tree - Vector3.forward * .5f);
            architecture.RegisterUtility<IPlayerRuntimePortProvider>(port);
            architecture.RegisterSystem(new WeaponSystem());
            architecture.GetSystem<UnitSystem>().TryUpdateTransform(1, port.Position, Quaternion.identity);
            architecture.SendEvent(new PlayerReadyEvent(1, 1));
            for (var i = 0; i < 20; i++) architecture.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(Amount(9), Is.Zero, "同一冷却内的重复输入不能累计采集");
            var tick = typeof(WeaponSystem).GetMethod("TickCooldown", BindingFlags.Instance | BindingFlags.NonPublic);
            for (var i = 0; i < 18; i++)
            {
                tick.Invoke(architecture.GetSystem<WeaponSystem>(), new object[] { .5f });
                architecture.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            }
            Assert.That(Amount(9), Is.Zero, "短剑19次命中尚未完成采集");
            tick.Invoke(architecture.GetSystem<WeaponSystem>(), new object[] { .5f });
            architecture.SendEvent(new PlayerAttackRequestedEvent(Vector3.forward));
            Assert.That(Amount(9), Is.Zero); Assert.That(Dropped(9), Is.EqualTo(4));
        }
        [TestCase(5, false, 9, 20)] [TestCase(5, true, 31, 20)]
        [TestCase(17, false, 9, 7)] [TestCase(18, false, 9, 14)]
        [TestCase(17, true, 31, 14)] [TestCase(18, true, 31, 7)]
        public void MeleeEfficiencyRequiresExpectedNumberOfHits(int weapon, bool mining, int item, int hits)
        {
            Equip(weapon);
            for (var i = 0; i < hits - 1; i++) Assert.That(Hit(mining ? Rock : Tree, weapon: weapon), Is.True);
            Assert.That(Amount(item), Is.Zero, "低效率武器不能提前完成采集");
            Assert.That(Hit(mining ? Rock : Tree, weapon: weapon), Is.True);
            Assert.That(Amount(item), Is.Zero); Assert.That(Dropped(item), Is.EqualTo(4));
        }
        [Test] public void GeneratedMapUsesCurrentPositionsAndHitsOnlyNearestResource()
        {
            var original = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.CivilisationValley, 1);
            var handle = new GeneratedValleyContentHandle(original, null, new ValleyLayoutSaveData());
            try
            {
                typeof(GeneratedValleyContentHandle).GetField("GeneratedRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(handle, original.Root);
                var position = new Vector3(1000, 0, 1000);
                var points = new[]
                {
                    new CivilisationSearchPointDefinition("generated.valley.forest.1", MapIds.CivilisationValley, "", 9, 4, 3, position.x, position.z, "", 17, true),
                    new CivilisationSearchPointDefinition("generated.valley.forest.2", MapIds.CivilisationValley, "", 9, 4, 3, position.x, position.z + .5f, "", 17, true)
                };
                typeof(GeneratedValleyContentHandle).GetProperty("Points").SetValue(handle, points);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.CivilisationValley, handle);
                for (var i = 0; i < 20; i++) Assert.That(Hit(position), Is.True);
                Assert.That(Amount(9), Is.Zero); Assert.That(Dropped(9), Is.EqualTo(4));
                Assert.That(Game.Interface.GetModel<LootModel>().Loot.Values.Single().Position, Is.EqualTo(position));
                Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(points[0].Id), Is.True);
                Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(points[1].Id), Is.False);
                Assert.That(Hit(Tree), Is.False, "生成图不能使用旧模板坐标");
            }
            finally
            {
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.CivilisationValley, null);
                UnityEngine.Object.DestroyImmediate(original.Root);
            }
        }
        private sealed class HarvestPort : ICharacterMotorPort, IPlayerRuntimePortProvider
        {
            public HarvestPort(Vector3 position) { Position = position; }
            public int UnitId => 1;
            public long MapInstanceId => 1;
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position { get; }
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0;
            public bool IsGrounded => true;
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
            public void FaceDirection(Vector3 direction) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Position; return true; }
            public void FaceWorldHud(Vector3 position, Vector3 up) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
            public bool TryGetMotorPort(int unitId, long instance, out ICharacterMotorPort port) { port = this; return true; }
            public bool TryGetCameraPort(int unitId, long instance, out IPlayerCameraPort port) { port = null; return false; }
            public bool IsPlayerCollider(int unitId, long instance, Collider collider) => false;
        }
    }
}

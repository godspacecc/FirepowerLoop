using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class CombatSystemTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
        }

        [TestCase(9f, 8f, 1.35f, EnemyIntent.Idle)]
        [TestCase(5f, 8f, 1.35f, EnemyIntent.Chase)]
        [TestCase(1f, 8f, 1.35f, EnemyIntent.Attack)]
        public void EnemyIntentDependsOnDistance(
            float distance,
            float aggroRange,
            float attackRange,
            EnemyIntent expected)
        {
            Assert.That(
                EnemySystem.ResolveIntent(distance, aggroRange, attackRange),
                Is.EqualTo(expected));
        }

        [Test]
        public void EnemyDeathPublishesOneEventWithoutDirectReward()
        {
            var deaths = 0;
            var bagBefore = Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts);
            var warehouseBefore = Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts);
            var unregister = Game.Interface.RegisterEvent<CombatantDiedEvent>(evt =>
            {
                if (evt.UnitId == 10 && evt.KillerUnitId == PlayerModel.LocalPlayerUnitId)
                {
                    deaths++;
                }
            });

            var combat = Game.Interface.GetSystem<CombatSystem>();
            RegisterPlayerAndEnemy(10, enemyHealth: 10, playerDamage: 10, enemyDamage: 2);
            Assert.That(combat.TryDamage(PlayerModel.LocalPlayerUnitId, 10), Is.True);
            Assert.That(combat.TryDamage(PlayerModel.LocalPlayerUnitId, 10), Is.False);

            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(bagBefore));
            Assert.That(Game.Interface.GetModel<WarehouseModel>().Container.GetTotalAmount(ItemIds.MetalParts), Is.EqualTo(warehouseBefore));
            unregister.UnRegister();
        }

        [Test]
        public void PlayerDamageWritesCombatModelAndPublishesEvent()
        {
            var changed = 0;
            var unregister = Game.Interface.RegisterEvent<CombatHealthChangedEvent>(evt =>
            {
                if (evt.Faction == CombatFaction.Player)
                {
                    changed++;
                }
            });

            RegisterPlayerAndEnemy(10, enemyHealth: 10, playerDamage: 10, enemyDamage: 25);
            Game.Interface.GetSystem<CombatSystem>().TryDamage(10, PlayerModel.LocalPlayerUnitId);

            Assert.That(
                Game.Interface.GetModel<CombatModel>().PlayerHP.Value,
                Is.EqualTo(75));
            Assert.That(changed, Is.EqualTo(1));
            unregister.UnRegister();
        }

        [Test]
        public void BaseRecoveryRestoresFiveHealthPerSecondWithAccumulator()
        {
            var health = Game.Interface.GetModel<CombatModel>();
            health.SetPlayerHP(50);
            var combat = Game.Interface.GetSystem<CombatSystem>();

            combat.TickBaseRecovery(0.1f, AppState.Playing, MapIds.Base);
            Assert.That(health.PlayerHP.Value, Is.EqualTo(50));

            combat.TickBaseRecovery(0.1f, AppState.Playing, MapIds.Base);
            Assert.That(health.PlayerHP.Value, Is.EqualTo(51));
        }

        [Test]
        public void BaseRecoveryDoesNotRunOutsideBasePlayingState()
        {
            var health = Game.Interface.GetModel<CombatModel>();
            health.SetPlayerHP(50);
            var combat = Game.Interface.GetSystem<CombatSystem>();

            combat.TickBaseRecovery(1f, AppState.Paused, MapIds.Base);
            combat.TickBaseRecovery(1f, AppState.Loading, MapIds.Base);
            combat.TickBaseRecovery(1f, AppState.Paused, MapIds.Base);
            combat.TickBaseRecovery(1f, AppState.Playing, MapIds.SurvivalSample);
            combat.TickBaseRecovery(1f, AppState.Playing, MapIds.MissionSample);

            Assert.That(health.PlayerHP.Value, Is.EqualTo(50));
        }

        [Test]
        public void BaseRecoveryDoesNotExceedMaxHP()
        {
            var health = Game.Interface.GetModel<CombatModel>();
            health.SetPlayerHP(99);

            Game.Interface.GetSystem<CombatSystem>()
                .TickBaseRecovery(10f, AppState.Playing, MapIds.Base);

            Assert.That(health.PlayerHP.Value, Is.EqualTo(100));
        }

        [Test]
        public void DeathCanPublishAgainAfterHealthBecomesPositive()
        {
            var deaths = 0;
            var unregister = Game.Interface.RegisterEvent<CombatantDiedEvent>(evt =>
            {
                if (evt.UnitId == PlayerModel.LocalPlayerUnitId)
                {
                    deaths++;
                }
            });
            var health = Game.Interface.GetModel<CombatModel>();
            var combat = Game.Interface.GetSystem<CombatSystem>();

            RegisterPlayerAndEnemy(10, enemyHealth: 10, playerDamage: 10, enemyDamage: 100);
            combat.TryDamage(10, PlayerModel.LocalPlayerUnitId);
            combat.TryDamage(10, PlayerModel.LocalPlayerUnitId);
            Assert.That(deaths, Is.EqualTo(1));

            health.SetPlayerHP(1);
            combat.TickBaseRecovery(0.1f, AppState.Playing, MapIds.Base);
            combat.TryDamage(10, PlayerModel.LocalPlayerUnitId);

            Assert.That(deaths, Is.EqualTo(2));
            unregister.UnRegister();
        }

        [Test]
        public void CombatSystemDoesNotExposeFullHealthReset()
        {
            var source = File.ReadAllText(
                "Assets/Game/Scripts/System/Combat/CombatSystem.cs");

            Assert.That(source, Does.Not.Contain("ResetPlayerHealth"));
        }

        [Test]
        public void EnemyKilledCreatesOnePickupAtDeathPosition()
        {
            var system = new EnemyDropRule();
            var position = new Vector3(3f, 0f, 5f);

            var first = system.TryHandle(
                new CombatantDiedEvent(7, PlayerModel.LocalPlayerUnitId),
                canCreateDrop: true,
                mapInstanceId: 10,
                lootConfigId: "loot.test-resource",
                position,
                Quaternion.identity,
                out var request);
            var second = system.TryHandle(
                new CombatantDiedEvent(7, PlayerModel.LocalPlayerUnitId),
                canCreateDrop: true,
                mapInstanceId: 10,
                lootConfigId: "loot.test-resource",
                position,
                Quaternion.identity,
                out _);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(request.Position, Is.EqualTo(position));
            Assert.That(request.MapInstanceId, Is.EqualTo(10));
            Assert.That(request.LootConfigId, Is.EqualTo("loot.test-resource"));
        }

        [Test]
        public void EnemyDropRuleRejectsCollectionWhenExpeditionCannotCollect()
        {
            var system = new EnemyDropRule();

            var created = system.TryHandle(
                new CombatantDiedEvent(7, PlayerModel.LocalPlayerUnitId),
                canCreateDrop: false,
                mapInstanceId: 10,
                lootConfigId: "loot.test-resource",
                Vector3.zero,
                Quaternion.identity,
                out _);

            Assert.That(created, Is.False);
        }

        [Test]
        public void EnemyDropRuleResetAllowsEnemyIdReuseAfterMapChange()
        {
            var system = new EnemyDropRule();
            var evt = new CombatantDiedEvent(7, PlayerModel.LocalPlayerUnitId);

            var first = system.TryHandle(evt, canCreateDrop: true, 10, "loot.test-resource", Vector3.zero, Quaternion.identity, out _);
            var second = system.TryHandle(evt, canCreateDrop: true, 10, "loot.test-resource", Vector3.zero, Quaternion.identity, out _);
            system.Reset();
            var third = system.TryHandle(evt, canCreateDrop: true, 10, "loot.test-resource", Vector3.zero, Quaternion.identity, out _);

            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
            Assert.That(third, Is.True);
        }

        [Test]
        public void EnemyDropSystemResetsRuleWhenMapLoadingCompletes()
        {
            var source = File.ReadAllText(
                "Assets/Game/Scripts/System/Combat/EnemyDropSystem.cs");

            Assert.That(source, Does.Contain("RegisterEvent<MapLoadingCompletedEvent>"));
            Assert.That(source, Does.Contain("rule.Reset()"));
            Assert.That(source, Does.Not.Contain("Object.Instantiate"));
            Assert.That(source, Does.Not.Contain("TestResourcePickupPrefab"));
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private static void RegisterPlayerAndEnemy(
            int enemyId,
            int enemyHealth,
            int playerDamage,
            int enemyDamage)
        {
            var unitSystem = Game.Interface.GetSystem<UnitSystem>();
            unitSystem.TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                1,
                UnitType.Player,
                UnitSource.Player,
                "player.graybox.default",
                Vector3.zero,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out _);
            unitSystem.TryRegister(new UnitSnapshot(
                enemyId,
                1,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                "enemy.test",
                Vector3.forward,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out _);
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.RegisterOrResetCombatant(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                100,
                playerDamage,
                0);
            combat.RegisterOrResetCombatant(
                enemyId,
                CombatFaction.Enemy,
                enemyHealth,
                enemyDamage,
                PlayerModel.LocalPlayerUnitId);
        }

        private static void SetPrivateField<TTarget, TValue>(
            TTarget target,
            string fieldName,
            TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static TopDownCameraProfile playerViewCameraProfile(GameObject playerObject)
        {
            return playerObject.GetComponent<PlayerView>().CameraProfile;
        }

        private sealed class FakePlayerPortProvider : IPlayerRuntimePortProvider
        {
            private readonly ICharacterMotorPort motorPort;

            public FakePlayerPortProvider(ICharacterMotorPort motorPort)
            {
                this.motorPort = motorPort;
            }

            public bool TryGetMotorPort(int unitId, long MapInstanceId, out ICharacterMotorPort port)
            {
                port = motorPort;
                return true;
            }

            public bool TryGetCameraPort(int unitId, long MapInstanceId, out IPlayerCameraPort port)
            {
                port = new FakePlayerCameraPort();
                return true;
            }

            public bool IsPlayerCollider(int unitId, long MapInstanceId, Collider collider)
            {
                return false;
            }
        }

        private sealed class FakePlayerCameraPort : IPlayerCameraPort
        {
            public bool IsValid => true;
            public void ApplyProfile() { }
            public void LateUpdate() { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
            {
                position = Vector3.back;
                up = Vector3.up;
                return true;
            }
        }

    }
}

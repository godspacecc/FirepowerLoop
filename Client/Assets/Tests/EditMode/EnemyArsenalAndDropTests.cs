using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class EnemyArsenalAndDropTests
    {
        private const long MapInstanceId = 44;
        private const string BasicEnemyId = "enemy.graybox.basic";
        private const string ArcherEnemyId = "enemy.graybox.archer";
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private NavMeshDataInstance navMeshDataInstance;
        private FakeProjectilePort projectilePort;
        private FakePlayerPortProvider playerProvider;
        private FakeEnemyResolver enemyResolver;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.ResetForTests();
            projectilePort = new FakeProjectilePort();
            playerProvider = new FakePlayerPortProvider();
            enemyResolver = new FakeEnemyResolver();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestRuntimeConfigProvider());
                architecture.RegisterUtility<IProjectileRuntimePortProvider>(projectilePort);
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(playerProvider);
                architecture.RegisterUtility<IEnemyColliderResolver>(enemyResolver);
            };
            _ = Game.Interface.GetSystem<ProjectileSystem>();
            _ = Game.Interface.GetSystem<EnemySystem>();
            _ = Game.Interface.GetSystem<EnemyDropSystem>();
            _ = Game.Interface.GetSystem<CombatSystem>();
            Game.Interface.RegisterEvent<ProjectileEndedEvent>(evt => projectilePort.EndedReasons.Add(evt.Reason));
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
        }

        [TearDown]
        public void TearDown()
        {
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            ResourceLoader.ResetForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
            for (var i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            }

            objects.Clear();
        }

        [Test]
        public void EnemyArsenalConfigsAndValidationAreExact()
        {
            var provider = CreateRealConfigProvider();
            Assert.That(provider.TryGetEnemy(BasicEnemyId, out var basic, out var error), Is.True, error);
            Assert.That(basic.GuaranteedWeaponLootConfigId, Is.EqualTo("loot.weapon.short-sword"));
            Assert.That(basic.GuaranteedAmmoLootConfigId, Is.Empty);
            Assert.That(basic.AmmoCapacity, Is.EqualTo(0));
            Assert.That(basic.FallbackMeleeAttackRange, Is.EqualTo(0f).Within(0.001f));
            Assert.That(basic.LootConfigId, Is.EqualTo("loot.metal-parts"));

            Assert.That(provider.TryGetEnemy(ArcherEnemyId, out var archer, out error), Is.True, error);
            Assert.That(archer.GuaranteedWeaponLootConfigId, Is.EqualTo("loot.weapon.bow"));
            Assert.That(archer.GuaranteedAmmoLootConfigId, Is.EqualTo("loot.ammunition.arrow"));
            Assert.That(archer.AmmoCapacity, Is.EqualTo(10));
            Assert.That(archer.FallbackMeleeAttackRange, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(archer.LootConfigId, Is.EqualTo("loot.metal-parts"));

            AssertLoot(provider, "loot.weapon.short-sword", ItemIds.ShortSword, 1);
            AssertLoot(provider, "loot.weapon.bow", ItemIds.Bow, 1);
            AssertLoot(provider, "loot.ammunition.arrow", ItemIds.Arrow, 1);

            AssertInvalidEnemyConfig(
                table => table.Loots.Remove("loot.weapon.bow"),
                "TbEnemy",
                ArcherEnemyId,
                "GuaranteedWeaponLootConfigId");
            AssertInvalidEnemyConfig(
                table => table.Loots["loot.weapon.bow"] = WithItemId(table.Loots["loot.weapon.bow"], ItemIds.MetalParts),
                "TbLoot",
                "loot.weapon.bow",
                "ItemId");
            AssertInvalidEnemyConfig(
                table => table.Loots["loot.weapon.bow"] = WithAmount(table.Loots["loot.weapon.bow"], 2),
                "TbLoot",
                "loot.weapon.bow",
                "Amount");
            AssertInvalidEnemyConfig(
                table => table.Enemies[ArcherEnemyId] = WithAmmoCapacity(table.Enemies[ArcherEnemyId], 0),
                "TbEnemy",
                ArcherEnemyId,
                "AmmoCapacity");
            AssertInvalidEnemyConfig(
                table => table.Loots.Remove("loot.ammunition.arrow"),
                "TbEnemy",
                ArcherEnemyId,
                "GuaranteedAmmoLootConfigId");
            AssertInvalidEnemyConfig(
                table => table.Loots["loot.ammunition.arrow"] = WithItemId(table.Loots["loot.ammunition.arrow"], ItemIds.MetalParts),
                "TbLoot",
                "loot.ammunition.arrow",
                "ItemId");
            AssertInvalidEnemyConfig(
                table => table.Enemies[ArcherEnemyId] = WithRetreatDistance(table.Enemies[ArcherEnemyId], 0f),
                "TbEnemy",
                ArcherEnemyId,
                "RetreatDistance");
            AssertInvalidEnemyConfig(
                table => table.Enemies[ArcherEnemyId] = WithRetreatDistance(table.Enemies[ArcherEnemyId], 9f),
                "TbEnemy",
                ArcherEnemyId,
                "RetreatDistance");
            AssertInvalidEnemyConfig(
                table => table.Enemies[ArcherEnemyId] = WithFallbackMeleeAttackRange(table.Enemies[ArcherEnemyId], 0f),
                "TbEnemy",
                ArcherEnemyId,
                "FallbackMeleeAttackRange");
            AssertInvalidEnemyConfig(
                table => table.Enemies[ArcherEnemyId] = WithFallbackMeleeAttackRange(table.Enemies[ArcherEnemyId], 13f),
                "TbEnemy",
                ArcherEnemyId,
                "FallbackMeleeAttackRange");
            AssertInvalidEnemyConfig(
                table => table.Enemies[BasicEnemyId] = WithGuaranteedAmmoLootConfigId(table.Enemies[BasicEnemyId], "loot.ammunition.arrow"),
                "TbEnemy",
                BasicEnemyId,
                "GuaranteedAmmoLootConfigId");
            AssertInvalidEnemyConfig(
                table => table.Enemies[BasicEnemyId] = WithAmmoCapacity(table.Enemies[BasicEnemyId], 1),
                "TbEnemy",
                BasicEnemyId,
                "AmmoCapacity");
            AssertInvalidEnemyConfig(
                table => table.Enemies[BasicEnemyId] = WithFallbackMeleeAttackRange(table.Enemies[BasicEnemyId], 1f),
                "TbEnemy",
                BasicEnemyId,
                "FallbackMeleeAttackRange");
        }

        [Test]
        public void ArcherConsumesTenSuccessfulShotsThenFallsBackToMelee()
        {
            RegisterPlayer(Vector3.zero);
            var archer = TestRuntimeConfigProvider.ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 5f), archer, 100);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);

            for (var i = 9; i >= 0; i--)
            {
                FireOneShot(1000);
                Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetRemainingAmmo(1000, out var ammo), Is.True);
                Assert.That(ammo, Is.EqualTo(i));
                SetEnemyCooldown(1000, 0f);
            }

            Assert.That(projectilePort.CreateCount, Is.EqualTo(10));
            MovePlayer(new Vector3(0f, 0f, 5f));
            InvokeEnemyTick(1000, 0.1f);
            Assert.That(projectilePort.CreateCount, Is.EqualTo(10));
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(PlayerModel.LocalPlayerUnitId, out var player), Is.True);
            Assert.That(player.HP, Is.EqualTo(92));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(1000, out var intent), Is.True);
            Assert.That(intent, Is.EqualTo(EnemyIntent.Attack));
        }

        [Test]
        public void ArcherReloadsOneRoundAutomaticallyAndCleansStateAtIdle()
        {
            RegisterPlayer(Vector3.zero);
            var archer = TestRuntimeConfigProvider.ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 5f), archer, 100);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f));
            var enemies = Game.Interface.GetSystem<EnemySystem>();
            Assert.That(enemies.TryRegisterEnemy(port, archer, out var error), Is.True, error);

            // 初始弹匣为 1；第一发成功后总弹药减少并立即开始换弹。
            InvokeEnemyTick(1000, 0f);
            InvokeEnemyTick(1000, 1f);
            AssertRemainingAmmo(1000, 9);
            Assert.That(enemies.TryGetReloadProgress(1000, out var progress, out _), Is.True);
            Assert.That(progress, Is.EqualTo(0f).Within(0.001f));
            Assert.That(GetEnemyCooldown(1000), Is.GreaterThan(0f));

            // 冷却与换弹并行，半程可见，完成后进度隐藏。
            InvokeEnemyTick(1000, 0.5f);
            Assert.That(enemies.TryGetReloadProgress(1000, out progress, out _), Is.True);
            Assert.That(progress, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(GetEnemyCooldown(1000), Is.EqualTo(1.5f).Within(0.001f));
            InvokeEnemyTick(1000, 0.5f);
            Assert.That(enemies.TryGetReloadProgress(1000, out _, out _), Is.False);

            // 离开仇恨范围会清理换弹并补满；重新注册也回到初始装填状态。
            SetEnemyCooldown(1000, 0f);
            InvokeEnemyTick(1000, 0f);
            InvokeEnemyTick(1000, 1f);
            Assert.That(enemies.TryGetReloadProgress(1000, out _, out _), Is.True);
            MovePlayer(new Vector3(0f, 0f, 30f));
            InvokeEnemyTick(1000, 0f);
            AssertRemainingAmmo(1000, 10);
            Assert.That(enemies.TryGetReloadProgress(1000, out _, out _), Is.False);
            Assert.That(enemies.UnregisterEnemy(1000), Is.True);
            Assert.That(enemies.TryRegisterEnemy(port, archer, out error), Is.True, error);
            MovePlayer(Vector3.zero);
            FireOneShot(1000);
            AssertRemainingAmmo(1000, 9);
        }

        [Test]
        public void ArcherPrefabProvidesReloadProgressPresentation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxArcher.prefab");
            Assert.That(prefab, Is.Not.Null);
            var view = prefab.GetComponent<EnemyView>();
            Assert.That(view, Is.Not.Null);
            var progress = typeof(EnemyView)
                .GetField("actionProgressView", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(view);
            Assert.That(progress, Is.Not.Null, "弓箭手必须接入 UnitActionProgressView。");
            Assert.That(
                progress.GetType().GetField("root", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(progress),
                Is.Not.Null,
                "换弹进度环必须引用既有 UnitActionProgress.prefab。");
        }

        [Test]
        public void FailedProjectileDoesNotConsumeAmmoAndUsesCooldown()
        {
            projectilePort.CreateSucceeds = false;
            RegisterPlayer(Vector3.zero);
            var archer = TestRuntimeConfigProvider.ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 5f), archer, 100);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);

            InvokeEnemyTick(1000, 0f);
            InvokeEnemyTick(1000, 1f);

            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetRemainingAmmo(1000, out var ammo), Is.True);
            Assert.That(ammo, Is.EqualTo(10));
            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetReloadProgress(1000, out _, out _), Is.False);
            InvokeEnemyTick(1000, 0.1f);
            Assert.That(projectilePort.CreateCount, Is.EqualTo(1));
        }

        [Test]
        public void OnlyAggroExitRefillsArcherAmmo()
        {
            RegisterPlayer(Vector3.zero);
            var archer = TestRuntimeConfigProvider.ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 5f), archer, 100);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);
            FireOneShot(1000);
            AssertRemainingAmmo(1000, 9);

            MovePlayer(new Vector3(0f, 0f, 10f));
            InvokeEnemyTick(1000, 0f);
            AssertRemainingAmmo(1000, 9);

            MovePlayer(new Vector3(0f, 0f, 30f));
            InvokeEnemyTick(1000, 0f);
            AssertRemainingAmmo(1000, 10);
            Assert.That(port.TelegraphVisible, Is.False);
        }

        [Test]
        public void EnemyAmmoIsTransientPerMapInstance()
        {
            RegisterPlayer(Vector3.zero);
            var archer = TestRuntimeConfigProvider.ArcherConfig();
            RegisterEnemy(1000, new Vector3(0f, 0f, 5f), archer, 100);
            var port = new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f));
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out var error), Is.True, error);
            FireOneShot(1000);
            AssertRemainingAmmo(1000, 9);

            Assert.That(Game.Interface.GetSystem<EnemySystem>().UnregisterEnemy(1000), Is.True);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetRemainingAmmo(1000, out _), Is.False);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(port, archer, out error), Is.True, error);
            AssertRemainingAmmo(1000, 10);
        }

        [Test]
        public void ProjectileMovementUsesNearestOrderedContinuousHit()
        {
            RegisterPlayer(Vector3.zero);
            RegisterEnemy(2000, new Vector3(0f, 0f, 4f), TestRuntimeConfigProvider.BasicConfig(), 20);
            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TrySpawn(CreateSpawnRequest(), out var id, out var error), Is.True, error);
            var farEnemy = CreateCollider("far-enemy", true);
            var nearObstacle = CreateCollider("near-wall", false);
            enemyResolver.Bind(farEnemy, 2000);
            projectilePort.Hits.Add(new ProjectileMovementHit(farEnemy, 4f));
            projectilePort.Hits.Add(new ProjectileMovementHit(nearObstacle, 1f));

            InvokeProjectileAdvance(id, 1f);

            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(2000, out var enemy), Is.True);
            Assert.That(enemy.HP, Is.EqualTo(20));
            Assert.That(projectilePort.MoveCount, Is.EqualTo(0));
            Assert.That(projectilePort.EndedReasons.Single(), Is.EqualTo(ProjectileEndReason.HitObstacle));
        }

        [Test]
        public void EnemyArrowDamagesPlayerOnceAndPassesThroughEnemies()
        {
            RegisterPlayer(Vector3.zero);
            RegisterEnemy(1000, Vector3.back, TestRuntimeConfigProvider.ArcherConfig(), 100);
            RegisterEnemy(2000, new Vector3(0f, 0f, 1f), TestRuntimeConfigProvider.BasicConfig(), 20);
            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TrySpawn(CreateSpawnRequest(1000, CombatFaction.Enemy), out var id, out var error), Is.True, error);
            var friendly = CreateCollider("friendly", true);
            var player = CreateCollider("player", false);
            enemyResolver.Bind(friendly, 2000);
            playerProvider.PlayerCollider = player;
            projectilePort.Hits.Add(new ProjectileMovementHit(friendly, 1f));
            projectilePort.Hits.Add(new ProjectileMovementHit(player, 2f));

            InvokeProjectileAdvance(id, 1f);

            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(PlayerModel.LocalPlayerUnitId, out var combatant), Is.True);
            Assert.That(combatant.HP, Is.EqualTo(90));
            Assert.That(projectilePort.EndedReasons.Count, Is.EqualTo(1));
        }

        [Test]
        public void PlayerArrowIgnoresPlayerAndDamagesEnemyOnce()
        {
            RegisterPlayer(Vector3.zero);
            RegisterEnemy(2000, new Vector3(0f, 0f, 2f), TestRuntimeConfigProvider.BasicConfig(), 20);
            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TrySpawn(CreateSpawnRequest(PlayerModel.LocalPlayerUnitId, CombatFaction.Player), out var id, out var error), Is.True, error);
            var player = CreateCollider("player", false);
            var enemy = CreateCollider("enemy", true);
            playerProvider.PlayerCollider = player;
            enemyResolver.Bind(enemy, 2000);
            projectilePort.Hits.Add(new ProjectileMovementHit(player, 1f));
            projectilePort.Hits.Add(new ProjectileMovementHit(enemy, 2f));

            InvokeProjectileAdvance(id, 1f);

            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(2000, out var combatant), Is.True);
            Assert.That(combatant.HP, Is.EqualTo(10));
            Assert.That(projectilePort.EndedReasons.Count, Is.EqualTo(1));
        }

        [Test]
        public void ObstacleBeforeTargetEndsProjectileWithoutDamage()
        {
            RegisterPlayer(Vector3.zero);
            RegisterEnemy(2000, new Vector3(0f, 0f, 3f), TestRuntimeConfigProvider.BasicConfig(), 20);
            Assert.That(Game.Interface.GetSystem<ProjectileSystem>().TrySpawn(CreateSpawnRequest(PlayerModel.LocalPlayerUnitId, CombatFaction.Player), out var id, out var error), Is.True, error);
            var wall = CreateCollider("wall", false);
            var enemy = CreateCollider("enemy", true);
            enemyResolver.Bind(enemy, 2000);
            projectilePort.Hits.Add(new ProjectileMovementHit(wall, 1f));
            projectilePort.Hits.Add(new ProjectileMovementHit(enemy, 2f));

            InvokeProjectileAdvance(id, 1f);

            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryGetCombatant(2000, out var combatant), Is.True);
            Assert.That(combatant.HP, Is.EqualTo(20));
            Assert.That(projectilePort.EndedReasons.Single(), Is.EqualTo(ProjectileEndReason.HitObstacle));
        }

        [Test]
        public void BasicEnemyDropsSwordAndExistingExtraLootAsOneBatch()
        {
            RegisterDropEnemy(1000, BasicEnemyId, Vector3.one, TestRuntimeConfigProvider.BasicConfig(), 20);
            var batches = CaptureCorpseRequests();
            KillEnemy(1000);

            Assert.That(batches.Count, Is.EqualTo(1));
            AssertBatch(batches[0], 1000, "loot.weapon.short-sword", "loot.metal-parts");
        }

        [Test]
        public void ArcherDropsBowRemainingAmmoAndExistingExtraLoot()
        {
            RegisterDropEnemy(1000, ArcherEnemyId, Vector3.one, TestRuntimeConfigProvider.ArcherConfig(), 20);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(
                new TestEnemyPort(1000, MapInstanceId, Vector3.one),
                TestRuntimeConfigProvider.ArcherConfig(),
                out var error), Is.True, error);
            var batches = CaptureCorpseRequests();
            KillEnemy(1000);

            Assert.That(batches.Count, Is.EqualTo(1));
            AssertBatch(batches[0], 1000, "loot.weapon.bow", "loot.ammunition.arrow", "loot.metal-parts");
            Assert.That(batches[0].Entries[1].AmountOverride, Is.EqualTo(10));

            RegisterDropEnemy(1001, ArcherEnemyId, new Vector3(0f, 0f, 5f), TestRuntimeConfigProvider.ArcherConfig(), 20);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(
                new TestEnemyPort(1001, MapInstanceId, new Vector3(0f, 0f, 5f)),
                TestRuntimeConfigProvider.ArcherConfig(),
                out error), Is.True, error);
            for (var i = 0; i < 9; i++)
            {
                FireOneShot(1001);
                SetEnemyCooldown(1001, 0f);
            }

            var oneArrowBatches = CaptureCorpseRequests();
            KillEnemy(1001);

            Assert.That(oneArrowBatches.Count, Is.EqualTo(1));
            AssertBatch(oneArrowBatches[0], 1001, "loot.weapon.bow", "loot.ammunition.arrow", "loot.metal-parts");
            Assert.That(oneArrowBatches[0].Entries[1].AmountOverride, Is.EqualTo(1));
        }

        [Test]
        public void ZeroAmmoOmitsArrowLoot()
        {
            var archer = TestRuntimeConfigProvider.ArcherConfig();
            RegisterPlayer(Vector3.zero);
            RegisterDropEnemy(1000, ArcherEnemyId, new Vector3(0f, 0f, 5f), archer, 20);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryRegisterEnemy(
                new TestEnemyPort(1000, MapInstanceId, new Vector3(0f, 0f, 5f)),
                archer,
                out var error), Is.True, error);
            for (var i = 0; i < 10; i++)
            {
                FireOneShot(1000);
                SetEnemyCooldown(1000, 0f);
            }

            var batches = CaptureCorpseRequests();
            KillEnemy(1000);

            Assert.That(batches.Count, Is.EqualTo(1));
            AssertBatch(batches[0], 1000, "loot.weapon.bow", "loot.metal-parts");
        }

        [Test]
        public void BatchFailureRollsBackAllLootUnitAndPresentationState()
        {
            var loader = new TestResourceLoader { Asset = CreateLootPrefab("batch-loot-prefab") };
            var instances = new TestViewInstanceProvider();
            var configs = new DropConfigProvider();
            configs.Loots["loot.good"] = new LootConfigDefinition("loot.good", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.MetalParts, 1);
            configs.Loots["loot.invalid-item"] = new LootConfigDefinition("loot.invalid-item", "Prefabs/SceneObjects/TestResourcePickup", 0, 1);
            configs.Enemies["enemy.batch"] = WithId(
                WithGuaranteedWeaponLootConfigId(
                    WithLootConfigId(TestRuntimeConfigProvider.BasicConfig(), "loot.invalid-item"),
                    "loot.good"),
                "enemy.batch");
            ResourceLoader.SetLoaderForTests(loader);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configs);
            var ctrl = new LootCtrl(Game.Interface);
            var context = CreateContext(new[] { EnemySpawn("enemy.batch") });
            var prepareResult = Prepare(ctrl, context);
            Assert.That(prepareResult.Success, Is.True, prepareResult.Error);
            var commitResult = ctrl.CommitPreparedMap();
            Assert.That(commitResult.Success, Is.True, commitResult.Error);

            var beforeUnits = Game.Interface.GetModel<UnitModel>().Count;
            var beforeLoot = Game.Interface.GetModel<LootModel>().Loot.Count;
            var beforeViews = Game.Interface.GetUtility<ViewCtrlRegistry>().Count;
            var logs = new List<string>();
            Application.LogCallback logCapture = (condition, _, type) =>
            {
                if (type == LogType.Error)
                {
                    logs.Add(condition);
                }
            };

            Application.logMessageReceived += logCapture;
            try
            {
                LogAssert.Expect(LogType.Error, "批次动态掉落失败：批次提交失败：EnemyUnitId=2000 stage=commit error=掉落物注册失败：ItemId 必须大于 0");
                Game.Interface.SendEvent(new LootDropBatchRequestedEvent(
                    MapInstanceId,
                    2000,
                    Vector3.zero,
                    Quaternion.identity,
                    new[]
                    {
                        new LootDropBatchEntry("loot.good", 0, Vector3.zero),
                        new LootDropBatchEntry("loot.invalid-item", 0, Vector3.right)
                    }));
            }
            finally
            {
                Application.logMessageReceived -= logCapture;
            }

            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(beforeUnits));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(beforeLoot));
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.EqualTo(beforeViews));
            Assert.That(instances.ReleaseCount, Is.EqualTo(2));
            Assert.That(instances.LiveCount, Is.EqualTo(0));
            Assert.That(logs.Count(item => item.Contains("批次动态掉落失败")), Is.EqualTo(1));
            Assert.That(logs.Single(item => item.Contains("批次动态掉落失败")), Does.Contain("EnemyUnitId=2000"));
            Assert.That(logs.Single(item => item.Contains("批次动态掉落失败")), Does.Contain("stage=commit"));
            RegisterDropEnemy(3000, "enemy.batch", new Vector3(0f, 0f, 2f), configs.Enemies["enemy.batch"], 20);
            var batches = CaptureBatches();
            beforeUnits = Game.Interface.GetModel<UnitModel>().Count;
            beforeLoot = Game.Interface.GetModel<LootModel>().Loot.Count;
            beforeViews = Game.Interface.GetUtility<ViewCtrlRegistry>().Count;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*批次提交失败：EnemyUnitId=3000 stage=commit error=掉落物注册失败：ItemId 必须大于 0"));
            KillEnemy(3000);
            Game.Interface.SendEvent(new CombatantDiedEvent(3000, PlayerModel.LocalPlayerUnitId));

            Assert.That(batches.Count, Is.EqualTo(1));
            Assert.That(batches[0].SourceEnemyUnitId, Is.EqualTo(3000));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(beforeUnits));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(beforeLoot));
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.EqualTo(beforeViews));
            Assert.That(instances.LiveCount, Is.EqualTo(0));
        }

        [Test]
        public void CorpseSearchPresentationSurvivesCommittedMapReactivation()
        {
            var enemyPrefab = CreateEnemyPrefab("corpse-reactivation-enemy");
            objects.Add(enemyPrefab);
            ResourceLoader.SetLoaderForTests(new TestResourceLoader { Asset = enemyPrefab });
            var instances = new TestViewInstanceProvider();
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            AddFlatNavMesh(new Bounds(Vector3.zero, new Vector3(20f, 4f, 20f)));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
            RegisterPlayer(Vector3.zero);
            var ctrl = new EnemyCtrl(Game.Interface);

            Assert.That(ctrl.PrepareMapAsync(CreateContext(new[] { EnemySpawn(BasicEnemyId) }), default)
                .GetAwaiter()
                .GetResult()
                .Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(instances.Created.Count, Is.EqualTo(1));
            var enemyObject = instances.Created.Single();
            var view = enemyObject.GetComponent<EnemyView>();
            var enemyUnitId = Game.Interface.GetModel<UnitModel>().Units.Values.Single(unit => unit.UnitType == UnitType.Enemy).UnitId;

            KillEnemy(enemyUnitId);
            LifeCycle.Update();
            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(enemyUnitId, out _), Is.True);
            Assert.That(view.CanSearch, Is.True);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(enemyUnitId, out _), Is.False);
            Assert.That(Game.Interface.GetModel<CombatModel>().TryGetCombatant(enemyUnitId, out _), Is.False);

            ctrl.SetCurrentMapActive(false);
            Assert.That(enemyObject.activeSelf, Is.False);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(enemyUnitId, out var hidden), Is.True);
            Assert.That(hidden.IsActive, Is.False);

            ctrl.SetCurrentMapActive(true);

            Assert.That(enemyObject.activeSelf, Is.True);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(enemyUnitId, out var visible), Is.True);
            Assert.That(visible.IsActive, Is.True);
            Assert.That(view.CanSearch, Is.True);
            Assert.That(enemyObject.GetComponent<BoxCollider>().enabled, Is.False);
            Assert.That(enemyObject.GetComponent<NavMeshAgent>().enabled, Is.False);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(enemyUnitId, out _), Is.False);
            Assert.That(Game.Interface.GetModel<CombatModel>().TryGetCombatant(enemyUnitId, out _), Is.False);
            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(enemyUnitId, out _), Is.True);
        }

        [Test]
        public void FailedCandidateMapRestoresOldCorpseSearchPresentationThroughMapSceneSystem()
        {
            var enemyPrefab = CreateEnemyPrefab("corpse-map-rollback-enemy");
            objects.Add(enemyPrefab);
            var loader = new FailingUnloadMapLoader();
            var instances = new TestViewInstanceProvider();
            var configs = new MapRollbackConfigProvider();
            ResourceLoader.SetLoaderForTests(new TestResourceLoader { Asset = enemyPrefab });
            Game.Interface.RegisterUtility<IMapContentLoader>(loader);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configs);
            AddFlatNavMesh(new Bounds(Vector3.zero, new Vector3(20f, 4f, 20f)));

            var map = Game.Interface.GetSystem<MapSceneSystem>();
            var loaded = map.LoadMapAsync(MapIds.SurvivalSample).GetAwaiter().GetResult();
            Assert.That(loaded.Success, Is.True, loaded.Error);
            Assert.That(instances.Created.Count, Is.EqualTo(1));
            var enemyObject = instances.Created.Single();
            var view = enemyObject.GetComponent<EnemyView>();
            var enemyUnitId = Game.Interface.GetModel<UnitModel>().Units.Values.Single(unit => unit.UnitType == UnitType.Enemy).UnitId;
            RegisterPlayer(Vector3.zero);
            Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 60f);
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);

            KillEnemy(enemyUnitId);
            LifeCycle.Update();
            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(enemyUnitId, out _), Is.True);
            Assert.That(view.CanSearch, Is.True);

            loader.FailNextUnload = true;
            var failed = map.LoadMapAsync(MapIds.MissionSample).GetAwaiter().GetResult();

            Assert.That(failed.Success, Is.False);
            Assert.That(Game.Interface.GetModel<MapSceneModel>().CurrentMapId, Is.EqualTo(MapIds.SurvivalSample));
            Assert.That(enemyObject.activeSelf, Is.True);
            Assert.That(enemyObject.GetComponents<SphereCollider>().Single(collider => collider.isTrigger).enabled, Is.True);
            Assert.That(enemyObject.GetComponent<BoxCollider>().enabled, Is.False);
            Assert.That(enemyObject.GetComponent<NavMeshAgent>().enabled, Is.False);
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetIntent(enemyUnitId, out _), Is.False);
            Assert.That(Game.Interface.GetModel<CombatModel>().TryGetCombatant(enemyUnitId, out _), Is.False);
            Assert.That(Game.Interface.GetSystem<SearchContainerSystem>().TryGetSnapshot(enemyUnitId, out _), Is.True);
            Assert.That(view.CanSearch, Is.True);
            Assert.That(Game.Interface.GetModel<UnitModel>().Units.Values.Count(unit => unit.UnitType == UnitType.Enemy), Is.EqualTo(1));
            Assert.That(loader.UnloadFailures, Is.EqualTo(1));
            loader.DestroyAll();
        }


        [Test]
        public void DuplicateDeathDoesNotDuplicateBatch()
        {
            RegisterDropEnemy(1000, BasicEnemyId, Vector3.one, TestRuntimeConfigProvider.BasicConfig(), 20);
            var batches = CaptureCorpseRequests();
            KillEnemy(1000);
            Game.Interface.SendEvent(new CombatantDiedEvent(1000, PlayerModel.LocalPlayerUnitId));
            Assert.That(batches.Count, Is.EqualTo(1));
            Assert.That(batches[0].SourceEnemyUnitId, Is.EqualTo(1000));
        }

        [Test]
        public void EnemyPreparationPreloadsAllGuaranteedAndExtraLootOnce()
        {
            var loader = new TestResourceLoader { Asset = CreateLootPrefab("preload-loot-prefab") };
            var instances = new TestViewInstanceProvider();
            var configs = new DropConfigProvider();
            configs.Loots["loot.shared.weapon"] = new LootConfigDefinition("loot.shared.weapon", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Bow, 1);
            configs.Loots["loot.shared.ammo"] = new LootConfigDefinition("loot.shared.ammo", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Arrow, 1);
            configs.Loots["loot.shared.extra"] = new LootConfigDefinition("loot.shared.extra", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.MetalParts, 4);
            configs.Enemies["enemy.shared-loot"] = WithGuaranteedAmmoLootConfigId(
                WithGuaranteedWeaponLootConfigId(
                    WithLootConfigId(TestRuntimeConfigProvider.ArcherConfig(), "loot.shared.extra"),
                    "loot.shared.weapon"),
                "loot.shared.ammo");
            ResourceLoader.SetLoaderForTests(loader);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configs);
            var ctrl = new LootCtrl(Game.Interface);

            var result = Prepare(ctrl, CreateContext(new[] { EnemySpawn("enemy.shared-loot") }));
            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(1));
            Assert.That(instances.Created.Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<UnitModel>().Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Count, Is.EqualTo(0));
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            ctrl.ReleaseCurrentMap();
            Assert.That(loader.ReleaseCount, Is.EqualTo(0));
            ctrl.ReleaseAll();
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));

            var rollbackLoader = new TestResourceLoader { Asset = CreateLootPrefab("rollback-loot-prefab") };
            ResourceLoader.SetLoaderForTests(rollbackLoader);
            var rollbackConfigs = new DropConfigProvider();
            rollbackConfigs.Loots["loot.rollback.weapon"] = new LootConfigDefinition("loot.rollback.weapon", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Bow, 1);
            rollbackConfigs.Enemies["enemy.rollback"] = WithGuaranteedAmmoLootConfigId(
                WithGuaranteedWeaponLootConfigId(
                    WithLootConfigId(TestRuntimeConfigProvider.ArcherConfig(), string.Empty),
                    "loot.rollback.weapon"),
                "loot.missing");
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(rollbackConfigs);
            var rollbackCtrl = new LootCtrl(Game.Interface);

            var failed = Prepare(rollbackCtrl, CreateContext(new[] { EnemySpawn("enemy.rollback") }));
            Assert.That(failed.Success, Is.False);
            Assert.That(rollbackLoader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(1));
            Assert.That(rollbackLoader.ReleaseCount, Is.EqualTo(1));
            Assert.That(instances.Created.Count, Is.EqualTo(0));
        }

        [Test]
        public void CandidateMapReusesCurrentPrefabCacheThroughRealReleaseThenCommit()
        {
            var loader = new TestResourceLoader { Asset = CreateLootPrefab("cross-map-loot-prefab") };
            var instances = new TestViewInstanceProvider();
            var configs = new DropConfigProvider();
            configs.Loots["loot.current.weapon"] = new LootConfigDefinition("loot.current.weapon", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Bow, 1);
            configs.Loots["loot.current.extra"] = new LootConfigDefinition("loot.current.extra", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.MetalParts, 4);
            configs.Loots["loot.next.weapon"] = new LootConfigDefinition("loot.next.weapon", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.ShortSword, 1);
            configs.Loots["loot.next.extra"] = new LootConfigDefinition("loot.next.extra", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Cloth, 3);
            configs.Enemies["enemy.current"] = WithId(
                WithGuaranteedWeaponLootConfigId(
                    WithLootConfigId(TestRuntimeConfigProvider.BasicConfig(), "loot.current.extra"),
                    "loot.current.weapon"),
                "enemy.current");
            configs.Enemies["enemy.next"] = WithId(
                WithGuaranteedWeaponLootConfigId(
                    WithLootConfigId(TestRuntimeConfigProvider.BasicConfig(), "loot.next.extra"),
                    "loot.next.weapon"),
                "enemy.next");
            ResourceLoader.SetLoaderForTests(loader);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(instances);
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(configs);
            var ctrl = new LootCtrl(Game.Interface);

            Assert.That(Prepare(ctrl, CreateContext(new[] { EnemySpawn("enemy.current") })).Success, Is.True);
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);
            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(1));

            Assert.That(Prepare(ctrl, CreateContext(new[] { EnemySpawn("enemy.next") })).Success, Is.True);
            Assert.That(loader.LoadCount("Prefabs/SceneObjects/TestResourcePickup"), Is.EqualTo(1));

            ctrl.ReleaseCurrentMap();
            Assert.That(loader.ReleaseCount, Is.EqualTo(0));
            Assert.That(ctrl.CommitPreparedMap().Success, Is.True);

            Game.Interface.SendEvent(new LootDropBatchRequestedEvent(
                MapInstanceId,
                4000,
                Vector3.zero,
                Quaternion.identity,
                new[]
                {
                    new LootDropBatchEntry("loot.next.weapon", 0, Vector3.zero),
                    new LootDropBatchEntry("loot.next.extra", 0, Vector3.right)
                }));

            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Values.Select(item => item.ConfigId),
                Is.EquivalentTo(new[] { "loot.next.weapon", "loot.next.extra" }));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Values.Single(item => item.ConfigId == "loot.next.weapon").ItemId,
                Is.EqualTo(ItemIds.ShortSword));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Values.Single(item => item.ConfigId == "loot.next.extra").ItemId,
                Is.EqualTo(ItemIds.Cloth));
            Assert.That(Game.Interface.GetModel<LootModel>().Loot.Values.Single(item => item.ConfigId == "loot.next.extra").Amount,
                Is.EqualTo(3));
            Assert.That(instances.LiveCount, Is.EqualTo(2));

            ctrl.ReleaseAll();
            Assert.That(loader.ReleaseCount, Is.EqualTo(1));
        }

        private void FireOneShot(int unitId)
        {
            InvokeEnemyTick(unitId, 0f);
            InvokeEnemyTick(unitId, 1f);
            if (Game.Interface.GetSystem<EnemySystem>().TryGetReloadProgress(unitId, out _, out _))
            {
                InvokeEnemyTick(unitId, 1f);
            }
        }

        private void AssertRemainingAmmo(int unitId, int expected)
        {
            Assert.That(Game.Interface.GetSystem<EnemySystem>().TryGetRemainingAmmo(unitId, out var ammo), Is.True);
            Assert.That(ammo, Is.EqualTo(expected));
        }

        private static void AssertLoot(IRuntimeConfigProvider provider, string lootId, int itemId, int amount)
        {
            Assert.That(provider.TryGetLoot(lootId, out var loot, out var error), Is.True, error);
            Assert.That(loot.PrefabAddress, Is.EqualTo("Prefabs/SceneObjects/TestResourcePickup"));
            Assert.That(loot.ItemId, Is.EqualTo(itemId));
            Assert.That(loot.Amount, Is.EqualTo(amount));
        }

        private static void AssertInvalidEnemyConfig(
            Action<MemoryConfigTable> mutate,
            string table,
            string rowId,
            string field)
        {
            var data = MemoryConfigTable.Valid();
            mutate(data);
            var provider = new LubanRuntimeConfigProvider(() => data.ToTables());
            Assert.That(provider.TryGetEnemy(ArcherEnemyId, out _, out var error), Is.False);
            Assert.That(error, Does.Contain(table));
            Assert.That(error, Does.Contain(rowId));
            Assert.That(error, Does.Contain(field));
        }

        private static void AssertBatch(LootDropBatchRequestedEvent batch, int sourceEnemyUnitId, params string[] ids)
        {
            Assert.That(batch.SourceEnemyUnitId, Is.EqualTo(sourceEnemyUnitId));
            Assert.That(batch.Entries.Select(item => item.LootConfigId), Is.EqualTo(ids));
            for (var i = 0; i < batch.Entries.Count; i++)
            {
                var expectedX = (i - (batch.Entries.Count - 1) / 2f) * 0.6f;
                Assert.That(batch.Entries[i].Offset.x, Is.EqualTo(expectedX).Within(0.001f));
                Assert.That(batch.Entries[i].Offset.y, Is.EqualTo(0f).Within(0.001f));
                Assert.That(batch.Entries[i].Offset.z, Is.EqualTo(0f).Within(0.001f));
            }
        }

        private static void AssertBatch(EnemyCorpseContentsRequestedEvent batch, int sourceEnemyUnitId, params string[] ids)
        {
            Assert.That(batch.SourceEnemyUnitId, Is.EqualTo(sourceEnemyUnitId));
            Assert.That(batch.Entries.Select(item => item.LootConfigId), Is.EqualTo(ids));
            for (var i = 0; i < batch.Entries.Count; i++)
            {
                var expectedX = (i - (batch.Entries.Count - 1) / 2f) * 0.6f;
                Assert.That(batch.Entries[i].Offset.x, Is.EqualTo(expectedX).Within(0.001f));
                Assert.That(batch.Entries[i].Offset.y, Is.EqualTo(0f).Within(0.001f));
                Assert.That(batch.Entries[i].Offset.z, Is.EqualTo(0f).Within(0.001f));
            }
        }

        private static MapControllerResult Prepare(LootCtrl ctrl, MapPreparationContext context)
        {
            return ctrl.PrepareMapAsync(context, default).GetAwaiter().GetResult();
        }

        private static MapPreparationContext CreateContext(IReadOnlyList<MapSpawnDefinition> spawns)
        {
            var root = new GameObject("map-root");
            var runtime = new GameObject("DynamicObjectRoot").transform;
            runtime.SetParent(root.transform, false);
            var map = new GameObject("MapContext");
            var context = map.AddComponent<MapContext>();
            SetField(context, "mapContentRoot", root);
            SetField(context, "dynamicObjectRoot", runtime);
            return new MapPreparationContext(MapIds.SurvivalSample, MapInstanceId, context, spawns);
        }

        private static MapSpawnDefinition EnemySpawn(string configId)
        {
            return new MapSpawnDefinition("enemy-" + configId, MapIds.SurvivalSample, MapSpawnObjectType.Enemy, configId, 0f, 0f, 0f, 0f);
        }

        private static GameObject CreateLootPrefab(string name)
        {
            var prefab = new GameObject(name, typeof(BoxCollider), typeof(ResourcePickupView));
            prefab.GetComponent<BoxCollider>().isTrigger = true;
            return prefab;
        }

        private static GameObject CreateEnemyPrefab(string name)
        {
            var prefab = new GameObject(name, typeof(BoxCollider), typeof(NavMeshAgent), typeof(EnemyView));
            var view = prefab.GetComponent<EnemyView>();
            var combatCollider = prefab.GetComponent<BoxCollider>();
            var corpseTrigger = prefab.AddComponent<SphereCollider>();
            corpseTrigger.enabled = false;
            corpseTrigger.isTrigger = true;
            SetField(view, "combatCollider", combatCollider);
            SetField(view, "corpseSearchTrigger", corpseTrigger);
            var emitter = prefab.AddComponent<DamageNumberEmitter>();
            SetField(emitter, "numberPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/WorldHud/DamageNumberBox.prefab").GetComponent<DamageNumberView>());
            SetField(view, "damageNumberEmitter", emitter);
            return prefab;
        }

        private void AddFlatNavMesh(Bounds bounds)
        {
            if (navMeshDataInstance.valid)
            {
                navMeshDataInstance.Remove();
            }

            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one),
                    size = new Vector3(bounds.size.x, 0.1f, bounds.size.z),
                    area = 0
                }
            };
            var data = NavMeshBuilder.BuildNavMeshData(
                NavMesh.GetSettingsByID(0),
                sources,
                bounds,
                Vector3.zero,
                Quaternion.identity);
            navMeshDataInstance = NavMesh.AddNavMeshData(data);
        }

        private static void SetField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private List<LootDropBatchRequestedEvent> CaptureBatches()
        {
            var batches = new List<LootDropBatchRequestedEvent>();
            Game.Interface.RegisterEvent<LootDropBatchRequestedEvent>(evt => batches.Add(evt));
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
            return batches;
        }

        private List<EnemyCorpseContentsRequestedEvent> CaptureCorpseRequests()
        {
            var requests = new List<EnemyCorpseContentsRequestedEvent>();
            Game.Interface.RegisterEvent<EnemyCorpseContentsRequestedEvent>(evt => requests.Add(evt));
            Assert.That(Game.Interface.GetSystem<InventorySystem>()
                .TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out var error), Is.True, error);
            return requests;
        }

        private void RegisterDropEnemy(int unitId, string configId, Vector3 position, EnemyConfigDefinition config, int hp)
        {
            RegisterPlayer(Vector3.zero);
            RegisterEnemy(unitId, position, config, hp);
        }

        private void KillEnemy(int unitId)
        {
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId), Is.True);
            Assert.That(Game.Interface.GetSystem<CombatSystem>().TryDamage(PlayerModel.LocalPlayerUnitId, unitId), Is.True);
        }

        private static ProjectileSpawnRequest CreateSpawnRequest()
        {
            return CreateSpawnRequest(PlayerModel.LocalPlayerUnitId, CombatFaction.Player);
        }

        private static ProjectileSpawnRequest CreateSpawnRequest(int attackerUnitId, CombatFaction faction)
        {
            return new ProjectileSpawnRequest(
                attackerUnitId,
                faction,
                MapInstanceId,
                10,
                Vector3.zero,
                Vector3.forward,
                "projectile.arrow.basic");
        }

        private void RegisterPlayer(Vector3 position)
        {
            Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                PlayerModel.LocalPlayerUnitId,
                MapInstanceId,
                UnitType.Player,
                UnitSource.Player,
                "player",
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(
                PlayerModel.LocalPlayerUnitId,
                CombatFaction.Player,
                100,
                10,
                0);
        }

        private static void MovePlayer(Vector3 position)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>()
                .TryUpdateTransform(PlayerModel.LocalPlayerUnitId, position, Quaternion.identity), Is.True);
        }

        private static void RegisterEnemy(int unitId, Vector3 position, EnemyConfigDefinition config, int health)
        {
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(new UnitSnapshot(
                unitId,
                MapInstanceId,
                UnitType.Enemy,
                UnitSource.MapPlaced,
                config.Id,
                position,
                Quaternion.identity,
                UnitAuthorityMode.Local,
                0,
                true), out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(
                unitId,
                CombatFaction.Enemy,
                health,
                config.AttackDamage,
                PlayerModel.LocalPlayerUnitId);
        }

        private Collider CreateCollider(string name, bool trigger)
        {
            var go = new GameObject(name);
            objects.Add(go);
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = trigger;
            return collider;
        }

        private static void InvokeEnemyTick(int unitId, float deltaTime)
        {
            var system = Game.Interface.GetSystem<EnemySystem>();
            var enemies = typeof(EnemySystem)
                .GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(system);
            var state = enemies.GetType().GetProperty("Item")?.GetValue(enemies, new object[] { unitId });
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player), Is.True);
            typeof(EnemySystem)
                .GetMethod("TickEnemy", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(system, new object[] { unitId, state, player, deltaTime });
        }

        private static void SetEnemyCooldown(int unitId, float value)
        {
            var system = Game.Interface.GetSystem<EnemySystem>();
            var field = typeof(EnemySystem).GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic);
            var enemies = field?.GetValue(system);
            var state = enemies.GetType().GetProperty("Item")?.GetValue(enemies, new object[] { unitId });
            state.GetType().GetField("AttackCooldownRemaining", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(state, value);
            enemies.GetType().GetProperty("Item")?.SetValue(enemies, state, new object[] { unitId });
        }

        private static void InvokeProjectileAdvance(int projectileRuntimeId, float deltaTime)
        {
            var system = Game.Interface.GetSystem<ProjectileSystem>();
            var model = Game.Interface.GetModel<ProjectileModel>();
            Assert.That(model.Projectiles.TryGetValue(projectileRuntimeId, out var state), Is.True);
            typeof(ProjectileSystem)
                .GetMethod("Advance", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(system, new object[] { state, deltaTime });
        }

        private static LubanRuntimeConfigProvider CreateRealConfigProvider()
        {
            return new LubanRuntimeConfigProvider(() =>
                new Tables(table => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{table}.bytes")))));
        }

        private static float GetEnemyCooldown(int unitId)
        {
            var system = Game.Interface.GetSystem<EnemySystem>();
            var enemies = typeof(EnemySystem).GetField("enemies", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(system);
            var state = enemies.GetType().GetProperty("Item")?.GetValue(enemies, new object[] { unitId });
            return (float)state.GetType().GetField("AttackCooldownRemaining", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(state);
        }

        private static EnemyConfigDefinition WithId(EnemyConfigDefinition source, string value)
        {
            return new EnemyConfigDefinition(
                value,
                source.PrefabAddress,
                source.MaxHP,
                source.AttackDamage,
                source.AggroRange,
                source.AttackRange,
                source.AttackCooldownSeconds,
                source.MoveSpeed,
                source.LootConfigId,
                source.AttackKind,
                source.RetreatDistance,
                source.TelegraphSeconds,
                source.ProjectileConfigId,
                source.GuaranteedWeaponLootConfigId,
                source.GuaranteedAmmoLootConfigId,
                source.AmmoCapacity,
                source.FallbackMeleeAttackRange,
                source.MagazineCapacity,
                source.ReloadSeconds);
        }

        private static EnemyConfigDefinition WithLootConfigId(EnemyConfigDefinition source, string value)
        {
            return new EnemyConfigDefinition(
                source.Id,
                source.PrefabAddress,
                source.MaxHP,
                source.AttackDamage,
                source.AggroRange,
                source.AttackRange,
                source.AttackCooldownSeconds,
                source.MoveSpeed,
                value,
                source.AttackKind,
                source.RetreatDistance,
                source.TelegraphSeconds,
                source.ProjectileConfigId,
                source.GuaranteedWeaponLootConfigId,
                source.GuaranteedAmmoLootConfigId,
                source.AmmoCapacity,
                source.FallbackMeleeAttackRange,
                source.MagazineCapacity,
                source.ReloadSeconds);
        }

        private static EnemyConfigDefinition WithGuaranteedWeaponLootConfigId(EnemyConfigDefinition source, string value)
        {
            return new EnemyConfigDefinition(
                source.Id,
                source.PrefabAddress,
                source.MaxHP,
                source.AttackDamage,
                source.AggroRange,
                source.AttackRange,
                source.AttackCooldownSeconds,
                source.MoveSpeed,
                source.LootConfigId,
                source.AttackKind,
                source.RetreatDistance,
                source.TelegraphSeconds,
                source.ProjectileConfigId,
                value,
                source.GuaranteedAmmoLootConfigId,
                source.AmmoCapacity,
                source.FallbackMeleeAttackRange,
                source.MagazineCapacity,
                source.ReloadSeconds);
        }

        private static EnemyConfigDefinition WithGuaranteedAmmoLootConfigId(EnemyConfigDefinition source, string value)
        {
            return new EnemyConfigDefinition(
                source.Id,
                source.PrefabAddress,
                source.MaxHP,
                source.AttackDamage,
                source.AggroRange,
                source.AttackRange,
                source.AttackCooldownSeconds,
                source.MoveSpeed,
                source.LootConfigId,
                source.AttackKind,
                source.RetreatDistance,
                source.TelegraphSeconds,
                source.ProjectileConfigId,
                source.GuaranteedWeaponLootConfigId,
                value,
                source.AmmoCapacity,
                source.FallbackMeleeAttackRange,
                source.MagazineCapacity,
                source.ReloadSeconds);
        }

        private static EnemyConfigDefinition WithAmmoCapacity(EnemyConfigDefinition source, int value)
        {
            return new EnemyConfigDefinition(
                source.Id,
                source.PrefabAddress,
                source.MaxHP,
                source.AttackDamage,
                source.AggroRange,
                source.AttackRange,
                source.AttackCooldownSeconds,
                source.MoveSpeed,
                source.LootConfigId,
                source.AttackKind,
                source.RetreatDistance,
                source.TelegraphSeconds,
                source.ProjectileConfigId,
                source.GuaranteedWeaponLootConfigId,
                source.GuaranteedAmmoLootConfigId,
                value,
                source.FallbackMeleeAttackRange,
                source.MagazineCapacity,
                source.ReloadSeconds);
        }

        private static EnemyConfigDefinition WithRetreatDistance(EnemyConfigDefinition source, float value)
        {
            return new EnemyConfigDefinition(
                source.Id,
                source.PrefabAddress,
                source.MaxHP,
                source.AttackDamage,
                source.AggroRange,
                source.AttackRange,
                source.AttackCooldownSeconds,
                source.MoveSpeed,
                source.LootConfigId,
                source.AttackKind,
                value,
                source.TelegraphSeconds,
                source.ProjectileConfigId,
                source.GuaranteedWeaponLootConfigId,
                source.GuaranteedAmmoLootConfigId,
                source.AmmoCapacity,
                source.FallbackMeleeAttackRange,
                source.MagazineCapacity,
                source.ReloadSeconds);
        }

        private static EnemyConfigDefinition WithFallbackMeleeAttackRange(EnemyConfigDefinition source, float value)
        {
            return new EnemyConfigDefinition(
                source.Id,
                source.PrefabAddress,
                source.MaxHP,
                source.AttackDamage,
                source.AggroRange,
                source.AttackRange,
                source.AttackCooldownSeconds,
                source.MoveSpeed,
                source.LootConfigId,
                source.AttackKind,
                source.RetreatDistance,
                source.TelegraphSeconds,
                source.ProjectileConfigId,
                source.GuaranteedWeaponLootConfigId,
                source.GuaranteedAmmoLootConfigId,
                source.AmmoCapacity,
                value,
                source.MagazineCapacity,
                source.ReloadSeconds);
        }

        private static LootConfigDefinition WithAmount(LootConfigDefinition source, int amount)
        {
            return new LootConfigDefinition(source.Id, source.PrefabAddress, source.ItemId, amount);
        }

        private static LootConfigDefinition WithItemId(LootConfigDefinition source, int itemId)
        {
            return new LootConfigDefinition(source.Id, source.PrefabAddress, itemId, source.Amount);
        }

        private sealed class MemoryConfigTable
        {
            public readonly Dictionary<int, ItemDefinition> Items = new Dictionary<int, ItemDefinition>();
            public readonly Dictionary<int, WeaponConfigDefinition> Weapons = new Dictionary<int, WeaponConfigDefinition>();
            public readonly Dictionary<string, ProjectileConfigDefinition> Projectiles = new Dictionary<string, ProjectileConfigDefinition>();
            public readonly Dictionary<string, MapAssetDefinition> Maps = new Dictionary<string, MapAssetDefinition>();
            public readonly List<MapSpawnDefinition> Spawns = new List<MapSpawnDefinition>();
            public readonly Dictionary<string, PlayerConfigDefinition> Players = new Dictionary<string, PlayerConfigDefinition>();
            public readonly Dictionary<string, EnemyConfigDefinition> Enemies = new Dictionary<string, EnemyConfigDefinition>();
            public readonly Dictionary<string, LootConfigDefinition> Loots = new Dictionary<string, LootConfigDefinition>();
            public readonly Dictionary<string, ExtractionConfigDefinition> Extractions = new Dictionary<string, ExtractionConfigDefinition>();

            public static MemoryConfigTable Valid()
            {
                var table = new MemoryConfigTable();
                table.Items[ItemIds.MetalParts] = Item(ItemIds.MetalParts, ItemType.Material, 20, ItemUseEffect.None, 0);
                table.Items[ItemIds.Cloth] = Item(ItemIds.Cloth, ItemType.Material, 20, ItemUseEffect.None, 0);
                table.Items[ItemIds.Medkit] = Item(ItemIds.Medkit, ItemType.Consumable, 5, ItemUseEffect.RestoreHealth, 30);
                table.Items[ItemIds.Ration] = Item(ItemIds.Ration, ItemType.Consumable, 5, ItemUseEffect.RestorePower, 40);
                table.Items[ItemIds.ShortSword] = Item(ItemIds.ShortSword, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                table.Items[ItemIds.Bow] = Item(ItemIds.Bow, ItemType.Weapon, 1, ItemUseEffect.None, 0);
                table.Items[ItemIds.Arrow] = Item(ItemIds.Arrow, ItemType.Ammunition, 20, ItemUseEffect.None, 0);
                table.Projectiles["projectile.arrow.basic"] = new ProjectileConfigDefinition("projectile.arrow.basic", "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                table.Weapons[ItemIds.ShortSword] = new WeaponConfigDefinition(ItemIds.ShortSword, WeaponKind.Melee, 10, 1f, 1.35f, 120f, string.Empty, 0);
                table.Weapons[ItemIds.Bow] = new WeaponConfigDefinition(ItemIds.Bow, WeaponKind.Projectile, 8, 1.8f, 8f, 0f, "projectile.arrow.basic", ItemIds.Arrow);
                table.Maps[MapIds.SurvivalSample] = new MapAssetDefinition(MapIds.SurvivalSample, "Scenes/SurvivalSampleMap");
                table.Players["player"] = new PlayerConfigDefinition("player", "Prefabs/Characters/GrayboxPlayer", 100, 10, 1f, 1f, 3f, 360f, 5f);
                table.Spawns.Add(new MapSpawnDefinition("player", MapIds.SurvivalSample, MapSpawnObjectType.Player, "player", 0f, 0f, 0f, 0f));
                table.Loots["loot.metal-parts"] = new LootConfigDefinition("loot.metal-parts", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.MetalParts, 4);
                table.Loots["loot.weapon.short-sword"] = new LootConfigDefinition("loot.weapon.short-sword", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.ShortSword, 1);
                table.Loots["loot.weapon.bow"] = new LootConfigDefinition("loot.weapon.bow", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Bow, 1);
                table.Loots["loot.ammunition.arrow"] = new LootConfigDefinition("loot.ammunition.arrow", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Arrow, 1);
                table.Enemies[BasicEnemyId] = TestRuntimeConfigProvider.BasicConfig();
                table.Enemies[ArcherEnemyId] = TestRuntimeConfigProvider.ArcherConfig();
                table.Extractions["extraction.test"] = new ExtractionConfigDefinition("extraction.test", "Prefabs/SceneObjects/TaskExit");
                return table;
            }

            public Tables ToTables()
            {
                return new Tables(name => new ByteBuf(ToBytes(name)));
            }

            private byte[] ToBytes(string name)
            {
                var buf = new ByteBuf();
                switch (name)
                {
                    case "tbitem":
                        buf.WriteSize(Items.Count);
                        foreach (var item in Items.Values) WriteItem(buf, item);
                        break;
                    case "tbweapon":
                        buf.WriteSize(Weapons.Count);
                        foreach (var weapon in Weapons.Values) WriteWeapon(buf, weapon);
                        break;
                    case "tbprojectile":
                        buf.WriteSize(Projectiles.Count);
                        foreach (var projectile in Projectiles.Values) WriteProjectile(buf, projectile);
                        break;
                    case "tbmap":
                        buf.WriteSize(Maps.Count);
                        foreach (var map in Maps.Values) WriteMap(buf, map);
                        break;
                    case "tbmapspawn":
                        buf.WriteSize(Spawns.Count);
                        foreach (var spawn in Spawns) WriteSpawn(buf, spawn);
                        break;
                    case "tbplayer":
                        buf.WriteSize(Players.Count);
                        foreach (var player in Players.Values) WritePlayer(buf, player);
                        break;
                    case "tbenemy":
                        buf.WriteSize(Enemies.Count);
                        foreach (var enemy in Enemies.Values) WriteEnemy(buf, enemy);
                        break;
                    case "tbloot":
                        buf.WriteSize(Loots.Count);
                        foreach (var loot in Loots.Values) WriteLoot(buf, loot);
                        break;
                    case "tbextraction":
                        buf.WriteSize(Extractions.Count);
                        foreach (var extraction in Extractions.Values) WriteExtraction(buf, extraction);
                        break;
                    default:
                        buf.WriteSize(0);
                        break;
                }

                return buf.CopyData();
            }

            private static ItemDefinition Item(int id, ItemType type, int maxStack, ItemUseEffect effect, int value)
            {
                return new ItemDefinition(id, id.ToString(), id.ToString(), id + ".name", id + ".desc", string.Empty, type, maxStack, effect, value);
            }

            private static void WriteItem(ByteBuf buf, ItemDefinition item)
            {
                buf.WriteInt(item.Id);
                buf.WriteString(item.Name);
                buf.WriteString(item.Desc);
                buf.WriteString(item.NameKey);
                buf.WriteString(item.DescKey);
                buf.WriteString(item.IconAddress);
                buf.WriteInt((int)item.ItemType);
                buf.WriteInt(item.MaxStackAmount);
                buf.WriteInt((int)item.UseEffect);
                buf.WriteInt(item.UseValue);
            }

            private static void WriteWeapon(ByteBuf buf, WeaponConfigDefinition weapon)
            {
                buf.WriteInt(weapon.ItemId);
                buf.WriteInt((int)weapon.WeaponKind);
                buf.WriteInt(weapon.AttackDamage);
                buf.WriteFloat(weapon.AttackCooldownSeconds);
                buf.WriteFloat(weapon.AttackRange);
                buf.WriteFloat(weapon.MeleeArcDegrees);
                buf.WriteString(weapon.ProjectileConfigId);
                buf.WriteInt(weapon.AmmoItemId);
            }

            private static void WriteProjectile(ByteBuf buf, ProjectileConfigDefinition projectile)
            {
                buf.WriteString(projectile.Id);
                buf.WriteString(projectile.PrefabAddress);
                buf.WriteFloat(projectile.MoveSpeed);
                buf.WriteFloat(projectile.MaxTravelDistance);
            }

            private static void WriteMap(ByteBuf buf, MapAssetDefinition map)
            {
                buf.WriteString(map.Id);
                buf.WriteString(map.SceneAddress);
            }

            private static void WriteSpawn(ByteBuf buf, MapSpawnDefinition spawn)
            {
                buf.WriteString(spawn.Id);
                buf.WriteString(spawn.MapId);
                buf.WriteInt((int)spawn.ObjectType);
                buf.WriteString(spawn.ConfigId);
                buf.WriteFloat(spawn.LocalPosition.x);
                buf.WriteFloat(spawn.LocalPosition.y);
                buf.WriteFloat(spawn.LocalPosition.z);
                buf.WriteFloat(spawn.LocalRotation.eulerAngles.y);
            }

            private static void WritePlayer(ByteBuf buf, PlayerConfigDefinition player)
            {
                buf.WriteString(player.Id);
                buf.WriteString(player.PrefabAddress);
                buf.WriteInt(player.MaxHP);
                buf.WriteInt(player.AttackDamage);
                buf.WriteFloat(player.AttackRange);
                buf.WriteFloat(player.AttackCooldownSeconds);
                buf.WriteFloat(player.MoveSpeed);
                buf.WriteFloat(player.TurnSpeed);
                buf.WriteFloat(player.BaseRecoveryPerSecond);
            }

            private static void WriteEnemy(ByteBuf buf, EnemyConfigDefinition enemy)
            {
                buf.WriteString(enemy.Id);
                buf.WriteString(enemy.PrefabAddress);
                buf.WriteInt(enemy.MaxHP);
                buf.WriteInt(enemy.AttackDamage);
                buf.WriteFloat(enemy.AggroRange);
                buf.WriteFloat(enemy.AttackRange);
                buf.WriteFloat(enemy.AttackCooldownSeconds);
                buf.WriteFloat(enemy.MoveSpeed);
                buf.WriteString(enemy.LootConfigId);
                buf.WriteInt((int)enemy.AttackKind);
                buf.WriteFloat(enemy.RetreatDistance);
                buf.WriteFloat(enemy.TelegraphSeconds);
                buf.WriteString(enemy.ProjectileConfigId);
                buf.WriteString(enemy.GuaranteedWeaponLootConfigId);
                buf.WriteString(enemy.GuaranteedAmmoLootConfigId);
                buf.WriteInt(enemy.AmmoCapacity);
                buf.WriteFloat(enemy.FallbackMeleeAttackRange);
            }

            private static void WriteLoot(ByteBuf buf, LootConfigDefinition loot)
            {
                buf.WriteString(loot.Id);
                buf.WriteString(loot.PrefabAddress);
                buf.WriteInt(loot.ItemId);
                buf.WriteInt(loot.Amount);
            }

            private static void WriteExtraction(ByteBuf buf, ExtractionConfigDefinition extraction)
            {
                buf.WriteString(extraction.Id);
                buf.WriteString(extraction.PrefabAddress);
            }
        }

        private sealed class DropConfigProvider : IRuntimeConfigProvider
        {
            public readonly Dictionary<string, EnemyConfigDefinition> Enemies =
                new Dictionary<string, EnemyConfigDefinition>();

            public readonly Dictionary<string, LootConfigDefinition> Loots =
                new Dictionary<string, LootConfigDefinition>();

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                var type = id == ItemIds.Arrow ? ItemType.Ammunition :
                    id == ItemIds.MetalParts ? ItemType.Material : ItemType.Weapon;
                value = new ItemDefinition(id, id.ToString(), id.ToString(), id + ".name", id + ".desc", string.Empty, type, 20, ItemUseEffect.None, 0);
                error = null;
                return id > 0;
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                error = null;
                return id == "projectile.arrow.basic";
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                if (Enemies.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                if (Loots.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                value = default;
                error = "missing loot";
                return false;
            }

            public bool TryGetCraftRecipes(
                out IReadOnlyList<CraftRecipeDefinition> values,
                out string error)
            {
                values = Array.Empty<CraftRecipeDefinition>();
                error = null;
                return true;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            private readonly Dictionary<string, int> loads = new Dictionary<string, int>();
            public GameObject Asset { get; set; }
            public int ReleaseCount { get; private set; }

            public T LoadAsset<T>(string path) where T : Object
            {
                return Asset as T;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                loads[path] = LoadCount(path) + 1;
                var handle = new ResourceLoadHandle(new object());
                onLoaded?.Invoke(Asset as T);
                return handle;
            }

            public void Release(ResourceLoadHandle handle)
            {
                if (handle.IsValid)
                {
                    ReleaseCount++;
                }
            }

            public int LoadCount(string path)
            {
                return loads.TryGetValue(path, out var count) ? count : 0;
            }
        }

        private sealed class MapRollbackConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                value = new ItemDefinition(id, id.ToString(), id.ToString(), id + ".name", id + ".desc", string.Empty,
                    id == ItemIds.MetalParts ? ItemType.Material : ItemType.Weapon,
                    id == ItemIds.MetalParts ? 20 : 1,
                    ItemUseEffect.None,
                    0);
                error = null;
                return id == ItemIds.MetalParts || id == ItemIds.ShortSword;
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                value = itemId == ItemIds.ShortSword
                    ? new WeaponConfigDefinition(itemId, WeaponKind.Melee, 10, 1f, 1.35f, 120f, string.Empty, 0)
                    : default;
                error = value.ItemId > 0 ? null : "missing";
                return value.ItemId > 0;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, "Scenes/" + id);
                error = null;
                return id == MapIds.SurvivalSample || id == MapIds.MissionSample;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = mapId == MapIds.SurvivalSample
                    ? new[] { new MapSpawnDefinition("enemy.rollback", mapId, MapSpawnObjectType.Enemy, BasicEnemyId, 0f, 0f, 0f, 0f) }
                    : Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = TestRuntimeConfigProvider.BasicConfig();
                error = null;
                return id == BasicEnemyId;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = id == "loot.weapon.short-sword"
                    ? new LootConfigDefinition(id, string.Empty, ItemIds.ShortSword, 1)
                    : id == "loot.metal-parts"
                        ? new LootConfigDefinition(id, string.Empty, ItemIds.MetalParts, 4)
                        : default;
                error = value.ItemId > 0 ? null : "missing";
                return value.ItemId > 0;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }
        }

        private sealed class FailingUnloadMapLoader : IMapContentLoader
        {
            public bool FailNextUnload { get; set; }
            public int UnloadFailures { get; private set; }
            private readonly List<FakeMapHandle> handles = new List<FakeMapHandle>();

            public Cysharp.Threading.Tasks.UniTask<MapContentLoadResult> LoadAsync(
                string mapId,
                string address,
                Action<float> onProgress,
                System.Threading.CancellationToken cancellationToken = default)
            {
                onProgress?.Invoke(1f);
                var handle = new FakeMapHandle(mapId, CreateContext(mapId));
                handles.Add(handle);
                return Cysharp.Threading.Tasks.UniTask.FromResult(MapContentLoadResult.Ok(handle));
            }

            public Cysharp.Threading.Tasks.UniTask<MapContentUnloadResult> UnloadAsync(
                IMapContentHandle handle,
                System.Threading.CancellationToken cancellationToken = default)
            {
                if (FailNextUnload)
                {
                    FailNextUnload = false;
                    UnloadFailures++;
                    return Cysharp.Threading.Tasks.UniTask.FromResult(MapContentUnloadResult.Fail("unload failed"));
                }

                if (handle is FakeMapHandle fake)
                {
                    fake.Valid = false;
                    Object.DestroyImmediate(fake.Root);
                }

                return Cysharp.Threading.Tasks.UniTask.FromResult(MapContentUnloadResult.Ok());
            }

            public void DestroyAll()
            {
                foreach (var handle in handles)
                {
                    if (handle.Root != null)
                    {
                        Object.DestroyImmediate(handle.Root);
                    }
                }

                handles.Clear();
            }

            private static MapContext CreateContext(string mapId)
            {
                var root = new GameObject("RollbackMap-" + mapId);
                var mapContentRoot = new GameObject("MapContentRoot");
                mapContentRoot.transform.SetParent(root.transform, false);
                var runtimeRoot = new GameObject("DynamicObjectRoot");
                runtimeRoot.transform.SetParent(mapContentRoot.transform, false);
                mapContentRoot.SetActive(false);
                var context = root.AddComponent<MapContext>();
                SetField(context, "mapContentRoot", mapContentRoot);
                SetField(context, "dynamicObjectRoot", runtimeRoot.transform);
                return context;
            }
        }

        private sealed class FakeMapHandle : IMapContentHandle
        {
            public FakeMapHandle(string mapId, MapContext context)
            {
                MapId = mapId;
                Context = context;
                Root = context.gameObject;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public GameObject Root { get; }
            public bool Valid { get; set; } = true;
            public bool IsValid => Valid && Root != null;
        }

        private sealed class TestViewInstanceProvider : IViewInstanceProvider
        {
            private readonly HashSet<GameObject> live = new HashSet<GameObject>();
            public readonly List<GameObject> Created = new List<GameObject>();
            public int ReleaseCount { get; private set; }
            public int LiveCount => live.Count;

            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                var instance = Object.Instantiate(prefab.gameObject, position, rotation, parent);
                Created.Add(instance);
                live.Add(instance);
                return instance.GetComponent<T>();
            }

            public void Release(GameObject instance)
            {
                if (instance == null)
                {
                    return;
                }

                ReleaseCount++;
                live.Remove(instance);
                Object.DestroyImmediate(instance);
            }
        }

        private sealed class FakeProjectilePort : IProjectileRuntimePortProvider
        {
            public readonly List<ProjectileMovementHit> Hits = new List<ProjectileMovementHit>();
            public readonly List<ProjectileEndReason> EndedReasons = new List<ProjectileEndReason>();
            public bool CreateSucceeds { get; set; } = true;
            public int CreateCount { get; private set; }
            public int MoveCount { get; private set; }

            public bool TryCreateProjectile(int projectileRuntimeId, long mapInstanceId, Vector3 position, Vector3 direction, string prefabAddress, out string error)
            {
                CreateCount++;
                error = CreateSucceeds ? null : "blocked";
                return CreateSucceeds;
            }

            public void CollectMovementHits(int projectileRuntimeId, Vector3 from, Vector3 to, List<ProjectileMovementHit> results)
            {
                results.Clear();
                results.AddRange(Hits.OrderBy(item => item.Distance));
            }

            public void MoveProjectile(int projectileRuntimeId, Vector3 position, Vector3 direction)
            {
                MoveCount++;
            }

            public void ReleaseProjectile(int projectileRuntimeId) { }
        }

        private sealed class FakePlayerPortProvider : IPlayerRuntimePortProvider
        {
            public Collider PlayerCollider { get; set; }

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            {
                port = null;
                return false;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            {
                port = null;
                return false;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
            {
                return unitId == PlayerModel.LocalPlayerUnitId &&
                       mapInstanceId == MapInstanceId &&
                       collider != null &&
                       collider == PlayerCollider;
            }
        }

        private sealed class FakeEnemyResolver : IEnemyColliderResolver
        {
            private readonly Dictionary<Collider, int> map = new Dictionary<Collider, int>();

            public void Bind(Collider collider, int unitId)
            {
                map[collider] = unitId;
            }

            public bool TryResolveEnemyUnitId(Collider collider, out int unitId)
            {
                return map.TryGetValue(collider, out unitId);
            }
        }

        private sealed class TestEnemyPort : IEnemyNavigationPort
        {
            public TestEnemyPort(int unitId, long mapInstanceId, Vector3 position)
            {
                UnitId = unitId;
                MapInstanceId = mapInstanceId;
                Position = position;
            }

            public int UnitId { get; }
            public long MapInstanceId { get; }
            public bool IsValid { get; } = true;
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; } = Quaternion.identity;
            public bool TelegraphVisible { get; set; }
            public bool IsOnNavigableSurface(float sampleRadius) => true;
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Position;
                return true;
            }

            public bool TrySetRetreatDestination(Vector3 requestedPosition, float sampleRadius, out Vector3 destination)
            {
                destination = requestedPosition;
                return false;
            }

            public void PlayAttackAnimation() { }
            public void SetTelegraphVisible(bool visible) => TelegraphVisible = visible;
            public void SetDestination(Vector3 position) => Position = position;
            public void Stop() { }
        }

        private sealed class TestRuntimeConfigProvider : IRuntimeConfigProvider
        {
            public static EnemyConfigDefinition BasicConfig()
            {
                return new EnemyConfigDefinition(
                    BasicEnemyId,
                    "Prefabs/Characters/GrayboxEnemy",
                    20,
                    10,
                    8f,
                    1.35f,
                    1f,
                    2.2f,
                    "loot.metal-parts",
                    EnemyAttackKind.Melee,
                    0f,
                    0f,
                    string.Empty,
                    "loot.weapon.short-sword",
                    string.Empty,
                    0,
                    0f);
            }

            public static EnemyConfigDefinition ArcherConfig()
            {
                return new EnemyConfigDefinition(
                    ArcherEnemyId,
                    "Prefabs/Characters/GrayboxArcher",
                    15,
                    8,
                    12f,
                    8f,
                    2f,
                    1.8f,
                    "loot.metal-parts",
                    EnemyAttackKind.Projectile,
                    3f,
                    0.8f,
                    "projectile.arrow.basic",
                    "loot.weapon.bow",
                    "loot.ammunition.arrow",
                    10,
                    1.35f,
                    1,
                    1f);
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                var type = id == ItemIds.Arrow ? ItemType.Ammunition :
                    id == ItemIds.MetalParts ? ItemType.Material : ItemType.Weapon;
                value = new ItemDefinition(id, id.ToString(), string.Empty, string.Empty, string.Empty, string.Empty, type, 20, ItemUseEffect.None, 0);
                error = null;
                return true;
            }

            public bool TryGetWeapon(int itemId, out WeaponConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetProjectile(string id, out ProjectileConfigDefinition value, out string error)
            {
                value = new ProjectileConfigDefinition(id, "Prefabs/Combat/GrayboxArrow", 14f, 14f);
                error = null;
                return id == "projectile.arrow.basic";
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = id == ArcherEnemyId ? ArcherConfig() : BasicConfig();
                error = null;
                return id == ArcherEnemyId || id == BasicEnemyId;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                switch (id)
                {
                    case "loot.weapon.short-sword":
                        value = new LootConfigDefinition(id, "Prefabs/SceneObjects/TestResourcePickup", ItemIds.ShortSword, 1);
                        break;
                    case "loot.weapon.bow":
                        value = new LootConfigDefinition(id, "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Bow, 1);
                        break;
                    case "loot.ammunition.arrow":
                        value = new LootConfigDefinition(id, "Prefabs/SceneObjects/TestResourcePickup", ItemIds.Arrow, 1);
                        break;
                    default:
                        value = new LootConfigDefinition("loot.metal-parts", "Prefabs/SceneObjects/TestResourcePickup", ItemIds.MetalParts, 4);
                        break;
                }

                error = null;
                return true;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "missing";
                return false;
            }
        }
    }
}

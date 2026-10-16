using NUnit.Framework;
using FirepowerLoop.Arcade;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Luban;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>街机场地机关的配置与纯规则验收；不启动整局、不触发包装。</summary>
    public sealed class ArcadeHazardTests
    {
        [Test]
        public void ArenaCatalog_ExposesIndependentHazardConfiguration()
        {
            Assert.That(typeof(ArcadeArenaDefinition).GetProperty("Hazards"), Is.Not.Null,
                "机关必须有独立配置目录，不能复用静态障碍或在规则端写死数值");
        }

        [Test]
        public void GeneratedCatalog_MapsBothKindsAndCasualHazardBalance()
        {
            var catalog = LoadCatalog();
            Assert.That(catalog.Arenas[1].Hazards.Count, Is.EqualTo(2));
            Assert.That(catalog.Arenas[7].Hazards.Count, Is.EqualTo(1));
            Assert.That(catalog.Arenas.Where((arena, index) => index != 1 && index != 7).All(arena => arena.Hazards.Count == 0), Is.True);
            var tower = catalog.Arenas[1].Hazards[0];
            Assert.That(tower.Kind, Is.EqualTo(1));
            Assert.That(tower.PlayerDamageRatio, Is.EqualTo(1f));
            Assert.That(tower.BossDamageRatio, Is.EqualTo(.05f));
            Assert.That(tower.HitIntervalSeconds, Is.EqualTo(1f));
            Assert.That(tower.CycleSeconds, Is.EqualTo(8.5f));
            Assert.That(catalog.Arenas[7].Hazards[0].AngularSpeedDegrees, Is.EqualTo(90f));
        }

        [TestCase(2, 0, 100)]
        [TestCase(2, 1, 100)]
        [TestCase(8, 0, 100)]
        [TestCase(2, 0, 250)]
        [TestCase(2, 1, 250)]
        [TestCase(8, 0, 250)]
        public void ConfiguredHazard_UnprotectedContactLosesExactlyOneLife(int arenaNumber, int hazardIndex, int maximumHealth)
        {
            WithConfiguredHazard(arenaNumber, hazardIndex, maximumHealth, (system, runtime, run) =>
            {
                Hit(system, runtime, .1f);
                Assert.That(run.Current.RemainingLives, Is.EqualTo(2), "危险命中须消耗一条命，不能只扣部分生命");
                Assert.That(runtime.PlayerHealth, Is.Zero);
                Assert.That(runtime.RespawnRemaining, Is.GreaterThan(0f));
                Hit(system, runtime, 1f);
                Assert.That(run.Current.RemainingLives, Is.EqualTo(2), "复活等待期间不能继续扣命");
            });
        }

        [TestCase(2, 0)]
        [TestCase(8, 0)]
        public void ConfiguredHazard_ShieldBlocksFatalContactThenExposesNextContact(int arenaNumber, int hazardIndex)
        {
            WithConfiguredHazard(arenaNumber, hazardIndex, 100, (system, runtime, run) =>
            {
                runtime.Powerups.Add(ArcadePowerupBehavior.Shield,
                    new ArcadePowerupRuntime { RemainingSeconds = 10, Charges = 1 });
                Hit(system, runtime, .1f);
                Assert.That(run.Current.RemainingLives, Is.EqualTo(3));
                Assert.That(runtime.PlayerHealth, Is.EqualTo(100));
                Assert.That(runtime.Powerups.ContainsKey(ArcadePowerupBehavior.Shield), Is.False);
                runtime.PlayerInvulnerability = 0f;
                // 旋转横臂同次扫过不重复命中；下个活动周期才是新的危险接触。
                Hit(system, runtime, runtime.Hazards[0].Parameters.CycleSeconds);
                Assert.That(run.Current.RemainingLives, Is.EqualTo(2));
            });
        }

        [TestCase(2, 0, false)]
        [TestCase(8, 0, false)]
        [TestCase(2, 0, true)]
        [TestCase(8, 0, true)]
        public void ConfiguredHazard_InvulnerabilityAndDashProtectLives(int arenaNumber, int hazardIndex, bool dash)
        {
            WithConfiguredHazard(arenaNumber, hazardIndex, 100, (system, runtime, run) =>
            {
                if (dash) runtime.DashRemaining = 1f;
                else runtime.PlayerInvulnerability = 1f;
                Hit(system, runtime, .1f);
                Assert.That(run.Current.RemainingLives, Is.EqualTo(3));
                Assert.That(runtime.PlayerHealth, Is.EqualTo(100));
                Assert.That(runtime.RespawnRemaining, Is.Zero);
            });
        }

        [TestCase(0d, 0)]
        [TestCase(4d, 1)]
        [TestCase(5.5d, 2)]
        [TestCase(8.5d, 0)]
        public void TowerCycle_UsesConfiguredPhaseBoundaries(double time, int expected)
        {
            ArcadeHazardRules.Evaluate(Tower(), time, out int phase, out _);
            Assert.That(phase, Is.EqualTo(expected));
        }

        [Test]
        public void TowerContact_ThrottlesReentryAndSeparatesTargets()
        {
            var state = new ArcadeHazardState(Tower());
            state.Advance(5.6f);
            Assert.That(state.TryHit(-1, 4, 0, 4, 0, .4f), Is.True);
            state.Advance(.1f);
            Assert.That(state.TryHit(-1, 6, 0, 4, 0, .4f), Is.False);
            Assert.That(state.TryHit(-2, 4, 0, 4, 0, .4f), Is.True);
            state.Advance(1f);
            Assert.That(state.TryHit(-1, 4, 0, 4, 0, .4f), Is.True);
        }

        [Test]
        public void TowerCooldown_ClipsMovementToEligibleTime()
        {
            var state = new ArcadeHazardState(Tower());
            state.Advance(5.6f);
            Assert.That(state.TryHit(1, 4, 0, 4, 0, .4f), Is.True);
            state.Advance(1.2f);
            Assert.That(state.TryHit(1, 4, 0, 10, 0, .4f), Is.False,
                "冷却结束时已离开危险区，不能把冷却开始前的位置重新计伤");
        }

        [Test]
        public void TowerContact_DetectsCrossingWithBothEndpointsOutside()
        {
            Assert.That(ArcadeHazardRules.Intersects(Tower(), 6, 6.05, 1, 0, 7, 0, .4f, out _), Is.True);
            Assert.That(ArcadeHazardRules.Intersects(Tower(), 4, 4.05, 1, 0, 7, 0, .4f, out _), Is.False);
            Assert.That(ArcadeHazardRules.Intersects(Tower(), 6, 6.05, 1, 4, 7, 4, .4f, out _), Is.False);
        }

        [Test]
        public void Sweeper_UsesActualArmSweepRatherThanFullCircle()
        {
            var p = Arm();
            Assert.That(ArcadeHazardRules.Intersects(p, 5.5, 6.5, 1.4f, 1.4f, 1.4f, 1.4f, .1f, out _), Is.True);
            Assert.That(ArcadeHazardRules.Intersects(p, 5.5, 6.5, -1.4f, -1.4f, -1.4f, -1.4f, .1f, out _), Is.False);
            Assert.That(ArcadeHazardRules.Intersects(p, 4, 5, 1, 0, 1, 0, .1f, out _), Is.False);
        }

        [Test]
        public void Sweeper_OnlyHitsOncePerSweepEvenAfterContactInterval()
        {
            var state = new ArcadeHazardState(Arm());
            state.Advance(5.6f);
            Assert.That(state.TryHit(1, 0, 0, 0, 0, .2f), Is.True);
            state.Advance(1.1f);
            Assert.That(state.TryHit(1, 0, 0, 0, 0, .2f), Is.False);
            state.Advance(8.5f);
            Assert.That(state.TryHit(1, 0, 0, 0, 0, .2f), Is.True);
        }

        [Test]
        public void PauseAndTargetRelease_DoNotAdvanceOrCarryStaleCooldown()
        {
            var state = new ArcadeHazardState(Tower());
            state.Advance(5.6f);
            Assert.That(state.TryHit(7, 4, 0, 4, 0, .4f), Is.True);
            state.Advance(0f);
            Assert.That(state.Elapsed, Is.EqualTo(state.PreviousElapsed));
            state.ForgetTarget(7);
            state.Advance(.01f);
            Assert.That(state.TryHit(7, 4, 0, 4, 0, .4f), Is.True);
        }

        [Test]
        public void Validation_RejectsSpawnAreaAndBoundary()
        {
            Assert.That(ArcadeHazardRules.Validate(LoadCatalog().Arenas[1].Hazards[0], 12, 8, .4f), Is.Null);
            Assert.That(ArcadeHazardRules.Validate(Tower(), 12, 8, .4f), Is.Not.Null);
            Assert.That(ArcadeHazardRules.Validate(Arm(), 12, 8, .4f), Is.Not.Null);
            Assert.That(ArcadeHazardRules.Validate(Tower(), 5, 8, .4f), Is.Not.Null);
        }

        [Test]
        public void HazardPresentation_IsIdempotentColliderFreeAndReleasesObjects()
        {
            var root = new GameObject("HazardTestRoot");
            var presentation = new ArcadeHazardPresentation(root.transform);
            try
            {
                var p = new[] { Tower(), new ArcadeHazardParameters(2, 2, -4, 0, 0, 0, 2, .2f, 4, 1.5f, 4, 90, 1, .25f, .05f) };
                presentation.Sync(p, 4.5);
                presentation.Sync(p, 6);
                Assert.That(root.transform.childCount, Is.EqualTo(2));
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(root.transform.GetChild(0).Find("ElectricDanger").gameObject.activeSelf, Is.True);
                presentation.Sync(p, 0);
                Assert.That(root.transform.GetChild(0).Find("ElectricDanger").gameObject.activeSelf, Is.False);
                presentation.Dispose();
                Assert.That(root.transform.childCount, Is.Zero);
            }
            finally { presentation.Dispose(); UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void Authority_PlayerHitUsesShieldAndInvulnerability()
        {
            WithCombat((system, runtime) =>
            {
                runtime.Powerups.Add(ArcadePowerupBehavior.Shield, new ArcadePowerupRuntime { RemainingSeconds = 10, Charges = 1 });
                Hit(system, runtime, .1f);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(100));
                Assert.That(runtime.Powerups.ContainsKey(ArcadePowerupBehavior.Shield), Is.False);
                runtime.PlayerInvulnerability = 0;
                Hit(system, runtime, .1f);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(100), "护盾挡伤仍消耗机关接触间隔");
                Hit(system, runtime, 1f);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(75));
                runtime.PlayerInvulnerability = 10;
                Hit(system, runtime, 1f);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(75));
            });
        }

        [Test]
        public void Authority_DashRespawnAndCompletionWaitBlockHazards()
        {
            WithCombat((system, runtime) =>
            {
                runtime.DashRemaining = .5f;
                Hit(system, runtime, .1f);
                Assert.That(runtime.PlayerHealth, Is.EqualTo(100));
                double elapsed = runtime.HazardElapsedSeconds;
                runtime.RespawnRemaining = 1;
                Hit(system, runtime, .1f);
                Assert.That(runtime.HazardElapsedSeconds, Is.EqualTo(elapsed));
                runtime.RespawnRemaining = 0;
                runtime.WaveClearAwarded = true;
                Hit(system, runtime, .1f);
                Assert.That(runtime.HazardElapsedSeconds, Is.EqualTo(elapsed));
            });
        }

        [Test]
        public void Authority_HazardKillsNormalEnemyWithoutDropAndOnlyDamagesPlaceholderBoss()
        {
            WithCombat((system, runtime) =>
            {
                runtime.PlayerPosition = Vector2.zero;
                runtime.Enemies.Add(1, new ArcadeEnemyRuntime { Id = 1, Position = new Vector2(4, 0), Health = 100, MaxHealth = 100 });
                runtime.Enemies.Add(2, new ArcadeEnemyRuntime { Id = 2, Position = new Vector2(4, 0), Health = 100, MaxHealth = 100, IsBossPresentation = true });
                Hit(system, runtime, .1f);
                Assert.That(runtime.Enemies.ContainsKey(1), Is.False);
                Assert.That(runtime.Enemies[2].Health, Is.EqualTo(95));
                Assert.That(runtime.Drops, Is.Empty);
            });
        }

        private static ArcadeHazardParameters Tower() => new ArcadeHazardParameters(1, 1, 4, 0, 0, 1.2f, 0, 0, 4, 1.5f, 3, 0, 1, .25f, .05f);
        private static ArcadeHazardParameters Arm() => new ArcadeHazardParameters(1, 2, 0, 0, 0, 0, 2.2f, .2f, 4, 1.5f, 4, 90, 1, .25f, .05f);
        private static ArcadeConfigCatalogDefinition LoadCatalog()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes(
                Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog, out var error), Is.True, error);
            return catalog;
        }
        private static void Hit(ArcadeCombatSystem system, ArcadeCombatRuntime runtime, float seconds) =>
            typeof(ArcadeCombatSystem).GetMethod("TickHazards", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(system, new object[] { runtime, seconds, runtime.PlayerPosition });

        /// <summary>读取正式生成配置并调用真实受伤、失命入口；只测试尚有复活命数的局，不创建实际存档或切换地图。</summary>
        private static void WithConfiguredHazard(int arenaNumber, int hazardIndex, int maximumHealth,
            Action<ArcadeCombatSystem, ArcadeCombatRuntime, ArcadeRunModel> action)
        {
            var architecture = new Game();
            var run = new ArcadeRunModel();
            architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            architecture.RegisterSystem(new ArcadeRunSystem());
            run.CommitSafeNode(new ArcadeRunSaveData { RemainingLives = 3 });
            run.EnterArena();
            var system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            var catalog = LoadCatalog();
            var parameters = catalog.Arenas.Single(value => value.ArenaNumber == arenaNumber).Hazards[hazardIndex];
            var runtime = new ArcadeCombatRuntime
            {
                PlayerPosition = new Vector2(parameters.X, parameters.Y),
                PlayerHealth = maximumHealth,
                PlayerMaxHealth = maximumHealth,
                HazardElapsedSeconds = 5.6
            };
            var hazard = new ArcadeHazardState(parameters);
            hazard.Advance(5.6f);
            runtime.Hazards.Add(hazard);
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ArcadeCombatSystem).GetField("catalog", flags).SetValue(system, catalog);
            typeof(ArcadeCombatSystem).GetField("view", flags).SetValue(system, new TestView());
            try { action(system, runtime, run); }
            finally { architecture.GetModel<ArcadeCombatModel>().Clear(); }
        }
        private static void WithCombat(Action<ArcadeCombatSystem, ArcadeCombatRuntime> action)
        {
            var architecture = new Game();
            architecture.RegisterModel(new ArcadeCombatModel());
            var system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            var catalog = LoadCatalog();
            var runtime = new ArcadeCombatRuntime { PlayerPosition = new Vector2(4, 0), PlayerHealth = 100, PlayerMaxHealth = 100, HazardElapsedSeconds = 5.6 };
            var hazard = new ArcadeHazardState(Tower()); hazard.Advance(5.6f); runtime.Hazards.Add(hazard);
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ArcadeCombatSystem).GetField("catalog", flags).SetValue(system, catalog);
            typeof(ArcadeCombatSystem).GetField("view", flags).SetValue(system, new TestView());
            action(system, runtime);
        }
        private sealed class TestView : IArcadeCombatViewPort
        {
            public bool IsActive => true;
            public void Begin(int number, float width, float height, IReadOnlyList<ArcadeObstacleDefinition> obstacles) { }
            public void End() { }
            public bool TryScreenToArena(Vector2 screen, out Vector2 position) { position = default; return false; }
            public void SetPlayer(Vector2 position, Vector2 facing, bool visible, bool invulnerable) { }
            public void SetPlayerHealth(int current, int maximum) { }
            public void SetEnemy(int id, string config, Vector2 position, int current, int maximum, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false) { }
            public void RemoveEnemy(int id, bool playDeath = false) { }
            public void SetProjectile(int id, bool hostile, Vector2 position, Vector2 direction) { }
            public void RemoveProjectile(int id) { }
            public void SetDrop(int id, ArcadeDropDefinition definition, Vector2 position) { }
            public void RemoveDrop(int id) { }
            public void SetRemotePlayer(long id, Vector2 position, Vector2 facing, bool visible, bool invulnerable, int current, int maximum) { }
            public void RemoveRemotePlayer(long id) { }
            public void TickPlayerFeedback(float seconds) { }
        }
    }
}

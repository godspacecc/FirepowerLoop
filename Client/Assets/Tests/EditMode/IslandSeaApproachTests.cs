using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式海岛生成与移动链路：三通道连续入场、旧海岸接近及玩家边界。</summary>
    public sealed class IslandSeaApproachTests
    {
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeRunModel run;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp] public void Setup()
        {
            var architecture = new Game();
            run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            run.CommitSafeNode(new ArcadeRunSaveData { RunId = "sea-approach-test", RemainingLives = 3,
                IsCasualRoute = true, ArenaConfigNumber = 1, NextArenaNumber = 1,
                Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 1 } });
            run.EnterArena();
            runtime = new ArcadeCombatRuntime { ArenaNumber = 1, PlayerHealth = 100, PlayerMaxHealth = 100,
                PlayerInvulnerability = 1000 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog", catalog); Set("arena", catalog.Arenas.Single(a => a.ArenaNumber == 1));
            Set("random", new System.Random(37)); Set("view", new ArcadeCombatViewController());
        }

        // 十二个实际通道全部覆盖，另保留旧顺序局的连续涉水回归。
        [TestCase(0, 0, true)]
        [TestCase(0, 1, true)]
        [TestCase(0, 2, true)]
        [TestCase(1, 0, true)]
        [TestCase(1, 1, true)]
        [TestCase(1, 2, true)]
        [TestCase(2, 0, true)]
        [TestCase(2, 1, true)]
        [TestCase(2, 2, true)]
        [TestCase(3, 0, true)]
        [TestCase(3, 1, true)]
        [TestCase(3, 2, true)]
        [TestCase(0, 0, false)]
        public void SpawnedEnemy_ApproachesContinuouslyThroughItsPassage(int wave, int side, bool casual)
        {
            var data = run.Current; data.IsCasualRoute = casual; run.CommitSafeNode(data); run.EnterArena();
            var enemy = Spawn(wave, side);
            var region = new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, casual ? wave + 1 : 0);
            Assert.That(region.Contains(enemy.Position.x, enemy.Position.y), Is.False, "必须从战斗边界外进入");
            if (casual)
            {
                var entry = catalog.RoundLayouts.Single(p => p.ArenaConfigNumber == 1 && p.RoundNumber == wave + 1).Entries[side];
                Assert.That(Vector2.Dot(enemy.Position.normalized, new Vector2(entry.X, entry.Y).normalized), Is.GreaterThan(.995f));
                Assert.That(region.IsShallow(entry.X, entry.Y), Is.False, "陆路和船梯不能错当涉水入口");
            }
            bool sawShallow = false, sawLand = false;
            for (int step = 0; step < 800 && !sawLand; step++)
            {
                var before = enemy.Position;
                Call("TickEnemies", runtime, .05f);
                Assert.That(Vector2.Distance(enemy.Position, before), Is.LessThanOrEqualTo(enemy.Speed * .05f + .01f), "入场不能瞬移");
                sawShallow |= region.IsShallow(enemy.Position.x, enemy.Position.y);
                sawLand = region.ContainsCircle(enemy.Position.x, enemy.Position.y, catalog.Rule.EnemyContactRadius) &&
                    !region.IsShallow(enemy.Position.x, enemy.Position.y);
            }
            if (!casual) Assert.That(sawShallow, Is.True, "旧顺序局仍经过浅水");
            Assert.That(sawLand, Is.True, "必须连续移动至干燥战斗区");
        }

        [Test] public void SeaCrowdingAndKnockback_DoNotTeleportEnemiesToShore()
        {
            var enemy = Spawn(0, 1);
            var origin = enemy.Position;
            runtime.Enemies.Add(999, new ArcadeEnemyRuntime { Id = 999, Position = origin,
                Speed = enemy.Speed, Health = 100, MaxHealth = 100 });
            Call("TickEnemies", runtime, .01f);
            foreach (var value in runtime.Enemies.Values)
                Assert.That(Vector2.Distance(value.Position, origin), Is.LessThan(2f), "拥挤修正不能投影到岸内");
            var before = enemy.Position;
            Call("DamageAndKnockbackEnemies", runtime, before + Vector2.right * .2f, 1f, 1, 1f);
            Assert.That(Vector2.Distance(enemy.Position, before), Is.LessThanOrEqualTo(1.01f), "深海击退不能投影到岸内");
        }

        [Test] public void SeaEnemy_CanDamagePlayerStandingAtShore()
        {
            var enemy = Spawn(0, 1);
            var region = new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, 1);
            var shore = region.Constrain(enemy.Position.x, enemy.Position.y, catalog.Rule.PlayerRadius);
            runtime.PlayerPosition = new Vector2(shore.X, shore.Y);
            runtime.PlayerInvulnerability = 0;
            enemy.AttackCooldown = 0;
            for (int step = 0; step < 800 && runtime.PlayerHealth == 100; step++)
                Call("TickEnemies", runtime, .05f);
            Assert.That(runtime.PlayerHealth, Is.LessThan(100), "贴岸玩家不能因敌人未整圆入岸而免疫接触伤害");
        }

        [Test] public void PlayerMovement_RemainsInsideOriginalIsland()
        {
            var region = new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, 1);
            var resolved = ArcadeCombatSystem.ResolvePosition(new Vector2(30, 0), catalog.Rule.PlayerRadius,
                catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, Array.Empty<ArcadeObstacleDefinition>(), region);
            Assert.That(region.Contains(resolved.x, resolved.y), Is.True);
            Assert.That(resolved.x, Is.LessThan(catalog.Rule.ArenaHalfWidth));
        }

        private ArcadeEnemyRuntime Spawn(int wave, int passage)
        {
            Call("BeginWave", runtime, wave);
            runtime.IslandWaveBudget = runtime.SpawnQueue.Count;
            runtime.IslandEntryWarningRemaining = 0; runtime.IslandSpawnSequence = 0;
            if (run.Current.IsCasualRoute)
            {
                int seed = 0; while (new System.Random(seed).Next(3) != passage) seed++;
                Set("random", new System.Random(seed)); runtime.IslandPassages = null;
            }
            Call("TickSpawning", runtime, .01f);
            return runtime.Enemies.Values.Single();
        }
        private void Set(string name, object value) => typeof(ArcadeCombatSystem).GetField(name, Flags).SetValue(system, value);
        private object Call(string name, params object[] values) => typeof(ArcadeCombatSystem).GetMethod(name, Flags).Invoke(system, values);
    }
}

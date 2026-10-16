using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>SP-REQ-001：用正式配置和可控随机结果验证敌人禁掉武器，保留其他来源与权重。</summary>
    public sealed class ArcadeEnemyDropTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;

        [SetUp]
        public void Setup()
        {
            architecture = new Game();
            var run = new ArcadeRunModel();
            architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            run.CommitSafeNode(new ArcadeRunSaveData { RunId = "enemy-drop-test", IsCasualRoute = true,
                RemainingLives = 3 });
            run.EnterArena();
            runtime = new ArcadeCombatRuntime { ArenaNumber = 12, PlayerHealth = 100, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system = new ArcadeCombatSystem();
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog", catalog);
            Set("arena", catalog.Arenas.Single(a => a.ArenaNumber == 2));
            Set("view", new ArcadeCombatViewController());
        }

        private void Set(string field, object value) => typeof(ArcadeCombatSystem).GetField(field, Private).SetValue(system, value);
        private object Call(string method, params object[] args) => typeof(ArcadeCombatSystem).GetMethod(method, Private).Invoke(system, args);
        private ArcadeEnemyRuntime Enemy(bool boss = false) => new ArcadeEnemyRuntime
        {
            Id = 900, Position = Vector2.one, Health = 0, MaxHealth = 100,
            IsBossPresentation = boss, BossId = boss ? "drop-test-boss" : null
        };

        /// <summary>逐个覆盖原权重区间；武器命中必须留空，所有非武器仍落到原区间且死亡不重复结算。</summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void EnemyDeath_EveryOriginalWeightedResultSuppressesOnlyWeapons(bool boss, bool finalBoss)
        {
            var candidates = catalog.Drops.Where(d => d.Weight > 0 && d.MinArena <= 12 &&
                (d.MaxArena <= 0 || d.MaxArena >= 12)).ToArray();
            Assert.That(candidates.Count(d => d.Kind == ArcadeDropKind.TemporaryWeapon), Is.EqualTo(6));
            int total = candidates.Sum(d => d.Weight), roll = 0;
            foreach (var candidate in candidates)
            for (int offset = 0; offset < candidate.Weight; offset++, roll++)
            {
                runtime.Drops.Clear();
                var random = new ControlledRandom(total, roll);
                Set("random", random);
                var enemy = Enemy(boss);
                runtime.Enemies.Add(enemy.Id, enemy);
                runtime.IsFinalBoss = finalBoss;
                runtime.FinalBossEnemyId = finalBoss ? enemy.Id : 0;
                int nextId = runtime.NextEntityId;
                Call("KillEnemy", runtime, enemy, false);
                Assert.That(runtime.Enemies, Is.Empty);
                if (candidate.Kind == ArcadeDropKind.TemporaryWeapon)
                {
                    Assert.That(runtime.Drops, Is.Empty, candidate.Id + " 不得生成或补发奖励");
                    Assert.That(runtime.NextEntityId, Is.EqualTo(nextId));
                }
                else
                {
                    Assert.That(runtime.Drops.Count, Is.EqualTo(1), candidate.Id);
                    Assert.That(runtime.Drops.Values.Single().Definition.Id, Is.EqualTo(candidate.Id));
                }
                if (finalBoss)
                    Assert.That(runtime.FinalBossDefeated && runtime.AwaitingExit, Is.True);
                Call("KillEnemy", runtime, enemy, false);
                Assert.That(random.ChanceCalls, Is.EqualTo(1));
                Assert.That(random.WeightCalls, Is.EqualTo(1), "不得重抽或重复死亡奖励");
            }
        }

        /// <summary>禁掉敌人武器只约束本次生成，不清理旧地面武器，也不替换或重置已持有武器。</summary>
        [Test]
        public void SuppressedEnemyWeapon_PreservesExistingPickupAndHeldWeapon()
        {
            var candidates = catalog.Drops.Where(d => d.Weight > 0 && d.MinArena <= 12 &&
                (d.MaxArena <= 0 || d.MaxArena >= 12)).ToArray();
            var weapon = candidates.First(d => d.Kind == ArcadeDropKind.TemporaryWeapon);
            var existing = new ArcadeDropRuntime { Id = 42, Definition = weapon, Position = Vector2.one };
            runtime.Drops.Add(existing.Id, existing);
            runtime.TemporaryWeaponId = "arcade.weapon.rocket";
            runtime.TemporaryWeaponRemaining = 7f;
            var random = new ControlledRandom(candidates.Sum(d => d.Weight),
                candidates.TakeWhile(d => d.Id != weapon.Id).Sum(d => d.Weight));
            Set("random", random);
            var enemy = Enemy();
            runtime.Enemies.Add(enemy.Id, enemy);
            Call("KillEnemy", runtime, enemy, false);
            Assert.That(runtime.Drops.Count, Is.EqualTo(1));
            Assert.That(runtime.Drops[42], Is.SameAs(existing));
            Assert.That(existing.Definition.Id, Is.EqualTo(weapon.Id));
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo("arcade.weapon.rocket"));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(7f));
            Assert.That(random.WeightCalls, Is.EqualTo(1));
        }

        [TestCase("hazard")]
        [TestCase("challenge")]
        [TestCase("capacity")]
        [TestCase("chance")]
        public void IneligibleEnemyDeath_DoesNotCreateRewards(string reason)
        {
            var random = new ControlledRandom(0, 0, reason == "chance" ? 1d : 0d);
            Set("random", random);
            runtime.IsChallenge = reason == "challenge";
            if (reason == "capacity")
                for (int id = 0; id < catalog.Rule.MaxDrops; id++) runtime.Drops.Add(id, new ArcadeDropRuntime { Id = id });
            int before = runtime.Drops.Count;
            var enemy = Enemy();
            runtime.Enemies.Add(enemy.Id, enemy);
            Call("KillEnemy", runtime, enemy, reason == "hazard");
            Assert.That(runtime.Enemies, Is.Empty);
            Assert.That(runtime.Drops.Count, Is.EqualTo(before));
            Assert.That(random.WeightCalls, Is.Zero);
            Assert.That(random.ChanceCalls, Is.EqualTo(reason == "chance" ? 1 : 0));
        }

        /// <summary>命中箱子武器后经正式拾取和计时入口验证更换、持续与到期恢复；不改共享配置。</summary>
        [TestCase("arcade.drop.weapon.rapid")]
        [TestCase("arcade.drop.weapon.spread")]
        public void CrateWeapon_StillSpawnsCollectsAndExpires(string dropId)
        {
            runtime.ArenaNumber = 2;
            var candidates = (ArcadeDropDefinition[])Call("CrateDropPool");
            var arena = catalog.Arenas.Single(a => a.ArenaNumber == 2);
            int Weight(ArcadeDropDefinition d) => d.Weight *
                (d.Kind == ArcadeDropKind.RunGold ? arena.Babylon.CrateTreasureWeightMultiplier : 1);
            var random = new ControlledRandom(candidates.Sum(Weight), candidates.TakeWhile(d => d.Id != dropId).Sum(Weight));
            Set("random", random);
            Call("InitializeCrates", runtime);
            int crateId = runtime.Crates.Keys.First();
            Call("DamageCrate", runtime, crateId, 10000);
            Call("DamageCrate", runtime, crateId, 10000);
            var drop = runtime.Drops.Values.Single();
            Assert.That(drop.Definition.Id, Is.EqualTo(dropId));
            var definition = drop.Definition;
            runtime.PlayerPosition = drop.Position;
            runtime.TemporaryWeaponId = "arcade.weapon.rocket";
            runtime.TemporaryWeaponRemaining = 1f;
            Call("TickDrops", runtime);
            Assert.That(runtime.Drops, Is.Empty);
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo(definition.ProductId));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(definition.DurationSeconds));
            Call("TickTimers", runtime, .5f);
            Assert.That(runtime.TemporaryWeaponId, Is.Not.Null);
            Call("TickTimers", runtime, 100f);
            Assert.That(runtime.TemporaryWeaponId, Is.Null);
            var args = new object[] { runtime, null };
            Assert.That((bool)Call("TryGetWeapon", args), Is.True);
            Assert.That(((ArcadeWeaponDefinition)args[1]).Id, Is.EqualTo(catalog.Rule.BaseWeaponId));
            Assert.That(random.WeightCalls, Is.EqualTo(1));
        }

        /// <summary>只控制随机输入，实际死亡、掉落、拾取和对象池仍执行生产实现。</summary>
        private sealed class ControlledRandom : System.Random
        {
            private readonly int total;
            private readonly int roll;
            private readonly double chance;
            public int ChanceCalls { get; private set; }
            public int WeightCalls { get; private set; }
            public ControlledRandom(int total, int roll, double chance = 0d)
            { this.total = total; this.roll = roll; this.chance = chance; }
            public override double NextDouble() { ChanceCalls++; return chance; }
            public override int Next(int maxValue)
            {
                WeightCalls++;
                Assert.That(maxValue, Is.EqualTo(total), "原武器权重必须保留在总权重中");
                return roll;
            }
        }
    }
}

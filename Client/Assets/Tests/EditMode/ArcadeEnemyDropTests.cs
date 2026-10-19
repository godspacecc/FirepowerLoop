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
    /// <summary>用正式配置和可控随机结果验证敌人武器掉落、拾取、开火与到期恢复。</summary>
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

        /// <summary>真实喷火命中产生短暂燃烧表现身份，计时不造成额外伤害，后续普通伤害清除火焰击杀归属。</summary>
        [Test]
        public void FlameImpactRecordsVisibleBurnWithoutAddingDamageOverTime()
        {
            var timer=typeof(ArcadeEnemyRuntime).GetField("FlameHitRemainingSeconds");
            var source=typeof(ArcadeEnemyRuntime).GetField("LastDamageWasFlame");
            Assert.That(timer,Is.Not.Null,"实际命中缺少燃烧表现状态");Assert.That(source,Is.Not.Null);
            var enemy=Enemy();enemy.Health=100;enemy.Position=Vector2.zero;
            runtime.Enemies.Add(enemy.Id,enemy);
            runtime.Projectiles.Add(901,new ArcadeProjectileRuntime {Id=901,Position=Vector2.zero,Velocity=Vector2.zero,
                Damage=5,RemainingSeconds=1,SourceWeaponId="arcade.weapon.flamethrower",FromPlayerWeapon=true,
                Behavior=ArcadeWeaponBehavior.Piercing,RemainingHits=8});
            Call("TickProjectiles",runtime,.01f);
            Assert.That(enemy.Health,Is.EqualTo(95));Assert.That((float)timer.GetValue(enemy),Is.GreaterThan(0));
            Assert.That((bool)source.GetValue(enemy),Is.True);
            Call("TickTimers",runtime,1f);
            Assert.That((float)timer.GetValue(enemy),Is.Zero);Assert.That(enemy.Health,Is.EqualTo(95),"视觉燃烧不得额外扣血");
            Call("DamageEnemy",enemy,1);
            Assert.That((bool)source.GetValue(enemy),Is.False,"普通枪最终击杀不能误播火焰死亡");
            enemy.Reset();Assert.That((float)timer.GetValue(enemy),Is.Zero);Assert.That((bool)source.GetValue(enemy),Is.False);
        }

        /// <summary>喷火受伤后被海岛冲刺撞死，不能沿用已过期的火焰击杀来源。</summary>
        [Test]
        public void IslandDashKillDoesNotInheritEarlierFlameHit()
        {
            runtime.ArenaNumber=1;runtime.DashRemaining=1;
            Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==1));
            Set("random",new ControlledRandom(0,0,1d));
            var port=new DeathKindPort();var view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("enemyPresentation",Private).SetValue(view,port);Set("view",view);
            var enemy=Enemy();enemy.Health=100;enemy.Position=Vector2.zero;
            runtime.Enemies.Add(enemy.Id,enemy);Call("DamageEnemyFromFlame",enemy,1);
            enemy.FlameHitRemainingSeconds=0;
            var path=(System.Collections.Generic.List<FirepowerLoop.Arcade.ArcadeRegionPoint>)typeof(ArcadeCombatSystem).GetField("islandMovementPath",Private).GetValue(system);
            path.Add(new FirepowerLoop.Arcade.ArcadeRegionPoint(-1,0));path.Add(new FirepowerLoop.Arcade.ArcadeRegionPoint(1,0));
            Call("KillAlongIslandDash",runtime);
            Assert.That(runtime.Enemies,Is.Empty);Assert.That(port.FlameKill,Is.False,"检查实际死亡表现参数，不能只检查回池后已清空的敌人字段");
        }

        /// <summary>地图空投在落地开箱前不能被接触或磁铁提前领取。</summary>
        [Test]
        public void MapWeaponDeliveryBlocksPickupUntilLandedAndOpened()
        {
            runtime.ArenaNumber=1;Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==1));Set("random",new System.Random(5));
            Call("BeginWave",runtime,0);Call("TickWeaponPickups",runtime,100f);
            var drop=runtime.Drops.Values.Single();var product=drop.Definition.ProductId;runtime.PlayerPosition=drop.Position;Call("TickDrops",runtime);
            Assert.That(runtime.Drops.Count,Is.EqualTo(1),"空投未落地开箱就被领取");
            Call("TickDropLifetimes",runtime,0f);Call("TickDrops",runtime);Assert.That(runtime.Drops.Count,Is.EqualTo(1));
            Call("TickDropLifetimes",runtime,2f);Call("TickDrops",runtime);
            Assert.That(runtime.Drops,Is.Empty);Assert.That(runtime.TemporaryWeaponId,Is.EqualTo(product));
        }

        [Test]
        public void AirdropFlightDoesNotConsumeGroundLifetime()
        {
            runtime.ArenaNumber=1;Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==1));Set("random",new System.Random(5));
            Call("BeginWave",runtime,0);Call("TickWeaponPickups",runtime,100f);var drop=runtime.Drops.Values.Single();
            Call("TickDropLifetimes",runtime,.5f);Assert.That(drop.RemainingSeconds,Is.EqualTo(30f));
            Call("TickDropLifetimes",runtime,2f);Assert.That(drop.RemainingSeconds,Is.InRange(28.5f,29.5f));
        }

        [Test]
        public void OrdinaryWeaponLootStillAllowsImmediateContact()
        {
            var definition=catalog.Drops.First(d=>d.Id=="arcade.drop.weapon.rapid");
            runtime.Drops.Add(999,new ArcadeDropRuntime{Id=999,Definition=definition,Position=runtime.PlayerPosition});
            Call("TickDrops",runtime);Assert.That(runtime.Drops,Is.Empty);Assert.That(runtime.TemporaryWeaponId,Is.EqualTo(definition.ProductId));
        }

        /// <summary>固定点位逐把错峰生成，拾取才装备；不同随机源可改变位置和武器。</summary>
        [Test]
        public void WaveStart_RandomWeaponPickupsUseConfiguredPointsAndEquipOnContact()
        {
            runtime.ArenaNumber = 1;
            Set("arena", catalog.Arenas.Single(a => a.ArenaNumber == 1));
            var layouts = new System.Collections.Generic.HashSet<string>();
            var weapons = new System.Collections.Generic.HashSet<string>();
            for (int seed = 0; seed < 16; seed++)
            {
                runtime.Drops.Clear();
                runtime.TemporaryWeaponId = null;
                Set("random", new System.Random(seed));
                Call("BeginWave", runtime, seed % 4);
                Assert.That(runtime.Drops, Is.Empty, "不能在开场同时生成两把武器");
                Call("TickWeaponPickups", runtime, 100f);
                Assert.That(runtime.Drops.Count, Is.EqualTo(1), "大步长也只能生成一把");
                Call("TickWeaponPickups", runtime, 0f);
                Assert.That(runtime.Drops.Count, Is.EqualTo(1));
                Call("TickWeaponPickups", runtime, 100f);
                Assert.That(runtime.Drops.Count, Is.EqualTo(2), "本轮计划应逐把提供两把地面武器");
                var drops = runtime.Drops.Values.ToArray();
                Assert.That(drops.Select(d => d.Position).Distinct().Count(), Is.EqualTo(2));
                Assert.That(runtime.TemporaryWeaponId, Is.Null, "靠近前不得自动装备");
                foreach (var drop in drops)
                {
                    Assert.That(drop.Definition.Kind, Is.EqualTo(ArcadeDropKind.TemporaryWeapon));
                    Assert.That(drop.Definition.MinArena, Is.LessThanOrEqualTo(1));
                    weapons.Add(drop.Definition.ProductId);
                }
                layouts.Add(string.Join(";", drops.Select(d => d.Position.ToString()).OrderBy(p => p)));
                var first = drops[0]; var definition = first.Definition;
                runtime.PlayerPosition = first.Position;
                Call("TickDropLifetimes",runtime,catalog.Rule.WeaponDeliverySeconds);
                Call("TickDrops", runtime);
                Assert.That(runtime.TemporaryWeaponId, Is.EqualTo(definition.ProductId));
                Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(definition.DurationSeconds));
                Assert.That(runtime.Drops.Count, Is.EqualTo(1));
            }
            Assert.That(layouts.Count, Is.GreaterThan(4));
            Assert.That(weapons, Does.Contain("arcade.weapon.flamethrower"));
            Assert.That(weapons.Count, Is.GreaterThan(1));
        }

        /// <summary>普通开场生成受总预算限制；已有物品不被清理，挑战和独立Boss不额外刷枪。</summary>
        [Test]
        public void OpeningWeaponsRespectGroundBudgetAndSpecialBattleGuards()
        {
            runtime.ArenaNumber=1;
            Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==1));
            Set("random",new System.Random(0));
            for(int i=0;i<catalog.Rule.MaxDrops-1;i++)
                runtime.Drops.Add(1000+i,new ArcadeDropRuntime {Id=1000+i,Definition=catalog.Drops.First()});
            Call("BeginWave",runtime,0);
            Assert.That(runtime.Drops.Count,Is.EqualTo(catalog.Rule.MaxDrops-1));
            Call("TickWeaponPickups",runtime,100f);
            Assert.That(runtime.Drops.Count,Is.EqualTo(catalog.Rule.MaxDrops));
            Assert.That(runtime.Drops.ContainsKey(1000),Is.True);
            Call("TickWeaponPickups",runtime,100f);
            Assert.That(runtime.Drops.Count,Is.EqualTo(catalog.Rule.MaxDrops));
            Assert.That(runtime.WeaponSpawns.RemainingCount,Is.EqualTo(1),"满预算不得丢失待刷武器");
            runtime.Drops.Clear();runtime.IsChallenge=true;
            Call("InitializeWeaponPickups",runtime);
            Assert.That(runtime.Drops,Is.Empty);
            runtime.IsChallenge=false;runtime.IsFinalBoss=true;
            Call("InitializeWeaponPickups",runtime);
            Assert.That(runtime.Drops,Is.Empty);
        }

        /// <summary>每把使用独立正间隔；房间、切图和清场冻结计时，切轮取消上轮计划。</summary>
        [Test]
        public void WeaponSpawnTimingStaggersAndFreezesOutsideActiveCombat()
        {
            runtime.ArenaNumber=1;Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==1));
            Set("random",new System.Random(7));Call("BeginWave",runtime,0);
            var schedule=runtime.WeaponSpawns;
            Assert.That(schedule.RemainingSeconds,Is.InRange(3f,8f));
            Call("TickWeaponPickups",runtime,schedule.RemainingSeconds-.01f);
            Assert.That(runtime.Drops,Is.Empty);
            Call("TickWeaponPickups",runtime,.02f);
            Assert.That(runtime.Drops.Count,Is.EqualTo(1));
            float nextDelay=schedule.RemainingSeconds;
            Assert.That(nextDelay,Is.InRange(3f,8f));
            foreach(string gate in new[]{"room","transition","clear","zero"})
            {
                runtime.InTreasureRoom=gate=="room";runtime.IslandTransitionPending=gate=="transition";runtime.WaveClearAwarded=gate=="clear";
                Call("TickWeaponPickups",runtime,gate=="zero"?0f:100f);
                Assert.That(runtime.Drops.Count,Is.EqualTo(1));
                Assert.That(schedule.RemainingSeconds,Is.EqualTo(nextDelay),gate);
            }
            runtime.WaveClearAwarded=false;
            Call("TickWeaponPickups",runtime,nextDelay-.01f);
            Assert.That(runtime.Drops.Count,Is.EqualTo(1));
            Call("TickWeaponPickups",runtime,.02f);
            Assert.That(runtime.Drops.Count,Is.EqualTo(2));
            Call("BeginWave",runtime,1);
            Assert.That(runtime.WeaponSpawns,Is.Not.SameAs(schedule));
            Assert.That(runtime.WeaponSpawns.RemainingCount,Is.EqualTo(2));
        }

        /// <summary>逐个覆盖权重区间；武器与其他物品按同一规则生成，死亡不重复结算。</summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void EnemyDeath_EveryWeightedResultCreatesConfiguredDrop(bool boss, bool finalBoss)
        {
            var candidates = catalog.Drops.Where(d => d.Weight > 0 && d.MinArena <= 12 &&
                (d.MaxArena <= 0 || d.MaxArena >= 12)).ToArray();
            Assert.That(candidates.Any(d => d.Kind == ArcadeDropKind.TemporaryWeapon), Is.True);
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
                Assert.That(runtime.Drops.Count, Is.EqualTo(1), candidate.Id);
                Assert.That(runtime.Drops.Values.Single().Definition.Id, Is.EqualTo(candidate.Id));
                Assert.That(runtime.NextEntityId, Is.EqualTo(nextId + 1));
                if (finalBoss)
                    Assert.That(runtime.FinalBossDefeated && runtime.AwaitingExit, Is.True);
                Call("KillEnemy", runtime, enemy, false);
                Assert.That(random.ChanceCalls, Is.EqualTo(1));
                Assert.That(random.WeightCalls, Is.EqualTo(1), "不得重抽或重复死亡奖励");
            }
        }

        /// <summary>新武器掉落不清理旧地面武器，也不在拾取前替换或重置已持有武器。</summary>
        [Test]
        public void EnemyWeapon_PreservesExistingPickupAndHeldWeapon()
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
            Assert.That(runtime.Drops.Count, Is.EqualTo(2));
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

        /// <summary>首场喷火器经击杀生成、拾取及开火；短程火流可穿过多个敌人，结束后恢复基础枪。</summary>
        [Test]
        public void OpeningFlamethrower_DropsFiresPiercingFlamesAndExpires()
        {
            runtime.ArenaNumber = 1;
            var candidates = catalog.Drops.Where(d => d.Weight > 0 && d.MinArena <= 1 &&
                (d.MaxArena <= 0 || d.MaxArena >= 1)).ToArray();
            var drop = candidates.Single(d => d.Id == "arcade.drop.weapon.flamethrower");
            var weapon = catalog.Weapons.Single(w => w.Id == drop.ProductId);
            Assert.That(weapon.Behavior, Is.EqualTo(ArcadeWeaponBehavior.Piercing));
            Assert.That(weapon.ProjectileSpeed * weapon.ProjectileLifetimeSeconds, Is.InRange(2f, 5f));
            Assert.That(weapon.FireIntervalSeconds, Is.LessThanOrEqualTo(.12f));
            Set("random", new ControlledRandom(candidates.Sum(d => d.Weight),
                candidates.TakeWhile(d => d.Id != drop.Id).Sum(d => d.Weight)));
            var enemy = Enemy();
            runtime.Enemies.Add(enemy.Id, enemy);
            Call("KillEnemy", runtime, enemy, false);
            var pickup = runtime.Drops.Values.Single();
            runtime.PlayerPosition = pickup.Position;
            Call("TickDrops", runtime);
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo(weapon.Id));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(drop.DurationSeconds));
            runtime.AimDirection = Vector2.up;
            Call("TryFire", runtime);
            Assert.That(runtime.Projectiles.Count, Is.EqualTo(weapon.ProjectileCount));
            foreach (var flame in runtime.Projectiles.Values)
            {
                Assert.That(flame.SourceWeaponId, Is.EqualTo(weapon.Id));
                Assert.That(flame.ProjectileVisualResourceName, Is.EqualTo("ArcadeFlameBurst"));
                Assert.That(ArcadeCombatSystem.TryRegisterProjectileHit(flame, 101), Is.True);
                Assert.That(ArcadeCombatSystem.TryRegisterProjectileHit(flame, 101), Is.False);
                Assert.That(ArcadeCombatSystem.TryRegisterProjectileHit(flame, 102), Is.True);
            }
            Call("TickTimers", runtime, drop.DurationSeconds + 1);
            Assert.That(runtime.TemporaryWeaponId, Is.Null);
            var args = new object[] { runtime, null };
            Assert.That((bool)Call("TryGetWeapon", args), Is.True);
            Assert.That(((ArcadeWeaponDefinition)args[1]).Id, Is.EqualTo(catalog.Rule.BaseWeaponId));
        }

        /// <summary>只控制随机输入，实际死亡、掉落、拾取和对象池仍执行生产实现。</summary>
        /// <summary>截获正式视图转发的死亡类别，避免对象回池重置掩盖来源错误。</summary>
        private sealed class DeathKindPort:IArcadeEnemyPresentationPort
        {
            public bool? FlameKill;
            public bool TrySetEnemy(int id,string config,Vector3 position,Vector3 facing,int health,int maximum,out string error,bool telegraph=false,int sequence=0,bool persistentHealthBar=false)
            {error=null;return true;}
            public void RemoveEnemy(int id,bool playDeath=false){}
            public void RemoveEnemy(int id,bool playDeath,Vector3 direction,bool explosive,bool flameKill){FlameKill=flameKill;}
            public void ClearArcadeEnemies(){}
        }

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

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using FirepowerLoop.Arcade;

namespace Game.Tests
{
    /// <summary>通过真实出生和敌人活动入口验证码头右侧上岸，不以动画预览替代战斗规则。</summary>
    public sealed class DockSeaEntryFlowTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Game app;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            app = new Game(); app.RegisterModel(new ArcadeRunModel()); app.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", n + ".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog, out var error), Is.True, error);
            app.RegisterUtility<IRuntimeConfigProvider>(provider);
            app.GetModel<ArcadeRunModel>().CommitSafeNode(new ArcadeRunSaveData { RunId = "dock-sea-entry", IsCasualRoute = true, ArenaConfigNumber = 21, NextArenaNumber = 2, CompletedArenaCount = 1 });
            app.GetModel<ArcadeRunModel>().EnterArena();
            runtime = new ArcadeCombatRuntime { ArenaNumber = 2, PlayerHealth = 100, PlayerMaxHealth = 100, PlayerPosition = new Vector2(11, -4.1f) };
            app.GetModel<ArcadeCombatModel>().Begin(runtime);
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(app);
            var arena = catalog.Arenas.Single(a => a.ArenaNumber == 21);
            Set("view", new ArcadeCombatViewController()); Set("catalog", catalog); Set("arena", arena);
            Set("group", catalog.EnemyGroups.Single(g => g.Id == arena.WaveGroupIds[0])); Set("random", new System.Random(17));
            Call("InitializeRoundContent", runtime);
        }
        [TearDown] public void Cleanup() => app.GetModel<ArcadeCombatModel>().Clear();
        private void Set(string key, object value) => typeof(ArcadeCombatSystem).GetField(key, Hidden).SetValue(system, value);
        private object Call(string key, params object[] args) => typeof(ArcadeCombatSystem).GetMethod(key, Hidden).Invoke(system, args);
        private ArcadeEnemyRuntime Spawn(int passage)
        {
            runtime.IslandEntryBatch = passage;
            runtime.SpawnQueue.Enqueue("enemy.arcade.zombie.shambler");
            Assert.That(Call("SpawnQueuedEnemy", runtime, null), Is.True);
            return runtime.Enemies.Values.OrderBy(e => e.Id).Last();
        }
        [Test] public void AllThreePassagesStartAtSeaAndReachWalkableDeck()
        {
            var right = Spawn(2);
            Assert.That(right.Position.x, Is.EqualTo(18.3f).Within(.001f), "右路应从采用版海面位置开始，不能仍直接刷在旧木通道");
            Assert.That(right.Position.y, Is.InRange(-5.201f, -2.999f));
            var layout = catalog.RoundLayouts.Single(l => l.ArenaConfigNumber == 21 && l.RoundNumber == 1);
            foreach (int passage in new[] { 0, 1 })
            {
                var other = Spawn(passage);
                Assert.That(other.DockSeaEntry.Started,Is.True,"左、上方也必须从海里爬上来");
                Assert.That(layout.IsWalkable(other.Position.x,other.Position.y,catalog.Rule.EnemyContactRadius,20.5f,19.5f),Is.False);
                Call("TickDockSeaEntry",runtime,other,6.75f);
                Assert.That(layout.IsWalkable(other.Position.x,other.Position.y,catalog.Rule.EnemyContactRadius,20.5f,19.5f),Is.True);
            }
            Assert.That(runtime.Enemies.Count, Is.EqualTo(3), "上岸只能改变原出生方式，不能额外生成敌人");
        }
        [Test] public void RightPassageProgressUsesActiveSecondsWithoutAttacking()
        {
            var enemy = Spawn(2);
            Call("TickEnemies", runtime, 2.1f);
            Assert.That(enemy.Position.x, Is.EqualTo(12.55f).Within(.002f), "靠近阶段结束应停在码头边抓边，不能普通导航抄近路");
            Assert.That(runtime.PlayerHealth, Is.EqualTo(100)); Assert.That(enemy.AttackSequence, Is.Zero);
            var at = enemy.Position;
            Call("TickEnemies", runtime, 0f);
            Assert.That(enemy.Position, Is.EqualTo(at), "无活动时间不能推进上岸");
        }
        [Test] public void RemovedRightWalkwayRejectsPlayersWhileKeepingProjectilesOpen()
        {
            foreach (var layout in catalog.RoundLayouts.Where(l => l.ArenaConfigNumber == 21))
            {
                Assert.That(layout.IsWalkable(18, -4, catalog.Rule.PlayerRadius, 20.5f, 19.5f), Is.False, "撤掉通道后水面不能继续行走");
                Assert.That(layout.IsWalkable(11, -4.1f, catalog.Rule.EnemyContactRadius, 20.5f, 19.5f), Is.True, "上岸终点应在实体半径可站稳的平台内");
                Assert.That(layout.Obstacles.Any(o => !o.BlocksProjectiles && Math.Abs(18 - o.X) < o.HalfWidth && Math.Abs(-4 - o.Y) < o.HalfHeight), Is.True,
                    "新增海面边界只限行走，不能恢复弹道空气墙");
            }
        }
        [Test] public void StunAndHerdFreezeThenResumeFromSameAge()
        {
            var enemy = Spawn(2); Call("TickEnemies", runtime, .3f);
            var position = enemy.Position;
            enemy.ContentStunUntil = 10; Call("TickEnemies", runtime, .7f);
            Assert.That(enemy.DockSeaEntry.Age, Is.EqualTo(.3f).Within(.0001f));
            Assert.That(enemy.Position, Is.EqualTo(position)); enemy.ContentStunUntil = 0;
            enemy.HerdImpact = new ArcadeHerdImpactState { Remaining = 1 };
            Call("TickEnemies", runtime, .7f);
            Assert.That(enemy.DockSeaEntry.Age, Is.EqualTo(.3f).Within(.0001f));
            enemy.HerdImpact = default; Call("TickEnemies", runtime, .2f);
            Assert.That(enemy.DockSeaEntry.Age, Is.EqualTo(.5f).Within(.0001f));
        }
        [Test] public void CompletionIsWalkableAndNextFrameResumesPursuit()
        {
            var enemy = Spawn(2); enemy.ContentSpecialKind = 0;
            runtime.PlayerPosition = new Vector2(0, -3); enemy.AttackRange = 100; enemy.AttackCooldown = 0;
            Call("TickEnemies", runtime, 6.75f);
            Assert.That(enemy.Position.x, Is.EqualTo(10.9f).Within(.001f));
            Assert.That(enemy.DockSeaEntry.Started && !enemy.DockSeaEntry.Active, Is.True);
            Assert.That(enemy.AttackSequence, Is.Zero); Assert.That(runtime.PlayerHealth, Is.EqualTo(100));
            var layout = catalog.RoundLayouts.Single(l => l.ArenaConfigNumber == 21 && l.RoundNumber == 1);
            Assert.That(layout.IsWalkable(enemy.Position.x, enemy.Position.y, catalog.Rule.EnemyContactRadius, 20.5f, 19.5f), Is.True);
            enemy.AttackRange = .1f; Call("TickEnemies", runtime, .1f);
            Assert.That(enemy.Position.x, Is.LessThan(10.9f));
            enemy.Reset(); Assert.That(enemy.DockSeaEntry.Started, Is.False, "回池不能带着上次上岸终态");
        }
        [Test] public void CrowdingAndHandgloveKeepPathWhileDamageStillLands()
        {
            var enemy = Spawn(2); enemy.ContentSpecialKind = 0; enemy.Health = enemy.MaxHealth = 1000;
            Call("TickEnemies", runtime, ArcadeDockSeaEntryRules.ClimbSeconds); var position = enemy.Position;
            runtime.Enemies.Add(999, new ArcadeEnemyRuntime { Id = 999, Health = 1000, Position = position + Vector2.right * .01f });
            Call("ResolveEnemyCrowding", runtime); Assert.That(enemy.Position, Is.EqualTo(position));
            runtime.Enemies.Remove(999);
            Call("DamageAndKnockbackEnemies", runtime, position - Vector2.right, 2f, 1, 2f);
            Assert.That(enemy.Health, Is.EqualTo(999), "双脚登台后必须恢复受伤");
            Assert.That(Vector2.Distance(enemy.Position, position), Is.LessThan(.0001f), "登台伤害可命中，但不能打断既有上岸末段路径");
        }

        /// <summary>游近、抓边与尚未登台期间，武器/范围/火焰均不能消耗生命、护甲或留下受伤标记。</summary>
        [TestCase(0f)] [TestCase(2.1f)] [TestCase(3.4f)] [TestCase(5.74f)]
        public void BeforeLanding_RejectsAllIncomingDamage(float age)
        {
            var enemy=Spawn(2); enemy.ContentSpecialKind=0; enemy.Health=enemy.MaxHealth=1000; enemy.ArmorHealth=20;
            Call("TickEnemies",runtime,age);
            Call("DamageEnemy",enemy,10);
            Call("DamageEnemyFromFlame",enemy,10);
            Call("DamageEnemiesInRadius",runtime,enemy.Position,2f,10);
            Assert.That(enemy.Health,Is.EqualTo(1000)); Assert.That(enemy.ArmorHealth,Is.EqualTo(20));
            Assert.That(enemy.FlameHitRemainingSeconds,Is.Zero); Assert.That(enemy.LastDamageWasFlame,Is.False);
        }

        [TestCase("basic",0)] [TestCase("rocket",2)] [TestCase("raygun",2)] [TestCase("grenade",2)] [TestCase("piercing",2)]
        public void WaterEnemy_DoesNotConsumePlayerProjectile(string weapon,int level)
        {
            var enemy=Spawn(2); enemy.ContentSpecialKind=0; enemy.Health=enemy.MaxHealth=1000;
            runtime.Projectiles.Add(900,new ArcadeProjectileRuntime{Id=900,FromPlayerWeapon=true,SourceWeaponId="arcade.weapon."+weapon,
                WeaponPowerLevel=level,TrackingEnemyId=enemy.Id,Position=enemy.Position,Velocity=Vector2.right,RemainingSeconds=3,RemainingHits=1,Damage=10});
            Call("TickProjectiles",runtime,.01f);
            Assert.That(enemy.Health,Is.EqualTo(1000));
            Assert.That(runtime.Projectiles.ContainsKey(900),Is.True,"水中目标不能拦截弹丸");
            if(weapon=="rocket")Assert.That(runtime.Projectiles[900].TrackingEnemyId,Is.Zero,"追踪不能锁住水中目标");
            Call("TickEnemies",runtime,ArcadeDockSeaEntryRules.ClimbSeconds);
            Call("DamageEnemy",enemy,10); Assert.That(enemy.Health,Is.EqualTo(990),"登台当帧恢复受伤");
        }
    }
}

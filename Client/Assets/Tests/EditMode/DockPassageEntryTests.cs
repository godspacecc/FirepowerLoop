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
    /// <summary>通过真实出生和敌人活动入口验证码头地面通道入场，不以动画预览替代战斗规则。</summary>
    public sealed class DockPassageEntryTests
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
        /// <summary>任何方向在通道内都不能被普通、范围或火焰伤害提前消耗生命、护甲和背桶。</summary>
        [Test] public void BothPassagesRejectDamageBeforeEnteringDeck()
        {
            foreach(int passage in new[]{0,1})
            {
                var e=Spawn(passage);e.Health=e.MaxHealth=1000;e.ArmorHealth=20;e.ContentSpecialKind=1;
                Call("DamageEnemy",e,10);Call("DamageEnemyFromFlame",e,10);
                Call("DamageEnemiesInRadius",runtime,e.Position,2f,10);
                Call("TryDamageCarrierBarrel",runtime,e,10);
                Assert.That(e.Health,Is.EqualTo(1000));Assert.That(e.ArmorHealth,Is.EqualTo(20));
                Assert.That(e.CarriedBarrelHealth,Is.Zero);Assert.That(e.FlameHitRemainingSeconds,Is.Zero);
            }
        }
        /// <summary>真实普通及进化弹丸可穿过入场敌人，追踪弹不锁定它们。</summary>
        [Test] public void BothPassagesLetEveryProjectileContinue()
        {
            foreach(int passage in new[]{0,1})foreach(string weapon in new[]{"basic","rocket","raygun","grenade","piercing"})
            {
                runtime.Enemies.Clear();runtime.Projectiles.Clear();var e=Spawn(passage);e.ContentSpecialKind=0;e.Health=1000;
                runtime.Projectiles.Add(900,new ArcadeProjectileRuntime{Id=900,FromPlayerWeapon=true,SourceWeaponId="arcade.weapon."+weapon,
                    WeaponPowerLevel=weapon=="basic"?0:2,TrackingEnemyId=e.Id,Position=e.Position,Velocity=Vector2.right,
                    RemainingSeconds=3,RemainingHits=1,Damage=10});
                Call("TickProjectiles",runtime,.01f);
                Assert.That(e.Health,Is.EqualTo(1000),weapon+"不能伤害通道内敌人");
                Assert.That(runtime.Projectiles.ContainsKey(900),Is.True,weapon+"不能在入场敌人身上消失");
                Assert.That(runtime.Projectiles[900].Position.x,Is.GreaterThan(e.Position.x),weapon+"不能卡在入场敌人身上");
                if(weapon=="rocket")Assert.That(runtime.Projectiles[900].TrackingEnemyId,Is.Zero);
            }
        }
        /// <summary>真实活动移动跨过主平台边界后立刻恢复伤害；随后被推回通道不能重新无敌。</summary>
        [Test] public void WalkingInsideUnlocksDamagePermanently()
        {
            foreach(int passage in new[]{0,1})
            {
                runtime.Enemies.Clear();var e=Spawn(passage);e.ContentSpecialKind=0;e.Health=e.MaxHealth=1000;
                Vector2 entry=e.Position;runtime.PlayerPosition=entry; // 玩家堵入口也不能让入场敌人停下互相无敌。
                for(int i=0;i<160;i++){Call("TickEnemies",runtime,.1f);Call("DamageEnemy",e,1);if(e.Health<1000)break;}
                Assert.That(e.Health,Is.EqualTo(999),"入场后应解除免疫");e.Health=1000;
                float radius=catalog.Rule.EnemyContactRadius;
                Assert.That(e.Position.x,Is.InRange(-14+radius-.001f,12-radius+.001f));
                Assert.That(e.Position.y,Is.InRange(-11.5f+radius-.001f,11.5f-radius+.001f));
                Call("DamageEnemy",e,10);Assert.That(e.Health,Is.EqualTo(990));
                e.Position=entry;Call("DamageEnemy",e,10);Assert.That(e.Health,Is.EqualTo(980));
            }
        }
        /// <summary>近战或远程玩家贴入口时，入场敌人先走入平台，不能站在免疫区输出。</summary>
        [Test] public void EntryCannotAttackEvenWithPlayerWithinReach()
        {
            foreach(int passage in new[]{0,1})foreach(var kind in new[]{global::EnemyAttackKind.Projectile,global::EnemyAttackKind.Melee})
            {
                runtime.Enemies.Clear();runtime.Projectiles.Clear();runtime.PlayerHealth=100;
                var e=Spawn(passage);e.ContentSpecialKind=0;e.AttackKind=kind;e.AttackRange=100;e.AttackCooldown=0;e.MeleeWindupSeconds=0;
                runtime.PlayerPosition=e.Position;Call("TickEnemies",runtime,.1f);
                Assert.That(e.AttackSequence,Is.Zero);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(runtime.Projectiles,Is.Empty);
            }
        }
        /// <summary>零时间及受控期间保持入场位置，恢复后继续向平台移动。</summary>
        [Test] public void EntryFreezesAndResumesWithoutLosingProtection()
        {
            foreach(int passage in new[]{0,1})
            {
                runtime.Enemies.Clear();var e=Spawn(passage);e.ContentSpecialKind=0;var position=e.Position;
                foreach(float dt in new[]{0f,-1f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})
                {Call("TickDockPassageEntry",runtime,e,dt);Assert.That(e.Position,Is.EqualTo(position));}
                e.ContentStunUntil=10;Call("TickEnemies",runtime,.5f);Assert.That(e.Position,Is.EqualTo(position));
                e.ContentStunUntil=0;e.HerdImpact=new ArcadeHerdImpactState{Remaining=1};Call("TickEnemies",runtime,.5f);Assert.That(e.Position,Is.EqualTo(position));
                e.HerdImpact=default;Call("TickEnemies",runtime,.1f);Assert.That(Vector2.Distance(e.Position,position),Is.GreaterThan(.001f));
                int health=e.Health;Call("DamageEnemy",e,1);Assert.That(e.Health,Is.EqualTo(health));
            }
        }
        /// <summary>范围击退与拥挤解算不能把入场敌人提前推入场地或偏离通道。</summary>
        [Test] public void KnockbackAndCrowdingPreserveEntryPath()
        {
            foreach(int passage in new[]{0,1})
            {
                runtime.Enemies.Clear();var e=Spawn(passage);e.ContentSpecialKind=0;e.Health=1000;var position=e.Position;
                Call("DamageAndKnockbackEnemies",runtime,position-Vector2.right,2f,10,10f);
                runtime.Enemies.Add(999,new ArcadeEnemyRuntime{Id=999,Health=1000,Position=position+Vector2.right*.01f});
                Call("ResolveEnemyCrowding",runtime);
                ArcadeHerdImpactRules.Begin(ref e.HerdImpact,1f,0f);Call("TickHerdImpacts",runtime,.1f);
                Assert.That(e.Position,Is.EqualTo(position));Assert.That(e.Health,Is.EqualTo(1000));
            }
        }
        /// <summary>解除入场保护后，真实武器依旧先打背桶，两发基础弹独立爆桶。</summary>
        [Test] public void EnteredCarrierBarrelBreaksWithTwoShots()
        {
            foreach(int passage in new[]{0,1})
            {
                runtime.Enemies.Clear();runtime.Projectiles.Clear();var e=Spawn(passage);e.ContentSpecialKind=1;e.Health=e.MaxHealth=1000;
                runtime.PlayerPosition=Vector2.zero;
                for(int i=0;i<160&&e.DockPassageEntering;i++)Call("TickEnemies",runtime,.1f);
                Assert.That(e.DockPassageEntering,Is.False);
                for(int shot=0;shot<2;shot++)
                {
                    runtime.Projectiles.Add(900,new ArcadeProjectileRuntime{Id=900,FromPlayerWeapon=true,Position=e.Position,
                        Velocity=Vector2.right,RemainingSeconds=3,RemainingHits=1,Damage=36});
                    Call("TickProjectiles",runtime,.01f);Assert.That(runtime.Projectiles,Is.Empty);
                    if(shot==0)Assert.That(e.CarriedBarrelHealth,Is.EqualTo(4));
                }
                Assert.That(e.ContentSpecialKind,Is.Zero);
            }
        }
        /// <summary>全屏炸弹也遵守入场保护，只清理已经走入战斗区的目标。</summary>
        [Test] public void BombOnlyClearsEnemiesThatHaveEntered()
        {
            var protectedIds=new[]{Spawn(0).Id,Spawn(1).Id,Spawn(2).Id};
            var ordinary=Spawn(0);ordinary.ContentSpecialKind=0;
            for(int i=0;i<160&&ordinary.DockPassageEntering;i++)Call("TickDockPassageEntry",runtime,ordinary,.1f);
            Assert.That(ordinary.DockPassageEntering,Is.False);int ordinaryId=ordinary.Id;
            runtime.Bombs=1;Call("UseBomb",runtime);
            Assert.That(runtime.Bombs,Is.Zero);Assert.That(runtime.Enemies.ContainsKey(ordinaryId),Is.False);
            foreach(int id in protectedIds)Assert.That(runtime.Enemies.ContainsKey(id),Is.True,"未入场目标不能被炸弹提前清除");
        }
        /// <summary>回池后复用为普通单位，不能继承上次通道免疫。</summary>
        [Test] public void PoolResetRemovesEntryProtection()
        {
            var e=Spawn(0);e.Reset();e.Health=e.MaxHealth=100;
            Call("DamageEnemy",e,10);Assert.That(e.Health,Is.EqualTo(90));
        }
    }
}

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
    /// <summary>以正式配置和战斗系统验证海岛弹丸边界，不加载场景或写入玩家存档。</summary>
    public sealed class IslandProjectileBoundaryTests
    {
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeRunModel run;
        private ArcadeConfigCatalogDefinition catalog;
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            architecture = new Game();
            run = new ArcadeRunModel(); architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            var data = new ArcadeRunSaveData { RunId="opening-test", RemainingLives=3, IsCasualRoute=true,
                ArenaConfigNumber=1, NextArenaNumber=1, Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=1} };
            run.CommitSafeNode(data); run.EnterArena();
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime{ArenaNumber=1,PlayerMaxHealth=100,PlayerHealth=100};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            typeof(ArcadeCombatSystem).GetField("catalog",Flags).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("arena",Flags).SetValue(system,catalog.Arenas[0]);
            typeof(ArcadeCombatSystem).GetField("random",Flags).SetValue(system,new System.Random(37));
            // 只推进内存中的权威弹道，表现端不 Begin，不启动场景资源或存档事务。
            typeof(ArcadeCombatSystem).GetField("view",Flags).SetValue(system,new ArcadeCombatViewController());
        }
        private object Call(string method, params object[] args) => typeof(ArcadeCombatSystem).GetMethod(method, Flags).Invoke(system, args);

        /// <summary>岸边朝四侧深海开火，越过旧矩形边界后仍按正式武器寿命飞行。</summary>
        [TestCase(1, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 1)]
        [TestCase(0, -1)]
        public void IslandShot_CrossesArenaEdgeAndExpiresAtWeaponLifetime(int x, int y)
        {
            var direction = new Vector2(x, y);
            var region = new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, 1);
            var shore = region.Constrain(x * 100, y * 100, catalog.Rule.PlayerRadius);
            var origin = new Vector2(shore.X, shore.Y);
            var weapon = catalog.Weapons.Single(value => value.Id == catalog.Rule.BaseWeaponId);
            runtime.Projectiles.Add(901, new ArcadeProjectileRuntime
            {
                Id = 901, Position = origin, Velocity = direction * weapon.ProjectileSpeed,
                RemainingSeconds = weapon.ProjectileLifetimeSeconds, Behavior = weapon.Behavior,
                SourceWeaponId = weapon.Id, FromPlayerWeapon = true, RemainingHits = 1
            });

            Call("TickProjectiles", runtime, .3f);
            Assert.That(runtime.Projectiles.ContainsKey(901), Is.True, "深海背景中的弹丸不能在旧场地边界被提前回收");
            var projectile = runtime.Projectiles[901];
            Assert.That(Mathf.Max(Mathf.Abs(projectile.Position.x), Mathf.Abs(projectile.Position.y)), Is.GreaterThan(19.5f));
            Call("TickProjectiles", runtime, weapon.ProjectileLifetimeSeconds - .4f);
            Assert.That(runtime.Projectiles.ContainsKey(901), Is.True, "未到武器寿命时仍须保留");
            Assert.That(Vector2.Distance(projectile.Position, origin), Is.EqualTo(weapon.ProjectileSpeed * (weapon.ProjectileLifetimeSeconds - .1f)).Within(.001f));
            Call("TickProjectiles", runtime, .11f);
            Assert.That(runtime.Projectiles.ContainsKey(901), Is.False, "寿命耗尽仍须正常回收");
        }

        /// <summary>宝藏房、挑战及其他主题同样放行空旷边缘，按寿命回收。</summary>
        [TestCase("treasure")]
        [TestCase("challenge")]
        [TestCase("other-arena")]
        public void NonIslandShot_CrossesArenaEdgeAndExpiresAtLifetime(string sceneKind)
        {
            runtime.InTreasureRoom = sceneKind == "treasure";
            runtime.IsChallenge = sceneKind == "challenge";
            if (sceneKind == "other-arena")
                typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(system, catalog.Arenas.Single(value => value.ArenaNumber == 2));
            runtime.Projectiles.Add(901, new ArcadeProjectileRuntime
            {
                Id = 901, Position = new Vector2(catalog.Rule.ArenaHalfWidth - .1f, 0),
                Velocity = Vector2.right * 16, RemainingSeconds = 1.6f, RemainingHits = 1
            });
            Call("TickProjectiles", runtime, .1f);
            Assert.That(runtime.Projectiles.ContainsKey(901), Is.True);
            Call("TickProjectiles", runtime, 1.51f);
            Assert.That(runtime.Projectiles.ContainsKey(901), Is.False);
        }

        /// <summary>放行深海边界不能放行岛上的真实障碍。</summary>
        [Test]
        public void IslandShot_StillStopsAtRealObstacle()
        {
            var obstacles = (System.Collections.Generic.IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor", runtime);
            Assert.That(obstacles.Count, Is.GreaterThan(0));
            runtime.Projectiles.Add(901, new ArcadeProjectileRuntime
            {
                Id = 901, Position = obstacles[0].Center, RemainingSeconds = 1.6f,
                RemainingHits = 1, FromPlayerWeapon = true, SourceWeaponId = "arcade.weapon.basic"
            });
            Call("TickProjectiles", runtime, .01f);
            Assert.That(runtime.Projectiles.ContainsKey(901), Is.False);
        }

    }
}

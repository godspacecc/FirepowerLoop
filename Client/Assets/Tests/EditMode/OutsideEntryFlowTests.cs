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
    /// <summary>通过真实出生和敌人活动入口验证普通敌人场外接近，不以动画预览替代战斗规则。</summary>
    public sealed class OutsideEntryFlowTests
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
            app.GetModel<ArcadeRunModel>().CommitSafeNode(new ArcadeRunSaveData { RunId = "dock-sea-entry", IsCasualRoute = true, ArenaConfigNumber = 22, NextArenaNumber = 2, CompletedArenaCount = 1 });
            app.GetModel<ArcadeRunModel>().EnterArena();
            runtime = new ArcadeCombatRuntime { ArenaNumber = 2, PlayerHealth = 100, PlayerMaxHealth = 100, PlayerPosition = Vector2.zero };
            app.GetModel<ArcadeCombatModel>().Begin(runtime);
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(app);
            var arena = catalog.Arenas.Single(a => a.ArenaNumber == 22);
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
        [Test] public void RealSpawnContinuousMovementAndPoolReset()
        {
            var enemy=Spawn(0);Assert.That(enemy.OutsideEntry.Active,Is.True);Assert.That(enemy.Position.x,Is.LessThan(-30.5f));
            float start=enemy.Position.x;Call("TickEnemies",runtime,.1f);Assert.That(enemy.Position.x,Is.GreaterThan(start));
            Assert.That(enemy.Position.x,Is.LessThan(-30.5f));Assert.That(enemy.CanBeAttacked,Is.True);
            Call("TickEnemies",runtime,100f);Assert.That(enemy.Position,Is.EqualTo(new Vector2(-28,-14)));Assert.That(enemy.OutsideEntry.Active,Is.False);
            enemy.Reset();Assert.That(enemy.OutsideEntry.Distance,Is.Zero);Assert.That(enemy.OutsideEntry.Active,Is.False);
        }
        [Test] public void FreezeCrowdingAndDamageDoNotClampOutside()
        {
            var enemy=Spawn(0);enemy.Health=enemy.MaxHealth=1000;var start=enemy.Position;
            enemy.ContentStunUntil=100;Call("TickEnemies",runtime,1f);Assert.That(enemy.Position,Is.EqualTo(start));enemy.ContentStunUntil=0;
            runtime.Enemies.Add(999,new ArcadeEnemyRuntime{Id=999,Health=1000,Position=start+Vector2.right*.01f});
            Call("ResolveEnemyCrowding",runtime);Assert.That(enemy.Position,Is.EqualTo(start));runtime.Enemies.Remove(999);
            Call("DamageAndKnockbackEnemies",runtime,start-Vector2.right,2f,1,2f);
            Assert.That(enemy.Health,Is.EqualTo(999));Assert.That(enemy.Position,Is.EqualTo(start));
            Assert.That((Vector2)Call("OutsideEntryDropPosition",enemy),Is.EqualTo(new Vector2(-28,-14)));
        }
        [Test] public void ChallengeFallbackUsesRealSpawnAndKeepsSpecialArrival()
        {
            runtime.IsChallenge=true;var enemy=Spawn(0);Assert.That(enemy.OutsideEntry.Active,Is.True);
            Assert.That(Mathf.Abs(enemy.Position.x)>30.5f||Mathf.Abs(enemy.Position.y)>30.5f,Is.True);
            var special=new ArcadeEnemyRuntime{ContentSpecialKind=18,StageEnemy=new ArcadeStageEnemyState{Phase=1}};
            Call("InitializeOutsideEntry",runtime,special);Assert.That(special.OutsideEntry.Active,Is.False);
            var carrier=new ArcadeEnemyRuntime{ContentSpecialKind=1};Call("InitializeOutsideEntry",runtime,carrier);Assert.That(carrier.OutsideEntry.Active,Is.True);
        }
    }
}

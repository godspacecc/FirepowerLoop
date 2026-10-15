using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AnotherDay.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>首场真实单人规则的定向回归；使用内存领域现场，不修改正式存档或地图。</summary>
    public sealed class ArcadeOpeningFlowTests
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
                ArenaConfigNumber=1, NextArenaNumber=1, Shop=new ArcadeShopSaveData{ArenaNumber=1} };
            run.CommitSafeNode(data); run.EnterArena();
            system = new ArcadeCombatSystem(); ((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime = new ArcadeCombatRuntime{ArenaNumber=1,PlayerMaxHealth=100,PlayerHealth=100};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            typeof(ArcadeCombatSystem).GetField("catalog",Flags).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("arena",Flags).SetValue(system,catalog.Arenas[0]);
            typeof(ArcadeCombatSystem).GetField("random",Flags).SetValue(system,new System.Random(37));
        }
        private object Call(string method,params object[] args) => typeof(ArcadeCombatSystem).GetMethod(method,Flags).Invoke(system,args);
        [Test]
        public void IslandEntry_FourWavesUseBothSidesAndMoveContinuouslyWithoutBorderSnap()
        {
            typeof(ArcadeCombatSystem).GetField("random",Flags).SetValue(system,new System.Random(37));
            runtime.PlayerInvulnerability=1000f;
            for(int wave=0;wave<4;wave++)
            {
                runtime.Enemies.Clear();Call("BeginWave",runtime,wave);
                int total=runtime.SpawnQueue.Count;
                Assert.That(total,Is.EqualTo(110+10*wave));
                Call("TickSpawning",runtime,1f);runtime.SpawnCooldown=0;Call("TickSpawning",runtime,1f);
                var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight);
                var water=runtime.Enemies.Values.Single(e=>e.Position.y < 0);
                var shore=runtime.Enemies.Values.Single(e=>e.Position.y > 0);
                Assert.That(region.Contains(water.Position.x,water.Position.y),Is.True);
                Assert.That(region.IsShallow(water.Position.x,water.Position.y),Is.True);
                Assert.That(region.IsShallow(shore.Position.x,shore.Position.y),Is.False);
                Assert.That(runtime.Enemies.Count+runtime.SpawnQueue.Count,Is.EqualTo(total));
                Vector2 before=water.Position;Call("TickEnemies",runtime,.05f);
                Assert.That(Vector2.Distance(before,water.Position),Is.LessThanOrEqualTo(water.Speed*.05f+.001f));
                runtime.PlayerPosition=water.Position+Vector2.up*.25f;
                int health=runtime.PlayerHealth;runtime.PlayerInvulnerability=0;water.AttackCooldown=0;
                Call("TickEnemies",runtime,.05f);
                Assert.That(water.EnteringIsland,Is.True);
                Assert.That(runtime.PlayerHealth,Is.LessThan(health),"入场阶段仍按原接触规则攻击水侧玩家");
                runtime.PlayerInvulnerability=1000f;runtime.PlayerPosition=Vector2.zero;
                for(int i=0;i<500;i++)Call("TickEnemies",runtime,.05f);
                Assert.That(water.EnteringIsland,Is.False);
                Assert.That(region.IsShallow(water.Position.x,water.Position.y),Is.False);
                Assert.That(Mathf.Abs(shore.Position.y),Is.LessThan(catalog.Rule.ArenaHalfHeight));
                int spawned=runtime.Enemies.Count;
                while(runtime.SpawnQueue.Count>0)
                {
                    runtime.Enemies.Clear();runtime.SpawnCooldown=0;Call("TickSpawning",runtime,1f);
                    Assert.That(runtime.Enemies.Count,Is.EqualTo(1));spawned++;
                    // 弹丸等共享实体序号的奇偶变化不能让入口长期只用一侧。
                    runtime.NextEntityId++;
                    var e=runtime.Enemies.Values.Single();
                    Assert.That(e.Position.y<0,Is.EqualTo((spawned&1)==1));
                }
                Assert.That(spawned,Is.EqualTo(total));
                runtime.Enemies.Clear();
                if(wave==2)
                {
                    Call("TickTreasureRoom",runtime,0f);Assert.That(runtime.InTreasureRoom,Is.True);
                    Call("ReturnFromTreasure",runtime);Assert.That(runtime.WaveIndex,Is.EqualTo(3));
                }
                else {Call("TickWaveTransition",runtime,0f);Assert.That(runtime.WaveClearAwarded,Is.True);}
            }
        }
        [Test] public void NewOrdinaryWave_ReplenishesOnlyEmptyInventoryAndQueuesFullWave()
        {
            runtime.Bombs=0;runtime.Dashes=4;Call("BeginWave",runtime,1);
            Assert.That(runtime.Bombs,Is.EqualTo(1));Assert.That(runtime.Dashes,Is.EqualTo(4));
            Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(120));
        }
        [TestCase("weapon.short-sword")]
        [TestCase("weapon.greatsword")]
        [TestCase("weapon.knife")]
        [TestCase("weapon.axe")]
        [TestCase("weapon.pickaxe")]
        public void LegacyMeleeEquipment_UsesBaselineGun(string id)
        {
            var data = run.Current;
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId] = id;
            run.CommitSafeNode(data);
            var args = new object[] { runtime, default(ArcadeWeaponDefinition) };
            Assert.That((bool)Call("TryGetWeapon", args), Is.True);
            Assert.That(((ArcadeWeaponDefinition)args[1]).Id, Is.EqualTo(catalog.Rule.BaseWeaponId));
            runtime.TemporaryWeaponId = id; runtime.TemporaryWeaponRemaining = 10f;
            Assert.That((bool)Call("TryGetWeapon", args), Is.True);
            Assert.That(((ArcadeWeaponDefinition)args[1]).Id, Is.EqualTo(catalog.Rule.BaseWeaponId));
        }

        [Test] public void LegacyMeleeOffer_CannotBePurchased()
        {
            var data = run.Current;
            data.RunGold = 100;
            data.Shop.Offers.Add(new ArcadeShopOfferSaveData { OfferId="legacy-melee", ProductId="weapon.knife",
                Kind=ArcadePurchaseKind.Equipment, EquipmentSlotId=ArcadeRunDefaults.WeaponSlotId, Price=1 });
            run.CommitSafeNode(data);
            var shop = new ArcadeRunSystem(); ((ICanSetArchitecture)shop).SetArchitecture(architecture);
            Assert.That(shop.TryPurchase("legacy-melee", out _, out var error), Is.False);
            Assert.That(error, Does.Contain("近战"));
            Assert.That(run.Current.RunGold, Is.EqualTo(100));
        }
        [Test] public void PendingQueue_PreventsFalseClearEvenWhenNoEnemyIsAlive()
        {
            Call("BeginWave",runtime,0);Call("TickWaveTransition",runtime,200f);
            Assert.That(runtime.WaveClearAwarded,Is.False);Assert.That(runtime.WaveIndex,Is.Zero);
        }
        [Test] public void Rest_RequiresReleaseThenFireAndOtherwiseUsesConfiguredTimeout()
        {
            Call("BeginWave",runtime,0);runtime.SpawnQueue.Clear();runtime.Firing=true;
            Call("TickWaveTransition",runtime,0f);Call("TickWaveTransition",runtime,2f);
            Assert.That(runtime.WaveIndex,Is.Zero);
            runtime.Firing=false;Call("TickWaveTransition",runtime,.1f);
            runtime.Firing=true;Call("TickWaveTransition",runtime,.1f);
            Assert.That(runtime.WaveIndex,Is.EqualTo(1));Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(120));
            runtime.SpawnQueue.Clear();runtime.Firing=false;Call("TickWaveTransition",runtime,0f);
            Call("TickWaveTransition",runtime,120f);Assert.That(runtime.WaveIndex,Is.EqualTo(2));
        }
        [Test] public void FinalClear_ShowsTwoExitsWithoutStartingAnotherWave()
        {
            Call("BeginWave",runtime,3);runtime.SpawnQueue.Clear();Call("TickWaveTransition",runtime,0f);
            Assert.That(runtime.AwaitingExit,Is.True);Assert.That(runtime.WaveTransitionRemaining,Is.EqualTo(300));
            Assert.That(catalog.Arenas[0].NextArenaNumbers,Is.EquivalentTo(new[]{2,3}));
        }
        [Test] public void WaterMovement_UsesSameConfiguredZoneAsPresentation()
        {
            var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight);
            region.Entry(0,.5f,catalog.Rule.EnemyContactRadius,out var water,out _);
            runtime.PlayerPosition=new Vector2(water.X,water.Y);runtime.MoveDirection=Vector2.right;
            Call("TickPlayer",runtime,1f);
            Assert.That(runtime.PlayerPosition.x,Is.EqualTo(catalog.Rule.PlayerMoveSpeed*.55f).Within(.001f));
            runtime.PlayerPosition=Vector2.zero;Call("TickPlayer",runtime,1f);
            Assert.That(runtime.PlayerPosition.x,Is.EqualTo(catalog.Rule.PlayerMoveSpeed).Within(.001f));
        }
        [Test] public void IslandKillCountsOnceUsesMultiplierAndDoesNotAdvanceTreasureProgress()
        {
            run.SetTransientTreasureScore(new ArcadeTreasureScoreState(199950,2,4,3));
            for(int i=0;i<catalog.Rule.MaxDrops;i++)runtime.Drops.Add(i,new ArcadeDropRuntime());
            var enemy=new ArcadeEnemyRuntime{Id=1,Position=Vector2.zero};runtime.Enemies.Add(1,enemy);
            Call("KillEnemy",runtime,enemy,false);
            Assert.That(run.Current.Score,Is.EqualTo(200150));Assert.That(run.Current.RemainingLives,Is.EqualTo(4));
            Assert.That(run.Current.MultiplierProgress,Is.EqualTo(4));Assert.That(run.Current.ScoreMultiplier,Is.EqualTo(2));
            Call("KillEnemy",runtime,enemy,false);Assert.That(run.Current.Score,Is.EqualTo(200150));
            var hazard=new ArcadeEnemyRuntime{Id=2};runtime.Enemies.Add(2,hazard);
            Call("KillEnemy",runtime,hazard,true);Assert.That(run.Current.Score,Is.EqualTo(200150));
        }
        [Test] public void BombProtectsForThreeSecondsWithoutShorteningLongProtectionOrRewardingInvalidUse()
        {
            runtime.Bombs=1;runtime.PlayerInvulnerability=5;Call("UseBomb",runtime);
            Assert.That(runtime.PlayerInvulnerability,Is.EqualTo(5));Assert.That(runtime.Bombs,Is.Zero);
            runtime.Bombs=1;runtime.PlayerInvulnerability=0;Call("UseBomb",runtime);
            Assert.That(runtime.PlayerInvulnerability,Is.EqualTo(3));Call("DamagePlayer",runtime,20);
            Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
            runtime.PlayerInvulnerability=0;runtime.InTreasureRoom=true;runtime.Bombs=1;Call("UseBomb",runtime);
            Assert.That(runtime.PlayerInvulnerability,Is.Zero);Assert.That(runtime.Bombs,Is.EqualTo(1));
            runtime.InTreasureRoom=false;runtime.Bombs=0;Call("UseBomb",runtime);Assert.That(runtime.PlayerInvulnerability,Is.Zero);
        }
        [Test] public void DashKillsSweptOrdinaryTargetButNotOffPathBossOrDeepSea()
        {
            runtime.Dashes=1;runtime.MoveDirection=Vector2.right;Call("UseDash",runtime);
            runtime.Enemies.Add(1,new ArcadeEnemyRuntime{Id=1,Position=new Vector2(2,0)});
            runtime.Enemies.Add(2,new ArcadeEnemyRuntime{Id=2,Position=new Vector2(2,4)});
            runtime.Enemies.Add(3,new ArcadeEnemyRuntime{Id=3,Position=new Vector2(2,0),BossId="boss"});
            Call("TickPlayer",runtime,.35f);
            Assert.That(runtime.Enemies.ContainsKey(1),Is.False);Assert.That(runtime.Enemies.ContainsKey(2),Is.True);
            Assert.That(runtime.Enemies.ContainsKey(3),Is.True);Assert.That(run.Current.Score,Is.EqualTo(100));
            var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight);
            var edge=region.Constrain(30,0,catalog.Rule.PlayerRadius);runtime.PlayerPosition=new Vector2(edge.X,edge.Y);
            var outer=region.Constrain(30,0,0);
            runtime.Enemies.Add(4,new ArcadeEnemyRuntime{Id=4,Position=new Vector2(outer.X+.1f,outer.Y)});
            Call("TickPlayer",runtime,.35f);Assert.That(runtime.Enemies.ContainsKey(4),Is.True);
            Assert.That(region.Contains(runtime.PlayerPosition.x,runtime.PlayerPosition.y),Is.True);
        }
        [Test] public void ThirdClearGuaranteesTreasureAndCannotRaceRestTimer()
        {
            Call("BeginWave",runtime,2);runtime.SpawnQueue.Clear();runtime.PlayerPosition=Vector2.zero;
            Call("TickTreasureRoom",runtime,0f);
            Assert.That(runtime.InTreasureRoom,Is.True);Assert.That(runtime.TreasureDiscovered,Is.True);
            Call("TickWaveTransition",runtime,500f);Assert.That(runtime.WaveIndex,Is.EqualTo(2));
            Call("TickTreasureRoom",runtime,60f);
            Assert.That(runtime.InTreasureRoom,Is.False);Assert.That(runtime.WaveIndex,Is.EqualTo(3));
            Assert.That(runtime.SpawnQueue.Count,Is.EqualTo(140));
            Call("TickTreasureRoom",runtime,0f);Assert.That(runtime.InTreasureRoom,Is.False);
        }
        [Test] public void Campfire_OnlyBurnsFourthRoundAndRespectsInvulnerability()
        {
            var p=catalog.Arenas[0].Opening;runtime.PlayerPosition=new Vector2(p.CampfireX,p.CampfireY);
            runtime.WaveIndex=2;Call("TickOpeningFire",runtime,1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));
            runtime.WaveIndex=3;Call("TickOpeningFire",runtime,1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(88));
            Call("TickOpeningFire",runtime,1f);Assert.That(runtime.PlayerHealth,Is.EqualTo(88));
            runtime.PlayerInvulnerability=0;runtime.InTreasureRoom=true;Call("TickOpeningFire",runtime,1f);
            Assert.That(runtime.PlayerHealth,Is.EqualTo(88));
        }
        [Test] public void OrdinaryTreasure_AwardsPersonalMultiplierScoreWithoutCash()
        {
            var drop=catalog.Drops.Single(d=>d.Id=="arcade.drop.gold.small");
            for(int i=0;i<11;i++)Call("ApplyDrop",runtime,drop);
            Assert.That(run.Current.Score,Is.EqualTo(1200));Assert.That(run.Current.ScoreMultiplier,Is.EqualTo(2));
            Assert.That(runtime.ArenaGold,Is.Zero);Assert.That(run.Current.RunGold,Is.Zero);
        }
        [Test] public void DualPlayerTotals_AddFiftyInsteadOfMultiplyingSolo()
        {
            for(int i=1;i<=4;i++)Assert.That(ArcadeOpeningRules.OrdinaryRoundTotal(i,2),Is.EqualTo(150+10*i));
            Assert.That(ArcadeOpeningRules.OrdinaryRoundTotal(40,1),Is.EqualTo(400));
        }
    }
}

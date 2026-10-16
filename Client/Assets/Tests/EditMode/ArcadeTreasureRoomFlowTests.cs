using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>单人真实区域与文件提交边界；只使用临时文件，不启动正式地图。</summary>
    public sealed class ArcadeTreasureRoomFlowTests
    {
        private Game architecture;
        private ArcadeCombatSystem system;
        private ArcadeRunModel run;
        private ArcadeRunSystem runs;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        private string root;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            root=Path.Combine(Path.GetTempPath(),"ArcadeTreasureFlow-"+Guid.NewGuid().ToString("N"));
            architecture=new Game();run=new ArcadeRunModel();architecture.RegisterModel(run);
            architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            runs=new ArcadeRunSystem();architecture.RegisterSystem(runs);runs.ConfigureForTests(root);
            var data=new ArcadeRunSaveData{RunId=Guid.NewGuid().ToString("N"),RemainingLives=3,IsCasualRoute=true,ArenaConfigNumber=1,
                UpdatedAtUtc=DateTime.UtcNow.ToString("O"),Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=1}};
            data.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId]=ArcadeRunDefaults.BasicWeaponId;
            Assert.That(new ArcadeRunFileStore(root).Save(data,out error),Is.True,error);
            run.CommitSafeNode(data);run.EnterArena();
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,WaveIndex=2,PlayerPosition=new Vector2(-15,-15),PlayerHealth=100,PlayerMaxHealth=100};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            typeof(ArcadeCombatSystem).GetField("catalog",Flags).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("arena",Flags).SetValue(system,catalog.Arenas[0]);
            typeof(ArcadeCombatSystem).GetField("view",Flags).SetValue(system,new TestView());
        }
        [TearDown] public void Cleanup(){architecture.GetModel<ArcadeCombatModel>().Clear();if(Directory.Exists(root))Directory.Delete(root,true);}
        private void Tick(float seconds=0)=>typeof(ArcadeCombatSystem).GetMethod("TickTreasureRoom",Flags).Invoke(system,new object[]{runtime,seconds});
        private void Reward(int index)
        {runtime.PlayerPosition=new Vector2(runtime.TreasureRoom.Parameters.Rewards[index].X,runtime.TreasureRoom.Parameters.Rewards[index].Y);Tick();}
        [Test] public void DiscoveryDoesNotEnterUnclearedWave()
        {
            var rules=catalog.TreasureRooms.First();
            runtime.PlayerPosition=ArcadeIslandLayout.GetLandmarkPosition(new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,
                catalog.Rule.ArenaHalfHeight,3),new Vector2(rules.EntryX,rules.EntryY),rules.EntryRadius+catalog.Rule.PlayerRadius);
            runtime.SpawnQueue.Enqueue("pending");Tick();Assert.That(runtime.TreasureDiscovered,Is.True);
            Assert.That(runtime.InTreasureRoom,Is.False);Assert.That(runtime.TreasureRoom.Started,Is.False);
        }
        [Test] public void ClearAllowsEntryWithOneFixedSet()
        {Tick();Assert.That(runtime.InTreasureRoom,Is.True);Assert.That(runtime.TreasureRoom.RemainingRewards.Length,Is.EqualTo(18));Assert.That(runtime.PlayerPosition,Is.EqualTo(new Vector2(0,-4)));}
        [Test] public void GoldIsPersonalScore_NotShoppingCash()
        {Tick();Reward(0);Assert.That(run.Current.Score,Is.EqualTo(100));Assert.That(run.Current.RunGold,Is.Zero);Reward(0);Assert.That(run.Current.Score,Is.EqualTo(100));}
        [Test] public void FullBombDoesNotConsumeSharedPickup()
        {Tick();runtime.Bombs=9;Reward(16);Assert.That(runtime.TreasureRoom.RemainingRewards.Any(r=>r.Id==17),Is.True);runtime.Bombs=8;Reward(16);Assert.That(runtime.Bombs,Is.EqualTo(9));Assert.That(runtime.TreasureRoom.RemainingRewards.Any(r=>r.Id==17),Is.False);}
        [Test] public void TimeoutReturnsPlayerWithoutReopening()
        {Tick();Tick(60);Assert.That(runtime.InTreasureRoom,Is.False);Assert.That(runtime.PlayerPosition,Is.EqualTo(ArcadeIslandLayout.GetSpawn(4,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight)));Assert.That(runtime.WaveIndex,Is.EqualTo(3));runtime.PlayerPosition=new Vector2(-15,-15);Tick();Assert.That(runtime.InTreasureRoom,Is.False);}
        [Test] public void EarlyReturnContinuesFourthWave()
        {
            Tick();typeof(ArcadeCombatSystem).GetMethod("TickWaveTransition",Flags).Invoke(system,new object[]{runtime,2f});Assert.That(runtime.WaveIndex,Is.EqualTo(2));
            runtime.PlayerPosition=new Vector2(0,-5.5f);Tick();Assert.That(runtime.InTreasureRoom,Is.False);
            typeof(ArcadeCombatSystem).GetMethod("TickWaveTransition",Flags).Invoke(system,new object[]{runtime,2f});
            typeof(ArcadeCombatSystem).GetMethod("TickWaveTransition",Flags).Invoke(system,new object[]{runtime,.1f});
            runtime.Firing = true; // 首场休整已先经历松开帧，主动射击结束等待。
            typeof(ArcadeCombatSystem).GetMethod("TickWaveTransition",Flags).Invoke(system,new object[]{runtime,2f});Assert.That(runtime.WaveIndex,Is.EqualTo(3));
        }
        [TestCase(true,1)] [TestCase(false,1)] [TestCase(true,2)]
        public void ChallengesOldRunsAndOtherMapsDoNotCreateRoom(bool casual,int arenaNumber)
        {var data=run.Current;data.IsCasualRoute=casual;run.CommitSafeNode(data);run.EnterArena();runtime.IsChallenge=casual&&arenaNumber==1;typeof(ArcadeCombatSystem).GetField("arena",Flags).SetValue(system,catalog.Arenas[arenaNumber-1]);Tick();Assert.That(runtime.TreasureRoom,Is.Null);}
        [Test] public void TransientScoreDoesNotLeakToSavedNode()
        {Tick();Reward(0);var saved=new ArcadeRunFileStore(root).Inspect();Assert.That(saved.Data.Score,Is.Zero);Assert.That(run.Current.Score,Is.EqualTo(100));}
        [Test] public void RealCompletionCommitsScoreAndRejectsRepeat()
        {
            Tick();Reward(0);var c=Completion();Assert.That(runs.TryCompleteArena(c,out var saved,out var error),Is.True,error);
            Assert.That(saved.Score,Is.EqualTo(100));Assert.That(new ArcadeRunFileStore(root).Inspect().Data.Score,Is.EqualTo(100));
            Assert.That(runs.TryCompleteArena(c,out _,out _),Is.False);
        }
        [Test] public void FailedFileCommitKeepsScoreAndRetryCommitsOnce()
        {
            Tick();Reward(0);var store=new ArcadeRunFileStore(root);using(var locked=new FileStream(store.RunPath,FileMode.Open,FileAccess.Read,FileShare.None))
            {Assert.That(runs.TryCompleteArena(Completion(),out _,out _),Is.False);Assert.That(run.Phase,Is.EqualTo(ArcadeRunPhase.Arena));Assert.That(run.Current.Score,Is.EqualTo(100));}
            Assert.That(runs.TryCompleteArena(Completion(),out _,out var error),Is.True,error);Assert.That(store.Inspect().Data.Score,Is.EqualTo(100));
        }
        [Test] public void OldJsonMissingScoreFieldsDefaultsSafely()
        {
            var store=new ArcadeRunFileStore(root);var raw=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(store.RunPath));raw.Remove("Score");raw.Remove("ScoreMultiplier");raw.Remove("MultiplierProgress");File.WriteAllText(store.RunPath,raw.ToString());
            var old=store.Inspect();Assert.That(old.Status,Is.EqualTo(SaveFileStatus.Valid),old.Error);Assert.That(old.Data.ScoreMultiplier,Is.EqualTo(1));Assert.That(old.Data.Score,Is.Zero);
        }
        [Test] public void DeathResetsMultiplierAndCloneKeepsScore()
        {run.SetTransientTreasureScore(new ArcadeTreasureScoreState(1000,3,500,3));typeof(ArcadeCombatSystem).GetMethod("ResetTreasureMultiplierAfterDeath",Flags).Invoke(system,null);Assert.That(run.Current.Score,Is.EqualTo(1000));Assert.That(run.Current.ScoreMultiplier,Is.EqualTo(1));Assert.That(run.Current.Clone().MultiplierProgress,Is.Zero);}
        /// <summary>第二场使用真实目录及既有奖励房入口，不替换第一场财宝。</summary>
        private void ConfigureArmory()
        {
            var data = run.Current; data.NextArenaNumber = 2; data.ArenaConfigNumber = 2; data.CompletedArenaCount = 1;
            data.Checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = 2 };
            run.CommitSafeNode(data); run.EnterArena();
            runtime.ArenaNumber = 2;
            typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(system, catalog.Arenas.Single(a => a.ArenaNumber == 2));
            // 通过正式连续轮前事务准备第三轮，避免用缺少前序节点的假现场测试恢复。
            for (int round = 0; round < 3; round++)
                typeof(ArcadeCombatSystem).GetMethod("StartContentRound", Flags).Invoke(system, new object[] { runtime, round, null });
            Assert.That(runtime.CompletionAwaitingRetry, Is.False, "真实第三轮节点准备失败");
            runtime.SpawnQueue.Clear();
        }
        [Test] public void ArmorySecondArenaThirdWaveContainsSixSharedWeapons()
        {
            ConfigureArmory(); Tick();
            Assert.That(runtime.TreasureRoom, Is.Not.Null, "第二场军械库尚未创建");
            Assert.That(runtime.InTreasureRoom, Is.True);
            Assert.That(runtime.TreasureRoom.Parameters.Id, Is.EqualTo("arcade.armory.second"));
            Assert.That(runtime.TreasureRoom.RemainingRewards.Length, Is.EqualTo(6));
            Assert.That(runtime.TreasureRoom.RemainingRewards.All(r => (int)r.Kind == 5), Is.True);
        }
        [Test] public void ArmoryWeaponClockWaitsInsideRoomAndBeforeBattle()
        {
            ConfigureArmory(); Tick();
            runtime.TemporaryWeaponId = "arcade.weapon.rapid"; runtime.TemporaryWeaponRemaining = 25;
            typeof(ArcadeCombatSystem).GetMethod("TickTimers", Flags).Invoke(system, new object[] { runtime, 5f });
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(25), "房内不得消耗武器时间");
        }
        [Test] public void ArmoryPickupReplacesWeaponWithoutChangingScoreOrEquipment()
        {
            ConfigureArmory(); Tick(); Reward(0);
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo("arcade.weapon.rapid"));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(25));
            Assert.That(runtime.TreasureRoom.RemainingRewards.Length, Is.EqualTo(5));
            Reward(1);
            Assert.That(runtime.TemporaryWeaponId, Is.EqualTo("arcade.weapon.spread"));
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(20));
            typeof(ArcadeCombatSystem).GetMethod("SyncView", Flags).Invoke(system, new object[] { runtime });
            var testView = (TestView)typeof(ArcadeCombatSystem).GetField("view", Flags).GetValue(system);
            Assert.That(testView.WeaponId, Is.EqualTo("arcade.weapon.spread"));
            Assert.That(testView.ProductId, Is.EqualTo("arcade.weapon.spread"));
            Assert.That(run.Current.Score, Is.Zero);
            Assert.That(run.Current.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId], Is.EqualTo(ArcadeRunDefaults.BasicWeaponId));
        }
        [Test] public void ArmoryGroundWeaponsExpireAtThirtySecondsButExitRemainsUntilSixty()
        {
            ConfigureArmory(); Tick(); Tick(29);
            Assert.That(runtime.TreasureRoom.RemainingRewards.Length, Is.EqualTo(6));
            Tick(1); Assert.That(runtime.TreasureRoom.RemainingRewards, Is.Empty);
            Assert.That(runtime.InTreasureRoom, Is.True); Assert.That(runtime.TreasureRoom.Expired, Is.False);
            Tick(30); Assert.That(runtime.InTreasureRoom, Is.False); Assert.That(runtime.WaveIndex, Is.EqualTo(3));
        }
        [Test] public void ArmoryReturnStartsFourthWaveAndWeaponCountdownResumes()
        {
            ConfigureArmory(); Tick(); Reward(0);
            runtime.PlayerPosition = new Vector2(0, -5.5f); Tick();
            Assert.That(runtime.InTreasureRoom, Is.False); Assert.That(runtime.WaveIndex, Is.EqualTo(3));
            Assert.That(runtime.SpawnQueue, Is.Not.Empty);
            typeof(ArcadeCombatSystem).GetMethod("TickTimers", Flags).Invoke(system, new object[] { runtime, 1f });
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(25), "首敌预警期间不扣时");
            typeof(ArcadeCombatSystem).GetMethod("TickSpawning", Flags).Invoke(system, new object[] { runtime, 2f });
            Assert.That(runtime.Enemies, Is.Not.Empty, "使用正式生成入口进入战斗");
            typeof(ArcadeCombatSystem).GetMethod("TickTimers", Flags).Invoke(system, new object[] { runtime, 1f });
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(24));
            typeof(ArcadeCombatSystem).GetMethod("SyncTreasureView", Flags).Invoke(system, new object[] { runtime });
            var testView = (TestView)typeof(ArcadeCombatSystem).GetField("view", Flags).GetValue(system);
            Assert.That(testView.TreasureEntranceShown, Is.False, "离场后不能残留假入口");
            runtime.PlayerPosition = new Vector2(-15, -15); Tick(); Assert.That(runtime.InTreasureRoom, Is.False);
        }
        [Test] public void ArmoryFourthRoundRestoreKeepsFullWeaponUntilFirstEnemy()
        {
            ConfigureArmory(); Tick(); Reward(0);
            runtime.PlayerPosition = new Vector2(0, -5.5f); Tick();
            var checkpoint = run.Current.RoundCheckpoint;
            Assert.That(checkpoint, Is.Not.Null);
            runtime = new ArcadeCombatRuntime { ArenaNumber = 2, PlayerHealth = 100, PlayerMaxHealth = 100 };
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            typeof(ArcadeCombatSystem).GetMethod("StartContentRound", Flags).Invoke(system, new object[] { runtime, 3, checkpoint });
            Tick(); Assert.That(runtime.TreasureRoom, Is.Null, "第四轮恢复不能重开军械库");
            typeof(ArcadeCombatSystem).GetMethod("TickTimers", Flags).Invoke(system, new object[] { runtime, 1f });
            Assert.That(runtime.TemporaryWeaponRemaining, Is.EqualTo(25), "恢复后预警也不能消耗武器");
        }
        [Test] public void ArmoryDoesNotOpenBeforeThirdWaveClear()
        {
            ConfigureArmory(); runtime.SpawnQueue.Enqueue("pending"); Tick();
            Assert.That(runtime.InTreasureRoom, Is.False); Assert.That(runtime.TreasureRoom.Started, Is.False);
        }
        [Test] public void ArmorySupportsForestAsSecondRouteDestination()
        {
            ConfigureArmory();
            typeof(ArcadeCombatSystem).GetField("arena", Flags).SetValue(system, catalog.Arenas.Single(a => a.ArenaNumber == 3));
            Tick(); Assert.That(runtime.InTreasureRoom, Is.True); Assert.That(runtime.TreasureRoom.Parameters.IsArmory, Is.True);
        }
        private ArcadeArenaCompletion Completion()=>new ArcadeArenaCompletion{ArenaNumber=1,NextArenaConfigNumber=2,NextCheckpoint=new ArcadeCheckpointSaveData{ArenaNumber=2},Score=run.Current.Score,ScoreMultiplier=run.Current.ScoreMultiplier,MultiplierProgress=run.Current.MultiplierProgress,EmergencyResources=run.Current.EmergencyResources,EquipmentBySlot=run.Current.EquipmentBySlot};
        private sealed class TestView : IArcadeCombatViewPort
        {
            public string WeaponId, ProductId;
            public void SetPlayerWeapon(ArcadeWeaponDefinition weapon) { WeaponId = weapon.Id; }
            public void PlayProductPickup(string productId) { ProductId = productId; }
            public bool TreasureEntranceShown;
            public void SetTreasureRoom(FirepowerLoop.Arcade.ArcadeTreasureRoomParameters p, bool inside, bool discovered, bool open, IReadOnlyList<int> ids)
            { TreasureEntranceShown = !inside && discovered; }
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

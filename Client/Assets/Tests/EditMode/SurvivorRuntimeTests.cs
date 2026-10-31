using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>幸存者现场限定八项测试；真实配置、假表现、独立临时存档。</summary>
    public sealed class SurvivorRuntimeTests
    {
        private Game architecture;
        private SurvivorSystem system;
        private SurvivorModel model;
        private ArcadeRunModel arcade;
        private SurvivorConfiguration config;
        private TestView view;
        private PlayerInputCtrl input;
        private string root;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private void Set(string name,object value)=>typeof(SurvivorSystem).GetField(name,Flags).SetValue(system,value);
        private T Get<T>(string name)=>(T)typeof(SurvivorSystem).GetField(name,Flags).GetValue(system);
        private object Call(string name,params object[] args)=>typeof(SurvivorSystem).GetMethod(name,Flags).Invoke(system,args);
        [SetUp] public void Setup()
        {
            architecture=new Game();model=new SurvivorModel();arcade=new ArcadeRunModel();architecture.RegisterModel(model);architecture.RegisterModel(arcade);architecture.RegisterModel(new MapSceneModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            Assert.That(provider.TryGetSurvivor(out config,out var error),Is.True,error);Assert.That(provider.TryGetArcadeCatalog(out var catalog,out error),Is.True,error);Assert.That(provider.TryGetEnemy(config.EnemyId,out var enemy,out error),Is.True,error);
            system=new SurvivorSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);view=new TestView();input=new PlayerInputCtrl();root=Path.Combine(Path.GetTempPath(),"SurvivorRuntimeTests-"+Guid.NewGuid().ToString("N"));
            Set("config",config);Set("catalog",catalog);Set("layout",catalog.RoundLayouts.Single(x=>x.Id==config.LayoutId));Set("weapon",catalog.Weapons.Single(x=>x.Id==config.WeaponId));Set("enemyConfig",enemy);Set("obstacles",Array.Empty<ArcadeObstacleDefinition>());Set("view",view);Set("input",input);Set("store",new SurvivorResultFileStore(1,root));Set("runId",123L);
            model.State=new SurvivorRunState(config.InitialExperienceThreshold,config.ExperienceThresholdGrowth);model.Loadout=new SurvivorLoadout(config.WeaponSlots,"Rifle");model.Active=true;model.Health=model.MaximumHealth=config.PlayerMaxHealth;
            arcade.CommitSafeNode(new ArcadeRunSaveData{RunId="unrelated-arcade",RemainingLives=3,NextArenaNumber=1});
        }
        [TearDown] public void Cleanup(){system.Stop();input.Dispose();if(Directory.Exists(root))Directory.Delete(root,true);}
        /// <summary>步枪打空30发后停火，换弹完成才重新发射，备弹不限制循环。</summary>
        [Test] public void RifleWaitsForAutomaticReloadAfterThirtyRounds()
        {
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=100,Position=new Vector2(2,0)});
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            for(int i=0;i<31;i++){Set("fireClock",0f);Call("Fire");}
            Assert.That(shots.Count,Is.EqualTo(30),"空弹夹必须停火");
            Call("AdvanceRifleReload",1f);Set("fireClock",0f);Call("Fire");Assert.That(shots.Count,Is.EqualTo(30));
            Call("AdvanceRifleReload",1f);Set("fireClock",0f);Call("Fire");Assert.That(shots.Count,Is.EqualTo(31));
        }
        /// <summary>通过真实选择事务取得指定强化，避免直接改内部层数。</summary>
        private int SelectMagazineUpgrade(string kind)
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind==kind);
            model.State.AddExperience(model.State.RequiredExperience);model.State.Offer(new[]{id,1,2},17);
            Assert.That(system.Choose(model.State.ChoiceRevision,id),Is.True);return id;
        }
        [Test] public void MagazineUpgradeAddsThreeRoundsWithoutRefillingCurrentMagazine()
        {
            Call("EnsureRifleMagazine");for(int i=0;i<8;i++)Assert.That(Call("TryConsumeRifleRound"),Is.True);
            int id=SelectMagazineUpgrade("Magazine");SelectMagazineUpgrade("Magazine");
            Assert.That(model.RifleAmmo,Is.EqualTo(22));Assert.That(model.RifleMagazineSize,Is.EqualTo(36));
            Assert.That(system.TryGetRifleUpgradeDetails(id,out var magazine,out var current,out var next),Is.True);
            Assert.That(magazine,Is.True);Assert.That(current,Is.EqualTo(36));Assert.That(next,Is.EqualTo(39));
            for(int i=0;i<22;i++)Call("TryConsumeRifleRound");Call("AdvanceRifleReload",2f);Assert.That(model.RifleAmmo,Is.EqualTo(36));
        }
        [Test] public void ReloadUpgradeKeepsActiveTimerAndStopsAtOneSecond()
        {
            for(int i=0;i<30;i++)Call("TryConsumeRifleRound");Call("AdvanceRifleReload",.5f);
            int id=SelectMagazineUpgrade("Reload");Assert.That(model.RifleReloadTimeRemaining,Is.EqualTo(1.5f));
            for(int i=0;i<4;i++)SelectMagazineUpgrade("Reload");
            Assert.That(system.TryGetRifleUpgradeDetails(id,out _,out var current,out _),Is.True);Assert.That(current,Is.EqualTo(1));
            Call("AdvanceRifleReload",1.5f);for(int i=0;i<30;i++)Call("TryConsumeRifleRound");Assert.That(model.RifleReloadTimeRemaining,Is.EqualTo(1f));
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");Assert.That(model.UpgradeChoices,Has.No.Member(id));
        }
        [Test] public void ReloadFreezesDuringPauseAndResetsForNewRun()
        {
            UIHelper.ResetForTests();architecture.RegisterModel(new AppStateModel());architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            Set("navigation",new SurvivorNavigation(30.5f,30.5f,.5f,Array.Empty<ArcadeObstacleDefinition>()));Set("spawnClock",100f);
            for(int i=0;i<30;i++)Call("TryConsumeRifleRound");system.SetPaused(true);Call("TickFrame",.05f);Assert.That(model.RifleReloadTimeRemaining,Is.EqualTo(2f));
            system.SetPaused(false);Call("TickFrame",.05f);Assert.That(model.RifleReloadTimeRemaining,Is.LessThan(2f));
            system.Stop();Assert.That(model.RifleAmmo,Is.Zero);Assert.That(model.RifleReloadTimeRemaining,Is.Zero);
            model.State=new SurvivorRunState(10,5);Call("EnsureRifleMagazine");Assert.That(model.RifleAmmo,Is.EqualTo(30));
        }
        [Test] public void MissingTargetAndProjectileBudgetDoNotConsumeAmmoAndDroneKeepsFiring()
        {
            Call("EnsureRifleMagazine");Call("Fire");Assert.That(model.RifleAmmo,Is.EqualTo(30));
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=100,Position=new Vector2(2,0)});
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");for(int i=0;i<config.MaxProjectiles;i++)shots.Add(1000+i,new ArcadeProjectileRuntime());
            Call("Fire");Assert.That(model.RifleAmmo,Is.EqualTo(30));shots.Clear();
            for(int i=0;i<30;i++)Call("TryConsumeRifleRound");model.Loadout.TryUpgrade("Drone",5);Call("TickDrone",.01f);
            Assert.That(shots.Values.Single().SourceWeaponId,Is.EqualTo("survivor.drone"));Assert.That(model.RifleAmmo,Is.Zero);
        }
        /// <summary>拾取卡展示包含基础半径的真实数值，后续升级持续累加。</summary>
        [Test] public void PickupUpgradeDetailsIncludeBaseRadiusAndCurrentLevels()
        {
            int id=Array.FindIndex(config.Upgrades.ToArray(),x=>x.Kind=="Pickup")+1;
            var method=typeof(SurvivorSystem).GetMethod("TryGetPickupUpgradeDetails");Assert.That(method,Is.Not.Null);
            for(int level=0;level<12;level++)
            {
                object[] args={id,0d,0d};Assert.That(method.Invoke(system,args),Is.True);
                Assert.That((double)args[1],Is.EqualTo(2+level*.25));Assert.That((double)args[2],Is.EqualTo(2+(level+1)*.25));
                model.State.AddExperience(model.State.RequiredExperience);
                model.State.Offer(new[]{id,1,2},17);Assert.That(model.State.Choose(model.State.ChoiceRevision,id),Is.True);
            }
            object[] other={1,0d,0d};Assert.That(method.Invoke(system,other),Is.False);
        }
        /// <summary>幸存者敌人持续累积，超过旧的存活阈值仍按刷新节奏生成。</summary>
        [Test] public void SpawnContinuesBeyondFormerAliveLimit()
        {
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            for(int i=0;i<125;i++)Call("Spawn");
            Assert.That(enemies.Count,Is.EqualTo(125),"不能因存活数量达到60而停止刷新");
            Assert.That(model.Paused,Is.False);
        }
        [Test] public void OutsideSpawnRejectsInvalidRouteWithoutCreatingEnemy()
        {
            // 海岛特殊布局不属于幸存者普通通道配置，模拟准备后配置失效。
            Set("layout",Get<ArcadeConfigCatalogDefinition>("catalog").RoundLayouts.First(x=>x.ArenaConfigNumber==1));
            Call("Spawn");
            Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
            Assert.That(model.Paused,Is.True);Assert.That(system.LastError,Does.Contain("场外通道"));
            Assert.That(model.ResultSnapshot,Is.Null,"配置故障不能伪造失败结算");
            Call("Tick");Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void SpawnCameraCheckIncludesBodyAndScreenMargin(bool orthographic)
        {
            var cameraObject=new GameObject("SpawnCameraTest");var map=new GameObject("SpawnMapTest");
            try
            {
                var camera=cameraObject.AddComponent<Camera>();camera.enabled=true;camera.orthographic=orthographic;
                camera.orthographicSize=10;camera.fieldOfView=90;camera.aspect=1;
                camera.transform.position=new Vector3(0,10,0);camera.transform.rotation=Quaternion.Euler(90,0,0);
                var method=typeof(ArcadeCombatViewController).GetMethod("IsSpawnHiddenByCamera",BindingFlags.Static|BindingFlags.NonPublic);
                bool Hidden(Vector2 p)=>(bool)method.Invoke(null,new object[]{camera,map.transform,p,.5f,new Plane[6]});
                Assert.That(Hidden(Vector2.zero),Is.False);
                Assert.That(Hidden(new Vector2(10.6f,0)),Is.False,"画面边缘外仍保留身体与镜头移动余量");
                Assert.That(Hidden(new Vector2(20,0)),Is.True);
                map.transform.position=Vector3.right*40;Assert.That(Hidden(Vector2.zero),Is.True,"应使用正式地图世界变换");
                Assert.That((bool)method.Invoke(null,new object[]{null,map.transform,Vector2.zero,.5f,new Plane[6]}),Is.False);
            }
            finally{UnityEngine.Object.DestroyImmediate(cameraObject);UnityEngine.Object.DestroyImmediate(map);}
        }
        [Test] public void HiddenLaneExtensionRejectsWallBeyondOriginalSpawn()
        {
            var layout=Get<ArcadeRoundLayoutParameters>("layout");
            var stage=Get<ArcadeConfigCatalogDefinition>("catalog").EarlyStages.Single(x=>x.ArenaConfigNumber==layout.ArenaConfigNumber);
            Assert.That(ArcadeOutsideRouteRules.TryStart(layout,0,stage.HalfWidth,stage.HalfHeight,.5f,out var entry),Is.True);
            var dx=(entry.StartX-entry.TargetX)/entry.Distance;var dz=(entry.StartZ-entry.TargetZ)/entry.Distance;
            var clone=(ArcadeRoundLayoutParameters)typeof(object).GetMethod("MemberwiseClone",Flags).Invoke(layout,null);
            var wall=new ArcadeLayoutObstacle(entry.StartX+dx*3,entry.StartZ+dz*3,1,1);
            typeof(ArcadeRoundLayoutParameters).GetField("<Obstacles>k__BackingField",Flags).SetValue(clone,layout.Obstacles.Concat(new[]{wall}).ToArray());
            Assert.That(ArcadeOutsideRouteRules.TryExtendToHidden(clone,entry,.5f,(x,z)=>Mathf.Abs(x)>60||Mathf.Abs(z)>60,out _),Is.False);
        }
        [Test] public void SpawnExtendsExistingLaneUntilOutsideCamera()
        {
            view.SpawnHidden=p=>Mathf.Abs(p.x)>45||Mathf.Abs(p.y)>45;
            Call("Spawn");var enemy=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Values.Single();
            Assert.That(view.SpawnHidden(enemy.Position),Is.True,"场地外不等于镜头外");
            Assert.That(enemy.OutsideEntry.Active,Is.True);
            var start=enemy.Position;var state=enemy.OutsideEntry;
            Assert.That(ArcadeOutsideEntryRules.Advance(ref state,.1f,enemy.Speed,out var next),Is.True);
            Assert.That(Vector2.Distance(start,new Vector2(next.X,next.Y)),Is.EqualTo(enemy.Speed*.1f).Within(.001f));
            Assert.That(state.TargetX,Is.EqualTo(enemy.OutsideEntry.TargetX));
            Assert.That(state.TargetZ,Is.EqualTo(enemy.OutsideEntry.TargetZ));
        }
        [Test] public void VisibleSpawnWithoutHiddenLaneWaitsWithoutPausingRun()
        {
            view.SpawnHidden=p=>false;Call("Spawn");
            Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
            Assert.That(model.Paused,Is.False);Assert.That(system.LastError,Is.Null);
        }
        [Test] public void OutsideSpawnPauseAndEarlyKillLandExperienceInsideEntry()
        {
            Call("Spawn");var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");var enemy=enemies.Values.Single();
            Assert.That(enemy.OutsideEntry.Active,Is.True);Assert.That(enemy.Position.x,Is.LessThan(-30.5f));
            var start=enemy.Position;system.SetPaused(true);Call("Tick");Assert.That(enemy.Position,Is.EqualTo(start));
            var bullets=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            bullets.Add(500,new ArcadeProjectileRuntime{Id=500,Position=start-Vector2.right,Velocity=Vector2.right*10,Damage=enemy.Health,RemainingSeconds=1});
            Call("AdvanceBullets",.1f);Assert.That(enemies,Is.Empty);Assert.That(model.Kills,Is.EqualTo(1));
            var drops=Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops");
            Assert.That(drops.Values.Single().Position,Is.EqualTo(new Vector2(enemy.OutsideEntry.TargetX,enemy.OutsideEntry.TargetZ)));
        }
        [Test] public void MovementTickDoesNotClearAnimationWithSecondMove()
        {
            UIHelper.ResetForTests();
            architecture.RegisterModel(new AppStateModel());
            architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            Set("navigation",new SurvivorNavigation(30.5f,30.5f,.5f,Array.Empty<ArcadeObstacleDefinition>()));
            Set("spawnClock",100f);
            typeof(PlayerInputCtrl).GetProperty("MoveInput").SetValue(input,Vector2.right);
            Call("TickFrame",.02f);
            Assert.That(Get<Vector2>("player").x,Is.GreaterThan(0));
            Assert.That(view.PlayerWrites,Is.EqualTo(1),"同帧第二次零位移会清空腿部动画速度");
            Assert.That(view.CombatPoseWrites,Is.EqualTo(1),"末尾仍需刷新本帧射击朝向和保护状态");
        }
        [Test] public void PauseFreezesClockEnemiesAndInputPresentation()
        {
            system.SetPaused(true);var before=Get<Vector2>("player");Call("Tick");Assert.That(model.State.ElapsedSeconds,Is.Zero);Assert.That(Get<Vector2>("player"),Is.EqualTo(before));Assert.That(view.PlayerWrites,Is.Zero);Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
        }
        [Test] public void LostPreparedBattleReportsFailureOnceInsteadOfSilentlyFreezing()
        {
            var commit=typeof(SurvivorSystem).GetMethod("ConfirmBattlePrepared");
            Assert.That(commit,Is.Not.Null,"缺少准备完成交接");
            commit.Invoke(system,null);
            int changes=0;architecture.RegisterEvent<SurvivorChangedEvent>(_=>changes++);
            view.Ready=false;
            Call("Tick");Call("Tick");
            Assert.That(model.Paused,Is.True);
            Assert.That(system.LastError,Does.Contain("战场"));
            Assert.That(changes,Is.EqualTo(1));
            Assert.That(model.State.ElapsedSeconds,Is.Zero);
            Assert.That(Directory.Exists(root),Is.False,"准备故障不能伪造结算存档");
        }
        [Test] public void InitialPreparationWaitDoesNotReportLostBattle()
        {
            view.Ready=false;Call("Tick");
            Assert.That(system.LastError,Is.Null);
            Assert.That(model.Paused,Is.False);
        }
        [Test] public void UnreadyBattleCannotCommitPreparation()
        {
            view.Ready=false;
            Assert.Throws<InvalidOperationException>(()=>system.ConfirmBattlePrepared());
            Assert.That(model.State.ElapsedSeconds,Is.Zero);
        }
        [Test] public void FarmEntryUsesItsConfiguredArenaBounds()
        {
            var position = new Vector2(-28, -14);
            var resolved = (Vector2)Call("Resolve", position, .5f);
            Assert.That(resolved, Is.EqualTo(position), "不能用旧竞技场的小边界截断草地入口");
        }
        [Test] public void ConsecutiveLevelsRequireSeparateVersionedChoices()
        {
            model.State.AddExperience(config.InitialExperienceThreshold*2L+config.ExperienceThresholdGrowth);Call("Offer");Assert.That(model.State.PendingChoices,Is.EqualTo(2));int revision=model.Snapshot.ChoiceRevision,id=model.UpgradeChoices[0];Assert.That(system.Choose(revision,id),Is.True);Assert.That(model.Paused,Is.True);Assert.That(system.Choose(revision,id),Is.False);Assert.That(system.Choose(model.Snapshot.ChoiceRevision,model.UpgradeChoices[0]),Is.True);Assert.That(model.Paused,Is.False);
        }
        [Test] public void FirstUpgradeAcquiresDroneAndNextChoiceUpgradesSameSlot()
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Drone");
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");Assert.That(model.UpgradeChoices,Does.Contain(id));
            Assert.That(system.Choose(0,id),Is.True);Assert.That(model.WeaponLevel("Drone"),Is.EqualTo(1));Assert.That(model.UsedWeaponSlots,Is.EqualTo(2));
            Assert.That(system.TryGetWeaponUpgrade(id,out var kind,out var level,out var current,out var next),Is.True);Assert.That(level,Is.EqualTo(1));Assert.That(next.Damage,Is.GreaterThan(current.Damage));
            model.State.AddExperience(model.State.RequiredExperience);model.State.OfferWeighted(new[]{id,1,2},new[]{1,1,1},1,id);
            Assert.That(system.Choose(1,id),Is.True);Assert.That(model.WeaponLevel("Drone"),Is.EqualTo(2));Assert.That(model.UsedWeaponSlots,Is.EqualTo(2));Assert.That(system.Choose(1,id),Is.False);
        }
        [Test] public void MaxLevelDroneIsRemovedFromCandidates()
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Drone");
            for(int i=0;i<5;i++)model.Loadout.TryUpgrade("Drone",5);
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");Assert.That(model.UpgradeChoices,Has.No.Member(id));Assert.That(system.Choose(0,id),Is.False);
        }
        [Test] public void NewlyAcquiredDroneStartsBesideCurrentPlayer()
        {
            var player=new Vector2(12,-8);Set("player",player);Set("dronePosition",new Vector2(-20,20));
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Drone");
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");Assert.That(system.Choose(0,id),Is.True);
            var tuning=Get<ArcadeConfigCatalogDefinition>("catalog").Rule.Drone;
            Assert.That(Get<Vector2>("dronePosition"),Is.EqualTo(player+new Vector2(tuning.OffsetX,tuning.OffsetZ)));
        }
        [Test] public void DroneFiresAtPlayerThreatAndSharesProjectileBudget()
        {
            model.Loadout.TryUpgrade("Drone",5);
            var enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            enemies.Add(201,new ArcadeEnemyRuntime{Id=201,Health=100,Position=new Vector2(0,2)});
            enemies.Add(202,new ArcadeEnemyRuntime{Id=202,Health=100,Position=new Vector2(4,0)});
            Set("dronePosition",new Vector2(4,0));Call("TickDrone",.01f);
            var shots=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");Assert.That(shots.Count,Is.EqualTo(1));var shot=shots.Values.Single();Assert.That(shot.SourceWeaponId,Is.EqualTo("survivor.drone"));Assert.That(shot.Velocity.x,Is.LessThan(0));Assert.That(shot.Velocity.y,Is.GreaterThan(0));
            for(int i=shots.Count;i<config.MaxProjectiles;i++)shots.Add(1000+i,new ArcadeProjectileRuntime());
            Set("droneFireClock",0f);Call("TickDrone",.01f);Assert.That(shots.Count,Is.EqualTo(config.MaxProjectiles));
        }
        [Test] public void UpgradeSelectionDoesNotDismissMenuPause()
        {
            model.State.AddExperience(config.InitialExperienceThreshold);Call("Offer");system.SetPaused(true);Assert.That(system.Choose(model.Snapshot.ChoiceRevision,model.UpgradeChoices[0]),Is.True);Assert.That(model.Paused,Is.True);system.SetPaused(false);Assert.That(model.Paused,Is.False);
        }
        [Test] public void HealthUpgradeAddsOnlyNewCapacityWithoutFullHeal()
        {
            int id=Enumerable.Range(1,config.Upgrades.Count).Single(i=>config.Upgrades[i-1].Kind=="Health");model.Health=20;model.State.AddExperience(config.InitialExperienceThreshold);var eligible=Enumerable.Range(1,config.Upgrades.Count).ToArray();int seed=0;
            while(true){model.State=new SurvivorRunState(config.InitialExperienceThreshold,config.ExperienceThresholdGrowth);model.State.AddExperience(config.InitialExperienceThreshold);if(model.State.Offer(eligible,seed++).Contains(id))break;Assert.That(seed,Is.LessThan(100));}
            Assert.That(system.Choose(0,id),Is.True);Assert.That(model.Health,Is.EqualTo(20+model.MaximumHealth-config.PlayerMaxHealth));Assert.That(model.Health,Is.LessThan(model.MaximumHealth));
        }
        [Test] public void AutoWeaponsWaitUntilTargetIsVisible()
        {
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=100,MaxHealth=100,Position=new Vector2(2,0)});
            model.Loadout.TryUpgrade("Drone",5);
            Set("droneVisual",new ArcadePowerupDefinition("survivor.drone",global::ArcadePowerupBehavior.Drone,"",false,Vector3.zero,0,0,0,0,0,0));
            var bullets=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets");
            view.TargetVisible=false;Call("Fire");Call("TickDrone",.01f);
            Assert.That(bullets,Is.Empty,"看不到敌人时两种自动武器都应等待");
            view.TargetVisible=true;Call("Fire");Call("TickDrone",.01f);
            Assert.That(bullets.Count,Is.EqualTo(2),"进入画面后步枪和无人机都应恢复攻击");
        }
        [Test] public void ExactOverlapKeepsOneDropWithAllExperience()
        {
            KillOne();KillOne();
            var drops=Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops");
            Assert.That(drops.Count,Is.EqualTo(1));
            Assert.That(drops.Values.Single().Experience,Is.EqualTo(config.ExperiencePerKill*2));
            Assert.That(model.State.Experience,Is.Zero,"经验仍需拾取才能获得");
        }
        [Test] public void NearbyDistinctDropsRemainSeparate()
        {
            KillOne();KillOne(new Vector2(1.000001f,0));
            Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Count,Is.EqualTo(2),"不能用距离容差合并不同落点");
        }
        [Test] public void ExperiencePresentationSendsNoNameOrQuantityLabel()
        {
            var rootObject=new GameObject("ExperienceNameTest");
            try
            {
                var port=new ExperienceLootSpy();var presentation=new ArcadeCombatViewController();
                typeof(ArcadeCombatViewController).GetField("mapRoot",Flags).SetValue(presentation,rootObject.transform);
                typeof(ArcadeCombatViewController).GetField("lootPresentation",Flags).SetValue(presentation,port);
                presentation.SetExperienceDrop(1,config.ExperienceVisualPrefabAddress,Vector2.zero,3);
                Assert.That(port.Name,Is.Empty);Assert.That(port.Amount,Is.EqualTo(3));
                Assert.That(port.Address,Is.EqualTo(config.ExperienceVisualPrefabAddress));
            }
            finally{UnityEngine.Object.DestroyImmediate(rootObject);}
        }
        private sealed class ExperienceLootSpy:IArcadeLootPresentationPort
        {
            public string Name,Address;public int Amount;
            public bool TrySetDrop(int id,Vector3 position,string name,int amount,out string error){Name=name;Amount=amount;error=null;return true;}
            public bool TrySetDrop(int id,Vector3 position,string name,int amount,string address,out string error){Address=address;return TrySetDrop(id,position,name,amount,out error);}
            public void RemoveDrop(int id){}
            public void ClearArcadeDrops(){}
        }
        private void KillOne(Vector2? position=null)
        {
            var point=position??new Vector2(1,0);
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=1,MaxHealth=1,Position=point});
            Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Add(902,new ArcadeProjectileRuntime{Id=902,Position=point-Vector2.right,Velocity=new Vector2(20,0),Damage=1,RemainingSeconds=1});Call("AdvanceBullets",.05f);
        }
        [Test] public void ZeroExperienceChanceKeepsKillAndScoreWithoutDrop()
        {
            typeof(SurvivorConfiguration).GetProperty("ExperienceDropChance").SetValue(config,0f);
            KillOne();
            Assert.That(model.Kills,Is.EqualTo(1));Assert.That(model.Score,Is.EqualTo(config.ScorePerKill));
            Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops"),Is.Empty);
            Assert.That(model.State.Experience,Is.Zero);
        }
        [Test] public void KillCreatesExperienceOnGroundWithoutRemoteCollection()
        {
            KillOne();Assert.That(model.Kills,Is.EqualTo(1));Assert.That(model.Score,Is.EqualTo(config.ScorePerKill));Assert.That(model.State.Experience,Is.Zero);Assert.That(Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops").Values.Single().Experience,Is.EqualTo(config.ExperiencePerKill));
        }
        [Test] public void FullDropBudgetMergesExperienceWithoutLoss()
        {
            var drops=Get<Dictionary<int,(Vector2 Position,long Experience)>>("drops");for(int i=0;i<config.MaxDrops;i++)drops.Add(1000+i,(new Vector2(i,0),1));KillOne();Assert.That(drops.Count,Is.EqualTo(config.MaxDrops));Assert.That(drops.Values.Sum(x=>x.Experience),Is.EqualTo(config.MaxDrops+config.ExperiencePerKill));
        }
        [Test] public void DeathSavesIndependentResultAndRetryIsIdempotent()
        {
            model.Health=0;model.Kills=2;model.Score=20;Assert.That(system.TryFinish(out var error),Is.True,error);Assert.That(system.TryFinish(out error),Is.True,error);var store=new SurvivorResultFileStore(1,root);Assert.That(store.TryRead(out var result,out error),Is.True,error);Assert.That(result.Outcome,Is.EqualTo("failed"));Assert.That(result.Kills,Is.EqualTo(2));Assert.That(Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length,Is.EqualTo(1));Assert.That(arcade.Current.RunId,Is.EqualTo("unrelated-arcade"));
        }
        [Test] public void QuitSavesAbandonedSurvivorBeforeAllowingApplicationExit()
        {
            architecture.RegisterSystem(system);
            Assert.That(QuitGameCommand.TryPrepareQuit(architecture), Is.True);
            Assert.That(model.ResultSnapshot, Is.Not.Null);
            Assert.That(model.ResultSnapshot.Outcome, Is.EqualTo("abandoned"));
            Assert.That(new SurvivorResultFileStore(1, root).TryRead(out var result, out var error), Is.True, error);
            Assert.That(result, Is.Not.Null);
            Assert.That(arcade.Current.RunId, Is.EqualTo("unrelated-arcade"));
        }
        [Test] public void StopReleasesOwnedViewAndPreservesArcadeRun()
        {
            Set("ownsView",true);system.Stop();system.Stop();Assert.That(view.Ends,Is.EqualTo(1));Assert.That(model.Active,Is.False);Assert.That(arcade.Current.RunId,Is.EqualTo("unrelated-arcade"));Assert.That(Directory.Exists(root),Is.False);
        }
        private sealed class TestView:IArcadeCombatViewPort
        {
            public Func<Vector2,bool> SpawnHidden=p=>true;
            public bool IsArenaSpawnHidden(Vector2 position,float radius)=>SpawnHidden(position);
            public bool TargetVisible=true;
            public bool IsArenaTargetVisible(Vector2 position)=>TargetVisible;
            public bool Ready=true;
            public bool IsRoundLayoutReady=>Ready;
            public int PlayerWrites,CombatPoseWrites,Ends;
            public bool IsActive=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){Ends++;}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){PlayerWrites++;}
            public void SetPlayerCombatState(Vector2 facing,bool invulnerable){CombatPoseWrites++;}
            public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string c,Vector2 p,int hp,int max,bool t=false,int seq=0,bool bar=false){}
            public void RemoveEnemy(int id,bool death=false){}
            public void SetProjectile(int id,bool h,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}
            public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){}
            public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}
            public void TickPlayerFeedback(float d){}
        }
    }
}

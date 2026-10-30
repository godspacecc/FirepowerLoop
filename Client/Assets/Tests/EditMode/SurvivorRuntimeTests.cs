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
            model.State=new SurvivorRunState(config.InitialExperienceThreshold,config.ExperienceThresholdGrowth);model.Active=true;model.Health=model.MaximumHealth=config.PlayerMaxHealth;
            arcade.CommitSafeNode(new ArcadeRunSaveData{RunId="unrelated-arcade",RemainingLives=3,NextArenaNumber=1});
        }
        [TearDown] public void Cleanup(){system.Stop();input.Dispose();if(Directory.Exists(root))Directory.Delete(root,true);}
        [Test] public void PauseFreezesClockEnemiesAndInputPresentation()
        {
            system.SetPaused(true);var before=Get<Vector2>("player");Call("Tick");Assert.That(model.State.ElapsedSeconds,Is.Zero);Assert.That(Get<Vector2>("player"),Is.EqualTo(before));Assert.That(view.PlayerWrites,Is.Zero);Assert.That(Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies"),Is.Empty);
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
        private void KillOne()
        {
            Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies").Add(901,new ArcadeEnemyRuntime{Id=901,Health=1,MaxHealth=1,Position=new Vector2(1,0)});
            Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Add(902,new ArcadeProjectileRuntime{Id=902,Position=Vector2.zero,Velocity=new Vector2(20,0),Damage=1,RemainingSeconds=1});Call("AdvanceBullets",.05f);
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
            public int PlayerWrites,Ends;
            public bool IsActive=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){Ends++;}
            public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){PlayerWrites++;}
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

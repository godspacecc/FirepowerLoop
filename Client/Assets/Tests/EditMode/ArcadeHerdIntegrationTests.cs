using System.Collections.Generic;
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
    /// <summary>实际农场活动循环必须生成单排牛群并经过正式保护与伤害入口。</summary>
    public sealed class ArcadeHerdIntegrationTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;
        private string directory;
        [SetUp] public void Setup()
        {
            var app=new Game();var run=new ArcadeRunModel();app.RegisterModel(run);app.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            app.RegisterUtility<IRuntimeConfigProvider>(provider);var runs=new ArcadeRunSystem();app.RegisterSystem(runs);directory=Path.Combine(Path.GetTempPath(),"HerdCheck-"+System.Guid.NewGuid().ToString("N"));runs.ConfigureForTests(directory);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="herd",RemainingLives=3,IsCasualRoute=true,ArenaConfigNumber=22,NextArenaNumber=3,CompletedArenaCount=2,UpdatedAtUtc=System.DateTime.UtcNow.ToString("O"),Checkpoint=new ArcadeCheckpointSaveData{ArenaNumber=3}});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=3,PlayerHealth=100,PlayerMaxHealth=100,PlayerPosition=new Vector2(-20,-14)};app.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(app);
            Set("catalog",catalog);Set("arena",catalog.Arenas.Single(a=>a.ArenaNumber==22));Set("view",new ArcadeCombatViewController());Set("random",new System.Random(8));Call("InitializeRoundContent",runtime);
        }
        private void Set(string key,object value)=>typeof(ArcadeCombatSystem).GetField(key,Flags).SetValue(system,value);
        private object Call(string key,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(key,Flags).Invoke(system,args);
        private List<ArcadeHerdState> Cows=>typeof(ArcadeCombatRuntime).GetField("HerdCows")?.GetValue(runtime) as List<ArcadeHerdState>??new();
        private void Tick(float seconds)=>Call("TickRoundContent",runtime,seconds);
        [TearDown] public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        [Test] public void FirstBatchIsOneRowAndWindupDoesNotHurt()
        {
            Tick(8);Assert.That(Cows.Count,Is.EqualTo(5));Assert.That(Cows.Select(c=>c.X).Distinct().Count(),Is.EqualTo(1));Assert.That(Cows.Select(c=>c.Z).OrderBy(z=>z),Is.EqualTo(new[]{-17.3f,-15.65f,-14f,-12.35f,-10.7f}).Within(.001));
            runtime.PlayerPosition=new Vector2(Cows[2].X,Cows[2].Z);Tick(.4f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(Cows.All(c=>c.Phase==ArcadeHerdPhase.Windup),Is.True);
        }
        [Test] public void SweptChargeHurtsPlayerAndKillsEnemyOnlyOnce()
        {
            Tick(8);runtime.Enemies.Add(12,new ArcadeEnemyRuntime{Id=12,Position=new Vector2(-20,-14),Health=10,MaxHealth=10});Tick(2);
            Assert.That(runtime.PlayerHealth,Is.EqualTo(75));Assert.That(runtime.Enemies.ContainsKey(12),Is.False);Assert.That(runtime.Drops.Count,Is.Zero);
            runtime.PlayerInvulnerability=0;Tick(.01f);Assert.That(runtime.PlayerHealth,Is.EqualTo(75));
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)] public void ExistingProtectionBlocksCowDamage(int protection)
        {
            Tick(8);if(protection==1)runtime.PlayerInvulnerability=1;if(protection==2)runtime.DashRemaining=1;
            if(protection==3)runtime.Powerups.Add(global::ArcadePowerupBehavior.Shield,new ArcadePowerupRuntime{Charges=1,RemainingSeconds=10});
            Tick(2);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));if(protection==3)Assert.That(runtime.Powerups.ContainsKey(global::ArcadePowerupBehavior.Shield),Is.False);
        }
        [Test] public void ClearFreezesCowsAndNewMapInvalidatesPreviousCharge()
        {
            Tick(8);Assert.That(Cows.Count,Is.EqualTo(5));var old=Cows[0];runtime.WaveClearAwarded=true;Tick(5);Assert.That(old.ElapsedSeconds,Is.Zero);
            runtime.WaveIndex=1;runtime.WaveClearAwarded=false;Call("InitializeRoundContent",runtime);Assert.That(Cows.Count,Is.Zero);Assert.That(old.MapInstanceId,Is.Zero);
            Tick(8);Assert.That(Cows.Count,Is.EqualTo(5));Assert.That(Cows[0].MapInstanceId,Is.GreaterThan(0));
        }
        [Test] public void RestoredGroundIdentityStillInitializesFreshHerdGeneration()
        {
            var fresh=new ArcadeCombatRuntime{ArenaNumber=3,PlayerHealth=100,PlayerMaxHealth=100,GroundBarrelMapId="arena.22.round.1",GroundBarrelLayoutVersion=1};
            runtime=fresh;system.GetModel<ArcadeCombatModel>().Begin(fresh);Call("InitializeRoundContent",fresh);Tick(8);
            Assert.That(Cows.Count,Is.EqualTo(5));Assert.That(Cows.All(c=>c.MapInstanceId>0),Is.True,"轮前恢复不能因桶身份已恢复而跳过牛群初始化");
        }
        [Test] public void LethalMiddleCowDoesNotSplitSingleRow()
        {
            Tick(8);runtime.PlayerHealth=1;Tick(2);Assert.That(runtime.RespawnRemaining,Is.GreaterThan(0));
            Assert.That(Cows.Select(c=>c.X).Distinct().Count(),Is.EqualTo(1),"死亡等待不能让尚未遍历的牛漏走本帧，破坏整排");
        }
    }
}

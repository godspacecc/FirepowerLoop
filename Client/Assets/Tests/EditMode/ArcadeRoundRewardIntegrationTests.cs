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
    /// <summary>正式清场、活动时钟及拾取入口必须遵守每轮奖励合同。</summary>
    public sealed class ArcadeRoundRewardIntegrationTests
    {
        private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;private ArcadeRunModel run;
        private object Call(string name,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(name,Private).Invoke(system,args);
        private ArcadeRoundRewardState Reward=>(ArcadeRoundRewardState)typeof(ArcadeCombatRuntime).GetField("RoundReward")?.GetValue(runtime);
        [SetUp]public void Setup()
        {
            var game=new Game();run=new ArcadeRunModel();game.RegisterModel(run);game.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="reward-integration",IsCasualRoute=true,RemainingLives=3});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100,PlayerPosition=new Vector2(6,6)};
            game.GetModel<ArcadeCombatModel>().Begin(runtime);system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(game);
            typeof(ArcadeCombatSystem).GetField("catalog",Private).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("random",Private).SetValue(system,new System.Random(1));
            typeof(ArcadeCombatSystem).GetField("arena",Private).SetValue(system,catalog.Arenas.Single(a=>a.ArenaNumber==1));
        }
        [Test]public void ClearCreatesOnlyOneChestForThisRound()
        {Call("TickWaveTransition",runtime,0f);Assert.That(Reward,Is.Not.Null,"清场必须生成本轮宝箱");var first=Reward;Call("TickWaveTransition",runtime,0f);Assert.That(Reward,Is.SameAs(first));Assert.That(runtime.IslandRoundAwaitingExit,Is.True);}
        /// <summary>清场落点取玩家当时的位置；移动与重复清场不能把已落地箱子带走。</summary>
        [Test]public void ChestStartsNearCurrentPlayerAndStaysAtClearPosition()
        {
            foreach(var position in new[]{new Vector2(6,6),new Vector2(-6,6)})
            {
                runtime.RoundReward=null;runtime.WaveClearAwarded=false;runtime.IslandRoundAwaitingExit=false;
                runtime.PlayerPosition=position;Call("TickWaveTransition",runtime,0f);
                var reward=Reward;Assert.That(reward,Is.Not.Null);
                var landing=new Vector2(reward.X,reward.Z);
                Assert.That(Vector2.Distance(landing,position),Is.LessThanOrEqualTo(3f),"金币箱应落在清场玩家附近而非场地中心");
                runtime.PlayerPosition=-position;Call("TickWaveTransition",runtime,0f);Call("TickRoundReward",runtime,.1f);
                Assert.That(Reward,Is.SameAs(reward));Assert.That(new Vector2(reward.X,reward.Z),Is.EqualTo(landing));
            }
        }
        [Test]public void FullEffectBlocksPickupThenAwardsOnlyOnce()
        {
            Call("TickWaveTransition",runtime,0f);Assert.That(Reward,Is.Not.Null);var item=Reward.Items[0];runtime.PlayerPosition=new Vector2(item.X,item.Z);
            long before=run.Current.Score;Call("TickTimers",runtime,3.29f);Call("TickDrops",runtime);Assert.That(run.Current.Score,Is.EqualTo(before));
            Call("TickTimers",runtime,.02f);Call("TickDrops",runtime);Assert.That(run.Current.Score,Is.GreaterThan(before));
            long after=run.Current.Score;Call("TickDrops",runtime);Assert.That(run.Current.Score,Is.EqualTo(after));
        }
        [Test]public void BeginningNextRoundDropsUnclaimedRewardsWithoutAutoAward()
        {Call("TickWaveTransition",runtime,0f);Assert.That(Reward,Is.Not.Null);long before=run.Current.Score;Call("BeginWave",runtime,1);Assert.That(Reward,Is.Null);Assert.That(run.Current.Score,Is.EqualTo(before));}
        [Test]public void IndependentChallengeDoesNotCreateMainRoundChest()
        {runtime.IsChallenge=true;Call("TickWaveTransition",runtime,0f);Assert.That(Reward,Is.Null);}
        [Test]public void FourMapsKeepRewardsAndPortalOnUnblockedGround()
        {
            for(int round=0;round<4;round++)
            {
                runtime.WaveIndex=round;runtime.WaveClearAwarded=false;runtime.AwaitingExit=false;runtime.IslandRoundAwaitingExit=false;runtime.RoundReward=null;runtime.StagePortal=null;
                Call("TickWaveTransition",runtime,0f);Call("TickTimers",runtime,.1f);var state=Reward;Assert.That(state,Is.Not.Null);
                var obstacles=(System.Collections.Generic.IReadOnlyList<ArcadeObstacleDefinition>)Call("ObstaclesFor",runtime);
                var island=(ArcadeIslandRegion)Call("IslandFor",runtime);
                foreach(var item in state.Items)
                {Assert.That(island.ContainsCircle(item.X,item.Z,.25f),Is.True);Assert.That(obstacles.Any(o=>Mathf.Abs(item.X-o.Center.x)<o.HalfExtents.x+.25f&&Mathf.Abs(item.Z-o.Center.y)<o.HalfExtents.y+.25f),Is.False);}
                if(round==3){Assert.That(runtime.StagePortal,Is.Not.Null);Assert.That(island.ContainsCircle(runtime.StagePortal.X,runtime.StagePortal.Z,1.5f),Is.True);}
            }
        }
    }
}

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
    /// <summary>整关出口出现与主动接触，不让中央玩家在清场瞬间被自动送走。</summary>
    public sealed class ArcadeStagePortalTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;
        private object Call(string name,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(name,Flags).Invoke(system,args);
        private object Portal=>typeof(ArcadeCombatRuntime).GetField("StagePortal")?.GetValue(runtime);
        [SetUp]public void Setup()
        {
            var game=new Game();var run=new ArcadeRunModel();game.RegisterModel(run);game.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="portal-test",IsCasualRoute=true,RemainingLives=3});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100};game.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(game);
            typeof(ArcadeCombatSystem).GetField("catalog",Flags).SetValue(system,catalog);typeof(ArcadeCombatSystem).GetField("arena",Flags).SetValue(system,catalog.Arenas.Single(a=>a.ArenaNumber==1));
        }
        [Test]public void OrdinaryRoundHasPassagesAndNoStagePortal()
        {Call("TickWaveTransition",runtime,0f);Call("TickTimers",runtime,.1f);Assert.That(runtime.IslandRoundAwaitingExit,Is.True);Assert.That(Portal,Is.Null);}
        [Test]public void FourthRoundCreatesCentralPortalOnlyAfterClear()
        {runtime.WaveIndex=3;Call("TickTimers",runtime,.1f);Assert.That(Portal,Is.Null);Call("TickWaveTransition",runtime,0f);Call("TickTimers",runtime,.1f);Assert.That(Portal,Is.Not.Null);Assert.That(runtime.AwaitingExit,Is.True);}
        [Test]public void PlayerAlreadyAtPortalMustLeaveThenEnter()
        {
            runtime.WaveIndex=3;Call("TickWaveTransition",runtime,0f);Call("TickTimers",runtime,1f);Assert.That(Portal,Is.Not.Null);
            var type=Portal.GetType();float x=(float)type.GetField("X").GetValue(Portal),z=(float)type.GetField("Z").GetValue(Portal);
            runtime.PlayerPosition=new Vector2(x,z);var method=typeof(ArcadeCombatSystem).GetMethod("StagePortalContact",Flags);Assert.That(method,Is.Not.Null);
            Assert.That((bool)method.Invoke(system,new object[]{runtime}),Is.False);
            runtime.PlayerPosition=new Vector2(x+3,z);Assert.That((bool)method.Invoke(system,new object[]{runtime}),Is.False);
            runtime.PlayerPosition=new Vector2(x,z);Assert.That((bool)method.Invoke(system,new object[]{runtime}),Is.True);
        }
    }
}

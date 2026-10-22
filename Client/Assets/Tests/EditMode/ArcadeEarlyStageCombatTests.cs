using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>实际战斗消费者必须使用场地米制尺寸，不能只让配置目录看起来正确。</summary>
    public sealed class ArcadeEarlyStageCombatTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var app=new Game();var run=new ArcadeRunModel();app.RegisterModel(run);app.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="early-map",RemainingLives=3,IsCasualRoute=true,ArenaConfigNumber=22,NextArenaNumber=3,CompletedArenaCount=2});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=3,PlayerHealth=100,PlayerMaxHealth=100};app.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(app);Set("catalog",catalog);Stage(22);Set("view",new ArcadeCombatViewController());Set("random",new System.Random(8));
        }
        private void Set(string key,object value)=>typeof(ArcadeCombatSystem).GetField(key,Flags).SetValue(system,value);
        private object Call(string key,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(key,Flags).Invoke(system,args);
        private void Stage(int id){Set("arena",catalog.Arenas.First(p=>p.ArenaNumber==id));Set("contentObstacleIdentity",null);}
        [Test] public void FarmPlayerCanUseOuterYardBeyondLegacyNineteenMetres()
        {
            runtime.PlayerPosition=new Vector2(20,-15);runtime.MoveDirection=Vector2.right;Call("TickPlayer",runtime,.1f);
            Assert.That(runtime.PlayerPosition.x,Is.GreaterThan(20));Assert.That(runtime.PlayerPosition.x,Is.LessThan(21));
        }
        [Test] public void FastMovementStopsAtFarmDividerButCrossesApprovedBreach()
        {
            runtime.PlayerPosition=new Vector2(-2,0);runtime.MoveDirection=Vector2.right;Call("TickPlayer",runtime,1f);Assert.That(runtime.PlayerPosition.x,Is.LessThanOrEqualTo(-.89f));
            runtime.PlayerPosition=new Vector2(-2,-14);Call("TickPlayer",runtime,1f);Assert.That(runtime.PlayerPosition.x,Is.GreaterThan(1));
        }
        [Test] public void FastMovementCannotCrossWaterToDisconnectedSideOfDock()
        {
            Stage(21);runtime.PlayerPosition=new Vector2(11,5);runtime.MoveDirection=Vector2.right;Call("TickPlayer",runtime,1f);Assert.That(runtime.PlayerPosition.x,Is.LessThanOrEqualTo(11.46f));
        }
        [Test] public void FarmStagePortalAppearsInCentralGateInsteadOfInsideCrossWall()
        {
            runtime.AwaitingExit=true;runtime.WaveIndex=3;Call("TickRoundReward",runtime,0f);
            Assert.That(runtime.StagePortal,Is.Not.Null);Assert.That(runtime.StagePortal.X,Is.EqualTo(0).Within(.001));Assert.That(runtime.StagePortal.Z,Is.EqualTo(14).Within(.001));
        }
        [Test] public void LegacyMapStillUsesOriginalArenaBoundary()
        {
            Stage(2);runtime.PlayerPosition=new Vector2(20,10);runtime.MoveDirection=Vector2.right;Call("TickPlayer",runtime,.1f);Assert.That(runtime.PlayerPosition.x,Is.EqualTo(18.95f).Within(.001));
        }
        [Test] public void WaitingInDockCentreDoesNotInventAFourthPassageOrAdvance()
        {
            Stage(21);runtime.IslandRoundAwaitingExit=true;runtime.PlayerPosition=Vector2.zero;
            Assert.DoesNotThrow(()=>Call("TickContentExit",runtime));Assert.That(runtime.WaveIndex,Is.Zero);Assert.That(runtime.IslandTransitionPending,Is.False);
        }
        [Test] public void NewStageUsesOneTimedRandomEntryWithoutDirectionWarning()
        {
            Stage(21);runtime.IslandWaveBudget=100;for(int i=0;i<100;i++)runtime.SpawnQueue.Enqueue("remaining-enemy");
            Assert.That(Call("ContentEntryReady",runtime,0f),Is.True);int entry=runtime.IslandEntryBatch;
            for(int i=0;i<70;i++)runtime.SpawnQueue.Dequeue();Assert.That(Call("ContentEntryReady",runtime,1f),Is.True);
            Assert.That(runtime.IslandEntryBatch,Is.EqualTo(entry));Assert.That(runtime.IslandEntryWarningRemaining,Is.Zero);Assert.That(runtime.IslandPassages,Is.Not.Null);
        }
        [Test] public void LegacySequentialCycleDoesNotStartUsingNewCasualStageIds()
        {
            Assert.That(ArcadeCombatSystem.ResolveConfiguredArenaIndex(21,catalog.Arenas.Count),Is.Zero);
            Assert.That(ArcadeCombatSystem.ResolveConfiguredArenaIndex(22,catalog.Arenas.Count),Is.EqualTo(1));
        }
        [TestCase(21,9.9f)] [TestCase(2,10f)] public void DockDoesNotInheritLegacyArmoryWeaponTimerHold(int map,float expected)
        {
            Stage(map);runtime.ArenaNumber=2;runtime.WaveIndex=3;runtime.IslandWaveBudget=1;runtime.SpawnQueue.Enqueue("not-spawned");runtime.TemporaryWeaponId="arcade.weapon.rapid";runtime.TemporaryWeaponRemaining=10;
            Call("TickTimers",runtime,.1f);Assert.That(runtime.TemporaryWeaponRemaining,Is.EqualTo(expected).Within(.001));
        }
    }
}

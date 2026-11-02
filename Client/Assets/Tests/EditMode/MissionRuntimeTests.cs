using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>复用真实幸存者测试装配，验证任务结算与钱包隔离，不进入用户存档。</summary>
    public sealed class MissionRuntimeTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Clear(MissionRunState state){while(state.TryRecordSpawn()){}state.Advance(1,0,false,false);}
        private static MissionRunState DefenseReady()
        {
            var state=new MissionRunState(1,new[]{1,1,1},1,1,1,1,1);
            Clear(state);foreach(int i in new[]{2,0,1}){Assert.That(state.TryCollect(i),Is.True);Clear(state);}
            state.Advance(1,0,true,false);while(state.TryRecordSpawn()){}
            Assert.That(state.Phase,Is.EqualTo(MissionPhase.Defend));return state;
        }
        [Test] public void PausedMissionDoesNotAdvanceFacilityOrOfferReward()
        {
            var fixture=new SurvivorRuntimeTests();fixture.Setup();
            try {
                var system=(SurvivorSystem)typeof(SurvivorRuntimeTests).GetField("system",Flags).GetValue(fixture);
                var model=(SurvivorModel)typeof(SurvivorRuntimeTests).GetField("model",Flags).GetValue(fixture);
                var state=DefenseReady();typeof(SurvivorSystem).GetField("mission",Flags).SetValue(system,state);
                var tables=new global::Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Game/Config",n+".bytes"))));
                typeof(SurvivorSystem).GetField("missionMode",Flags).SetValue(system,true);
                typeof(SurvivorSystem).GetField("missionConfig",Flags).SetValue(system,new MissionConfiguration(tables));
                model.Paused=true;typeof(SurvivorSystem).GetMethod("TickMission",Flags).Invoke(system,new object[]{1f});
                Assert.That(state.SecondsRemaining,Is.EqualTo(1));Assert.That(state.Phase,Is.EqualTo(MissionPhase.Defend));Assert.That(model.State.PendingChoices,Is.Zero);
            } finally {fixture.Cleanup();}
        }
        [Test] public void FacilityCompletionOffersOneActualUpgradeAndNeverTouchesGold()
        {
            var fixture=new SurvivorRuntimeTests();fixture.Setup();
            try {
                T Field<T>(string n)=>(T)typeof(SurvivorRuntimeTests).GetField(n,Flags).GetValue(fixture);
                var system=Field<SurvivorSystem>("system");var model=Field<SurvivorModel>("model");var state=DefenseReady();
                var tables=new global::Tables(n=>new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Game/Config",n+".bytes"))));
                void Set(string n,object value)=>typeof(SurvivorSystem).GetField(n,Flags).SetValue(system,value);
                Set("missionMode",true);Set("mission",state);Set("missionConfig",new MissionConfiguration(tables));Set("missionSupplies",new UnityEngine.Vector2[3]);
                Set("missionFacility",UnityEngine.Vector2.zero);Set("missionBreakthrough",new UnityEngine.Vector2(100,100));
                void Tick()=>typeof(SurvivorSystem).GetMethod("TickMission",Flags).Invoke(system,new object[]{1f});
                Tick();Assert.That(state.Phase,Is.EqualTo(MissionPhase.Breakthrough));Assert.That(model.State.PendingChoices,Is.EqualTo(1));Assert.That(model.UpgradeChoices.Length,Is.GreaterThan(0));Assert.That(model.Paused,Is.True);
                int level=model.State.Level;Tick();Assert.That(model.State.Level,Is.EqualTo(level));
                Assert.That(system.Choose(model.State.ChoiceRevision,model.UpgradeChoices[0]),Is.True);
                Tick();Assert.That(model.State.PendingChoices,Is.Zero);Assert.That(model.State.Level,Is.EqualTo(level));
                Assert.That(File.Exists(Path.Combine(Field<string>("root"),"Saves","survivor-gold-1.json")),Is.False);
            } finally {fixture.Cleanup();}
        }
        [Test]
        public void MissionFinishesOnceWithoutWritingGoldAndStopClearsMode()
        {
            var fixture=new SurvivorRuntimeTests();fixture.Setup();
            try {
                T Field<T>(string name)=>(T)typeof(SurvivorRuntimeTests).GetField(name,Flags).GetValue(fixture);
                var system=Field<SurvivorSystem>("system");var model=Field<SurvivorModel>("model");var root=Field<string>("root");
                var state=DefenseReady();state.Advance(1,0,true,false);state.Advance(1,0,false,true);Clear(state);Assert.That(state.TryExtract(0),Is.True);
                typeof(SurvivorSystem).GetField("missionMode",Flags).SetValue(system,true);
                typeof(SurvivorSystem).GetField("mission",Flags).SetValue(system,state);
                model.Kills=7;
                typeof(SurvivorSystem).GetMethod("TickGold",Flags).Invoke(system,new object[]{1f});
                Assert.That(system.TryFinish(out var error),Is.True,error);Assert.That(system.TryFinish(out error),Is.True,error);
                Assert.That(model.Result.Outcome,Is.EqualTo("completed"));Assert.That(model.Result.Score,Is.EqualTo(7));
                Assert.That(File.Exists(Path.Combine(root,"Saves","survivor-gold-1.json")),Is.False);
                Assert.That(new SurvivorResultFileStore(1,root).TryRead(out var saved,out error),Is.True,error);Assert.That(saved.Outcome,Is.EqualTo("completed"));
                system.Stop();Assert.That(system.IsMission,Is.False);
            } finally {fixture.Cleanup();}
        }
    }
}

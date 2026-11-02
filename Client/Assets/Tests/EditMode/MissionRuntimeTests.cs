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
        [Test]
        public void MissionFinishesOnceWithoutWritingGoldAndStopClearsMode()
        {
            var fixture=new SurvivorRuntimeTests();fixture.Setup();
            try {
                T Field<T>(string name)=>(T)typeof(SurvivorRuntimeTests).GetField(name,Flags).GetValue(fixture);
                var system=Field<SurvivorSystem>("system");var model=Field<SurvivorModel>("model");var root=Field<string>("root");
                var state=new MissionRunState(3,1);for(int i=0;i<3;i++)state.TryCollect(i);state.TryRecordSpawn();Assert.That(state.TryExtract(0),Is.True);
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

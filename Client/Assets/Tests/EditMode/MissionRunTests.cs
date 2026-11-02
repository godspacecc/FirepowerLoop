using System;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>真实生成配置在运行边界拒绝非有限数。</summary>
    public sealed class MissionRunTests
    {
        [Test] public void SearchLocationSelectsConfiguredEncounterAndFailedSpawnKeepsChoice()
        {
            var tables=new global::Tables(n=>new Luban.ByteBuf(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Game/Config",n+".bytes"))));
            var config=new MissionConfiguration(tables);
            var run=new MissionRunState(1,new[]{2,2,2},1,3,1,1,1);
            run.TryRecordSpawn();run.Advance(1,0,false,false);run.TryCollect(1);
            Assert.That(config.RosterId(run),Is.EqualTo("gaunt"));
            Assert.That(config.RosterId(run),Is.EqualTo("gaunt"),"未确认出生不消耗组合序号");
            run.TryRecordSpawn();Assert.That(config.RosterId(run),Is.EqualTo("shambler"));
            run.TryRecordSpawn();run.Advance(1,0,false,false);run.TryCollect(2);
            Assert.That(config.RosterId(run),Is.EqualTo("shield"));
        }
        [Test] public void RealGeneratedConfigurationRejectsNonFiniteInteractionRadius()
        {
            var tables=new global::Tables(n=>new Luban.ByteBuf(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Game/Config",n+".bytes"))));
            var config=new MissionConfiguration(tables);Assert.That(config.SupplyCount,Is.EqualTo(3));
            typeof(global::MissionRuleConfig).GetField("InteractionRadius").SetValue(tables.TbMissionRule.DataList[0],float.NaN);
            Assert.Throws<InvalidOperationException>(()=>new MissionConfiguration(tables));
        }
    }
}

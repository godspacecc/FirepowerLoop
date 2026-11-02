using System;
using NUnit.Framework;
namespace Game.Tests
{
    /// <summary>任务有限战斗与结算顺序的真实规则测试。</summary>
    public sealed class MissionRunTests
    {
        [Test]
        public void RealGeneratedConfigurationRejectsNonFiniteInteractionRadius()
        {
            var tables=new global::Tables(n=>new Luban.ByteBuf(System.IO.File.ReadAllBytes(System.IO.Path.Combine(UnityEngine.Application.dataPath,"Game/Config",n+".bytes"))));
            var config=new MissionConfiguration(tables);
            Assert.That(config.SupplyCount,Is.EqualTo(3));Assert.That(config.AmbushCount,Is.EqualTo(12));
            typeof(global::MissionRuleConfig).GetField("InteractionRadius").SetValue(tables.TbMissionRule.DataList[0],float.NaN);
            Assert.Throws<InvalidOperationException>(()=>new MissionConfiguration(tables));
        }
        [Test]
        public void SuppliesAreUniqueAndExtractionRequiresAllSpawnedEnemiesDefeated()
        {
            var type=typeof(SurvivorSystem).Assembly.GetType("Game.MissionRunState");
            Assert.That(type,Is.Not.Null,"任务必须拥有独立有限目标状态");
            dynamic run=Activator.CreateInstance(type,new object[]{3,6});
            Assert.That((bool)run.TryCollect(0),Is.True);
            Assert.That((bool)run.TryCollect(0),Is.False);
            Assert.That((bool)run.TryCollect(1),Is.True);
            Assert.That((bool)run.TryExtract(0),Is.False);
            Assert.That((bool)run.TryCollect(2),Is.True);
            Assert.That((bool)run.TryExtract(0),Is.False);
            for(int i=0;i<6;i++)Assert.That((bool)run.TryRecordSpawn(),Is.True);
            Assert.That((bool)run.TryRecordSpawn(),Is.False);
            Assert.That((bool)run.TryExtract(1),Is.False);
            Assert.That((bool)run.TryExtract(0),Is.True);
            Assert.That((bool)run.TryExtract(0),Is.False);
        }
    }
}

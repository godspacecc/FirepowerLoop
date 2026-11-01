using NUnit.Framework;
using System;
using System.Reflection;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>昼夜周期四个关键点与重复周期的规则边界。</summary>
    public sealed class SurvivorNightRangeTests
    {
        [TestCase(0,24)][TestCase(90,18)][TestCase(180,12)][TestCase(270,18)][TestCase(360,24)]
        public void CycleRangeMatchesConfirmedPhases(double seconds,float expected)
        { Assert.That(SurvivorDayNightRules.Range(24,12,seconds),Is.EqualTo(expected).Within(.0001)); }
        /// <summary>复用真实配置测试夹具，直接执行权威开火与弹丸步骤。</summary>
        private void WithRuntime(Action<SurvivorRuntimeTests,SurvivorSystem,SurvivorModel,SurvivorConfiguration> action)
        {
            var fixture=new SurvivorRuntimeTests();fixture.Setup();
            try { action(fixture,Field<SurvivorSystem>(fixture,"system"),Field<SurvivorModel>(fixture,"model"),Field<SurvivorConfiguration>(fixture,"config")); }
            finally { fixture.Cleanup(); }
        }
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,Hidden).GetValue(value);
        private static object Call(object value,string name,params object[] args)=>value.GetType().GetMethod(name,Hidden).Invoke(value,args);
        [TestCase(0,true)][TestCase(90,true)][TestCase(180,false)][TestCase(270,true)]
        public void RealFireTracksDayNightRange(float elapsed,bool fires)
        {
            WithRuntime((fixture,system,model,config)=>{
                model.State.Advance(elapsed,false);
                Field<Dictionary<int,ArcadeEnemyRuntime>>(system,"enemies").Add(90,new ArcadeEnemyRuntime{Id=90,Health=100,Position=new Vector2(0,15)});
                Call(system,"Fire");
                Assert.That(Field<Dictionary<int,ArcadeProjectileRuntime>>(system,"bullets").Count>0,Is.EqualTo(fires));
            });
        }
        [TestCase("Rifle",true)][TestCase("Drone",false)]
        public void ExistingShotsOutsideMovingNightBoundaryRespectOwner(string owner,bool removed)
        {
            WithRuntime((fixture,system,model,config)=>{
                model.State.Advance(180,false);
                Call(system,"Shoot",new Vector2(0,13),Vector2.up,config.WeaponLevel(owner,1),owner);
                Call(system,"AdvanceBullets",.01f);
                Assert.That(Field<Dictionary<int,ArcadeProjectileRuntime>>(system,"bullets").Count==0,Is.EqualTo(removed));
            });
        }
        [Test] public void PiercingAndExplosionCannotDamageEnemyBeyondNightEdge()
        {
            WithRuntime((fixture,system,model,config)=>{
                model.State.Advance(180,false);Call(fixture,"GrantModifier","RiflePierce");Call(fixture,"GrantModifier","RifleExplosion");
                var enemies=Field<Dictionary<int,ArcadeEnemyRuntime>>(system,"enemies");
                enemies.Add(90,new ArcadeEnemyRuntime{Id=90,Health=100,Position=new Vector2(0,11.5f)});
                enemies.Add(91,new ArcadeEnemyRuntime{Id=91,Health=100,Position=new Vector2(0,12.1f)});
                Call(system,"Shoot",new Vector2(0,11),Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");
                Call(system,"AdvanceBullets",.1f);
                Assert.That(enemies[90].Health,Is.LessThan(100));Assert.That(enemies[91].Health,Is.EqualTo(100));
                Assert.That(Field<Dictionary<int,ArcadeProjectileRuntime>>(system,"bullets"),Is.Empty);
            });
        }
        [Test] public void GeneratedNightRangeRejectsInvalidValues()
        {
            WithRuntime((fixture,system,model,config)=>{
                Assert.That(config.NightAimRange,Is.EqualTo(12));
                var tables=new Tables(n=>new Luban.ByteBuf(System.IO.File.ReadAllBytes(System.IO.Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
                var field=typeof(global::SurvivorRuleConfig).GetField("NightAimRange");
                foreach(float invalid in new[]{0f,-1f,25f,float.NaN,float.PositiveInfinity}){
                    field.SetValue(tables.TbSurvivorRule.DataList[0],invalid);
                    Assert.Throws<ArgumentException>(()=>SurvivorConfiguration.FromTables(tables));
                }
            });
        }
    }
}

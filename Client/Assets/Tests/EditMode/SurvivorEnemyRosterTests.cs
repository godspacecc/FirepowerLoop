using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>只验证幸存者独立敌池，不启动地图、不改闯关参数。</summary>
    public sealed class SurvivorEnemyRosterTests
    {
        private object Load()
        {
            var type=typeof(SurvivorSystem).Assembly.GetType("Game.SurvivorEnemyRoster");
            Assert.That(type,Is.Not.Null,"缺少独立幸存者敌池");
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            return type.GetMethod("FromTables").Invoke(null,new object[]{tables});
        }
        private static object Select(object pool,double seconds,float roll)
            =>pool.GetType().GetMethod("Select").Invoke(pool,new object[]{seconds,roll});
        private static T Value<T>(object row,string name)=>(T)row.GetType().GetProperty(name).GetValue(row);
        [TestCase(0d,0f,"enemy.arcade.zombie.shambler")]
        [TestCase(0d,.99f,"enemy.arcade.zombie.shambler")]
        [TestCase(119.99d,.99f,"enemy.arcade.zombie.shambler")]
        [TestCase(120d,.99f,"enemy.arcade.zombie.gaunt")]
        [TestCase(240d,.99f,"enemy.arcade.zombie.bloated")]
        [TestCase(360d,.99f,"enemy.arcade.zombie.bloated")]
        public void UnlocksOnlyAtConfirmedTimes(double seconds,float roll,string expected)
            =>Assert.That(Value<string>(Select(Load(),seconds,roll),"EnemyId"),Is.EqualTo(expected));
        [Test] public void OnlyApprovedEnemiesAndFastMultipliersRemain()
        {
            var pool=Load();var fast=Select(pool,120,.99f);
            var entries=(System.Collections.IEnumerable)pool.GetType().GetProperty("Entries").GetValue(pool);
            var ids=entries.Cast<object>().Select(e=>Value<string>(e,"EnemyId")).ToArray();
            Assert.That(ids,Is.EquivalentTo(new[]{"enemy.arcade.zombie.shambler","enemy.arcade.zombie.gaunt","enemy.arcade.zombie.bloated"}));
            Assert.That(Value<float>(fast,"HealthMultiplier"),Is.LessThan(1));
            Assert.That(Value<float>(fast,"SpeedMultiplier"),Is.GreaterThan(1));
        }
        [Test] public void InvalidTimeAndRandomInputAreRejected()
        {
            var pool=Load();
            foreach(var args in new[]{new object[]{double.NaN,0f},new object[]{-1d,0f},new object[]{0d,1f}})
                Assert.That(()=>pool.GetType().GetMethod("Select").Invoke(pool,args),Throws.TypeOf<TargetInvocationException>().With.InnerException.TypeOf<ArgumentOutOfRangeException>());
        }
        [Test] public void ApprovedProportionsAndHeavyUnlockComeFromModeConfig()
        {
            var pool=Load();var fast=Select(pool,120,.99f);var heavy=Select(pool,240,.99f);
            Assert.That(heavy.GetType().GetProperty("VisualScale"),Is.Not.Null,"缺少本模式体型配置");
            Assert.That(Value<Vector3>(fast,"VisualScale"),Is.EqualTo(new Vector3(.9f,.82f,.9f)));
            Assert.That(Value<Vector3>(heavy,"VisualScale"),Is.EqualTo(new Vector3(1.5f,1.2f,1.4f)));
            Assert.That(Value<string>(Select(pool,239.99,.99f),"EnemyId"),Is.EqualTo("enemy.arcade.zombie.gaunt"));
            Assert.That(Value<string>(heavy,"EnemyId"),Is.EqualTo("enemy.arcade.zombie.bloated"));
        }
        /// <summary>均匀抽样覆盖整个概率区间，精确验证各时间段的实际抽取比例。</summary>
        [TestCase(0d,100,0,0)]
        [TestCase(119.999d,100,0,0)]
        [TestCase(120d,60,40,0)]
        [TestCase(239.999d,60,40,0)]
        [TestCase(240d,50,35,15)]
        [TestCase(359.999d,50,35,15)]
        [TestCase(360d,35,40,25)]
        [TestCase(479.999d,35,40,25)]
        [TestCase(480d,20,45,35)]
        [TestCase(599.999d,20,45,35)]
        [TestCase(600d,10,50,40)]
        [TestCase(36000d,10,50,40)]
        public void TimePhasesProduceApprovedMix(double seconds,int normal,int fast,int heavy)
        {
            var pool=Load();var counts=new int[3];
            for(int i=0;i<1000;i++)
            {
                string id=Value<string>(Select(pool,seconds,(i+.5f)/1000),"EnemyId");
                counts[id=="enemy.arcade.zombie.shambler"?0:id=="enemy.arcade.zombie.gaunt"?1:2]++;
            }
            Assert.That(counts,Is.EqualTo(new[]{normal*10,fast*10,heavy*10}));
        }
        [TestCase(0)] [TestCase(-1)] [TestCase(101)]
        public void BrokenPhasePercentagesRejectEntry(int normalPercent)
        {
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var row=tables.TbSurvivorEnemy.DataList[0];var field=row.GetType().GetField("PhaseWeights");
            Assert.That(field,Is.Not.Null,"缺少权威分时比例配置");
            ((int[])field.GetValue(row))[0]=normalPercent;
            var type=typeof(SurvivorSystem).Assembly.GetType("Game.SurvivorEnemyRoster");
            Assert.That(()=>type.GetMethod("FromTables").Invoke(null,new object[]{tables}),Throws.TypeOf<TargetInvocationException>().With.InnerException.TypeOf<ArgumentException>());
        }
    }
}

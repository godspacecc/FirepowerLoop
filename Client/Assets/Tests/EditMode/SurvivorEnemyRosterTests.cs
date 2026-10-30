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
        [TestCase(240d,.99f,"enemy.arcade.zombie.gaunt")]
        [TestCase(360d,.99f,"enemy.arcade.zombie.gaunt")]
        public void UnlocksOnlyAtConfirmedTimes(double seconds,float roll,string expected)
            =>Assert.That(Value<string>(Select(Load(),seconds,roll),"EnemyId"),Is.EqualTo(expected));
        [Test] public void OnlyApprovedEnemiesAndFastMultipliersRemain()
        {
            var pool=Load();var fast=Select(pool,120,.99f);
            var entries=(System.Collections.IEnumerable)pool.GetType().GetProperty("Entries").GetValue(pool);
            var ids=entries.Cast<object>().Select(e=>Value<string>(e,"EnemyId")).ToArray();
            Assert.That(ids,Is.EquivalentTo(new[]{"enemy.arcade.zombie.shambler","enemy.arcade.zombie.gaunt"}));
            Assert.That(Value<float>(fast,"HealthMultiplier"),Is.LessThan(1));
            Assert.That(Value<float>(fast,"SpeedMultiplier"),Is.GreaterThan(1));
        }
        [Test] public void InvalidTimeAndRandomInputAreRejected()
        {
            var pool=Load();
            foreach(var args in new[]{new object[]{double.NaN,0f},new object[]{-1d,0f},new object[]{0d,1f}})
                Assert.That(()=>pool.GetType().GetMethod("Select").Invoke(pool,args),Throws.TypeOf<TargetInvocationException>().With.InnerException.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}

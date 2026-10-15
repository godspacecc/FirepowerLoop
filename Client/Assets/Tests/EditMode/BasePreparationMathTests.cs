using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class BasePreparationMathTests
    {
        private static object Call(string name, params object[] values)
        {
            var type = typeof(ItemStack).Assembly.GetType("Game.PreparationMath");
            Assert.That(type, Is.Not.Null, "尚未实现基地计时和负重规则");
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static).Invoke(null, values);
        }
        [Test] public void TwentyFiveKgAppliesBothOverweightCosts()
        {
            Assert.That((double)Call("MoveMultiplier",25000L,20000L,.02,.5),Is.EqualTo(.9).Within(.000001));
            Assert.That((double)Call("SprintMultiplier",25000L,20000L,.05,2.0),Is.EqualTo(1.25).Within(.000001));
        }
        [Test] public void OverweightCostsAreBoundedAndDisappearBelowNormalLoad()
        {
            Assert.That((double)Call("MoveMultiplier",100000L,20000L,.02,.5),Is.EqualTo(.5));
            Assert.That((double)Call("SprintMultiplier",100000L,20000L,.05,2.0),Is.EqualTo(2));
            Assert.That((double)Call("MoveMultiplier",15000L,20000L,.02,.5),Is.EqualTo(1));
        }
        [Test] public void RestBeforeEightStillTargetsFollowingDay()
        {
            Assert.That((long)Call("NextRestTime",7L*3600000,480),Is.EqualTo(115200000L));
        }
        [Test] public void CrossedDaysCreditOnceIncludingSeveralDays()
        {
            Assert.That((long)Call("DailyCredit",259200000L,1L,80L),Is.EqualTo(240L));
            Assert.That((long)Call("DailyCredit",259200000L,4L,80L),Is.EqualTo(0L));
        }
    }
}

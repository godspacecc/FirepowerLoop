using System;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>武器槽权威规则的限定测试，不加载地图或访问正式存档。</summary>
    public sealed class SurvivorWeaponTests
    {
        private object NewLoadout(int slots=3)
        {
            var type=typeof(ArcadeCombatSystem).Assembly.GetType("Game.SurvivorLoadout");
            Assert.That(type,Is.Not.Null,"缺少幸存者武器槽规则");
            return Activator.CreateInstance(type,slots,"Rifle");
        }
        private static object Call(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method).Invoke(obj,args);
        private static int Slots(object obj)=>(int)obj.GetType().GetProperty("UsedSlots").GetValue(obj);
        /// <summary>有限项目选满后剩余两项仍能选择，不因不足三张卡卡住升级。</summary>
        [Test] public void TwoRemainingChoicesStayStableAndCanBeSelected()
        {
            var state = new SurvivorRunState(10,5);
            state.AddExperience(10);
            var choices = state.OfferWeighted(new[]{3,8},new[]{10,10},7,0);
            Assert.That(choices, Is.EquivalentTo(new[]{3,8}));
            Assert.That(state.OfferWeighted(new[]{3,8},new[]{10,10},99,0), Is.EqualTo(choices));
            Assert.That(state.Choose(0,choices[0]),Is.True);
            Assert.That(state.PendingChoices,Is.Zero);
        }
        [Test] public void StartsWithOneLevelOneRifle(){var s=NewLoadout();Assert.That(Slots(s),Is.EqualTo(1));Assert.That(Call(s,"LevelOf","Rifle"),Is.EqualTo(1));Assert.That(Call(s,"LevelOf","Drone"),Is.Zero);}
        [Test] public void AcquiringThenUpgradingDroneUsesOnlyOneSlot(){var s=NewLoadout();Assert.That(Call(s,"TryUpgrade","Drone",5),Is.True);Assert.That(Call(s,"TryUpgrade","Drone",5),Is.True);Assert.That(Call(s,"LevelOf","Drone"),Is.EqualTo(2));Assert.That(Slots(s),Is.EqualTo(2));}
        [Test] public void FullSlotsRejectNewWeaponButAllowOwnedUpgrade(){var s=NewLoadout(2);Call(s,"TryUpgrade","Drone",5);Assert.That(Call(s,"CanUpgrade","Turret",5),Is.False);Assert.That(Call(s,"TryUpgrade","Turret",5),Is.False);Assert.That(Call(s,"TryUpgrade","Drone",5),Is.True);}
        [Test] public void MaximumLevelCannotBeSelectedAgain(){var s=NewLoadout();for(int i=0;i<5;i++)Call(s,"TryUpgrade","Drone",5);Assert.That(Call(s,"CanUpgrade","Drone",5),Is.False);Assert.That(Call(s,"TryUpgrade","Drone",5),Is.False);Assert.That(Call(s,"LevelOf","Drone"),Is.EqualTo(5));}
        [Test] public void InvalidWeaponDoesNotConsumeSlot(){var s=NewLoadout();Assert.That(Call(s,"TryUpgrade","",5),Is.False);Assert.That(Call(s,"TryUpgrade","Drone",0),Is.False);Assert.That(Slots(s),Is.EqualTo(1));}
        [Test] public void NewRunDoesNotInheritEquipment(){var old=NewLoadout();Call(old,"TryUpgrade","Drone",5);Assert.That(Call(NewLoadout(),"LevelOf","Drone"),Is.Zero);}
        [Test] public void WeightedFirstChoiceIsGuaranteedDistinctAndStable()
        {
            var type=typeof(ArcadeCombatSystem).Assembly.GetType("Game.SurvivorRunState");
            var state=Activator.CreateInstance(type,5,3);Call(state,"AddExperience",5L);
            var method=type.GetMethod("OfferWeighted");Assert.That(method,Is.Not.Null,"缺少加权候选与首次武器保底");
            var args=new object[]{new[]{1,2,3,4},new[]{1,1,1,1},7,4};
            var choices=(int[])method.Invoke(state,args);Assert.That(choices,Has.Length.EqualTo(3));Assert.That(choices,Does.Contain(4));Assert.That(choices,Is.Unique);
            args[2]=999;Assert.That((int[])method.Invoke(state,args),Is.EqualTo(choices));
        }
    }
}

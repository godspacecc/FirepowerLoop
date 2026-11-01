using System;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>复用真实配置和独立战斗夹具，验证本局路线与恢复，不访问玩家存档。</summary>
    public sealed class SurvivorRecoveryTests
    {
        private SurvivorRuntimeTests fixture;
        private SurvivorSystem system;
        private SurvivorModel model;
        private SurvivorConfiguration config;
        private PlayerInputCtrl input;
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            fixture=new SurvivorRuntimeTests();fixture.Setup();
            T Read<T>(string name)=>(T)typeof(SurvivorRuntimeTests).GetField(name,Flags).GetValue(fixture);
            system=Read<SurvivorSystem>("system");model=Read<SurvivorModel>("model");config=Read<SurvivorConfiguration>("config");input=Read<PlayerInputCtrl>("input");
            var architecture=Read<Game>("architecture");architecture.RegisterModel(new AppStateModel());architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            UIHelper.ResetForTests();Set("spawnClock",10000f);
        }
        [TearDown] public void Cleanup(){fixture?.Cleanup();UIHelper.ResetForTests();}
        private void Set(string name,object value)=>typeof(SurvivorSystem).GetField(name,Flags).SetValue(system,value);
        private object Call(string name,params object[] args)
        {
            var method=typeof(SurvivorSystem).GetMethod(name,Flags);Assert.That(method,Is.Not.Null,name+"尚未实现");return method.Invoke(system,args);
        }
        private int Id(string kind)
        {
            int index=config.Upgrades.ToList().FindIndex(x=>x.Kind==kind);Assert.That(index,Is.GreaterThanOrEqualTo(0),kind+"尚未进入正式配置");return index+1;
        }
        private void Grant(string kind)
        {
            int id=Id(kind);model.State.AddExperience(model.State.RequiredExperience);model.State.OfferWeighted(new[]{id},new[]{1},1,0);
            Assert.That(system.Choose(model.State.ChoiceRevision,id),Is.True);
        }
        private void Step(float seconds,bool moving=false)
        {
            typeof(PlayerInputCtrl).GetProperty("MoveInput").SetValue(input,moving?Vector2.right:Vector2.zero);
            int frames=(int)Math.Round(seconds/.05f);for(int i=0;i<frames;i++)Call("TickFrame",.05f);
        }
        [TestCase("Magazine","Reload")][TestCase("Reload","Magazine")]
        [TestCase("RecoveryStill","RecoverySafe")][TestCase("RecoveryStill","RecoveryAlways")]
        [TestCase("RecoverySafe","RecoveryStill")][TestCase("RecoverySafe","RecoveryAlways")]
        [TestCase("RecoveryAlways","RecoveryStill")][TestCase("RecoveryAlways","RecoverySafe")]
        public void FirstChoiceRemovesAndRejectsRival(string chosen,string rival)
        {
            Grant(chosen);int rivalId=Id(rival);model.State.AddExperience(model.State.RequiredExperience);Call("Offer");
            Assert.That(model.UpgradeChoices,Has.No.Member(rivalId));
            model.State.OfferWeighted(new[]{rivalId},new[]{1},1,0);
            Assert.That(system.Choose(model.State.ChoiceRevision,rivalId),Is.False);Assert.That(model.State.UpgradeLevel(rivalId),Is.Zero);
        }
        [Test] public void SeparateGroupsAndSameRouteCanUpgradeTogether()
        {
            Grant("Magazine");Grant("RecoveryStill");Grant("Magazine");Grant("RecoveryStill");
            Assert.That(model.State.UpgradeLevel(Id("Magazine")),Is.EqualTo(2));Assert.That(model.State.UpgradeLevel(Id("RecoveryStill")),Is.EqualTo(2));
        }
        [Test] public void StillRecoveryWaitsAndMovementRestartsDelay()
        {
            Grant("RecoveryStill");model.Health=50;Step(2);Assert.That(model.Health,Is.EqualTo(50));
            Step(1);Assert.That(model.Health,Is.EqualTo(51));Step(.05f,true);Step(2);Assert.That(model.Health,Is.EqualTo(51));
            Step(1);Assert.That(model.Health,Is.EqualTo(52));
        }
        [Test] public void SafeRecoveryWaitsAfterRealDamageAndIgnoresMovement()
        {
            Grant("RecoverySafe");model.Health=50;Step(5,true);Assert.That(model.Health,Is.EqualTo(50));
            Step(5,true);Assert.That(model.Health,Is.EqualTo(53));
            Call("DamageSurvivor",10);Assert.That(model.Health,Is.EqualTo(43));Step(5);Assert.That(model.Health,Is.EqualTo(43));
            Step(5);Assert.That(model.Health,Is.EqualTo(46));
        }
        [Test] public void AlwaysRecoveryAccumulatesFractionalHealthWhileMoving()
        {
            Grant("RecoveryAlways");model.Health=50;Step(10,true);Assert.That(model.Health,Is.EqualTo(53));
        }
        [Test] public void PauseAndChoiceFreezeRecoveryAndItsDelay()
        {
            Grant("RecoveryStill");model.Health=50;Step(1);system.SetPaused(true);Step(10);Assert.That(model.Health,Is.EqualTo(50));
            system.SetPaused(false);Step(1);Assert.That(model.Health,Is.EqualTo(50));
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");Step(10);Assert.That(model.Health,Is.EqualTo(50));
            Assert.That(model.Paused,Is.True);Assert.That(model.State.PendingChoices,Is.GreaterThan(0));
        }
        [Test] public void RecoveryCapsAtMaximumAndCannotReviveOrBankOverflow()
        {
            Grant("RecoveryAlways");model.Health=99;Step(10);Assert.That(model.Health,Is.EqualTo(100));
            Call("DamageSurvivor",10);Step(.05f);Assert.That(model.Health,Is.EqualTo(90));
            Call("DamageSurvivor",1000);Call("AdvanceRecovery",1f,false);Assert.That(model.Health,Is.Zero);
        }
        [Test] public void MaximumLevelScalesFromCurrentMaximumHealth()
        {
            for(int i=0;i<5;i++)Grant("RecoveryAlways");model.MaximumHealth=200;model.Health=50;
            Step(10);Assert.That(model.Health,Is.EqualTo(80));
            model.State.AddExperience(model.State.RequiredExperience);Call("Offer");Assert.That(model.UpgradeChoices,Has.No.Member(Id("RecoveryAlways")));
        }
        [Test] public void SafeRecoveryZeroDamageDoesNotInterrupt()
        {
            Grant("RecoverySafe");model.Health=50;Step(5);Call("DamageSurvivor",0);Step(5);Assert.That(model.Health,Is.EqualTo(53));
        }
        [Test] public void RealContactDamageRestartsSafeRecovery()
        {
            Grant("RecoverySafe");model.Health=50;Step(5);
            var foes=(System.Collections.Generic.Dictionary<int,ArcadeEnemyRuntime>)typeof(SurvivorSystem).GetField("enemies",Flags).GetValue(system);
            var position=(Vector2)typeof(SurvivorSystem).GetField("player",Flags).GetValue(system);
            foes.Add(9001,new ArcadeEnemyRuntime{Id=9001,ConfigId=config.EnemyId,Position=position,Health=100,Damage=10});
            Step(.05f);Assert.That(model.Health,Is.EqualTo(40));foes.Clear();
            Step(5);Assert.That(model.Health,Is.EqualTo(40));Step(5);Assert.That(model.Health,Is.EqualTo(43));
        }
        [Test] public void StopClearsRecoveryProgress()
        {
            Grant("RecoveryAlways");model.Health=50;Step(3);system.Stop();
            Call("AdvanceRecovery",.5f,false);Assert.That(model.Health,Is.EqualTo(50));
        }
    }
}

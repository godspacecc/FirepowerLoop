using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using QFramework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>真实步枪命中与本局实体验证元素弹头，不加载正式特效或写玩家存档。</summary>
    public sealed class SurvivorElementTests
    {
        private SurvivorRuntimeTests fixture;
        private SurvivorSystem system;
        private SurvivorModel model;
        private SurvivorConfiguration config;
        private Dictionary<int,ArcadeEnemyRuntime> enemies;
        private const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
        [SetUp] public void Setup()
        {
            fixture=new SurvivorRuntimeTests();fixture.Setup();
            T Read<T>(string name)=>(T)typeof(SurvivorRuntimeTests).GetField(name,Flags).GetValue(fixture);
            system=Read<SurvivorSystem>("system");model=Read<SurvivorModel>("model");config=Read<SurvivorConfiguration>("config");
            var architecture=Read<Game>("architecture");architecture.RegisterModel(new AppStateModel());architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;
            UIHelper.ResetForTests();Set("spawnClock",10000f);enemies=Get<Dictionary<int,ArcadeEnemyRuntime>>("enemies");
            Set("navigation",new SurvivorNavigation(30.5f,30.5f,.5f,Array.Empty<ArcadeObstacleDefinition>()));
        }
        [TearDown] public void Cleanup(){fixture?.Cleanup();UIHelper.ResetForTests();}
        private T Get<T>(string name)=>(T)typeof(SurvivorSystem).GetField(name,Flags).GetValue(system);
        private void Set(string name,object value)=>typeof(SurvivorSystem).GetField(name,Flags).SetValue(system,value);
        private object Call(string name,params object[] args)
        {var method=typeof(SurvivorSystem).GetMethod(name,Flags);Assert.That(method,Is.Not.Null,name+"尚未实现");return method.Invoke(system,args);}
        private int Id(string kind)
        {int at=config.Upgrades.ToList().FindIndex(x=>x.Kind==kind);Assert.That(at,Is.GreaterThanOrEqualTo(0),kind+"尚未进入配置");return at+1;}
        private void Grant(string kind)
        {int id=Id(kind);model.State.AddExperience(model.State.RequiredExperience);model.State.OfferWeighted(new[]{id},new[]{1},1,0);Assert.That(system.Choose(model.State.ChoiceRevision,id),Is.True,kind+" current="+model.State.UpgradeLevel(id)+" offered="+string.Join(",",model.UpgradeChoices));}
        private ArcadeEnemyRuntime Target(int health=1000)
        {var e=new ArcadeEnemyRuntime{Id=900,ConfigId=config.EnemyId,Health=health,MaxHealth=health,Position=new Vector2(0,.8f),Speed=2};enemies.Add(e.Id,e);return e;}
        private void Hit(string owner="Rifle")
        {Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel(owner,1),owner);Call("AdvanceBullets",.1f);}
        [TestCase("RifleFire","RifleFrost")][TestCase("RifleFire","RiflePierce")]
        [TestCase("RifleFrost","RifleFire")][TestCase("RifleFrost","RiflePierce")]
        [TestCase("RiflePierce","RifleFire")][TestCase("RiflePierce","RifleFrost")]
        public void ElementAndPierceLockEachOtherInBothDirections(string chosen,string rival)
        {
            Grant(chosen);Grant(chosen);Assert.That(model.State.UpgradeLevel(Id(chosen)),Is.EqualTo(2));
            int id=Id(rival);model.State.AddExperience(model.State.RequiredExperience);Call("Offer");Assert.That(model.UpgradeChoices,Has.No.Member(id));
            model.State.OfferWeighted(new[]{id},new[]{1},1,0);Assert.That(system.Choose(model.State.ChoiceRevision,id),Is.False);
        }
        [Test] public void FireDealsFractionalDamageForExactlyThreeSeconds()
        {
            Grant("RifleFire");var e=Target();Hit();Assert.That(e.Health,Is.EqualTo(964));
            Call("AdvanceElementDamage",1f);Assert.That(e.Health,Is.EqualTo(957));Call("AdvanceElementDamage",2f);Assert.That(e.Health,Is.EqualTo(943));
            Call("AdvanceElementDamage",10f);Assert.That(e.Health,Is.EqualTo(943));
        }
        [Test] public void RepeatedFireRefreshesWithoutStackingOrLosingFraction()
        {
            Grant("RifleFire");var e=Target();Hit();Call("AdvanceElementDamage",1f);Hit();Call("AdvanceElementDamage",3f);
            Assert.That(e.Health,Is.EqualTo(900));
        }
        [Test] public void FireKillCommitsOneKillAndNormalExperience()
        {
            Grant("RifleFire");Target(40);Hit();Call("AdvanceElementDamage",1f);Call("AdvanceElementDamage",10f);
            Assert.That(enemies,Is.Empty);Assert.That(model.Kills,Is.EqualTo(1));Assert.That(Get<System.Collections.ICollection>("drops").Count,Is.EqualTo(1));
        }
        [Test] public void FrostSlowsRealMovementThenRestoresOriginalSpeed()
        {
            Grant("RifleFrost");var e=Target();Hit();e.Position=new Vector2(0,10);Get<SurvivorNavigation>("navigation").SetTarget(Vector2.zero);
            var before=e.Position;Call("TickEnemies",.5f);Assert.That(Vector2.Distance(before,e.Position),Is.EqualTo(.8f).Within(.001f));
            Call("TickEnemies",1.5f);before=e.Position;Call("TickEnemies",.5f);Assert.That(Vector2.Distance(before,e.Position),Is.EqualTo(1f).Within(.001f));Assert.That(e.Speed,Is.EqualTo(2));
        }
        [Test] public void FrostRefreshDoesNotMultiplySlowAndHandlesPartialExpiry()
        {
            Grant("RifleFrost");var e=Target();Hit();Call("ElementMovementSeconds",e,1.5f);Hit();
            Assert.That((float)Call("ElementMovementSeconds",e,2.5f),Is.EqualTo(2.1f).Within(.00001));
        }
        [Test] public void ElementCanCombineWithMultishotAndEitherBodyBranch()
        {Grant("RifleFire");Grant("RifleMultishot");Grant("RifleExplosion");Assert.That(model.State.UpgradeLevel(Id("RifleExplosion")),Is.EqualTo(1));}
        [Test] public void DroneDoesNotApplyPlayerElement()
        {
            Grant("RifleFire");var e=Target();Hit("Drone");int hp=e.Health;Call("AdvanceElementDamage",3f);Assert.That(e.Health,Is.EqualTo(hp));
        }
        [Test] public void FireFreezesDuringPauseAndUpgradeChoice()
        {
            Grant("RifleFire");var e=Target();Hit();system.SetPaused(true);
            // 累计两秒足以产生整数燃烧伤害，避免单帧小数尚未入账造成假通过。
            for(int i=0;i<40;i++)Call("TickFrame",.05f);Assert.That(e.Health,Is.EqualTo(964));
            system.SetPaused(false);model.State.AddExperience(model.State.RequiredExperience);Call("Offer");
            for(int i=0;i<40;i++)Call("TickFrame",.05f);Assert.That(e.Health,Is.EqualTo(964));
        }
        [Test] public void StopClearsAllElementState()
        {Grant("RifleFire");Target();Hit();system.Stop();Assert.That(Get<System.Collections.IDictionary>("elementStatuses").Count,Is.Zero);}
        [Test] public void InFlightElementKeepsItsLaunchLevel()
        {
            Grant("RifleFire");var e=Target();Call("Shoot",Vector2.zero,Vector2.up,config.WeaponLevel("Rifle",1),"Rifle");Grant("RifleFire");
            Call("AdvanceBullets",.1f);Call("AdvanceElementDamage",3f);Assert.That(e.Health,Is.EqualTo(943));
        }
        [Test] public void ElementStrengthsMatchAllThreeLevels()
        {
            foreach(var kind in new[]{"RifleFire","RifleFrost"})
            {
                var u=config.Upgrades[Id(kind)-1];Assert.That(u.StatusDurationSeconds,Is.EqualTo(kind=="RifleFire"?3:2));
                Assert.That(u.StatusStrength(1),Is.EqualTo(.2f).Within(.000001));Assert.That(u.StatusStrength(2),Is.EqualTo(.3f).Within(.000001));Assert.That(u.StatusStrength(3),Is.EqualTo(.4f).Within(.000001));
            }
        }
        [Test] public void ExplosionDamageDoesNotSpreadBurn()
        {
            Grant("RifleFire");Grant("RifleExplosion");Target();var side=new ArcadeEnemyRuntime{Id=901,Health=1000,Position=new Vector2(.8f,.8f)};enemies.Add(side.Id,side);
            Hit();int health=side.Health;Assert.That(health,Is.LessThan(1000));Call("AdvanceElementDamage",3f);Assert.That(side.Health,Is.EqualTo(health));
        }
        [Test] public void SplitChildrenDoNotCarryElement()
        {
            Grant("RifleFire");Grant("RifleSplit");Target();Hit();
            var children=Get<Dictionary<int,ArcadeProjectileRuntime>>("bullets").Values.ToArray();Assert.That(children,Has.Length.EqualTo(2));
            foreach(var child in children){var e=new ArcadeEnemyRuntime{Id=1000+child.Id,Health=1000,Position=child.Position+child.Velocity.normalized*.5f};enemies.Add(e.Id,e);}
            Call("AdvanceBullets",.1f);var targets=enemies.Values.Where(e=>e.Id!=900).ToArray();Assert.That(targets.Any(e=>e.Health<1000),Is.True);var hp=targets.Select(e=>e.Health).ToArray();
            Call("AdvanceElementDamage",3f);Assert.That(targets.Select(e=>e.Health),Is.EqualTo(hp));
        }
        [Test] public void MultipleBurnDeathsDoNotSkipOrRepeatSettlement()
        {
            Grant("RifleFire");var first=Target(40);Hit();first.Position=new Vector2(10,10);
            var second=new ArcadeEnemyRuntime{Id=901,Health=40,Position=new Vector2(0,.8f)};enemies.Add(second.Id,second);Hit();
            Call("AdvanceElementDamage",1f);Call("AdvanceElementDamage",1f);Assert.That(enemies,Is.Empty);Assert.That(model.Kills,Is.EqualTo(2));
        }
        [Test] public void FrostDoesNotChangeMeleeWindupOrCooldown()
        {
            Grant("RifleFrost");var iced=Target();Hit();Assert.That(Call("PrepareEnemyRoster",new object[]{null}),Is.True);
            iced.ConfigId="enemy.arcade.zombie.shambler";iced.Position=new Vector2(.7f,0);iced.Damage=10;
            foreach(float dt in new[]{.1f,.25f,.25f})Call("TickEnemies",dt);
            int hp=model.Health;var normal=new ArcadeEnemyRuntime{Id=901,ConfigId=iced.ConfigId,Position=new Vector2(.7f,0),Damage=10,Speed=2,Health=100};
            enemies.Clear();enemies.Add(normal.Id,normal);model.Health=100;Set("hurtClock",0f);
            foreach(float dt in new[]{.1f,.25f,.25f})Call("TickEnemies",dt);
            Assert.That(model.Health,Is.EqualTo(hp));Assert.That(iced.AttackSequence,Is.EqualTo(normal.AttackSequence));Assert.That(iced.MeleeRecoveryRemaining,Is.EqualTo(normal.MeleeRecoveryRemaining));Assert.That(iced.AttackCooldown,Is.EqualTo(normal.AttackCooldown));
        }
    }
}

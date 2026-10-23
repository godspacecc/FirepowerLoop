using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>使用正式配置及单人战斗入口检查无人机不会顶掉手持枪。</summary>
    public sealed class ArcadeDroneIntegrationTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var architecture=new Game();
            var run=new ArcadeRunModel();architecture.RegisterModel(run);architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",name+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="drone-test",IsCasualRoute=true,RemainingLives=3});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            typeof(ArcadeCombatSystem).GetField("catalog",Private).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("arena",Private).SetValue(system,catalog.Arenas.Single(a=>a.ArenaNumber==1));
        }
        private object Call(string method,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(method,Private).Invoke(system,args);
        private ArcadeDropDefinition Drone=>catalog.Drops.Single(d=>d.ProductId=="arcade.support.drone");
        [Test] public void FirstStageAirdropsContainOnlyFlameAndDrone()
        {
            var ids=catalog.Drops.Where(d=>(d.Kind==ArcadeDropKind.TemporaryWeapon||d.Kind==ArcadeDropKind.Support)&&d.MinArena<=1&&d.Weight>0).Select(d=>d.ProductId);
            Assert.That(ids,Is.EquivalentTo(new[]{"arcade.weapon.flamethrower","arcade.support.drone"}));
        }
        [Test] public void TimedDroneCoexistsWithHeldFlamethrower()
        {
            runtime.TemporaryWeaponId="arcade.weapon.flamethrower";runtime.TemporaryWeaponRemaining=15;
            Assert.That((bool)Call("CanApplyDrop",Drone),Is.True);
            Assert.That((bool)Call("ApplyDrop",runtime,Drone),Is.True);
            Assert.That(runtime.TemporaryWeaponId,Is.EqualTo("arcade.weapon.flamethrower"));
            Assert.That(runtime.TemporaryWeaponRemaining,Is.EqualTo(15));
            Assert.That(runtime.Powerups.Values.Single().ProductId,Is.EqualTo("arcade.support.drone"));
            Assert.That(runtime.Powerups.Values.Single().RemainingSeconds,Is.GreaterThan(0));
        }
        [Test] public void SupportExpiresEvenWhenThereAreNoEnemies()
        {
            Call("ApplyDrop",runtime,Drone);
            Call("TickTimers",runtime,Drone.DurationSeconds+1);
            Assert.That(runtime.Powerups.Values.Any(p=>p.ProductId=="arcade.support.drone"),Is.False);
            Assert.That(runtime.Projectiles,Is.Empty);
        }
        [Test] public void ActualShotsReadPlayerTierAndRevert()
        {
            Call("ApplyDrop",runtime,Drone);
            var skull=catalog.Powerups.Single(p=>p.Behavior==ArcadePowerupBehavior.WeaponSkull&&p.Magnitude==3);
            Call("ApplyDrop",runtime,catalog.Drops.Single(d=>d.ProductId==skull.Id));
            runtime.Enemies.Add(50,new ArcadeEnemyRuntime{Id=50,Health=1000,MaxHealth=1000,Position=new Vector2(3,.35f)});
            Call("TickTimers",runtime,.01f);
            Assert.That(runtime.Projectiles.Count,Is.EqualTo(2));
            Assert.That(runtime.Projectiles.Values.All(p=>p.Damage==36),Is.True);
            runtime.Projectiles.Clear();runtime.Powerups.Remove(ArcadePowerupBehavior.WeaponSkull);
            runtime.Powerups[ArcadePowerupBehavior.Drone].Drone.FireCooldownSeconds=0;
            Call("TickTimers",runtime,.01f);
            Assert.That(runtime.Projectiles.Values.Single().Damage,Is.EqualTo(12));
        }
        [Test] public void ActualRamDamagesOnceAndExpiresAfterVisual()
        {
            Call("ApplyDrop",runtime,Drone);
            var power=runtime.Powerups[ArcadePowerupBehavior.Drone];power.RemainingSeconds=.2f;
            var enemy=new ArcadeEnemyRuntime{Id=50,Health=1000,MaxHealth=1000,Position=new Vector2(1.85f,.35f)};runtime.Enemies.Add(50,enemy);
            Call("TickTimers",runtime,.1f);
            Assert.That(enemy.Health,Is.EqualTo(940));
            Call("TickTimers",runtime,.1f);
            Assert.That(enemy.Health,Is.EqualTo(940));
            Call("TickTimers",runtime,1f);
            Assert.That(runtime.Powerups.ContainsKey(ArcadePowerupBehavior.Drone),Is.False);
        }
        [Test] public void FullProjectileBudgetDoesNotSpendDroneCooldown()
        {
            Call("ApplyDrop",runtime,Drone);
            runtime.Enemies.Add(50,new ArcadeEnemyRuntime{Id=50,Health=1000,Position=new Vector2(3,.35f)});
            for(int i=0;i<catalog.Rule.MaxProjectiles;i++)runtime.Projectiles.Add(100+i,new ArcadeProjectileRuntime{Id=100+i});
            Call("TickTimers",runtime,.01f);
            Assert.That(runtime.Projectiles.Count,Is.EqualTo(catalog.Rule.MaxProjectiles));
            Assert.That(runtime.Powerups[ArcadePowerupBehavior.Drone].CooldownSeconds,Is.EqualTo(0));
        }
        [Test]public void ExplodedDroneCannotBeSavedAsAnotherLiveDrone()
        {
            Call("ApplyDrop",runtime,Drone);runtime.Powerups[ArcadePowerupBehavior.Drone].RemainingSeconds=.2f;
            runtime.Enemies.Add(50,new ArcadeEnemyRuntime{Id=50,Health=1000,MaxHealth=1000,Position=new Vector2(1.85f,.35f)});
            Call("TickTimers",runtime,.1f);
            var checkpoint=(ArcadeIslandRoundCheckpoint)Call("CaptureIslandCheckpoint",runtime);
            Assert.That(checkpoint.Powerups.Any(p=>p.ProductId=="arcade.support.drone"),Is.False,"已爆炸无人机的视觉余时不能成为下一轮可用寿命");
        }
    }
}

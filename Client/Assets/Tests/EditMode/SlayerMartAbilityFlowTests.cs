using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过正式战斗伤害入口验证护甲，使用生成配置且不启动场景或真实存档。</summary>
    public sealed class SlayerMartAbilityFlowTests
    {
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeSlayerMartPlayer player;
        private Game architecture;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            system=new ArcadeCombatSystem();typeof(ArcadeCombatSystem).GetField("catalog",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,catalog);typeof(ArcadeCombatSystem).GetField("arena",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,catalog.Arenas[1]);typeof(ArcadeCombatSystem).GetField("random",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,new System.Random(37));
            architecture=new Game();architecture.RegisterModel(new ArcadeCombatModel());architecture.RegisterModel(new ArcadeRunModel());
            ((ICanSetArchitecture)system).SetArchitecture(architecture);
            Assert.That(provider.TryGetSlayerMart(out var config,out error),Is.True,error);
            typeof(ArcadeCombatSystem).GetField("slayerMartConfig",BindingFlags.Instance|BindingFlags.NonPublic)?.SetValue(system,config);
            player=new(){PlayerId=1,VestSlots=2,Plates=2};
            runtime=new(){SlayerMartRulesVersion=1,SlayerMart=new(){Players=new(){player}},PlayerHealth=100,PlayerMaxHealth=100};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
        }
        [Test] public void RealHitConsumesPlateBeforeHealth(){Hit();Assert.That(player.Plates,Is.EqualTo(1));Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(runtime.PlayerInvulnerability,Is.GreaterThan(0));}
        [Test] public void SecondPulseDuringProtectionConsumesNothing(){Hit();Hit();Assert.That(player.Plates,Is.EqualTo(1));}
        [Test] public void NoPlateUsesExistingHealthDamage(){player.Plates=0;Hit();Assert.That(runtime.PlayerHealth,Is.EqualTo(80));}
        [Test] public void InvulnerabilityRetainsPlate(){runtime.PlayerInvulnerability=1;Hit();Assert.That(player.Plates,Is.EqualTo(2));}
        [Test] public void DashRetainsPlate(){runtime.DashRemaining=1;Hit();Assert.That(player.Plates,Is.EqualTo(2));}
        [Test] public void RespawnWaitingRetainsPlate(){runtime.RespawnRemaining=1;Hit();Assert.That(player.Plates,Is.EqualTo(2));}
        [Test] public void LethalMechanismSkipsPlate(){Hit(SlayerMartHitKind.LethalMechanism);Assert.That(player.Plates,Is.EqualTo(2));Assert.That(runtime.PlayerHealth,Is.EqualTo(80));}
        [Test] public void OldRunCannotUseInjectedPlate(){runtime.SlayerMartRulesVersion=0;Hit();Assert.That(player.Plates,Is.EqualTo(2));Assert.That(runtime.PlayerHealth,Is.EqualTo(80));}
        [Test] public void StormCannotActivateAtNinetyNine(){ReadyStorm(99);Assert.That(Call("UseSlayerMartUpgrade",runtime),Is.False);Assert.That(player.StormCharge,Is.EqualTo(99));}
        [Test] public void StormConsumesFullChargeOnce(){ReadyStorm();Assert.That(Call("UseSlayerMartUpgrade",runtime),Is.True);Assert.That(Call("UseSlayerMartUpgrade",runtime),Is.False);Assert.That(player.StormCharge,Is.Zero);}
        [Test] public void StormInMenuRetainsCharge(){ReadyStorm();player.MenuOpen=true;Assert.That(Call("UseSlayerMartUpgrade",runtime),Is.False);Assert.That(player.StormCharge,Is.EqualTo(100));}
        [Test] public void StormRespawnCannotActivate(){ReadyStorm();runtime.RespawnRemaining=1;Assert.That(Call("UseSlayerMartUpgrade",runtime),Is.False);}
        [Test] public void StormFirstFullCycleDamagesOnlyInside(){ReadyStorm();Call("UseSlayerMartUpgrade",runtime);var near=Enemy(1,1);var far=Enemy(2,6);Call("TickSlayerMartStorm",runtime,.49f);Assert.That(near.Health,Is.EqualTo(1000));Call("TickSlayerMartStorm",runtime,.01f);Assert.That(near.Health,Is.EqualTo(980));Assert.That(far.Health,Is.EqualTo(1000));}
        [Test] public void StormFinalCycleIsNotLost(){ReadyStorm();Call("UseSlayerMartUpgrade",runtime);var e=Enemy(1,1);var dying=Enemy(2,1);dying.Health=400;Call("TickSlayerMartStorm",runtime,10f);Assert.That(e.Health,Is.EqualTo(600));Assert.That(runtime.Enemies.ContainsKey(2),Is.False);Assert.That(player.StormCharge,Is.Zero);Assert.That(player.StormRemaining,Is.Zero);}
        [Test] public void StormOrdinaryControlLeavesOnExit(){ReadyStorm();Call("UseSlayerMartUpgrade",runtime);var e=Enemy(1,1);e.Speed=3;Assert.That(Call("SlayerMartStormSpeed",runtime,e),Is.EqualTo(0f));Call("TickEnemies",runtime,.1f);Assert.That(e.Position,Is.EqualTo(new Vector2(1,0)));e.Position=new Vector2(6,0);Assert.That(Call("SlayerMartStormSpeed",runtime,e),Is.EqualTo(1f));Call("TickEnemies",runtime,.1f);Assert.That(e.Position.x,Is.LessThan(6));}
        [Test] public void StormEliteIsSlowedWithoutChangingBase(){ReadyStorm();Call("UseSlayerMartUpgrade",runtime);var e=Enemy(1,1);e.ContentSpecialKind=1;e.Speed=3;Assert.That(Call("SlayerMartStormSpeed",runtime,e),Is.EqualTo(.5f));Assert.That(e.Speed,Is.EqualTo(3));}
        [Test] public void StormBossTakesDamageWithoutControl(){ReadyStorm();Call("UseSlayerMartUpgrade",runtime);var e=Enemy(1,1);e.ContentBossKind=1;Assert.That(Call("SlayerMartStormSpeed",runtime,e),Is.EqualTo(1f));Call("TickSlayerMartStorm",runtime,.5f);Assert.That(e.Health,Is.EqualTo(980));}
        [Test] public void StormTransitionFreezesClock(){ReadyStorm();Call("UseSlayerMartUpgrade",runtime);runtime.IslandTransitionPending=true;Call("TickSlayerMartStorm",runtime,5f);Assert.That(player.StormRemaining,Is.EqualTo(10));}
        [Test] public void RealOwnKillChargesAbility(){ReadyStorm(99);var e=Enemy(1,1);Call("KillEnemy",runtime,e,false);Assert.That(player.StormCharge,Is.EqualTo(100));}
        [Test] public void CleanupKillDoesNotChargeAbility(){ReadyStorm(99);var e=Enemy(1,1);Call("KillEnemy",runtime,e,true);Assert.That(player.StormCharge,Is.EqualTo(99));}
        private void ReadyStorm(int charge=100){player.Owned.Add("slayermart.item.16");player.StormCharge=charge;}
        private ArcadeEnemyRuntime Enemy(int id,float x){var e=new ArcadeEnemyRuntime{Id=id,Position=new Vector2(x,0),Health=1000,MaxHealth=1000};runtime.Enemies.Add(id,e);return e;}
        private object Call(string method,params object[] args)
        {
            var m=typeof(ArcadeCombatSystem).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(m,Is.Not.Null,"正式能力接点尚未实现："+method);return m.Invoke(system,args);
        }
        private void Hit(SlayerMartHitKind kind=SlayerMartHitKind.Ordinary)
        {
            var method=typeof(ArcadeCombatSystem).GetMethod("DamagePlayerCore",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(method,Is.Not.Null,"正式分源伤害入口尚未接入");method.Invoke(system,new object[]{runtime,20,kind});
        }
    }
}

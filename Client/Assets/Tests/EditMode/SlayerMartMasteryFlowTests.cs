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
    /// <summary>普通拾取、发射、到期爆炸和复起保护的精通接线。</summary>
    public sealed class SlayerMartMasteryFlowTests
    {
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeSlayerMartPlayer player;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            Assert.That(provider.TryGetSlayerMart(out var config,out error),Is.True,error);
            var architecture=new Game();architecture.RegisterModel(new ArcadeCombatModel());architecture.RegisterModel(new ArcadeRunModel());
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Set("arena",catalog.Arenas[1]);Set("random",new System.Random(37));Set("slayerMartConfig",config);
            player=new(){PlayerId=1};runtime=new(){SlayerMartRulesVersion=1,SlayerMart=new(){Players=new(){player}},PlayerHealth=100,PlayerMaxHealth=100,AimDirection=Vector2.right};
            architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
        }
        [TestCase(ArcadePowerupBehavior.Chicken,10,1.25f)]
        [TestCase(ArcadePowerupBehavior.Magnet,7,1.5f)]
        [TestCase(ArcadePowerupBehavior.Gloves,9,1.6f)]
        [TestCase(ArcadePowerupBehavior.Barrel,22,1.25f)]
        public void OrdinaryPickupUsesMasteryLifetime(ArcadePowerupBehavior behavior,int mastery,float scale)
        {Own(mastery);var drop=Drop(behavior);Call("ApplyPowerup",runtime,drop);Assert.That(runtime.Powerups[behavior].RemainingSeconds,Is.EqualTo(drop.DurationSeconds*scale).Within(.0001f));}
        [Test] public void WeaponEmissionUsesOneDeadOperatorMultiplier()
        {Own(12);Call("TryFire",runtime);var weapon=catalog.Weapons.First(p=>p.Id==catalog.Rule.BaseWeaponId);Assert.That(runtime.Projectiles.Values.First().Damage,Is.EqualTo(Mathf.RoundToInt(weapon.Damage*1.25f)));}
        [Test] public void ChickenEmissionUsesOneDeadOperatorMultiplier()
        {Own(12);Enemy(1);var powerup=Powerup(ArcadePowerupBehavior.Chicken);Call("FirePowerupProjectile",runtime,powerup,Vector2.zero,0f);Assert.That(runtime.Projectiles.Values.Single().Damage,Is.EqualTo(Mathf.RoundToInt(powerup.Definition.Damage*1.25f)));}
        [Test] public void SentryEmissionUsesOnlySentryMastery()
        {Own(12);Own(23);Enemy(1);var powerup=Powerup(ArcadePowerupBehavior.Sentry);Call("FirePowerupProjectile",runtime,powerup,Vector2.zero,0f);Assert.That(runtime.Projectiles.Values.Single().Damage,Is.EqualTo(Mathf.RoundToInt(powerup.Definition.Damage*1.75f)));}
        [Test] public void DecoyExpirationUsesMasteryRadius()
        {Own(8);var powerup=Powerup(ArcadePowerupBehavior.Decoy);powerup.RemainingSeconds=.01f;runtime.Powerups.Add(ArcadePowerupBehavior.Decoy,powerup);var e=Enemy(powerup.Definition.Radius*1.5f);Call("TickTimers",runtime,.1f);Assert.That(e.Health,Is.EqualTo(1000-powerup.Definition.Damage));}
        [Test] public void ActualRespawnUsesOnlyNewProtectionMultiplier()
        {Own(21);runtime.RespawnRemaining=.01f;runtime.PlayerHealth=0;Call("TickTimers",runtime,.02f);Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(runtime.PlayerInvulnerability,Is.EqualTo(catalog.Rule.ContactInvulnerabilitySeconds*3).Within(.0001f));}
        [Test] public void ExistingLifetimeDoesNotRefreshOnMasteryPurchase()
        {Call("ApplyPowerup",runtime,Drop(ArcadePowerupBehavior.Chicken));runtime.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds=7;Own(10);Call("TickTimers",runtime,.5f);Assert.That(runtime.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds,Is.EqualTo(6.5f));}
        [Test] public void RestoreNeverMultipliesRemainingAgain()
        {Own(10);Call("ApplyPowerup",runtime,Drop(ArcadePowerupBehavior.Chicken));runtime.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds=5;var saved=(ArcadeIslandRoundCheckpoint)Call("CaptureIslandCheckpoint",runtime);Call("RestoreIslandCheckpoint",runtime,saved);Assert.That(runtime.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds,Is.EqualTo(5));}
        [Test] public void OrdinaryPickupWithoutMasteryKeepsOriginalLifetime()
        {var d=Drop(ArcadePowerupBehavior.Chicken);Call("ApplyPowerup",runtime,d);Assert.That(runtime.Powerups[ArcadePowerupBehavior.Chicken].RemainingSeconds,Is.EqualTo(d.DurationSeconds));}
        private ArcadeDropDefinition Drop(ArcadePowerupBehavior behavior)=>catalog.Drops.First(d=>d.Kind==ArcadeDropKind.Powerup&&catalog.Powerups.Any(p=>p.Id==d.ProductId&&p.Behavior==behavior));
        private ArcadePowerupRuntime Powerup(ArcadePowerupBehavior behavior)=>new(){Definition=catalog.Powerups.First(p=>p.Behavior==behavior),RemainingSeconds=10};
        private ArcadeEnemyRuntime Enemy(float x){var e=new ArcadeEnemyRuntime{Id=1,Position=new Vector2(x,0),Health=1000,MaxHealth=1000};runtime.Enemies.Add(1,e);return e;}
        private void Own(int mastery)=>player.Owned.Add("slayermart.item."+mastery.ToString("00"));
        private void Set(string field,object value)=>typeof(ArcadeCombatSystem).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,value);
        private object Call(string method,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(system,args);
    }
}

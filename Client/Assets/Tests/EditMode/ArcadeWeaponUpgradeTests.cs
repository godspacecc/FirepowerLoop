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
    /// <summary>使用正式配置及真实拾取/移动入口验证同枪升档，不加载场景。</summary>
    public sealed class ArcadeWeaponUpgradeTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeConfigCatalogDefinition catalog;
        [SetUp] public void Setup()
        {
            var architecture=new Game();var run=new ArcadeRunModel();architecture.RegisterModel(run);architecture.RegisterModel(new ArcadeCombatModel());
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            run.CommitSafeNode(new ArcadeRunSaveData{RunId="upgrade-test",RemainingLives=3,IsCasualRoute=true});run.EnterArena();
            runtime=new ArcadeCombatRuntime{ArenaNumber=1,PlayerHealth=100,PlayerMaxHealth=100,AimDirection=Vector2.up};architecture.GetModel<ArcadeCombatModel>().Begin(runtime);
            system=new ArcadeCombatSystem();((ICanSetArchitecture)system).SetArchitecture(architecture);
            Set("catalog",catalog);Set("arena",catalog.Arenas.First(a=>a.ArenaNumber==1));Set("view",new ArcadeCombatViewController());Set("random",new System.Random(5));
        }
        private void Set(string name,object value)=>typeof(ArcadeCombatSystem).GetField(name,Private).SetValue(system,value);
        private object Call(string name,params object[] args)=>typeof(ArcadeCombatSystem).GetMethod(name,Private).Invoke(system,args);
        private void Pick(string suffix)=>Assert.That(Call("ApplyDrop",runtime,catalog.Drops.First(d=>d.Id=="arcade.drop."+suffix)),Is.True);
        private int Level=>runtime.Powerups.TryGetValue(ArcadePowerupBehavior.WeaponSkull,out var s)&&s.RemainingSeconds>0?Mathf.RoundToInt(s.Definition.Magnitude)-1:0;
        [Test] public void RepeatedWeaponAdvancesRedPurpleAndCapsWithoutMultiplyingAgain()
        {
            Pick("weapon.rapid");Assert.That(Level,Is.Zero);
            Pick("weapon.rapid");Assert.That(Level,Is.EqualTo(1));
            Pick("weapon.rapid");Assert.That(Level,Is.EqualTo(2));
            Pick("weapon.rapid");Assert.That(Level,Is.EqualTo(2));
            Call("TryFire",runtime);Assert.That(runtime.Projectiles.Values.Single().Damage,Is.EqualTo(catalog.Weapons.First(w=>w.Id=="arcade.weapon.rapid").Damage*3));
        }
        [Test] public void SameWeaponRefillDoesNotShortenLongerWeaponOrPowerClock()
        {
            Pick("weapon.rapid");Pick("skull.large");runtime.TemporaryWeaponRemaining=40;runtime.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=45;
            Pick("weapon.rapid");Assert.That(runtime.TemporaryWeaponRemaining,Is.EqualTo(40));Assert.That(runtime.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds,Is.EqualTo(45));
        }
        [Test] public void EverySkullAndRepeatRefillsMinimumWithoutShorteningOrDowngrading()
        {
            Pick("weapon.rapid");Pick("skull.large");var skull=runtime.Powerups[ArcadePowerupBehavior.WeaponSkull];skull.RemainingSeconds=45;
            Pick("skull.large");Assert.That(skull.RemainingSeconds,Is.EqualTo(45));Pick("skull.small");Assert.That(Level,Is.EqualTo(2));Assert.That(skull.RemainingSeconds,Is.EqualTo(45));
            skull.RemainingSeconds=1;Pick("weapon.rapid");Assert.That(skull.RemainingSeconds,Is.GreaterThanOrEqualTo(20));
        }
        [Test] public void DifferentWeaponPreservesPowerClockAndIndependentDrone()
        {
            Pick("weapon.rapid");Pick("skull.large");Pick("support.drone");var drone=runtime.Powerups[ArcadePowerupBehavior.Drone];
            var skull=runtime.Powerups[ArcadePowerupBehavior.WeaponSkull];skull.RemainingSeconds=7;
            Pick("weapon.spread");Assert.That(Level,Is.EqualTo(2));Assert.That(skull.RemainingSeconds,Is.EqualTo(7));
            Assert.That(runtime.Powerups[ArcadePowerupBehavior.WeaponSkull],Is.SameAs(skull));
            Assert.That(runtime.Powerups[ArcadePowerupBehavior.Drone],Is.SameAs(drone));
        }
        [Test] public void PurpleMovementCombinesWithBootsAndDashThenExpires()
        {
            runtime.MoveDirection=Vector2.up;
            float Move(){runtime.PlayerPosition=Vector2.zero;Call("TickPlayer",runtime,.01f);return runtime.PlayerPosition.magnitude;}
            float normal=Move();Pick("skull.small");Assert.That(Move(),Is.EqualTo(normal).Within(.0001));
            Pick("skull.large");Assert.That(Move(),Is.EqualTo(normal*1.1f).Within(.0001));
            Pick("powerup.boots");runtime.DashRemaining=1;runtime.DashDirection=Vector2.up;float purple=Move();runtime.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=.001f;Call("TickTimers",runtime,.002f);
            Assert.That(purple,Is.EqualTo(Move()*1.1f).Within(.0001));
        }
        [Test] public void TreasureWeaponUsesTheSameUpgradeEntry()
        {
            Pick("weapon.rapid");
            var reward=new FirepowerLoop.Arcade.ArcadeTreasureRewardParameters(1,FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Weapon,0,0,0,0,1,"arcade.drop.weapon.rapid");
            Assert.That(Call("ApplyTreasureReward",runtime,reward),Is.True);Assert.That(Level,Is.EqualTo(1));
        }
        [Test] public void RestoredUpgradeKeepsIdentityAndRemainingTimeWithoutGrantingAgain()
        {
            Pick("weapon.rapid");Pick("weapon.rapid");Pick("weapon.rapid");Assert.That(Level,Is.EqualTo(2));
            runtime.Powerups[ArcadePowerupBehavior.WeaponSkull].RemainingSeconds=7;
            var saved=(ArcadeIslandRoundCheckpoint)Call("CaptureIslandCheckpoint",runtime);var restored=new ArcadeCombatRuntime{ArenaNumber=1};Call("RestoreIslandCheckpoint",restored,saved);
            var skull=restored.Powerups[ArcadePowerupBehavior.WeaponSkull];Assert.That(skull.ProductId,Is.EqualTo("arcade.powerup.skull.large"));Assert.That(skull.RemainingSeconds,Is.EqualTo(7));
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>通过正式战斗伤害入口验证护甲，使用生成配置且不启动场景或真实存档。</summary>
    public sealed class SlayerMartAbilityFlowTests
    {
        private ArcadeCombatSystem system;
        private ArcadeCombatRuntime runtime;
        private ArcadeSlayerMartPlayer player;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            system=new ArcadeCombatSystem();typeof(ArcadeCombatSystem).GetField("catalog",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,catalog);
            player=new(){PlayerId=1,VestSlots=2,Plates=2};
            runtime=new(){SlayerMartRulesVersion=1,SlayerMart=new(){Players=new(){player}},PlayerHealth=100,PlayerMaxHealth=100};
        }
        [Test] public void RealHitConsumesPlateBeforeHealth(){Hit();Assert.That(player.Plates,Is.EqualTo(1));Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(runtime.PlayerInvulnerability,Is.GreaterThan(0));}
        [Test] public void SecondPulseDuringProtectionConsumesNothing(){Hit();Hit();Assert.That(player.Plates,Is.EqualTo(1));}
        [Test] public void NoPlateUsesExistingHealthDamage(){player.Plates=0;Hit();Assert.That(runtime.PlayerHealth,Is.EqualTo(80));}
        [Test] public void InvulnerabilityRetainsPlate(){runtime.PlayerInvulnerability=1;Hit();Assert.That(player.Plates,Is.EqualTo(2));}
        [Test] public void DashRetainsPlate(){runtime.DashRemaining=1;Hit();Assert.That(player.Plates,Is.EqualTo(2));}
        [Test] public void RespawnWaitingRetainsPlate(){runtime.RespawnRemaining=1;Hit();Assert.That(player.Plates,Is.EqualTo(2));}
        [Test] public void LethalMechanismSkipsPlate(){Hit(SlayerMartHitKind.LethalMechanism);Assert.That(player.Plates,Is.EqualTo(2));Assert.That(runtime.PlayerHealth,Is.EqualTo(80));}
        [Test] public void OldRunCannotUseInjectedPlate(){runtime.SlayerMartRulesVersion=0;Hit();Assert.That(player.Plates,Is.EqualTo(2));Assert.That(runtime.PlayerHealth,Is.EqualTo(80));}
        private void Hit(SlayerMartHitKind kind=SlayerMartHitKind.Ordinary)
        {
            var method=typeof(ArcadeCombatSystem).GetMethod("DamagePlayerCore",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(method,Is.Not.Null,"正式分源伤害入口尚未接入");method.Invoke(system,new object[]{runtime,20,kind});
        }
    }
}

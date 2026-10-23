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
    /// <summary>商店效果必须走真实空投领取，下一关重复准备不能重复升档。</summary>
    public sealed class ScoreShopDeliveryTests
    {
        private ArcadeCombatSystem system;private ArcadeConfigCatalogDefinition catalog;private Game architecture;
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            architecture=new Game();architecture.RegisterModel(new ArcadeRunModel());architecture.RegisterModel(new ArcadeCombatModel());
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            // 此类只验证正式Drop领取，不依赖其他主题的路线整表校验；完整开局仍由ScoreShopRunTests覆盖。
            object[] args={tables,null};typeof(LubanRuntimeConfigProvider).GetMethod("BuildArcadeSnapshot",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
            catalog=(ArcadeConfigCatalogDefinition)args[1];system=new ArcadeCombatSystem();architecture.RegisterSystem(system);
            typeof(ArcadeCombatSystem).GetField("catalog",Hidden).SetValue(system,catalog);
        }
        private ArcadeRunSaveData Run(string id)
        {
            var shop=ArcadeScoreShopRules.Open(catalog.ScoreShop,2,1);shop.Offers[0]=id;shop.Offers=shop.Offers.Distinct().ToList();
            foreach(var item in catalog.ScoreShop.Items.Where(i=>i.Eligible(2)))if(shop.Offers.Count<3&&!shop.Offers.Contains(item.DropId))shop.Offers.Add(item.DropId);
            shop.Completed=true;shop.PurchasedDropId=id;
            var run=new ArcadeRunSaveData{NextArenaNumber=2,IsCasualRoute=true,ArenaConfigNumber=21,ScoreShopRulesVersion=1,ScoreShop=shop};
            run.EquipmentBySlot[ArcadeRunDefaults.WeaponSlotId]=catalog.Rule.BaseWeaponId;
            architecture.GetModel<ArcadeRunModel>().CommitSafeNode(run);return run;
        }
        private bool Apply(ArcadeCombatRuntime runtime,ArcadeRunSaveData run)
        {
            var method=typeof(ArcadeCombatSystem).GetMethod("TryApplyScoreShopDrop",Hidden);
            Assert.That(method,Is.Not.Null,"下一关尚未接入商店空投领取");
            object[] args={runtime,run,null};var success=(bool)method.Invoke(system,args);Assert.That(success,Is.True,args[2]?.ToString());return success;
        }
        [Test] public void Weapon_UsesFullOriginalDurationAndRepeatedPreparationDoesNotUpgradeTwice()
        {
            var run=Run("arcade.drop.weapon.rapid");var runtime=new ArcadeCombatRuntime{ArenaNumber=2,TemporaryWeaponId="arcade.weapon.rapid",TemporaryWeaponRemaining=5};
            Apply(runtime,run);Assert.That(runtime.TemporaryWeaponRemaining,Is.EqualTo(25));
            var tier=runtime.Powerups[global::ArcadePowerupBehavior.WeaponSkull].Definition.Magnitude;
            Apply(runtime,run);Assert.That(runtime.Powerups[global::ArcadePowerupBehavior.WeaponSkull].Definition.Magnitude,Is.EqualTo(tier));
        }
        [Test] public void Drone_UsesExistingSupportAndOriginalDuration()
        {
            var run=Run("arcade.drop.support.drone");var runtime=new ArcadeCombatRuntime{ArenaNumber=2};Apply(runtime,run);
            Assert.That(runtime.Powerups[global::ArcadePowerupBehavior.Drone].RemainingSeconds,Is.EqualTo(25));
        }
        [Test] public void AlreadyDeliveredOrChallenge_DoesNotReceiveAnotherDrop()
        {
            var run=Run("arcade.drop.weapon.rapid");run.ScoreShop.Delivered=true;
            var runtime=new ArcadeCombatRuntime{ArenaNumber=2};Apply(runtime,run);Assert.That(runtime.TemporaryWeaponId,Is.Null);
            run.ScoreShop.Delivered=false;runtime.IsChallenge=true;Apply(runtime,run);Assert.That(runtime.TemporaryWeaponId,Is.Null);
        }
    }
}

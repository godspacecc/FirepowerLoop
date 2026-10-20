using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed partial class EnemyDeathPresentationTests
    {
        private void FireWeapon(string weapon)
        {
            PrepareSolo(); runtime.PlayerPosition = Vector2.zero; runtime.AimDirection = Vector2.right;
            runtime.TemporaryWeaponId = weapon; runtime.TemporaryWeaponRemaining = 10;
            CallSolo("TryFire", runtime);
            Assert.That(runtime.Projectiles.Count, Is.GreaterThan(0));
        }

        [TestCase("arcade.weapon.raygun", 2.85f, 1)]
        [TestCase("arcade.weapon.rocket", 1.75f, 0)]
        [TestCase("arcade.weapon.grenade", 1.55f, 0)]
        public void ProjectileExplosionKeepsItsDeathStyleAfterWeaponChange(string weapon, float targetX, int style)
        {
            FireWeapon(weapon);
            runtime.Enemies.Add(1, new ArcadeEnemyRuntime { Id=1, Health=1, MaxHealth=20, Position=new Vector2(targetX,0) });
            runtime.Enemies.Add(2, new ArcadeEnemyRuntime { Id=2, Health=1, MaxHealth=20, Position=new Vector2(targetX,1) });
            runtime.TemporaryWeaponId="arcade.weapon.basic";
            CallSolo("TickProjectiles",runtime,.1f);
            Assert.That(recorder.Deaths.Count, Is.EqualTo(2));
            Assert.That(recorder.Deaths.All(d=>d.explosive), Is.True);
            CollectionAssert.AreEqual(new[]{style,style}, recorder.Impacts);
        }

        [TestCase(2f, .09f, 2)] [TestCase(4f, .2233333f, 0)]
        [TestCase(3f, .1566667f, 2)] [TestCase(3.01f, .1573333f, 0)]
        public void ShotgunStyleUsesFiringPositionDespiteShooterMoving(float targetX, float seconds, int style)
        {
            FireWeapon("arcade.weapon.spread");
            runtime.Enemies.Add(1,new ArcadeEnemyRuntime {Id=1,Health=1,MaxHealth=20,Position=new Vector2(targetX,0)});
            runtime.PlayerPosition=new Vector2(20,0); runtime.TemporaryWeaponId="arcade.weapon.raygun";
            CallSolo("TickProjectiles",runtime,seconds);
            Assert.That(recorder.Deaths.Count,Is.EqualTo(1));
            Assert.That(recorder.Deaths[0].explosive,Is.False);
            Assert.That(recorder.Deaths[0].direction.normalized.x,style==2?Is.GreaterThan(.95f):Is.EqualTo(-1f));
            CollectionAssert.AreEqual(new[]{style},recorder.Impacts);
        }

        [Test] public void NonLethalShotgunDoesNotDisplaceLivingEnemyOrEmitDeath()
        {
            FireWeapon("arcade.weapon.spread");
            var enemy=new ArcadeEnemyRuntime {Id=1,Health=1000,MaxHealth=1000,Position=new Vector2(2,0)};
            runtime.Enemies.Add(1,enemy); CallSolo("TickProjectiles",runtime,.09f);
            Assert.That(enemy.Health,Is.LessThan(1000)); Assert.That(enemy.Position,Is.EqualTo(new Vector2(2,0)));
            Assert.That(recorder.Deaths,Is.Empty);
        }

        [Test] public void ProjectionAndVisibleOnlineRemovalPreserveTypedImpactWithoutReplay()
        {
            var message=new ET.OnlineArcadeBattleInfo();
            var death=new ET.OnlineArcadeEnemyDeathInfo {RuntimeId=41,DirectionX=1,Explosive=true,RoundGeneration=7};
            var property=death.GetType().GetProperty("ImpactKind");
            Assert.NotNull(property,"协议需要传递权威死亡表现类型"); property.SetValue(death,1); message.EnemyDeaths.Add(death);
            var snapshot=(OnlineArcadeBattleSnapshot)typeof(OnlineArcadeRunSystem).GetMethod("CopyBattle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{message});
            property.SetValue(death,0);
            Assert.That(snapshot.EnemyDeaths[0].GetType().GetProperty("ImpactKind").GetValue(snapshot.EnemyDeaths[0]),Is.EqualTo(1));
            var online=new OnlineArcadePresentationSystem(); typeof(OnlineArcadePresentationSystem).GetField("view",Flags).SetValue(online,recorder);
            var sync=typeof(OnlineArcadePresentationSystem).GetMethod("SyncEnemies",Flags);
            sync.Invoke(online,new object[]{new[]{new OnlineArcadeEnemySnapshot(41,"enemy.arcade.basic",3,0,20,20,1,1)},false,null,7,true});
            for(int i=0;i<2;i++) sync.Invoke(online,new object[]{Array.Empty<OnlineArcadeEnemySnapshot>(),false,snapshot.EnemyDeaths,7,true});
            CollectionAssert.AreEqual(new[]{1},recorder.Impacts);
        }
    }
}

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>通过实际牛群接触与活动步进验证击飞、落地禁用和地图清理。</summary>
    public sealed class ArcadeHerdImpactTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeHerdIntegrationTests fixture;private ArcadeCombatSystem system;private ArcadeCombatRuntime runtime;
        [SetUp] public void Setup()
        {
            fixture=new ArcadeHerdIntegrationTests();fixture.Setup();
            system=(ArcadeCombatSystem)typeof(ArcadeHerdIntegrationTests).GetField("system",Hidden).GetValue(fixture);
            runtime=(ArcadeCombatRuntime)typeof(ArcadeHerdIntegrationTests).GetField("runtime",Hidden).GetValue(fixture);
        }
        [TearDown] public void Cleanup()=>fixture.Cleanup();
        private object Call(string method,params object[] args)
        {var target=typeof(ArcadeCombatSystem).GetMethod(method,Hidden);Assert.That(target,Is.Not.Null,method+"尚未接入");return target.Invoke(system,args);}
        private object Impact(object target)
        {var field=target.GetType().GetField("HerdImpact");Assert.That(field,Is.Not.Null,"牛接触后没有权威击飞状态");return field.GetValue(target);}
        private float Remaining(object target)=>(float)Impact(target).GetType().GetField("Remaining").GetValue(Impact(target));
        private void Hit(){Call("TickRoundContent",runtime,8f);Call("TickRoundContent",runtime,2f);}
        [Test] public void PlayerFliesThreeMetersThenHoldsGroundedForHalfSecond()
        {
            Hit();Assert.That(Remaining(runtime),Is.EqualTo(1.7f).Within(.001));var start=runtime.PlayerPosition;
            Call("TickHerdImpacts",runtime,.475f);Assert.That(Vector2.Distance(runtime.PlayerPosition,start),Is.EqualTo(1.5f).Within(.001));
            Call("TickHerdImpacts",runtime,.475f);Assert.That(Vector2.Distance(runtime.PlayerPosition,start),Is.EqualTo(3).Within(.001));
            var landed=runtime.PlayerPosition;Assert.That(landed.x-start.x,Is.EqualTo(1.2f).Within(.001));
            Assert.That(Mathf.Abs(landed.y-start.y),Is.EqualTo(Mathf.Sqrt(.84f)*3).Within(.001));
            Call("TickHerdImpacts",runtime,.49f);Assert.That(Remaining(runtime),Is.GreaterThan(.25f));
            Assert.That(runtime.PlayerPosition,Is.EqualTo(landed));
            Call("TickHerdImpacts",runtime,.26f);Assert.That(Remaining(runtime),Is.Zero.Within(.001));
        }
        [Test] public void ImpactBlocksWalkingShootingBombAndDashUntilRecovery()
        {
            Hit();runtime.MoveDirection=Vector2.up;runtime.Firing=true;runtime.AimDirection=Vector2.right;runtime.Bombs=2;runtime.Dashes=2;
            var position=runtime.PlayerPosition;Call("TickPlayer",runtime,.1f);Call("TryFire",runtime);Call("UseBomb",runtime);Call("UseDash",runtime);
            Assert.That(runtime.PlayerPosition,Is.EqualTo(position));Assert.That(runtime.Projectiles,Is.Empty);Assert.That(runtime.Bombs,Is.EqualTo(2));Assert.That(runtime.Dashes,Is.EqualTo(2));
        }
        [Test] public void ProtectedPlayerIsNotLaunched()
        {runtime.PlayerInvulnerability=10;Hit();Assert.That(runtime.PlayerHealth,Is.EqualTo(100));Assert.That(Remaining(runtime),Is.Zero);}
        [Test] public void ClearStopsCowsButFinishesLandingAndFreshMapClearsIt()
        {
            Hit();float before=Remaining(runtime);runtime.WaveClearAwarded=true;Call("TickRoundContent",runtime,5f);Assert.That(Remaining(runtime),Is.EqualTo(before));
            Call("TickPlayer",runtime,1.7f);Assert.That(Remaining(runtime),Is.Zero,"清场后仍应完成落地，避免锁死出口操作");
            FirepowerLoop.Arcade.ArcadeHerdImpactRules.Begin(ref runtime.HerdImpact,1,0);
            runtime.WaveIndex=1;runtime.WaveClearAwarded=false;Call("InitializeRoundContent",runtime);Assert.That(Remaining(runtime),Is.Zero);
        }
        [Test] public void LivingEnemyLaunchCancelsAttackAndDoesNotChase()
        {
            var enemy=new ArcadeEnemyRuntime{Id=12,Position=new Vector2(-26,-14),Health=10,MaxHealth=10,ContentSpecialKind=3};runtime.Enemies.Add(12,enemy);
            Hit();Assert.That(runtime.Enemies.ContainsKey(12),Is.True);Assert.That(Remaining(enemy),Is.GreaterThan(0));
            var position=enemy.Position;Call("TickEnemies",runtime,.1f);Assert.That(enemy.Position,Is.EqualTo(position));Assert.That(enemy.AttackSequence,Is.Zero);
        }
    }
}

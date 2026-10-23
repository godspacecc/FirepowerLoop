using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>正式丧尸实例的已采用抓挠姿态、权威进度和复用清理，不加载战斗地图。</summary>
    public sealed class ArcadeZombieGrabVisualTests
    {
        private GameObject actor;
        private EnemyView view;
        private AnimationClip idle;
        private Transform hand;

        [SetUp] public void Setup()
        {
            actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));
            actor.transform.SetPositionAndRotation(new Vector3(-.3f,0,0),Quaternion.Euler(0,90,0));
            view=actor.GetComponent<EnemyView>();view.BeginPresentationUse();view.SetNavigationEnabled(false);
            view.Animator.Rebind();view.Animator.Update(0);view.Animator.enabled=false;
            idle=view.Animator.runtimeAnimatorController.animationClips.First(c=>c.name.EndsWith("|Idle"));
            hand=actor.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Middle1.R");
        }
        [TearDown] public void Cleanup(){if(actor!=null)Object.DestroyImmediate(actor);}

        private void State(float remaining,float recovery)
        {
            var method=typeof(EnemyView).GetMethod("SetMeleeAttack",BindingFlags.Instance|BindingFlags.Public);
            Assert.That(method,Is.Not.Null,"正式EnemyView尚未接入权威近战进度");
            method.Invoke(view,new object[]{.4f,remaining,recovery,Vector3.right});
        }
        private void Pose(float remaining,float recovery)
        {
            view.TickCombatFeedback(0);idle.SampleAnimation(view.Animator.gameObject,.1f);
            State(remaining,recovery);view.LateTickCombatFeedback();
        }

        [Test] public void LargeGrabPullsBackAndReachesPlayer()
        {
            Pose(.1f,0);var rear=hand.position;
            Pose(0,.55f);
            Assert.That(Vector3.Distance(rear,hand.position),Is.GreaterThan(.55f),"必须明显后拉再前抓");
            Assert.That(Vector3.Distance(hand.position,new Vector3(.6f,1.3f,0)),Is.LessThan(.55f),"手掌须到达主角躯干附近");
        }
        [Test] public void RepeatedLateSamplingDoesNotAccumulate()
        {
            Pose(.1f,0);var position=hand.position;var rotation=hand.rotation;
            view.LateTickCombatFeedback();view.LateTickCombatFeedback();
            Assert.That(Vector3.Distance(hand.position,position),Is.LessThan(.0001f));
            Assert.That(Quaternion.Angle(hand.rotation,rotation),Is.LessThan(.01f));
        }
        [Test] public void CancellationReturnsToIdleWithoutSwinging()
        {
            Pose(.2f,0);State(0,0);view.TickCombatFeedback(.25f);
            idle.SampleAnimation(view.Animator.gameObject,.1f);var baseline=hand.position;
            view.LateTickCombatFeedback();
            Assert.That(Vector3.Distance(hand.position,baseline),Is.LessThan(.0001f));
        }
        [Test] public void PoolResetClearsPendingGrab()
        {
            Pose(.1f,0);view.ResetPhysicalFeedback();view.BeginPresentationUse();
            idle.SampleAnimation(view.Animator.gameObject,.1f);var baseline=hand.position;
            view.LateTickCombatFeedback();
            Assert.That(Vector3.Distance(hand.position,baseline),Is.LessThan(.0001f));
        }
        [Test] public void RecoveryUsesAuthorityRatherThanLocalClock()
        {
            Pose(0,.55f);var contact=hand.position;
            view.TickCombatFeedback(.3f);idle.SampleAnimation(view.Animator.gameObject,.1f);view.LateTickCombatFeedback();
            Assert.That(Vector3.Distance(hand.position,contact),Is.LessThan(.0001f),"不能自行播过尚未收到的权威进度");
            Pose(0,.4f);Assert.That(Vector3.Distance(hand.position,contact),Is.GreaterThan(.1f));
        }
        [Test] public void LateLoadSamplesCurrentRecoveryImmediately()
        {
            Pose(.1f,0);Pose(0,.4f);var expected=hand.position;
            view.ResetPhysicalFeedback();view.BeginPresentationUse();Pose(0,.4f);
            Assert.That(Vector3.Distance(hand.position,expected),Is.LessThan(.0001f));
        }
        [Test] public void DeathClearsPendingGrab()
        {
            Pose(.1f,0);view.TryPlayDeathAnimation(out _);
            idle.SampleAnimation(view.Animator.gameObject,.1f);var baseline=hand.position;view.LateTickCombatFeedback();
            Assert.That(Vector3.Distance(hand.position,baseline),Is.LessThan(.0001f));
        }
        [Test] public void OnlineProjectionKeepsProgressAndAuthoritativeTarget()
        {
            var system=new OnlineArcadePresentationSystem();var recorder=new Recorder();
            typeof(OnlineArcadePresentationSystem).GetField("view",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(system,recorder);
            var sync=typeof(OnlineArcadePresentationSystem).GetMethod("SyncEnemies",BindingFlags.Instance|BindingFlags.NonPublic);
            var players=new[]{new OnlineArcadeBattlePlayerSnapshot(1,8,8,0,1,100,100,true,false,0),new OnlineArcadeBattlePlayerSnapshot(2,.6f,0,0,1,100,100,true,false,0)};
            void Send(float remaining,float recovery)
            {
                var enemies=new[]{new OnlineArcadeEnemySnapshot(123,"enemy.arcade.zombie.shambler",-.3f,0,100,100,2,1,
                    meleeWindupSeconds:.4f,meleeWindupRemaining:remaining,meleeRecoveryRemaining:recovery)};
                sync.Invoke(system,new object[]{enemies,false,Array.Empty<OnlineArcadeEnemyDeathSnapshot>(),1,false,players});
                Assert.That(Vector2.Distance(recorder.Direction,new Vector2(.9f,0)),Is.LessThan(.0001f));
                Assert.That(recorder.Duration,Is.EqualTo(.4f));Assert.That(recorder.Remaining,Is.EqualTo(remaining));Assert.That(recorder.Recovery,Is.EqualTo(recovery));
            }
            Send(.2f,0);Send(.2f,0);Send(0,.4f);
        }
        /// <summary>记录真实同步入口的参数，不创建地图或模拟伤害。</summary>
        private sealed class Recorder:IArcadeCombatViewPort
        {
            public float Duration,Remaining,Recovery;public Vector2 Direction;
            public void SetEnemyMelee(int id,float duration,float remaining,float recovery,Vector2 direction)
            {Duration=duration;Remaining=remaining;Recovery=recovery;Direction=direction;}
            public bool IsActive=>true;
            public void Begin(int n,float w,float h,IReadOnlyList<ArcadeObstacleDefinition> o){}
            public void End(){} public bool TryScreenToArena(Vector2 s,out Vector2 p){p=default;return false;}
            public void SetPlayer(Vector2 p,Vector2 f,bool v,bool i){}public void SetPlayerHealth(int c,int m){}
            public void SetEnemy(int id,string config,Vector2 p,int c,int m,bool telegraph=false,int attackSequence=0,bool persistentHealthBar=false){}
            public void RemoveEnemy(int id,bool playDeath=false){}public void SetProjectile(int id,bool hostile,Vector2 p,Vector2 d){}
            public void RemoveProjectile(int id){}public void SetDrop(int id,ArcadeDropDefinition d,Vector2 p){}
            public void RemoveDrop(int id){}public void SetRemotePlayer(long id,Vector2 p,Vector2 f,bool v,bool i,int c,int m){}
            public void RemoveRemotePlayer(long id){}public void TickPlayerFeedback(float s){}
        }
    }
}

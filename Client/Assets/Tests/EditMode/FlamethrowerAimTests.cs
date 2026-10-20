using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>使用正式Animator与共享持枪入口复现喷火器偏左，不用直接SampleAnimation代替运行时播放。</summary>
    public sealed class FlamethrowerAimTests
    {
        private GameObject actor;
        private CharacterWeaponPresenter presenter;
        private Animator animator;

        [SetUp] public void SetUp()
        {
            actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            presenter=actor.GetComponent<CharacterWeaponPresenter>();animator=presenter.BodyAnimator;
            actor.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);
            Assert.That(presenter.SetWeaponId("arcade.weapon.flamethrower"),Is.True,presenter.LastError);
            // 编辑器隔离实例不一定在摄像机内，强制求值才能复现正式角色的重定向骨骼。
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0);
        }

        [TearDown] public void TearDown(){if(actor!=null)Object.DestroyImmediate(actor);}

        [TestCase(0,false)] [TestCase(0,true)]
        [TestCase(1,false)] [TestCase(1,true)]
        [TestCase(2,false)] [TestCase(2,true)]
        [TestCase(3,false)] [TestCase(3,true)]
        [TestCase(4,false)] [TestCase(4,true)]
        public void ActualAnimatorKeepsNozzleFacingAimWhileHoldingMovingAndFiring(int movement,bool firing)
        {
            actor.transform.rotation=Quaternion.Euler(0,73,0);
            var local=movement==0?Vector3.zero:movement==1?Vector3.forward:movement==2?Vector3.back:movement==3?Vector3.left:Vector3.right;
            presenter.SetMovementDirection(actor.transform.TransformDirection(local));animator.SetFloat("MoveSpeed",movement==0?0:1);
            var nozzle=presenter.Visual.GetComponentsInChildren<Transform>().Single(t=>t.name=="Nozzle");
            for(int frame=0;frame<90;frame++)
            {
                if(frame==45)actor.transform.rotation=Quaternion.Euler(0,157,0);
                presenter.TickUpperBodyGunFire(1f/60f);
                if(firing&&frame%6==0)presenter.PlayUpperBodyGunFire();
                animator.Update(1f/60f);presenter.ApplyOffhandGrip();
                var forward=Vector3.ProjectOnPlane(nozzle.TransformDirection(Vector3.up),actor.transform.up).normalized;
                Assert.That(Vector3.Dot(forward,actor.transform.forward),Is.GreaterThan(.99f),$"movement={movement}, fire={firing}, frame={frame}");
            }
        }

        [Test] public void RepeatedOriginQueriesDoNotAccumulateAimOrMoveTheActor()
        {
            animator.Update(.05f);presenter.ApplyOffhandGrip();
            var hand=animator.GetBoneTransform(presenter.Current.MainHand);var rotation=hand.rotation;var rootPosition=actor.transform.position;
            for(int i=0;i<12;i++)Assert.That(presenter.TryGetProjectileOrigin(out _),Is.True);
            Assert.That(Quaternion.Angle(hand.rotation,rotation),Is.LessThan(.05f));
            Assert.That(actor.transform.position,Is.EqualTo(rootPosition));
            Assert.That(Vector3.Dot(Vector3.ProjectOnPlane(presenter.Visual.transform.forward,actor.transform.up).normalized,actor.transform.forward),Is.GreaterThan(.99f));
        }

        [Test] public void SwitchingBackToRifleKeepsItsAnimatorDrivenHandRotation()
        {
            animator.Update(.05f);presenter.ApplyOffhandGrip();
            Assert.That(presenter.SetWeaponId("arcade.weapon.rapid"),Is.True);
            animator.Rebind();animator.Update(.05f);
            var hand=animator.GetBoneTransform(presenter.Current.MainHand);var expected=hand.rotation;
            presenter.ApplyOffhandGrip();
            Assert.That(Quaternion.Angle(hand.rotation,expected),Is.LessThan(.01f));
        }

        [Test] public void HitAnimationKeepsItsAnimatorDrivenHandRotation()
        {
            animator.Play("Base Layer.Hit",0,0);animator.Update(.05f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"),Is.True);
            var hand=animator.GetBoneTransform(presenter.Current.MainHand);var expected=hand.rotation;
            presenter.ApplyOffhandGrip();
            Assert.That(Quaternion.Angle(hand.rotation,expected),Is.LessThan(.01f));
        }

        [Test] public void ReloadKeepsItsAnimatorDrivenHandRotation()
        {
            presenter.ShowReload(.3f);animator.Update(.05f);
            Assert.That(animator.GetLayerWeight(animator.GetLayerIndex("RifleReload")),Is.EqualTo(1));
            var hand=animator.GetBoneTransform(presenter.Current.MainHand);var expected=hand.rotation;
            presenter.ApplyOffhandGrip();
            Assert.That(Quaternion.Angle(hand.rotation,expected),Is.LessThan(.01f));
        }
    }
}

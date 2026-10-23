using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>检查正式角色动画与世界粒子的交界，防止枪口转正前抢先喷出侧向火焰。</summary>
    public sealed class FlamethrowerEmissionTests
    {
        private GameObject actor;
        private CharacterWeaponPresenter presenter;
        private ParticleSystem flame;
        private Animator animator;

        [SetUp]
        public void SetUp()
        {
            actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            presenter = actor.GetComponent<CharacterWeaponPresenter>();
            actor.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);
            Assert.That(presenter.SetWeaponId("arcade.weapon.flamethrower"), Is.True, presenter.LastError);
            animator = presenter.BodyAnimator;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            flame = presenter.Visual.GetComponentInChildren<ParticleSystem>();
            flame.useAutoRandomSeed = false;
            flame.randomSeed = 123;
        }

        [TearDown]
        public void TearDown()
        {
            if (actor != null) Object.DestroyImmediate(actor);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void AnimatedHandCannotEmitSidewaysBeforeLateAlignment(int movement)
        {
            actor.transform.rotation = Quaternion.Euler(0f, 73f, 0f);
            var motion = movement == 0 ? Vector3.zero : movement == 1 ? Vector3.forward :
                movement == 2 ? Vector3.back : movement == 3 ? Vector3.left : Vector3.right;
            presenter.SetMovementDirection(actor.transform.TransformDirection(motion));
            animator.SetFloat("MoveSpeed", movement == 0 ? 0f : 1f);
            presenter.PlayUpperBodyGunFire();
            // 首次开火不能依赖碰巧有人刚读过枪口位置；本机和远端都走这一共享入口。
            animator.Update(.05f);
            presenter.PlayWeaponMuzzleEffect();
            animator.Update(.05f);
            // 复现PlayerLoop：粒子自动更新在动画之后、LateUpdate校正之前。
            if (flame.isPlaying) flame.Simulate(.05f, false, false, false);
            typeof(CharacterWeaponPresenter).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(presenter, null);
            AssertForwardParticles();
            Assert.That(flame.isPlaying, Is.False, "不能让自动粒子阶段在未校正的手骨上继续发射");
            for (int frame = 0; frame < 3; frame++)
            {
                animator.Update(.03f);
                AdvanceLatePresentation(.03f);
                AssertForwardParticles();
            }
            Assert.That(flame.particleCount, Is.GreaterThan(10), "连续发射必须由真实帧末入口推进");
        }

        [Test]
        public void PausedTimeFreezesFlameAndStopFireStillLetsItDisappear()
        {
            presenter.PlayWeaponMuzzleEffect();
            AdvanceLatePresentation(.05f);
            int count = flame.particleCount;
            float time = flame.time;
            AdvanceLatePresentation(0f);
            Assert.That(flame.particleCount, Is.EqualTo(count));
            Assert.That(flame.time, Is.EqualTo(time));
            AdvanceLatePresentation(.6f);
            Assert.That(flame.particleCount, Is.Zero, "松开开火后不能冻结最后一团火");
        }

        [Test]
        public void DisableClearsWorldFlameAndEnableDoesNotRestartOldFire()
        {
            presenter.PlayWeaponMuzzleEffect();
            AdvanceLatePresentation(.05f);
            Assert.That(flame.particleCount, Is.GreaterThan(2));
            presenter.enabled = false;
            // 非ExecuteAlways组件在编辑态不会自动收生命周期消息，显式触发真实清理入口。
            typeof(CharacterWeaponPresenter).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(presenter, null);
            Assert.That(flame.particleCount, Is.Zero);
            presenter.PlayWeaponMuzzleEffect();
            Assert.That(flame.particleCount, Is.Zero);
            presenter.enabled = true;
            AdvanceLatePresentation(.05f);
            Assert.That(flame.particleCount, Is.Zero);
            presenter.PlayWeaponMuzzleEffect();
            AssertForwardParticles();
        }

        [Test]
        public void HitStopsNewEmissionWithoutRotatingHandOrFreezingOldFlame()
        {
            presenter.PlayWeaponMuzzleEffect();
            AdvanceLatePresentation(.05f);
            int count = flame.particleCount;
            presenter.ClearUpperBodyGunFire();
            animator.Play("Base Layer.Hit", 0, 0f);
            animator.Update(.05f);
            var hand = animator.GetBoneTransform(presenter.Current.MainHand);
            var rotation = hand.rotation;
            AdvanceLatePresentation(.03f);
            Assert.That(Quaternion.Angle(hand.rotation, rotation), Is.LessThan(.01f));
            Assert.That(flame.particleCount, Is.LessThanOrEqualTo(count));
            presenter.PlayWeaponMuzzleEffect();
            Assert.That(flame.particleCount, Is.LessThanOrEqualTo(count));
            AdvanceLatePresentation(.6f);
            Assert.That(flame.particleCount, Is.Zero);
        }

        /// <summary>编辑态没有游戏deltaTime，显式调用正式LateUpdate使用的同一表现帧。</summary>
        private void AdvanceLatePresentation(float seconds) =>
            typeof(CharacterWeaponPresenter).GetMethod("UpdateLatePresentation", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(presenter, new object[] { seconds });

        private void AssertForwardParticles()
        {
            var particles = new ParticleSystem.Particle[64];
            int count = flame.GetParticles(particles);
            Assert.That(count, Is.GreaterThanOrEqualTo(2), "实际火焰不能因禁止自动发射而消失");
            for (int index = 0; index < count; index++)
            {
                var direction = Vector3.ProjectOnPlane(particles[index].velocity, actor.transform.up).normalized;
                Assert.That(Vector3.Dot(direction, actor.transform.forward), Is.GreaterThan(.97f),
                    $"粒子{index}必须在向前锥体内，实际速度为{particles[index].velocity}");
            }
        }
    }
}

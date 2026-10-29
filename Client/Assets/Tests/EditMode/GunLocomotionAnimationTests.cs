using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>使用正式玩家和动作资源验证移动射击分层，覆盖本机与远端反馈入口。</summary>
    public sealed class GunLocomotionAnimationTests
    {
        [TestCase("arcade.weapon.basic", false)]
        [TestCase("arcade.weapon.rapid", false)]
        [TestCase("arcade.weapon.spread", false)]
        [TestCase("arcade.weapon.piercing", false)]
        [TestCase("arcade.weapon.raygun", false)]
        [TestCase("arcade.weapon.rocket", false)]
        [TestCase("arcade.weapon.basic", true)]
        [TestCase("arcade.weapon.rapid", true)]
        public void ContinuousGunfireKeepsLocomotionOnBaseLayer(string weapon, bool remote)
        {
            WithPlayer((presenter, view, animator, peer) =>
            {
                Equip(presenter, view, animator, weapon);
                view.SetMovementSpeed(3f);
                presenter.SetMovementDirection(Vector3.forward);
                Step(animator, view, 20);
                var leg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                var initialLegRotation = leg.localRotation;
                float largestLegAngle = 0f;
                for (var shot = 0; shot < 6; shot++)
                {
                    if (remote) peer.SetTarget(peer.Instance.transform.localPosition + Vector3.forward * .3f, 0f);
                    if (remote) peer.PlayProjectileFeedback();
                    else view.PlayProjectileFireFeedback();
                    Step(animator, view, 6, remote ? peer : null);
                    largestLegAngle = Mathf.Max(largestLegAngle, Quaternion.Angle(initialLegRotation, leg.localRotation));
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"), Is.True,
                        "连续射击不能抢占腿部移动状态");
                    Assert.That(animator.GetCurrentAnimatorClipInfo(0).Any(c =>
                        c.clip == presenter.Current.Animations.MoveForward.Clip), Is.True);
                }
                Assert.That(largestLegAngle, Is.GreaterThan(1f), "连续射击期间腿骨应持续运动，不能只改变状态名称");
                int layer = animator.GetLayerIndex("Shared Gun Upper Body");
                Assert.That(layer, Is.GreaterThan(0));
                Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f));
                Assert.That(animator.GetCurrentAnimatorClipInfo(layer).Any(c =>
                    c.clip == presenter.Current.Animations.Attack.Clip), Is.True);
                var controller = (AnimatorController)((AnimatorOverrideController)animator.runtimeAnimatorController).runtimeAnimatorController;
                var mask = controller.layers[layer].avatarMask;
                Assert.That(mask, Is.Not.Null);
                foreach (var part in new[] { AvatarMaskBodyPart.Root, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg,
                             AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK })
                    Assert.That(mask.GetHumanoidBodyPartActive(part), Is.False, part.ToString());
                Assert.That(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body), Is.True);
                Step(animator, view, 90);
                Assert.That(animator.GetLayerWeight(layer), Is.Zero, "射击结束应撤销上身覆盖");
            });
        }

        [TestCase("weapon.greatsword")]
        [TestCase("weapon.short-sword")]
        public void MeleeStillUsesFullBodyAttackWhileMoving(string weapon)
        {
            WithPlayer((presenter, view, animator, peer) =>
            {
                Equip(presenter, view, animator, weapon);
                view.SetMovementSpeed(3f);
                Step(animator, view, 20);
                view.PlayWeaponSwingFeedback();
                Step(animator, view, 5);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True);
                int layer = animator.GetLayerIndex("Shared Gun Upper Body");
                if (layer >= 0) Assert.That(animator.GetLayerWeight(layer), Is.Zero);
            });
        }

        [Test]
        public void MovingGunUsesConfiguredFirePoseOnGunLayer()
        {
            WithPlayer((presenter, view, animator, peer) =>
            {
                Equip(presenter, view, animator, "arcade.weapon.basic");
                view.SetMovementSpeed(3f);
                Step(animator, view, 20);
                var entry = presenter.Current;
                entry.GetPose(entry.Animations.Attack, out var position, out var rotation, out var scale);
                view.PlayProjectileFireFeedback();
                Step(animator, view, 6);
                Assert.That(presenter.TryGetProjectileOrigin(out _), Is.True);
                // 当前基础枪使用 AK；射击与移动可共享姿态，仍须验证后坐与动作姿态的组合。
                var kick = presenter.Visual.transform.localPosition - position;
                Assert.That(Vector3.Dot(kick, rotation * Vector3.back), Is.GreaterThan(0f));
                Assert.That(kick.magnitude, Is.LessThanOrEqualTo(.0351f));
                Assert.That(Vector3.Cross(kick, rotation * Vector3.back).magnitude, Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(presenter.Visual.transform.localRotation, rotation), Is.LessThan(.01f),
                    "上身射击时挂点应使用射击动作覆盖值，而非腿部移动片段");
                Assert.That(Vector3.Distance(presenter.Visual.transform.localScale, scale), Is.LessThan(.0001f));
                Step(animator, view, 3);
                Assert.That(presenter.TryGetProjectileOrigin(out _), Is.True);
                Assert.That(Vector3.Distance(presenter.Visual.transform.localPosition, position), Is.LessThan(.0001f),
                    "短后坐结束后应回到当前射击姿态，不能累积偏移或退回行走姿态");
            });
        }

        [Test]
        public void ReloadRemovesPendingGunOverlay()
        {
            WithPlayer((presenter, view, animator, peer) =>
            {
                Equip(presenter, view, animator, "arcade.weapon.rapid");
                view.PlayProjectileFireFeedback();
                Step(animator, view, 3);
                int layer = animator.GetLayerIndex("Shared Gun Upper Body");
                Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f));
                view.ShowActionProgress(.1f, null);
                animator.Update(0f);
                Assert.That(animator.GetLayerWeight(layer), Is.Zero, "射击余帧不能覆盖换弹");
                view.PlayProjectileFireFeedback();
                Step(animator, view, 2);
                Assert.That(animator.GetLayerWeight(layer), Is.Zero);
                view.HideActionProgress();
                view.PlayProjectileFireFeedback();
                Step(animator, view, 2);
                Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f));
            });
        }

        [TestCase("switch")]
        [TestCase("hurt")]
        [TestCase("death")]
        public void InterruptionsRemoveGunOverlay(string interruption)
        {
            WithPlayer((presenter, view, animator, peer) =>
            {
                Equip(presenter, view, animator, "arcade.weapon.basic");
                view.PlayProjectileFireFeedback();
                Step(animator, view, 6);
                int layer = animator.GetLayerIndex("Shared Gun Upper Body");
                Assert.That(layer, Is.GreaterThan(0));
                Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f));
                if (interruption == "switch") presenter.SetWeaponId("weapon.short-sword");
                else if (interruption == "hurt") view.PlayHurtFeedback();
                else view.SetAlivePresentation(false);
                Step(animator, view, 6);
                Assert.That(animator.GetLayerWeight(layer), Is.Zero);
                if (interruption != "switch")
                {
                    view.PlayProjectileFireFeedback();
                    Step(animator, view, 2);
                    Assert.That(animator.GetLayerWeight(layer), Is.Zero, "受击或死亡期间不能重新覆盖全身动作");
                }
            });
        }

        [Test]
        public void ArcadeHurtKeepsRunningAndGunfireUntilDeath()
        {
            WithPlayer((presenter, view, animator, peer) =>
            {
                view.SetArcadePresentationActive(true);
                Equip(presenter, view, animator, "arcade.weapon.basic");
                view.SetMovementSpeed(3f);
                Step(animator, view, 20);
                view.PlayProjectileFireFeedback();
                Step(animator, view, 2);
                int layer = animator.GetLayerIndex("Shared Gun Upper Body");
                view.PlayHurtFeedback();
                Step(animator, view, 2);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Run"), Is.True,
                    "街机受击不能用全身 Hit 动作打断移动表现");
                Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f), "非致命受击不能撤销当前射击");
                view.SetAlivePresentation(false);
                Assert.That(animator.GetLayerWeight(layer), Is.Zero, "死亡仍须撤销射击");
            });
        }

        [Test]
        public void ArcadeHurtAfterWeaponChangesOnlyTintsVisibleBody()
        {
            WithPlayer((presenter, view, animator, peer) =>
            {
                view.SetArcadePresentationActive(true);
                foreach (var weapon in new[] { "arcade.weapon.basic", "arcade.weapon.rapid" })
                {
                    Equip(presenter, view, animator, weapon);
                    view.PlayHurtFeedback();
                    var block = new MaterialPropertyBlock();
                    var visibleBody = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .First(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy);
                    visibleBody.GetPropertyBlock(block);
                    Assert.That(block.GetColor("_BaseColor"), Is.EqualTo(Color.red));
                    foreach (var renderer in presenter.Visual.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.GetPropertyBlock(block);
                        Assert.That(block.HasColor("_Color") && block.GetColor("_Color") == Color.red, Is.False,
                            "枪体不参与人物受击闪红，入场与切装顺序不能改变该约定");
                    }
                    view.TickCombatFeedback(1f);
                    visibleBody.GetPropertyBlock(block);
                    Assert.That(block.HasColor("_BaseColor"), Is.False, "结束后应撤销临时覆盖");
                }
            });
        }

        private static void Equip(CharacterWeaponPresenter presenter, PlayerView view, Animator animator, string id)
        {
            Assert.That(presenter.SetWeaponId(id), Is.True, presenter.LastError);
            view.SetSharedWeaponAnimationActive(true);
            animator.Rebind();
            animator.Update(0f);
        }

        private static void Step(Animator animator, PlayerView view, int frames, OnlineRemotePlayerViewCtrl remote = null)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                animator.Update(1f / 60f);
                if (remote != null) remote.Tick(1f / 60f);
                else view.TickCombatFeedback(1f / 60f);
            }
        }

        /// <summary>隔离实例只使用正式 Prefab；关闭其他 MonoBehaviour，避免测试启动地图与存档。</summary>
        private static void WithPlayer(Action<CharacterWeaponPresenter, PlayerView, Animator, OnlineRemotePlayerViewCtrl> check)
        {
            var holder = new GameObject("GunLocomotionAnimationTest");
            holder.SetActive(false);
            OnlineRemotePlayerViewCtrl remote = null;
            try
            {
                var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), holder.transform);
                foreach (var behaviour in player.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                var presenter = player.GetComponent<CharacterWeaponPresenter>();
                var animator = presenter.BodyAnimator;
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                holder.SetActive(true);
                remote = new OnlineRemotePlayerViewCtrl(player);
                check(presenter, player.GetComponent<PlayerView>(), animator, remote);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }
    }
}

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>以正式目录和玩家 Animator 检查单刀表现，不向地图或街机添加装备入口。</summary>
    public sealed class SingleMeleeAnimationTests
    {
        private const string CatalogPath = "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset";

        [TestCase("weapon.knife")]
        [TestCase("weapon.axe")]
        [TestCase("weapon.pickaxe")]
        [TestCase("weapon.han.axe")]
        [TestCase("weapon.han.pick")]
        public void SingleSlashUsesDifferentMotionWithoutInstallingSteelBladeCombo(string id)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(CatalogPath);
            var entry = catalog.Find(id);
            Assert.That(entry.Animations.Attack.Clip, Is.Not.SameAs(catalog.Find("weapon.short-sword").Animations.Attack.Clip));
            Assert.That(entry.MeleeComboController, Is.Null);
            Assert.That(entry.MeleeCombo, Is.Empty);
            Assert.That(entry.Animations.Attack.Clip.isLooping, Is.False);
            Assert.That(AnimationUtility.GetAnimationEvents(entry.Animations.Attack.Clip), Is.Empty,
                "动作只能提供表现，不能由动画事件追加命中");
        }

        [TestCase("weapon.knife")]
        [TestCase("weapon.axe")]
        [TestCase("weapon.pickaxe")]
        [TestCase("weapon.han.axe")]
        [TestCase("weapon.han.pick")]
        public void SingleSlashCompletesWithinExistingOrdinaryIntervalAtRestAndInMotion(string id)
        {
            WithPlayer((presenter, view, animator) =>
            {
                foreach (var speed in new[] { 0f, 2f })
                {
                    Assert.That(presenter.SetWeaponId(id), Is.True, presenter.LastError);
                    animator.Rebind();
                    view.SetMovementSpeed(speed);
                    animator.Update(0.12f);
                    view.PlayWeaponSwingFeedback();
                    var playback = animator.GetFloat("AttackPlaybackSpeed");
                    Assert.That(presenter.Current.Animations.Attack.Clip.length / playback,
                        Is.LessThanOrEqualTo(0.35f + 0.001f));
                    Step(animator, view, 4);
                    Assert.That(animator.GetCurrentAnimatorClipInfo(0).Any(info =>
                        info.clip == presenter.Current.Animations.Attack.Clip), Is.True);
                    for (var frame = 0; frame < 24; frame++)
                    {
                        animator.Update(1f / 60f);
                        view.TickCombatFeedback(1f / 60f);
                    }
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.False);
                    if (animator.IsInTransition(0))
                        Assert.That(animator.GetNextAnimatorStateInfo(0).IsName("Attack"), Is.False,
                            "允许恢复移动时混合，但不能留在攻击转换中");
                    Assert.That(animator.GetFloat("AttackPlaybackSpeed"), Is.EqualTo(1f));
                    Assert.That(animator.speed, Is.EqualTo(1f), "不应加速整个人物 Animator");
                }
            });
        }

        [TestCase("switch")]
        [TestCase("hurt")]
        [TestCase("death")]
        public void InterruptedSlashClearsSpeedAndRespectsExistingInterruptState(string interruption)
        {
            WithPlayer((presenter, view, animator) =>
            {
                presenter.SetWeaponId("weapon.han.axe");
                animator.Rebind();
                animator.Update(0f);
                view.PlayWeaponSwingFeedback(0.45f);
                Step(animator, view, 4);
                if (interruption == "switch") presenter.SetWeaponId("weapon.short-sword");
                else if (interruption == "hurt") view.PlayHurtFeedback();
                else view.SetAlivePresentation(false);
                Step(animator, view, 12);
                Assert.That(animator.GetFloat("AttackPlaybackSpeed"), Is.EqualTo(1f));
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.False);
                if (interruption == "death") Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Death"), Is.True);
                if (interruption == "hurt") Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"), Is.True);
            });
        }

        private static void Step(Animator animator, PlayerView view, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                animator.Update(1f / 60f);
                view.TickCombatFeedback(1f / 60f);
            }
        }

        [Test]
        public void ProtectedWeaponsKeepThrustTwoHandAndSteelBladeFourStages()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(CatalogPath);
            var thrust = AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(
                "Assets/Game/Art/Characters/Presentation/Definitions/MeleeAttack.asset");
            Assert.That(catalog.Find("weapon.short-sword").Animations.Attack, Is.SameAs(thrust));
            Assert.That(catalog.Find("weapon.greatsword").Animations.Attack,
                Is.SameAs(AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(
                    "Assets/Game/Art/Characters/Presentation/Definitions/TwoHandAttack.asset")));
            var blade = catalog.Find("weapon.han.blade");
            Assert.That(blade.MeleeCombo, Has.Length.EqualTo(4));
            Assert.That(blade.MeleeComboController, Is.Not.Null);
            Assert.That(blade.Animations.Attack, Is.SameAs(blade.MeleeCombo[0]));
        }

        private static void WithPlayer(System.Action<CharacterWeaponPresenter, PlayerView, Animator> check)
        {
            var holder = new GameObject("SingleMeleeAnimationTest");
            holder.SetActive(false);
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
                animator.Rebind();
                animator.Update(0f);
                check(presenter, player.GetComponent<PlayerView>(), animator);
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}

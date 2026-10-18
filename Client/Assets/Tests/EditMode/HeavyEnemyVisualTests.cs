using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Tests
{
    /// <summary>重型替换必须保留战斗根合同，真实五动作和武器重定向不得拉伸或丢失。</summary>
    public sealed class HeavyEnemyVisualTests
    {
        private const string Path = "Assets/Game/Prefabs/Characters/ArcadeEnemyBrute.prefab";

        [Test]
        public void BruteUsesLocalHumanoidCandidateAndKeepsCombatRoot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(AssetDatabase.GetAssetPath(animator.avatar),
                Is.EqualTo("Assets/Game/Art/Enemies/InfectedBrute/Source/BruteWyder.fbx"));
            Assert.That(animator.avatar.isValid && animator.avatar.isHuman, Is.True);
            Assert.That(prefab.transform.Find("ArcadeVisual").localScale.x, Is.EqualTo(1.05f).Within(.001f));
            var agent = prefab.GetComponent<NavMeshAgent>();
            Assert.That(agent.radius, Is.EqualTo(.35f).Within(.001f));
            Assert.That(agent.height, Is.EqualTo(1.8f).Within(.001f));
            Assert.That(agent.speed, Is.EqualTo(2.2f).Within(.001f));
        }

        [Test]
        public void FiveRetargetedActionsKeepFiniteBodyAndBoundedWeapon()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var view = instance.GetComponent<EnemyView>();
                Assert.That(view.SetCombatWeapon("weapon.greatsword"), Is.True);
                var animator = view.Animator;
                Assert.That(AssetDatabase.GetAssetPath(animator.avatar), Does.Contain("InfectedBrute"));
                foreach (string state in new[] { "Idle", "Run", "Attack", "Hit", "Death" })
                {
                    animator.Rebind(); animator.Update(0f); animator.Play(state, 0, .35f); animator.Update(.02f);
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, state);
                    instance.GetComponent<CharacterWeaponPresenter>().ApplyOffhandGrip();
                    var weapon = instance.GetComponent<CharacterWeaponPresenter>().Visual;
                    Assert.That(weapon, Is.Not.Null, state);
                    Assert.That(weapon.GetComponentsInChildren<Renderer>().All(r => r.bounds.size.magnitude < 3f), Is.True, state);
                    foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Assert.That(renderer.bones.All(bone => bone != null), Is.True, state);
                        Assert.That(float.IsNaN(renderer.bounds.size.sqrMagnitude), Is.False, state);
                    }
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void HeavyVariantsRemainDistinctAndCompleteWhenReused()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path));
            try
            {
                var view = instance.GetComponent<EnemyView>();
                Assert.That(instance.transform.Find("ArcadeVisual/InfectedBruteBody"), Is.Not.Null);
                Assert.That(view.PresentationVariantCount, Is.EqualTo(6));
                for (int id = 0; id < 12; id++)
                {
                    view.SetPresentationVariant(id);
                    var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Assert.That(renderers.Count(r => r.enabled), Is.EqualTo(1));
                    Assert.That(renderers.All(r => r.sharedMesh != null && r.sharedMaterials.All(m => m != null)), Is.True);
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class CombatFeedbackTests
    {
        [Test]
        public void PlayerAttackFeedbackTemporarilyExpandsAndRestoresScale()
        {
            var root = new GameObject("Player", typeof(CharacterController), typeof(PlayerView));
            try
            {
                var view = root.GetComponent<PlayerView>();
                root.transform.localScale = Vector3.one;

                view.PlayAttackFeedback();

                Assert.That(root.transform.localScale.z, Is.GreaterThan(1f));

                view.TickCombatFeedback(1f);

                Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PlayerHurtFeedbackTemporarilyFlashesRedAndRestoresColor()
        {
            var root = new GameObject("Player", typeof(CharacterController), typeof(MeshRenderer), typeof(PlayerView));
            try
            {
                var view = root.GetComponent<PlayerView>();
                var renderer = root.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = CreateMaterial(Color.white);

                view.PlayHurtFeedback();

                Assert.That(GetFeedbackColor(renderer), Is.EqualTo(Color.red));

                view.TickCombatFeedback(1f);

                Assert.That(GetFeedbackColor(renderer), Is.EqualTo(Color.white));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FormalPlayerHumanoidReceivesMovementSpeed()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            instance.SetActive(true);

            try
            {
                var view = instance.GetComponent<PlayerView>();
                Assert.NotNull(view.Animator);

                view.SetMovementSpeed(3.5f);

                Assert.That(view.Animator.GetFloat("MoveSpeed"), Is.EqualTo(3.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FormalPlayerHumanoidBindsWeaponsToRightHandAndKeepsDeathVisible()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            instance.SetActive(true);

            try
            {
                var view = instance.GetComponent<PlayerView>();
                var hand = view.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                Assert.That(view.TryGetWeaponAttachRoot(global::PlayerWeaponAttachPoint.MeleeWeapon,
                    out var meleeRoot), Is.True);
                Assert.That(view.TryGetWeaponAttachRoot(global::PlayerWeaponAttachPoint.RangedWeapon,
                    out var rangedRoot), Is.True);
                Assert.That(meleeRoot.parent, Is.SameAs(hand));
                Assert.That(rangedRoot.parent, Is.SameAs(hand));

                var humanoidRenderer = view.Animator.GetComponentInChildren<SkinnedMeshRenderer>();
                view.SetAlivePresentation(false);
                Assert.That(view.CharacterController.enabled, Is.False);
                Assert.That(humanoidRenderer.enabled, Is.True);
                Assert.That(view.Animator.GetBool("Dead"), Is.True);

                view.SetAlivePresentation(true);
                Assert.That(view.CharacterController.enabled, Is.True);
                Assert.That(humanoidRenderer.enabled, Is.True);
                Assert.That(view.Animator.GetBool("Dead"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FormalPlayerRangedWeaponStaysUprightWhenHandBoneRolls()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            instance.SetActive(true);

            try
            {
                var view = instance.GetComponent<PlayerView>();
                Assert.That(view.TryGetWeaponAttachRoot(global::PlayerWeaponAttachPoint.RangedWeapon,
                    out var rangedRoot), Is.True);
                var hand = view.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                hand.rotation *= Quaternion.Euler(35f, 20f, 75f);

                view.TickCombatFeedback(0f);

                Assert.That(Vector3.Dot(rangedRoot.up, Vector3.up), Is.GreaterThan(0.999f));
                Assert.That(Vector3.Dot(rangedRoot.forward, instance.transform.forward),
                    Is.GreaterThan(0.999f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FormalPlayerProjectileFeedbackRecoilsWeaponAndRestoresPosition()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            instance.SetActive(true);

            try
            {
                var view = instance.GetComponent<PlayerView>();
                Assert.That(view.TryGetWeaponAttachRoot(global::PlayerWeaponAttachPoint.RangedWeapon,
                    out var rangedRoot), Is.True);
                var basePosition = rangedRoot.localPosition;

                view.PlayProjectileFireFeedback();

                Assert.That(rangedRoot.localPosition, Is.Not.EqualTo(basePosition));
                view.TickCombatFeedback(0.2f);
                Assert.That(rangedRoot.localPosition, Is.EqualTo(basePosition));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FormalPlayerFirearmUsesTrackedMuzzleFlashAtProjectileOrigin()
        {
            const string effectPath = "Assets/Game/Art/Player/FX/PlayerMuzzleFlash.prefab";
            var dependencies = AssetDatabase.GetDependencies(effectPath, true);
            Assert.That(dependencies, Has.None.StartsWith("Assets/Art/"));

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            instance.SetActive(true);

            try
            {
                var view = instance.GetComponent<PlayerView>();
                var flash = instance.GetComponentsInChildren<ParticleSystem>(true)
                    .First(x => x.name == "PlayerMuzzleFlash");
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadePistol", true,
                    new Vector3(0f, 0.04f, 0.43f));

                Assert.That(flash.transform.position,
                    Is.EqualTo(view.GetProjectileOrigin(Vector3.forward)));
                view.PlayProjectileFireFeedback();
                Assert.That(flash.isPlaying, Is.True);

                flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(view, "ArcadeGrenade", false,
                    new Vector3(0f, 0.02f, 0.32f));
                view.PlayProjectileFireFeedback();
                Assert.That(flash.isPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void EnemyHitFeedbackTemporarilyFlashesRedAndRestoresColor()
        {
            var root = new GameObject("Enemy", typeof(BoxCollider), typeof(MeshRenderer), typeof(EnemyView));
            try
            {
                var view = root.GetComponent<EnemyView>();
                var renderer = root.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = CreateMaterial(Color.white);

                view.PlayHitFeedback();

                Assert.That(GetFeedbackColor(renderer), Is.EqualTo(Color.red));

                view.TickCombatFeedback(1f);

                Assert.That(GetFeedbackColor(renderer), Is.EqualTo(Color.white));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material CreateMaterial(Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = color;
            return material;
        }

        private static Color GetFeedbackColor(Renderer targetRenderer)
        {
            var propertyBlock = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(propertyBlock);
            return propertyBlock.GetColor("_Color");
        }
    }
}

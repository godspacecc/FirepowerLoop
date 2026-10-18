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
        public void RepeatedHurtRestoresExistingOverridesWithoutTintingHiddenOrHudRenderers()
        {
            var root = new GameObject("Player", typeof(CharacterController), typeof(MeshRenderer), typeof(PlayerView));
            var material = CreateMaterial(Color.white);
            try
            {
                var body = root.GetComponent<MeshRenderer>();
                body.sharedMaterial = material;
                var hidden = new GameObject("HiddenOutfit", typeof(MeshRenderer));
                hidden.transform.SetParent(root.transform);
                hidden.GetComponent<MeshRenderer>().sharedMaterial = material;
                hidden.SetActive(false);
                var hud = new GameObject("HUD", typeof(Canvas), typeof(MeshRenderer));
                hud.transform.SetParent(root.transform);
                hud.GetComponent<MeshRenderer>().sharedMaterial = material;
                var original = new MaterialPropertyBlock();
                original.SetColor("_Color", Color.blue);
                original.SetFloat("_ExistingValue", .7f);
                body.SetPropertyBlock(original);
                var view = root.GetComponent<PlayerView>();
                typeof(PlayerView).GetField("arcadeProtectionShader", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(view, AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Unlit.shader"));
                view.SetArcadePresentationActive(true);
                view.PlayHurtFeedback();
                Assert.That(GetFeedbackColor(body), Is.EqualTo(Color.red));
                Assert.That(GetFeedbackColor(hidden.GetComponent<MeshRenderer>()), Is.EqualTo(Color.white));
                Assert.That(GetFeedbackColor(hud.GetComponent<MeshRenderer>()), Is.EqualTo(Color.white));
                view.PlayHurtFeedback();
                view.TickCombatFeedback(1f);
                body.GetPropertyBlock(original);
                Assert.That(original.GetColor("_Color"), Is.EqualTo(Color.blue));
                Assert.That(original.GetFloat("_ExistingValue"), Is.EqualTo(.7f));
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(material); }
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
        public void FormalPlayerSharedWeaponKeepsConfiguredPoseWhenHandBoneRolls()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            instance.SetActive(true);

            try
            {
                var view = instance.GetComponent<PlayerView>();
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                Assert.That(presenter.SetWeaponId("arcade.weapon.basic"), Is.True, presenter.LastError);
                view.SetSharedWeaponAnimationActive(true);
                view.Animator.Rebind(); view.Animator.Update(0f);
                presenter.ApplyOffhandGrip();
                var weapon = presenter.Visual.transform;
                var localPosition = weapon.localPosition; var localRotation = weapon.localRotation;
                var worldRotation = weapon.rotation;
                var hand = view.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                hand.rotation *= Quaternion.Euler(35f, 20f, 75f);

                view.TickCombatFeedback(0f);
                presenter.ApplyOffhandGrip();

                Assert.That(weapon.parent, Is.EqualTo(hand), "正式目录枪体应跟随配置的主手骨");
                Assert.That(Vector3.Distance(weapon.localPosition, localPosition), Is.LessThan(.0001f));
                Assert.That(Quaternion.Angle(weapon.localRotation, localRotation), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(weapon.rotation, hand.rotation * localRotation), Is.LessThan(.01f));
                Assert.That(Quaternion.Angle(weapon.rotation, worldRotation), Is.GreaterThan(1f),
                    "共享动作枪体应随手骨运动，旧世界直立约束不能覆盖配置姿态");
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
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                Assert.That(presenter.SetWeaponId("arcade.weapon.basic"), Is.True, presenter.LastError);
                view.SetSharedWeaponAnimationActive(true);
                view.Animator.Rebind(); view.Animator.Update(0f);
                var flash = instance.GetComponentsInChildren<ParticleSystem>(true)
                    .First(x => x.name == "PlayerMuzzleFlash");

                view.PlayProjectileFireFeedback();
                Assert.That(Vector3.Distance(flash.transform.position, view.GetProjectileOrigin(Vector3.forward)),
                    Is.LessThan(.00001f), "世界坐标父子变换允许浮点舍入，枪口偏差仍必须小于0.01毫米");
                Assert.That(flash.isPlaying, Is.True);

                flash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                Assert.That(presenter.SetWeaponId("arcade.weapon.grenade"), Is.True, presenter.LastError);
                view.Animator.Rebind(); view.Animator.Update(0f);
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

        [Test]
        public void EnemyMultimaterialFeedbackRestoresOverridesAfterRepeatedHitsAndReuse()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab"));
            try
            {
                var view = instance.GetComponent<EnemyView>();
                view.SetPresentationVariant(0);
                var renderer = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(part => part.enabled);
                var colors = renderer.sharedMaterials.Select(material => material.color).ToArray();
                var block = new MaterialPropertyBlock();
                block.SetFloat("_Smoothness", .71f); renderer.SetPropertyBlock(block);
                view.PlayHitFeedback(); view.TickCombatFeedback(.04f); view.PlayHitFeedback();
                view.TickCombatFeedback(.2f);
                renderer.GetPropertyBlock(block);
                Assert.That(block.HasColor("_BaseColor"), Is.False, "不能用皮肤颜色覆盖全部制服材质");
                Assert.That(block.GetFloat("_Smoothness"), Is.EqualTo(.71f), "已有非颜色覆盖必须保留");
                Assert.That(renderer.sharedMaterials.Select(material => material.color), Is.EqualTo(colors));
                view.PlayHitFeedback();
                // 显式调用正式池化禁用回调，编辑态不依赖MonoBehaviour自动调度。
                typeof(EnemyView).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
                view.BeginPresentationUse(); view.SetPresentationVariant(4);
                renderer.GetPropertyBlock(block);
                Assert.That(block.HasColor("_BaseColor"), Is.False, "受击中回池不能留下红色覆盖");
                var reused = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(part => part.enabled);
                reused.GetPropertyBlock(block);
                Assert.That(block.HasColor("_BaseColor"), Is.False, "未显示的变体也要恢复");
            }
            finally { Object.DestroyImmediate(instance); }
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
            return propertyBlock.HasColor("_Color") ? propertyBlock.GetColor("_Color") : targetRenderer.sharedMaterial.color;
        }
    }
}

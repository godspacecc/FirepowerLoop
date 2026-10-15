using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class CharacterPresentationTests
    {
        [Test]
        public void RifleReloadUsesUpperBodySequenceAndClearsOnCancelOrWeaponSwitch()
        {
            var holder = new GameObject("ReloadPresentationHolder");
            holder.SetActive(false);
            try
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), holder.transform);
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                var view = instance.GetComponent<PlayerView>();
                Assert.That(presenter.SetWeapon(presenter.Catalog.Find("arcade.weapon.rapid")), Is.True);
                holder.SetActive(true);
                var animator = presenter.BodyAnimator;
                animator.Rebind();
                animator.Update(0f);
                var layer = animator.GetLayerIndex("RifleReload");
                Assert.That(layer, Is.GreaterThanOrEqualTo(0), "缺少上半身换弹层");
                view.ShowActionProgress(1f/3.1f, null);
                animator.Update(0f);
                Assert.That(animator.GetLayerWeight(layer), Is.EqualTo(1f));
                Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName("Reload"), Is.True);
                view.ShowActionProgress(2.8f/3.1f, null);
                animator.Update(0f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(layer).IsName("PullBolt"), Is.True);
                view.HideActionProgress();
                Assert.That(animator.GetLayerWeight(layer), Is.Zero);
                view.ShowActionProgress(0f, null);
                presenter.SetWeapon(presenter.Catalog.Find("arcade.weapon.basic"));
                Assert.That(animator.GetLayerWeight(layer), Is.Zero);
                view.ShowActionProgress(0f, null);
                Assert.That(animator.GetLayerWeight(layer), Is.Zero, "手枪不可播放步枪动作");
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Test]
        public void SharedPlayerPistolKeepsBothHandsCenteredAndBarrelForward()
        {
            var holder = new GameObject("PistolPresentationHolder");
            holder.SetActive(false);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
                var instance = Object.Instantiate(prefab, holder.transform);
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                var view = instance.GetComponent<PlayerView>();
                var animator = presenter.BodyAnimator;
                var entry = presenter.Catalog.Find("arcade.weapon.basic");
                view.SetSharedWeaponAnimationActive(true);
                view.SetManualWeaponPoseSampling(true);
                presenter.SetManualPoseSampling(true);
                Assert.That(presenter.SetWeapon(entry), Is.True, presenter.LastError);
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                holder.SetActive(true);

                foreach (var yaw in new[] { 0f, 90f })
                {
                    instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    foreach (var definition in new[] { entry.Animations.Idle, entry.Animations.Attack })
                    {
                        var graph = PlayableGraph.Create("PistolPresentationRegression");
                        try
                        {
                            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                            var playable = AnimationClipPlayable.Create(graph, definition.Clip);
                            playable.SetApplyFootIK(false);
                            AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(playable);
                            graph.Play();
                            for (var frame = 0; frame <= 8; frame++)
                            {
                                playable.SetTime(definition.Clip.length * frame / 8f);
                                graph.Evaluate(0f);
                                presenter.ApplyOffhandGrip(definition);
                                var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                                var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                                var context = $"{definition.name}/yaw {yaw}/frame {frame}";
                                Assert.That(Mathf.Abs(instance.transform.InverseTransformPoint(right.position).x),
                                    Is.LessThan(0.12f), context + " main hand centered");
                                Assert.That(Mathf.Abs(instance.transform.InverseTransformPoint(left.position).x),
                                    Is.LessThan(0.15f), context + " offhand centered");
                                var barrel = Vector3.ProjectOnPlane(presenter.Visual.transform.forward, instance.transform.up);
                                Assert.That(Vector3.Angle(instance.transform.forward, barrel), Is.LessThan(12f),
                                    context + " horizontal barrel aim");
                                var grip = presenter.Visual.transform.TransformPoint(entry.OffhandGripLocalPosition);
                                Assert.That(Vector3.Distance(left.position, grip), Is.LessThan(0.025f),
                                    context + " offhand reaches grip");
                            }
                        }
                        finally
                        {
                            if (graph.IsValid()) graph.Destroy();
                        }
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void InvalidWeaponClearsThePreviousModelAndController()
        {
            var scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.Empty, "程序集隔离夹具应先创建无路径场景");
            var holder = new GameObject("CharacterPresentationTestHolder");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
                Assert.That(prefab, Is.Not.Null);
                var instance = Object.Instantiate(prefab, holder.transform);
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                var animator = instance.GetComponent<PlayerView>().Animator;
                var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(
                    "Assets/Game/Art/Characters/Presentation/Profiles/Player.asset");
                var valid = presenter.Catalog.Find("arcade.weapon.basic");

                Assert.That(presenter.SetWeapon(valid), Is.True);
                Assert.That(presenter.Visual, Is.Not.Null);
                var originMethod = typeof(CharacterWeaponPresenter).GetMethod("TryGetProjectileOrigin",
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.That(originMethod, Is.Not.Null);
                var originArgs = new object[] { Vector3.zero };
                Assert.That((bool)originMethod.Invoke(presenter, originArgs), Is.True);
                Assert.That(Vector3.Distance((Vector3)originArgs[0], presenter.Visual.transform.TransformPoint(
                    valid.ProjectileFirePointLocalPosition)), Is.LessThan(0.001f));
                Assert.That(presenter.SetWeapon(new CharacterWeaponCatalog.Entry()), Is.False);
                Assert.That(presenter.Current, Is.Null);
                Assert.That(presenter.Visual, Is.Null);
                originArgs[0] = Vector3.zero;
                Assert.That((bool)originMethod.Invoke(presenter, originArgs), Is.False);
                var unarmed = animator.runtimeAnimatorController as AnimatorOverrideController;
                Assert.That(unarmed, Is.Not.Null);
                Assert.That(OverrideFor(unarmed, profile.IdleSlot),
                    Is.SameAs(profile.UnarmedAnimations.Idle.Clip));
                var errorProperty = typeof(CharacterWeaponPresenter).GetProperty("LastError");
                Assert.That(errorProperty, Is.Not.Null);
                Assert.That(errorProperty.GetValue(presenter) as string, Is.Not.Empty);
                Assert.That(presenter.SetWeapon(valid), Is.True);
                Assert.That(presenter.Current, Is.SameAs(valid));
                Assert.That(errorProperty.GetValue(presenter) as string, Is.Null.Or.Empty);
                var malformed = (CharacterWeaponCatalog.Entry)typeof(object)
                    .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(valid, null);
                typeof(CharacterWeaponCatalog.Entry)
                    .GetField("handLocalScale", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(malformed, new Vector3(-1f, 1f, 1f));
                Assert.That(presenter.SetWeapon(malformed), Is.False);
                Assert.That(presenter.Current, Is.Null);
                Assert.That(presenter.Visual, Is.Null);
                Assert.That(errorProperty.GetValue(presenter) as string, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void DirectionChangeReusesOverrideAndSelectsTheMatchingClip()
        {
            var scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.Empty);
            var holder = new GameObject("CharacterPresentationDirectionHolder");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
                var instance = Object.Instantiate(prefab, holder.transform);
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                var animator = instance.GetComponent<PlayerView>().Animator;
                var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(
                    "Assets/Game/Art/Characters/Presentation/Profiles/Player.asset");
                var entry = presenter.Catalog.Find("arcade.weapon.rapid");
                Assert.That(presenter.SetWeapon(entry), Is.True);
                var controller = animator.runtimeAnimatorController as AnimatorOverrideController;
                Assert.That(controller, Is.Not.Null);
                var visual = presenter.Visual;
                var setDirection = typeof(CharacterWeaponPresenter).GetMethod("SetMovementDirection",
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.That(setDirection, Is.Not.Null);

                setDirection.Invoke(presenter, new object[] { instance.transform.right });
                Assert.That(OverrideFor(controller, profile.MoveSlot),
                    Is.SameAs(entry.Animations.MoveRight.Clip));
                setDirection.Invoke(presenter, new object[] { -instance.transform.right });
                Assert.That(OverrideFor(controller, profile.MoveSlot),
                    Is.SameAs(entry.Animations.MoveLeft.Clip));
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
                Assert.That(presenter.Visual, Is.SameAs(visual));
                Assert.That(presenter.SetWeapon(entry), Is.True);
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(controller));
                Assert.That(presenter.Visual, Is.SameAs(visual));
                Assert.That(presenter.SetWeapon(null), Is.True);
                var unarmedController = animator.runtimeAnimatorController as AnimatorOverrideController;
                Assert.That(unarmedController, Is.Not.Null);
                setDirection.Invoke(presenter, new object[] { instance.transform.right });
                Assert.That(OverrideFor(unarmedController, profile.MoveSlot),
                    Is.SameAs(profile.UnarmedAnimations.MoveRight.Clip));
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void UnarmedSprintUsesRunOnlyForForwardAndRestoresWalk()
        {
            var holder = new GameObject("SprintPresentationHolder");
            holder.SetActive(false);
            try
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"), holder.transform);
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                var view = instance.GetComponent<PlayerView>();
                var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(
                    "Assets/Game/Art/Characters/Presentation/Profiles/Player.asset");
                var setSprint = typeof(PlayerView).GetMethod("SetSprinting");
                Assert.That(setSprint, Is.Not.Null, "体力结算需要能驱动加速跑表现");
                var runProperty = typeof(CharacterAnimationProfile).GetProperty("UnarmedRunForward");
                Assert.That(runProperty, Is.Not.Null);
                var run = runProperty.GetValue(profile) as AnimationClip;
                Assert.That(run, Is.Not.Null);
                Assert.That(presenter.SetWeapon(null), Is.True);
                var controller = view.Animator.runtimeAnimatorController as AnimatorOverrideController;
                presenter.SetMovementDirection(instance.transform.forward);
                Assert.That(OverrideFor(controller, profile.MoveSlot), Is.SameAs(profile.UnarmedAnimations.MoveForward.Clip));
                setSprint.Invoke(view, new object[] { true });
                Assert.That(OverrideFor(controller, profile.MoveSlot), Is.SameAs(run));
                presenter.SetMovementDirection(-instance.transform.forward);
                Assert.That(OverrideFor(controller, profile.MoveSlot), Is.SameAs(profile.UnarmedAnimations.MoveBackward.Clip));
                presenter.SetMovementDirection(instance.transform.right);
                Assert.That(OverrideFor(controller, profile.MoveSlot), Is.SameAs(profile.UnarmedAnimations.MoveRight.Clip));
                presenter.SetMovementDirection(instance.transform.forward);
                setSprint.Invoke(view, new object[] { false });
                Assert.That(OverrideFor(controller, profile.MoveSlot), Is.SameAs(profile.UnarmedAnimations.MoveForward.Clip));
                setSprint.Invoke(view, new object[] { true });
                var weapon = presenter.Catalog.Find("arcade.weapon.rapid");
                Assert.That(presenter.SetWeapon(weapon), Is.True);
                presenter.SetMovementDirection(instance.transform.forward);
                controller = view.Animator.runtimeAnimatorController as AnimatorOverrideController;
                Assert.That(OverrideFor(controller, profile.MoveSlot), Is.SameAs(weapon.Animations.MoveForward.Clip));
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Test]
        public void EnemyWeaponValidationUsesExactTableIdentityAndBehavior()
        {
            const string enemyId = "enemy.graybox.basic";
            const string originalWeaponId = "weapon.short-sword";
            var accepted = RealTables();
            var newWeapon = (ArcadeWeaponConfig)typeof(object)
                .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(accepted.TbArcadeWeapon.Get(originalWeaponId), null);
            SetReadonly(newWeapon, "Id", "weapon.test.new-melee");
            accepted.TbArcadeWeapon.DataList.Add(newWeapon);
            accepted.TbArcadeWeapon.DataMap.Add(newWeapon.Id, newWeapon);
            SetReadonly(accepted.TbEnemy.Get(enemyId), "WeaponId", newWeapon.Id);
            var provider = new LubanRuntimeConfigProvider(() => accepted);
            Assert.That(provider.TryGetEnemy(enemyId, out _, out var error), Is.True, error);

            var unknown = RealTables();
            SetReadonly(unknown.TbEnemy.Get(enemyId), "WeaponId", "weapon.test.unknown");
            provider = new LubanRuntimeConfigProvider(() => unknown);
            Assert.That(provider.TryGetEnemy(enemyId, out _, out error), Is.False);
            Assert.That(error, Does.Contain("TbEnemy[" + enemyId + "]"));
            Assert.That(error, Does.Contain("TbArcadeWeapon"));

            var mismatch = RealTables();
            SetReadonly(mismatch.TbEnemy.Get(enemyId), "WeaponId", "weapon.bow");
            provider = new LubanRuntimeConfigProvider(() => mismatch);
            Assert.That(provider.TryGetEnemy(enemyId, out _, out error), Is.False);
            Assert.That(error, Does.Contain("AttackKind"));

            var unknownBehavior = RealTables();
            SetReadonly(unknownBehavior.TbArcadeWeapon.Get(originalWeaponId), "Behavior",
                (ArcadeWeaponBehavior)999);
            provider = new LubanRuntimeConfigProvider(() => unknownBehavior);
            Assert.That(provider.TryGetEnemy(enemyId, out _, out error), Is.False);
            Assert.That(error, Does.Contain("Behavior"));
        }

        [Test]
        public void HiddenBindingPreservesOriginalControllerWhenProfileFails()
        {
            var scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.Empty);
            var holder = new GameObject("HiddenPresentationControllerHolder");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);
            CharacterWeaponPresenter presenter = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
                Assert.That(prefab, Is.Not.Null);
                var instance = Object.Instantiate(prefab, holder.transform);
                presenter = instance.GetComponent<CharacterWeaponPresenter>();
                var animator = instance.GetComponent<PlayerView>().Animator;
                var original = animator.runtimeAnimatorController;
                var profile = presenter.Profile;
                var valid = presenter.Catalog.Find("arcade.weapon.basic");
                var another = presenter.Catalog.Find("weapon.bow");
                Assert.That(profile, Is.Not.Null);
                Assert.That(valid, Is.Not.Null);
                Assert.That(another, Is.Not.Null);

                SetPresenterField(presenter, "profile", null);
                Assert.That(presenter.SetWeapon(valid), Is.False);
                Assert.That(presenter.Current, Is.Null);
                Assert.That(presenter.Visual, Is.Null);
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(original));
                Assert.That(presenter.LastError, Is.Not.Empty);

                SetPresenterField(presenter, "profile", profile);
                Assert.That(presenter.SetWeapon(valid), Is.True, presenter.LastError);
                Assert.That(presenter.Current, Is.SameAs(valid));
                SetPresenterField(presenter, "profile", null);
                Assert.That(presenter.SetWeapon(another), Is.False);
                Assert.That(presenter.Current, Is.Null);
                Assert.That(presenter.Visual, Is.Null);
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(original));
                Assert.That(presenter.LastError, Is.Not.Empty);

                SetPresenterField(presenter, "profile", profile);
                Assert.That(presenter.SetWeapon(valid), Is.True, presenter.LastError);
                Assert.That(presenter.Current, Is.SameAs(valid));
            }
            finally
            {
                if (presenter != null)
                {
                    SetPresenterField(presenter, "profile", null);
                    presenter.SetWeapon(null);
                }
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void EnemyPresentationRejectsGameplayKindMismatch()
        {
            var scene = SceneManager.GetActiveScene();
            Assert.That(scene.path, Is.Empty);
            var holder = new GameObject("EnemyPresentationKindHolder");
            holder.SetActive(false);
            SceneManager.MoveGameObjectToScene(holder, scene);
            CharacterWeaponCatalog catalogCopy = null;
            CharacterWeaponPresenter presenter = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab");
                Assert.That(prefab, Is.Not.Null);
                var instance = Object.Instantiate(prefab, holder.transform);
                var view = instance.GetComponent<EnemyView>();
                presenter = instance.GetComponent<CharacterWeaponPresenter>();
                Assert.That(view, Is.Not.Null);
                Assert.That(presenter, Is.Not.Null);
                catalogCopy = Object.Instantiate(presenter.Catalog);
                SetPresenterField(presenter, "catalog", catalogCopy);
                Assert.That(presenter.Catalog, Is.SameAs(catalogCopy));

                var meleeWithBow = PresentationConfig("enemy.kind.melee", "weapon.bow",
                    global::EnemyAttackKind.Melee);
                Assert.That(EnemyCtrl.ValidatePresentationWeaponKind(view, meleeWithBow, out var error), Is.False);
                Assert.That(error, Does.Contain(meleeWithBow.Id).And.Contain(meleeWithBow.WeaponId));
                Assert.That(error, Does.Contain("近战").And.Contain("投射"));

                var projectileWithSword = PresentationConfig("enemy.kind.projectile", "weapon.short-sword",
                    global::EnemyAttackKind.Projectile);
                Assert.That(EnemyCtrl.ValidatePresentationWeaponKind(view, projectileWithSword, out error), Is.False);
                Assert.That(error, Does.Contain(projectileWithSword.Id).And.Contain(projectileWithSword.WeaponId));
                Assert.That(error, Does.Contain("投射").And.Contain("近战"));

                var meleeWithSword = PresentationConfig("enemy.kind.melee", "weapon.short-sword",
                    global::EnemyAttackKind.Melee);
                Assert.That(EnemyCtrl.ValidatePresentationWeaponKind(view, meleeWithSword, out error), Is.True, error);
                Assert.That(view.SetCombatWeapon(meleeWithSword.WeaponId), Is.True, presenter.LastError);
                Assert.That(presenter.Current.Id, Is.EqualTo(meleeWithSword.WeaponId));

                var projectileWithBow = PresentationConfig("enemy.kind.projectile", "weapon.bow",
                    global::EnemyAttackKind.Projectile);
                Assert.That(EnemyCtrl.ValidatePresentationWeaponKind(view, projectileWithBow, out error), Is.True, error);
                Assert.That(view.SetCombatWeapon(projectileWithBow.WeaponId), Is.True, presenter.LastError);
                Assert.That(presenter.Current.Id, Is.EqualTo(projectileWithBow.WeaponId));
            }
            finally
            {
                if (presenter != null)
                {
                    SetPresenterField(presenter, "profile", null);
                    presenter.SetWeapon(null);
                }
                Object.DestroyImmediate(holder);
                if (catalogCopy != null) Object.DestroyImmediate(catalogCopy);
            }
        }

        private static EnemyConfigDefinition PresentationConfig(string enemyId, string weaponId,
            global::EnemyAttackKind attackKind) => new EnemyConfigDefinition(
            enemyId, "test-only", 20, 1, 5f, 2f, 1f, 1f, string.Empty,
            attackKind, 0f, 0f, string.Empty, string.Empty, string.Empty, 0, 0f,
            weaponId: weaponId);

        private static void SetPresenterField(CharacterWeaponPresenter presenter, string name, object value)
        {
            var field = typeof(CharacterWeaponPresenter).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(presenter, value);
        }

        private static Tables RealTables() => new Tables(name =>
            new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes"))));

        private static void SetReadonly(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static AnimationClip OverrideFor(AnimatorOverrideController controller, AnimationClip slot)
        {
            var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            controller.GetOverrides(overrides);
            foreach (var pair in overrides)
                if (pair.Key == slot) return pair.Value;
            return null;
        }
    }
}

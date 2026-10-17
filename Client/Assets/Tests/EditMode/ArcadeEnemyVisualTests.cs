using System;
using System.IO;
using System.Linq;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Tests
{
    /// <summary>验证街机专用敌人配置与正式 Prefab 的视觉隔离、组件契约和资源依赖。</summary>
    public sealed class ArcadeEnemyVisualTests
    {
        private static readonly string[] EnemyIds =
        {
            "enemy.arcade.basic",
            "enemy.arcade.archer",
            "enemy.arcade.brute",
            "enemy.arcade.boss.archer"
        };

        private static readonly string[] PrefabNames =
        {
            "ArcadeEnemyBasic",
            "ArcadeEnemyArcher",
            "ArcadeEnemyBrute",
            "ArcadeEnemyBossArcher"
        };

        [Test]
        public void GeneratedEnemyWeaponIdsResolveToTheSharedCatalog()
        {
            var provider = new LubanRuntimeConfigProvider(LoadTables);
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            for (var index = 0; index < EnemyIds.Length; index++)
            {
                Assert.That(provider.TryGetEnemy(EnemyIds[index], out var enemy, out var error),
                    Is.True, error);
                Assert.That(catalog.Find(enemy.WeaponId), Is.Not.Null, EnemyIds[index]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{PrefabNames[index]}.prefab");
                var bound = prefab.GetComponent<CharacterWeaponPresenter>()?.Catalog?.Find(enemy.WeaponId);
                Assert.That(bound, Is.Not.Null, PrefabNames[index]);
                Assert.That(bound.VisualSource, Is.SameAs(catalog.Find(enemy.WeaponId).VisualSource));
                Assert.That(bound.Animations.Attack, Is.SameAs(catalog.Find(enemy.WeaponId).Animations.Attack));
            }
        }

        [Test]
        public void AllSharedWeaponsHaveUsableWorldDefinitions()
        {
            var provider = new LubanRuntimeConfigProvider(LoadTables);
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Count, Is.GreaterThan(0));
            Assert.That(catalog.ValidateIdentities(out var identityError), Is.True, identityError);
            for (var index = 0; index < catalog.Count; index++)
            {
                var entry = catalog.Get(index);
                Assert.That(entry.WorldItemId, Is.GreaterThan(0), entry.Id);
                Assert.That(catalog.FindByWorldItemId(entry.WorldItemId), Is.SameAs(entry));
                Assert.That(provider.TryGetItem(entry.WorldItemId, out var item, out var itemError),
                    Is.True, $"{entry.Id}: {itemError}");
                Assert.That(entry.HasVisual, Is.True, entry.Id);
                // 共用目录也收录手持照明工具；工具不应被误当成有伤害的武器配置。
                if (item.ItemType == global::ItemType.Tool) continue;
                Assert.That(item.ItemType, Is.EqualTo(global::ItemType.Weapon), entry.Id);
                Assert.That(provider.TryGetWeapon(entry.WorldItemId, out var weapon, out var error),
                    Is.True, $"{entry.Id}: {error}");
                Assert.That(provider.TryGetPlayerWeaponAppearance(entry.WorldItemId,
                    out var appearance, out error), Is.True, $"{entry.Id}: {error}");
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/" + appearance.PrefabAddress + ".prefab"), Is.Not.Null, entry.Id);
                if (weapon.WeaponKind != global::WeaponKind.Projectile) continue;
                Assert.That(provider.TryGetItem(weapon.AmmoItemId, out _, out error),
                    Is.True, $"{entry.Id}: {error}");
                Assert.That(provider.TryGetProjectile(weapon.ProjectileConfigId,
                    out var projectile, out error), Is.True, $"{entry.Id}: {error}");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/" + projectile.PrefabAddress + ".prefab");
                Assert.That(prefab, Is.Not.Null, entry.Id);
                Assert.That(prefab.GetComponent<ProjectileView>().Validate(out error),
                    Is.True, $"{entry.Id}: {error}");
            }
        }

        [Test]
        public void ArcadeGunCatalogResolvesSharedVisualsAndTemporaryDrops()
        {
            var provider = new LubanRuntimeConfigProvider(LoadTables);
            var shared = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            Assert.That(provider.TryGetArcadeCatalog(out var arcade, out var error), Is.True, error);
            foreach (var entry in arcade.Weapons.Where(weapon => weapon.Id.StartsWith("arcade.weapon.")))
            {
                Assert.That(shared.Find(entry.Id), Is.Not.Null, entry.Id);
                Assert.That(entry.Id == arcade.Rule.BaseWeaponId || arcade.Drops.Any(item =>
                    item.ProductId == entry.Id), Is.True, entry.Id);
            }
        }

        [Test]
        public void GeneratedCatalog_UsesOnlyArcadeSpecificEnemyIds()
        {
            var provider = new LubanRuntimeConfigProvider(LoadTables);

            Assert.That(provider.TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition catalog,
                out string error), Is.True, error);
            Assert.That(catalog.EnemyGroups.SelectMany(group => group.Members)
                .Select(member => member.EnemyId), Has.All.StartsWith("enemy.arcade."));
            for (var index = 0; index < EnemyIds.Length; index++)
            {
                Assert.That(provider.TryGetEnemy(EnemyIds[index], out EnemyConfigDefinition enemy,
                    out error), Is.True, error);
                Assert.That(enemy.PrefabAddress,
                    Is.EqualTo($"Prefabs/Characters/{PrefabNames[index]}"));
            }
        }

        [Test]
        public void ArcadeEnemyPrefabs_PreserveRuntimeContractWithoutIgnoredDependencies()
        {
            foreach (string prefabName in PrefabNames)
            {
                string path = $"Assets/Game/Prefabs/Characters/{prefabName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                Assert.That(prefab, Is.Not.Null, path);
                Assert.That(prefab.GetComponent<EnemyView>(), Is.Not.Null, prefabName);
                Assert.That(prefab.GetComponent<NavMeshAgent>(), Is.Not.Null, prefabName);
                Assert.That(prefab.GetComponent<Collider>(), Is.Not.Null, prefabName);
                Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one),
                    $"{prefabName} 根节点必须保持等比缩放，避免旋转模型和世界空间 HUD 时产生拉伸");
                var combatCollider = prefab.GetComponent<BoxCollider>();
                Assert.That(combatCollider, Is.Not.Null, prefabName);
                Assert.That(combatCollider.center, Is.EqualTo(new Vector3(0f, 1f, 0f)), prefabName);
                Assert.That(combatCollider.size, Is.EqualTo(new Vector3(1f, 2f, 1f)), prefabName);
                Assert.That(prefab.GetComponent<MeshRenderer>().enabled, Is.False, prefabName);
                Transform visual = prefab.transform.Find("ArcadeVisual");
                Assert.That(visual, Is.Not.Null, prefabName);
                Assert.That(visual.lossyScale.x, Is.EqualTo(visual.lossyScale.y).Within(0.0001f),
                    $"{prefabName} 模型容器必须保持等比缩放");
                Transform healthCanvas = prefab.transform.Find("EnemyHealthBarRoot/Canvas");
                Assert.That(healthCanvas, Is.Not.Null, prefabName);
                Assert.That(((RectTransform)healthCanvas).sizeDelta, Is.EqualTo(new Vector2(76f, 8f)),
                    $"{prefabName} 血条应弱于角色本体轮廓");
                Assert.That(healthCanvas.lossyScale.x,
                    Is.EqualTo(healthCanvas.lossyScale.y).Within(0.0001f),
                    $"{prefabName} 世界血条必须保持等比缩放");
                Assert.That(visual.GetComponentInChildren<Animator>(true), Is.Not.Null, prefabName);
                Assert.That(visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Count(renderer => renderer.enabled), Is.EqualTo(1), prefabName);
                string weaponName = prefabName == "ArcadeEnemyArcher" || prefabName == "ArcadeEnemyBossArcher"
                    ? "ArcherBow"
                    : "Weapon";
                Assert.That(visual.GetComponentsInChildren<MeshRenderer>(true)
                    .Count(renderer => renderer.enabled && renderer.name == weaponName), Is.EqualTo(1),
                    prefabName);
                Assert.That(visual.GetComponentsInChildren<Collider>(true), Is.Empty, prefabName);
                Assert.That(AssetDatabase.GetDependencies(path, true).Any(dependency =>
                    dependency.StartsWith("Assets/Art/", StringComparison.Ordinal)), Is.False, prefabName);
            }
        }

        [Test]
        public void BasicEnemy_HumanoidAnimatorUsesInPlaceFormalAssets()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab");
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            var controller = animator.runtimeAnimatorController as AnimatorController;

            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid && animator.avatar.isHuman, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.parameters.Select(parameter => parameter.name),
                Is.EquivalentTo(new[] { "MoveSpeed", "Attack", "Hit", "Dead" }));
            Assert.That(controller.layers[0].stateMachine.states.Select(state => state.state.name),
                Is.EquivalentTo(new[] { "Idle", "Run", "Attack", "Hit", "Death" }));
            Assert.That(AssetDatabase.GetDependencies(
                    "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab", true)
                .Any(dependency => dependency.StartsWith("Assets/Art/", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void ArcherEnemy_HumanoidAnimatorUsesTwoHandedAttackAndLeftHandBow()
        {
            const string path = "Assets/Game/Prefabs/Characters/ArcadeEnemyArcher.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            var controller = animator.runtimeAnimatorController as AnimatorController;
            MeshRenderer bow = prefab.GetComponentsInChildren<MeshRenderer>(true)
                .Single(renderer => renderer.name == "ArcherBow");

            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid && animator.avatar.isHuman, Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.layers[0].stateMachine.states.Select(state => state.state.name),
                Is.EquivalentTo(new[] { "Idle", "Run", "Attack", "Hit", "Death" }));
            AnimatorState attack = controller.layers[0].stateMachine.states
                .Select(state => state.state).Single(state => state.name == "Attack");
            Assert.That(attack.motion.name, Does.Contain("Attack2H01"));
            Assert.That(bow.transform.parent, Is.EqualTo(animator.GetBoneTransform(HumanBodyBones.LeftHand)));
            Assert.That(AssetDatabase.GetDependencies(path, true).Any(dependency =>
                dependency.StartsWith("Assets/Art/", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void ArcadeEnemyRuntime_ResetClearsAttackPresentationSequence()
        {
            var runtime = new ArcadeEnemyRuntime { AttackSequence = 7, IsBossPresentation = true };

            runtime.Reset();

            Assert.That(runtime.AttackSequence, Is.Zero);
            Assert.That(runtime.IsBossPresentation, Is.False);
        }

        [Test]
        public void ArcadeEnemyPrefabs_ExposeSixDeterministicBodyVariants()
        {
            foreach (string prefabName in PrefabNames)
            {
                string path = $"Assets/Game/Prefabs/Characters/{prefabName}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var serialized = new SerializedObject(prefab.GetComponent<EnemyView>());
                SerializedProperty variants = serialized.FindProperty("presentationVariants");

                Assert.That(variants, Is.Not.Null, prefabName);
                Assert.That(variants.arraySize, Is.EqualTo(6), prefabName);
                Assert.That(Enumerable.Range(0, variants.arraySize)
                    .Select(index => variants.GetArrayElementAtIndex(index).objectReferenceValue)
                    .Distinct().Count(), Is.EqualTo(6), prefabName);
            }

            Assert.That(Enumerable.Range(1, 6)
                .Select(runtimeId => EnemyView.SelectPresentationVariantIndex(runtimeId, 6))
                .Distinct().Count(), Is.EqualTo(6));
            Assert.That(EnemyView.SelectPresentationVariantIndex(7, 6),
                Is.EqualTo(EnemyView.SelectPresentationVariantIndex(1, 6)));
        }

        [Test]
        public void ArcadeHealthBarRule_HidesNormalFullHealthAndExpiresAfterHitWindow()
        {
            Assert.That(EnemyCtrl.ShouldShowArcadeHealthBar(false, 0f), Is.False);
            Assert.That(EnemyCtrl.ShouldShowArcadeHealthBar(false, 0.01f), Is.True);
            Assert.That(EnemyCtrl.ShouldShowArcadeHealthBar(true, 0f), Is.True);
            Assert.That(EnemyCtrl.AdvanceArcadeHealthBarReveal(
                EnemyCtrl.ArcadeHealthBarRevealSeconds, 1f), Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(EnemyCtrl.AdvanceArcadeHealthBarReveal(1f, 2f), Is.Zero);
        }

        [Test]
        public void GrayboxPlayer_ArcadeMarkerReplacesRedundantWorldHealthBar()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Transform marker = prefab.transform.Find("ArcadePlayerMarker");

            Assert.That(marker, Is.Not.Null);
            Assert.That(marker.gameObject.activeSelf, Is.False);
            var markerRenderer = marker.GetComponent<SpriteRenderer>();
            Assert.That(markerRenderer, Is.Not.Null);
            Assert.That(markerRenderer.sprite, Is.Not.Null);
            Assert.That(markerRenderer.sharedMaterial, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(markerRenderer.sharedMaterial),
                Is.EqualTo("Assets/Game/Materials/ArcadePlayerMarker.mat"));
            Assert.That(markerRenderer.sharedMaterial.shader.name,
                Is.EqualTo("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
            Assert.That(markerRenderer.bounds.size.x, Is.EqualTo(1.75f).Within(0.02f));

            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<PlayerView>();
                view.SetAlivePresentation(true);
                view.SetArcadePresentationActive(true);
                Assert.That(instance.transform.Find("HealthBarRoot"), Is.Null);
                Assert.That(instance.transform.Find("ArcadePlayerMarker").gameObject.activeSelf, Is.True);

                view.SetArcadePresentationActive(false);
                Assert.That(instance.transform.Find("HealthBarRoot"), Is.Null);
                Assert.That(instance.transform.Find("ArcadePlayerMarker").gameObject.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BossVisuals_AreLargerAndUseDedicatedMaterial()
        {
            Transform basic = LoadVisual("ArcadeEnemyBasic");
            Transform archer = LoadVisual("ArcadeEnemyArcher");
            Transform brute = LoadVisual("ArcadeEnemyBrute");
            Transform bossArcher = LoadVisual("ArcadeEnemyBossArcher");

            Assert.That(brute.localScale.x, Is.GreaterThan(basic.localScale.x));
            Assert.That(bossArcher.localScale.x, Is.GreaterThan(archer.localScale.x));
            Assert.That(brute.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(renderer => renderer.enabled).sharedMaterial.name,
                Is.EqualTo("ArcadeEnemyBoss"));
            Assert.That(bossArcher.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(renderer => renderer.enabled).sharedMaterial.name,
                Is.EqualTo("ArcadeEnemyBoss"));
        }

        [Test]
        public void LightEnemies_UseRetargetableInfectedSoldierWithCompleteBodyVariants()
        {
            foreach (string name in new[] { "ArcadeEnemyBasic", "ArcadeEnemyArcher" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab");
                var animator = prefab.GetComponentInChildren<Animator>(true);
                Assert.That(AssetDatabase.GetAssetPath(animator.avatar),
                    Is.EqualTo("Assets/Game/Art/Enemies/InfectedSoldier/Source/SoldierA.fbx"), name);
                Assert.That(animator.avatar.isHuman && animator.avatar.isValid, Is.True, name);
                var presenter = prefab.GetComponent<CharacterWeaponPresenter>();
                Assert.That(presenter.BodyAnimator, Is.EqualTo(animator), name);
                var variants = prefab.GetComponent<EnemyView>().PresentationVariantCount;
                Assert.That(variants, Is.EqualTo(6), name);
                foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Assert.That(renderer.sharedMesh.subMeshCount, Is.EqualTo(4), renderer.name);
                    Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(4), renderer.name);
                    Assert.That(renderer.sharedMesh.boneWeights.Length,
                        Is.EqualTo(renderer.sharedMesh.vertexCount), renderer.name);
                    Assert.That(renderer.bones.All(bone => bone != null), Is.True, renderer.name);
                    Assert.That(renderer.sharedMaterials.All(material => material != null &&
                        material.shader.name == "Universal Render Pipeline/Lit"), Is.True, renderer.name);
                    Assert.That(renderer.sharedMesh.bounds.size.y, Is.InRange(1.6f, 2.1f), renderer.name);
                }
                Assert.That(prefab.GetComponentsInChildren<Transform>(true)
                    .Any(part => part.name == "MG42" || part.name == "PanzerSchreck"), Is.False, name);
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var view = instance.GetComponent<EnemyView>();
                    Assert.That(view.SetCombatWeapon(name == "ArcadeEnemyBasic" ? "weapon.short-sword" : "weapon.bow"),
                        Is.True, instance.GetComponent<CharacterWeaponPresenter>().LastError);
                    var bodyAnimator = view.Animator;
                    foreach (var state in new[] { "Idle", "Run", "Attack", "Hit", "Death" })
                    {
                        bodyAnimator.Rebind(); bodyAnimator.Update(0f);
                        bodyAnimator.Play(state, 0, .35f); bodyAnimator.Update(.02f);
                        instance.GetComponent<CharacterWeaponPresenter>().ApplyOffhandGrip();
                        var weapon = instance.GetComponent<CharacterWeaponPresenter>().Visual;
                        Assert.That(weapon.GetComponentsInChildren<Renderer>().All(part =>
                            part.bounds.size.magnitude < 3f), Is.True, name + " " + state + " 武器不能继承原骨架放大");
                    }
                    for (int variant = 0; variant < 12; variant++)
                    {
                        view.SetPresentationVariant(variant);
                        Assert.That(instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).Count(part => part.enabled),
                            Is.EqualTo(1), name);
                    }
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
            }
        }

        [Test]
        public void PooledEnemyPreparesWhileInactiveWithoutEvaluatingAnimator()
        {
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab"));
            var messages = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log && message.IndexOf("Animator", StringComparison.OrdinalIgnoreCase) >= 0)
                    messages.Add(message);
            };
            Application.logMessageReceived += capture;
            try
            {
                var view = instance.GetComponent<EnemyView>();
                view.Animator.Rebind(); view.Animator.Update(.01f); view.Animator.SetBool("Dead", true);
                instance.SetActive(false);
                view.BeginPresentationUse();
                Assert.That(view.SetCombatWeapon("weapon.short-sword"), Is.True);
                view.SetNavigationEnabled(false);
                instance.SetActive(true);
                typeof(EnemyView).GetMethod("OnEnable", System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
                Assert.That(view.Animator.GetBool("Dead"), Is.False, "复用激活后必须回到存活状态");
                Assert.That(view.Animator.GetFloat("MoveSpeed"), Is.Zero);
                Assert.That(messages, Is.Empty, "不可见准备阶段不得求值Animator或访问未初始化的层");
            }
            finally { Application.logMessageReceived -= capture; UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static Transform LoadVisual(string prefabName)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/Game/Prefabs/Characters/{prefabName}.prefab");
            return prefab != null ? prefab.transform.Find("ArcadeVisual") : null;
        }

        private static global::Tables LoadTables() => new(name => new ByteBuf(
            File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes"))));
    }
}

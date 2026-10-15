using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class AppearanceGallerySceneTests
    {
        [Test]
        public void SharedAnimationOptionsKeepChineseDisplayNames()
        {
            const string folder = "Assets/Game/Art/Characters/Presentation/Definitions";
            var paths = Directory.GetFiles(folder, "*.asset");
            Assert.That(paths, Is.Not.Empty);
            foreach (var path in paths)
            {
                var definition = AssetDatabase.LoadAssetAtPath<CharacterAnimationDefinition>(
                    path.Replace('\\', '/'));
                Assert.That(definition, Is.Not.Null, path);
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(
                    definition.DisplayName ?? string.Empty, "[\\u4e00-\\u9fff]"),
                    Is.True, path + " 缺少中文显示名");
            }
        }

        [Test]
        public void FormalRifleAimClipHasNoOrphanMoveEvent()
        {
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                "Assets/Game/Art/Player/Humanoid/PlayerHumanoid.controller");
            var clip = AssetDatabase.LoadAllAssetsAtPath(
                    "Assets/Game/Art/Player/Animations/Mixamo/Imported/RifleIdleAiming.fbx")
                .OfType<AnimationClip>().Single(candidate => candidate.name == "RifleIdleAiming");
            Assert.That(controller, Is.Not.Null);
            Assert.That(clip, Is.Not.Null);
            Assert.That(controller.animationClips, Does.Contain(clip), "正式玩家 Animator 应继续使用同一动画");
            foreach (var animationEvent in AnimationUtility.GetAnimationEvents(clip))
                Assert.That(animationEvent.functionName, Is.Not.EqualTo("move"),
                    "玩家移动由正式移动系统驱动，不应残留无接收者的动画事件");
        }

        [Test]
        public void PiercingWeaponStaysNearBothHandsInRiflePreviewClips()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var player = instance.GetComponent<PlayerView>();
                PlayerViewCtrl.ConfigureArcadeWeaponPresentation(player, "ArcadePiercingGun", false, Vector3.zero);
                var animator = player.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var weaponMesh = FindChild(instance, "ArcadeK98").GetComponent<MeshFilter>();
                Assert.That(weaponMesh, Is.Not.Null);

                foreach (var name in new[] { "RifleIdleAiming", "RifleFire" })
                {
                    var path = $"Assets/Game/Art/Player/Animations/Mixamo/Imported/{name}.fbx";
                    var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                        .Single(candidate => candidate.name == name);
                    var graph = PlayableGraph.Create("PiercingWeaponGripTest");
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var playable = AnimationClipPlayable.Create(graph, clip);
                        AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(playable);
                        graph.Play();
                        playable.SetTime(clip.length * 0.5f);
                        graph.Evaluate(0f);

                        float NearestMeshDistance(Vector3 handPosition) => weaponMesh.sharedMesh.vertices
                            .Min(vertex => Vector3.Distance(weaponMesh.transform.TransformPoint(vertex), handPosition));
                        Assert.That(NearestMeshDistance(leftHand.position), Is.LessThan(0.06f),
                            $"{name} 左手应靠近武器前握位");
                        Assert.That(NearestMeshDistance(rightHand.position), Is.LessThan(0.04f),
                            $"{name} 右手应靠近扳机握位");
                    }
                    finally
                    {
                        if (graph.IsValid()) graph.Destroy();
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SharedWeaponGripKeepsTheOffhandOnCompatibleWeapons()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var player = instance.GetComponent<PlayerView>();
                var animator = player.Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                var bindings = new SerializedObject(player).FindProperty("arcadeWeaponVisuals");
                foreach (var pair in new[]
                         {
                             (weapon: "ArcadePistol", clips: new[] { "PistolIdle", "PistolFire" }),
                             (weapon: "ArcadeRifle", clips: new[] { "RifleIdleAiming", "RifleFire", "RifleWalkForward", "RifleWalkBackward", "RifleWalkLeft", "RifleWalkRight" }),
                             (weapon: "ArcadePiercingGun", clips: new[] { "RifleIdleAiming", "RifleFire", "RifleWalkForward", "RifleWalkBackward", "RifleWalkLeft", "RifleWalkRight" }),
                             (weapon: "ArcadeScifiShotgun", clips: new[] { "RifleIdleAiming", "RifleFire", "RifleWalkForward", "RifleWalkBackward", "RifleWalkLeft", "RifleWalkRight" }),
                             (weapon: "ArcadeScifiPlasmaRifle", clips: new[] { "RifleIdleAiming", "RifleFire", "RifleWalkForward", "RifleWalkBackward", "RifleWalkLeft", "RifleWalkRight" })
                         })
                {
                    PlayerViewCtrl.ConfigureArcadeWeaponPresentation(player, pair.weapon, false, Vector3.zero);
                    GameObject weaponRoot = null;
                    Vector3 grip = default;
                    for (var index = 0; index < player.ArcadeWeaponVisualCount; index++)
                    {
                        if (player.GetArcadeWeaponVisualName(index) != pair.weapon) continue;
                        weaponRoot = player.GetArcadeWeaponVisualRoot(index);
                        var binding = bindings.GetArrayElementAtIndex(index);
                        Assert.That(binding.FindPropertyRelative("hasLeftHandGrip").boolValue, Is.True);
                        grip = binding.FindPropertyRelative("leftHandGripLocalPosition").vector3Value;
                        break;
                    }
                    Assert.That(weaponRoot, Is.Not.Null, pair.weapon);
                    foreach (var name in pair.clips)
                    {
                        var path = $"Assets/Game/Art/Player/Animations/Mixamo/Imported/{name}.fbx";
                        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                            .Single(candidate => candidate.name == name);
                        var graph = PlayableGraph.Create("SharedOffhandGripTest");
                        try
                        {
                            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                            var playable = AnimationClipPlayable.Create(graph, clip);
                            AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(playable);
                            graph.Play();
                            for (var frame = 0; frame <= 4; frame++)
                            {
                                playable.SetTime(clip.length * frame / 4f);
                                graph.Evaluate(0f);
                                Assert.That(PlayerViewCtrl.AllowsOffhandGrip(pair.weapon, name), Is.True);
                                player.ApplyWeaponGripPose();
                                var tolerance = pair.weapon == "ArcadePistol" ? 0.07f : 0.025f;
                                Assert.That(Vector3.Distance(leftHand.position,
                                        weaponRoot.transform.TransformPoint(grip)), Is.LessThan(tolerance),
                                    $"{pair.weapon} / {name} / 采样点 {frame} 副手脱离握点");
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
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FormalBowUsesTheGalleryIdleWalkAndFireClips()
        {
            var bowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/Weapons/GrayboxBow.prefab");
            Assert.That(bowPrefab, Is.Not.Null);
            Assert.That(bowPrefab.transform.Find("Visual").localPosition, Is.EqualTo(Vector3.zero),
                "弓的握持中心应与左手挂点重合，正式游戏和造型库不能有额外旧偏移");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var player = instance.GetComponent<PlayerView>();
                var animator = player.Animator;
                var baseController = animator.runtimeAnimatorController;
                player.SetWeaponPresentation(true, false, true);
                var bowController = animator.runtimeAnimatorController as AnimatorOverrideController;
                Assert.That(bowController, Is.Not.Null);
                Assert.That(bowController.runtimeAnimatorController, Is.SameAs(baseController));
                Assert.That(bowController["HumanM@Idle01"].name, Is.EqualTo("StandingAimIdle01"));
                Assert.That(bowController["HumanM@Attack1H01_R"].name, Is.EqualTo("ShootingArrow"));
                foreach (var direction in new[]
                         {
                             (Vector3.forward, "StandingAimWalkForward"),
                             (Vector3.back, "StandingAimWalkBack"),
                             (Vector3.left, "StandingAimWalkLeft"),
                             (Vector3.right, "BowAimWalkRightComposite")
                         })
                {
                    player.SetMovementDirection(direction.Item1);
                    Assert.That(bowController["HumanM@Run01_Forward"].name,
                        Is.EqualTo(direction.Item2), direction.Item2);
                }
                var composite = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Game/Art/Player/Animations/Mixamo/Imported/BowAimWalkRightComposite.anim");
                Assert.That(composite.humanMotion, Is.True);
                var rightWalk = AssetDatabase.LoadAllAssetsAtPath(
                        "Assets/Game/Art/Player/Animations/Mixamo/Imported/Right Strafe Walk.fbx")
                    .OfType<AnimationClip>().Single(clip => clip.name == "BowRightStrafeWalk");
                var legBinding = AnimationUtility.GetCurveBindings(composite)
                    .Single(binding => binding.propertyName == "Left Upper Leg Front-Back");
                Assert.That(AnimationUtility.GetEditorCurve(composite, legBinding).Evaluate(0.3f),
                    Is.EqualTo(AnimationUtility.GetEditorCurve(rightWalk, legBinding).Evaluate(0.3f))
                        .Within(0.0001f), "组合动画的侧移步法应来自新下载资源");
                Assert.That(animator.GetBool("RangedWeaponActive"), Is.False,
                    "举弓时不能再叠加步枪上半身姿态");

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Update(0.1f);
                player.PlayBowFireFeedback();
                animator.Update(0.05f);
                animator.Update(0.05f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True,
                    "正式射箭反馈应进入使用 ShootingArrow 的攻击状态");

                player.SetWeaponPresentation(false, false, false);
                Assert.That(animator.runtimeAnimatorController, Is.SameAs(baseController));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FormalBowFirePortTriggersTheArrowAnimation()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            PlayerViewCtrl controller = null;
            try
            {
                var view = instance.GetComponent<PlayerView>();
                controller = new PlayerViewCtrl(view, 1, 1, new ViewCtrlRegistry(),
                    new UnityViewInstanceProvider());
                controller.SetWeaponPresentation(true, false, true);
                view.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.Animator.Update(0.1f);
                controller.PlayBowFireFeedback();
                view.Animator.Update(0.05f);
                view.Animator.Update(0.05f);
                Assert.That(view.Animator.GetCurrentAnimatorStateInfo(0).IsName("Attack"), Is.True);
            }
            finally
            {
                controller?.Release();
                if (instance != null) Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void GallerySceneReferencesFormalCharactersAndAllControls()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            try
            {
                AppearanceGallerySceneView view = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((view = root.GetComponentInChildren<AppearanceGallerySceneView>(true)) != null) break;
                Assert.That(view, Is.Not.Null);

                var data = new SerializedObject(view);
                var prefabs = data.FindProperty("characterPrefabs");
                Assert.That(prefabs.arraySize, Is.EqualTo(6));
                for (int i = 0; i < 5; i++)
                {
                    var prefab = prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.That(prefab, Is.Not.Null, $"角色 {i} 缺少正式 Prefab");
                    Assert.That(AssetDatabase.GetAssetPath(prefab), Does.StartWith("Assets/Game/Prefabs/Characters/"));
                }
                Assert.That(AssetDatabase.GetAssetPath(prefabs.GetArrayElementAtIndex(5).objectReferenceValue),
                    Is.EqualTo("Assets/Game/Art/Characters/Presentation/Samples/PolygonCityBusinessManSample.prefab"));

                var previews = data.FindProperty("playerPreviewAnimations");
                Assert.That(previews.arraySize, Is.EqualTo(16));
                for (var i = 0; i < previews.arraySize; i++)
                {
                    var clip = previews.GetArrayElementAtIndex(i).objectReferenceValue;
                    Assert.That(clip, Is.Not.Null, $"预览动画 {i} 缺少引用");
                    Assert.That(AssetDatabase.GetAssetPath(clip),
                        Does.StartWith("Assets/Game/Art/Player/Animations/Mixamo/Imported/"));
                }
                CollectionAssert.AreEquivalent(
                    new[]
                    {
                        "RifleIdleAiming", "RifleFire", "RifleReload", "RifleWalkForward",
                        "RifleWalkBackward", "RifleWalkLeft", "RifleWalkRight", "PistolIdle", "PistolFire",
                        "StandingAimIdle01", "StandingDrawArrow", "ShootingArrow", "StandingAimWalkForward",
                        "StandingAimWalkBack", "StandingAimWalkLeft", "BowAimWalkRightComposite"
                    },
                    Enumerable.Range(0, previews.arraySize)
                        .Select(index => previews.GetArrayElementAtIndex(index).objectReferenceValue.name));

                foreach (var field in new[]
                         {
                             "bowAppearancePrefab", "previewRoot", "previewStage", "previewCamera", "characterDropdown", "variantDropdown",
                             "equipmentDropdown", "animationDropdown", "timeline", "timelineText", "equipmentHintText",
                             "playPauseText", "cameraModeText", "playPauseButton", "cameraModeButton",
                             "resetCameraButton", "exitButton"
                         })
                    Assert.That(data.FindProperty(field).objectReferenceValue, Is.Not.Null, field);

                var camera = view.GetComponentInChildren<Camera>(true);
                Assert.That(camera.cullingMask, Is.EqualTo(1 << 31));
                Assert.That(camera.rect.xMin, Is.Zero);
                var controls = (RectTransform)view.GetComponentInChildren<Canvas>(true).transform.Find("Controls");
                Assert.That(camera.rect.xMax, Is.EqualTo(controls.anchorMin.x).Within(0.001f));
                var previewRoot = (Transform)data.FindProperty("previewRoot").objectReferenceValue;
                var stage = view.transform.Find("PreviewStage").GetComponent<Renderer>();
                Assert.That(previewRoot.position.y, Is.EqualTo(0f).Within(0.001f));
                Assert.That(data.FindProperty("previewStage").objectReferenceValue, Is.SameAs(stage));
                var dropdowns = new[] { "characterDropdown", "variantDropdown", "equipmentDropdown", "animationDropdown" }
                    .Select(field => (Dropdown)data.FindProperty(field).objectReferenceValue).ToArray();
                Assert.That(dropdowns.Distinct().Count(), Is.EqualTo(4));
                foreach (var dropdown in dropdowns)
                {
                    var item = dropdown.itemText.GetComponentInParent<Toggle>(true);
                    Assert.That(((RectTransform)item.transform).rect.height,
                        Is.GreaterThanOrEqualTo(dropdown.itemText.fontSize + 16f), dropdown.name);
                    Assert.That(dropdown.itemText.rectTransform.rect.height,
                        Is.GreaterThanOrEqualTo(dropdown.itemText.fontSize + 8f), dropdown.name);
                }
                Assert.That(view.GetComponentInChildren<Canvas>(true).sortingOrder, Is.GreaterThan(100));
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void GalleryAlignsOnlyTheSelectedAnimatedCharacter()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            try
            {
                AppearanceGallerySceneView view = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((view = root.GetComponentInChildren<AppearanceGallerySceneView>(true)) != null) break;
                Assert.That(view, Is.Not.Null);
                var data = new SerializedObject(view);
                var prefabs = data.FindProperty("characterPrefabs");
                var stage = (Renderer)data.FindProperty("previewStage").objectReferenceValue;
                var basic = (GameObject)prefabs.GetArrayElementAtIndex(1).objectReferenceValue;
                var brute = (GameObject)prefabs.GetArrayElementAtIndex(2).objectReferenceValue;

                var basicFootY = SampleAndAlign(basic, scene, stage);
                var bruteFootY = SampleAndAlign(brute, scene, stage);
                var stageTop = stage.bounds.max.y + 0.01f;
                Assert.That(basicFootY, Is.EqualTo(stageTop).Within(0.001f));
                Assert.That(bruteFootY, Is.EqualTo(stageTop).Within(0.001f));
                Assert.That(stage.transform.position.y, Is.EqualTo(-0.12f).Within(0.001f));
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void GalleryMeleeOptionsUseFormalPreviewAssets()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            try
            {
                AppearanceGallerySceneView view = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((view = root.GetComponentInChildren<AppearanceGallerySceneView>(true)) != null) break;
                Assert.That(view, Is.Not.Null);
                var catalog = new SerializedObject(view).FindProperty("weaponVisualCatalog").objectReferenceValue;
                Assert.That(catalog, Is.TypeOf<EnemyWeaponVisualCatalog>());
                Assert.That(AssetDatabase.GetAssetPath(catalog),
                    Is.EqualTo("Assets/Game/Art/Arcade/Weapons/EnemyPreview/EnemyWeaponVisualCatalog.asset"));
                var options = new SerializedObject(catalog).FindProperty("meleeVisuals");
                Assert.That(options.arraySize, Is.EqualTo(3));
                for (int i = 0; i < options.arraySize; i++)
                {
                    var option = options.GetArrayElementAtIndex(i);
                    Assert.That(option.FindPropertyRelative("displayName").stringValue, Is.Not.Empty);
                    foreach (var field in new[] { "mesh", "material" })
                    {
                        var asset = option.FindPropertyRelative(field).objectReferenceValue;
                        Assert.That(asset, Is.Not.Null, $"选项 {i} 缺少 {field}");
                        Assert.That(AssetDatabase.GetAssetPath(asset),
                            Does.StartWith("Assets/Game/Art/Arcade/"), $"选项 {i} 直接引用备用资源库");
                    }
                }
                var player = new SerializedObject(catalog).FindProperty("playerWeaponSource").objectReferenceValue;
                Assert.That(AssetDatabase.GetAssetPath(player),
                    Is.EqualTo("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SharedGunBarrelsPointForwardFromTheHandsOnAllFiveCharacters()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            foreach (var name in new[]
                     {
                         "GrayboxPlayer", "ArcadeEnemyBasic", "ArcadeEnemyBrute",
                         "ArcadeEnemyArcher", "ArcadeEnemyBossArcher"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab");
                var instance = Object.Instantiate(prefab);
                try
                {
                    var animator = instance.GetComponentsInChildren<Animator>(true).First(a => a.isHuman);
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                    foreach (var id in new[]
                             {
                                 "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.piercing",
                                 "arcade.weapon.grenade", "arcade.weapon.rocket", "arcade.weapon.spread",
                                 "arcade.weapon.raygun"
                             })
                    {
                        var entry = catalog.Find(id);
                        Assert.That(presenter.SetWeapon(entry), Is.True, $"{name}/{id}");
                        animator.Play("Idle", 0, 0f);
                        animator.Update(0.1f);
                        animator.Update(0.1f);
                        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                        Assert.That(Vector3.Dot(presenter.Visual.transform.forward, instance.transform.forward),
                            Is.GreaterThan(0.8f), $"{name}/{id} gun barrel");
                        Assert.That(Vector3.Distance(presenter.Visual.transform.position, hand.position),
                            Is.LessThan(0.2f), $"{name}/{id} hand mount");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void SharedGunOffhandReachesItsGripOnAllFiveCharacters()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            foreach (var name in new[]
                     {
                         "GrayboxPlayer", "ArcadeEnemyBasic", "ArcadeEnemyBrute",
                         "ArcadeEnemyArcher", "ArcadeEnemyBossArcher"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab");
                var instance = Object.Instantiate(prefab);
                try
                {
                    var animator = instance.GetComponentsInChildren<Animator>(true).First(a => a.isHuman);
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                    foreach (var id in new[]
                             {
                                 "arcade.weapon.basic", "arcade.weapon.rapid", "arcade.weapon.piercing",
                                 "arcade.weapon.spread", "arcade.weapon.raygun"
                             })
                    {
                        var entry = catalog.Find(id);
                        Assert.That(presenter.SetWeapon(entry), Is.True, $"{name}/{id}");
                        foreach (var definition in new[] { entry.Animations.Idle,
                                     entry.Animations.MoveForward, entry.Animations.Attack })
                        {
                            var clip = definition.Clip;
                            var graph = PlayableGraph.Create("SharedGunOffhandTest");
                            try
                            {
                                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                                var playable = AnimationClipPlayable.Create(graph, clip);
                                AnimationPlayableOutput.Create(graph, "Character", animator)
                                    .SetSourcePlayable(playable);
                                graph.Play();
                                for (var frame = 0; frame <= 4; frame++)
                                {
                                    playable.SetTime(clip.length * frame / 4f);
                                    graph.Evaluate(0f);
                                    var before = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                                    presenter.ApplyOffhandGrip(definition);
                                    var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                                    var target = presenter.Visual.transform.TransformPoint(entry.OffhandGripLocalPosition);
                                    if (definition.AllowOffhandGrip)
                                        Assert.That(Vector3.Distance(hand.position, target), Is.LessThan(0.07f),
                                            $"{name}/{id}/{clip.name}/frame {frame}");
                                    else
                                        Assert.That(Vector3.Distance(hand.position, before), Is.LessThan(0.001f),
                                            $"{name}/{id}/{clip.name}/disallowed grip");
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
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void SharedKnifePointsDownInIdleOnAllFiveCharacters()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var entry = catalog.Find("weapon.knife");
            foreach (var name in new[]
                     {
                         "GrayboxPlayer", "ArcadeEnemyBasic", "ArcadeEnemyBrute",
                         "ArcadeEnemyArcher", "ArcadeEnemyBossArcher"
                     })
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab"));
                try
                {
                    var animator = instance.GetComponentsInChildren<Animator>(true).First(a => a.isHuman);
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                    Assert.That(presenter.SetWeapon(entry), Is.True, name);
                    var graph = PlayableGraph.Create("SharedKnifeIdleTest");
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var playable = AnimationClipPlayable.Create(graph, entry.Animations.Idle.Clip);
                        AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(playable);
                        graph.Play();
                        graph.Evaluate(0f);
                        Assert.That(Vector3.Dot(presenter.Visual.transform.forward, Vector3.down),
                            Is.GreaterThan(0.95f), name);
                    }
                    finally
                    {
                        if (graph.IsValid()) graph.Destroy();
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void SharedShortSwordGripsAndPointsForwardOnAllFiveCharacters()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var entry = catalog.Find("weapon.short-sword");
            foreach (var name in new[]
                     {
                         "GrayboxPlayer", "ArcadeEnemyBasic", "ArcadeEnemyBrute",
                         "ArcadeEnemyArcher", "ArcadeEnemyBossArcher"
                     })
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab"));
                try
                {
                    var animator = instance.GetComponentsInChildren<Animator>(true).First(a => a.isHuman);
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                    Assert.That(presenter.SetWeapon(entry), Is.True, name);
                    animator.Play("Idle", 0, 0f);
                    animator.Update(0.1f);
                    animator.Update(0.1f);
                    var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var visual = presenter.Visual;
                    var mesh = visual.GetComponentInChildren<MeshFilter>(true)?.sharedMesh;
                    Assert.That(mesh, Is.Not.Null, name);
                    Assert.That(mesh.name, Is.Not.EqualTo("Cube"), name);
                    Assert.That(Vector3.Distance(visual.transform.position, hand.position),
                        Is.LessThan(0.08f), name + "/grip");
                    var expected = (instance.transform.forward * 0.9f + Vector3.up * 0.4f).normalized;
                    Assert.That(Vector3.Dot(visual.transform.up, expected),
                        Is.GreaterThan(0.9f), name + "/blade direction");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void EverySharedWeaponOverridesTheThreeFormalAnimatorActions()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            foreach (var name in new[]
                     {
                         "GrayboxPlayer", "ArcadeEnemyBasic", "ArcadeEnemyBrute",
                         "ArcadeEnemyArcher", "ArcadeEnemyBossArcher"
                     })
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab"));
                try
                {
                    var animator = instance.GetComponentsInChildren<Animator>(true).First(a => a.isHuman);
                    var original = animator.runtimeAnimatorController;
                    var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                    for (var index = 0; index < catalog.Count; index++)
                    {
                        var entry = catalog.Get(index);
                        Assert.That(presenter.SetWeapon(entry), Is.True, $"{name}/{entry.Id}");
                        var overrideController = animator.runtimeAnimatorController as AnimatorOverrideController;
                        Assert.That(overrideController, Is.Not.Null, $"{name}/{entry.Id}");
                        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                        overrideController.GetOverrides(overrides);
                        Assert.That(overrides.Any(pair => pair.Key == presenter.Profile.IdleSlot &&
                            pair.Value == entry.Animations.Idle.Clip), Is.True,
                            $"{name}/{entry.Id}/idle");
                        Assert.That(overrides.Any(pair => pair.Key == presenter.Profile.MoveSlot &&
                            pair.Value == entry.Animations.MoveForward.Clip), Is.True,
                            $"{name}/{entry.Id}/move");
                        Assert.That(overrides.Any(pair => pair.Key == presenter.Profile.AttackSlot &&
                            pair.Value == entry.Animations.Attack.Clip), Is.True,
                            $"{name}/{entry.Id}/attack");
                    }
                    Assert.That(presenter.SetWeapon(null), Is.True);
                    var unarmed = animator.runtimeAnimatorController as AnimatorOverrideController;
                    Assert.That(unarmed, Is.Not.Null, name);
                    Assert.That(unarmed.runtimeAnimatorController, Is.SameAs(original), name);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void FormalEnemyInstancesAcceptEverySharedWeapon()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            foreach (var name in new[]
                     {
                         "ArcadeEnemyBasic", "ArcadeEnemyBrute",
                         "ArcadeEnemyArcher", "ArcadeEnemyBossArcher"
                     })
            {
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab"));
                try
                {
                    var enemy = instance.GetComponent<EnemyView>();
                    var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                    var defaultId = new SerializedObject(enemy).FindProperty("defaultWeaponId").stringValue;
                    Assert.That(catalog.Find(defaultId), Is.Not.Null, $"{name}/default id");
                    Assert.That(enemy.SetCombatWeapon(defaultId), Is.True, $"{name}/default mount");
                    Assert.That(presenter.Current, Is.Not.Null, $"{name}/default");
                    for (var index = 0; index < catalog.Count; index++)
                    {
                        var entry = catalog.Get(index);
                        Assert.That(enemy.SetCombatWeapon(entry.Id), Is.True, $"{name}/{entry.Id}");
                        Assert.That(presenter.Current, Is.SameAs(entry), $"{name}/{entry.Id}");
                        Assert.That(presenter.Visual, Is.Not.Null, $"{name}/{entry.Id}");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void FormalArcadePlayerSelectsSharedGunByWeaponId()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var instance = Object.Instantiate(prefab);
            PlayerViewCtrl controller = null;
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                controller = new PlayerViewCtrl(view, 1, 1, new ViewCtrlRegistry(),
                    new UnityViewInstanceProvider());
                foreach (var pair in new[]
                         {
                             ("arcade.weapon.basic", "ArcadePistol"),
                             ("arcade.weapon.rapid", "ArcadeRifle"),
                             ("arcade.weapon.piercing", "ArcadePiercingGun"),
                             ("arcade.weapon.grenade", "ArcadeGrenade"),
                             ("arcade.weapon.rocket", "ArcadePanzerschreck"),
                             ("arcade.weapon.spread", "ArcadeScifiShotgun"),
                             ("arcade.weapon.raygun", "ArcadeScifiPlasmaRifle")
                         })
                {
                    var weapon = new ArcadeWeaponDefinition(pair.Item1, pair.Item2, null,
                        false, Vector3.zero, 1, 1f, 1f, 1f, 1, 0f,
                        global::ArcadeWeaponBehavior.Direct, 1, 0f);
                    controller.SetArcadeWeaponPresentation(weapon);
                    Assert.That(presenter.Current?.Id, Is.EqualTo(pair.Item1), pair.Item1);
                    Assert.That(view.Animator.runtimeAnimatorController,
                        Is.TypeOf<AnimatorOverrideController>(), pair.Item1);
                    var source = Enumerable.Range(0, view.ArcadeWeaponVisualCount)
                        .Where(index => view.GetArcadeWeaponVisualName(index) == pair.Item2)
                        .Select(view.GetArcadeWeaponVisualRoot).Single();
                    Assert.That(source.GetComponentsInChildren<MeshRenderer>(true)
                        .All(renderer => !renderer.enabled), Is.True, pair.Item1);
                    Assert.That(Vector3.Distance(view.GetProjectileOrigin(Vector3.forward),
                            presenter.Visual.transform.TransformPoint(
                                presenter.Current.ProjectileFirePointLocalPosition)),
                        Is.LessThan(0.001f), $"{pair.Item1} muzzle");
                }
                controller.SetArcadeWeaponPresentation((string)null);
                Assert.That(presenter.Current, Is.Null);
            }
            finally
            {
                controller?.Release();
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void FormalWorldPlayerSelectsEverySharedWeaponByEquipmentItemId()
        {
            var architecture = Game.Interface;
            var previousProvider = architecture.GetUtility<IRuntimeConfigProvider>();
            architecture.RegisterUtility<IRuntimeConfigProvider>(CreateConfigProvider());
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            PlayerViewCtrl controller = null;
            try
            {
                var view = instance.GetComponent<PlayerView>();
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                controller = new PlayerViewCtrl(view, 1, 1, new ViewCtrlRegistry(),
                    new UnityViewInstanceProvider());
                for (var index = 0; index < presenter.Catalog.Count; index++)
                {
                    var entry = presenter.Catalog.Get(index);
                    Assert.That(controller.TrySetSharedWeaponPresentation(entry.WorldItemId), Is.True,
                        entry.Id);
                    Assert.That(presenter.Current?.Id, Is.EqualTo(entry.Id));
                    Assert.That(view.Animator.runtimeAnimatorController,
                        Is.TypeOf<AnimatorOverrideController>());
                    if (entry.IsProjectile)
                        Assert.That(Vector3.Distance(view.GetProjectileOrigin(Vector3.forward),
                                presenter.Visual.transform.TransformPoint(
                                    entry.ProjectileFirePointLocalPosition)),
                            Is.LessThan(0.001f), $"{entry.Id} muzzle");
                }
                Assert.That(controller.TrySetSharedWeaponPresentation(0), Is.True);
                Assert.That(presenter.Current, Is.Null);
            }
            finally
            {
                controller?.Release();
                Object.DestroyImmediate(instance);
                architecture.RegisterUtility<IRuntimeConfigProvider>(previousProvider);
            }
        }

        [Test]
        public void EveryGalleryCharacterOffersTheSameThirteenWeapons()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            try
            {
                var view = scene.GetRootGameObjects()
                    .Select(root => root.GetComponentInChildren<AppearanceGallerySceneView>(true))
                    .First(candidate => candidate != null);
                var data = new SerializedObject(view);
                var prefabs = data.FindProperty("characterPrefabs");
                var dropdown = (Dropdown)data.FindProperty("equipmentDropdown").objectReferenceValue;
                string[] expected = null;
                for (var index = 0; index < prefabs.arraySize; index++)
                {
                    var prefab = (GameObject)prefabs.GetArrayElementAtIndex(index).objectReferenceValue;
                    var character = Object.Instantiate(prefab);
                    try
                    {
                        SceneManager.MoveGameObjectToScene(character, scene);
                        SetPrivate(view, "characterInstance", character);
                        SetPrivate(view, "playerView", character.GetComponent<PlayerView>());
                        SetPrivate(view, "enemyView", character.GetComponent<EnemyView>());
                        SetPrivate(view, "animator", character.GetComponentsInChildren<Animator>(true)
                            .First(a => a.isHuman));
                        SetPrivate(view, "weaponPresenter", character.GetComponent<CharacterWeaponPresenter>());
                        SetPrivate(view, "hasBowAppearance", true);
                        InvokePrivate(view, "RefreshEquipmentOptions");
                        var names = dropdown.options.Select(option => option.text).ToArray();
                        Assert.That(names, Has.Length.EqualTo(14), prefab.name);
                        Assert.That(names, Has.None.Contains("仅试装"), prefab.name);
                        if (expected == null) expected = names;
                        else CollectionAssert.AreEqual(expected, names, prefab.name);
                    }
                    finally
                    {
                        Object.DestroyImmediate(character);
                    }
                }
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void EveryGalleryWeaponMountsAndSamplesItsSharedActionsOnEveryCharacter()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            try
            {
                var view = scene.GetRootGameObjects()
                    .Select(root => root.GetComponentInChildren<AppearanceGallerySceneView>(true))
                    .First(candidate => candidate != null);
                var data = new SerializedObject(view);
                var prefabs = data.FindProperty("characterPrefabs");
                var catalog = (CharacterWeaponCatalog)data.FindProperty("characterWeaponCatalog").objectReferenceValue;
                Assert.That(catalog, Is.Not.Null);
                for (var actor = 0; actor < prefabs.arraySize; actor++)
                {
                    var prefab = (GameObject)prefabs.GetArrayElementAtIndex(actor).objectReferenceValue;
                    var character = Object.Instantiate(prefab);
                    try
                    {
                        SceneManager.MoveGameObjectToScene(character, scene);
                        SetPrivate(view, "characterInstance", character);
                        SetPrivate(view, "playerView", character.GetComponent<PlayerView>());
                        SetPrivate(view, "enemyView", character.GetComponent<EnemyView>());
                        SetPrivate(view, "animator", character.GetComponentsInChildren<Animator>(true)
                            .First(a => a.isHuman));
                        SetPrivate(view, "weaponPresenter", character.GetComponent<CharacterWeaponPresenter>());
                        for (var weapon = 0; weapon < catalog.Count; weapon++)
                        {
                            InvokePrivate(view, "SelectEquipment", weapon + 1);
                            var presenter = character.GetComponent<CharacterWeaponPresenter>();
                            Assert.That(presenter, Is.Not.Null, prefab.name);
                            Assert.That(presenter.Current, Is.SameAs(catalog.Get(weapon)),
                                $"{prefab.name} / {catalog.Get(weapon).Id}: {presenter.LastError}");
                            Assert.That(presenter.Visual, Is.Not.Null);
                            Assert.That(presenter.Visual.activeInHierarchy, Is.True,
                                $"{prefab.name} / {catalog.Get(weapon).Id} must be visible");
                            Assert.That(presenter.Visual.transform.parent,
                                Is.SameAs(character.GetComponentInChildren<Animator>(true)
                                    .GetBoneTransform(catalog.Get(weapon).MainHand)));
                            var actions = catalog.Get(weapon).Animations;
                            foreach (var pair in new[]
                                     {
                                         (index: 0, clip: actions.Idle.Clip),
                                         (index: 1, clip: actions.MoveForward.Clip),
                                         (index: 2, clip: actions.MoveBackward.Clip),
                                         (index: 3, clip: actions.MoveLeft.Clip),
                                         (index: 4, clip: actions.MoveRight.Clip),
                                         (index: 5, clip: actions.Attack.Clip)
                                     })
                            {
                                InvokePrivate(view, "SelectAnimation", pair.index);
                                Assert.That(GetPrivate<AnimationClip>(view, "selectedClip"), Is.SameAs(pair.clip),
                                    $"{prefab.name} / {catalog.Get(weapon).Id} / {pair.index}");
                            }
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(character);
                    }
                }
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SharedGalleryRestoresCompatibleActionTimeAndPauseAcrossCharacterAndWeapon()
        {
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            var view = scene.GetRootGameObjects()
                .Select(root => root.GetComponentInChildren<AppearanceGallerySceneView>(true))
                .First(candidate => candidate != null);
            try
            {
                view.Initialize(null, CreateConfigProvider());
                var catalog = (CharacterWeaponCatalog)new SerializedObject(view)
                    .FindProperty("characterWeaponCatalog").objectReferenceValue;
                var rifle = Enumerable.Range(0, catalog.Count)
                    .First(i => catalog.Get(i).Id == "arcade.weapon.rapid") + 1;
                var spread = Enumerable.Range(0, catalog.Count)
                    .First(i => catalog.Get(i).Id == "arcade.weapon.spread") + 1;
                var bow = Enumerable.Range(0, catalog.Count)
                    .First(i => catalog.Get(i).Id == "weapon.bow") + 1;
                InvokePrivate(view, "SelectEquipment", rifle);
                InvokePrivate(view, "SelectAnimation", 3);
                var action = GetPrivate<CharacterAnimationDefinition>(view, "selectedDefinition");
                Assert.That(action, Is.SameAs(catalog.Get(rifle - 1).Animations.MoveLeft));
                InvokePrivate(view, "Scrub", 0.4f);
                var seconds = GetPrivate<float>(view, "clipTimeSeconds");
                Assert.That(GetPrivate<bool>(view, "playing"), Is.False);

                InvokePrivate(view, "SelectCharacter", 1);
                Assert.That(GetPrivate<CharacterAnimationDefinition>(view, "selectedDefinition"), Is.SameAs(action));
                Assert.That(GetPrivate<float>(view, "clipTimeSeconds"), Is.EqualTo(seconds).Within(0.001f));
                Assert.That(GetPrivate<bool>(view, "playing"), Is.False);
                InvokePrivate(view, "SelectEquipment", spread);
                Assert.That(GetPrivate<CharacterAnimationDefinition>(view, "selectedDefinition"), Is.SameAs(action));
                Assert.That(GetPrivate<float>(view, "clipTimeSeconds"), Is.EqualTo(seconds).Within(0.001f));
                InvokePrivate(view, "SelectEquipment", bow);
                Assert.That(GetPrivate<CharacterAnimationDefinition>(view, "selectedDefinition"),
                    Is.SameAs(catalog.Get(bow - 1).Animations.Idle));
                Assert.That(GetPrivate<float>(view, "clipTimeSeconds"), Is.EqualTo(0f));
                Assert.That(GetPrivate<bool>(view, "playing"), Is.False);
            }
            finally
            {
                InvokePrivate(view, "OnDisable");
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void GalleryAppliesInspectorPoseEditsWithoutResettingCurrentPreview()
        {
            var source = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var catalog = Object.Instantiate(source);
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(
                "Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            var view = scene.GetRootGameObjects()
                .Select(root => root.GetComponentInChildren<AppearanceGallerySceneView>(true))
                .First(candidate => candidate != null);
            try
            {
                SetPrivate(view, "characterWeaponCatalog", catalog);
                view.Initialize(null, CreateConfigProvider());
                var sword = Enumerable.Range(0, catalog.Count)
                    .First(index => catalog.Get(index).Id == "weapon.short-sword");
                InvokePrivate(view, "SelectEquipment", sword + 1);
                InvokePrivate(view, "Scrub", 0.4f);
                var presenter = GetPrivate<CharacterWeaponPresenter>(view, "weaponPresenter");
                var visual = presenter.Visual;
                var action = GetPrivate<CharacterAnimationDefinition>(view, "selectedDefinition");
                var seconds = GetPrivate<float>(view, "clipTimeSeconds");
                var position = catalog.Get(sword).HandLocalPosition + new Vector3(0f, -0.08f, 0f);
                var serialized = new SerializedObject(catalog);
                var entry = serialized.FindProperty("weapons").GetArrayElementAtIndex(sword);
                var rotation = entry.FindPropertyRelative("handLocalEulerAngles").vector3Value +
                               new Vector3(0f, 8f, 0f);
                var scale = entry.FindPropertyRelative("handLocalScale").vector3Value +
                            new Vector3(0.05f, 0.05f, 0.05f);
                entry.FindPropertyRelative("handLocalPosition").vector3Value = position;
                entry.FindPropertyRelative("handLocalEulerAngles").vector3Value = rotation;
                entry.FindPropertyRelative("handLocalScale").vector3Value = scale;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                InvokePrivate(view, "LateUpdate");

                Assert.That(presenter.Visual, Is.SameAs(visual));
                Assert.That(visual.transform.localPosition, Is.EqualTo(position));
                Assert.That(Quaternion.Angle(visual.transform.localRotation, Quaternion.Euler(rotation)),
                    Is.LessThan(0.01f));
                Assert.That(visual.transform.localScale, Is.EqualTo(scale));
                Assert.That(GetPrivate<CharacterAnimationDefinition>(view, "selectedDefinition"), Is.SameAs(action));
                Assert.That(GetPrivate<float>(view, "clipTimeSeconds"), Is.EqualTo(seconds).Within(0.001f));
                Assert.That(GetPrivate<bool>(view, "playing"), Is.False);

                var animationPosition = position + new Vector3(0f, -0.07f, 0f);
                var overrides = entry.FindPropertyRelative("animationPoseOverrides");
                overrides.arraySize = 1;
                var actionPose = overrides.GetArrayElementAtIndex(0);
                actionPose.FindPropertyRelative("definition").objectReferenceValue = action;
                actionPose.FindPropertyRelative("overridePosition").boolValue = true;
                actionPose.FindPropertyRelative("localPosition").vector3Value = animationPosition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                InvokePrivate(view, "LateUpdate");
                Assert.That(presenter.Visual, Is.SameAs(visual));
                Assert.That(visual.transform.localPosition, Is.EqualTo(animationPosition),
                    "当前预览动画的独立姿态应在 Inspector 修改后立即显示");
            }
            finally
            {
                InvokePrivate(view, "OnDisable");
                Object.DestroyImmediate(catalog);
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SharedWeaponUsesDifferentConfiguredPosesForIdleAndMoving()
        {
            var source = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var catalog = Object.Instantiate(source);
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            try
            {
                var swordIndex = Enumerable.Range(0, catalog.Count)
                    .First(index => catalog.Get(index).Id == "weapon.short-sword");
                var sword = catalog.Get(swordIndex);
                var serialized = new SerializedObject(catalog);
                var entry = serialized.FindProperty("weapons").GetArrayElementAtIndex(swordIndex);
                var overrides = entry.FindPropertyRelative("animationPoseOverrides");
                Assert.That(overrides, Is.Not.Null, "武器应可按具体动画配置姿态");
                overrides.arraySize = 2;
                var idlePosition = sword.HandLocalPosition + new Vector3(0f, 0.12f, 0f);
                var movePosition = sword.HandLocalPosition + new Vector3(0f, -0.08f, 0f);
                var definitions = new[] { sword.Animations.Idle, sword.Animations.MoveForward };
                var positions = new[] { idlePosition, movePosition };
                var rotations = new[] { new Vector3(0f, 10f, 0f), new Vector3(0f, -15f, 0f) };
                var scales = new[] { Vector3.one * 0.7f, Vector3.one * 0.9f };
                for (var index = 0; index < overrides.arraySize; index++)
                {
                    var pose = overrides.GetArrayElementAtIndex(index);
                    pose.FindPropertyRelative("definition").objectReferenceValue = definitions[index];
                    pose.FindPropertyRelative("overridePosition").boolValue = true;
                    pose.FindPropertyRelative("localPosition").vector3Value = positions[index];
                    pose.FindPropertyRelative("overrideRotation").boolValue = true;
                    pose.FindPropertyRelative("localEulerAngles").vector3Value = rotations[index];
                    pose.FindPropertyRelative("overrideScale").boolValue = true;
                    pose.FindPropertyRelative("localScale").vector3Value = scales[index];
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                Assert.That(presenter.SetWeapon(sword), Is.True, presenter.LastError);
                presenter.ApplyOffhandGrip(sword.Animations.Idle);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(idlePosition));
                Assert.That(Quaternion.Angle(presenter.Visual.transform.localRotation,
                    Quaternion.Euler(rotations[0])), Is.LessThan(0.01f));
                Assert.That(presenter.Visual.transform.localScale, Is.EqualTo(scales[0]));
                presenter.ApplyOffhandGrip(sword.Animations.MoveForward);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(movePosition));
                Assert.That(Quaternion.Angle(presenter.Visual.transform.localRotation,
                    Quaternion.Euler(rotations[1])), Is.LessThan(0.01f));
                Assert.That(presenter.Visual.transform.localScale, Is.EqualTo(scales[1]));
                var visual = presenter.Visual;
                var editedMovePosition = movePosition + new Vector3(0f, 0.03f, 0f);
                overrides.GetArrayElementAtIndex(1).FindPropertyRelative("localPosition").vector3Value =
                    editedMovePosition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                presenter.ApplyOffhandGrip(sword.Animations.MoveForward);
                Assert.That(presenter.Visual, Is.SameAs(visual), "改姿态不应重建武器实例");
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(editedMovePosition),
                    "Inspector 修改应刷新当前动作姿态");
                presenter.ApplyOffhandGrip(sword.Animations.Attack);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(sword.HandLocalPosition));
                Assert.That(Quaternion.Angle(presenter.Visual.transform.localRotation,
                    sword.HandLocalRotation), Is.LessThan(0.01f));
                Assert.That(presenter.Visual.transform.localScale, Is.EqualTo(sword.HandLocalScale));

                var animator = instance.GetComponent<PlayerView>().Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var lateUpdate = typeof(CharacterWeaponPresenter).GetMethod("LateUpdate",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                animator.Play("Idle", 0, 0f);
                animator.Update(0.1f);
                lateUpdate.Invoke(presenter, null);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(idlePosition),
                    "正式待机应读取同一份动画姿态");
                animator.Play("Run", 0, 0f);
                animator.Update(0.1f);
                lateUpdate.Invoke(presenter, null);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(editedMovePosition),
                    "正式奔跑应读取同一份动画姿态");
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void NewAnimationPoseEntryKeepsWeaponDefaultsUntilFieldsAreEnabled()
        {
            var source = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var catalog = Object.Instantiate(source);
            try
            {
                var swordIndex = Enumerable.Range(0, catalog.Count)
                    .First(index => catalog.Get(index).Id == "weapon.short-sword");
                var serialized = new SerializedObject(catalog);
                var entry = serialized.FindProperty("weapons").GetArrayElementAtIndex(swordIndex);
                var overrides = entry.FindPropertyRelative("animationPoseOverrides");
                overrides.arraySize = 1;
                overrides.GetArrayElementAtIndex(0).FindPropertyRelative("definition")
                    .objectReferenceValue = catalog.Get(swordIndex).Animations.Idle;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var sword = catalog.Get(swordIndex);
                sword.GetPose(sword.Animations.Idle, out var position, out var rotation, out var scale);
                Assert.That(position, Is.EqualTo(sword.HandLocalPosition));
                Assert.That(Quaternion.Angle(rotation, sword.HandLocalRotation), Is.LessThan(0.01f));
                Assert.That(scale, Is.EqualTo(sword.HandLocalScale));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void SharedWeaponCanConfigureEveryListedAnimationIndependently()
        {
            var source = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>(
                "Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var catalog = Object.Instantiate(source);
            try
            {
                var swordIndex = Enumerable.Range(0, catalog.Count)
                    .First(index => catalog.Get(index).Id == "weapon.short-sword");
                var sword = catalog.Get(swordIndex);
                var actions = sword.Animations;
                var definitions = new[]
                    {
                        actions.Idle, actions.MoveForward, actions.MoveBackward,
                        actions.MoveLeft, actions.MoveRight, actions.Attack
                    }
                    .Concat(actions.Extras ?? System.Array.Empty<CharacterAnimationDefinition>())
                    .Where(definition => definition != null).ToArray();
                var serialized = new SerializedObject(catalog);
                var overrides = serialized.FindProperty("weapons").GetArrayElementAtIndex(swordIndex)
                    .FindPropertyRelative("animationPoseOverrides");
                overrides.arraySize = definitions.Length;
                for (var index = 0; index < definitions.Length; index++)
                {
                    var pose = overrides.GetArrayElementAtIndex(index);
                    pose.FindPropertyRelative("definition").objectReferenceValue = definitions[index];
                    pose.FindPropertyRelative("overridePosition").boolValue = true;
                    pose.FindPropertyRelative("localPosition").vector3Value =
                        new Vector3(index * 0.02f, index * 0.01f, 0f);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                for (var index = 0; index < definitions.Length; index++)
                {
                    var expected = new Vector3(index * 0.02f, index * 0.01f, 0f);
                    sword.GetPose(definitions[index], out var previewPosition, out _, out _);
                    sword.GetPose(definitions[index].Clip, out var formalPosition, out _, out _);
                    Assert.That(previewPosition, Is.EqualTo(expected), definitions[index].name + "/preview");
                    Assert.That(formalPosition, Is.EqualTo(expected), definitions[index].name + "/formal");
                }
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void TrialGunsStayNearTheHandAcrossEnemyRigs()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyWeaponVisualCatalog>(
                "Assets/Game/Art/Arcade/Weapons/EnemyPreview/EnemyWeaponVisualCatalog.asset");
            foreach (var name in new[]
                     {
                         "ArcadeEnemyBasic", "ArcadeEnemyBrute", "ArcadeEnemyArcher", "ArcadeEnemyBossArcher"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"Assets/Game/Prefabs/Characters/{name}.prefab");
                var instance = Object.Instantiate(prefab);
                try
                {
                    var enemy = instance.GetComponent<EnemyView>();
                    enemy.ConfigureWeaponVisualCatalog(catalog);
                    for (int index = 2; index < enemy.WeaponVisualOptionCount; index++)
                    {
                        var option = enemy.GetWeaponVisualOptionName(index);
                        if (!option.StartsWith("Arcade") || option.StartsWith("ArcadePanzerschreck")) continue;
                        Assert.That(enemy.SetWeaponPresentation(index), Is.True, $"{name}: {option}");
                        Transform visual = null;
                        foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                            if (child.name.StartsWith("EnemyWeaponVisual_") && child.gameObject.activeInHierarchy)
                                visual = child;
                        Assert.That(visual, Is.Not.Null, $"{name}: {option}");
                        Assert.That(visual.parent.name, Is.EqualTo("Hand_R"), $"{name}: {option}");
                        Assert.That(visual.localPosition.magnitude, Is.LessThan(0.25f), $"{name}: {option}");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        [Test]
        public void SharedEnemyViewRestoresFormalWeaponAfterTrial()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab");
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyWeaponVisualCatalog>(
                "Assets/Game/Art/Arcade/Weapons/EnemyPreview/EnemyWeaponVisualCatalog.asset");
            var enemy = Object.Instantiate(prefab);
            try
            {
                var view = enemy.GetComponent<EnemyView>();
                view.ConfigureWeaponVisualCatalog(catalog);
                var sword = FindChild(enemy, "Weapon");
                Assert.That(sword.gameObject.activeSelf, Is.True);
                Assert.That(view.SetWeaponPresentation(2), Is.True);
                Assert.That(sword.gameObject.activeSelf, Is.False);
                Assert.That(FindChild(enemy, "EnemyWeaponVisual"), Is.Not.Null);
                Assert.That(view.SetWeaponPresentation(1), Is.True);
                Assert.That(sword.gameObject.activeSelf, Is.True);
                Assert.That(FindChild(enemy, "EnemyWeaponVisual"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void FormalBasicEnemySwordGripSitsInsideThePalm()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/ArcadeEnemyBasic.prefab");
            Assert.That(prefab, Is.Not.Null);
            var hand = FindChild(prefab, "Hand_R");
            var sword = FindChild(prefab, "Weapon");
            var firstFinger = FindChild(hand.gameObject, "Finger_01_1");
            var lastFinger = FindChild(hand.gameObject, "Finger_04_1");
            Assert.That(sword.parent, Is.SameAs(hand));
            Assert.That(sword.localPosition.x,
                Is.InRange(hand.InverseTransformPoint(firstFinger.position).x,
                    hand.InverseTransformPoint(lastFinger.position).x));
            Assert.That(Mathf.Abs(sword.localPosition.z), Is.LessThan(0.05f));
        }

        private static IRuntimeConfigProvider CreateConfigProvider() =>
            new LubanRuntimeConfigProvider(() => new Tables(name =>
                new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")))));

        private static void SetPrivate(object target, string name, object value) =>
            typeof(AppearanceGallerySceneView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private static T GetPrivate<T>(object target, string name) =>
            (T)typeof(AppearanceGallerySceneView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);

        private static void InvokePrivate(object target, string name, params object[] arguments) =>
            typeof(AppearanceGallerySceneView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, arguments);

        private static void AssertWeaponAnimationOptions(
            Dropdown dropdown,
            bool hasPistol,
            bool hasRifle,
            bool hasComposite)
        {
            Assert.That(dropdown.options.Any(option => option.text.Contains("Pistol")), Is.EqualTo(hasPistol));
            Assert.That(dropdown.options.Any(option => option.text.Contains("Rifle")), Is.EqualTo(hasRifle));
            Assert.That(dropdown.options.Any(option => option.text.Contains("实战组合")), Is.EqualTo(hasComposite));
        }

        private static Transform FindChild(GameObject root, string name)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private static float SampleAndAlign(GameObject prefab, Scene scene, Renderer stage)
        {
            var character = Object.Instantiate(prefab);
            var graph = default(PlayableGraph);
            try
            {
                SceneManager.MoveGameObjectToScene(character, scene);
                character.transform.position = Vector3.zero;
                character.GetComponent<EnemyView>().SetPresentationVariant(0);
                var animator = character.GetComponentInChildren<Animator>(true);
                var clip = animator.runtimeAnimatorController.animationClips[0];
                graph = PlayableGraph.Create("GalleryAlignmentTest");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0f);
                Assert.That(AppearanceGallerySceneView.AlignVisibleSkinToStage(character, stage), Is.True);
                var minimumY = float.PositiveInfinity;
                var baked = new Mesh();
                try
                {
                    foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        baked.Clear();
                        renderer.BakeMesh(baked);
                        foreach (var vertex in baked.vertices)
                            minimumY = Mathf.Min(minimumY, renderer.transform.TransformPoint(vertex).y);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
                return minimumY;
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(character);
            }
        }
    }
}

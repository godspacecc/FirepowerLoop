using System.Reflection;
using NUnit.Framework;
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class WeaponPoseSettingsTests
    {
        private string directory;
        private WeaponPoseFileStore store;
        private WeaponPoseSettingsSystem system;
        private CharacterWeaponCatalog catalog;
        private CharacterAnimationDefinition idle;
        private CharacterAnimationDefinition move;
        private CharacterWeaponCatalog.Entry weapon;
        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "weapon-pose-tests-" + Guid.NewGuid());
            store = new WeaponPoseFileStore(directory);
            system = new WeaponPoseSettingsSystem(new WeaponPoseSettingsModel(), store);
            idle = ScriptableObject.CreateInstance<CharacterAnimationDefinition>(); idle.name = "Idle";
            move = ScriptableObject.CreateInstance<CharacterAnimationDefinition>(); move.name = "Move";
            catalog = ScriptableObject.CreateInstance<CharacterWeaponCatalog>();
            weapon = new CharacterWeaponCatalog.Entry();
            Field(weapon, "id", "sword");
            Field(weapon, "handLocalScale", Vector3.one);
            var actions = new CharacterAnimationSet();
            Field(actions, "idle", idle); Field(actions, "moveForward", move);
            Field(weapon, "animations", actions);
            Field(catalog, "weapons", new[] { weapon });
        }
        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(idle); UnityEngine.Object.DestroyImmediate(move);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private static void Field(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        [Test]
        public void LocalRollAtNinetyDegreesKeepsSwordDirection()
        {
            system.Set(weapon, idle, WeaponPosePart.Rotation, new Vector3(90, 0, 0), out _);
            var rotate = typeof(WeaponPoseSettingsSystem).GetMethod("RotateLocal");
            Assert.That(rotate, Is.Not.Null, "局部轴增量旋转入口尚未实现");
            var args = new object[] { weapon, idle, 1, 90f, null };
            Assert.That(rotate.Invoke(system, args), Is.EqualTo(true));
            system.Resolve(weapon, idle, out _, out var rotation, out _);
            Assert.That(Vector3.Distance(rotation * Vector3.up, Vector3.forward), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(rotation * Vector3.right, Vector3.up), Is.LessThan(.0001f));
            system.Save(out _);
            new WeaponPoseSettingsSystem(new WeaponPoseSettingsModel(), store).Resolve(weapon, idle, out _, out rotation, out _);
            Assert.That(Vector3.Distance(rotation * Vector3.up, Vector3.forward), Is.LessThan(.0001f));
        }

        [Test]
        public void CopyWholeGripIsAtomicAndDoesNotChangeSourceAnimation()
        {
            system.Set(weapon, idle, WeaponPosePart.Position, new Vector3(.1f,.2f,.3f), out _);
            var setPose = typeof(WeaponPoseSettingsSystem).GetMethod("SetPose");
            Assert.That(setPose, Is.Not.Null, "完整握持参数事务入口尚未实现");
            var before = system.Export();
            var args = new object[] { weapon, move, Vector3.one, Quaternion.identity, Vector3.zero, null };
            Assert.That(setPose.Invoke(system, args), Is.EqualTo(false));
            Assert.That(system.Export(), Is.EqualTo(before), "非法缩放不能留下部分复制");
            args = new object[] { weapon, move, new Vector3(.1f,.2f,.3f), Quaternion.Euler(90,0,0), Vector3.one * .5f, null };
            Assert.That(setPose.Invoke(system, args), Is.EqualTo(true));
            system.Resolve(weapon, move, out var p, out var r, out var s);
            Assert.That(p, Is.EqualTo(new Vector3(.1f,.2f,.3f)));
            Assert.That(Quaternion.Angle(r, Quaternion.Euler(90,0,0)), Is.LessThan(.001f));
            Assert.That(s, Is.EqualTo(Vector3.one * .5f));
            system.Resolve(weapon, idle, out p, out r, out s);
            Assert.That(p, Is.EqualTo(new Vector3(.1f,.2f,.3f)));
            Assert.That(r, Is.EqualTo(Quaternion.identity));
            Assert.That(s, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void LongPressRepeatsAndExitStopsWithoutApplyingReleaseClick()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            var events = new GameObject("HoldTestEvents", typeof(UnityEngine.EventSystems.EventSystem));
            try
            {
                AppearanceGalleryPoseTuningPanel panel = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((panel = root.GetComponentInChildren<AppearanceGalleryPoseTuningPanel>(true)) != null) break;
                var data = new SerializedObject(panel);
                typeof(AppearanceGalleryPoseTuningPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                panel.Initialize(system, catalog); panel.Bind(weapon, idle);
                ((Button)data.FindProperty("tuningButton").objectReferenceValue).onClick.Invoke();
                var button = (Button)data.FindProperty("xPlus").objectReferenceValue;
                var tick = typeof(AppearanceGalleryPoseTuningPanel).GetMethod("Tick");
                Assert.That(tick, Is.Not.Null, "连续加减入口尚未实现");
                var pointer = new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>()) { pointerId = 7 };
                button.OnPointerDown(pointer);
                system.Resolve(weapon, idle, out var p, out _, out _);
                Assert.That(p.x, Is.EqualTo(.001f).Within(.000001f));
                tick.Invoke(panel, new object[] { .4f });
                system.Resolve(weapon, idle, out p, out _, out _);
                Assert.That(p.x, Is.EqualTo(.002f).Within(.000001f));
                button.OnPointerExit(pointer); button.OnPointerUp(pointer); button.OnPointerClick(pointer);
                tick.Invoke(panel, new object[] { 1f });
                system.Resolve(weapon, idle, out p, out _, out _);
                Assert.That(p.x, Is.EqualTo(.002f).Within(.000001f));
                ((Button)data.FindProperty("undoButton").objectReferenceValue).onClick.Invoke();
                system.Resolve(weapon, idle, out p, out _, out _);
                Assert.That(p, Is.EqualTo(Vector3.zero), "一整次长按只需撤销一步");
            }
            finally { UnityEngine.Object.DestroyImmediate(events); EditorSceneManager.CloseScene(scene, true); }
        }
        [Test]
        public void GalleryHasSerializedRuntimeTuningPanel()
        {
            var field = typeof(AppearanceGallerySceneView).GetField("poseTuningPanel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "造型库需要可在游戏内使用的微调面板");
        }

        [Test]
        public void SwitchingAnimationStopsHeldAdjustmentAndCopyKeepsLocalGrip()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            var events = new GameObject("GripTestEvents", typeof(UnityEngine.EventSystems.EventSystem));
            try
            {
                AppearanceGalleryPoseTuningPanel panel = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((panel = root.GetComponentInChildren<AppearanceGalleryPoseTuningPanel>(true)) != null) break;
                var data = new SerializedObject(panel);
                typeof(AppearanceGalleryPoseTuningPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                panel.Initialize(system, catalog); panel.Bind(weapon, idle);
                ((Button)data.FindProperty("tuningButton").objectReferenceValue).onClick.Invoke();
                system.SetPose(weapon, idle, new Vector3(.1f,.2f,.3f), Quaternion.Euler(90,0,0), Vector3.one * .5f, out _);
                var button = (Button)data.FindProperty("xPlus").objectReferenceValue;
                button.OnPointerDown(new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>()));
                panel.Tick(.4f); panel.Bind(weapon, move); panel.Tick(1f);
                system.Resolve(weapon, move, out var p, out _, out _);
                Assert.That(p, Is.EqualTo(Vector3.zero), "切换动作不能把旧长按写入新动作");
                system.Resolve(weapon, idle, out var sourcePosition, out var sourceRotation, out var sourceScale);
                ((Button)data.FindProperty("copyPreviousButton").objectReferenceValue).onClick.Invoke();
                system.Resolve(weapon, move, out p, out var r, out var scale);
                Assert.That(p, Is.EqualTo(sourcePosition));
                Assert.That(Quaternion.Angle(r, sourceRotation), Is.LessThan(.001f));
                Assert.That(scale, Is.EqualTo(sourceScale));
                ((Button)data.FindProperty("undoButton").objectReferenceValue).onClick.Invoke();
                system.Resolve(weapon, move, out p, out _, out _);
                Assert.That(p, Is.EqualTo(Vector3.zero));
                Assert.That(((GameObject)data.FindProperty("valuesPage").objectReferenceValue).GetComponentsInChildren<InputField>(true), Is.Empty,
                    "微调数值不应保留输入框或唤起手机键盘");
            }
            finally { UnityEngine.Object.DestroyImmediate(events); EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void LocalDefaultsAndAnimationPartsRemainIndependentAfterRestart()
        {
            Assert.That(system.Set(weapon, null, WeaponPosePart.Position, Vector3.down, out _), Is.True);
            Assert.That(system.Set(weapon, idle, WeaponPosePart.Rotation, new Vector3(10, 20, 30), out _), Is.True);
            Assert.That(system.Set(weapon, move, WeaponPosePart.Position, Vector3.right, out _), Is.True);
            Assert.That(system.HasUnsavedChanges, Is.True);
            Assert.That(system.Save(out _), Is.True);
            var loaded = new WeaponPoseSettingsSystem(new WeaponPoseSettingsModel(), store);
            loaded.Resolve(weapon, idle, out var p, out var r, out var s);
            Assert.That(p, Is.EqualTo(Vector3.down));
            Assert.That(Quaternion.Angle(r, Quaternion.Euler(10, 20, 30)), Is.LessThan(0.001f));
            Assert.That(s, Is.EqualTo(Vector3.one));
            loaded.Resolve(weapon, move, out p, out r, out _);
            Assert.That(p, Is.EqualTo(Vector3.right)); Assert.That(r, Is.EqualTo(Quaternion.identity));
            loaded.Reset(weapon.Id, move);
            loaded.Resolve(weapon, move, out p, out _, out _);
            Assert.That(p, Is.EqualTo(Vector3.down));
            Assert.That(weapon.HandLocalPosition, Is.EqualTo(Vector3.zero), "不改目录资产");
        }

        [Test]
        public void InvalidImportAndScaleDoNotReplaceCurrentSession()
        {
            system.Set(weapon, idle, WeaponPosePart.Position, Vector3.left, out _);
            var before = system.Export();
            Assert.That(system.Set(weapon, idle, WeaponPosePart.Scale, Vector3.zero, out _), Is.False);
            Assert.That(system.Import("{\"version\":9,\"poses\":[]}", catalog, out _), Is.False);
            Assert.That(system.Import("{}", catalog, out _), Is.False);
            Assert.That(system.Import("{\"version\":1,\"poses\":[{\"weaponId\":\"unknown\",\"animationId\":\"\"}]}", catalog, out _), Is.False);
            Assert.That(system.Export(), Is.EqualTo(before));
            var other = new WeaponPoseSettingsSystem(new WeaponPoseSettingsModel(), store);
            Assert.That(other.Import(before, catalog, out _), Is.True);
            other.Resolve(weapon, idle, out var p, out _, out _);
            Assert.That(p, Is.EqualTo(Vector3.left));
        }

        [Test]
        public void BrokenPrimaryFileRecoversPreviousSavedBackup()
        {
            system.Set(weapon, idle, WeaponPosePart.Position, Vector3.up, out _); system.Save(out _);
            system.Set(weapon, idle, WeaponPosePart.Position, Vector3.right, out _); system.Save(out _);
            File.WriteAllText(store.FilePath, "broken");
            var loaded = new WeaponPoseSettingsSystem(new WeaponPoseSettingsModel(), store);
            loaded.Resolve(weapon, idle, out var p, out _, out _);
            Assert.That(p, Is.EqualTo(Vector3.up)); Assert.That(loaded.LoadWarning, Does.Contain("备份"));
        }

        [Test]
        public void ScenePanelReferencesResolveAndButtonsEditOnlyChosenAnimation()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            try
            {
                AppearanceGallerySceneView view = null;
                foreach (var root in scene.GetRootGameObjects())
                    if ((view = root.GetComponentInChildren<AppearanceGallerySceneView>(true)) != null) break;
                var viewData = new SerializedObject(view);
                var panel = (AppearanceGalleryPoseTuningPanel)viewData.FindProperty("poseTuningPanel").objectReferenceValue;
                Assert.That(panel, Is.Not.Null);
                var data = new SerializedObject(panel);
                var iterator = data.GetIterator();
                while (iterator.NextVisible(true))
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                        Assert.That(iterator.objectReferenceValue, Is.Not.Null, iterator.propertyPath);
                typeof(AppearanceGalleryPoseTuningPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                panel.Initialize(system, catalog); panel.Bind(weapon, idle);
                ((Button)data.FindProperty("tuningButton").objectReferenceValue).onClick.Invoke();
                ((Button)data.FindProperty("yMinus").objectReferenceValue).onClick.Invoke();
                system.Resolve(weapon, idle, out var p, out _, out _);
                Assert.That(p.y, Is.EqualTo(-0.001f).Within(0.000001f));
                system.Resolve(weapon, move, out p, out _, out _);
                Assert.That(p, Is.EqualTo(Vector3.zero));
                ((Button)data.FindProperty("saveButton").objectReferenceValue).onClick.Invoke();
                Assert.That(system.HasUnsavedChanges, Is.False);
                Assert.That(File.Exists(store.FilePath), Is.True);
                var other = new CharacterWeaponCatalog.Entry(); Field(other, "id", "knife");
                panel.Bind(other, idle);
                ((Button)data.FindProperty("xPlus").objectReferenceValue).onClick.Invoke();
                system.Resolve(weapon, idle, out p, out _, out _);
                Assert.That(p, Is.EqualTo(new Vector3(0,-0.001f,0)));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void LocalPoseIsUsedByManualPreviewAndFormalAnimator()
        {
            var source = AssetDatabase.LoadAssetAtPath<CharacterWeaponCatalog>("Assets/Game/Art/Arcade/Weapons/CharacterWeaponCatalog.asset");
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            try
            {
                var sword = source.Find("weapon.short-sword");
                var p = sword.HandLocalPosition + Vector3.down * 0.06f;
                system.Set(sword, sword.Animations.MoveForward, WeaponPosePart.Position, p, out _);
                var presenter = instance.GetComponent<CharacterWeaponPresenter>();
                presenter.ConfigurePoseSettings(system);
                Assert.That(presenter.SetWeapon(sword), Is.True, presenter.LastError);
                presenter.ApplyOffhandGrip(sword.Animations.MoveForward);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(p));
                var animator = instance.GetComponent<PlayerView>().Animator;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Play("Run", 0, 0f); animator.Update(0.1f);
                typeof(CharacterWeaponPresenter).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, null);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(p));
                system.Save(out _);
                presenter.ConfigurePoseSettings(new WeaponPoseSettingsSystem(new WeaponPoseSettingsModel(), store));
                presenter.ApplyOffhandGrip(sword.Animations.MoveForward);
                Assert.That(presenter.Visual.transform.localPosition, Is.EqualTo(p));
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test]
        public void ResourceAnimationOverrideWinsDefaultButLocalAnimationWinsResource()
        {
            var pose = new CharacterWeaponCatalog.Entry.AnimationPoseOverride();
            Field(pose, "definition", idle); Field(pose, "overridePosition", true);
            Field(pose, "localPosition", Vector3.forward);
            Field(weapon, "animationPoseOverrides", new[] { pose });
            system.Set(weapon, null, WeaponPosePart.Position, Vector3.down, out _);
            system.Resolve(weapon, idle, out var p, out _, out _);
            Assert.That(p, Is.EqualTo(Vector3.forward));
            system.Set(weapon, idle, WeaponPosePart.Position, Vector3.left, out _);
            system.Resolve(weapon, idle, out p, out _, out _); Assert.That(p, Is.EqualTo(Vector3.left));
            system.Reset(weapon.Id, idle);
            system.Resolve(weapon, idle, out p, out _, out _); Assert.That(p, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void SaveFailureKeepsUnsavedSessionAndReportsError()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Settings"), "block directory creation");
            system.Set(weapon, idle, WeaponPosePart.Position, Vector3.down, out _);
            Assert.That(system.Save(out var error), Is.False);
            Assert.That(error, Is.Not.Empty); Assert.That(system.HasUnsavedChanges, Is.True);
            system.Resolve(weapon, idle, out var p, out _, out _);
            Assert.That(p, Is.EqualTo(Vector3.down));
        }

        [Test]
        public void TuningSelectsAWeaponAndExcludesUnarmedWhileBrowsingKeepsIt()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/Maps/AppearanceGallery.unity", OpenSceneMode.Additive);
            AppearanceGallerySceneView view = null;
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    if ((view = root.GetComponentInChildren<AppearanceGallerySceneView>(true)) != null) break;
                typeof(AppearanceGallerySceneView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                var data = new SerializedObject(view);
                var weapons = (CharacterWeaponCatalog)data.FindProperty("characterWeaponCatalog").objectReferenceValue;
                var dropdown = (Dropdown)data.FindProperty("equipmentDropdown").objectReferenceValue;
                var panel = (AppearanceGalleryPoseTuningPanel)data.FindProperty("poseTuningPanel").objectReferenceValue;
                typeof(AppearanceGalleryPoseTuningPanel).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
                panel.Initialize(system, weapons); view.Initialize(null, null);
                var ui = new SerializedObject(panel);
                Assert.That(dropdown.options.Count, Is.EqualTo(weapons.Count + 1));
                ((Button)ui.FindProperty("tuningButton").objectReferenceValue).onClick.Invoke();
                Assert.That(dropdown.options.Count, Is.EqualTo(weapons.Count), "微调不能包含无武器");
                var presenter = (CharacterWeaponPresenter)typeof(AppearanceGallerySceneView)
                    .GetField("weaponPresenter", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                Assert.That(presenter.Current.Id, Is.EqualTo(weapons.Get(0).Id));
                Assert.That(((Text)ui.FindProperty("xValue").objectReferenceValue).text, Is.Not.Empty);
                dropdown.value = 1;
                Assert.That(presenter.Current.Id, Is.EqualTo(weapons.Get(1).Id), "过滤空手后索引仍须正确");
                ((Button)ui.FindProperty("browseButton").objectReferenceValue).onClick.Invoke();
                Assert.That(dropdown.options.Count, Is.EqualTo(weapons.Count + 1));
                Assert.That(presenter.Current.Id, Is.EqualTo(weapons.Get(1).Id), "切页保留已选武器");
                dropdown.value = 0;
                Assert.That(presenter.Current, Is.Null);
                ((Button)ui.FindProperty("tuningButton").objectReferenceValue).onClick.Invoke();
                Assert.That(presenter.Current.Id, Is.EqualTo(weapons.Get(0).Id));
            }
            finally
            {
                if (view != null) typeof(AppearanceGallerySceneView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

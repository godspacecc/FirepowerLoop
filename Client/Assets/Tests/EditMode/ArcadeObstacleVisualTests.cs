using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>守住街机障碍的表现契约，避免美术资源重新携带碰撞或依赖本地忽略目录。</summary>
    public sealed class ArcadeObstacleVisualTests
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";

        [Test]
        public void VisualSet_HasFourObstacleVariantsAndDormantContentTemplates()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.transform.childCount, Is.EqualTo(8));
            Assert.That(prefab.transform.GetChild(4).name, Is.EqualTo("Island"));
            Assert.That(prefab.transform.GetChild(5).name, Is.EqualTo("Crate"));
            Assert.That(prefab.transform.GetChild(6).name, Is.EqualTo("ArmorPlate"));
            Assert.That(prefab.transform.GetChild(7).name, Is.EqualTo("Content"));
            foreach (string name in new[] { "Babylon", "Forest", "TreasureRoom", "RouteGate", "TreasureEntrance", "GoldBar", "Gem", "ElectricTower" })
                Assert.That(prefab.transform.Find("Content/" + name), Is.Not.Null, name);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            for (var index = 0; index < prefab.transform.childCount; index++)
            {
                Transform variant = prefab.transform.GetChild(index);
                Assert.That(variant.gameObject.activeSelf, Is.False, variant.name);
                Assert.That(variant.GetComponentsInChildren<MeshRenderer>(true), Is.Not.Empty, variant.name);
            }
        }

        [Test]
        public void VisualSet_DoesNotDependOnIgnoredArtSources()
        {
            string[] dependencies = AssetDatabase.GetDependencies(PrefabPath, true);
            // 本批复用的样板资源已随仓库发布；只允许这份经过Git追踪核对的清单，不开放整个美术缓存目录。
            var publishedCoast = new[] {
                "Generated/DetailedPalm0.asset", "Generated/DetailedPalm1.asset", "Generated/DetailedPalm2.asset",
                "Generated/PalmGreen.mat", "Generated/PalmBark.mat", "Generated/PalmDry.mat", "Generated/PalmSunlit.mat",
                "Generated/Sea.mat", "Generated/WeatheredWood.mat", "Generated/CoastalRock.mat",
                "CoastStudyFoliage.shader", "CoastStudyWater.shader", "PolyHaven/boulder_01.fbx",
                "PolyHaven/boulder_01_nor_gl_1k.jpg", "PolyHaven/boulder_01_diff_1k.jpg", "PolyHaven/boulder_01_ao_1k.jpg",
                "PolyHaven/weathered_brown_planks_nor_gl_1k.jpg", "PolyHaven/weathered_brown_planks_diff_1k.jpg",
                "PolyHaven/weathered_brown_planks_ao_1k.jpg", "PolyHaven/coast_sand_01_nor_gl_1k.jpg",
                "PolyHaven/coast_sand_01_diff_1k.jpg", "PolyHaven/coast_sand_01_ao_1k.jpg"
            }.Select(p=>"Assets/Art/CoastStudy/"+p).ToArray();
            Assert.That(dependencies.Any(path =>
                path.StartsWith("Assets/Art/", StringComparison.Ordinal) && !publishedCoast.Contains(path)), Is.False);
        }

        [Test]
        public void Island_HidesBaseStageRenderersWithoutDisablingCollisionOrOtherMaps()
        {
            var stage = new GameObject("SharedStage");
            var otherMap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            otherMap.name = "EastBoundary";
            var controller = new ArcadeCombatViewController();
            try
            {
                var wall = AddStagePrimitive(stage, "EastBoundary");
                var ground = AddStagePrimitive(stage, "Ground");
                var decor = AddStagePrimitive(stage, "OtherDecoration");
                SetStage(controller, stage.transform);
                Invoke(controller, "HideIslandBaseArena");
                Assert.That(wall.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(ground.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(wall.GetComponent<Collider>().enabled, Is.True);
                Assert.That(ground.GetComponent<Collider>().enabled, Is.True);
                Assert.That(decor.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(otherMap.GetComponent<Renderer>().enabled, Is.True);
            }
            finally { controller.End(); UnityEngine.Object.DestroyImmediate(stage); UnityEngine.Object.DestroyImmediate(otherMap); }
        }

        [Test]
        public void Island_ReleaseRestoresOriginalVisibilityAfterRepeatedHide()
        {
            var stage = new GameObject("SharedStage");
            var controller = new ArcadeCombatViewController();
            try
            {
                var visible = AddStagePrimitive(stage, "NorthBoundary").GetComponent<Renderer>();
                var hidden = AddStagePrimitive(stage, "SouthBoundary").GetComponent<Renderer>();
                hidden.enabled = false;
                SetStage(controller, stage.transform);
                Invoke(controller, "HideIslandBaseArena");
                Invoke(controller, "HideIslandBaseArena");
                controller.SetOpening(default, false);
                Assert.That(visible.enabled, Is.True);
                Assert.That(hidden.enabled, Is.False, "恢复原始状态，不能强制打开原本隐藏的物件");
                controller.SetOpening(default, false);
                Assert.That(visible.enabled, Is.True);
            }
            finally { controller.End(); UnityEngine.Object.DestroyImmediate(stage); }
        }

        [Test]
        public void Island_OceanCoversAreaBeyondShoreWithoutColliders()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeIslandVisualSet.prefab");
            var ocean = prefab.transform.Find("Ocean");
            Assert.That(ocean, Is.Not.Null, "隐藏旧地板后，岛岸外应显示海面");
            var oceanBounds = ocean.GetComponent<Renderer>().bounds;
            var sandBounds = prefab.transform.Find("Sand").GetComponent<Renderer>().bounds;
            Assert.That(oceanBounds.min.x, Is.LessThan(sandBounds.min.x));
            Assert.That(oceanBounds.max.x, Is.GreaterThan(sandBounds.max.x));
            Assert.That(oceanBounds.min.z, Is.LessThan(sandBounds.min.z));
            Assert.That(oceanBounds.max.z, Is.GreaterThan(sandBounds.max.z));
            Assert.That(oceanBounds.max.y, Is.LessThan(sandBounds.min.y));
            Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        private static GameObject AddStagePrimitive(GameObject stage, string name)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name; obj.transform.SetParent(stage.transform, false); return obj;
        }

        [Test]
        public void Content_OpeningReleaseKeepsThemeHiddenAndEndRestoresBaseVisibility()
        {
            var stage = new GameObject("SharedStage");
            var root = new GameObject("ContentRuntime");
            var view = new ArcadeCombatViewController();
            try
            {
                var ground = AddStagePrimitive(stage, "Ground").GetComponent<Renderer>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ArcadeCombatViewController).GetField("root", flags).SetValue(view, root);
                typeof(ArcadeCombatViewController).GetField("contentArena", flags).SetValue(view, 2);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                typeof(ArcadeCombatViewController).GetField("contentTemplates", flags).SetValue(view, prefab.transform.Find("Content"));
                SetStage(view, stage.transform);
                Invoke(view, "SyncContentEnvironment");
                Assert.That(ground.enabled, Is.False);
                int count = root.transform.childCount;
                Invoke(view, "SyncContentEnvironment");
                Assert.That(root.transform.childCount, Is.EqualTo(count), "重复快照不叠加环境");
                view.SetOpening(default, false);
                Assert.That(ground.enabled, Is.False, "无岛岸分支不能恢复被主题覆盖的旧地板");
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
                view.End(); view.End();
                Assert.That(ground.enabled, Is.True, "离场恢复旧地板");
            }
            finally { view.End(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(stage); }
        }
        private static void SetStage(ArcadeCombatViewController controller, Transform stage)
        {
            var field = typeof(ArcadeCombatViewController).GetField("islandBaseArenaRoot", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null); field.SetValue(controller, stage);
        }
        private static void Invoke(ArcadeCombatViewController controller, string method)
        {
            var member = typeof(ArcadeCombatViewController).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(member, Is.Not.Null); member.Invoke(controller, null);
        }
    }
}

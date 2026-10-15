using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    /// <summary>验证基地装饰的地图归属、实体阻挡和出生及交互通路；不启动玩法或改变运行时对象。</summary>
    public sealed class BaseCampSceneTests
    {
        private Scene scene;

        private static void RestoreBuildableProps(MapContext context)
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name =>
                new Luban.ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/Game/Config/{name}.bytes").bytes)));
            Assert.That(provider.TryGetBaseCampLayout(out var layout, out var error), Is.True, error);
            foreach (var row in layout)
            {
                Assert.That(provider.TryGetTempBuilding(row.BuildingId, out var definition, out error), Is.True, error);
                var prefabPath = "Assets/Game/" + definition.PrefabAddress + ".prefab";
                Assert.That(AssetDatabase.GetDependencies(prefabPath).Where(p => p.StartsWith("Assets/Art/")), Is.Empty);
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), context.DynamicObjectRoot);
                instance.name = row.Id;
                instance.transform.SetPositionAndRotation(row.Position, Quaternion.Euler(0, row.Yaw, 0));
                instance.transform.localScale *= row.Scale;
                var view = instance.GetComponent<TemporaryBuildingView>();
                Assert.That(view.ValidateReferences(out error), Is.True, error);
                Assert.That(view.SeparatePhysicalColliders, Is.True);
                view.SetPreviewValidity(true);
                Assert.That(view.GetComponentsInChildren<Collider>(true).Where(c => c != view.FootprintCollider && c.enabled), Is.Empty);
                Assert.That(view.SetCommitted(definition.BlocksNavigation, out error), Is.True, error);
            }
            Assert.That(context.DynamicObjectRoot.childCount, Is.EqualTo(44));
        }

        [SetUp]
        public void SetUp() => scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity", OpenSceneMode.Additive);

        [TearDown]
        public void TearDown() => EditorSceneManager.CloseScene(scene, true);

        [Test]
        public void DecorationsFollowMapVisibilityAndPreserveBoundaryAndDynamicRoot()
        {
            var context = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MapContext>(true)).Single();
            Assert.That(context.Validate(out var error), Is.True, error);
            var decor = context.MapContentRoot.transform.Find("BaseCampDecoration");
            Assert.That(decor, Is.Not.Null, "营地装饰必须由现有地图内容根管理");
            Assert.That(decor.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
            // 已有地表音色标记也是纯表现组件；不能因补景删除正式脚步区域。
            Assert.That(decor.GetComponentsInChildren<MonoBehaviour>(true).Where(component => !(component is CameraOccluderView) && !(component is FootstepSurface)),
                Is.Empty, "静态营地仅允许本项目遮挡与地表音色组件，不引入第三方运行脚本");
            Assert.That(context.DynamicObjectRoot.childCount, Is.Zero);
            var colliders = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Collider>(true)).ToArray();
            Assert.That(colliders.Count(c => !c.isTrigger && !c.transform.IsChildOf(decor)), Is.EqualTo(5), "保留地面与四面边界实体碰撞，交互 Trigger 不计入");
            context.SetMapContentActive(false);
            Assert.That(decor.gameObject.activeInHierarchy, Is.False);
            context.SetMapContentActive(true);
            Assert.That(decor.gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public void SolidPropsBlockPlayerCapsuleAndOpenSheltersKeepEntrances()
        {
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapContext>(true)).Single();
            RestoreBuildableProps(context);
            context.SetMapContentActive(true);
            Physics.SyncTransforms();
            var decor = context.MapContentRoot.transform.Find("BaseCampDecoration");
            foreach (var path in new[] { "木栅栏与自然边界/外围岩石0", "木栅栏与自然边界/外围松树0" })
                Assert.That(decor.Find(path).GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger), Is.True, path + " 必须有实体阻挡");
            foreach (Transform prop in context.DynamicObjectRoot)
                Assert.That(prop.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger), Is.True, prop.name);
            var guideTable = context.DynamicObjectRoot.Find("camp_table_01.2");
            var hits = Physics.CapsuleCastAll(new Vector3(4, .45f, 0), new Vector3(4, 1.55f, 0), .45f, Vector3.right, 4, ~0, QueryTriggerInteraction.Ignore);
            Assert.That(hits.Any(h => h.collider.transform.IsChildOf(guideTable)), Is.True, "玩家胶囊走向桌子必须命中实体碰撞");
            foreach (var path in new[] { "camp_shed_wood_01.0", "camp_shed_wood_01.1", "camp_awning_standing_01a.0", "camp_shed_wood_01.2" })
            {
                var shelter = context.DynamicObjectRoot.Find(path).Find("Model");
                Assert.That(shelter.GetComponentsInChildren<Collider>(true), Is.Not.Empty, path);
                // 局部前方正中保留开口，棚子不能由覆盖整个占地的凸包封死。
                var from = shelter.TransformPoint(new Vector3(0, .9f, 1.2f));
                var to = shelter.TransformPoint(new Vector3(0, .9f, -.4f));
                Assert.That(Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore)
                    .Any(h => h.collider.transform.IsChildOf(shelter)), Is.False, path + " 前方开口被封住");
            }
            Assert.That(decor.GetComponentsInChildren<Collider>(true).Where(c => c.transform.name.Contains("灌木") || c.transform.name.Contains("草簇")), Is.Empty);
        }

        [Test]
        public void PlayerSpawnMainRoadAndNpcApproachesStayClear()
        {
            var context = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MapContext>(true)).Single();
            RestoreBuildableProps(context);
            context.SetMapContentActive(true);
            Physics.SyncTransforms();
            var decor = context.MapContentRoot.transform.Find("BaseCampDecoration");
            var routes = new[] {
                new[] { new Vector3(0, 0, -13), new Vector3(0, 0, 6.5f) },
                new[] { Vector3.zero, new Vector3(5, 0, 0) },
                new[] { Vector3.zero, new Vector3(-4.9f, 0, -.2f) },
                new[] { new Vector3(0, 0, 4.5f), new Vector3(-4.9f, 0, 4.5f) },
                new[] { new Vector3(0, 0, 4.2f), new Vector3(4.9f, 0, 4.2f) },
                new[] { new Vector3(0, 0, 5), new Vector3(0, 0, 7.1f) }
            };
            foreach (var route in routes)
            {
                var motion = route[1] - route[0];
                var hits = Physics.CapsuleCastAll(route[0] + Vector3.up * .45f, route[0] + Vector3.up * 1.55f, .45f,
                    motion.normalized, motion.magnitude, ~0, QueryTriggerInteraction.Ignore);
                Assert.That(hits.Where(h => h.collider.transform.IsChildOf(decor) || h.collider.transform.IsChildOf(context.DynamicObjectRoot)).Select(h => h.collider.name), Is.Empty,
                    "通路受阻：" + route[0] + " → " + route[1]);
                Assert.That(Physics.OverlapCapsule(route[1] + Vector3.up * .45f, route[1] + Vector3.up * 1.55f, .45f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => c.transform.IsChildOf(decor) || c.transform.IsChildOf(context.DynamicObjectRoot)), Is.Empty, "交互点与物件重叠：" + route[1]);
            }
        }

        [Test]
        public void FormalSceneDependenciesStayOutsideBackupArtLibrary()
        {
            var dependencies = AssetDatabase.GetDependencies("Assets/Game/Scenes/BaseMap.unity", true);
            Assert.That(dependencies.Any(path => path.StartsWith("Assets/Game/Art/Environment/BaseCamp/")), Is.True);
            Assert.That(dependencies.Where(path => path.StartsWith("Assets/Art/")), Is.Empty);
            foreach (var path in dependencies.Where(path => path.EndsWith(".mat")))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.That(material.shader, Is.Not.Null, path);
                Assert.That(material.shader.isSupported, Is.True, path);
            }
        }
    }
}

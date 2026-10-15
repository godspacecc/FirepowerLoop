using System.Linq;
using System.Reflection;
using Game;
using Luban;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    /// <summary>核对正式采集点配置与序列化契约；只读场景，表现测试只操作临时对象。</summary>
    public sealed class CivilisationHarvestSceneTests
    {
        [Test] public void MiningTargetsBindFourIndependentRocksAndCampPickaxe()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/CivilisationValleyMap.unity", OpenSceneMode.Additive);
            try
            {
                var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
                Assert.That(provider.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
                var views = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CivilisationPointView>(true)).ToArray();
                var rocks = catalog.Points.Where(p => p.RequiredWeaponItemId == 18).ToArray();
                Assert.That(rocks.Length, Is.EqualTo(4));
                foreach (var point in rocks)
                {
                    Assert.That(point.ItemId, Is.EqualTo(31)); Assert.That(point.Amount, Is.EqualTo(4)); Assert.That(point.Seconds, Is.EqualTo(3));
                    var view = views.Single(v => v.PointId == point.Id);
                    var data = new SerializedObject(view);
                    var resource = (GameObject)data.FindProperty("harvestedVisualRoot").objectReferenceValue;
                    Assert.That(resource, Is.Not.Null); Assert.That(resource.name, Does.StartWith("ValleyRock_"));
                    Assert.That(resource.GetComponentInParent<TemporaryBuildingView>(), Is.Null, "采石目标不能登记为可拆建筑");
                    Assert.That(resource.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger), Is.True);
                    Assert.That(view.GetComponent<Collider>().isTrigger, Is.True);
                    var prompt = (GameObject)data.FindProperty("nameRoot").objectReferenceValue;
                    Assert.That(prompt.GetComponentInChildren<TMP_Text>(true).text, Does.Contain("镐头"));
                }
                Assert.That(rocks.Select(p => views.Single(v => v.PointId == p.Id)).Distinct().Count(), Is.EqualTo(4));
                var pickup = views.Single(v => v.PointId == "camp.pickaxe");
                Assert.That(new SerializedObject(pickup).FindProperty("harvestedVisualRoot").objectReferenceValue, Is.Not.Null);
                Assert.That(scene.isDirty, Is.False);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        [Test] public void HarvestTargetsHaveStableIdsTreeBindingsAndReadablePrompts()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/CivilisationValleyMap.unity", OpenSceneMode.Additive);
            try
            {
                var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
                Assert.That(provider.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
                var views = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CivilisationPointView>(true)).ToArray();
                foreach (var point in catalog.Points)
                {
                    var view = views.Single(v => v.PointId == point.Id);
                    Assert.That(Vector2.Distance(new Vector2(view.transform.position.x, view.transform.position.z), new Vector2(point.Position.x, point.Position.z)), Is.LessThan(.01f));
                    var data = new SerializedObject(view); var prompt = (GameObject)data.FindProperty("nameRoot").objectReferenceValue;
                    Assert.That(prompt, Is.Not.Null); Assert.That(prompt.GetComponentInChildren<TMP_Text>(true).text, Is.Not.Empty);
                    if (!point.FellTree) continue;
                    var tree = (GameObject)data.FindProperty("harvestedVisualRoot").objectReferenceValue;
                    Assert.That(tree, Is.Not.Null); Assert.That(tree.activeSelf, Is.True);
                    Assert.That(tree.GetComponentsInChildren<Collider>().Any(c => c.enabled && !c.isTrigger), Is.True);
                    Assert.That(view.GetComponent<Collider>().isTrigger, Is.True);
                    Assert.That(tree.GetComponentInParent<TemporaryBuildingView>(), Is.Null, "树木不能登记为可拆建筑");
                }
                Assert.That(views.Count(v => v.Kind == CivilisationPointKind.Preparation), Is.EqualTo(1));
                Assert.That(views.Count(v => v.Kind == CivilisationPointKind.Return), Is.EqualTo(1));
                Assert.That(scene.isDirty, Is.False);
                Assert.That(AssetDatabase.GetDependencies(scene.path, true).Any(p => p.StartsWith("Assets/Art/")), Is.False);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        [Test] public void HarvestPresentationDisablesAndRestoresOnlyBoundResourceColliders()
        {
            var support = new CivilisationHarvestTests(); support.SetUp();
            var node = new GameObject("HarvestView"); node.SetActive(false);
            var resource = GameObject.CreatePrimitive(PrimitiveType.Cube); resource.name = "BoundResource";
            var neighbour = GameObject.CreatePrimitive(PrimitiveType.Cube); neighbour.name = "OtherBuilding";
            try
            {
                var view = node.AddComponent<CivilisationPointView>(); var data = new SerializedObject(view);
                data.FindProperty("harvestedVisualRoot").objectReferenceValue = resource; data.ApplyModifiedPropertiesWithoutUndo();
                var method = typeof(CivilisationPointView).GetMethod("RefreshHarvestPresentation", BindingFlags.NonPublic | BindingFlags.Instance);
                method.Invoke(view, new object[] { true }); method.Invoke(view, new object[] { true });
                Assert.That(resource.activeSelf, Is.False); Assert.That(node.GetComponent<Collider>().enabled, Is.False);
                Assert.That(neighbour.activeSelf, Is.True); Assert.That(neighbour.GetComponent<Collider>().enabled, Is.True);
                method.Invoke(view, new object[] { false });
                Assert.That(resource.activeSelf, Is.True); Assert.That(node.GetComponent<Collider>().enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(node); Object.DestroyImmediate(resource); Object.DestroyImmediate(neighbour); support.TearDown();
            }
        }
    }
}

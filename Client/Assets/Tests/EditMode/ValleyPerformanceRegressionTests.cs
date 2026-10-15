using System;
using System.Collections;
using System.Reflection;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证大森林的刷新边界与局部遮挡；不依赖机器速度设置耗时断言。</summary>
    public sealed class ValleyPerformanceRegressionTests
    {
        [Test]
        public void CivilisationCatalogReusesValidatedSnapshot()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(n => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + n + ".bytes").bytes)));
            Assert.That(provider.TryGetCivilisationCatalog(out var first, out var error), Is.True, error);
            Assert.That(provider.TryGetCivilisationCatalog(out var second, out error), Is.True, error);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void TimeNotificationDoesNotResynchronizeHarvestPresentation()
        {
            var support = new CivilisationHarvestTests(); support.SetUp();
            var node = new GameObject("Harvest refresh boundary"); node.SetActive(false);
            var resource = new GameObject("Resource");
            var ctrl = new CivilisationSceneCtrl();
            try
            {
                var view = node.AddComponent<CivilisationPointView>();
                var data = new SerializedObject(view);
                data.FindProperty("kind").enumValueIndex = (int)CivilisationPointKind.Search;
                data.FindProperty("pointId").stringValue = "tree.valley.1";
                data.FindProperty("harvestedVisualRoot").objectReferenceValue = resource;
                data.ApplyModifiedPropertiesWithoutUndo();
                ctrl.Initialize();
                ((IList)typeof(CivilisationSceneCtrl).GetField("views", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl)).Add(view);
                var model = Game.Interface.GetModel<CivilisationExpeditionModel>();
                typeof(CivilisationExpeditionModel).GetMethod("Visit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(model, new object[] { view.PointId, null });
                Game.Interface.SendEvent(new ExpeditionChangedEvent());
                Assert.That(resource.activeSelf, Is.True, "计时通知不能触发整图采集投影");
            }
            finally { ctrl.Dispose(); Object.DestroyImmediate(node); Object.DestroyImmediate(resource); support.TearDown(); }
        }

        [Test]
        public void RestoreAndResetResynchronizeHarvestPresentation()
        {
            var support = new CivilisationHarvestTests(); support.SetUp();
            var node = new GameObject("Restored harvest boundary"); node.SetActive(false);
            var resource = new GameObject("Restored resource"); var ctrl = new CivilisationSceneCtrl();
            try
            {
                var view = node.AddComponent<CivilisationPointView>(); var data = new SerializedObject(view);
                data.FindProperty("kind").enumValueIndex = (int)CivilisationPointKind.Search;
                data.FindProperty("pointId").stringValue = "tree.valley.5";
                data.FindProperty("harvestedVisualRoot").objectReferenceValue = resource; data.ApplyModifiedPropertiesWithoutUndo();
                ctrl.Initialize();
                ((Game)Game.Interface).RegisterSystem(new MapSceneSystem(null, null, null, null, null, null));
                typeof(CivilisationSceneCtrl).GetField("mapInstanceId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, Game.Interface.GetSystem<MapSceneSystem>().CurrentMapInstanceId);
                ((IList)typeof(CivilisationSceneCtrl).GetField("views", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl)).Add(view);
                var model = Game.Interface.GetModel<CivilisationExpeditionModel>();
                typeof(CivilisationExpeditionModel).GetMethod("Visit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(model, new object[] { view.PointId, null });
                model.Restore(model.Capture()); Assert.That(resource.activeSelf, Is.False);
                model.Reset(); Assert.That(resource.activeSelf, Is.True); Assert.That(node.GetComponent<Collider>().enabled, Is.True);
            }
            finally { ctrl.Dispose(); Object.DestroyImmediate(node); Object.DestroyImmediate(resource); support.TearDown(); }
        }

        [Test]
        public void OcclusionCandidatesExcludeDistantForestAndRecoverAfterCameraLeaves()
        {
            var root = new GameObject("Spatial forest fixture");
            var cameraObject = new GameObject("Spatial camera"); var player = new GameObject("Spatial player");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var transparent = new Material(material);
            var camera = cameraObject.AddComponent<Camera>(); camera.transform.position = new Vector3(0, 4, -4);
            try
            {
                Renderer near = null;
                for (var i = 0; i < 101; i++)
                {
                    var tree = GameObject.CreatePrimitive(PrimitiveType.Cube); tree.transform.SetParent(root.transform);
                    tree.transform.position = i == 0 ? new Vector3(0, 2.6f, -2) : new Vector3(500 + i, 0, 500);
                    var renderer = tree.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                    if (i == 0) near = renderer;
                    var view = tree.AddComponent<CameraOccluderView>(); view.BodyRenderers = new[] { renderer }; view.TransparentMaterial = transparent;
                }
                using (var ctrl = new TopDownOcclusionCtrl(camera, player.transform))
                {
                    Physics.SyncTransforms(); ctrl.Tick(1f);
                    var candidates = (ICollection)typeof(TopDownOcclusionCtrl).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
                    Assert.That(candidates.Count, Is.LessThan(10), "远处森林不应参与逐帧精确检测");
                    Assert.That(near.sharedMaterial, Is.Not.SameAs(material));
                    camera.transform.position += Vector3.right * 200; player.transform.position += Vector3.right * 200;
                    ctrl.Tick(.1f); ctrl.Tick(1f);
                    Assert.That(near.sharedMaterial, Is.SameAs(material), "离开分区后仍须完成淡化恢复");
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(player); Object.DestroyImmediate(material); Object.DestroyImmediate(transparent); }
        }
    }
}

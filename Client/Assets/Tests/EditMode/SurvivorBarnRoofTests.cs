using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>在正式仓房实例上验证透视及正交相机下的遮挡、恢复与材质回收。</summary>
    public sealed class SurvivorBarnRoofTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void BothBarnsFadeIndependentlyAndReleaseTogether(bool orthographic)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab");
            var farm = Object.Instantiate(prefab);
            var cameraObject = new GameObject("two barn test camera", typeof(Camera));
            var view = new ArcadeCombatViewController();
            try
            {
                var roofs = farm.GetComponentsInChildren<SurvivorBarnRoof>();
                Assert.That(roofs.Length, Is.EqualTo(2));
                const System.Reflection.BindingFlags hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var field = typeof(ArcadeCombatViewController).GetField("survivorBarnRoofs", hidden);
                Assert.That(field, Is.Not.Null);
                field.SetValue(view, roofs);
                var originals = new[] { roofs[0].GetComponent<MeshRenderer>().sharedMaterial, roofs[1].GetComponent<MeshRenderer>().sharedMaterial };
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = orthographic;
                for (int current = 0; current < roofs.Length; current++)
                {
                    var plane = roofs[current].transform.Find("RoofPlaneLeft");
                    camera.transform.SetPositionAndRotation(plane.position + Vector3.up * 10, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    foreach (var roof in roofs) roof.Tick(camera, plane.position - Vector3.up * 4, 1);
                    Assert.That(roofs[current].Opacity, Is.EqualTo(.32f).Within(.001f));
                    Assert.That(roofs[1-current].Opacity, Is.EqualTo(1));
                    Assert.That(roofs[1-current].GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(originals[1-current]));
                }
                // 分别保持两屋淡化后，通过真实场地释放路径检查全部归还。
                foreach (var roof in roofs)
                {
                    var plane = roof.transform.Find("RoofPlaneLeft");
                    camera.transform.SetPositionAndRotation(plane.position + Vector3.up * 10, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                    roof.Tick(camera, plane.position - Vector3.up * 4, 1);
                }
                typeof(ArcadeCombatViewController).GetMethod("ReleaseEarlyStageLoad", hidden).Invoke(view, null);
                for (int i = 0; i < roofs.Length; i++)
                {
                    Assert.That(roofs[i].Opacity, Is.EqualTo(1));
                    Assert.That(roofs[i].GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(originals[i]));
                }
                Assert.That((SurvivorBarnRoof[])field.GetValue(view), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(farm);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ActualRoofFadesOnlyBetweenCameraAndPlayerAndRestores(bool orthographic)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab");
            var farm = Object.Instantiate(prefab);
            var cameraObject = new GameObject("barn roof test camera", typeof(Camera));
            try
            {
                var roof = farm.GetComponentInChildren<SurvivorBarnRoof>();
                Assert.That(roof, Is.Not.Null);
                var renderer = roof.GetComponent<MeshRenderer>();
                var original = renderer.sharedMaterial;
                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = orthographic;
                var plane = roof.transform.Find("RoofPlaneLeft");
                Vector3 player = plane.position - Vector3.up * 4;
                camera.transform.SetPositionAndRotation(plane.position + Vector3.up * 10, Quaternion.LookRotation(Vector3.down, Vector3.forward));

                roof.Tick(camera, player, .05f);
                Assert.That(roof.Opacity, Is.InRange(.33f,.99f));
                roof.Tick(camera, player, 1);
                Assert.That(roof.Opacity, Is.EqualTo(.32f).Within(.001f));
                Assert.That(renderer.sharedMaterial.renderQueue, Is.EqualTo(3000));
                Assert.That(renderer.sharedMaterial.GetFloat("_ZWrite"), Is.Zero);
                roof.Tick(camera, plane.position + Vector3.up * 2, 1);
                Assert.That(roof.Opacity, Is.EqualTo(1));
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));

                roof.Tick(camera, player, 1);
                roof.Tick(camera, player + Vector3.forward * 12, 1);
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
                roof.Tick(camera, player, 1);
                roof.Restore(); // 验证场地退出时的显式释放。
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(farm);
            }
        }
    }
}

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>以正式农场验证墙体与屋顶共用的视线，以及退出清理。</summary>
    public sealed class SurvivorBarnRoofTests
    {
        private const string FarmPath = "Assets/Game/Prefabs/SceneObjects/SurvivorFarmVisual.prefab";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void CircularSightCoversAllDirectionsAndStopsAtSolidWall()
        {
            var farm = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FarmPath));
            ArcadeCombatViewController view = null;
            try
            {
                view = Prepare(farm);
                var outside = new Vector2(23.1f, 25.1f);
                view.SetSurvivorVisibility(1f, 6f, outside);
                view.TickSurvivorBuildingVisibility(1f);
                Assert.That(view.IsSurvivorPointVisible(outside + Vector2.down * .25f), Is.True);
                Assert.That(view.IsSurvivorPointVisible(outside + Vector2.down * 2f), Is.False, "射线停在实墙");
                Assert.That(view.IsSurvivorPointVisible(outside + Vector2.up), Is.True, "背后也在圆形范围内");
                Assert.That(view.IsSurvivorPointVisible(outside + Vector2.right), Is.True, "侧面也在圆形范围内");
                Assert.That(view.IsSurvivorPointVisible(outside + Vector2.right * 6.5f), Is.False, "圆外不可见");

                view.SetSurvivorVisibility(1f, 6f, new Vector2(20f, 25.1f));
                view.TickSurvivorBuildingVisibility(1f);
                Assert.That(view.IsSurvivorPointVisible(new Vector2(20f, 22f)), Is.True, "门洞后的可见内容");
            }
            finally
            {
                if (view != null) Release(view);
                Object.DestroyImmediate(farm);
            }
        }

        [Test]
        public void FormalFarmBindsBothRoofAndWallSurfacesToVisibility()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FarmPath);
            var surfaces = prefab.GetComponentsInChildren<SurvivorBuildingSurface>();
            Assert.That(surfaces.Count(surface => surface.RevealInterior), Is.EqualTo(2));
            var wall = surfaces.Single(surface => !surface.RevealInterior);
            Assert.That(wall.GetComponent<MeshRenderer>().name, Does.EndWith("FarmPlaster"));
            Assert.That(surfaces.Select(surface => surface.TransparentMaterial).Distinct().Single().shader.name,
                Is.EqualTo("Game/Survivor/Building Visibility"));
        }

        [Test]
        public void UpperWallAndRoofShareFirstObstruction()
        {
            var farm = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FarmPath));
            ArcadeCombatViewController view = null;
            try
            {
                view = Prepare(farm);
                var surfaces = farm.GetComponentsInChildren<SurvivorBuildingSurface>();
                var wall = surfaces.Single(surface => !surface.RevealInterior).GetComponent<MeshRenderer>();
                var roof = surfaces.First(surface => surface.RevealInterior).GetComponent<MeshRenderer>();
                var center = roof.bounds.center;
                var player = new Vector2(center.x + 3.1f, center.z + 4.1f);
                view.SetSurvivorVisibility(1f, 6f, player);
                view.TickSurvivorBuildingVisibility(1f);

                var wallBlock = ReadBlock(wall);
                var roofBlock = ReadBlock(roof);
                var sight = (Texture2D)wallBlock.GetTexture("_BuildingSight");
                Assert.That(sight, Is.Not.Null);
                Assert.That(roofBlock.GetTexture("_BuildingSight"), Is.SameAs(sight));
                Assert.That(wallBlock.GetFloat("_BuildingSightOpacity"), Is.EqualTo(.44f).Within(.001f));
                Assert.That(roofBlock.GetFloat("_BuildingSightOpacity"), Is.EqualTo(.13f).Within(.001f));

                Assert.That(ReadRay(sight, Vector2.down, 6f), Is.EqualTo(.6f).Within(.015f), "最先命中墙的外侧");

                view.SetSurvivorVisibility(1f, 6f, new Vector2(center.x, player.y));
                view.TickSurvivorBuildingVisibility(1f);
                Assert.That(ReadRay(sight, Vector2.down, 6f), Is.EqualTo(6f).Within(.015f), "通过门洞时同一视线延伸到室内");
            }
            finally
            {
                if (view != null) Release(view);
                Object.DestroyImmediate(farm);
            }
        }

        [Test]
        public void LeavingRangeAndReleasingFarmRestoreOriginalMaterials()
        {
            var farm = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FarmPath));
            try
            {
                var view = Prepare(farm);
                var renderers = farm.GetComponentsInChildren<SurvivorBuildingSurface>()
                    .Select(surface => surface.GetComponent<MeshRenderer>()).ToArray();
                var originals = renderers.Select(renderer => renderer.sharedMaterial).ToArray();
                var roof = renderers.First(renderer => renderer.GetComponent<SurvivorBuildingSurface>().RevealInterior);

                view.SetSurvivorVisibility(1f, 6f, new Vector2(roof.bounds.center.x, roof.bounds.center.z));
                view.TickSurvivorBuildingVisibility(1f);
                Assert.That(roof.sharedMaterial, Is.Not.SameAs(originals[System.Array.IndexOf(renderers, roof)]));

                view.SetSurvivorVisibility(1f, 6f, new Vector2(0, -25));
                view.TickSurvivorBuildingVisibility(1f);
                CollectionAssert.AreEqual(originals, renderers.Select(renderer => renderer.sharedMaterial));

                view.SetSurvivorVisibility(1f, 6f, new Vector2(roof.bounds.center.x, roof.bounds.center.z));
                view.TickSurvivorBuildingVisibility(1f);
                Release(view);
                CollectionAssert.AreEqual(originals, renderers.Select(renderer => renderer.sharedMaterial));
                Assert.That(renderers.All(renderer => ReadBlock(renderer).GetTexture("_BuildingSight") == null), Is.True);
            }
            finally { Object.DestroyImmediate(farm); }
        }

        private static ArcadeCombatViewController Prepare(GameObject farm)
        {
            var view = new ArcadeCombatViewController();
            var type = typeof(ArcadeCombatViewController);
            type.GetField("mapRoot", Hidden).SetValue(view, farm.transform);
            type.GetMethod("PrepareSurvivorBuildingVisibility", Hidden).Invoke(view, new object[] { farm });
            return view;
        }

        private static MaterialPropertyBlock ReadBlock(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block;
        }

        private static void Release(ArcadeCombatViewController view) =>
            typeof(ArcadeCombatViewController).GetMethod("ReleaseEarlyStageLoad", Hidden).Invoke(view, null);

        private static float ReadRay(Texture2D field, Vector2 direction, float radius)
        {
            float angle = Mathf.Repeat(Mathf.Atan2(direction.y, direction.x) / (2f * Mathf.PI), 1f);
            var color = (Color32)field.GetPixel(Mathf.FloorToInt(angle * field.width), 0);
            return (color.r * 256 + color.g) * (radius / 65535f);
        }
    }
}

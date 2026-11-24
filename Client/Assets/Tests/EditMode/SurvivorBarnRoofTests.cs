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
        public void UpperWallFadesThroughItsThicknessButKeepsInteriorBlocked()
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
                Assert.That(wallBlock.GetVector("_BuildingSurfaceParameters").x, Is.Zero);
                Assert.That(roofBlock.GetVector("_BuildingSurfaceParameters").x, Is.EqualTo(1f));
                Assert.That(wallBlock.GetVector("_BuildingSurfaceParameters").y, Is.EqualTo(.13f).Within(.001f));

                Vector2 limits = ReadRay(sight, Vector2.down, 6f);
                Assert.That(limits.x, Is.EqualTo(.6f).Within(.015f), "最先命中墙的外侧");
                Assert.That(limits.y, Is.EqualTo(1f).Within(.015f), "墙的内侧表面也能淡出");

                view.SetSurvivorVisibility(1f, 6f, new Vector2(center.x, player.y));
                view.TickSurvivorBuildingVisibility(1f);
                limits = ReadRay(sight, Vector2.down, 6f);
                Assert.That(limits.x, Is.EqualTo(6f).Within(.015f), "通过门洞时同一视线延伸到室内");
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

        private static Vector2 ReadRay(Texture2D field, Vector2 direction, float radius)
        {
            float angle = Mathf.Repeat(Mathf.Atan2(direction.y, direction.x) / (2f * Mathf.PI), 1f);
            var color = (Color32)field.GetPixel(Mathf.FloorToInt(angle * field.width), 0);
            return new Vector2(color.r * 256 + color.g, color.b * 256 + color.a) * (radius / 65535f);
        }
    }
}

using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class TerrainFootstepTests
    {
        private GameObject root;
        private TerrainData data;
        private TerrainLayer first, second;
        private FootstepSurface marker;
        [SetUp] public void SetUp()
        {
            data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16, size = new Vector3(16, 4, 16) };
            first = new TerrainLayer(); second = new TerrainLayer();
            data.terrainLayers = new[] { first, second };
            var weights = new float[16, 16, 2];
            for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) weights[z, x, x < 8 ? 0 : 1] = 1;
            data.SetAlphamaps(0, 0, weights);
            root = Terrain.CreateTerrainGameObject(data);
            root.transform.position = new Vector3(5000, 0, 5000);
            marker = root.AddComponent<FootstepSurface>();
        }
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(data);
            Object.DestroyImmediate(first); Object.DestroyImmediate(second);
        }
        private void Bind(params FootstepSurfaceKind[] kinds)
        {
            var terrainField = typeof(FootstepSurface).GetField("TerrainSource");
            var kindsField = typeof(FootstepSurface).GetField("TerrainLayerKinds");
            Assert.NotNull(terrainField, "Terrain 地表采样尚未实现"); Assert.NotNull(kindsField);
            terrainField.SetValue(marker, root.GetComponent<Terrain>()); kindsField.SetValue(marker, kinds);
        }
        private FootstepSurfaceKind Resolve(Vector3 world)
        {
            var method = typeof(FootstepSurface).GetMethod("ResolveKind");
            Assert.NotNull(method);
            return (FootstepSurfaceKind)method.Invoke(marker, new object[] { world });
        }
        [Test] public void PaintedLayersSelectDifferentFootstepKinds()
        {
            Bind(FootstepSurfaceKind.Grass, FootstepSurfaceKind.Gravel);
            Assert.AreEqual(FootstepSurfaceKind.Grass, Resolve(root.transform.position + new Vector3(2, 0, 2)));
            Assert.AreEqual(FootstepSurfaceKind.Gravel, Resolve(root.transform.position + new Vector3(14, 0, 2)));
        }
        [Test] public void MissingLayerMappingIsSilent()
        {
            Bind(FootstepSurfaceKind.Grass);
            Assert.AreEqual(FootstepSurfaceKind.Unknown, Resolve(root.transform.position + new Vector3(14, 0, 2)));
        }
        [Test] public void OuterBoundaryUsesLastPaintedTexel()
        {
            Bind(FootstepSurfaceKind.Grass, FootstepSurfaceKind.Gravel);
            Assert.AreEqual(FootstepSurfaceKind.Gravel, Resolve(root.transform.position + new Vector3(16, 0, 16)));
        }
        [Test] public void GroundProbeReadsPaintedLayerAtActualSupport()
        {
            Bind(FootstepSurfaceKind.Grass, FootstepSurfaceKind.Gravel);
            var actor = new GameObject("TerrainFootstepActor");
            try
            {
                actor.transform.position = root.transform.position + new Vector3(14, .01f, 2);
                var cc = actor.AddComponent<CharacterController>(); cc.center = Vector3.up; cc.height = 2; cc.enabled = false;
                Physics.SyncTransforms();
                Assert.True(new FootstepGroundProbe().TryGet(actor.transform, cc, out var kind));
                Assert.AreEqual(FootstepSurfaceKind.Gravel, kind);
            }
            finally { Object.DestroyImmediate(actor); }
        }
    }
}

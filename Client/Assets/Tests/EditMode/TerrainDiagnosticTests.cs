using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class TerrainDiagnosticTests
    {
        [Test]
        public void MissingTerrainDataIsReportedWithoutThrowingOrChangingTerrain()
        {
            var root = new GameObject("diagnostic-root");
            try
            {
                var terrain = root.AddComponent<Terrain>();
                var report = Build(root, Vector3.zero);
                Assert.That(report, Does.Contain("TerrainData=null"));
                Assert.That(terrain.terrainData, Is.Null);
                Assert.That(terrain.enabled, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void SamplesWeightsOnlyInsideTerrainAndLeavesDataUnchanged()
        {
            var data = new TerrainData { heightmapResolution = 33, alphamapResolution = 16, size = new Vector3(10, 10, 10) };
            var layer = new TerrainLayer();
            data.terrainLayers = new[] { layer };
            var root = Terrain.CreateTerrainGameObject(data);
            try
            {
                var before = data.GetAlphamaps(0, 0, 1, 1)[0, 0, 0];
                Assert.That(Build(root, new Vector3(5, 0, 5)), Does.Contain("weight[0]="));
                Assert.That(Build(root, new Vector3(-1, 0, 5)), Does.Contain("PlayerOutsideTerrain"));
                Assert.That(data.GetAlphamaps(0, 0, 1, 1)[0, 0, 0], Is.EqualTo(before));
                Assert.That(layer.diffuseTexture, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(data);
                UnityEngine.Object.DestroyImmediate(layer);
            }
        }

        private static string Build(GameObject root, Vector3 position)
        {
            var type = Type.GetType("Game.TerrainDiagnosticReport, Game");
            Assert.That(type, Is.Not.Null);
            return (string)type.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { root, (Vector3?)position });
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class HeJiaVillageTerrainAssetTests
    {
        private const string SourceRoot = "Assets/Game/MapTerrainSources/HeJiaVillage";
        private const string TexturePath = SourceRoot + "/HeJiaVillage_Heightmap_RGB48.png";
        private const string MetadataPath = SourceRoot + "/HeJiaVillage_Heightmap.metadata.json";
        private const string ProfilePath = SourceRoot + "/HeJiaVillage_TerrainProfile.asset";
        private const string SurfaceControlPath =
            SourceRoot + "/FeatureReferences/HeJiaVillage_SurfaceControl.png";
        private static readonly string[] SurfaceLayerPaths =
        {
            SourceRoot + "/TerrainLayers/HeJiaVillage_Sand.terrainlayer",
            SourceRoot + "/TerrainLayers/HeJiaVillage_Vegetation.terrainlayer",
            SourceRoot + "/TerrainLayers/HeJiaVillage_Dirt.terrainlayer",
            SourceRoot + "/TerrainLayers/HeJiaVillage_Rock.terrainlayer"
        };
        private const float TextureTolerance = 1.5f / 65535f;
        private const float TerrainTolerance = 2f / 65535f;
        private const float SeamTolerance = 1f / 65535f;

        private JObject metadata;
        private Texture2D texture;

        [SetUp]
        public void SetUp()
        {
            var absoluteMetadataPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", MetadataPath));
            Assert.That(File.Exists(absoluteMetadataPath), Is.True, "缺少高度图 metadata");
            metadata = JObject.Parse(File.ReadAllText(absoluteMetadataPath));
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Assert.That(texture, Is.Not.Null, "高度图没有成功导入 Unity");
        }

        [Test]
        public void ImportedTexturePreservesRgb48PrecisionAndRequiredSettings()
        {
            Assert.That(texture.width, Is.EqualTo(1025));
            Assert.That(texture.height, Is.EqualTo(1025));
            Assert.That(texture.format,
                Is.EqualTo(TextureFormat.RGB48).Or.EqualTo(TextureFormat.RGBA64));
            Assert.That(texture.isReadable, Is.True);

            var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Default));
            Assert.That(importer.textureShape, Is.EqualTo(TextureImporterShape.Texture2D));
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.isReadable, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
            Assert.That(importer.maxTextureSize, Is.GreaterThanOrEqualTo(2048));
        }

        [Test]
        public void ProfileMatchesFixedGenerationConfiguration()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ProfilePath);
            Assert.That(profile, Is.Not.Null, "缺少地形生成配置");
            var serialized = new SerializedObject(profile);
            Assert.That(serialized.FindProperty("mapName").stringValue, Is.EqualTo("HeJiaVillage"));
            Assert.That(serialized.FindProperty("heightmap").objectReferenceValue, Is.SameAs(texture));
            Assert.That(serialized.FindProperty("rows").intValue, Is.EqualTo(2));
            Assert.That(serialized.FindProperty("columns").intValue, Is.EqualTo(2));
            Assert.That(serialized.FindProperty("tileWorldSize").floatValue, Is.EqualTo(1024f));
            Assert.That(serialized.FindProperty("maxTerrainHeight").floatValue,
                Is.EqualTo((float)metadata["elevationEncoding"]!["terrainHeightRange"]!));
            Assert.That(serialized.FindProperty("heightmapResolution").intValue, Is.EqualTo(513));
            Assert.That(serialized.FindProperty("detailSeed").intValue, Is.Zero);
            Assert.That(serialized.FindProperty("detailFrequency").floatValue, Is.EqualTo(4f));
            Assert.That(serialized.FindProperty("detailAmplitude").floatValue, Is.Zero);
            Assert.That(serialized.FindProperty("detailOctaves").intValue, Is.EqualTo(3));
            Assert.That(serialized.FindProperty("alphamapResolution").intValue, Is.EqualTo(256));
            var surfaceControl = AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceControlPath);
            Assert.That(surfaceControl, Is.Not.Null, "缺少现实地表控制图");
            Assert.That(serialized.FindProperty("surfaceControlMap").objectReferenceValue,
                Is.SameAs(surfaceControl));
            var importer = AssetImporter.GetAtPath(SurfaceControlPath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.isReadable, Is.True);
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
            Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            var layers = serialized.FindProperty("surfaceLayers");
            Assert.That(layers.arraySize, Is.EqualTo(SurfaceLayerPaths.Length));
            var roles = new[] { 1, 2, 3, 4 };
            for (var index = 0; index < SurfaceLayerPaths.Length; index++)
            {
                var rule = layers.GetArrayElementAtIndex(index);
                Assert.That(rule.FindPropertyRelative("layer").objectReferenceValue,
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<TerrainLayer>(SurfaceLayerPaths[index])));
                Assert.That(rule.FindPropertyRelative("role").enumValueIndex, Is.EqualTo(roles[index]));
            }
        }

        [Test]
        public void MetadataControlPointsMatchImportedTextureAndGeneratedTerrain()
        {
            var points = (JArray)metadata["controlPoints"]!;
            Assert.That(points.Count, Is.EqualTo(25));
            foreach (var point in points.Cast<JObject>())
            {
                var row = (int)point["row"]!;
                var column = (int)point["column"]!;
                var expected = (int)point["gray16"]! / 65535f;
                var actualTexture = texture.GetPixel(column, 1024 - row).grayscale;
                Assert.That(actualTexture, Is.EqualTo(expected).Within(TextureTolerance),
                    $"纹理控制点不匹配 row={row}, column={column}");

                var globalY = 1024 - row;
                var tileRow = Math.Min(globalY / 512, 1) + 1;
                var tileColumn = Math.Min(column / 512, 1) + 1;
                var localY = globalY - (tileRow - 1) * 512;
                var localX = column - (tileColumn - 1) * 512;
                var data = LoadTerrainData(tileRow, tileColumn);
                var actualTerrain = data.GetHeights(localX, localY, 1, 1)[0, 0];
                Assert.That(actualTerrain, Is.EqualTo(expected).Within(TerrainTolerance),
                    $"Terrain 控制点不匹配 row={row}, column={column}");
            }
        }

        [Test]
        public void TerrainAssetsHaveFixedDimensionsAndAllInternalSeamsMatch()
        {
            var d11 = LoadTerrainData(1, 1);
            var d12 = LoadTerrainData(1, 2);
            var d21 = LoadTerrainData(2, 1);
            var d22 = LoadTerrainData(2, 2);
            foreach (var data in new[] { d11, d12, d21, d22 })
            {
                Assert.That(data.heightmapResolution, Is.EqualTo(513));
                Assert.That(data.size, Is.EqualTo(new Vector3(1024f, 330f, 1024f)));
                Assert.That(data.alphamapResolution, Is.EqualTo(256));
                Assert.That(data.alphamapLayers, Is.EqualTo(SurfaceLayerPaths.Length));
                Assert.That(data.terrainLayers, Is.EqualTo(SurfaceLayerPaths
                    .Select(AssetDatabase.LoadAssetAtPath<TerrainLayer>)));
            }

            CompareVerticalSeam("1_1 east / 1_2 west", d11, d12);
            CompareVerticalSeam("2_1 east / 2_2 west", d21, d22);
            CompareHorizontalSeam("1_1 north / 2_1 south", d11, d21);
            CompareHorizontalSeam("1_2 north / 2_2 south", d12, d22);
            CompareVerticalAlphamapSeam("1_1 east / 1_2 west", d11, d12);
            CompareVerticalAlphamapSeam("2_1 east / 2_2 west", d21, d22);
            CompareHorizontalAlphamapSeam("1_1 north / 2_1 south", d11, d21);
            CompareHorizontalAlphamapSeam("1_2 north / 2_2 south", d12, d22);
        }

        private static TerrainData LoadTerrainData(int row, int column)
        {
            var path = $"{SourceRoot}/TerrainData/HeJiaVillage_{row}_{column}.asset";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            Assert.That(data, Is.Not.Null, "缺少 TerrainData " + path);
            return data;
        }

        private static void CompareVerticalSeam(string label, TerrainData west, TerrainData east)
        {
            var westValues = west.GetHeights(512, 0, 1, 513);
            var eastValues = east.GetHeights(0, 0, 1, 513);
            for (var i = 0; i < 513; i++)
                Assert.That(westValues[i, 0], Is.EqualTo(eastValues[i, 0]).Within(SeamTolerance),
                    $"{label}, index={i}, west={westValues[i, 0]}, east={eastValues[i, 0]}");
        }

        private static void CompareHorizontalSeam(string label, TerrainData south, TerrainData north)
        {
            var southValues = south.GetHeights(0, 512, 513, 1);
            var northValues = north.GetHeights(0, 0, 513, 1);
            for (var i = 0; i < 513; i++)
                Assert.That(southValues[0, i], Is.EqualTo(northValues[0, i]).Within(SeamTolerance),
                    $"{label}, index={i}, south={southValues[0, i]}, north={northValues[0, i]}");
        }

        private static void CompareVerticalAlphamapSeam(string label, TerrainData west, TerrainData east)
        {
            var resolution = west.alphamapResolution;
            var westValues = west.GetAlphamaps(resolution - 1, 0, 1, resolution);
            var eastValues = east.GetAlphamaps(0, 0, 1, resolution);
            for (var y = 0; y < resolution; y++)
            for (var layer = 0; layer < west.alphamapLayers; layer++)
                Assert.That(westValues[y, 0, layer], Is.EqualTo(eastValues[y, 0, layer]).Within(0.01f),
                    $"{label}, y={y}, layer={layer}");
        }

        private static void CompareHorizontalAlphamapSeam(string label, TerrainData south, TerrainData north)
        {
            var resolution = south.alphamapResolution;
            var southValues = south.GetAlphamaps(0, resolution - 1, resolution, 1);
            var northValues = north.GetAlphamaps(0, 0, resolution, 1);
            for (var x = 0; x < resolution; x++)
            for (var layer = 0; layer < south.alphamapLayers; layer++)
                Assert.That(southValues[0, x, layer], Is.EqualTo(northValues[0, x, layer]).Within(0.01f),
                    $"{label}, x={x}, layer={layer}");
        }
    }
}

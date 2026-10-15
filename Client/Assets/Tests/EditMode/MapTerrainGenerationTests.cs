using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    public sealed class MapTerrainGenerationTests
    {
        private const string TempParent = "Assets/Tests/Temp";
        private const string HostScenePath = TempParent + "/MapTerrainGenerationHost.unity";
        private const string TempRoot = "Assets/Tests/Temp/MapTerrainGeneration";
        private const string OutputRoot = TempRoot + "/Output";
        private const string InputsRoot = TempRoot + "/Inputs";
        private Type generatorType;
        private Type profileType;
        private Type windowType;
        private List<SceneState> originalScenes;
        private bool isolationControlAcquired;
        private bool tempParentExistedBeforeClass;
        private bool hostSceneOwned;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            tempParentExistedBeforeClass = AssetDatabase.IsValidFolder(TempParent) ||
                                           Directory.Exists(ToAbsoluteAssetPath(TempParent));
            var isolatedScene = SceneManager.GetActiveScene();
            AssertCleanEmptyUntitledScene(isolatedScene, "地图地形测试未取得可安全接管的命名空间级隔离场景");
            if (AssetDatabase.LoadMainAssetAtPath(HostScenePath) != null || File.Exists(ToAbsoluteAssetPath(HostScenePath)))
                throw new InvalidOperationException("地图地形测试宿主场景路径已存在，拒绝覆盖：" + HostScenePath);

            try
            {
                EditModeSceneIsolation.Exit();
                isolationControlAcquired = true;
                var hostScene = SceneManager.GetActiveScene();
                AssertCleanEmptyUntitledScene(hostScene, "释放命名空间级隔离后的宿主候选场景不安全");
                EnsureFolder(TempParent);
                if (!EditorSceneManager.SaveScene(hostScene, HostScenePath, false))
                    throw new InvalidOperationException("保存地图地形测试宿主场景失败：" + HostScenePath);
                hostSceneOwned = true;
                var savedHost = SceneManager.GetActiveScene();
                if (!savedHost.IsValid() || !savedHost.isLoaded ||
                    !string.Equals(savedHost.path, HostScenePath, StringComparison.Ordinal) || savedHost.isDirty)
                    throw new InvalidOperationException("地图地形测试宿主场景保存后状态无效：" + HostScenePath);
            }
            catch (Exception exception)
            {
                if (!hostSceneOwned && (AssetDatabase.LoadMainAssetAtPath(HostScenePath) != null ||
                                        File.Exists(ToAbsoluteAssetPath(HostScenePath)))) hostSceneOwned = true;
                var cleanupErrors = CleanupClassHostAndReturnIsolation();
                if (cleanupErrors.Count == 0) throw;
                throw new AggregateException("地图地形测试类级准备失败，且回滚存在错误。",
                    new[] { exception }.Concat(cleanupErrors));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            var cleanupErrors = CleanupClassHostAndReturnIsolation();
            if (cleanupErrors.Count > 0)
                throw new AggregateException("地图地形测试类级清理存在错误。", cleanupErrors);
        }

        [SetUp]
        public void SetUp()
        {
            originalScenes = CaptureScenes();
            generatorType = RequireType("Game.MapTerrainGenerator, Assembly-CSharp-Editor");
            profileType = RequireType("Game.MapTerrainGenerationProfile, Assembly-CSharp-Editor");
            windowType = RequireType("Game.MapTerrainGeneratorWindow, Assembly-CSharp-Editor");
            DeleteIfPresent(TempRoot);
            EnsureFolder(InputsRoot);
            EnsureFolder(OutputRoot);
            SetStatic("outputRootOverride", OutputRoot);
            ClearFaults();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (generatorType != null)
                {
                    SetStatic("outputRootOverride", null);
                    ClearFaults();
                }
                RestoreOriginalScenes();
                DeleteIfPresent(TempRoot);
                AssetDatabase.Refresh();
            }
            finally
            {
                RestoreOriginalScenes();
                if (originalScenes != null) AssertScenes(originalScenes);
            }
        }

        [Test]
        public void InvalidProfile_DoesNotWriteAssets()
        {
            var before = CaptureScenes();
            var texture = CreateHeightmap("invalid-height");
            var cases = new[] { "", "CON", "has space", "bad/name", "bad\\name", "e\u0301" };
            foreach (var mapName in cases)
            {
                var profile = CreateProfile("invalid-" + Guid.NewGuid().ToString("N"), texture, mapName, 1, 1);
                AssertInvalidProfile(profile, "非法地图名：" + mapName);
            }

            AssertInvalidProfile(CreateProfile("null-height", texture, "空高度图", 1, 1), "heightmap=null",
                serialized => serialized.FindProperty("heightmap").objectReferenceValue = null);
            AssertInvalidProfile(CreateProfile("unreadable-profile", texture, "不可读高度图", 1, 1), "heightmap 不可读",
                serialized => serialized.FindProperty("heightmap").objectReferenceValue = CreateUnreadableHeightmap("unreadable-height"));
            AssertInvalidProfile(CreateProfile("width-profile", texture, "窄高度图", 1, 1), "heightmap width=1",
                serialized => serialized.FindProperty("heightmap").objectReferenceValue = CreateSizedHeightmap("width-one", 1, 2));
            AssertInvalidProfile(CreateProfile("height-profile", texture, "矮高度图", 1, 1), "heightmap height=1",
                serialized => serialized.FindProperty("heightmap").objectReferenceValue = CreateSizedHeightmap("height-one", 2, 1));
            AssertInvalidField(texture, "rows-zero", "行数零", "rows", 0);
            AssertInvalidField(texture, "rows-nine", "行数九", "rows", 9);
            AssertInvalidField(texture, "columns-zero", "列数零", "columns", 0);
            AssertInvalidField(texture, "columns-nine", "列数九", "columns", 9);
            AssertInvalidFloatField(texture, "tile-size-zero", "块尺寸零", "tileWorldSize", 0f);
            AssertInvalidFloatField(texture, "height-zero", "最大高度零", "maxTerrainHeight", 0f);
            AssertInvalidField(texture, "resolution-invalid", "分辨率非法", "heightmapResolution", 32);
            AssertInvalidFloatField(texture, "frequency-zero", "频率零", "detailFrequency", 0f);
            AssertInvalidFloatField(texture, "amplitude-low", "振幅过低", "detailAmplitude", -0.01f);
            AssertInvalidFloatField(texture, "amplitude-high", "振幅过高", "detailAmplitude", 1.01f);
            AssertInvalidField(texture, "octaves-zero", "倍频零", "detailOctaves", 0);
            AssertInvalidField(texture, "octaves-seven", "倍频七", "detailOctaves", 7);

            var chinese = CreateProfile("valid-chinese", texture, "长安地区", 1, 1);
            Assert.That(Get<bool>(Invoke("ValidateProfile", chinese), "Success"), Is.True);
            Assert.That(AssetDatabase.FindAssets(string.Empty, new[] { OutputRoot }), Is.Empty);
            AssertScenes(before);
        }

        [Test]
        public void InternalTexelCentersMapExactlyToGeneratedTerrainData()
        {
            var before = CaptureScenes();
            const int width = 5;
            const int height = 3;
            var columnValues = new[] { 0.05f, 0.85f, 0.15f, 0.65f, 0.95f };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = columnValues[x];
                pixels[y * width + x] = new Color(value, value, value, 1f);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "texel-centers",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, InputsRoot + "/texel-centers.asset");

            const string mapName = "纹理中心映射";
            var profile = CreateProfile("texel-center-profile", texture, mapName, 1, 1);
            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.True, Get<string>(result, "Error"));
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(
                OutputRoot + "/" + mapName + "/TerrainData/" + mapName + "_1_1.asset");
            Assert.That(data, Is.Not.Null);

            const float tolerance = 1f / 65535f;
            Assert.That(data.GetHeight(0, 16) / data.size.y,
                Is.EqualTo(texture.GetPixel(0, 1).grayscale).Within(tolerance), "首索引未命中首 texel 中心");
            Assert.That(data.GetHeight(8, 16) / data.size.y,
                Is.EqualTo(texture.GetPixel(1, 1).grayscale).Within(tolerance), "内部索引被相邻 texel 插值污染");
            Assert.That(data.GetHeight(16, 16) / data.size.y,
                Is.EqualTo(texture.GetPixel(2, 1).grayscale).Within(tolerance), "中心索引未命中同索引 texel 中心");
            Assert.That(data.GetHeight(32, 16) / data.size.y,
                Is.EqualTo(texture.GetPixel(4, 1).grayscale).Within(tolerance), "末索引未命中末 texel 中心");
            AssertScenes(before);
        }

        [Test]
        public void GenerateTwoByTwo_CreatesChineseNamedScenesAndMatchingBorders()
        {
            var before = CaptureScenes();
            var texture = CreateHeightmap("corners");
            var heightmapAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(InputsRoot + "/corners.asset");
            Assert.That(heightmapAsset, Is.Not.Null);
            Assert.That(heightmapAsset.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            var profile = CreateProfile("two-by-two", texture, "关中", 2, 2);
            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.True, Get<string>(result, "Error"));
            CollectionAssert.AreEqual(new[]
            {
                OutputRoot + "/关中/Scenes/关中_1_1.unity", OutputRoot + "/关中/Scenes/关中_1_2.unity",
                OutputRoot + "/关中/Scenes/关中_2_1.unity", OutputRoot + "/关中/Scenes/关中_2_2.unity"
            }, GetStrings(result, "ScenePaths"));

            var tiles = new TerrainData[2, 2];
            for (var row = 1; row <= 2; row++)
            for (var column = 1; column <= 2; column++)
            {
                var name = "关中_" + row + "_" + column;
                var dataPath = OutputRoot + "/关中/TerrainData/" + name + ".asset";
                var scenePath = OutputRoot + "/关中/Scenes/" + name + ".unity";
                tiles[row - 1, column - 1] = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
                Assert.That(tiles[row - 1, column - 1], Is.Not.Null);
                InspectGeneratedScene(scenePath, row, column, 2, 2, tiles[row - 1, column - 1]);
            }

            AssertHorizontalBorder(tiles[0, 0], tiles[0, 1]);
            AssertHorizontalBorder(tiles[1, 0], tiles[1, 1]);
            AssertVerticalBorder(tiles[0, 0], tiles[1, 0]);
            AssertVerticalBorder(tiles[0, 1], tiles[1, 1]);
            Assert.That(tiles[0, 0].GetHeight(0, 0) / tiles[0, 0].size.y, Is.EqualTo(Color.red.grayscale).Within(0.0001f));
            Assert.That(tiles[0, 1].GetHeight(tiles[0, 1].heightmapResolution - 1, 0) / tiles[0, 1].size.y,
                Is.EqualTo(Color.green.grayscale).Within(0.0001f));
            Assert.That(tiles[1, 0].GetHeight(0, tiles[1, 0].heightmapResolution - 1) / tiles[1, 0].size.y,
                Is.EqualTo(Color.blue.grayscale).Within(0.0001f));
            Assert.That(tiles[1, 1].GetHeight(tiles[1, 1].heightmapResolution - 1, tiles[1, 1].heightmapResolution - 1) / tiles[1, 1].size.y,
                Is.EqualTo(Color.white.grayscale).Within(0.0001f));

            AssertDetailFormula(texture, 7, 3.25f, 0.35f, 4);
            AssertDetailFormula(texture, 19, 3.25f, 0.35f, 4);
            AssertDetailFormula(texture, 7, 7.5f, 0.35f, 4);
            AssertDetailFormula(texture, 7, 3.25f, 0.55f, 4);
            AssertDetailFormula(texture, 7, 3.25f, 0.35f, 2);
            AssertScenes(before);
        }

        [Test]
        public void GenerateWithSurfaceLayers_CreatesNormalizedMatchingAlphamapBorders()
        {
            var before = CaptureScenes();
            var texture = CreateHeightmap("surface-height");
            var low = new TerrainLayer { name = "Low" };
            var high = new TerrainLayer { name = "High" };
            AssetDatabase.CreateAsset(low, InputsRoot + "/surface-low.terrainlayer");
            AssetDatabase.CreateAsset(high, InputsRoot + "/surface-high.terrainlayer");
            var profile = CreateProfile("surface-profile", texture, "表面地图", 1, 2);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("alphamapResolution").intValue = 16;
            var layers = serialized.FindProperty("surfaceLayers");
            layers.arraySize = 2;
            SetSurfaceRule(layers.GetArrayElementAtIndex(0), low, 0.15f, 0.6f);
            SetSurfaceRule(layers.GetArrayElementAtIndex(1), high, 0.85f, 0.6f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.True, Get<string>(result, "Error"));
            var left = AssetDatabase.LoadAssetAtPath<TerrainData>(
                OutputRoot + "/表面地图/TerrainData/表面地图_1_1.asset");
            var right = AssetDatabase.LoadAssetAtPath<TerrainData>(
                OutputRoot + "/表面地图/TerrainData/表面地图_1_2.asset");
            foreach (var data in new[] { left, right })
            {
                Assert.That(data.terrainLayers, Is.EqualTo(new[] { low, high }));
                Assert.That(data.alphamapResolution, Is.EqualTo(16));
                Assert.That(data.alphamapLayers, Is.EqualTo(2));
                var values = data.GetAlphamaps(0, 0, 16, 16);
                for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    Assert.That(values[y, x, 0] + values[y, x, 1], Is.EqualTo(1f).Within(0.005f));
            }

            var leftBorder = left.GetAlphamaps(15, 0, 1, 16);
            var rightBorder = right.GetAlphamaps(0, 0, 1, 16);
            for (var y = 0; y < 16; y++)
            for (var layer = 0; layer < 2; layer++)
                Assert.That(leftBorder[y, 0, layer], Is.EqualTo(rightBorder[y, 0, layer]).Within(0.001f));

            AssertScenes(before);
        }

        [Test]
        public void GenerateWithSurfaceControl_UsesLandCoverAndRoadMaskWithoutChangingHeight()
        {
            var before = CaptureScenes();
            var heightmap = CreateSizedHeightmap("surface-control-height", 2, 2);
            var control = CreateSurfaceControl("surface-control");
            var layers = Enumerable.Range(0, 4).Select(index => new TerrainLayer
            {
                name = "Control " + index,
                diffuseTexture = control
            }).ToArray();
            for (var index = 0; index < layers.Length; index++)
                AssetDatabase.CreateAsset(layers[index], InputsRoot + "/surface-control-" + index + ".terrainlayer");
            var profile = CreateProfile("surface-control-profile", heightmap, "控制图地图", 1, 1);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("alphamapResolution").intValue = 16;
            serialized.FindProperty("surfaceControlMap").objectReferenceValue = control;
            var rules = serialized.FindProperty("surfaceLayers");
            rules.arraySize = 4;
            SetSurfaceRule(rules.GetArrayElementAtIndex(0), layers[0], 0.05f, 0.5f, 1);
            SetSurfaceRule(rules.GetArrayElementAtIndex(1), layers[1], 0.2f, 0.5f, 2);
            SetSurfaceRule(rules.GetArrayElementAtIndex(2), layers[2], 0.45f, 0.5f, 3);
            SetSurfaceRule(rules.GetArrayElementAtIndex(3), layers[3], 0.8f, 0.5f, 4);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Assert.That(Mathf.RoundToInt(control.GetPixel(0, 0).r * 255f), Is.EqualTo(40),
                "测试控制图没有保留 WorldCover 分类值");
            serialized.Update();
            for (var index = 0; index < 4; index++)
                Assert.That(serialized.FindProperty("surfaceLayers").GetArrayElementAtIndex(index)
                    .FindPropertyRelative("role").enumValueIndex, Is.EqualTo(index + 1));

            var buildWeights = generatorType.GetMethod("BuildSurfaceWeights",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(buildWeights, Is.Not.Null);
            var profileRules = profile.GetType().GetProperty("SurfaceLayers")!.GetValue(profile);
            var sourceHeights = new float[33, 33];
            for (var y = 0; y < 33; y++)
            for (var x = 0; x < 33; x++) sourceHeights[y, x] = 1f;
            var calculated = (float[,,])buildWeights.Invoke(null,
                new[] { profile, profileRules, 16, 1, 1, sourceHeights });
            Assert.That(calculated[8, 2, 1], Is.GreaterThan(calculated[8, 2, 2]),
                "土地覆盖未提高植被层权重");
            Assert.That(calculated[8, 13, 2], Is.GreaterThan(calculated[8, 13, 1]),
                "道路遮罩未覆盖为泥土层");

            var generated = Invoke("Generate", profile);
            Assert.That(Get<bool>(generated, "Success"), Is.True, Get<string>(generated, "Error"));
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(
                OutputRoot + "/控制图地图/TerrainData/控制图地图_1_1.asset");
            Assert.That(data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution)
                    .Cast<float>().All(value => Mathf.Approximately(value, 1f)), Is.True,
                "地表控制不应改变高度图");
            AssertScenes(before);
        }

        [Test]
        public void RefreshSurface_OnlyChangesTerrainLayersAndAlphamaps()
        {
            var before = CaptureScenes();
            var texture = CreateHeightmap("refresh-surface-height");
            var low = new TerrainLayer { name = "Refresh Low" };
            var high = new TerrainLayer { name = "Refresh High" };
            AssetDatabase.CreateAsset(low, InputsRoot + "/refresh-surface-low.terrainlayer");
            AssetDatabase.CreateAsset(high, InputsRoot + "/refresh-surface-high.terrainlayer");
            var profile = CreateProfile("refresh-surface-profile", texture, "刷新表面地图", 1, 2);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("alphamapResolution").intValue = 16;
            var rules = serialized.FindProperty("surfaceLayers");
            rules.arraySize = 2;
            SetSurfaceRule(rules.GetArrayElementAtIndex(0), low, 0.15f, 0.6f);
            SetSurfaceRule(rules.GetArrayElementAtIndex(1), high, 0.85f, 0.6f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            var generated = Invoke("Generate", profile);
            Assert.That(Get<bool>(generated, "Success"), Is.True, Get<string>(generated, "Error"));

            var mapRoot = OutputRoot + "/刷新表面地图";
            var left = AssetDatabase.LoadAssetAtPath<TerrainData>(mapRoot + "/TerrainData/刷新表面地图_1_1.asset");
            var right = AssetDatabase.LoadAssetAtPath<TerrainData>(mapRoot + "/TerrainData/刷新表面地图_1_2.asset");
            var leftHeights = left.GetHeights(0, 0, left.heightmapResolution, left.heightmapResolution);
            var rightHeights = right.GetHeights(0, 0, right.heightmapResolution, right.heightmapResolution);
            var leftAlphamaps = left.GetAlphamaps(0, 0, 16, 16);
            var sceneBytes = new[]
            {
                File.ReadAllBytes(mapRoot + "/Scenes/刷新表面地图_1_1.unity"),
                File.ReadAllBytes(mapRoot + "/Scenes/刷新表面地图_1_2.unity")
            };

            serialized.Update();
            rules = serialized.FindProperty("surfaceLayers");
            rules.GetArrayElementAtIndex(0).FindPropertyRelative("centerHeight").floatValue = 0.75f;
            rules.GetArrayElementAtIndex(1).FindPropertyRelative("centerHeight").floatValue = 0.25f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var refreshed = Invoke("RefreshSurface", profile);

            Assert.That(Get<bool>(refreshed, "Success"), Is.True, Get<string>(refreshed, "Error"));
            Assert.That(Get<int>(refreshed, "UpdatedCount"), Is.EqualTo(2));
            CollectionAssert.AreEqual(leftHeights,
                left.GetHeights(0, 0, left.heightmapResolution, left.heightmapResolution));
            CollectionAssert.AreEqual(rightHeights,
                right.GetHeights(0, 0, right.heightmapResolution, right.heightmapResolution));
            CollectionAssert.AreEqual(sceneBytes[0],
                File.ReadAllBytes(mapRoot + "/Scenes/刷新表面地图_1_1.unity"));
            CollectionAssert.AreEqual(sceneBytes[1],
                File.ReadAllBytes(mapRoot + "/Scenes/刷新表面地图_1_2.unity"));
            Assert.That(Flatten(left.GetAlphamaps(0, 0, 16, 16)),
                Is.Not.EqualTo(Flatten(leftAlphamaps)));
            var leftBorder = left.GetAlphamaps(15, 0, 1, 16);
            var rightBorder = right.GetAlphamaps(0, 0, 1, 16);
            for (var y = 0; y < 16; y++)
            for (var layer = 0; layer < 2; layer++)
                Assert.That(leftBorder[y, 0, layer], Is.EqualTo(rightBorder[y, 0, layer]).Within(0.001f));

            var leftBeforeFailure = Flatten(left.GetAlphamaps(0, 0, 16, 16));
            var rightBeforeFailure = Flatten(right.GetAlphamaps(0, 0, 16, 16));
            serialized.Update();
            rules = serialized.FindProperty("surfaceLayers");
            rules.GetArrayElementAtIndex(0).FindPropertyRelative("centerHeight").floatValue = 0.1f;
            rules.GetArrayElementAtIndex(1).FindPropertyRelative("centerHeight").floatValue = 0.9f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetStatic("testSurfaceRefreshExceptionCallback", new Action<int>(updated =>
            {
                if (updated == 1) throw new InvalidOperationException("刷新回滚注入异常");
            }));
            var failed = Invoke("RefreshSurface", profile);
            Assert.That(Get<bool>(failed, "Success"), Is.False);
            Assert.That(Get<string>(failed, "Error"), Does.Contain("刷新回滚注入异常"));
            CollectionAssert.AreEqual(leftBeforeFailure, Flatten(left.GetAlphamaps(0, 0, 16, 16)));
            CollectionAssert.AreEqual(rightBeforeFailure, Flatten(right.GetAlphamaps(0, 0, 16, 16)));
            AssertScenes(before);
        }

        [Test]
        public void FreshGeneration_IsDeterministicAfterTestCleanup()
        {
            var before = CaptureScenes();
            var texture = CreateHeightmap("deterministic-height");
            var profile = CreateProfile("deterministic-profile", texture, "重复地图", 2, 2, 9, 4.75f, 0.4f, 3);
            Assert.That(Get<bool>(Invoke("Generate", profile), "Success"), Is.True);
            var first = CaptureAllHeights("重复地图", 2, 2);
            Assert.That(AssetDatabase.DeleteAsset(OutputRoot + "/重复地图"), Is.True);
            AssetDatabase.Refresh();
            Assert.That(Get<bool>(Invoke("Generate", profile), "Success"), Is.True);
            var second = CaptureAllHeights("重复地图", 2, 2);
            CollectionAssert.AreEqual(first, second);
            AssertScenes(before);
        }

        [Test]
        public void ExistingMap_StopsBeforeAnyWriteAndPreservesAllAuthoredObjects()
        {
            var before = CaptureScenes();
            var texture = CreateHeightmap("protected-height");
            var profile = CreateProfile("protected-profile", texture, "受保护地图", 1, 1);
            EnsureFolder(OutputRoot + "/受保护地图");
            var authoredPath = OutputRoot + "/受保护地图/Authored.unity";
            var originalActive = SceneManager.GetActiveScene();
            var authoredScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var first = new GameObject("AuthoredRootA");
            var second = new GameObject("AuthoredRootB");
            SceneManager.MoveGameObjectToScene(first, authoredScene);
            SceneManager.MoveGameObjectToScene(second, authoredScene);
            first.transform.localPosition = new Vector3(3f, 4f, 5f);
            second.transform.localRotation = Quaternion.Euler(10f, 20f, 30f);
            var firstChild = new GameObject("AuthoredChildA");
            var secondChild = new GameObject("AuthoredChildB");
            firstChild.transform.SetParent(first.transform, false);
            secondChild.transform.SetParent(second.transform, false);
            firstChild.transform.localScale = new Vector3(2f, 3f, 4f);
            secondChild.transform.localPosition = new Vector3(7f, 8f, 9f);
            first.AddComponent<MapTerrainProtectedReference>().Other = secondChild;
            second.AddComponent<MapTerrainProtectedReference>().Other = firstChild;
            Assert.That(EditorSceneManager.SaveScene(authoredScene, authoredPath), Is.True);
            EditorSceneManager.CloseScene(authoredScene, true);
            SceneManager.SetActiveScene(originalActive);
            var guid = AssetDatabase.AssetPathToGUID(authoredPath);
            var bytes = File.ReadAllBytes(authoredPath);
            var metaBytes = File.ReadAllBytes(authoredPath + ".meta");

            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.False);
            Assert.That(Get<string>(result, "Error"), Does.Contain(OutputRoot + "/受保护地图"));
            Assert.That(AssetDatabase.AssetPathToGUID(authoredPath), Is.EqualTo(guid));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(authoredPath));
            CollectionAssert.AreEqual(metaBytes, File.ReadAllBytes(authoredPath + ".meta"));
            var reopened = EditorSceneManager.OpenScene(authoredPath, OpenSceneMode.Additive);
            try
            {
                var roots = reopened.GetRootGameObjects().OrderBy(item => item.name).ToArray();
                Assert.That(roots.Select(item => item.name), Is.EqualTo(new[] { "AuthoredRootA", "AuthoredRootB" }));
                Assert.That(roots[0].transform.localPosition, Is.EqualTo(new Vector3(3f, 4f, 5f)));
                Assert.That(roots[0].transform.GetChild(0).localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
                Assert.That(roots[1].transform.GetChild(0).localPosition, Is.EqualTo(new Vector3(7f, 8f, 9f)));
                Assert.That(roots[0].GetComponent<MapTerrainProtectedReference>().Other,
                    Is.EqualTo(roots[1].transform.GetChild(0).gameObject));
                Assert.That(roots[1].GetComponent<MapTerrainProtectedReference>().Other,
                    Is.EqualTo(roots[0].transform.GetChild(0).gameObject));
            }
            finally { EditorSceneManager.CloseScene(reopened, true); }
            AssertScenes(before);
        }

        [Test]
        public void Preflight_PreservesOpenScenesAndDirtyState()
        {
            var before = CaptureScenes();
            var originalActive = SceneManager.GetActiveScene();
            var dirtyScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                new GameObject("MapTerrainPreflightDirtySentinel");
                EditorSceneManager.MarkSceneDirty(dirtyScene);
                var dirtyBefore = CaptureScenes();
                var texture = CreateHeightmap("preflight-height");
                var profile = CreateProfile("preflight-profile", texture, "预检查地图", 2, 2);
                var inputGuid = AssetDatabase.AssetPathToGUID(InputsRoot + "/preflight-height.asset");
                var inputBytes = File.ReadAllBytes(InputsRoot + "/preflight-height.asset");
                var inputMetaBytes = File.ReadAllBytes(InputsRoot + "/preflight-height.asset.meta");
                var freshOutputBefore = CaptureAssetTree(OutputRoot);
                var freshStagingBefore = CaptureAssetTree(OutputRoot + "/__Staging");
                var fresh = Invoke("Preflight", profile);
                Assert.That(Get<bool>(fresh, "Success"), Is.True);
                Assert.That(Get<int>(fresh, "CreateCount"), Is.EqualTo(4));
                Assert.That(CaptureAssetTree(OutputRoot), Is.EqualTo(freshOutputBefore), "新地图预检查改变了输出根");
                Assert.That(CaptureAssetTree(OutputRoot + "/__Staging"), Is.EqualTo(freshStagingBefore), "新地图预检查改变了暂存根");
                EnsureFolder(OutputRoot + "/预检查地图");
                var sentinelPath = OutputRoot + "/预检查地图/Sentinel.asset";
                var sentinel = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                sentinel.SetPixels(new[] { Color.black, Color.red, Color.green, Color.blue });
                sentinel.Apply();
                AssetDatabase.CreateAsset(sentinel, sentinelPath);
                AssetDatabase.SaveAssets();
                var sentinelGuid = AssetDatabase.AssetPathToGUID(sentinelPath);
                var sentinelBytes = File.ReadAllBytes(sentinelPath);
                var sentinelMetaBytes = File.ReadAllBytes(sentinelPath + ".meta");
                var existingOutputBefore = CaptureAssetTree(OutputRoot);
                var existingStagingBefore = CaptureAssetTree(OutputRoot + "/__Staging");
                var existing = Invoke("Preflight", profile);
                Assert.That(Get<bool>(existing, "Success"), Is.False);
                Assert.That(CaptureAssetTree(OutputRoot), Is.EqualTo(existingOutputBefore), "同名地图预检查改变了输出根");
                Assert.That(CaptureAssetTree(OutputRoot + "/__Staging"), Is.EqualTo(existingStagingBefore), "同名地图预检查改变了暂存根");
                Assert.That(AssetDatabase.AssetPathToGUID(sentinelPath), Is.EqualTo(sentinelGuid));
                CollectionAssert.AreEqual(sentinelBytes, File.ReadAllBytes(sentinelPath));
                CollectionAssert.AreEqual(sentinelMetaBytes, File.ReadAllBytes(sentinelPath + ".meta"));
                Assert.That(AssetDatabase.AssetPathToGUID(InputsRoot + "/preflight-height.asset"), Is.EqualTo(inputGuid));
                CollectionAssert.AreEqual(inputBytes, File.ReadAllBytes(InputsRoot + "/preflight-height.asset"));
                CollectionAssert.AreEqual(inputMetaBytes, File.ReadAllBytes(InputsRoot + "/preflight-height.asset.meta"));
                AssertScenes(dirtyBefore);
            }
            finally
            {
                if (dirtyScene.IsValid() && dirtyScene.isLoaded) EditorSceneManager.CloseScene(dirtyScene, true);
                if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
            }
            AssertScenes(before);
        }

        [Test]
        public void InjectedException_DeletesAllNewAssetsAndRestoresOpenScenes()
        {
            var before = CaptureScenes();
            var profile = CreateProfile("exception-profile", CreateHeightmap("exception-height"), "异常地图", 2, 2);
            SetStatic("testExceptionCallback", new Action<object, int>((stage, saved) =>
            {
                if (stage.ToString() == "AfterTileSaved" && saved == 3) throw new InvalidOperationException("第三块注入异常");
            }));
            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.False);
            Assert.That(Get<string>(result, "Error"), Does.Contain("第三块注入异常"));
            Assert.That(AssetDatabase.IsValidFolder(OutputRoot + "/异常地图"), Is.False);
            AssertNoStagingAssets();
            AssertScenes(before);
        }

        [Test]
        public void InjectedNonExceptionFailure_DeletesAllNewAssetsAndRestoresOpenScenes()
        {
            var before = CaptureScenes();
            var profile = CreateProfile("failure-profile", CreateHeightmap("failure-height"), "返回失败地图", 1, 1);
            SetStatic("testFailureCallback", new Func<object, int, bool>((stage, _) =>
                stage.ToString() == "AfterFinalMoveBeforeValidation"));
            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.False);
            Assert.That(Get<string>(result, "Error"), Does.Contain("非异常失败"));
            Assert.That(AssetDatabase.IsValidFolder(OutputRoot + "/返回失败地图"), Is.False);
            AssertNoStagingAssets();
            AssertScenes(before);
        }

        [Test]
        public void InjectedCleanupFailures_ContinueAndReportAllUnremovedPaths()
        {
            var before = CaptureScenes();
            var profile = CreateProfile("cleanup-profile", CreateHeightmap("cleanup-height"), "清理失败地图", 1, 1);
            SetStatic("testFailureCallback", new Func<object, int, bool>((stage, _) =>
                stage.ToString() == "AfterFinalMoveBeforeValidation"));
            SetStatic("testCleanupFailureCallback", new Func<string, bool>(path =>
                path.EndsWith("/清理失败地图", StringComparison.Ordinal) || path.EndsWith("/__Staging", StringComparison.Ordinal)));
            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.False);
            var unremoved = GetStrings(result, "UnremovedPaths");
            Assert.That(unremoved, Has.Count.EqualTo(2));
            Assert.That(unremoved.Any(path => path.EndsWith("/清理失败地图", StringComparison.Ordinal)), Is.True);
            Assert.That(unremoved.Any(path => path.EndsWith("/__Staging", StringComparison.Ordinal)), Is.True);
            Assert.That(Get<string>(result, "Error"), Does.Contain("未能清理"));
            Assert.That(AssetDatabase.IsValidFolder(OutputRoot + "/清理失败地图"), Is.True);
            AssertScenes(before);
        }

        [Test]
        public void WindowBehavior_RejectsExistingMapAndGeneratesFreshMap()
        {
            var before = CaptureScenes();
            var menu = windowType.GetMethod("OpenWindow", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(menu, Is.Not.Null);
            var attribute = menu.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Single();
            Assert.That(attribute.menuItem, Is.EqualTo("Tools/地图/通用地形工具/地图地形生成"));
            var profile = CreateProfile("window-profile", CreateHeightmap("window-height"), "窗口地图", 1, 1);
            EnsureFolder(OutputRoot + "/窗口地图");
            var window = ScriptableObject.CreateInstance(windowType) as EditorWindow;
            try
            {
                SetInstance(window, "profile", profile);
                var rejected = InvokeInstance(window, "ExecutePreflight");
                Assert.That(Get<bool>(rejected, "Success"), Is.False);
                DeleteIfPresent(OutputRoot + "/窗口地图");
                var accepted = InvokeInstance(window, "ExecutePreflight");
                Assert.That(Get<bool>(accepted, "Success"), Is.True);
                Assert.That(Get<bool>(InvokeInstance(window, "CanGenerate"), "Value"), Is.True);
                var generated = InvokeInstance(window, "ExecuteGenerate");
                Assert.That(Get<bool>(generated, "Success"), Is.True, Get<string>(generated, "Error"));
                CollectionAssert.AreEqual(new[] { OutputRoot + "/窗口地图/Scenes/窗口地图_1_1.unity" },
                    GetStrings(generated, "ScenePaths"));
            }
            finally
            {
                if (window != null) UnityEngine.Object.DestroyImmediate(window);
            }
            AssertScenes(before);
        }

        private void AssertDetailFormula(Texture2D texture, int seed, float frequency, float amplitude, int octaves)
        {
            var mapName = "公式" + seed + "_" + frequency.ToString("0.##").Replace('.', '_') + "_" +
                          amplitude.ToString("0.##").Replace('.', '_') + "_" + octaves;
            var profile = CreateProfile("profile-" + Guid.NewGuid().ToString("N"), texture, mapName, 1, 1,
                seed, frequency, amplitude, octaves);
            var result = Invoke("Generate", profile);
            Assert.That(Get<bool>(result, "Success"), Is.True, Get<string>(result, "Error"));
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(OutputRoot + "/" + mapName + "/TerrainData/" + mapName + "_1_1.asset");
            const int sample = 11;
            var u = sample / 32f;
            var v = sample / 32f;
            var expected = ExpectedHeight(texture, u, v, seed, frequency, amplitude, octaves);
            Assert.That(data.GetHeight(sample, sample) / data.size.y, Is.EqualTo(expected).Within(0.0001f));
        }

        private void AssertInvalidProfile(ScriptableObject profile, string label, Action<SerializedObject> mutation = null)
        {
            if (mutation != null)
            {
                var serialized = new SerializedObject(profile);
                mutation(serialized);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var outputBefore = CaptureAssetTree(OutputRoot);
            var validation = Invoke("ValidateProfile", profile);
            Assert.That(Get<bool>(validation, "Success"), Is.False, label);
            Assert.That(Get<string>(validation, "Error"), Is.Not.Empty, label);
            var generation = Invoke("Generate", profile);
            Assert.That(Get<bool>(generation, "Success"), Is.False, label);
            Assert.That(Get<string>(generation, "Error"), Is.Not.Empty, label);
            Assert.That(CaptureAssetTree(OutputRoot), Is.EqualTo(outputBefore), label + " 改变了输出根");
        }

        private void AssertInvalidField(Texture2D texture, string assetName, string mapName, string field, int value)
        {
            AssertInvalidProfile(CreateProfile(assetName, texture, mapName, 1, 1), field + "=" + value,
                serialized => serialized.FindProperty(field).intValue = value);
        }

        private void AssertInvalidFloatField(Texture2D texture, string assetName, string mapName, string field, float value)
        {
            AssertInvalidProfile(CreateProfile(assetName, texture, mapName, 1, 1), field + "=" + value,
                serialized => serialized.FindProperty(field).floatValue = value);
        }

        private ScriptableObject CreateProfile(string assetName, Texture2D texture, string mapName, int rows, int columns,
            int seed = 7, float frequency = 3.25f, float amplitude = 0f, int octaves = 4)
        {
            var profile = ScriptableObject.CreateInstance(profileType);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("mapName").stringValue = mapName;
            serialized.FindProperty("heightmap").objectReferenceValue = texture;
            serialized.FindProperty("rows").intValue = rows;
            serialized.FindProperty("columns").intValue = columns;
            serialized.FindProperty("tileWorldSize").floatValue = 128f;
            serialized.FindProperty("maxTerrainHeight").floatValue = 256f;
            serialized.FindProperty("heightmapResolution").intValue = 33;
            serialized.FindProperty("detailSeed").intValue = seed;
            serialized.FindProperty("detailFrequency").floatValue = frequency;
            serialized.FindProperty("detailAmplitude").floatValue = amplitude;
            serialized.FindProperty("detailOctaves").intValue = octaves;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var path = InputsRoot + "/" + assetName + ".asset";
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static void SetSurfaceRule(SerializedProperty rule, TerrainLayer layer, float centerHeight,
            float blendWidth, int role = 0)
        {
            rule.FindPropertyRelative("layer").objectReferenceValue = layer;
            rule.FindPropertyRelative("role").enumValueIndex = role;
            rule.FindPropertyRelative("centerHeight").floatValue = centerHeight;
            rule.FindPropertyRelative("blendWidth").floatValue = blendWidth;
        }

        private static Texture2D CreateSurfaceControl(string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = name };
            var vegetation = new Color32(40, 200, 0, 0);
            var road = new Color32(40, 200, 0, 255);
            texture.SetPixels32(new[] { vegetation, road, vegetation, road });
            texture.Apply(false, false);
            var path = InputsRoot + "/" + name + ".png";
            File.WriteAllBytes(ToAbsoluteAssetPath(path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Texture2D CreateHeightmap(string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, InputsRoot + "/" + name + ".asset");
            return texture;
        }

        private static Texture2D CreateSizedHeightmap(string name, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) { name = name };
            texture.SetPixels(Enumerable.Repeat(Color.white, width * height).ToArray());
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, InputsRoot + "/" + name + ".asset");
            return texture;
        }

        private static Texture2D CreateUnreadableHeightmap(string name)
        {
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            source.SetPixels(new[] { Color.black, Color.red, Color.green, Color.white });
            source.Apply();
            var path = InputsRoot + "/" + name + ".png";
            File.WriteAllBytes(ToAbsoluteAssetPath(path), source.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(source);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static float ExpectedHeight(Texture2D texture, float u, float v, int seed, float frequency,
            float detailAmplitude, int octaves)
        {
            var sum = 0f;
            var weight = 0f;
            var octaveAmplitude = 1f;
            for (var i = 0; i < octaves; i++)
            {
                var n = Mathf.PerlinNoise(u * frequency + seed * 0.01337f + i * 17.123f,
                    v * frequency + seed * 0.01717f + i * 31.337f);
                sum += (n * 2f - 1f) * octaveAmplitude;
                weight += octaveAmplitude;
                frequency *= 2f;
                octaveAmplitude *= 0.5f;
            }
            var sampleU = u * (texture.width - 1) / texture.width;
            var sampleV = v * (texture.height - 1) / texture.height;
            return Mathf.Clamp01(texture.GetPixelBilinear(sampleU, sampleV).grayscale +
                                 sum / weight * detailAmplitude);
        }

        private static void InspectGeneratedScene(string path, int row, int column, int rows, int columns, TerrainData expected)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                Assert.That(roots.Length, Is.EqualTo(1));
                Assert.That(roots[0].name, Is.EqualTo("MapRoot"));
                var contexts = roots[0].GetComponentsInChildren<MapContext>(true);
                Assert.That(contexts, Has.Length.EqualTo(1));
                Assert.That(contexts[0].Validate(out var error), Is.True, error);
                var content = roots[0].transform.Find("MapContentRoot");
                Assert.That(content, Is.Not.Null);
                Assert.That(content.gameObject.activeSelf, Is.False);
                var generated = content.Find("GeneratedTerrainRoot");
                Assert.That(content.Find("HandAuthoredContentRoot"), Is.Not.Null);
                Assert.That(content.Find("DynamicObjectRoot"), Is.Not.Null);
                var marker = generated.GetComponent<GeneratedTerrainTile>();
                Assert.That(marker.MapName, Is.EqualTo("关中"));
                Assert.That(marker.Row, Is.EqualTo(row));
                Assert.That(marker.Column, Is.EqualTo(column));
                Assert.That(marker.TotalRows, Is.EqualTo(rows));
                Assert.That(marker.TotalColumns, Is.EqualTo(columns));
                Assert.That(marker.TileWorldSize, Is.EqualTo(128f));
                Assert.That(marker.HeightmapResolution, Is.EqualTo(33));
                Assert.That(marker.GeneratorVersion, Is.EqualTo(1));
                Assert.That(marker.Terrain.transform.parent, Is.EqualTo(generated));
                Assert.That(marker.Terrain.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(marker.Terrain.terrainData, Is.SameAs(expected));
                Assert.That(marker.Terrain.terrainData.size, Is.EqualTo(new Vector3(128f, 256f, 128f)));
                Assert.That(marker.Terrain.GetComponent<TerrainCollider>().terrainData, Is.SameAs(expected));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        private static void AssertHorizontalBorder(TerrainData left, TerrainData right)
        {
            var last = left.heightmapResolution - 1;
            for (var y = 0; y <= last; y++) Assert.That(left.GetHeight(last, y), Is.EqualTo(right.GetHeight(0, y)));
        }

        private static void AssertVerticalBorder(TerrainData bottom, TerrainData top)
        {
            var last = bottom.heightmapResolution - 1;
            for (var x = 0; x <= last; x++) Assert.That(bottom.GetHeight(x, last), Is.EqualTo(top.GetHeight(x, 0)));
        }

        private static float[] CaptureAllHeights(string mapName, int rows, int columns)
        {
            var values = new List<float>();
            for (var row = 1; row <= rows; row++)
            for (var column = 1; column <= columns; column++)
            {
                var name = mapName + "_" + row + "_" + column;
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(OutputRoot + "/" + mapName + "/TerrainData/" + name + ".asset");
                var resolution = data.heightmapResolution;
                var heights = data.GetHeights(0, 0, resolution, resolution);
                foreach (var height in heights) values.Add(height);
            }
            return values.ToArray();
        }

        private object Invoke(string method, ScriptableObject profile)
        {
            return generatorType.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { profile });
        }

        private static object InvokeInstance(object target, string method)
        {
            var result = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
            return result is bool value ? new BoxedBool(value) : result;
        }

        private void SetStatic(string field, object value)
        {
            generatorType.GetField(field, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
        }

        private void ClearFaults()
        {
            SetStatic("testExceptionCallback", null);
            SetStatic("testFailureCallback", null);
            SetStatic("testCleanupFailureCallback", null);
            SetStatic("testSurfaceRefreshExceptionCallback", null);
        }

        private static float[] Flatten(float[,,] values)
        {
            return values.Cast<float>().ToArray();
        }

        private static void SetInstance(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static T Get<T>(object target, string property)
        {
            return (T)target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetValue(target);
        }

        private static List<string> GetStrings(object target, string property)
        {
            return ((IEnumerable)Get<object>(target, property)).Cast<object>().Select(value => (string)value).ToList();
        }

        private static Type RequireType(string name)
        {
            return Type.GetType(name) ?? throw new InvalidOperationException("找不到类型：" + name);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static void DeleteIfPresent(string path)
        {
            if (AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        private List<Exception> CleanupClassHostAndReturnIsolation()
        {
            var errors = new List<Exception>();
            if (!isolationControlAcquired && !hostSceneOwned) return errors;

            Scene handoffScene = default;
            try
            {
                handoffScene = FindCleanEmptyUntitledScene();
                if (!handoffScene.IsValid())
                    handoffScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                AssertCleanEmptyUntitledScene(handoffScene, "无法建立归还共享夹具的干净无标题场景");
                SceneManager.SetActiveScene(handoffScene);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                var hostScene = SceneManager.GetSceneByPath(HostScenePath);
                if (hostScene.IsValid() && hostScene.isLoaded && !EditorSceneManager.CloseScene(hostScene, true))
                    throw new InvalidOperationException("关闭地图地形测试宿主场景失败：" + HostScenePath);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                if ((AssetDatabase.IsValidFolder(TempRoot) || Directory.Exists(ToAbsoluteAssetPath(TempRoot))) &&
                    !AssetDatabase.DeleteAsset(TempRoot))
                    throw new IOException("删除地图地形测试临时根失败：" + TempRoot);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                if (hostSceneOwned &&
                    (AssetDatabase.LoadMainAssetAtPath(HostScenePath) != null || File.Exists(ToAbsoluteAssetPath(HostScenePath))) &&
                    !AssetDatabase.DeleteAsset(HostScenePath))
                    throw new IOException("删除地图地形测试宿主场景失败：" + HostScenePath);
                if (File.Exists(ToAbsoluteAssetPath(HostScenePath)) || File.Exists(ToAbsoluteAssetPath(HostScenePath) + ".meta"))
                    throw new IOException("地图地形测试宿主场景或其 .meta 仍然残留：" + HostScenePath);
                hostSceneOwned = false;
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                DeleteOwnedTempParentIfEmpty();
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            try
            {
                if (!handoffScene.IsValid() || !handoffScene.isLoaded)
                    handoffScene = FindCleanEmptyUntitledScene();
                AssertCleanEmptyUntitledScene(handoffScene, "调用共享隔离 Enter 前的活动场景不安全");
                SceneManager.SetActiveScene(handoffScene);
                if (isolationControlAcquired)
                {
                    EditModeSceneIsolation.Enter();
                    isolationControlAcquired = false;
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }

            return errors;
        }

        private static void AssertCleanEmptyUntitledScene(Scene scene, string message)
        {
            if (!scene.IsValid() || !scene.isLoaded || !string.IsNullOrEmpty(scene.path) || scene.isDirty ||
                scene.GetRootGameObjects().Length != 0)
                throw new InvalidOperationException(message + "。要求有效、已加载、无路径、未脏且没有根对象。");
        }

        private static Scene FindCleanEmptyUntitledScene()
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded && string.IsNullOrEmpty(scene.path) && !scene.isDirty &&
                    scene.GetRootGameObjects().Length == 0) return scene;
            }
            return default;
        }

        private void DeleteOwnedTempParentIfEmpty()
        {
            if (tempParentExistedBeforeClass) return;
            var absolute = ToAbsoluteAssetPath(TempParent);
            if (!Directory.Exists(absolute)) return;
            if (Directory.GetFileSystemEntries(absolute).Length != 0) return;
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(TempParent) || !AssetDatabase.DeleteAsset(TempParent))
                throw new AssertionException("无法删除本测试创建的空临时父目录：" + TempParent);
            if (Directory.Exists(absolute) || File.Exists(absolute + ".meta"))
                throw new AssertionException("临时父目录或其 .meta 仍然残留：" + TempParent);
        }

        private static string CaptureAssetTree(string assetPath)
        {
            var absolute = ToAbsoluteAssetPath(assetPath);
            if (!Directory.Exists(absolute) && !File.Exists(absolute)) return "missing";
            var values = new StringBuilder();
            values.Append("guid=").Append(AssetDatabase.AssetPathToGUID(assetPath));
            if (File.Exists(absolute + ".meta"))
                values.Append("|root-meta=").Append(Convert.ToBase64String(File.ReadAllBytes(absolute + ".meta")));
            if (File.Exists(absolute))
                values.Append("|file=").Append(Convert.ToBase64String(File.ReadAllBytes(absolute)));
            if (Directory.Exists(absolute))
            {
                foreach (var file in Directory.GetFiles(absolute, "*", SearchOption.AllDirectories)
                             .OrderBy(value => value, StringComparer.Ordinal))
                {
                    var relative = file.Substring(absolute.Length).Replace('\\', '/');
                    values.Append('|').Append(relative).Append('=').Append(Convert.ToBase64String(File.ReadAllBytes(file)));
                }
            }
            return values.ToString();
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static void AssertNoStagingAssets()
        {
            var staging = OutputRoot + "/__Staging";
            Assert.That(AssetDatabase.IsValidFolder(staging), Is.False);
        }

        private static List<SceneState> CaptureScenes()
        {
            var active = SceneManager.GetActiveScene();
            var values = new List<SceneState>();
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                values.Add(new SceneState(scene.handle, scene.path, scene.isDirty, scene == active));
            }
            return values;
        }

        private void RestoreOriginalScenes()
        {
            if (originalScenes == null) return;
            var originalHandles = new HashSet<int>(originalScenes.Select(item => item.Handle));
            for (var index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!originalHandles.Contains(scene.handle)) EditorSceneManager.CloseScene(scene, true);
            }

            foreach (var expected in originalScenes)
            {
                if (FindOpenScene(expected.Handle).IsValid() || string.IsNullOrEmpty(expected.Path)) continue;
                EditorSceneManager.OpenScene(expected.Path, OpenSceneMode.Additive);
            }

            var expectedActive = originalScenes.SingleOrDefault(item => item.Active);
            var active = FindOpenScene(expectedActive.Handle);
            if (!active.IsValid() && !string.IsNullOrEmpty(expectedActive.Path))
                active = SceneManager.GetSceneByPath(expectedActive.Path);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }

        private static Scene FindOpenScene(int handle)
        {
            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.handle == handle) return scene;
            }
            return default;
        }

        private static void AssertScenes(IReadOnlyList<SceneState> expected)
        {
            var actual = CaptureScenes();
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i].Handle, Is.EqualTo(expected[i].Handle), "scene handle/order " + i);
                Assert.That(actual[i].Path, Is.EqualTo(expected[i].Path), "scene path/order " + i);
                Assert.That(actual[i].Dirty, Is.EqualTo(expected[i].Dirty), "scene dirty " + i);
                Assert.That(actual[i].Active, Is.EqualTo(expected[i].Active), "active scene " + i);
            }
        }

        private readonly struct SceneState
        {
            internal SceneState(int handle, string path, bool dirty, bool active)
            { Handle = handle; Path = path; Dirty = dirty; Active = active; }
            internal int Handle { get; }
            internal string Path { get; }
            internal bool Dirty { get; }
            internal bool Active { get; }
        }

        private sealed class BoxedBool
        {
            internal BoxedBool(bool value) { Value = value; }
            public bool Value { get; }
        }
    }

    public sealed class MapTerrainProtectedReference : MonoBehaviour
    {
        public GameObject Other;
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game
{
    public sealed class MapTerrainValidationResult
    {
        internal MapTerrainValidationResult(bool success, string error)
        {
            Success = success;
            Error = error ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }
    }

    public sealed class MapTerrainPreflightResult
    {
        internal MapTerrainPreflightResult(bool success, string error, int createCount, string outputDirectory)
        {
            Success = success;
            Error = error ?? string.Empty;
            CreateCount = createCount;
            OutputDirectory = outputDirectory ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }
        public int CreateCount { get; }
        public string OutputDirectory { get; }
    }

    public sealed class MapTerrainGenerationResult
    {
        internal MapTerrainGenerationResult(bool success, string error, IReadOnlyList<string> scenePaths,
            IReadOnlyList<string> unremovedPaths)
        {
            Success = success;
            Error = error ?? string.Empty;
            ScenePaths = Copy(scenePaths);
            UnremovedPaths = Copy(unremovedPaths);
        }

        public bool Success { get; }
        public string Error { get; }
        public IReadOnlyList<string> ScenePaths { get; }
        public IReadOnlyList<string> UnremovedPaths { get; }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> source)
        {
            if (source == null || source.Count == 0)
            {
                return Array.AsReadOnly(Array.Empty<string>());
            }

            var values = new string[source.Count];
            for (var i = 0; i < source.Count; i++) values[i] = source[i];
            return Array.AsReadOnly(values);
        }
    }

    public sealed class MapTerrainSurfaceRefreshResult
    {
        internal MapTerrainSurfaceRefreshResult(bool success, string error, int updatedCount, string outputDirectory)
        {
            Success = success;
            Error = error ?? string.Empty;
            UpdatedCount = updatedCount;
            OutputDirectory = outputDirectory ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }
        public int UpdatedCount { get; }
        public string OutputDirectory { get; }
    }

    public static class MapTerrainGenerator
    {
        public const int GeneratorVersion = 1;

        private const string ProductOutputRoot = "Assets/Game/Scenes/GeneratedMaps";
        private const string TestOutputRoot = "Assets/Tests/Temp/MapTerrainGeneration";
        private static readonly HashSet<string> ReservedNames = BuildReservedNames();

        // These test seams are deliberately private and are cleared after every Generate call.
        private static string outputRootOverride;
        private static Action<object, int> testExceptionCallback;
        private static Func<object, int, bool> testFailureCallback;
        private static Func<string, bool> testCleanupFailureCallback;
        private static Action<int> testSurfaceRefreshExceptionCallback;

        private enum GenerationStage
        {
            AfterTileSaved,
            AfterFinalMoveBeforeValidation
        }

        public static MapTerrainValidationResult ValidateProfile(MapTerrainGenerationProfile profile)
        {
            if (profile == null) return Invalid("配置不能为空。");
            var nameError = ValidateMapName(profile.MapName);
            if (!string.IsNullOrEmpty(nameError)) return Invalid(nameError);
            if (profile.Heightmap == null) return Invalid("heightmap 不能为空。");
            if (profile.Heightmap.width < 2 || profile.Heightmap.height < 2)
                return Invalid("heightmap 宽高必须都至少为 2。");
            try
            {
                profile.Heightmap.GetPixelBilinear(0f, 0f);
            }
            catch (Exception exception)
            {
                return Invalid("heightmap 必须开启 Read/Write：" + exception.Message);
            }

            if (profile.Rows < 1 || profile.Rows > 8) return Invalid("rows 必须在 1..8 范围内。");
            if (profile.Columns < 1 || profile.Columns > 8) return Invalid("columns 必须在 1..8 范围内。");
            if (profile.Rows * profile.Columns > 64) return Invalid("rows * columns 不得超过 64。");
            if (!IsFinitePositive(profile.TileWorldSize)) return Invalid("tileWorldSize 必须是大于 0 的有限数值。");
            if (!IsFinitePositive(profile.MaxTerrainHeight)) return Invalid("maxTerrainHeight 必须是大于 0 的有限数值。");
            if (profile.HeightmapResolution != 33 && profile.HeightmapResolution != 65 &&
                profile.HeightmapResolution != 129 && profile.HeightmapResolution != 257 &&
                profile.HeightmapResolution != 513)
                return Invalid("heightmapResolution 只允许 33、65、129、257、513。");
            if (!IsFinitePositive(profile.DetailFrequency)) return Invalid("detailFrequency 必须是大于 0 的有限数值。");
            if (float.IsNaN(profile.DetailAmplitude) || float.IsInfinity(profile.DetailAmplitude) ||
                profile.DetailAmplitude < 0f || profile.DetailAmplitude > 1f)
                return Invalid("detailAmplitude 必须在 0..1 范围内。");
            if (profile.DetailOctaves < 1 || profile.DetailOctaves > 6)
                return Invalid("detailOctaves 必须在 1..6 范围内。");
            var surfaceLayers = profile.SurfaceLayers ?? Array.Empty<MapTerrainSurfaceLayerRule>();
            if (surfaceLayers.Length > 8) return Invalid("surfaceLayers 不得超过 8 层。");
            if (surfaceLayers.Length > 0 && !IsValidAlphamapResolution(profile.AlphamapResolution))
                return Invalid("alphamapResolution 只允许 16、32、64、128、256、512、1024、2048。");
            var uniqueLayers = new HashSet<TerrainLayer>();
            var uniqueRoles = new HashSet<MapTerrainSurfaceRole>();
            for (var index = 0; index < surfaceLayers.Length; index++)
            {
                var rule = surfaceLayers[index];
                if (rule == null || rule.Layer == null)
                    return Invalid("surfaceLayers[" + index + "] 的 TerrainLayer 不能为空。");
                if (!uniqueLayers.Add(rule.Layer))
                    return Invalid("surfaceLayers 不能重复引用同一个 TerrainLayer。");
                if (profile.SurfaceControlMap != null && rule.Role != MapTerrainSurfaceRole.HeightBased &&
                    !uniqueRoles.Add(rule.Role))
                    return Invalid("使用 surfaceControlMap 时，surfaceLayers 不能重复配置同一地表语义。");
                if (float.IsNaN(rule.CenterHeight) || float.IsInfinity(rule.CenterHeight) ||
                    rule.CenterHeight < 0f || rule.CenterHeight > 1f)
                    return Invalid("surfaceLayers[" + index + "] 的 centerHeight 必须在 0..1 范围内。");
                if (float.IsNaN(rule.BlendWidth) || float.IsInfinity(rule.BlendWidth) ||
                    rule.BlendWidth <= 0f || rule.BlendWidth > 1f)
                    return Invalid("surfaceLayers[" + index + "] 的 blendWidth 必须在 0..1 范围内且大于 0。");
            }
            if (profile.SurfaceControlMap != null)
            {
                if (profile.SurfaceControlMap.width < 2 || profile.SurfaceControlMap.height < 2)
                    return Invalid("surfaceControlMap 宽高必须都至少为 2。");
                var mapWidth = profile.Columns * profile.TileWorldSize;
                var mapHeight = profile.Rows * profile.TileWorldSize;
                if (!IsFinitePositive(profile.SurfaceSlopeSampleMeters) ||
                    profile.SurfaceSlopeSampleMeters * 2f > Mathf.Min(mapWidth, mapHeight))
                    return Invalid("surfaceSlopeSampleMeters 必须是有效半径，且采样跨度不能超过全图短边。");
                if (float.IsNaN(profile.RockSlopeStartDegrees) ||
                    float.IsInfinity(profile.RockSlopeStartDegrees) ||
                    profile.RockSlopeStartDegrees < 0f || profile.RockSlopeStartDegrees >= 90f)
                    return Invalid("rockSlopeStartDegrees 必须在 0..90 范围内。");
                if (float.IsNaN(profile.RockSlopeFullDegrees) ||
                    float.IsInfinity(profile.RockSlopeFullDegrees) ||
                    profile.RockSlopeFullDegrees <= profile.RockSlopeStartDegrees ||
                    profile.RockSlopeFullDegrees > 90f)
                    return Invalid("rockSlopeFullDegrees 必须大于起始坡度且不超过 90。");
                try
                {
                    profile.SurfaceControlMap.GetPixel(0, 0);
                }
                catch (Exception exception)
                {
                    return Invalid("surfaceControlMap 必须开启 Read/Write：" + exception.Message);
                }
                foreach (var role in new[] { MapTerrainSurfaceRole.Sand, MapTerrainSurfaceRole.Vegetation,
                             MapTerrainSurfaceRole.Dirt, MapTerrainSurfaceRole.Rock })
                    if (!uniqueRoles.Contains(role))
                        return Invalid("使用 surfaceControlMap 时缺少地表语义：" + role);
            }
            var outputRootError = ValidateOutputRootOverride();
            return string.IsNullOrEmpty(outputRootError) ? Valid() : Invalid(outputRootError);
        }

        public static MapTerrainPreflightResult Preflight(MapTerrainGenerationProfile profile)
        {
            var validation = ValidateProfile(profile);
            var output = profile == null || string.IsNullOrEmpty(profile.MapName)
                ? string.Empty
                : GetOutputRoot() + "/" + profile.MapName;
            if (!validation.Success)
                return new MapTerrainPreflightResult(false, validation.Error, 0, output);
            if (AssetDatabase.IsValidFolder(output) || AssetDatabase.LoadMainAssetAtPath(output) != null ||
                Directory.Exists(ToAbsolutePath(output)) || File.Exists(ToAbsolutePath(output)))
                return new MapTerrainPreflightResult(false, "同名地图目录已存在，拒绝写入：" + output, 0, output);
            return new MapTerrainPreflightResult(true, string.Empty, profile.Rows * profile.Columns, output);
        }

        public static MapTerrainGenerationResult Generate(MapTerrainGenerationProfile profile)
        {
            var originalActive = SceneManager.GetActiveScene();
            var scenePaths = new List<string>();
            var unremoved = new List<string>();
            var cleanupTargets = new List<string>();
            var success = false;
            var error = string.Empty;
            try
            {
                var preflight = Preflight(profile);
                if (!preflight.Success) return Failure(preflight.Error, unremoved);

                var plans = CalculateAllTiles(profile);
                var root = GetOutputRoot();
                var stagingRoot = root + "/__Staging";
                var stagingMap = stagingRoot + "/" + profile.MapName + "_" + Guid.NewGuid().ToString("N");
                var finalMap = preflight.OutputDirectory;
                EnsureFolder(root, cleanupTargets);
                EnsureFolder(stagingRoot, cleanupTargets);
                EnsureFolder(stagingMap, cleanupTargets);
                EnsureFolder(stagingMap + "/Scenes", cleanupTargets);
                EnsureFolder(stagingMap + "/TerrainData", cleanupTargets);

                var saved = 0;
                foreach (var plan in plans)
                {
                    CreateTile(profile, plan, stagingMap);
                    saved++;
                    InvokeFaults(GenerationStage.AfterTileSaved, saved);
                }

                ValidateMapAssets(profile, stagingMap);
                var moveError = AssetDatabase.MoveAsset(stagingMap, finalMap);
                if (!string.IsNullOrEmpty(moveError)) throw new InvalidOperationException("移动暂存地图失败：" + moveError);
                cleanupTargets.Remove(stagingMap);
                cleanupTargets.Add(finalMap);
                InvokeFaults(GenerationStage.AfterFinalMoveBeforeValidation, saved);
                ValidateMapAssets(profile, finalMap);

                if (cleanupTargets.Contains(stagingRoot) && AssetDatabase.IsValidFolder(stagingRoot))
                {
                    if (!AssetDatabase.DeleteAsset(stagingRoot))
                        throw new InvalidOperationException("删除空暂存目录失败：" + stagingRoot);
                    cleanupTargets.Remove(stagingRoot);
                }

                for (var row = 1; row <= profile.Rows; row++)
                for (var column = 1; column <= profile.Columns; column++)
                    scenePaths.Add(finalMap + "/Scenes/" + TileName(profile.MapName, row, column) + ".unity");
                success = true;
                return new MapTerrainGenerationResult(true, string.Empty, scenePaths, Array.Empty<string>());
            }
            catch (Exception exception)
            {
                error = "地图地形生成失败：" + exception.Message;
            }
            finally
            {
                if (!success) Cleanup(cleanupTargets, unremoved);
                if (originalActive.IsValid() && originalActive.isLoaded) SceneManager.SetActiveScene(originalActive);
                testExceptionCallback = null;
                testFailureCallback = null;
                testCleanupFailureCallback = null;
            }

            if (unremoved.Count > 0)
                error += " 未能清理以下路径：" + string.Join("；", unremoved);
            return Failure(error, unremoved);
        }

        public static MapTerrainSurfaceRefreshResult RefreshSurface(MapTerrainGenerationProfile profile)
        {
            var validation = ValidateProfile(profile);
            var outputDirectory = profile == null || string.IsNullOrEmpty(profile.MapName)
                ? string.Empty
                : GetOutputRoot() + "/" + profile.MapName;
            if (!validation.Success)
                return new MapTerrainSurfaceRefreshResult(false, validation.Error, 0, outputDirectory);
            var rules = profile.SurfaceLayers ?? Array.Empty<MapTerrainSurfaceLayerRule>();
            if (rules.Length == 0)
                return new MapTerrainSurfaceRefreshResult(false, "surfaceLayers 至少需要配置一层。", 0,
                    outputDirectory);
            if (!AssetDatabase.IsValidFolder(outputDirectory))
                return new MapTerrainSurfaceRefreshResult(false, "地图目录不存在：" + outputDirectory, 0,
                    outputDirectory);

            var plans = new List<SurfaceRefreshPlan>(profile.Rows * profile.Columns);
            for (var row = 1; row <= profile.Rows; row++)
            for (var column = 1; column <= profile.Columns; column++)
            {
                var name = TileName(profile.MapName, row, column);
                var path = outputDirectory + "/TerrainData/" + name + ".asset";
                var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (data == null)
                    return new MapTerrainSurfaceRefreshResult(false, "缺少 TerrainData：" + path, 0,
                        outputDirectory);
                if (data.heightmapResolution != profile.HeightmapResolution ||
                    !Mathf.Approximately(data.size.x, profile.TileWorldSize) ||
                    !Mathf.Approximately(data.size.y, profile.MaxTerrainHeight) ||
                    !Mathf.Approximately(data.size.z, profile.TileWorldSize))
                    return new MapTerrainSurfaceRefreshResult(false, "TerrainData 尺寸与配置不匹配：" + path, 0,
                        outputDirectory);
                var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
                var weights = BuildSurfaceWeights(profile, rules, profile.AlphamapResolution, row, column, heights);
                plans.Add(new SurfaceRefreshPlan(data, path, weights));
            }

            var backups = plans.Select(plan => new SurfaceBackup(plan.Data)).ToArray();
            var updated = 0;
            try
            {
                Undo.RegisterCompleteObjectUndo(plans.Select(plan => plan.Data).ToArray(), "刷新地图地形表面");
                foreach (var plan in plans)
                {
                    AssignSurface(plan.Data, rules, profile.AlphamapResolution, plan.Weights);
                    EditorUtility.SetDirty(plan.Data);
                    updated++;
                    testSurfaceRefreshExceptionCallback?.Invoke(updated);
                }
                AssetDatabase.SaveAssets();
                foreach (var plan in plans) ValidateSurfaceData(profile, plan.Data, plan.Path);
                return new MapTerrainSurfaceRefreshResult(true, string.Empty, updated, outputDirectory);
            }
            catch (Exception exception)
            {
                var rollbackError = string.Empty;
                try
                {
                    for (var index = 0; index < plans.Count; index++)
                    {
                        backups[index].Restore();
                        EditorUtility.SetDirty(plans[index].Data);
                    }
                    AssetDatabase.SaveAssets();
                }
                catch (Exception rollbackException)
                {
                    rollbackError = " 回滚失败：" + rollbackException.Message;
                }
                return new MapTerrainSurfaceRefreshResult(false,
                    "刷新地图地形表面失败：" + exception.Message + rollbackError, 0, outputDirectory);
            }
            finally
            {
                testSurfaceRefreshExceptionCallback = null;
            }
        }

        private static void InvokeFaults(GenerationStage stage, int saved)
        {
            testExceptionCallback?.Invoke(stage, saved);
            if (testFailureCallback != null && testFailureCallback(stage, saved))
                throw new GenerationStoppedException("测试注入了非异常失败，阶段=" + stage + "，已保存块数=" + saved + "。");
        }

        private static List<TilePlan> CalculateAllTiles(MapTerrainGenerationProfile profile)
        {
            var plans = new List<TilePlan>(profile.Rows * profile.Columns);
            var resolution = profile.HeightmapResolution;
            var globalWidth = profile.Columns * (resolution - 1);
            var globalHeight = profile.Rows * (resolution - 1);
            for (var row = 0; row < profile.Rows; row++)
            for (var column = 0; column < profile.Columns; column++)
            {
                var heights = new float[resolution, resolution];
                for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                {
                    var u = (column * (resolution - 1) + x) / (float)globalWidth;
                    var v = (row * (resolution - 1) + y) / (float)globalHeight;
                    heights[y, x] = SampleHeight(profile, u, v);
                }
                plans.Add(new TilePlan(row + 1, column + 1, heights));
            }
            return plans;
        }

        private static float SampleHeight(MapTerrainGenerationProfile profile, float u, float v)
        {
            var sampleU = u * (profile.Heightmap.width - 1) / profile.Heightmap.width;
            var sampleV = v * (profile.Heightmap.height - 1) / profile.Heightmap.height;
            var baseHeight = profile.Heightmap.GetPixelBilinear(sampleU, sampleV).grayscale;
            var frequency = profile.DetailFrequency;
            var amplitude = 1f;
            var sum = 0f;
            var weight = 0f;
            for (var i = 0; i < profile.DetailOctaves; i++)
            {
                var n = Mathf.PerlinNoise(u * frequency + profile.DetailSeed * 0.01337f + i * 17.123f,
                    v * frequency + profile.DetailSeed * 0.01717f + i * 31.337f);
                sum += (n * 2f - 1f) * amplitude;
                weight += amplitude;
                frequency *= 2f;
                amplitude *= 0.5f;
            }
            return Mathf.Clamp01(baseHeight + sum / weight * profile.DetailAmplitude);
        }

        private static void CreateTile(MapTerrainGenerationProfile profile, TilePlan plan, string mapRoot)
        {
            var name = TileName(profile.MapName, plan.Row, plan.Column);
            var dataPath = mapRoot + "/TerrainData/" + name + ".asset";
            var scenePath = mapRoot + "/Scenes/" + name + ".unity";
            var data = new TerrainData
            {
                name = name,
                heightmapResolution = profile.HeightmapResolution,
                size = new Vector3(profile.TileWorldSize, profile.MaxTerrainHeight, profile.TileWorldSize)
            };
            data.SetHeights(0, 0, plan.Heights);
            ApplySurface(profile, plan.Row, plan.Column, plan.Heights, data);
            AssetDatabase.CreateAsset(data, dataPath);
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath) == null)
                throw new InvalidOperationException("创建 TerrainData 失败：" + dataPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var mapRootObject = new GameObject("MapRoot");
                SceneManager.MoveGameObjectToScene(mapRootObject, scene);
                var context = mapRootObject.AddComponent<MapContext>();
                var content = NewChild("MapContentRoot", mapRootObject.transform);
                var generatedRoot = NewChild("GeneratedTerrainRoot", content.transform);
                NewChild("HandAuthoredContentRoot", content.transform);
                var dynamicRoot = NewChild("DynamicObjectRoot", content.transform);
                var terrainObject = Terrain.CreateTerrainGameObject(data);
                terrainObject.name = "Terrain";
                terrainObject.transform.SetParent(generatedRoot.transform, false);
                terrainObject.transform.localPosition = Vector3.zero;
                var terrain = terrainObject.GetComponent<Terrain>();
                var collider = terrainObject.GetComponent<TerrainCollider>();
                if (terrain == null || collider == null) throw new InvalidOperationException("Terrain 组件创建失败：" + name);
                terrain.terrainData = data;
                collider.terrainData = data;

                SetObjectReference(context, "mapContentRoot", content);
                SetObjectReference(context, "dynamicObjectRoot", dynamicRoot.transform);
                var marker = generatedRoot.AddComponent<GeneratedTerrainTile>();
                SetString(marker, "mapName", profile.MapName);
                SetInt(marker, "row", plan.Row);
                SetInt(marker, "column", plan.Column);
                SetInt(marker, "totalRows", profile.Rows);
                SetInt(marker, "totalColumns", profile.Columns);
                SetFloat(marker, "tileWorldSize", profile.TileWorldSize);
                SetInt(marker, "heightmapResolution", profile.HeightmapResolution);
                SetInt(marker, "generatorVersion", GeneratorVersion);
                SetObjectReference(marker, "terrain", terrain);
                if (!context.Validate(out var validationError))
                    throw new InvalidOperationException(name + " 的 MapContext 无效：" + validationError);
                content.SetActive(false);
                if (!EditorSceneManager.SaveScene(scene, scenePath, false))
                    throw new InvalidOperationException("保存场景失败：" + scenePath);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ApplySurface(MapTerrainGenerationProfile profile, int row, int column, float[,] heights,
            TerrainData data)
        {
            var rules = profile.SurfaceLayers ?? Array.Empty<MapTerrainSurfaceLayerRule>();
            if (rules.Length == 0) return;

            var weights = BuildSurfaceWeights(profile, rules, profile.AlphamapResolution, row, column, heights);
            AssignSurface(data, rules, profile.AlphamapResolution, weights);
        }

        /// <summary>
        /// 为单块 Terrain 生成归一化混合权重。现实控制图缺失时保持旧高度规则；存在时使用全图坐标采样，
        /// 确保四块 Terrain 的道路与土地覆盖在接缝处一致。
        /// </summary>
        private static float[,,] BuildSurfaceWeights(MapTerrainGenerationProfile profile,
            IReadOnlyList<MapTerrainSurfaceLayerRule> rules, int alphamapResolution, int row, int column,
            float[,] heights)
        {
            var weights = new float[alphamapResolution, alphamapResolution, rules.Count];
            for (var y = 0; y < alphamapResolution; y++)
            for (var x = 0; x < alphamapResolution; x++)
            {
                var u = x / (float)(alphamapResolution - 1);
                var v = y / (float)(alphamapResolution - 1);
                var height = SampleTileHeight(heights, u, v);
                var total = 0f;
                for (var layer = 0; layer < rules.Count; layer++)
                {
                    var rule = rules[layer];
                    var weight = Mathf.Max(0f, 1f - Mathf.Abs(height - rule.CenterHeight) / rule.BlendWidth);
                    weights[y, x, layer] = weight;
                    total += weight;
                }

                if (profile.SurfaceControlMap != null)
                {
                    var globalU = (column - 1 + u) / profile.Columns;
                    var globalV = (row - 1 + v) / profile.Rows;
                    ApplySurfaceControl(profile, rules, weights, y, x, globalU, globalV);
                    total = 0f;
                    for (var layer = 0; layer < rules.Count; layer++) total += weights[y, x, layer];
                }

                if (total <= Mathf.Epsilon)
                {
                    var closest = 0;
                    var closestDistance = float.MaxValue;
                    for (var layer = 0; layer < rules.Count; layer++)
                    {
                        var distance = Mathf.Abs(height - rules[layer].CenterHeight);
                        if (distance >= closestDistance) continue;
                        closest = layer;
                        closestDistance = distance;
                    }
                    weights[y, x, closest] = 1f;
                    continue;
                }

                for (var layer = 0; layer < rules.Count; layer++) weights[y, x, layer] /= total;
            }
            return weights;
        }

        /// <summary>
        /// 把 WorldCover、NDVI、坡度和道路遮罩叠加到既有高度权重；道路只覆盖到 Dirt 语义，
        /// 未知分类继续保留高度回退，避免控制图缺损产生无材质区域。
        /// </summary>
        private static void ApplySurfaceControl(MapTerrainGenerationProfile profile,
            IReadOnlyList<MapTerrainSurfaceLayerRule> rules, float[,,] weights, int y, int x,
            float globalU, float globalV)
        {
            var control = SampleControl(profile.SurfaceControlMap, globalU, globalV);
            var worldCoverClass = Mathf.RoundToInt(control.r * 255f);
            var vegetationIndex = FindRole(rules, MapTerrainSurfaceRole.Vegetation);
            var dirtIndex = FindRole(rules, MapTerrainSurfaceRole.Dirt);
            var sandIndex = FindRole(rules, MapTerrainSurfaceRole.Sand);
            var rockIndex = FindRole(rules, MapTerrainSurfaceRole.Rock);

            var ndvi = -1f + control.g * 255f * 0.008f;
            if (worldCoverClass == 10)
            {
                // 林地保持连续深植被；权重仍由同位置 NDVI 调节，不额外制造林线细节。
                weights[y, x, vegetationIndex] += Mathf.Lerp(2.2f, 3.2f,
                    Mathf.InverseLerp(0.25f, 0.85f, ndvi));
            }
            else if (worldCoverClass == 40)
            {
                // 耕地使用卫星分类和 NDVI 决定植被/裸土比例，使田块与林地明确分开。
                var cropVegetation = Mathf.InverseLerp(0.1f, 0.75f, ndvi);
                weights[y, x, vegetationIndex] += Mathf.Lerp(0.8f, 1.8f, cropVegetation);
                weights[y, x, dirtIndex] += Mathf.Lerp(1.5f, 0.45f, cropVegetation);
            }
            else if (worldCoverClass == 20 || worldCoverClass == 30 || worldCoverClass == 90 ||
                     worldCoverClass == 95 || worldCoverClass == 100)
            {
                weights[y, x, vegetationIndex] += Mathf.Lerp(1.4f, 2.2f,
                    Mathf.InverseLerp(0.1f, 0.8f, ndvi));
            }
            else if (worldCoverClass == 50 || worldCoverClass == 60)
            {
                weights[y, x, dirtIndex] += 2f;
            }
            else if (worldCoverClass == 70)
            {
                weights[y, x, sandIndex] += 1.5f;
            }
            else if (worldCoverClass == 80)
            {
                // 已核实水体只保留在控制数据中；没有真实水面层时不以沙地等材质冒充。
            }

            var globalWorldSizeX = profile.Columns * profile.TileWorldSize;
            var globalWorldSizeZ = profile.Rows * profile.TileWorldSize;
            // 使用配置声明的权威 DEM 采样尺度，避免在上采样像素间计算出虚假的米级坡度细节。
            var sampleMeters = profile.SurfaceSlopeSampleMeters;
            var du = sampleMeters / globalWorldSizeX;
            var dv = sampleMeters / globalWorldSizeZ;
            var heightLeft = SampleHeight(profile, Mathf.Clamp01(globalU - du), globalV);
            var heightRight = SampleHeight(profile, Mathf.Clamp01(globalU + du), globalV);
            var heightDown = SampleHeight(profile, globalU, Mathf.Clamp01(globalV - dv));
            var heightUp = SampleHeight(profile, globalU, Mathf.Clamp01(globalV + dv));
            var riseX = (heightRight - heightLeft) * profile.MaxTerrainHeight;
            var riseZ = (heightUp - heightDown) * profile.MaxTerrainHeight;
            var slope = Mathf.Atan(Mathf.Sqrt(riseX * riseX + riseZ * riseZ) / (sampleMeters * 2f)) *
                        Mathf.Rad2Deg;
            weights[y, x, rockIndex] += Mathf.SmoothStep(0f, 2.5f,
                Mathf.InverseLerp(profile.RockSlopeStartDegrees, profile.RockSlopeFullDegrees, slope));

            var road = Mathf.Clamp01(control.a);
            if (road <= 0f) return;
            for (var layer = 0; layer < rules.Count; layer++) weights[y, x, layer] *= 1f - road;
            weights[y, x, dirtIndex] += road * 4f;
        }

        private static Color SampleControl(Texture2D texture, float u, float v)
        {
            var x = Mathf.Clamp(Mathf.RoundToInt(u * (texture.width - 1)), 0, texture.width - 1);
            var y = Mathf.Clamp(Mathf.RoundToInt(v * (texture.height - 1)), 0, texture.height - 1);
            return texture.GetPixel(x, y);
        }

        private static int FindRole(IReadOnlyList<MapTerrainSurfaceLayerRule> rules, MapTerrainSurfaceRole role)
        {
            for (var index = 0; index < rules.Count; index++)
                if (rules[index].Role == role) return index;
            throw new InvalidOperationException("缺少地表语义：" + role);
        }

        private static void AssignSurface(TerrainData data, IReadOnlyList<MapTerrainSurfaceLayerRule> rules,
            int alphamapResolution, float[,,] weights)
        {
            data.alphamapResolution = alphamapResolution;
            data.terrainLayers = rules.Select(rule => rule.Layer).ToArray();
            data.SetAlphamaps(0, 0, weights);
        }

        private static void ValidateSurfaceData(MapTerrainGenerationProfile profile, TerrainData data, string path)
        {
            var rules = profile.SurfaceLayers ?? Array.Empty<MapTerrainSurfaceLayerRule>();
            if (data.alphamapResolution != profile.AlphamapResolution || data.alphamapLayers != rules.Length ||
                !data.terrainLayers.SequenceEqual(rules.Select(rule => rule.Layer)))
                throw new InvalidOperationException("TerrainData 表面层校验失败：" + path);
        }

        private static float SampleTileHeight(float[,] heights, float u, float v)
        {
            var width = heights.GetLength(1);
            var height = heights.GetLength(0);
            var sampleX = u * (width - 1);
            var sampleY = v * (height - 1);
            var x0 = Mathf.FloorToInt(sampleX);
            var y0 = Mathf.FloorToInt(sampleY);
            var x1 = Mathf.Min(x0 + 1, width - 1);
            var y1 = Mathf.Min(y0 + 1, height - 1);
            var bottom = Mathf.Lerp(heights[y0, x0], heights[y0, x1], sampleX - x0);
            var top = Mathf.Lerp(heights[y1, x0], heights[y1, x1], sampleX - x0);
            return Mathf.Lerp(bottom, top, sampleY - y0);
        }

        private static bool IsValidAlphamapResolution(int value)
        {
            return value == 16 || value == 32 || value == 64 || value == 128 || value == 256 ||
                   value == 512 || value == 1024 || value == 2048;
        }

        private sealed class SurfaceRefreshPlan
        {
            internal SurfaceRefreshPlan(TerrainData data, string path, float[,,] weights)
            {
                Data = data;
                Path = path;
                Weights = weights;
            }

            internal TerrainData Data { get; }
            internal string Path { get; }
            internal float[,,] Weights { get; }
        }

        private sealed class SurfaceBackup
        {
            private readonly TerrainData data;
            private readonly TerrainLayer[] layers;
            private readonly int alphamapResolution;
            private readonly float[,,] weights;

            internal SurfaceBackup(TerrainData data)
            {
                this.data = data;
                layers = data.terrainLayers.ToArray();
                alphamapResolution = data.alphamapResolution;
                weights = data.alphamapLayers == 0
                    ? null
                    : data.GetAlphamaps(0, 0, alphamapResolution, alphamapResolution);
            }

            internal void Restore()
            {
                if (weights == null)
                {
                    data.terrainLayers = Array.Empty<TerrainLayer>();
                    return;
                }
                data.alphamapResolution = alphamapResolution;
                data.terrainLayers = layers;
                data.SetAlphamaps(0, 0, weights);
            }
        }

        private static void ValidateMapAssets(MapTerrainGenerationProfile profile, string mapRoot)
        {
            for (var row = 1; row <= profile.Rows; row++)
            for (var column = 1; column <= profile.Columns; column++)
            {
                var name = TileName(profile.MapName, row, column);
                var dataPath = mapRoot + "/TerrainData/" + name + ".asset";
                var scenePath = mapRoot + "/Scenes/" + name + ".unity";
                var expectedData = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
                if (expectedData == null) throw new InvalidOperationException("TerrainData 校验失败：" + dataPath);
                if ((profile.SurfaceLayers ?? Array.Empty<MapTerrainSurfaceLayerRule>()).Length > 0)
                    ValidateSurfaceData(profile, expectedData, dataPath);
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                try
                {
                    var contexts = FindInScene<MapContext>(scene);
                    var contextError = contexts.Count == 1 ? string.Empty : "MapContext 数量=" + contexts.Count;
                    if (contexts.Count != 1 || !contexts[0].Validate(out contextError))
                        throw new InvalidOperationException("场景必须且只能包含一个有效 MapContext：" + scenePath + " " + contextError);
                    var root = scene.GetRootGameObjects().SingleOrDefault(item => item.name == "MapRoot");
                    var content = root == null ? null : root.transform.Find("MapContentRoot");
                    var generated = content == null ? null : content.Find("GeneratedTerrainRoot");
                    var authored = content == null ? null : content.Find("HandAuthoredContentRoot");
                    var dynamicRoot = content == null ? null : content.Find("DynamicObjectRoot");
                    var marker = generated == null ? null : generated.GetComponent<GeneratedTerrainTile>();
                    if (root == null || content == null || generated == null || authored == null || dynamicRoot == null ||
                        content.gameObject.activeSelf || marker == null || marker.Row != row || marker.Column != column ||
                        marker.TotalRows != profile.Rows || marker.TotalColumns != profile.Columns ||
                        marker.GeneratorVersion != GeneratorVersion || marker.Terrain == null ||
                        marker.Terrain.terrainData != expectedData ||
                        marker.Terrain.GetComponent<TerrainCollider>()?.terrainData != expectedData)
                        throw new InvalidOperationException("场景结构或地形标记校验失败：" + scenePath);
                }
                finally
                {
                    if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static List<T> FindInScene<T>(Scene scene) where T : Component
        {
            var values = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) values.AddRange(root.GetComponentsInChildren<T>(true));
            return values;
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var value = new GameObject(name);
            value.transform.SetParent(parent, false);
            return value;
        }

        private static void SetObjectReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field) ?? throw new InvalidOperationException("缺少序列化字段：" + field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(UnityEngine.Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(UnityEngine.Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(UnityEngine.Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path, List<string> cleanupTargets)
        {
            path = path.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent, cleanupTargets);
            var name = path.Substring(path.LastIndexOf('/') + 1);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
                throw new InvalidOperationException("创建目录失败：" + path);
            cleanupTargets.Add(path);
        }

        private static void Cleanup(List<string> paths, List<string> unremoved)
        {
            for (var index = paths.Count - 1; index >= 0; index--)
            {
                var path = paths[index];
                try
                {
                    if (testCleanupFailureCallback != null && testCleanupFailureCallback(path))
                    {
                        unremoved.Add(path);
                        continue;
                    }
                    if ((AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null) &&
                        !AssetDatabase.DeleteAsset(path)) unremoved.Add(path);
                }
                catch
                {
                    unremoved.Add(path);
                }
            }
            AssetDatabase.Refresh();
        }

        private static string ValidateMapName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 48) return "mapName 长度必须为 1..48。";
            if (!string.Equals(name.Normalize(NormalizationForm.FormC), name, StringComparison.Ordinal))
                return "mapName 必须已经是 Unicode Form C。";
            if (!char.IsLetterOrDigit(name[0])) return "mapName 首字符必须是字母或数字。";
            for (var i = 1; i < name.Length; i++)
                if (!char.IsLetterOrDigit(name[i]) && name[i] != '_' && name[i] != '-')
                    return "mapName 只能包含字母、数字、下划线或短横线，且不能包含空白或路径字符。";
            if (ReservedNames.Contains(name)) return "mapName 不能使用 Windows 保留名：" + name;
            return string.Empty;
        }

        private static string ValidateOutputRootOverride()
        {
            if (string.IsNullOrEmpty(outputRootOverride)) return string.Empty;
            var normalized = outputRootOverride.Replace('\\', '/').TrimEnd('/');
            if (normalized.Equals(TestOutputRoot, StringComparison.Ordinal) ||
                normalized.StartsWith(TestOutputRoot + "/", StringComparison.Ordinal)) return string.Empty;
            return "测试输出根覆盖只允许位于 " + TestOutputRoot + "/ 及其子目录。";
        }

        private static string GetOutputRoot()
        {
            return string.IsNullOrEmpty(outputRootOverride)
                ? ProductOutputRoot
                : outputRootOverride.Replace('\\', '/').TrimEnd('/');
        }

        private static string ToAbsolutePath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
        }

        private static bool IsFinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
        private static string TileName(string mapName, int row, int column) => mapName + "_" + row.ToString(CultureInfo.InvariantCulture) + "_" + column.ToString(CultureInfo.InvariantCulture);
        private static MapTerrainValidationResult Valid() => new MapTerrainValidationResult(true, string.Empty);
        private static MapTerrainValidationResult Invalid(string error) => new MapTerrainValidationResult(false, error);
        private static MapTerrainGenerationResult Failure(string error, IReadOnlyList<string> unremoved) => new MapTerrainGenerationResult(false, string.IsNullOrEmpty(error) ? "地图地形生成失败。" : error, Array.Empty<string>(), unremoved);

        private static HashSet<string> BuildReservedNames()
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "con", "prn", "aux", "nul" };
            for (var i = 1; i <= 9; i++) { values.Add("com" + i); values.Add("lpt" + i); }
            return values;
        }

        private sealed class TilePlan
        {
            internal TilePlan(int row, int column, float[,] heights) { Row = row; Column = column; Heights = heights; }
            internal int Row { get; }
            internal int Column { get; }
            internal float[,] Heights { get; }
        }

        private sealed class GenerationStoppedException : Exception
        {
            internal GenerationStoppedException(string message) : base(message) { }
        }
    }
}

using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Game
{
    /// <summary>
    /// 从保留的 DEM 源地形提取卫星截图覆盖的局部范围，生成独立场景。
    /// 仅负责编辑器生成，不注册玩法地图，也不改写源地形或标注来源。
    /// </summary>
    public static class MapLocalPreviewGenerator
    {
        public const float OriginX = 720f;
        public const float OriginZ = 976f;
        public const float WorldSize = 768f;
        public const string OutputRoot = "Assets/Game/Scenes/GeneratedMaps/HeJiaVillageLocalPreview";
        public const string ScenePath = OutputRoot + "/HeJiaVillageLocalPreview.unity";

        private const int HeightResolution = 513;
        private const int SurfaceResolution = 256;
        private const int OverlayVerticesPerAxis = 129;
        private const float SourceTileSize = 1024f;
        private const float TerrainHeight = 330f;
        private const string SourceRoot = "Assets/Game/MapTerrainSources/HeJiaVillage/TerrainData/";

        /// <summary>生成前验证锁定的底图、现有 DEM 块及目标目录；已有预览拒绝覆盖。</summary>
        public static bool TryPreflight(MapRegionAnnotationAsset annotation, out string error)
        {
            if (annotation == null || !EditorUtility.IsPersistent(annotation) || EditorUtility.IsDirty(annotation) ||
                !annotation.Validate(out error))
            {
                error = annotation == null ? "请先保存区域标注资产。" : "请先保存有效的区域标注资产。";
                return false;
            }
            if (annotation.ImageWidth != HeJiaVillageAnnotationFrame.ImageWidth ||
                annotation.ImageHeight != HeJiaVillageAnnotationFrame.ImageHeight ||
                !string.Equals(annotation.BaseImageProjectPath,
                    MapRegionAnnotationWindow.DefaultBaseImageProjectPath, StringComparison.Ordinal) ||
                !string.Equals(annotation.BaseImageSha256,
                    MapRegionAnnotationWindow.DefaultBaseImageSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = "区域标注未绑定当前已确认的局部卫星图。";
                return false;
            }
            if (AssetDatabase.IsValidFolder(OutputRoot) ||
                AssetDatabase.LoadMainAssetAtPath(OutputRoot) != null)
            {
                error = "预览目录已存在，已拒绝覆盖：" + OutputRoot;
                return false;
            }
            TerrainLayer[] expectedLayers = null;
            for (var row = 1; row <= 2; row++)
            for (var column = 1; column <= 2; column++)
            {
                var path = SourceRoot + "HeJiaVillage_" + row + "_" + column + ".asset";
                var source = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                if (source == null || source.heightmapResolution != HeightResolution ||
                    !Mathf.Approximately(source.size.x, SourceTileSize) ||
                    !Mathf.Approximately(source.size.y, TerrainHeight) ||
                    !Mathf.Approximately(source.size.z, SourceTileSize) ||
                    source.terrainLayers == null || source.terrainLayers.Length == 0)
                {
                    error = "原 DEM 地形块缺失或规格不符：" + path;
                    return false;
                }
                if (expectedLayers == null) expectedLayers = source.terrainLayers;
                if (source.terrainLayers.Length != expectedLayers.Length)
                {
                    error = "原地形块的地表层数量不一致：" + path;
                    return false;
                }
                for (var layer = 0; layer < expectedLayers.Length; layer++)
                    if (source.terrainLayers[layer] != expectedLayers[layer])
                    {
                        error = "原地形块的地表层顺序不一致：" + path;
                        return false;
                    }
            }
            error = string.Empty;
            return true;
        }

        /// <summary>只创建一份独立预览；失败时清理本次新建资产并恢复原先活动场景。</summary>
        public static bool TryGenerate(MapRegionAnnotationAsset annotation, out string error)
        {
            if (!TryPreflight(annotation, out error)) return false;
            var originalActive = SceneManager.GetActiveScene();
            var sources = LoadSources();
            Scene previewScene = default;
            var createdOutput = false;
            try
            {
                var data = CreateTerrainData(sources);
                var texture = CreateOverlayTexture(annotation);
                var mesh = CreateOverlayMesh(data);
                var material = CreateOverlayMaterial(texture);
                var folderGuid = AssetDatabase.CreateFolder("Assets/Game/Scenes/GeneratedMaps",
                    "HeJiaVillageLocalPreview");
                createdOutput = true;
                if (AssetDatabase.GUIDToAssetPath(folderGuid) != OutputRoot)
                    throw new InvalidOperationException("无法建立预览目录。 ");
                AssetDatabase.CreateAsset(data, OutputRoot + "/TerrainData.asset");
                AssetDatabase.CreateAsset(texture, OutputRoot + "/AnnotationOverlay.asset");
                AssetDatabase.CreateAsset(mesh, OutputRoot + "/AnnotationSurface.asset");
                AssetDatabase.CreateAsset(material, OutputRoot + "/AnnotationOverlayMaterial.mat");

                previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var root = new GameObject("LocalMapPreviewRoot");
                SceneManager.MoveGameObjectToScene(root, previewScene);
                var terrainObject = Terrain.CreateTerrainGameObject(data);
                terrainObject.name = "Terrain";
                terrainObject.transform.SetParent(root.transform, false);
                var overlay = new GameObject("RegionAnnotationPreview");
                overlay.transform.SetParent(root.transform, false);
                overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                overlay.AddComponent<MeshRenderer>().sharedMaterial = material;
                var lightObject = new GameObject("PreviewDirectionalLight");
                lightObject.transform.SetParent(root.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                if (!EditorSceneManager.SaveScene(previewScene, ScenePath))
                    throw new InvalidOperationException("预览场景保存失败。 ");
                AssetDatabase.SaveAssets();
                error = ScenePath;
                return true;
            }
            catch (Exception exception)
            {
                error = "预览地图生成失败：" + exception.Message;
                if (previewScene.IsValid() && previewScene.isLoaded)
                    EditorSceneManager.CloseScene(previewScene, true);
                if (createdOutput && !AssetDatabase.DeleteAsset(OutputRoot))
                    error += "；预览目录清理失败：" + OutputRoot;
                return false;
            }
            finally
            {
                if (previewScene.IsValid() && previewScene.isLoaded)
                    EditorSceneManager.CloseScene(previewScene, true);
                if (originalActive.IsValid() && originalActive.isLoaded)
                    SceneManager.SetActiveScene(originalActive);
            }
        }

        private static TerrainData[,] LoadSources()
        {
            var sources = new TerrainData[2, 2];
            for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++)
                sources[row, column] = AssetDatabase.LoadAssetAtPath<TerrainData>(SourceRoot +
                    "HeJiaVillage_" + (row + 1) + "_" + (column + 1) + ".asset");
            return sources;
        }

        private static TerrainData CreateTerrainData(TerrainData[,] sources)
        {
            var data = new TerrainData
            {
                heightmapResolution = HeightResolution,
                size = new Vector3(WorldSize, TerrainHeight, WorldSize),
                alphamapResolution = SurfaceResolution,
                terrainLayers = sources[0, 0].terrainLayers
            };
            var heights = new float[HeightResolution, HeightResolution];
            for (var z = 0; z < HeightResolution; z++)
            for (var x = 0; x < HeightResolution; x++)
            {
                var worldX = OriginX + WorldSize * x / (HeightResolution - 1f);
                var worldZ = OriginZ + WorldSize * z / (HeightResolution - 1f);
                var source = SourceAt(sources, worldX, worldZ, out var u, out var v);
                heights[z, x] = Mathf.Clamp01(source.GetInterpolatedHeight(u, v) / TerrainHeight);
            }
            data.SetHeights(0, 0, heights);

            var layerCount = data.terrainLayers.Length;
            var sourceWeights = new float[2, 2][,,];
            for (var row = 0; row < 2; row++)
            for (var column = 0; column < 2; column++)
                sourceWeights[row, column] = sources[row, column].GetAlphamaps(0, 0,
                    sources[row, column].alphamapWidth, sources[row, column].alphamapHeight);
            var weights = new float[SurfaceResolution, SurfaceResolution, layerCount];
            for (var z = 0; z < SurfaceResolution; z++)
            for (var x = 0; x < SurfaceResolution; x++)
            {
                var worldX = OriginX + WorldSize * (x + 0.5f) / SurfaceResolution;
                var worldZ = OriginZ + WorldSize * (z + 0.5f) / SurfaceResolution;
                SourceAt(sources, worldX, worldZ, out var u, out var v,
                    out var row, out var column);
                var source = sourceWeights[row, column];
                if (source.GetLength(2) != layerCount)
                    throw new InvalidOperationException("原地形块的地表层数量不一致。 ");
                var sourceX = Mathf.Clamp(Mathf.FloorToInt(u * source.GetLength(1)), 0,
                    source.GetLength(1) - 1);
                var sourceZ = Mathf.Clamp(Mathf.FloorToInt(v * source.GetLength(0)), 0,
                    source.GetLength(0) - 1);
                for (var layer = 0; layer < layerCount; layer++)
                    weights[z, x, layer] = source[sourceZ, sourceX, layer];
            }
            data.SetAlphamaps(0, 0, weights);
            return data;
        }

        private static TerrainData SourceAt(TerrainData[,] sources, float x, float z,
            out float u, out float v)
        {
            return SourceAt(sources, x, z, out u, out v, out _, out _);
        }

        private static TerrainData SourceAt(TerrainData[,] sources, float x, float z,
            out float u, out float v, out int row, out int column)
        {
            column = Mathf.Clamp(Mathf.FloorToInt(x / SourceTileSize), 0, 1);
            row = Mathf.Clamp(Mathf.FloorToInt(z / SourceTileSize), 0, 1);
            u = Mathf.Clamp01((x - column * SourceTileSize) / SourceTileSize);
            v = Mathf.Clamp01((z - row * SourceTileSize) / SourceTileSize);
            return sources[row, column];
        }

        private static Texture2D CreateOverlayTexture(MapRegionAnnotationAsset annotation)
        {
            var width = annotation.ImageWidth;
            var height = annotation.ImageHeight;
            var texture = new Texture2D(width + 2, height + 2, TextureFormat.RGBA32, false, true)
            {
                name = "局部地图区域标注预览",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[(width + 2) * (height + 2)];
            var coverage = new byte[5][];
            for (var kind = 0; kind < coverage.Length; kind++)
                coverage[kind] = MapRegionAnnotationRasterizer.BuildCoverage(annotation,
                    (MapRegionLayerKind)kind);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (coverage[(int)MapRegionLayerKind.Uncertain][index] != 0) continue;
                Color32 color = default;
                if (coverage[(int)MapRegionLayerKind.Water][index] != 0)
                    color = new Color32(30, 133, 238, 170);
                else if (coverage[(int)MapRegionLayerKind.Road][index] != 0)
                    color = new Color32(220, 113, 39, 180);
                else if (coverage[(int)MapRegionLayerKind.Vegetation][index] != 0)
                    color = new Color32(36, 176, 64, 135);
                else if (coverage[(int)MapRegionLayerKind.Farmland][index] != 0)
                    color = new Color32(221, 180, 53, 130);
                pixels[(height - y) * (width + 2) + x + 1] = color;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Mesh CreateOverlayMesh(TerrainData data)
        {
            var count = OverlayVerticesPerAxis;
            var vertices = new Vector3[count * count];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(count - 1) * (count - 1) * 6];
            for (var z = 0; z < count; z++)
            for (var x = 0; x < count; x++)
            {
                var u = x / (float)(count - 1);
                var v = z / (float)(count - 1);
                var index = z * count + x;
                vertices[index] = new Vector3(u * WorldSize,
                    data.GetInterpolatedHeight(u, v) + 0.35f, v * WorldSize);
                var world = new Vector2(OriginX + u * WorldSize, OriginZ + v * WorldSize);
                if (!HeJiaVillageAnnotationFrame.TryWorldToImage(world, out var imagePoint)) continue;
                uv[index] = new Vector2((imagePoint.x + 1.5f) /
                    (HeJiaVillageAnnotationFrame.ImageWidth + 2f),
                    (HeJiaVillageAnnotationFrame.ImageHeight - imagePoint.y + 0.5f) /
                    (HeJiaVillageAnnotationFrame.ImageHeight + 2f));
            }
            var cursor = 0;
            for (var z = 0; z < count - 1; z++)
            for (var x = 0; x < count - 1; x++)
            {
                var lowerLeft = z * count + x;
                triangles[cursor++] = lowerLeft;
                triangles[cursor++] = lowerLeft + count;
                triangles[cursor++] = lowerLeft + 1;
                triangles[cursor++] = lowerLeft + 1;
                triangles[cursor++] = lowerLeft + count;
                triangles[cursor++] = lowerLeft + count + 1;
            }
            var mesh = new Mesh { name = "局部地图区域标注贴地预览", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateOverlayMaterial(Texture2D texture)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("找不到 URP Unlit Shader。 ");
            var material = new Material(shader)
            {
                name = "局部地图区域标注预览材质",
                renderQueue = (int)RenderQueue.Transparent
            };
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}

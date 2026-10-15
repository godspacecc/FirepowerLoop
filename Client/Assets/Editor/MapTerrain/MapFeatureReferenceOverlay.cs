using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game
{
    /// <summary>在正式局部 Terrain 上只读显示全图地物参考；不创建场景对象或修改地形数据。</summary>
    public sealed class MapFeatureReferenceOverlay : IDisposable
    {
        public enum FeatureLayer
        {
            Forest,
            BuiltUp,
            Water,
            Roads
        }

        public const int GridVertexCountPerAxis = 129;
        public const float HeightOffset = 0.3f;
        public const string ReferenceRoot =
            "Assets/Game/MapTerrainSources/HeJiaVillage/FeatureReferences/";

        private static readonly IReadOnlyDictionary<FeatureLayer, string> LayerPaths =
            new Dictionary<FeatureLayer, string>
            {
                { FeatureLayer.Forest, ReferenceRoot + "HeJiaVillage_ForestReference.png" },
                { FeatureLayer.BuiltUp, ReferenceRoot + "HeJiaVillage_BuiltUpReference.png" },
                { FeatureLayer.Water, ReferenceRoot + "HeJiaVillage_WaterReference.png" },
                { FeatureLayer.Roads, ReferenceRoot + "HeJiaVillage_RoadReference.png" }
            };

        private Mesh mesh;
        private Material material;
        // 当前预览绑定的局部 Terrain；Release 后置空，避免继续绘制已关闭场景。
        private Terrain terrain;

        public Mesh PreviewMesh => mesh;
        public Material PreviewMaterial => material;
        public Terrain Terrain => terrain;
        public bool HasDrawableResources => mesh != null && material != null && terrain != null;

        public static string GetTexturePath(FeatureLayer layer)
        {
            return LayerPaths.TryGetValue(layer, out var path) ? path : string.Empty;
        }

        /// <summary>优先使用选中的局部 Terrain；未选中 Terrain 时从已加载的正式局部场景查找。</summary>
        public static bool TryResolveTerrain(GameObject selectedObject, out Terrain resolved, out string error)
        {
            resolved = null;
            if (selectedObject != null)
            {
                var selectedTerrain = selectedObject.GetComponentInParent<Terrain>(true);
                if (selectedTerrain != null)
                {
                    if (!TryValidateTerrain(selectedTerrain, out error)) return false;
                    resolved = selectedTerrain;
                    return true;
                }
            }

            var scene = SceneManager.GetSceneByPath(MapLocalPreviewGenerator.ScenePath);
            var candidates = new List<Terrain>();
            if (scene.IsValid() && scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var candidate in root.GetComponentsInChildren<Terrain>(true))
                        if (TryValidateTerrain(candidate, out _)) candidates.Add(candidate);

            if (candidates.Count == 1)
            {
                resolved = candidates[0];
                error = string.Empty;
                return true;
            }

            error = candidates.Count == 0
                ? "未找到局部地图 Terrain。请打开和家村局部场景。"
                : "局部场景中存在多个有效 Terrain，请先选中目标 Terrain。";
            return false;
        }

        /// <summary>仅接受正式局部场景的原点 Terrain，防止将全图纹理投到其它地图。</summary>
        public static bool TryValidateTerrain(Terrain candidate, out string error)
        {
            if (candidate == null)
            {
                error = "Terrain 为空。";
                return false;
            }
            var scene = candidate.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded ||
                !string.Equals(scene.path, MapLocalPreviewGenerator.ScenePath, StringComparison.Ordinal))
            {
                error = "目标必须位于正式和家村局部场景。";
                return false;
            }
            if (candidate.terrainData == null)
            {
                error = "Terrain 未引用 TerrainData。";
                return false;
            }
            var size = candidate.terrainData.size;
            if (!Mathf.Approximately(size.x, MapLocalPreviewGenerator.WorldSize) ||
                !Mathf.Approximately(size.z, MapLocalPreviewGenerator.WorldSize) ||
                candidate.terrainData.heightmapResolution != 513)
            {
                error = "局部 Terrain 必须为 768 米、513 高度图规格。";
                return false;
            }

            var terrainTransform = candidate.transform;
            if (terrainTransform.position.sqrMagnitude > 0.000001f)
            {
                error = "局部 Terrain 必须位于场景原点。";
                return false;
            }
            if (Quaternion.Angle(terrainTransform.rotation, Quaternion.identity) > 0.001f)
            {
                error = "Terrain Transform 必须没有旋转。";
                return false;
            }
            if ((terrainTransform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            {
                error = "Terrain Transform 缩放必须为 (1, 1, 1)。";
                return false;
            }

            error = string.Empty;
            return true;
        }

        /// <summary>构建贴地网格和临时材质；失败时释放本次预览资源。</summary>
        public bool TryBuild(Terrain candidate, FeatureLayer layer, float opacity, out string error)
        {
            Release();
            if (!TryValidateTerrain(candidate, out error)) return false;

            var path = GetTexturePath(layer);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                error = "无法加载参考图：" + path;
                return false;
            }

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                error = "找不到 Shader：Universal Render Pipeline/Unlit";
                return false;
            }

            try
            {
                mesh = CreateMesh(candidate);
                material = CreateMaterial(shader, texture, opacity);
                terrain = candidate;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                Release();
                error = "创建参考叠加预览失败：" + exception.Message;
                return false;
            }
        }

        public void SetOpacity(float opacity)
        {
            if (material == null) return;
            var color = Color.white;
            color.a = Mathf.Clamp(opacity, 0.05f, 1f);
            material.SetColor("_BaseColor", color);
        }

        public void Draw(SceneView sceneView)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || sceneView == null || !HasDrawableResources) return;
            if (!material.SetPass(0)) return;
            Graphics.DrawMeshNow(mesh, Matrix4x4.Translate(terrain.transform.position));
        }

        public void Release()
        {
            terrain = null;
            if (mesh != null) Object.DestroyImmediate(mesh);
            if (material != null) Object.DestroyImmediate(material);
            mesh = null;
            material = null;
        }

        public void Dispose()
        {
            Release();
        }

        private static Mesh CreateMesh(Terrain candidate)
        {
            var terrainData = candidate.terrainData;
            var vertexCount = GridVertexCountPerAxis * GridVertexCountPerAxis;
            var vertices = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            var triangles = new int[(GridVertexCountPerAxis - 1) * (GridVertexCountPerAxis - 1) * 6];
            var size = terrainData.size;
            var vertexIndex = 0;
            for (var zIndex = 0; zIndex < GridVertexCountPerAxis; zIndex++)
            {
                var normalizedZ = zIndex / (float)(GridVertexCountPerAxis - 1);
                var localZ = normalizedZ * size.z;
                for (var xIndex = 0; xIndex < GridVertexCountPerAxis; xIndex++)
                {
                    var normalizedX = xIndex / (float)(GridVertexCountPerAxis - 1);
                    var localX = normalizedX * size.x;
                    vertices[vertexIndex] = new Vector3(localX,
                        terrainData.GetInterpolatedHeight(normalizedX, normalizedZ) + HeightOffset, localZ);
                    // 参考纹理覆盖旧 2048 米 DEM；局部场景原点对应原图 (720, 976) 米。
                    var sourceSize = HeJiaVillageAnnotationFrame.TileWorldSizeMeters * 2f;
                    uv[vertexIndex] = new Vector2(
                        (MapLocalPreviewGenerator.OriginX + localX) / sourceSize,
                        (MapLocalPreviewGenerator.OriginZ + localZ) / sourceSize);
                    vertexIndex++;
                }
            }

            var triangleIndex = 0;
            for (var zIndex = 0; zIndex < GridVertexCountPerAxis - 1; zIndex++)
            for (var xIndex = 0; xIndex < GridVertexCountPerAxis - 1; xIndex++)
            {
                var bottomLeft = zIndex * GridVertexCountPerAxis + xIndex;
                var topLeft = bottomLeft + GridVertexCountPerAxis;
                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topLeft;
                triangles[triangleIndex++] = bottomLeft + 1;
                triangles[triangleIndex++] = bottomLeft + 1;
                triangles[triangleIndex++] = topLeft;
                triangles[triangleIndex++] = topLeft + 1;
            }

            var result = new Mesh
            {
                name = "HeJiaVillage Feature Reference Overlay",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32,
                vertices = vertices,
                uv = uv,
                triangles = triangles
            };
            result.RecalculateBounds();
            return result;
        }

        private static Material CreateMaterial(Shader shader, Texture2D texture, float opacity)
        {
            Material result = null;
            try
            {
                result = new Material(shader)
                {
                    name = "HeJiaVillage Feature Reference Overlay",
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = (int)RenderQueue.Transparent
                };
                result.SetFloat("_Surface", 1f);
                result.SetFloat("_Blend", 0f);
                result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                result.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                result.SetFloat("_ZWrite", 0f);
                result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                result.SetTexture("_BaseMap", texture);
                var color = Color.white;
                color.a = Mathf.Clamp(opacity, 0.05f, 1f);
                result.SetColor("_BaseColor", color);
                return result;
            }
            catch
            {
                if (result != null) Object.DestroyImmediate(result);
                throw;
            }
        }
    }
}

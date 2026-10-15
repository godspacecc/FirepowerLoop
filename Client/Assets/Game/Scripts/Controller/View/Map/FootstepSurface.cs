using UnityEngine;

namespace Game
{
    public enum FootstepSurfaceKind { Unknown, Grass, Dirt, Gravel, Mud, Water, Concrete }

    /// <summary>只标记地面的音色；覆盖区域必须为 Trigger，不承担移动碰撞或地图业务状态。</summary>
    [DisallowMultipleComponent]
    public sealed class FootstepSurface : MonoBehaviour
    {
        public FootstepSurfaceKind Kind = FootstepSurfaceKind.Dirt;
        public bool IsRegion;
        public int Priority;
        [Tooltip("覆盖区域的稳定身份；同优先级按此值排序。地面标记可留空。")]
        public string RegionId;
        [Tooltip("覆盖区域对应的原有地表网格；投影三角形避免包围盒把草地误算为土路。")]
        public MeshFilter SurfaceMesh;
        /// <summary>网格材质分区索引；-1 使用整个网格，旧场景保持原投影行为。</summary>
        public int SurfaceSubmesh = -1;
        /// <summary>浅水需同时检查脚底与水面高度，避免桥上和干岸误播涉水声。</summary>
        public bool CheckSurfaceHeight;
        // 世界米；允许脚底低于浅水面 0.45 米、高于水面 0.10 米的接触容差。
        public float MaximumBelowSurface = 0.45f;
        public float MaximumAboveSurface = 0.10f;
        /// <summary>可选 Terrain 支撑；按实际绘制层采样，不根据材质名字在运行时猜测。</summary>
        public Terrain TerrainSource;
        /// <summary>与 TerrainData.terrainLayers 同序；未配置或 Unknown 的主导层保持静音。</summary>
        public FootstepSurfaceKind[] TerrainLayerKinds = System.Array.Empty<FootstepSurfaceKind>();
        // 绘制权重只在该地图标记生命周期内缓存；更换 TerrainData 后重新读取，查询不逐帧分配。
        private TerrainData cachedTerrainData;
        private float[,,] terrainWeights;
        private Mesh cachedMesh;
        private int cachedSubmesh = int.MinValue;
        private Vector3[] vertices;
        private int[] triangles;

        /// <summary>最近真实支撑命中后调用；普通网格使用固定音色，Terrain 使用该落点的主导绘制层。</summary>
        public FootstepSurfaceKind ResolveKind(Vector3 worldPosition)
        {
            if (TerrainSource == null) return Kind;
            var data = TerrainSource.terrainData;
            if (data == null || data.alphamapLayers == 0 || data.size.x <= 0 || data.size.z <= 0)
                return FootstepSurfaceKind.Unknown;
            if (cachedTerrainData != data || terrainWeights == null)
            {
                cachedTerrainData = data;
                terrainWeights = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            }
            var local = TerrainSource.transform.InverseTransformPoint(worldPosition);
            if (local.x < 0 || local.z < 0 || local.x > data.size.x || local.z > data.size.z)
                return FootstepSurfaceKind.Unknown;
            int x = Mathf.Min((int)(local.x / data.size.x * data.alphamapWidth), data.alphamapWidth - 1);
            int z = Mathf.Min((int)(local.z / data.size.z * data.alphamapHeight), data.alphamapHeight - 1);
            int selected = -1; float weight = 0;
            for (int layer = 0; layer < terrainWeights.GetLength(2); layer++)
                if (terrainWeights[z, x, layer] > weight) { selected = layer; weight = terrainWeights[z, x, layer]; }
            return selected >= 0 && TerrainLayerKinds != null && selected < TerrainLayerKinds.Length
                ? TerrainLayerKinds[selected] : FootstepSurfaceKind.Unknown;
        }

        /// <summary>Trigger 粗筛后按原网格 XZ 投影判断；不可读网格不参与覆盖。</summary>
        public bool Contains(Vector3 worldPosition)
        {
            if (SurfaceMesh == null) return !CheckSurfaceHeight;
            var mesh = SurfaceMesh.sharedMesh;
            if (mesh == null || !mesh.isReadable) return false;
            if (SurfaceSubmesh < -1 || SurfaceSubmesh >= mesh.subMeshCount) return false;
            if (cachedMesh != mesh || cachedSubmesh != SurfaceSubmesh)
            {
                cachedMesh = mesh; cachedSubmesh = SurfaceSubmesh;
                vertices = mesh.vertices;
                triangles = SurfaceSubmesh < 0 ? mesh.triangles : mesh.GetTriangles(SurfaceSubmesh);
            }
            Vector3 point = SurfaceMesh.transform.InverseTransformPoint(worldPosition);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                float ab = Cross(b - a, point - a), bc = Cross(c - b, point - b), ca = Cross(a - c, point - c);
                float area = Cross(b - a, c - a);
                if (Mathf.Abs(area) < 0.000001f) continue;
                if (ab >= -0.00001f && bc >= -0.00001f && ca >= -0.00001f ||
                    ab <= 0.00001f && bc <= 0.00001f && ca <= 0.00001f)
                {
                    if (!CheckSurfaceHeight) return true;
                    // XZ 重心插值求真实斜水面高度，再换算世界米，不依赖网格包围盒高度。
                    float height = (bc * a.y + ca * b.y + ab * c.y) / area;
                    float surfaceY = SurfaceMesh.transform.TransformPoint(new Vector3(point.x, height, point.z)).y;
                    float delta = worldPosition.y - surfaceY;
                    if (delta >= -MaximumBelowSurface && delta <= MaximumAboveSurface) return true;
                }
            }
            return false;
        }
        private static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
    }
}

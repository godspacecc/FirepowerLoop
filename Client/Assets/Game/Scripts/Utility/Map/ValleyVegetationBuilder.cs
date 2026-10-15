using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>只装配河谷树形与无碰撞地被，使用已保存布局确定表现；不生成采集身份、收益、导航或存档。</summary>
    internal static class ValleyVegetationBuilder
    {
        /// <summary>在隐藏候选内替换树冠；保留原采集资源根、树干碰撞与遮挡组件，砍伐仍隐藏整个资源根。</summary>
        internal static void ApplyTree(CivilisationPointView view, ValleyPointData point, ValleyLayoutSaveData layout, ValleyGenerationKit kit)
        {
            var visual = view.HarvestedVisualRoot;
            if (visual == null) return;
            var prefabs = kit.TreeVisualPrefabs;
            if (prefabs != null && prefabs.Length > 0)
            {
                var p = point.Pose.ToVector();
                // 64米片区有主要树种，少量异种混生；不使用 string.GetHashCode 或全局随机状态，读档结果稳定。
                var patch = Hash(layout.Seed, Mathf.FloorToInt(p.x / 64), Mathf.FloorToInt(p.z / 64));
                var detail = Hash(layout.Seed, Mathf.RoundToInt(p.x * 10), Mathf.RoundToInt(p.z * 10));
                var index = (int)((detail % 5 == 0 ? detail : patch) % (uint)prefabs.Length);
                var source = kit.ConiferVisualPrefab!=null&&detail%3!=0?kit.ConiferVisualPrefab:prefabs[index];
                if (source == null) throw new InvalidOperationException("河谷树形引用缺失");
                var lod = visual.GetComponent<LODGroup>();
                if (lod != null) GeneratedValleyContentHandle.DestroyOwned(lod);
                foreach (Transform child in visual.transform.Cast<Transform>().ToArray())
                {
                    child.gameObject.SetActive(false);
                    GeneratedValleyContentHandle.DestroyOwned(child.gameObject);
                }
                var crown = UnityEngine.Object.Instantiate(source, visual.transform);
                crown.name = source.name;
                crown.transform.localPosition = Vector3.zero;
                crown.transform.localRotation = Quaternion.identity;
                crown.transform.localScale = Vector3.one;
                crown.SetActive(true);
                var occluder = visual.GetComponent<CameraOccluderView>();
                if (occluder != null) { occluder.BodyRenderers = crown.GetComponentsInChildren<Renderer>(true); occluder.InvalidateOcclusionBounds(); }
            }
            if (point.Id.StartsWith("generated.valley.forest."))
                visual.transform.localScale *= 2.3f + .8f * point.Pose.Yaw / 360;
            foreach (var group in visual.GetComponentsInChildren<LODGroup>(true))
            {
                var levels = group.GetLODs();
                if (levels.Length == 0) continue;
                levels[levels.Length - 1].screenRelativeTransitionHeight = .001f;
                group.SetLODs(levels); group.RecalculateBounds();
            }
        }

        /// <summary>按64米地块合并草灌，只生成网格；候选取消或卸载时由同一地图句柄释放所有网格。</summary>
        internal static async UniTask BuildGroundCoverAsync(GeneratedValleyContentHandle handle, ValleyGenerationKit kit, CancellationToken token, Action<float> onProgress = null)
        {
            if (kit.ShrubPrefabs == null || kit.GrassPrefabs == null || kit.ShrubPrefabs.Length == 0 || kit.GrassPrefabs.Length == 0) return;
            var layout = handle.Layout;
            var root = new GameObject("ValleyGroundCover"); root.transform.SetParent(handle.GeneratedRoot.transform, false);
            var groups = new Dictionary<(int x, int z, bool shrub, Material material), List<CombineInstance>>();
            var clearings = layout.Points.Where(p => !p.TemplateId.StartsWith("tree.")).Select(p => p.Pose.ToVector())
                .Concat(layout.Clues.Select(c => c.Pose.ToVector())).ToArray();
            var barrier = Vector3.Lerp(layout.Shortcut[0].ToVector(), layout.Shortcut[1].ToVector(), .8f);
            var count = 0;
            var trees = layout.Points.Where(p => p.TemplateId.StartsWith("tree.")).ToArray();
            // 正式资源名限定低矮组合；缺少该变体时仍沿用现有调色板，不新增资源依赖。
            var lowShrubIndex = Array.FindIndex(kit.ShrubPrefabs, p => p != null && p.name == "Bush_03");
            var tuftGrassIndex = Array.FindIndex(kit.GrassPrefabs, p => p != null && p.name == "Grass_02");
            foreach (var point in trees)
            {
                var center = point.Pose.ToVector();
                var random = new System.Random((int)(Hash(layout.Seed, Mathf.RoundToInt(center.x * 10), Mathf.RoundToInt(center.z * 10)) & 0x7fffffff));
                // 草灌集中在部分林地，保留空隙；同一局不依赖遍历顺序或全局 Unity 随机数。
                var patch = Hash(layout.Seed + 71, Mathf.FloorToInt(center.x / 48), Mathf.FloorToInt(center.z / 48));
                if (++count % 64 == 0) { onProgress?.Invoke(.6f * count / trees.Length); await UniTask.Yield(); token.ThrowIfCancellationRequested(); }
                if (patch % 4 == 0) continue;
                if (random.NextDouble() < .3)
                {
                    // 同一簇共享中心，草环绕低灌木；限制簇数以保持原有地被顶点预算。
                    var angle = (float)random.NextDouble() * Mathf.PI * 2;
                    var cluster = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 4.5f;
                    Add(cluster, true, random, lowShrubIndex, .6f);
                    for (var i = 0; i < 5; i++) Add(cluster, false, random, tuftGrassIndex, 2.2f);
                }
                else Add(center, false, random, tuftGrassIndex);

            }
            // 河岸有独立草带；浅滩与取水站位仍留白，不以树木位置决定整个河岸的地被。
            for (var i = 1; i < layout.River.Count; i++)
            {
                var a = layout.River[i - 1].ToVector(); var b = layout.River[i].ToVector();
                var direction = b - a; direction.y = 0;
                var normal = Vector3.Cross(Vector3.up, direction.normalized);
                var steps = Mathf.CeilToInt(direction.magnitude / 7);
                for (var j = 0; j < steps; j++)
                {
                    var center = Vector3.Lerp(a, b, (j + .5f) / steps);
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var random = new System.Random((int)(Hash(layout.Seed + i, j, side) & 0x7fffffff));
                        Add(center + normal * (side * 10), false, random);
                        Add(center + normal * (side * 15), false, random);
                    }
                }
                onProgress?.Invoke(.6f + .1f * i / (layout.River.Count - 1));
                await UniTask.Yield(); token.ThrowIfCancellationRequested();
            }
            count = 0;
            foreach (var entry in groups)
            {
                var name = (entry.Key.shrub ? "ShrubChunk_" : "GrassChunk_") + entry.Key.x + "_" + entry.Key.z;
                var mesh = new Mesh { name = name };
                // 源包网格很小，每块最多数百簇；超限明确失败，避免手机端16位索引溢出。
                if (entry.Value.Sum(c => c.mesh.vertexCount) > 65535) { GeneratedValleyContentHandle.DestroyOwned(mesh); throw new InvalidOperationException("河谷地被合批超过16位网格上限"); }
                handle.Meshes.Add(mesh);
                mesh.CombineMeshes(entry.Value.ToArray(), true, true); mesh.RecalculateBounds();
                // 渲染只需GPU网格；运行时上传后丢弃CPU副本，编辑态保留以供定向验证与美术检查。
                mesh.UploadMeshData(Application.isPlaying);
                var go = new GameObject(name); go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(entry.Key.x * 64 - 512, 0, entry.Key.z * 64 - 512);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = entry.Key.material;
                renderer.shadowCastingMode = entry.Key.shrub ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (++count % 8 == 0) { onProgress?.Invoke(.7f + .3f * count / groups.Count); await UniTask.Yield(); token.ThrowIfCancellationRequested(); }
            }

            onProgress?.Invoke(1f);

            // 紧凑簇使用指定变体与散布半径（米）；每个成员仍独立检查地形和交互留白。
            void Add(Vector3 center, bool shrub, System.Random random, int preferredIndex = -1, float spreadRadius = 6.5f)
            {
                var angle = (float)random.NextDouble() * Mathf.PI * 2;
                var radius = spreadRadius < 6.5f ? (float)random.NextDouble() * spreadRadius : 2.5f + (float)random.NextDouble() * 4;
                var p = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                if (Mathf.Abs(p.x) > 470 || Mathf.Abs(p.z) > 470 || !CanPlace(p)) return;
                p.y = layout.HeightAt(p.x, p.z) - .03f;
                var prefabs = shrub ? kit.ShrubPrefabs : kit.GrassPrefabs;
                var riverDistance = ValleyLayoutGenerator.Distance(p, layout.River, out _);
                var index = preferredIndex >= 0 ? preferredIndex : riverDistance < 24 ? 0 : random.Next(prefabs.Length);
                var prefab = prefabs[index];
                if (prefab == null) throw new InvalidOperationException("河谷地被引用缺失");
                var scale = shrub ? 1.5f + (float)random.NextDouble() * 1.4f : 3.5f + (float)random.NextDouble() * 2;
                var yaw = (float)random.NextDouble() * 360;
                var x = Mathf.FloorToInt((p.x + 512) / 64); var z = Mathf.FloorToInt((p.z + 512) / 64);
                var origin = new Vector3(x * 64 - 512, 0, z * 64 - 512);
                var matrix = Matrix4x4.TRS(p - origin, Quaternion.Euler(0, yaw, 0), Vector3.one * scale);
                var lod = prefab.GetComponent<LODGroup>();
                // 低矮草灌在俯视镜头下复用原包简化LOD，避免数千簇复制最高精度顶点。
                var renderers = lod != null ? lod.GetLODs()[lod.lodCount - 1].renderers : prefab.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (!(renderer is MeshRenderer) || !renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                    if (!filter.sharedMesh.isReadable) throw new InvalidOperationException("河谷地被网格未开启读写：" + filter.sharedMesh.name);
                    var materials = renderer.sharedMaterials;
                    for (var sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
                    {
                        if (sub >= materials.Length || materials[sub] == null) throw new InvalidOperationException("河谷地被缺少材质");
                        var key = (x, z, shrub, materials[sub]);
                        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = sub,
                            transform = matrix * prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                    }
                }
            }
            bool CanPlace(Vector3 p)
            {
                if (ValleyLayoutGenerator.Distance(p, layout.River, out _) < 7.5f || Horizontal(p, layout.Spawn.ToVector()) < 18 ||
                    Horizontal(p, layout.Camp.ToVector()) < 22 || Horizontal(p, barrier) < 30 ||
                    layout.Fords.Any(f => Horizontal(p, f.ToVector()) < 14) || clearings.Any(c => Horizontal(p, c) < 5)) return false;
                var dx = (layout.HeightAt(p.x + 4, p.z) - layout.HeightAt(p.x - 4, p.z)) / 8;
                var dz = (layout.HeightAt(p.x, p.z + 4) - layout.HeightAt(p.x, p.z - 4)) / 8;
                return dx * dx + dz * dz < .22f; // 约25度以下的缓坡，避免陡岸上明显悬空。
            }
        }
        private static float Horizontal(Vector3 a, Vector3 b) { a.y = b.y = 0; return Vector3.Distance(a, b); }
        /// <summary>固定整数散列只派生表现随机数；不影响原布局种子、点位和领域随机状态。</summary>
        private static uint Hash(int seed, int x, int z)
        {
            unchecked { var h = (uint)seed * 747796405u ^ (uint)x * 2891336453u ^ (uint)z * 277803737u;
                h = (h ^ (h >> 16)) * 2246822519u; h = (h ^ (h >> 13)) * 3266489917u; return h ^ (h >> 16); }
        }
    }
}

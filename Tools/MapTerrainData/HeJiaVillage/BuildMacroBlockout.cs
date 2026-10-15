using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 从已保存的地图依据生成四块场景的粗布局占位物。只负责可替换的静态表现，
/// 不修改 Terrain 高度、来源数据或地图运行对象。
/// 此文件由 Unity MCP 的 script-execute 执行，不编入游戏程序集。
/// </summary>
public static class HeJiaVillageMacroBlockout
{
    private const string MapAssetRoot = "Assets/Game/MapTerrainSources/HeJiaVillage";
    private const string BlockoutAssetRoot = "Assets/Game/Art/Environment/HeJiaVillage/MacroBlockout";
    private const string SceneRoot = "Assets/Game/Scenes/GeneratedMaps/HeJiaVillage";
    private const string TreePrefabPath = "Assets/Game/Art/Environment/HeJiaVillage/Nature/Prefabs/HeJia_Pine_Tree_03.prefab";
    private const string BroadleafPrefabPath = "Assets/Game/Art/Environment/HeJiaVillage/Nature/Prefabs/HeJia_Fruit_Tree_01.prefab";

    private struct Segment { public Vector2 A, B; }
    private struct Pond { public string Id; public Vector2 Center, Radius; }
    private struct TreePlacement { public Vector2 Position; public bool Broadleaf; }
    private struct BuildingPlacement { public Vector2 Position; public float Width, Depth, Height, Yaw; }

    private sealed class Inputs
    {
        public readonly List<Segment> Roads = new List<Segment>();
        public readonly List<Pond> Ponds = new List<Pond>();
        public readonly List<TreePlacement> Trees = new List<TreePlacement>();
        public readonly List<BuildingPlacement> Buildings = new List<BuildingPlacement>();
        public TerrainData[,] Terrain = new TerrainData[2, 2];
        public GameObject PinePrefab, BroadleafPrefab;
    }

    /// <summary>只读取来源并统计数量；不会新建或保存资源与场景。</summary>
    public static void DryRun()
    {
        var input = ReadInputs();
        for (int row = 1; row <= 2; row++)
        for (int col = 1; col <= 2; col++)
        {
            int c = col, r = row;
            Debug.Log($"Macro dry-run {col}_{row}: ponds={input.Ponds.FindAll(p => TileCol(p.Center.x) == c && TileRow(p.Center.y) == r).Count}, trees={input.Trees.FindAll(p => TileCol(p.Position.x) == c && TileRow(p.Position.y) == r).Count}, buildings={input.Buildings.FindAll(p => TileCol(p.Position.x) == c && TileRow(p.Position.y) == r).Count}");
        }
    }

    /// <summary>
    /// 在每块无既有粗布局根节点的场景中写入占位物并保存。执行前验证全部输入，
    /// 遇到同名根节点时失败，避免覆盖人工修改。
    /// </summary>
    public static void Main()
    {
        var input = ReadInputs();
        for (int row = 1; row <= 2; row++)
        for (int col = 1; col <= 2; col++)
        {
            string path = ScenePath(col, row);
            var open = SceneManager.GetSceneByPath(path);
            if (open.IsValid() && open.isDirty)
                throw new InvalidOperationException("目标场景有未保存修改：" + path);
            bool openedHere = !open.IsValid();
            var scene = openedHere ? EditorSceneManager.OpenScene(path, OpenSceneMode.Additive) : open;
            try
            {
                var parent = FindAuthoredRoot(scene);
                if (parent.Find("MacroBlockoutRoot") != null)
                    throw new InvalidOperationException("已有 MacroBlockoutRoot，停止以保护现有内容：" + path);
            }
            finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
        }

        EnsureFolder(BlockoutAssetRoot);
        var waterMaterial = GetOrCreateMaterial(BlockoutAssetRoot + "/PondAreaPlaceholder.mat", new Color(0.10f, 0.48f, 0.60f, 1f));
        var buildingMaterial = GetOrCreateMaterial(BlockoutAssetRoot + "/BuiltUpPlaceholder.mat", new Color(0.70f, 0.68f, 0.61f, 1f));

        for (int row = 1; row <= 2; row++)
        for (int col = 1; col <= 2; col++)
            BuildTile(input, col, row, waterMaterial, buildingMaterial);
        AssetDatabase.SaveAssets();
        Debug.Log("HeJiaVillage macro blockout saved for four tiles.");
    }

    private static Inputs ReadInputs()
    {
        string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string dataDir = Path.Combine(repo, "Tools", "MapTerrainData", "HeJiaVillage");
        string referenceDir = Path.Combine(Application.dataPath, "Game", "MapTerrainSources", "HeJiaVillage", "FeatureReferences");
        var result = new Inputs();
        result.PinePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreePrefabPath);
        result.BroadleafPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BroadleafPrefabPath);
        if (result.PinePrefab == null || result.BroadleafPrefab == null)
            throw new FileNotFoundException("项目自有树 Prefab 缺失");

        for (int row = 1; row <= 2; row++)
        for (int col = 1; col <= 2; col++)
        {
            result.Terrain[col - 1, row - 1] = AssetDatabase.LoadAssetAtPath<TerrainData>($"{SceneRoot}/TerrainData/HeJiaVillage_{col}_{row}.asset");
            if (result.Terrain[col - 1, row - 1] == null || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath(col, row)) == null)
                throw new FileNotFoundException("Terrain 或场景缺失：" + col + "_" + row);
        }

        var roadJson = JObject.Parse(File.ReadAllText(Path.Combine(dataDir, "HeJiaVillage_VerifiedRoads.json")));
        foreach (var road in roadJson["roads"])
        {
            Vector2? previous = null;
            foreach (var xy in road["localMeters"])
            {
                var point = new Vector2((float)xy[0] + 1024f, (float)xy[1] + 1024f);
                if (previous.HasValue) result.Roads.Add(new Segment { A = previous.Value, B = point });
                previous = point;
            }
        }
        var pondJson = JObject.Parse(File.ReadAllText(Path.Combine(dataDir, "HeJiaVillage_VerifiedSurface.json")));
        foreach (var water in pondJson["waterBodies"])
            result.Ponds.Add(new Pond {
                Id = (string)water["id"],
                Center = new Vector2((float)water["centerLocalMeters"][0] + 1024f, (float)water["centerLocalMeters"][1] + 1024f),
                Radius = new Vector2((float)water["radiusMeters"][0], (float)water["radiusMeters"][1])
            });

        var forest = LoadMask(Path.Combine(referenceDir, "HeJiaVillage_ForestReference.png"));
        var built = LoadMask(Path.Combine(referenceDir, "HeJiaVillage_BuiltUpReference.png"));
        try
        {
            var treeRandom = new System.Random(49149);
            for (int z = 32; z < 2048; z += 64)
            for (int x = 32; x < 2048; x += 64)
            {
                var p = new Vector2(x + (float)(treeRandom.NextDouble() * 24 - 12), z + (float)(treeRandom.NextDouble() * 24 - 12));
                bool broadleaf = treeRandom.NextDouble() < 0.35;
                if (HasMask(forest, p) && !HasMask(built, p) && IsClear(input: result, p: p, roadDistance: 12f, pondFactor: 1.35f, spawnDistance: 35f))
                    result.Trees.Add(new TreePlacement { Position = p, Broadleaf = broadleaf });
            }
            var buildingRandom = new System.Random(49150);
            for (int z = 24; z < 2048; z += 48)
            for (int x = 24; x < 2048; x += 48)
            {
                var p = new Vector2(x + (float)(buildingRandom.NextDouble() * 10 - 5), z + (float)(buildingRandom.NextDouble() * 10 - 5));
                float width = 7f + (float)buildingRandom.NextDouble() * 4f;
                float depth = 7f + (float)buildingRandom.NextDouble() * 4f;
                float height = 3f + (float)buildingRandom.NextDouble() * 2f;
                float yaw = (float)buildingRandom.NextDouble() * 180f;
                if (HasMask(built, p) && IsClear(result, p, 18f, 1.5f, 45f))
                    result.Buildings.Add(new BuildingPlacement { Position = p, Width = width, Depth = depth, Height = height, Yaw = yaw });
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(forest); UnityEngine.Object.DestroyImmediate(built); }
        return result;
    }

    private static Texture2D LoadMask(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false) || texture.width != 2048 || texture.height != 2048)
        {
            UnityEngine.Object.DestroyImmediate(texture);
            throw new InvalidDataException("参考遮罩尺寸错误：" + path);
        }
        return texture;
    }

    private static bool HasMask(Texture2D texture, Vector2 point)
    {
        return texture.GetPixel(Mathf.Clamp((int)point.x, 0, 2047), Mathf.Clamp((int)point.y, 0, 2047)).a > 0.01f;
    }

    private static bool IsClear(Inputs input, Vector2 p, float roadDistance, float pondFactor, float spawnDistance)
    {
        if (Vector2.Distance(p, new Vector2(768, 512)) < spawnDistance) return false;
        foreach (var road in input.Roads)
        {
            var delta = road.B - road.A;
            float t = Mathf.Clamp01(Vector2.Dot(p - road.A, delta) / delta.sqrMagnitude);
            if (Vector2.Distance(p, road.A + delta * t) < roadDistance) return false;
        }
        foreach (var pond in input.Ponds)
        {
            float dx = (p.x - pond.Center.x) / (pond.Radius.x * pondFactor);
            float dz = (p.y - pond.Center.y) / (pond.Radius.y * pondFactor);
            if (dx * dx + dz * dz < 1f) return false;
        }
        return true;
    }

    private static int TileCol(float x) { return x < 1024f ? 1 : 2; }
    private static int TileRow(float z) { return z < 1024f ? 1 : 2; }
    private static string ScenePath(int col, int row) { return $"{SceneRoot}/Scenes/HeJiaVillage_{col}_{row}.unity"; }

    private static Transform FindAuthoredRoot(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = root.transform.Find("MapContentRoot/HandAuthoredContentRoot");
            if (found != null) return found;
        }
        throw new InvalidDataException("场景缺少 HandAuthoredContentRoot：" + scene.path);
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static Material GetOrCreateMaterial(string path, Color color)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("URP Unlit Shader 不可用");
        var material = new Material(shader) { color = color };
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void BuildTile(Inputs input, int col, int row, Material waterMaterial, Material buildingMaterial)
    {
        string path = ScenePath(col, row);
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            var parent = FindAuthoredRoot(scene);
            var root = new GameObject("MacroBlockoutRoot");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetParent(parent, false);
            var waterRoot = NewChild("PondAreaPlaceholders", root.transform, scene);
            var forestRoot = NewChild("ForestProxies", root.transform, scene);
            var builtRoot = NewChild("BuiltUpProxies", root.transform, scene);
            var terrain = input.Terrain[col - 1, row - 1];
            int ponds = 0, trees = 0, buildings = 0;
            foreach (var pond in input.Ponds)
            {
                if (TileCol(pond.Center.x) != col || TileRow(pond.Center.y) != row) continue;
                string meshPath = BlockoutAssetRoot + "/" + pond.Id + "_AreaMesh.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (mesh == null)
                {
                    mesh = CreateConformingPondMesh(pond, terrain, col, row);
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }
                var go = NewChild("PondArea_" + pond.Id, waterRoot.transform, scene);
                go.transform.localPosition = new Vector3(pond.Center.x - (col - 1) * 1024f, 0f, pond.Center.y - (row - 1) * 1024f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = waterMaterial;
                ponds++;
            }
            foreach (var tree in input.Trees)
            {
                if (TileCol(tree.Position.x) != col || TileRow(tree.Position.y) != row) continue;
                var prefab = tree.Broadleaf ? input.BroadleafPrefab : input.PinePrefab;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.name = $"ForestProxy_{Mathf.RoundToInt(tree.Position.x)}_{Mathf.RoundToInt(tree.Position.y)}";
                go.transform.SetParent(forestRoot.transform, true);
                go.transform.localPosition = new Vector3(tree.Position.x - (col - 1) * 1024f, HeightAt(terrain, col, row, tree.Position), tree.Position.y - (row - 1) * 1024f);
                foreach (var collider in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                trees++;
            }
            foreach (var building in input.Buildings)
            {
                if (TileCol(building.Position.x) != col || TileRow(building.Position.y) != row) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.name = $"BuiltUpProxy_{Mathf.RoundToInt(building.Position.x)}_{Mathf.RoundToInt(building.Position.y)}";
                go.transform.SetParent(builtRoot.transform, true);
                go.transform.localPosition = new Vector3(building.Position.x - (col - 1) * 1024f, HeightAt(terrain, col, row, building.Position) + building.Height * 0.5f, building.Position.y - (row - 1) * 1024f);
                go.transform.rotation = Quaternion.Euler(0f, building.Yaw, 0f);
                go.transform.localScale = new Vector3(building.Width, building.Height, building.Depth);
                go.GetComponent<MeshRenderer>().sharedMaterial = buildingMaterial;
                UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                buildings++;
            }
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"Macro saved {col}_{row}: ponds={ponds}, trees={trees}, buildings={buildings}");
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static GameObject NewChild(string name, Transform parent, Scene scene)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static float HeightAt(TerrainData terrain, int col, int row, Vector2 global)
    {
        float x = (global.x - (col - 1) * 1024f) / 1024f;
        float z = (global.y - (row - 1) * 1024f) / 1024f;
        return terrain.GetInterpolatedHeight(Mathf.Clamp01(x), Mathf.Clamp01(z));
    }

    private static Mesh CreateConformingPondMesh(Pond pond, TerrainData terrain, int col, int row)
    {
        const int sides = 32, rings = 4;
        var vertices = new Vector3[1 + sides * rings];
        var uv = new Vector2[vertices.Length];
        var centerY = HeightAt(terrain, col, row, pond.Center) + 0.25f;
        vertices[0] = new Vector3(0f, centerY, 0f);
        uv[0] = new Vector2(0.5f, 0.5f);
        for (int ring = 1; ring <= rings; ring++)
        for (int side = 0; side < sides; side++)
        {
            float angle = Mathf.PI * 2f * side / sides;
            float radius = (float)ring / rings;
            float dx = Mathf.Cos(angle) * pond.Radius.x * radius;
            float dz = Mathf.Sin(angle) * pond.Radius.y * radius;
            int index = 1 + (ring - 1) * sides + side;
            vertices[index] = new Vector3(dx, HeightAt(terrain, col, row, pond.Center + new Vector2(dx, dz)) + 0.25f, dz);
            uv[index] = new Vector2(0.5f + Mathf.Cos(angle) * radius * 0.5f, 0.5f + Mathf.Sin(angle) * radius * 0.5f);
        }
        var triangles = new List<int>(sides * (1 + (rings - 1) * 2) * 3);
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + side);
            for (int ring = 1; ring < rings; ring++)
            {
                int inner = 1 + (ring - 1) * sides;
                int outer = inner + sides;
                triangles.Add(inner + side); triangles.Add(inner + next); triangles.Add(outer + side);
                triangles.Add(inner + next); triangles.Add(outer + next); triangles.Add(outer + side);
            }
        }
        var mesh = new Mesh { name = pond.Id + "_AreaMesh", vertices = vertices, uv = uv, triangles = triangles.ToArray() };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

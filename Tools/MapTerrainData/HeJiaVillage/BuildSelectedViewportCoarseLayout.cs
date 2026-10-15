using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 根据用户在高德卫星图中选定的当前视野，为和家村 1_1 生成可替换粗布局。
/// 本工具只创建道路带和范围边框；和家村不生成建筑或建筑占位。
/// </summary>
public static class HeJiaVillageSelectedViewportCoarseLayout
{
    private const string ScenePath =
        "Assets/Game/Scenes/GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_1_1.unity";
    private const string TerrainPath =
        "Assets/Game/Scenes/GeneratedMaps/HeJiaVillage/TerrainData/HeJiaVillage_1_1.asset";
    private const string RoadMaterialPath =
        "Assets/Game/Art/Environment/HeJiaVillage/RoadSample/HeJia_RoadSample.mat";
    private const string AssetFolder =
        "Assets/Game/Art/Environment/HeJiaVillage/CurrentViewCoarseLayout";
    private const string RootName = "CurrentViewCoarseLayoutRoot";
    private const float RoadHeightOffset = 0.16f;

    /// <summary>
    /// 首次生成用户选定范围的粗布局。若同名根节点已存在则失败，避免覆盖后续手工调整。
    /// </summary>
    public static void Main()
    {
        var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        var roadMaterial = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (terrain == null || roadMaterial == null || sceneAsset == null || shader == null)
            throw new InvalidOperationException("粗布局所需 Terrain、道路材质、场景或 URP Shader 缺失");

        var data = JObject.Parse(File.ReadAllText(GetSourcePath()));
        if (data["buildings"] != null)
            throw new InvalidDataException("和家村禁止生成建筑或建筑占位，请移除 buildings 数据");
        var existing = SceneManager.GetSceneByPath(ScenePath);
        if (existing.IsValid() && existing.isDirty)
            throw new InvalidOperationException("目标场景存在未保存修改，停止生成粗布局");

        bool openedHere = !existing.IsValid();
        var scene = openedHere
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
            : existing;
        try
        {
            var authored = FindAuthoredRoot(scene);
            if (authored.Find(RootName) != null)
                throw new InvalidOperationException(RootName + " 已存在，停止以保护手工调整");

            EnsureFolder(AssetFolder);
            var boundaryMaterial = LoadOrCreateMaterial(shader, AssetFolder + "/CoarseBoundary.mat",
                new Color(1f, 0.78f, 0.08f, 1f));

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetParent(authored, false);

            var roadsRoot = CreateChild(root.transform, "Roads");
            int roadCount = 0;
            float maxGrade = 0f;
            foreach (var roadToken in data["roads"] ?? new JArray())
            {
                string id = (string)roadToken["id"];
                float width = (float)roadToken["widthMeters"];
                var points = ReadPoints((JArray)roadToken["unityPoints"]);
                var mesh = CreateRibbonMesh(terrain, points, width, RoadHeightOffset);
                string meshPath = AssetFolder + "/" + id + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
                    throw new InvalidOperationException("粗布局网格已存在：" + meshPath);
                AssetDatabase.CreateAsset(mesh, meshPath);
                var road = CreateChild(roadsRoot.transform, id);
                road.AddComponent<MeshFilter>().sharedMesh = mesh;
                road.AddComponent<MeshRenderer>().sharedMaterial = roadMaterial;
                maxGrade = Mathf.Max(maxGrade, SampleMaxGrade(terrain, points));
                roadCount++;
            }

            var bounds = (JObject)data["estimatedUnityBounds"];
            var boundary = new List<Vector2>
            {
                new Vector2((float)bounds["west"], (float)bounds["south"]),
                new Vector2((float)bounds["east"], (float)bounds["south"]),
                new Vector2((float)bounds["east"], (float)bounds["north"]),
                new Vector2((float)bounds["west"], (float)bounds["north"]),
                new Vector2((float)bounds["west"], (float)bounds["south"])
            };
            var boundaryMesh = CreateRibbonMesh(terrain, boundary, 0.8f, 0.38f);
            string boundaryPath = AssetFolder + "/SelectedViewportBoundary.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(boundaryPath) != null)
                throw new InvalidOperationException("粗布局边界网格已存在：" + boundaryPath);
            AssetDatabase.CreateAsset(boundaryMesh, boundaryPath);
            var boundaryObject = CreateChild(root.transform, "SelectedViewportBoundary");
            boundaryObject.AddComponent<MeshFilter>().sharedMesh = boundaryMesh;
            boundaryObject.AddComponent<MeshRenderer>().sharedMaterial = boundaryMaterial;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("保存和家村 1_1 场景失败");
            AssetDatabase.SaveAssets();
            Debug.Log($"Selected viewport coarse layout saved: roads={roadCount}, " +
                      $"maxTerrainGrade={maxGrade:F1}%, " +
                      "bounds=286m×276m; terrain unchanged, no colliders added.");
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static string GetSourcePath()
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        return Path.Combine(repositoryRoot, "Tools", "MapTerrainData", "HeJiaVillage",
            "HeJiaVillage_SelectedViewport_20260927.json");
    }

    private static Transform FindAuthoredRoot(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var authored = root.transform.Find("MapContentRoot/HandAuthoredContentRoot");
            if (authored != null) return authored;
        }
        throw new InvalidDataException("目标场景缺少 HandAuthoredContentRoot");
    }

    private static GameObject CreateChild(Transform parent, string name)
    {
        var child = new GameObject(name);
        SceneManager.MoveGameObjectToScene(child, parent.gameObject.scene);
        child.transform.SetParent(parent, false);
        return child;
    }

    /// <summary>沿折线约每米采样并贴合当前 Terrain；宽度只是粗布局显示宽度。</summary>
    private static Mesh CreateRibbonMesh(TerrainData terrain, List<Vector2> controlPoints,
        float width, float heightOffset)
    {
        var samples = SamplePolyline(controlPoints, 1f);
        const int widthSteps = 4;
        var vertices = new Vector3[samples.Count * (widthSteps + 1)];
        var uv = new Vector2[vertices.Length];
        float distance = 0f;
        for (int i = 0; i < samples.Count; i++)
        {
            if (i > 0) distance += Vector2.Distance(samples[i - 1], samples[i]);
            var previous = samples[Mathf.Max(0, i - 1)];
            var next = samples[Mathf.Min(samples.Count - 1, i + 1)];
            var tangent = (next - previous).normalized;
            var across = new Vector2(-tangent.y, tangent.x);
            for (int j = 0; j <= widthSteps; j++)
            {
                float side = ((float)j / widthSteps - 0.5f) * width;
                var point = samples[i] + across * side;
                float y = terrain.GetInterpolatedHeight(point.x / 1024f, point.y / 1024f) +
                          heightOffset;
                int index = i * (widthSteps + 1) + j;
                vertices[index] = new Vector3(point.x, y, point.y);
                uv[index] = new Vector2(side / 8f, distance / 8f);
            }
        }

        var triangles = new int[(samples.Count - 1) * widthSteps * 6];
        int cursor = 0;
        for (int i = 0; i < samples.Count - 1; i++)
        for (int j = 0; j < widthSteps; j++)
        {
            int first = i * (widthSteps + 1) + j;
            int next = first + widthSteps + 1;
            triangles[cursor++] = first;
            triangles[cursor++] = first + 1;
            triangles[cursor++] = next;
            triangles[cursor++] = next;
            triangles[cursor++] = first + 1;
            triangles[cursor++] = next + 1;
        }

        var mesh = new Mesh { name = "SelectedViewportCoarseRibbon" };
        mesh.indexFormat = vertices.Length > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static List<Vector2> SamplePolyline(List<Vector2> controls, float spacing)
    {
        var samples = new List<Vector2> { controls[0] };
        for (int segment = 0; segment < controls.Count - 1; segment++)
        {
            var start = controls[segment];
            var end = controls[segment + 1];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(start, end) / spacing));
            for (int step = 1; step <= steps; step++)
                samples.Add(Vector2.Lerp(start, end, (float)step / steps));
        }
        return samples;
    }

    private static List<Vector2> ReadPoints(JArray array)
    {
        var points = new List<Vector2>();
        foreach (var point in array)
            points.Add(new Vector2((float)point[0], (float)point[1]));
        if (points.Count < 2) throw new InvalidDataException("道路折线至少需要两个点");
        return points;
    }

    private static float SampleMaxGrade(TerrainData terrain, List<Vector2> controls)
    {
        var samples = SamplePolyline(controls, 2f);
        float maximum = 0f;
        for (int i = 1; i < samples.Count; i++)
        {
            float previous = terrain.GetInterpolatedHeight(samples[i - 1].x / 1024f,
                samples[i - 1].y / 1024f);
            float current = terrain.GetInterpolatedHeight(samples[i].x / 1024f,
                samples[i].y / 1024f);
            maximum = Mathf.Max(maximum, Mathf.Abs(current - previous) /
                Vector2.Distance(samples[i - 1], samples[i]) * 100f);
        }
        return maximum;
    }

    private static Material LoadOrCreateMaterial(Shader shader, string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(material, path);
        return material;
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
}

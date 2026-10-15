using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 为和家村 1_1 生成一段可替换的贴地道路样板。道路中心线仍以 VerifiedRoads.json 为准；
/// 本工具不修改 Terrain 高度、出生点或运行时地图对象。
/// 此文件由 Unity MCP 的 script-execute 执行，不编入游戏程序集。
/// </summary>
public static class HeJiaVillageRoadSample
{
    private const string ScenePath = "Assets/Game/Scenes/GeneratedMaps/HeJiaVillage/Scenes/HeJiaVillage_1_1.unity";
    private const string TerrainPath = "Assets/Game/Scenes/GeneratedMaps/HeJiaVillage/TerrainData/HeJiaVillage_1_1.asset";
    private const string SourceColor = "Assets/Art/Julhiecio TPS Controller/Textures/CC0 Textures/Ground 2/Ground036_1K_Color.png";
    private const string SourceNormal = "Assets/Art/Julhiecio TPS Controller/Textures/CC0 Textures/Ground 2/Ground036_1K_Normal.png";
    private const string AssetFolder = "Assets/Game/Art/Environment/HeJiaVillage/RoadSample";
    private const float WidthMeters = 4f;
    private const float HeightOffsetMeters = 0.12f;

    /// <summary>先核对资源、中心线和目标场景；重复执行时不覆盖现有样板。</summary>
    public static void Main()
    {
        var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        var sourceColor = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceColor);
        var sourceNormal = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceNormal);
        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (terrain == null || sourceColor == null || sourceNormal == null || sceneAsset == null || shader == null)
            throw new InvalidOperationException("道路样板所需 Terrain、贴图、场景或 URP Shader 缺失");

        var a = new Vector2(752f, 381f);
        var b = new Vector2(666f, 339f);
        VerifySourceSegment(a, b);

        var existing = SceneManager.GetSceneByPath(ScenePath);
        if (existing.IsValid() && existing.isDirty)
            throw new InvalidOperationException("目标场景有未保存修改，停止生成道路样板");
        bool openedHere = !existing.IsValid();
        var scene = openedHere ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive) : existing;
        try
        {
            var authored = FindAuthoredRoot(scene);
            if (authored.Find("RoadSampleRoot") != null)
                throw new InvalidOperationException("RoadSampleRoot 已存在，停止以保护现有内容");

            EnsureFolder(AssetFolder);
            string colorPath = AssetFolder + "/Ground036_Road_Color.png";
            string normalPath = AssetFolder + "/Ground036_Road_Normal.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath) == null && !AssetDatabase.CopyAsset(SourceColor, colorPath))
                throw new IOException("复制道路颜色贴图失败");
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath) == null && !AssetDatabase.CopyAsset(SourceNormal, normalPath))
                throw new IOException("复制道路法线贴图失败");
            AssetDatabase.ImportAsset(colorPath);
            AssetDatabase.ImportAsset(normalPath);
            var color = AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (color == null || normal == null) throw new InvalidOperationException("项目自有贴图导入失败");

            string materialPath = AssetFolder + "/HeJia_RoadSample.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "HeJia_RoadSample" };
                material.SetTexture("_BaseMap", color);
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 0.35f);
                material.SetFloat("_Smoothness", 0f);
                material.SetFloat("_Metallic", 0f);
                material.SetColor("_BaseColor", new Color(0.68f, 0.59f, 0.50f, 1f));
                material.EnableKeyword("_NORMALMAP");
                AssetDatabase.CreateAsset(material, materialPath);
            }

            string meshPath = AssetFolder + "/SouthwestConnector_752_381_to_666_339.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = CreateRoadMesh(terrain, a, b);
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            var root = new GameObject("RoadSampleRoot");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.SetParent(authored, false);
            var road = new GameObject("southwest_connector__752_381_to_666_339");
            SceneManager.MoveGameObjectToScene(road, scene);
            road.transform.SetParent(root.transform, false);
            road.transform.localPosition = new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f);
            road.AddComponent<MeshFilter>().sharedMesh = mesh;
            road.AddComponent<MeshRenderer>().sharedMaterial = material;
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Road sample saved: southwest_connector, 95.7m, provisional 4m surface width, terrain unchanged.");
        }
        finally { if (openedHere) EditorSceneManager.CloseScene(scene, true); }
    }

    /// <summary>根据同一来源重建样板网格；保留场景对象、材质和贴图 GUID。</summary>
    public static void RefreshMesh()
    {
        var terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainPath);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetFolder + "/SouthwestConnector_752_381_to_666_339.asset");
        var material = AssetDatabase.LoadAssetAtPath<Material>(AssetFolder + "/HeJia_RoadSample.mat");
        if (terrain == null || mesh == null || material == null) throw new InvalidOperationException("现有道路样板资产缺失");
        var replacement = CreateRoadMesh(terrain, new Vector2(752f, 381f), new Vector2(666f, 339f));
        mesh.Clear();
        mesh.vertices = replacement.vertices;
        mesh.uv = replacement.uv;
        mesh.triangles = replacement.triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        UnityEngine.Object.DestroyImmediate(replacement);
        material.SetColor("_BaseColor", new Color(0.68f, 0.59f, 0.50f, 1f));
        material.SetFloat("_BumpScale", 0.35f);
        EditorUtility.SetDirty(mesh);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Debug.Log("Road sample mesh refreshed with 0.5m sampling and warmer dirt color.");
    }

    private static void VerifySourceSegment(Vector2 a, Vector2 b)
    {
        string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string path = Path.Combine(repo, "Tools", "MapTerrainData", "HeJiaVillage", "HeJiaVillage_VerifiedRoads.json");
        var json = JObject.Parse(File.ReadAllText(path));
        foreach (var road in json["roads"])
        {
            if ((string)road["id"] != "southwest_connector") continue;
            var points = (JArray)road["localMeters"];
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p = new Vector2((float)points[i][0] + 1024f, (float)points[i][1] + 1024f);
                var q = new Vector2((float)points[i + 1][0] + 1024f, (float)points[i + 1][1] + 1024f);
                if (p == a && q == b) return;
            }
        }
        throw new InvalidDataException("道路样板端点不再是 VerifiedRoads.json 中的连续中心线顶点");
    }

    private static Transform FindAuthoredRoot(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var found = root.transform.Find("MapContentRoot/HandAuthoredContentRoot");
            if (found != null) return found;
        }
        throw new InvalidDataException("目标场景缺少 HandAuthoredContentRoot");
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

    /// <summary>每约 0.5 米采样中心线和两侧地形，避免网格局部钻入 Terrain。</summary>
    private static Mesh CreateRoadMesh(TerrainData terrain, Vector2 a, Vector2 b)
    {
        int lengthSteps = Mathf.CeilToInt(Vector2.Distance(a, b) / 0.5f);
        const int widthSteps = 8;
        var midpoint = (a + b) * 0.5f;
        var direction = (b - a).normalized;
        var across = new Vector2(-direction.y, direction.x);
        var vertices = new Vector3[(lengthSteps + 1) * (widthSteps + 1)];
        var uv = new Vector2[vertices.Length];
        for (int i = 0; i <= lengthSteps; i++)
        for (int j = 0; j <= widthSteps; j++)
        {
            float along = (float)i / lengthSteps;
            float side = ((float)j / widthSteps - 0.5f) * WidthMeters;
            var point = Vector2.Lerp(a, b, along) + across * side;
            float y = terrain.GetInterpolatedHeight(point.x / 1024f, point.y / 1024f) + HeightOffsetMeters;
            int index = i * (widthSteps + 1) + j;
            vertices[index] = new Vector3(point.x - midpoint.x, y, point.y - midpoint.y);
            uv[index] = new Vector2(side / 8f, along * Vector2.Distance(a, b) / 8f);
        }
        var triangles = new int[lengthSteps * widthSteps * 6];
        int cursor = 0;
        for (int i = 0; i < lengthSteps; i++)
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
        var mesh = new Mesh { name = "SouthwestConnector_RoadSample" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>把已验收场景造物导出为正式建筑 Prefab 和 Luban 默认布局；不创建玩家或写玩家存档。</summary>
public static class BaseCampBuildableAuthoring
{
    private const string ScenePath = "Assets/Game/Scenes/BaseMap.unity";
    private const string Folder = "Assets/Game/Prefabs/Building";
    private static readonly string[] Kinds = { "shed_wood_01", "awning_standing_01a", "crate_01", "crate_02", "barrel_01", "sack_02_set", "table_01", "bench_wood_01", "fence_wood_01a", "log_wood_01", "RPGFXCampfire" };
    private static readonly string[] Names = { "木棚", "布棚", "小木箱", "大木箱", "水桶", "麻袋", "木桌", "长凳", "木栅栏", "柴堆", "营火" };
    private static readonly float[] Scales = { .9f, .7f, 1f, 1f, 1f, 1f, .75f, 1f, 1.05f, .85f, .75f };
    private static readonly string[] Costs = { "9|12;2|2", "9|8;2|4", "9|2", "9|3", "9|3;1|1", "2|2", "9|5", "9|3", "9|2", "9|2", "9|2;1|2" };
    private static string Id(int i) => "camp_" + Kinds[i].ToLowerInvariant();
    private static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Export()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var decor = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "BaseCampDecoration");
            var objects = decor.Cast<Transform>().SelectMany(g => g.Cast<Transform>()).ToArray();
            var layout = new List<string> { "##var,Id,BuildingId,PositionX,PositionY,PositionZ,RotationY,UniformScale", "##type,string,string,float,float,float,float,float" };
            var catalog = new List<string>();
            for (int i = 0; i < Kinds.Length; i++)
            {
                var matching = objects.Where(t => t.GetComponentsInChildren<MeshFilter>(true).Any(f => Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(f.sharedMesh)).EndsWith(Kinds[i], StringComparison.OrdinalIgnoreCase))).ToArray();
                if (matching.Length == 0) throw new InvalidOperationException("缺少摆件模型：" + Kinds[i]);
                var source = matching[0];
                var root = new GameObject("Camp_" + Kinds[i]);
                try
                {
                    var model = Object.Instantiate(source.gameObject, root.transform);
                    model.name = "Model";
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    model.transform.localScale = Vector3.one * Scales[i];
                    model.SetActive(true);
                    var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    var footprint = root.AddComponent<BoxCollider>();
                    footprint.center = bounds.center;
                    footprint.size = new Vector3(Mathf.Max(.1f, bounds.size.x), Mathf.Max(.1f, bounds.size.y), Mathf.Max(.1f, bounds.size.z));
                    footprint.isTrigger = true;
                    var view = root.AddComponent<TemporaryBuildingView>();
                    view.FootprintCollider = footprint;
                    view.ValidHighlight = Highlight(root.transform, "ValidHighlight", footprint, new Color(.2f, .9f, .5f, .25f));
                    view.InvalidHighlight = Highlight(root.transform, "InvalidHighlight", footprint, new Color(.9f, .15f, .15f, .25f));
                    view.DestroyHighlight = new GameObject("DestroyHighlight"); view.DestroyHighlight.transform.SetParent(root.transform, false); view.DestroyHighlight.SetActive(false);
                    var serialized = new SerializedObject(view);
                    var separate = serialized.FindProperty("SeparatePhysicalColliders");
                    if (separate != null) { separate.boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo(); }
                    if (i == 10)
                    {
                        // 原营火模型有偏移原点，恢复到石圈中心；火焰与光源归同一个效果根管理。
                        model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                        footprint.center -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                        var effects = new GameObject("CommittedEffects"); effects.transform.SetParent(root.transform, false);
                        var fire = Object.Instantiate(decor.Find("营火休息区/营火火焰").gameObject, effects.transform);
                        fire.transform.localPosition = Vector3.up * .35f;
                        var light = Object.Instantiate(decor.Find("营火休息区/营火暖光").gameObject, effects.transform);
                        light.transform.localPosition = Vector3.up * .8f;
                        var effect = effects.AddComponent<CampfireVisualEffect>();
                        var effectData = new SerializedObject(effect);
                        effectData.FindProperty("fireLight").objectReferenceValue = light.GetComponent<Light>();
                        effectData.FindProperty("flameParticles").objectReferenceValue = fire.GetComponent<ParticleSystem>();
                        effectData.FindProperty("flameMaterial").objectReferenceValue = fire.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                        effectData.ApplyModifiedPropertiesWithoutUndo();
                        view.CommittedEffectRoot = effects; effects.SetActive(false);
                    }
                    // 简单物件让已有雕刻形状跟随占地；棚子使用实体结构，不雕刻整片棚下空间。
                    if (i != 0 && i != 1)
                    {
                        view.NavMeshObstacle = root.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                        view.NavMeshObstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                        view.NavMeshObstacle.center = footprint.center; view.NavMeshObstacle.size = footprint.size; view.NavMeshObstacle.enabled = false;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, Folder + "/Camp_" + Kinds[i] + ".prefab");
                    RenderIcon(root, footprint.bounds, "Camp_" + Kinds[i]);
                    var radius = Mathf.Max(footprint.size.x, footprint.size.z) * .5f;
                    // 建筑目录只输出占地半径，统一操作距离由玩家配置拥有。
                    catalog.Add("," + Id(i) + "," + Names[i] + ",temp_building." + Id(i) + ".name,Camp_" + Kinds[i] + ",Camp_" + Kinds[i] + "," + Costs[i] + "," + (i < 2 ? "2" : "1") + "," + Number(radius) + ",20," + (i < 2 ? "0" : "0.8") + "," + (i < 2 ? "false" : "true") + "," + (1000 + i * 10) + ",true");
                }
                finally { Object.DestroyImmediate(root); }
                for (int j = 0; j < matching.Length; j++)
                {
                    var t = matching[j]; var position = t.position;
                    var scale = t.lossyScale.x / Scales[i];
                    if (i == 10) { position = new Vector3(-6, 0, -6); scale = 1; }
                    layout.Add("," + Id(i) + "." + j + "," + Id(i) + "," + Number(position.x) + "," + Number(position.y) + "," + Number(position.z) + "," + Number(t.eulerAngles.y) + "," + Number(scale));
                }
            }
            var config = Path.GetFullPath("../Config/DataCsv");
            File.WriteAllLines(Path.Combine(config, "base_camp_layout.csv"), layout, new System.Text.UTF8Encoding(false));
            var old = File.ReadAllLines(Path.Combine(config, "temp_building.csv")).Where(line => !line.StartsWith(",camp_"));
            File.WriteAllLines(Path.Combine(config, "temp_building.csv"), old.Concat(catalog), new System.Text.UTF8Encoding(false));
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            return "Exported 11 buildable prefabs and " + (layout.Count - 2) + " default placements; source scene retained until migration validated.";
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }

    private static void RenderIcon(GameObject root, Bounds bounds, string name)
    {
        const string folder = "Assets/Game/Art/UI/TemporaryBuilding";
        Directory.CreateDirectory(folder);
        var preview = EditorSceneManager.NewPreviewScene();
        var oldPosition = root.transform.position;
        var originalScene = root.scene;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        var cameraObject = new GameObject("IconCamera", typeof(Camera));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, preview);
        var camera = cameraObject.GetComponent<Camera>();
        var target = new RenderTexture(256, 256, 24);
        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            camera.scene = preview;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f,.12f,.16f,1);
            camera.orthographic = true; camera.orthographicSize = bounds.size.magnitude * .58f;
            camera.transform.position = bounds.center + new Vector3(1.3f, 1f, -1.5f).normalized * (bounds.size.magnitude * 3 + 5);
            camera.transform.LookAt(bounds.center); camera.targetTexture = target;
            var light = new GameObject("IconLight", typeof(Light));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(light, preview);
            light.GetComponent<Light>().type = LightType.Directional; light.GetComponent<Light>().intensity = 2;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0,0,256,256),0,0); texture.Apply();
            var path = folder + "/" + name + ".png";
            File.WriteAllBytes(path, texture.EncodeToPNG()); AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(target); Object.DestroyImmediate(texture);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, originalScene);
            root.transform.position = oldPosition; EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static GameObject Highlight(Transform parent, string name, BoxCollider footprint, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>()); go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(footprint.center.x, .035f, footprint.center.z);
        go.transform.localScale = new Vector3(footprint.size.x, .025f, footprint.size.z);
        var path = "Assets/Game/Art/Environment/BaseCamp/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); go.GetComponent<Renderer>().sharedMaterial = material;
        go.SetActive(false); return go;
    }

    public static string RemoveStaticProps()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var decor = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Single(t => t.name == "BaseCampDecoration");
            int count = 0;
            foreach (var t in decor.Cast<Transform>().SelectMany(g => g.Cast<Transform>()).ToArray())
            {
                if (t.GetComponentsInChildren<MeshFilter>(true).Any(f => Kinds.Any(kind => Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(f.sharedMesh)).EndsWith(kind, StringComparison.OrdinalIgnoreCase))) || t.name == "营火火焰" || t.name == "营火暖光")
                { Object.DestroyImmediate(t.gameObject); count++; }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("保存基地静态物件清理失败");
            return "Removed " + count + " static props/effects; fixed environment and original boundaries retained.";
        }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }
}

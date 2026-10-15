using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>离线制作旧营地样片和基地制作入口；不改变原始美术资源，不运行游戏结算。</summary>
public static class CivilisationValleyAuthoring
{
    private const string Art = "Assets/Game/Art/Environment/CivilisationExpedition";
    private const string ScenePath = "Assets/Game/Scenes/CivilisationValleyMap.unity";
    private static readonly string[] Sources =
    {
        "Assets/Art/SimpleNaturePack/Prefabs/Tree_01.prefab",
        "Assets/Art/SimpleNaturePack/Prefabs/Rock_01.prefab",
        "Assets/Art/SimpleNaturePack/Prefabs/Branch_01.prefab",
        "Assets/Art/RPGPP_LT/Prefabs/Props/Containers/rpgpp_lt_basket_01.prefab",
        "Assets/Art/RPGPP_LT/Prefabs/Props/Containers/rpgpp_lt_sack_01.prefab"
    };
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var i = path.LastIndexOf('/'); Folder(path.Substring(0, i)); AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }
    private static string Destination(string source) => Art + "/Sources/" + source.Substring("Assets/Art/".Length);
    private static void CopyArt()
    {
        var dependencies = AssetDatabase.GetDependencies(Sources, true).Where(p => p.StartsWith("Assets/Art/") && !p.EndsWith(".cs")).ToArray();
        var guids = new Dictionary<string, string>();
        foreach (var source in dependencies)
        {
            var destination = Destination(source); Folder(Path.GetDirectoryName(destination).Replace('\\', '/'));
            if (AssetDatabase.LoadMainAssetAtPath(destination) == null && !AssetDatabase.CopyAsset(source, destination)) throw new InvalidOperationException("复制失败：" + source);
            guids[AssetDatabase.AssetPathToGUID(source)] = AssetDatabase.AssetPathToGUID(destination);
        }
        foreach (var source in dependencies)
        {
            foreach (var file in new[] { Destination(source), Destination(source) + ".meta" })
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".meta" && extension != ".prefab" && extension != ".mat" && extension != ".asset") continue;
                var text = File.ReadAllText(file);
                foreach (var pair in guids) text = text.Replace("guid: " + pair.Key, "guid: " + pair.Value);
                File.WriteAllText(file, text, new UTF8Encoding(false));
            }
        }
        AssetDatabase.Refresh();
        RepairNatureMaterial();
    }
    /// <summary>原包材质带纯白自发光；仅修复正式副本，保留色板贴图与原包内容。</summary>
    public static string RepairNatureMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Sources/SimpleNaturePack/Materials/SimpleNaturePack_Texture_01.mat");
        if (material == null) throw new InvalidOperationException("缺少自然物件材质副本");
        material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
        return "正式自然物件材质已关闭白色自发光。";
    }
    private static Material Material(string name, Color color)
    {
        Folder(Art + "/Materials"); var path = Art + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0); EditorUtility.SetDirty(material); return material;
    }
    private static GameObject Place(Transform parent, string path, string name, Vector3 position, float yaw = 0, float scale = 1)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("缺少已核对资源：" + path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name; go.transform.localPosition = position; go.transform.localRotation = Quaternion.Euler(0, yaw, 0); go.transform.localScale = Vector3.one * scale;
        foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
        foreach (var collider in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (var audio in go.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
        return go;
    }
    private static GameObject Ground(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material; return go;
    }
    private static void Point(Transform parent, string name, Vector3 position, CivilisationPointKind kind, string id)
    {
        var go = new GameObject(name); go.SetActive(false); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        var view = go.AddComponent<CivilisationPointView>(); var trigger = go.GetComponent<BoxCollider>(); trigger.isTrigger = true; trigger.center = Vector3.up; trigger.size = new Vector3(3.2f, 2, 3.2f);
        var data = new SerializedObject(view); data.FindProperty("kind").enumValueIndex = (int)kind; data.FindProperty("pointId").stringValue = id;
        data.FindProperty("displayName").stringValue = name; data.ApplyModifiedPropertiesWithoutUndo(); go.SetActive(true);
    }
    /// <summary>仅创建新地图；已存在场景拒绝覆盖，保护后续手工美术调整。</summary>
    public static string CreateValley()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return "旧营地场景已存在，未覆盖。";
        CopyArt();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var root = new GameObject("CivilisationValleyMap"); SceneManager.MoveGameObjectToScene(root, scene); root.SetActive(false);
        try
        {
            var content = new GameObject("MapContentRoot"); content.transform.SetParent(root.transform, false);
            var runtime = new GameObject("RuntimeUnitRoot"); runtime.transform.SetParent(content.transform, false);
            var context = root.AddComponent<MapContext>(); var serialized = new SerializedObject(context);
            serialized.FindProperty("mapContentRoot").objectReferenceValue = content; serialized.FindProperty("dynamicObjectRoot").objectReferenceValue = runtime.transform; serialized.ApplyModifiedPropertiesWithoutUndo();
            var grass = Material("ValleyEarth", new Color(.30f,.38f,.24f)); var earth = Material("CampPath", new Color(.49f,.42f,.28f));
            Ground(content.transform, "ValleyGround", new Vector3(0,-.25f,0), new Vector3(60,.5f,60), grass);
            Ground(content.transform, "ApproachPath", new Vector3(0,.015f,-1), new Vector3(3,.025f,24), earth);
            Ground(content.transform, "OldCampClearing", new Vector3(0,.025f,10), new Vector3(13,.025f,10), earth);
            var random = new System.Random(105);
            for (var i=0; i<38; i++)
            {
                var angle = i * Mathf.PI * 2 / 38; var radius = 17 + (float)random.NextDouble()*10;
                var tree = Place(content.transform, Destination(Sources[0]), "ValleyTree_"+i, new Vector3(Mathf.Sin(angle)*radius,0,Mathf.Cos(angle)*radius), (float)random.NextDouble()*360, .8f+(float)random.NextDouble()*.5f);
                var collider = tree.AddComponent<CapsuleCollider>(); collider.radius = .35f; collider.height = 3; collider.center = Vector3.up*1.5f;
            }
            for(var i=0;i<10;i++) Place(content.transform, Destination(Sources[1]), "ValleyRock_"+i, new Vector3(i%2==0?-7:8,0,-12+i*2.5f), i*37, 1.1f);
            Place(content.transform,"Assets/Game/Prefabs/Building/Camp_RPGFXCampfire.prefab","OldHearth",new Vector3(0,0,10));
            Place(content.transform,"Assets/Game/Prefabs/Building/Camp_awning_standing_01a.prefab","OldCampAwning",new Vector3(-4,0,12),90);
            Place(content.transform,Destination(Sources[3]),"FoodBasket",new Vector3(-3,0,9));
            Place(content.transform,Destination(Sources[4]),"PreparationSack",new Vector3(-2,0,-12));
            Place(content.transform,"Assets/Game/Prefabs/Building/Camp_log_wood_01.prefab","WoodPile",new Vector3(4,0,11));
            Point(content.transform,"调查旧火塘工艺",new Vector3(0,0,10),CivilisationPointKind.Search,"camp.fire");
            Point(content.transform,"搜索食物储藏处",new Vector3(-3,0,9),CivilisationPointKind.Search,"camp.food");
            Point(content.transform,"搜索木材堆",new Vector3(4,0,11),CivilisationPointKind.Search,"camp.wood");
            Point(content.transform,"领取基础补给",new Vector3(-2,0,-12),CivilisationPointKind.Preparation,"");
            Point(content.transform,"返回基地",new Vector3(2,0,-12),CivilisationPointKind.Return,"");
            root.SetActive(true);
            var sources = new List<UnityEngine.AI.NavMeshBuildSource>();
            UnityEngine.AI.NavMeshBuilder.CollectSources(root.transform, ~0, UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders, 0, new List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);
            var navigation = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(UnityEngine.AI.NavMesh.GetSettingsByIndex(0), sources, new Bounds(Vector3.zero, new Vector3(62,12,62)), Vector3.zero, Quaternion.identity);
            if (navigation == null) throw new InvalidOperationException("旧营地导航数据为空");
            Folder(Art + "/Navigation");
            AssetDatabase.CreateAsset(navigation, Art + "/Navigation/CivilisationValleyNavMesh.asset");
            SceneManager.SetActiveScene(scene);
            var navigationSettings = new SerializedObject(UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject);
            navigationSettings.FindProperty("m_NavMeshData").objectReferenceValue = navigation;
            navigationSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene,ScenePath)) throw new InvalidOperationException("新地图保存失败");
            return "旧营地样片已创建：60×60 米，38 棵树，3 个搜索点、补给点和返程点；已生成导航数据。";
        }
        finally { EditorSceneManager.CloseScene(scene,true); }
    }
    /// <summary>只在基地添加本批次制作位，不改变已有基地布局或 Boot。</summary>
    public static string AddWorkshop()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/BaseMap.unity", OpenSceneMode.Additive);
        try
        {
            var context = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MapContext>(true)).Single();
            if(context.MapContentRoot.transform.Find("CivilisationDryingStation")!=null) return "基地制作位已存在，未重复添加。";
            var station=new GameObject("CivilisationDryingStation"); station.SetActive(false); station.transform.SetParent(context.MapContentRoot.transform,false);
            station.transform.localPosition=new Vector3(-2.5f,0,3);
            Place(station.transform,"Assets/Game/Prefabs/Building/Camp_RPGFXCampfire.prefab","DryingFire",Vector3.zero);
            Place(station.transform,Destination(Sources[4]),"PreparedFood",new Vector3(.8f,0,0));
            Point(station.transform,"干粮制作位",Vector3.zero,CivilisationPointKind.Workshop,""); station.SetActive(true);
            if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("基地制作位保存失败");
            return "基地干粮制作位已保存。";
        }
        finally{EditorSceneManager.CloseScene(scene,true);}
    }
}

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>把用户已采用的三关候选原样保存为持久资源；与原预览源一起交给 script-execute 编译。</summary>
public static class NextThreeStagesAssetBake
{
    const string Art = "Assets/Game/Art/Arcade/NextThree";
    const string Prefabs = "Assets/Game/Prefabs/SceneObjects/";
    const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
    static readonly Type Source = typeof(NextThreeStagesArtPreview);
    static readonly string[] Names = { "Graveyard", "Temple", "Marketplace" };
    static readonly string[] Folders = { "Graveyard", "Temple", "Marketplace" };
    static readonly List<Object> Temporary = new List<Object>();

    /// <summary>通过同源方法构造候选，避免正式制作另写布局与配色。</summary>
    static object Call(string name, params object[] args)
    {
        var method = Source.GetMethod(name, Flags);
        var parameters = method.GetParameters();
        var full = new object[parameters.Length];
        for (int i = 0; i < full.Length; i++) full[i] = i < args.Length ? args[i] : parameters[i].DefaultValue;
        try { return method.Invoke(null, full); }
        catch (System.Reflection.TargetInvocationException error) { throw error.InnerException ?? error; }
    }
    static void Set(string name, object value) { Source.GetField(name, Flags).SetValue(null, value); }
    static T Get<T>(string name) { return (T)Source.GetField(name, Flags).GetValue(null); }

    /// <summary>只允许本工程编辑态，保留活动场景和时间状态。</summary>
    static void Guard()
    {
        if (Application.dataPath.Replace('\\', '/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying || EditorApplication.isCompiling)
            throw new InvalidOperationException("正式资源制作只允许 FirepowerLoop 编辑态");
    }
    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        EnsureFolder(Path.GetDirectoryName(folder).Replace('\\', '/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
    }
    static T Save<T>(T source, string path) where T : Object
    {
        var saved = AssetDatabase.LoadAssetAtPath<T>(path);
        if (saved == null) { AssetDatabase.CreateAsset(source, path); return source; }
        // 保留GUID覆盖时须刷新原生渲染缓存，否则CPU数据已变而同次实拍仍显示旧网格/材质。
        if (saved is Mesh targetMesh) targetMesh.Clear();
        if (saved is Material targetMaterial && source is Material sourceMaterial)
        {
            targetMaterial.shader = sourceMaterial.shader;
            targetMaterial.CopyPropertiesFromMaterial(sourceMaterial); targetMaterial.name = sourceMaterial.name;
        }
        else EditorUtility.CopySerialized(source, saved);
        if (saved is Mesh uploadedMesh) uploadedMesh.UploadMeshData(false);
        EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
        return saved;
    }
    static void Cleanup()
    {
        var owned = Get<List<Object>>("owned");
        foreach (var item in owned.Concat(Temporary)) if (item != null && !EditorUtility.IsPersistent(item)) Object.DestroyImmediate(item);
        owned.Clear(); Temporary.Clear();
        var actors = (System.Collections.IList)Source.GetField("actors", Flags).GetValue(null); actors.Clear();
    }

    /// <summary>合并同材质静态网格；原点、法线、UV、颜色与候选保持一致。</summary>
    static void Combine(Transform root, string folder, string prefix)
    {
        var groups = new Dictionary<Material, List<CombineInstance>>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || filter.sharedMesh == null) throw new InvalidOperationException("静态候选缺少网格或Renderer");
            for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++)
            {
                var material = renderer.sharedMaterials[Mathf.Min(sub, renderer.sharedMaterials.Length - 1)];
                if (!groups.TryGetValue(material, out var items)) groups[material] = items = new List<CombineInstance>();
                items.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = sub, transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix });
            }
        }
        var combined = new GameObject("CombinedEnvironment").transform; combined.SetParent(root, false); int index = 0;
        foreach (var pair in groups)
        {
            var mesh = new Mesh { name = prefix + "Mesh" + index, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            Temporary.Add(mesh); mesh.CombineMeshes(pair.Value.ToArray(), true, true); mesh.RecalculateBounds();
            var savedMesh = Save(mesh, folder + "/" + mesh.name + ".asset");
            var material = new Material(pair.Key) { name = prefix + pair.Key.name.Replace("TEMP_", "") }; Temporary.Add(material);
            var savedMaterial = Save(material, folder + "/" + material.name + ".mat");
            var part = new GameObject("EnvironmentPart" + index++, typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(combined, false);
            part.GetComponent<MeshFilter>().sharedMesh = savedMesh; part.GetComponent<MeshRenderer>().sharedMaterial = savedMaterial;
        }
        foreach (var child in root.Cast<Transform>().Where(t => t != combined).ToArray()) Object.DestroyImmediate(child.gameObject);
    }

    /// <summary>模板保留可操纵的翅膀和蓝影节点，网格及材质全部转为持久引用。</summary>
    static void PersistTemplate(Transform root, string folder)
    {
        var meshes = new Dictionary<Mesh, Mesh>(); var materials = new Dictionary<Material, Material>(); int index = 0;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (!EditorUtility.IsPersistent(mesh))
            {
                if (!meshes.TryGetValue(mesh, out var saved))
                {
                    var copy = Object.Instantiate(mesh); copy.name = root.name + "Mesh" + index++; Temporary.Add(copy);
                    saved = Save(copy, folder + "/" + copy.name + ".asset"); meshes[mesh] = saved;
                }
                filter.sharedMesh = saved;
            }
        }
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = renderer.sharedMaterials.Select(material =>
        {
            if (EditorUtility.IsPersistent(material)) return material;
            if (!materials.TryGetValue(material, out var saved))
            {
                var copy = new Material(material) { name = material.name.Replace("TEMP_", "") }; Temporary.Add(copy);
                saved = Save(copy, folder + "/" + copy.name + ".mat"); materials[material] = saved;
            }
            return saved;
        }).ToArray();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 0;
    }

    /// <summary>批量落成三场地和暗影模板；不写集合、配置、角色原件或运行时逻辑。</summary>
    public static string BakeAll()
    {
        Guard(); var prior = SceneManager.GetActiveScene(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            for (int stage = 0; stage < 3; stage++)
            {
                string folder = Art + "/" + Folders[stage]; EnsureFolder(folder); Call("Palette", stage);
                var root = new GameObject("Arcade" + Names[stage] + "Visual").transform; Set("root", root);
                Call("Box", "BackdropGround", 0f, -.4f, 0f, 108f, .5f, 104f, Get<Material>("ground"));
                Call(stage == 0 ? "GraveArt" : stage == 1 ? "TempleArt" : "MarketArt");
                if (stage == 2) MarketplaceRoadConnectionPreview.ApplyConfirmedRoads(root);
                Combine(root, folder, Names[stage]);
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, Prefabs + root.name + ".prefab");
                Object.DestroyImmediate(root.gameObject); Cleanup();
            }
            BakeShadowTemplates();
            return Validate();
        }
        finally { SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true); Cleanup(); }
    }

    /// <summary>仅接入已采用的集市道路；不重新生成墓园、神庙或暗影模板。</summary>
    public static string BakeMarketplaceRoads()
    {
        Guard(); var prior = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene); Call("Palette", 2);
            var root = new GameObject("ArcadeMarketplaceVisual").transform; Set("root", root);
            Call("Box", "BackdropGround", 0f, -.4f, 0f, 108f, .5f, 104f, Get<Material>("ground"));
            Call("MarketArt"); MarketplaceRoadConnectionPreview.ApplyConfirmedRoads(root);
            Combine(root, Art + "/Marketplace", "Marketplace");
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, Prefabs + root.name + ".prefab");
            return Validate();
        }
        finally { SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true); Cleanup(); }
    }

    static void BakeShadowTemplates()
    {
        const string folder = Art + "/Temple/Shadow"; EnsureFolder(folder); Call("Palette", 1); Call("ArtPalette", 1);
        var owner = new GameObject("TemplateOwner").transform; Set("root", owner);
        var crow = (Transform)Call("Group", "ArcadeShadowCrowVisual", Vector3.zero);
        Call("Poly", "CrowBody", Vector3.zero, new Vector3(.24f, .22f, .45f), Get<Material>("dark"), 6, crow);
        var wings = (Transform[])Call("ArtCrow", crow); wings[0].name = "WingLeft"; wings[1].name = "WingRight";
        PersistTemplate(crow, folder); PrefabUtility.SaveAsPrefabAsset(crow.gameObject, Prefabs + crow.name + ".prefab");
        var travel = new GameObject("ArcadeShadowTravelVisual").transform; travel.SetParent(owner, false);
        Call("Poly", "Core", Vector3.zero, Vector3.one * .4f, Get<Material>("blue"), 7, travel);
        for (int i = 0; i < 6; i++) Call("Poly", "Mote" + i, Vector3.zero, Vector3.one * .1f, Get<Material>("blue"), 5, travel);
        PersistTemplate(travel, folder); PrefabUtility.SaveAsPrefabAsset(travel.gameObject, Prefabs + travel.name + ".prefab");
        var material = new Material(Get<Material>("dark")) { name = "ShadowZombieDark" }; Temporary.Add(material);
        var dark = Save(material, folder + "/ShadowZombieDark.mat");
        var zombie = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab")); zombie.name = "ArcadeShadowZombie";
        foreach (var renderer in zombie.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => dark).ToArray();
        PrefabUtility.SaveAsPrefabAsset(zombie, "Assets/Game/Prefabs/Characters/ArcadeShadowZombie.prefab"); Object.DestroyImmediate(zombie); Object.DestroyImmediate(owner.gameObject); Cleanup();
    }

    /// <summary>有界实际资源加载核查，拒绝演员、相机、灯光、脚本、碰撞体和瞬态网格/材质。</summary>
    public static string Validate()
    {
        Guard(); var report = new List<string>();
        var paths = Names.Select(n => Prefabs + "Arcade" + n + "Visual.prefab").Concat(new[] { Prefabs + "ArcadeShadowCrowVisual.prefab", Prefabs + "ArcadeShadowTravelVisual.prefab" });
        foreach (var path in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (asset == null) throw new InvalidOperationException("加载失败：" + path);
            var instance = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (instance.GetComponentsInChildren<Component>(true).Any(c => c == null || !(c is Transform || c is MeshFilter || c is MeshRenderer))) throw new InvalidOperationException("静态模板混入组件：" + path);
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh == null || !EditorUtility.IsPersistent(filter.sharedMesh)) throw new InvalidOperationException("瞬态网格：" + path);
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true)) foreach (var material in renderer.sharedMaterials) if (material == null || !EditorUtility.IsPersistent(material) || material.shader == null) throw new InvalidOperationException("瞬态材质：" + path);
                report.Add(path + "：持久网格=" + instance.GetComponentsInChildren<MeshFilter>(true).Length + "，依赖=" + AssetDatabase.GetDependencies(path, true).Length);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
        }
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");
        var shadow = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeShadowZombie.prefab");
        if (shadow == null || shadow.transform.localScale != source.transform.localScale) throw new InvalidOperationException("暗影丧尸尺寸改变");
        foreach (var renderer in shadow.GetComponentsInChildren<Renderer>(true)) foreach (var material in renderer.sharedMaterials) if (material == null || !EditorUtility.IsPersistent(material)) throw new InvalidOperationException("暗影丧尸瞬态材质");
        report.Add("暗影丧尸根尺寸=" + shadow.transform.localScale + "；正式原件未改"); return string.Join("\n", report);
    }

    /// <summary>同机位拍摄已保存资源；主角与六只丧尸仅作为候选中的临时比例参照。</summary>
    public static string CaptureApplied()
    {
        Guard(); var prior = SceneManager.GetActiveScene(); var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); var previousRT = RenderTexture.active;
        try
        {
            SceneManager.SetActiveScene(scene);
            for (int stage = 0; stage < 3; stage++)
            {
                Call("Palette", stage); Set("idle", Call("Clip", "ArcadeEnemyIdle")); Set("run", Call("Clip", "ArcadeEnemyRun"));
                var root = new GameObject("AppliedCapture").transform; Set("root", root);
                UnityEngine.Rendering.AmbientMode ambient = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientMode = ambient; RenderSettings.ambientLight = new Color(.52f, .55f, .53f); RenderSettings.fog = false;
                var sun = new GameObject("PreviewSun", typeof(Light)).GetComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.05f; sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(48, -28, 0); sun.cullingMask = 1 << 29;
                var camera = new GameObject("PreviewCamera", typeof(Camera)).GetComponent<Camera>(); Set("camera", camera); camera.orthographic = true; camera.cullingMask = 1 << 29; camera.farClipPlane = 220; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .20f, .21f);
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Arcade" + Names[stage] + "Visual.prefab"), root);
                var hero = (GameObject)Call("Hero", stage == 1 ? -3f : 0f, stage == 2 ? -6f : -5f);
                for (int i = 0; i < 6; i++) Call("Zombie", new Vector3((i % 3 - 1) * 8, 0, 10 + i / 3 * 5), false);
                Call("SetLayers"); var folder = Path.GetFullPath("Temp/NextThreeStagesApplied/" + (stage == 0 ? "graveyard" : stage == 1 ? "temple" : "marketplace")); Directory.CreateDirectory(folder);
                camera.orthographicSize = 36; camera.transform.position = new Vector3(0, 74, -57); camera.transform.LookAt(Vector3.zero); Call("Save", folder + "/overview.png", 1500, 1100);
                camera.orthographicSize = 11; camera.transform.position = new Vector3(-5, 17, -27); camera.transform.LookAt(new Vector3(-3, .6f, -5)); Call("Save", folder + "/detail.png", 1280, 800);
                var focus = stage == 0 ? new Vector3(8, 1, -8) : stage == 1 ? new Vector3(0, 1, 0) : new Vector3(-15, 1, -14);
                if (stage == 0) hero.transform.position = new Vector3(5, 0, -8); else if (stage == 2) hero.transform.position = new Vector3(-11, 0, -12);
                camera.orthographicSize = stage == 1 ? 6 : 5.5f; camera.transform.position = focus + new Vector3(-5, 9, -12); camera.transform.LookAt(focus); Call("Save", folder + "/landmark.png", 1280, 800);
                Object.DestroyImmediate(root.gameObject); Object.DestroyImmediate(sun.gameObject); Object.DestroyImmediate(camera.gameObject); Cleanup();
            }
            return "Temp/NextThreeStagesApplied：正式Prefab三机位实拍；非自然对局录像";
        }
        finally { RenderTexture.active = previousRT; SceneManager.SetActiveScene(prior); EditorSceneManager.CloseScene(scene, true); Cleanup(); }
    }
}

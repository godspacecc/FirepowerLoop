using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>复制河谷需要的自然美术及依赖并绑定正式Kit；不编辑第三方原件、基地、场景或存档。</summary>
public static class ValleyVegetationAuthoring
{
    private const string Source = "Assets/Art/SimpleNaturePack/";
    private const string Art = "Assets/Game/Art/Environment/CivilisationExpedition/";
    private const string KitPath = "Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab";
    /// <summary>只制作确认采用的五种树、三种灌木和两种草；重复调用复用已有GUID。</summary>
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("仅允许编辑模式制作植被");
        var names = new[] { "Tree_01", "Tree_02", "Tree_03", "Tree_04", "Tree_05", "Bush_01", "Bush_02", "Bush_03", "Grass_01", "Grass_02" };
        var sources = names.Select(n => Source + "Prefabs/" + n + ".prefab").ToArray();
        var dependencies = AssetDatabase.GetDependencies(sources, true).Where(p => p.StartsWith(Source)).ToArray();
        var map = new Dictionary<string, string>(); var created = new List<string>();
        string Destination(string p) => p.EndsWith(".prefab") ? Art + "Vegetation/" + Path.GetFileName(p) : Art + "Sources/SimpleNaturePack/" + p.Substring(Source.Length);
        foreach (var p in dependencies)
        {
            var destination = Destination(p); Folder(Path.GetDirectoryName(destination).Replace('\\', '/'));
            if (AssetDatabase.LoadMainAssetAtPath(destination) == null)
            {
                if (!AssetDatabase.CopyAsset(p, destination)) throw new InvalidOperationException("植被复制失败：" + p);
                created.Add(destination);
            }
            map[AssetDatabase.AssetPathToGUID(p)] = AssetDatabase.AssetPathToGUID(destination);
        }
        foreach (var p in created)
            foreach (var file in new[] { p, p + ".meta" })
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".prefab" && ext != ".mat" && ext != ".meta" && ext != ".asset") continue;
                var text = File.ReadAllText(file);
                foreach (var pair in map) text = text.Replace("guid: " + pair.Key, "guid: " + pair.Value);
                File.WriteAllText(file, text, new UTF8Encoding(false));
            }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var name in names.Where(n => n.StartsWith("Bush_") || n.StartsWith("Grass_")))
        {
            var model = Art + "Sources/SimpleNaturePack/Models/" + name + ".fbx";
            var importer = AssetImporter.GetAtPath(model) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("缺少草灌网格导入器：" + model);
            // Mesh.CombineMeshes在运行包中需要CPU顶点；仅打开五个正式草灌副本，不改原包和树木导入设置。
            if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "Sources/SimpleNaturePack/Materials/SimpleNaturePack_Texture_01.mat");
        if (material == null || material.shader.name != "Universal Render Pipeline/Lit") throw new InvalidOperationException("正式自然材质未适配URP");
        var prefabs = new List<GameObject>();
        foreach (var name in names)
        {
            var path = Art + "Vegetation/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                // 植被副本只提供美术。树干碰撞和相机淡化由原采集模板保留，装饰地被不生成Collider。
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(c);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            var outside = AssetDatabase.GetDependencies(path, true).Where(p => p.StartsWith("Assets/Art/"));
            if (outside.Any()) throw new InvalidOperationException("正式植被仍依赖备用库：" + string.Join(",", outside));
        }
        var kitRoot = PrefabUtility.LoadPrefabContents(KitPath);
        try
        {
            var data = new SerializedObject(kitRoot.GetComponent<ValleyGenerationKit>());
            Set("treeVisualPrefabs", prefabs.Take(5)); Set("shrubPrefabs", prefabs.Skip(5).Take(3)); Set("grassPrefabs", prefabs.Skip(8));
            data.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(kitRoot, KitPath);
            void Set(string field, IEnumerable<GameObject> values)
            { var list = values.ToArray(); var prop = data.FindProperty(field); prop.arraySize = list.Length; for (var i = 0; i < list.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = list[i]; }
        }
        finally { PrefabUtility.UnloadPrefabContents(kitRoot); }
        // 只保存本次制作的Prefab；不调用全局SaveAssets，避免混入其他任务的编辑器状态。
        return "已接入5种树、3种灌木、2种草；正式副本无备用库依赖，原包未修改。";
    }
    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var i = path.LastIndexOf('/'); Folder(path.Substring(0, i)); AssetDatabase.CreateFolder(path.Substring(0, i), path.Substring(i + 1));
    }
}

using System;
using System.IO;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>将已采用的v2图片接到正式界面并生成引用；不改玩法、场景或未采用资源。</summary>
public static class UiBackgroundApply
{
    private const string Art = "Assets/Game/Art/UI/ArcadeBackgrounds/";
    private static readonly string[] Files = { "01-island", "02-docks", "03-farm", "04-graveyard", "05-temple", "06-market", "07-henge", "08-fort", "09-koi", "10-nest" };
    private static readonly int[] Ids = { 1, 21, 22, 23, 24, 25, 5, 7, 10, 20 };

    public static string Run()
    {
        if (EditorApplication.isPlaying || Application.dataPath.Replace('\\', '/') != "F:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅在本工程编辑态接入");
        foreach (var file in Directory.GetFiles(Art, "*.png"))
        {
            var path = file.Replace('\\', '/');
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        Apply("Home/Start", root => root.transform.Find("Background/Artwork").GetComponent<RawImage>().texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "main-menu.png"));
        Apply("Online/OnlineRoom", root => root.transform.Find("LandscapeBackground").GetComponent<RawImage>().texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "online-lobby.png"));
        Apply("Common/Loading", root =>
        {
            var view = root.GetComponent<LoadingUI>();
            var image = view.MapArtwork.GetComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "01-island.png");
            image.raycastTarget = false;
            var artwork = image.GetComponent<ArcadeLoadingArtwork>() ?? image.gameObject.AddComponent<ArcadeLoadingArtwork>();
            var serialized = new SerializedObject(artwork);
            serialized.FindProperty("target").objectReferenceValue = image;
            serialized.FindProperty("fallback").objectReferenceValue = image.sprite;
            var entries = serialized.FindProperty("entries"); entries.arraySize = Ids.Length;
            for (int i = 0; i < Ids.Length; i++)
            {
                var item = entries.GetArrayElementAtIndex(i);
                item.FindPropertyRelative("ArenaConfigNumber").intValue = Ids[i];
                item.FindPropertyRelative("Artwork").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(Art + Files[i] + ".png");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            root.GetComponent<ReferenceCollector>().Add("LoadingArtwork", artwork);
        });
        return "已接入12张图片及3个Prefab，须顺序调用Generate并等每次重载完成后回读";
    }

    /// <summary>每次只生成一个界面，避免生成器的单项待绑定队列被后一个覆盖。</summary>
    public static string Generate(string relativePath)
    {
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/" + relativePath + ".prefab"));
        return relativePath;
    }

    /// <summary>脚本重载后调用生成器现有绑定步骤，并在反序列化后确认每个必需字段有效。</summary>
    public static string BindReady()
    {
        if (EditorApplication.isCompiling) throw new InvalidOperationException("等待脚本编译完成");
        foreach (var name in new[] { "Home/Start", "Online/OnlineRoom", "Common/Loading" })
        {
            string path = "Assets/Game/Prefabs/UI/" + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            string typeName = "Game." + prefab.name + "UI";
            var type = typeof(LoadingUI).Assembly.GetType(typeName);
            foreach (var reference in prefab.GetComponent<ReferenceCollector>().data)
                if (type.GetField(reference.key) == null) throw new InvalidOperationException("生成字段尚未编译：" + reference.key);
            typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Invoke(null, new object[] { path, typeName });
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var reference in prefab.GetComponent<ReferenceCollector>().data)
                if (!(type.GetField(reference.key).GetValue(prefab.GetComponent(type)) is UnityEngine.Object value) || value == null)
                    throw new InvalidOperationException("绑定无效：" + name + "/" + reference.key);
        }
        return "三份正式Prefab的全部生成引用均已反序列化验证";
    }

    private static void Apply(string name, Action<GameObject> change)
    {
        string path = "Assets/Game/Prefabs/UI/" + name + ".prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { change(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}

using System;
using System.IO;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

/// <summary>从正式骷髅模型拍摄透明 HUD 图标，并由项目生成器更新宿主和复用 Box 引用。</summary>
public static class ArcadeSkullHudAuthoring
{
    const string Hud="Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab";
    const string Box="Assets/Game/Prefabs/UI/Arcade/Box/ArcadeEffectBox.prefab";
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作");
        var red=Capture("Red");var purple=Capture("Purple");
        var root=PrefabUtility.LoadPrefabContents(Hud);
        try
        {
            var collector=root.GetComponent<ReferenceCollector>();collector.Add("SkullRedIcon",red);collector.Add("SkullPurpleIcon",purple);collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root,Hud);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        root=PrefabUtility.LoadPrefabContents(Box);
        try
        {
            var view=root.GetComponent<ArcadeEffectBoxUI>();var collector=root.GetComponent<ReferenceCollector>();
            collector.Add("ProgressRoot",view.ProgressFill.transform.parent.gameObject);collector.Add("Backdrop",root.GetComponent<Image>());collector.Sort();
            PrefabUtility.SaveAsPrefabAsset(root,Box);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        BoxUICodeSpawner.AutoUpdateBoxUI(AssetDatabase.LoadAssetAtPath<GameObject>(Box));
        UICodeSpawner.AutoUpdateUI(AssetDatabase.LoadAssetAtPath<GameObject>(Hud));
        return "已制作红紫骷髅图标并调用正式 UI/Box 生成器";
    }
    /// <summary>脚本编译后用正式生成器完成绑定，不手写生成 View 字段。</summary>
    public static string Bind()
    {
        typeof(BoxUICodeSpawner).GetMethod("BindGeneratedComponent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Box,"Game.ArcadeEffectBoxUI"});
        typeof(UICodeSpawner).GetMethod("BindGeneratedUiComponent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{Hud,"Game.ArcadeHudUI"});
        return "HUD 与 Box 正式引用已绑定";
    }
    private static Sprite Capture(string tier)
    {
        string path="Assets/Game/Art/UI/Items/IconSkull"+tier+".png";
        var prior=SceneManager.GetActiveScene();var targetBefore=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);RenderTexture target=null;Texture2D pixels=null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var skull=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeDropSkull"+tier+".prefab"));
            skull.transform.localScale=Vector3.one;
            foreach(var node in skull.GetComponentsInChildren<Transform>(true))node.gameObject.layer=30;
            var light=new GameObject("SkullIconLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;
            light.transform.rotation=Quaternion.Euler(25,155,0);light.cullingMask=1<<30;
            var camera=new GameObject("SkullIconCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;
            camera.orthographic=true;camera.orthographicSize=.41f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;
            camera.transform.position=new Vector3(0,.38f,3);camera.transform.LookAt(new Vector3(0,.38f,0));
            target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);camera.targetTexture=target;camera.Render();RenderTexture.active=target;
            pixels=new Texture2D(128,128,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();camera.targetTexture=null;
            File.WriteAllBytes(path,pixels.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spritePixelsPerUnit=128;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        finally
        {
            RenderTexture.active=targetBefore;if(pixels!=null)Object.DestroyImmediate(pixels);if(target!=null)Object.DestroyImmediate(target);
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
}

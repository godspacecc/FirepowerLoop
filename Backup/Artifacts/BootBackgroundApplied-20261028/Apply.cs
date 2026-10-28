using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>原样接入已采用的Boot独立背景与底部渐暗，保持加载组件及原资源不变。</summary>
public static class ApplyBootArtwork
{
    public static string Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (Application.dataPath.Replace('\\','/') != "E:/GitHub/FirepowerLoop/Client/Assets" || EditorApplication.isPlaying || scene.path != "Assets/Game/Scenes/Boot.unity" || scene.isDirty)
            throw new InvalidOperationException("需要本工程干净Boot编辑态");
        const string art = "Assets/Boot/Art/BootResourceBackground.png";
        AssetDatabase.ImportAsset(art, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(art);
        importer.textureType=TextureImporterType.Default; importer.mipmapEnabled=false;
        importer.npotScale=TextureImporterNPOTScale.None; importer.maxTextureSize=2048;
        importer.textureCompression=TextureImporterCompression.Uncompressed;
        importer.wrapMode=TextureWrapMode.Clamp; importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(art);
        const string shadePath="Assets/Boot/Art/BootBottomShade.asset";
        if (AssetDatabase.LoadMainAssetAtPath(shadePath)) throw new InvalidOperationException("渐变资源已存在，避免覆盖");
        var shadeTexture=new Texture2D(1,64,TextureFormat.RGBA32,false);
        shadeTexture.name="BootBottomShade"; shadeTexture.wrapMode=TextureWrapMode.Clamp;
        for(int y=0;y<64;y++) shadeTexture.SetPixel(0,y,new Color(.015f,.025f,.035f,.65f*(1f-y/63f)));
        shadeTexture.Apply(); AssetDatabase.CreateAsset(shadeTexture,shadePath);
        var root=scene.GetRootGameObjects().Single(g=>g.name=="HotUpdateCanvas");
        var artwork=root.transform.Find("Background/Artwork").GetComponent<RawImage>();
        artwork.texture=texture;
        artwork.GetComponent<AspectRatioFitter>().aspectRatio=(float)texture.width/texture.height;
        var shade=new GameObject("BottomReadability",typeof(RectTransform),typeof(RawImage));
        shade.layer=root.layer; shade.transform.SetParent(root.transform,false); shade.transform.SetSiblingIndex(1);
        var rect=(RectTransform)shade.transform;
        rect.anchorMin=Vector2.zero; rect.anchorMax=new Vector2(1,.27f); rect.offsetMin=Vector2.zero; rect.offsetMax=Vector2.zero;
        var image=shade.GetComponent<RawImage>(); image.texture=shadeTexture; image.raycastTarget=false;
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("场景保存失败");
        AssetDatabase.SaveAssets();
        return "已保存Boot背景、底部渐变及场景引用；旧图片及meta保留。";
    }
}

using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Game;

/// <summary>从正式衣裤渲染专用背包图标，不使用与物品外形无关的占位图。</summary>
public static class ClothingIconAuthoring
{
    public static string Run()
    {
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Player/OutfitPrototype/ClothingCharacterPreview.prefab"));
        root.transform.position=new Vector3(10000,-10000,10000);
        var camera=new GameObject("ClothingIconCamera",typeof(Camera)).GetComponent<Camera>();
        var light=new GameObject("ClothingIconLight",typeof(Light)).GetComponent<Light>();
        var texture=new RenderTexture(256,256,24);var previous=RenderTexture.active;
        try
        {
            foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            var animator=root.GetComponentInChildren<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0f);
            camera.enabled=false;camera.orthographic=true;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.clear;camera.targetTexture=texture;
            light.type=LightType.Directional;light.cullingMask=1<<31;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(35,150,0);
            foreach(var name in new[]{"Top_RedHoodie","Top_OliveUtility","Pants_BlueDenim","Pants_TanCargo","Head_OliveCap","Hands_WorkGloves","Feet_BrownBoots","Back_CanvasPack","Armor_UtilityVest"})
            {
                var renderers=root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach(var r in renderers){r.gameObject.SetActive(r.name==name);r.enabled=r.name==name;}
                var renderer=renderers.Single(r=>r.name==name);var mesh=new Mesh();renderer.BakeMesh(mesh);
                var center=renderer.transform.TransformPoint(mesh.bounds.center);var size=Vector3.Scale(mesh.bounds.size,renderer.transform.lossyScale);Object.DestroyImmediate(mesh);
                // 背包从背面拍摄；手套分布在两侧，图标保留完整双手轮廓。
                camera.transform.position=center+new Vector3(0,0,name.StartsWith("Back_")?-4:4);camera.transform.LookAt(center);camera.orthographicSize=Mathf.Max(size.x,size.y)*.58f;camera.Render();
                RenderTexture.active=texture;var png=new Texture2D(256,256,TextureFormat.RGBA32,false);
                var path="Assets/Game/Art/UI/Items/IconClothing_"+name+".png";
                try{png.ReadPixels(new Rect(0,0,256,256),0,0);png.Apply();File.WriteAllBytes(path,png.EncodeToPNG());}finally{Object.DestroyImmediate(png);}
                AssetDatabase.ImportAsset(path);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.SaveAndReimport();
            }
            return "9 clothing icons rendered";
        }
        finally{RenderTexture.active=previous;camera.targetTexture=null;texture.Release();Object.DestroyImmediate(texture);Object.DestroyImmediate(camera.gameObject);Object.DestroyImmediate(light.gameObject);Object.DestroyImmediate(root);}
    }
}


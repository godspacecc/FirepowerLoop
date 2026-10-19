using System;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>制作可被现有掉落与跟随强化端口持有的两级头骨，无碰撞和额外生命周期。</summary>
public static class ArcadeSkullAuthoring
{
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本项目编辑态制作");
        // 按光束几何生成中性亮度遮罩，保留原色弹尾资源，只给强化弹使用。
        const string maskPath="Assets/Game/Art/Arcade/Weapons/SkullBeamMask.asset";
        var mask=AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
        if(mask==null)
        {
            mask=new Texture2D(128,32,TextureFormat.RGBA32,false){name="SkullBeamMask",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[128*32];
            for(int y=0;y<32;y++)for(int x=0;x<128;x++)
            {
                float across=Mathf.Pow(Mathf.Clamp01(1-Mathf.Abs((y+.5f)/32-.5f)*2),3);
                float along=Mathf.Sin((x+.5f)/128*Mathf.PI);pixels[y*128+x]=new Color(1,1,1,across*along);
            }
            mask.SetPixels(pixels);mask.Apply();AssetDatabase.CreateAsset(mask,maskPath);
        }
        foreach(var projectile in new[]{"GrayboxBullet","GrayboxArrow"})
        {
            string path="Assets/Game/Prefabs/Combat/"+projectile+".prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var serialized=new SerializedObject(root.GetComponent<Game.ProjectileView>());serialized.FindProperty("weaponPowerBeamTexture").objectReferenceValue=mask;
                serialized.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        var dark=Material("SkullDark",new Color(.025f,.015f,.025f));var teeth=Material("SkullTeeth",new Color(.85f,.8f,.65f));
        for(int tier=1;tier<=2;tier++)
        {
            var mat=Material(tier==1?"SkullRed":"SkullPurple",tier==1?new Color(.85f,.04f,.03f):new Color(.6f,.06f,.9f));
            var root=new GameObject(tier==1?"ArcadeDropSkullRed":"ArcadeDropSkullPurple");
            try
            {
                Part(root,"Cranium",PrimitiveType.Sphere,new Vector3(0,.38f,0),new Vector3(.62f,.64f,.52f),mat);
                Part(root,"Jaw",PrimitiveType.Cube,new Vector3(0,.13f,.02f),new Vector3(.4f,.16f,.35f),mat);
                for(int side=-1;side<=1;side+=2)
                {
                    Part(root,"EyeSocket",PrimitiveType.Sphere,new Vector3(side*.145f,.4f,.225f),new Vector3(.19f,.2f,.07f),dark);
                    Part(root,"Cheek",PrimitiveType.Sphere,new Vector3(side*.23f,.23f,.11f),new Vector3(.17f,.19f,.3f),mat);
                }
                Part(root,"Nose",PrimitiveType.Cube,new Vector3(0,.275f,.253f),new Vector3(.075f,.1f,.035f),dark);
                for(int t=0;t<5;t++)Part(root,"Tooth",PrimitiveType.Cube,new Vector3((t-2)*.067f,.17f,.212f),new Vector3(.052f,.105f,.045f),teeth);
                root.transform.localScale=Vector3.one*(tier==1?.85f:1.1f);
                PrefabUtility.SaveAsPrefabAsset(root,"Assets/Game/Prefabs/SceneObjects/"+root.name+".prefab");
            }
            finally{Object.DestroyImmediate(root);}
        }
        AssetDatabase.SaveAssets();return "红色小头骨、紫色大头骨资源制作完成";
    }
    private static Material Material(string name,Color color)
    {
        var path="Assets/Game/Art/Arcade/Weapons/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(mat,path);}
        mat.SetColor("_BaseColor",color);mat.SetFloat("_Smoothness",.35f);EditorUtility.SetDirty(mat);return mat;
    }
    private static void Part(GameObject parent,string name,PrimitiveType type,Vector3 position,Vector3 scale,Material material)
    {
        var part=GameObject.CreatePrimitive(type);part.name=name;part.transform.SetParent(parent.transform,false);
        part.transform.localPosition=position;part.transform.localScale=scale;part.GetComponent<Renderer>().sharedMaterial=material;
        Object.DestroyImmediate(part.GetComponent<Collider>());
    }
}

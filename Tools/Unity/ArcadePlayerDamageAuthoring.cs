using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>制作玩家专用红白受击、落尘及可救援标记，显式绑定正式角色依赖。</summary>
public static class ArcadePlayerDamageAuthoring
{
    private const string Folder="Assets/Game/Art/Arcade/VFX/PlayerDamage";
    private const string Player="Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab";
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var red=Material("ImpactRed",new Color(3.1f,.17f,.08f,.95f));
        var white=Material("ImpactWhite",new Color(3,2.5f,2,.95f));
        var teal=Material("RescueTeal",new Color(.1f,1.8f,1.4f,.7f));
        string dustPath=Folder+"/LandingDust.mat";
        if(!File.Exists(dustPath))AssetDatabase.CopyAsset("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/BlastSmoke.mat",dustPath);
        var dust=AssetDatabase.LoadAssetAtPath<Material>(dustPath);dust.SetColor("_BaseColor",new Color(.62f,.58f,.5f,.55f));EditorUtility.SetDirty(dust);
        var root=new GameObject("ArcadeDamage");
        try
        {
            var hit=Group(root.transform,"Hit",new Vector3(.25f,1.05f,0));
            MeshObject(hit,"RedRays",Star(7,.48f,.08f),red);
            MeshObject(hit,"WhiteCore",Star(4,.17f,.04f),white);
            // 正式死亡片段向前倒下，落尘覆盖躯干着地点而非原站立脚点。
            var landing=Group(root.transform,"Landing",new Vector3(0,.07f,.45f));
            MeshObject(landing,"DustClouds",Dust(),dust);
            var rescue=Group(root.transform,"Rescue",new Vector3(0,0,.45f));
            Box(rescue,"CrossHorizontal",new Vector3(0,1f,0),new Vector3(.44f,.1f,.1f),teal);
            Box(rescue,"CrossVertical",new Vector3(0,1f,0),new Vector3(.1f,.44f,.1f),teal);
            var ring=new GameObject("RescueRing",typeof(LineRenderer));ring.transform.SetParent(rescue,false);ring.transform.localPosition=Vector3.up*.04f;
            var line=ring.GetComponent<LineRenderer>();line.useWorldSpace=false;line.loop=false;line.positionCount=30;line.widthMultiplier=.035f;line.sharedMaterial=teal;
            for(int i=0;i<30;i++){float a=i*Mathf.PI*1.65f/29;line.SetPosition(i,new Vector3(Mathf.Cos(a)*.65f,0,Mathf.Sin(a)*.65f));}
            hit.gameObject.SetActive(false);landing.gameObject.SetActive(false);rescue.gameObject.SetActive(false);
            foreach(var r in root.GetComponentsInChildren<Renderer>(true)){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
            PrefabUtility.SaveAsPrefabAsset(root,Folder+"/PlayerDamage.prefab");
        }
        finally{Object.DestroyImmediate(root);}
        root=PrefabUtility.LoadPrefabContents(Player);
        try
        {
            var serialized=new SerializedObject(root.GetComponent<Game.PlayerView>());
            serialized.FindProperty("arcadeDamageTemplate").objectReferenceValue=AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/PlayerDamage.prefab");
            serialized.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(root,Player);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();return "PlayerDamage saved and bound to formal PlayerView";
    }
    private static Transform Group(Transform parent,string name,Vector3 position)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;return go.transform;}
    private static Material Material(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Art/Arcade/VFX/Bomb/BombAdditive.shader"));AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",color);material.SetTexture("_BaseMap",Texture2D.whiteTexture);EditorUtility.SetDirty(material);return material;
    }
    private static void MeshObject(Transform parent,string name,Mesh mesh,Material material)
    {
        string path=Folder+"/"+name+".asset";mesh.name=name;var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(saved==null){AssetDatabase.CreateAsset(mesh,path);saved=mesh;}else{EditorUtility.CopySerialized(mesh,saved);Object.DestroyImmediate(mesh);}
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=saved;go.GetComponent<MeshRenderer>().sharedMaterial=material;
    }
    private static void Box(Transform parent,string name,Vector3 position,Vector3 scale,Material mat)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<MeshRenderer>().sharedMaterial=mat;
    }
    private static Mesh Star(int points,float outer,float inner)
    {
        var v=new List<Vector3>{Vector3.zero};var t=new List<int>();var c=new List<Color>{Color.white};
        for(int i=0;i<points*2;i++){float a=i*Mathf.PI/points,r=i%2==0?outer:inner;v.Add(new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r));c.Add(new Color(1,1,1,i%2==0?.08f:.8f));}
        for(int i=0;i<points*2;i++)t.AddRange(new[]{0,i+1,(i+1)%(points*2)+1});
        return Build(v,t,c,null);
    }
    private static Mesh Dust()
    {
        var v=new List<Vector3>();var t=new List<int>();var c=new List<Color>();var uv=new List<Vector2>();
        for(int i=0;i<9;i++)
        {
            float a=i*2.4f,r=.18f+i*.055f,s=.25f+(i%3)*.09f;var p=new Vector3(Mathf.Cos(a)*r,i*.006f,Mathf.Sin(a)*r);int k=v.Count;
            v.Add(p+new Vector3(-s,0,-s));v.Add(p+new Vector3(-s,0,s));v.Add(p+new Vector3(s,0,s));v.Add(p+new Vector3(s,0,-s));
            uv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});for(int j=0;j<4;j++)c.Add(Color.white);t.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});
        }
        return Build(v,t,c,uv);
    }
    private static Mesh Build(List<Vector3> v,List<int> t,List<Color> c,List<Vector2> uv)
    {var m=new Mesh();m.SetVertices(v);m.SetTriangles(t,0);m.SetColors(c);if(uv!=null)m.SetUVs(0,uv);m.RecalculateNormals();m.RecalculateBounds();return m;}
}

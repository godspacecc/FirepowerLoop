using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

/// <summary>制作可随角色模型替换的青白冲刺模板，只保存纯表现网格和材质。</summary>
public static class ArcadeDashAuthoring
{
    private const string Folder="Assets/Game/Art/Arcade/VFX/Dash";
    private const string Kit="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var cyan=Material("DashCyan",new Color(.18f,1.8f,2.5f,.8f));
        var white=Material("DashWhite",new Color(1.7f,2.6f,2.8f,.85f));
        var dust=Material("DashDust",new Color(.45f,.48f,.5f,.34f));
        var root=new GameObject("Dash");
        try
        {
            MeshObject(root.transform,"Burst",Burst(),white);
            var ribbons=new GameObject("Ribbons");ribbons.transform.SetParent(root.transform,false);
            MeshObject(ribbons.transform,"Left",Ribbon(-.32f,.65f,2.5f,.14f),cyan);
            MeshObject(ribbons.transform,"Right",Ribbon(.32f,.38f,2.1f,.12f),white);
            MeshObject(ribbons.transform,"Ground",Ribbon(.05f,.015f,2.9f,.06f),cyan);
            var fragments=new GameObject("Fragments");fragments.transform.SetParent(root.transform,false);
            MeshObject(fragments.transform,"FragmentLeft",Ribbon(-.3f,.3f,.55f,.045f),cyan);
            MeshObject(fragments.transform,"FragmentRight",Ribbon(.28f,.03f,.8f,.035f),white);
            var cloud=new GameObject("Dust");cloud.transform.SetParent(root.transform,false);
            MeshObject(cloud.transform,"Chips",Dust(),dust);
            foreach(var r in root.GetComponentsInChildren<Renderer>()){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
            PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Dash.prefab");
        }
        finally{Object.DestroyImmediate(root);}
        root=PrefabUtility.LoadPrefabContents(Kit);
        try
        {
            var parent=root.transform.Find("Content");var old=parent.Find("Dash");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var child=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/Dash.prefab"),parent);
            child.name="Dash";child.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(child);PrefabUtility.SaveAsPrefabAsset(root,Kit);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();return "Dash template saved: directional burst, three ribbons, dust chips";
    }
    private static Material Material(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(mat==null){mat=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Art/Arcade/VFX/Bomb/BombAdditive.shader"));AssetDatabase.CreateAsset(mat,path);}
        mat.SetColor("_BaseColor",color);mat.SetTexture("_BaseMap",Texture2D.whiteTexture);EditorUtility.SetDirty(mat);return mat;
    }
    private static void MeshObject(Transform parent,string name,Mesh mesh,Material material)
    {
        string path=Folder+"/"+name+".asset";mesh.name=name;var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);existing=mesh;}else{EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);}
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=existing;go.GetComponent<MeshRenderer>().sharedMaterial=material;
    }
    /// <summary>三段渐宽再收尖的带状棱片沿负 Z 延伸，不使用人物残影。</summary>
    private static Mesh Ribbon(float x,float y,float length,float width)
    {
        var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
        for(int i=0;i<8;i++)
        {
            float t=i/7f;float center=x+Mathf.Sin(t*Mathf.PI)*x*.4f;float w=Mathf.Sin(t*Mathf.PI)*width;
            vertices.Add(new Vector3(center-w,y,-.2f-length*t));vertices.Add(new Vector3(center,y,-.2f-length*t));vertices.Add(new Vector3(center+w,y,-.2f-length*t));
            float alpha=(1-t)*.9f;colors.Add(new Color(1,1,1,0));colors.Add(new Color(1,1,1,alpha));colors.Add(new Color(1,1,1,0));
            if(i>0){int k=i*3;triangles.AddRange(new[]{k-3,k,k-2,k-2,k,k+1,k-2,k+1,k-1,k-1,k+1,k+2});}
        }
        return Build(vertices,triangles,colors);
    }
    private static Mesh Burst()
    {
        var v=new List<Vector3>();var t=new List<int>();var c=new List<Color>();
        for(int i=0;i<10;i++)
        {
            float a=Mathf.Lerp(-1.3f,1.3f,i/9f);var d=new Vector3(Mathf.Sin(a),0,-Mathf.Cos(a));var side=new Vector3(-d.z,0,d.x);
            int k=v.Count;v.Add(d*.32f+side*.1f);v.Add(d*(i%2==0?1.05f:.8f));v.Add(d*.5f-side*.12f);
            t.AddRange(new[]{k,k+1,k+2});c.Add(Color.white);c.Add(new Color(1,1,1,.05f));c.Add(Color.white);
        }
        return Build(v,t,c);
    }
    private static Mesh Dust()
    {
        var v=new List<Vector3>();var t=new List<int>();var c=new List<Color>();
        for(int i=0;i<10;i++)
        {
            float x=Mathf.Sin(i*7.1f)*.6f,z=-.4f-i*.16f,size=.035f+(i%3)*.025f;int k=v.Count;
            v.Add(new Vector3(x-size,.03f,z));v.Add(new Vector3(x,.12f,z+size));v.Add(new Vector3(x+size,.03f,z));v.Add(new Vector3(x,.01f,z-size));
            t.AddRange(new[]{k,k+1,k+2,k,k+2,k+3});for(int j=0;j<4;j++)c.Add(Color.white);
        }
        return Build(v,t,c);
    }
    private static Mesh Build(List<Vector3> v,List<int> t,List<Color> c)
    {var m=new Mesh();m.SetVertices(v);m.SetTriangles(t,0);m.SetColors(c);m.RecalculateNormals();m.RecalculateBounds();return m;}
}

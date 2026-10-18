using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>制作炸弹三阶段的纯表现模板；保存直接材质引用，不携带碰撞或伤害脚本。</summary>
public static class ArcadeBombAuthoring
{
    private const string Folder="Assets/Game/Art/Arcade/VFX/Bomb";
    private const string Kit="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var glow=SaveMaterial("BombGlow",new Color(3f,.48f,.025f,1));
        var ringGlow=SaveMaterial("BombRing",new Color(3f,.75f,.08f,1));ringGlow.SetFloat("_SoftEdge",1);EditorUtility.SetDirty(ringGlow);
        var core=SaveMaterial("BombCore",new Color(4f,3.3f,1.6f,1));
        var soft=SaveMaterial("BombSoftGlow",new Color(3f,.7f,.08f,.7f));
        soft.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Art/Effects/ArcadeARPG/Textures/basicglow.png"));EditorUtility.SetDirty(soft);
        var shard=SaveMesh("BombShard",Star(3,.25f));var star=SaveMesh("BombFlash",Star(16,.23f));
        var root=new GameObject("BombBlast");
        try
        {
            var flash=new GameObject("Flash",typeof(MeshFilter),typeof(MeshRenderer));flash.transform.SetParent(root.transform,false);
            flash.GetComponent<MeshFilter>().sharedMesh=star;flash.GetComponent<MeshRenderer>().sharedMaterial=core;
            var glowPlane=new GameObject("Glow",typeof(MeshFilter),typeof(MeshRenderer));glowPlane.transform.SetParent(root.transform,false);
            glowPlane.GetComponent<MeshFilter>().sharedMesh=SaveMesh("BombGlowPlane",GlowPlane());glowPlane.GetComponent<MeshRenderer>().sharedMaterial=soft;
            var rays=new GameObject("Rays",typeof(MeshFilter),typeof(MeshRenderer));rays.transform.SetParent(root.transform,false);
            rays.GetComponent<MeshFilter>().sharedMesh=SaveMesh("BombRays",Rays());rays.GetComponent<MeshRenderer>().sharedMaterial=glow;
            var ring=new GameObject("Shockwave",typeof(LineRenderer));ring.transform.SetParent(root.transform,false);
            var line=ring.GetComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;line.positionCount=96;
            line.widthMultiplier=.36f;line.sharedMaterial=ringGlow;line.numCornerVertices=1;
            for(int i=0;i<96;i++){float a=i*Mathf.PI*2/96;line.SetPosition(i,new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)));}
            var halo=Object.Instantiate(ring,root.transform);halo.name="ShockwaveHalo";halo.GetComponent<LineRenderer>().widthMultiplier=.9f;
            Burst(root.transform,"Sparks",glow,shard,42,.65f,22f,.16f,new Color(1,.65f,.2f,1));
            Burst(root.transform,"Shards",glow,shard,18,.5f,15f,.32f,new Color(1,.42f,.07f,.65f));
            var dust=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/BlastSmoke.mat");
            var ps=Burst(root.transform,"Dust",dust,null,18,.95f,7f,2.8f,new Color(.72f,.59f,.36f,.22f));
            var main=ps.main;main.startDelay=.12f;
            foreach(var r in root.GetComponentsInChildren<Renderer>()){r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
            PrefabUtility.SaveAsPrefabAsset(root,Folder+"/BombBlast.prefab");
        }
        finally{Object.DestroyImmediate(root);}
        root=PrefabUtility.LoadPrefabContents(Kit);
        try
        {
            var content=root.transform.Find("Content");var old=content.Find("BombBlast");if(old!=null)Object.DestroyImmediate(old.gameObject);
            var child=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/BombBlast.prefab"),content);
            child.name="BombBlast";child.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(child);
            PrefabUtility.SaveAsPrefabAsset(root,Kit);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        AssetDatabase.SaveAssets();return "BombBlast saved: flash, shockwave, sparks, shards, dust; no colliders or scripts";
    }
    private static Material SaveMaterial(string name,Color color)
    {
        string path=Folder+"/"+name+".mat";
        if(!File.Exists(path))AssetDatabase.CopyAsset("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/BlastGlow.mat",path);
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);material.shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/BombAdditive.shader");
        material.SetTexture("_BaseMap",Texture2D.whiteTexture);material.SetColor("_BaseColor",color);material.SetFloat("_SoftEdge",0);
        EditorUtility.SetDirty(material);return material;
    }
    private static Mesh SaveMesh(string name,Mesh mesh)
    {
        string path=Folder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);mesh.name=name;
        if(existing==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);return existing;
    }
    private static Mesh Star(int count,float inner)
    {
        var vertices=new List<Vector3>{Vector3.zero};var triangles=new List<int>();
        for(int i=0;i<count*2;i++){float a=i*Mathf.PI/count;float r=i%2==0?1:inner;vertices.Add(new Vector3(Mathf.Cos(a)*r,0,Mathf.Sin(a)*r));}
        for(int i=0;i<count*2;i++){triangles.Add(0);triangles.Add(1+(i+1)%(count*2));triangles.Add(i+1);}
        var colors=new Color[vertices.Count];colors[0]=Color.white;
        for(int i=1;i<colors.Length;i++)colors[i]=new Color(1,1,1,i%2==1?.05f:.65f);
        var m=new Mesh();m.SetVertices(vertices);m.colors=colors;m.SetTriangles(triangles,0);m.RecalculateBounds();m.RecalculateNormals();return m;
    }
    private static Mesh GlowPlane()
    {
        var m=new Mesh();m.vertices=new[]{new Vector3(-1,.015f,-1),new Vector3(-1,.015f,1),new Vector3(1,.015f,1),new Vector3(1,.015f,-1)};
        m.uv=new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};m.triangles=new[]{0,1,2,0,2,3};m.RecalculateBounds();m.RecalculateNormals();return m;
    }
    /// <summary>不规则径向棱片在爆心外留下间隙，保证角色与地面可读。</summary>
    private static Mesh Rays()
    {
        var vertices=new List<Vector3>();var triangles=new List<int>();
        for(int i=0;i<28;i++)
        {
            float a=i*Mathf.PI*2/28+.04f*Mathf.Sin(i*13);var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));var side=new Vector3(-d.z,0,d.x);
            float tip=.55f+.45f*Mathf.Abs(Mathf.Sin(i*7.31f));int k=vertices.Count;
            vertices.Add(d*(tip-.15f)+side*.012f);vertices.Add(d*tip);vertices.Add(d*(tip-.32f)-side*.018f);
            triangles.AddRange(new[]{k,k+1,k+2});
        }
        var colors=new Color[vertices.Count];for(int i=0;i<colors.Length;i++)colors[i]=new Color(1,1,1,i%3==1?1:.04f);
        var m=new Mesh();m.SetVertices(vertices);m.colors=colors;m.SetTriangles(triangles,0);m.RecalculateNormals();m.RecalculateBounds();return m;
    }
    private static ParticleSystem Burst(Transform parent,string name,Material material,Mesh mesh,int count,float life,float speed,float size,Color color)
    {
        var go=new GameObject(name,typeof(ParticleSystem));go.transform.SetParent(parent,false);
        var ps=go.GetComponent<ParticleSystem>();ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.loop=false;main.playOnAwake=false;main.duration=1;main.startLifetime=life;main.startSpeed=new ParticleSystem.MinMaxCurve(speed*.55f,speed);
        main.startSize=new ParticleSystem.MinMaxCurve(size*.4f,size);main.startColor=color;main.maxParticles=count;main.simulationSpace=ParticleSystemSimulationSpace.Local;
        main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.startRotation3D=true;main.startRotationY=new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Circle;shape.radius=.4f;shape.rotation=new Vector3(90,0,0);
        var sizeLife=ps.sizeOverLifetime;sizeLife.enabled=true;sizeLife.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,1,1,mesh==null?1.7f:.1f));
        var alpha=ps.colorOverLifetime;alpha.enabled=true;var g=new Gradient();g.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.6f,.4f),new GradientAlphaKey(0,1)});alpha.color=g;
        var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;
        if(mesh!=null){renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=mesh;}
        ps.useAutoRandomSeed=false;ps.randomSeed=37;return ps;
    }
}

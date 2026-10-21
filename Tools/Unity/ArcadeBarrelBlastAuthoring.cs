using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>从确认的预览制作桶专属紫色爆炸，保留普通火焰爆炸与已有资源身份。</summary>
public static class ArcadeBarrelBlastAuthoring
{
    const string Folder="Assets/Game/Art/Arcade/VFX/Barrel";
    const string KitPath="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本项目编辑态制作");
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Game/Art/Arcade/VFX","Barrel");
        var soft=CopyMaterial("EnemyFlameSmoke","PurpleSmoke");var glow=CopyMaterial("BlastGlow","PurpleGlow");
        var paint=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/Environment/Content/TowerMetal.mat"));
        paint.SetColor("_BaseColor",new Color(.49f,.26f,.62f));paint.color=new Color(.49f,.26f,.62f);paint=SaveMaterial(paint,"PurpleFragments");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(KitPath).transform.Find("Content/ProjectileBlast");
        var root=Object.Instantiate(source.gameObject);root.name="VortexBarrelBlast";root.transform.position=Vector3.zero;root.transform.localScale=Vector3.one;
        try
        {
            foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
                var renderer=ps.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=ps.name=="Burst"||ps.name=="Smoke"?soft:glow;
                var main=ps.main;main.playOnAwake=false;main.loop=false;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
                main.startColor=ps.name=="ImpactFlash"?new Color(1.8f,.8f,2.2f,1):ps.name=="Burst"?new Color(.8f,.055f,1,.85f):ps.name=="Sparks"?new Color(1.5f,.25f,2f,1):new Color(.48f,.065f,.7f,.6f);
                Fade(ps);var sheet=ps.textureSheetAnimation;sheet.enabled=false;
            }
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Game/Art/Arcade/VFX/EnemyExplosionDeath/SmallFragments.asset");
            var fragments=Particles(root.transform,"BarrelFragments",paint,12,1.15f,3.3f,new Color(.49f,.26f,.62f),.16f);
            var fragmentMain=fragments.main;fragmentMain.gravityModifier=.8f;fragmentMain.startRotation3D=true;
            fragmentMain.startRotationX=new ParticleSystem.MinMaxCurve(0,6.28f);fragmentMain.startRotationY=new ParticleSystem.MinMaxCurve(0,6.28f);fragmentMain.startRotationZ=new ParticleSystem.MinMaxCurve(0,6.28f);
            var rotation=fragments.rotationOverLifetime;rotation.enabled=true;rotation.x=4;rotation.y=6;rotation.z=5;
            var fragmentRenderer=fragments.GetComponent<ParticleSystemRenderer>();fragmentRenderer.renderMode=ParticleSystemRenderMode.Mesh;fragmentRenderer.mesh=mesh;
            Particles(root.transform,"PurpleResidualSmoke",soft,42,1.15f,.85f,new Color(.57f,.09f,.78f,.24f),.65f);
            root.SetActive(false);PrefabUtility.SaveAsPrefabAsset(root,Folder+"/VortexBarrelBlast.prefab");
        }
        finally{Object.DestroyImmediate(root);}
        var kit=PrefabUtility.LoadPrefabContents(KitPath);
        try
        {
            var parent=kit.transform.Find("Content");var existing=parent.Find("VortexBarrelBlast");
            if(existing==null)
            {
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder+"/VortexBarrelBlast.prefab"),parent);
                instance.name="VortexBarrelBlast";instance.SetActive(false);PrefabUtility.SaveAsPrefabAsset(kit,KitPath);
            }
        }
        finally{PrefabUtility.UnloadPrefabContents(kit);}
        return "桶专属紫色爆炸已加入当前Content租约；普通ProjectileBlast保持原资源";
    }
    /// <summary>正式粒子的一次性发射，不创建业务Update、碰撞或伤害组件。</summary>
    static ParticleSystem Particles(Transform root,string name,Material material,int count,float lifetime,float speed,Color color,float size)
    {
        var ps=new GameObject(name,typeof(ParticleSystem)).GetComponent<ParticleSystem>();ps.transform.SetParent(root,false);ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=ps.main;main.playOnAwake=false;main.loop=false;main.duration=2;main.startLifetime=lifetime;main.startSpeed=speed;main.startSize=size;main.startColor=color;main.maxParticles=64;main.scalingMode=ParticleSystemScalingMode.Hierarchy;
        var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.35f;
        var emission=ps.emission;emission.rateOverTime=0;emission.SetBursts(new[]{new ParticleSystem.Burst(0,(short)count)});
        ps.GetComponent<ParticleSystemRenderer>().sharedMaterial=material;Fade(ps);return ps;
    }
    static void Fade(ParticleSystem ps)
    {
        var fade=ps.colorOverLifetime;fade.enabled=true;var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.85f,.5f),new GradientAlphaKey(0,1)});fade.color=gradient;
    }
    static Material CopyMaterial(string source,string name)
    {var material=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/"+source+".mat"));material.SetColor("_BaseColor",Color.white);material.SetColor("_Color",Color.white);return SaveMaterial(material,name);}
    static Material SaveMaterial(Material material,string name)
    {
        string path=Folder+"/"+name+".mat";material.name=name;var saved=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(saved==null){AssetDatabase.CreateAsset(material,path);return material;}
        EditorUtility.CopySerialized(material,saved);Object.DestroyImmediate(material);AssetDatabase.SaveAssetIfDirty(saved);return saved;
    }
}

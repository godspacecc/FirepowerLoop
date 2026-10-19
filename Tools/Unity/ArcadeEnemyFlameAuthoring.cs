using System;
using Game;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>给正式街机敌人制作随躯干移动的受火、火星和死亡余烟，不修改动画或战斗数值。</summary>
public static class ArcadeEnemyFlameAuthoring
{
    private const string Materials="Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/";
    public static string Run()
    {
        if(EditorApplication.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作敌人火焰");
        var fire=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Fire.mat");
        var glow=AssetDatabase.LoadAssetAtPath<Material>(Materials+"Glow.mat");
        var smoke=AssetDatabase.LoadAssetAtPath<Material>(Materials+"EnemyFlameSmoke.mat");
        if(smoke==null)
        {
            if(!AssetDatabase.CopyAsset(Materials+"BlastSmoke.mat",Materials+"EnemyFlameSmoke.mat"))throw new Exception("烟雾材质创建失败");
            smoke=AssetDatabase.LoadAssetAtPath<Material>(Materials+"EnemyFlameSmoke.mat");
        }
        if(fire==null||glow==null||smoke==null)throw new Exception("敌人火焰材质缺失");
        foreach(var name in new[]{"ArcadeEnemyBasic","ArcadeEnemyArcher","ArcadeEnemyBrute","ArcadeEnemyBossArcher"})
        {
            string path="Assets/Game/Prefabs/Characters/"+name+".prefab";
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var view=root.GetComponent<EnemyView>();var animator=view.Animator;
                var spine=animator.GetBoneTransform(HumanBodyBones.Spine);
                if(spine==null)throw new Exception(name+"缺少躯干骨骼");
                var old=spine.Find("EnemyFlameEffects");if(old!=null)Object.DestroyImmediate(old.gameObject);
                var group=new GameObject("EnemyFlameEffects");group.transform.SetParent(spine,false);
                var body=Create("BodyFlame",group.transform,fire,true,.25f,.45f,.25f,.4f);
                var main=body.main;main.maxParticles=40;
                var emission=body.emission;emission.rateOverTime=55;
                var shape=body.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.45f,.35f,.65f);
                var sheet=body.textureSheetAnimation;sheet.enabled=true;sheet.numTilesX=16;sheet.numTilesY=5;
                sheet.frameOverTime=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,0,1,1));
                var velocity=body.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
                velocity.x=new ParticleSystem.MinMaxCurve(0f,0f);velocity.y=new ParticleSystem.MinMaxCurve(.35f,.7f);velocity.z=new ParticleSystem.MinMaxCurve(0f,0f);
                Fade(body,new Color(1,.35f,.03f),.9f);

                var embers=Create("HitEmbers",group.transform,glow,false,.2f,.45f,.035f,.065f);
                main=embers.main;main.maxParticles=80;main.simulationSpace=ParticleSystemSimulationSpace.World;main.gravityModifier=.35f;
                emission=embers.emission;emission.enabled=false;
                shape=embers.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.3f;
                velocity=embers.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
                velocity.x=new ParticleSystem.MinMaxCurve(-1.2f,1.2f);velocity.y=new ParticleSystem.MinMaxCurve(.8f,1.8f);velocity.z=new ParticleSystem.MinMaxCurve(-1.2f,1.2f);
                Fade(embers,new Color(1,.32f,.02f),1);

                var cloud=Create("DeathSmoke",group.transform,smoke,false,.6f,1f,.35f,.7f);
                main=cloud.main;main.duration=.6f;main.maxParticles=32;main.simulationSpace=ParticleSystemSimulationSpace.World;
                emission=cloud.emission;emission.rateOverTime=24;
                shape=cloud.shape;shape.shapeType=ParticleSystemShapeType.Sphere;shape.radius=.28f;
                velocity=cloud.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
                velocity.x=new ParticleSystem.MinMaxCurve(-.15f,.15f);velocity.y=new ParticleSystem.MinMaxCurve(.4f,.8f);velocity.z=new ParticleSystem.MinMaxCurve(-.15f,.15f);
                var size=cloud.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.Linear(0,.4f,1,1.6f));
                Fade(cloud,new Color(.12f,.1f,.08f),.55f);
                var previous=root.GetComponent<EnemyFlameFeedback>();if(previous!=null)Object.DestroyImmediate(previous);
                var feedback=group.AddComponent<EnemyFlameFeedback>();
                var serialized=new SerializedObject(feedback);
                serialized.FindProperty("bodyFlame").objectReferenceValue=body;
                serialized.FindProperty("embers").objectReferenceValue=embers;
                serialized.FindProperty("deathSmoke").objectReferenceValue=cloud;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                feedback.ResetForReuse();PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        AssetDatabase.SaveAssets();return "4种正式敌人已接入躯干火焰、受击火星和死亡余烟";
    }

    /// <summary>粒子仅随既有表现播放，默认不自启、不携带碰撞或伤害。</summary>
    private static ParticleSystem Create(string name,Transform parent,Material material,bool loop,float lifeMin,float lifeMax,float sizeMin,float sizeMax)
    {
        var go=new GameObject(name,typeof(ParticleSystem));go.transform.SetParent(parent,false);go.transform.localRotation=Quaternion.Euler(-90,0,0);
        var particles=go.GetComponent<ParticleSystem>();particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=particles.main;main.loop=loop;main.playOnAwake=false;main.duration=1;main.startSpeed=0;
        main.startLifetime=new ParticleSystem.MinMaxCurve(lifeMin,lifeMax);main.startSize=new ParticleSystem.MinMaxCurve(sizeMin,sizeMax);
        main.startColor=Color.white;main.simulationSpace=ParticleSystemSimulationSpace.Local;
        var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        return particles;
    }
    private static void Fade(ParticleSystem particles,Color end,float alpha)
    {
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(end,1)},
            new[]{new GradientAlphaKey(alpha,0),new GradientAlphaKey(alpha,.45f),new GradientAlphaKey(0,1)});
        var colors=particles.colorOverLifetime;colors.enabled=true;colors.color=gradient;
    }
}

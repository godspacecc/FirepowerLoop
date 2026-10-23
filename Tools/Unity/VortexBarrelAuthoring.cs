using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using Luban;
using FirepowerLoop.Arcade;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
/// <summary>将用户采用的浓紫雾候选原样持久化，资源归当前Content租约，不新建动态寻址。</summary>
public static class VortexBarrelAuthoring
{
    private const string Folder="Assets/Game/Art/Arcade/VFX/Barrel";
    private const string Kit="Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab";
    public static string Run()
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying)throw new Exception("仅本项目编辑态");
        var prior=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        ArcadeCombatViewController view=null;GameObject candidate=null;
        try
        {
            var root=new GameObject("BarrelAuthoringRoot");view=new ArcadeCombatViewController();typeof(ArcadeCombatViewController).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,root);
            // 直接复用已采用候选的旧桶几何构造器，避免正式入口改为Prefab后重跑时再次嵌套浓雾。
            candidate=new GameObject("VortexBarrelVisual");var body=candidate.transform;
            var cylinder=typeof(ArcadeCombatViewController).GetMethod("RoundCylinder",BindingFlags.Instance|BindingFlags.NonPublic);
            cylinder.Invoke(view,new object[]{body,"BarrelBody",0f,.55f,0f,.9f,1.1f,0x7D429DFFu});
            cylinder.Invoke(view,new object[]{body,"UpperMetalHoop",0f,.95f,0f,.96f,.12f,0x3E4850FFu});
            cylinder.Invoke(view,new object[]{body,"LowerMetalHoop",0f,.15f,0f,.96f,.12f,0x3E4850FFu});
            cylinder.Invoke(view,new object[]{body,"VortexSmoke",0f,1.3f,0f,.6f,.18f,0xB16AE2FFu});
            typeof(ArcadeCombatViewController).GetMethod("RoundRing",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{body,"VortexMouth",.32f,1.12f,0xDC9CFFFFu});
            const float height=1.761965f;candidate.transform.localScale=Vector3.one*(height*.5f/1.1f);
            var funnel=new Funnel(candidate.transform,height);funnel.Tick(0);
            var straps=new GameObject("ShoulderStraps").transform;straps.SetParent(candidate.transform,false);straps.localScale=Vector3.one/candidate.transform.localScale.x;
            var metal=candidate.transform.Find("UpperMetalHoop").GetComponent<Renderer>().sharedMaterial;
            foreach(float x in new[]{-.2f,.2f})
            {
                var line=new GameObject("ShoulderStrap",typeof(LineRenderer)).GetComponent<LineRenderer>();line.transform.SetParent(straps,false);line.useWorldSpace=false;line.positionCount=4;line.widthMultiplier=.055f;line.sharedMaterial=metal;
                line.SetPositions(new[]{new Vector3(x,.6f,.02f),new Vector3(x,.95f,.22f),new Vector3(x,.95f,.44f),new Vector3(x,.41f,.58f)});
            }
            straps.gameObject.SetActive(false);
            int meshIndex=0;foreach(var filter in candidate.GetComponentsInChildren<MeshFilter>(true))if(!EditorUtility.IsPersistent(filter.sharedMesh))
            {
                var mesh=Object.Instantiate(filter.sharedMesh);string path=Folder+"/VortexBarrelMesh"+(meshIndex++)+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(old==null)AssetDatabase.CreateAsset(mesh,path);else{EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);mesh=old;}filter.sharedMesh=mesh;
            }
            var materials=candidate.GetComponentsInChildren<Renderer>(true).Select(r=>r.sharedMaterial).Distinct().ToArray();int index=0;
            foreach(var material in materials)
            {
                string path=Folder+"/VortexBarrelMaterial"+(index++)+".mat";var persistent=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(persistent==null){persistent=Object.Instantiate(material);AssetDatabase.CreateAsset(persistent,path);}else EditorUtility.CopySerialized(material,persistent);
                foreach(var renderer in candidate.GetComponentsInChildren<Renderer>(true))if(renderer.sharedMaterial==material)renderer.sharedMaterial=persistent;
                if(!EditorUtility.IsPersistent(material)&&!material.name.StartsWith("RoundOwned_"))Object.DestroyImmediate(material);
            }
            foreach(var t in candidate.GetComponentsInChildren<Transform>(true))t.name=t.name.Replace("Preview_","");
            candidate.SetActive(false);string prefabPath=Folder+"/VortexBarrelVisual.prefab";PrefabUtility.SaveAsPrefabAsset(candidate,prefabPath);
            var kit=PrefabUtility.LoadPrefabContents(Kit);try
            {
                var parent=kit.transform.Find("Content");if(parent.Find("VortexBarrelVisual")==null){var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),parent);instance.name="VortexBarrelVisual";instance.SetActive(false);PrefabUtility.SaveAsPrefabAsset(kit,Kit);}
            }finally{PrefabUtility.UnloadPrefabContents(kit);}
            AssetDatabase.SaveAssets();return "正式桶及浓紫雾已保存，沿用已采用候选Funnel，身体高0.8809825米；Content同租约";
        }
        finally{if(candidate!=null)Object.DestroyImmediate(candidate);view?.End();SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);}
    }
    private sealed class Funnel : IDisposable
    {
        private readonly ParticleSystem smoke;
        private readonly ParticleSystem.Particle[] particles=new ParticleSystem.Particle[190];
        private readonly LineRenderer[] ribbons=new LineRenderer[2];
        private readonly Material soft,flow,core;private readonly float outerRadius;
        public Funnel(Transform barrel,float humanHeight)
        {
            outerRadius=humanHeight;
            var air=new GameObject("Preview_IndependentWorldSizedVortex").transform;air.SetParent(barrel,false);air.localPosition=new Vector3(0,1.12f,0);
            air.localScale=new Vector3(1/barrel.lossyScale.x,1/barrel.lossyScale.y,1/barrel.lossyScale.z);
            barrel.Find("VortexSmoke").gameObject.SetActive(false);
            soft=new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Art/Arcade/VFX/FirstBatch/Materials/EnemyFlameSmoke.mat"));
            soft.name="Preview_VioletExistingSmoke";
            soft.SetColor("_BaseColor",Color.white);soft.SetColor("_Color",Color.white);
            flow=new Material(soft);flow.SetTexture("_BaseMap",null);
            core=new Material(Shader.Find("Universal Render Pipeline/Unlit"));core.color=new Color(.035f,.01f,.075f);
            var mouth=GameObject.CreatePrimitive(PrimitiveType.Cylinder);mouth.name="Preview_DarkMouth";mouth.transform.SetParent(barrel,false);
            mouth.transform.localPosition=new Vector3(0,1.107f,0);mouth.transform.localScale=new Vector3(.62f,.006f,.62f);
            Object.DestroyImmediate(mouth.GetComponent<Collider>());mouth.GetComponent<Renderer>().sharedMaterial=core;
            smoke=new GameObject("Preview_SwirlingVapor",typeof(ParticleSystem)).GetComponent<ParticleSystem>();smoke.transform.SetParent(air,false);
            var main=smoke.main;main.playOnAwake=false;main.maxParticles=220;main.simulationSpace=ParticleSystemSimulationSpace.Local;main.startSpeed=0;main.startLifetime=10;
            var emission=smoke.emission;emission.enabled=false;var shape=smoke.shape;shape.enabled=false;
            var renderer=smoke.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=soft;renderer.renderMode=ParticleSystemRenderMode.Billboard;renderer.sortingFudge=.1f;
            smoke.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            for(int arm=0;arm<ribbons.Length;arm++)
            {
                var line=new GameObject("Preview_ConvergingRibbon",typeof(LineRenderer)).GetComponent<LineRenderer>();line.transform.SetParent(air,false);
                line.useWorldSpace=false;line.positionCount=72;line.sharedMaterial=flow;line.numCapVertices=4;line.widthMultiplier=1;
                line.widthCurve=new AnimationCurve(new Keyframe(0,0),new Keyframe(.15f,.024f),new Keyframe(.65f,.04f),new Keyframe(1,0));
                var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(new Color(.73f,.26f,1),0),new GradientColorKey(new Color(.6f,.12f,.95f),1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.24f,.15f),new GradientAlphaKey(.12f,.7f),new GradientAlphaKey(0,1)});
                line.colorGradient=gradient;ribbons[arm]=line;
            }
        }
        public void Tick(float time)
        {
            for(int arm=0;arm<ribbons.Length;arm++)for(int step=0;step<72;step++)
            {
                float u=step/71f,r=.09f+(outerRadius-.09f)*u,angle=u*Mathf.PI*2.5f-time*3.5f+arm*Mathf.PI;
                ribbons[arm].SetPosition(step,new Vector3(Mathf.Cos(angle)*r,.05f+u*.5f,Mathf.Sin(angle)*r));
            }
            for(int i=0;i<particles.Length;i++)
            {
                float age=Mathf.Repeat(time*.34f+(i/4)/47.5f,1),u=1-age;
                float r=(.50f+(outerRadius*1.5f-.50f)*u)*(.97f+.03f*Mathf.Sin(i*4.1f)),angle=(i%4)*Mathf.PI*.5f-time*2.3f+u*Mathf.PI*2.5f;
                particles[i].position=new Vector3(Mathf.Cos(angle)*r,.06f+u*.55f,Mathf.Sin(angle)*r);
                particles[i].startSize=.28f+.6f*u;particles[i].startColor=new Color(.70f,.055f,.98f,.45f*Mathf.Pow(Mathf.Sin(age*Mathf.PI),.65f));
                particles[i].startLifetime=10;particles[i].remainingLifetime=10;particles[i].rotation=i*41+time*30;
            }
            smoke.SetParticles(particles,particles.Length);smoke.Pause();
        }
        public void Dispose(){Object.DestroyImmediate(soft);Object.DestroyImmediate(flow);Object.DestroyImmediate(core);}
    }


}

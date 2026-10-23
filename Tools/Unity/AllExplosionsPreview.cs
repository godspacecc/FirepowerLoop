using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>其他爆炸的隔离动态对比，只改临时实例，不写回任何正式资源。</summary>
public static class AllExplosionsPreview
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly string[] Names={"energy","barrel","bomb","drone","cluster-main","cluster-child","enemy-death","decoy","orbit-barrel"};
    /// <summary>每类48帧、20fps，当前与候选相同随机种子、相机和正式主角参照。</summary>
    public static string Run(int kind)
        =>Capture(kind,false);
    /// <summary>正式接入后只录制回读结果；不再用临时隐藏覆盖掩盖资源差异。</summary>
    public static string RunApplied(int kind)=>Capture(kind,true);
    private static string Capture(int kind,bool applied)
    {
        if(kind<0||kind>=Names.Length||EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅本工程编辑态隔离录制");
        string output=(applied?"Temp/AllExplosionsApplied-20261024/":"Temp/AllExplosions-v1-20261023/")+Names[kind];Directory.CreateDirectory(output);
        var previous=SceneManager.GetActiveScene();var oldRt=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        var rt=new RenderTexture(600,420,24);var tex=new Texture2D(600,420,TextureFormat.RGB24,false);Material ground=null;
        try
        {
            ground=new Material(Shader.Find("Universal Render Pipeline/Unlit"));ground.SetColor("_BaseColor",new Color(.13f,.17f,.16f));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Plane);floor.transform.localScale=Vector3.one*5;floor.GetComponent<Renderer>().sharedMaterial=ground;
            var hero=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            foreach(var canvas in hero.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
            hero.transform.position=new Vector3(-2.05f,0,-1.3f);hero.transform.rotation=Quaternion.Euler(0,20,0);
            hero.GetComponent<PlayerView>().SetSharedWeaponAnimationActive(true);var weapon=hero.GetComponent<CharacterWeaponPresenter>();
            if(!weapon.SetWeaponId("arcade.weapon.basic"))throw new Exception(weapon.LastError);
            weapon.BodyAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;weapon.BodyAnimator.Rebind();weapon.BodyAnimator.Update(.01f);weapon.ApplyOffhandGrip();
            var light=new GameObject("PreviewLight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(55,-30,0);
            var cam=new GameObject("PreviewCamera").AddComponent<Camera>();cam.orthographic=true;
            cam.orthographicSize=kind==2?10.5f:kind==4?4.5f:kind==6?3.3f:3.8f;
            cam.transform.rotation=Quaternion.Euler(68,0,0);cam.transform.position=Vector3.up*.55f-cam.transform.forward*25;
            cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.025f,.04f,.045f);cam.targetTexture=rt;cam.allowHDR=false;
            var content=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            for(int variant=applied?1:0;variant<2;variant++)
            {
                var root=new GameObject("TemporaryFeedback_"+variant);object presentation=null;GameObject effect=null;
                GameObject supportBody=null;
                if(kind>=7)
                {
                    supportBody=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/"+(kind==7?"ArcadeDropDecoy":"ArcadeDropBarrel")+".prefab"),root.transform,false);
                    supportBody.transform.localPosition=new Vector3(0,.4f,0);
                }
                var type=typeof(ArcadeCombatViewController).Assembly.GetType(kind==2?"Game.ArcadeBombBlastPresentation":"Game.EnemyExplosionDeathPresentation");
                if(kind==2)presentation=Activator.CreateInstance(type,Flags,null,new object[]{root.transform,content.transform.Find("Content/BombBlast")},null);
                else if(kind==6)
                {
                    var template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Arcade/VFX/EnemyExplosionDeath/EnemyExplosionDeath.prefab");
                    presentation=Activator.CreateInstance(type,Flags,null,new object[]{root.transform,template,1},null);
                    type.GetField("random",Flags).SetValue(presentation,new System.Random(235));
                }
                for(int frame=0;frame<48;frame++)
                {
                    if(frame==4)
                    {
                        if(kind==2)type.GetMethod("Play",Flags).Invoke(presentation,new object[]{Vector2.zero,10f});
                        else if(kind==6)type.GetMethod("Play",Flags).Invoke(presentation,new object[]{new Vector3(0,.85f,0),Vector3.forward});
                        else if(kind<7||variant==1)
                        {
                            GameObject template;
                            if(kind<=1||kind>=7)template=content.transform.Find(kind==1?"Content/VortexBarrelBlast":"Content/ProjectileBlast").gameObject;
                            else template=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/"+(kind==3?"ArcadeDroneExplosion":kind==4?"ArcadeWeaponGrenadePrimaryBlast":"ArcadeWeaponGrenadeChildBlast")+".prefab");
                            if(template==null)throw new Exception("预览模板缺失: "+Names[kind]);
                            if(applied&&(kind==0||kind>=7))
                            {
                                var view=new ArcadeCombatViewController();typeof(ArcadeCombatViewController).GetField("root",Flags).SetValue(view,root);
                                typeof(ArcadeCombatViewController).GetField("projectileBlastTemplate",Flags).SetValue(view,template.transform);
                                typeof(ArcadeCombatViewController).GetMethod("PlayProjectileBlast",Flags).Invoke(view,new object[]{Vector2.zero,kind==0?1.35f:kind==7?3.5f:2f,kind==0});
                                effect=root.transform.GetChild(root.transform.childCount-1).gameObject;
                            }
                            else{effect=Object.Instantiate(template,root.transform,false);effect.SetActive(true);effect.transform.localPosition=Vector3.up*(kind>=4&&kind<=5?.55f:.75f);}
                            if(kind==0)effect.transform.localScale=Vector3.one*Mathf.Clamp(1.35f/2.8f,.35f,2);
                            if(kind>=7)effect.transform.localScale=Vector3.one*Mathf.Clamp((kind==7?3.5f:2f)/2.8f,.35f,2);
                            foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                            {
                                ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.useAutoRandomSeed=false;ps.randomSeed=353;
                                if(kind==0){var main=ps.main;main.startColor=new Color(.3f,2.5f,4,1);}
                                if(kind==0&&variant==1&&!applied&&ps.name=="ImpactFlash")
                                {
                                    // 只增强现有中心闪光，避免橙色火焰贴图偏绿或变成方片；候选不新增正式材质。
                                    var main=ps.main;main.startLifetime=.32f;
                                    main.startSize=new ParticleSystem.MinMaxCurve(main.startSize.constantMin*1.35f,main.startSize.constantMax*1.35f);
                                }
                            }
                            if(kind>=4&&kind<=5)effect.GetComponent<ArcadeWeaponEvolutionVisual>().Apply(kind==4?3.2f:1.8f,0,kind==5,0);
                        }
                        foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true)){ps.useAutoRandomSeed=false;ps.randomSeed=353;}
                        if(kind==7)supportBody.SetActive(false);
                    }
                    if(frame>=4)
                    {
                        if(kind==2)
                        {
                            type.GetMethod("Tick",Flags).Invoke(presentation,new object[]{.05f});
                            foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))ps.Simulate((frame-4)*.05f+.001f,false,true,false);
                        }
                        else if(kind==6)type.GetMethod("Tick",Flags).Invoke(presentation,new object[]{.05f});
                        else if(kind>=4&&kind<=5)effect.GetComponent<ArcadeWeaponEvolutionVisual>().Tick(.05f);
                        else if(effect!=null)foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            // 能量弹保留中心闪光；橙色火焰与烟尾都不作为青蓝爆炸主体。
                            if(kind==0&&(ps.name=="Smoke"||ps.name=="Burst"))continue;
                            if(applied&&(kind==0||kind>=7)&&ps.name=="Sparks")continue;
                            ps.Simulate((frame-4)*.05f+.001f,false,true,false);
                        }
                        if(variant==1&&!applied)HideOuterScatter(root,kind);
                    }
                    cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,600,420),0,0);tex.Apply();
                    File.WriteAllBytes(output+"/"+variant+"-"+frame.ToString("D3")+".png",tex.EncodeToPNG());
                }
                if(presentation is IDisposable disposable)disposable.Dispose();Object.DestroyImmediate(root);
            }
            File.WriteAllText(output+"/facts.txt","Formal templates cloned; no source changes. 48 frames per variant at 20fps. Same camera, seed and main-character scale. Kind="+Names[kind]);
            return Path.GetFullPath(output);
        }
        finally
        {
            RenderTexture.active=oldRt;Object.DestroyImmediate(tex);Object.DestroyImmediate(rt);if(ground!=null)Object.DestroyImmediate(ground);
            SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);
        }
    }

    /// <summary>仅禁用候选实例上的飞散渲染；正式模板、粒子参数和资源均不更改。</summary>
    private static void HideOuterScatter(GameObject root,int kind)
    {
        foreach(var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            string name=ps.name.ToLowerInvariant();
            if(name.Contains("spark")||name.Contains("fragment")||name.Contains("shard"))ps.GetComponent<ParticleSystemRenderer>().enabled=false;
        }
        if(kind==2)
            foreach(var mesh in root.GetComponentsInChildren<MeshRenderer>(true))if(mesh.name=="Rays")mesh.enabled=false;
    }
}

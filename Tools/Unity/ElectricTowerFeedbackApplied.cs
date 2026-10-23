using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Game;
using Luban;
using FirepowerLoop.Arcade;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>正式电塔命中入口的隔离场景采样；声音在编码时按正式资源与增益同步。</summary>
public static class ElectricTowerFeedbackApplied
{
    private static readonly List<Object> owned=new List<Object>();
    private const float HitTime=6.1f;
    private sealed class Pair
    {
        public Transform Root;
        public GameObject Hero;
        public Renderer[] HeroRenderers;
        public object Presentation;
                public ArcadeCombatViewController View;
        public bool HitPlayed;
    }

    /// <summary>两侧沿用正式塔与主角；右侧只调用正式命中入口，不重画临时命中实现。</summary>
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")
            throw new InvalidOperationException("仅FirepowerLoop编辑态制作临时反馈预览");
        var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
        if(!provider.TryGetArcadeCatalog(out var catalog,out var error))throw new InvalidOperationException(error);
        var source=catalog.Arenas.Single(a=>a.ArenaNumber==21).Hazards.Single();
        var p=new ArcadeHazardParameters(source.Id,source.Kind,0,0,source.DirectionDegrees,source.Radius,source.ArmLength,source.ArmHalfWidth,
            source.IdleSeconds,source.WarningSeconds,source.ActiveSeconds,source.AngularSpeedDegrees,source.HitIntervalSeconds,source.PlayerDamageRatio,source.BossDamageRatio);
        var prior=SceneManager.GetActiveScene();var priorRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        RenderTexture rt=null;Texture2D pixels=null;
        var pairs=new List<Pair>();
        string folder=Path.GetFullPath("Temp/ElectricTowerFeedbackApplied");Directory.CreateDirectory(folder);
        try
        {
            SceneManager.SetActiveScene(scene);owned.Clear();
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.66f,.70f);

            var deck=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=new Color(.28f,.24f,.19f)};owned.Add(deck);
            var holder=new GameObject("TEMP_ELECTRIC_HIT_PREVIEW");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content/DocksElectricTower").gameObject;
            var heroPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            var type=typeof(ArcadeCombatViewController).Assembly.GetType("Game.ArcadeHazardPresentation");var sync=type.GetMethod("Sync");
            for(int side=0;side<2;side++)
            {
                var root=new GameObject(side==0?"CURRENT_FORMAL":"FORMAL_FEEDBACK_APPLIED").transform;root.SetParent(holder.transform,false);root.localPosition=new Vector3(side==0?-3.7f:2.7f,0,0);
                var pair=new Pair{Root=root};pairs.Add(pair);
                pair.Presentation=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{root,prefab},null);
                sync.Invoke(pair.Presentation,new object[]{new[]{p},0d});
                pair.Hero=Object.Instantiate(heroPrefab,root);pair.Hero.transform.rotation=Quaternion.Euler(0,210,0);
                var weapon=pair.Hero.GetComponent<CharacterWeaponPresenter>();if(!weapon.SetWeaponId("arcade.weapon.basic"))throw new InvalidOperationException(weapon.LastError);
                weapon.SetManualPoseSampling(true);weapon.BodyAnimator.Rebind();weapon.BodyAnimator.Update(0);
                weapon.Current.Animations.Idle.Clip.SampleAnimation(weapon.BodyAnimator.gameObject,.1f);weapon.ApplyOffhandGrip(weapon.Current.Animations.Idle);
                pair.HeroRenderers=pair.Hero.GetComponentsInChildren<Renderer>(true);
                foreach(var canvas in pair.Hero.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
                if(side==1)
                {
                    pair.View=new ArcadeCombatViewController();
                    void Set(string name,object value)=>typeof(ArcadeCombatViewController).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pair.View,value);
                    Set("root",root.gameObject);Set("mapRoot",root);Set("contentArena",21);Set("contentTemplates",prefab.transform.parent);
                    Set("contentHazards",new[]{p});Set("electricHitDefinitions",new[]{p});
                }
            }
            for(int i=0;i<28;i++)
            {
                var plank=GameObject.CreatePrimitive(PrimitiveType.Cube);plank.name="TEMP_木平台";plank.transform.SetParent(holder.transform,false);
                plank.transform.localPosition=new Vector3(0,-.1f,-5+i*.38f);plank.transform.localScale=new Vector3(19,.18f,.36f);
                Object.DestroyImmediate(plank.GetComponent<Collider>());plank.GetComponent<Renderer>().sharedMaterial=deck;
            }
            foreach(var transform in holder.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=30;
            var light=new GameObject("TEMP_Sun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.45f;light.shadows=LightShadows.Soft;
            light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(50,-30,0);
            var camera=new GameObject("TEMP_Camera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=3.55f;
            camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.transform.position=new Vector3(0,10,-10);camera.transform.LookAt(new Vector3(0,1,.2f));
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.20f,.24f);
            rt=new RenderTexture(1280,720,24){antiAliasing=4};rt.Create();pixels=new Texture2D(1280,720,TextureFormat.RGB24,false);camera.targetTexture=rt;
            for(int frame=0;frame<212;frame++)
            {
                // 完整8.5秒周期后追加四分之一速命中回放，字幕编码时明确标注。
                float time=frame<170?frame/20f:5.95f+(frame-170)/80f;
                float hitAge=time-HitTime;var heroPosition=HeroPosition(time);
                foreach(var pair in pairs)
                {
                    sync.Invoke(pair.Presentation,new object[]{new[]{p},(double)time});
                    pair.Hero.transform.localPosition=heroPosition;
                    // 只示意致命机关命中后的消失与原位电火花；不增加站立麻痹或控制限制。
                    pair.Hero.SetActive(time<HitTime+.20f||time>=8.0f);
                    if(pair.View!=null)
                    {
                        typeof(ArcadeCombatViewController).GetField("electricHitDefinitions",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pair.View,new[]{p});
                        if(frame==170){typeof(ArcadeCombatViewController).GetMethod("ReleaseElectricTowerFeedback",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pair.View,null);pair.HitPlayed=false;}
                        if(hitAge>=0&&!pair.HitPlayed){pair.View.PlayElectricTowerHit(p.Id,new Vector2(heroPosition.x,heroPosition.z));pair.HitPlayed=true;}
                        if(hitAge>=0)pair.View.TickPlayerFeedback(frame<170?.05f:.0125f);
                    }
                }
                camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,1280,720),0,0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),pixels.EncodeToPNG());
            }
            camera.targetTexture=null;
            File.WriteAllText(Path.Combine(folder,"timing.txt"),"212 frames;20fps;10.6s;hit=6.1s;replay=8.5s;replayHit=9.1s;source cycle=8.5s;radius=1.2;formal view entry; isolated scene; post-synchronized audio");
            return folder+"; "+File.ReadAllText(Path.Combine(folder,"timing.txt"));
        }
        finally
        {
            foreach(var pair in pairs){pair.View?.End();if(pair.Presentation is IDisposable d)d.Dispose();}
            RenderTexture.active=priorRT;if(pixels!=null)Object.DestroyImmediate(pixels);if(rt!=null){rt.Release();Object.DestroyImmediate(rt);}
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
            foreach(var o in owned)if(o!=null)Object.DestroyImmediate(o);owned.Clear();
        }
    }

    /// <summary>正式主角靠近危险圈；镜头采样轨迹只用于预览，不作为正式碰撞或伤害判断。</summary>
    private static Vector3 HeroPosition(float time)
    {
        var outside=new Vector3(1.95f,0,-1.25f);var contact=new Vector3(1.05f,0,-.70f);
        return time>=8?outside:Vector3.Lerp(outside,contact,Mathf.Clamp01((time-5.5f)/.6f));
    }

}

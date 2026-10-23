using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>已确认纵队的正式资源隔离录制；牛由真实活动循环推进，玩家按基础速度作同框对照，不保存场景。</summary>
public static class HerdSingleFileCapture
{
    private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Run()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅编辑态录制");
        var prior=SceneManager.GetActiveScene();var oldLoader=ResourceLoader.Current;var oldRT=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);object fixture=null;Type fixtureType=null;ArcadeCombatViewController view=null;
        string folder=Path.GetFullPath("Temp/HerdSingleFile-20261023");Directory.CreateDirectory(folder);Directory.CreateDirectory(folder+"/frames");
        try
        {
            SceneManager.SetActiveScene(scene);
            // 复用隔离存档fixture获取真实System及Model，逐帧调用现有权威活动入口。
            fixtureType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Game.Tests.ArcadeHerdIntegrationTests")).First(t=>t!=null);
            fixture=Activator.CreateInstance(fixtureType);fixtureType.GetMethod("Setup").Invoke(fixture,null);
            var system=fixtureType.GetField("system",Hidden).GetValue(fixture);var runtime=fixtureType.GetField("runtime",Hidden).GetValue(fixture);var runtimeType=runtime.GetType();
            runtimeType.GetField("PlayerPosition").SetValue(runtime,new Vector2(-29,-17));runtimeType.GetField("PlayerInvulnerability").SetValue(runtime,100f);
            var catalog=(ArcadeConfigCatalogDefinition)system.GetType().GetField("catalog",Hidden).GetValue(system);
            var stage=catalog.EarlyStages.Single(p=>p.ArenaConfigNumber==22);
            ResourceLoader.Use(new Loader());view=new ArcadeCombatViewController();var root=new GameObject("ConfirmedFarmIntegration");
            typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);typeof(ArcadeCombatViewController).GetField("earlyStage",Hidden).SetValue(view,stage);
            view.ConfigureRoundBounds(stage.HalfWidth,stage.HalfHeight,.55f);view.SetRoundLayout(catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==22&&p.RoundNumber==1));
            if(!view.IsRoundLayoutReady)throw new InvalidOperationException(view.RoundLayoutLoadError??"场地未就绪");
            var hero=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),root.transform);
            var presenter=hero.GetComponent<CharacterWeaponPresenter>();presenter.SetWeaponId("arcade.weapon.basic");presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            hero.transform.localPosition=new Vector3(-29,0,-17);foreach(var canvas in hero.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
            var light=new GameObject("CaptureSun",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.15f;light.cullingMask=1<<30;light.transform.rotation=Quaternion.Euler(48,-30,0);
            var camera=new GameObject("CaptureCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=36;camera.cullingMask=1<<30;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.09f,.11f);camera.farClipPlane=250;
            camera.transform.position=new Vector3(0,68,-44);camera.transform.LookAt(Vector3.zero);
            void Layers(){foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;}
            void Capture(string name,int width,int height)
            {
                Layers();var rt=RenderTexture.GetTemporary(width,height,24);var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
                try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(folder,name),texture.EncodeToPNG());}
                finally{camera.targetTexture=null;RenderTexture.active=oldRT;Object.DestroyImmediate(texture);RenderTexture.ReleaseTemporary(rt);}
            }
            var tick=system.GetType().GetMethod("TickRoundContent",Hidden);tick.Invoke(system,new[]{runtime,(object)8f});
            var cows=(List<ArcadeHerdState>)runtimeType.GetField("HerdCows").GetValue(runtime);long map=(long)runtimeType.GetField("HerdMapId").GetValue(runtime);
            if(cows.Count!=5)throw new InvalidOperationException("未生成实际前后纵队");
            for(int frame=0;frame<65;frame++)
            {
                if(frame>0)tick.Invoke(system,new[]{runtime,(object).1f});
                // 玩家仅作基础速度同框对照；牛的位置仍由正式活动循环推进。
                float motionSeconds=Mathf.Max(0,frame*.1f-stage.Herd.WindupSeconds);
                var heroPosition=new Vector3(-29+catalog.Rule.PlayerMoveSpeed*motionSeconds,0,-17);
                hero.transform.localPosition=heroPosition;runtimeType.GetField("PlayerPosition").SetValue(runtime,new Vector2(heroPosition.x,heroPosition.z));
                hero.transform.rotation=Quaternion.Euler(0,90,0);
                var move=presenter.Current.Animations.MoveForward;move.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,motionSeconds%move.Clip.length);presenter.ApplyOffhandGrip(move);
                if(cows.Select(c=>c.Z).Distinct().Count()!=1||cows.Select((c,i)=>Mathf.Abs(c.X-cows[0].X+i*3)<.001f).Any(ok=>!ok))throw new InvalidOperationException("正式纵队出现横排或间距漂移");
                view.SetHerds(map,cows.Select(c=>ArcadeHerdRules.Snapshot(c,stage.Herd)).ToArray());
                camera.orthographicSize=frame==0?36:24;camera.transform.position=frame==0?new Vector3(0,68,-44):new Vector3(-14,39,-43);camera.transform.LookAt(frame==0?Vector3.zero:new Vector3(-14,0,-14));
                if(frame==30)Capture("farm-integration.png",1400,1000);
                Capture("frames/herd-"+frame.ToString("D3")+".png",1000,700);
            }
            return folder;
        }
        catch(Exception error)
        {
            throw new InvalidOperationException(error.GetBaseException().ToString());
        }
        finally
        {
            view?.End();fixtureType?.GetMethod("Cleanup")?.Invoke(fixture,null);ResourceLoader.Use(oldLoader);RenderTexture.active=oldRT;
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
    private sealed class Loader:IResourceLoader
    {
        public T LoadAsset<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab");
        public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> complete) where T:Object{complete(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
        public void Release(ResourceLoadHandle handle){}
    }
}

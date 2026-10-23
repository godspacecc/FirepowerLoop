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

/// <summary>隔离采样正式Prefab及正式快照投影，展示背桶到卸桶；不改资源，不冒充真人对局。</summary>
public static class DockBarrelAppliedCapture
{
    public static string Run()
    {
        if(Application.isPlaying||Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new Exception("仅本工程编辑态");
        var prior=SceneManager.GetActiveScene();var oldTarget=RenderTexture.active;var loader=ResourceLoader.Current;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);SceneManager.SetActiveScene(scene);
        ArcadeCombatViewController view=null;RenderTexture target=null;Texture2D image=null;
        try
        {
            ResourceLoader.Use(new FormalLoader());
            var root=new GameObject("AppliedDockBarrelCapture");view=new ArcadeCombatViewController();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Field(string key,object value)=>typeof(ArcadeCombatViewController).GetField(key,flags).SetValue(view,value);
            Field("root",root);Field("contentTemplates",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            Field("earlyStage",ArcadeEarlyStageParameters.FromConfig(tables.TbArcadeEarlyStage.Get(21)));
            view.ConfigureRoundBounds(20.5f,19.5f,.55f);view.SetRoundLayout(ArcadeRoundLayoutParameters.FromConfig(tables.TbArcadeRoundLayout.Get("arena.21.round.1")));
            if(!view.IsRoundLayoutReady)throw new Exception(view.RoundLayoutLoadError);
            var zombie=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"));zombie.SetActive(true);
            zombie.transform.position=new Vector3(0,0,-4);zombie.transform.rotation=Quaternion.Euler(0,65,0);
            var enemy=zombie.GetComponent<EnemyView>();enemy.SetNavigationEnabled(false);enemy.BeginPresentationUse();enemy.SetPresentationVariant(0);enemy.Animator.Rebind();enemy.Animator.Update(0);
            var clip=enemy.Animator.runtimeAnimatorController.animationClips.First(c=>c.name.EndsWith("|Idle_Attack"));clip.SampleAnimation(enemy.Animator.gameObject,clip.length*.35f);enemy.Animator.enabled=false;
            Field("enemyPresentation",new EnemyPort{Root=zombie.transform});
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));player.SetActive(true);
            player.transform.position=new Vector3(-3.5f,0,-4);player.transform.rotation=Quaternion.Euler(0,160,0);
            var presenter=player.GetComponent<CharacterWeaponPresenter>();if(!presenter.SetWeaponId("arcade.weapon.basic"))throw new Exception(presenter.LastError);
            presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,0);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
            foreach(var animator in player.GetComponentsInChildren<Animator>())animator.enabled=false;
            foreach(var canvas in player.GetComponentsInChildren<Canvas>(true))canvas.gameObject.SetActive(false);
            foreach(var ps in player.GetComponentsInChildren<ParticleSystem>(true))ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var camera=new GameObject("CaptureCamera",typeof(Camera)).GetComponent<Camera>();camera.cullingMask=1<<30;camera.orthographic=true;camera.orthographicSize=3.9f;
            var focus=new Vector3(0,.55f,-4);camera.transform.position=focus+new Vector3(0,7,-15);camera.transform.LookAt(focus);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.11f,.15f);
            var light=new GameObject("CaptureKey",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.eulerAngles=new Vector3(45,-35,0);light.cullingMask=1<<30;
            target=new RenderTexture(1280,720,24);image=new Texture2D(1280,720,TextureFormat.RGB24,false);camera.targetTexture=target;
            string folder=Path.GetFullPath("Temp/DockBarrelApplied");Directory.CreateDirectory(folder);
            for(int frame=0;frame<96;frame++)
            {
                double time=frame/12d;bool carrying=time<5;
                view.SetRoundContent(time,carrying?new[]{new ArcadeContentAreaSnapshot(-10000,11,3,-4,.45f,false)}:new[]{new ArcadeContentAreaSnapshot(-10000,11,3,-4,.45f,false),new ArcadeContentAreaSnapshot(-10001,13,0,-4,.45f,false)});
                view.SetEnemyContent(7,carrying?1:0,0,false);
                zombie.transform.position=new Vector3(carrying?0:Mathf.Min(2.1f,(float)(time-5)),0,-4);
                foreach(var sceneRoot in scene.GetRootGameObjects())foreach(var t in sceneRoot.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
                camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(folder,frame.ToString("D3")+".png"),image.EncodeToPNG());
            }
            camera.targetTexture=null;return folder+";96 frames; official controller and prefabs; synthetic snapshots at active seconds; not live gameplay";
        }
        finally
        {
            view?.End();ResourceLoader.Use(loader);RenderTexture.active=oldTarget;if(image!=null)Object.DestroyImmediate(image);if(target!=null){target.Release();Object.DestroyImmediate(target);}
            SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);
        }
    }
    private sealed class EnemyPort:IArcadeEnemyPresentationPort
    {
        public Transform Root;
        public bool TryGetEnemyPresentationRoot(int id,out Transform value){value=Root;return value!=null;}
        public bool TrySetEnemy(int id,string config,Vector3 position,Vector3 facing,int health,int maximum,out string error,bool telegraph=false,int sequence=0,bool persistentHealthBar=false){error=null;return true;}
        public void RemoveEnemy(int id,bool playDeath=false){throw new Exception("不应移除正式丧尸");}
        public void ClearArcadeEnemies(){}
    }
    private sealed class FormalLoader:IResourceLoader
    {
        public T LoadAsset<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab");
        public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:Object{loaded(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
        public void Release(ResourceLoadHandle handle){}
    }
}

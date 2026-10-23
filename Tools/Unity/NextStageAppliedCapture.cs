using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Game;
using FirepowerLoop.Arcade;
using Luban;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>实际Controller公开阶段入口和正式Prefab的隔离动态实拍；不冒充自然对局。</summary>
public static class NextStageAppliedCapture
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static ArcadeCombatViewController view;static GameObject root;static Camera camera;
    static void Field(string name,object value)=>typeof(ArcadeCombatViewController).GetField(name,Hidden).SetValue(view,value);
    public static string Graveyard()=>Capture(23);
    public static string Temple()=>Capture(24);
    public static string Marketplace()=>Capture(25);
    static string Capture(int arena)
    {
        if(Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets"||EditorApplication.isPlaying||EditorApplication.isCompiling)throw new InvalidOperationException("仅本项目编辑态隔离实拍");
        var previous=ResourceLoader.Current;var prior=SceneManager.GetActiveScene();var oldRT=RenderTexture.active;var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);ResourceLoader.Use(new FormalAssets());
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var stage=tables.TbArcadeEarlyStage.DataList.Select(ArcadeEarlyStageParameters.FromConfig).Single(s=>s.ArenaConfigNumber==arena);
            var layout=tables.TbArcadeRoundLayout.DataList.Select(ArcadeRoundLayoutParameters.FromConfig).First(p=>p.ArenaConfigNumber==arena);
            view=new ArcadeCombatViewController();root=new GameObject("ControllerAppliedCapture");Field("root",root);Field("earlyStage",stage);view.ConfigureRoundBounds(stage.HalfWidth,stage.HalfHeight,.55f);view.SetRoundLayout(layout);
            if(!view.IsRoundLayoutReady)throw new InvalidOperationException(view.RoundLayoutLoadError??"正式资源未齐");
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(.52f,.55f,.53f);RenderSettings.fog=false;
            var sun=new GameObject("CaptureSun",typeof(Light)).GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.05f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(48,-28,0);sun.cullingMask=1<<29;
            camera=new GameObject("CaptureCamera",typeof(Camera)).GetComponent<Camera>();camera.orthographic=true;camera.cullingMask=1<<29;camera.farClipPlane=220;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.15f,.20f,.21f);
            var hero=Hero(arena==23?new Vector3(3,0,-12):arena==24?new Vector3(-3,0,-5):new Vector3(0,0,-6));
            if(arena==23){camera.orthographicSize=8.3f;camera.transform.position=new Vector3(5,15,-28);camera.transform.LookAt(new Vector3(8,1,-10));}
            else if(arena==24){camera.orthographicSize=6.5f;camera.transform.position=new Vector3(-3,14,-22);camera.transform.LookAt(new Vector3(-3,.7f,-3));}
            else{camera.orthographicSize=22;camera.transform.position=new Vector3(0,46,-34);camera.transform.LookAt(Vector3.zero);}
            string folder=Path.GetFullPath("Temp/NextStagesControllerApplied/"+(arena==23?"graveyard":arena==24?"temple":"marketplace"));Directory.CreateDirectory(folder+"/frames");
            int count=arena==23?108:96;var crowd=arena==25?CreateCrowd():null;bool removed=false;
            for(int frame=0;frame<count;frame++)
            {
                float t=frame/12f;
                if(arena==23)
                {
                    var tombs=new[]{new Vector2(8,-8),new Vector2(13,-8),new Vector2(8,-13)};
                    for(int i=0;i<tombs.Length;i++)
                    {
                        float age=t-.4f-i*1.15f;if(age<0)continue;var tomb=tombs[i];var landing=tomb+Vector2.down*ArcadeNextStageEnemyRules.GraveLandingOffset;
                        var at=age<=2.1f?landing:Vector2.MoveTowards(landing,new Vector2(hero.transform.localPosition.x,hero.transform.localPosition.z),(age-2.1f)*1.1f);
                        Project(10+i,18,at,new ArcadeStageEnemyState{Phase=age<2.1f?1:0,Age=age<2.1f?age:0,OriginX=tomb.x,OriginY=tomb.y-ArcadeNextStageEnemyRules.GraveOriginOffset});
                    }
                }
                else if(arena==24)
                {
                    if(t<1.25f)Project(20,19,new Vector2(-7,-1),new ArcadeStageEnemyState{Phase=2,Age=t,OriginX=-7,OriginY=-1,RevivesRemaining=1});
                    else if(t<3.2f)Project(20,19,Vector2.Lerp(new Vector2(-7,-1),new Vector2(-5,-4),(t-1.25f)/1.95f),new ArcadeStageEnemyState{Phase=0,RevivesRemaining=1});
                    else if(t<4.55f)Project(20,19,new Vector2(2,-2),new ArcadeStageEnemyState{Phase=3,Age=t-3.2f,OriginX=-5,OriginY=-4,RevivesRemaining=0});
                    else if(t<7.2f)Project(20,19,Vector2.Lerp(new Vector2(2,-2),new Vector2(0,-4.5f),(t-4.55f)/2.65f),new ArcadeStageEnemyState{Phase=0,RevivesRemaining=0});
                    else if(!removed){view.RemoveEnemy(20,true);removed=true;}
                }
                else
                {
                    // 普通群敌沿已采用通道手工采样正式角色；暗影仍走实际Controller状态投影。
                    for(int i=0;i<24;i++)
                    {
                        float age=t-i/4*.55f;if(age<0)continue;
                        var entry=layout.Entries[i%layout.Entries.Count];var start=new Vector2(entry.X,entry.Y);var destination=new Vector2((i%3-1)*3,-3+i%5);
                        var at=Vector2.MoveTowards(start,destination,age*4.5f);
                        if(i==23)Project(100+i,19,at,new ArcadeStageEnemyState{Phase=0,RevivesRemaining=1});
                        else
                        {
                            crowd[i].SetActive(true);var animator=crowd[i].GetComponentInChildren<Animator>(true);var clip=animator.runtimeAnimatorController.animationClips.First(c=>c.name.IndexOf("Walk",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Run",StringComparison.OrdinalIgnoreCase)>=0);
                            clip.SampleAnimation(animator.gameObject,t+i*.2f);crowd[i].transform.localPosition=new Vector3(at.x,0,at.y);var delta=destination-start;crowd[i].transform.localRotation=Quaternion.LookRotation(new Vector3(delta.x,0,delta.y));
                        }
                    }
                }
                view.TickPlayerFeedback(1f/12);foreach(var transform in root.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=29;
                Save(folder+"/frames/"+frame.ToString("D3")+".png");
            }
            File.WriteAllText(folder+"/source.txt","实际 ArcadeCombatViewController.SetEnemyContent/SetEnemy/SetNextStageEnemyState/RemoveEnemy 与 TickPlayerFeedback + 正式资源隔离采样；不是自然对局或真人双端。集市普通群敌使用正式Prefab和其自带移动片段逐帧采样；唯一暗影通过Controller。TimeScale未修改。");
            return folder;
        }
        finally
        {
            if(view!=null)typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);view=null;
            RenderTexture.active=oldRT;SceneManager.SetActiveScene(prior);EditorSceneManager.CloseScene(scene,true);ResourceLoader.Use(previous);root=null;camera=null;
        }
    }
    static void Project(int id,int kind,Vector2 position,ArcadeStageEnemyState state)
    {view.SetEnemyContent(id,kind,0,false);view.SetEnemy(id,"arcade.enemy.shambler",position,10,10);view.SetNextStageEnemyState(id,kind,state);}
    static GameObject Hero(Vector3 position)
    {
        var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),root.transform,false);
        var presenter=go.GetComponent<CharacterWeaponPresenter>();presenter.SetWeaponId("arcade.weapon.basic");presenter.SetManualPoseSampling(true);presenter.BodyAnimator.Rebind();presenter.BodyAnimator.Update(0);presenter.Current.Animations.Idle.Clip.SampleAnimation(presenter.BodyAnimator.gameObject,.1f);presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
        foreach(var canvas in go.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;go.transform.localPosition=position;return go;
    }
    static List<GameObject> CreateCrowd()
    {
        var result=new List<GameObject>();var parent=new GameObject("FormalCrowdSampling");parent.transform.SetParent(root.transform,false);parent.SetActive(false);
        for(int i=0;i<23;i++)
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab"),parent.transform,false);
            foreach(var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true))behaviour.enabled=false;foreach(var collider in go.GetComponentsInChildren<Collider>(true))collider.enabled=false;foreach(var agent in go.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))agent.enabled=false;foreach(var canvas in go.GetComponentsInChildren<Canvas>(true))canvas.enabled=false;
            foreach(var animator in go.GetComponentsInChildren<Animator>(true))animator.enabled=false;go.SetActive(false);result.Add(go);
        }
        parent.SetActive(true);return result;
    }
    static void Save(string path)
    {
        var rt=RenderTexture.GetTemporary(960,600,24);var previous=RenderTexture.active;var pixels=new Texture2D(960,600,TextureFormat.RGB24,false);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,600),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.targetTexture=null;RenderTexture.active=previous;Object.DestroyImmediate(pixels);RenderTexture.ReleaseTemporary(rt);}
    }
    sealed class FormalAssets:IResourceLoader
    {
        public T LoadAsset<T>(string path)where T:Object
        {foreach(var extension in new[]{".prefab",".asset",".fbx"}){var asset=AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+extension);if(asset!=null)return asset;}return null;}
        public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback)where T:Object{callback(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
        public void Release(ResourceLoadHandle handle){}
    }
}

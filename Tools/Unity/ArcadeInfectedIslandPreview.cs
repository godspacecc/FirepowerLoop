using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Luban;
using QFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game;
using Object=UnityEngine.Object;

/// <summary>调用正式海岛控制器生成隔离四图预览，只写Temp PNG，不运行战斗、不保存场景或存档。</summary>
public static class ArcadeInfectedIslandPreview
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Before() => Run("before");
    public static string After() => Run("after");
    public static string PaletteBefore() => Run("before-palette");
    private static string Run(string stage)
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("四图隔离预览只在编辑态执行");
        var architectureField=typeof(Architecture<global::Game.Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
        var languageCache=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
        var priorArchitecture=architectureField.GetValue(null);var priorLanguage=languageCache.GetValue(null);
        var priorLoader=ResourceLoader.Current;var priorScene=SceneManager.GetActiveScene();var priorRt=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        ArcadeCombatViewController view=null;RenderTexture rt=null;Texture2D tex=null;
        var files=new List<string>();
        try
        {
            SceneManager.SetActiveScene(scene);ResourceLoader.Use(new PreviewAssetLoader());LocalizationLanguages.ResetForTests();
            var architecture=new global::Game.Game();architectureField.SetValue(null,architecture);
            typeof(Architecture<global::Game.Game>).GetField("mInited",Private).SetValue(architecture,true);
            architecture.RegisterModel(new LocalizationModel());
            var localization=new LocalizationUtility(false);localization.LoadFromJson("zh-CN",File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));architecture.RegisterUtility(localization);
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes("Assets/Game/Config/"+name+".bytes"))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            if(!provider.TryGetArcadeCatalog(out var catalog,out var error))throw new InvalidOperationException(error);
            var opening=catalog.Arenas.Single(a=>a.ArenaNumber==1).Opening;
            var camera=new GameObject("IslandPreviewCamera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(0,16,-12);camera.transform.LookAt(Vector3.zero);camera.orthographic=true;camera.orthographicSize=9;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.17f,.22f);camera.cullingMask=1<<31;
            var light=new GameObject("IslandPreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(55,-30,0);
            rt=new RenderTexture(1400,1000,24);tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/InfectedIslandPreview"));Directory.CreateDirectory(directory);
            for(int round=1;round<=1;round++)
            {
                view=new ArcadeCombatViewController();var root=new GameObject("IslandRoundPreview_"+round);
                Set(view,"root",root);
                Call(view,"CreateArenaCollision",1,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,ArcadeIslandLayout.GetObstacles(round,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight));
                Call(view,"LoadContentAsync",1);view.SetIslandRound(round);view.SetOpening(opening,round==4);
                if(!view.IsIslandRoundReady)throw new InvalidOperationException(view.IslandRoundLoadError??"海岛正式资源未准备完成");
                if(round!=3)view.SetExits(round<3?new[]{-(round+1)}:new[]{2,3},catalog.Rule.ArenaHalfHeight,false);
                var region=new FirepowerLoop.Arcade.ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,round);
                var edge=region.Outer[round==2?48:72];view.SetIslandEntryWarning(new Vector2(edge.X*.95f,edge.Y*.95f),true);
                var actors=new GameObject("Actors");actors.transform.SetParent(root.transform,false);actors.SetActive(false);
                for(int i=0;i<6;i++)
                {
                    string prefabName=i<3?"ArcadeEnemyBasic":"ArcadeEnemyArcher";
                    var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/"+prefabName+".prefab"),actors.transform);
                    actor.transform.localPosition=new Vector3((i%3-1)*3.4f,0,i<3?1.8f:5.2f);
                    actor.transform.localRotation=Quaternion.Euler(0,180,0);
                    actor.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled=false;
                    actor.GetComponent<EnemyView>().SetPresentationVariant(i);
                }
                var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),actors.transform);
                player.transform.localPosition=new Vector3(0,0,-2);var playerAgent=player.GetComponent<UnityEngine.AI.NavMeshAgent>();if(playerAgent!=null)playerAgent.enabled=false;
                actors.SetActive(true);
                var playerView=player.GetComponent<PlayerView>();
                playerView.SetAlivePresentation(true);playerView.SetArcadePresentationActive(true);
                typeof(PlayerViewCtrl).GetMethod("ConfigureSharedArcadeWeaponPresentation",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{playerView,catalog.Weapons.Single(w=>w.Id==catalog.Rule.BaseWeaponId)});
                playerView.Animator.enabled=true;playerView.Animator.Rebind();playerView.Animator.Play("Idle",0,.3f);playerView.Animator.Update(.02f);
                int actorIndex=0;
                foreach(var enemy in actors.GetComponentsInChildren<EnemyView>())
                {
                    if(!enemy.SetCombatWeapon(actorIndex<3?"weapon.short-sword":"weapon.bow"))throw new InvalidOperationException("预览正式武器绑定失败");
                    var animator=enemy.Animator;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();animator.Update(0f);
                    string state=new[]{"Idle","Run","Attack"}[actorIndex++%3];animator.Play(state,0,.35f);animator.Update(.02f);
                    var presenter=enemy.GetComponent<CharacterWeaponPresenter>();presenter.ApplyOffhandGrip();
                    var baked=new Mesh();var skin=enemy.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.enabled);skin.BakeMesh(baked);
                    files.Add(enemy.name+" state="+state+" baked="+baked.bounds+" weapon="+presenter.Current?.Id+" error="+presenter.LastError);
                    Object.DestroyImmediate(baked);
                }
                foreach(var transform in root.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=31;
                foreach(var text in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.ForceMeshUpdate(true,true);
                foreach(var particles in root.GetComponentsInChildren<ParticleSystem>())
                {particles.Simulate(.75f,false,true,false);particles.Pause();}
                camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();
                string path=Path.Combine(directory,stage+".png");File.WriteAllBytes(path,tex.EncodeToPNG());files.Add(path);
                camera.transform.position=new Vector3(0,48,-26);camera.transform.LookAt(Vector3.zero);camera.orthographicSize=25;
                camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();
                string overview=Path.Combine(directory,stage+"-overview.png");File.WriteAllBytes(overview,tex.EncodeToPNG());files.Add(overview);
                view.End();view=null;
            }
            camera.targetTexture=null;
            return "正式控制器隔离布局预览（不是实际游玩截图）：\n"+string.Join("\n",files);
        }
        finally
        {
            try{view?.End();if(tex!=null)Object.DestroyImmediate(tex);if(rt!=null)Object.DestroyImmediate(rt);}
            finally{RenderTexture.active=priorRt;ResourceLoader.Use(priorLoader);architectureField.SetValue(null,priorArchitecture);languageCache.SetValue(null,priorLanguage);SceneManager.SetActiveScene(priorScene);EditorSceneManager.CloseScene(scene,true);}
        }
    }
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private).SetValue(owner,value);
    private static void Call(object owner,string name,params object[] values)=>owner.GetType().GetMethod(name,Private).Invoke(owner,values);
    private sealed class PreviewAssetLoader:IResourceLoader
    {
        public T LoadAsset<T>(string path)where T:Object
        {
            if(typeof(T)==typeof(TextAsset)&&!path.Contains("/"))path="Config/Localization/"+path;
            var extension=typeof(T)==typeof(TextAsset)?".json":".prefab";
            return AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+(path.EndsWith(extension)?"":extension));
        }
        public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback)where T:Object{callback(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
        public void Release(ResourceLoadHandle handle){}
    }
}

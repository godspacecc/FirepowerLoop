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

/// <summary>调用正式轮级控制器生成八十图隔离预览，只写 Temp PNG，不运行战斗或保存场景及存档。</summary>
public static class ArcadeFullRoutePreview
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Run() => RunTheme(0);
    public static string RunTheme(int arenaFilter)
    {
        if(arenaFilter<0||arenaFilter>20)throw new ArgumentOutOfRangeException(nameof(arenaFilter));
        if(EditorApplication.isPlaying)throw new InvalidOperationException("全路线隔离预览只在编辑态执行");
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
            var camera=new GameObject("FullRoutePreviewCamera",typeof(Camera)).GetComponent<Camera>();
            camera.transform.position=new Vector3(0,51,-31);camera.transform.LookAt(new Vector3(0,0,1));camera.orthographic=true;camera.orthographicSize=27;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.055f,.075f,.095f);camera.cullingMask=1<<31;
            var light=new GameObject("FullRoutePreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.8f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(55,-30,0);
            rt=new RenderTexture(1400,1000,24);tex=new Texture2D(1400,1000,TextureFormat.RGB24,false);camera.targetTexture=rt;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/FullRoutePreview"));Directory.CreateDirectory(directory);
            foreach(var p in catalog.RoundLayouts.Where(p=>arenaFilter==0||p.ArenaConfigNumber==arenaFilter))
            {
                view=new ArcadeCombatViewController();var root=new GameObject("FullRoutePreview_"+p.Id);Set(view,"root",root);
                view.ConfigureRoundBounds(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,catalog.Rule.PlayerRadius);
                if(p.ArenaConfigNumber==1)
                {
                    var obstacles=p.Obstacles.Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight)).ToArray();
                    Call(view,"CreateArenaCollision",1,19.5f,19.5f,obstacles);Call(view,"LoadContentAsync",1);
                    view.SetIslandRound(p.RoundNumber);view.SetOpening(catalog.Arenas.Single(a=>a.ArenaNumber==1).Opening,p.RoundNumber==4);
                    if(!view.IsIslandRoundReady)throw new InvalidOperationException(view.IslandRoundLoadError??"海岛表现尚未完成");
                }
                view.SetRoundLayout(p);
                if(!view.IsRoundLayoutReady)throw new InvalidOperationException(view.RoundLayoutLoadError??"轮级表现尚未完成");
                if(p.SpecialKind!=0||p.BossKind!=0){view.RecordRoundEnemyPosition(1,new Vector2(0,-5));view.SetEnemyContent(1,p.SpecialKind,p.BossKind,true);view.SetRoundEnemyHealth(1,70,100);}
                // 隔离预览显式提供与运行层同身份的机关/桶快照，不让 View 自行推断存活对象。
                var areas=new List<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot>();
                if(p.EventKind!=0)areas.Add(new FirepowerLoop.Arcade.ArcadeContentAreaSnapshot(-100,p.EventKind,8,-9,p.EventRadius,true,12,-3,p.EventKind==4));
                for(int barrel=0;barrel<p.Barrels.Count;barrel++)areas.Add(new FirepowerLoop.Arcade.ArcadeContentAreaSnapshot(-10000-barrel,11,p.Barrels[barrel].X,p.Barrels[barrel].Y,.5f,false));
                view.SetRoundContent(1,areas);
                foreach(var transform in root.GetComponentsInChildren<Transform>(true))transform.gameObject.layer=31;
                foreach(var text in root.GetComponentsInChildren<TMPro.TextMeshPro>(true))text.ForceMeshUpdate(true,true);
                camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1400,1000),0,0);tex.Apply();
                string path=Path.Combine(directory,"arena-"+p.ArenaConfigNumber.ToString("00")+"-round-"+p.RoundNumber+".png");File.WriteAllBytes(path,tex.EncodeToPNG());files.Add(path);
                view.End();view=null;
            }
            camera.targetTexture=null;
            return "正式全路线控制器隔离布局预览（不是实际游玩截图）：\n"+string.Join("\n",files);
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

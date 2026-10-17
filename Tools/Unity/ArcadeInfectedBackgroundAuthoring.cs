using UnityEngine.Playables;
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

/// <summary>用正式海岛、角色和动画搭景渲染菜单背景；不运行战斗或保存场景及存档。</summary>
public static class ArcadeInfectedBackgroundAuthoring
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Run()
    {
        const string stage="background";
        if(EditorApplication.isPlaying || Application.dataPath.Replace('\\','/')!="E:/GitHub/FirepowerLoop/Client/Assets")throw new InvalidOperationException("仅本工程编辑态制作");
        var architectureField=typeof(Architecture<global::Game.Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
        var languageCache=typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
        var priorArchitecture=architectureField.GetValue(null);var priorLanguage=languageCache.GetValue(null);
        var priorLoader=ResourceLoader.Current;var priorScene=SceneManager.GetActiveScene();var priorRt=RenderTexture.active;
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        ArcadeCombatViewController view=null;RenderTexture rt=null;Texture2D tex=null;
        var files=new List<string>();var owned=new List<Object>();var graphs=new List<UnityEngine.Playables.PlayableGraph>();
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
            camera.transform.position=new Vector3(0,5,-10);camera.transform.LookAt(new Vector3(0,1,2));camera.fieldOfView=55;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.06f,.17f,.22f);camera.cullingMask=1<<31;
            var light=new GameObject("IslandPreviewLight",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1f;light.cullingMask=1<<31;light.transform.rotation=Quaternion.Euler(55,-30,0);
            rt=new RenderTexture(1920,1080,24);tex=new Texture2D(1920,1080,TextureFormat.RGB24,false);camera.targetTexture=rt;
            string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../Temp/InfectedBackgroundPreview"));Directory.CreateDirectory(directory);
            for(int round=1;round<=1;round++)
            {
                view=new ArcadeCombatViewController();var root=new GameObject("IslandRoundPreview_"+round);
                Set(view,"root",root);
                Call(view,"CreateArenaCollision",1,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,ArcadeIslandLayout.GetObstacles(round,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight));
                Call(view,"LoadContentAsync",1);view.SetIslandRound(round);view.SetOpening(opening,round==4);
                if(!view.IsIslandRoundReady)throw new InvalidOperationException(view.IslandRoundLoadError??"海岛正式资源未准备完成");
                var actors=new GameObject("Actors");actors.transform.SetParent(root.transform,false);actors.SetActive(false);
                for(int i=0;i<6;i++)
                {
                    string prefabName=i<3?"ArcadeEnemyBasic":"ArcadeEnemyArcher";
                    var actor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/"+prefabName+".prefab"),actors.transform);
                    actor.transform.localPosition=new[]{new Vector3(-4,0,1),new Vector3(-1.5f,0,2.5f),new Vector3(-.2f,0,4),new Vector3(-5,0,5),new Vector3(-3,0,7),new Vector3(-1,0,7)}[i];
                    actor.transform.rotation=Quaternion.LookRotation(new Vector3(-3.5f,0,-2)-actor.transform.position);
                    actor.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled=false;
                    actor.GetComponent<EnemyView>().SetPresentationVariant(i);
                }
                var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"),actors.transform);
                player.transform.localPosition=new Vector3(-3.5f,0,-2);var playerAgent=player.GetComponent<UnityEngine.AI.NavMeshAgent>();if(playerAgent!=null)playerAgent.enabled=false;
                player.transform.rotation=Quaternion.LookRotation(new Vector3(3,0,6));
                actors.SetActive(true);
                var playerView=player.GetComponent<PlayerView>();
                playerView.SetAlivePresentation(true);playerView.SetArcadePresentationActive(true);
                typeof(PlayerViewCtrl).GetMethod("ConfigureSharedArcadeWeaponPresentation",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{playerView,catalog.Weapons.Single(w=>w.Id==catalog.Rule.BaseWeaponId)});
                playerView.Animator.enabled=true;playerView.Animator.Rebind();playerView.Animator.Play("Idle",0,.3f);playerView.Animator.Update(.02f);
                var playerPresenter=player.GetComponent<CharacterWeaponPresenter>();
                var graph=UnityEngine.Playables.PlayableGraph.Create("BackgroundPlayerPose");graphs.Add(graph);
                graph.SetTimeUpdateMode(UnityEngine.Playables.DirectorUpdateMode.Manual);
                var playable=UnityEngine.Animations.AnimationClipPlayable.Create(graph,playerPresenter.Current.Animations.Idle.Clip);
                var animationOutput=UnityEngine.Animations.AnimationPlayableOutput.Create(graph,"Player",playerView.Animator);
                animationOutput.SetSourcePlayable(playable);playable.SetTime(.3);graph.Play();graph.Evaluate(0f);
                playerPresenter.ApplyOffhandGrip(playerPresenter.Current.Animations.Idle,.3f);
                Gradient(camera,root.transform,owned);
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
                camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1920,1080),0,0);tex.Apply();
                string path=Path.Combine(directory,stage+".png");File.WriteAllBytes(path,tex.EncodeToPNG());files.Add(path);
                BindArtwork(path);
                view.End();view=null;
            }
            camera.targetTexture=null;
            return "同款正式资源搭景渲染背景（不是实际游玩截图），菜单/加载引用已更新：\n"+string.Join("\n",files);
        }
        finally
        {
            try{foreach(var graph in graphs)if(graph.IsValid())graph.Destroy();foreach(var item in owned)Object.DestroyImmediate(item);view?.End();if(tex!=null)Object.DestroyImmediate(tex);if(rt!=null)Object.DestroyImmediate(rt);}
            finally{RenderTexture.active=priorRt;ResourceLoader.Use(priorLoader);architectureField.SetValue(null,priorArchitecture);languageCache.SetValue(null,priorLanguage);SceneManager.SetActiveScene(priorScene);EditorSceneManager.CloseScene(scene,true);}
        }
    }
    /// <summary>用临时Unity网格做右侧界面遮罩；场景主体全部来自正式模型、材质和动画。</summary>
    private static void Gradient(Camera camera,Transform parent,List<Object> owned)
    {
        var mesh=new Mesh{name="MenuReadabilityGradient"};var vertices=new List<Vector3>();var colors=new List<Color>();var indices=new List<int>();
        for(int x=0;x<5;x++)
        {
            float u=.5f+x*.125f;float alpha=new[]{0f,.25f,.65f,.88f,.97f}[x];
            for(int y=0;y<2;y++){vertices.Add(camera.ViewportToWorldPoint(new Vector3(u,y,1f)));colors.Add(new Color(.015f,.035f,.065f,alpha));}
            if(x>0){int i=x*2;indices.AddRange(new[]{i-2,i-1,i,i,i-1,i+1});}
        }
        mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        var material=new Material(Shader.Find("Sprites/Default")){name="MenuReadabilityGradient",renderQueue=4000};
        var go=new GameObject("MenuReadabilityGradient",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
        go.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        owned.Add(mesh);owned.Add(material);
        // 页脚版本/更新提示使用白字，在浅沙地上增加柔和底部遮罩。
        var footer=new Mesh{name="MenuFooterGradient"};vertices.Clear();colors.Clear();indices.Clear();
        for(int row=0;row<3;row++)
        {
            float v=row*.14f;float alpha=new[]{.65f,.18f,0f}[row];
            for(int x=0;x<2;x++){vertices.Add(camera.ViewportToWorldPoint(new Vector3(x,v,1f)));colors.Add(new Color(.015f,.035f,.065f,alpha));}
            if(row>0){int i=row*2;indices.AddRange(new[]{i-2,i-1,i,i,i-1,i+1});}
        }
        footer.SetVertices(vertices);footer.SetColors(colors);footer.SetTriangles(indices,0);footer.RecalculateBounds();
        var footerGo=new GameObject("MenuFooterGradient",typeof(MeshFilter),typeof(MeshRenderer));footerGo.transform.SetParent(parent,false);
        footerGo.GetComponent<MeshFilter>().sharedMesh=footer;footerGo.GetComponent<MeshRenderer>().sharedMaterial=material;
        footerGo.GetComponent<MeshRenderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;owned.Add(footer);
    }
    /// <summary>保留旧图及.meta，只把既有RawImage/Image引用切到同一张正式模型渲染图。</summary>
    private static void BindArtwork(string source)
    {
        const string asset="Assets/Game/Art/UI/Start/ArcadeIslandInfectedBackground.png";
        File.WriteAllBytes(asset,File.ReadAllBytes(source));AssetDatabase.ImportAsset(asset,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(asset);importer.textureType=TextureImporterType.Sprite;
        importer.spriteImportMode=SpriteImportMode.Single;importer.mipmapEnabled=false;importer.maxTextureSize=2048;
        importer.npotScale=TextureImporterNPOTScale.None;importer.SaveAndReimport();
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(asset);var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(asset);
        foreach(string path in new[]{"Assets/Game/Prefabs/UI/Home/Start.prefab","Assets/Game/Prefabs/UI/Common/Loading.prefab"})
        {
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed=false;
                if(path.Contains("/Start.prefab"))
                {
                    var image=root.GetComponentsInChildren<UnityEngine.UI.RawImage>(true).Single(i=>i.texture!=null&&
                        (AssetDatabase.GetAssetPath(i.texture).Contains("_StartBackground")||AssetDatabase.GetAssetPath(i.texture)==asset));
                    changed=image.texture!=texture;image.texture=texture;
                }
                else
                {
                    var image=root.GetComponentsInChildren<UnityEngine.UI.Image>(true).Single(i=>i.name=="BackgroundArtwork");
                    changed=image.sprite!=sprite;image.sprite=sprite;
                }
                if(changed)PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        AssetDatabase.SaveAssets();
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

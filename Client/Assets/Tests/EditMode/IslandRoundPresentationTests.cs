using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>隔离实例验证真实海岛表现、资源引用、异步就绪与离场回调；不运行玩法或存档。</summary>
    public sealed class IslandRoundPresentationTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static FieldInfo ArchitectureField => typeof(Architecture<Game>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic);
        private static FieldInfo LanguageCache => typeof(LocalizationLanguages).GetField("cachedConfig",BindingFlags.Static|BindingFlags.NonPublic);
        private object priorArchitecture, priorLanguageCache;
        private IResourceLoader priorLoader;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeCombatViewController view;
        private GameObject root;
        private AssetLoader loader;
        private GameObject resourceCopy;
        private Material wadingSourceMaterial;

        [SetUp] public void Setup()
        {
            priorArchitecture=ArchitectureField.GetValue(null); priorLoader=ResourceLoader.Current;
            priorLanguageCache=LanguageCache.GetValue(null);
            loader=new AssetLoader(); ResourceLoader.Use(loader); LocalizationLanguages.ResetForTests();
            var architecture=new Game(); ArchitectureField.SetValue(null,architecture);
            typeof(Architecture<Game>).GetField("mInited",Private).SetValue(architecture,true);
            architecture.RegisterModel(new LocalizationModel());
            var localization=new LocalizationUtility(false);
            localization.LoadFromJson("zh-CN",File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            architecture.RegisterUtility(localization);
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(name=>new ByteBuf(File.ReadAllBytes("Assets/Game/Config/"+name+".bytes"))));
            architecture.RegisterUtility<IRuntimeConfigProvider>(provider);
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
        }
        [TearDown] public void Cleanup()
        {
            try
            {
                view?.End(); if(root!=null)Object.DestroyImmediate(root);
                if(resourceCopy!=null)Object.DestroyImmediate(resourceCopy);
                if(wadingSourceMaterial!=null)Object.DestroyImmediate(wadingSourceMaterial);
            }
            finally { ResourceLoader.Use(priorLoader); ArchitectureField.SetValue(null,priorArchitecture); LanguageCache.SetValue(null,priorLanguageCache); }
        }
        private void Prepare(int round,bool delayed=false)
        {
            view?.End(); view=new ArcadeCombatViewController(); root=new GameObject("IslandPresentationTestOwner");
            Set("root",root); loader.Delayed=delayed;
            Call("CreateArenaCollision",1,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,
                ArcadeIslandLayout.GetObstacles(round,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight));
            Call("LoadContentAsync",1);
            view.SetIslandRound(round);
            view.SetOpening(catalog.Arenas.Single(a=>a.ArenaNumber==1).Opening,false);
        }
        [Test] public void FourRealLayoutsWaitForResourcesAndGenerateTheirAuthoritativeMeshes()
        {
            for(int round=1;round<=4;round++)
            {
                Prepare(round,true);
                Assert.That(view.IsIslandRoundReady,Is.False,"等待正式资源时不能开战 round="+round);
                loader.CompleteAll();
                Assert.That(view.IslandRoundLoadError,Is.Null);
                Assert.That(view.IsIslandRoundReady,Is.True,"正式资源与网格准备完成 round="+round);
                var region=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,round);
                var sand=IslandRoot.Find("Sand");
                Assert.That(sand,Is.Not.Null);
                var mesh=sand.GetComponent<MeshFilter>().sharedMesh;
                for(int i=0;i<region.Sand.Count;i++)
                    Assert.That(mesh.vertices[i+1],Is.EqualTo(new Vector3(region.Sand[i].X,0,region.Sand[i].Y)));
                var obstacles=ArcadeIslandLayout.GetObstacles(round,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight);
                for(int index=0;index<obstacles.Length;index++)
                {
                    if(round==3 && index==0)continue;
                    var obstacle=root.transform.Find("Obstacle_"+(index+1));
                    var landmark=obstacle.Find("IslandLandmark");
                    Assert.That(landmark,Is.Not.Null,"海岛阻挡必须用同源礁石地标 round="+round+" index="+index);
                    var renderers=landmark.GetComponentsInChildren<Renderer>();
                    Assert.That(renderers,Is.Not.Empty);
                    var bounds=renderers[0].bounds;
                    foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                    Assert.That(bounds.size.x,Is.EqualTo(obstacles[index].HalfExtents.x*2).Within(.015f));
                    Assert.That(bounds.size.z,Is.EqualTo(obstacles[index].HalfExtents.y*2).Within(.015f));
                    Assert.That(bounds.center.x,Is.EqualTo(obstacles[index].Center.x).Within(.015f));
                    Assert.That(bounds.center.z,Is.EqualTo(obstacles[index].Center.y).Within(.015f));
                    Assert.That(obstacle.GetComponent<BoxCollider>().enabled,Is.True);
                    Assert.That(landmark.GetComponentsInChildren<Collider>(true),Is.Empty);
                    Assert.That(obstacle.Find("Visual").gameObject.activeSelf,Is.False);
                }
                view.SetOpening(default,false);
                for(int index=0;index<obstacles.Length;index++)
                {
                    var obstacle=root.transform.Find("Obstacle_"+(index+1));
                    Assert.That(obstacle.Find("IslandLandmark"),Is.Null,"岛岸释放时清理外挂专用地标");
                    Assert.That(obstacle.GetComponent<BoxCollider>().enabled,Is.True);
                    Assert.That(obstacle.Find("Visual").gameObject.activeSelf,Is.True,"恢复原通用表现 round="+round+" index="+index);
                }
            }
        }
        [Test] public void FirstCoastalRoundExtendsInlandAndReleasesItsBackdropBeforeNextRound()
        {
            Prepare(1);
            var inland=IslandRoot.Find("InlandBackdrop");
            Assert.That(inland,Is.Not.Null);
            Assert.That(inland.GetComponent<MeshFilter>().sharedMesh.bounds.min.x,Is.LessThan(-catalog.Rule.ArenaHalfWidth*2));
            Assert.That(inland.GetComponentsInChildren<Collider>(),Is.Empty);
            Assert.That(IslandRoot.Find("CoastDecor").gameObject.activeSelf,Is.True);
            Assert.That(IslandRoot.Find("CoastDecor").GetComponentsInChildren<Collider>(true),Is.Empty);
            Prepare(2);
            Assert.That(inland==null,Is.True,"切图释放旧陆地延伸，不残留上一轮资源");
            Assert.That(IslandRoot.Find("InlandBackdrop"),Is.Null);
            Assert.That(IslandRoot.Find("CoastDecor").gameObject.activeSelf,Is.False);
        }

        [Test] public void FourthRoundWarningPrecedesFlameAndClearTurnsBothOff()
        {
            Prepare(4); Assert.That(view.IsIslandRoundReady,Is.True);
            var fire=IslandRoot.Find("Campfire"); var danger=fire.Find("DangerBoundary"); var flame=fire.Find("Flame");
            view.SetIslandCampfireWarning(true);
            Assert.That(danger.gameObject.activeInHierarchy,Is.True); Assert.That(flame.gameObject.activeInHierarchy,Is.False);
            var warningColor=danger.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor");
            view.SetOpening(catalog.Arenas.Single(a=>a.ArenaNumber==1).Opening,true); view.SetIslandCampfireWarning(false);
            Assert.That(flame.gameObject.activeInHierarchy,Is.True);
            Assert.That(danger.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor"),Is.Not.EqualTo(warningColor));
            view.SetOpening(catalog.Arenas.Single(a=>a.ArenaNumber==1).Opening,false);
            Assert.That(danger.gameObject.activeInHierarchy,Is.False); Assert.That(flame.gameObject.activeInHierarchy,Is.False);
        }
        [Test] public void ThirdRoundWreckCollisionAndExitArrowsUseTheSameLayout()
        {
            Prepare(3); Assert.That(view.IsIslandRoundReady,Is.True);
            var ship=ArcadeIslandLayout.GetObstacles(3,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight)[0];
            var collision=root.transform.Find("Obstacle_1").GetComponent<BoxCollider>();
            var wreck=IslandRoot.Find("WreckLandmark");
            Assert.That(wreck.localPosition.x,Is.EqualTo(ship.Center.x)); Assert.That(wreck.localPosition.z,Is.EqualTo(ship.Center.y));
            Assert.That(collision.size.x,Is.EqualTo(ship.HalfExtents.x*2)); Assert.That(collision.size.z,Is.EqualTo(ship.HalfExtents.y*2));
            Assert.That(wreck.GetComponent<MeshFilter>().sharedMesh.bounds.extents.x,Is.EqualTo(ship.HalfExtents.x).Within(.001f));
            view.SetExits(new[]{-4},catalog.Rule.ArenaHalfHeight,false);
            var exit=root.transform.Find("ArcadeRouteExit_-4");
            var expected=ArcadeIslandLayout.GetExitPosition(3,0,4,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,catalog.Rule.PlayerRadius);
            Assert.That(exit.localPosition,Is.EqualTo(new Vector3(expected.x,0,expected.y)));
            Assert.That(exit.Find("ExitCue/ApproachArrow"),Is.Not.Null);
            Assert.That(exit.GetComponentsInChildren<MeshRenderer>(true),Is.Empty);
        }
        [Test] public void LateResourceCallbacksCannotRepopulateReleasedMapRoot()
        {
            Prepare(3,true); var oldRoot=root; Assert.That(view.IsIslandRoundReady,Is.False);
            view.End(); root=new GameObject("ReplacementMapOwner"); Set("root",root);
            loader.CompleteAll();
            Assert.That(oldRoot==null,Is.True); Assert.That(root.transform.childCount,Is.Zero);
            Assert.That(view.IsIslandRoundReady,Is.False);
        }
        [Test] public void ReleasedLocalLayoutCannotLeakIntoFollowingSharedMap()
        {
            Prepare(4); view.End();
            root=new GameObject("FollowingSharedMapOwner"); Set("root",root);
            Call("CreateArenaCollision",1,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,
                Array.Empty<ArcadeObstacleDefinition>());
            Call("LoadContentAsync",1);
            // 既有联机调用没有本地轮图参数，必须恢复默认共享轮廓。
            view.SetOpening(catalog.Arenas.Single(a=>a.ArenaNumber==1).Opening,false);
            Assert.That(view.IslandRoundLoadError,Is.Null,"旧本地轮图身份不能令无障碍的共享地图准备失败");
            var legacy=new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight);
            var mesh=IslandRoot.Find("Sand").GetComponent<MeshFilter>().sharedMesh;
            for(int i=0;i<legacy.Sand.Count;i++)
                Assert.That(mesh.vertices[i+1],Is.EqualTo(new Vector3(legacy.Sand[i].X,0,legacy.Sand[i].Y)));
        }
        [Test] public void WadingUsesLoadedMaterialShaderInsteadOfGlobalShaderLookup()
        {
            CopyVisualResource();
            var source=resourceCopy.transform.Find("Content/BombBlast/Glow").GetComponent<Renderer>();
            // 用另一份真实 Shader 识别来源，防止编辑器的全局缓存掩盖 Android 引用缺失。
            var shader=Shader.Find("Universal Render Pipeline/Unlit");
            Assert.That(shader,Is.Not.Null);
            wadingSourceMaterial=new Material(shader);
            source.sharedMaterial=wadingSourceMaterial;
            Prepare(1,true); loader.CompleteAll();
            Assert.That(view.IslandRoundLoadError,Is.Null);
            Assert.That(view.IsIslandRoundReady,Is.True);
            var wading=root.transform.Find("IslandWadingEffects");
            Assert.That(wading,Is.Not.Null);
            Assert.That(wading.GetComponent<Renderer>().sharedMaterial.shader,Is.SameAs(shader),
                "涉水 Shader 必须来自本次资源句柄持有的材质，不能依赖 Shader.Find");
        }
        [Test] public void MissingWadingMaterialFailsBeforeCommittingIslandAndCanRetry()
        {
            CopyVisualResource();
            var source=resourceCopy.transform.Find("Content/BombBlast/Glow").GetComponent<Renderer>();
            var original=source.sharedMaterial; source.sharedMaterial=null;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                "[ArcadeCombatViewController] 岛岸加载失败：涉水效果缺少显式材质或 Shader 引用");
            Prepare(1,true); loader.CompleteAll();
            Assert.That(view.IsIslandRoundReady,Is.False);
            Assert.That(view.IslandRoundLoadError,Is.Not.Null);
            Assert.That(root.transform.Find("IslandWadingEffects"),Is.Null);
            Assert.That(typeof(ArcadeCombatViewController).GetField("island",Private).GetValue(view),Is.Null);
            source.sharedMaterial=original;
            view.RetryIslandRoundLoad(); loader.CompleteAll();
            Assert.That(view.IslandRoundLoadError,Is.Null);
            Assert.That(view.IsIslandRoundReady,Is.True);
        }
        private void CopyVisualResource()
        {
            resourceCopy=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab"));
            resourceCopy.SetActive(false); loader.VisualResource=resourceCopy;
        }
        private void Set(string field,object value)=>typeof(ArcadeCombatViewController).GetField(field,Private).SetValue(view,value);
        private Transform IslandRoot => ((GameObject)typeof(ArcadeCombatViewController).GetField("island",Private).GetValue(view)).transform;
        private void Call(string method,params object[] values)=>typeof(ArcadeCombatViewController).GetMethod(method,Private).Invoke(view,values);
        private sealed class AssetLoader:IResourceLoader
        {
            public bool Delayed;
            public GameObject VisualResource;
            private readonly Queue<Action> callbacks=new();
            public T LoadAsset<T>(string path) where T:Object
            {
                if(typeof(T)==typeof(GameObject) && VisualResource!=null &&
                    path=="Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet")return VisualResource as T;
                if(typeof(T)==typeof(TextAsset) && !path.Contains("/"))path="Config/Localization/"+path;
                var extension=typeof(T)==typeof(TextAsset)?".json":".prefab";
                return AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+(path.EndsWith(extension)?"":extension));
            }
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback) where T:Object
            { if(Delayed)callbacks.Enqueue(()=>callback(LoadAsset<T>(path)));else callback(LoadAsset<T>(path));return ResourceLoadHandle.Empty; }
            public void CompleteAll(){while(callbacks.Count>0)callbacks.Dequeue()();}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}

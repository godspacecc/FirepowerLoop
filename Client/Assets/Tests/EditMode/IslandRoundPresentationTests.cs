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
                catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==1 && p.RoundNumber==round).Obstacles
                    .Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight)).ToArray());
            Call("LoadContentAsync",1);
            view.SetIslandRound(round);
            view.SetOpening(catalog.Arenas.Single(a=>a.ArenaNumber==1).Opening,false);
        }
        [Test] public void FourRealLayoutsWaitForResourcesAndGenerateTheirAuthoritativeMeshes()
        {
            for(int round=1;round<=4;round++)
            {
                Prepare(round,true); Assert.That(view.IsIslandRoundReady,Is.False);
                loader.CompleteAll(); Assert.That(view.IslandRoundLoadError,Is.Null); Assert.That(view.IsIslandRoundReady,Is.True);
                var fixedRounds=IslandRoot.Find("FixedRounds");
                Assert.That(fixedRounds.Cast<Transform>().Count(t=>t.gameObject.activeSelf),Is.EqualTo(1));
                var actual=fixedRounds.Find("Round"+round); Assert.That(actual.gameObject.activeInHierarchy,Is.True);
                Assert.That(actual.Find("ContinuousMountain").GetComponent<MeshFilter>().sharedMesh.bounds.size.x,Is.GreaterThan(100));
                Assert.That(actual.GetComponentsInChildren<Collider>(true),Is.Empty,"实体阻挡归属权威配置根，资源不携带第二套碰撞");
                var layout=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==1&&p.RoundNumber==round);
                var feet=actual.Cast<Transform>().Where(t=>t.name=="SolidRockFoot").ToArray();
                Assert.That(feet.Length,Is.EqualTo(layout.Obstacles.Count));
                for(int i=0;i<feet.Length;i++)
                {
                    var owner=root.transform.Find("Obstacle_"+(i+1));var collider=owner.GetComponent<BoxCollider>();
                    var bounds=feet[i].GetComponent<Renderer>().bounds;
                    Assert.That(collider.enabled,Is.True); Assert.That(bounds.center.x,Is.EqualTo(collider.bounds.center.x).Within(.02f));
                    Assert.That(bounds.center.z,Is.EqualTo(collider.bounds.center.z).Within(.02f));
                    Assert.That(bounds.size.x,Is.EqualTo(collider.bounds.size.x).Within(.02f));
                    Assert.That(bounds.size.z,Is.EqualTo(collider.bounds.size.z).Within(.02f));
                    Assert.That(owner.Find("Visual").gameObject.activeSelf,Is.False);
                }
                view.SetOpening(default,false);
                for(int i=0;i<feet.Length;i++) Assert.That(root.transform.Find("Obstacle_"+(i+1)+"/Visual").gameObject.activeSelf,Is.True);
            }
        }
        [Test] public void LateObstacleCallbacksCannotExposeGenericVisualsOnAnyFixedMap()
        {
            for(int round=1;round<=4;round++)
            {
                Prepare(round,true); loader.CompleteAll(true);
                Assert.That(view.IsIslandRoundReady,Is.True,view.IslandRoundLoadError);
                int count=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==1&&p.RoundNumber==round).Obstacles.Count;
                for(int i=1;i<=count;i++) Assert.That(root.transform.Find("Obstacle_"+i+"/Visual").gameObject.activeSelf,Is.False,
                    "岛岸先完成时也不能露出晚到的通用障碍：round="+round+" obstacle="+i);
                view.SetOpening(default,false);
                for(int i=1;i<=count;i++) Assert.That(root.transform.Find("Obstacle_"+i+"/Visual").gameObject.activeSelf,Is.True);
            }
        }

        /// <summary>已采用的低多边形配色与地表细节由当前轮资源提供，不侵占中央通行或依赖临时材质。</summary>
        [Test] public void ApprovedLowPolyPaletteAndDetailsLoadForAllFourRounds()
        {
            for(int round=1;round<=4;round++)
            {
                Prepare(round);
                Assert.That(view.IsIslandRoundReady,Is.True,view.IslandRoundLoadError);
                var active=IslandRoot.Find("FixedRounds/Round"+round);
                var sandMaterial=IslandRoot.Find("Sand").GetComponent<Renderer>().sharedMaterial;
                Assert.That(sandMaterial.mainTexture,Is.Null,"正式沙地应使用已确认的纯色色块");
                var ocean=IslandRoot.Find("Ocean").GetComponent<Renderer>().sharedMaterial;
                var shallows=IslandRoot.Find("Water").GetComponent<Renderer>().sharedMaterial;
                Assert.That(ocean.color,Is.Not.EqualTo(shallows.color),"深海和浅水需要保持预览中的不同色块");
                var detail=active.Find("LowPolyDetails");
                Assert.That(detail,Is.Not.Null,"每轮必须带自己的沙斑、湿沙、通道沙带和低矮装饰");
                Assert.That(detail.GetComponentsInChildren<Collider>(true),Is.Empty,"中央视觉细节不得增加阻挡");
                Assert.That(detail.GetComponentsInChildren<Renderer>().Count(r=>r.name.StartsWith("SandTone_")),Is.EqualTo(6));
                foreach(var filter in active.GetComponentsInChildren<MeshFilter>(true))
                    Assert.That(EditorUtility.IsPersistent(filter.sharedMesh),Is.True,"正式网格必须有资源引用："+filter.name);
                foreach(var renderer in active.GetComponentsInChildren<Renderer>(true))
                    foreach(var material in renderer.sharedMaterials)
                        Assert.That(material!=null&&EditorUtility.IsPersistent(material),Is.True,"正式材质不得引用临时对象："+renderer.name);
                foreach(var renderer in detail.GetComponentsInChildren<Renderer>())
                    if(renderer.name=="BeachPebble"||renderer.name.StartsWith("DryLeaf_")||renderer.name.StartsWith("SandTone_"))
                        Assert.That(renderer.bounds.max.y,Is.LessThan(.15f),"中央装饰必须足够低矮，不形成绕行点");
            }
        }

        [Test] public void EachObstacleInstantiatesOnlyItsChosenVariantWithoutAnyMapTemplate()
        {
            Prepare(1);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab");
            int count=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==1&&p.RoundNumber==1).Obstacles.Count;
            for(int i=0;i<count;i++)
            {
                var visual=root.transform.Find("Obstacle_"+(i+1)+"/Visual");
                Assert.That(visual.Find("Island"),Is.Null,"每个障碍不能重复创建整套海岛");
                Assert.That(visual.Find("Content"),Is.Null,"每个障碍不能携带场景模板");
                Assert.That(visual.childCount,Is.EqualTo(1));
                var expected=prefab.transform.GetChild(i*2%4);
                Assert.That(visual.GetComponentsInChildren<MeshRenderer>(true).Length,
                    Is.EqualTo(expected.GetComponentsInChildren<MeshRenderer>(true).Length));
                Assert.That(visual.GetChild(0).gameObject.activeSelf,Is.True);
            }
        }

        [Test] public void FixedForestsReleaseTheirOldMapBeforeShowingNextRound()
        {
            Prepare(1);var old=IslandRoot.Find("FixedRounds");
            Assert.That(IslandRoot.Find("CoastDecor").gameObject.activeSelf,Is.False);
            Assert.That(old.Find("Round1").gameObject.activeInHierarchy,Is.True);
            Prepare(2);Assert.That(old==null,Is.True);
            Assert.That(IslandRoot.Find("FixedRounds/Round2").gameObject.activeInHierarchy,Is.True);
            Assert.That(IslandRoot.Find("FixedRounds/Round1").gameObject.activeInHierarchy,Is.False);
        }
        [Test] public void OnlyPassagesKeepInvisibleWallsAndMountainsUseAuthoritativeSolidRoots()
        {
            Prepare(1);
            foreach(string name in new[]{"NorthBoundary","EastBoundary","SouthBoundary","WestBoundary"})
                Assert.That(root.transform.Find(name).GetComponent<Collider>().enabled,Is.False);
            var gates=root.transform.Cast<Transform>().Where(t=>t.name=="IslandPassageBoundary").ToArray();
            Assert.That(gates.Length,Is.EqualTo(3));
            Assert.That(gates.All(t=>t.GetComponentsInChildren<Renderer>().Length==0&&t.GetComponent<BoxCollider>().enabled),Is.True);
            view.SetOpening(default,false);
            foreach(var gate in gates) Assert.That(gate==null,Is.True);
            Assert.That(root.transform.Find("NorthBoundary").GetComponent<Collider>().enabled,Is.True,"释放后恢复原地图合同");
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
        [Test] public void ThreeExitGuidesSharePassagesAndBoatAppearsOnlyInSecondRound()
        {
            for(int round=1;round<=4;round++)
            {
                Prepare(round);view.SetExits(new[]{-4},catalog.Rule.ArenaHalfHeight,false);
                var exits=(List<GameObject>)typeof(ArcadeCombatViewController).GetField("exitVisuals",Private).GetValue(view);
                Assert.That(exits.Count,Is.EqualTo(3));
                for(int i=0;i<3;i++)
                {
                    var p=ArcadeIslandLayout.GetExitPosition(round,i,3,catalog.Rule.ArenaHalfWidth,catalog.Rule.ArenaHalfHeight,catalog.Rule.PlayerRadius);
                    Assert.That(exits[i].transform.localPosition,Is.EqualTo(new Vector3(p.x,0,p.y)));
                }
                var boat=IslandRoot.Find("FixedRounds/Round2/DockedBoatPassage");
                Assert.That(boat.gameObject.activeInHierarchy,Is.EqualTo(round==2));
            }
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
            private readonly List<Action> callbacks=new();
            public T LoadAsset<T>(string path) where T:Object
            {
                if(typeof(T)==typeof(GameObject) && VisualResource!=null &&
                    path=="Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet")return VisualResource as T;
                if(typeof(T)==typeof(TextAsset) && !path.Contains("/"))path="Config/Localization/"+path;
                var extension=typeof(T)==typeof(TextAsset)?".json":".prefab";
                return AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+(path.EndsWith(extension)?"":extension));
            }
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback) where T:Object
            { if(Delayed)callbacks.Add(()=>callback(LoadAsset<T>(path)));else callback(LoadAsset<T>(path));return ResourceLoadHandle.Empty; }
            public void CompleteAll(bool reverse=false)
            {while(callbacks.Count>0){int index=reverse?callbacks.Count-1:0;var callback=callbacks[index];callbacks.RemoveAt(index);callback();}}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}

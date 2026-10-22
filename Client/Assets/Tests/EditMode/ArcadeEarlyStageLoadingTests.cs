using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
namespace Game.Tests
{
    /// <summary>只替换异步磁盘边界，实际正式Prefab/Controller负责准备、失败和过期释放。</summary>
    public sealed class ArcadeEarlyStageLoadingTests
    {
        private ArcadeCombatViewController view;private GameObject root;private Loader loader;private IResourceLoader previous;
        private ArcadeConfigCatalogDefinition catalog;private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
            previous=ResourceLoader.Current;loader=new Loader();ResourceLoader.Use(loader);view=new ArcadeCombatViewController();root=new GameObject("early-map");typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);
        }
        private void Start(int id)
        {
            var p=catalog.EarlyStages.First(p=>p.ArenaConfigNumber==id);view.ConfigureRoundBounds(p.HalfWidth,p.HalfHeight,.55f);
            typeof(ArcadeCombatViewController).GetField("earlyStage",Hidden)?.SetValue(view,p);view.SetRoundLayout(catalog.RoundLayouts.First(p=>p.ArenaConfigNumber==id));
        }
        private void Release()=>typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);
        [TearDown] public void Cleanup(){Release();UnityEngine.Object.DestroyImmediate(root);ResourceLoader.Use(previous);}
        [Test] public void FarmWaitsForEnvironmentAndCowBeforePublishingReadiness()
        {
            Start(22);Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(loader.Pending.Count,Is.EqualTo(2));
            loader.Finish(0);Assert.That(view.IsRoundLayoutReady,Is.False);loader.Finish(1);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true).Length,Is.GreaterThanOrEqualTo(10));Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(root.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="ThemeDecor"),Is.False);
        }
        [Test] public void DockUsesConfirmedPrefabAndReleasesItsHandleOnce()
        {
            Start(21);Assert.That(loader.Pending.Count,Is.EqualTo(1));loader.Finish(0);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
            Assert.That(root.GetComponentsInChildren<MeshRenderer>(true).Length,Is.EqualTo(7));Release();Assert.That(root.transform.childCount,Is.Zero);Assert.That(loader.Released.Count,Is.EqualTo(1));Release();Assert.That(loader.Released.Count,Is.EqualTo(1));
        }
        [Test] public void LateCallbacksCannotRestoreReleasedMap()
        {
            Start(22);Assert.That(loader.Pending.Count,Is.EqualTo(2));Release();loader.Finish(0);loader.Finish(1);
            Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(root.transform.childCount,Is.Zero);Assert.That(loader.Released.Count,Is.EqualTo(2));
        }
        [Test] public void FailedResourceKeepsMapUnreadyAndSameLayoutCanRetry()
        {
            Start(21);Assert.That(loader.Pending.Count,Is.EqualTo(1));LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex(".*已确认场地准备失败.*"));loader.Finish(0,true);Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(view.RoundLayoutLoadError,Is.Not.Null.And.Not.Empty);
            Start(21);Assert.That(loader.Pending.Count,Is.EqualTo(2));loader.Finish(1);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
        }
        [Test] public void ExpandedFarmHasPhysicalGroundSupportAndNoTemporaryObstacleArtRequest()
        {
            var stage=catalog.EarlyStages.First(p=>p.ArenaConfigNumber==22);typeof(ArcadeCombatViewController).GetField("earlyStage",Hidden).SetValue(view,stage);
            var layout=catalog.RoundLayouts.First(p=>p.ArenaConfigNumber==22);var obstacles=layout.Obstacles.Select(p=>new ArcadeObstacleDefinition(p.X,p.Y,p.HalfWidth,p.HalfHeight)).ToArray();
            typeof(ArcadeCombatViewController).GetMethod("CreateArenaCollision",Hidden).Invoke(view,new object[]{22,30.5f,30.5f,obstacles});
            var ground=root.transform.Find("EarlyStageGround");Assert.That(ground,Is.Not.Null,"扩大后的正式主角需要实际地面支撑");Assert.That(ground.GetComponent<BoxCollider>().size.x,Is.EqualTo(61));Assert.That(loader.Pending,Is.Empty,"新环境不加载通用临时障碍美术");
        }
        [Test] public void NewMapReplacesOldInvisibleWallsAndRestoresTheirOriginalEnabledState()
        {
            var map=new GameObject("real-base-map");root.transform.SetParent(map.transform,false);
            var oldWall=new GameObject("EastBoundary",typeof(BoxCollider));oldWall.transform.SetParent(map.transform,false);
            var oldDisabled=new GameObject("WestBoundary",typeof(BoxCollider));oldDisabled.transform.SetParent(map.transform,false);oldDisabled.GetComponent<Collider>().enabled=false;
            var ownedWall=new GameObject("EastBoundary",typeof(BoxCollider));ownedWall.transform.SetParent(root.transform,false);
            typeof(ArcadeCombatViewController).GetField("islandBaseArenaRoot",Hidden).SetValue(view,map.transform);
            try
            {
                Start(22);Assert.That(oldWall.GetComponent<Collider>().enabled,Is.True,"准备期间仍保留旧场地");loader.Finish(0);loader.Finish(1);
                Assert.That(oldWall.GetComponent<Collider>().enabled,Is.False,"扩大地图不能被旧±20米空气墙卡住");Assert.That(ownedWall.GetComponent<Collider>().enabled,Is.True);
                Release();Assert.That(oldWall.GetComponent<Collider>().enabled,Is.True);Assert.That(oldDisabled.GetComponent<Collider>().enabled,Is.False);
            }
            finally{root.transform.SetParent(null);UnityEngine.Object.DestroyImmediate(map);}
        }
        [Test] public void HerdSnapshotWaitsForTemplateAndRebindsAfterHiddenMap()
        {
            Start(22);var method=typeof(ArcadeCombatViewController).GetMethod("SetHerds");Assert.That(method,Is.Not.Null,"牛群只读投影入口尚未接入");
            var cows=new[]{new ArcadeHerdSnapshot(7,1,ArcadeHerdPhase.Charging,-20,-14,1,0,.2)};
            method.Invoke(view,new object[]{7L,cows});Assert.That(root.GetComponentsInChildren<ArcadeHerdVisual>(true),Is.Empty);
            loader.Finish(0);loader.Finish(1);var visual=root.GetComponentsInChildren<ArcadeHerdVisual>(true).Single();Assert.That(visual.transform.localPosition.x,Is.EqualTo(-20));
            root.SetActive(false);root.SetActive(true);method.Invoke(view,new object[]{7L,new[]{new ArcadeHerdSnapshot(7,1,ArcadeHerdPhase.Charging,-18,-14,1,0,.4)}});
            Assert.That(visual.transform.localPosition.x,Is.EqualTo(-18));Assert.That(visual.gameObject.activeSelf,Is.True);
            method.Invoke(view,new object[]{7L,System.Array.Empty<ArcadeHerdSnapshot>()});Assert.That(root.GetComponentsInChildren<ArcadeHerdVisual>(true),Is.Empty);
        }
        [Test] public void OldMapHerdSnapshotCannotReplaceCurrentCows()
        {
            Start(22);loader.Finish(0);loader.Finish(1);var method=typeof(ArcadeCombatViewController).GetMethod("SetHerds");Assert.That(method,Is.Not.Null);
            method.Invoke(view,new object[]{7L,new[]{new ArcadeHerdSnapshot(7,1,ArcadeHerdPhase.Windup,-33,-14,1,0,0)}});
            method.Invoke(view,new object[]{6L,new[]{new ArcadeHerdSnapshot(6,2,ArcadeHerdPhase.Windup,33,-14,-1,0,0)}});
            Assert.That(root.GetComponentsInChildren<ArcadeHerdVisual>(true).Single().transform.localPosition.x,Is.EqualTo(-33));
            Release();Assert.That(root.GetComponentsInChildren<ArcadeHerdVisual>(true),Is.Empty);
        }
        private sealed class Loader:IResourceLoader
        {
            public readonly List<Action<bool>> Pending=new();public readonly HashSet<object> Released=new();
            public T LoadAsset<T>(string path) where T:UnityEngine.Object=>throw new InvalidOperationException("unexpected synchronous load");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> complete) where T:UnityEngine.Object
            {var identity=new object();Pending.Add(fail=>complete(fail?null:AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab")));return new ResourceLoadHandle(identity);}
            public void Release(ResourceLoadHandle handle){Assert.That(Released.Add(handle.RawHandle),Is.True,"same handle released twice");}
            public void Finish(int index,bool fail=false)=>Pending[index](fail);
        }
    }
}

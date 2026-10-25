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

namespace Game.Tests
{
    /// <summary>已采用四轮巨石环境的配置、持久资源与轮级加载事务；最多八项。</summary>
    public sealed class HengeAppliedTests
    {
        private const string PrefabPath="Assets/Game/Prefabs/SceneObjects/ArcadeHengeVisual.prefab";
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeCombatViewController view;
        private GameObject root;
        private IResourceLoader previous;
        private Loader loader;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out catalog,out var error),Is.True,error);
        }
        [TearDown] public void Cleanup()
        {
            if(view!=null)typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            if(previous!=null)ResourceLoader.Use(previous);
        }
        [Test] public void HengeUsesConfirmedEnvironmentWithoutChangingBoundsOrHerdRules()
        {
            var stage=catalog.EarlyStages.SingleOrDefault(p=>p.ArenaConfigNumber==5);
            Assert.That(stage,Is.Not.Null,"巨石仍未接入已采用环境目录");
            Assert.That(stage.EnvironmentAddress,Is.EqualTo("Prefabs/SceneObjects/ArcadeHengeVisual"));
            Assert.That(stage.HalfWidth,Is.EqualTo(19.5f));Assert.That(stage.HalfHeight,Is.EqualTo(19.5f));
            Assert.That(stage.HerdCount,Is.Zero);Assert.That(stage.PortalX,Is.Zero);Assert.That(stage.PortalY,Is.Zero);
            // 同一资源目录不能把原巨石切进新图的出生、短步移动及传送门规则分支。
            var system=new ArcadeCombatSystem();
            typeof(ArcadeCombatSystem).GetField("catalog",Hidden).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("arena",Hidden).SetValue(system,catalog.Arenas.Single(p=>p.ArenaNumber==5));
            Assert.That(typeof(ArcadeCombatSystem).GetProperty("EarlyStage",Hidden).GetValue(system),Is.Null);
        }
        [TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)]
        public void EachPersistentVariantPreservesConfiguredFootprintsAndOuterLandmarks(int round)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);Assert.That(prefab,Is.Not.Null,"缺少采用版正式巨石Prefab");
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<Animator>(true),Is.Empty);
            var variant=prefab.transform.Find("Round_"+round);Assert.That(variant,Is.Not.Null);
            var layout=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==5&&p.RoundNumber==round);
            Assert.That(layout.FitsArena(19.5f,19.5f,.55f),Is.True);
            foreach(var filter in variant.GetComponentsInChildren<MeshFilter>(true))Assert.That(filter.sharedMesh!=null&&EditorUtility.IsPersistent(filter.sharedMesh),Is.True);
            var renderers=variant.GetComponentsInChildren<Renderer>(true);Assert.That(renderers.Length,Is.InRange(1,96),"应合并同材质静态网格");
            foreach(var renderer in renderers)foreach(var material in renderer.sharedMaterials)Assert.That(material!=null&&EditorUtility.IsPersistent(material)&&material.shader!=null,Is.True);
            for(int i=0;i<layout.Obstacles.Count;i++)
            {
                var obstacle=layout.Obstacles[i];var holder=variant.Find("ConfiguredHengeObstacle_"+i);Assert.That(holder,Is.Not.Null);
                var points=Points(holder);Assert.That(points,Is.Not.Empty);
                Assert.That(points.Min(p=>p.x),Is.EqualTo(obstacle.X-obstacle.HalfWidth).Within(.001f));Assert.That(points.Max(p=>p.x),Is.EqualTo(obstacle.X+obstacle.HalfWidth).Within(.001f));
                Assert.That(points.Min(p=>p.z),Is.EqualTo(obstacle.Y-obstacle.HalfHeight).Within(.001f));Assert.That(points.Max(p=>p.z),Is.EqualTo(obstacle.Y+obstacle.HalfHeight).Within(.001f));
            }
            var outside=variant.Find("OutsidePlayableArea_MegalithLandmarks");Assert.That(outside,Is.Not.Null);
            Assert.That(Points(outside).All(p=>Mathf.Abs(p.x)>=19.5f||Mathf.Abs(p.z)>=19.5f),Is.True);
        }
        [Test] public void LoadingAndChangingRoundsSelectOnlyTheMatchingVariant()
        {
            Prepare();
            for(int round=1;round<=4;round++)
            {
                Start(round);Assert.That(view.IsRoundLayoutReady,Is.False);loader.Finish(round-1);
                Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
                var environment=root.GetComponentsInChildren<Transform>(true).Single(p=>p.name=="ConfirmedEarlyEnvironment");
                Assert.That(environment.Cast<Transform>().Where(p=>p.gameObject.activeSelf).Select(p=>p.name),Is.EqualTo(new[]{"Round_"+round}));
                Assert.That(root.GetComponentsInChildren<Transform>(true).Count(p=>p.name.StartsWith("Entry_")),Is.EqualTo(4));
            }
            Assert.That(loader.Released.Count,Is.EqualTo(3));
        }
        [Test] public void ReleasedMapRejectsLateHengeCallbackAndReleasesHandleExactlyOnce()
        {
            Prepare();Start(1);typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);
            loader.Finish(0);Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(root.transform.childCount,Is.Zero);Assert.That(loader.Released.Count,Is.EqualTo(1));
        }
        [Test] public void MissingRoundVariantFailsBeforePublishingEnvironmentAndCanRetry()
        {
            Prepare();Start(2);var source=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);Assert.That(source,Is.Not.Null);
            var invalid=UnityEngine.Object.Instantiate(source);
            try
            {
                UnityEngine.Object.DestroyImmediate(invalid.transform.Find("Round_2").gameObject);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error,new System.Text.RegularExpressions.Regex(".*已确认场地准备失败.*"));
                loader.Finish(0,invalid);Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(root.transform.childCount,Is.Zero);Assert.That(loader.Released.Count,Is.EqualTo(1));
                Start(2);loader.Finish(1);Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
            }
            finally{UnityEngine.Object.DestroyImmediate(invalid);}
        }
        private static Vector3[] Points(Transform holder)=>holder.GetComponentsInChildren<MeshFilter>(true).SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v))).ToArray();
        private void Prepare()
        {
            previous=ResourceLoader.Current;loader=new Loader();ResourceLoader.Use(loader);view=new ArcadeCombatViewController();root=new GameObject("HengeAppliedContract");
            typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);
            typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden).SetValue(view,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
        }
        private void Start(int round)
        {
            var stage=catalog.EarlyStages.SingleOrDefault(p=>p.ArenaConfigNumber==5);Assert.That(stage,Is.Not.Null,"巨石仍未接入已采用环境目录");
            typeof(ArcadeCombatViewController).GetField("earlyStage",Hidden).SetValue(view,stage);view.ConfigureRoundBounds(stage.HalfWidth,stage.HalfHeight,.55f);
            view.SetRoundLayout(catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==5&&p.RoundNumber==round));
        }
        private sealed class Loader:IResourceLoader
        {
            private readonly List<Action<GameObject>> pending=new();
            public readonly HashSet<object> Released=new();
            public T LoadAsset<T>(string path) where T:UnityEngine.Object=>throw new InvalidOperationException("不应同步加载巨石资源");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> complete) where T:UnityEngine.Object
            {Assert.That(path,Is.EqualTo("Prefabs/SceneObjects/ArcadeHengeVisual"));var identity=new object();pending.Add(prefab=>complete(prefab as T));return new ResourceLoadHandle(identity);}
            public void Finish(int index,GameObject replacement=null)=>pending[index](replacement!=null?replacement:AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            public void Release(ResourceLoadHandle handle)=>Assert.That(Released.Add(handle.RawHandle),Is.True,"同一句柄重复释放");
        }
    }
}

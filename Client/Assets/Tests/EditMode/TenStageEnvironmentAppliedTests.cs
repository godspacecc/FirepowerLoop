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
    /// <summary>采用版三场四轮环境契约：实际目录、足迹、加载/切轮/过期回调和失败重试；限定17项。</summary>
    public sealed class TenStageEnvironmentAppliedTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeCombatViewController view;
        private GameObject root;
        private IResourceLoader previous;
        private Loader loader;
        private int arena;
        private static string Address(int number)=>"Prefabs/SceneObjects/"+(number==7?"ArcadeFortVisual":number==10?"ArcadeKoiVisual":"ArcadeFinalNestVisual");
        private static string PathFor(int number)=>"Assets/Game/"+Address(number)+".prefab";
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
        [TestCase(7)][TestCase(10)][TestCase(20)]
        public void ConfirmedEnvironmentDoesNotChangeCombatBoundsOrIntroduceHerdRules(int number)
        {
            var stage=catalog.EarlyStages.SingleOrDefault(p=>p.ArenaConfigNumber==number);
            Assert.That(stage,Is.Not.Null,"已采用环境尚未接入正式目录");
            Assert.That(stage.EnvironmentAddress,Is.EqualTo(Address(number)));
            Assert.That(stage.HalfWidth,Is.EqualTo(19.5f));Assert.That(stage.HalfHeight,Is.EqualTo(19.5f));
            Assert.That(stage.HerdCount,Is.Zero);Assert.That(stage.PortalX,Is.Zero);Assert.That(stage.PortalY,Is.Zero);
            var system=new ArcadeCombatSystem();
            typeof(ArcadeCombatSystem).GetField("catalog",Hidden).SetValue(system,catalog);
            typeof(ArcadeCombatSystem).GetField("arena",Hidden).SetValue(system,catalog.Arenas.Single(p=>p.ArenaNumber==number));
            Assert.That(typeof(ArcadeCombatSystem).GetProperty("EarlyStage",Hidden).GetValue(system),Is.Null);
        }
        [TestCase(7)][TestCase(10)][TestCase(20)]
        public void AllFourPersistentVariantsPreserveObstaclesAndKeepLandmarksOutsideCombat(int number)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(number));Assert.That(prefab,Is.Not.Null,"缺少采用版正式环境Prefab");
            Assert.That(prefab.transform.childCount,Is.EqualTo(4));
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty);Assert.That(prefab.GetComponentsInChildren<Animator>(true),Is.Empty);
            for(int round=1;round<=4;round++)
            {
                var variant=prefab.transform.Find("Round_"+round);Assert.That(variant,Is.Not.Null);
                var layout=catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==number&&p.RoundNumber==round);
                Assert.That(layout.FitsArena(19.5f,19.5f,.55f),Is.True);
                foreach(var filter in variant.GetComponentsInChildren<MeshFilter>(true))Assert.That(filter.sharedMesh!=null&&EditorUtility.IsPersistent(filter.sharedMesh),Is.True);
                var renderers=variant.GetComponentsInChildren<Renderer>(true);Assert.That(renderers.Length,Is.InRange(1,160),"同材质静态几何应合并");
                foreach(var renderer in renderers)foreach(var material in renderer.sharedMaterials)Assert.That(material!=null&&EditorUtility.IsPersistent(material)&&material.shader!=null,Is.True);
                for(int i=0;i<layout.Obstacles.Count;i++)
                {
                    var o=layout.Obstacles[i];var holder=variant.Find("ConfiguredObstacle_"+i);Assert.That(holder,Is.Not.Null);
                    var points=Points(holder);Assert.That(points,Is.Not.Empty);
                    Assert.That(points.Min(p=>p.x),Is.EqualTo(o.X-o.HalfWidth).Within(.001f));Assert.That(points.Max(p=>p.x),Is.EqualTo(o.X+o.HalfWidth).Within(.001f));
                    Assert.That(points.Min(p=>p.z),Is.EqualTo(o.Y-o.HalfHeight).Within(.001f));Assert.That(points.Max(p=>p.z),Is.EqualTo(o.Y+o.HalfHeight).Within(.001f));
                }
                var outside=variant.Find("OutsidePlayableArea_Landmarks");Assert.That(outside,Is.Not.Null);
                Assert.That(Points(outside).All(p=>Mathf.Abs(p.x)>=19.5f||Mathf.Abs(p.z)>=19.5f),Is.True);
            }
        }
        [TestCase(7)][TestCase(10)][TestCase(20)]
        public void RealLoadingAndRoundChangesSelectOnlyMatchingVariant(int number)
        {
            Prepare(number);
            for(int round=1;round<=4;round++)
            {
                Start(round);Assert.That(view.IsRoundLayoutReady,Is.False);loader.Finish(round-1);
                Assert.That(view.IsRoundLayoutReady,Is.True,view.RoundLayoutLoadError);
                var environment=root.GetComponentsInChildren<Transform>(true).Single(p=>p.name=="ConfirmedEarlyEnvironment");
                Assert.That(environment.Cast<Transform>().Where(p=>p.gameObject.activeSelf).Select(p=>p.name),Is.EqualTo(new[]{"Round_"+round}));
                Assert.That(root.GetComponentsInChildren<Transform>(true).Count(p=>p.name.StartsWith("Entry_")),Is.EqualTo(4));
                Assert.That(root.GetComponentsInChildren<Transform>(true).Any(p=>p.name=="ThemeDecor"),Is.False,"不得叠加旧程序环境");
            }
            Assert.That(loader.Released.Count,Is.EqualTo(3));
        }
        [TestCase(7)][TestCase(10)][TestCase(20)]
        public void ReleasedMapRejectsLateCallbackAndReleasesExactlyOnce(int number)
        {
            Prepare(number);Start(1);typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",Hidden).Invoke(view,null);
            loader.Finish(0);Assert.That(view.IsRoundLayoutReady,Is.False);Assert.That(root.transform.childCount,Is.Zero);Assert.That(loader.Released.Count,Is.EqualTo(1));
        }
        [TestCase(7)][TestCase(10)][TestCase(20)]
        public void MissingRoundFailsBeforePublishingAndOriginalRoundCanRetry(int number)
        {
            Prepare(number);Start(2);var source=AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(number));Assert.That(source,Is.Not.Null);
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
        [TestCase(false)][TestCase(true)]
        public void CatalogRejectsMissingOrDuplicatedAdoptedEnvironment(bool duplicate)
        {
            var tables=new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes"))));
            var rows=tables.TbArcadeEarlyStage.DataList;
            var accepted=rows.Single(p=>p.ArenaConfigNumber==7);
            if(duplicate)rows.Add(accepted);else rows.Remove(accepted);
            var validate=typeof(LubanRuntimeConfigProvider).GetMethod("ValidateArcade",BindingFlags.Static|BindingFlags.NonPublic);
            Assert.That((string)validate.Invoke(null,new object[]{tables}),Is.EqualTo("已采用场地目录缺失或重复"));
        }
        private static Vector3[] Points(Transform holder)=>holder.GetComponentsInChildren<MeshFilter>(true).SelectMany(f=>f.sharedMesh.vertices.Select(v=>f.transform.TransformPoint(v))).ToArray();
        private void Prepare(int number)
        {
            arena=number;previous=ResourceLoader.Current;loader=new Loader(number);ResourceLoader.Use(loader);view=new ArcadeCombatViewController();root=new GameObject("TenStageEnvironmentAppliedContract");
            typeof(ArcadeCombatViewController).GetField("root",Hidden).SetValue(view,root);
            typeof(ArcadeCombatViewController).GetField("contentTemplates",Hidden).SetValue(view,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
        }
        private void Start(int round)
        {
            var stage=catalog.EarlyStages.SingleOrDefault(p=>p.ArenaConfigNumber==arena);Assert.That(stage,Is.Not.Null,"已采用环境尚未接入正式目录");
            typeof(ArcadeCombatViewController).GetField("earlyStage",Hidden).SetValue(view,stage);view.ConfigureRoundBounds(stage.HalfWidth,stage.HalfHeight,.55f);
            view.SetRoundLayout(catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==arena&&p.RoundNumber==round));
        }
        /// <summary>仅替换异步资源边界；回调返回真实持久Prefab，生产加载事务仍执行。</summary>
        private sealed class Loader:IResourceLoader
        {
            private readonly int arena;
            private readonly List<Action<GameObject>> pending=new();
            public readonly HashSet<object> Released=new();
            public Loader(int number){arena=number;}
            public T LoadAsset<T>(string path) where T:UnityEngine.Object=>throw new InvalidOperationException("不应同步加载环境");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> complete) where T:UnityEngine.Object
            {Assert.That(path,Is.EqualTo(Address(arena)));var identity=new object();pending.Add(prefab=>complete(prefab as T));return new ResourceLoadHandle(identity);}
            public void Finish(int index,GameObject replacement=null)=>pending[index](replacement!=null?replacement:AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(arena)));
            public void Release(ResourceLoadHandle handle)=>Assert.That(Released.Add(handle.RawHandle),Is.True,"同一句柄重复释放");
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using Luban;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>正式码头电塔的相位投影、暂停与资源释放；不启动战斗或修改玩家档。</summary>
    public sealed class DockElectricTowerTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject root;
        private ArcadeCombatViewController view;
        private ArcadeHazardParameters[] definitions;
        [SetUp] public void Setup()
        {
            var provider=new LubanRuntimeConfigProvider(()=>new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            Assert.That(provider.TryGetArcadeCatalog(out var catalog,out var error),Is.True,error);
            definitions=catalog.Arenas.Single(a=>a.ArenaNumber==21).Hazards.ToArray();
            root=new GameObject("DockTowerTest");view=new ArcadeCombatViewController();
            Field("root",root);Field("contentArena",21);
            Field("contentTemplates",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/Arcade/ArcadeObstacleVisualSet.prefab").transform.Find("Content"));
        }
        [TearDown] public void Cleanup(){view.End();if(root!=null)Object.DestroyImmediate(root);}
        private void Field(string name,object value)=>typeof(ArcadeCombatViewController).GetField(name,Hidden).SetValue(view,value);
        private Transform Named(string name)=>root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name==name);
        private LineRenderer[] Lines(string name)=>root.GetComponentsInChildren<LineRenderer>(true).Where(l=>l.name==name).ToArray();

        [Test] public void FormalTowerLoadsPersistentCoilsAndShaderReferencesWithoutColliders()
        {
            view.SetHazards(definitions,0);
            Assert.That(Named("冠顶放电环"),Is.Not.Null,"码头应选择已采用的线圈电塔资源");
            Assert.That(Named("螺旋铜线"),Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
            foreach(var r in root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name!="DangerBoundary"))
            {
                Assert.That(EditorUtility.IsPersistent(r.sharedMaterial),Is.True,r.name);
                Assert.That(r.sharedMaterial.shader,Is.Not.Null,r.name);
            }
            foreach(var f in root.GetComponentsInChildren<MeshFilter>(true))Assert.That(EditorUtility.IsPersistent(f.sharedMesh),Is.True,f.name);
        }
        [Test] public void WarningBuildsChargeAboveTowerAndSameAuthorityTimeFreezesGeometry()
        {
            view.SetHazards(definitions,4.1);
            var core=Named("塔顶聚电核");Assert.That(core,Is.Not.Null,"正式表现必须在预警阶段显示顶部聚电");
            Assert.That(core.gameObject.activeSelf,Is.True);Assert.That(core.localPosition.y,Is.GreaterThan(2.6f));
            float initial=core.localScale.x;view.SetHazards(definitions,5.3);Assert.That(core.localScale.x,Is.GreaterThan(initial));
            var line=Lines("向上聚电").First(l=>l.enabled);var positions=new Vector3[line.positionCount];line.GetPositions(positions);
            Assert.That(positions.Last().y,Is.GreaterThan(positions.First().y));
            var scale=core.localScale;view.SetHazards(definitions,5.3);var repeated=new Vector3[line.positionCount];line.GetPositions(repeated);
            Assert.That(repeated,Is.EqualTo(positions));Assert.That(core.localScale,Is.EqualTo(scale));
            foreach(var arc in Lines("分叉放电").Where(l=>l.enabled))for(int i=0;i<arc.positionCount;i++)Assert.That(arc.GetPosition(i).y,Is.GreaterThan(1.2f),"预警火花限于塔上");
        }
        [Test] public void ActiveArcsFlickerToExistingBoundaryAndSafePhaseExtinguishesThem()
        {
            view.SetHazards(definitions,6);
            var arcs=Lines("分叉放电");Assert.That(arcs.Count(l=>l.enabled),Is.GreaterThanOrEqualTo(8));
            var first=arcs[0].GetPosition(3);view.SetHazards(definitions,6.1);Assert.That(arcs[0].GetPosition(3),Is.Not.EqualTo(first));
            // 已采用折线带少量视觉抖动；判定半径由真实边界和放电端点独立核对。
            foreach(var arc in arcs)
            {var point=arc.GetPosition(arc.positionCount-1);Assert.That(new Vector2(point.x,point.z).magnitude,Is.LessThanOrEqualTo(definitions[0].Radius+.01f));}
            var boundary=Lines("危险边界").Single();for(int i=0;i<boundary.positionCount;i++)
            {var point=boundary.GetPosition(i);Assert.That(new Vector2(point.x,point.z).magnitude,Is.EqualTo(1.2f).Within(.001f));}
            view.SetHazards(definitions,8.5);
            Assert.That(arcs.All(l=>!l.enabled),Is.True);Assert.That(Lines("向上聚电").All(l=>!l.enabled),Is.True);
            Assert.That(Named("塔顶聚电核").gameObject.activeSelf,Is.False);
        }
        [Test] public void RepeatedSnapshotsReuseObjectsAndEmptySnapshotPreservesBorrowedMaterials()
        {
            view.SetHazards(definitions,5);var core=Named("塔顶聚电核");Assert.That(core,Is.Not.Null);
            int count=root.GetComponentsInChildren<Transform>(true).Length;
            var shared=root.GetComponentsInChildren<LineRenderer>(true).Single(l=>l.name=="危险边界").sharedMaterial;
            view.SetHazards(definitions,6);Assert.That(Named("塔顶聚电核"),Is.SameAs(core));Assert.That(root.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(count));
            view.SetHazards(Array.Empty<ArcadeHazardParameters>(),6);Assert.That(core==null,Is.True);Assert.That(shared!=null,Is.True);Assert.That(EditorUtility.IsPersistent(shared),Is.True);Assert.That(root.transform.childCount,Is.Zero);
            view.SetHazards(definitions,0);Assert.That(Named("塔顶聚电核"),Is.Not.Null);view.End();Assert.That(root==null,Is.True);
        }
        [Test] public void LegacyArenaKeepsOriginalTowerAndFixedArcPresentation()
        {
            Field("contentArena",2);view.SetHazards(definitions,6);
            Assert.That(Named("塔顶聚电核"),Is.Null);Assert.That(Named("Mast"),Is.Not.Null);Assert.That(Named("ElectricDanger"),Is.Not.Null);
        }
        [Test] public void ContentLoadReplacesPendingTowerAndReplaysLatestAuthorityTime()
        {
            var previous=ResourceLoader.Current;ResourceLoader.Use(new FormalAssetLoader());
            try
            {
                Field("contentTemplates",null);view.SetHazards(definitions,5.3);
                Assert.That(Named("塔顶聚电核"),Is.Null);
                typeof(ArcadeCombatViewController).GetMethod("LoadContentAsync",Hidden).Invoke(view,new object[]{21});
                Assert.That(Named("塔顶聚电核"),Is.Not.Null);Assert.That(Named("塔顶聚电核").gameObject.activeSelf,Is.True);
                Assert.That(root.GetComponentsInChildren<Transform>(true).Count(t=>t.name.StartsWith("Hazard_")),Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<LineRenderer>(true).Count(l=>l.name=="DangerBoundary"),Is.Zero,"加载完成后应释放旧临时机关");
            }
            finally{view.End();ResourceLoader.Use(previous);}
        }
        /// <summary>仅以AssetDatabase替换外部异步运输，真实内容加载和重绘逻辑仍由正式Controller执行。</summary>
        private sealed class FormalAssetLoader:IResourceLoader
        {
            public T LoadAsset<T>(string path) where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> loaded) where T:Object{loaded(LoadAsset<T>(path));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}

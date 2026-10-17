using System.Collections;
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
    /// <summary>墓穴区域正式组件的现金身份、资源门禁与场景回收，不裁决领取。</summary>
    public sealed class SlayerMartRegionVisualTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatViewController view;
        private GameObject root;
        private ArcadeSlayerMartConfiguration config;
        private Transform templates;
        [SetUp] public void Setup()
        {
            config=ArcadeSlayerMartConfiguration.FromTables(new Tables(n=>new ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath,"Game/Config",n+".bytes")))));
            var type=typeof(ArcadeCombatViewController);string address=(string)type.GetField("ObstacleVisualAddress",BindingFlags.Static|BindingFlags.NonPublic).GetRawConstantValue();
            templates=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/"+address+".prefab").transform.Find("Content");Assert.That(templates,Is.Not.Null);
            root=new GameObject("SlayerMartRegionVisualTests");view=new();Put("root",root);Put("contentTemplates",templates);
        }
        [TearDown] public void Cleanup(){view.End();if(root!=null)Object.DestroyImmediate(root);}
        [TestCase(0,9)][TestCase(1,11)] public void RegionHasExactlyConfiguredCashBoxes(int index,int count)
        {var layout=config.Regions[index];view.SetSlayerMartRegion(layout,Enumerable.Range(1,count).ToArray());Assert.That(view.IsSlayerMartRegionReady,Is.True,view.SlayerMartRegionLoadError);Assert.That(Boxes.Count,Is.EqualTo(count));foreach(DictionaryEntry entry in Boxes){var point=layout.Boxes[(int)entry.Key-1];Assert.That(((GameObject)entry.Value).transform.localPosition,Is.EqualTo(new Vector3(point.X,0,point.Y)));}}
        [Test] public void CollectingOneBoxOnlyChangesItsPresentation()
        {var layout=config.Regions[0];view.SetSlayerMartRegion(layout,Enumerable.Range(1,9).ToArray());var region=Get<GameObject>("slayerMartRegionRoot");view.SetSlayerMartRegion(layout,Enumerable.Range(2,8).ToArray());Assert.That(Get<GameObject>("slayerMartRegionRoot"),Is.SameAs(region));Assert.That(((GameObject)Boxes[1]).activeSelf,Is.False);Assert.That(((GameObject)Boxes[2]).activeSelf,Is.True);}
        [Test] public void RegionVisualsDoNotCreateGameplayColliders()
        {view.SetSlayerMartRegion(config.Regions[0],Enumerable.Range(1,9).ToArray());var region=Get<GameObject>("slayerMartRegionRoot");Assert.That(region.GetComponentsInChildren<Renderer>(true).Length,Is.GreaterThan(0));Assert.That(region.GetComponentsInChildren<Collider>(true),Is.Empty);}
        [Test] public void CommonResourcesMustBeReadyBeforeRegionClockCanStart()
        {Put("contentTemplates",null);view.SetSlayerMartRegion(config.Regions[0],Enumerable.Range(1,9).ToArray());Assert.That(view.IsSlayerMartRegionReady,Is.False);Put("contentTemplates",templates);Assert.That(view.IsSlayerMartRegionReady,Is.True);}
        [Test] public void LateEnvironmentRefreshCannotCoverCrypt()
        {var environment=new GameObject("OldEnvironment");environment.transform.SetParent(root.transform);Put("contentEnvironment",environment);view.SetSlayerMartRegion(config.Regions[0],Enumerable.Range(1,9).ToArray());typeof(ArcadeCombatViewController).GetMethod("SyncContentEnvironment",Flags).Invoke(view,null);Assert.That(environment.activeSelf,Is.False);Assert.That(Get<GameObject>("slayerMartRegionRoot").activeSelf,Is.True);}
        [Test] public void MapEndReleasesCryptModelsAndOwnedMaterials()
        {view.SetSlayerMartRegion(config.Regions[0],Enumerable.Range(1,9).ToArray());var region=Get<GameObject>("slayerMartRegionRoot");var material=region.GetComponentInChildren<Renderer>().sharedMaterial;view.End();Assert.That(region==null,Is.True);Assert.That(material==null,Is.True);Assert.That(view.IsSlayerMartRegionReady,Is.False);Assert.That(Boxes.Count,Is.Zero);}
        private IDictionary Boxes=>Get<IDictionary>("slayerMartCashBoxes");
        private T Get<T>(string field)=>(T)typeof(ArcadeCombatViewController).GetField(field,Flags).GetValue(view);
        private void Put(string field,object value)=>typeof(ArcadeCombatViewController).GetField(field,Flags).SetValue(view,value);
    }
}

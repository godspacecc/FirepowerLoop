using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class ValleyOpenPassageTests
    {
        private GameObject root;
        private GeneratedValleyContentHandle built;
        [TearDown] public void Cleanup()
        { if(built!=null)built.ReleaseGeneratedAsync().GetAwaiter().GetResult(); if(root!=null)UnityEngine.Object.DestroyImmediate(root); }
        private async UniTask Build(int seed)
        {
            var l=ValleyRandomSwimmingTests.Generate(seed);
            var provider=ValleyRandomSwimmingTests.Provider();
            Assert.That(provider.TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var profile,out _),Is.True);
            provider.TryGetMapSpawns(MapIds.CivilisationValley,out var spawns,out _);
            var resolved=ValleyChapterSpawns.Resolve(l,spawns);
            Assert.That(resolved.Count(s=>s.ObjectType==MapSpawnObjectType.Extraction),Is.EqualTo(1));
            root=new GameObject("OpenValleyTest");var context=root.AddComponent<MapContext>();
            var dynamicRoot=new GameObject("RuntimeUnitRoot");dynamicRoot.transform.SetParent(root.transform);
            var fields=new SerializedObject(context);fields.FindProperty("mapContentRoot").objectReferenceValue=root;fields.FindProperty("dynamicObjectRoot").objectReferenceValue=dynamicRoot.transform;fields.ApplyModifiedPropertiesWithoutUndo();root.SetActive(false);
            var result=await new ValleyMapBuilder(new Loader()).BuildAsync(new Handle(context),l,profile,CancellationToken.None);
            Assert.That(result.Success,Is.True,result.Error);built=(GeneratedValleyContentHandle)result.Handle;
        }
        [UnityTest] public IEnumerator ActualCandidateHasOpenRoadsFordsAndNoFreeEntry() => CheckCandidate().ToCoroutine();
        private async UniTask CheckCandidate()
        {
            await Build(37);
            var transforms=root.GetComponentsInChildren<Transform>(true);
            Assert.That(transforms.Count(t=>t.name.StartsWith("Road_")),Is.GreaterThanOrEqualTo(5));
            Assert.That(transforms.Count(t=>t.name.StartsWith("ValleyBoundary_")),Is.EqualTo(4));
            Assert.That(transforms.Any(t=>t.name.StartsWith("DeepWaterCollision_")||t.name=="ReturnShortcutBarrier"),Is.False);
            var views=root.GetComponentsInChildren<CivilisationPointView>(true).Where(v=>!v.transform.IsChildOf(transforms.First(t=>t.name.Contains("GenerationKit")).transform)).ToArray();
            Assert.That(views.Any(v=>v.Kind==CivilisationPointKind.Preparation||v.Kind==CivilisationPointKind.Return||v.Kind==CivilisationPointKind.Shortcut),Is.False);
            var l=built.Layout;
            for(var i=0;i<l.Fords.Count;i++)
            {
                var ford=l.Fords[i];var a=l.BranchRoads[i].First().ToVector();var b=l.BranchRoads[i].Last().ToVector();
                a.y=l.HeightAt(a.x,a.z);b.y=l.HeightAt(b.x,b.z);
                var path=new NavMeshPath();
                Assert.That(NavMesh.CalculatePath(a,b,NavMesh.AllAreas,path),Is.True);Assert.That(path.status,Is.EqualTo(NavMeshPathStatus.PathComplete));
                Assert.That(NavMesh.CalculatePath(b,a,NavMesh.AllAreas,path),Is.True);Assert.That(path.status,Is.EqualTo(NavMeshPathStatus.PathComplete));
                Assert.That(ValleySwimmingRules.TrySample(l,ford.ToVector(),out _,out var depth),Is.True);Assert.That(depth,Is.LessThan(.7f));
            }
            var deep=l.River.First(p=>l.HeightAt(p.X,p.Z)<p.Y-2 && Mathf.Abs(p.Z)<420);
            Assert.That(ValleySwimmingRules.TrySample(l,deep.ToVector(),out _,out var d),Is.True);Assert.That(ValleySwimmingRules.ShouldSwim(d,false),Is.True);
            Assert.That(NavMesh.SamplePosition(new Vector3(deep.X,l.HeightAt(deep.X,deep.Z),deep.Z),out _,1,NavMesh.AllAreas),Is.False,"敌人导航不能进入深水");
        }
        [UnityTest] public IEnumerator RestoredCandidateKeepsEveryPointAndHeight() => CheckRestore().ToCoroutine();
        private async UniTask CheckRestore()
        {
            await Build(101);var json=Newtonsoft.Json.JsonConvert.SerializeObject(built.Layout);var saved=built.Layout.Clone();
            await built.ReleaseGeneratedAsync();built=null;
            var context=root.GetComponent<MapContext>();ValleyRandomSwimmingTests.Provider().TryGetValleyGenerationProfile(MapIds.CivilisationValley,out var p,out _);
            var result=await new ValleyMapBuilder(new Loader()).BuildAsync(new Handle(context),saved,p,CancellationToken.None);
            Assert.That(result.Success,Is.True,result.Error);built=(GeneratedValleyContentHandle)result.Handle;
            Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(built.Layout),Is.EqualTo(json));
        }
        private sealed class Handle:IMapContentHandle
        {public Handle(MapContext c){Context=c;}public string MapId=>MapIds.CivilisationValley;public MapContext Context{get;}public bool IsValid=>Context!=null;}
        private sealed class Loader:IResourceLoader
        {
            public T LoadAsset<T>(string path)where T:UnityEngine.Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/Prefabs/Environment/CivilisationValleyGenerationKit.prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> done)where T:UnityEngine.Object{done(LoadAsset<T>(path));return new ResourceLoadHandle(new object());}
            public void Release(ResourceLoadHandle h){}
        }
    }
}

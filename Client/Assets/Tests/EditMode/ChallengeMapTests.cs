using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System;
using System.Threading;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>用户采用的道路端点、六门分层与实际通行面契约。</summary>
    public sealed class ChallengeMapTests
    {
        /// <summary>资源仍挂起时退出必须立即完成取消，晚到回调不能复活旧地图。</summary>
        [Test]
        public void CancelPendingResourceThenLateCallbackDoesNotCreateWorld()
        {
            var loader=new PendingLoader();
            var world=new ChallengeWorldCtrl(loader,1);
            using(var cancel=new CancellationTokenSource())
            {
                var task=world.PrepareAsync("unused-player",cancel.Token);
                cancel.Cancel();
                Assert.Throws<OperationCanceledException>(()=>task.GetAwaiter().GetResult());
                world.Dispose();world.Dispose();
                Assert.That(loader.Releases,Is.EqualTo(1));
                loader.Callback(null);
                Assert.That(world.Ready,Is.False);
                Assert.Throws<InvalidOperationException>(()=>world.Activate());
            }
        }

        private sealed class PendingLoader : IResourceLoader
        {
            public Action<GameObject> Callback;
            public int Releases;
            public T LoadAsset<T>(string path) where T:Object => null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback) where T:Object
            {Callback=asset=>callback(asset as T);return new ResourceLoadHandle(new object());}
            public void Release(ResourceLoadHandle handle)=>Releases++;
        }
        [Test]
        public void IndependentWorldSupportsEveryRoadSegmentAndRejectsVoid()
        {
            var type=typeof(ChallengeModel).Assembly.GetType("Game.ChallengeWorldCtrl");
            Assert.That(type,Is.Not.Null,"独立地图控制器尚未接入");
            var map=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Challenge/Crater/CraterMap.prefab"));
            try
            {
                Physics.SyncTransforms();
                var method=type.GetMethod("TryFindSupport");
                var road=map.transform.Find("Road").GetComponent<MeshCollider>();
                var ground=map.transform.Find("Ground").GetComponent<MeshCollider>();
                var v=road.sharedMesh.vertices;
                for(int i=0;i<390;i++)
                {
                    var p=(v[i*2]+v[i*2+1]+v[i*2+2]+v[i*2+3])*.25f;
                    var args=new object[]{ground,road,p,0f};
                    Assert.That(method.Invoke(null,args),Is.True,"路段"+i);
                    Assert.That((float)args[3],Is.EqualTo(p.y).Within(.08f));
                }
                Assert.That(method.Invoke(null,new object[]{ground,road,new Vector3(100,12,100),0f}),Is.False);
                Assert.That(method.Invoke(null,new object[]{ground,road,new Vector3(0,12,0),0f}),Is.False,"高层不得瞬移落到一层");
            }
            finally{Object.DestroyImmediate(map);}
        }
        [Test]
        public void AdoptedMapHasContinuousWalkableRoadAndSixGates()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Art/Challenge/Crater/CraterMap.prefab");
            Assert.That(prefab, Is.Not.Null, "已采用地图必须制作正式资源");
            var road = prefab.transform.Find("Road").GetComponent<MeshCollider>();
            Assert.That(road.sharedMesh.vertexCount, Is.EqualTo(782));
            var vertices = road.sharedMesh.vertices;
            var end = (vertices[780] + vertices[781]) * .5f;
            Assert.That(end.x, Is.LessThan(-20));
            Assert.That(end.z, Is.LessThan(-20));
            Assert.That(end.y, Is.EqualTo(12).Within(.001));
            var third = (vertices[660] + vertices[661]) * .5f;
            Assert.That(third.x, Is.LessThan(-20));
            Assert.That(third.z, Is.GreaterThan(20));
            Assert.That(third.y, Is.EqualTo(12).Within(.001));
            var gates = prefab.transform.Find("SpawnGates");
            Assert.That(gates.childCount, Is.EqualTo(6));
            for (int i=0;i<6;i++)
            {
                Assert.That(gates.GetChild(i).Find("Entry"), Is.Not.Null);
                Assert.That(gates.GetChild(i).position.y, i<4 ? Is.EqualTo(0).Within(.001) : Is.GreaterThan(6));
            }
            Assert.That(prefab.transform.Find("Ground").GetComponent<MeshCollider>().sharedMesh, Is.Not.Null);
            Assert.That(prefab.transform.Find("SupplyPoint"), Is.Not.Null);
        }
    }
}

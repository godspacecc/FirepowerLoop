using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证正式无人机模型、当前强度和异步阶段替换，避免迟到机体重新出现。</summary>
    public sealed class ArcadeDronePresentationTests
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        private ArcadeCombatViewController view;
        private GameObject root;
        private Loader loader;
        private IResourceLoader previous;
        private ArcadePowerupDefinition definition;
        private IDictionary Entries=>(IDictionary)typeof(ArcadeCombatViewController).GetField("effectVisuals",Flags).GetValue(view);
        [SetUp]public void Setup()
        {
            root=new GameObject("drone-presentation-test");view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",Flags).SetValue(view,root);
            previous=ResourceLoader.Current;loader=new Loader();ResourceLoader.Use(loader);
            definition=new ArcadePowerupDefinition("arcade.support.drone",ArcadePowerupBehavior.Drone,"Prefabs/SceneObjects/ArcadeTimedDroneVisual",false,new Vector3(0,1.35f,0),0,0,0,0,0,0);
        }
        [TearDown]public void Cleanup()
        {try{view.End();loader.Complete();}finally{ResourceLoader.Use(previous);if(root!=null)Object.DestroyImmediate(root);}}
        private GameObject Instance(long owner=1)
        {var e=Entries[$"powerup:{owner}:13"];return e==null?null:(GameObject)e.GetType().GetField("Instance",Flags).GetValue(e);}
        [Test]public void DelayedLoadUsesLatestTierAndRestoresOriginalMaterial()
        {
            view.SetDrone(1,definition,Vector2.one,Vector2.right,0,2);
            view.SetDrone(1,definition,Vector2.one,Vector2.right,0,3);loader.Complete();
            var instance=Instance();Assert.That(instance,Is.Not.Null);
            var renderer=instance.GetComponentInChildren<Renderer>();var source=renderer.sharedMaterial;var original=source.GetColor("_BaseColor");
            var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor("_BaseColor"),Is.EqualTo(Color.Lerp(Color.white,new Color(.7f,.15f,1),.55f)));
            view.SetDrone(1,definition,Vector2.one,Vector2.right,0,1);renderer.GetPropertyBlock(block);
            Assert.That(block.isEmpty,Is.True);Assert.That(renderer.sharedMaterial,Is.SameAs(source));Assert.That(source.GetColor("_BaseColor"),Is.EqualTo(original));
        }
        [Test]public void LateBodyCannotReplaceExplosionAndExpiredCallbackCannotReviveIt()
        {
            view.SetDrone(1,definition,Vector2.one,Vector2.right,0,1);
            view.SetDrone(1,definition,Vector2.one,Vector2.right,2,3);loader.Complete();
            Assert.That(Instance().name,Does.Contain("arcade.support.drone"));
            Assert.That(Instance().GetComponentsInChildren<ParticleSystem>(true).Length,Is.GreaterThan(0));
            Assert.That(root.transform.childCount,Is.EqualTo(1));
            view.SetDrone(1,definition,Vector2.one,Vector2.right,0,1);
            view.SetDrone(1,definition,Vector2.one,Vector2.right,3,1);loader.Complete();
            Assert.That(Entries.Count,Is.Zero);Assert.That(root.transform.childCount,Is.Zero);
        }
        [Test]public void TwoOwnersKeepIndependentPositionAndColor()
        {
            view.SetDrone(1,definition,Vector2.one,Vector2.right,0,2);
            view.SetDrone(2,definition,Vector2.left,Vector2.up,0,3);loader.Complete();
            Assert.That(Instance(1),Is.Not.SameAs(Instance(2)));
            Assert.That(Instance(1).transform.localPosition,Is.EqualTo(new Vector3(1,1.35f,1)));
            Assert.That(Instance(2).transform.localPosition,Is.EqualTo(new Vector3(-1,1.35f,0)));
        }
        [Test]public void FormalDroneMatchesApprovedSizeAndCarriesNoColliders()
        {
            var instance=Object.Instantiate(loader.LoadAsset<GameObject>(definition.ActiveVisualPrefabAddress),root.transform);
            var rs=instance.GetComponentsInChildren<Renderer>(true);var bounds=rs[0].bounds;foreach(var r in rs)bounds.Encapsulate(r.bounds);
            Assert.That(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z),Is.EqualTo(.85f).Within(.002f));
            Assert.That(instance.GetComponentsInChildren<Collider>(true),Is.Empty);
        }
        private sealed class Loader:IResourceLoader
        {
            private readonly List<Action> callbacks=new List<Action>();
            public T LoadAsset<T>(string path)where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+path+".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string path,Action<T> callback)where T:Object
            {callbacks.Add(()=>callback(LoadAsset<T>(path)));return ResourceLoadHandle.Empty;}
            public void Complete(){var pending=callbacks.ToArray();callbacks.Clear();foreach(var action in pending)action();}
            public void Release(ResourceLoadHandle handle){}
        }
    }
}

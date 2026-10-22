using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>正式资源必须保持已确认动态和无地面HUD；视觉不裁决拾取。</summary>
    public sealed class ArcadeRewardPortalPresentationTests
    {
        private GameObject instance;
        [TearDown]public void Cleanup(){if(instance!=null)Object.DestroyImmediate(instance);}
        private GameObject Load(string name){var p=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/"+name+".prefab");Assert.That(p,Is.Not.Null,"缺少已确认的正式资源");instance=Object.Instantiate(p);return instance;}
        private static ArcadeRoundRewardTuning Tuning=>new ArcadeRoundRewardTuning(1.1f,.4f,1.8f,30,.75f,18,6,10,50,.75f,.32f,5);
        [Test]public void ChestFallsBurstsAndKeepsGroundRewardsAfterWholeEffect()
        {
            var go=Load("ArcadeRoundRewardVisual");var c=go.GetComponent("ArcadeRoundRewardVisual");Assert.That(c,Is.Not.Null);
            ArcadeRoundRewardState r=null;ArcadeRoundRewardRules.TryStart(ref r,Tuning,1,0,0);
            void Apply()=>c.GetType().GetMethod("Apply").Invoke(c,new object[]{r,Tuning});
            Apply();Assert.That(go.transform.Find("Chest").gameObject.activeSelf,Is.True);Assert.That(go.transform.Find("Chest").localPosition.y,Is.GreaterThan(4));
            ArcadeRoundRewardRules.Advance(r,Tuning,1.6f);Apply();Assert.That(go.transform.Find("Chest").gameObject.activeSelf,Is.False);Assert.That(go.transform.Find("Reward0").gameObject.activeSelf,Is.True);
            ArcadeRoundRewardRules.Advance(r,Tuning,1.8f);r.Collected.Add(0);Apply();Assert.That(go.transform.Find("Reward0").gameObject.activeSelf,Is.False);Assert.That(go.transform.Find("Reward1").gameObject.activeSelf,Is.True);
        }
        [Test]public void RewardVisualHasBothSizesWithoutGroundHudOrColliders()
        {var go=Load("ArcadeRoundRewardVisual");Assert.That(go.GetComponentsInChildren<Collider>(true),Is.Empty);Assert.That(go.GetComponentsInChildren<Canvas>(true),Is.Empty);Assert.That(go.transform.Find("Reward0"),Is.Not.Null);Assert.That(go.transform.Find("Reward23"),Is.Not.Null);}
        [Test]public void PortalUsesApprovedRingAndAppearingAnimation()
        {
            var go=Load("ArcadeStagePortalVisual");Assert.That(go.GetComponentsInChildren<Collider>(true),Is.Empty);Assert.That(go.GetComponentsInChildren<Canvas>(true),Is.Empty);
            var ring=go.transform.Find("Portal/PortalOuter").GetComponent<LineRenderer>();Assert.That(ring.GetPosition(0).x,Is.EqualTo(1.3f).Within(.001f));
            var c=go.GetComponent("ArcadeStagePortalVisual");Assert.That(c,Is.Not.Null);var method=c.GetType().GetMethod("Apply");method.Invoke(c,new object[]{0f,.75f});Assert.That(go.transform.Find("Portal").gameObject.activeSelf,Is.False);
            method.Invoke(c,new object[]{.75f,.75f});Assert.That(go.transform.Find("Portal").localScale,Is.EqualTo(Vector3.one));
        }
        [Test]public void DelayedChestUsesLatestCollectedState()
        {
            var previous=ResourceLoader.Current;var loader=new Loader();var root=new GameObject("reward-async-test");var view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,root);ResourceLoader.Use(loader);
            try
            {
                ArcadeRoundRewardState state=null;ArcadeRoundRewardRules.TryStart(ref state,Tuning,1,0,0);view.SetRoundReward(state,Tuning);
                ArcadeRoundRewardRules.Advance(state,Tuning,3.4f);state.Collected.Add(0);view.SetRoundReward(state,Tuning);loader.Complete();
                Assert.That(root.transform.childCount,Is.EqualTo(1));Assert.That(root.transform.GetChild(0).Find("Reward0").gameObject.activeSelf,Is.False);Assert.That(root.transform.GetChild(0).Find("Chest").gameObject.activeSelf,Is.False);
            }
            finally{view.End();loader.Complete();ResourceLoader.Use(previous);Object.DestroyImmediate(root);}
        }
        [Test]public void EndedMapRejectsLateRewardAndPortalResources()
        {
            var previous=ResourceLoader.Current;var loader=new Loader();var root=new GameObject("reward-late-test");var view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,root);ResourceLoader.Use(loader);
            try
            {ArcadeRoundRewardState state=null;ArcadeRoundRewardRules.TryStart(ref state,Tuning,1,0,0);view.SetRoundReward(state,Tuning);view.SetStagePortal(true,Vector2.one,.5f,.75f);view.End();loader.Complete();Assert.That(root==null||root.transform.childCount==0,Is.True);}
            finally{view.End();ResourceLoader.Use(previous);if(root!=null)Object.DestroyImmediate(root);}
        }
        private sealed class Loader:IResourceLoader
        {
            private readonly List<Action> callbacks=new List<Action>();
            public T LoadAsset<T>(string address)where T:Object=>AssetDatabase.LoadAssetAtPath<T>("Assets/Game/"+address+".prefab");
            public ResourceLoadHandle LoadAssetAsync<T>(string address,Action<T> callback)where T:Object{callbacks.Add(()=>callback(LoadAsset<T>(address)));return ResourceLoadHandle.Empty;}
            public void Release(ResourceLoadHandle handle){}
            public void Complete(){var pending=callbacks.ToArray();callbacks.Clear();foreach(var callback in pending)callback();}
        }
    }
}

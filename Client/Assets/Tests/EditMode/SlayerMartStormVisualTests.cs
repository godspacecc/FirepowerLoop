using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Game.Tests
{
    /// <summary>风暴独立几何表现及双端投影，不承担伤害或控制判定。</summary>
    public sealed class SlayerMartStormVisualTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private ArcadeCombatViewController view;private GameObject root;
        [SetUp]public void Setup(){view=new ArcadeCombatViewController();root=new GameObject("StormVisualFixture");typeof(ArcadeCombatViewController).GetField("root",Private).SetValue(view,root);}
        [TearDown]public void Cleanup(){Release();if(root!=null)Object.DestroyImmediate(root);}
        private void Release()=>typeof(ArcadeCombatViewController).GetMethod("ReleaseAllEffectVisuals",Private).Invoke(view,null);
        private void Set(long owner=1,float radius=5,Vector2 position=default){var method=typeof(ArcadeCombatViewController).GetMethod("SetSlayerMartStorm");Assert.That(method,Is.Not.Null,"缺少独立风暴视觉端口");method.Invoke(view,new object[]{owner,position,radius});}
        private LineRenderer Ring(long owner=1){var node=root.transform.Find("SlayerMartStorm_"+owner);Assert.That(node,Is.Not.Null);return node.GetComponentsInChildren<LineRenderer>().Single(l=>l.name=="Range");}
        [Test]public void DedicatedStormCreatesVisibleRingAndLightning(){view.BeginEffectSync();Set();view.EndEffectSync();Assert.That(root.GetComponentsInChildren<LineRenderer>().Length,Is.GreaterThanOrEqualTo(2));Assert.That(Ring().sharedMaterial.shader.isSupported,Is.True);}
        [Test]public void RingUsesActualRadius(){Set(radius:5);var ring=Ring();Assert.That(ring.loop,Is.True);for(int i=0;i<ring.positionCount;i++){var p=ring.GetPosition(i);Assert.That(new Vector2(p.x,p.z).magnitude,Is.EqualTo(5).Within(.001));}}
        [Test]public void RepeatedFramesFollowOwnerWithoutRecreating(){view.BeginEffectSync();Set(position:new Vector2(2,3));view.EndEffectSync();var ring=Ring();view.BeginEffectSync();Set(position:new Vector2(4,6));view.EndEffectSync();Assert.That(Ring(),Is.SameAs(ring));Assert.That(ring.transform.parent.localPosition.x,Is.EqualTo(4));Assert.That(ring.transform.parent.localPosition.z,Is.EqualTo(6));}
        [Test]public void TwoPlayersKeepIndependentStorms(){view.BeginEffectSync();Set(1);Set(2);view.EndEffectSync();Assert.That(Ring(1),Is.Not.SameAs(Ring(2)));}
        [Test]public void MissingNextFrameReleasesObjectAndMaterial(){view.BeginEffectSync();Set();view.EndEffectSync();var ring=Ring();var material=ring.sharedMaterial;view.BeginEffectSync();view.EndEffectSync();Assert.That(ring==null,Is.True);Assert.That(material==null,Is.True);}
        [Test]public void MapReleaseDestroysAllStormMaterials(){Set(1);Set(2);var materials=root.GetComponentsInChildren<LineRenderer>().Select(l=>l.sharedMaterial).Distinct().ToArray();Release();Assert.That(root.GetComponentsInChildren<LineRenderer>(),Is.Empty);Assert.That(materials.All(m=>m==null),Is.True);}
        [Test]public void DestroyedMapRootStillReleasesMaterial(){Set();var material=Ring().sharedMaterial;Object.DestroyImmediate(root);try{Assert.DoesNotThrow(Release);Assert.That(material==null,Is.True);}finally{if(material!=null)Object.DestroyImmediate(material);}}
        [Test]public void MapReleaseCanCreateFreshStorm(){Set();var material=Ring().sharedMaterial;Release();Set();Assert.That(Ring().sharedMaterial,Is.Not.SameAs(material));Assert.That(material==null,Is.True);}
        [Test]public void InvalidRadiusCreatesNothing(){Set(radius:0);Set(radius:float.NaN);Assert.That(root.transform.childCount,Is.Zero);}
        [Test]public void VisualHasNoCombatColliders(){Set();Assert.That(root.GetComponentsInChildren<Collider>(),Is.Empty);}
        [Test]public void SingleProjectionUsesFormalRadiusAndPosition(){var h=new SlayerMartAbilityFlowTests();h.Setup();h.player.Owned.Add("slayermart.item.16");h.player.StormRemaining=5;h.runtime.PlayerPosition=new Vector2(3,4);typeof(ArcadeCombatSystem).GetField("view",Private).SetValue(h.system,view);var method=typeof(ArcadeCombatSystem).GetMethod("SyncSlayerMartStormVisual",Private);Assert.That(method,Is.Not.Null);view.BeginEffectSync();method.Invoke(h.system,new object[]{h.runtime});view.EndEffectSync();Assert.That(Ring().GetPosition(0).magnitude,Is.EqualTo(5).Within(.01));Assert.That(Ring().transform.parent.localPosition.x,Is.EqualTo(3));}
        [Test]public void OnlineProjectionUsesActualDisplayedPosition(){var h=new SlayerMartUiFlowTests();h.Setup();try{h.Configure(r=>{r.Battle.SlayerMartActive=false;var p=ET.OnlineArcadeBattlePlayerInfo.Create();p.PlayerId=10;p.Alive=true;p.HasStorm=true;p.StormSeconds=4;r.Battle.Players.Add(p);});h.Network.PushConfigured();var system=new OnlineArcadePresentationSystem();((ICanSetArchitecture)system).SetArchitecture(h.Architecture);h.Architecture.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog,out _);typeof(OnlineArcadePresentationSystem).GetField("catalog",Private).SetValue(system,catalog);typeof(OnlineArcadePresentationSystem).GetField("view",Private).SetValue(system,view);view.BeginEffectSync();typeof(OnlineArcadePresentationSystem).GetMethod("SyncPlayerEffects",Private).Invoke(system,new object[]{h.Online.Snapshot.Battle.Players.Single(),new Vector2(7,8)});view.EndEffectSync();Assert.That(Ring(10).transform.parent.localPosition.x,Is.EqualTo(7));Assert.That(Ring(10).transform.parent.localPosition.z,Is.EqualTo(8));}finally{h.Cleanup();}}
    }
}

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
namespace Game.Tests
{
 /// <summary>附魔表现只消费快照，覆盖所有模型与独立波纹寿命。</summary>
 public sealed class SurvivorEnchantPresentationTests
 {
  [Test] public void EveryReceiverRendersCoexistingSnapshotAndClearsOnReuse()
  {
   var method=typeof(EnemyView).GetMethod("SetSurvivorEnchantment");Assert.That(method,Is.Not.Null);
   foreach(var n in new[]{"Shambler","Caster","Shield","EnchantHealth","EnchantSpeed","EnchantShield"})
   {
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie"+n+".prefab");Assert.That(prefab,Is.Not.Null,n);
    var o=Object.Instantiate(prefab);try{var v=o.GetComponent<EnemyView>();v.BeginPresentationUse();method.Invoke(v,new object[]{new SurvivorEnchantSnapshot(2,3,20,80,2)});
    var rings=o.GetComponentsInChildren<LineRenderer>(true).Where(x=>x.name.StartsWith("EnchantReceived")).ToArray();Assert.That(rings.Length,Is.EqualTo(3),n);Assert.That(rings.All(x=>x.enabled),Is.True);
    v.SetSurvivorShield(false);method.Invoke(v,new object[]{new SurvivorEnchantSnapshot(2,3,0,80,2)});Assert.That(rings.Count(x=>x.enabled),Is.EqualTo(2));
    Assert.That(o.GetComponentsInChildren<Renderer>(true).Where(x=>x.name=="ShieldSteel"||x.name=="ShieldCross").All(x=>!x.enabled),Is.True);
    v.ResetPhysicalFeedback();Assert.That(rings.All(x=>!x.enabled),Is.True);
    }finally{Object.DestroyImmediate(o);}
   }
  }
  [Test] public void PulsePoolIsBoundedFreezesAndClearsIndependently()
  {
   var flags=BindingFlags.Instance|BindingFlags.NonPublic;var ctrl=new EnemyCtrl(null);var root=new GameObject("EnchantTestMap");
   try{
    typeof(EnemyCtrl).GetField("arcadeRoot",flags).SetValue(ctrl,root.transform);
    var cache=(System.Collections.IDictionary)typeof(EnemyCtrl).GetField("committedPrefabs",flags).GetValue(ctrl);
    var entryType=typeof(EnemyCtrl).GetNestedType("PrefabCacheEntry",BindingFlags.NonPublic);
    cache.Add("test",System.Activator.CreateInstance(entryType,new object[]{AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieEnchantHealth.prefab"),ResourceLoadHandle.Empty}));
    for(int i=0;i<50;i++)ctrl.PlaySurvivorEnchantPulse(i,"EnchantHealth",Vector3.zero,6);
    var rings=root.GetComponentsInChildren<LineRenderer>();Assert.That(rings.Length,Is.EqualTo(24));
    var tick=typeof(EnemyCtrl).GetMethod("TickEnchantPulses",flags);tick.Invoke(ctrl,new object[]{1f,true});Assert.That(rings.All(x=>x.transform.localScale.x==.1f),Is.True);
    tick.Invoke(ctrl,new object[]{.65f,false});Assert.That(rings.All(x=>x.enabled&&x.transform.localScale.x>2),Is.True);
    tick.Invoke(ctrl,new object[]{.7f,false});Assert.That(rings.All(x=>!x.enabled),Is.True);
    ctrl.PlaySurvivorEnchantPulse(1,"EnchantShield",Vector3.zero,6);ctrl.ClearArcadeEnemies();Assert.That(root.GetComponentsInChildren<LineRenderer>().Length,Is.Zero);
   }finally{ctrl.ReleaseAll();Object.DestroyImmediate(root);}
  }
  [Test] public void ThreeFormalAddressesHaveUniqueArcadeCollectionAndRingDependencies()
  {
   var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");
   foreach(bool dry in new[]{true,false}){
    var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",dry,false});
    var all=((System.Collections.IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result)).Cast<object>().ToArray();
    foreach(var kind in new[]{"Health","Speed","Shield"}){
     var address="Prefabs/Characters/ArcadeZombieEnchant"+kind;
     var rows=all.Where(x=>(string)x.GetType().GetProperty("Address").GetValue(x)==address).ToArray();Assert.That(rows.Length,Is.EqualTo(1),address);
     var tags=((System.Collections.IEnumerable)rows[0].GetType().GetProperty("AssetTags").GetValue(rows[0])).Cast<string>();Assert.That(tags,Does.Contain("arcade"));
     if(!dry){var deps=((System.Collections.IEnumerable)rows[0].GetType().GetField("DependAssets").GetValue(rows[0])).Cast<object>().Select(x=>(string)x.GetType().GetField("AssetPath").GetValue(x)).ToArray();Assert.That(deps.Any(x=>x.EndsWith("EnchantRing.prefab")),Is.True);Assert.That(deps.Any(x=>x.EndsWith("EnchantRing.mat")),Is.True);}
    }
   }
  }
  [Test] public void FormalControllerOverridesBothProjectionPorts()
  {
   Assert.That(typeof(ArcadeCombatViewController).GetMethod("SetSurvivorEnemyEnchantment"),Is.Not.Null);
   Assert.That(typeof(ArcadeCombatViewController).GetMethod("PlaySurvivorEnchantPulse"),Is.Not.Null);
  }
 }
}

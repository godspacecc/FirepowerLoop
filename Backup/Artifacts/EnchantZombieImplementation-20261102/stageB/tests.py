from pathlib import Path
p=Path('Client/Assets/Tests/EditMode/Flow/SurvivorEnchantPresentationTests.cs');s=p.read_text(encoding="utf8");pos=s.index('  [Test] public void FormalController')
s=s[:pos]+'''  [Test] public void PulsePoolIsBoundedFreezesAndClearsIndependently()
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
'''+s[pos:];p.write_text(s,encoding='utf8')


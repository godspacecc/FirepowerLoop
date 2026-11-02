using System;using System.Collections;using System.Linq;using System.Reflection;using NUnit.Framework;using UnityEditor;using UnityEngine;
namespace Game.Tests {
/// <summary>特殊敌人正式资源、完整表现快照与真实收集回归，不使用替身宣称运行加载。</summary>
public sealed class SurvivorSpecialVisualTests {
const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
[TestCase("Exploder")][TestCase("Caster")][TestCase("Shield")]
public void FormalPrefabKeepsBodyAndClearsWarning(string kind){
 var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShambler.prefab");
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombie"+kind+".prefab");Assert.That(prefab,Is.Not.Null);
 Assert.That(prefab.transform.localScale,Is.EqualTo(original.transform.localScale));Assert.That(prefab.GetComponent<EnemyView>().Animator.runtimeAnimatorController,Is.EqualTo(original.GetComponent<EnemyView>().Animator.runtimeAnimatorController));
 var o=UnityEngine.Object.Instantiate(prefab);try{var view=o.GetComponent<EnemyView>();view.SetNavigationEnabled(false);view.BeginPresentationUse();view.SetSurvivorSpecial(kind,Vector3.right,true,.5f,1.5f);Assert.That(Vector3.Dot(o.transform.forward,Vector3.right),Is.GreaterThan(.999f));var line=(LineRenderer)typeof(EnemyView).GetField("survivorWarning",Flags).GetValue(view);Assert.That(line.enabled,Is.EqualTo(kind=="Exploder"));view.ResetPhysicalFeedback();Assert.That(line.enabled,Is.False);
 var equipment=o.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="Dynamite"||r.name=="CasterOrb"||r.name=="ShieldSteel").ToArray();Assert.That(equipment,Is.Not.Empty);foreach(var r in equipment)Assert.That(r.transform.parent.name,Is.EqualTo(kind=="Exploder"?"Torso":kind=="Caster"?"Middle1.R":"Middle1.L"));
 }finally{UnityEngine.Object.DestroyImmediate(o);}}
/// <summary>正式盾装备隐藏及回池恢复，身体始终可见。</summary>
[TestCase(false)] [TestCase(true)] public void BrokenShieldHidesOnlyEquipmentAndReuseRestoresIt(bool physicalDeath){
 var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/ArcadeZombieShield.prefab");
 var o=UnityEngine.Object.Instantiate(prefab);try{
 var view=o.GetComponent<EnemyView>();view.SetNavigationEnabled(false);view.BeginPresentationUse();
 var equipment=o.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="ShieldSteel"||r.name=="ShieldCross").ToArray();Assert.That(equipment.Length,Is.EqualTo(3));
 var body=o.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();Assert.That(body,Is.Not.Empty);
 var method=typeof(EnemyView).GetMethod("SetSurvivorShield");Assert.That(method,Is.Not.Null);method.Invoke(view,new object[]{false});
 foreach(var r in equipment)Assert.That(r.enabled,Is.False);foreach(var r in body)Assert.That(r.enabled&&r.gameObject.activeInHierarchy,Is.True);
 method.Invoke(view,new object[]{false});foreach(var r in equipment)Assert.That(r.enabled,Is.False);
 bool dying=physicalDeath?view.TryPlayPhysicalDeathWithImpact(Vector3.back,false,0,out _):view.TryPlayDeathAnimation(out _);Assert.That(dying,Is.True,"正式Prefab必须实际进入死亡分支");
 foreach(var r in equipment)Assert.That(r.enabled,Is.False,"死亡不能复原已破盾装备");
 view.ResetPhysicalFeedback();foreach(var r in equipment)Assert.That(r.enabled,Is.True,"回池恢复新盾");
 view.SetSurvivorShield(false);view.BeginPresentationUse();foreach(var r in equipment)Assert.That(r.enabled&&r.gameObject.activeInHierarchy,Is.True,"新用途恢复新盾");
 }finally{UnityEngine.Object.DestroyImmediate(o);}}
[Test]public void ExactAddressesAndSerializedDependenciesAreCollected(){
 var setting=AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/AssetBundleCollectorSetting.asset");var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",true,false});
 Func<object,string,object> read=(o,n)=>o.GetType().GetProperty(n).GetValue(o);var all=((IEnumerable)read(result,"CollectAssets")).Cast<object>().ToArray();var report=new System.Collections.Generic.List<string>();
 foreach(var name in new[]{"ArcadeZombieExploder","ArcadeZombieCaster","ArcadeZombieShield","GrayboxBullet"}){var address="Prefabs/"+(name=="GrayboxBullet"?"Combat/":"Characters/")+name;var matches=all.Where(o=>(string)read(o,"Address")==address).ToArray();Assert.That(matches.Length,Is.EqualTo(1));var a=matches[0];Assert.That(((IEnumerable)read(a,"AssetTags")).Cast<string>(),Does.Contain("arcade"));var info=read(a,"AssetInfo");var path=(string)info.GetType().GetField("AssetPath").GetValue(info);report.Add(address+" | "+path+" | "+read(a,"BundleName")+" | arcade");}
 System.IO.File.WriteAllLines("../Backup/Artifacts/ShieldZombieBreak-20261101/implementation/collection.txt",report);
 var full=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",false,false});var collected=((IEnumerable)read(full,"CollectAssets")).Cast<object>().ToArray();
 foreach(var a in collected.Where(a=>new[]{"Prefabs/Characters/ArcadeZombieExploder","Prefabs/Characters/ArcadeZombieCaster","Prefabs/Characters/ArcadeZombieShield","Prefabs/Combat/GrayboxBullet"}.Contains((string)read(a,"Address")))){var deps=((IEnumerable)a.GetType().GetField("DependAssets").GetValue(a)).Cast<object>().Select(d=>(string)d.GetType().GetField("AssetPath").GetValue(d)).ToArray();if((string)read(a,"Address")=="Prefabs/Combat/GrayboxBullet"){Assert.That(deps.Any(p=>p.EndsWith("Fireball.prefab")),Is.True);Assert.That(deps.Any(p=>p.EndsWith("ArcadeDroneExplosion.prefab")),Is.True);}else Assert.That(deps.Any(p=>p.Contains("SpecialZombies/")&&p.EndsWith(".mat")),Is.True);System.IO.File.AppendAllLines("../Backup/Artifacts/ShieldZombieBreak-20261101/implementation/collection.txt",new[]{(string)read(a,"Address")}.Concat(deps));}}
}}

using System.Collections;using System.Linq;using NUnit.Framework;using UnityEditor;using UnityEngine;
namespace Game.Tests {
/// <summary>发电机正式加载地址与材质依赖，不使用替身加载结果冒充资源包验证。</summary>
public sealed class MissionGeneratorTests {
[Test]public void GeneratorIsCollectedWithMaterialsAndWithoutPickupCollider(){
var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",false,false});var assets=((IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result)).Cast<object>();var matches=assets.Where(x=>(string)x.GetType().GetProperty("Address").GetValue(x)=="Prefabs/SceneObjects/MissionGeneratorVisual").ToArray();Assert.That(matches.Length,Is.EqualTo(1));var a=matches[0];Assert.That(((IEnumerable)a.GetType().GetProperty("AssetTags").GetValue(a)).Cast<string>(),Does.Contain("arcade"));var deps=((IEnumerable)a.GetType().GetField("DependAssets").GetValue(a)).Cast<object>().Select(x=>(string)x.GetType().GetField("AssetPath").GetValue(x)).ToArray();foreach(var name in new[]{"Green","Dark","Steel","Amber"})Assert.That(deps,Does.Contain("Assets/Game/Art/Effects/MissionGenerator"+name+".mat"));var p=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/MissionGeneratorVisual.prefab");Assert.That(p.GetComponentsInChildren<Collider>(),Is.Empty);Assert.That(p.GetComponentsInChildren<Renderer>().Length,Is.GreaterThan(10));
}}
}

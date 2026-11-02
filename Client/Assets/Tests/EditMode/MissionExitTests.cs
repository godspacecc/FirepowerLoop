using System.Collections;using System.Linq;using NUnit.Framework;using UnityEditor;using UnityEngine;
namespace Game.Tests {
/// <summary>正式出口的精确寻址、标签与静态地面形状。</summary>
public sealed class MissionExitTests {
[Test] public void ExitIsCollectedAndMatchesApprovedGroundRing(){
var setting=AssetDatabase.LoadAssetAtPath<Object>("Assets/AssetBundleCollectorSetting.asset");var result=setting.GetType().GetMethod("BeginCollect").Invoke(setting,new object[]{"DefaultPackage",true,false});
var assets=((IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result)).Cast<object>();
var matches=assets.Where(a=>(string)a.GetType().GetProperty("Address").GetValue(a)=="Prefabs/SceneObjects/MissionExitVisual").ToArray();Assert.That(matches.Length,Is.EqualTo(1));
var tags=((IEnumerable)matches[0].GetType().GetProperty("AssetTags").GetValue(matches[0])).Cast<string>();Assert.That(tags,Does.Contain("arcade"));
var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/MissionExitVisual.prefab");var ring=prefab.GetComponent<LineRenderer>();Assert.That(ring,Is.Not.Null);Assert.That(ring.loop,Is.True);Assert.That(ring.positionCount,Is.EqualTo(64));Assert.That(prefab.GetComponentsInChildren<Collider>(),Is.Empty);
for(int i=0;i<ring.positionCount;i++){Assert.That(ring.GetPosition(i).y,Is.EqualTo(0).Within(.001f));Assert.That(ring.GetPosition(i).magnitude,Is.EqualTo(1.8f).Within(.001f));}
Assert.That(ring.sharedMaterial,Is.Not.Null);
}}
}

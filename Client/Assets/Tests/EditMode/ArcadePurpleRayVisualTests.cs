using System;
using System.Linq;
using System.Reflection;
using FirepowerLoop.Arcade;
using NUnit.Framework;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>电弧是只读命中结果；旧主题也显示，无危险圈、无自主时钟。</summary>
    public sealed class ArcadePurpleRayVisualTests
    {
        [Test] public void ArcUsesAuthoritativeEndpointsAndFollowsMapWithoutRoundLayout()
        {
            var root=new GameObject("ray-map");var view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,root);
            var snapshots=new[]{new ArcadeContentAreaSnapshot(1,30,1,2,.07f,false,4,6,true)};
            try
            {
                view.SetRoundContent(1,snapshots);var line=root.GetComponentInChildren<LineRenderer>();Assert.That(line,Is.Not.Null);
                Assert.That(line.useWorldSpace,Is.False);Assert.That(line.sharedMaterial,Is.Not.Null);Assert.That(line.positionCount,Is.GreaterThan(2));
                Assert.That(line.GetPosition(0),Is.EqualTo(new Vector3(1,.9f,2)));Assert.That(line.GetPosition(line.positionCount-1),Is.EqualTo(new Vector3(4,.9f,6)));
                var points=Enumerable.Range(0,line.positionCount).Select(line.GetPosition).ToArray();root.transform.position=new Vector3(100,0,-30);view.SetRoundContent(1,snapshots);
                Assert.That(Enumerable.Range(0,line.positionCount).Select(line.GetPosition),Is.EqualTo(points));
                Assert.That(line.transform.TransformPoint(line.GetPosition(0)),Is.EqualTo(new Vector3(101,.9f,-28)));Assert.That(root.GetComponentsInChildren<Collider>(),Is.Empty);
                Assert.That(root.GetComponentsInChildren<MeshFilter>(),Is.Empty,"电弧不能附带危险边界或地面圈");
            }
            finally{typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,null);UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void MissingOrInvalidArcAndMapReleaseRemoveOldVisualImmediately()
        {
            var root=new GameObject("ray-map");var view=new ArcadeCombatViewController();
            typeof(ArcadeCombatViewController).GetField("root",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,root);
            try
            {
                var good=new[]{new ArcadeContentAreaSnapshot(1,30,0,0,.07f,false,2,0,true)};view.SetRoundContent(0,good);Assert.That(root.GetComponentsInChildren<LineRenderer>().Length,Is.EqualTo(1));
                view.SetRoundContent(0,new[]{new ArcadeContentAreaSnapshot(1,30,float.NaN,0,.07f,false,2,0,true)});Assert.That(root.GetComponentsInChildren<LineRenderer>(),Is.Empty);
                view.SetRoundContent(0,good);view.SetRoundContent(0,Array.Empty<ArcadeContentAreaSnapshot>());Assert.That(root.GetComponentsInChildren<LineRenderer>(),Is.Empty);
                view.SetRoundContent(0,good);typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,null);Assert.That(root.GetComponentsInChildren<LineRenderer>(),Is.Empty);
            }
            finally{typeof(ArcadeCombatViewController).GetMethod("ReleaseRoundContent",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,null);UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}

using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>曳光线必须随子弹移动，复用时清除旧轨迹，且不能修改碰撞。</summary>
    public sealed class ArcadeFlyingTracerTests
    {
        [Test] public void FlyingTracerMovesAndResetsWithoutChangingCollision()
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxBullet.prefab"));
            try {
                var view=go.GetComponent<ProjectileView>();var scale=go.transform.localScale;var box=go.GetComponent<BoxCollider>();var size=box.size;
                view.Bind(1,null);view.SetArcadePresentation("ArcadeBulletBasic",false,.7f);
                var line=go.GetComponentsInChildren<LineRenderer>(true).FirstOrDefault(x=>x.name=="FlyingTracer");
                Assert.That(line,Is.Not.Null);Assert.That(line.enabled,Is.True);Assert.That(line.sharedMaterial,Is.Not.Null);
                view.ApplyPose(new Vector3(0,1,0),Vector3.forward);
                Assert.That(Vector3.Distance(line.GetPosition(0),line.GetPosition(1)),Is.LessThan(.001f),"出生时不能伸到枪后");
                view.ApplyPose(new Vector3(0,1,2),Vector3.forward);
                Assert.That(line.GetPosition(0),Is.EqualTo(new Vector3(0,1,2)));
                Assert.That(line.GetPosition(1).z,Is.GreaterThan(0).And.LessThan(2));
                Assert.That(go.transform.localScale,Is.EqualTo(scale));Assert.That(box.size,Is.EqualTo(size));
                view.SetArcadePresentation("ArcadeBulletBasic",true,.7f);Assert.That(line.enabled,Is.False);
                view.Bind(2,null);view.SetArcadePresentation("ArcadeBulletBasic",false,.55f);view.ApplyPose(new Vector3(10,1,0),Vector3.forward);
                Assert.That(Vector3.Distance(line.GetPosition(0),line.GetPosition(1)),Is.LessThan(.001f),"回池后不连接上一次位置");
                view.SetArcadePresentation("ArcadeBulletRapid",false,.7f);Assert.That(line.enabled,Is.False);
            }finally{Object.DestroyImmediate(go);}
        }
    }
}

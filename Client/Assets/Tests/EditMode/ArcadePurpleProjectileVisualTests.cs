using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace Game.Tests
{
    /// <summary>正式弹体紫色短尾只读姿态，不写战斗结论，复用不能串到红/普通/敌弹。</summary>
    public sealed class ArcadePurpleProjectileVisualTests
    {
        [Test] public void PurpleRocketTailUsesWorldScaleAndRestoresOnReuse()=>Check("ArcadePanzerschreckRocket",.8f);
        [Test] public void PurpleRayTailUsesWorldScaleAndRestoresOnReuse()=>Check("ArcadeRaygunBolt",.45f);
        private static void Check(string visual,float length)
        {
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Combat/GrayboxArrow.prefab"));
            try
            {
                var view=go.GetComponent<ProjectileView>();view.SetArcadePresentation(visual+"#Purple",false);view.ApplyPose(new Vector3(2,1,3),Vector3.forward);
                var tail=go.GetComponentsInChildren<LineRenderer>(true).FirstOrDefault(x=>x.name=="PurpleWeaponTail");Assert.That(tail,Is.Not.Null);
                Assert.That(tail.enabled,Is.True);Assert.That(tail.useWorldSpace,Is.True);Assert.That(tail.sharedMaterial,Is.Not.Null);
                Assert.That(Vector3.Distance(tail.GetPosition(0),tail.GetPosition(1)),Is.EqualTo(length).Within(.001));
                foreach(var value in new[]{visual+"#Red",visual,"ArcadeGrenade#Purple"}){view.SetArcadePresentation(value,false);Assert.That(tail.enabled,Is.False);}
                view.SetArcadePresentation(visual+"#Purple",true);Assert.That(tail.enabled,Is.False);
            }
            finally{Object.DestroyImmediate(go);}
        }
    }
}

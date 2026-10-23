using System.Linq;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>正式掉落模板的下落、自动开盖、重复快照和回池恢复，不驱动任何拾取裁决。</summary>
    public sealed class ArcadeWeaponDeliveryTests
    {
        private GameObject instance;
        private ResourcePickupView pickup;
        private ArcadeWeaponDeliveryView crate;
        [SetUp] public void Setup()
        {
            instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab"));
            instance.transform.position=new Vector3(0,.3f,0);pickup=instance.GetComponent<ResourcePickupView>();
            pickup.SetArcadePresentation(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/SceneObjects/ArcadeFlamethrower.prefab"),"Prefabs/SceneObjects/ArcadeFlamethrower");
            crate=instance.GetComponentInChildren<ArcadeWeaponDeliveryView>(true);Assert.That(crate,Is.Not.Null);
        }
        [TearDown] public void Cleanup()=>Object.DestroyImmediate(instance);
        [Test] public void FallingBoxHidesWeaponAndLandsAtGround()
        {
            pickup.SetWeaponDelivery(true,1.2f,1.2f);Assert.That(crate.transform.position.y,Is.GreaterThan(7));
            Assert.That(instance.transform.Find("ArcadeDropVisual").gameObject.activeSelf,Is.False);
            pickup.SetWeaponDelivery(true,.3f,1.2f);Assert.That(crate.transform.position.y,Is.EqualTo(0).Within(.001f));
        }
        [Test] public void LidOpensUpwardAndRevealsWeaponAutomatically()
        {
            pickup.SetWeaponDelivery(true,0,1.2f);var lid=crate.transform.Find("LidHinge");
            Assert.That(lid.TransformPoint(new Vector3(0,0,-.5f)).y,Is.GreaterThan(lid.position.y));
            Assert.That(instance.transform.Find("ArcadeDropVisual").gameObject.activeSelf,Is.True);
            var renderers=instance.transform.Find("ArcadeDropVisual").GetComponentsInChildren<MeshRenderer>();
            var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
            Assert.That(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z),Is.EqualTo(1.7f).Within(.01f),"开箱后空投物接近玩家体量；击杀掉落不放大");
            Assert.That(crate.GetComponentsInChildren<Collider>(true),Is.Empty);
        }
        [Test] public void OrdinaryReuseClearsCrateAndRestoresWeaponPose()
        {
            var visual=instance.transform.Find("ArcadeDropVisual");var original=visual.localPosition;var originalScale=visual.localScale;
            pickup.SetWeaponDelivery(true,0,1.2f);pickup.SetWeaponDelivery(false,0,1.2f);
            Assert.That(crate.gameObject.activeSelf,Is.False);Assert.That(visual.gameObject.activeSelf,Is.True);Assert.That(visual.localPosition,Is.EqualTo(original));
            Assert.That(visual.localScale,Is.EqualTo(originalScale));
            pickup.SetWeaponDelivery(true,1.2f,1.2f);Assert.That(crate.transform.position.y,Is.GreaterThan(7));
        }
        [Test] public void LandingDustDoesNotReplayOnDuplicateOrLateSnapshot()
        {
            var dust=crate.GetComponentInChildren<ParticleSystem>(true);
            pickup.SetWeaponDelivery(true,1.2f,1.2f);pickup.SetWeaponDelivery(true,.3f,1.2f);
            int count=dust.particleCount;Assert.That(count,Is.GreaterThan(0));pickup.SetWeaponDelivery(true,.3f,1.2f);Assert.That(dust.particleCount,Is.EqualTo(count));
            pickup.ClearArcadePresentation();pickup.SetWeaponDelivery(true,0,1.2f);Assert.That(dust.particleCount,Is.Zero);
        }
        [Test] public void PoolResetRemovesDeliveryAuthorityFlags()
        {
            var drop=new ArcadeDropRuntime{WeaponAirdrop=true,DeliveryRemainingSeconds=1.2f};drop.Reset();
            Assert.That(drop.WeaponAirdrop,Is.False);Assert.That(drop.DeliveryRemainingSeconds,Is.Zero);Assert.That(drop.RemainingSeconds,Is.EqualTo(30));
        }

        /// <summary>空投枪身中心固定在拾取轴上，重复快照/整周旋转不偏移；普通回池姿态仍恢复。</summary>
        [Test] public void FlamethrowerAirdropRotatesAroundItsCenterAndRestoresOrdinaryPose()
        {
            var visual=instance.transform.Find("ArcadeDropVisual");
            var originalPosition=visual.localPosition;var originalScale=visual.localScale;var originalRotation=visual.localRotation;
            var rootPosition=instance.transform.position;
            for(int cycle=0;cycle<2;cycle++)
            {
                for(int angle=0;angle<360;angle+=45)
                {
                    instance.transform.rotation=Quaternion.Euler(0,angle,0);
                    pickup.SetWeaponDelivery(true,0,1.2f);
                    var b=EntityBoundsInPickup(visual);
                    Assert.That(new Vector2(b.center.x,b.center.z).magnitude,Is.LessThan(.0002f),"枪身中心必须对齐拾取旋转轴："+angle);
                    var scale=instance.transform.lossyScale;
                    Assert.That(Mathf.Max(b.size.x,b.size.y,b.size.z)*scale.x,Is.EqualTo(1.7f).Within(.002f));
                    Assert.That(instance.transform.position,Is.EqualTo(rootPosition),"不能移动拾取根或箱体位置来补偿枪身");
                }
                pickup.SetWeaponDelivery(false,0,1.2f);
                Assert.That(visual.localPosition,Is.EqualTo(originalPosition));
                Assert.That(visual.localScale,Is.EqualTo(originalScale));
                Assert.That(Quaternion.Angle(visual.localRotation,originalRotation),Is.LessThan(.001f));
            }
        }

        /// <summary>使用实体网格角点度量，忽略枪口粒子；旋转拾取根后仍在同一根坐标系比较。</summary>
        private Bounds EntityBoundsInPickup(Transform visual)
        {
            var result=new Bounds();bool found=false;
            foreach(var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                var bounds=renderer.localBounds;var matrix=instance.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
                for(int corner=0;corner<8;corner++)
                {
                    var point=matrix.MultiplyPoint3x4(bounds.center+Vector3.Scale(bounds.extents,
                        new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                    if(found)result.Encapsulate(point);else{result=new Bounds(point,Vector3.zero);found=true;}
                }
            }
            Assert.That(found,Is.True);return result;
        }
    }
}

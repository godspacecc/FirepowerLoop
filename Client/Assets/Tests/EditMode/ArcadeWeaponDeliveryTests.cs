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
            Assert.That(Mathf.Max(bounds.size.x,bounds.size.y,bounds.size.z),Is.InRange(.85f,1.2f),"开箱后武器应清晰可辨");
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
    }
}

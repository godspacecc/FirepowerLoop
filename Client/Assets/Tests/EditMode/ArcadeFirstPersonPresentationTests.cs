using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>以正式角色、武器和手臂验证街机瞄准表现，不写正式资源、不生成规则弹丸。</summary>
    public sealed class ArcadeFirstPersonPresentationTests
    {
        [Test]
        public void RifleArms_PointWeaponTowardCenterWithoutChangingSourceOrCollision() =>
            AssertWeaponAim("arcade.weapon.rapid");

        [Test]
        public void ShotgunArms_PointWeaponTowardCenterWithoutChangingSourceOrCollision() =>
            AssertWeaponAim("arcade.weapon.spread");

        private void AssertWeaponAim(string weaponId)
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab"));
            var cameraObject = new GameObject("test-camera", typeof(Camera));
            PlayerCameraCtrl ctrl = null;
            try
            {
                var view = player.GetComponent<PlayerView>();
                var presenter = player.GetComponent<CharacterWeaponPresenter>();
                Assert.That(presenter.SetWeaponId(weaponId), Is.True, presenter.LastError);
                presenter.SetManualPoseSampling(true);
                presenter.Current.Animations.Idle.Clip.SampleAnimation(view.Animator.gameObject, .2f);
                presenter.ApplyOffhandGrip(presenter.Current.Animations.Idle);
                var sourceRotation = presenter.Visual.transform.rotation;
                var originalColliders = player.GetComponentsInChildren<Collider>(true)
                    .ToDictionary(c => c, c => c.enabled);
                var camera = cameraObject.GetComponent<Camera>();
                ctrl = new PlayerCameraCtrl(camera, player.transform, view.CameraProfile, view.ShoulderCameraProfile);
                ctrl.SetArcadeMode(true);
                Assert.That(ctrl.SupportsMode(PlayerCameraMode.FirstPerson, out var error), Is.True, error);
                ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0, 0);
                var source = presenter.Visual.GetComponentsInChildren<MeshRenderer>().First();
                var copy = cameraObject.GetComponentsInChildren<MeshRenderer>()
                    .First(r => r.name == source.name);
                var copiedForward = copy.transform.TransformDirection(
                    source.transform.InverseTransformDirection(player.transform.forward));
                var sourceMuzzle = presenter.Visual.transform.TransformPoint(presenter.Current.ProjectileFirePointLocalPosition);
                var copiedMuzzle = copy.transform.TransformPoint(source.transform.InverseTransformPoint(sourceMuzzle));
                Assert.That(Vector3.Angle(copiedForward,
                    camera.transform.position + camera.transform.forward * 20 - copiedMuzzle), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(sourceRotation, presenter.Visual.transform.rotation), Is.LessThan(.001f));
                foreach (var pair in originalColliders) Assert.That(pair.Key.enabled, Is.EqualTo(pair.Value));
                Assert.That(player.GetComponentsInChildren<Renderer>().All(r => r.forceRenderingOff), Is.True);
                ctrl.ApplyMode(PlayerCameraMode.TopDown, 0, 0);
                Assert.That(cameraObject.transform.Find("PlayerFirstPersonRig"), Is.Null);
                Assert.That(player.GetComponentsInChildren<Renderer>().Any(r => !r.forceRenderingOff), Is.True);
            }
            finally
            {
                ctrl?.Dispose();
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}

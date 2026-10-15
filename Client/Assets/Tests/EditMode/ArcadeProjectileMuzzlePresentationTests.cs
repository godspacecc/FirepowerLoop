using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证街机武器的可见弹丸从当前枪口衔接到规则弹道。</summary>
    public sealed class ArcadeProjectileMuzzlePresentationTests
    {
        [Test]
        public void WeaponProjectilesStartAtMuzzleAndReturnToAuthoritativePath()
        {
            var arena = new GameObject("arena");
            var motor = new RecordingMotor { Muzzle = new Vector3(0.3f, 1.1f, 0.9f) };
            var projectiles = new RecordingProjectiles();
            var view = new ArcadeCombatViewController();
            SetField(view, "mapRoot", arena.transform);
            SetField(view, "playerMotor", motor);
            SetField(view, "projectilePresentation", projectiles);
            try
            {
                view.SetPlayerWeapon(new ArcadeWeaponDefinition("spread", "ArcadeScifiShotgun", null, true,
                    Vector3.zero, 1, 0.3f, 15f, 1f, 2, 20f, ArcadeWeaponBehavior.Direct, 1, 0f));
                view.SetPlayerFireSequence(0);
                view.SetProjectile(10, false, new Vector2(0f, 0.65f), Vector2.up);
                Assert.That(projectiles.LastPosition, Is.EqualTo(new Vector3(0f, 0.75f, 0.65f)));

                view.SetPlayerFireSequence(1);
                view.SetProjectile(11, false, new Vector2(0f, 0.65f), Vector2.up, null, true);
                Assert.That(projectiles.LastPosition, Is.EqualTo(motor.Muzzle));
                view.SetProjectile(12, false, new Vector2(0f, 0.65f), Vector2.up, null, true);
                Assert.That(projectiles.LastPosition, Is.EqualTo(motor.Muzzle));
                view.SetProjectile(13, false, new Vector2(0f, 0.65f), Vector2.up);
                Assert.That(projectiles.LastPosition, Is.EqualTo(new Vector3(0f, 0.75f, 0.65f)),
                    "非本次武器发射的弹丸不得继承枪口位置");

                view.SetProjectile(11, false, new Vector2(0f, 3.65f), Vector2.up, null, true);
                Assert.That(projectiles.LastPosition, Is.EqualTo(new Vector3(0f, 0.75f, 3.65f)));
            }
            finally
            {
                view.End();
                Object.DestroyImmediate(arena);
            }
        }

        [Test]
        public void FirstShotSequenceWithoutBaselineStillUsesMuzzle()
        {
            var arena = new GameObject("arena");
            var motor = new RecordingMotor { Muzzle = new Vector3(0.3f, 1.1f, 0.9f) };
            var projectiles = new RecordingProjectiles();
            var view = new ArcadeCombatViewController();
            SetField(view, "mapRoot", arena.transform);
            SetField(view, "playerMotor", motor);
            SetField(view, "projectilePresentation", projectiles);
            try
            {
                view.SetPlayerFireSequence(1);
                view.SetProjectile(1, false, new Vector2(0f, 0.65f), Vector2.up, null, true);
                Assert.That(projectiles.LastPosition, Is.EqualTo(motor.Muzzle));
            }
            finally
            {
                view.End();
                Object.DestroyImmediate(arena);
            }
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);

        private sealed class RecordingProjectiles : IArcadeProjectilePresentationPort
        {
            public Vector3 LastPosition { get; private set; }
            public bool TrySetProjectile(int id, Vector3 position, Vector3 direction, out string error)
            {
                LastPosition = position;
                error = null;
                return true;
            }
            public bool TrySetProjectile(int id, Vector3 position, Vector3 direction, string visual,
                bool hostile, out string error) => TrySetProjectile(id, position, direction, out error);
            public void RemoveProjectile(int id) { }
            public void ClearArcadeProjectiles() { }
        }

        private sealed class RecordingMotor : ICharacterMotorPort
        {
            public Vector3 Muzzle { get; set; }
            public int UnitId => 1;
            public long MapInstanceId => 1;
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
            public void FaceDirection(Vector3 direction) { }
            public void SetArcadeWeaponPresentation(string visualResourceName, bool muzzleFlash,
                Vector3 projectileFirePointLocalPosition) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Muzzle;
                return true;
            }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }
    }
}

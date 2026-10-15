using System.Reflection;
using Game;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class PlayerFlashlightViewTests
    {
        [Test]
        public void SetFlashlightActive_ControlsSerializedSpotLight()
        {
            var root = new GameObject("PlayerFlashlightTest");
            try
            {
                root.AddComponent<CharacterController>();
                var view = root.AddComponent<PlayerView>();
                var lightObject = new GameObject("Flashlight");
                lightObject.transform.SetParent(root.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Spot;
                SetField(view, "flashlight", light);

                view.SetFlashlightActive(true);
                Assert.That(light.enabled, Is.True);
                view.SetFlashlightActive(false);
                Assert.That(light.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetFlashlightDirection_PointsTowardHorizontalAimWithDownwardPitch()
        {
            var root = new GameObject("PlayerFlashlightDirectionTest");
            try
            {
                root.AddComponent<CharacterController>();
                var view = root.AddComponent<PlayerView>();
                var lightObject = new GameObject("Flashlight");
                lightObject.transform.SetParent(root.transform, false);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Spot;
                SetField(view, "flashlight", light);

                view.SetFlashlightDirection(Vector3.right);
                Assert.That(light.transform.forward.x, Is.GreaterThan(0.9f));
                Assert.That(light.transform.forward.y, Is.LessThan(0f));
                Assert.That(Mathf.Abs(light.transform.forward.z), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GrayboxPlayerPrefab_HasConfiguredFlashlight()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.That(prefab, Is.Not.Null);

            var view = prefab.GetComponent<PlayerView>();
            Assert.That(view, Is.Not.Null);
            var light = GetField<Light>(view, "flashlight");
            Assert.That(light, Is.Not.Null);
            Assert.That(light.transform.IsChildOf(prefab.transform), Is.True);
            Assert.That(light.type, Is.EqualTo(LightType.Spot));
            Assert.That(light.enabled, Is.False);
            Assert.That(light.intensity, Is.EqualTo(3.5f).Within(0.001f));
            Assert.That(light.range, Is.EqualTo(18f).Within(0.001f));
            Assert.That(light.spotAngle, Is.EqualTo(48f).Within(0.001f));
            Assert.That(light.innerSpotAngle, Is.EqualTo(28f).Within(0.001f));
            Assert.That(light.transform.localPosition.x, Is.Zero.Within(0.001f));
            Assert.That(light.transform.localPosition.y, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(light.transform.localPosition.z, Is.EqualTo(0.05f).Within(0.001f));
        }

        private static void SetField(object target, string name, object value)
        {
            typeof(PlayerView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name) where T : class
        {
            return typeof(PlayerView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target) as T;
        }
    }
}

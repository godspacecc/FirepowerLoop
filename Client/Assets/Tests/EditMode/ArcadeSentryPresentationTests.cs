using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证哨塔和战斗鸡按发射方向旋转、停火保持朝向，并隔离其它强化视觉。</summary>
    public sealed class ArcadeSentryPresentationTests
    {
        [TestCase(ArcadePowerupBehavior.Sentry)]
        [TestCase(ArcadePowerupBehavior.Chicken)]
        public void ShootingPowerupRotatesToShotDirectionAndKeepsPosition(ArcadePowerupBehavior behavior)
        {
            WithEffect(behavior, (view, instance, entry) =>
            {
                Sync(view, behavior, Vector2.right);
                Assert.That(Vector3.Angle(instance.transform.forward, Vector3.right), Is.LessThan(0.01f));
                Assert.That(instance.transform.localPosition, Is.EqualTo(new Vector3(2f, 0.5f, 3f)));
                Sync(view, behavior, Vector2.down);
                Assert.That(Vector3.Angle(instance.transform.forward, Vector3.back), Is.LessThan(0.01f));
                Sync(view, behavior, Vector2.zero);
                Assert.That(Vector3.Angle(instance.transform.forward, Vector3.back), Is.LessThan(0.01f));
            });
        }

        [TestCase(ArcadePowerupBehavior.Sentry)]
        [TestCase(ArcadePowerupBehavior.Chicken)]
        public void ShootingPowerupRemembersDirectionBeforeVisualFinishesLoading(ArcadePowerupBehavior behavior)
        {
            WithEffect(behavior, (view, instance, entry) =>
            {
                Set(entry, "Instance", null);
                Sync(view, behavior, Vector2.left);
                Assert.That(Get(entry, "HasShotDirection"), Is.True);
                var rotation = (Quaternion)Get(entry, "LocalRotation");
                Assert.That(Vector3.Angle(rotation * Vector3.forward, Vector3.left), Is.LessThan(0.01f));
            });
        }

        [Test]
        public void ShotDirectionDoesNotRotateOtherPowerupVisuals()
        {
            WithEffect(ArcadePowerupBehavior.Shield, (view, instance, entry) =>
            {
                instance.transform.localRotation = Quaternion.Euler(0f, 15f, 0f);
                Sync(view, ArcadePowerupBehavior.Shield, Vector2.right);
                Assert.That(Quaternion.Angle(instance.transform.localRotation, Quaternion.Euler(0f, 15f, 0f)),
                    Is.LessThan(0.01f));
                Assert.That(Get(entry, "HasShotDirection"), Is.False);
            });
        }

        [Test]
        public void ActualSentryBarrelPointsAlongShotDirection()
        {
            WithEffect(ArcadePowerupBehavior.Sentry, (view, instance, entry) =>
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Prefabs/SceneObjects/ArcadeSentryTurret.prefab");
                var model = UnityEngine.Object.Instantiate(prefab, instance.transform);
                var barrel = Array.Find(model.GetComponentsInChildren<Renderer>(), value => value.name == "namlu");
                Assert.That(barrel, Is.Not.Null);
                Sync(view, ArcadePowerupBehavior.Sentry, Vector2.right);
                var offset = barrel.bounds.center - model.transform.position;
                Assert.That(offset.x, Is.GreaterThan(0.2f), "实际炮管应伸向射击方向");
                Assert.That(Mathf.Abs(offset.z), Is.LessThan(0.01f));
            });
        }

        [Test]
        public void ActualChickenFacesPrefabForward()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/ArcadeChickenCompanion.prefab");
            var head = Array.Find(prefab.GetComponentsInChildren<Transform>(true), value => value.name == "head");
            Assert.That(head, Is.Not.Null);
            Assert.That(prefab.transform.InverseTransformPoint(head.position).z, Is.GreaterThan(0.1f),
                "鸡头应位于 Prefab 正前方，避免背对实际射击方向");
        }

        private static void Sync(ArcadeCombatViewController view, ArcadePowerupBehavior behavior, Vector2 direction)
        {
            var definition = new ArcadePowerupDefinition("test", behavior, "test-address", false,
                new Vector3(0f, 0.5f, 0f), 0f, 1, 0.2f, 5f, 10f, 0);
            var method = typeof(ArcadeCombatViewController).GetMethod("SetPowerup",
                new[] { typeof(long), typeof(ArcadePowerupDefinition), typeof(Vector2), typeof(Vector2) });
            Assert.That(method, Is.Not.Null, "强化表现尚未接收哨塔的实际发射方向");
            method.Invoke(view, new object[] { 7L, definition, new Vector2(2f, 3f), direction });
        }

        private static void WithEffect(ArcadePowerupBehavior behavior,
            Action<ArcadeCombatViewController, GameObject, object> assertion)
        {
            var root = new GameObject("sentry-test-root");
            var instance = new GameObject("effect");
            instance.transform.SetParent(root.transform, false);
            var view = new ArcadeCombatViewController();
            var type = typeof(ArcadeCombatViewController).GetNestedType("EffectVisualEntry", BindingFlags.NonPublic);
            var entry = Activator.CreateInstance(type, true);
            var key = $"powerup:7:{(int)behavior}";
            Set(entry, "Key", key);
            Set(entry, "Address", "test-address");
            Set(entry, "Instance", instance);
            Set(view, "root", root);
            ((IDictionary)Get(view, "effectVisuals")).Add(key, entry);
            try { assertion(view, instance, entry); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static object Get(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
    }
}

using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class GameObjectPoolTests
    {
        private GameObject poolRoot;

        [SetUp]
        public void SetUp()
        {
            poolRoot = new GameObject("PoolRoot");
        }

        [TearDown]
        public void TearDown()
        {
            GameObjectPoolHelper.ClearForTests();
            EditModeSceneIsolation.DestroyIfInActiveTestScene(poolRoot);
            poolRoot = null;
        }

        [Test]
        public void InitPool_PrewarmsInactiveInstancesAndReusesReturnedObject()
        {
            var prefab = CreatePrefab("TipTemplate");

            GameObjectPoolHelper.InitPool(prefab, "tip", 2);
            var first = GameObjectPoolHelper.GetObjectFromPool(prefab, "tip");
            GameObjectPoolHelper.ReturnObjectToPool(first);
            var second = GameObjectPoolHelper.GetObjectFromPool(prefab, "tip");

            Assert.That(first, Is.SameAs(second));
            Assert.That(second.activeSelf, Is.True);
            Assert.That(second.GetComponent<PoolObject>().IsPooled, Is.False);
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void GetObjectFromPool_AutoCreatesPoolWhenRequested()
        {
            var prefab = CreatePrefab("AutoTemplate");

            var instance = GameObjectPoolHelper.GetObjectFromPool(prefab, "auto", true, 1);

            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.activeSelf, Is.True);
            Assert.That(instance.GetComponent<PoolObject>().PoolName, Is.EqualTo("auto"));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void IncrementInflation_AddsOneObject()
        {
            var prefab = CreatePrefab("IncrementTemplate");
            GameObjectPoolHelper.InitPool(prefab, "increment", 1, PoolInflationType.Increment);

            var first = GameObjectPoolHelper.GetObjectFromPool(prefab, "increment");
            var second = GameObjectPoolHelper.GetObjectFromPool(prefab, "increment");

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.That(GameObjectPoolHelper.TotalInflateCount, Is.EqualTo(1));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void DoubleInflation_AddsInUseCount()
        {
            var prefab = CreatePrefab("DoubleTemplate");
            GameObjectPoolHelper.InitPool(prefab, "double", 2, PoolInflationType.Double);

            var first = GameObjectPoolHelper.GetObjectFromPool(prefab, "double");
            var second = GameObjectPoolHelper.GetObjectFromPool(prefab, "double");
            var third = GameObjectPoolHelper.GetObjectFromPool(prefab, "double");

            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.NotNull(third);
            Assert.That(GameObjectPoolHelper.TotalInflateCount, Is.EqualTo(2));
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void ReturnObjectToPool_IgnoresDuplicateReturn()
        {
            var prefab = CreatePrefab("DuplicateTemplate");
            GameObjectPoolHelper.InitPool(prefab, "duplicate", 1);
            var instance = GameObjectPoolHelper.GetObjectFromPool(prefab, "duplicate");

            GameObjectPoolHelper.ReturnObjectToPool(instance);
            GameObjectPoolHelper.ReturnObjectToPool(instance);

            var first = GameObjectPoolHelper.GetObjectFromPool(prefab, "duplicate");
            var second = GameObjectPoolHelper.GetObjectFromPool(prefab, "duplicate");

            Assert.AreSame(instance, first);
            Assert.AreNotSame(first, second);
            Object.DestroyImmediate(prefab);
        }

        [Test]
        public void ReturnObjectToPool_IgnoresNonPooledObject()
        {
            var go = new GameObject("LooseObject");

            Assert.DoesNotThrow(() => GameObjectPoolHelper.ReturnObjectToPool(go));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void RemovePool_OnlyDestroysNamedPoolAndIsIdempotent()
        {
            var tipPrefab = CreatePrefab("NotifyFloatTemplate");
            var otherPrefab = CreatePrefab("OtherTemplate");
            GameObjectPoolHelper.InitPool(tipPrefab, "NotifyFloatTip", 1);
            GameObjectPoolHelper.InitPool(otherPrefab, "other", 1);

            var released = GameObjectPoolHelper.GetObjectFromPool(tipPrefab, "NotifyFloatTip");
            GameObjectPoolHelper.ReturnObjectToPool(released);
            var retained = GameObjectPoolHelper.GetObjectFromPool(otherPrefab, "other");
            GameObjectPoolHelper.ReturnObjectToPool(retained);

            GameObjectPoolHelper.RemovePool("NotifyFloatTip");

            Assert.That(GameObjectPoolHelper.GetObjectFromPool(tipPrefab, "NotifyFloatTip", autoCreate: 0), Is.Null);
            Assert.That(released == null, Is.True);
            var other = GameObjectPoolHelper.GetObjectFromPool(otherPrefab, "other");
            Assert.That(other, Is.SameAs(retained));
            GameObjectPoolHelper.ReturnObjectToPool(other);

            Assert.DoesNotThrow(() => GameObjectPoolHelper.RemovePool("NotifyFloatTip"));
            Assert.DoesNotThrow(() => GameObjectPoolHelper.RemovePool(string.Empty));

            var replacementPrefab = CreatePrefab("ReplacementTemplate");
            GameObjectPoolHelper.InitPool(replacementPrefab, "NotifyFloatTip", 1);
            var replacement = GameObjectPoolHelper.GetObjectFromPool(replacementPrefab, "NotifyFloatTip");
            Assert.That(replacement, Is.Not.Null);
            Assert.That(replacement, Is.Not.SameAs(released));
            Object.DestroyImmediate(tipPrefab);
            Object.DestroyImmediate(otherPrefab);
            Object.DestroyImmediate(replacementPrefab);
        }

        private static GameObject CreatePrefab(string name)
        {
            var prefab = new GameObject(name);
            prefab.AddComponent<RectTransform>();
            prefab.SetActive(false);
            return prefab;
        }
    }
}

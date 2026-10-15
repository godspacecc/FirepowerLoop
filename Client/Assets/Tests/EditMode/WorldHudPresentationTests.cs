using System.IO;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class WorldHudPresentationTests
    {
        [TestCase(90f, 0f)]
        [TestCase(25f, 120f)]
        [TestCase(-45f, 270f)]
        public void PickupNameStaysParallelToScreenAwayFromScreenCenter(float pitch, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/SceneObjects/TestResourcePickup.prefab");
            var root = Object.Instantiate(prefab);
            try
            {
                var view = root.GetComponent<ResourcePickupView>();
                var cameraRotation = Quaternion.Euler(pitch, yaw, 0f);
                root.transform.position = new Vector3(7f, 2f, -4f);
                view.SetNameVisible(true);
                view.FaceName(cameraRotation * Vector3.forward, cameraRotation * Vector3.up);

                Assert.That(Quaternion.Angle(view.NameRoot.transform.rotation, cameraRotation),
                    Is.LessThan(0.01f), "名称应与屏幕平行，不随物品在画面中的位置倾斜");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnemyHealthBarVisibilityDependsOnSessionSwitchAndCorpse()
        {
            Assert.That(
                EnemyView.ShouldShowHealthBar(unitHealthBarsVisible: false, isCorpse: false),
                Is.False);
            Assert.That(
                EnemyView.ShouldShowHealthBar(unitHealthBarsVisible: true, isCorpse: false),
                Is.True);
            Assert.That(
                EnemyView.ShouldShowHealthBar(unitHealthBarsVisible: true, isCorpse: true),
                Is.False);
        }

        [TestCase(10, 20, 0.5f)]
        [TestCase(0, 20, 0f)]
        [TestCase(30, 20, 1f)]
        [TestCase(10, 0, 0f)]
        public void EnemyHealthFillIsClamped(int current, int maximum, float expected)
        {
            Assert.That(
                EnemyView.CalculateHealthFill(current, maximum),
                Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void PickupDisplayNameFormatsAmount()
        {
            Assert.That(
                ResourcePickupView.FormatDisplayName("测试物资", 3),
                Is.EqualTo("测试物资x3"));
            Assert.That(
                ResourcePickupView.FormatDisplayName("测试物资", 1),
                Is.EqualTo("测试物资"));
            Assert.That(
                ResourcePickupView.FormatDisplayName("测试物资", 0),
                Is.EqualTo("测试物资"));
        }

        [Test]
        public void PickupDisplayRangeUsesSixMeters()
        {
            Assert.That(
                LootCtrl.IsWithinDisplayRange(
                    Vector3.zero,
                    new Vector3(6f, 0f, 0f)),
                Is.True);
            Assert.That(
                LootCtrl.IsWithinDisplayRange(
                    Vector3.zero,
                    new Vector3(6.01f, 0f, 0f)),
                Is.False);
        }

        [Test]
        public void PickupPresentationAvoidsSceneSearchAndPerFrameCameraMain()
        {
            var source = File.ReadAllText(
                "Assets/Game/Scripts/Controller/Loot/LootCtrl.cs");

            Assert.That(source, Does.Not.Contain("FindObjectOfType"));
            Assert.That(source, Does.Not.Contain("FindObjectsOfType"));
            Assert.That(source, Does.Not.Contain("OnUpdate()\n        {\n            playerCamera = Camera.main"));
        }
    }
}

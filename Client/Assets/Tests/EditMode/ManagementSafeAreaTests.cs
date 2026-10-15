using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    /// <summary>验证真实管理宿主的安全区边界；不改写产品 Prefab 或设备显示设置。</summary>
    public sealed class ManagementSafeAreaTests
    {
        private GameObject canvasRoot;
        private BagUI ui;

        [SetUp]
        public void SetUp()
        {
            canvasRoot = new GameObject("SafeAreaTestCanvas", typeof(RectTransform), typeof(Canvas));
            canvasRoot.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");
            ui = Object.Instantiate(prefab, canvasRoot.transform, false).GetComponent<BagUI>();
            ui.ManagementRoot.SetActive(true);
            ui.CompanionRoot.SetActive(false);
            ui.WarehouseCompanionRoot.SetActive(false);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(canvasRoot);

        [Test]
        public void ManagementBackgroundKeepsCoverageWhileEveryPageAndNavigationFitsSafeArea()
        {
            var min = new Vector2(.08f, .05f);
            var max = new Vector2(.98f, .97f);
            Apply(min, max);
            var background = ui.ManagementRoot.GetComponent<RectTransform>();
            Assert.That(background.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(background.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(background.sizeDelta, Is.EqualTo(Vector2.zero));
            Assert.That(background.GetComponent<Image>().raycastTarget, Is.True);
            foreach (var page in new[] { ui.InventoryPageRoot.transform, ui.EquipmentPage.transform,
                         ui.StatusPage.transform, ui.MapPage.transform, ui.TasksPage.transform, ui.JournalPage.transform })
            {
                Assert.That(((RectTransform)page).anchorMin, Is.EqualTo(min), page.name);
                Assert.That(((RectTransform)page).anchorMax, Is.EqualTo(max), page.name);
            }
            var close = ui.BtnManagementClose.GetComponent<RectTransform>();
            Assert.That(close.anchorMin, Is.EqualTo(max));
            var navigation = (RectTransform)ui.BtnPageBackpack.transform.parent;
            Assert.That(navigation.anchorMin, Is.EqualTo(min));
            Assert.That(navigation.anchorMax, Is.EqualTo(new Vector2(max.x, min.y)));
            Canvas.ForceUpdateCanvases();
            var bounds = background.rect;
            var safeBounds = Rect.MinMaxRect(bounds.xMin + bounds.width * min.x, bounds.yMin + bounds.height * min.y,
                bounds.xMin + bounds.width * max.x, bounds.yMin + bounds.height * max.y);
            foreach (var control in new[] { close, navigation })
            {
                var corners = new Vector3[4]; control.GetWorldCorners(corners);
                foreach (var corner in corners)
                    Assert.That(safeBounds.Contains(background.InverseTransformPoint(corner)), Is.True, control.name);
            }
        }

        [Test]
        public void RotatedInsetsAndRepeatedShowNeverAccumulateOrLeaveDesktopMargins()
        {
            Apply(new Vector2(.08f, .05f), new Vector2(.98f, .97f));
            ui.ManagementRoot.SetActive(false);
            ui.ManagementRoot.SetActive(true);
            var min = new Vector2(.02f, .04f); var max = new Vector2(.92f, .98f);
            Apply(min, max); Apply(min, max);
            Assert.That(ui.BtnManagementClose.GetComponent<RectTransform>().anchorMin, Is.EqualTo(max));
            Apply(Vector2.zero, Vector2.one);
            Assert.That(ui.InventoryPageRoot.GetComponent<RectTransform>().anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(ui.BtnManagementClose.GetComponent<RectTransform>().anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(ui.BtnManagementClose.GetComponent<RectTransform>().offsetMax, Is.EqualTo(new Vector2(-20, -16)));
        }

        private void Apply(Vector2 min, Vector2 max)
        {
            var rootFitter = ui.GetComponent<SafeAreaFitter>() ?? ui.gameObject.AddComponent<SafeAreaFitter>();
            var contentFitter = ui.ManagementRoot.GetComponent<SafeAreaFitter>();
            Assert.That(contentFitter, Is.Not.Null, "全屏背景容器的内部控件必须拥有安全区适配");
            var apply = typeof(SafeAreaFitter).GetMethod("ApplyAnchors", BindingFlags.Instance | BindingFlags.NonPublic);
            apply.Invoke(rootFitter, new object[] { min, max });
            apply.Invoke(contentFitter, new object[] { min, max });
        }
    }
}

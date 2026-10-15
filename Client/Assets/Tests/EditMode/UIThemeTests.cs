using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class UIThemeTests
    {
        [Test]
        public void ThemeAppliesSharedButtonMetricsAndSemanticColours()
        {
            var root = new GameObject("OnlineLogin(Clone)", typeof(RectTransform), typeof(Image));
            try
            {
                var login = CreateButton(root.transform, "LoginButton", 56f);
                var exit = CreateButton(root.transform, "ExitButton", 38f);

                UITheme.Apply(root);

                Assert.That(login.targetGraphic.color, Is.EqualTo(UITheme.Primary));
                Assert.That(exit.targetGraphic.color, Is.EqualTo(UITheme.Danger));
                Assert.That(((RectTransform)login.transform).sizeDelta.y, Is.EqualTo(UITheme.StandardControlHeight));
                Assert.That(((RectTransform)exit.transform).sizeDelta.y, Is.EqualTo(UITheme.StandardControlHeight));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ThemeKeepsInventorySlotsAtTheirFunctionalSize()
        {
            var root = new GameObject("Inventory", typeof(RectTransform), typeof(Image));
            try
            {
                var slot = CreateButton(root.transform, "InventorySlot", 72f);

                UITheme.Apply(root);

                Assert.That(((RectTransform)slot.transform).sizeDelta.y, Is.EqualTo(72f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ThemePreservesMessageBackgroundButtonAppearanceAndInteraction()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Game/Prefabs/UI/Notify/Message.prefab");
            try
            {
                var background = root.GetComponent<MessageUI>().BackgroundButton;
                Assert.That(background, Is.Not.Null, "正式消息 Prefab 必须绑定背景取消按钮。");
                var image = background.targetGraphic as Image;
                Assert.That(image, Is.Not.Null);
                var expectedColor = image.color;
                var expectedStates = background.colors;
                Assert.That(expectedColor, Is.EqualTo(new Color(0f, 0f, 0f, 0.15f)));

                UITheme.Apply(root);
                UITheme.Apply(root);

                Assert.That(image.color, Is.EqualTo(expectedColor), "主题不得把全屏遮罩染成不透明操作按钮。");
                Assert.That(background.colors, Is.EqualTo(expectedStates), "遮罩的点击状态颜色由 Prefab 持有。");
                Assert.That(image.raycastTarget, Is.True);
                Assert.That(background.interactable, Is.True);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void ThemePreservesSettingBackdropAndThemesItsPanel()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/Game/Prefabs/UI/Home/Setting.prefab");
            try
            {
                var backdrop = root.GetComponent<Image>();
                var expectedColor = backdrop.color;
                Assert.That(expectedColor, Is.EqualTo(new Color(0f, 0f, 0f, 0.45f)));

                UITheme.Apply(root);
                UITheme.Apply(root);

                Assert.That(backdrop.color, Is.EqualTo(expectedColor), "主题不得加深设置窗口的全屏遮罩。");
                Assert.That(backdrop.raycastTarget, Is.True);
                Assert.That(root.transform.Find("Panel").GetComponent<Image>().color,
                    Is.EqualTo(UITheme.SurfaceRaised), "保留遮罩不应取消中央面板的主题样式。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Button CreateButton(Transform parent, string name, float height)
        {
            var target = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            target.transform.SetParent(parent, false);
            ((RectTransform)target.transform).sizeDelta = new Vector2(160f, height);
            return target.GetComponent<Button>();
        }
    }
}

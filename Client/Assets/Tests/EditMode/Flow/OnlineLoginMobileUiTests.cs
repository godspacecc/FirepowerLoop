using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Game.Tests
{
    public sealed class OnlineLoginMobileUiTests
    {
        private const string Path = "Assets/Game/Prefabs/UI/Online/OnlineLogin.prefab";

        [Test]
        public void LoginUsesFullScreenBackgroundAndStartScreenControlSizes()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var rect = (RectTransform)root.transform;
            Assert.AreEqual(Vector2.zero, rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);
            var background = root.GetComponentInChildren<RawImage>(true);
            Assert.IsNotNull(background);
            Assert.AreEqual("Assets/Game/Art/UI/Start/StartBackground.png", AssetDatabase.GetAssetPath(background.texture));
            Assert.IsFalse(background.raycastTarget);
            var view = root.GetComponent<OnlineLoginUI>();
            foreach (var input in new[] { view.UserNameInput, view.PasswordInput, view.ServerAddressInput })
            {
                Assert.AreEqual(64, ((RectTransform)input.transform).sizeDelta.y);
                Assert.IsNotNull(input.textViewport.GetComponent<RectMask2D>());
                Assert.GreaterOrEqual(input.textComponent.fontSize, 24);
            }
            foreach (var button in new[] { view.LoginButton, view.RoomButton, view.WorldButton, view.LogoutButton, view.CloseButton })
                Assert.AreEqual(64, ((RectTransform)button.transform).sizeDelta.y);
            Assert.AreEqual(TMP_InputField.ContentType.Password, view.PasswordInput.contentType);
        }

        [Test]
        public void AuthenticatedActionsDoNotOverlapAndThemePreservesPrimaryColor()
        {
            var host = new GameObject("LoginLayoutHost", typeof(RectTransform));
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Path), host.transform, false);
            try
            {
                var view = instance.GetComponent<OnlineLoginUI>();
                var room = (RectTransform)view.RoomButton.transform;
                var world = (RectTransform)view.WorldButton.transform;
                var logout = (RectTransform)view.LogoutButton.transform;
                // 覆盖手机截图及常见宽屏；按正式 CanvasScaler 的宽高混合比例检查布局。
                foreach (var resolution in new[] { new Vector2(1920, 1080), new Vector2(2712, 1220), new Vector2(2400, 1080) })
                {
                    float scale = Mathf.Sqrt(resolution.x / 1920f * resolution.y / 1080f);
                    ((RectTransform)host.transform).sizeDelta = resolution / scale;
                    Canvas.ForceUpdateCanvases();
                    Assert.IsFalse(Bounds(room).Overlaps(Bounds(world)));
                    Assert.IsFalse(Bounds(world).Overlaps(Bounds(logout)));
                    Assert.IsFalse(Bounds((RectTransform)view.PasswordInput.transform).Overlaps(Bounds((RectTransform)view.LoginButton.transform)));
                }
                var before = view.RoomButton.targetGraphic.color;
                UITheme.Apply(instance);
                Assert.AreEqual(before, view.RoomButton.targetGraphic.color);
                Assert.Greater(before.g, .7f);
                Assert.IsTrue(view.ServerAddressInput.gameObject.activeSelf);
                Assert.IsTrue(view.SaveServerButton.gameObject.activeSelf);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void StaticCaptionsHaveReadableChinesePreviewsAndBothLanguages()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
            var chinese = JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            var english = JObject.Parse(File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            foreach (var localized in root.GetComponentsInChildren<LocalizedText>(true))
            {
                Assert.IsNotNull(chinese[localized.Key], localized.name);
                Assert.IsNotNull(english[localized.Key], localized.name);
                Assert.AreEqual((string)chinese[localized.Key], localized.GetComponent<TextMeshProUGUI>().text, localized.name);
                Assert.IsEmpty(localized.Texts);
            }
            var view = root.GetComponent<OnlineLoginUI>();
            Assert.IsNull(view.AuthenticatedUserText.GetComponent<LocalizedText>());
            Assert.IsFalse(view.AuthenticatedUserText.richText);
        }

        private static Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }
    }
}

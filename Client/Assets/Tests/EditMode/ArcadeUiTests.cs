using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;
using TMPro;

namespace Game.Tests
{
    /// <summary>锁定街机 UI 的生成引用、列表 Box 和输入阻断边界，防止后续 Prefab 调整破坏运行时装载。</summary>
    public sealed class ArcadeUiTests
    {
        private const string Root = "Assets/Game/Prefabs/UI/Arcade/";
        private const string OfficialStickPrefabPath =
            "Assets/Samples/Input System/1.7.0/On-Screen Controls/Stick.prefab";

        [Test]
        public void ArcadePrefabs_HaveCollectorGeneratedViewAndBoundReferences()
        {
            AssertBound<ArcadeHudUI>(Root + "ArcadeHud.prefab");
            AssertBound<ArcadeShopUI>(Root + "ArcadeShop.prefab");
            AssertBound<ArcadeResumeUI>(Root + "ArcadeResume.prefab");
            AssertBound<ArcadeShopOfferBoxUI>(Root + "Box/ArcadeShopOfferBox.prefab");
            AssertBound<ArcadePlayerStatusBoxUI>(Root + "Box/ArcadePlayerStatusBox.prefab");
        }

        [Test]
        public void ArcadeShop_UsesExternalOfferBoxPrefab()
        {
            var shop = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "ArcadeShop.prefab").GetComponent<ArcadeShopUI>();
            var box = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Box/ArcadeShopOfferBox.prefab");
            Assert.That(AssetDatabase.GetAssetPath(shop.OfferList.itemPrefab), Is.EqualTo(AssetDatabase.GetAssetPath(box)));
            Assert.That(shop.OfferList.transform.childCount, Is.Zero, "正式列表元素不应内嵌在主 Prefab 中");
            Assert.That(shop.BtnBuy.gameObject.activeSelf, Is.True, "购买入口在商品详情区独立呈现");
            Assert.That(shop.TextDetailEffect.rectTransform.rect.height, Is.GreaterThanOrEqualTo(400f),
                "正式商品详情需容纳前置、限购与作用范围");
            Assert.That(shop.OfferList.GetComponent<GridLayoutGroup>().constraintCount, Is.EqualTo(4));
            Assert.That(shop.BtnReroll, Is.Not.Null);Assert.That(shop.BtnFinish, Is.Not.Null);
            Assert.That(shop.BtnCloseMenu, Is.Not.Null);
        }

        [TestCase(null, false, false)]
        [TestCase("", false, false)]
        [TestCase("challenge.time", false, true)]
        [TestCase("challenge.time", true, false)]
        public void ArcadeCheckpointChallengeButton_OnlyShowsBeforeCurrentNodeAttempt(
            string challengeId, bool attempted, bool expected)
        {
            Assert.That(ArcadeResumeUICtrl.ShouldShowChallengeButton(challengeId, attempted), Is.EqualTo(expected));
        }

        [Test]
        public void SlayerMartUsesPreservedResourceHost()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "ArcadeShop.prefab").GetComponent<ArcadeShopUI>(), Is.Not.Null);
        }

        [Test]
        public void ArcadeControllers_UseExpectedBlockingLayers()
        {
            var hud = new ArcadeHudUICtrl();
            var resume = new ArcadeResumeUICtrl();
            Assert.That(hud.Layer(), Is.EqualTo(UILayer.HUD));
            Assert.That(hud.BlocksGame, Is.False);
            Assert.That(resume.Layer(), Is.EqualTo(UILayer.Dialog));
            Assert.That(resume.BlocksGame, Is.True);
        }

        [Test]
        public void ArcadeHud_UsesOfficialDualOnScreenSticks()
        {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "ArcadeHud.prefab").GetComponent<ArcadeHudUI>();

            Assert.That(hud.MoveJoystick, Is.TypeOf<OnScreenStick>());
            Assert.That(hud.MoveJoystick.controlPath, Is.EqualTo("<Gamepad>/leftStick"));
            Assert.That(hud.AimJoystick, Is.TypeOf<OnScreenStick>());
            Assert.That(hud.AimJoystick.controlPath, Is.EqualTo("<Gamepad>/rightStick"));
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(
                hud.MoveJoystick.gameObject)), Is.EqualTo(OfficialStickPrefabPath));
            Assert.That(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(
                hud.AimJoystick.gameObject)), Is.EqualTo(OfficialStickPrefabPath));
            Assert.That(hud.MoveJoystick.transform.Find("Text"), Is.Not.Null);
            Assert.That(hud.AimJoystick.transform.Find("Text"), Is.Not.Null);
            var moveLabel = hud.MoveJoystick.transform.Find("Text");
            var aimLabel = hud.AimJoystick.transform.Find("Text");
            Assert.That(moveLabel.GetComponent<Text>(), Is.Null, "移动摇杆不应保留硬编码英文的旧版 Text");
            Assert.That(aimLabel.GetComponent<Text>(), Is.Null, "瞄准摇杆不应保留硬编码英文的旧版 Text");
            Assert.That(moveLabel.GetComponent<TextMeshProUGUI>(), Is.Not.Null);
            Assert.That(aimLabel.GetComponent<TextMeshProUGUI>(), Is.Not.Null);
            Assert.That(moveLabel.GetComponent<LocalizedText>()?.Key, Is.EqualTo("ui.arcade.hud.move_stick"));
            Assert.That(aimLabel.GetComponent<LocalizedText>()?.Key, Is.EqualTo("ui.arcade.hud.aim_stick"));
            Assert.That(hud.MoveJoystick.transform.parent.gameObject, Is.EqualTo(hud.MoveJoystickRoot));
            Assert.That(hud.AimJoystick.transform.parent.gameObject, Is.EqualTo(hud.AimJoystickRoot));
            Assert.That(hud.MoveJoystick.gameObject.activeSelf, Is.True, "内部摇杆由外层布局容器控制显隐");
            Assert.That(hud.AimJoystick.gameObject.activeSelf, Is.True);
            Assert.That(hud.MoveJoystickRoot.activeSelf, Is.False, "编辑器加载 Prefab 时不应默认显示移动摇杆");
            Assert.That(hud.AimJoystickRoot.activeSelf, Is.False, "编辑器加载 Prefab 时不应默认显示瞄准摇杆");

            var statusBoxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                Root + "Box/ArcadePlayerStatusBox.prefab");
            var statusBox = statusBoxPrefab.GetComponent<ArcadePlayerStatusBoxUI>();
            Assert.That(hud.PlayerStatusList, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(hud.PlayerStatusList.itemPrefab),
                Is.EqualTo(AssetDatabase.GetAssetPath(statusBoxPrefab)),
                "多人状态列表必须复用独立 Box Prefab");
            Assert.That(hud.PlayerStatusList.transform.childCount, Is.Zero,
                "玩家状态列表不应在 HUD Prefab 内嵌固定人数条目");
            Assert.That(statusBox.TextGold, Is.Not.Null, "每名玩家的列表行都必须显示个人金币");
            Assert.That(statusBox.HealthFill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(statusBox.TextName.fontSize, Is.GreaterThanOrEqualTo(28f));
            Assert.That(statusBox.TextLives.fontSize, Is.GreaterThanOrEqualTo(28f));
            Assert.That(statusBox.TextGold.fontSize, Is.GreaterThanOrEqualTo(28f));
            Assert.That(statusBox.TextName.enableAutoSizing, Is.True);
            Assert.That(statusBox.TextName.rectTransform.rect.width, Is.GreaterThanOrEqualTo(80f));
            Assert.That(statusBox.TextState.enableAutoSizing, Is.True);
            Assert.That(statusBox.TextLives.enableAutoSizing, Is.True);
            Assert.That(statusBox.TextGold.enableAutoSizing, Is.True);
            Assert.That(statusBox.TextGold.rectTransform.rect.width, Is.GreaterThanOrEqualTo(100f));
            Assert.That(((RectTransform)statusBox.transform).sizeDelta,
                Is.EqualTo(new Vector2(500f, 124f)), "玩家行需容纳原信息及独立倍率进度行");
            Assert.That(hud.EffectList.itemPrefab, Is.Not.Null);
            Assert.That(hud.TextArena.enableAutoSizing, Is.True);
            Assert.That(hud.TextArena.rectTransform.rect.width, Is.GreaterThanOrEqualTo(220f));
            Assert.That(hud.TextWave.enableAutoSizing, Is.True);
            Assert.That(hud.TextWave.rectTransform.rect.width, Is.GreaterThanOrEqualTo(340f));
            Assert.That(hud.TextRespawn.enableAutoSizing, Is.True);
            Assert.That(hud.TextRespawn.enableWordWrapping, Is.True);
            Assert.That(hud.TextRespawn.rectTransform.rect.width, Is.GreaterThanOrEqualTo(880f));
            Assert.That(hud.TextRespawn.rectTransform.rect.height, Is.GreaterThanOrEqualTo(112f));

            var bombLabel = hud.BtnBomb.GetComponentInChildren<TextMeshProUGUI>(true);
            var dashLabel = hud.BtnDash.GetComponentInChildren<TextMeshProUGUI>(true);
            var pauseLabel = hud.BtnPause.GetComponentInChildren<TextMeshProUGUI>(true);
            var retryLabel = hud.BtnRetry.GetComponentInChildren<TextMeshProUGUI>(true);
            Assert.That(bombLabel.enableAutoSizing, Is.True);
            Assert.That(dashLabel.enableAutoSizing, Is.True);
            Assert.That(pauseLabel.enableAutoSizing, Is.True);
            Assert.That(retryLabel.enableAutoSizing, Is.True);
            Assert.That(bombLabel.GetComponent<LocalizedText>(), Is.Null, "动态数量由 Controller 完整刷新，不应被静态本地化组件抢写");
            Assert.That(dashLabel.GetComponent<LocalizedText>(), Is.Null, "动态数量由 Controller 完整刷新，不应被静态本地化组件抢写");

            var moveRect = (RectTransform)hud.MoveJoystick.transform;
            var aimRect = (RectTransform)hud.AimJoystick.transform;
            var bombRect = (RectTransform)hud.BtnBomb.transform;
            var dashRect = (RectTransform)hud.BtnDash.transform;
            var pauseRect = (RectTransform)hud.BtnPause.transform;
            Assert.That(moveRect.sizeDelta, Is.EqualTo(new Vector2(180f, 180f)),
                "左摇杆应提供适合移动端拇指操作的触控区域");
            Assert.That(aimRect.sizeDelta, Is.EqualTo(new Vector2(180f, 180f)),
                "右摇杆应提供适合移动端拇指操作的触控区域");
            Assert.That(hud.MoveJoystick.movementRange, Is.EqualTo(75f).Within(0.001f));
            Assert.That(hud.AimJoystick.movementRange, Is.EqualTo(75f).Within(0.001f));
            Assert.That(pauseRect.rect.width, Is.GreaterThanOrEqualTo(160f));
            Assert.That(pauseRect.rect.height, Is.GreaterThanOrEqualTo(88f));
            Assert.That(dashRect.anchorMin, Is.EqualTo(Vector2.one), "能力集中于已确认的右上区域");
            Assert.That(bombRect.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(RectTop(bombRect), Is.LessThan(RectBottom(pauseRect)), "炸弹不覆盖暂停");
            Assert.That(RectTop(dashRect), Is.LessThan(RectBottom(bombRect)), "两种能力不覆盖");
        }

        [Test]
        public void ArcadePlayerStatusHealthFill_UpdatesWithoutSprite()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Box/ArcadePlayerStatusBox.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var fill = instance.GetComponent<ArcadePlayerStatusBoxUI>().HealthFill;
                Assert.That(fill.sprite, Is.Null, "该回归覆盖正式 Box 当前使用的无 Sprite 血条路径");

                ArcadeHudUICtrl.ApplyHealthFill(fill, 0.5f);
                Assert.That(fill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(fill.rectTransform.anchorMax.x, Is.EqualTo(0.5f).Within(0.001f));

                ArcadeHudUICtrl.ApplyHealthFill(fill, 0.25f);
                Assert.That(fill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(fill.rectTransform.anchorMax.x, Is.EqualTo(0.25f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void ArcadeStaticLabels_HaveLocalizationKeys()
        {
            foreach (var path in new[]
                     {
                         Root + "ArcadeHud.prefab", Root + "ArcadeShop.prefab", Root + "ArcadeResume.prefab",
                         Root + "Box/ArcadeShopOfferBox.prefab"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var localized in prefab.GetComponentsInChildren<LocalizedText>(true))
                    Assert.That(localized.Key, Is.Not.Empty, $"{path}:{localized.name}");
            }
        }

        [Test]
        public void ArcadeResume_OnlineEntryIsGeneratedAndLocalized()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "ArcadeResume.prefab");
            var view = prefab.GetComponent<ArcadeResumeUI>();
            Assert.That(view.BtnOnline, Is.Not.Null);
            Assert.That(view.BtnOnline.name, Is.EqualTo("BtnOnline"));
            Assert.That(view.BtnOnline.GetComponentInChildren<LocalizedText>(true)?.Key,
                Is.EqualTo("ui.arcade.entry.lobby"));
        }

        private static void AssertBound<T>(string path) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var collector = prefab.GetComponent<ReferenceCollector>();
            var view = prefab.GetComponent<T>();
            Assert.That(collector, Is.Not.Null, path);
            Assert.That(view, Is.Not.Null, path);
            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.That(collector.data.Select(value => value.key), Is.EquivalentTo(fields.Select(value => value.Name)), path);
            foreach (var field in fields) Assert.That(field.GetValue(view), Is.Not.Null, path + ":" + field.Name);
        }

        private static float RectCenterX(RectTransform rect) =>
            rect.anchoredPosition.x + (0.5f - rect.pivot.x) * rect.rect.width;

        private static float RectBottom(RectTransform rect) =>
            rect.anchoredPosition.y - rect.pivot.y * rect.rect.height;

        private static float RectTop(RectTransform rect) =>
            rect.anchoredPosition.y + (1f - rect.pivot.y) * rect.rect.height;
    }
}

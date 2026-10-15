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
            Assert.That(shop.BtnBuy.gameObject.activeSelf, Is.False, "购买入口应位于商品卡，底部旧购买按钮必须隐藏");
            Assert.That(shop.TextDetailEffect.rectTransform.rect.height, Is.GreaterThanOrEqualTo(100f),
                "底部已购内容需要足够的多行展示高度");

            RectTransform challenge = shop.BtnChallenge.GetComponent<RectTransform>();
            RectTransform settle = shop.BtnSettle.GetComponent<RectTransform>();
            RectTransform continueButton = shop.BtnContinue.GetComponent<RectTransform>();
            Assert.That(challenge.anchoredPosition.x,
                Is.LessThanOrEqualTo(settle.anchoredPosition.x - settle.rect.width),
                "挑战按钮不得与结算按钮重叠");
            Assert.That(settle.anchoredPosition.x,
                Is.LessThanOrEqualTo(continueButton.anchoredPosition.x - continueButton.rect.width),
                "结算按钮不得与继续按钮重叠");
        }

        [TestCase(null, false, false)]
        [TestCase("", false, false)]
        [TestCase("challenge.time", false, true)]
        [TestCase("challenge.time", true, false)]
        public void ArcadeShopChallengeButton_OnlyShowsBeforeCurrentNodeAttempt(
            string challengeId, bool attempted, bool expected)
        {
            Assert.That(ArcadeShopUICtrl.ShouldShowChallengeButton(challengeId, attempted), Is.EqualTo(expected));
        }

        [Test]
        public void ArcadeShopOwnedContent_AggregatesRunInventoryAndOmitsBasicWeapon()
        {
            // 本测试会通过 Loc 读取真实语言表；独立运行时不能依赖先前测试初始化资源包。
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            try
            {
            string content = ArcadeShopUICtrl.FormatOwnedContent(
                new Dictionary<string, int> { ["test.upgrade"] = 2 },
                new Dictionary<string, int> { ["test.supply"] = 3 },
                new Dictionary<string, string>
                {
                    [ArcadeRunDefaults.WeaponSlotId] = ArcadeRunDefaults.BasicWeaponId,
                    ["support"] = "test.equipment",
                });

            Assert.That(content, Does.Contain("test.upgrade × 2"));
            Assert.That(content, Does.Contain("test.supply × 3"));
            Assert.That(content, Does.Contain("test.equipment × 1"));
            Assert.That(content, Does.Not.Contain(ArcadeRunDefaults.BasicWeaponId));
            }
            finally
            {
                TestReset.ResetArchitecture();
                ResourceLoader.SetLoaderForTests(null);
            }
        }

        [Test]
        public void ArcadeControllers_UseExpectedBlockingLayers()
        {
            var hud = new ArcadeHudUICtrl();
            var shop = new ArcadeShopUICtrl();
            var resume = new ArcadeResumeUICtrl();
            Assert.That(hud.Layer(), Is.EqualTo(UILayer.HUD));
            Assert.That(hud.BlocksGame, Is.False);
            Assert.That(shop.Layer(), Is.EqualTo(UILayer.Dialog));
            Assert.That(shop.BlocksGame, Is.True);
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
            Assert.That(statusBox.TextName.rectTransform.rect.width, Is.GreaterThanOrEqualTo(170f));
            Assert.That(statusBox.TextState.enableAutoSizing, Is.True);
            Assert.That(statusBox.TextLives.enableAutoSizing, Is.True);
            Assert.That(statusBox.TextGold.enableAutoSizing, Is.True);
            Assert.That(statusBox.TextGold.rectTransform.rect.width, Is.GreaterThanOrEqualTo(100f));
            Assert.That(((RectTransform)statusBox.transform).sizeDelta,
                Is.EqualTo(new Vector2(500f, 108f)), "玩家行需容纳原信息及独立倍率进度行");
            Assert.That(hud.TemporaryStatusRoot, Is.Not.Null);
            Assert.That(hud.transform.Find("WeaponStatus"), Is.Null, "HUD 不应保留常驻底部武器栏");
            var temporaryStatusRect = (RectTransform)hud.TemporaryStatusRoot.transform;
            var temporaryTextRect = (RectTransform)hud.TextTemporary.transform;
            var supportTextRect = (RectTransform)hud.TextSupport.transform;
            Assert.That(temporaryStatusRect.rect.width, Is.GreaterThanOrEqualTo(600f));
            Assert.That(temporaryStatusRect.rect.height, Is.LessThanOrEqualTo(96f),
                "默认单行临时火力背景不应继续占用多行强化的预留高度");
            Assert.That(temporaryTextRect.rect.width, Is.GreaterThanOrEqualTo(560f),
                "临时武器名称和倒计时必须拥有完整显示宽度");
            Assert.That(temporaryTextRect.rect.height, Is.GreaterThanOrEqualTo(72f));
            Assert.That(hud.TextTemporary.enableWordWrapping, Is.True);
            Assert.That(hud.TextTemporary.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(hud.TextTemporary.enableAutoSizing, Is.True);
            Assert.That(supportTextRect.rect.height, Is.GreaterThanOrEqualTo(320f),
                "多个并存强化必须拥有独立的多行显示空间");
            Assert.That(hud.TextSupport.enableWordWrapping, Is.True);
            Assert.That(hud.TextSupport.overflowMode, Is.EqualTo(TextOverflowModes.Overflow));
            Assert.That(ArcadeHudUICtrl.CalculateTimedStatusHeight(true, 0), Is.EqualTo(80f));
            Assert.That(ArcadeHudUICtrl.CalculateTimedStatusHeight(false, 1), Is.EqualTo(58f));
            Assert.That(ArcadeHudUICtrl.CalculateTimedStatusHeight(true, 3), Is.EqualTo(212f));

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
            Assert.That(dashRect.anchoredPosition.x,
                Is.LessThanOrEqualTo(bombRect.anchoredPosition.x - bombRect.rect.width), "两种能力不覆盖");
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

        [Test]
        public void TimedStatus_ContractsToTextAndKeepsRightEdgeAndCountdownWidth()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "ArcadeHud.prefab"));
            try
            {
                var hud = instance.GetComponent<ArcadeHudUI>();
                var ctrl = new ArcadeHudUICtrl();
                typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, hud);
                var layout = typeof(ArcadeHudUICtrl).GetMethod("ApplyTimedStatusLayout", BindingFlags.Instance | BindingFlags.NonPublic);
                var root = (RectTransform)hud.TemporaryStatusRoot.transform;
                float originalWidth = root.rect.width;
                Vector2 position = root.anchoredPosition;
                hud.TextTemporary.text = "临时火力：速射武器  11秒";
                hud.TextSupport.text = "强化：自动哨戒炮  10秒\n强化：战斗鸡伙伴  21秒";
                layout.Invoke(ctrl, new object[] { true, 2 });
                float width = root.rect.width;
                Assert.That(width, Is.LessThan(originalWidth - 40f), "短文本不应继续占满固定背景宽度");
                Assert.That(root.anchoredPosition, Is.EqualTo(position));
                Assert.That(root.pivot.x, Is.EqualTo(1f));
                Assert.That(hud.TextTemporary.rectTransform.rect.width, Is.LessThan(width));
                hud.TextTemporary.text = "临时火力：速射武器  9秒";
                hud.TextSupport.text = "强化：自动哨戒炮  9秒\n强化：战斗鸡伙伴  20秒";
                layout.Invoke(ctrl, new object[] { true, 2 });
                Assert.That(root.rect.width, Is.EqualTo(width).Within(0.1f), "跨两位数倒计时不能使背景跳动");
                hud.TextTemporary.text = "Temporary firepower: Grenade Launcher  16 seconds";
                hud.TextSupport.text = string.Empty;
                layout.Invoke(ctrl, new object[] { true, 0 });
                Assert.That(root.rect.width, Is.LessThanOrEqualTo(originalWidth));
                Assert.That(root.rect.width, Is.GreaterThan(width), "较长英文仍应能扩展到最大宽度");
                hud.TextTemporary.ForceMeshUpdate();
                Assert.That(hud.TextTemporary.GetRenderedValues().y,
                    Is.LessThanOrEqualTo(hud.TextTemporary.rectTransform.rect.height + 0.1f),
                    "长英文换行后也必须留在背景内");
            }
            finally { Object.DestroyImmediate(instance); }
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

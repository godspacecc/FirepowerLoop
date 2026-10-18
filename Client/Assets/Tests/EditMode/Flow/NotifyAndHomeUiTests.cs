using System.Collections.Generic;
using System;
using Boot;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class NotifyAndHomeUiTests
    {
        private FakeDisplayResolutionApplier displayApplier;
        private FakeSettingsFileStore settingsStore;
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            TimerMgr.Instance.ClearForTests();
            GameObjectPoolHelper.ClearForTests();
            ResourceLoader.SetLoaderForTests(new LocalizationTestsLoader());
            LocalizationLanguages.ResetForTests();
            tempRoot = Path.Combine(Path.GetTempPath(), "FirepowerLoopNotifyHomeUiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            displayApplier = new FakeDisplayResolutionApplier();
            settingsStore = new FakeSettingsFileStore();
            Architecture<Game>.OnRegisterPatch = _ => { };
            CreateGameArchitectureForTests();
            UIHelper.SetPrefabLoaderForTests(CreatePrefab);
            CreateTestUIRoot();
            UIRoot.EnsureInstance();
            typeof(SettingUICtrl).GetField("lastPage", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, System.Activator.CreateInstance(
                    typeof(SettingUICtrl).GetNestedType("Page", BindingFlags.NonPublic)));
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            TimerMgr.Instance.ClearForTests();
            GameObjectPoolHelper.ClearForTests();
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            QuitGameCommand.SetQuitHandlerForTests(null);
            DestroySceneObject("UIRoot");
            DestroySceneObject("EventSystem");
            DestroySceneObject(SceneRoot.PoolRootName);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void NotifyMessage_DefaultsToConfirmButton()
        {
            Notify.ShowMessage("标题", "内容");

            var ctrl = UIHelper.GetUICtrl<MessageUICtrl>();
            var ui = GetBaseUI<MessageUI>(ctrl);

            Assert.That(ui.TitleText.text, Is.EqualTo("标题"));
            Assert.That(ui.ContentText.text, Is.EqualTo("内容"));
            Assert.That(ui.ButtonRoot.GetComponentsInChildren<MessageButtonBoxUI>(false).Length, Is.EqualTo(1));
            Assert.That(ui.ButtonRoot.GetComponentInChildren<MessageButtonBoxUI>(false).ButtonText.text, Is.EqualTo("确认"));
        }

        [Test]
        public void NotifyMessage_BackgroundDoesNotCloseByDefault()
        {
            Notify.ShowMessage("标题", "内容");
            var ctrl = UIHelper.GetUICtrl<MessageUICtrl>();
            var ui = GetBaseUI<MessageUI>(ctrl);

            ui.BackgroundButton.onClick.Invoke();

            Assert.That(ctrl.IsShow(), Is.True);
        }

        [Test]
        public void NotifyInput_UsesDefaultLimitValidationAndClearsState()
        {
            var submitted = new List<string>();
            Notify.ShowInput("命名", "存档 1", 20, value =>
            {
                submitted.Add(value);
                return string.IsNullOrEmpty(value) ? "名称不能为空" : string.Empty;
            });
            var ctrl = UIHelper.GetUICtrl<MessageUICtrl>();
            var ui = GetBaseUI<MessageUI>(ctrl);

            Assert.That(ui.TitleText.text, Is.EqualTo("命名"));
            Assert.That(ui.ContentText.gameObject.activeSelf, Is.False);
            Assert.That(ui.InputField.gameObject.activeSelf, Is.True);
            Assert.That(ui.InputErrorText.gameObject.activeSelf, Is.True);
            Assert.That(ui.InputField.text, Is.EqualTo("存档 1"));
            Assert.That(ui.InputField.characterLimit, Is.EqualTo(20));

            ui.InputField.text = "   ";
            FindButton(ui, Texts.CommonConfirm).BtnButton.onClick.Invoke();

            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(submitted.Single(), Is.EqualTo(string.Empty));
            Assert.That(ui.InputErrorText.text, Is.EqualTo("名称不能为空"));

            ui.InputField.text = "Hero";
            Assert.That(ui.InputErrorText.text, Is.Empty);
            FindButton(ui, Texts.CommonConfirm).BtnButton.onClick.Invoke();

            Assert.That(submitted.Last(), Is.EqualTo("Hero"));
            Assert.That(ctrl.IsShow(), Is.False);

            Notify.ShowMessage("普通", "内容");
            Assert.That(ui.ContentText.gameObject.activeSelf, Is.True);
            Assert.That(ui.InputField.gameObject.activeSelf, Is.False);
            Assert.That(ui.InputErrorText.gameObject.activeSelf, Is.False);
            Assert.That(ui.InputField.text, Is.Empty);
        }

        [Test]
        public void NotifyInput_CancelDoesNotSubmit()
        {
            var submitted = false;
            Notify.ShowInput("命名", "存档 1", 20, _ =>
            {
                submitted = true;
                return string.Empty;
            });
            var ctrl = UIHelper.GetUICtrl<MessageUICtrl>();
            var ui = GetBaseUI<MessageUI>(ctrl);

            FindButton(ui, Texts.CommonCancel).BtnButton.onClick.Invoke();

            Assert.That(submitted, Is.False);
            Assert.That(ctrl.IsShow(), Is.False);
        }

        [Test]
        public void NotifyFloat_CreatesTipAndTimerRemovesIt()
        {
            Notify.ShowFloatTips("框架已就绪", 0.1f);
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var ui = GetBaseUI<FloatUI>(ctrl);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(1));

            TimerMgr.Instance.TickForTests(0.2f);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(0));
        }

        [Test]
        public void NotifyFloat_AmmoTipIsUniqueWhileVisibleAndCanReturnAfterExpiry()
        {
            void Ammo(string text, float lifetime)
            {
                Notify.ShowFloatTips(text, lifetime, uniqueKey: "combat.ammo_insufficient");
            }
            Ammo("弹药不足", .5f);
            for (var i = 0; i < 30; i++) Ammo("弹药不足", 10f);
            Ammo("不同武器或语言的缺弹文字", 10f);
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var ui = GetBaseUI<FloatUI>(ctrl);
            TimerMgr.Instance.TickForTests(.31f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Has.Length.EqualTo(1));
            TimerMgr.Instance.TickForTests(.21f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Is.Empty,
                "重复请求不能延长原提示，也不能留下排队的缺弹提示");
            Ammo("再次缺弹", 5f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Has.Length.EqualTo(1));
            ctrl.Hide();
            Ammo("隐藏后再次缺弹", 5f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Single().TextTips.text,
                Is.EqualTo("隐藏后再次缺弹"));
        }

        [Test]
        public void NotifyFloat_AmmoTipIsUniqueWhileQueuedAndOtherTipsStillRepeat()
        {
            Notify.ShowFloatTips("其他提示", 5f);
            for (var i = 0; i < 30; i++)
            {
                Notify.ShowFloatTips("弹药不足", 5f, uniqueKey: "combat.ammo_insufficient");
            }
            Notify.ShowFloatTips("其他提示", 5f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());
            for (var i = 0; i < 4; i++) TimerMgr.Instance.TickForTests(.31f);
            var texts = ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(box => box.TextTips.text).ToArray();
            Assert.That(texts.Count(text => text == "弹药不足"), Is.EqualTo(1));
            Assert.That(texts.Count(text => text == "其他提示"), Is.EqualTo(2));
        }

        [Test]
        public void NotifyFloat_FormalPrefabDisplaysTextThroughNotify()
        {
            UIHelper.SetPrefabLoaderForTests(path =>
                path == "Prefabs/UI/Notify/Float"
                    ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Notify/Float.prefab")
                    : CreatePrefab(path));

            Notify.ShowFloatTips("正式模板提示", 5f);

            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());
            var visible = ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false);
            Assert.That(visible.Length, Is.EqualTo(1));
            Assert.That(visible[0].TextTips.text, Is.EqualTo("正式模板提示"));
        }

        [Test]
        public void NotifyFloat_RecyclesExpiredTipIntoNamedPool()
        {
            Notify.ShowFloatTips("框架已就绪", 0.1f);
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var ui = GetBaseUI<FloatUI>(ctrl);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(1));

            TimerMgr.Instance.TickForTests(0.2f);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(0));
            Assert.That(SceneRoot.PoolRoot.GetComponentsInChildren<PoolObject>(true).Length, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void NotifyFloat_KeepsAtMostFiveVisibleTips()
        {
            for (var i = 0; i < 7; i++)
            {
                Notify.ShowFloatTips($"提示{i}", 5f);
            }

            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var ui = GetBaseUI<FloatUI>(ctrl);
            TickQueueOpenSlots(4);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(5));

            TimerMgr.Instance.TickForTests(0.4f);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(5));
        }

        [Test]
        public void NotifyFloat_RevealsQueuedTipsAfterVisibleTipsExpire()
        {
            for (var i = 0; i < 6; i++)
            {
                Notify.ShowFloatTips($"提示{i}", 2f);
            }

            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var ui = GetBaseUI<FloatUI>(ctrl);
            TickQueueOpenSlots(4);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(5));

            TimerMgr.Instance.TickForTests(2.1f);

            var visible = ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false);
            Assert.That(visible.Length, Is.EqualTo(1));
            Assert.That(visible[0].TextTips.text, Is.EqualTo("提示5"));
        }

        [Test]
        public void NotifyFloat_ReusesExpiredTipFromItsOwnCache()
        {
            Notify.ShowFloatTips("第一条", 0.1f);
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var ui = GetBaseUI<FloatUI>(ctrl);
            var first = ui.TipsRoot.GetComponentInChildren<FloatTextBoxUI>(false);

            TimerMgr.Instance.TickForTests(0.2f);
            Notify.ShowFloatTips("第二条", 0.1f);
            var second = ui.TipsRoot.GetComponentInChildren<FloatTextBoxUI>(false);

            Assert.That(second, Is.SameAs(first));
            Assert.That(second.TextTips.text, Is.EqualTo("第二条"));
        }

        [Test]
        public void NotifyFloat_IgnoresBlankTips()
        {
            Notify.ShowFloatTips(" ");
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            Assert.That(ctrl, Is.Null);
        }

        /// <summary>连续领取应更新真实累计值，并从最新领取时刻计算消失时间。</summary>
        [Test]
        public void NotifyFloat_PickupReplacementUpdatesVisibleAndQueuedValues()
        {
            Notify.ShowFloatTips("积分 +100", .5f, "arcade.pickup.1", true);
            Notify.ShowFloatTips("金币 +2", .5f, "arcade.pickup.2", true);
            Notify.ShowFloatTips("金币 +5", .5f, "arcade.pickup.2", true);
            TimerMgr.Instance.TickForTests(.2f);
            Notify.ShowFloatTips("积分 +300", .5f, "arcade.pickup.1", true);
            TimerMgr.Instance.TickForTests(.11f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(x => x.TextTips.text),
                Is.EquivalentTo(new[] { "积分 +300", "金币 +5" }));
            TimerMgr.Instance.TickForTests(.2f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(2));
            TimerMgr.Instance.TickForTests(.4f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Is.Empty);
        }

        [Test]
        public void NotifyFloat_MapCleanupRemovesOnlyPickupFeedbackAndItsQueue()
        {
            Notify.ShowFloatTips("积分 +100", 2, "arcade.pickup.1", true);
            Notify.ShowFloatTips("获得散射", 2, "arcade.pickup.2", true);
            Notify.ShowFloatTips("其他通知", 2, "other");
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            ctrl.RemoveTipsByPrefix("arcade.pickup.");
            ctrl.RemoveTipsByPrefix("arcade.pickup.");
            TimerMgr.Instance.TickForTests(.4f);
            var ui = GetBaseUI<FloatUI>(ctrl);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(x => x.TextTips.text),
                Is.EqualTo(new[] { "其他通知" }));
        }

        [Test]
        public void NotifyFloat_HideCancelsTimersAndClearsVisibleTips()
        {
            Notify.ShowFloatTips("会被清理", 0.1f);
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var ui = GetBaseUI<FloatUI>(ctrl);

            ctrl.Hide();
            TimerMgr.Instance.TickForTests(0.2f);

            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Is.Empty);
        }

        [Test]
        public void NotifyFloat_NonPositiveLifetimeUsesTwoSecondDefault()
        {
            Notify.ShowFloatTips("默认寿命", 0f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());

            TimerMgr.Instance.TickForTests(1.9f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(1));

            TimerMgr.Instance.TickForTests(0.2f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Is.Empty);
        }

        [Test]
        public void NotifyFloat_QueuesFollowingTipsForIntervalAndKeepsFifoOrder()
        {
            Notify.ShowFloatTips("第一条", 5f);
            Notify.ShowFloatTips("第二条", 5f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());

            TimerMgr.Instance.TickForTests(0.29f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(item => item.TextTips.text),
                Is.EqualTo(new[] { "第一条" }));

            TimerMgr.Instance.TickForTests(0.02f);
            var visible = ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).ToArray();
            Assert.That(visible.Select(item => item.TextTips.text),
                Is.EqualTo(new[] { "第一条", "第二条" }));
            Assert.That(visible[0].transform.localPosition.y - visible[1].transform.localPosition.y,
                Is.GreaterThanOrEqualTo(visible[0].GetComponent<RectTransform>().rect.height));
            var firstPosition = visible[0].transform.localPosition;
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.TipsRoot);
            Assert.That(visible[0].transform.localPosition, Is.EqualTo(firstPosition));
        }

        [Test]
        public void NotifyFloat_PausedClockStillDisplaysAndExpiresQueuedTips()
        {
            Notify.ShowFloatTips("第一条", 0.5f);
            Notify.ShowFloatTips("第二条", 0.5f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());

            TickPausedClock(0.31f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Length, Is.EqualTo(2));
            TickPausedClock(0.2f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(item => item.TextTips.text),
                Is.EqualTo(new[] { "第二条" }));
            TickPausedClock(0.31f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Is.Empty);
        }

        private static void TickPausedClock(float unscaledDeltaTime)
        {
            TimerMgr.Instance.TickForTests(0f, unscaledDeltaTime);
        }

        [Test]
        public void NotifyFloat_FormalPickupStaysBelowArenaHudAndAboveBattleCenter()
        {
            // 用单个完整 testMethod 覆盖两个比例，兼容 MCP 对参数化方法名的精确过滤。
            foreach (var size in new[] { new Vector2(1920, 1080), new Vector2(2028, 1080) })
                VerifyFormalPickupLayout(size.x, size.y);
        }

        private void VerifyFormalPickupLayout(float width, float height)
        {
            UIHelper.GetUICtrl<FloatUICtrl>()?.RemoveTipsByPrefix("arcade.pickup.");
            UIHelper.SetPrefabLoaderForTests(path => path == "Prefabs/UI/Notify/Float"
                ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Notify/Float.prefab")
                : CreatePrefab(path));
            Notify.ShowFloatTips("积分 +300", 5f, "arcade.pickup.layout", true);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());
            // 只设置测试画布的参考尺寸，覆盖标准横屏与已发现重叠的小尺寸横屏比例。
            var layer = (RectTransform)ui.transform.parent;
            layer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            layer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            var hud = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab"), ui.transform.parent).GetComponent<ArcadeHudUI>();
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Notify.ShowFloatTips("其他奖励 " + i, 5f, "arcade.pickup.layout." + i, true);
                    TimerMgr.Instance.TickForTests(.31f);
                }
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(ui.TipsRoot);
                var boxes = ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false);
                Assert.That(boxes.Length, Is.EqualTo(5));
                var hudCorners = new Vector3[4];
                hud.TextWave.rectTransform.GetWorldCorners(hudCorners);
                var center = ((RectTransform)ui.transform).TransformPoint(((RectTransform)ui.transform).rect.center);
                foreach (var box in boxes)
                {
                    var corners = new Vector3[4];
                    ((RectTransform)box.transform).GetWorldCorners(corners);
                    Assert.That(corners[2].y, Is.LessThan(hudCorners[0].y), "拾取提示不能覆盖顶部波次 HUD");
                    Assert.That(corners[0].y, Is.GreaterThan(center.y), "密集提示不能延伸至玩家所在的战斗中心");
                    Assert.That(box.TextTips.raycastTarget, Is.False);
                }
            }
            finally { Object.DestroyImmediate(hud.gameObject); }
        }

        [Test]
        public void NotifyFloat_FormalLayoutDoesNotJumpOnRepeatedRequest()
        {
            UIHelper.SetPrefabLoaderForTests(path => path == "Prefabs/UI/Notify/Float"
                ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Notify/Float.prefab")
                : CreatePrefab(path));
            Notify.ShowFloatTips("第一条", 5f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());
            var first = ui.TipsRoot.GetComponentInChildren<FloatTextBoxUI>(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.TipsRoot);
            var position = first.transform.localPosition;

            Notify.ShowFloatTips("第二条", 5f);
            Assert.That(first.transform.localPosition, Is.EqualTo(position));
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.TipsRoot);
            Assert.That(first.transform.localPosition, Is.EqualTo(position));
        }

        [Test]
        public void NotifyFloat_ExpiresVisibleTipsByTheirOwnLifetime()
        {
            Notify.ShowFloatTips("短寿命", 0.5f);
            Notify.ShowFloatTips("长寿命", 1f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());

            TimerMgr.Instance.TickForTests(0.31f);
            TimerMgr.Instance.TickForTests(0.2f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(item => item.TextTips.text),
                Is.EqualTo(new[] { "长寿命" }));

            TimerMgr.Instance.TickForTests(0.8f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false), Is.Empty);
        }

        [Test]
        public void NotifyFloat_HideIsIdempotentAndReuseResetsPriorStateAndTimer()
        {
            Notify.ShowFloatTips("旧提示", 0.1f);
            var ui = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var first = ui.TipsRoot.GetComponentInChildren<FloatTextBoxUI>(false);
            first.transform.localPosition = new Vector3(99f, 99f, 0f);

            Assert.DoesNotThrow(() =>
            {
                ctrl.Hide();
                ctrl.Hide();
            });
            Assert.That(first.gameObject.activeSelf, Is.False);
            Assert.That(first.TextTips.text, Is.Empty);
            Assert.That(first.GetComponent<PoolObject>().IsPooled, Is.True);

            Notify.ShowFloatTips("新提示", 5f);
            var reused = ui.TipsRoot.GetComponentInChildren<FloatTextBoxUI>(false);
            Assert.That(reused, Is.SameAs(first));
            Assert.That(reused.gameObject.activeSelf, Is.True);
            Assert.That(reused.TextTips.text, Is.EqualTo("新提示"));
            var position = reused.transform.localPosition;
            LayoutRebuilder.ForceRebuildLayoutImmediate(ui.TipsRoot);
            Assert.That(reused.transform.localPosition, Is.EqualTo(position));

            TimerMgr.Instance.TickForTests(0.2f);
            Assert.That(ui.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(item => item.TextTips.text),
                Is.EqualTo(new[] { "新提示" }));
        }

        [Test]
        public void NotifyFloat_DestroyedViewReloadsWithoutStaleVisibleTips()
        {
            var baselineTimerCount = GetActiveTimerCount();
            Notify.ShowFloatTips("销毁前", 5f);
            Notify.ShowFloatTips("等待间隔", 5f);
            var ctrl = UIHelper.GetUICtrl<FloatUICtrl>();
            var oldUi = GetBaseUI<FloatUI>(ctrl);
            Assert.That(oldUi.GetComponent<FloatViewDestroyWatcher>(), Is.Not.Null);
            Assert.That(GetActiveTimerCount(), Is.GreaterThanOrEqualTo(baselineTimerCount + 2));

            Object.DestroyImmediate(oldUi.gameObject);

            ctrl.Show();
            ctrl.SetTips("新实例", 10f);
            var newUi = GetBaseUI<FloatUI>(ctrl);
            Assert.That(newUi, Is.Not.SameAs(oldUi));
            Assert.That(newUi.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(item => item.TextTips.text),
                Is.EqualTo(new[] { "新实例" }));

            TimerMgr.Instance.TickForTests(5.1f);
            Assert.That(newUi.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Select(item => item.TextTips.text),
                Is.EqualTo(new[] { "新实例" }));
        }

        private static int GetActiveTimerCount()
        {
            var timersField = typeof(TimerMgr).GetField("_timers", BindingFlags.Instance | BindingFlags.NonPublic);
            var timers = timersField?.GetValue(TimerMgr.Instance) as System.Collections.IDictionary;
            Assert.That(timers, Is.Not.Null);
            return timers.Count;
        }

        [Test]
        public void Notify_ReentrantCallbacksAndInputRoles_UseStableIdentity()
        {
            Notify.ShowMessage("旧标题", "旧内容", new List<MessageButtonConfig>
            {
                new MessageButtonConfig
                {
                    ButtonText = "打开新消息",
                    OnClick = () => Notify.ShowMessage("新标题", "新内容")
                }
            });
            var message = UIHelper.GetUICtrl<MessageUICtrl>();
            var messageUi = GetBaseUI<MessageUI>(message);

            FindButton(messageUi, "打开新消息").BtnButton.onClick.Invoke();

            Assert.That(message.IsShow(), Is.True);
            Assert.That(messageUi.TitleText.text, Is.EqualTo("新标题"));
            Assert.That(messageUi.ContentText.text, Is.EqualTo("新内容"));

            Notify.ShowMessage("普通", "内容", new List<MessageButtonConfig>
            {
                new MessageButtonConfig { ButtonText = "关闭" }
            });
            FindButton(messageUi, "关闭").BtnButton.onClick.Invoke();
            Assert.That(message.IsShow(), Is.False);

            var submitted = false;
            Notify.ShowInput("输入", "默认", 20, _ =>
            {
                submitted = true;
                return string.Empty;
            });
            messageUi = GetBaseUI<MessageUI>(message);
            FindButton(messageUi, Texts.CommonCancel).ButtonText.text = "别的显示文字";
            FindButton(messageUi, "别的显示文字").BtnButton.onClick.Invoke();
            Assert.That(submitted, Is.False);
            Assert.That(message.IsShow(), Is.False);

            Notify.ShowInput("校验", "默认", 20, _ => "错误");
            messageUi = GetBaseUI<MessageUI>(message);
            FindButton(messageUi, Texts.CommonConfirm).BtnButton.onClick.Invoke();
            Assert.That(message.IsShow(), Is.True);
            Assert.That(messageUi.TitleText.text, Is.EqualTo("校验"));
            Assert.That(messageUi.InputErrorText.text, Is.EqualTo("错误"));

            Notify.ShowInput("提交", "默认", 20, _ =>
            {
                Notify.ShowMessage("提交后标题", "提交后内容");
                return string.Empty;
            });
            messageUi = GetBaseUI<MessageUI>(message);
            FindButton(messageUi, Texts.CommonConfirm).BtnButton.onClick.Invoke();
            Assert.That(message.IsShow(), Is.True);
            Assert.That(messageUi.TitleText.text, Is.EqualTo("提交后标题"));
            Assert.That(messageUi.ContentText.text, Is.EqualTo("提交后内容"));
        }

        [Test]
        public void Start_OpensSettingAndRefreshesLanguage()
        {
            var start = UIHelper.ShowUI<StartUICtrl>();
            var startUi = GetBaseUI<StartUI>(start);

            Assert.That(FindText(startUi.transform, "Panel/TitleText").text, Is.EqualTo("开始"));
            startUi.BtnSetting.onClick.Invoke();

            Assert.IsTrue(UIHelper.GetUICtrl<SettingUICtrl>().IsShow());

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });

            Assert.That(FindText(startUi.transform, "Panel/TitleText").text, Is.EqualTo("Start"));
        }

        [Test]
        public void Start_NewGameAndContinueUseFixedBaseDomain()
        {
            var start = UIHelper.ShowUI<StartUICtrl>();
            var startUi = GetBaseUI<StartUI>(start);

            Assert.IsFalse(startUi.BtnContinue.gameObject.activeSelf);
            Assert.IsTrue(startUi.BtnNewGame.gameObject.activeSelf);
            Assert.IsFalse(startUi.BtnContinue.gameObject.activeSelf);
        }

        [Test]
        public void Setting_DynamicTexts_AreFullyLocalized()
        {
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var settingUi = GetBaseUI<SettingUI>(setting);
            var rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);

            Assert.That(rows.Length, Is.EqualTo(3));
            Assert.That(rows[0].TextSetting.text, Is.EqualTo("语言"));
            Assert.That(rows[0].ValueDropdown.captionText.text, Is.EqualTo("简体中文"));
            Assert.That(rows[0].ValueDropdown.options.Select(option => option.text), Is.EqualTo(new[] { "简体中文", "英语" }));
            SelectSettingOption(rows[0], "英语");

            Assert.That(Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value, Is.EqualTo("en-US"));
            rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            Assert.That(rows[0].TextSetting.text, Is.EqualTo("Language"));
            Assert.That(rows[0].ValueDropdown.captionText.text, Is.EqualTo("English"));
            Assert.That(Texts.SettingsFollowDesktop, Is.EqualTo("Follow Desktop"));

            Assert.That(rows[0].ValueDropdown.options.Select(option => option.text), Is.EqualTo(new[] { "Simplified Chinese", "English" }));

            settingUi.ControlsTabButton.onClick.Invoke();
            Game.Interface.SendEvent(new HotKeyRebindCapturedEvent(
                HotKeyRebindResult.Conflict(HotKeyActionIds.ToggleRuntimeLog)));
            var messageUi = GetBaseUI<MessageUI>(UIHelper.GetUICtrl<MessageUICtrl>());
            Assert.That(messageUi.ContentText.text, Is.EqualTo("This key is already used by: Runtime Log"));
            Assert.That(messageUi.ContentText.text, Does.Not.Contain(HotKeyActionIds.ToggleRuntimeLog));

            Game.Interface.SendEvent(new HotKeyRebindCapturedEvent(
                HotKeyRebindResult.Conflict("diagnostic.unmapped")));
            messageUi = GetBaseUI<MessageUI>(UIHelper.GetUICtrl<MessageUICtrl>());
            Assert.That(messageUi.ContentText.text, Is.EqualTo("This key is already used by: Unknown Action"));
            Assert.That(messageUi.ContentText.text, Does.Not.Contain("diagnostic.unmapped"));

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "zh-CN" });
            Assert.That(Texts.SettingsFollowDesktop, Is.EqualTo("跟随桌面"));
            Game.Interface.SendEvent(new HotKeyRebindCapturedEvent(
                HotKeyRebindResult.Conflict(HotKeyActionIds.TogglePause)));
            messageUi = GetBaseUI<MessageUI>(UIHelper.GetUICtrl<MessageUICtrl>());
            Assert.That(messageUi.ContentText.text, Is.EqualTo("该按键已用于：暂停 / 继续"));
            Assert.That(messageUi.ContentText.text, Does.Not.Contain(HotKeyActionIds.TogglePause));

            Game.Interface.SendEvent(new HotKeyRebindCapturedEvent(
                HotKeyRebindResult.Conflict("legacy.internal.action")));
            messageUi = GetBaseUI<MessageUI>(UIHelper.GetUICtrl<MessageUICtrl>());
            Assert.That(messageUi.ContentText.text, Is.EqualTo("该按键已用于：未知动作"));
            Assert.That(messageUi.ContentText.text, Does.Not.Contain("legacy.internal.action"));
        }

        [Test]
        public void Setting_DisplaySettingsApplyImmediatelyAndCloseNeedsNoSave()
        {
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var settingUi = GetBaseUI<SettingUI>(setting);
            var rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);

            SelectSettingOption(rows[1], "1920x1080");
            SelectSettingOption(rows[2], "无边框全屏");

            Assert.That(displayApplier.LastPreset.Code, Is.EqualTo("1920x1080"));
            Assert.That(displayApplier.LastWindowMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, Is.EqualTo("1920x1080"));

            settingUi.BtnExit.onClick.Invoke();

            Assert.That(setting.IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [Test]
        public void Setting_FullscreenWindowDisablesResolutionAndRestoresWindowedSelection()
        {
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var settingUi = GetBaseUI<SettingUI>(setting);
            var rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);

            SelectSettingOption(rows[1], "1920x1080");
            rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            Assert.That(rows[1].ValueDropdown.captionText.text, Is.EqualTo("1920x1080"));

            SelectSettingOption(rows[2], "无边框全屏");
            rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);

            Assert.That(rows.Length, Is.EqualTo(3));
            Assert.That(rows[1].TextSetting.text, Is.EqualTo(Texts.SettingsWindowSize));
            Assert.That(rows[1].ValueDropdown.captionText.text, Is.EqualTo(Texts.SettingsFollowDesktop));
            Assert.That(rows[1].ValueDropdown.interactable, Is.False);
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);

            rows[1].ValueDropdown.Show();
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            Assert.That(rows[1].ValueDropdown.captionText.text, Is.EqualTo("Follow Desktop"));

            SelectSettingOption(rows[2], "Windowed");
            rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);

            Assert.That(rows[1].ValueDropdown.interactable, Is.True);
            Assert.That(rows[1].ValueDropdown.captionText.text, Is.EqualTo("1920x1080"));

            Assert.That(displayApplier.LastPreset.Code, Is.EqualTo("1920x1080"));
            Assert.That(displayApplier.LastWindowMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, Is.EqualTo("1920x1080"));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentWindowMode.Value, Is.EqualTo(FullScreenMode.Windowed));
            // 显示立即生效，落盘由设置系统延迟合并；检查文件前显式完成待保存内容。
            Assert.That(Game.Interface.GetSystem<SettingsSystem>().Flush(), Is.True);
            Assert.That(settingsStore.LastSaved.Display.ResolutionCode, Is.EqualTo("1920x1080"));
            Assert.That(settingsStore.LastSaved.Display.WindowMode, Is.EqualTo(FullScreenMode.Windowed));
        }

        [Test]
        public void Setting_CloseAndPerPageReset_FollowImmediateSemantics()
        {
            OpenFreshSetting(out var currentSetting, out var currentUi, out var currentRows);
            SelectSettingOption(currentRows[1], "1920x1080");
            Assert.That(displayApplier.LastPreset.Code, Is.EqualTo("1920x1080"));

            General(currentUi).ResetButton.onClick.Invoke();
            var defaults = SettingsData.CreateDefault();
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value,
                Is.EqualTo(defaults.Display.ResolutionCode));
            Assert.That(currentSetting.IsShow(), Is.True);

            currentUi.AudioTabButton.onClick.Invoke();
            Audio(currentUi).MasterVolumeSlider.value = 0.4f;
            Assert.That(Game.Interface.GetModel<AudioSettingsModel>().MasterVolume.Value, Is.EqualTo(0.4f));

            currentUi.BtnExit.onClick.Invoke();
            Assert.That(currentSetting.IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Setting_DropdownClosesWithoutSelectionAndWithoutChangingSettings(int rowIndex)
        {
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var settingUi = GetBaseUI<SettingUI>(setting);
            var rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            var languageBefore = Game.Interface.GetModel<LocalizationModel>()
                .CurrentLanguageCode.Value;
            var resolutionBefore = Game.Interface.GetModel<DisplaySettingsModel>()
                .CurrentResolutionCode.Value;
            var windowModeBefore = Game.Interface.GetModel<DisplaySettingsModel>()
                .CurrentWindowMode.Value;

            var dropdown = rows[rowIndex].ValueDropdown;
            SettingsDropdownTestSupport.Start(dropdown); dropdown.Show();
            Assert.That(dropdown.IsExpanded, Is.True);
            SettingsDropdownTestSupport.ExpectNativeEditModeClose();
            settingUi.AudioTabButton.onClick.Invoke();
            Assert.That(dropdown.IsExpanded, Is.False);
            Assert.That(setting.IsShow(), Is.True);
            Assert.That(Game.Interface.GetModel<LocalizationModel>()
                .CurrentLanguageCode.Value, Is.EqualTo(languageBefore));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>()
                .CurrentResolutionCode.Value, Is.EqualTo(resolutionBefore));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>()
                .CurrentWindowMode.Value, Is.EqualTo(windowModeBefore));
        }

        [Test]
        public void Setting_CloseAfterImmediateDisplayChange_DoesNotOpenConfirmMessage()
        {
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var settingUi = GetBaseUI<SettingUI>(setting);
            var rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);

            SelectSettingOption(rows[1], "1280x720");
            settingUi.BtnExit.onClick.Invoke();

            Assert.That(setting.IsShow(), Is.False);
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [Test]
        public void Setting_CloseCancelsPendingHotKeyRebind()
        {
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var settingUi = GetBaseUI<SettingUI>(setting);
            settingUi.ControlsTabButton.onClick.Invoke();
            var hotKeyRow = Controls(settingUi).HotKeyList.GetComponentsInChildren<HotKeyItemBoxUI>(false).First();
            var hotKeys = Game.Interface.GetSystem<HotKeySystem>();

            hotKeyRow.RebindButton.onClick.Invoke();
            Assert.IsTrue(hotKeys.IsCapturingRebind);

            settingUi.BtnExit.onClick.Invoke();

            Assert.IsFalse(setting.IsShow());
            Assert.IsFalse(hotKeys.IsCapturingRebind);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Setting_MobileGeneralActionOpensEditorAndReturnsToPopulatedGeneralPage(bool save)
        {
            ConfigureControlLayoutEntry(true);
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var ui = GetBaseUI<SettingUI>(setting);
            Assert.That(ui.ControlsTabButton.gameObject.activeSelf, Is.False, "手机操作入口不是左侧分页");
            Assert.That(ui.SettingGeneralRoot.activeSelf, Is.True);
            var rows = General(ui).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            Assert.That(rows.Select(row => row.TextSetting.text), Is.EqualTo(new[] { "语言", "帧率上限", "操作" }));
            Assert.That(rows[2].TextContext.text, Is.EqualTo("自定义操控布局"));
            rows[2].BtnContext.onClick.Invoke();

            var editor = UIHelper.GetUICtrl<MobileControlLayoutUICtrl>();
            Assert.That(editor, Is.Not.Null, "通用页操作按钮直接打开全屏编辑器");
            Assert.That(editor.IsShow(), Is.True);
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(editor));
            Assert.That(ui.SettingMobileRoot.activeSelf, Is.False, "不再显示二次入口页");
            var editorUi = GetBaseUI<MobileControlLayoutUI>(editor);
            editorUi.SizeSlider.value = 1.4f;
            if (save) editorUi.SaveButton.onClick.Invoke();
            else editorUi.CancelButton.onClick.Invoke();

            Assert.That(editor.IsShow(), Is.False);
            Assert.That(setting.IsShow(), Is.True);
            Assert.That(UIHelper.GetTopmostBlockingUI(), Is.SameAs(setting));
            Assert.That(ui.SettingGeneralRoot.activeSelf, Is.True, "保存或取消后通用页仍有内容");
            Assert.That(General(ui).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false), Has.Length.EqualTo(3));
            Assert.That(Game.Interface.GetModel<MobileControlLayoutModel>().ToData().Move.Scale,
                Is.EqualTo(save ? 1.4f : 1f));
            rows[2].BtnContext.onClick.Invoke();
            Assert.That(editor.IsShow(), Is.True);
            editorUi.CancelButton.onClick.Invoke();
            setting.Hide(); setting.Show();
            Assert.That(editor.IsShow(), Is.False, "重新打开设置不能自动进入编辑器");
            Assert.That(ui.SettingGeneralRoot.activeSelf, Is.True);
            Assert.That(General(ui).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false), Has.Length.EqualTo(3));
        }

        [Test]
        public void Setting_DesktopKeepsHotkeysAndSwitchingToMobileDoesNotOpenEditorAutomatically()
        {
            ConfigureControlLayoutEntry(false);
            var setting = UIHelper.ShowUI<SettingUICtrl>();
            var ui = GetBaseUI<SettingUI>(setting);
            ui.ControlsTabButton.onClick.Invoke();
            Assert.That(ui.SettingControlsRoot.activeSelf, Is.True);
            Assert.That(Controls(ui).HotKeyList.GetComponentsInChildren<HotKeyItemBoxUI>(false), Is.Not.Empty);
            Assert.That(ui.ControlsTabButton.gameObject.activeSelf, Is.True);
            Assert.That(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>(), Is.Null);

            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            Assert.That(ui.SettingControlsRoot.activeSelf, Is.False);
            Assert.That(ui.SettingMobileRoot.activeSelf, Is.False);
            Assert.That(ui.SettingGeneralRoot.activeSelf, Is.True);
            Assert.That(ui.ControlsTabButton.gameObject.activeSelf, Is.False);
            Assert.That(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>(), Is.Null);
            General(ui).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false)[2].BtnContext.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>().IsShow(), Is.True);
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(false);
            Assert.That(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>().IsShow(), Is.False);
            Assert.That(ui.ControlsTabButton.gameObject.activeSelf, Is.True);
            Assert.That(General(ui).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false)
                .Select(row => row.TextSetting.text), Is.EqualTo(new[] { "语言", "窗口大小", "窗口模式" }));
            ui.ControlsTabButton.onClick.Invoke();
            Assert.That(ui.SettingControlsRoot.activeSelf, Is.True);
        }

        /// <summary>设置宿主使用已有夹具，按键编辑器加载正式Prefab验证真实保存、取消与浮层路径。</summary>
        private static void ConfigureControlLayoutEntry(bool mobile)
        {
            var architecture = Game.Interface;
            var localization = architecture.GetUtility<LocalizationUtility>();
            localization.LoadFromJson("zh-CN", File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            localization.LoadFromJson("en-US", File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            architecture.GetSystem<MobileInputSystem>().TrySetEditorSimulation(mobile);
            UIHelper.SetPrefabLoaderForTests(path =>
            {
                if (path == "Prefabs/UI/Home/MobileControlLayout")
                    return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/MobileControlLayout.prefab");
                var root = CreatePrefab(path);
                if (path == "Prefabs/UI/Home/Setting")
                {
                    var mobileUi = root.GetComponent<SettingUI>().SettingMobileRoot.GetComponent<SettingMobilePageUI>();
                    mobileUi.OpenLayoutButton = CreateButton(mobileUi.transform, "OpenLayoutButton", out _);
                }
                return root;
            });
        }

        [Test]
        public void ControllerPaths_UseCollectorAddresses()
        {
            Assert.That(InvokePath(new StartUICtrl()), Is.EqualTo("Home/Start"));
            Assert.That(InvokePath(new SettingUICtrl()), Is.EqualTo("Home/Setting"));
            Assert.That(InvokePath(new MessageUICtrl()), Is.EqualTo("Notify/Message"));
            Assert.That(InvokePath(new FloatUICtrl()), Is.EqualTo("Notify/Float"));
        }

        [Test]
        public void FloatController_UsesEmbeddedTemplatePoolAndDoesNotAccessViewDuringUnload()
        {
            var source = File.ReadAllText("Assets/Game/Scripts/Controller/UICtrl/Notify/FloatUICtrl.cs");

            Assert.That(source, Does.Contain("NotifyFloatTip"));
            Assert.That(source, Does.Contain("GameObjectPoolHelper.RemovePool(PoolName);"));
            var unloadStart = source.IndexOf("protected override void OnUnload()", StringComparison.Ordinal);
            var unloadEnd = source.IndexOf("private void ClearForUnload", unloadStart, StringComparison.Ordinal);
            Assert.That(source.Substring(unloadStart, unloadEnd - unloadStart), Does.Not.Contain("UI"));
        }

        private static string InvokePath(BaseUICtrl ctrl)
        {
            return (string)ctrl.GetType()
                .GetMethod("Path", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(ctrl, null);
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static SettingGeneralPageUI General(SettingUI ui)
        {
            return ui.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>();
        }

        /// <summary>通过真实下拉组件提交候选值，避免继续模拟已移除的消息弹窗。</summary>
        private static void SelectSettingOption(SettingItemBoxUI row, string label)
        {
            var index = row.ValueDropdown.options.FindIndex(option => option.text == label);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), label);
            row.ValueDropdown.value = index;
        }

        private static SettingAudioPageUI Audio(SettingUI ui)
        {
            return ui.SettingAudioRoot.GetComponent<SettingAudioPageUI>();
        }

        private static SettingControlsPageUI Controls(SettingUI ui)
        {
            return ui.SettingControlsRoot.GetComponent<SettingControlsPageUI>();
        }

        private static void OpenFreshSetting(
            out SettingUICtrl setting,
            out SettingUI settingUi,
            out SettingItemBoxUI[] rows)
        {
            UIHelper.HideUI<MessageUICtrl>();
            UIHelper.HideUI<SettingUICtrl>();
            setting = UIHelper.ShowUI<SettingUICtrl>();
            settingUi = GetBaseUI<SettingUI>(setting);
            rows = General(settingUi).SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
        }

        private void ResetModelsToDefaultAndFlush()
        {
            settingsStore.SaveSucceeds = true;
            thisApplyDefaults();
            settingsStore.ResetSaveStats();
            displayApplier.Applied.Clear();

            void thisApplyDefaults()
            {
                var defaults = SettingsData.CreateDefault();
                Game.Interface.GetModel<LocalizationModel>().SetLanguageCode(defaults.LanguageCode);
                Game.Interface.GetModel<DisplaySettingsModel>().Apply(defaults.Display);
                Game.Interface.GetModel<AudioSettingsModel>().Apply(defaults.Audio);
                Game.Interface.GetModel<HotKeyModel>().Apply(defaults.HotKeys);
                Game.Interface.GetSystem<HotKeySystem>().RebuildActions();
                Assert.IsTrue(Game.Interface.GetSystem<SettingsSystem>().Flush());
            }
        }

        private static MessageButtonBoxUI FindButton(MessageUI ui, string label)
        {
            foreach (var button in ui.ButtonRoot.GetComponentsInChildren<MessageButtonBoxUI>(false))
            {
                if (button.ButtonText.text == label)
                {
                    return button;
                }
            }

            Assert.Fail($"Button not found: {label}");
            return null;
        }

        private static void AssertLanguageButtons(MessageUI ui, params string[] labels)
        {
            var actualLabels = ui.ButtonRoot.GetComponentsInChildren<MessageButtonBoxUI>(false)
                .Select(button => button.ButtonText.text)
                .ToArray();
            Assert.That(actualLabels, Is.EquivalentTo(labels));
            Assert.That(actualLabels.Length, Is.EqualTo(labels.Length));
        }

        private static GameObject CreatePrefab(string path)
        {
            switch (path)
            {
                case "Prefabs/UI/Notify/Message":
                    return CreateMessagePrefab();
                case "Prefabs/UI/Notify/Float":
                    return CreateFloatPrefab();
                case "Prefabs/UI/Home/Start":
                    return CreateStartPrefab();
                case "Prefabs/UI/Home/Setting":
                    return CreateSettingPrefab();
                default:
                    return null;
            }
        }

        private static void CreateTestUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.AddComponent<UIRoot>();
            CreateRect(root.transform, "HUD");
            CreateRect(root.transform, "Dialog");
            CreateRect(root.transform, "Popup");
            CreateRect(root.transform, "Tips");
            CreateRect(root.transform, "Top");

            var eventSystem = new GameObject("EventSystem", typeof(RectTransform));
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null)
            {
                eventSystem.AddComponent(inputModuleType);
            }

            new GameObject(SceneRoot.PoolRootName).transform.SetParent(null);
        }

        private static GameObject CreateMessagePrefab()
        {
            var root = CreateRoot("Message", out var ui);
            var messageUi = root.AddComponent<MessageUI>();
            messageUi.TitleText = CreateText(root.transform, "Title");
            messageUi.ContentText = CreateText(root.transform, "Content");
            messageUi.InputField = new GameObject("Input", typeof(RectTransform)).AddComponent<TMP_InputField>();
            messageUi.InputField.transform.SetParent(root.transform, false);
            messageUi.InputErrorText = CreateText(root.transform, "InputError");
            messageUi.BackgroundButton = CreateButton(root.transform, "Background", out _);
            var template = CreateButton<MessageButtonBoxUI>(root.transform, "ButtonTemplate", out var text);
            template.BtnButton = template.GetComponent<Button>();
            template.ButtonText = (TextMeshProUGUI)text;
            template.gameObject.SetActive(false);
            messageUi.ButtonRoot = CreateList(root.transform, "Buttons", template.gameObject);
            Object.DestroyImmediate(ui);
            return root;
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = CreateRoot("Float", out var ui);
            var floatUi = root.AddComponent<FloatUI>();
            floatUi.TipsRoot = CreateRect(root.transform, "Tips");
            floatUi.TipsRoot.sizeDelta = new Vector2(420f, 160f);
            var layout = floatUi.TipsRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 8f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            var template = new GameObject("TipTemplate", typeof(RectTransform)).AddComponent<FloatTextBoxUI>();
            template.transform.SetParent(floatUi.TipsRoot, false);
            template.GetComponent<RectTransform>().sizeDelta = new Vector2(420f, 44f);
            template.TextTips = (TextMeshProUGUI)CreateText(template.transform, "Text");
            template.gameObject.SetActive(false);
            floatUi.TipTemplate = template;
            Object.DestroyImmediate(ui);
            return root;
        }

        private static GameObject CreateStartPrefab()
        {
            var root = CreateRoot("Start", out var ui);
            var startUi = root.AddComponent<StartUI>();
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            var title = CreateText(panel.transform, "TitleText");
            title.gameObject.AddComponent<LocalizedText>().Key = "ui.start.title";
            startUi.VersionText = (TextMeshProUGUI)CreateText(root.transform, "Version");
            startUi.UpdateStatusText = (TextMeshProUGUI)CreateText(root.transform, "UpdateStatus");
            startUi.RunSummaryText = (TextMeshProUGUI)CreateText(panel.transform, "RunSummary");
            startUi.BtnNewGame = CreateButton(root.transform, "BtnNewGame", out _);
            startUi.BtnContinue = CreateButton(root.transform, "BtnContinue", out _);
            startUi.BtnOnline = CreateButton(root.transform, "BtnOnline", out _);
            startUi.BtnCheckUpdate = CreateButton(root.transform, "BtnCheckUpdate", out _);
            startUi.BtnSetting = CreateButton(root.transform, "BtnSetting", out _);
            startUi.BtnExit = CreateButton(root.transform, "BtnExit", out _);
            Object.DestroyImmediate(ui);
            return root;
        }

        private static GameObject CreateSettingPrefab()
        {
            var root = CreateRoot("Setting", out var ui);
            var settingUi = root.AddComponent<SettingUI>();

            settingUi.BtnExit = CreateButton(root.transform, "BtnExit", out _);
            settingUi.GeneralTabButton = CreateButton(root.transform, "GeneralTabButton", out _);
            settingUi.AudioTabButton = CreateButton(root.transform, "AudioTabButton", out _);
            settingUi.ControlsTabButton = CreateButton(root.transform, "ControlsTabButton", out _);
            settingUi.NetworkTabButton = CreateButton(root.transform, "NetworkTabButton", out _);

            var generalRoot = new GameObject("SettingGeneral", typeof(RectTransform));
            generalRoot.transform.SetParent(root.transform, false);
            var generalUi = generalRoot.AddComponent<SettingGeneralPageUI>();
            // 使用正式Box的原生下拉模板，夹具不再复制一套按钮式设置行。
            var templateGo = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Home/Box/SettingItemBox.prefab"), generalRoot.transform, false);
            templateGo.name = "SettingItemTemplate";
            templateGo.SetActive(false);
            generalUi.SettingList = CreateList(generalRoot.transform, "SettingList", templateGo);
            generalUi.ResetButton = CreateButton(generalRoot.transform, "ResetButton", out _);
            settingUi.SettingGeneralRoot = generalRoot;

            var audioRoot = new GameObject("SettingAudio", typeof(RectTransform));
            audioRoot.transform.SetParent(root.transform, false);
            var audioUi = audioRoot.AddComponent<SettingAudioPageUI>();
            audioUi.MasterVolumeSlider = CreateSlider(audioRoot.transform, "MasterVolume");
            audioUi.MusicVolumeSlider = CreateSlider(audioRoot.transform, "MusicVolume");
            audioUi.SoundVolumeSlider = CreateSlider(audioRoot.transform, "SoundVolume");
            audioUi.MasterVolumeValue = CreateText(audioRoot.transform, "MasterVolumeValue");
            audioUi.MusicVolumeValue = CreateText(audioRoot.transform, "MusicVolumeValue");
            audioUi.SoundVolumeValue = CreateText(audioRoot.transform, "SoundVolumeValue");
            audioUi.MuteThumb = new GameObject("MuteThumb", typeof(RectTransform)).GetComponent<RectTransform>();
            audioUi.MuteThumb.SetParent(audioRoot.transform, false);
            audioUi.MuteTrack = new GameObject("MuteTrack", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            audioUi.MuteTrack.transform.SetParent(audioRoot.transform, false);
            audioUi.MuteToggle = CreateToggle(audioRoot.transform, "Mute");
            audioUi.TestSoundButton = CreateButton(audioRoot.transform, "TestSound", out _);
            audioUi.ResetButton = CreateButton(audioRoot.transform, "ResetButton", out _);
            settingUi.SettingAudioRoot = audioRoot;

            var controlsRoot = new GameObject("SettingControls", typeof(RectTransform));
            controlsRoot.transform.SetParent(root.transform, false);
            var controlsUi = controlsRoot.AddComponent<SettingControlsPageUI>();
            var hotKeyTemplateGo = new GameObject("HotKeyItemTemplate", typeof(RectTransform));
            hotKeyTemplateGo.transform.SetParent(controlsRoot.transform, false);
            var hotKeyItem = hotKeyTemplateGo.AddComponent<HotKeyItemBoxUI>();
            hotKeyItem.ActionNameText = CreateText(hotKeyTemplateGo.transform, "ActionName");
            hotKeyItem.BindingText = CreateText(hotKeyTemplateGo.transform, "Binding");
            hotKeyItem.RebindButton = CreateButton(hotKeyTemplateGo.transform, "Rebind", out _);
            hotKeyTemplateGo.SetActive(false);
            controlsUi.HotKeyList = CreateList(controlsRoot.transform, "HotKeyList", hotKeyTemplateGo);
            controlsUi.ResetButton = CreateButton(controlsRoot.transform, "ResetButton", out _);
            settingUi.SettingControlsRoot = controlsRoot;

            // 本夹具只测试桌面设置；补齐当前宿主的手机页绑定，不初始化手机页业务。
            var mobileRoot = new GameObject("SettingMobile", typeof(RectTransform), typeof(SettingMobilePageUI));
            mobileRoot.transform.SetParent(root.transform, false);
            mobileRoot.SetActive(false);
            settingUi.SettingMobileRoot = mobileRoot;

            var networkRoot = new GameObject("SettingNetwork", typeof(RectTransform));
            networkRoot.transform.SetParent(root.transform, false);
            networkRoot.AddComponent<SettingNetworkPageUI>();
            settingUi.SettingNetworkRoot = networkRoot;

            generalRoot.SetActive(false);
            audioRoot.SetActive(false);
            controlsRoot.SetActive(false);
            networkRoot.SetActive(false);
            Object.DestroyImmediate(ui);
            return root;
        }

        private static GameObject CreateRoot(string name, out BaseUI ui)
        {
            var root = new GameObject(name, typeof(RectTransform));
            ui = root.AddComponent<BaseUI>();
            return root;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static CustomListView CreateList(Transform parent, string name, GameObject itemPrefab)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var list = go.AddComponent<CustomListView>();
            list.itemPrefab = itemPrefab;
            return list;
        }

        private static LoopVerticalScrollRect CreateLoopList(
            Transform parent,
            string name,
            GameObject itemPrefab,
            float width,
            float height,
            int poolSize)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content.transform.SetParent(viewport.transform, false);

            var list = root.AddComponent<LoopVerticalScrollRect>();
            list.viewport = viewportRect;
            list.content = (RectTransform)content.transform;
            list.horizontal = false;
            list.vertical = true;
            list.prefabSource.prefab = itemPrefab;
            list.prefabSource.poolSize = poolSize;
            return list;
        }

        private static T CreateButton<T>(Transform parent, string name, out TMP_Text text) where T : Component
        {
            var button = CreateButton(parent, name, out text);
            var go = button.gameObject;
            return go.AddComponent<T>();
        }

        private static Button CreateButton(Transform parent, string name, out TMP_Text text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            text = CreateText(go.transform, "Text");
            return go.GetComponent<Button>();
        }

        private static Slider CreateSlider(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Slider));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Slider>();
        }

        private static Toggle CreateToggle(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Toggle>();
        }

        private static TMP_Text FindText(Transform root, string path)
        {
            return root.Find(path).GetComponent<TMP_Text>();
        }

        private static void TickQueueOpenSlots(int count)
        {
            for (var i = 0; i < count; i++)
            {
                TimerMgr.Instance.TickForTests(0.31f);
            }
        }

        private static void DestroySceneObject(string name)
        {
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void ResetArchitecture()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, null);
        }

        private void CreateGameArchitectureForTests()
        {
            var architecture = new Game();
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
            typeof(Architecture<Game>)
                .GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(architecture, true);

            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterModel(new DisplaySettingsModel());
            architecture.RegisterModel(new AudioSettingsModel());
            architecture.RegisterModel(new HotKeyModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new RuntimeLogModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new ResourceUpdateModel());
            architecture.RegisterModel(new ArcadeRunModel());
            architecture.RegisterModel(new TestToolSettingsModel());
            architecture.RegisterModel(new MobileControlLayoutModel());
            architecture.RegisterUtility<IDisplayResolutionApplier>(displayApplier);
            architecture.RegisterUtility<ISettingsFileStore>(settingsStore);
            architecture.RegisterUtility(new LocalizationUtility());
            architecture.RegisterUtility(new HotKeyActionRegistry());
            architecture.RegisterUtility<IApplicationRestartUtility>(new FakeRestart());
            architecture.RegisterSystem(new SettingsSystem());
            architecture.RegisterSystem(new MapSceneSystem(null, null, null, null));
            architecture.RegisterSystem(new AppStateSystem());
            architecture.RegisterSystem(new RuntimeLogSystem());
            architecture.RegisterSystem(new AudioSystem());
            architecture.RegisterSystem(new HotKeySystem());
            architecture.RegisterSystem(new MobileInputSystem());
            architecture.RegisterSystem(new RedPointSystem());
            architecture.RegisterSystem(new ResourceUpdateSystem());
            architecture.RegisterSystem(new ArcadeRunSystem());
            architecture.GetSystem<ArcadeRunSystem>().ConfigureForTests(tempRoot);
            architecture.GetSystem<ResourceUpdateSystem>().InitializeStartupInfo(
                new BootResourceStartupInfo(BootResourceSource.BuiltinFallback, string.Empty, string.Empty, string.Empty));
        }

        private sealed class FakeDisplayResolutionApplier : IDisplayResolutionApplier
        {
            public List<string> Applied { get; } = new List<string>();
            public DisplayResolutionPreset LastPreset { get; private set; }
            public FullScreenMode LastWindowMode { get; private set; }

            public void Apply(DisplayResolutionPreset preset, FullScreenMode windowMode)
            {
                LastPreset = preset;
                LastWindowMode = windowMode;
                Applied.Add($"{preset.Code}:{windowMode}");
            }
        }

        private sealed class FakeSettingsFileStore : ISettingsFileStore
        {
            public bool SaveSucceeds { get; set; } = true;
            public int SaveCount { get; private set; }
            public SettingsData LastSaved { get; private set; }

            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                result = SettingsLoadResult.CreatedDefault;
                return SettingsData.CreateDefault();
            }

            public bool Save(SettingsData data, out string error)
            {
                SaveCount++;
                LastSaved = data?.Clone();
                error = SaveSucceeds ? null : "test save failure";
                return SaveSucceeds;
            }

            public void ResetSaveStats()
            {
                SaveCount = 0;
                LastSaved = null;
            }
        }

        private sealed class FakeRestart : IApplicationRestartUtility
        {
            public bool TryRestart(out string error)
            {
                error = string.Empty;
                return true;
            }
        }

        private sealed class LocalizationTestsLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                        return new TextAsset("{\"language.zh-CN.name\":\"简体中文\",\"language.en-US.name\":\"英语\",\"ui.hotkey.unknown\":\"未知动作\",\"ui.hotkey.toggle_pause\":\"暂停 / 继续\",\"ui.hotkey.toggle_runtime_log\":\"运行日志\",\"ui.settings.hot_key_conflict\":\"该按键已用于：{0}\",\"ui.settings.rebind\":\"修改\",\"ui.settings.press_hotkey\":\"请按下新的快捷键\",\"ui.common.swap\":\"交换\",\"ui.settings.error.rebind_failed\":\"修改快捷键失败\",\"ui.settings.error.save_failed\":\"设置保存失败，请重试\",\"ui.common.confirm\":\"确认\",\"ui.common.cancel\":\"取消\",\"ui.common.save\":\"保存\",\"ui.common.dont_save\":\"不保存\",\"ui.settings.language\":\"语言\",\"ui.settings.window_size\":\"窗口大小\",\"ui.settings.follow_desktop\":\"跟随桌面\",\"ui.settings.window_mode\":\"窗口模式\",\"ui.settings.select_language.title\":\"语言\",\"ui.settings.select_language.content\":\"选择界面语言\",\"ui.settings.select_window_size.title\":\"窗口大小\",\"ui.settings.select_window_size.content\":\"选择游戏窗口大小\",\"ui.settings.select_window_mode.title\":\"窗口模式\",\"ui.settings.select_window_mode.content\":\"选择游戏窗口显示模式\",\"ui.settings.windowed\":\"窗口\",\"ui.settings.fullscreen_window\":\"无边框全屏\",\"notify.unsaved.title\":\"未保存修改\",\"notify.unsaved.content\":\"你有未保存的设置修改，要保存吗？\",\"notify.settings_reset.title\":\"恢复默认设置\",\"notify.settings_reset.content\":\"确定要恢复全部默认设置吗？\",\"tip.framework_ready\":\"框架已就绪\",\"ui.start.title\":\"开始\",\"ui.start.new_game\":\"新游戏\",\"ui.save.create_new\":\"新建存档\",\"ui.save.create_new_summary\":\"创建新的游戏进度\",\"ui.save.create\":\"新建\",\"ui.save.rename\":\"重命名\",\"ui.save.delete\":\"删除\",\"ui.save.load\":\"加载\",\"ui.save.clear\":\"清空\",\"ui.save.title\":\"存档\",\"ui.save.name_error.duplicate\":\"存档名称已存在\",\"ui.save.corrupted_summary\":\"存档已损坏\"}") as T;
                    case "en-US.json":
                        return new TextAsset("{\"language.zh-CN.name\":\"Simplified Chinese\",\"language.en-US.name\":\"English\",\"ui.hotkey.unknown\":\"Unknown Action\",\"ui.hotkey.toggle_pause\":\"Pause / Resume\",\"ui.hotkey.toggle_runtime_log\":\"Runtime Log\",\"ui.settings.hot_key_conflict\":\"This key is already used by: {0}\",\"ui.settings.rebind\":\"Change\",\"ui.settings.press_hotkey\":\"Press a new shortcut.\",\"ui.common.swap\":\"Swap\",\"ui.settings.error.rebind_failed\":\"Failed to change the shortcut.\",\"ui.settings.error.save_failed\":\"Failed to save settings. Please try again.\",\"ui.common.confirm\":\"Confirm\",\"ui.common.cancel\":\"Cancel\",\"ui.common.save\":\"Save\",\"ui.common.dont_save\":\"Don't Save\",\"ui.settings.language\":\"Language\",\"ui.settings.window_size\":\"Window Size\",\"ui.settings.follow_desktop\":\"Follow Desktop\",\"ui.settings.window_mode\":\"Window Mode\",\"ui.settings.select_language.title\":\"Language\",\"ui.settings.select_language.content\":\"Choose the interface language.\",\"ui.settings.select_window_size.title\":\"Window Size\",\"ui.settings.select_window_size.content\":\"Choose the game window size.\",\"ui.settings.select_window_mode.title\":\"Window Mode\",\"ui.settings.select_window_mode.content\":\"Choose the game window mode.\",\"ui.settings.windowed\":\"Windowed\",\"ui.settings.fullscreen_window\":\"Borderless Fullscreen\",\"notify.unsaved.title\":\"Unsaved Changes\",\"notify.unsaved.content\":\"You have unsaved setting changes. Save them?\",\"notify.settings_reset.title\":\"Reset Settings\",\"notify.settings_reset.content\":\"Restore all settings to their defaults?\",\"tip.framework_ready\":\"Framework ready\",\"ui.start.title\":\"Start\",\"ui.start.new_game\":\"New Game\",\"ui.save.create_new\":\"Create New Save\",\"ui.save.create_new_summary\":\"Create a new game progress.\",\"ui.save.create\":\"Create\",\"ui.save.rename\":\"Rename\",\"ui.save.delete\":\"Delete\",\"ui.save.load\":\"Load\",\"ui.save.clear\":\"Clear\",\"ui.save.title\":\"Saves\",\"ui.save.name_error.duplicate\":\"A save with this name already exists.\"}") as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}

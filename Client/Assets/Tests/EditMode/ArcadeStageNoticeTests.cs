using Game;
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using QFramework;
using System.IO;

namespace Game.Tests
{
    /// <summary>第一场横幅的阶段去重、生命周期和正式 Prefab 输入穿透验收。</summary>
    public sealed class ArcadeStageNoticeTests
    {
        [Test]
        public void BannerPrefabIsHiddenAndCannotCaptureInput()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            var banner = prefab.transform.Find("StageBannerRoot");
            Assert.That(banner, Is.Not.Null, "正式 HUD 必须包含独立短暂横幅");
            var generated = prefab.GetComponent<ArcadeHudUI>();
            Assert.That(generated.StageBannerRoot, Is.SameAs(banner.gameObject));
            Assert.That(generated.StageBannerOpacity, Is.Not.Null);
            Assert.That(generated.TextStageBanner, Is.Not.Null);
            Assert.That(banner.gameObject.activeSelf, Is.False);
            var group = banner.GetComponent<CanvasGroup>();
            Assert.That(group, Is.Not.Null);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(group.interactable, Is.False);
            Assert.That(banner.GetComponentsInChildren<Selectable>(true), Is.Empty);
            foreach (var graphic in banner.GetComponentsInChildren<Graphic>(true)) Assert.That(graphic.raycastTarget, Is.False);
            Assert.That(banner.GetComponentInChildren<TextMeshProUGUI>(true), Is.Not.Null);
            Assert.That(new ArcadeHudUICtrl().BlocksGame, Is.False);
        }

        [Test]
        public void StageChangesAppearOnceAndConsumedNoticeCannotReplay()
        {
            var state = State();
            Assert.That(Observe(state, "run", 1, 1, false, false, 10), Is.True);
            Assert.That(Consume(state, 10, out var notice), Is.True);
            Assert.That(Property(notice, "Kind").ToString(), Is.EqualTo("WaveStarted"));
            Assert.That(Property(notice, "WaveNumber"), Is.EqualTo(1));
            Assert.That(Observe(state, "run", 1, 1, false, false, 10.2), Is.False);
            Assert.That(Consume(state, 10.2, out _), Is.False, "新 HUD 消费同一会话不能重新播放");
            Assert.That(Observe(state, "run", 1, 1, true, false, 10.3), Is.True);
            Assert.That(Consume(state, 10.3, out notice), Is.True);
            Assert.That(Property(notice, "Kind").ToString(), Is.EqualTo("WaveCompleted"));
            Assert.That(Observe(state, "run", 1, 1, true, false, 10.4), Is.False);
            Assert.That(Observe(state, "run", 1, 2, false, false, 10.5), Is.True);
        }

        [Test]
        public void ArenaCompletionReplacesWaveCompletionWithoutQueue()
        {
            var state = State();
            Observe(state, "run", 1, 4, true, false, 10);
            Assert.That(Observe(state, "run", 1, 4, true, true, 10), Is.True);
            Assert.That(Consume(state, 10, out var notice), Is.True);
            Assert.That(Property(notice, "Kind").ToString(), Is.EqualTo("ArenaCompleted"));
            Assert.That(Observe(state, "run", 1, 4, true, false, 10.1), Is.False);
            Assert.That(Consume(state, 10.1, out _), Is.False);
        }

        [Test]
        public void ExpiredNoticeCannotRestartAfterLateHudOpen()
        {
            var state = State();
            Observe(state, "run", 1, 1, false, false, 10);
            Assert.That(Consume(state, 12, out _), Is.False);
            Assert.That(Observe(state, "run", 1, 1, false, false, 12.1), Is.False);
            Assert.That(Consume(state, 12.1, out _), Is.False);
        }

        [Test]
        public void OldSnapshotsAndOtherArenasDoNotProduceBanner()
        {
            var state = State();
            Assert.That(Observe(state, "run", 2, 1, false, false, 10), Is.False);
            Observe(state, "run", 1, 2, false, false, 10);
            Consume(state, 10, out _);
            Assert.That(Observe(state, "run", 1, 1, true, false, 10.1), Is.False);
            Assert.That(Consume(state, 10.1, out _), Is.False);
        }

        [Test]
        public void NewBattleCanAnnounceFirstWaveAgainAfterClear()
        {
            var state = State();
            Observe(state, "run", 1, 1, false, false, 10);
            Consume(state, 10, out _);
            state.GetType().GetMethod("Clear").Invoke(state, null);
            Assert.That(Consume(state, 10.1, out _), Is.False);
            Assert.That(Observe(state, "retry", 1, 1, false, false, 11), Is.True);
            Assert.That(Consume(state, 11, out _), Is.True);
        }

        [Test]
        public void BannerFadesAgainstOriginalDeadlineWithoutGameplayTime()
        {
            var state = State();
            Observe(state, "run", 1, 1, false, false, 10);
            Consume(state, 10.5, out var notice);
            var opacity = notice.GetType().GetMethod("Opacity");
            Assert.That((float)opacity.Invoke(notice, new object[] { 10.5 }), Is.EqualTo(1f));
            Assert.That((float)opacity.Invoke(notice, new object[] { 11.575 }), Is.EqualTo(.5f).Within(.01));
            Assert.That((float)opacity.Invoke(notice, new object[] { 11.8 }), Is.Zero.Within(.001));
            Assert.That((float)opacity.Invoke(notice, new object[] { 12.0 }), Is.Zero);
        }

        [Test]
        public void RealHudFadesAndReopenedHudCannotReplayConsumedNotice()
        {
            var architectureField = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = architectureField.GetValue(null);
            var previousLoader = ResourceLoader.Current;
            var languageCache = typeof(LocalizationLanguages).GetField("cachedConfig", BindingFlags.Static | BindingFlags.NonPublic);
            var previousLanguageCache = languageCache.GetValue(null);
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            var architecture = new Game();
            architectureField.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(architecture, true);
            var model = new ArcadeCombatModel();
            architecture.RegisterModel(model);
            architecture.RegisterModel(new LocalizationModel());
            var localization = new LocalizationUtility(false);
            localization.LoadFromJson("zh-CN", File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization/zh-CN.json")));
            architecture.RegisterUtility(localization);
            GameObject first = null, reopened = null;
            float timeScale = Time.timeScale;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
                first = UnityEngine.Object.Instantiate(prefab);
                var ui = first.GetComponent<ArcadeHudUI>();
                var controller = Attach(ui);
                double now = Time.unscaledTimeAsDouble;
                model.StageNotices.Observe("run", 1, 1, false, false, now);
                Invoke(controller, "TryPresentStageNotice");
                Assert.That(ui.StageBannerRoot.activeSelf, Is.True);
                Assert.That(ui.TextStageBanner.text, Is.EqualTo("第 1 波"));
                Invoke(controller, "TryPresentStageNotice");
                Invoke(controller, "UpdateStageBanner", now + 1.575);
                Assert.That(ui.StageBannerOpacity.alpha, Is.EqualTo(.5f).Within(.01));
                Invoke(controller, "UpdateStageBanner", now + 1.81);
                Assert.That(ui.StageBannerRoot.activeSelf, Is.False);
                model.StageNotices.Observe("run", 1, 4, true, true, Time.unscaledTimeAsDouble);
                Invoke(controller, "TryPresentStageNotice");
                Assert.That(ui.TextStageBanner.text, Is.EqualTo("海岛完成"));
                Invoke(controller, "OnHide");
                Assert.That(ui.StageBannerRoot.activeSelf, Is.False);
                reopened = UnityEngine.Object.Instantiate(prefab);
                var reopenedUi = reopened.GetComponent<ArcadeHudUI>();
                Invoke(Attach(reopenedUi), "TryPresentStageNotice");
                Assert.That(reopenedUi.StageBannerRoot.activeSelf, Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            }
            finally
            {
                if (first != null) UnityEngine.Object.DestroyImmediate(first);
                if (reopened != null) UnityEngine.Object.DestroyImmediate(reopened);
                architectureField.SetValue(null, previous);
                ResourceLoader.Use(previousLoader);
                languageCache.SetValue(null, previousLanguageCache);
            }
        }

        private static ArcadeHudUICtrl Attach(ArcadeHudUI ui)
        {
            var controller = new ArcadeHudUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, ui);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, true);
            return controller;
        }
        private static void Invoke(ArcadeHudUICtrl controller, string name, params object[] args) =>
            typeof(ArcadeHudUICtrl).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, args);

        // 允许测试先于新实现编译；缺少功能时以断言失败呈现，而非编译错误阻塞共享编辑器。
        private static object State()
        {
            var type = typeof(ArcadeCombatModel).Assembly.GetType("Game.ArcadeStageNoticeState");
            Assert.That(type, Is.Not.Null, "缺少战斗会话拥有的阶段提示状态");
            return Activator.CreateInstance(type, true);
        }
        private static bool Observe(object state, object session, int arena, int wave, bool cleared, bool completed, double now) =>
            (bool)state.GetType().GetMethod("Observe").Invoke(state, new object[] { session, arena, wave, cleared, completed, now });
        private static bool Consume(object state, double now, out object notice)
        {
            var args = new object[] { now, null };
            bool consumed = (bool)state.GetType().GetMethod("TryConsume").Invoke(state, args);
            notice = args[1];
            return consumed;
        }
        private static object Property(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    }
}

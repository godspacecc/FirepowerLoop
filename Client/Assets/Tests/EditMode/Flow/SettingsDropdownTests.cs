using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>通过正式Prefab验证真正的下拉选择、设备分支与页面关闭时的列表回收。</summary>
    public sealed class SettingsDropdownTests
    {
        private NotifyAndHomeUiTests fixture;
        private SettingUICtrl controller;
        private SettingUI ui;

        [SetUp]
        public void SetUp()
        {
            fixture = new NotifyAndHomeUiTests(); fixture.SetUp();
            var localization = Game.Interface.GetUtility<LocalizationUtility>();
            foreach (var language in new[] { "zh-CN", "en-US" })
                localization.LoadFromJson(language, System.IO.File.ReadAllText("Assets/Game/Config/Localization/" + language + ".json"));
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            controller = UIHelper.ShowUI<SettingUICtrl>();
            ui = View<SettingUI>(controller);
        }

        [TearDown]
        public void TearDown() => fixture.TearDown();

        [Test]
        public void LanguageDropdown_ExpandsRealOptionsAndAppliesClickedOptionWithoutMessage()
        {
            var dropdown = Selector(Rows()[0]);
            Assert.That(dropdown.options.Select(option => option.text), Is.EqualTo(new[] { "简体中文", "英语" }));
            SettingsDropdownTestSupport.Start(dropdown);
            dropdown.Show();
            Assert.That(dropdown.IsExpanded, Is.True);
            var option = dropdown.GetComponentsInChildren<Toggle>(false)
                .Single(toggle => toggle.GetComponentInChildren<TMP_Text>().text == "英语");
            SettingsDropdownTestSupport.ExpectNativeEditModeClose();
            option.isOn = true;
            Assert.That(Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value, Is.EqualTo("en-US"));
            Assert.That(dropdown.captionText.text, Is.EqualTo("English"));
            Assert.That(UIHelper.GetUICtrl<MessageUICtrl>()?.IsShow() ?? false, Is.False);
        }

        [Test]
        public void MobileFrameRateUsesDropdownWhileControlLayoutRemainsOrdinaryButton()
        {
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            var rows = Rows();
            var frameRate = Selector(rows[1]);
            Assert.That(frameRate.options.Select(option => option.text), Is.EqualTo(new[] { "30 FPS", "60 FPS" }));
            frameRate.value = 0;
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentMobileFrameRate.Value, Is.EqualTo(30));
            Assert.That(rows[2].BtnContext.gameObject.activeInHierarchy, Is.True);
            Assert.That(Selector(rows[2]).gameObject.activeSelf, Is.False);
            Assert.That(rows[2].BtnContext.GetComponentsInChildren<Transform>(false).Any(t => t.name == "Chevron"), Is.False);
            rows[2].BtnContext.onClick.Invoke();
            Assert.That(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>().IsShow(), Is.True);
            View<MobileControlLayoutUI>(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>()).CancelButton.onClick.Invoke();
            Assert.That(ui.SettingGeneralRoot.activeInHierarchy, Is.True);
        }

        [Test]
        public void DesktopDropdownsApplyResolutionAndWindowModeAndDisableFullscreenResolution()
        {
            var rows = Rows();
            var resolution = Selector(rows[1]);
            resolution.value = resolution.options.FindIndex(option => option.text == "1920x1080");
            var mode = Selector(rows[2]);
            mode.value = mode.options.FindIndex(option => option.text == "无边框全屏");
            var model = Game.Interface.GetModel<DisplaySettingsModel>();
            Assert.That(model.CurrentWindowMode.Value, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(resolution.interactable, Is.False);
            Assert.That(resolution.captionText.text, Is.EqualTo("跟随桌面"));
            mode.value = mode.options.FindIndex(option => option.text == "窗口");
            Assert.That(model.CurrentResolutionCode.Value, Is.EqualTo("1920x1080"));
            Assert.That(resolution.interactable, Is.True);
            Assert.That(resolution.captionText.text, Is.EqualTo("1920x1080"));
        }

        [Test]
        public void PageHideClosesExpandedDropdownAndModeChangeRebindsReusedRows()
        {
            var language = Selector(Rows()[0]); SettingsDropdownTestSupport.Start(language); language.Show();
            Assert.That(language.IsExpanded, Is.True);
            SettingsDropdownTestSupport.ExpectNativeEditModeClose();
            ui.AudioTabButton.onClick.Invoke();
            Assert.That(language.IsExpanded, Is.False);
            ui.GeneralTabButton.onClick.Invoke();
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            Assert.That(Selector(Rows()[2]).gameObject.activeSelf, Is.False);
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(false);
            var mode = Selector(Rows()[2]);
            Assert.That(mode.gameObject.activeInHierarchy, Is.True);
            Assert.That(Rows()[2].BtnContext.gameObject.activeSelf, Is.False);
            mode.value = mode.options.FindIndex(option => option.text == "无边框全屏");
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentWindowMode.Value, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>(), Is.Null);
        }

        private SettingItemBoxUI[] Rows() => ui.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>()
            .SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
        private static TMP_Dropdown Selector(SettingItemBoxUI row)
        {
            var dropdown = typeof(SettingItemBoxUI).GetField("ValueDropdown")?.GetValue(row) as TMP_Dropdown;
            Assert.That(dropdown, Is.Not.Null, "正式Box必须通过生成器绑定真正的下拉组件");
            return dropdown;
        }
        private static T View<T>(BaseUICtrl ctrl) where T : BaseUI => (T)typeof(BaseUICtrl)
            .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
    }

    /// <summary>TMP原生生命周期依赖Start和运行时Destroy；EditMode显式初始化并精确声明这两个已知清理日志。</summary>
    internal static class SettingsDropdownTestSupport
    {
        internal static void Start(TMP_Dropdown dropdown) => typeof(TMP_Dropdown)
            .GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dropdown, null);

        internal static void ExpectNativeEditModeClose()
        {
            for (var i = 0; i < 2; i++)
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex("^Destroy may not be called from edit mode!"));
        }
    }
}

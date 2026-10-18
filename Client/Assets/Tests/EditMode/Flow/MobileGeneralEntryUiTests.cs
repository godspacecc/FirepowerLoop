using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    /// <summary>使用正式Prefab验证手机通用页入口、取消返回内容与隐藏分页后的侧栏排列。</summary>
    public sealed class MobileGeneralEntryUiTests
    {
        private NotifyAndHomeUiTests fixture;

        [SetUp]
        public void SetUp()
        {
            fixture = new NotifyAndHomeUiTests();
            fixture.SetUp();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            Game.Interface.GetUtility<LocalizationUtility>().LoadFromJson("zh-CN",
                File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
        }

        [TearDown]
        public void TearDown() => fixture.TearDown();

        [Test]
        public void CancelReturnsToPopulatedGeneralPageAndSidebarHasNoHiddenTabGap()
        {
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            var ctrl = UIHelper.ShowUI<SettingUICtrl>();
            var ui = View<SettingUI>(ctrl);
            Assert.That(ui.ControlsTabButton.gameObject.activeSelf, Is.False);
            var rows = ui.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>().SettingList
                .GetComponentsInChildren<SettingItemBoxUI>(false);
            Assert.That(rows.Select(row => row.TextSetting.text), Is.EqualTo(new[] { "语言", "帧率上限", "操作" }));
            rows[2].BtnContext.onClick.Invoke();
            View<MobileControlLayoutUI>(UIHelper.GetUICtrl<MobileControlLayoutUICtrl>()).CancelButton.onClick.Invoke();
            Assert.That(ui.SettingGeneralRoot.activeInHierarchy, Is.True);
            Assert.That(rows.All(row => row.gameObject.activeInHierarchy), Is.True, "退出编辑后右侧不能空白");
            var sidebar = (RectTransform)ui.GeneralTabButton.transform.parent;
            LayoutRebuilder.ForceRebuildLayoutImmediate(sidebar);
            var audio = (RectTransform)ui.AudioTabButton.transform;
            var network = (RectTransform)ui.NetworkTabButton.transform;
            Assert.That(audio.anchoredPosition.y - network.anchoredPosition.y, Is.EqualTo(96).Within(.1f),
                "隐藏手机操作分页后，网络应紧接音频排列");
        }

        private static T View<T>(BaseUICtrl ctrl) where T : BaseUI => (T)typeof(BaseUICtrl)
            .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
    }
}

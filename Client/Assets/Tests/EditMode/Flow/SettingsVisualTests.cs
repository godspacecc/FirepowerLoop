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
    /// <summary>验证正式设置页面的样式所有权、分页状态和音量数值同步，不写入玩家设置。</summary>
    public sealed class SettingsVisualTests
    {
        private NotifyAndHomeUiTests fixture;

        [SetUp]
        public void SetUp()
        {
            fixture = new NotifyAndHomeUiTests();
            fixture.SetUp();
            // 正式Prefab使用发布后的完整文案，避免旧测试字典遗漏新增键。
            var localization = Game.Interface.GetUtility<LocalizationUtility>();
            localization.LoadFromJson("zh-CN", System.IO.File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            localization.LoadFromJson("en-US", System.IO.File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
        }

        [TearDown]
        public void TearDown() => fixture.TearDown();

        [Test]
        public void LegacyTheme_DoesNotOverwriteAuthoredSettingsControls()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Setting.prefab"));
            try
            {
                var button = root.GetComponent<SettingUI>().GeneralTabButton;
                var tint = new Color(.2f, .5f, .4f);
                button.image.color = tint;
                ((RectTransform)button.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 54);
                UITheme.Apply(root);
                Assert.That(button.image.color, Is.EqualTo(tint), "设置的正式Prefab拥有样式，旧通用主题不得覆盖。");
                Assert.That(((RectTransform)button.transform).rect.height, Is.EqualTo(54));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void PageSwitchAndReopen_KeepAnExplicitSelectedAppearance()
        {
            var ctrl = UIHelper.ShowUI<SettingUICtrl>();
            var ui = View(ctrl);
            var generalSelected = ui.GeneralTabButton.image.color;
            var audioIdle = ui.AudioTabButton.image.color;
            Assert.That(generalSelected, Is.Not.EqualTo(audioIdle), "选中页应有独立强调色。");
            Assert.That(ui.GeneralTabButton.interactable, Is.True, "选中不是禁用状态。");
            ui.AudioTabButton.onClick.Invoke();
            Assert.That(ui.AudioTabButton.image.color, Is.EqualTo(generalSelected));
            Assert.That(ui.GeneralTabButton.image.color, Is.EqualTo(audioIdle));
            ctrl.Hide(); ctrl.Show();
            Assert.That(ui.SettingAudioRoot.activeSelf, Is.True);
            Assert.That(ui.AudioTabButton.image.color, Is.EqualTo(generalSelected));
        }

        [Test]
        public void ClickingCurrentPage_DoesNotCancelItsPendingInput()
        {
            var ctrl = UIHelper.ShowUI<SettingUICtrl>();
            var ui = View(ctrl);
            ui.ControlsTabButton.onClick.Invoke();
            var hotkeys = Game.Interface.GetSystem<HotKeySystem>();
            hotkeys.BeginRebind(Game.Interface.GetModel<HotKeyModel>().Bindings.First().ActionId);
            Assert.That(hotkeys.IsCapturingRebind, Is.True);
            ui.ControlsTabButton.onClick.Invoke();
            Assert.That(hotkeys.IsCapturingRebind, Is.True, "重复点击当前页不得触发Hide/Show并丢失未完成的编辑。");
            ctrl.Hide();
            Assert.That(hotkeys.IsCapturingRebind, Is.False);
        }

        [Test]
        public void AudioValues_FollowSlidersAndModelOnReopen()
        {
            var ctrl = UIHelper.ShowUI<SettingUICtrl>();
            var ui = View(ctrl);
            ui.AudioTabButton.onClick.Invoke();
            var audio = ui.SettingAudioRoot.GetComponent<SettingAudioPageUI>();
            var value = audio.MasterVolumeSlider.transform.parent.Find("VolumeValue")?.GetComponent<TMP_Text>();
            Assert.That(value, Is.Not.Null, "正式音量行必须显示当前百分比。");
            audio.MasterVolumeSlider.value = .37f;
            Assert.That(value.text, Is.EqualTo("37%"));
            Assert.That(Game.Interface.GetModel<AudioSettingsModel>().MasterVolume.Value, Is.EqualTo(.37f));
            ctrl.Hide();
            Game.Interface.GetModel<AudioSettingsModel>().MasterVolume.Value = .62f;
            ctrl.Show();
            Assert.That(value.text, Is.EqualTo("62%"));
        }

        [Test]
        public void NetworkFeedback_ChangesLanguageWithoutReplacingPendingAddress()
        {
            Game.Interface.RegisterUtility<global::Game.Network.INetworkConfigProvider>(new NetworkPreviewProvider());
            Game.Interface.RegisterUtility<IResourceServerConfigProvider>(new ResourcePreviewProvider());
            var ctrl = UIHelper.ShowUI<SettingUICtrl>();
            var ui = View(ctrl);
            ui.NetworkTabButton.onClick.Invoke();
            var network = ui.SettingNetworkRoot.GetComponent<SettingNetworkPageUI>();
            network.GameServerInput.text = "invalid address";
            network.GameServerApplyButton.onClick.Invoke();
            Assert.That(network.GameServerFeedback.text, Is.EqualTo("地址格式无效，请输入 IP 或域名:端口。"));
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(network.GameServerFeedback.text, Is.EqualTo("Enter an IP address or hostname with a port."));
            Assert.That(network.GameServerInput.text, Is.EqualTo("invalid address"));
            var details = ui.SettingNetworkRoot.GetComponentsInChildren<UnityEngine.UI.Toggle>(true).Single();
            // Prefab持久化监听仅在运行时触发，EditMode夹具显式模拟其运行状态。
            Assert.That(details.onValueChanged.GetPersistentEventCount(), Is.EqualTo(1));
            details.onValueChanged.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            details.isOn = true;
            Assert.That(network.DiagnosticStatus.gameObject.activeSelf, Is.True);
            details.isOn = false;
            Assert.That(network.DiagnosticStatus.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void FormalViews_HaveResolvedGeneratedReferencesAndValueSelectors()
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Setting.prefab");
            foreach (var view in root.GetComponentsInChildren<BaseUI>(true))
                foreach (var field in view.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (typeof(Object).IsAssignableFrom(field.FieldType))
                        Assert.That((Object)field.GetValue(view), Is.Not.Null, view.name + "/" + field.Name);
            var row = root.GetComponent<SettingUI>().SettingGeneralRoot.GetComponent<SettingGeneralPageUI>().SettingList.itemPrefab.GetComponent<SettingItemBoxUI>();
            Assert.That(row.TextContext.transform.IsChildOf(row.BtnContext.transform), Is.True, "当前值本身应为选择入口。");
            Assert.That(root.GetComponent<SettingUI>().GeneralTabButton.image.sprite.name, Is.Not.EqualTo("Icon_Close"));
        }

        private static SettingUI View(SettingUICtrl ctrl) => (SettingUI)typeof(BaseUICtrl)
            .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);

        /// <summary>隔离玩家服务器配置，只为页面反馈测试提供读取失败结果。</summary>
        private sealed class NetworkPreviewProvider : global::Game.Network.INetworkConfigProvider
        {
            public System.Threading.Tasks.Task<global::Game.Network.NetworkConfigResult> LoadAsync(System.Threading.CancellationToken token)
                => System.Threading.Tasks.Task.FromResult(global::Game.Network.NetworkConfigResult.Invalid("test"));
        }

        private sealed class ResourcePreviewProvider : IResourceServerConfigProvider
        {
            public System.Threading.Tasks.Task<ResourceServerConfigResult> LoadAsync(System.Threading.CancellationToken token)
                => System.Threading.Tasks.Task.FromResult(new ResourceServerConfigResult(false, "", "test"));
            public bool TrySave(string value, out string server, out string diagnostic) { server = ""; diagnostic = "test"; return false; }
            public bool TryReset(out string diagnostic) { diagnostic = "test"; return false; }
        }
    }
}

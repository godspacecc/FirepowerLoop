using System.Collections;
using System.Reflection;
using Boot;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using YooAsset;

namespace Game.Tests
{
    [TestFixture]
    public sealed class CommonFrameworkUiSmokeTests
    {
        private const float TimeoutSeconds = 30f;
        private const string BootScenePath = "Assets/Game/Scenes/Boot.unity";
        private const string MainSceneName = "Main";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            ClearPrefs();
            yield return CleanupRuntimeObjects();
            yield return LoadBootToMain();
        }

        [UnityTest]
        public IEnumerator Main_StartSettingAndFloatTip_WorkEndToEnd()
        {
            var start = default(StartUICtrl);
            yield return WaitUntil(() =>
            {
                start = UIHelper.GetUICtrl<StartUICtrl>();
                return start != null && start.IsShow();
            }, "Start UI should be visible.");

            var startUi = GetBaseUI<StartUI>(start);
            Assert.That(GetStartTitle(startUi), Is.EqualTo(Loc.T("ui.start.title")));

            startUi.BtnSetting.onClick.Invoke();
            yield return null;

            var setting = UIHelper.GetUICtrl<SettingUICtrl>();
            Assert.That(setting, Is.Not.Null);
            Assert.IsTrue(setting.IsShow());
            var settingUi = GetBaseUI<SettingUI>(setting);
            var generalUi = settingUi.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>();
            var rows = generalUi.SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            Assert.That(rows.Length, Is.EqualTo(3));

            rows[0].BtnContext.onClick.Invoke();
            yield return null;
            FindMessageButton("English").onClick.Invoke();
            yield return null;
            Assert.That(GetStartTitle(startUi), Is.EqualTo("Start"));

            rows = generalUi.SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            rows[1].BtnContext.onClick.Invoke();
            yield return null;
            FindMessageButton("1920x1080").onClick.Invoke();
            yield return null;

            rows = generalUi.SettingList.GetComponentsInChildren<SettingItemBoxUI>(false);
            rows[2].BtnContext.onClick.Invoke();
            yield return null;
            FindMessageButton("Borderless Fullscreen").onClick.Invoke();
            yield return null;

            settingUi.BtnExit.onClick.Invoke();
            yield return null;
            var displayModel = Game.Interface.GetModel<DisplaySettingsModel>();
            Assert.That(displayModel.CurrentResolutionCode.Value, Is.EqualTo("1920x1080"));
            Assert.That(displayModel.CurrentWindowMode.Value, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(setting.IsShow(), Is.False);

            Assert.IsFalse(startUi.BtnNewGame.gameObject.activeSelf,
                "已有运行时基地会话时，开始界面不应再提供旧存档选择入口。");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            ClearPrefs();
            yield return CleanupRuntimeObjects();
        }

        private static Button FindMessageButton(string label)
        {
            foreach (var button in Object.FindObjectsOfType<MessageButtonBoxUI>())
            {
                if (button.gameObject.activeInHierarchy && button.ButtonText.text == label)
                {
                    return button.BtnButton;
                }
            }

            Assert.Fail($"Message button not found: {label}");
            return null;
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private static string GetStartTitle(StartUI ui)
        {
            return ui.transform.Find("Panel/TitleText").GetComponent<TMP_Text>().text;
        }

        private static IEnumerator LoadBootToMain()
        {
            var loadOperation = SceneManager.LoadSceneAsync(BootScenePath, LoadSceneMode.Single);
            Assert.That(loadOperation, Is.Not.Null);

            while (!loadOperation.isDone)
            {
                yield return null;
            }

            yield return WaitUntil(() => SceneManager.GetActiveScene().name == MainSceneName, "Boot should enter Main scene.");
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, string failMessage)
        {
            var startTime = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - startTime < TimeoutSeconds)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail(failMessage);
        }

        private static IEnumerator CleanupRuntimeObjects()
        {
            ResourceSceneLoader.ReleaseCurrent();
            UIHelper.ResetForTests();

            ResourcePackage package = null;
            try
            {
                package = YooAssets.TryGetPackage("DefaultPackage");
            }
            catch (System.Exception)
            {
            }

            if (package != null)
            {
                var destroyOperation = package.DestroyAsync();
                while (!destroyOperation.IsDone)
                {
                    yield return null;
                }

                YooAssets.RemovePackage(package);
            }

            var launchers = Object.FindObjectsOfType<Launcher>();
            foreach (var launcher in launchers)
            {
                Object.DestroyImmediate(launcher.gameObject);
            }

            yield return null;
        }

        private static void ClearPrefs()
        {
            PlayerPrefs.DeleteKey(DisplaySettingsModel.ResolutionPlayerPrefsKey);
            PlayerPrefs.DeleteKey(DisplaySettingsModel.WindowModePlayerPrefsKey);
            PlayerPrefs.DeleteKey(LocalizationModel.PlayerPrefsKey);
        }
    }
}

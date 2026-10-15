using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class SettingRevisionTests
    {
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        [Test]
        public void SettingPrefabKeepsSingleHostAndFourSerializedPages()
        {
            var yaml = ReadProjectFile("Assets/Game/Prefabs/UI/Home/Setting.prefab");

            var prefabRoot = PrefabUtility.LoadPrefabContents("Assets/Game/Prefabs/UI/Home/Setting.prefab");
            try
            {
                Assert.NotNull(prefabRoot);
                Assert.IsNull(prefabRoot.transform.parent);
                var ui = prefabRoot.GetComponent<SettingUI>();
                Assert.NotNull(ui);
                Assert.NotNull(ui.SettingGeneralRoot.GetComponent<SettingGeneralPageUI>());
                Assert.NotNull(ui.SettingAudioRoot.GetComponent<SettingAudioPageUI>());
                Assert.NotNull(ui.SettingControlsRoot.GetComponent<SettingControlsPageUI>());
                Assert.NotNull(ui.SettingNetworkRoot.GetComponent<SettingNetworkPageUI>());
                var scrollRect = ui.SettingControlsRoot.GetComponentInChildren<ScrollRect>(true);
                Assert.NotNull(scrollRect, "Hot key page must keep its own clipped scrolling viewport.");
                Assert.NotNull(scrollRect.viewport);
                Assert.NotNull(scrollRect.viewport.GetComponent<RectMask2D>());
                Assert.IsFalse(scrollRect.horizontal);
                Assert.IsTrue(scrollRect.vertical);
            }
            finally
            {
                if (prefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }

            Assert.That(yaml, Does.Contain("GeneralTabButton:"));
            Assert.That(yaml, Does.Contain("AudioTabButton:"));
            Assert.That(yaml, Does.Contain("ControlsTabButton:"));
            Assert.That(yaml, Does.Contain("NetworkTabButton:"));
            Assert.That(yaml, Does.Contain("SettingNetworkRoot:"));
        }

        [Test]
        public void SettingControllerDoesNotGenerateFallbackControls()
        {
            var source = string.Join("\n", new[]
            {
                ReadProjectFile("Assets/Game/Scripts/Controller/UICtrl/Home/SettingUICtrl.cs"),
                ReadProjectFile("Assets/Game/Scripts/Controller/UICtrl/Home/Page/SettingGeneralPageCtrl.cs"),
                ReadProjectFile("Assets/Game/Scripts/Controller/UICtrl/Home/Page/SettingAudioPageCtrl.cs"),
                ReadProjectFile("Assets/Game/Scripts/Controller/UICtrl/Home/Page/SettingControlsPageCtrl.cs"),
                ReadProjectFile("Assets/Game/Scripts/Controller/UICtrl/Home/Page/SettingNetworkPageCtrl.cs")
            });

            Assert.That(source, Does.Not.Contain("EnsureRuntimeControls"));
            Assert.That(source, Does.Not.Contain("new GameObject"));
            Assert.That(source, Does.Not.Contain("AddComponent<"));
            Assert.That(source, Does.Not.Contain("row.ActionNameText.text = binding.ActionId"));
            Assert.That(source, Does.Not.Contain("row.BindingText.text = binding.BindingPath"));
        }

        [Test]
        public void HotKeyRowsUseReadableActionNamesAndBindingText()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            _ = Game.Interface;

            Assert.That(InvokePrivateStaticString("FormatAction", HotKeyActionIds.TogglePause), Is.EqualTo("暂停 / 继续"));
            Assert.That(InvokePrivateStaticString("FormatAction", HotKeyActionIds.ToggleRuntimeLog), Is.EqualTo("运行日志"));
            Assert.That(InvokePrivateStaticString("FormatAction", "map.toggle"), Is.EqualTo("未知动作"));
            Assert.That(InvokePrivateStaticString("FormatAction", "flashlight.toggle"), Is.EqualTo("未知动作"));
            Assert.That(InvokePrivateStaticString("FormatAction", "test-command.toggle"), Is.EqualTo("未知动作"));
            Assert.That(HotKeyDisplayFormatter.FormatPath(HotKeyDefaults.Escape), Is.EqualTo("Esc"));
            Assert.That(HotKeyDisplayFormatter.FormatPath(HotKeyDefaults.F1), Is.EqualTo("F1"));
        }

        [Test]
        public void ResetAllToDefault_AppliesRuntimeDefaultsAndPreservesDirtyOnFailure()
        {
            var store = new FakeSettingsStore { SaveSucceeds = false };
            var displayApplier = new FakeDisplayApplier();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<ISettingsFileStore>(store);
                architecture.RegisterUtility<IDisplayResolutionApplier>(displayApplier);
            };
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            var settings = Game.Interface.GetSystem<SettingsSystem>();
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.GetModel<DisplaySettingsModel>().SetResolutionCode("1280x720");
            Game.Interface.GetModel<DisplaySettingsModel>().SetWindowMode(FullScreenMode.FullScreenWindow);
            Game.Interface.GetModel<AudioSettingsModel>().Apply(new AudioSettingsData
            {
                MasterVolume = 0.2f,
                MusicVolume = 0.3f,
                SoundVolume = 0.4f,
                Mute = true
            });
            Game.Interface.GetModel<HotKeyModel>().Apply(new[]
            {
                new HotKeyBinding { ActionId = HotKeyActionIds.TogglePause, Context = HotKeyContext.Gameplay, BindingPath = HotKeyDefaults.F1 }
            });

            LogAssert.Expect(LogType.Error, "[SettingsSystem] 保存设置失败：test save failure");
            Assert.IsFalse(settings.ResetAllToDefault());

            var defaults = SettingsData.CreateDefault();
            Assert.That(Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value, Is.EqualTo(defaults.LanguageCode));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, Is.EqualTo(DisplayResolutionPresets.Default.Code));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentWindowMode.Value, Is.EqualTo(FullScreenMode.Windowed));
            var audio = Game.Interface.GetModel<AudioSettingsModel>().ToData();
            Assert.That(audio.MasterVolume, Is.EqualTo(defaults.Audio.MasterVolume));
            Assert.That(audio.MusicVolume, Is.EqualTo(defaults.Audio.MusicVolume));
            Assert.That(audio.SoundVolume, Is.EqualTo(defaults.Audio.SoundVolume));
            Assert.That(audio.Mute, Is.EqualTo(defaults.Audio.Mute));
            CollectionAssert.AreEquivalent(
                HotKeyDefaults.Create().Select(item => item.ActionId + item.BindingPath),
                Game.Interface.GetModel<HotKeyModel>().Bindings.Select(item => item.ActionId + item.BindingPath));
            Assert.That(displayApplier.LastPreset.Code, Is.EqualTo(DisplayResolutionPresets.Default.Code));
            Assert.That(displayApplier.LastWindowMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(ReadProjectFile("Assets/Game/Scripts/System/Settings/SettingsSystem.cs"),
                Does.Contain("this.GetSystem<AudioSystem>().ApplyVolumes();"));

            store.SaveSucceeds = true;
            Assert.IsTrue(settings.Flush());
            Assert.That(store.SaveCount, Is.EqualTo(2));
            Assert.That(store.LastSaved.Display.ResolutionCode, Is.EqualTo(defaults.Display.ResolutionCode));
        }

        private static string ReadProjectFile(string path)
        {
            return File.ReadAllText(Path.Combine(ProjectRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        }

        [Test]
        public void MobileFrameRateChoiceSavesAndGeneralResetRestoresDefaultWithoutChangingDesktopTarget()
        {
            var store = new FakeSettingsStore();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<ISettingsFileStore>(store);
                architecture.RegisterUtility<IDisplayResolutionApplier>(new FakeDisplayApplier());
            };
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            var settings = Game.Interface.GetSystem<SettingsSystem>();
            var previousTarget = Application.targetFrameRate;
            Assert.IsFalse(settings.SetMobileFrameRate(120));
            Assert.IsTrue(settings.Flush());
            Assert.That(store.SaveCount, Is.Zero);
            Assert.IsTrue(settings.SetMobileFrameRate(30));
            Assert.IsTrue(settings.Flush());
            Assert.That(store.LastSaved.Display.MobileFrameRate, Is.EqualTo(30));
            settings.Apply(store.LastSaved.Clone());
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentMobileFrameRate.Value, Is.EqualTo(30));
            Assert.IsTrue(settings.SetMobileFrameRate(30));
            Assert.IsTrue(settings.Flush());
            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.IsTrue(settings.ResetGeneralToDefault());
            Assert.That(store.LastSaved.Display.MobileFrameRate, Is.EqualTo(60));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentMobileFrameRate.Value, Is.EqualTo(60));
            Assert.That(Application.targetFrameRate, Is.EqualTo(previousTarget));
        }

        private static string InvokePrivateStaticString(string methodName, string value)
        {
            return (string)typeof(SettingControlsPageCtrl)
                .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, new object[] { value });
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class FakeSettingsStore : ISettingsFileStore
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
        }

        private sealed class FakeDisplayApplier : IDisplayResolutionApplier
        {
            public DisplayResolutionPreset LastPreset { get; private set; }
            public FullScreenMode LastWindowMode { get; private set; }

            public void Apply(DisplayResolutionPreset preset, FullScreenMode windowMode)
            {
                LastPreset = preset;
                LastWindowMode = windowMode;
            }
        }

        private sealed class MinimalLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"Chinese\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                        return new TextAsset("{\"ui.hotkey.toggle_pause\":\"暂停 / 继续\",\"ui.hotkey.toggle_runtime_log\":\"运行日志\",\"ui.hotkey.toggle_map\":\"打开/关闭地图\",\"ui.hotkey.toggle_flashlight\":\"打开/关闭手电筒\",\"ui.hotkey.unknown\":\"未知动作\"}") as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, System.Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}

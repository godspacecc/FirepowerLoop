using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class SettingsFileTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDaySettingsTests", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }

            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
        }

        [Test]
        public void SettingsData_DefaultCloneAndSanitizeStayVersionOne()
        {
            var data = SettingsData.CreateDefault();
            var clone = data.Clone();
            var sanitized = new SettingsData
            {
                Version = 99,
                LanguageCode = "missing",
                Display = new DisplaySettingsData
                {
                    ResolutionCode = "999x999",
                    WindowMode = FullScreenMode.ExclusiveFullScreen
                },
                Audio = new AudioSettingsData
                {
                    MasterVolume = 2f,
                    MusicVolume = -1f,
                    SoundVolume = 0.5f,
                    Mute = true
                },
                HotKeys = HotKeyDefaults.Create(),
                TestTools = new TestToolSettingsData
                {
                    TestCmdButtonPositionX = float.NaN,
                    TestCmdButtonPositionY = 2f
                }
            }.Sanitize();

            Assert.That(SettingsData.CurrentVersion, Is.EqualTo(1));
            Assert.That(data.Version, Is.EqualTo(1));
            Assert.That(clone.Version, Is.EqualTo(1));
            Assert.That(sanitized.Version, Is.EqualTo(1));
            Assert.That(data.LanguageCode, Is.EqualTo("zh-CN"));
            Assert.That(data.Display.ResolutionCode, Is.EqualTo(DisplayResolutionPresets.Default.Code));
            Assert.That(data.Display.WindowMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(data.Audio.MasterVolume, Is.EqualTo(1f));
            Assert.That(data.Audio.MusicVolume, Is.EqualTo(1f));
            Assert.That(data.Audio.SoundVolume, Is.EqualTo(1f));
            Assert.That(data.Audio.Mute, Is.False);
            Assert.That(data.HotKeys.Select(item => item.ActionId), Is.EquivalentTo(new[]
            {
                HotKeyActionIds.TogglePause,
                HotKeyActionIds.ToggleInventory,
                HotKeyActionIds.ToggleRuntimeLog,
                HotKeyActionIds.ToggleMap,
                HotKeyActionIds.ToggleFlashlight,
            }));
            Assert.That(data.HotKeys.Single(item => item.ActionId == HotKeyActionIds.ToggleMap).BindingPath,
                Is.EqualTo(HotKeyDefaults.Map));
            Assert.That(data.HotKeys.Single(item => item.ActionId == HotKeyActionIds.ToggleFlashlight).BindingPath,
                Is.EqualTo(HotKeyDefaults.Flashlight));
            Assert.That(data.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.09f));
            Assert.That(data.TestTools.TestCmdButtonPositionY, Is.EqualTo(0.11f));
            Assert.That(sanitized.Audio.MasterVolume, Is.EqualTo(1f));
            Assert.That(sanitized.Audio.MusicVolume, Is.EqualTo(0f));
            Assert.That(sanitized.Display.WindowMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(sanitized.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.09f));
            Assert.That(sanitized.TestTools.TestCmdButtonPositionY, Is.EqualTo(1f));
        }

        [Test]
        public void LegacySettingsFillMapBindingWithoutOverwritingGameplayKeys()
        {
            var free = SettingsData.SanitizeHotKeys(new[]
            {
                new HotKeyBinding { ActionId = HotKeyActionIds.TogglePause, Context = HotKeyContext.Gameplay, BindingPath = HotKeyDefaults.Escape }
            });
            Assert.That(free.Single(item => item.ActionId == HotKeyActionIds.ToggleMap).BindingPath, Is.EqualTo(HotKeyDefaults.Map));

            var occupied = SettingsData.SanitizeHotKeys(new[]
            {
                new HotKeyBinding { ActionId = "custom.m", Context = HotKeyContext.Gameplay, BindingPath = HotKeyDefaults.Map },
                new HotKeyBinding { ActionId = "custom.n", Context = HotKeyContext.Gameplay, BindingPath = HotKeyDefaults.MapFallbackN }
            });
            Assert.That(occupied.Single(item => item.ActionId == HotKeyActionIds.ToggleMap).BindingPath, Is.EqualTo(HotKeyDefaults.MapFallbackF2));
            Assert.That(occupied.Single(item => item.ActionId == "custom.m").BindingPath, Is.EqualTo(HotKeyDefaults.Map));
            var gameplay = occupied.Where(item => item.Context == HotKeyContext.Gameplay).ToArray();
            Assert.That(gameplay.Select(item => item.BindingPath).Distinct(System.StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(gameplay.Length));
        }

        [Test]
        public void LegacySettingsFillFlashlightBindingWithoutOverwritingGameplayKeys()
        {
            var free = SettingsData.SanitizeHotKeys(System.Array.Empty<HotKeyBinding>());
            Assert.That(free.Single(item => item.ActionId == HotKeyActionIds.ToggleFlashlight).BindingPath,
                Is.EqualTo(HotKeyDefaults.Flashlight));

            var occupied = SettingsData.SanitizeHotKeys(new[]
            {
                new HotKeyBinding { ActionId = "custom.f", Context = HotKeyContext.Gameplay, BindingPath = HotKeyDefaults.Flashlight }
            });
            Assert.That(occupied.Single(item => item.ActionId == HotKeyActionIds.ToggleFlashlight).BindingPath,
                Is.EqualTo(HotKeyDefaults.FlashlightFallbackL));
        }

        [Test]
        public void RetiredQuickSlotBindingsAreRemovedWithoutChangingOtherKeys()
        {
            var oldBindings = HotKeyDefaults.Create();
            oldBindings.Single(binding => binding.ActionId == HotKeyActionIds.ToggleInventory).BindingPath = "<Keyboard>/b";
            for (var index = 1; index <= 6; index++)
                oldBindings.Add(new HotKeyBinding
                {
                    ActionId = "quick-slot." + index,
                    Context = HotKeyContext.Gameplay,
                    BindingPath = "<Keyboard>/" + (index + 2)
                });
            var cleaned = SettingsData.SanitizeHotKeys(oldBindings);
            Assert.That(cleaned.Any(binding => binding.ActionId.StartsWith("quick-slot.")), Is.False);
            Assert.That(cleaned.Single(binding => binding.ActionId == HotKeyActionIds.ToggleInventory).BindingPath,
                Is.EqualTo("<Keyboard>/b"));
        }

        [Test]
        public void TestToolRecentCommands_SanitizeCloneAndModelRoundTripAreIsolated()
        {
            var source = new TestToolSettingsData
            {
                RecentCommands = new System.Collections.Generic.List<string>
                {
                    " #clone 1 1 ", null, "", "clone 2 1", "$ping", "#clone 1 1", "#  "
                }
            };

            var sanitized = source.Clone().Sanitize();
            Assert.That(sanitized.RecentCommands, Is.EqualTo(new[] { "#clone 1 1", "$ping" }));
            Assert.That(source.RecentCommands[0], Is.EqualTo(" #clone 1 1 "), "clone must not mutate source data");

            var store = new SettingsFileStore(Path.Combine(tempRoot, "recent"));
            CreateGameArchitectureForTests(store);
            var model = Game.Interface.GetModel<TestToolSettingsModel>();
            model.Apply(sanitized);
            sanitized.RecentCommands.Add("#clear bag");
            Assert.That(model.RecentCommands, Is.EqualTo(new[] { "#clone 1 1", "$ping" }));

            var data = model.ToData();
            data.RecentCommands.Clear();
            Assert.That(model.RecentCommands, Is.EqualTo(new[] { "#clone 1 1", "$ping" }));
            Assert.That(model.RecordSubmittedCommand("$ping"), Is.True);
            Assert.That(model.RecordSubmittedCommand("$ping"), Is.False);
            Assert.That(model.RecentCommands, Is.EqualTo(new[] { "$ping", "#clone 1 1" }));
        }

        [Test]
        public void Store_SavesAtomicallyAndLeavesNoTmp()
        {
            var store = new SettingsFileStore(tempRoot);
            var data = SettingsData.CreateDefault();
            data.LanguageCode = "en-US";

            Assert.IsTrue(store.Save(data, out var error), error);
            var loaded = store.LoadOrDefault(out var result);

            Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedPrimary));
            Assert.That(loaded.LanguageCode, Is.EqualTo("en-US"));
            Assert.IsFalse(File.Exists(Path.Combine(tempRoot, "Settings", "settings.tmp.json")));
            Assert.IsTrue(File.Exists(Path.Combine(tempRoot, "Settings", "settings.json")));
        }

        [Test]
        public void Store_InvalidPrimaryFallsBackToBackupThenDefault()
        {
            var store = new SettingsFileStore(tempRoot);
            Assert.IsTrue(store.Save(SettingsData.CreateDefault(), out _));

            var settingsDir = Path.Combine(tempRoot, "Settings");
            var primary = Path.Combine(settingsDir, "settings.json");
            var backup = Path.Combine(settingsDir, "settings.backup.json");
            File.WriteAllText(backup, "{\"version\":1,\"languageCode\":\"en-US\"}");
            File.WriteAllText(primary, "{ broken json");

            var loaded = store.LoadOrDefault(out var result);
            Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedBackup));
            Assert.That(loaded.LanguageCode, Is.EqualTo("en-US"));

            File.WriteAllText(backup, "{ broken backup");
            loaded = store.LoadOrDefault(out result);
            Assert.That(result, Is.EqualTo(SettingsLoadResult.CreatedDefault));
            Assert.That(loaded.LanguageCode, Is.EqualTo("zh-CN"));
        }

        [Test]
        public void Store_RejectsMissingOrUnknownVersion()
        {
            var settingsDir = Path.Combine(tempRoot, "Settings");
            Directory.CreateDirectory(settingsDir);
            var primary = Path.Combine(settingsDir, "settings.json");
            var store = new SettingsFileStore(tempRoot);

            File.WriteAllText(primary, "{\"languageCode\":\"en-US\"}");
            Assert.That(store.LoadOrDefault(out var result).LanguageCode, Is.EqualTo("zh-CN"));
            Assert.That(result, Is.EqualTo(SettingsLoadResult.CreatedDefault));

            File.WriteAllText(primary, "{\"version\":2,\"languageCode\":\"en-US\"}");
            Assert.That(store.LoadOrDefault(out result).LanguageCode, Is.EqualTo("zh-CN"));
            Assert.That(result, Is.EqualTo(SettingsLoadResult.CreatedDefault));
        }

        [Test]
        public void TestToolPosition_SaveLoadBackupAndResetRoundTrips()
        {
            var legacyRoot = Path.Combine(tempRoot, "legacy");
            var legacyDir = Path.Combine(legacyRoot, "Settings");
            Directory.CreateDirectory(legacyDir);
            var legacyStore = new SettingsFileStore(legacyRoot);
            var primary = Path.Combine(legacyDir, "settings.json");

            File.WriteAllText(primary, "{\"version\":1,\"languageCode\":\"zh-CN\"}");
            var loaded = legacyStore.LoadOrDefault(out var result);
            Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedPrimary));
            Assert.That(loaded.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.09f));
            Assert.That(loaded.TestTools.TestCmdButtonPositionY, Is.EqualTo(0.11f));
            Assert.That(loaded.TestTools.RecentCommands, Is.Empty);

            File.WriteAllText(primary, "{\"version\":1,\"testTools\":{\"recentCommands\":null}}");
            loaded = legacyStore.LoadOrDefault(out result);
            Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedPrimary));
            Assert.That(loaded.TestTools.RecentCommands, Is.Empty);

            File.WriteAllText(primary, "{\"version\":1,\"testTools\":{\"testCmdButtonPositionX\":0.77}}");
            loaded = legacyStore.LoadOrDefault(out result);
            Assert.That(loaded.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.77f));
            Assert.That(loaded.TestTools.TestCmdButtonPositionY, Is.EqualTo(0.11f));

            File.WriteAllText(primary, "{\"version\":1,\"testTools\":{\"testCmdButtonPositionX\":NaN,\"testCmdButtonPositionY\":Infinity}}");
            loaded = legacyStore.LoadOrDefault(out result);
            Assert.That(loaded.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.09f));
            Assert.That(loaded.TestTools.TestCmdButtonPositionY, Is.EqualTo(0.11f));

            File.WriteAllText(primary, "{\"version\":1,\"testTools\":{\"testCmdButtonPositionX\":-2,\"testCmdButtonPositionY\":3}}");
            loaded = legacyStore.LoadOrDefault(out result);
            Assert.That(loaded.TestTools.TestCmdButtonPositionX, Is.EqualTo(0f));
            Assert.That(loaded.TestTools.TestCmdButtonPositionY, Is.EqualTo(1f));

            var systemRoot = Path.Combine(tempRoot, "system");
            var systemStore = new SettingsFileStore(systemRoot);
            CreateGameArchitectureForTests(systemStore);
            var settings = Game.Interface.GetSystem<SettingsSystem>();
            var model = Game.Interface.GetModel<TestToolSettingsModel>();
            model.SetTestCmdButtonPosition(new Vector2(0.42f, 0.84f));
            Assert.That(model.RecordSubmittedCommand("#clone 1 1"), Is.True);
            Assert.That(model.RecordSubmittedCommand("$ping"), Is.True);
            settings.MarkDirty();
            Assert.That(settings.Flush(), Is.True);

            loaded = systemStore.LoadOrDefault(out result);
            Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedPrimary));
            Assert.That(loaded.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.42f).Within(0.0001f));
            Assert.That(loaded.TestTools.TestCmdButtonPositionY, Is.EqualTo(0.84f).Within(0.0001f));
            Assert.That(loaded.TestTools.RecentCommands, Is.EqualTo(new[] { "$ping", "#clone 1 1" }));

            Assert.That(systemStore.Save(loaded, out var error), Is.True, error);
            File.WriteAllText(systemStore.SettingsPath, "{ broken json");
            loaded = systemStore.LoadOrDefault(out result);
            Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedBackup));
            Assert.That(loaded.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.42f).Within(0.0001f));
            Assert.That(loaded.TestTools.TestCmdButtonPositionY, Is.EqualTo(0.84f).Within(0.0001f));
            Assert.That(loaded.TestTools.RecentCommands, Is.EqualTo(new[] { "$ping", "#clone 1 1" }));

            CreateUIRoot();
            var buttonPrefab = CreateTestCmdButtonPrefab();
            UIHelper.SetPrefabLoaderForTests(path => path == "Prefabs/UI/Diagnostics/TestCmdButton" ? buttonPrefab : null);
            model.SetTestCmdButtonPosition(new Vector2(0.88f, 0.91f));
            var buttonCtrl = UIHelper.ShowUI<TestCmdButtonUICtrl>();
            var buttonRect = GetBaseUI<TestCmdButtonUI>(buttonCtrl).transform as RectTransform;
            Assert.That(buttonRect.anchoredPosition.x, Is.GreaterThan(0f));

            Assert.That(settings.ResetAllToDefault(), Is.True);
            Assert.That(model.TestCmdButtonNormalizedPosition.Value, Is.EqualTo(new Vector2(0.09f, 0.11f)));
            Assert.That(model.RecentCommands, Is.Empty);
            loaded = systemStore.LoadOrDefault(out result);
            Assert.That(loaded.TestTools.RecentCommands, Is.Empty);
            Assert.That(buttonRect.anchoredPosition.x, Is.EqualTo(-960f + 1920f * 0.09f).Within(0.01f));
            Assert.That(buttonRect.anchoredPosition.y, Is.EqualTo(-540f + 1080f * 0.11f).Within(0.01f));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, Is.EqualTo(DisplayResolutionPresets.Default.Code));
            Assert.That(Game.Interface.GetModel<AudioSettingsModel>().ToData().MasterVolume, Is.EqualTo(1f));
            CollectionAssert.AreEquivalent(
                HotKeyDefaults.Create().Select(item => item.ActionId + item.BindingPath),
                Game.Interface.GetModel<HotKeyModel>().Bindings.Select(item => item.ActionId + item.BindingPath));
            Object.DestroyImmediate(buttonPrefab);

            var blockedRoot = Path.Combine(tempRoot, "blocked");
            File.WriteAllText(blockedRoot, "not a directory");
            var blockedStore = new SettingsFileStore(blockedRoot);
            CreateGameArchitectureForTests(blockedStore);
            settings = Game.Interface.GetSystem<SettingsSystem>();
            model = Game.Interface.GetModel<TestToolSettingsModel>();
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.GetModel<DisplaySettingsModel>().SetResolutionCode("1280x720");
            Game.Interface.GetModel<AudioSettingsModel>().Apply(new AudioSettingsData { MasterVolume = 0.4f, MusicVolume = 0.5f, SoundVolume = 0.6f });
            model.SetTestCmdButtonPosition(new Vector2(0.31f, 0.62f));
            settings.MarkDirty();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[SettingsSystem\] 保存设置失败：保存设置文件异常："));
            Assert.That(settings.Flush(), Is.False);
            Assert.That(Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Value, Is.EqualTo("en-US"));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, Is.EqualTo("1280x720"));
            Assert.That(Game.Interface.GetModel<AudioSettingsModel>().ToData().MasterVolume, Is.EqualTo(0.4f));

            File.Delete(blockedRoot);
            Assert.That(settings.Flush(), Is.True);
            loaded = blockedStore.LoadOrDefault(out result);
            Assert.That(result, Is.EqualTo(SettingsLoadResult.LoadedPrimary));
            Assert.That(loaded.TestTools.TestCmdButtonPositionX, Is.EqualTo(0.31f).Within(0.0001f));
            Assert.That(loaded.TestTools.TestCmdButtonPositionY, Is.EqualTo(0.62f).Within(0.0001f));
            Assert.That(loaded.TestTools.RecentCommands, Is.Empty);
        }

        private static void CreateGameArchitectureForTests(ISettingsFileStore store)
        {
            UIHelper.ResetForTests();
            UIRoot.ClearForTests();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<ISettingsFileStore>(store);
                architecture.RegisterUtility<IDisplayResolutionApplier>(new FakeDisplayApplier());
            };
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            Game.Interface.GetSystem<SettingsSystem>().Apply(SettingsData.CreateDefault());
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            ((RectTransform)root.transform).sizeDelta = new Vector2(1920f, 1080f);
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
                var rect = (RectTransform)layer.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.GetComponent<EventSystem>().UpdateModules();
        }

        private static GameObject CreateTestCmdButtonPrefab()
        {
            var prefab = new GameObject("TestCmdButton", typeof(RectTransform), typeof(Image), typeof(TestCmdButtonUI), typeof(TestCmdButtonPointerRelay));
            var rect = (RectTransform)prefab.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160f, 48f);
            return prefab;
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(ctrl);
        }

        private sealed class FakeDisplayApplier : IDisplayResolutionApplier
        {
            public void Apply(DisplayResolutionPreset preset, FullScreenMode windowMode)
            {
            }
        }

        private sealed class MinimalLocalizationLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                return path == LocalizationLanguages.LanguagesAddress
                    ? new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"Chinese\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T
                    : null;
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

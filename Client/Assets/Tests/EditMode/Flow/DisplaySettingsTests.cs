using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class DisplaySettingsTests
    {
        private FakeDisplayResolutionApplier applier;
        private FakeSettingsFileStore store;
        private FakeSettingsClock clock;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            applier = new FakeDisplayResolutionApplier();
            store = new FakeSettingsFileStore();
            clock = new FakeSettingsClock();
            Architecture<Game>.OnRegisterPatch = _ => { };
            CreateGameArchitectureForTests();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        [Test]
        public void Model_StartsWithRuntimeDefaults()
        {
            var model = Game.Interface.GetModel<DisplaySettingsModel>();

            Assert.That(model.CurrentResolutionCode.Value, Is.EqualTo("1600x900"));
            Assert.That(model.CurrentWindowMode.Value, Is.EqualTo(FullScreenMode.Windowed));
        }

        [Test]
        public void ApplyCommand_UsesWindowedOrFullscreenWindowOnly()
        {
            Game.Interface.SendCommand(new ApplyDisplaySettingsCommand
            {
                ResolutionCode = "1280x720",
                WindowMode = FullScreenMode.ExclusiveFullScreen
            });

            Assert.That(applier.LastPreset.Code, Is.EqualTo("1280x720"));
            Assert.That(applier.LastWindowMode, Is.EqualTo(FullScreenMode.Windowed));

            Game.Interface.SendCommand(new ApplyDisplaySettingsCommand
            {
                ResolutionCode = "1920x1080",
                WindowMode = FullScreenMode.FullScreenWindow
            });

            Assert.That(applier.LastPreset.Code, Is.EqualTo("1920x1080"));
            Assert.That(applier.LastWindowMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
        }

        [Test]
        public void UnityApplierResolveSize_UsesPresetForWindowedAndSanitizedModes()
        {
            var preset = DisplayResolutionPresets.FindOrDefault("1600x900");

            Assert.That(
                UnityDisplayResolutionApplier.ResolveSize(
                    preset,
                    FullScreenMode.Windowed,
                    2560,
                    1440),
                Is.EqualTo(new Vector2Int(1600, 900)));
            Assert.That(
                UnityDisplayResolutionApplier.ResolveSize(
                    preset,
                    FullScreenMode.ExclusiveFullScreen,
                    2560,
                    1440),
                Is.EqualTo(new Vector2Int(1600, 900)));
        }

        [Test]
        public void UnityApplierResolveSize_UsesValidDesktopForFullscreenWindow()
        {
            var preset = DisplayResolutionPresets.FindOrDefault("1280x720");

            Assert.That(
                UnityDisplayResolutionApplier.ResolveSize(
                    preset,
                    FullScreenMode.FullScreenWindow,
                    2560,
                    1440),
                Is.EqualTo(new Vector2Int(2560, 1440)));
        }

        [TestCase(0, 1440)]
        [TestCase(2560, 0)]
        public void UnityApplierResolveSize_FallsBackToPresetWhenDesktopSizeIsInvalid(
            int desktopWidth,
            int desktopHeight)
        {
            var preset = DisplayResolutionPresets.FindOrDefault("1920x1080");

            Assert.That(
                UnityDisplayResolutionApplier.ResolveSize(
                    preset,
                    FullScreenMode.FullScreenWindow,
                    desktopWidth,
                    desktopHeight),
                Is.EqualTo(new Vector2Int(1920, 1080)));
        }

        [Test]
        public void ApplyCommand_CommitFalse_DoesNotWriteSettings()
        {
            Game.Interface.SendCommand(new ApplyDisplaySettingsCommand
            {
                ResolutionCode = "1280x720",
                WindowMode = FullScreenMode.FullScreenWindow,
                Commit = false
            });

            Game.Interface.GetSystem<SettingsSystem>().Flush();
            Assert.That(store.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public void ApplyCommand_CommitTrue_MarksSettingsDirty()
        {
            Game.Interface.SendCommand(new ApplyDisplaySettingsCommand
            {
                ResolutionCode = "1920x1080",
                WindowMode = FullScreenMode.FullScreenWindow,
                Commit = true
            });

            Game.Interface.GetSystem<SettingsSystem>().Flush();

            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.That(store.LastSaved.Display.ResolutionCode, Is.EqualTo("1920x1080"));
            Assert.That(store.LastSaved.Display.WindowMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
        }

        [Test]
        public void LoadAndApply_AppliesSavedDisplayWithoutSaving()
        {
            store.DataToLoad = SettingsData.CreateDefault();
            store.DataToLoad.Display.ResolutionCode = "1920x1080";
            store.DataToLoad.Display.WindowMode = FullScreenMode.FullScreenWindow;
            store.LoadResult = SettingsLoadResult.LoadedPrimary;

            Game.Interface.GetSystem<SettingsSystem>().LoadAndApply();

            Assert.That(applier.LastPreset.Code, Is.EqualTo("1920x1080"));
            Assert.That(applier.LastWindowMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
            Assert.That(Game.Interface.GetModel<DisplaySettingsModel>().CurrentResolutionCode.Value, Is.EqualTo("1920x1080"));
            Assert.That(store.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public void LoadAndApply_AppliesCreatedDefaultWithoutSaving()
        {
            store.DataToLoad = SettingsData.CreateDefault();
            store.LoadResult = SettingsLoadResult.CreatedDefault;

            Game.Interface.GetSystem<SettingsSystem>().LoadAndApply();

            Assert.That(applier.LastPreset.Code, Is.EqualTo(DisplayResolutionPresets.Default.Code));
            Assert.That(applier.LastWindowMode, Is.EqualTo(FullScreenMode.Windowed));
            Assert.That(store.SaveCount, Is.EqualTo(0));
        }

        private static void ResetArchitecture()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, null);
            LifeCycle.ClearForTests();
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
            // SettingsSystem 的完整快照也包含测试工具与移动布局，测试宿主必须提供这些既有依赖。
            architecture.RegisterModel(new TestToolSettingsModel());
            architecture.RegisterModel(new MobileControlLayoutModel());
            architecture.RegisterUtility<IDisplayResolutionApplier>(applier);
            architecture.RegisterUtility<ISettingsFileStore>(store);
            architecture.RegisterUtility(new HotKeyActionRegistry());
            architecture.RegisterSystem(new HotKeySystem());
            architecture.RegisterSystem(new SettingsSystem());
            architecture.GetSystem<SettingsSystem>().SetClockForTests(clock);
        }

        private sealed class FakeDisplayResolutionApplier : IDisplayResolutionApplier
        {
            public System.Collections.Generic.List<string> Applied { get; } = new System.Collections.Generic.List<string>();
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
            public SettingsData DataToLoad { get; set; } = SettingsData.CreateDefault();
            public SettingsLoadResult LoadResult { get; set; } = SettingsLoadResult.CreatedDefault;
            public int SaveCount { get; private set; }
            public SettingsData LastSaved { get; private set; }

            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                result = LoadResult;
                return DataToLoad?.Clone() ?? SettingsData.CreateDefault();
            }

            public bool Save(SettingsData data, out string error)
            {
                SaveCount++;
                LastSaved = data.Clone();
                error = null;
                return true;
            }
        }

        private sealed class FakeSettingsClock : ISettingsClock
        {
            public float Now { get; set; }
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

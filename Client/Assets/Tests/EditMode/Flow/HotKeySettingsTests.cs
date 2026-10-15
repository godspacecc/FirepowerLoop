using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Reflection;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class HotKeySettingsTests
    {
        private const string LegacyCloseTopActionId = "ui.close-top";

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
        }

        [Test]
        public void DefaultsDoNotBindLegacyRemovedUiActionToEscape()
        {
            var model = new HotKeyModel();
            ((QFramework.ICanInit)model).Init();

            Assert.IsFalse(model.TryGet(LegacyCloseTopActionId, out _));
            Assert.IsTrue(model.TryGet(HotKeyActionIds.TogglePause, out var pause));
            Assert.That(pause.BindingPath, Is.EqualTo(HotKeyDefaults.Escape));
            Assert.That(model.Bindings.Count(item => item.BindingPath == HotKeyDefaults.Escape), Is.EqualTo(1));
            Assert.IsFalse(model.TryGet("map.toggle", out _));
            Assert.IsFalse(model.TryGet("flashlight.toggle", out _));
        }

        [Test]
        public void SanitizeDropsLegacyRemovedUiBinding()
        {
            var sanitized = SettingsData.SanitizeHotKeys(new[]
            {
                new HotKeyBinding { ActionId = LegacyCloseTopActionId, Context = HotKeyContext.UI, BindingPath = HotKeyDefaults.Escape }
            });

            Assert.IsFalse(sanitized.Any(item => item.ActionId == LegacyCloseTopActionId));
            Assert.IsTrue(sanitized.Any(item => item.ActionId == HotKeyActionIds.TogglePause));
            Assert.IsTrue(sanitized.Any(item => item.ActionId == HotKeyActionIds.ToggleRuntimeLog));
            Assert.IsFalse(sanitized.Any(item => item.ActionId == "map.toggle"));
            Assert.IsFalse(sanitized.Any(item => item.ActionId == "flashlight.toggle"));
            Assert.IsFalse(sanitized.Any(item => item.ActionId == "test-command.toggle"));
        }

        [Test]
        public void RegistryExecutesHighestPriorityExecutableCandidate()
        {
            var registry = new HotKeyActionRegistry();
            var executed = string.Empty;
            registry.Register(new HotKeyAction
            {
                ActionId = "low",
                Priority = 1,
                CanExecute = () => true,
                Execute = () => executed = "low"
            });
            registry.Register(new HotKeyAction
            {
                ActionId = "high-blocked",
                Priority = 100,
                CanExecute = () => false,
                Execute = () => executed = "blocked"
            });
            registry.Register(new HotKeyAction
            {
                ActionId = "high",
                Priority = 50,
                CanExecute = () => true,
                Execute = () => executed = "high"
            });

            Assert.IsTrue(registry.TryExecute(new[] { "low", "high-blocked", "high" }));
            Assert.That(executed, Is.EqualTo("high"));
        }

        [Test]
        public void ModelCanConfirmConflictReplacementBySwappingOldBinding()
        {
            var model = new HotKeyModel();
            ((ICanInit)model).Init();
            model.Apply(new[]
            {
                new HotKeyBinding { ActionId = "ui.primary", Context = HotKeyContext.UI, BindingPath = HotKeyDefaults.Escape },
                new HotKeyBinding { ActionId = "ui.other", Context = HotKeyContext.UI, BindingPath = HotKeyDefaults.F1 }
            });

            Assert.IsTrue(model.TryReplace(new HotKeyBinding
            {
                ActionId = "ui.primary",
                Context = HotKeyContext.UI,
                BindingPath = HotKeyDefaults.F1
            }, replaceConflict: true));

            Assert.IsTrue(model.TryGet("ui.primary", out var primary));
            Assert.IsTrue(model.TryGet("ui.other", out var other));
            Assert.That(primary.BindingPath, Is.EqualTo(HotKeyDefaults.F1));
            Assert.That(other.BindingPath, Is.EqualTo(HotKeyDefaults.Escape));
        }

        [Test]
        public void HotKeySystemCaptureCanCancelAndConfirmReplacement()
        {
            ResetArchitecture();
            var model = Game.Interface.GetModel<HotKeyModel>();
            model.Apply(new[]
            {
                new HotKeyBinding { ActionId = "ui.primary", Context = HotKeyContext.UI, BindingPath = HotKeyDefaults.Escape },
                new HotKeyBinding { ActionId = "ui.other", Context = HotKeyContext.UI, BindingPath = HotKeyDefaults.F1 }
            });
            var system = Game.Interface.GetSystem<HotKeySystem>();
            system.RebuildActions();

            Assert.IsTrue(system.BeginRebind("ui.primary").Success);
            var cancelled = system.ReceiveBindingPath(HotKeyDefaults.Escape);
            Assert.IsFalse(cancelled.Success);
            Assert.IsFalse(system.IsCapturingRebind);

            Assert.IsTrue(system.BeginRebind("ui.primary").Success);
            var conflict = system.ReceiveBindingPath(HotKeyDefaults.F1);
            Assert.IsTrue(conflict.NeedsConfirmation);
            Assert.That(conflict.ConflictingActionId, Is.EqualTo("ui.other"));

            var confirmed = system.ConfirmPendingBinding(replaceConflict: true);
            Assert.IsTrue(confirmed.Success, confirmed.Error);
            Assert.IsTrue(model.TryGet("ui.primary", out var primary));
            Assert.That(primary.BindingPath, Is.EqualTo(HotKeyDefaults.F1));
        }

        [Test]
        public void CancelConflictReplacementKeepsOldBindings()
        {
            ResetArchitecture();
            var model = Game.Interface.GetModel<HotKeyModel>();
            model.Apply(new[]
            {
                new HotKeyBinding { ActionId = "ui.primary", Context = HotKeyContext.UI, BindingPath = HotKeyDefaults.Escape },
                new HotKeyBinding { ActionId = "ui.other", Context = HotKeyContext.UI, BindingPath = HotKeyDefaults.F1 }
            });
            var system = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(system.BeginRebind("ui.primary").Success);
            Assert.IsTrue(system.ReceiveBindingPath(HotKeyDefaults.F1).NeedsConfirmation);
            system.CancelRebind();

            Assert.IsTrue(model.TryGet("ui.primary", out var primary));
            Assert.IsTrue(model.TryGet("ui.other", out var other));
            Assert.That(primary.BindingPath, Is.EqualTo(HotKeyDefaults.Escape));
            Assert.That(other.BindingPath, Is.EqualTo(HotKeyDefaults.F1));
        }

        [Test]
        public void SuccessfulCaptureRebuildsActionsAndMarksSettingsDirty()
        {
            ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new MinimalLocalizationLoader());
            LocalizationLanguages.ResetForTests();
            var store = new FakeSettingsStore();
            Architecture<Game>.OnRegisterPatch = architecture => architecture.RegisterUtility<ISettingsFileStore>(store);
            var system = Game.Interface.GetSystem<HotKeySystem>();

            Assert.IsTrue(system.BeginRebind(HotKeyActionIds.ToggleRuntimeLog).Success);
            Assert.IsTrue(system.ReceiveBindingPath("<Keyboard>/f2").Success);
            Game.Interface.GetSystem<SettingsSystem>().Flush();

            Assert.That(store.SaveCount, Is.EqualTo(1));
            Assert.IsFalse(system.ExecuteBinding(HotKeyDefaults.F1));
        }

        [Test]
        public void CaptureModeShieldsNormalUpdateExecutionUntilAKeyIsCaptured()
        {
            var source = File.ReadAllText(Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Assets/Game/Scripts/System/Input/HotKeySystem.cs"));

            StringAssert.IsMatch(@"if\s*\(isCapturing\)[\s\S]*CapturePressedKey\(\);[\s\S]*return;", source);
        }

        [Test]
        public void HotKeyActionsAreCreatedAsButtonActions()
        {
            var source = File.ReadAllText(Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Assets/Game/Scripts/System/Input/HotKeySystem.cs"));

            StringAssert.Contains("InputActionType.Button", source);
            StringAssert.IsMatch(
                @"new\s+InputAction\s*\(\s*\$""HotKey:\{path\}""\s*,\s*InputActionType\.Button\s*,\s*binding:\s*path\s*\)",
                source);
        }

        [Test]
        public void HotKeyItemPrefabExists()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Prefabs/UI/Home/Box/HotKeyItemBox.prefab")));
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }

        private sealed class FakeSettingsStore : ISettingsFileStore
        {
            public int SaveCount { get; private set; }

            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                result = SettingsLoadResult.CreatedDefault;
                return SettingsData.CreateDefault();
            }

            public bool Save(SettingsData data, out string error)
            {
                SaveCount++;
                error = null;
                return true;
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
                return asset != null ? new ResourceLoadHandle(asset) : ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}

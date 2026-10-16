using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class RuntimeLogTests
    {
        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
        }

        [Test]
        public void EmptyQueueDrainReturnsZeroWithoutModelEvent()
        {
            var system = Game.Interface.GetSystem<RuntimeLogSystem>();
            var model = Game.Interface.GetModel<RuntimeLogModel>();
            var eventCount = 0;
            Game.Interface.RegisterEvent<RuntimeLogChangedEvent>(_ => eventCount++);

            Assert.That(system.DrainForTests(100), Is.EqualTo(0));
            Assert.That(model.Snapshot(), Is.Empty);
            Assert.That(eventCount, Is.EqualTo(0));
        }

        [Test]
        public void QueueDrainsToModelAndKeepsCapacity()
        {
            var system = Game.Interface.GetSystem<RuntimeLogSystem>();
            for (var i = 0; i < 505; i++)
            {
                system.EnqueueForTests(new RuntimeLogEntry(DateTime.UnixEpoch.AddSeconds(i), LogType.Log, $"m{i}", ""));
            }

            Assert.That(system.DrainForTests(600), Is.EqualTo(505));
            var snapshot = Game.Interface.GetModel<RuntimeLogModel>().Snapshot();
            Assert.That(snapshot.Count, Is.EqualTo(500));
            Assert.That(snapshot.First().Message, Is.EqualTo("m5"));
            Assert.That(snapshot.Last().Message, Is.EqualTo("m504"));
        }

        [Test]
        public void ModelAddRangeSendsOneEvent()
        {
            var model = Game.Interface.GetModel<RuntimeLogModel>();
            var eventCount = 0;
            Game.Interface.RegisterEvent<RuntimeLogChangedEvent>(_ => eventCount++);

            model.AddRange(new[]
            {
                new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Log, "a", ""),
                new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Warning, "b", "")
            });

            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(model.Snapshot().Count, Is.EqualTo(2));
        }

        [Test]
        public void SystemClearEmptiesModelAndSendsExistingChangedEvent()
        {
            var model = Game.Interface.GetModel<RuntimeLogModel>();
            model.AddRange(new[]
            {
                new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Log, "a", ""),
                new RuntimeLogEntry(DateTime.UnixEpoch.AddSeconds(1), LogType.Warning, "b", "")
            });

            var eventCount = 0;
            RuntimeLogChangedEvent lastEvent = default;
            Game.Interface.RegisterEvent<RuntimeLogChangedEvent>(evt =>
            {
                eventCount++;
                lastEvent = evt;
            });

            Game.Interface.GetSystem<RuntimeLogSystem>().Clear();

            Assert.That(model.Snapshot(), Is.Empty);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(lastEvent.Entries, Is.Empty);
        }

        [Test]
        public void SystemClearDoesNotDiscardPendingEntries()
        {
            var system = Game.Interface.GetSystem<RuntimeLogSystem>();
            var model = Game.Interface.GetModel<RuntimeLogModel>();
            system.EnqueueForTests(new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Error, "pending", ""));
            model.Add(new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Log, "visible", ""));

            system.Clear();
            Assert.That(model.Snapshot(), Is.Empty);

            Assert.That(system.DrainForTests(10), Is.EqualTo(1));
            var snapshot = model.Snapshot();
            Assert.That(snapshot.Count, Is.EqualTo(1));
            Assert.That(snapshot[0].Message, Is.EqualTo("pending"));
        }

        [Test]
        public void RuntimeLogClearButtonUsesSystemOwner()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var source = File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/Controller/UICtrl/Diagnostics/RuntimeLogUICtrl.cs"));
            var match = Regex.Match(source, @"private\s+void\s+OnClearClick\s*\(\s*\)\s*\{(?<body>[\s\S]*?)\n\s*\}");

            Assert.That(match.Success, Is.True);
            var body = match.Groups["body"].Value;
            Assert.That(body, Does.Contain("this.GetSystem<RuntimeLogSystem>().Clear();"));
            Assert.That(body, Does.Not.Contain("GetModel<RuntimeLogModel>()"));
        }

        [Test]
        public void BackpressureDropsLogsBeforeWarnings()
        {
            var system = Game.Interface.GetSystem<RuntimeLogSystem>();
            for (var i = 0; i < 2100; i++)
            {
                system.EnqueueForTests(new RuntimeLogEntry(DateTime.UnixEpoch.AddSeconds(i), LogType.Log, $"log{i}", ""));
            }

            system.EnqueueForTests(new RuntimeLogEntry(DateTime.UnixEpoch.AddSeconds(3000), LogType.Error, "important", ""));
            system.DrainForTests(2100);

            var snapshot = Game.Interface.GetModel<RuntimeLogModel>().Snapshot();
            Assert.That(snapshot.Any(item => item.Message == "important"), Is.True);
            Assert.That(snapshot.Any(item => item.Message.Contains("RuntimeLog dropped")), Is.True);
        }

        [Test]
        public void FormatterIncludesRuntimeHeaderAndEntries()
        {
            var text = RuntimeLogTextFormatter.Format(new[]
            {
                new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Warning, "watch this", "stack")
            }, AppState.Playing);

            Assert.That(text, Does.Contain("Application.version"));
            Assert.That(text, Does.Contain("AppState: Playing"));
            Assert.That(text, Does.Contain("watch this"));
            Assert.That(text, Does.Contain("stack"));
        }

        [Test]
        public void AutomaticErrorLogPersistsAndRotatesWithoutRuntimeUi()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FirepowerLoop-RuntimeErrorLogTests-" + Guid.NewGuid());
            try
            {
                var store = new RuntimeErrorLogStore(directory, 120);
                var first = new RuntimeLogEntry(DateTime.UnixEpoch, LogType.Error, "first-error", "first-stack");
                var second = new RuntimeLogEntry(DateTime.UnixEpoch.AddSeconds(1), LogType.Exception,
                    "second-error", "second-stack");
                Assert.That(store.Append(first), Is.True);
                Assert.That(store.Append(second), Is.True);

                var current = Path.Combine(directory, "runtime-error.log");
                var previous = Path.Combine(directory, "runtime-error.previous.log");
                Assert.That(File.ReadAllText(current), Does.Contain("second-error"));
                Assert.That(File.ReadAllText(current), Does.Contain("second-stack"));
                Assert.That(File.ReadAllText(previous), Does.Contain("first-error"));
                Assert.That(new FileInfo(current).Length, Is.LessThanOrEqualTo(120));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RuntimeLogSystemAutomaticallyPersistsErrorsButNotNormalLogs()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FirepowerLoop-RuntimeErrorLogTests-" + Guid.NewGuid());
            try
            {
                var system = Game.Interface.GetSystem<RuntimeLogSystem>();
                var field = typeof(RuntimeLogSystem).GetField("errorLogStore", BindingFlags.Instance | BindingFlags.NonPublic);
                var callback = typeof(RuntimeLogSystem).GetMethod("OnLogReceived", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null);
                Assert.That(callback, Is.Not.Null);
                field.SetValue(system, new RuntimeErrorLogStore(directory));

                callback.Invoke(system, new object[] { "ordinary", "", LogType.Log });
                Assert.That(File.Exists(Path.Combine(directory, RuntimeErrorLogStore.CurrentFileName)), Is.False);

                callback.Invoke(system, new object[] { "runtime failure", "failure stack", LogType.Error });
                var text = File.ReadAllText(Path.Combine(directory, RuntimeErrorLogStore.CurrentFileName));
                Assert.That(text, Does.Contain("runtime failure"));
                Assert.That(text, Does.Contain("failure stack"));
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RuntimeLogPrefabsExist()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets/Game/Prefabs/UI/Diagnostics/Box/RuntimeLogItemBox.prefab")));
        }

        [Test]
        public void RuntimeLogRegistrationAndHotKeyAreIncludedInDiagnosticRelease()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var gameSource = File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/Game.cs"));
            var hotKeySource = File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/System/Input/HotKeySystem.cs"));

            Assert.That(gameSource, Does.Contain("RegisterSystem(new RuntimeLogSystem());"));
            Assert.That(hotKeySource, Does.Contain("HotKeyActionIds.ToggleRuntimeLog"));
            StringAssert.DoesNotMatch(
                @"#if\s+UNITY_EDITOR\s*\|\|\s*DEVELOPMENT_BUILD[\s\S]*RegisterSystem\(new RuntimeLogSystem\(\)\);[\s\S]*#endif",
                gameSource);
            StringAssert.DoesNotMatch(
                @"#if\s+UNITY_EDITOR\s*\|\|\s*DEVELOPMENT_BUILD[\s\S]*HotKeyActionIds\.ToggleRuntimeLog[\s\S]*#endif",
                hotKeySource);
        }

        [Test]
        public void RuntimeLogControllerUsesSerializedButtonsAndDetailOnly()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var source = File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/Controller/UICtrl/Diagnostics/RuntimeLogUICtrl.cs"));
            var prefab = File.ReadAllText(Path.Combine(root, "Assets/Game/Prefabs/UI/Diagnostics/RuntimeLog.prefab"));
            var view = File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/Controller/UI/Diagnostics/RuntimeLogUI.cs"));
            var generated = File.ReadAllText(Path.Combine(root, "Assets/Game/Scripts/Utility/Localization/TextsGenerated.cs"));

            Assert.That(source, Does.Not.Contain("EnsureRuntimeControls"));
            Assert.That(source, Does.Not.Contain("new GameObject"));
            Assert.That(source, Does.Not.Contain("AddComponent<Toggle>"));
            Assert.That(source, Does.Not.Contain("LanguageChangedEvent"));
            Assert.That(source, Does.Not.Contain("SetToggleText"));
            Assert.That(source, Does.Contain("item.TextMessage.text = entry.Message;"));
            Assert.That(source, Does.Not.Contain("item.TextMessage.text = string.IsNullOrWhiteSpace(entry.StackTrace)"));
            AssertNotSerializedReference(prefab, "TitleText");
            Assert.That(prefab, Does.Not.Contain("- key: TitleText"));
            Assert.That(view, Does.Not.Contain("TitleText"));
            AssertSerializedReference(prefab, "BtnAll");
            AssertSerializedReference(prefab, "BtnNormal");
            AssertSerializedReference(prefab, "BtnWarning");
            AssertSerializedReference(prefab, "BtnError");
            AssertSerializedReference(prefab, "DetailScrollRect");
            AssertSerializedReference(prefab, "DetailText");
            Assert.That(prefab, Does.Not.Contain("ToggleLog"));
            Assert.That(prefab, Does.Not.Contain("ToggleException"));
            Assert.That(generated, Does.Not.Contain("ui.runtime_log."));
            Assert.That(generated, Does.Not.Contain("RuntimeLogCopy"));
            Assert.That(generated, Does.Not.Contain("RuntimeLogExport"));
            Assert.That(generated, Does.Not.Contain("RuntimeLogFilter"));
            Assert.That(generated, Does.Not.Contain("RuntimeLogTitle"));
            Assert.That(generated, Does.Contain("HotkeyToggleRuntimeLog"));
        }

        private static void AssertSerializedReference(string yaml, string fieldName)
        {
            StringAssert.IsMatch($@"{fieldName}:\s*\{{fileID:\s*(?!0\b)\d+", yaml);
        }

        private static void AssertNotSerializedReference(string yaml, string fieldName)
        {
            StringAssert.DoesNotMatch($@"{fieldName}:\s*\{{fileID:\s*(?!0\b)\d+", yaml);
        }

        private static void ResetArchitecture()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, null);
        }
    }
}

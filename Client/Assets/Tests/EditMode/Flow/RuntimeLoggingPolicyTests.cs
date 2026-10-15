using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class RuntimeLoggingPolicyTests
    {
        private static readonly string[] RuntimeStartupFiles =
        {
            "Assets/Boot/Launcher.cs",
            "Assets/Boot/Res/CodeLoader.cs",
            "Assets/Boot/Res/ResourceBoot.cs",
            "Assets/Boot/Res/ResourceSceneLoader.cs",
            "Assets/Game/Scripts/GameStart.cs",
            "Assets/Game/Scripts/MissingCharCollector.cs",
            "Assets/Game/Scripts/Utility/Res/DataLoader.cs"
        };

        private static readonly Regex OrdinaryDebugLog = new Regex(
            @"\b(?:Debug\.Log|Log\.Debug)\s*\(", RegexOptions.Compiled);

        [Test]
        public void AutomaticStartupFilesDoNotContainOrdinaryDebugLogs()
        {
            var violations = new List<string>();
            foreach (var assetPath in RuntimeStartupFiles)
            {
                var source = ReadAsset(assetPath);
                var lines = source.Split('\n');
                for (var index = 0; index < lines.Length; index++)
                {
                    if (OrdinaryDebugLog.IsMatch(lines[index]))
                    {
                        violations.Add($"{assetPath}:{index + 1}:{lines[index].Trim()}");
                    }
                }
            }

            Assert.That(violations, Is.Empty, string.Join("\n", violations));
        }

        [Test]
        public void DataLoaderFailureLogKeepsFullException()
        {
            var source = ReadAsset("Assets/Game/Scripts/Utility/Res/DataLoader.cs");
            Assert.That(source, Does.Contain("配置初始化失败：{exception}"));
            Assert.That(source, Does.Not.Contain("配置初始化失败：{exception.Message}"));
        }

        [Test]
        public void GameStartInitializesArchitectureOnceInAwake()
        {
            var source = ReadAsset("Assets/Game/Scripts/GameStart.cs");
            Assert.That(Regex.Matches(source, @"_\s*=\s*Game\.Interface\s*;").Count, Is.EqualTo(1));
            Assert.That(Regex.IsMatch(
                source,
                @"private\s+void\s+Awake\s*\(\s*\)\s*\{(?:(?!\}).)*_\s*=\s*Game\.Interface\s*;",
                RegexOptions.Singleline), Is.True);
        }

        [Test]
        public void GameStartInitializesRegisteredLocalizationUtilityWithoutReplacingIt()
        {
            var source = ReadAsset("Assets/Game/Scripts/GameStart.cs");
            Assert.That(source, Does.Not.Contain("RegisterUtility(new LocalizationUtility"));
            Assert.That(source, Does.Contain("GetUtility<LocalizationUtility>()"));
            Assert.That(source, Does.Contain("InitializeConfiguredTables()"));
        }

        [Test]
        public void GameStartExitDeinitializesBeforeQuittingSharedLifecycle()
        {
            var source = ReadAsset("Assets/Game/Scripts/GameStart.cs");
            Assert.That(source, Does.Not.Contain("GetSystem<SaveSystem>"));
            Assert.That(source.IndexOf("architecture.Deinit()", System.StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(source.IndexOf("LifeCycle.Quit()", System.StringComparison.Ordinal), Is.GreaterThan(source.IndexOf("architecture.Deinit()", System.StringComparison.Ordinal)));
            var settings = ReadAsset("Assets/Game/Scripts/System/Settings/SettingsSystem.cs");
            Assert.That(settings, Does.Contain("Flush()"));
        }

        private static string ReadAsset(string assetPath)
        {
            var fullPath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
            Assert.That(File.Exists(fullPath), Is.True, assetPath);
            return File.ReadAllText(fullPath);
        }
    }
}

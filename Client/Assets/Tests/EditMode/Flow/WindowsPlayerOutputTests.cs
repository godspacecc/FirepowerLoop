using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    [TestFixture]
    public class WindowsPlayerOutputTests
    {
        private const string BurstSuffix = "_BurstDebugInformation_DoNotShip";
        private const string Il2CppSuffix = "_BackUpThisFolder_ButDontShipItWithYourGame";
        private static readonly Type BuildUtilityType = Type.GetType("Game.Editor.BuildUtility, Assembly-CSharp-Editor");

        [Test]
        public void BatchPathUsesInjectedTimestampAndPreservesOutputRoot()
        {
            string outputRoot = Path.Combine(Path.GetTempPath(), "FirepowerLoopWindowsOutputTests", Guid.NewGuid().ToString("N"));
            try
            {
                string path = (string)InvokePrivate("GetWindowsPlayerBatchDirectory", outputRoot, new DateTime(2026, 8, 28, 9, 7, 6));

                Assert.That(path, Is.EqualTo(Path.GetFullPath(Path.Combine(outputRoot, "20260828_090706"))));
                Assert.That(Directory.Exists(outputRoot), Is.False);
            }
            finally
            {
                DeleteOwnedRoot(outputRoot);
            }
        }

        [Test]
        public void SeparationKeepsPlayerFilesAndMovesDebugDirectories()
        {
            string batchRoot = CreateBatchRoot();
            try
            {
                string playerDirectory = Path.Combine(batchRoot, "FirepowerLoop");
                Directory.CreateDirectory(playerDirectory);
                File.WriteAllText(Path.Combine(playerDirectory, "FirepowerLoop.exe"), "player");
                Directory.CreateDirectory(Path.Combine(playerDirectory, "FirepowerLoop_Data"));
                File.WriteAllText(Path.Combine(playerDirectory, "FirepowerLoop_Data", "keep.txt"), "keep");
                CreateDebugDirectory(playerDirectory, "Space Name" + BurstSuffix, "burst-marker");
                CreateDebugDirectory(playerDirectory, "FirepowerLoop" + Il2CppSuffix, "il2cpp-marker");

                string symbolsDirectory = (string)InvokePrivate("SeparateWindowsPlayerDebugSymbols", playerDirectory);

                Assert.That(File.Exists(Path.Combine(playerDirectory, "FirepowerLoop.exe")), Is.True);
                Assert.That(File.Exists(Path.Combine(playerDirectory, "FirepowerLoop_Data", "keep.txt")), Is.True);
                Assert.That(Directory.GetDirectories(playerDirectory, "*" + BurstSuffix, SearchOption.TopDirectoryOnly), Is.Empty);
                Assert.That(Directory.GetDirectories(playerDirectory, "*" + Il2CppSuffix, SearchOption.TopDirectoryOnly), Is.Empty);
                Assert.That(File.ReadAllText(Path.Combine(symbolsDirectory, "Space Name" + BurstSuffix, "marker.txt")), Is.EqualTo("burst-marker"));
                Assert.That(File.ReadAllText(Path.Combine(symbolsDirectory, "FirepowerLoop" + Il2CppSuffix, "marker.txt")), Is.EqualTo("il2cpp-marker"));
            }
            finally
            {
                DeleteOwnedRoot(batchRoot);
            }
        }

        [TestCase("MissingIl2Cpp")]
        [TestCase("DuplicateBurst")]
        [TestCase("DuplicateIl2Cpp")]
        [TestCase("SymbolsExists")]
        public void SeparationPreflightFailureDoesNotMoveAnyDirectory(string failure)
        {
            string batchRoot = CreateBatchRoot();
            try
            {
                string playerDirectory = Path.Combine(batchRoot, "FirepowerLoop");
                Directory.CreateDirectory(playerDirectory);
                if (failure != "MissingBurst")
                    CreateDebugDirectory(playerDirectory, "FirepowerLoop" + BurstSuffix, "primary-burst");
                if (failure != "MissingIl2Cpp")
                    CreateDebugDirectory(playerDirectory, "FirepowerLoop" + Il2CppSuffix, "primary-il2cpp");
                if (failure == "DuplicateBurst")
                    CreateDebugDirectory(playerDirectory, "Alternate" + BurstSuffix, "duplicate-burst");
                if (failure == "DuplicateIl2Cpp")
                    CreateDebugDirectory(playerDirectory, "Alternate" + Il2CppSuffix, "duplicate-il2cpp");
                if (failure == "SymbolsExists")
                    Directory.CreateDirectory(Path.Combine(batchRoot, "Symbols"));

                Dictionary<string, string> before = SnapshotDebugDirectories(playerDirectory);

                var exception = Assert.Throws<TargetInvocationException>(() => InvokePrivate("SeparateWindowsPlayerDebugSymbols", playerDirectory));
                Assert.That(exception.InnerException.Message, Does.Contain(batchRoot));
                Dictionary<string, string> after = SnapshotDebugDirectories(playerDirectory);
                Assert.That(after.Count, Is.EqualTo(before.Count));
                CollectionAssert.AreEquivalent(before.Keys, after.Keys);
                foreach (var entry in before)
                    Assert.That(after[entry.Key], Is.EqualTo(entry.Value), $"预检失败后调试目录内容发生变化：{entry.Key}");
            }
            finally
            {
                DeleteOwnedRoot(batchRoot);
            }
        }

        [Test]
        public void SeparationAllowsMissingBurstForScriptOnlyBuild()
        {
            string batchRoot = CreateBatchRoot();
            try
            {
                string playerDirectory = Path.Combine(batchRoot, "FirepowerLoop");
                Directory.CreateDirectory(playerDirectory);
                File.WriteAllText(Path.Combine(playerDirectory, "FirepowerLoop.exe"), "player");
                CreateDebugDirectory(playerDirectory, "FirepowerLoop" + Il2CppSuffix, "il2cpp-marker");

                LogAssert.Expect(LogType.Warning,
                    $"[BuildUtility] 本次 Player 未生成 Burst 调试资料目录，按 script-only build 产物继续：{batchRoot}");
                string symbolsDirectory = (string)InvokePrivate("SeparateWindowsPlayerDebugSymbols", playerDirectory);

                Assert.That(File.Exists(Path.Combine(playerDirectory, "FirepowerLoop.exe")), Is.True);
                Assert.That(Directory.GetDirectories(playerDirectory, "*" + BurstSuffix, SearchOption.TopDirectoryOnly), Is.Empty);
                Assert.That(Directory.GetDirectories(playerDirectory, "*" + Il2CppSuffix, SearchOption.TopDirectoryOnly), Is.Empty);
                Assert.That(File.ReadAllText(Path.Combine(symbolsDirectory, "FirepowerLoop" + Il2CppSuffix, "marker.txt")), Is.EqualTo("il2cpp-marker"));
            }
            finally
            {
                DeleteOwnedRoot(batchRoot);
            }
        }

        [Test]
        public void OnlyRealPlayerWorkflowsUseSeparatedOutput()
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Build/BuildUtility.cs"));
            string buildWindows = Extract(source, "private static string BuildWindowsPlayer()", "private static void BuildPlayer");
            Assert.That(buildWindows, Does.Contain("GetWindowsPlayerBatchDirectory(outputRoot, DateTime.Now)"));
            Assert.That(IndexOfOrFail(buildWindows, "if (Directory.Exists(batchDirectory) || File.Exists(batchDirectory))"), Is.LessThan(IndexOfOrFail(buildWindows, "BuildPlayer(exePath)")));
            Assert.That(buildWindows, Does.Contain("SeparateWindowsPlayerDebugSymbols(playerDirectory)"));
            Assert.That(buildWindows, Does.Not.Contain("DeleteDirectory(outputRoot)"));
            Assert.That(Extract(source, "private static string BuildPreparatoryPlayer()", "private static void CleanupPreparatoryPlayer"), Does.Not.Contain("SeparateWindowsPlayerDebugSymbols"));
            Assert.That(Extract(source, "public static void BuildHostCodeAndResourcesBatch()", "public static void BuildRecentHostPlayerBatch()"), Does.Not.Contain("BuildWindowsPlayer"));
            Assert.That(Extract(source, "public static void BuildBuiltinResourcesOnlyBatch()", "public static void StartResourceServerFromLatestPackage()"), Does.Not.Contain("BuildWindowsPlayer"));
            Assert.That(Extract(source, "private static string CreateVersion()", "private static string GetBuildStateRoot()"), Does.Contain("yyyyMMddHHmm"));
            Assert.That(CountOccurrences(source, "OfferStartServer(version, package.OutputPackageDirectory,"), Is.EqualTo(3));
            string dialog = Extract(source, "private static void OfferStartServer", "private static bool Confirm");
            Assert.That(dialog, Does.Contain("state.LastPlayer?.PlayerPath"));
            Assert.That(dialog, Does.Contain("Path.GetDirectoryName(playerPath)"));
            Assert.That(dialog, Does.Contain("Path.Combine(Directory.GetParent(playerDirectory).FullName, \"Symbols\")"));
            Assert.That(dialog, Does.Contain("玩家目录"));
            Assert.That(dialog, Does.Contain("调试资料目录"));
            Assert.That(dialog, Does.Contain("资源目录"));
            AssertPlayerWorkflowSaveOrder(source, "public static void BuildLatestCodeAndResourcesBatch()", "public static void BuildHostCodeAndResourcesBatch()", "SaveSuccessfulState(version, \"Full\", package.OutputPackageDirectory, player", false);
            AssertPlayerWorkflowSaveOrder(source, "public static void BuildRecentHostPlayerBatch()", "public static void BuildQuickAotPlayerBatch()", "state.LastPlayer = NewPlayerRecord(version, \"RecentHost\", player)", true);
            AssertPlayerWorkflowSaveOrder(source, "public static void BuildQuickAotPlayerBatch()", "public static void BuildBuiltinResourcesOnlyBatch()", "state.LastPlayer = NewPlayerRecord(version, \"QuickAot\", player)", true);
        }

        private static string CreateBatchRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "FirepowerLoopWindowsOutputTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void CreateDebugDirectory(string playerDirectory, string name, string marker)
        {
            string directory = Path.Combine(playerDirectory, name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "marker.txt"), marker);
        }

        private static Dictionary<string, string> SnapshotDebugDirectories(string playerDirectory)
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string directory in Directory.GetDirectories(playerDirectory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetFileName(path).EndsWith(BurstSuffix, StringComparison.Ordinal) || Path.GetFileName(path).EndsWith(Il2CppSuffix, StringComparison.Ordinal)))
            {
                string marker = string.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .Select(path => path.Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "=" + File.ReadAllText(path)));
                snapshot.Add(Path.GetFileName(directory), marker);
            }
            return snapshot;
        }

        private static void AssertPlayerWorkflowSaveOrder(string source, string startMarker, string endMarker, string playerRecordMarker, bool hasSeparateStateSave)
        {
            string workflow = Extract(source, startMarker, endMarker);
            int validate = IndexOfOrFail(workflow, "ValidatePlayerAndBuiltin(player, version)");
            int playerRecord = IndexOfOrFail(workflow, playerRecordMarker);
            int save = hasSeparateStateSave ? IndexOfOrFail(workflow, "BuildStateStore.Save(state)") : playerRecord;
            int offer = IndexOfOrFail(workflow, "OfferStartServer(version, package.OutputPackageDirectory,");
            Assert.That(validate, Is.LessThan(save));
            if (hasSeparateStateSave)
                Assert.That(playerRecord, Is.LessThan(save));
            Assert.That(save, Is.LessThan(offer));
        }

        private static object InvokePrivate(string name, params object[] arguments)
        {
            Assert.That(BuildUtilityType, Is.Not.Null);
            MethodInfo method = BuildUtilityType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"缺少私有实现: {name}");
            return method.Invoke(null, arguments);
        }

        private static string Extract(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            return source.Substring(start, end - start);
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int start = 0;
            while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
            {
                count++;
                start += value.Length;
            }
            return count;
        }

        private static int IndexOfOrFail(string source, string value)
        {
            int index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"缺少接线：{value}");
            return index;
        }

        private static void DeleteOwnedRoot(string root)
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}

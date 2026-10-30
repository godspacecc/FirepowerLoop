using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ResourceServerPublishTests
    {
        private string root;
        private readonly List<Process> serverProcesses = new List<Process>();

        private Type PublishUtilityType => RequiredType("Game.Editor.ResourceServerPublishUtility");
        private Type NoticeEditorUtilityType => RequiredType("Game.Editor.ResourceUpdateNoticeEditorUtility");
        private Type NoticeEditorWindowType => RequiredType("Game.Editor.ResourceUpdateNoticeEditorWindow");

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerLoopResourceServerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var process in serverProcesses)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }

            serverProcesses.Clear();

            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            var allowed = Path.Combine(Path.GetTempPath(), "FirepowerLoopResourceServerTests");
            var resolvedRoot = Path.GetFullPath(root);
            var resolvedAllowed = Path.GetFullPath(allowed);
            if (resolvedRoot.StartsWith(resolvedAllowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolvedRoot))
            {
                Directory.Delete(resolvedRoot, true);
            }
        }

        [Test]
        public void Publish_SelectsLatestValidPackageAndCopiesTemplateAndResources()
        {
            var packageRoot = Path.Combine(root, "packages");
            var templateRoot = CreateTemplateRoot();
            var releaseRoot = Path.Combine(root, "release");
            CreateValidPackage(packageRoot, "202607220900");
            var latest = CreateValidPackage(packageRoot, "202607230800");

            var result = InvokePublish(packageRoot, templateRoot, releaseRoot, new DateTime(2026, 7, 23, 11, 58, 00));

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Version, Is.EqualTo("202607230800"));
            Assert.That(result.SourcePackageDirectory, Is.EqualTo(Path.GetFullPath(latest)));
            Assert.That(Path.GetFileName(result.BundleDirectory), Is.EqualTo("ResourceServerBundle_20260723_115800"));
            AssertTemplateFiles(result.BundleDirectory);
            Assert.That(File.Exists(Path.Combine(result.BundleDirectory, "ResourceFiles", "DefaultPackage.version")), Is.True);
            Assert.That(ValidateBuildResult(Path.Combine(result.BundleDirectory, "ResourceFiles"), result.Version), Is.True);
        }

        [Test]
        public void Publish_SkipsNewerInvalidPackage()
        {
            var packageRoot = Path.Combine(root, "packages");
            var templateRoot = CreateTemplateRoot();
            var releaseRoot = Path.Combine(root, "release");
            CreateValidPackage(packageRoot, "202607230800");
            Directory.CreateDirectory(Path.Combine(packageRoot, "202607231000"));
            File.WriteAllText(Path.Combine(packageRoot, "202607231000", "DefaultPackage.version"), "202607231000");

            var result = InvokePublish(packageRoot, templateRoot, releaseRoot, new DateTime(2026, 7, 23, 12, 00, 00));

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.Version, Is.EqualTo("202607230800"));
        }

        [Test]
        public void Publish_ExistingFinalDirectoryFailsWithoutOverwriteOrTempLeft()
        {
            var packageRoot = Path.Combine(root, "packages");
            var templateRoot = CreateTemplateRoot();
            var releaseRoot = Path.Combine(root, "release");
            CreateValidPackage(packageRoot, "202607230800");
            var final = Path.Combine(releaseRoot, "ResourceServerBundle_20260723_115800");
            Directory.CreateDirectory(final);
            File.WriteAllText(Path.Combine(final, "sentinel.txt"), "old");

            var result = InvokePublish(packageRoot, templateRoot, releaseRoot, new DateTime(2026, 7, 23, 11, 58, 00));

            Assert.That(result.Success, Is.False);
            Assert.That(File.ReadAllText(Path.Combine(final, "sentinel.txt")), Is.EqualTo("old"));
            Assert.That(Directory.GetDirectories(releaseRoot, "*.tmp").Length, Is.EqualTo(0));
        }

        [Test]
        public void Publish_MissingTemplateFileFailsAndLeavesNoFakeBundle()
        {
            var packageRoot = Path.Combine(root, "packages");
            var templateRoot = CreateTemplateRoot();
            var releaseRoot = Path.Combine(root, "release");
            CreateValidPackage(packageRoot, "202607230800");
            File.Delete(Path.Combine(templateRoot, "README.txt"));

            var result = InvokePublish(packageRoot, templateRoot, releaseRoot, new DateTime(2026, 7, 23, 11, 58, 00));

            Assert.That(result.Success, Is.False);
            Assert.That(Directory.Exists(Path.Combine(releaseRoot, "ResourceServerBundle_20260723_115800")), Is.False);
            Assert.That(Directory.Exists(releaseRoot)
                ? Directory.GetDirectories(releaseRoot, "*.tmp").Length
                : 0, Is.EqualTo(0));
        }

        [Test]
        public void TryFindLatestBundle_UsesStrictTimestampNameOnly()
        {
            var releaseRoot = Path.Combine(root, "release");
            Directory.CreateDirectory(Path.Combine(releaseRoot, "ResourceServerBundle_20260722_090000"));
            var latest = Directory.CreateDirectory(Path.Combine(releaseRoot, "ResourceServerBundle_20260723_080000")).FullName;
            Directory.CreateDirectory(Path.Combine(releaseRoot, "ResourceServerBundle_invalid"));
            Directory.CreateDirectory(Path.Combine(releaseRoot, "other"));

            var ok = InvokeTryFindLatestBundle(releaseRoot, out var bundleDirectory, out var error);

            Assert.That(ok, Is.True, error);
            Assert.That(bundleDirectory, Is.EqualTo(Path.GetFullPath(latest)));
        }

        [Test]
        public void Update_ReplacesOnlyLatestBundleResourceFilesAndReportsVersions()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "release");
            var oldPackage = CreateValidPackage(Path.Combine(root, "oldPackages"), "202607220900", "old");
            CreateValidPackage(packageRoot, "202607230800", "new");
            var olderBundle = CreateBundle(releaseRoot, "ResourceServerBundle_20260722_090000", oldPackage);
            var latestBundle = CreateBundle(releaseRoot, "ResourceServerBundle_20260723_080000", oldPackage);
            var olderSnapshot = SnapshotDirectory(olderBundle);

            var result = InvokeUpdate(packageRoot, releaseRoot);

            Assert.That(result.Success, Is.True, result.Error);
            Assert.That(result.PreviousVersion, Is.EqualTo("202607220900"));
            Assert.That(result.Version, Is.EqualTo("202607230800"));
            Assert.That(result.BundleDirectory, Is.EqualTo(Path.GetFullPath(latestBundle)));
            Assert.That(ValidateBuildResult(Path.Combine(latestBundle, "ResourceFiles"), "202607230800"), Is.True);
            Assert.That(SnapshotDirectory(olderBundle), Is.EqualTo(olderSnapshot));
            Assert.That(Directory.GetDirectories(latestBundle, "*.new").Length, Is.EqualTo(0));
            Assert.That(Directory.GetDirectories(latestBundle, "*.backup").Length, Is.EqualTo(0));
        }

        [Test]
        public void OldBundleIsNotNoticeCapableAndUpdateUpgradesSupportFilesAndNotice()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "release");
            var oldPackage = CreateValidPackage(Path.Combine(root, "oldPackages"), "202607220900", "old");
            CreateValidPackage(packageRoot, "202607230800", "new");
            var bundle = CreateOldBundle(releaseRoot, "ResourceServerBundle_20260723_080000", oldPackage);
            var templateRoot = Path.Combine(WorkspaceRoot, "Server", "ResourceServer");

            Assert.That(InvokeIsNoticeCapableBundle(bundle, templateRoot, releaseRoot, out _), Is.False);

            var result = InvokeUpdate(packageRoot, releaseRoot);

            Assert.That(result.ResourceFilesSucceeded, Is.True);
            Assert.That(result.Success, Is.True, result.Error);
            foreach (var fileName in new[]
                     {
                         "start_resource_server.bat",
                         "resource_server.ps1",
                         "README.txt",
                         "validate_resource_update_notices.ps1",
                         "validate_resource_update_notices.bat"
                     })
            {
                Assert.That(HashFile(Path.Combine(bundle, fileName)), Is.EqualTo(HashFile(Path.Combine(templateRoot, fileName))), fileName);
            }

            Assert.That(File.Exists(Path.Combine(bundle, "Notices", "resource-update-notices.json")), Is.True);
            Assert.That(ResourceUpdateNoticeDocument.TryParse(
                File.ReadAllText(Path.Combine(bundle, "Notices", "resource-update-notices.json")),
                out _).Success, Is.True);
            Assert.That(InvokeIsNoticeCapableBundle(bundle, templateRoot, releaseRoot, out var error), Is.True, error);
            Assert.That(Directory.GetDirectories(bundle, ".ServerSupport_*").Length, Is.EqualTo(0));
        }

        [Test]
        public void IsNoticeCapableBundle_RejectsBundleOutsideReleaseResourceServer()
        {
            var templateRoot = CreateTemplateRoot();
            var package = CreateValidPackage(Path.Combine(root, "packages"), "202607230800");
            var realReleaseRoot = Path.Combine(root, "Release", "ResourceServer");
            var outsideRoot = Path.Combine(root, "other");
            var bundle = CreateCompatibleBundle(outsideRoot, "ResourceServerBundle_20260723_080000", package, templateRoot);

            var ok = InvokeIsNoticeCapableBundle(bundle, templateRoot, realReleaseRoot, out var error);

            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain("Release/ResourceServer"));
        }

        [Test]
        public void Update_ServerSupportCommitFailureRestoresSupportFilesAndKeepsResourceUpdateResult()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "release");
            var oldPackage = CreateValidPackage(Path.Combine(root, "oldPackages"), "202607220900", "old");
            CreateValidPackage(packageRoot, "202607230800", "new");
            var bundle = CreateOldBundle(releaseRoot, "ResourceServerBundle_20260723_080000", oldPackage);
            var oldStart = File.ReadAllText(Path.Combine(bundle, "start_resource_server.bat"));
            var oldScript = File.ReadAllText(Path.Combine(bundle, "resource_server.ps1"));
            var oldReadme = File.ReadAllText(Path.Combine(bundle, "README.txt"));
            var hook = PublishUtilityType.GetField("BeforeCommitServerSupportForTests", BindingFlags.Static | BindingFlags.NonPublic);
            hook.SetValue(null, new Action<string>(_ => throw new InvalidOperationException("simulated support failure")));

            var result = InvokeUpdate(packageRoot, releaseRoot);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ResourceFilesSucceeded, Is.True);
            Assert.That(result.Error, Does.Contain("simulated support failure"));
            Assert.That(ValidateBuildResult(Path.Combine(bundle, "ResourceFiles"), "202607230800"), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(bundle, "start_resource_server.bat")), Is.EqualTo(oldStart));
            Assert.That(File.ReadAllText(Path.Combine(bundle, "resource_server.ps1")), Is.EqualTo(oldScript));
            Assert.That(File.ReadAllText(Path.Combine(bundle, "README.txt")), Is.EqualTo(oldReadme));
            Assert.That(File.Exists(Path.Combine(bundle, "validate_resource_update_notices.ps1")), Is.False);
            Assert.That(File.Exists(Path.Combine(bundle, "validate_resource_update_notices.bat")), Is.False);
            Assert.That(Directory.GetDirectories(bundle, ".ServerSupport_*").Length, Is.EqualTo(0));
            Assert.That(hook.GetValue(null), Is.Null);
        }

        [Test]
        public void Update_ServerSupportBackupMidwayFailureRestoresEveryOriginalFile()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "release");
            var oldPackage = CreateValidPackage(Path.Combine(root, "oldPackages"), "202607220900", "old");
            CreateValidPackage(packageRoot, "202607230800", "new");
            var bundle = CreateOldBundle(releaseRoot, "ResourceServerBundle_20260723_080000", oldPackage);
            var originals = new Dictionary<string, string>
            {
                { "start_resource_server.bat", File.ReadAllText(Path.Combine(bundle, "start_resource_server.bat")) },
                { "resource_server.ps1", File.ReadAllText(Path.Combine(bundle, "resource_server.ps1")) },
                { "README.txt", File.ReadAllText(Path.Combine(bundle, "README.txt")) },
            };
            var hook = PublishUtilityType.GetField("AfterBackupServerSupportFileForTests", BindingFlags.Static | BindingFlags.NonPublic);
            var backedUp = new List<string>();
            hook.SetValue(null, new Action<string, string>((_, fileName) =>
            {
                backedUp.Add(fileName);
                if (backedUp.Count == 1)
                {
                    throw new InvalidOperationException("simulated midway backup failure");
                }
            }));

            var result = InvokeUpdate(packageRoot, releaseRoot);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ResourceFilesSucceeded, Is.True);
            Assert.That(result.NoticeSyncSucceeded, Is.False);
            Assert.That(result.Error, Does.Contain("服务器支持文件同步失败"));
            Assert.That(backedUp.Count, Is.EqualTo(1));
            Assert.That(ValidateBuildResult(Path.Combine(bundle, "ResourceFiles"), "202607230800"), Is.True);
            foreach (var pair in originals)
            {
                Assert.That(File.Exists(Path.Combine(bundle, pair.Key)), Is.True, pair.Key);
                Assert.That(File.ReadAllText(Path.Combine(bundle, pair.Key)), Is.EqualTo(pair.Value), pair.Key);
            }

            Assert.That(File.Exists(Path.Combine(bundle, "validate_resource_update_notices.ps1")), Is.False);
            Assert.That(File.Exists(Path.Combine(bundle, "validate_resource_update_notices.bat")), Is.False);
            Assert.That(Directory.GetDirectories(bundle, ".ServerSupport_*").Length, Is.EqualTo(0));
            Assert.That(hook.GetValue(null), Is.Null);
            Assert.That(PublishUtilityType.GetField("BeforeCommitServerSupportForTests", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), Is.Null);
        }

        [Test]
        public void Update_ServerSupportRestoreMidwayFailureKeepsRemainingBackupForManualInspection()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "release");
            var oldPackage = CreateValidPackage(Path.Combine(root, "oldPackages"), "202607220900", "old");
            CreateValidPackage(packageRoot, "202607230800", "new");
            var bundle = CreateOldBundle(releaseRoot, "ResourceServerBundle_20260723_080000", oldPackage);
            var restored = new List<string>();
            var commitHook = PublishUtilityType.GetField("BeforeCommitServerSupportForTests", BindingFlags.Static | BindingFlags.NonPublic);
            var restoreHook = PublishUtilityType.GetField("AfterRestoreServerSupportFileForTests", BindingFlags.Static | BindingFlags.NonPublic);
            commitHook.SetValue(null, new Action<string>(_ => throw new InvalidOperationException("simulated install failure")));
            restoreHook.SetValue(null, new Action<string, string>((_, fileName) =>
            {
                restored.Add(fileName);
                if (restored.Count == 1)
                {
                    throw new InvalidOperationException("simulated restore failure");
                }
            }));

            var result = InvokeUpdate(packageRoot, releaseRoot);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ResourceFilesSucceeded, Is.True);
            Assert.That(result.NoticeSyncSucceeded, Is.False);
            Assert.That(result.Error, Does.Contain("人工"));
            Assert.That(restored.Count, Is.EqualTo(1));
            Assert.That(ValidateBuildResult(Path.Combine(bundle, "ResourceFiles"), "202607230800"), Is.True);
            Assert.That(Directory.GetDirectories(bundle, ".ServerSupport_*.new").Length, Is.EqualTo(0));
            var backups = Directory.GetDirectories(bundle, ".ServerSupport_*.backup");
            Assert.That(backups.Length, Is.EqualTo(1));
            Assert.That(Directory.GetFiles(backups[0]).Length, Is.GreaterThan(0));
            Assert.That(File.Exists(Path.Combine(backups[0], "resource_server.ps1")), Is.True);
            Assert.That(File.Exists(Path.Combine(backups[0], "README.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundle, "validate_resource_update_notices.ps1")), Is.False);
            Assert.That(File.Exists(Path.Combine(bundle, "validate_resource_update_notices.bat")), Is.False);
            Assert.That(commitHook.GetValue(null), Is.Null);
            Assert.That(restoreHook.GetValue(null), Is.Null);
        }

        [Test]
        public void Update_CommitFailureRestoresOldResourceFiles()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "release");
            var oldPackage = CreateValidPackage(Path.Combine(root, "oldPackages"), "202607220900", "old");
            CreateValidPackage(packageRoot, "202607230800", "new");
            var bundle = CreateBundle(releaseRoot, "ResourceServerBundle_20260723_080000", oldPackage);
            var before = SnapshotDirectory(Path.Combine(bundle, "ResourceFiles"));
            var hook = PublishUtilityType.GetField("BeforeCommitUpdateForTests", BindingFlags.Static | BindingFlags.NonPublic);
            hook.SetValue(null, new Action<string>(_ => throw new InvalidOperationException("simulated commit failure")));

            var result = InvokeUpdate(packageRoot, releaseRoot);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Does.Contain("已恢复旧资源"));
            Assert.That(SnapshotDirectory(Path.Combine(bundle, "ResourceFiles")), Is.EqualTo(before));
            Assert.That(ValidateBuildResult(Path.Combine(bundle, "ResourceFiles"), "202607220900"), Is.True);
        }

        [Test]
        public void Update_InvalidLatestBundleFailsWithoutFallingBackToOlderBundle()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "release");
            var oldPackage = CreateValidPackage(Path.Combine(root, "oldPackages"), "202607220900", "old");
            CreateValidPackage(packageRoot, "202607230800", "new");
            var olderBundle = CreateBundle(releaseRoot, "ResourceServerBundle_20260722_090000", oldPackage);
            var latestBundle = Path.Combine(releaseRoot, "ResourceServerBundle_20260723_080000");
            Directory.CreateDirectory(latestBundle);
            File.WriteAllText(Path.Combine(latestBundle, "README.txt"), "readme");
            File.WriteAllText(Path.Combine(latestBundle, "start_resource_server.bat"), "start");
            var olderSnapshot = SnapshotDirectory(olderBundle);

            var result = InvokeUpdate(packageRoot, releaseRoot);

            Assert.That(result.Success, Is.False);
            Assert.That(result.BundleDirectory, Is.EqualTo(Path.GetFullPath(latestBundle)));
            Assert.That(SnapshotDirectory(olderBundle), Is.EqualTo(olderSnapshot));
        }

        [Test]
        public void NoticeEditor_VersionBindingRejectsNewerCompatibleServerBundle()
        {
            var packageRoot = Path.Combine(root, "packages");
            var releaseRoot = Path.Combine(root, "Release", "ResourceServer");
            var templateRoot = CreateTemplateRoot();
            CreateValidPackage(packageRoot, "202607230800", "host");
            var newerPackage = CreateValidPackage(Path.Combine(root, "serverPackages"), "202607231000", "server");
            CreateCompatibleBundle(releaseRoot, "ResourceServerBundle_20260723_100000", newerPackage, templateRoot);

            var binding = InvokeResolveVersionBinding(packageRoot, releaseRoot, templateRoot);

            Assert.That(GetField<bool>(binding, "CanSave"), Is.False);
            Assert.That(GetField<string>(binding, "Version"), Is.EqualTo("202607230800"));
            Assert.That(GetField<string>(binding, "Error"), Does.Contain("高于 Host 版本"));
        }

        [Test]
        public void NoticeEditor_LoadEditStateAddsDynamicLanguageAndSaveWritesIt()
        {
            var noticeFile = Path.Combine(root, "Notices", "resource-update-notices.json");
            Directory.CreateDirectory(Path.GetDirectoryName(noticeFile));
            File.WriteAllText(noticeFile,
                "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[{\"version\":\"202607230800\",\"enabled\":true,\"texts\":{\"zh-CN\":{\"title\":\"标题\",\"summary\":\"摘要\",\"items\":[\"一\"]}}}]}");
            var languages = CreateLanguages(("zh-CN", "Chinese"), ("en-US", "English"));

            var state = InvokeLoadEditState("202607230800", languages, noticeFile);
            var texts = GetField<object>(state, "Texts");
            var contains = texts.GetType().GetMethod("ContainsKey");

            Assert.That((bool)contains.Invoke(texts, new object[] { "en-US" }), Is.True);

            var result = InvokeSaveDetailed(state, "zh-CN", null, noticeFile, "待发布");
            var rootJson = JObject.Parse(File.ReadAllText(noticeFile));

            Assert.That(GetField<bool>(result, "Success"), Is.True);
            Assert.That(GetField<string>(result, "TargetVersion"), Is.EqualTo("202607230800"));
            Assert.That(GetField<string>(result, "VersionStatus"), Is.EqualTo("待发布"));
            Assert.That(rootJson["notices"]?[0]?["texts"]?["en-US"], Is.Not.Null);
            Assert.That(rootJson["notices"]?[0]?["texts"]?["en-US"]?["title"]?.Value<string>(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void NoticeEditor_SaveDetailedGradesBundleSyncFailureAfterMaintenanceSave()
        {
            var noticeFile = Path.Combine(root, "Notices", "resource-update-notices.json");
            var bundle = Path.Combine(root, "Release", "ResourceServer", "ResourceServerBundle_20260723_080000");
            Directory.CreateDirectory(bundle);
            File.WriteAllText(Path.Combine(bundle, "Notices"), "not a directory");
            var state = CreateNoticeState("202607230800", "zh-CN", "标题", "摘要");

            var result = InvokeSaveDetailed(state, "zh-CN", bundle, noticeFile, "当前服务器包");

            Assert.That(GetField<bool>(result, "Success"), Is.False);
            Assert.That(GetField<bool>(result, "MaintenanceSourceSucceeded"), Is.True);
            Assert.That(GetField<bool>(result, "BundleSyncAttempted"), Is.True);
            Assert.That(GetField<bool>(result, "BundleSyncSucceeded"), Is.False);
            Assert.That(GetField<string>(result, "TargetVersion"), Is.EqualTo("202607230800"));
            Assert.That(GetField<string>(result, "VersionStatus"), Is.EqualTo("当前服务器包"));
            Assert.That(GetField<string>(result, "Message"), Does.Contain("bundle sync failed"));
            Assert.That(ResourceUpdateNoticeDocument.TryParse(File.ReadAllText(noticeFile), out _).Success, Is.True);
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(noticeFile), "*.tmp").Length, Is.EqualTo(0));
        }

        [Test]
        public void NoticeEditor_SaveEntryStopsWhenRefreshedBindingCannotSave()
        {
            var state = CreateNoticeState("202607220900", "zh-CN", "旧标题", "旧摘要");
            var originalVersion = GetField<string>(state, "Version");
            var bundle = Path.Combine(root, "Release", "ResourceServer", "ResourceServerBundle_20260723_080000");
            var bundleNotice = Path.Combine(bundle, "Notices", "resource-update-notices.json");
            Directory.CreateDirectory(Path.GetDirectoryName(bundleNotice));
            File.WriteAllText(bundleNotice, "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[]}");
            var bundleBefore = File.ReadAllText(bundleNotice);
            var resolveHook = NoticeEditorWindowType.GetField("ResolveVersionBindingForTests", BindingFlags.Static | BindingFlags.NonPublic);
            var saveHook = NoticeEditorWindowType.GetField("SaveForTests", BindingFlags.Static | BindingFlags.NonPublic);
            var saveCalls = 0;
            var window = UnityEngine.ScriptableObject.CreateInstance(NoticeEditorWindowType);
            try
            {
                SetField(window, "state", state);
                SetField(window, "binding", CreateVersionBinding("202607220900", "待发布", true, null, bundle));
                SetField(window, "languages", CreateLanguages(("zh-CN", "Chinese")));
                SetField(window, "defaultLanguage", "zh-CN");
                resolveHook.SetValue(null, new Func<object>(() =>
                    CreateVersionBinding("202607230800", "版本冲突", false, "最新 ResourceServer 包版本高于 Host 版本。", bundle)));
                saveHook.SetValue(null, new Func<object, string, string, string, string>((_, __, ___, ____) =>
                {
                    saveCalls++;
                    return "unexpected save";
                }));

                InvokeWindowSave(window);

                Assert.That(GetField<string>(state, "Version"), Is.EqualTo(originalVersion));
                Assert.That(File.ReadAllText(bundleNotice), Is.EqualTo(bundleBefore));
                Assert.That(saveCalls, Is.EqualTo(0));
                Assert.That(GetField<string>(window, "message"), Does.Contain("最新 ResourceServer 包版本高于 Host 版本"));
                Assert.That(Directory.GetFiles(Path.GetDirectoryName(bundleNotice), "*.tmp").Length, Is.EqualTo(0));
            }
            finally
            {
                resolveHook.SetValue(null, null);
                saveHook.SetValue(null, null);
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ServerTemplate_UsesAnyReadOnlyStreamingHttpContract()
        {
            var script = File.ReadAllText(Path.Combine(WorkspaceRoot, "Server/ResourceServer/resource_server.ps1"));

            Assert.That(script, Does.Contain("IPAddress]::Any"));
            Assert.That(script, Does.Contain("GET"));
            Assert.That(script, Does.Contain("HEAD"));
            Assert.That(script, Does.Contain("403"));
            Assert.That(script, Does.Contain("404"));
            Assert.That(script, Does.Contain("405"));
            Assert.That(script, Does.Contain("500"));
            Assert.That(script, Does.Contain("File]::Open"));
            Assert.That(script, Does.Not.Contain("File.ReadAllBytes"));
        }

        [Test]
        public void PowerShellServer_EnforcesRequestHeaderLimitAndKeepsRunning()
        {
            var resourceRoot = CreateServerResourceFiles();
            var port = GetFreePort();
            var process = StartServerProcess(resourceRoot, port);
            WaitForServerReady(port, process);

            var shortResponse = SendRawHttpRequest(port, GetRequest("/DefaultPackage.version"));
            Assert.That(shortResponse.StatusCode, Is.EqualTo(200));
            Assert.That(Encoding.UTF8.GetString(shortResponse.Body).Trim(), Is.EqualTo("202607231414"));

            var boundaryResponse = SendRawHttpRequest(port, BuildSizedGetRequest("/DefaultPackage.version", 16 * 1024));
            Assert.That(boundaryResponse.StatusCode, Is.EqualTo(200));

            var tooLargeResponse = SendRawHttpRequest(port, BuildSizedGetRequest("/DefaultPackage.version", (16 * 1024) + 1));
            Assert.That(tooLargeResponse.StatusCode, Is.EqualTo(400));

            var overlongHeaderResponse = SendRawHttpRequest(port, BuildSizedGetRequest("/DefaultPackage.version", (16 * 1024) + 256));
            Assert.That(overlongHeaderResponse.StatusCode, Is.EqualTo(400));

            var incompleteResponse = SendRawHttpRequest(port, "GET /DefaultPackage.version HTTP/1.1\r\nHost: localhost\r\n");
            Assert.That(incompleteResponse.StatusCode == 0 || incompleteResponse.StatusCode == 400, Is.True);

            var afterMalformedResponse = SendRawHttpRequest(port, GetRequest("/DefaultPackage.version"));
            Assert.That(afterMalformedResponse.StatusCode, Is.EqualTo(200));
            Assert.That(process.HasExited, Is.False);
        }

        [Test]
        public void PowerShellServer_ReturnsExpectedMimeTypesAndHeadResponses()
        {
            var resourceRoot = CreateServerResourceFiles();
            var port = GetFreePort();
            var process = StartServerProcess(resourceRoot, port);
            WaitForServerReady(port, process);

            var expectations = new Dictionary<string, string>
            {
                { "/DefaultPackage.version", "text/plain; charset=utf-8" },
                { "/sample.txt", "text/plain; charset=utf-8" },
                { "/sample.json", "application/json; charset=utf-8" },
                { "/sample.xml", "application/xml; charset=utf-8" },
                { "/DefaultPackage_202607231414.bytes", "application/octet-stream" },
                { "/DefaultPackage_202607231414.hash", "application/octet-stream" },
                { "/sample.bundle", "application/octet-stream" },
                { "/0123456789abcdef0123456789abcdef", "application/octet-stream" },
            };

            foreach (var expectation in expectations)
            {
                var getResponse = SendRawHttpRequest(port, GetRequest(expectation.Key));
                Assert.That(getResponse.StatusCode, Is.EqualTo(200), expectation.Key);
                Assert.That(getResponse.Headers["Content-Type"], Is.EqualTo(expectation.Value), expectation.Key);
                Assert.That(getResponse.Headers["Connection"], Is.EqualTo("close"), expectation.Key);
                Assert.That(int.Parse(getResponse.Headers["Content-Length"]), Is.EqualTo(getResponse.Body.Length), expectation.Key);

                var headResponse = SendRawHttpRequest(port, HeadRequest(expectation.Key));
                Assert.That(headResponse.StatusCode, Is.EqualTo(200), expectation.Key);
                Assert.That(headResponse.Headers["Content-Type"], Is.EqualTo(getResponse.Headers["Content-Type"]), expectation.Key);
                Assert.That(headResponse.Headers["Content-Length"], Is.EqualTo(getResponse.Headers["Content-Length"]), expectation.Key);
                Assert.That(headResponse.Headers["Connection"], Is.EqualTo("close"), expectation.Key);
                Assert.That(headResponse.Body.Length, Is.EqualTo(0), expectation.Key);
            }
        }

        [Test]
        public void PowerShellServer_RejectsInvalidDecodedPathWithoutExiting()
        {
            var resourceRoot = CreateServerResourceFiles();
            var port = GetFreePort();
            var process = StartServerProcess(resourceRoot, port);
            WaitForServerReady(port, process);

            foreach (var path in new[] { "/%00", "/%3A" })
            {
                var invalid = SendRawHttpRequest(port, GetRequest(path));

                Assert.That(invalid.StatusCode, Is.EqualTo(400), path);
                Assert.That(process.HasExited, Is.False, path);

                var afterInvalid = SendRawHttpRequest(port, GetRequest("/DefaultPackage.version"));
                Assert.That(afterInvalid.StatusCode, Is.EqualTo(200), path);
                Assert.That(Encoding.UTF8.GetString(afterInvalid.Body).Trim(), Is.EqualTo("202607231414"), path);
                Assert.That(process.HasExited, Is.False, path);
            }
        }

        [Test]
        public void PowerShellServer_ServesNoticeRouteHeadIsolationHotReplaceAndInvalidPaths()
        {
            var resourceRoot = CreateServerResourceFiles();
            var noticeFile = Path.Combine(root, "notice.json");
            File.WriteAllText(noticeFile, "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[]}");
            var port = GetFreePort();
            var process = StartServerProcess(resourceRoot, port, noticeFile);
            WaitForServerReady(port, process);

            var get = SendRawHttpRequest(port, GetRequest("/resource-update-notices.json"));
            Assert.That(get.StatusCode, Is.EqualTo(200));
            Assert.That(get.Headers["Content-Type"], Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(get.Headers["Cache-Control"], Does.Contain("no-store"));
            Assert.That(get.Headers["Pragma"], Is.EqualTo("no-cache"));
            Assert.That(get.Headers["Expires"], Is.EqualTo("0"));
            Assert.That(Encoding.UTF8.GetString(get.Body), Does.Contain("\"schemaVersion\":1"));

            var head = SendRawHttpRequest(port, HeadRequest("/resource-update-notices.json"));
            Assert.That(head.StatusCode, Is.EqualTo(200));
            Assert.That(head.Headers["Content-Length"], Is.EqualTo(get.Headers["Content-Length"]));
            Assert.That(head.Body.Length, Is.EqualTo(0));

            var isolated = SendRawHttpRequest(port, GetRequest("/Notices/resource-update-notices.json"));
            Assert.That(isolated.StatusCode, Is.EqualTo(404));

            using (File.Open(noticeFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var replacement = Path.Combine(root, "notice-new.json");
                File.WriteAllText(replacement, "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[{\"version\":\"202607231729\",\"enabled\":true,\"texts\":{\"zh-CN\":{\"title\":\"T\",\"summary\":\"S\",\"items\":[]}}}]}");
                File.Replace(replacement, noticeFile, null);
            }

            var replaced = SendRawHttpRequest(port, GetRequest("/resource-update-notices.json"));
            Assert.That(Encoding.UTF8.GetString(replaced.Body), Does.Contain("202607231729"));

            foreach (var path in new[] { "/%00", "/%3A" })
            {
                var invalid = SendRawHttpRequest(port, GetRequest(path));
                Assert.That(invalid.StatusCode, Is.EqualTo(400), path);
                Assert.That(process.HasExited, Is.False, path);
            }

            var normal = SendRawHttpRequest(port, GetRequest("/DefaultPackage.version"));
            Assert.That(normal.StatusCode, Is.EqualTo(200));
            Assert.That(process.HasExited, Is.False);
        }

        [Test]
        public void BuildUtility_MenusDelegateWithoutBuildOrServerStart()
        {
            var buildSource = File.ReadAllText(Path.Combine(WorkspaceRoot, "Client/Assets/Editor/Build/BuildUtility.cs"));
            var publishSource = File.ReadAllText(Path.Combine(WorkspaceRoot, "Client/Assets/Editor/YooAsset/ResourceServerPublishUtility.cs"));

            Assert.That(buildSource, Does.Contain("Tools/服务器/PC/启动资源服"));
            Assert.That(buildSource, Does.Contain("Tools/服务器/PC/生成资源服"));
            Assert.That(buildSource, Does.Contain("Tools/服务器/PC/更新资源服"));
            Assert.That(buildSource, Does.Contain("ResourceServerPublishUtility.PublishLatestValidPackage"));
            Assert.That(buildSource, Does.Contain("ResourceServerPublishUtility.UpdateLatestBundle"));
            Assert.That(publishSource, Does.Not.Contain("HybridCLRBuildUtility"));
            Assert.That(publishSource, Does.Not.Contain("BuildDefaultPackage"));
            Assert.That(publishSource, Does.Not.Contain("BuildHostCodeAndResourcesBatch"));
            Assert.That(publishSource, Does.Not.Contain("LocalResourceServerUtility.StartAndProbe"));
            Assert.That(publishSource, Does.Not.Contain("Process.Start"));
        }

        private ResourceServerPublishResultProxy InvokePublish(
            string packageRoot,
            string templateRoot,
            string releaseRoot,
            DateTime timestamp)
        {
            var method = PublishUtilityType.GetMethod("Publish", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return new ResourceServerPublishResultProxy(method.Invoke(null, new object[] { packageRoot, templateRoot, releaseRoot, timestamp }));
        }

        private ResourceServerPublishResultProxy InvokeUpdate(string packageRoot, string releaseRoot)
        {
            var method = PublishUtilityType.GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return new ResourceServerPublishResultProxy(method.Invoke(null, new object[] { packageRoot, releaseRoot }));
        }

        private bool InvokeTryFindLatestBundle(string releaseRoot, out string bundleDirectory, out string error)
        {
            var method = PublishUtilityType.GetMethod("TryFindLatestBundle", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] args = { releaseRoot, null, null };
            var ok = (bool)method.Invoke(null, args);
            bundleDirectory = args[1] as string;
            error = args[2] as string;
            return ok;
        }

        private bool InvokeIsNoticeCapableBundle(string bundle, string templateRoot, out string error)
        {
            var method = PublishUtilityType.GetMethod("IsNoticeCapableBundle", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            object[] args = { bundle, templateRoot, null };
            var ok = (bool)method.Invoke(null, args);
            error = args[2] as string;
            return ok;
        }

        private bool InvokeIsNoticeCapableBundle(string bundle, string templateRoot, string releaseRoot, out string error)
        {
            var method = PublishUtilityType.GetMethod(
                "IsNoticeCapableBundle",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(string), typeof(string), typeof(string).MakeByRefType() },
                null);
            Assert.That(method, Is.Not.Null);
            object[] args = { bundle, templateRoot, releaseRoot, null };
            var ok = (bool)method.Invoke(null, args);
            error = args[3] as string;
            return ok;
        }

        private object InvokeResolveVersionBinding(string packageRoot, string releaseRoot, string templateRoot)
        {
            var method = NoticeEditorUtilityType.GetMethod("ResolveVersionBinding", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { packageRoot, releaseRoot, templateRoot });
        }

        private object InvokeLoadEditState(string version, Array languages, string noticeFilePath)
        {
            var method = NoticeEditorUtilityType.GetMethod("LoadEditState", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method.Invoke(null, new object[] { version, languages, noticeFilePath });
        }

        private object InvokeSaveDetailed(
            object state,
            string defaultLanguage,
            string latestBundle,
            string noticeFilePath,
            string versionStatus = "")
        {
            var methods = NoticeEditorUtilityType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
            var method = methods.First(item => item.Name == "SaveDetailed" && item.GetParameters().Length == 5);
            return method.Invoke(null, new[] { state, defaultLanguage, latestBundle, noticeFilePath, versionStatus });
        }

        private Array CreateLanguages(params (string Code, string Label)[] items)
        {
            var type = NoticeEditorUtilityType.GetNestedType("LanguageInfo");
            var languages = Array.CreateInstance(type, items.Length);
            for (var i = 0; i < items.Length; i++)
            {
                languages.SetValue(CreateLanguage(type, items[i].Code, items[i].Label), i);
            }

            return languages;
        }

        private static object CreateLanguage(Type type, string code, string label)
        {
            var language = Activator.CreateInstance(type);
            type.GetField("Code").SetValue(language, code);
            type.GetField("Label").SetValue(language, label);
            type.GetField("Active").SetValue(language, true);
            return language;
        }

        private object CreateNoticeState(string version, string language, string title, string summary)
        {
            var stateType = NoticeEditorUtilityType.GetNestedType("NoticeEditState");
            var state = Activator.CreateInstance(stateType);
            stateType.GetField("Version").SetValue(state, version);
            stateType.GetField("Enabled").SetValue(state, true);
            var texts = stateType.GetField("Texts").GetValue(state);
            var content = new ResourceUpdateNoticeContent(version, title, summary, Array.Empty<string>());
            texts.GetType().GetProperty("Item").SetValue(texts, content, new object[] { language });
            return state;
        }

        private static T GetField<T>(object instance, string name)
        {
            return (T)instance.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(instance);
        }

        private static void SetField(object instance, string name, object value)
        {
            instance.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(instance, value);
        }

        private static void InvokeWindowSave(object window)
        {
            window.GetType()
                .GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(window, Array.Empty<object>());
        }

        private object CreateVersionBinding(
            string version,
            string status,
            bool canSave,
            string error,
            string latestBundle)
        {
            var bindingType = NoticeEditorUtilityType.GetNestedType("VersionBinding");
            var binding = Activator.CreateInstance(bindingType);
            bindingType.GetField("Version").SetValue(binding, version);
            bindingType.GetField("Status").SetValue(binding, status);
            bindingType.GetField("CanSave").SetValue(binding, canSave);
            bindingType.GetField("Error").SetValue(binding, error);
            bindingType.GetField("LatestBundle").SetValue(binding, latestBundle);
            return binding;
        }

        private string CreateTemplateRoot()
        {
            var templateRoot = Path.Combine(root, "template");
            Directory.CreateDirectory(templateRoot);
            File.WriteAllText(Path.Combine(templateRoot, "start_resource_server.bat"), "start");
            File.WriteAllText(Path.Combine(templateRoot, "resource_server.ps1"), "script");
            File.WriteAllText(Path.Combine(templateRoot, "README.txt"), "readme");
            File.WriteAllText(Path.Combine(templateRoot, "validate_resource_update_notices.ps1"), "validate");
            File.WriteAllText(Path.Combine(templateRoot, "validate_resource_update_notices.bat"), "validate-bat");
            File.WriteAllText(Path.Combine(templateRoot, "publish_app_update.ps1"), "publish-apk");
            File.WriteAllText(Path.Combine(templateRoot, "validate_android_signature.ps1"), "validate-signing");
            File.WriteAllText(Path.Combine(templateRoot, "android-signing.json"), "signing-policy");
            File.WriteAllText(Path.Combine(templateRoot, "publish_windows_update.ps1"), "publish-pc");
            Directory.CreateDirectory(Path.Combine(templateRoot, "Notices"));
            File.WriteAllText(Path.Combine(templateRoot, "Notices", "resource-update-notices.json"),
                "{\"schemaVersion\":1,\"defaultLanguage\":\"zh-CN\",\"notices\":[]}");
            return templateRoot;
        }

        private string CreateBundle(string releaseRoot, string bundleName, string sourcePackage)
        {
            var templateRoot = CreateTemplateRoot();
            return CreateCompatibleBundle(releaseRoot, bundleName, sourcePackage, templateRoot);
        }

        private string CreateCompatibleBundle(string releaseRoot, string bundleName, string sourcePackage, string templateRoot)
        {
            var bundle = Path.Combine(releaseRoot, bundleName);
            Directory.CreateDirectory(bundle);
            foreach (var file in Directory.GetFiles(templateRoot))
            {
                File.Copy(file, Path.Combine(bundle, Path.GetFileName(file)));
            }

            CopyDirectory(Path.Combine(templateRoot, "Notices"), Path.Combine(bundle, "Notices"));
            CopyDirectory(sourcePackage, Path.Combine(bundle, "ResourceFiles"));
            return bundle;
        }

        private string CreateOldBundle(string releaseRoot, string bundleName, string sourcePackage)
        {
            var bundle = Path.Combine(releaseRoot, bundleName);
            Directory.CreateDirectory(bundle);
            File.WriteAllText(Path.Combine(bundle, "start_resource_server.bat"), "old-start");
            File.WriteAllText(Path.Combine(bundle, "resource_server.ps1"), "old-script");
            File.WriteAllText(Path.Combine(bundle, "README.txt"), "old-readme");
            CopyDirectory(sourcePackage, Path.Combine(bundle, "ResourceFiles"));
            return bundle;
        }

        private static string CreateValidPackage(string packageRoot, string version, string marker = "data")
        {
            var directory = Path.Combine(packageRoot, version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "DefaultPackage.version"), version);
            File.WriteAllBytes(Path.Combine(directory, $"DefaultPackage_{version}.bytes"), new byte[] { 1, 2, 3 });
            File.WriteAllText(Path.Combine(directory, $"DefaultPackage_{version}.hash"), "hash-" + marker);
            File.WriteAllText(Path.Combine(directory, "0123456789abcdef0123456789abcdef"), "bundle-" + marker);
            return directory;
        }

        private string CreateServerResourceFiles()
        {
            var directory = Path.Combine(root, "serverResourceFiles");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "DefaultPackage.version"), "202607231414");
            File.WriteAllBytes(Path.Combine(directory, "DefaultPackage_202607231414.bytes"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllText(Path.Combine(directory, "DefaultPackage_202607231414.hash"), "hash-202607231414");
            File.WriteAllBytes(Path.Combine(directory, "sample.bundle"), new byte[] { 5, 6, 7 });
            File.WriteAllText(Path.Combine(directory, "sample.json"), "{\"ok\":true}");
            File.WriteAllText(Path.Combine(directory, "sample.xml"), "<root />");
            File.WriteAllText(Path.Combine(directory, "sample.txt"), "plain text");
            File.WriteAllText(Path.Combine(directory, "0123456789abcdef0123456789abcdef"), "bundle-no-extension");
            return directory;
        }

        private Process StartServerProcess(string resourceRoot, int port, string noticeFile = null)
        {
            var scriptPath = Path.Combine(WorkspaceRoot, "Server/ResourceServer/resource_server.ps1");
            var stdoutPath = Path.Combine(root, $"server-{port}.out.log");
            var stderrPath = Path.Combine(root, $"server-{port}.err.log");
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Port {port} -Root \"{resourceRoot}\"" +
                    (string.IsNullOrWhiteSpace(noticeFile) ? string.Empty : $" -NoticeFile \"{noticeFile}\""),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var process = Process.Start(startInfo);
            Assert.That(process, Is.Not.Null);
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    File.AppendAllText(stdoutPath, args.Data + Environment.NewLine);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data != null)
                {
                    File.AppendAllText(stderrPath, args.Data + Environment.NewLine);
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            serverProcesses.Add(process);
            return process;
        }

        private void WaitForServerReady(int port, Process process)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            Exception lastException = null;
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    Assert.Fail($"Resource server exited before ready. ExitCode={process.ExitCode}");
                }

                try
                {
                    var response = SendRawHttpRequest(port, GetRequest("/DefaultPackage.version"), 1000);
                    if (response.StatusCode == 200)
                    {
                        return;
                    }
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }

                System.Threading.Thread.Sleep(100);
            }

            Assert.Fail($"Resource server was not ready within 10 seconds. Last error: {lastException}");
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private static string GetRequest(string path)
        {
            return $"GET {path} HTTP/1.1\r\nHost: localhost\r\n\r\n";
        }

        private static string HeadRequest(string path)
        {
            return $"HEAD {path} HTTP/1.1\r\nHost: localhost\r\n\r\n";
        }

        private static string BuildSizedGetRequest(string path, int totalBytes)
        {
            var prefix = $"GET {path} HTTP/1.1\r\nHost: localhost\r\nX-Fill: ";
            const string suffix = "\r\n\r\n";
            var fixedBytes = Encoding.ASCII.GetByteCount(prefix + suffix);
            var fillBytes = totalBytes - fixedBytes;
            Assert.That(fillBytes, Is.GreaterThanOrEqualTo(0));
            return prefix + new string('a', fillBytes) + suffix;
        }

        private static RawHttpResponse SendRawHttpRequest(int port, string request, int timeoutMs = 7000)
        {
            using (var client = new TcpClient())
            {
                client.ReceiveTimeout = timeoutMs;
                client.SendTimeout = timeoutMs;
                client.Connect(IPAddress.Loopback, port);
                using (var stream = client.GetStream())
                {
                    var requestBytes = Encoding.ASCII.GetBytes(request);
                    stream.Write(requestBytes, 0, requestBytes.Length);
                    client.Client.Shutdown(SocketShutdown.Send);

                    using (var memory = new MemoryStream())
                    {
                        var buffer = new byte[4096];
                        try
                        {
                            int read;
                            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                memory.Write(buffer, 0, read);
                            }
                        }
                        catch (IOException)
                        {
                        }

                        return RawHttpResponse.Parse(memory.ToArray());
                    }
                }
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(target, Relative(source, directory)));
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, Path.Combine(target, Relative(source, file)));
            }
        }

        private static Dictionary<string, string> SnapshotDirectory(string directory)
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(file => Relative(directory, file), HashFile, StringComparer.OrdinalIgnoreCase);
        }

        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string Relative(string root, string path)
        {
            var rootUri = new Uri(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static bool ValidateBuildResult(string directory, string version)
        {
            var type = RequiredType("Game.Editor.YooAssetBuildUtility");
            var method = type.GetMethod("ValidateBuildResult", BindingFlags.Static | BindingFlags.Public);
            return (bool)method.Invoke(null, new object[] { directory, version });
        }

        private static void AssertTemplateFiles(string bundleRoot)
        {
            Assert.That(File.Exists(Path.Combine(bundleRoot, "start_resource_server.bat")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "resource_server.ps1")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "README.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "validate_resource_update_notices.ps1")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "validate_resource_update_notices.bat")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "publish_app_update.ps1")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "validate_android_signature.ps1")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "android-signing.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "publish_windows_update.ps1")), Is.True);
            Assert.That(File.Exists(Path.Combine(bundleRoot, "Notices", "resource-update-notices.json")), Is.True);
        }

        private static Type RequiredType(string fullName)
        {
            var type = Type.GetType($"{fullName}, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, $"Missing editor type: {fullName}");
            return type;
        }

        private static string WorkspaceRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        private sealed class ResourceServerPublishResultProxy
        {
            private readonly object result;

            public ResourceServerPublishResultProxy(object result)
            {
                this.result = result;
            }

            public bool Success => (bool)Get("Success");
            public string Operation => Get("Operation") as string;
            public string Version => Get("Version") as string;
            public string PreviousVersion => Get("PreviousVersion") as string;
            public string SourcePackageDirectory => Get("SourcePackageDirectory") as string;
            public string BundleDirectory => Get("BundleDirectory") as string;
            public string Error => Get("Error") as string;
            public bool ResourceFilesSucceeded => (bool)Get("ResourceFilesSucceeded");
            public bool NoticeSyncSucceeded => (bool)Get("NoticeSyncSucceeded");
            public string NoticeSyncError => Get("NoticeSyncError") as string;

            private object Get(string name)
            {
                return result.GetType().GetField(name).GetValue(result);
            }
        }

        private sealed class RawHttpResponse
        {
            public int StatusCode { get; private set; }
            public Dictionary<string, string> Headers { get; private set; }
            public byte[] Body { get; private set; }

            public static RawHttpResponse Parse(byte[] bytes)
            {
                var separatorIndex = IndexOf(bytes, new byte[] { 13, 10, 13, 10 });
                if (separatorIndex < 0)
                {
                    return new RawHttpResponse
                    {
                        StatusCode = 0,
                        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                        Body = bytes,
                    };
                }

                var headerText = Encoding.ASCII.GetString(bytes, 0, separatorIndex);
                var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
                var statusParts = lines[0].Split(' ');
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 1; i < lines.Length; i++)
                {
                    var colon = lines[i].IndexOf(':');
                    if (colon <= 0)
                    {
                        continue;
                    }

                    headers[lines[i].Substring(0, colon)] = lines[i].Substring(colon + 1).Trim();
                }

                var bodyOffset = separatorIndex + 4;
                var body = new byte[bytes.Length - bodyOffset];
                Buffer.BlockCopy(bytes, bodyOffset, body, 0, body.Length);
                return new RawHttpResponse
                {
                    StatusCode = statusParts.Length > 1 ? int.Parse(statusParts[1]) : 0,
                    Headers = headers,
                    Body = body,
                };
            }

            private static int IndexOf(byte[] haystack, byte[] needle)
            {
                for (var i = 0; i <= haystack.Length - needle.Length; i++)
                {
                    var matched = true;
                    for (var j = 0; j < needle.Length; j++)
                    {
                        if (haystack[i + j] != needle[j])
                        {
                            matched = false;
                            break;
                        }
                    }

                    if (matched)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }
    }
}

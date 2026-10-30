using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>只用临时文件验证快速 APK 的版本预留与复用门禁；不调用 Player 或资源构建。</summary>
    public sealed class AndroidQuickBuildTests
    {
        private string root;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "FirepowerQuick", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        private static object Call(string type, string method, params object[] args)
        {
            var target = Type.GetType("Game.Editor." + type + ", Assembly-CSharp-Editor");
            Assert.That(target, Is.Not.Null, "缺少快速打包策略：" + type);
            try { return target.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException; }
        }

        private object Reserve(string version = "0.0.2", int code = 3) =>
            Call("AndroidApkVersionUtility", "Reserve", version, code, Path.Combine(root, "Release"), Path.Combine(root, "reserved.json"));
        private static T Field<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);
        private void Published(string name)
        {
            string directory = Path.Combine(root, "Release", "ResourceServerBundle_test", "AppUpdates", "Android");
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, name), "old-apk");
        }

        [Test] public void AllocationExceedsPublishedApkEvenWhenProjectVersionWasReset()
        {
            Published("FirepowerLoop-0.0.3-4.apk"); var next = Reserve();
            Assert.That(Field<string>(next, "VersionName"), Is.EqualTo("0.0.4"));
            Assert.That(Field<int>(next, "VersionCode"), Is.EqualTo(5));
        }
        [Test] public void ReservationSurvivesFailedAttemptAndResetProjectSettings()
        {
            Reserve(); var next = Reserve();
            Assert.That(Field<string>(next, "VersionName"), Is.EqualTo("0.0.4"));
            Assert.That(Field<int>(next, "VersionCode"), Is.EqualTo(5));
        }
        [Test] public void AllocationUsesIndependentMaximumNameAndCode()
        {
            Published("FirepowerLoop-1.2.9-4.apk"); Published("FirepowerLoop-0.1.0-50.apk"); var next = Reserve();
            Assert.That(Field<string>(next, "VersionName"), Is.EqualTo("1.2.10"));
            Assert.That(Field<int>(next, "VersionCode"), Is.EqualTo(51));
        }
        [Test] public void DescriptorAlsoPreventsReuseWhenApkFileIsMissing()
        {
            Published("placeholder");
            File.WriteAllText(Path.Combine(root, "Release", "ResourceServerBundle_test", "AppUpdates", "Android", "app-update.json"),
                "{\"schemaVersion\":1,\"platform\":\"Android\",\"packageName\":\"com.firepowerloop.game\",\"versionName\":\"0.0.8\",\"versionCode\":10}");
            var next = Reserve(); Assert.That(Field<string>(next, "VersionName"), Is.EqualTo("0.0.9"));
            Assert.That(Field<int>(next, "VersionCode"), Is.EqualTo(11));
        }
        [Test] public void CorruptReservationFailsWithoutOverwritingIt()
        {
            string path = Path.Combine(root, "reserved.json"); File.WriteAllText(path, "broken");
            Assert.Throws<InvalidOperationException>(() => Reserve()); Assert.That(File.ReadAllText(path), Is.EqualTo("broken"));
        }
        [Test] public void InvalidProjectVersionFailsBeforeReservation()
        {
            Assert.Throws<InvalidOperationException>(() => Reserve("invalid"));
            Assert.That(File.Exists(Path.Combine(root, "reserved.json")), Is.False);
        }
        [Test] public void ExhaustedVersionCodeFailsBeforeReservation() => Assert.Throws<InvalidOperationException>(() => Reserve("0.0.2", int.MaxValue));
        [Test] public void MaterialFingerprintDetectsSameLengthContentChange()
        {
            string path = Path.Combine(root, "AOT.dll"); File.WriteAllText(path, "abc");
            var before = Call("AndroidQuickBuildUtility", "FingerprintFiles", new object[] { new[] { path } });
            File.WriteAllText(path, "xyz");
            Assert.That(Call("AndroidQuickBuildUtility", "FingerprintFiles", new object[] { new[] { path } }), Is.Not.EqualTo(before));
        }
        [Test] public void MissingReuseInputIsRejected() => Assert.Throws<FileNotFoundException>(() =>
            Call("AndroidQuickBuildUtility", "FingerprintFiles", new object[] { new[] { Path.Combine(root, "missing.dll") } }));
        [Test] public void RecoveryRejectsInputsEditedAfterPlayerStarted()
        {
            string path = Path.Combine(root, "Boot.cs"); File.WriteAllText(path, "source");
            Assert.Throws<InvalidOperationException>(() => Call("AndroidQuickBuildUtility", "ValidateRecoveryInputTimes", new[] { path }, DateTime.UtcNow.AddHours(-1)));
        }
        [Test] public void RecoveryAcceptsUnchangedOldInputs()
        {
            string path = Path.Combine(root, "Boot.cs"); File.WriteAllText(path, "source");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
            Assert.DoesNotThrow(() => Call("AndroidQuickBuildUtility", "ValidateRecoveryInputTimes", new[] { path }, DateTime.UtcNow.AddHours(-1)));
        }
        [Test] public void QuickWorkflowIsRegisteredWithoutExecutingIt()
        {
            var action = (Action)Call("AndroidBuildUtility", "ResolveWorkflow", "Android 复用本次 AOT和资源快速打包");
            Assert.That(action, Is.Not.Null); Assert.That(action.Method.Name, Is.EqualTo("BuildQuickReuseApkBatch"));
        }

        [Test] public void UnknownPreparedKindIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidateIdentity", 2, "Android", "Channel"));
        [Test] public void CrossPlatformPreparedSnapshotIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidateIdentity", 2, "StandaloneWindows64", "HostBase"));
        [Test] public void LegacyPreparedSnapshotIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidateIdentity", 0, "Android", "SmallHostBase"));

        [TestCase("new", false)]
        [TestCase("old", true)]
        public void ApkResourceValidationRejectsTamperedOrExtraFiles(string content, bool extra)
        {
            string builtin = Path.Combine(root, "builtin"); Directory.CreateDirectory(builtin);
            File.WriteAllText(Path.Combine(builtin, "sample.bundle"), "old");
            string apk = Path.Combine(root, "sample.apk");
            using (var archive = ZipFile.Open(apk, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("assets/yoo/DefaultPackage/sample.bundle").Open())) writer.Write(content);
                if (extra) archive.CreateEntry("assets/yoo/DefaultPackage/stale.bundle");
            }
            Assert.Throws<InvalidOperationException>(() => Call("AndroidQuickBuildUtility", "ValidateApkResources", apk, builtin));
        }
        [Test] public void AotReuseWorkflowIsRegisteredWithoutExecutingIt()
        {
            var action = (Action)Call("AndroidBuildUtility", "ResolveWorkflow", "Android 复用 AOT 打包");
            Assert.That(action.Method.Name, Is.EqualTo("BuildQuickReuseAotBatch"));
        }

        [Test] public void NonMainProjectSettingsChangeInvalidatesPreparedInputs()
        {
            string settings = Path.Combine(root, "ProjectSettings"); Directory.CreateDirectory(settings);
            File.WriteAllText(Path.Combine(settings, "ProjectSettings.asset"), "  bundleVersion: 1.0.0\n  AndroidBundleVersionCode: 1\n");
            string quality = Path.Combine(settings, "QualitySettings.asset"); File.WriteAllText(quality, "old");
            object before = Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root);
            File.WriteAllText(quality, "new");
            Assert.That(Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root), Is.Not.EqualTo(before));
        }
        [Test] public void ApplicationVersionOnlyChangeKeepsPreparedInputs()
        {
            string settings = Path.Combine(root, "ProjectSettings"); Directory.CreateDirectory(settings);
            string path = Path.Combine(settings, "ProjectSettings.asset");
            File.WriteAllText(path, "  bundleVersion: 1.0.0\n  AndroidBundleVersionCode: 1\n");
            object before = Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root);
            File.WriteAllText(path, "  bundleVersion: 1.0.1\n  AndroidBundleVersionCode: 2\n");
            Assert.That(Call("AndroidQuickBuildUtility", "ComputeSettingsFingerprint", root), Is.EqualTo(before));
        }
        [Test] public void CrossVersionFrozenDirectoryIsRejected() => Assert.Throws<InvalidOperationException>(() =>
            Call("AndroidQuickBuildUtility", "ValidatePreparedPaths", "20261030000000", Path.Combine(root, "packages", "20261030000000"),
                Path.Combine(root, "cache", "20261029000000"), Path.Combine(root, "packages"), Path.Combine(root, "cache")));

        [Test] public void HostAssetSelectionIncludesEmbeddedConfigButExcludesRemoteArtAndBundles()
        {
            foreach (string relative in new[] { "Resources/local.txt", "StreamingAssets/host.json", "StreamingAssets/yoo/DefaultPackage/test.bundle", "Game/Art/test.png", "Game/Prefabs/boot.prefab" })
            {
                string path = Path.Combine(root, "Assets", relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "data");
            }
            string boot = Path.GetFullPath(Path.Combine(root, "Assets", "Game/Prefabs/boot.prefab"));
            var files = (string[])Call("AndroidQuickAotUtility", "CollectHostAssetInputs", root, new[] { boot });
            Assert.That(files, Does.Contain(boot));
            Assert.That(files, Does.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "Resources/local.txt"))));
            Assert.That(files, Does.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "StreamingAssets/host.json"))));
            Assert.That(files, Does.Not.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "StreamingAssets/yoo/DefaultPackage/test.bundle"))));
            Assert.That(files, Does.Not.Contain(Path.GetFullPath(Path.Combine(root, "Assets", "Game/Art/test.png"))));
        }

        private string Preserve()
        {
            string dlls = Path.Combine(root, "dlls"); Directory.CreateDirectory(dlls);
            string list = Path.Combine(root, "list.bytes"); File.WriteAllText(list, "Sample.dll");
            return (string)Call("AndroidQuickBuildUtility", "PreserveAotMetadata", "20261017170250", list, dlls, Path.Combine(root, "cache"));
        }
        [Test] public void PreservedAotIsIndependentOfMutableBuildInputs()
        {
            string dlls = Path.Combine(root, "dlls"); Directory.CreateDirectory(dlls);
            string source = Path.Combine(dlls, "Sample.dll.bytes"); File.WriteAllText(source, "old-metadata");
            string cache = Preserve(); File.Delete(source);
            Assert.That(File.ReadAllText(Path.Combine(cache, "Sample.dll")), Is.EqualTo("old-metadata"));
        }
        [Test] public void ChangedMetadataCannotOverwriteAnExistingVersionBaseline()
        {
            string dlls = Path.Combine(root, "dlls"); Directory.CreateDirectory(dlls);
            string source = Path.Combine(dlls, "Sample.dll.bytes"); File.WriteAllText(source, "old");
            string cache = Preserve(); File.WriteAllText(source, "new");
            Assert.Throws<InvalidOperationException>(() => Preserve());
            Assert.That(File.ReadAllText(Path.Combine(cache, "Sample.dll")), Is.EqualTo("old"));
        }
        [Test] public void MissingMetadataDoesNotLeaveAnAcceptedBaseline()
        {
            Assert.Throws<FileNotFoundException>(() => Preserve());
            Assert.That(Directory.Exists(Path.Combine(root, "cache", "20261017170250")), Is.False);
            Assert.That(Directory.GetDirectories(Path.Combine(root, "cache")), Is.Empty);
        }
        [Test] public void MalformedPublishedNamePreventsUnsafeVersionAllocation()
        {
            Published("FirepowerLoop-invalid.apk");
            Assert.Throws<InvalidOperationException>(() => Reserve());
        }
        [Test] public void ExhaustedPatchVersionDoesNotWrap() =>
            Assert.Throws<InvalidOperationException>(() => Reserve("0.0.2147483647"));

        [Test] public void ExactResourceSelectionIgnoresNewerPackage()
        {
            foreach (string version in new[] { "20261016000000", "20261017000000" })
            {
                string path = Path.Combine(root, version); Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "DefaultPackage.version"), version);
                File.WriteAllText(Path.Combine(path, "DefaultPackage_" + version + ".bytes"), "manifest");
                File.WriteAllText(Path.Combine(path, "DefaultPackage_" + version + ".hash"), "hash");
                File.WriteAllText(Path.Combine(path, "sample.bundle"), "bundle");
            }
            object[] args = { root, "20261016000000", null, null, null };
            Assert.That(Call("ResourceServerPublishUtility", "TryFindPackage", args), Is.True);
            Assert.That(args[2], Is.EqualTo(Path.Combine(root, "20261016000000")));
            Assert.That(args[3], Is.EqualTo("20261016000000"));
        }
    }
}

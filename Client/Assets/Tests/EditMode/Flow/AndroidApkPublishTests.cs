using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace Game.Tests
{
    /// <summary>使用临时目录验证 Android 整包自动发布的文件事务与目录选择；不运行 Unity 构建。</summary>
    public sealed class AndroidApkPublishTests
    {
        private string root, apk, bundle, signingPolicy;
        private byte[] fixture;
        private string manualOutput;
        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerLoopApkPublishTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            fixture = File.ReadAllBytes(Path.Combine(Application.dataPath, "Tests/EditMode/Flow/AndroidSignedFixture.apk.bytes"));
            apk = Path.Combine(root, "input.apk"); File.WriteAllBytes(apk, fixture);
            signingPolicy = Path.Combine(root, "android-signing.json");
            File.WriteAllText(signingPolicy, "{\"schemaVersion\":1,\"certificateSha256\":\"cda3f92a5d2ede27605d93eae6345cd62f9dfe2460ed51a171b723a71614813b\"}");
            bundle = Path.Combine(root, "Bundle");
            Directory.CreateDirectory(Path.Combine(bundle, "ResourceFiles"));
            File.WriteAllText(Path.Combine(bundle, "ResourceFiles", "DefaultPackage.version"), "test");
        }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private string Manifest => Path.Combine(bundle, "AppUpdates", "Android", "app-update.json");
        [Test] public void UnsignedApkCannotReplaceExistingUpdate()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Manifest));
            File.WriteAllText(Manifest, "previous-publication");
            File.WriteAllBytes(apk, new byte[] { 1, 2, 3 });
            Assert.Throws<InvalidOperationException>(() => PublishProduction());
            Assert.That(File.ReadAllText(Manifest), Is.EqualTo("previous-publication"));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(Manifest), "FirepowerLoop-0.1.10-2.apk")), Is.False);
        }
        [Test] public void DifferentSigningCertificateCannotReplaceExistingUpdate()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Manifest));
            File.WriteAllText(Manifest, "previous-publication");
            Assert.That(Assert.Throws<InvalidOperationException>(() => PublishProduction()).Message, Does.Contain("SIGNATURE_MISMATCH"));
            Assert.That(File.ReadAllText(Manifest), Is.EqualTo("previous-publication"));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(Manifest), "FirepowerLoop-0.1.10-2.apk")), Is.False);
        }
        private string PublishProduction()
        {
            var type = Type.GetType("Game.Editor.AndroidApkPublishUtility, Assembly-CSharp-Editor");
            try { return (string)type.GetMethod("Publish").Invoke(null, new object[] { apk, bundle, "0.1.10", 2 }); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }
        private string Publish(string version = "0.1.10", int code = 2)
        {
            var type = Type.GetType("Game.Editor.AndroidApkPublishUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "自动 APK 发布尚未实现");
            try { return (string)type.GetMethod("PublishWithPolicy", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { apk, bundle, version, code, signingPolicy }); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }
        [Test] public void PublishCreatesDownloadableApkAndVersionInformation()
        {
            Assert.That(Publish(), Is.EqualTo(Manifest));
            var value = ApkUpdateManifest.ParseAvailable(File.ReadAllText(Manifest), "com.firepowerloop.game", "0.1.9", 1);
            Assert.That(value, Is.Not.Null);
            Assert.That(ApkUpdateManifest.ParseAvailable(File.ReadAllText(Manifest), "com.anotherday.game", "0.1.9", 1), Is.Null,
                "街机 APK 发布描述不能被旧项目当作兼容更新");
            Assert.That(value.VerifyFile(Path.Combine(Path.GetDirectoryName(Manifest), value.apkFileName)), Is.True);
            Assert.That(File.Exists(apk), Is.True);
        }
        [Test] public void NewVersionKeepsOldApkDownloadable()
        {
            Publish(); Publish("0.1.11", 3);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(Manifest), "FirepowerLoop-0.1.10-2.apk")), Is.True);
            Assert.That(File.ReadAllText(Manifest), Does.Contain("0.1.11"));
        }
        [Test] public void ConflictingVersionPreservesPreviousPublication()
        {
            Publish(); string before = File.ReadAllText(Manifest);
            File.WriteAllBytes(apk, File.ReadAllBytes(Path.Combine(Application.dataPath, "Tests/EditMode/Flow/AndroidSignedVariantFixture.apk.bytes")));
            Assert.Throws<InvalidOperationException>(() => Publish());
            Assert.That(File.ReadAllText(Manifest), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Manifest), "FirepowerLoop-0.1.10-2.apk")), Is.EqualTo(fixture));
        }
        [Test] public void MissingApkCannotReplaceExistingUpdate()
        {
            Publish(); string before = File.ReadAllText(Manifest); File.Delete(apk);
            Assert.Throws<FileNotFoundException>(() => Publish("0.1.11", 3));
            Assert.That(File.ReadAllText(Manifest), Is.EqualTo(before));
        }
        [Test] public void InvalidVersionCannotPublish() => Assert.Throws<ArgumentException>(() => Publish("invalid"));
        [Test] public void IdenticalPublicationIsRepeatable() { Publish(); string before = File.ReadAllText(Manifest); Publish(); Assert.That(File.ReadAllText(Manifest), Is.EqualTo(before)); }

        private string PublishLatest(string releaseRoot)
        {
            var type = Type.GetType("Game.Editor.AndroidApkPublishUtility, Assembly-CSharp-Editor");
            var method = type.GetMethod("PublishToLatestBundleWithPolicy", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "完整包发布到最近资源服的入口尚未实现");
            try { return (string)method.Invoke(null, new object[] { apk, releaseRoot, "0.1.10", 2, signingPolicy }); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }

        [Test] public void LatestPublicationKeepsExistingHotUpdateResources()
        {
            string release = Path.Combine(root, "Release");
            string older = Path.Combine(release, "ResourceServerBundle_20261005_120000");
            string latest = Path.Combine(release, "ResourceServerBundle_20261006_120000");
            foreach (string directory in new[] { older, latest })
            {
                Directory.CreateDirectory(Path.Combine(directory, "ResourceFiles"));
                File.WriteAllText(Path.Combine(directory, "ResourceFiles", "DefaultPackage.version"), "existing-version");
                File.WriteAllBytes(Path.Combine(directory, "ResourceFiles", "resource.bundle"), new byte[] { 9, 8, 7 });
            }
            string descriptor = PublishLatest(release);
            Assert.That(descriptor, Is.EqualTo(Path.Combine(latest, "AppUpdates", "Android", "app-update.json")));
            Assert.That(Directory.Exists(Path.Combine(older, "AppUpdates")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(latest, "ResourceFiles", "DefaultPackage.version")), Is.EqualTo("existing-version"));
            Assert.That(File.ReadAllBytes(Path.Combine(latest, "ResourceFiles", "resource.bundle")), Is.EqualTo(new byte[] { 9, 8, 7 }));
            var value = ApkUpdateManifest.ParseAvailable(File.ReadAllText(descriptor), "com.firepowerloop.game", "0.1.9", 1);
            Assert.That(value, Is.Not.Null);
            Assert.That(value.VerifyFile(Path.Combine(Path.GetDirectoryName(descriptor), value.apkFileName)), Is.True);
        }

        [Test] public void MissingResourceServerPreservesSourceApk()
        {
            Assert.Throws<InvalidOperationException>(() => PublishLatest(Path.Combine(root, "MissingRelease")));
            Assert.That(File.ReadAllBytes(apk), Is.EqualTo(fixture));
        }

        [Test] public void ManualPublisherRejectsDifferentCertificateWithoutReplacingManifest()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Manifest));
            File.WriteAllText(Manifest, "previous-publication");
            Assert.That(RunManualPublisher(Path.Combine(ServerScripts, "publish_app_update.ps1")), Is.Not.Zero);
            Assert.That(File.ReadAllText(Manifest), Is.EqualTo("previous-publication"));
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(Manifest), "FirepowerLoop-0.1.10-2.apk")), Is.False);
        }
        [Test] public void ManualPublisherUsesBundledSignaturePolicyAndTools()
        {
            string scripts = Path.Combine(root, "scripts"); Directory.CreateDirectory(scripts);
            foreach (string file in new[] { "publish_app_update.ps1", "validate_android_signature.ps1" })
                File.Copy(Path.Combine(ServerScripts, file), Path.Combine(scripts, file));
            File.Copy(signingPolicy, Path.Combine(scripts, "android-signing.json"));
            Assert.That(RunManualPublisher(Path.Combine(scripts, "publish_app_update.ps1")), Is.Zero, manualOutput);
            var manifest = ApkUpdateManifest.ParseAvailable(File.ReadAllText(Manifest), "com.firepowerloop.game", "0.1.9", 1);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.VerifyFile(Path.Combine(Path.GetDirectoryName(Manifest), manifest.apkFileName)), Is.True);
        }
        private static string ServerScripts => Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName, "Server", "ResourceServer");
        private int RunManualPublisher(string script)
        {
            using (var process = Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -Apk \"" + apk +
                "\" -VersionName 0.1.10 -VersionCode 2 -Bundle \"" + bundle + "\" -UnityExe \"" + EditorApplication.applicationPath + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                Assert.That(process.WaitForExit(10000), Is.True, "手动发布脚本超时");
                manualOutput = stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult();
                return process.ExitCode;
            }
        }
    }
}

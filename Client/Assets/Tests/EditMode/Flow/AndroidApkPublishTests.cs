using System;
using System.IO;
using System.Reflection;
using Boot;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>使用临时目录验证 Android 整包自动发布的文件事务与目录选择；不运行 Unity 构建。</summary>
    public sealed class AndroidApkPublishTests
    {
        private string root, apk, bundle;
        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AnotherDayApkPublishTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            apk = Path.Combine(root, "input.apk"); File.WriteAllBytes(apk, new byte[] { 1, 2, 3 });
            bundle = Path.Combine(root, "Bundle");
            Directory.CreateDirectory(Path.Combine(bundle, "ResourceFiles"));
            File.WriteAllText(Path.Combine(bundle, "ResourceFiles", "DefaultPackage.version"), "test");
        }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }
        private string Manifest => Path.Combine(bundle, "AppUpdates", "Android", "app-update.json");
        private string Publish(string version = "0.1.10", int code = 2)
        {
            var type = Type.GetType("Game.Editor.AndroidApkPublishUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "自动 APK 发布尚未实现");
            try { return (string)type.GetMethod("Publish").Invoke(null, new object[] { apk, bundle, version, code }); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }
        [Test] public void PublishCreatesDownloadableApkAndVersionInformation()
        {
            Assert.That(Publish(), Is.EqualTo(Manifest));
            var value = ApkUpdateManifest.ParseAvailable(File.ReadAllText(Manifest), "com.anotherday.game", "0.1.9", 1);
            Assert.That(value, Is.Not.Null);
            Assert.That(value.VerifyFile(Path.Combine(Path.GetDirectoryName(Manifest), value.apkFileName)), Is.True);
            Assert.That(File.Exists(apk), Is.True);
        }
        [Test] public void NewVersionKeepsOldApkDownloadable()
        {
            Publish(); Publish("0.1.11", 3);
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(Manifest), "AnotherDay-0.1.10-2.apk")), Is.True);
            Assert.That(File.ReadAllText(Manifest), Does.Contain("0.1.11"));
        }
        [Test] public void ConflictingVersionPreservesPreviousPublication()
        {
            Publish(); string before = File.ReadAllText(Manifest);
            File.WriteAllBytes(apk, new byte[] { 3, 2, 1 });
            Assert.Throws<InvalidOperationException>(() => Publish());
            Assert.That(File.ReadAllText(Manifest), Is.EqualTo(before));
            Assert.That(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Manifest), "AnotherDay-0.1.10-2.apk")), Is.EqualTo(new byte[] { 1, 2, 3 }));
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
            var method = type.GetMethod("PublishToLatestBundle");
            Assert.That(method, Is.Not.Null, "完整包发布到最近资源服的入口尚未实现");
            try { return (string)method.Invoke(null, new object[] { apk, releaseRoot, "0.1.10", 2 }); }
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
            var value = ApkUpdateManifest.ParseAvailable(File.ReadAllText(descriptor), "com.anotherday.game", "0.1.9", 1);
            Assert.That(value, Is.Not.Null);
            Assert.That(value.VerifyFile(Path.Combine(Path.GetDirectoryName(descriptor), value.apkFileName)), Is.True);
        }

        [Test] public void MissingResourceServerPreservesSourceApk()
        {
            Assert.Throws<InvalidOperationException>(() => PublishLatest(Path.Combine(root, "MissingRelease")));
            Assert.That(File.ReadAllBytes(apk), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
    }
}

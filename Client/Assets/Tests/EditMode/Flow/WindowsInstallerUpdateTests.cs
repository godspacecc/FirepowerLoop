using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Boot;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>PC 安装包身份和原生交接边界；仅启动无安装行为的系统程序，不切平台、不构建或安装游戏。</summary>
    public sealed class WindowsInstallerUpdateTests
    {
        [Test]
        public void NativeLaunchReportsMissingFileWithoutThrowing()
        {
            var method = typeof(WindowsInstaller).GetMethod("TryStartNative", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "IL2CPP 启动必须使用 Windows 原生接口。");
            object[] arguments = { Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), null };
            Assert.That(method.Invoke(null, arguments), Is.False);
            Assert.That(arguments[1], Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void NativeLaunchCanStartBenignWindowsProgram()
        {
            var method = typeof(WindowsInstaller).GetMethod("TryStartNative", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            // where.exe 只查询路径，不执行安装；未知参数使它自行退出。
            object[] arguments = { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), null };
            Assert.That(method.Invoke(null, arguments), Is.True, arguments[1] as string);
            Assert.That(arguments[1], Is.Null);
        }

        [Test]
        public void NativeHandoffUsesCurrentPidAndCreationTime()
        {
            var method = typeof(WindowsInstaller).GetMethod("GetHandoffArguments", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            using (var current = System.Diagnostics.Process.GetCurrentProcess())
            {
                long created = current.StartTime.ToUniversalTime().ToFileTimeUtc();
                Assert.That(method.Invoke(null, null), Is.EqualTo(
                    $"/WAITPID={current.Id} /WAITLOW={(uint)created} /WAITHIGH={(uint)(created >> 32)}"));
            }
        }

        private static Type RequiredType(string name)
        {
            var type = typeof(BootstrapConfig).Assembly.GetType(name);
            Assert.That(type, Is.Not.Null, "Windows 整包更新尚未实现。");
            return type;
        }

        private static string Manifest(int release = 10, string name = "AnotherDay-Setup-10.exe",
            string product = "com.anotherday.game", string platform = "Windows", string arch = "x64",
            long size = 3, string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") =>
            $"{{\"schemaVersion\":1,\"productId\":\"{product}\",\"platform\":\"{platform}\",\"architecture\":\"{arch}\",\"versionName\":\"0.1.10\",\"releaseNumber\":{release},\"installerFileName\":\"{name}\",\"sizeBytes\":{size},\"sha256\":\"{hash}\"}}";

        private static object Parse(string json, int current = 1) =>
            RequiredType("Boot.WindowsUpdateManifest").GetMethod("ParseAvailable").Invoke(null, new object[] { json, current, false });

        [Test] public void FullInstallerCanSkipIntermediateReleases() => Assert.That(Parse(Manifest()), Is.Not.Null);
        [Test] public void EqualReleaseIsNotAnUpdate() => Assert.That(Parse(Manifest(), 10), Is.Null);
        [Test] public void DowngradeIsRejected() => Assert.That(Parse(Manifest(), 11), Is.Null);
        [Test] public void OtherProductIsRejected() => Assert.That(Parse(Manifest(product: "other")), Is.Null);
        [Test] public void AndroidManifestIsRejected() => Assert.That(Parse(Manifest(platform: "Android")), Is.Null);
        [Test] public void OtherArchitectureIsRejected() => Assert.That(Parse(Manifest(arch: "arm64")), Is.Null);
        [TestCase("../setup.exe")]
        [TestCase("setup.exe.cmd")]
        [TestCase("https://other/setup.exe")]
        public void InstallerFilenameCannotEscapePublishedDirectory(string file) => Assert.That(Parse(Manifest(name: file)), Is.Null);
        [Test] public void InvalidHashIsRejected() => Assert.That(Parse(Manifest(hash: "abc")), Is.Null);
        [Test] public void EmptyFileIsRejected() => Assert.That(Parse(Manifest(size: 0)), Is.Null);
        [Test]
        public void CachedInstallerIsRevalidatedBeforeReuse()
        {
            string path = Path.GetTempFileName();
            try
            {
                byte[] bytes = { 1, 2, 3 };
                File.WriteAllBytes(path, bytes);
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                object candidate = Parse(Manifest(hash: hash));
                MethodInfo verify = candidate.GetType().GetMethod("VerifyFile");
                Assert.That(verify.Invoke(candidate, new object[] { path }), Is.True);
                File.WriteAllBytes(path, new byte[] { 3, 2, 1 });
                Assert.That(verify.Invoke(candidate, new object[] { path }), Is.False);
            }
            finally { File.Delete(path); }
        }
    }
}

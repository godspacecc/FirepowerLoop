using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests.Flow
{
    /// <summary>验证整包缓存清理的版本边界和文件隔离；仅操作测试临时目录，不触发安装。</summary>
    public sealed class UpdatePackageCacheTests
    {
        private string root;
        private Type cache;
        private const string Identity = "com.anotherday.game";

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AnotherDayUpdateCache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            cache = Type.GetType("Boot.UpdatePackageCache, Boot");
            Assert.That(cache, Is.Not.Null, "缺少已安装整包缓存清理器。");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private string Package(char hash, string extension, string version, int release, string identity = Identity)
        {
            string digest = new string(hash, 64);
            string path = Path.Combine(root, digest + extension);
            File.WriteAllText(path, "verified-package");
            cache.GetMethod("RecordVerified").Invoke(null, new object[] { root, extension, digest, identity, version, release });
            return path;
        }

        private void Clean(string extension, string version, int release)
        {
            cache.GetMethod("CleanInstalled").Invoke(null, new object[] { root, extension, Identity, version, release });
        }

        [TestCase(".apk")]
        [TestCase(".exe")]
        public void InstalledAndOlderPackagesAreDeletedButFutureAndUnmanagedFilesRemain(string extension)
        {
            string older = Package('a', extension, "0.1.1", 1);
            string installed = Package('b', extension, "0.1.2", 2);
            string future = Package('c', extension, "0.1.3", 3);
            string unmanaged = Path.Combine(root, new string('d', 64) + extension);
            File.WriteAllText(unmanaged, "legacy-without-version");
            File.WriteAllText(Path.Combine(root, "save.json"), "save");
            Clean(extension, "0.1.2", 2);
            Assert.That(File.Exists(older), Is.False);
            Assert.That(File.Exists(installed), Is.False);
            Assert.That(File.Exists(installed + ".receipt.json"), Is.False);
            Assert.That(File.Exists(future), Is.True);
            Assert.That(File.Exists(unmanaged), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(root, "save.json")), Is.EqualTo("save"));
            Assert.DoesNotThrow(() => Clean(extension, "0.1.2", 2));
        }

        [Test]
        public void AndroidSameCodeButNewerVersionRemainsUntilNewVersionRuns()
        {
            string path = Package('a', ".apk", "0.1.3", 2);
            Clean(".apk", "0.1.2", 2);
            Assert.That(File.Exists(path), Is.True);
            Clean(".apk", "0.1.3", 2);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void UnknownInstalledVersionAndOtherIdentityOrPlatformAreNeverDeleted()
        {
            string path = Package('a', ".apk", "0.1.1", 1);
            string other = Package('b', ".apk", "0.1.1", 1, "other.game");
            string windows = Package('c', ".exe", "0.1.1", 1);
            Clean(".apk", "invalid", 2);
            Clean(".apk", "0.1.2", 0);
            Assert.That(File.Exists(path), Is.True);
            Clean(".apk", "0.1.2", 2);
            Assert.That(File.Exists(other), Is.True);
            Assert.That(File.Exists(windows), Is.True);
        }

        [Test]
        public void MalformedAndOversizedReceiptsArePreserved()
        {
            string malformed = Package('a', ".apk", "0.1.1", 1);
            string oversized = Package('b', ".apk", "0.1.1", 1);
            File.WriteAllText(malformed + ".receipt.json", "{broken");
            File.WriteAllText(oversized + ".receipt.json", new string('x', 65537));
            Assert.DoesNotThrow(() => Clean(".apk", "0.1.2", 2));
            Assert.That(File.Exists(malformed), Is.True);
            Assert.That(File.Exists(oversized), Is.True);
        }

        [Test]
        public void LockedPackageRetainsReceiptAndRetriesNextStartup()
        {
            string path = Package('a', ".exe", "0.1.1", 1);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.DoesNotThrow(() => Clean(".exe", "0.1.2", 2));
                Assert.That(File.Exists(path + ".receipt.json"), Is.True);
            }
            Clean(".exe", "0.1.2", 2);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        public void TraversalAndSubdirectoriesAreNotOwnedByCache()
        {
            string sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            string path = Path.Combine(sub, "keep.apk");
            File.WriteAllText(path, "keep");
            cache.GetMethod("RecordVerified").Invoke(null, new object[] { root, ".apk", "../sub/keep", Identity, "0.1.1", 1 });
            cache.GetMethod("CleanInstalled").Invoke(null, new object[] { root, "../sub/keep.apk", Identity, "0.1.2", 2 });
            Clean(".apk", "0.1.2", 2);
            Assert.That(File.ReadAllText(path), Is.EqualTo("keep"));
        }

        [Test]
        public void UnwritableRootDoesNotBlockDownloadOrStartup()
        {
            string blocked = Path.Combine(root, "file");
            File.WriteAllText(blocked, "blocking-root");
            Assert.DoesNotThrow(() => cache.GetMethod("RecordVerified").Invoke(null,
                new object[] { blocked, ".apk", new string('a', 64), Identity, "0.1.1", 1 }));
            Assert.DoesNotThrow(() => cache.GetMethod("CleanInstalled").Invoke(null,
                new object[] { blocked, ".apk", Identity, "0.1.2", 2 }));
        }
    }
}

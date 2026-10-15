using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Boot;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>整包更新的版本、包身份与下载完整性边界；不执行安装。</summary>
    public sealed class ApkUpdateTests
    {
        private Type Policy
        {
            get
            {
                var type = typeof(BootstrapConfig).Assembly.GetType("Boot.ApkUpdateManifest");
                Assert.That(type, Is.Not.Null, "整包版本检查尚未实现");
                return type;
            }
        }
        private string Json(string version = "0.1.10", string file = "AnotherDay-0.1.10.apk",
            string package = "com.anotherday.game", int code = 2, long size = 3,
            string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa") =>
            $"{{\"schemaVersion\":1,\"platform\":\"Android\",\"packageName\":\"{package}\",\"versionName\":\"{version}\",\"versionCode\":{code},\"apkFileName\":\"{file}\",\"sizeBytes\":{size},\"sha256\":\"{hash}\"}}";
        private object Parse(string json, string current = "0.1.9", int code = 1) =>
            Policy.GetMethod("ParseAvailable").Invoke(null, new object[] { json, "com.anotherday.game", current, code });
        [Test] public void NumericVersionComparisonAcceptsTenAfterNine() => Assert.That(Parse(Json()), Is.Not.Null);
        [Test] public void EqualVersionDoesNotOfferUpdate() => Assert.That(Parse(Json(), "0.1.10"), Is.Null);
        [Test] public void OlderVersionDoesNotOfferUpdate() => Assert.That(Parse(Json(), "0.2.0"), Is.Null);
        [Test] public void WrongPackageCannotBeInstalled() => Assert.That(Parse(Json(package: "other.app")), Is.Null);
        [Test] public void LowerAndroidVersionCodeIsRejected() => Assert.That(Parse(Json(), code: 3), Is.Null);
        [Test] public void TraversalFilenameIsRejected() => Assert.That(Parse(Json(file: "../other.apk")), Is.Null);
        [Test] public void InvalidSemanticVersionIsRejected() => Assert.That(Parse(Json(version: "0.1.10-beta")), Is.Null);
        [Test] public void InvalidHashIsRejected() => Assert.That(Parse(Json(hash: "123")), Is.Null);
        [Test] public void EmptyDownloadIsRejected() => Assert.That(Parse(Json(size: 0)), Is.Null);
        [Test]
        public void DownloadMustMatchBothLengthAndSha256()
        {
            string path = Path.GetTempFileName();
            try
            {
                byte[] bytes = { 1, 2, 3 };
                File.WriteAllBytes(path, bytes);
                string hash;
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                object manifest = Parse(Json(hash: hash));
                var verify = Policy.GetMethod("VerifyFile");
                Assert.That(verify.Invoke(manifest, new object[] { path }), Is.True);
                File.WriteAllBytes(path, new byte[] { 3, 2, 1 });
                Assert.That(verify.Invoke(manifest, new object[] { path }), Is.False);
                File.WriteAllBytes(path, new byte[] { 1, 2 });
                Assert.That(verify.Invoke(manifest, new object[] { path }), Is.False);
            }
            finally { File.Delete(path); }
        }
    }
}

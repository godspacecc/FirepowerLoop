using Boot;
using NUnit.Framework;

namespace Game.Tests
{
    /// <summary>验证原生结果协议、玩家原因及可复制详情，不把交接成功当成覆盖安装成功。</summary>
    public sealed class ApkInstallResultTests
    {
        [Test]
        public void ValidBridgeResultsPreservePendingAndActivityHandoff()
        {
            foreach (int status in new[] { 0, 1, 2 })
                Assert.That(ApkInstallResult.Parse("{\"schemaVersion\":1,\"result\":" + status + ",\"stage\":\"system_intent\"}").result, Is.EqualTo(status));
        }
        [Test]
        public void InvalidBridgeDataCannotBecomeSuccessfulInstall()
        {
            foreach (string json in new[] { null, "{}", "invalid", "{\"result\":0,\"stage\":\"installer\"}", "{\"schemaVersion\":2,\"result\":0,\"stage\":\"installer\"}" })
                Assert.That(ApkInstallResult.Parse(json).errorCode, Is.EqualTo("INVALID_BRIDGE_RESULT"));
        }
        [Test]
        public void KnownFailuresShowActionablePlayerReason()
        {
            Assert.That(ApkInstallResult.Failure("SIGNATURE_MISMATCH", "validation", "").PlayerMessage, Does.Contain("签名"));
            Assert.That(ApkInstallResult.Failure("INSTALL_TIMEOUT", "system_intent", "").PlayerMessage, Does.Contain("超时"));
            Assert.That(ApkInstallResult.Failure("SECURITY_EXCEPTION", "system_intent", "").PlayerMessage, Does.Contain("权限"));
        }
        [Test]
        public void CopyIncludesVersionErrorAndBoundedNativeException()
        {
            string text = ApkInstallResult.Failure("INTENT_EXCEPTION", "system_intent", new string('x', 20000))
                .ToCopyDetails(new ApkUpdateManifest { versionName = "0.1.16", versionCode = 6, apkFileName = "test.apk", sha256 = "hash", sizeBytes = 42 });
            Assert.That(text, Does.Contain("INTENT_EXCEPTION").And.Contain("system_intent").And.Contain("0.1.16").And.Contain("42 bytes"));
            Assert.That(text.Length, Is.LessThanOrEqualTo(16384));
            Assert.That(text, Does.Contain("截断"));
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>验证固定签名的失败门禁与设置恢复；不调用 Player Build，也不依赖生产私钥。</summary>
    public sealed class AndroidSigningTests
    {
        private string root, settings, policy, fixture;
        private string keyRoot, testKeystore, testFingerprint;

        [OneTimeSetUp] public void CreateDisposableTestKey()
        {
            keyRoot = Path.Combine(Path.GetTempPath(), "FirepowerSigningKeyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(keyRoot);
            testKeystore = Path.Combine(keyRoot, "fixture.keystore");
            RunKeytool("-genkeypair -storetype JKS -keystore \"" + testKeystore +
                "\" -storepass fixture-store -keypass fixture-key -alias fixture -keyalg RSA -validity 2 -dname \"CN=Disposable Signing Test\"");
            string certificate = Path.Combine(keyRoot, "certificate.der");
            RunKeytool("-exportcert -keystore \"" + testKeystore + "\" -storepass fixture-store -alias fixture -file \"" + certificate + "\"");
            using (var hash = SHA256.Create())
                testFingerprint = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(certificate))).Replace("-", "").ToLowerInvariant();
        }
        [OneTimeTearDown] public void RemoveDisposableTestKey() { if (Directory.Exists(keyRoot)) Directory.Delete(keyRoot, true); }
        [SetUp] public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "FirepowerSigningTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            settings = Path.Combine(root, "local.json");
            policy = Path.Combine(root, "policy.json");
            fixture = Path.Combine(Application.dataPath, "Tests/EditMode/Flow/AndroidSignedFixture.apk.bytes");
            File.WriteAllText(policy, "{\"schemaVersion\":1,\"certificateSha256\":\"cda3f92a5d2ede27605d93eae6345cd62f9dfe2460ed51a171b723a71614813b\"}");
        }
        [TearDown] public void TearDown() { Directory.Delete(root, true); }

        private static object Call(string method, params object[] args)
        {
            var type = Type.GetType("Game.Editor.AndroidSigningUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            var target = type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            try { return target.Invoke(null, args); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }

        [Test] public void MissingLocalCredentialsCannotFallBackToMachineDebugKey()
        {
            bool before = PlayerSettings.Android.useCustomKeystore;
            Assert.That(Assert.Throws<InvalidOperationException>(() => Call("UseFixedSigning", settings, policy)).Message, Does.Contain("固定签名配置"));
            Assert.That(PlayerSettings.Android.useCustomKeystore, Is.EqualTo(before));
        }
        [Test] public void IncompleteCredentialsFailWithoutChangingPlayerSettings()
        {
            File.WriteAllText(settings, "{\"keystorePath\":\"missing.keystore\",\"keyAlias\":\"fixture\"}");
            string before = PlayerSettings.Android.keystoreName;
            Assert.Throws<InvalidOperationException>(() => Call("UseFixedSigning", settings, policy));
            Assert.That(PlayerSettings.Android.keystoreName, Is.EqualTo(before));
        }
        [Test] public void InvalidCredentialsDoNotLeakPasswordsInExceptions()
        {
            File.WriteAllText(settings, "{\"keystorePath\":\"missing.keystore\",\"keyAlias\":\"fixture\",\"keystorePassword\":\"store-secret-marker\",\"keyPassword\":\"key-secret-marker\"}");
            var error = Assert.Throws<InvalidOperationException>(() => Call("UseFixedSigning", settings, policy));
            Assert.That(error.Message, Does.Not.Contain("store-secret-marker").And.Not.Contain("key-secret-marker"));
        }
        [Test] public void MatchingSignedApkPassesWithoutAnyPrivateKey()
        {
            Assert.DoesNotThrow(() => Call("ValidateApk", fixture, policy));
        }
        [Test] public void CertificateMismatchRejectsCryptographicallyValidApk()
        {
            File.WriteAllText(policy, "{\"schemaVersion\":1,\"certificateSha256\":\"0000000000000000000000000000000000000000000000000000000000000000\"}");
            Assert.That(Assert.Throws<InvalidOperationException>(() => Call("ValidateApk", fixture, policy)).Message, Does.Contain("SIGNATURE_MISMATCH"));
        }
        [Test] public void InvalidPolicyCannotDisableSignatureValidation()
        {
            File.WriteAllText(policy, "{\"schemaVersion\":1,\"certificateSha256\":\"\"}");
            Assert.Throws<InvalidOperationException>(() => Call("ValidateApk", fixture, policy));
        }
        [Test] public void TamperedApkIsRejectedEvenWithMatchingCertificatePolicy()
        {
            string tampered = Path.Combine(root, "tampered.apk");
            byte[] bytes = File.ReadAllBytes(fixture);
            bytes[60] ^= 0x20;
            File.WriteAllBytes(tampered, bytes);
            Assert.Throws<InvalidOperationException>(() => Call("ValidateApk", tampered, policy));
        }

        [Test] public void ValidatedSigningScopeRestoresEverySettingWhenCallerThrows()
        {
            ConfigureDisposableKey("fixture-key", testFingerprint);
            var before = new SettingsSnapshot();
            try
            {
                using ((IDisposable)Call("UseFixedSigning", settings, policy))
                {
                    Assert.That(PlayerSettings.Android.useCustomKeystore, Is.True);
                    Assert.That(Path.GetFullPath(PlayerSettings.Android.keystoreName), Is.EqualTo(Path.GetFullPath(testKeystore)));
                    Assert.That(PlayerSettings.Android.keyaliasName, Is.EqualTo("fixture"));
                    Assert.That(PlayerSettings.Android.keystorePass == "fixture-store", Is.True);
                    Assert.That(PlayerSettings.Android.keyaliasPass == "fixture-key", Is.True);
                    throw new OperationCanceledException("simulated caller failure");
                }
            }
            catch (OperationCanceledException) { }
            before.AssertRestored();
        }
        [Test] public void WrongPrivateKeyPasswordIsRejectedBeforeChangingSettings()
        {
            ConfigureDisposableKey("wrong-key-marker", testFingerprint);
            var before = new SettingsSnapshot();
            var error = Assert.Throws<InvalidOperationException>(() => Call("UseFixedSigning", settings, policy));
            Assert.That(error.Message, Does.Not.Contain("wrong-key-marker").And.Not.Contain("fixture-store"));
            before.AssertRestored();
        }
        [Test] public void ValidPrivateKeyWithUnexpectedCertificateCannotBeApplied()
        {
            ConfigureDisposableKey("fixture-key", "0000000000000000000000000000000000000000000000000000000000000000");
            var before = new SettingsSnapshot();
            Assert.That(Assert.Throws<InvalidOperationException>(() => Call("UseFixedSigning", settings, policy)).Message, Does.Contain("SIGNATURE_MISMATCH"));
            before.AssertRestored();
        }

        private void ConfigureDisposableKey(string keyPassword, string fingerprint)
        {
            File.WriteAllText(settings, JsonUtility.ToJson(new TestCredentials
            { keystorePath = testKeystore, keyAlias = "fixture", keystorePassword = "fixture-store", keyPassword = keyPassword }));
            File.WriteAllText(policy, "{\"schemaVersion\":1,\"certificateSha256\":\"" + fingerprint + "\"}");
        }
        private static void RunKeytool(string arguments)
        {
            string tool = Path.Combine(Path.GetDirectoryName(EditorApplication.applicationPath), "Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/keytool.exe");
            using (var process = Process.Start(new ProcessStartInfo(tool, arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                Assert.That(process.WaitForExit(10000), Is.True, "生成一次性测试密钥超时");
                stdout.GetAwaiter().GetResult(); stderr.GetAwaiter().GetResult();
                Assert.That(process.ExitCode, Is.Zero, "生成一次性测试密钥失败");
            }
        }
        [Serializable] private sealed class TestCredentials
        {
            public string keystorePath, keyAlias, keystorePassword, keyPassword;
        }
        private sealed class SettingsSnapshot
        {
            private readonly bool custom = PlayerSettings.Android.useCustomKeystore;
            private readonly string path = PlayerSettings.Android.keystoreName, alias = PlayerSettings.Android.keyaliasName,
                storePassword = PlayerSettings.Android.keystorePass, keyPassword = PlayerSettings.Android.keyaliasPass;
            public void AssertRestored()
            {
                Assert.That(PlayerSettings.Android.useCustomKeystore == custom, Is.True, "useCustomKeystore 未恢复");
                Assert.That(PlayerSettings.Android.keystoreName == path, Is.True, "keystoreName 未恢复");
                Assert.That(PlayerSettings.Android.keyaliasName == alias, Is.True, "keyaliasName 未恢复");
                Assert.That(PlayerSettings.Android.keystorePass == storePassword, Is.True, "keystorePass 未恢复");
                Assert.That(PlayerSettings.Android.keyaliasPass == keyPassword, Is.True, "keyaliasPass 未恢复");
            }
        }
    }
}

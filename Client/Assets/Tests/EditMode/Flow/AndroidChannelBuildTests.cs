using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>渠道候选包策略和真实 APK 读取的限定检查；不调用任何 Player 构建或发布。</summary>
    public sealed class AndroidChannelBuildTests
    {
        private const string Certificate = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        /// <summary>通过反射访问 Editor 默认程序集，保持现有测试程序集边界。</summary>
        private static object Call(string method, params object[] args)
        {
            var type = Type.GetType("Game.Editor.AndroidChannelBuildUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "缺少渠道候选包工具");
            var target = type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(target, Is.Not.Null, "缺少渠道检查方法：" + method);
            try { return target.Invoke(null, args); }
            catch (TargetInvocationException exception) { throw exception.InnerException; }
        }

        /// <summary>描述满足本项目渠道技术检查的包，不代表平台资质或真机验收完成。</summary>
        private static JObject ValidFacts() => JObject.Parse(@"{
            'packageName':'com.firepowerloop.game','versionName':'0.1.28','versionCode':18,
            'signatureValid':true,'certificateSha256':'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
            'certificateSubject':'CN=FirepowerLoop Release','debuggable':false,
            'architectures':['arm64-v8a'],'permissions':['android.permission.INTERNET'],
            'resourceServer':'https://resources.firepowerloop.com/android',
            'gameGateEndpoint':'gate.firepowerloop.com:31011',
            'diagnosticMode':'Fixed','diagnosticUrl':'https://logs.firepowerloop.com/errors'
        }");

        private static JObject Evaluate(JObject facts, bool channel = true, string expected = Certificate) =>
            JObject.Parse((string)Call("Evaluate", facts.ToString(), channel, expected));

        private static string[] Failures(JObject report) => report["checks"].Children<JObject>()
            .Where(check => (string)check["status"] == "Failed").Select(check => (string)check["id"]).ToArray();

        [Test] public void ChannelOptionsRemoveDevelopmentAndDebuggerFlags()
        {
            var flags = (BuildOptions)Call("BuildOptionsFor", true);
            Assert.That(flags & (BuildOptions.Development | BuildOptions.AllowDebugging | BuildOptions.ConnectWithProfiler), Is.EqualTo(BuildOptions.None));
            Assert.That(flags & BuildOptions.CompressWithLz4, Is.Not.EqualTo(BuildOptions.None));
        }

        [Test] public void InternalOptionsKeepExistingDevelopmentBehavior()
        {
            var flags = (BuildOptions)Call("BuildOptionsFor", false);
            Assert.That(flags & BuildOptions.Development, Is.Not.EqualTo(BuildOptions.None));
        }

        [Test] public void ValidChannelFactsPassTechnicalChecksOnly()
        {
            var report = Evaluate(ValidFacts());
            Assert.That((bool)report["passed"], Is.True);
            Assert.That((bool)report["deviceVerified"], Is.False);
            Assert.That((bool)report["storeApproved"], Is.False);
        }

        [Test] public void DebuggablePackageCannotPassChannelChecks()
        {
            var facts = ValidFacts(); facts["debuggable"] = true;
            Assert.That(Failures(Evaluate(facts)), Does.Contain("debuggable"));
        }

        [Test] public void DebugCertificateIsRejectedEvenWhenFingerprintMatches()
        {
            var facts = ValidFacts(); facts["certificateSubject"] = "C=US, O=Android, CN=Android Debug";
            Assert.That(Failures(Evaluate(facts)), Does.Contain("releaseCertificate"));
        }

        [Test] public void MissingOrDifferentExpectedCertificateCannotPass()
        {
            Assert.That(Failures(Evaluate(ValidFacts(), true, "")), Does.Contain("certificate"));
            Assert.That(Failures(Evaluate(ValidFacts(), true, new string('b', 64))), Does.Contain("certificate"));
            var facts = ValidFacts(); facts["signatureValid"] = false;
            Assert.That(Failures(Evaluate(facts)), Does.Contain("signature"));
        }

        [Test] public void LocalAndPlaceholderServersAreRejectedForChannel()
        {
            foreach (string host in new[] { "192.168.31.94", "127.0.0.1", "10.0.0.2", "172.16.0.1", "169.254.1.1", "100.64.0.1", "[::1]", "[fc00::1]", "localhost", "server.local", "server.example", "example.com" })
            {
                var facts = ValidFacts(); facts["resourceServer"] = "https://" + host + "/assets";
                Assert.That(Failures(Evaluate(facts)), Does.Contain("resourceServer"), host);
            }
            var invalidGate = ValidFacts(); invalidGate["gameGateEndpoint"] = "192.168.31.94:31011";
            Assert.That(Failures(Evaluate(invalidGate)), Does.Contain("gameGateEndpoint"));
        }

        [Test] public void CleartextAndCredentialBearingUrlsAreRejected()
        {
            foreach (string url in new[] { "http://resources.firepowerloop.com", "https://user:pass@resources.firepowerloop.com", "https://resources.firepowerloop.com/#fragment" })
            {
                var facts = ValidFacts(); facts["resourceServer"] = url;
                Assert.That(Failures(Evaluate(facts)), Does.Contain("resourceServer"));
            }
        }

        [Test] public void DiagnosticLocalModeCannotMasqueradeAsDisabledUpload()
        {
            var facts = ValidFacts(); facts["diagnosticMode"] = "LocalGameServer"; facts["diagnosticUrl"] = "";
            Assert.That(Failures(Evaluate(facts)), Does.Contain("diagnostics"));
        }

        [Test] public void InternalInspectionAllowsDebugAndLocalServices()
        {
            var facts = ValidFacts(); facts["debuggable"] = true;
            facts["certificateSubject"] = "CN=Android Debug";
            facts["resourceServer"] = "http://192.168.31.94:8001";
            facts["gameGateEndpoint"] = "192.168.31.94:31011";
            facts["diagnosticMode"] = "LocalGameServer"; facts["diagnosticUrl"] = "";
            Assert.That((bool)Evaluate(facts, false)["passed"], Is.True);
        }

        [Test] public void IdentityAndArchitectureErrorsAreReportedTogether()
        {
            var facts = ValidFacts(); facts["packageName"] = "com.anotherday.game";
            facts["versionCode"] = 0; facts["architectures"] = new JArray("armeabi-v7a");
            Assert.That(Failures(Evaluate(facts)), Does.Contain("package").And.Contain("version").And.Contain("architecture"));
        }

        [Test] public void InstallerPermissionRemainsVisibleAsReviewWarning()
        {
            var facts = ValidFacts(); facts["permissions"] = new JArray("android.permission.INTERNET", "android.permission.REQUEST_INSTALL_PACKAGES");
            var report = Evaluate(facts);
            Assert.That(report["checks"].Children<JObject>().Any(check => (string)check["id"] == "installerPermission" && (string)check["status"] == "Warning"), Is.True);
        }

        [Test] public void SignedFixtureCanBeInspectedWithoutRunningItsCode()
        {
            string fixture = Path.Combine(Application.dataPath, "Tests/EditMode/Flow/AndroidSignedFixture.apk.bytes");
            var report = JObject.Parse((string)Call("Inspect", fixture, false, "cda3f92a5d2ede27605d93eae6345cd62f9dfe2460ed51a171b723a71614813b"));
            Assert.That((bool)report["facts"]["signatureValid"], Is.True);
            Assert.That((string)report["facts"]["certificateSha256"], Is.EqualTo("cda3f92a5d2ede27605d93eae6345cd62f9dfe2460ed51a171b723a71614813b"));
            Assert.That((string)report["facts"]["sha256"], Has.Length.EqualTo(64));
        }

        [Test] public void MissingChannelCredentialsFailBeforeEditorSettingsChange()
        {
            string original = Environment.GetEnvironmentVariable("FIREPOWER_ANDROID_CHANNEL_SIGNING_CONFIG");
            bool development = EditorUserBuildSettings.development;
            string version = PlayerSettings.bundleVersion;
            try
            {
                Environment.SetEnvironmentVariable("FIREPOWER_ANDROID_CHANNEL_SIGNING_CONFIG", null);
                Assert.That(Assert.Throws<InvalidOperationException>(() => Call("ValidateChannelConfiguration")).Message,
                    Does.Contain("FIREPOWER_ANDROID_CHANNEL_SIGNING_CONFIG"));
                Assert.That(EditorUserBuildSettings.development, Is.EqualTo(development));
                Assert.That(PlayerSettings.bundleVersion, Is.EqualTo(version));
            }
            finally { Environment.SetEnvironmentVariable("FIREPOWER_ANDROID_CHANNEL_SIGNING_CONFIG", original); }
        }

        [Test] public void ChannelEditorOptionsRestoreOriginalValues()
        {
            var optionsType = Type.GetType("Game.Editor.AndroidBuildUtility+AndroidEditorOptions, Assembly-CSharp-Editor");
            Assert.That(optionsType, Is.Not.Null);
            object snapshot = optionsType.GetMethod("Capture").Invoke(null, null);
            bool development = EditorUserBuildSettings.development, debugging = EditorUserBuildSettings.allowDebugging,
                profiler = EditorUserBuildSettings.connectProfiler, appBundle = EditorUserBuildSettings.buildAppBundle;
            try
            {
                optionsType.GetMethod("ApplyChannelBuildOptions").Invoke(snapshot, null);
                Assert.That(EditorUserBuildSettings.development || EditorUserBuildSettings.allowDebugging ||
                    EditorUserBuildSettings.connectProfiler || EditorUserBuildSettings.buildAppBundle, Is.False);
            }
            finally { optionsType.GetMethod("Restore").Invoke(snapshot, null); }
            Assert.That(EditorUserBuildSettings.development, Is.EqualTo(development));
            Assert.That(EditorUserBuildSettings.allowDebugging, Is.EqualTo(debugging));
            Assert.That(EditorUserBuildSettings.connectProfiler, Is.EqualTo(profiler));
            Assert.That(EditorUserBuildSettings.buildAppBundle, Is.EqualTo(appBundle));
        }

        [Test] public void PlatformReloadResolvesChannelEntryWithoutInvokingBuild()
        {
            var buildType = Type.GetType("Game.Editor.AndroidBuildUtility, Assembly-CSharp-Editor");
            var resolver = buildType.GetMethod("ResolveWorkflow", BindingFlags.Static | BindingFlags.NonPublic);
            var action = (Action)resolver.Invoke(null, new object[] { "Android 渠道候选包完整打包" });
            Assert.That(action, Is.Not.Null);
            Assert.That(action.Method.Name, Is.EqualTo("BuildChannelCandidateBatch"));
        }

        [Test] public void Ipv6DocumentationAddressCannotPassPublicServerCheck()
        {
            var facts = ValidFacts(); facts["resourceServer"] = "https://[2001:db8::1]/assets";
            Assert.That(Failures(Evaluate(facts)), Does.Contain("resourceServer"));
        }

        [Test] public void PublicIpv4OutsideDocumentationSubnetIsNotRejected()
        {
            var facts = ValidFacts(); facts["resourceServer"] = "https://198.51.42.1/assets";
            Assert.That((bool)Evaluate(facts)["passed"], Is.True);
            facts["resourceServer"] = "https://198.51.100.1/assets";
            Assert.That(Failures(Evaluate(facts)), Does.Contain("resourceServer"));
        }

        [Test] public void InspectionInputPreventsConcurrentOverwrite()
        {
            string file = Path.Combine(Path.GetTempPath(), "FirepowerApkLock-" + Guid.NewGuid().ToString("N") + ".apk");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            try
            {
                using ((IDisposable)Call("OpenInspectionInput", file))
                    Assert.Throws<IOException>(() => File.WriteAllBytes(file, new byte[] { 4, 5, 6 }));
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(file));
            }
            finally { File.Delete(file); }
        }
    }
}

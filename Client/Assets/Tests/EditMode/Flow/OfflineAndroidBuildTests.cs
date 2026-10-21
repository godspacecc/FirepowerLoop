using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Xml;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Analytics;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>离线构建策略与生成清单处理的限定检查；不调用 Player Build。</summary>
    public sealed class OfflineAndroidBuildTests
    {
        private static object Call(string method, params object[] args)
        {
            var type = Type.GetType("Game.Editor.AndroidOfflineReleaseBuildUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null, "缺少独立离线候选构建策略");
            try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args); }
            catch (TargetInvocationException error) { throw error.GetBaseException(); }
        }
        private static JObject Facts() => JObject.Parse(@"{'packageName':'com.firepowerloop.game','versionName':'0.1.28','versionCode':18,
            'signatureValid':true,'certificateSha256':'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
            'certificateSubject':'CN=FirepowerLoop Release','debuggable':false,'architectures':['arm64-v8a'],'permissions':[], 'releaseProfile':'OfflineSinglePlayer'}");
        private static JObject Evaluate(JObject facts)
        {
            var type = Type.GetType("Game.Editor.AndroidChannelBuildUtility, Assembly-CSharp-Editor");
            var method = type.GetMethod("EvaluateOffline"); Assert.That(method, Is.Not.Null);
            return JObject.Parse((string)method.Invoke(null, new object[] { facts.ToString(), new string('a', 64) }));
        }
        [Test] public void OfflineDefinesAreExplicitAndRequireBuiltinResources()
        {
            var offline = (string[])Call("PlayerDefines", true, true);
            Assert.That(offline, Does.Contain("FIREPOWERLOOP_ANDROID_BUILTIN_RESOURCES").And.Contain("FIREPOWERLOOP_OFFLINE_SINGLE_PLAYER"));
            Assert.That((string[])Call("PlayerDefines", true, false), Does.Not.Contain("FIREPOWERLOOP_OFFLINE_SINGLE_PLAYER"));
            Assert.Throws<InvalidOperationException>(() => Call("PlayerDefines", false, true));
        }
        [Test] public void OfflineBuildScopeDisablesAnalyticsAndRestoresAllValues()
        {
            bool enabled = AnalyticsSettings.enabled, startup = AnalyticsSettings.initializeOnStartup, core = AnalyticsSettings.packageRequiringCoreStatsPresent;
            using ((IDisposable)Call("UseBuildScope", true))
            {
                Assert.That(AnalyticsSettings.enabled || AnalyticsSettings.initializeOnStartup || AnalyticsSettings.packageRequiringCoreStatsPresent, Is.False);
            }
            Assert.That(AnalyticsSettings.enabled, Is.EqualTo(enabled));
            Assert.That(AnalyticsSettings.initializeOnStartup, Is.EqualTo(startup));
            Assert.That(AnalyticsSettings.packageRequiringCoreStatsPresent, Is.EqualTo(core));
        }
        [Test] public void OfflineManifestExplicitlyRemovesNetworkAndInstallerPermissions()
        {
            string input = "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><uses-permission android:name='android.permission.INTERNET'/><uses-permission android:name='android.permission.REQUEST_INSTALL_PACKAGES'/><application/></manifest>";
            string result = (string)Call("PrepareOfflineManifest", input);
            var xml = new XmlDocument(); xml.LoadXml(result);
            var ns = new XmlNamespaceManager(xml.NameTable); ns.AddNamespace("android", "http://schemas.android.com/apk/res/android"); ns.AddNamespace("tools", "http://schemas.android.com/tools");
            foreach (string permission in new[] { "android.permission.INTERNET", "android.permission.REQUEST_INSTALL_PACKAGES" })
                Assert.That(xml.SelectSingleNode("/manifest/uses-permission[@android:name='" + permission + "']/@tools:node", ns)?.Value, Is.EqualTo("remove"));
            Assert.That(xml.SelectSingleNode("/manifest/application/meta-data[@android:name='com.firepowerloop.release-profile']/@android:value", ns)?.Value, Is.EqualTo("OfflineSinglePlayer"));
            Assert.That((string)Call("PrepareOfflineManifest", result), Is.EqualTo(result));
        }
        [Test] public void ActualGradleCallbackTouchesOnlyOfflineLauncherManifest()
        {
            string root = Path.Combine(Path.GetTempPath(), "FirepowerGradle-" + Guid.NewGuid().ToString("N"));
            string library = Path.Combine(root, "unityLibrary"), launcher = Path.Combine(root, "launcher/src/main/AndroidManifest.xml");
            Directory.CreateDirectory(library); Directory.CreateDirectory(Path.GetDirectoryName(launcher));
            const string original = "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><application/></manifest>";
            File.WriteAllText(launcher, original);
            try
            {
                var type = Type.GetType("Game.Editor.OfflineSinglePlayerGradleProcessor, Assembly-CSharp-Editor"); Assert.That(type, Is.Not.Null);
                var processor = Activator.CreateInstance(type); var run = type.GetMethod("OnPostGenerateGradleAndroidProject");
                using ((IDisposable)Call("UseBuildScope", false)) run.Invoke(processor, new object[] { library });
                Assert.That(File.ReadAllText(launcher), Is.EqualTo(original));
                using ((IDisposable)Call("UseBuildScope", true)) run.Invoke(processor, new object[] { library });
                Assert.That(File.ReadAllText(launcher), Does.Contain("OfflineSinglePlayer"));
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public void OfflineProfileIsReadOnlyFromItsOwnManifestMetadataBlock()
        {
            const string prefix = "E: manifest (line=2)\n  E: application (line=5)\n    E: meta-data (line=6)\n      A: android:name(0x01010003)=\"other.profile\" (Raw: \"other.profile\")\n      A: android:value(0x01010024)=\"unrelated\" (Raw: \"unrelated\")\n";
            const string marker = "    E: meta-data (line=7)\n      A: android:name(0x01010003)=\"com.firepowerloop.release-profile\" (Raw: \"com.firepowerloop.release-profile\")\n";
            const string value = "      A: android:value(0x01010024)=\"OfflineSinglePlayer\" (Raw: \"OfflineSinglePlayer\")\n";
            Assert.That((string)Call("ReadManifestProfile", prefix + marker + value), Is.EqualTo("OfflineSinglePlayer"));
            Assert.That((string)Call("ReadManifestProfile", prefix), Is.Empty);
            Assert.That((string)Call("ReadManifestProfile", prefix + marker + "    E: activity (line=8)\n" + value), Is.Empty);
        }
        [Test] public void OfflineFactsPassWithoutAnyPublicServerConfiguration()
        {
            var result = Evaluate(Facts());
            Assert.That((bool)result["passed"], Is.True);
            Assert.That((string)result["profile"], Is.EqualTo("OfflineSinglePlayerCandidate"));
            Assert.That((bool)result["deviceVerified"], Is.False);
        }
        [Test] public void OfflineFactsRejectMissingProfileAndNetworkOrInstallerPermission()
        {
            foreach (string permission in new[] { "android.permission.INTERNET", "android.permission.REQUEST_INSTALL_PACKAGES" })
            { var facts = Facts(); facts["permissions"] = new JArray(permission); Assert.That((bool)Evaluate(facts)["passed"], Is.False); }
            var missing = Facts(); missing.Remove("releaseProfile"); Assert.That((bool)Evaluate(missing)["passed"], Is.False);
        }
        [Test] public void OfflineFactsStillRequireReleaseIdentityAndSignature()
        {
            var facts = Facts(); facts["debuggable"] = true; facts["certificateSubject"] = "CN=Android Debug"; facts["signatureValid"] = false;
            Assert.That((bool)Evaluate(facts)["passed"], Is.False);
        }
        [Test] public void ExistingSignedFixtureCannotClaimOfflineProfile()
        {
            var type = Type.GetType("Game.Editor.AndroidChannelBuildUtility, Assembly-CSharp-Editor");
            var method = type.GetMethod("InspectOffline"); Assert.That(method, Is.Not.Null);
            var report = JObject.Parse((string)method.Invoke(null, new object[] { Path.Combine(Application.dataPath, "Tests/EditMode/Flow/AndroidSignedFixture.apk.bytes"), "cda3f92a5d2ede27605d93eae6345cd62f9dfe2460ed51a171b723a71614813b" }));
            Assert.That((bool)report["passed"], Is.False);
            Assert.That(report["checks"].Children<JObject>().Any(c => (string)c["id"] == "offlineProfile" && (string)c["status"] == "Failed"), Is.True);
        }
    }
}

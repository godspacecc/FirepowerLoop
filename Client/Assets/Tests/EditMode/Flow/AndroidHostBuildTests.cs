using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 锁定 Android Host 基础包、增量资源和跨平台状态隔离合同；不执行真实 Player 构建。
    /// </summary>
    [TestFixture]
    public sealed class AndroidHostBuildTests
    {
        private const string AndroidBuildPath = "Assets/Editor/Build/AndroidBuildUtility.cs";
        private const string YooAssetBuildPath = "Assets/Editor/YooAsset/YooAssetBuildUtility.cs";
        private const string BuildStatePath = "Assets/Editor/Build/BuildStateStore.cs";
        private const string PublishPath = "Assets/Editor/YooAsset/ResourceServerPublishUtility.cs";
        private const string BootstrapConfigPath = "Assets/Boot/Runtime/BootstrapConfig.cs";
        private const string ResourceBootPath = "Assets/Boot/Res/ResourceBoot.cs";

        [Test]
        public void HostBaseBuildUsesHostPlayerAndKeepsFullBuiltinFallback()
        {
            string source = File.ReadAllText(AndroidBuildPath);
            string body = Extract(source,
                "public static void BuildHostBaseCodeAndResourcesBatch()",
                "public static void BuildSmallHostBaseCodeAndResourcesBatch()");

            Assert.That(body, Does.Contain("BuildPackageOrThrow(version, true)"));
            Assert.That(body, Does.Contain("BuildFinalPlayerWithIncrementedPatchVersion(context, false)"));
            Assert.That(body, Does.Contain("SaveAndroidHostState(version, \"HostBase\""));
        }

        [Test]
        public void OfflineBuildStillForcesBuiltinModeDefine()
        {
            string source = File.ReadAllText(AndroidBuildPath);
            string body = Extract(source,
                "public static void BuildLatestCodeAndResourcesBatch()",
                "public static void BuildHostBaseCodeAndResourcesBatch()");

            Assert.That(body, Does.Contain("BuildFinalPlayerWithIncrementedPatchVersion(context, true)"));
            Assert.That(source, Does.Contain("BuiltinResourcesScriptingDefine"));
        }

        [Test]
        public void HostUpdateReusesAndroidAotAndDoesNotBuildPlayer()
        {
            string source = File.ReadAllText(AndroidBuildPath);
            string body = Extract(source,
                "public static void BuildHostCodeAndResourcesBatch()",
                "internal static void ValidateAndroidEnvironment()");

            Assert.That(body, Does.Contain("CompileHotUpdateDll(BuildTarget.Android)"));
            Assert.That(body, Does.Contain("CopyGeneratedDllsFromBaseline"));
            Assert.That(body, Does.Contain("state.LastAndroidAotBaseline"));
            Assert.That(body, Does.Contain("BuildPackageOrThrow(version, false)"));
            Assert.That(body, Does.Contain("PublishOrUpdateLatestAndroidPackage"));
            Assert.That(body, Does.Not.Contain("BuildPlayer("));
            // 按菜单约定自动启动本次发布包，成功时不弹出启动选择。
            Assert.That(body, Does.Contain("StartPublishedResourceServer(publish.BundleDirectory, version, timing)"));
            Assert.That(body, Does.Not.Contain("OfferStartServer("));
            Assert.That(body, Does.Not.Contain("LocalResourceServerUtility.StartAndProbe("));
        }

        [Test]
        public void SmallHostBaseBuildCreatesManifestOnlyApkAndPublishesMatchingResources()
        {
            string source = File.ReadAllText(AndroidBuildPath);
            string body = Extract(source,
                "public static void BuildSmallHostBaseCodeAndResourcesBatch()",
                "public static void BuildHostCodeAndResourcesBatch()");

            Assert.That(source, Does.Contain("Tools/打包/Android/首包完整打包"));
            Assert.That(source, Does.Contain("BuildAndroidApkFileName(appVersion)"));
            Assert.That(body, Does.Contain("BuildSmallHostPackageOrThrow(version)"));
            Assert.That(body, Does.Contain("ValidateManifestOnlyBuiltin(version)"));
            Assert.That(body, Does.Contain("BuildFinalPlayerWithIncrementedPatchVersion(context, false)"));
            Assert.That(body, Does.Contain("PublishOrUpdateLatestAndroidPackage"));
            Assert.That(body, Does.Contain("\"SmallHostBase\""));
            Assert.That(body, Does.Not.Contain("BuildFinalPlayerWithIncrementedPatchVersion(context, true)"));
        }

        [Test]
        public void FinalAndroidPlayerBuildIncrementsOnlySemanticPatchVersion()
        {
            Type type = Type.GetType("Game.Editor.AndroidBuildUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod(
                "IncrementAndroidPatchVersion",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            Assert.That(method.Invoke(null, new object[] { "0.1.0" }), Is.EqualTo("0.1.1"));
            Assert.That(method.Invoke(null, new object[] { "2.7.19" }), Is.EqualTo("2.7.20"));

            var invalid = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, new object[] { "0.1" }));
            Assert.That(invalid.InnerException, Is.TypeOf<InvalidOperationException>());

            string source = File.ReadAllText(AndroidBuildPath);
            string hostUpdate = Extract(source,
                "public static void BuildHostCodeAndResourcesBatch()",
                "private static void EnsureAndroidBuildTarget()");
            Assert.That(hostUpdate, Does.Not.Contain("BuildFinalPlayerWithIncrementedPatchVersion"));
        }

        [Test]
        public void FinalAndroidApkNameUsesEnglishGameNameAndAppVersion()
        {
            Type type = Type.GetType("Game.Editor.AndroidBuildUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod("BuildAndroidApkFileName",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { "0.1.3" }), Is.EqualTo("AnotherDay-0.1.3.apk"));

            string source = File.ReadAllText(AndroidBuildPath);
            Assert.That(source, Does.Not.Contain("AnotherDay-small-host-test.apk"));
            Assert.That(source, Does.Not.Contain("AnotherDay-host-test.apk"));
            Assert.That(source, Does.Not.Contain("AnotherDay-test.apk"));
        }

        [Test]
        public void SmallHostPackageClearsOldBundlesAndKeepsOnlyManifestCatalog()
        {
            string source = File.ReadAllText(YooAssetBuildPath);
            string buildBody = Extract(source,
                "public static BuildResult BuildManifestOnlyBuiltinPackage(string version)",
                "private static BuildResult BuildPackage(");
            string validateBody = Extract(source,
                "public static bool ValidateManifestOnlyBuiltinPackage(string version)",
                "private sealed class ManifestOnlyCatalog");

            Assert.That(buildBody, Does.Contain("EBuildinFileCopyOption.ClearAndCopyByTags"));
            Assert.That(buildBody, Does.Contain("ManifestOnlyBuiltinTag"));
            Assert.That(validateBody, Does.Contain("BuildinCatalog.bytes"));
            Assert.That(validateBody, Does.Contain("BuildinCatalog.json"));
            Assert.That(validateBody, Does.Contain("不应包含 Bundle"));
            Assert.That(validateBody, Does.Contain("catalog.Wrappers.Count != 0"));
        }

        [Test]
        public void AndroidBuildStateIsSeparatedFromWindowsRecords()
        {
            string source = File.ReadAllText(BuildStatePath);

            Assert.That(source, Does.Contain("LastAndroidHost"));
            Assert.That(source, Does.Contain("LastAndroidAotBaseline"));
            Assert.That(source, Does.Contain("LastAndroidPackage"));
            Assert.That(source, Does.Contain("LastAndroidPlayer"));
            Assert.That(source, Does.Contain("TryValidateAndroidAotBaseline"));
        }

        [Test]
        public void AndroidHostUrlRejectsLoopbackAndAcceptsReachableHttps()
        {
            Type type = Type.GetType("Game.Editor.AndroidBuildUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod(
                "ValidateAndroidHostServerUrl",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            var exception = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, new object[] { "http://127.0.0.1:8000" }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException.Message, Does.Contain("回环"));
            Assert.DoesNotThrow(() => method.Invoke(null, new object[] { "https://updates.example.com/game" }));
        }

        [Test]
        public void AndroidResourceServerMenuStartsLatestPublishedResourceFiles()
        {
            string source = File.ReadAllText(AndroidBuildPath);

            Assert.That(source, Does.Contain("Tools/服务器/Android/启动资源服"));
            Assert.That(source, Does.Contain("StartAndroidResourceServerFromLatestPublishedBundle"));
            Assert.That(source, Does.Contain("ResourceServerPublishUtility.TryFindLatestBundle"));
            Assert.That(source, Does.Contain("Path.Combine(bundleDirectory, \"ResourceFiles\")"));
            Assert.That(source, Does.Contain("LocalResourceServerUtility.StartAndProbe(resourceRoot, version, true)"));
        }

        [Test]
        public void AndroidGameServerEndpointAcceptsEditableBootstrapAndRejectsInvalidSyntax()
        {
            Type type = Type.GetType("Game.Editor.AndroidBuildUtility, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            MethodInfo method = type.GetMethod(
                "ValidateAndroidGameServerEndpoint",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);

            Assert.DoesNotThrow(() => method.Invoke(null, new object[] { "127.0.0.1:31001" }));
            Assert.DoesNotThrow(() => method.Invoke(null, new object[] { "192.168.1.20:31001" }));
            var exception = Assert.Throws<TargetInvocationException>(() =>
                method.Invoke(null, new object[] { "missing-port" }));
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AndroidPlayerBuildsValidateGameServerAndCopyPlayerGuide()
        {
            string source = File.ReadAllText(AndroidBuildPath);
            string offlineBuild = Extract(source,
                "public static void BuildLatestCodeAndResourcesBatch()",
                "public static void BuildHostBaseCodeAndResourcesBatch()");
            string hostBuild = Extract(source,
                "public static void BuildHostBaseCodeAndResourcesBatch()",
                "public static void BuildSmallHostBaseCodeAndResourcesBatch()");

            foreach (string body in new[] { offlineBuild, hostBuild })
            {
                Assert.That(body, Does.Contain("ValidateAndroidNetworkConfig"));
                Assert.That(body, Does.Contain("CopyPlayerGuide"));
            }
        }

        [Test]
        public void AndroidWorkflowsAutomaticallySwitchTargetBeforeBuilding()
        {
            string source = File.ReadAllText(AndroidBuildPath);
            string offlineBuild = Extract(source,
                "public static void BuildLatestCodeAndResourcesBatch()",
                "public static void BuildHostBaseCodeAndResourcesBatch()");
            string hostBaseBuild = Extract(source,
                "public static void BuildHostBaseCodeAndResourcesBatch()",
                "public static void BuildSmallHostBaseCodeAndResourcesBatch()");
            string smallHostBaseBuild = Extract(source,
                "public static void BuildSmallHostBaseCodeAndResourcesBatch()",
                "public static void BuildHostCodeAndResourcesBatch()");
            string hostUpdate = Extract(source,
                "public static void BuildHostCodeAndResourcesBatch()",
                "private static void EnsureAndroidBuildTarget()");
            string ensureTarget = Extract(source,
                "private static void EnsureAndroidBuildTarget()",
                "internal static void ValidateAndroidEnvironment()");

            foreach (string body in new[] { offlineBuild, hostBaseBuild, smallHostBaseBuild, hostUpdate })
                Assert.That(body, Does.Contain("timing.Run(\"EnsureAndroidBuildTarget\", EnsureAndroidBuildTarget)"));
            Assert.That(ensureTarget, Does.Contain("EditorUserBuildSettings.SwitchActiveBuildTarget"));
            Assert.That(ensureTarget, Does.Contain("BuildTarget.Android"));
            Assert.That(ensureTarget, Does.Contain("BuildPipeline.IsBuildTargetSupported"));
        }

        [Test]
        public void AndroidPublisherUsesPlatformSpecificBundleAndReleaseRoots()
        {
            string source = File.ReadAllText(PublishPath);
            string publish = Extract(source,
                "public static ResourceServerPublishResult PublishLatestValidAndroidPackage()",
                "public static ResourceServerPublishResult UpdateLatestAndroidBundle()");
            string update = Extract(source,
                "public static ResourceServerPublishResult UpdateLatestAndroidBundle()",
                "internal static ResourceServerPublishResult Publish(");

            Assert.That(publish, Does.Contain("\"Bundles\", \"Android\", PackageName"));
            Assert.That(publish, Does.Contain("\"Release\", \"ResourceServer\", \"Android\""));
            Assert.That(update, Does.Contain("\"Bundles\", \"Android\", PackageName"));
            Assert.That(update, Does.Contain("\"Release\", \"ResourceServer\", \"Android\""));
        }

        [Test]
        public void AndroidBootstrapReadsStreamingAssetsAsynchronously()
        {
            string config = File.ReadAllText(BootstrapConfigPath);
            string boot = File.ReadAllText(ResourceBootPath);

            Assert.That(config, Does.Contain("#if UNITY_ANDROID && !UNITY_EDITOR"));
            Assert.That(config, Does.Contain("UnityWebRequest.Get(path)"));
            Assert.That(config, Does.Contain("await request.SendWebRequest()"));
            Assert.That(boot, Does.Contain("await BootstrapConfig.LoadAsync()"));
        }

        [Test]
        public void BootDownloadsOnlyMandatoryArcadeTagsAndUsesSameScopeForCacheValidation()
        {
            string boot = File.ReadAllText(ResourceBootPath);

            Assert.That(boot, Does.Contain("\"startup\""));
            Assert.That(boot, Does.Contain("\"arcade\""));
            Assert.That(boot, Does.Contain("\"arcade-online\""));
            Assert.That(boot, Does.Contain("CreateResourceDownloader(StartupResourceTags, MAX_DOWNLOAD_COUNT, 3)"));
            Assert.That(CountOccurrences(boot, "CreateStartupResourceDownloader()"), Is.EqualTo(3));
            Assert.That(boot, Does.Not.Contain("_package.CreateResourceDownloader(MAX_DOWNLOAD_COUNT, 3)"));
        }

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        private static string Extract(string source, string startMarker, string endMarker)
        {
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Missing marker: {startMarker}");
            int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
            Assert.That(end, Is.GreaterThan(start), $"Missing marker: {endMarker}");
            return source.Substring(start, end - start);
        }
    }
}

using System.IO;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ResourceUpdateBoundaryTests
    {
        private const string CheckerPath = "Assets/Game/Scripts/Utility/ResourceUpdate/YooAssetResourceUpdateRuntimeChecker.cs";
        private const string ProbePath = "Assets/Game/Scripts/Utility/ResourceUpdate/UnityWebRequestResourceVersionProbe.cs";
        private const string SystemPath = "Assets/Game/Scripts/System/ResourceUpdate/ResourceUpdateSystem.cs";
        private const string StartPath = "Assets/Game/Scripts/Controller/UICtrl/Home/StartUICtrl.cs";
        private const string ResourceBootPath = "Assets/Boot/Res/ResourceBoot.cs";

        [Test]
        public void YooAssetCheckerOnlyPrecomputesDownloadInfo()
        {
            var source = File.ReadAllText(CheckerPath);

            Assert.That(source, Does.Contain("GetPackageVersion()"));
            Assert.That(source, Does.Contain("RequestPackageVersionAsync(true, timeoutSeconds)"));
            Assert.That(source, Does.Contain("string.Equals(currentVersion, targetVersion, StringComparison.Ordinal)"));
            Assert.That(source, Does.Contain("PreDownloadContentAsync(targetVersion, timeoutSeconds)"));
            Assert.That(
                source.IndexOf("string.Equals(currentVersion, targetVersion, StringComparison.Ordinal)", System.StringComparison.Ordinal),
                Is.LessThan(source.IndexOf("PreDownloadContentAsync(targetVersion, timeoutSeconds)", System.StringComparison.Ordinal)));
            Assert.That(source, Does.Contain("MandatoryResourceTags,"));
            Assert.That(source, Does.Contain("\"startup\""));
            Assert.That(source, Does.Contain("\"arcade\""));
            Assert.That(source, Does.Contain("\"arcade-online\""));
            Assert.That(source, Does.Not.Contain("CreateResourceDownloader(DownloadingMaxNumber, FailedTryAgain)"));
            Assert.That(source, Does.Not.Contain("UpdatePackageManifestAsync"));
            Assert.That(source, Does.Not.Contain("BeginDownload"));
            Assert.That(source, Does.Not.Contain("ClearCacheFilesAsync"));
            Assert.That(source, Does.Not.Contain("Boot"));
        }

        [Test]
        public void SystemAndStartDoNotImplementSkipVersionPolicy()
        {
            var source = File.ReadAllText(SystemPath) + File.ReadAllText(StartPath);

            Assert.That(source, Does.Not.Contain("SkippedPackageVersion"));
            Assert.That(source, Does.Not.Contain("SkipCurrentVersion"));
            Assert.That(source, Does.Not.Contain("skip version"));
        }

        [Test]
        public void BuiltinVersionProbeOnlyRequestsVersionFile()
        {
            var source = File.ReadAllText(ProbePath);

            Assert.That(source, Does.Contain("DefaultPackage.version"));
            Assert.That(source, Does.Not.Contain("YooAssets"));
            Assert.That(source, Does.Not.Contain("PreDownloadContentAsync"));
            Assert.That(source, Does.Not.Contain("UpdatePackageManifestAsync"));
            Assert.That(source, Does.Not.Contain("CreateResourceDownloader"));
            Assert.That(source, Does.Not.Contain("BeginDownload"));
        }

        [Test]
        public void StartControllerKeepsExistingCheckButtonOnly()
        {
            var source = File.ReadAllText(StartPath);

            Assert.That(source, Does.Contain("BtnCheckUpdate"));
            Assert.That(source, Does.Not.Contain("BtnServer"));
            Assert.That(source, Does.Not.Contain("BtnBuiltin"));
            Assert.That(source, Does.Not.Contain("BtnRestartUpdate"));
        }

        [Test]
        public void ResourceBootStillDoesNotReferenceGame()
        {
            var source = File.ReadAllText(ResourceBootPath);

            Assert.That(source, Does.Not.Contain("using Game"));
            Assert.That(source, Does.Not.Contain("Game.Interface"));
            Assert.That(source, Does.Not.Contain("namespace Game"));
        }
    }
}

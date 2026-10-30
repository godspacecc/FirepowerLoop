using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>完整PC入口的构建安全合同；只读取源码，绝不执行Player或外部发布。</summary>
    public sealed class PcFullBuildFlowTests
    {
        private static string Source(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Build", file));
        private static string FullEntry()
        {
            string source = Source("BuildUtility.cs");
            int start = source.IndexOf("public static void BuildLatestCodeAndResourcesBatch()");
            int end = source.IndexOf("public static void BuildSmallHostBaseCodeAndResourcesBatch()", start);
            return source.Substring(start, end - start);
        }

        [Test]
        public void FullEntryUsesStripAotBuildWithoutExtraPreparatoryPlayer()
        {
            string entry = FullEntry();
            Assert.That(entry, Does.Not.Contain("BuildPreparatoryPlayer"));
            Assert.That(entry, Does.Contain("ConfigureStandalonePlayerBuild"));
            Assert.That(entry, Does.Not.Contain("CleanupPreparatoryPlayer"));
            Assert.That(entry, Does.Contain("cleanupPrepPlayer: false"));
        }

        [Test]
        public void FullSuccessIsReportedOnlyAfterPublishingWhilePlayerRecordIsRetained()
        {
            string entry = FullEntry();
            int publish = entry.IndexOf("string serverResources =");
            Assert.That(entry.IndexOf("SaveSuccessfulState("), Is.LessThan(publish));
            Assert.That(entry.IndexOf("Player 构建成功"), Is.InRange(0, publish));
            Assert.That(entry.IndexOf("完整打包完成"), Is.GreaterThan(publish));
        }

        [Test]
        public void InstallerFailureDiagnosticsIdentifyRetainedArtifactsAndRecovery()
        {
            string source = Source("WindowsInstallerPublishUtility.cs");
            Assert.That(source, Does.Contain("安装器生成成功"));
            Assert.That(source, Does.Contain("安装器发布成功"));
            Assert.That(source, Does.Contain("可使用 GenerateOnly"));
            Assert.That(source, Does.Contain("可使用 PublishGenerated"));
            Assert.That(source, Does.Not.Contain("安装器或发布步骤失败"));
        }
    }
}

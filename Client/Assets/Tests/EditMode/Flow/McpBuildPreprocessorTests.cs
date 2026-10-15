using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>仅执行 MCP 的 link.xml 预处理，不调用 BuildPipeline 或生成 Player。</summary>
    public class McpBuildPreprocessorTests
    {
        [Test]
        public void EmbeddedPreprocessorMergesRegisteredPackageLinksWithoutPlayerBuild()
        {
            const string typeName = "com.IvanMurzak.Unity.MCP.Editor.Utils.LinkExtractor";
            Type type = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null) break;
            }
            Assert.That(type, Is.Not.Null, "MCP 构建预处理类型必须已加载。");
            var package = PackageInfo.FindForAssembly(type.Assembly);
            Assert.That(package, Is.Not.Null);
            Assert.That(package.source, Is.EqualTo(PackageSource.Embedded), "必须加载项目内持久化修复的嵌入包。");

            string folder = Path.Combine(Application.dataPath, "packages-merged-link");
            Assert.That(Directory.Exists(folder), Is.False, "拒绝覆盖已有合并目录，避免破坏其他操作的现场。");
            object extractor = Activator.CreateInstance(type);
            var watch = Stopwatch.StartNew();
            try
            {
                type.GetMethod("OnPreprocessBuild").Invoke(extractor, new object[] { null });
                watch.Stop();
                Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(10), "读取已注册包及合并 link.xml 不应长时间阻塞。");
                var document = new XmlDocument();
                document.Load(Path.Combine(folder, "link.xml"));
                Assert.That(document.DocumentElement.Name, Is.EqualTo("linker"));
                Assert.That(document.SelectNodes("/linker/assembly").Count, Is.GreaterThan(0), "合并结果必须保留实际包的程序集规则。");
            }
            finally
            {
                type.GetMethod("OnPostprocessBuild").Invoke(extractor, new object[] { null });
            }
            Assert.That(Directory.Exists(folder), Is.False);
            Assert.That(File.Exists(folder + ".meta"), Is.False);
        }
    }
}

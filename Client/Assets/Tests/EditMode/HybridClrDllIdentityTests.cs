using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>使用临时文件验证 DLL 发布不破坏资源身份和旧产物。</summary>
    public class HybridClrDllIdentityTests
    {
        // 每次测试独享临时目录，避免触碰工程资源。
        private string root;
        [SetUp] public void SetUp() { root = Path.Combine(Path.GetTempPath(), "HybridClrIdentity_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); }
        [TearDown] public void TearDown() { if (Directory.Exists(root)) Directory.Delete(root, true); }

        /// <summary>覆盖同名内容时保留 GUID，仅移除退役 DLL。</summary>
        [Test] public void Publish_PreservesMetadataAndUnrelatedFiles()
        {
            string hot = CreateDirectory("hot"), aot = CreateDirectory("aot"), target = CreateDirectory("target");
            File.WriteAllText(Path.Combine(hot, "Game.dll"), "new game");
            File.WriteAllText(Path.Combine(aot, "Core.dll"), "new core");
            foreach (string name in new[] { "Game.dll.bytes", "Core.dll.bytes", "HybridCLRAssemblyList.bytes", "notes.bytes", "Old.dll.bytes" })
            { File.WriteAllText(Path.Combine(target, name), "old"); File.WriteAllText(Path.Combine(target, name + ".meta"), "guid:" + name); }
            Publish(hot, aot, new[] { "Core.dll" }, target);
            Assert.AreEqual("new game", File.ReadAllText(Path.Combine(target, "Game.dll.bytes")));
            Assert.AreEqual("new core", File.ReadAllText(Path.Combine(target, "Core.dll.bytes")));
            CollectionAssert.AreEqual(new[] { "Core.dll" }, File.ReadAllLines(Path.Combine(target, "HybridCLRAssemblyList.bytes")));
            foreach (string name in new[] { "Game.dll.bytes", "Core.dll.bytes", "HybridCLRAssemblyList.bytes", "notes.bytes" })
                Assert.AreEqual("guid:" + name, File.ReadAllText(Path.Combine(target, name + ".meta")));
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(target, "notes.bytes")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "Old.dll.bytes")));
            Assert.IsFalse(File.Exists(Path.Combine(target, "Old.dll.bytes.meta")));
        }

        /// <summary>任意源缺失时，不修改已有发布内容。</summary>
        [TestCase(true)] [TestCase(false)]
        public void Publish_MissingSourceLeavesExistingFilesUntouched(bool missingGame)
        {
            string hot = CreateDirectory("hot"), aot = CreateDirectory("aot"), target = CreateDirectory("target");
            if (!missingGame) File.WriteAllText(Path.Combine(hot, "Game.dll"), "new");
            if (missingGame) File.WriteAllText(Path.Combine(aot, "Core.dll"), "new");
            File.WriteAllText(Path.Combine(target, "Game.dll.bytes"), "old game");
            File.WriteAllText(Path.Combine(target, "Old.dll.bytes"), "old aot");
            var error = Assert.Throws<TargetInvocationException>(() => Publish(hot, aot, new[] { "Core.dll" }, target));
            Assert.IsInstanceOf<FileNotFoundException>(error.InnerException);
            Assert.AreEqual("old game", File.ReadAllText(Path.Combine(target, "Game.dll.bytes")));
            Assert.AreEqual("old aot", File.ReadAllText(Path.Combine(target, "Old.dll.bytes")));
            Assert.AreEqual(2, Directory.GetFiles(target).Length);
        }

        /// <summary>建立独立的输入和输出目录。</summary>
        private string CreateDirectory(string name) { string path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
        /// <summary>反射访问编辑器发布入口，不触发编译或 Player 构建。</summary>
        private static void Publish(string hot, string aot, string[] names, string target)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Game.Editor.HybridCLRBuildUtility")).FirstOrDefault(t => t != null);
            Assert.IsNotNull(type);
            var method = type.GetMethod("PublishDllFiles", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "缺少保留资源身份的文件发布入口");
            method.Invoke(null, new object[] { hot, aot, names, target });
        }
    }
}

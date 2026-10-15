using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using Boot;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>隔离临时目录验证 PC 发布序号事务；不写项目台账，不构建 Player。</summary>
    public sealed class WindowsReleaseIdentityTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void MappedIdentityFilesCanCommitOrRollbackWithoutTruncatingReaders(bool commit)
        {
            WithFiles((type, ledger, embedded) =>
            {
                byte[] beforeLedger = File.ReadAllBytes(ledger);
                byte[] beforeEmbedded = System.Text.Encoding.UTF8.GetBytes("previous embedded identity");
                File.WriteAllBytes(embedded, beforeEmbedded);
                // 模拟 Unity 导入器仍持有旧资源的映射；原地截断会触发 Windows 错误 1224。
                using (var ledgerFile = new FileStream(ledger, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var embeddedFile = new FileStream(embedded, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var ledgerMap = MemoryMappedFile.CreateFromFile(ledgerFile, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, true))
                using (var embeddedMap = MemoryMappedFile.CreateFromFile(embeddedFile, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, true))
                using (var ledgerView = ledgerMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                using (var embeddedView = embeddedMap.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
                {
                    using (var transaction = (IDisposable)Activator.CreateInstance(type, ledger, embedded, new Action<string>(_ => { })))
                    {
                        Assert.That(JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(embedded)).releaseNumber, Is.EqualTo(8));
                        if (commit) type.GetMethod("Commit").Invoke(transaction, null);
                    }
                    Assert.That(File.ReadAllBytes(ledger), commit ? Is.Not.EqualTo(beforeLedger) : Is.EqualTo(beforeLedger));
                    Assert.That(File.ReadAllBytes(embedded), commit ? Is.Not.EqualTo(beforeEmbedded) : Is.EqualTo(beforeEmbedded));
                    byte[] mapped = new byte[beforeEmbedded.Length];
                    embeddedView.ReadArray(0, mapped, 0, mapped.Length);
                    Assert.That(mapped, Is.EqualTo(beforeEmbedded), "旧映射读取者不能看到半写入的新身份。");
                    Assert.That(Directory.GetFiles(Path.GetDirectoryName(ledger)).Length, Is.EqualTo(2), "临时文件必须清理。");
                }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedBuildRestoresPreviousIdentity(bool existing)
        {
            WithFiles((type, ledger, embedded) =>
            {
                if (existing) File.WriteAllText(embedded, "previous identity");
                string before = File.ReadAllText(ledger);
                using ((IDisposable)Activator.CreateInstance(type, ledger, embedded, new Action<string>(_ => { })))
                {
                    var next = JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(embedded));
                    Assert.That(next.releaseNumber, Is.EqualTo(8));
                    Assert.That(File.ReadAllText(ledger), Is.EqualTo(before));
                }
                Assert.That(File.ReadAllText(ledger), Is.EqualTo(before));
                Assert.That(File.Exists(embedded), Is.EqualTo(existing));
                if (existing) Assert.That(File.ReadAllText(embedded), Is.EqualTo("previous identity"));
            });
        }

        [Test]
        public void SuccessfulPlayerCommitsExactlyOneRelease()
        {
            WithFiles((type, ledger, embedded) =>
            {
                using (var transaction = (IDisposable)Activator.CreateInstance(type, ledger, embedded, new Action<string>(_ => { })))
                {
                    type.GetMethod("Commit").Invoke(transaction, null);
                    type.GetMethod("Commit").Invoke(transaction, null);
                }
                Assert.That(JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(ledger)).releaseNumber, Is.EqualTo(8));
                Assert.That(File.ReadAllText(ledger), Is.EqualTo(File.ReadAllText(embedded)));
            });
        }

        [Test]
        public void OutputMustCarryTheSameReleaseBeforeCommit()
        {
            WithFiles((type, ledger, embedded) =>
            {
                using (var transaction = (IDisposable)Activator.CreateInstance(type, ledger, embedded, new Action<string>(_ => { })))
                {
                    string player = Path.Combine(Path.GetDirectoryName(ledger), "Player");
                    string identityPath = Path.Combine(player, "AnotherDay_Data", "StreamingAssets", "windows-release.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(identityPath));
                    File.Copy(embedded, identityPath);
                    foreach (string file in new[] { "AnotherDay.exe", "UnityPlayer.dll", "GameAssembly.dll" })
                        File.WriteAllText(Path.Combine(player, file), "fixture");
                    var validate = type.GetMethod("ValidatePlayer");
                    Assert.DoesNotThrow(() => validate.Invoke(transaction, new object[] { player }));
                    var other = JsonUtility.FromJson<WindowsUpdateManifest>(File.ReadAllText(identityPath));
                    other.releaseNumber++;
                    File.WriteAllText(identityPath, JsonUtility.ToJson(other));
                    Assert.Throws<TargetInvocationException>(() => validate.Invoke(transaction, new object[] { player }));
                    File.Copy(embedded, identityPath, true);
                    File.Delete(Path.Combine(player, "GameAssembly.dll"));
                    Assert.Throws<TargetInvocationException>(() => validate.Invoke(transaction, new object[] { player }));
                }
            });
        }

        private static void WithFiles(Action<Type, string, string> test)
        {
            var type = Type.GetType("Game.Editor.WindowsReleaseIdentity, Assembly-CSharp-Editor");
            Assert.That(type, Is.Not.Null);
            string root = Path.Combine(Path.GetTempPath(), "AnotherDayIdentityTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string ledger = Path.Combine(root, "ledger.json");
                File.WriteAllText(ledger, "{\"schemaVersion\":1,\"productId\":\"com.anotherday.game\",\"platform\":\"Windows\",\"architecture\":\"x64\",\"versionName\":\"0.1.0\",\"releaseNumber\":7}");
                test(type, ledger, Path.Combine(root, "embedded.json"));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}

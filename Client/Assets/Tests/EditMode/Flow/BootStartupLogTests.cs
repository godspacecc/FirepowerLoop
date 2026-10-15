using System;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Flow
{
    /// <summary>验证 Boot 诊断的会话覆盖、容量及真实日志订阅；不执行资源下载或 Player 构建。</summary>
    public sealed class BootStartupLogTests
    {
        private string root;
        private IDisposable store;
        private Type storeType;
        private string LogPath => Path.Combine(root, "Logs", "boot-startup.log");

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AnotherDayBootLog-" + Guid.NewGuid().ToString("N"));
            storeType = Type.GetType("Boot.BootStartupLogStore, Boot");
            Assert.That(storeType, Is.Not.Null, "缺少 Boot 阶段日志采集器。");
        }

        [TearDown]
        public void TearDown()
        {
            store?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private void Begin(int maxBytes = 262144)
        {
            store = (IDisposable)Activator.CreateInstance(storeType, root, "0.1.18", "Android", maxBytes);
        }

        private void Record(string stage)
        {
            storeType.GetMethod("RecordStage").Invoke(store, new object[] { stage });
        }

        [Test]
        public void NewSessionOverwritesOldRunAndKeepsVersionAndStages()
        {
            Begin();
            Record("old-run");
            store.Dispose();
            Begin();
            Record("MainScene:Started; ResourceVersion=20261009133000");
            string text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Not.Contain("old-run"));
            Assert.That(text, Does.Contain("0.1.18"));
            Assert.That(text, Does.Contain("Android"));
            Assert.That(text, Does.Contain("MainScene:Started"));
            Assert.That(text, Does.Contain("20261009133000"));
        }

        [Test]
        public void UnityErrorIncludesStackAndDisposeStopsCapturing()
        {
            Begin();
            LogAssert.Expect(LogType.Error, "boot-log-first-error");
            Debug.LogError("boot-log-first-error");
            string text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Contain("boot-log-first-error"));
            Assert.That(text, Does.Contain(nameof(UnityErrorIncludesStackAndDisposeStopsCapturing)));
            LogAssert.Expect(LogType.Warning, "boot-log-missing-script-warning");
            Debug.LogWarning("boot-log-missing-script-warning");
            text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Contain("boot-log-missing-script-warning"));
            store.Dispose();
            LogAssert.Expect(LogType.Error, "boot-log-after-dispose");
            Debug.LogError("boot-log-after-dispose");
            Assert.That(File.ReadAllText(LogPath), Is.EqualTo(text));
        }

        [Test]
        public void ErrorFloodStaysBoundedAndLeavesRoomForNextStage()
        {
            Begin(4096);
            string error = new string('错', 6000);
            LogAssert.Expect(LogType.Error, error);
            Debug.LogError(error);
            Record("MainScene:Completed");
            Assert.That(new FileInfo(LogPath).Length, Is.LessThanOrEqualTo(4096));
            string text = File.ReadAllText(LogPath);
            Assert.That(text, Does.Contain("[truncated]"));
            Assert.That(text, Does.Contain("MainScene:Completed"));
            Assert.DoesNotThrow(() => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(LogPath)),
                "截断后的文件仍须是合法 UTF-8。");
        }

        [Test]
        public void UnwritableDirectoryDoesNotThrowOrBlockStartup()
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Logs"), "blocking-file");
            Assert.DoesNotThrow(() => Begin());
            Assert.DoesNotThrow(() => Record("MainScene:Started"));
            Assert.DoesNotThrow(() => store.Dispose());
        }
    }
}

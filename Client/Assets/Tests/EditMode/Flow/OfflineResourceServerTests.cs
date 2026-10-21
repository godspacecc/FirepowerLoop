using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Boot;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.Flow
{
    /// <summary>验证内置模式向更新检查提供真实配置地址；测试后原样恢复配置和玩家覆盖文件。</summary>
    public sealed class OfflineResourceServerTests
    {
        private string configPath;
        private string overridePath;
        private byte[] configBackup;
        private byte[] overrideBackup;
        private GameObject root;
        private ResourceBoot boot;

        [SetUp]
        public void SetUp()
        {
            configPath = Path.Combine(Application.streamingAssetsPath, BootstrapConfig.FileName);
            overridePath = Path.Combine(Application.persistentDataPath, "Network", ResourceServerPreference.OverrideFileName);
            configBackup = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
            overrideBackup = File.Exists(overridePath) ? File.ReadAllBytes(overridePath) : null;
            File.WriteAllText(configPath, "{\"resourceServer\":\"https://resources.example.test/game\"}");
            if (File.Exists(overridePath)) File.Delete(overridePath);
            root = new GameObject(nameof(OfflineResourceServerTests));
            boot = root.AddComponent<ResourceBoot>();
            Set("_actualPlayMode", ResourcePlayMode.OfflinePlayMode);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
            Restore(configPath, configBackup);
            Restore(overridePath, overrideBackup);
        }

        [Test]
        public void BuiltinUpdateChecksUsePackagedResourceServer()
        {
            Assert.That(Load(), Is.Null);
            Assert.That(Server(), Is.EqualTo("https://resources.example.test/game"));
            Assert.That(boot.PlayMode, Is.EqualTo(ResourcePlayMode.OfflinePlayMode));
        }

        [Test]
        public void BuiltinUpdateChecksPreferSavedResourceServer()
        {
            Assert.That(ResourceServerPreference.TrySave("http://192.168.31.126:8001/", out _, out _), Is.True);
            Assert.That(Load(), Is.Null);
            Assert.That(Server(), Is.EqualTo("http://192.168.31.126:8001"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnreadableConfigDoesNotBlockOfflineResourcesOrReuseOldAddress(bool missing)
        {
            Set("_resourceServer", "http://old.example.test");
            if (missing) File.Delete(configPath);
            else File.WriteAllText(configPath, "{}");
            LogAssert.Expect(LogType.Warning, new Regex("\\[ResourceBoot\\].*"));

            Assert.That(Load(), Is.Null, "更新地址不可用不能阻断完整内置资源启动");
            Assert.That(Server(), Is.Empty, "失败后不能保留上轮地址或手机回环地址");
            Assert.That(boot.LastError, Is.Null);
            Assert.That(boot.PlayMode, Is.EqualTo(ResourcePlayMode.OfflinePlayMode));
        }

        [Test]
        public void HostStillRejectsUnreadableConfig()
        {
            Set("_actualPlayMode", ResourcePlayMode.HostPlayMode);
            File.WriteAllText(configPath, "{}");
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: resourceServer"));
            var result = Load();
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Failure.Code, Is.EqualTo(BootFailureCode.BootstrapConfigInvalid));
        }

        private BootOperationResult Load() => ((UniTask<BootOperationResult>)typeof(ResourceBoot)
            .GetMethod("LoadBootstrapConfigAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(boot, null)).GetAwaiter().GetResult();

        private string Server() => (string)typeof(ResourceBoot)
            .GetField("_resourceServer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(boot);

        private void Set(string name, object value) => typeof(ResourceBoot)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boot, value);

        private static void Restore(string path, byte[] bytes)
        {
            if (bytes != null) File.WriteAllBytes(path, bytes);
            else if (File.Exists(path)) File.Delete(path);
        }
    }
}

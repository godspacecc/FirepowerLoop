using System;
using System.Collections.Generic;
using ET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Reflection;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class NetworkDiagnosticsTests
    {
        private IResourceLoader previousLoader;

        [SetUp]
        public void SetUp()
        {
            previousLoader = ResourceLoader.Current;
            ResourceLoader.Use(new NoAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
#if UNITY_EDITOR
            NetworkDiagnostics.SetEnabled(true);
#else
            NetworkDiagnostics.SetEnabled(false);
#endif
            try { ResetArchitecture(); }
            finally
            {
                ResourceLoader.Use(previousLoader);
                LocalizationLanguages.ResetForTests();
            }
        }

        [Test]
        public void NetworkDiagnosticsDefaultsToEnabledInEditor()
        {
#if UNITY_EDITOR
            Assert.That(NetworkDiagnostics.Enabled, Is.True);
#else
            Assert.That(NetworkDiagnostics.Enabled, Is.False);
#endif
        }

        [Test]
        public void NetworkDiagnosticsSetEnabledLogsChineseState()
        {
            LogAssert.Expect(LogType.Log, "[Network] 诊断日志已开启=True");
            NetworkDiagnostics.SetEnabled(true);
        }

        [Test]
        public void NetworkDiagnosticsCommandTogglesRuntimeLogging()
        {
            ResetArchitecture();
            var system = TestNetworkDiagnosticsArchitecture.Interface.GetSystem<TestCmdSystem>();

            NetworkDiagnostics.SetEnabled(false);
            Assert.That(NetworkDiagnostics.Enabled, Is.False);

            TestCmdExecutionResult enabled = system.ExecuteAsync("#netlog on", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(enabled.Recognized, Is.True);
            Assert.That(enabled.Success, Is.True);
            Assert.That(enabled.Code, Is.EqualTo(TestCmdResultCode.NetworkDiagnosticsChanged));
            Assert.That(enabled.Message, Is.EqualTo("网络日志已开启"));
            Assert.That(NetworkDiagnostics.Enabled, Is.True);

            TestCmdExecutionResult disabled = system.ExecuteAsync("#网络日志 关", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(disabled.Recognized, Is.True);
            Assert.That(disabled.Success, Is.True);
            Assert.That(disabled.Code, Is.EqualTo(TestCmdResultCode.NetworkDiagnosticsChanged));
            Assert.That(disabled.Message, Is.EqualTo("网络日志已关闭"));
            Assert.That(NetworkDiagnostics.Enabled, Is.False);
        }

        /// <summary>所有密码均为测试专用哨兵，不使用真实账号或签名凭据。</summary>
        private const string SyntheticPassword = "AUDIT_ONLY_NOT_A_REAL_LOGIN_PASSWORD";

        [Test]
        public void LoginLogRedactsPasswordWithoutChangingWirePayload()
        {
            NetworkDiagnostics.SetEnabled(true);
            var request = C2G_Login.Create();
            request.RpcId = 17;
            request.UserName = "audit_user";
            request.Password = SyntheticPassword;
            var codec = new MemoryPackMessageCodec();
            byte[] before = codec.Encode(request);

            JObject logged = CaptureJson(() => NetworkDiagnostics.LogSend(request));

            Assert.That((string)logged["Password"], Is.EqualTo("***"));
            Assert.That(logged.ToString(), Does.Not.Contain(SyntheticPassword));
            Assert.That((string)logged["UserName"], Is.EqualTo(request.UserName));
            Assert.That((int)logged["RpcId"], Is.EqualTo(request.RpcId));
            Assert.That(request.Password, Is.EqualTo(SyntheticPassword));
            CollectionAssert.AreEqual(before, codec.Encode(request));
        }

        [Test]
        public void RegisterLogMasksPresentEmptyAndNullPasswords()
        {
            NetworkDiagnostics.SetEnabled(true);
            foreach (string password in new[] { SyntheticPassword, "", null })
            {
                var request = C2G_Register.Create();
                request.UserName = "audit_register";
                request.Password = password;
                JObject logged = CaptureJson(() => NetworkDiagnostics.LogSend(request));
                Assert.That((string)logged["Password"], Is.EqualTo("***"));
                Assert.That(request.Password, Is.EqualTo(password));
            }
        }

        [Test]
        public void ReceivedNestedPasswordsAreMaskedIgnoringCaseWithoutChangingSource()
        {
            NetworkDiagnostics.SetEnabled(true);
            var child = new JObject { ["PASSWORD"] = SyntheticPassword, ["sequence"] = 3 };
            var envelope = new PasswordEnvelope
            {
                Password = SyntheticPassword,
                Children = new object[] { child, new { password = SyntheticPassword, state = "ready" } }
            };

            JObject logged = CaptureJson(() => NetworkDiagnostics.LogReceive(envelope));

            Assert.That((string)logged["pAsSwOrD"], Is.EqualTo("***"));
            Assert.That((string)logged["Children"][0]["PASSWORD"], Is.EqualTo("***"));
            Assert.That((string)logged["Children"][1]["password"], Is.EqualTo("***"));
            Assert.That((int)logged["Children"][0]["sequence"], Is.EqualTo(3));
            Assert.That((string)logged["Children"][1]["state"], Is.EqualTo("ready"));
            Assert.That(logged.ToString(), Does.Not.Contain(SyntheticPassword));
            Assert.That(envelope.Password, Is.EqualTo(SyntheticPassword));
            Assert.That((string)child["PASSWORD"], Is.EqualTo(SyntheticPassword));
        }

        [Test]
        public void OrdinaryProtocolFieldsAndPasswordWordInTextRemainReadable()
        {
            NetworkDiagnostics.SetEnabled(true);
            var request = C2G_SendRoomChat.Create();
            request.Content = "The word Password is ordinary chat text.";
            request.ClientMessageId = "audit_message";
            JObject logged = CaptureJson(() => NetworkDiagnostics.LogSend(request));
            Assert.That((string)logged["Content"], Is.EqualTo(request.Content));
            Assert.That((string)logged["ClientMessageId"], Is.EqualTo(request.ClientMessageId));
        }

        [Test]
        public void DisabledProtocolLoggingDoesNotEvaluatePayload()
        {
            NetworkDiagnostics.SetEnabled(false);
            List<string> logs = CaptureProtocolLogs(() =>
            {
                NetworkDiagnostics.LogSend(new FailingMessage());
                NetworkDiagnostics.LogReceive(new FailingMessage());
            });
            Assert.That(logs, Is.Empty);
        }

        [Test]
        public void SerializationFailureDoesNotLogExceptionMessageOrFallbackPayload()
        {
            NetworkDiagnostics.SetEnabled(true);
            LogAssert.Expect(LogType.Warning, "[Network] Send FailingMessage JSON序列化失败 error=JsonSerializationException");
            List<string> logs = CaptureProtocolLogs(() => NetworkDiagnostics.LogSend(new FailingMessage()));
            Assert.That(logs, Has.Count.EqualTo(1));
            Assert.That(logs[0], Does.Not.Contain(SyntheticPassword));
        }

        [Test]
        public void NullMessageRetainsJsonNullWithoutWarning()
        {
            NetworkDiagnostics.SetEnabled(true);
            List<string> logs = CaptureProtocolLogs(() => NetworkDiagnostics.LogReceive(null));
            Assert.That(logs, Is.EqualTo(new[] { "[Network] Receive null=null" }));
        }

        /// <summary>捕获真实 Unity 日志出口，避免只验证未接入发送和接收路径的格式化辅助函数。</summary>
        private static List<string> CaptureProtocolLogs(Action write)
        {
            var logs = new List<string>();
            Application.LogCallback listener = (message, stack, type) =>
            {
                if (message.StartsWith("[Network] ", StringComparison.Ordinal)) logs.Add(message);
            };
            Application.logMessageReceived += listener;
            try { write(); }
            finally { Application.logMessageReceived -= listener; }
            return logs;
        }

        private static JObject CaptureJson(Action write)
        {
            List<string> logs = CaptureProtocolLogs(write);
            Assert.That(logs, Has.Count.EqualTo(1));
            return JObject.Parse(logs[0].Substring(logs[0].IndexOf('=') + 1));
        }

        private sealed class PasswordEnvelope : IMessage
        {
            [JsonProperty("pAsSwOrD")]
            public string Password { get; set; }
            public object[] Children { get; set; }
        }

        private sealed class FailingMessage : IMessage
        {
            public string Password => throw new InvalidOperationException(SyntheticPassword);
        }

        private static void ResetArchitecture()
        {
            FieldInfo field = typeof(Architecture<TestNetworkDiagnosticsArchitecture>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }

            field?.SetValue(null, null);
        }

        /// <summary>设置序列化仅需语言默认值；不加载 YooAsset 包，不创建玩家资源或磁盘文件。</summary>
        private sealed class NoAssetResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object => null;
            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                onLoaded?.Invoke(null);
                return ResourceLoadHandle.Empty;
            }
            public void Release(ResourceLoadHandle handle) { }
        }

        /// <summary>命令历史依赖设置系统；使用内存存储，避免回归测试读取或写入真实玩家设置。</summary>
        private sealed class MemorySettingsStore : ISettingsFileStore
        {
            public SettingsData LoadOrDefault(out SettingsLoadResult result)
            {
                result = SettingsLoadResult.CreatedDefault;
                return SettingsData.CreateDefault();
            }

            public bool Save(SettingsData data, out string error)
            {
                error = null;
                return true;
            }
        }

        private sealed class TestNetworkDiagnosticsArchitecture : Architecture<TestNetworkDiagnosticsArchitecture>
        {
            protected override void Init()
            {
                RegisterModel(new TestCmdModel());
                RegisterModel(new TestToolSettingsModel());
                RegisterModel(new LocalizationModel());
                RegisterModel(new DisplaySettingsModel());
                RegisterModel(new AudioSettingsModel());
                RegisterModel(new HotKeyModel());
                RegisterModel(new MobileControlLayoutModel());
                RegisterUtility<ISettingsFileStore>(new MemorySettingsStore());
                RegisterSystem(new SettingsSystem());
                RegisterSystem(new TestCmdSystem());
            }
        }
    }
}

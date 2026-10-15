using System;
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
        [TearDown]
        public void TearDown()
        {
#if UNITY_EDITOR
            NetworkDiagnostics.SetEnabled(true);
#else
            NetworkDiagnostics.SetEnabled(false);
#endif
            ResetArchitecture();
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

        private static void ResetArchitecture()
        {
            FieldInfo field = typeof(Architecture<TestNetworkDiagnosticsArchitecture>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture)
            {
                architecture.Deinit();
            }

            field?.SetValue(null, null);
        }

        private sealed class TestNetworkDiagnosticsArchitecture : Architecture<TestNetworkDiagnosticsArchitecture>
        {
            protected override void Init()
            {
                RegisterModel(new TestCmdModel());
                RegisterSystem(new TestCmdSystem());
            }
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Text;
using Boot;
using NUnit.Framework;
using UnityEngine;
using Game.Network;
using System.Threading.Tasks;

namespace Game.Tests.Flow
{
    /// <summary>使用隔离队列和回环 HTTP 验证手动发送、确认与失败保留，不访问真实服务器。</summary>
    public sealed class DiagnosticUploadTests
    {
        private string root;
        private DiagnosticReportStore store;
        private System.Net.Sockets.TcpListener listener;
        private readonly System.Collections.Generic.List<string> bodies = new System.Collections.Generic.List<string>();
        private Task serverTask;
        private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AnotherDay-Upload-" + Guid.NewGuid().ToString("N"));
            store = new DiagnosticReportStore(Path.Combine(root, "queue.json"));
            DiagnosticReportSender.UseStoreForTests(store);
            bodies.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            DiagnosticReportSender.UseStoreForTests(null);
            listener?.Stop();
            if (serverTask != null) { try { serverTask.Wait(1000); } catch (AggregateException) { } }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private static object Read(string name)
        {
            var property = typeof(DiagnosticReportSender).GetProperty(name, Flags);
            Assert.NotNull(property, "需要公开上传状态：" + name);
            return property.GetValue(null);
        }

        private static bool Upload()
        {
            var method = typeof(DiagnosticReportSender).GetMethod("RequestUpload", Flags);
            Assert.NotNull(method, "设置页需要立即上传入口。");
            return (bool)method.Invoke(null, null);
        }

        private void AddGroups(int count)
        {
            for (int i = 0; i < count; i++)
                Assert.True(store.TryAdd("Runtime", "Error", i.ToString("x64"), "test", "Android"));
        }

        private void SetUrl(string url) => typeof(DiagnosticReportSender).GetField("reportUrl", Flags).SetValue(null, url);

        private void StartServer(int status, int requests, int delayMs = 0)
        {
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            SetUrl("http://127.0.0.1:" + ((System.Net.IPEndPoint)listener.LocalEndpoint).Port + "/diagnostics/errors");
            serverTask = Task.Run(async () =>
            {
                for (int i = 0; i < requests; i++)
                {
                    using (var client = await listener.AcceptTcpClientAsync())
                    using (var stream = client.GetStream())
                    {
                        var bytes = new System.Collections.Generic.List<byte>();
                        var one = new byte[1];
                        string headers;
                        do
                        {
                            if (await stream.ReadAsync(one, 0, 1) == 0) throw new IOException("HTTP headers truncated");
                            bytes.Add(one[0]);
                            headers = Encoding.ASCII.GetString(bytes.ToArray());
                        } while (!headers.EndsWith("\r\n\r\n"));
                        int length = 0;
                        foreach (var line in headers.Split(new[] { "\r\n" }, StringSplitOptions.None))
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                        var body = new byte[length];
                        int offset = 0;
                        while (offset < length)
                        {
                            int read = await stream.ReadAsync(body, offset, length - offset);
                            if (read == 0) throw new IOException("HTTP body truncated");
                            offset += read;
                        }
                        lock (bodies) bodies.Add(Encoding.UTF8.GetString(body));
                        if (delayMs > 0) await Task.Delay(delayMs);
                        var response = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " Result\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(response, 0, response.Length);
                    }
                }
            });
        }

        private static System.Collections.IEnumerator WaitForResult()
        {
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 12;
            while ((bool)Read("IsUploading") && UnityEditor.EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.False((bool)Read("IsUploading"), "发送应在超时上限内结束。");
        }

        [Test]
        public void EmptyQueue_ReportsNothingToUpload()
        {
            Assert.False(Upload());
            Assert.AreEqual("Empty", Read("Status").ToString());
        }

        [Test]
        public void InvalidTarget_RetainsQueueAndExplainsFailure()
        {
            AddGroups(1);
            Assert.False(Upload());
            Assert.AreEqual("ConfigurationInvalid", Read("Status").ToString());
            Assert.AreEqual(1, store.PendingGroups);
        }

        [Test]
        public void InvalidTarget_ChoosingUnsupportedHostReportsConfigurationFailureBeforeClick()
        {
            typeof(DiagnosticReportSender).GetField("configJson", Flags).SetValue(null, "{\"mode\":\"LocalGameServer\"}");
            DiagnosticReportSender.UseGameGateEndpoint("8.8.8.8:31001");
            Assert.AreEqual("ConfigurationInvalid", Read("Status").ToString());
            Assert.IsNull(Read("ReportUrl"));
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ClearDuringUpload_DoesNotConfirmNewQueueOrOverwriteClearStatus()
        {
            AddGroups(1);
            StartServer(200, 1, 500);
            Assert.True(Upload());
            Assert.True(DiagnosticReportSender.Clear());
            AddGroups(1);
            yield return WaitForResult();
            Assert.AreEqual("Empty", Read("Status").ToString());
            Assert.AreEqual(1, store.PendingGroups, "清理前请求不得扣减清理后新建的记录。");
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ManualUpload_BypassesBackoffDrains58GroupsAndRejectsDoubleClick()
        {
            AddGroups(58);
            StartServer(200, 3);
            typeof(DiagnosticReportSender).GetField("nextAttemptUtc", Flags).SetValue(null, DateTime.UtcNow.AddMinutes(30));
            Assert.True(Upload());
            Assert.False(Upload());
            Assert.AreEqual("Sending", Read("Status").ToString());
            yield return WaitForResult();
            Assert.AreEqual("Completed", Read("Status").ToString());
            Assert.AreEqual(0, store.PendingGroups);
            Assert.AreNotEqual(DateTime.MinValue, Read("LastAttemptUtc"));
            Assert.True(serverTask.IsCompleted && !serverTask.IsFaulted);
            Assert.AreEqual(3, bodies.Count);
            Assert.AreEqual(20, JsonUtility.FromJson<DiagnosticBatch>(bodies[0]).reports.Count);
            Assert.AreEqual(18, JsonUtility.FromJson<DiagnosticBatch>(bodies[2]).reports.Count);
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator HttpFailure_RetainsBatchIdentityAndReportsStatusCode()
        {
            AddGroups(1);
            string batchId = store.GetOrCreateBatch().batchId;
            StartServer(503, 1);
            Assert.True(Upload());
            yield return WaitForResult();
            Assert.AreEqual("HttpFailed", Read("Status").ToString());
            Assert.AreEqual(503L, Read("LastResponseCode"));
            Assert.AreEqual(1, store.PendingGroups);
            Assert.AreEqual(batchId, store.GetOrCreateBatch().batchId);
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ConnectionFailure_PreservesPendingRecords()
        {
            AddGroups(1);
            listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            SetUrl("http://127.0.0.1:" + port + "/diagnostics/errors");
            Assert.True(Upload());
            yield return WaitForResult();
            Assert.AreEqual("ConnectionFailed", Read("Status").ToString());
            Assert.AreEqual(1, store.PendingGroups);
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator Timeout_PreservesPendingRecordsAndReportsTimeout()
        {
            AddGroups(1);
            StartServer(200, 1, 6500);
            Assert.True(Upload());
            yield return WaitForResult();
            Assert.AreEqual("TimedOut", Read("Status").ToString());
            Assert.AreEqual(1, store.PendingGroups);
        }

        [Test]
        public void NetworkPrefab_ExposesManualButtonAndDynamicStatusThroughGeneratedBindings()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Page/SettingNetwork.prefab");
            var ui = prefab.GetComponent<SettingNetworkPageUI>();
            var collector = prefab.GetComponent<ReferenceCollector>();
            foreach (string fieldName in new[] { "DiagnosticUploadButton", "DiagnosticStatus" })
            {
                var field = ui.GetType().GetField(fieldName);
                Assert.NotNull(field, "生成的 View 缺少字段：" + fieldName);
                var value = field.GetValue(ui) as UnityEngine.Object;
                Assert.NotNull(value, fieldName + " 必须实际反序列化成功。");
                Assert.AreSame(value, collector.GetObject(fieldName));
            }
        }
    }

    [TestFixture]
    public sealed class DiagnosticReportTests
    {
        private string root;
        private DiagnosticReportStore store;
        private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        private static string ResolveUrl(string config, string endpoint)
        {
            MethodInfo method = typeof(DiagnosticReportSender).GetMethod("ResolveReportUrl",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "发送器需要提供统一的诊断地址选择逻辑。");
            return (string)method.Invoke(null, new object[] { config, endpoint });
        }

        [TestCase("192.168.2.104:31001", "http://192.168.2.104:31002/diagnostics/errors")]
        [TestCase("10.0.0.8:32001", "http://10.0.0.8:31002/diagnostics/errors")]
        [TestCase("172.16.0.8:31001", "http://172.16.0.8:31002/diagnostics/errors")]
        [TestCase("127.0.0.1:31001", "http://127.0.0.1:31002/diagnostics/errors")]
        public void Endpoint_LocalModeUsesGameServerHostAndIndependentPort(string endpoint, string expected)
        {
            Assert.AreEqual(expected, ResolveUrl("{\"mode\":\"LocalGameServer\"}", endpoint));
        }

        [TestCase("8.8.8.8:31001")]
        [TestCase("172.32.0.8:31001")]
        [TestCase("server.example.com:31001")]
        [TestCase("192.168.2.104:0")]
        [TestCase("192.168.2.104:31001/other")]
        [TestCase("user@192.168.2.104:31001")]
        [TestCase("")]
        public void Endpoint_LocalModeRejectsPublicOrInvalidTargets(string endpoint)
        {
            Assert.IsNull(ResolveUrl("{\"mode\":\"LocalGameServer\"}", endpoint));
        }

        [Test]
        public void Endpoint_FixedHttpsDoesNotFollowEditableGameServer()
        {
            const string config = "{\"mode\":\"Fixed\",\"reportUrl\":\"https://diagnostics.example.com/diagnostics/errors\"}";
            Assert.AreEqual("https://diagnostics.example.com/diagnostics/errors", ResolveUrl(config, "192.168.2.104:31001"));
            Assert.IsNull(ResolveUrl("{\"mode\":\"Fixed\",\"reportUrl\":\"http://192.168.2.104:31002/diagnostics/errors\"}", "192.168.2.104:31001"));
            Assert.AreEqual("http://127.0.0.1:31002/diagnostics/errors", ResolveUrl(
                "{\"reportUrl\":\"http://127.0.0.1:31002/diagnostics/errors\"}", "192.168.2.104:31001"));
        }

        [Test]
        public void Endpoint_StartupPrefersSavedAddressAndBlocksCorruptOverride()
        {
            MethodInfo read = typeof(DiagnosticReportSender).GetMethod("ReadGameGateEndpoint", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(read);
            Assert.AreEqual("192.168.2.104:31001", read.Invoke(null, new object[] { root, "192.168.2.104:31001" }));
            Directory.CreateDirectory(Path.Combine(root, "Online"));
            string file = Path.Combine(root, "Online", "network-endpoint.json");
            File.WriteAllText(file, "{\"version\":1,\"gameGateEndpoint\":\"192.168.2.105:31001\"}");
            Assert.AreEqual("192.168.2.105:31001", read.Invoke(null, new object[] { root, "192.168.2.104:31001" }));
            File.WriteAllText(file, "{\"version\":2,\"gameGateEndpoint\":\"192.168.2.105:31001\"}");
            Assert.IsNull(read.Invoke(null, new object[] { root, "192.168.2.104:31001" }));
            File.WriteAllText(file, "{ broken");
            Assert.IsNull(read.Invoke(null, new object[] { root, "192.168.2.104:31001" }));
        }

        [Test]
        public void Endpoint_SavingGameServerRefreshesTargetAndKeepsHistoricalBatch()
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            typeof(DiagnosticReportSender).GetField("configJson", flags).SetValue(null, "{\"mode\":\"LocalGameServer\"}");
            typeof(DiagnosticReportSender).GetField("nextAttemptUtc", flags).SetValue(null, DateTime.UtcNow.AddMinutes(30));
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "Android", utc: DateTime.UtcNow.AddDays(-1)));
            DiagnosticBatch history = store.GetOrCreateBatch();
            int generation = store.Generation;
            var provider = new StreamingAssetsNetworkConfigProvider(_ => Task.FromResult("{}"), root);
            Assert.IsTrue(provider.TrySaveEndpoint("192.168.2.104:31001", out _, out _));
            Assert.AreEqual("http://192.168.2.104:31002/diagnostics/errors", typeof(DiagnosticReportSender).GetField("reportUrl", flags).GetValue(null));
            Assert.AreEqual(DateTime.MinValue, typeof(DiagnosticReportSender).GetField("nextAttemptUtc", flags).GetValue(null));
            Assert.AreEqual(generation, store.Generation);
            Assert.AreEqual(history.batchId, store.GetOrCreateBatch().batchId);
            Assert.AreEqual(1, store.PendingGroups);
            Assert.IsFalse(provider.TrySaveEndpoint("broken", out _, out _));
            Assert.AreEqual("http://192.168.2.104:31002/diagnostics/errors", typeof(DiagnosticReportSender).GetField("reportUrl", flags).GetValue(null));
        }

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "AnotherDay-Diagnostic-" + Guid.NewGuid().ToString("N"));
            store = new DiagnosticReportStore(Path.Combine(root, "queue.json"));
            DiagnosticReportSender.UseStoreForTests(store);
        }

        [TearDown]
        public void TearDown()
        {
            DiagnosticReportSender.UseStoreForTests(null);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void Queue_MergesSameDayAndSeparatesUtcDays()
        {
            DateTime first = DateTime.UtcNow.Date.AddHours(12);
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor", utc: first));
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor", utc: first.AddMinutes(1)));
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor", utc: first.AddDays(1)));
            Assert.AreEqual(2, store.PendingGroups);
            DiagnosticBatch batch = store.GetOrCreateBatch();
            Assert.AreEqual(2, batch.reports.Count);
            Assert.AreEqual(2, batch.reports[0].count);
        }

        [Test]
        public void Batch_RetryAndLateConfirmationRespectClearGeneration()
        {
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor"));
            DiagnosticBatch original = store.GetOrCreateBatch();
            int generation = store.Generation;
            Assert.AreEqual(original.batchId, new DiagnosticReportStore(Path.Combine(root, "queue.json")).GetOrCreateBatch().batchId);
            Assert.IsTrue(store.Clear());
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor"));
            Assert.IsFalse(store.Confirm(original.batchId, generation));
            Assert.AreEqual(1, store.PendingGroups);
        }

        [Test]
        public void Batch_ConfirmationKeepsNewSameFingerprintCount()
        {
            DateTime firstUtc = DateTime.UtcNow.Date.AddHours(12);
            Assert.IsTrue(store.TryAdd("Runtime", "Exception", Hash, "test", "WindowsEditor", utc: firstUtc));
            DiagnosticBatch batch = store.GetOrCreateBatch();
            int generation = store.Generation;
            Assert.IsTrue(store.TryAdd("Runtime", "Exception", Hash, "test", "WindowsEditor", utc: firstUtc.AddMinutes(1)));
            Assert.IsTrue(store.Confirm(batch.batchId, generation));
            DiagnosticReport remaining = store.GetOrCreateBatch().reports[0];
            Assert.AreEqual(1, remaining.count);
            Assert.AreEqual(firstUtc.AddMinutes(1).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"), remaining.firstUtc);
        }

        [Test]
        public void Fingerprint_DoesNotContainRawLocation()
        {
            string hash = DiagnosticReportStore.Fingerprint("Runtime", "Error", "secret/path");
            Assert.AreEqual(64, hash.Length);
            Assert.IsFalse(hash.Contains("secret"));
            Assert.AreEqual(hash, DiagnosticReportStore.Fingerprint("Runtime", "Error", "secret/path"));
        }

        [Test]
        public void Batch_SplitsCountAboveTenThousandAcrossConfirmations()
        {
            DateTime now = DateTime.UtcNow;
            string date = now.ToString("yyyy-MM-dd");
            string timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
            string file = Path.Combine(root, "queue.json");
            Directory.CreateDirectory(root);
            File.WriteAllText(file,
                "{\"reports\":[{\"source\":\"Runtime\",\"kind\":\"Error\",\"fingerprint\":\"" + Hash +
                "\",\"buildVersion\":\"test\",\"platform\":\"WindowsEditor\",\"eventDate\":\"" + date +
                "\",\"firstUtc\":\"" + timestamp + "\",\"lastUtc\":\"" + timestamp +
                "\",\"count\":10001,\"bootCode\":\"\",\"bootStage\":\"\"}],\"hasPending\":false}", new UTF8Encoding(false));
            store = new DiagnosticReportStore(file);
            DiagnosticBatch first = store.GetOrCreateBatch();
            Assert.AreEqual(10000, first.reports[0].count);
            Assert.IsTrue(store.Confirm(first.batchId, store.Generation));
            Assert.AreEqual(1, store.GetOrCreateBatch().reports[0].count);
        }

        [Test]
        public void Queue_CorruptFileIsIsolatedAndDoesNotBlockNewEvents()
        {
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "queue.json");
            File.WriteAllText(file, "{ broken", new UTF8Encoding(false));
            store = new DiagnosticReportStore(file);
            Assert.AreEqual(0, store.PendingGroups);
            Assert.AreEqual(1, Directory.GetFiles(root, "queue.json.corrupt-*").Length);
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor"));
            Assert.AreEqual(1, store.PendingGroups);
        }

        [Test]
        public void Queue_ExpiresEventsAfterSevenUtcDays()
        {
            DateTime old = DateTime.UtcNow.Date.AddDays(-7).AddHours(12);
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor", utc: old));
            Assert.IsNull(store.GetOrCreateBatch());
            Assert.AreEqual(0, store.PendingGroups);
        }

        [Test]
        public void Clear_DeletionFailureRetainsRetryableQueue()
        {
            Assert.IsTrue(store.TryAdd("Runtime", "Error", Hash, "test", "WindowsEditor"));
            string file = Path.Combine(root, "queue.json");
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.IsFalse(store.Clear());
                Assert.AreEqual(1, store.PendingGroups);
            }
            Assert.IsTrue(store.Clear());
            Assert.AreEqual(0, store.PendingGroups);
        }

        [Test]
        public void Queue_CapacityEvictsNonBatchGroupBeforeProtectedBatch()
        {
            DateTime now = DateTime.UtcNow.Date.AddHours(12);
            for (int i = 0; i < 128; i++)
                Assert.IsTrue(store.TryAdd("Runtime", "Error", i.ToString("x64"), "test", "WindowsEditor",
                    utc: now.AddSeconds(i)));
            DiagnosticBatch protectedBatch = store.GetOrCreateBatch();
            Assert.AreEqual(20, protectedBatch.reports.Count);
            Assert.IsTrue(store.TryAdd("Runtime", "Error", 129.ToString("x64"), "test", "WindowsEditor",
                utc: now.AddMinutes(3)));
            Assert.AreEqual(128, store.PendingGroups);
            Assert.AreEqual(protectedBatch.batchId, store.GetOrCreateBatch().batchId);
            Assert.IsTrue(store.Confirm(protectedBatch.batchId, store.Generation));
            Assert.AreEqual(108, store.PendingGroups);
            StringAssert.Contains(129.ToString("x64"), File.ReadAllText(Path.Combine(root, "queue.json")));
        }

        [Test]
        public void Driver_TransfersOnlyAfterMainReady()
        {
            DiagnosticReportSender.BeginBoot();
            DiagnosticReportSender.RegisterGame();
            Assert.AreEqual("Boot", DiagnosticReportSender.ActiveDriverForTests);
            DiagnosticReportSender.MainReady();
            Assert.AreEqual("Game", DiagnosticReportSender.ActiveDriverForTests);
            DiagnosticReportSender.EndBoot();
            Assert.AreEqual("Game", DiagnosticReportSender.ActiveDriverForTests);
            DiagnosticReportSender.UnregisterGame();
            Assert.AreEqual("None", DiagnosticReportSender.ActiveDriverForTests);
        }

        [Test]
        public void RuntimeCapture_OnlyErrorsReachAnonymousQueue()
        {
            DiagnosticReportSender.RegisterGame();
            DiagnosticReportSender.CaptureRuntime("ignored log", LogType.Log);
            DiagnosticReportSender.CaptureRuntime("ignored warning", LogType.Warning);
            DiagnosticReportSender.CaptureRuntime("private/path", LogType.Error);
            DiagnosticReportSender.CaptureRuntime("private/path", LogType.Exception);
            DiagnosticReportSender.CaptureRuntime("private/path", LogType.Assert);
            DiagnosticReportSender.TickGame();
            DiagnosticBatch batch = store.GetOrCreateBatch();
            Assert.AreEqual(3, batch.reports.Count);
            foreach (DiagnosticReport report in batch.reports)
            {
                Assert.AreEqual("Runtime", report.source);
                Assert.IsEmpty(report.bootCode);
                Assert.IsEmpty(report.bootStage);
            }
            StringAssert.DoesNotContain("private/path", File.ReadAllText(Path.Combine(root, "queue.json")));
        }

        [Test]
        public void BootFailure_RecordsCodeAndStageWithoutDetails()
        {
            BootFailure failure = BootFailure.Create(BootFailureCode.UnexpectedException, BootFailureStage.Unexpected);
            DiagnosticReportSender.RecordBoot(failure);
            DiagnosticReport report = store.GetOrCreateBatch().reports[0];
            Assert.AreEqual("Boot", report.source);
            Assert.AreEqual("BootFailure", report.kind);
            Assert.AreEqual("UnexpectedException", report.bootCode);
            Assert.AreEqual("Unexpected", report.bootStage);
            StringAssert.DoesNotContain(failure.CopyDetails, File.ReadAllText(Path.Combine(root, "queue.json")));
        }
    }
}

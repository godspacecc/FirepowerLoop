using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Boot
{
    /// <summary>诊断发送结果；界面负责本地化，发送器只持有网络与队列状态。</summary>
    public enum DiagnosticUploadStatus
    {
        Ready, Loading, Sending, Completed, Empty, ConfigurationInvalid,
        ConnectionFailed, TimedOut, HttpFailed, StorageFailed, Cancelled
    }

    /// <summary>
    /// Boot 与 Game 的单一诊断发送器；负责包内模式、目标地址及上传，不管理登录或完整日志。
    /// 日志线程只投递有限快照，网络任务不引用界面或 System。
    /// </summary>
    public static class DiagnosticReportSender
    {
        private const int MaxSnapshots = 256;
        private static readonly ConcurrentQueue<RuntimeSnapshot> snapshots = new ConcurrentQueue<RuntimeSnapshot>();
        private static DiagnosticReportStore store;
        private static int snapshotCount;
        private static bool bootOwned;
        private static bool gameCandidate;
        private static bool gameOwned;
        private static bool sending;
        private static bool configLoaded;
        private static string reportUrl;
        /// <summary>本次运行的包内模式配置；加载完成前为 null，玩家不能切换模式。</summary>
        private static string configJson;
        /// <summary>包内联机初始地址；无玩家覆盖时使用，生命周期与配置加载一致。</summary>
        private static string defaultGameGateEndpoint;
        private static DateTime nextAttemptUtc;
        private static int failures;
        private static int lease;
        private static UnityWebRequest activeRequest;

        /// <summary>整个连续批次发送期间为 true，手动与自动发送共用单一并发锁。</summary>
        public static bool IsUploading => sending;
        public static DiagnosticUploadStatus Status { get; private set; }
        /// <summary>最后一个实际 HTTP 请求开始的 UTC 时间；MinValue 表示尚未发送。</summary>
        public static DateTime LastAttemptUtc { get; private set; }
        /// <summary>最近请求的 HTTP 状态码；0 表示没有收到 HTTP 响应。</summary>
        public static long LastResponseCode { get; private set; }
        public static string ReportUrl => reportUrl;
        public static DateTime NextAttemptUtc => nextAttemptUtc;
        /// <summary>界面刷新版本；状态、目标或请求结果变化时递增，不承载业务权威。</summary>
        public static int StatusRevision { get; private set; }

        private static void SetStatus(DiagnosticUploadStatus value)
        {
            Status = value;
            StatusRevision++;
        }

        private struct RuntimeSnapshot
        {
            public string Kind;
            public string Stack;
            public DateTime Utc;
        }

        private static DiagnosticReportStore Store => store ?? (store = new DiagnosticReportStore(
            Path.Combine(Application.persistentDataPath, "Diagnostics", "queue.json")));

#if UNITY_INCLUDE_TESTS
        public static string ActiveDriverForTests => bootOwned ? "Boot" : gameOwned ? "Game" : "None";

        /// <summary>隔离诊断采集测试，不读取设备真实队列或发起 HTTP。</summary>
        public static void UseStoreForTests(DiagnosticReportStore isolatedStore)
        {
            CancelInFlight();
            store = isolatedStore;
            bootOwned = gameCandidate = gameOwned = sending = false;
            configLoaded = isolatedStore != null;
            reportUrl = null;
            configJson = null;
            defaultGameGateEndpoint = null;
            nextAttemptUtc = DateTime.MinValue;
            failures = 0;
            LastAttemptUtc = DateTime.MinValue;
            LastResponseCode = 0;
            SetStatus(DiagnosticUploadStatus.Ready);
            lease++;
            while (snapshots.TryDequeue(out _)) { }
            Interlocked.Exchange(ref snapshotCount, 0);
        }
#endif

        public static int PendingGroups => Store.PendingGroups;

        /// <summary>玩家点击时立即补传，绕过退避；空队列、无效配置及重复点击不发请求。</summary>
        /// <returns>是否已启动连续批次发送；失败原因由 Status 提供，原队列保持不变。</returns>
        public static bool RequestUpload()
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return false;
            if (sending) return false;
            DrainSnapshots(MaxSnapshots);
            if (Store.PendingGroups == 0) { SetStatus(DiagnosticUploadStatus.Empty); return false; }
            if (!configLoaded) { SetStatus(DiagnosticUploadStatus.Loading); return false; }
            if (string.IsNullOrEmpty(reportUrl)) { SetStatus(DiagnosticUploadStatus.ConfigurationInvalid); return false; }
            nextAttemptUtc = DateTime.MinValue;
            SendAsync().Forget();
            return true;
        }

        /// <summary>Boot 入口先持有驱动；主场景未宣告就绪时仍可在失败页重试发送。</summary>
        public static void BeginBoot()
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return;
            bootOwned = true;
            gameOwned = false;
            lease++;
            LoadConfigAsync().Forget();
        }

        public static void RegisterGame()
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return;
            gameCandidate = true;
            if (!bootOwned) { gameOwned = true; lease++; LoadConfigAsync().Forget(); }
        }

        public static void MainReady()
        {
            if (!bootOwned) return;
            bootOwned = false;
            gameOwned = gameCandidate;
            lease++;
            CancelInFlight();
        }

        public static void EndBoot()
        {
            if (!bootOwned) return;
            bootOwned = false;
            gameOwned = gameCandidate;
            lease++;
            CancelInFlight();
        }

        public static void UnregisterGame()
        {
            gameCandidate = false;
            if (gameOwned) { gameOwned = false; lease++; CancelInFlight(); }
        }

        public static void RecordBoot(BootFailure failure)
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return;
            if (failure == null) return;
            try
            {
                string code = failure.Code.ToString();
                string stage = failure.Stage.ToString();
                Store.TryAdd("Boot", "BootFailure", DiagnosticReportStore.Fingerprint("Boot", "BootFailure", code + "/" + stage),
                    Application.version, Application.platform.ToString(), code, stage);
            }
            catch { }
        }

        /// <summary>线程日志回调只保留有限调用栈快照；不访问 Unity 主线程 API、磁盘和网络。</summary>
        public static void CaptureRuntime(string stack, LogType type)
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return;
            string kind = type switch
            {
                LogType.Error => "Error", LogType.Exception => "Exception", LogType.Assert => "Assert", _ => null
            };
            if (kind == null || Interlocked.Increment(ref snapshotCount) > MaxSnapshots)
            {
                Interlocked.Decrement(ref snapshotCount);
                return;
            }
            snapshots.Enqueue(new RuntimeSnapshot { Kind = kind, Stack = stack == null ? "" : stack.Substring(0, Math.Min(stack.Length, 4096)), Utc = DateTime.UtcNow });
        }

        public static void TickBoot() { if (bootOwned) Tick(); }
        public static void TickGame() { if (gameOwned) Tick(); }

        private static void Tick()
        {
            if (ClientReleasePolicy.OfflineSinglePlayer) return;
            DrainSnapshots(32);
            if (configLoaded && !sending && !string.IsNullOrEmpty(reportUrl) && DateTime.UtcNow >= nextAttemptUtc && Store.PendingGroups > 0)
                SendAsync().Forget();
        }

        /// <summary>仅主线程将有限日志快照落盘；手动上传先纳入已采集但尚未 Tick 的错误。</summary>
        private static void DrainSnapshots(int maximum)
        {
            for (int i = 0; i < maximum && snapshots.TryDequeue(out RuntimeSnapshot snapshot); i++)
            {
                Interlocked.Decrement(ref snapshotCount);
                try
                {
                    Store.TryAdd("Runtime", snapshot.Kind,
                        DiagnosticReportStore.Fingerprint("Runtime", snapshot.Kind, snapshot.Stack),
                        Application.version, Application.platform.ToString(), utc: snapshot.Utc);
                }
                catch { }
            }
        }

        public static bool Clear()
        {
            bool cleared = Store.Clear();
            if (cleared)
            {
                lease++; CancelInFlight();
                while (snapshots.TryDequeue(out _)) Interlocked.Decrement(ref snapshotCount);
                SetStatus(DiagnosticUploadStatus.Empty);
            }
            return cleared;
        }

        private static void CancelInFlight()
        {
            if (sending) SetStatus(DiagnosticUploadStatus.Cancelled);
            try { activeRequest?.Abort(); } catch { }
        }

        /// <summary>联机覆盖文件删除后重新选择诊断地址；不清理统计队列。</summary>
        public static void RefreshGameGateEndpoint()
        {
            if (configJson == null) return;
            UseGameGateEndpoint(ReadGameGateEndpoint(Application.persistentDataPath, defaultGameGateEndpoint));
        }

        /// <summary>联机地址成功保存后刷新局域网目标；固定模式不受影响，切换时保留未确认批次。</summary>
        public static void UseGameGateEndpoint(string endpoint)
        {
            if (configJson == null) return;
            string nextUrl = ResolveReportUrl(configJson, endpoint);
            if (nextUrl == reportUrl)
            {
                // 初次加载即无效时两者都为 null，也必须结束“读取中”并说明配置问题。
                if (string.IsNullOrEmpty(nextUrl)) SetStatus(DiagnosticUploadStatus.ConfigurationInvalid);
                return;
            }
            reportUrl = nextUrl;
            lease++;
            CancelInFlight();
            failures = 0;
            nextAttemptUtc = DateTime.MinValue;
            LastResponseCode = 0;
            SetStatus(string.IsNullOrEmpty(reportUrl) ? DiagnosticUploadStatus.ConfigurationInvalid : DiagnosticUploadStatus.Ready);
        }

        /// <summary>读取与联机 Provider 相同的版本 1 覆盖文件；损坏覆盖不偷偷回退到其它电脑。</summary>
        private static string ReadGameGateEndpoint(string persistentRoot, string fallback)
        {
            try
            {
                string path = Path.Combine(persistentRoot, "Online", "network-endpoint.json");
                if (!File.Exists(path)) return fallback;
                var saved = JsonUtility.FromJson<GameGateConfig>(File.ReadAllText(path));
                return saved != null && saved.version == 1 ? saved.gameGateEndpoint : null;
            }
            catch { return null; }
        }

        /// <summary>局域网模式仅接受内网 IPv4/回环 IP；固定模式保留 HTTPS 与旧本机 HTTP 配置。</summary>
        private static string ResolveReportUrl(string json, string endpoint)
        {
            try
            {
                var config = JsonUtility.FromJson<DiagnosticConfig>(json);
                if (config == null) return null;
                if (config.mode == "LocalGameServer")
                {
                    if (string.IsNullOrWhiteSpace(endpoint) || endpoint != endpoint.Trim() ||
                        !Uri.TryCreate("tcp://" + endpoint, UriKind.Absolute, out Uri gate) ||
                        gate.Port < 1 || gate.Port > 65535 || gate.AbsolutePath != "/" ||
                        gate.UserInfo.Length != 0 || gate.Query.Length != 0 || gate.Fragment.Length != 0 ||
                        !IPAddress.TryParse(gate.Host, out IPAddress ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                        return null;
                    byte[] bytes = ip.GetAddressBytes();
                    bool local = bytes[0] == 10 || bytes[0] == 127 ||
                        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                        (bytes[0] == 192 && bytes[1] == 168);
                    return local ? "http://" + ip + ":31012/diagnostics/errors" : null;
                }
                if (!string.IsNullOrEmpty(config.mode) && config.mode != "Fixed") return null;
                if (!Uri.TryCreate(config.reportUrl, UriKind.Absolute, out Uri uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                    (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)) return null;
                return uri.AbsoluteUri;
            }
            catch { return null; }
        }

        /// <summary>包内配置选择局域网或固定地址；Android APK 内通过 UnityWebRequest 读取。</summary>
        private static async UniTaskVoid LoadConfigAsync()
        {
            if (configLoaded) return;
            SetStatus(DiagnosticUploadStatus.Loading);
            try
            {
                string json = await ReadStreamingTextAsync("diagnosticConfig.json");
                var config = JsonUtility.FromJson<DiagnosticConfig>(json);
                if (config?.mode == "LocalGameServer")
                {
                    try
                    {
                        string networkJson = await ReadStreamingTextAsync("networkConfig.json");
                        defaultGameGateEndpoint = JsonUtility.FromJson<GameGateConfig>(networkJson)?.gameGateEndpoint;
                    }
                    catch (FileNotFoundException) { defaultGameGateEndpoint = "127.0.0.1:31011"; }
                }
                configJson = json;
                RefreshGameGateEndpoint();
            }
            catch { SetStatus(DiagnosticUploadStatus.ConfigurationInvalid); }
            finally { configLoaded = true; }
        }

        private static async UniTask<string> ReadStreamingTextAsync(string fileName)
        {
            string path = Path.Combine(Application.streamingAssetsPath, fileName);
            if (!path.Contains("://")) return File.ReadAllText(path);
            using (var request = UnityWebRequest.Get(path))
            {
                request.timeout = 5;
                await request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) throw new IOException("StreamingAssets read failed");
                return request.downloadHandler.text;
            }
        }

        private static async UniTaskVoid SendAsync()
        {
            if (sending) return;
            sending = true;
            int expectedLease = lease;
            int expectedGeneration = Store.Generation;
            SetStatus(DiagnosticUploadStatus.Sending);
            bool success = false;
            DiagnosticUploadStatus result = DiagnosticUploadStatus.StorageFailed;
            try
            {
                // 每批最多 20 组，确认成功后连续发送下一批；任一失败立即停下并保留不可变批次。
                while (expectedLease == lease && expectedGeneration == Store.Generation)
                {
                    DiagnosticBatch batch = Store.GetOrCreateBatch();
                    if (batch == null)
                    {
                        success = Store.PendingGroups == 0;
                        result = success ? DiagnosticUploadStatus.Completed : DiagnosticUploadStatus.StorageFailed;
                        break;
                    }
                    byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(batch));
                    using (var request = new UnityWebRequest(reportUrl, "POST"))
                    {
                        activeRequest = request;
                        request.uploadHandler = new UploadHandlerRaw(body);
                        request.downloadHandler = new DownloadHandlerBuffer();
                        request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                        request.timeout = 5;
                        LastAttemptUtc = DateTime.UtcNow;
                        LastResponseCode = 0;
                        StatusRevision++;
                        // UniTask 对非 2xx 会抛异常；仍须从 request 读取状态码和连接失败类型。
                        try { await request.SendWebRequest(); } catch { }
                        if (expectedLease != lease || expectedGeneration != Store.Generation) break;
                        LastResponseCode = request.responseCode;
                        if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                        {
                            result = request.responseCode > 0 ? DiagnosticUploadStatus.HttpFailed :
                                (request.error?.IndexOf("tim", StringComparison.OrdinalIgnoreCase) >= 0
                                    ? DiagnosticUploadStatus.TimedOut : DiagnosticUploadStatus.ConnectionFailed);
                            break;
                        }
                    }
                    activeRequest = null;
                    if (!Store.Confirm(batch.batchId, expectedGeneration))
                    {
                        result = DiagnosticUploadStatus.StorageFailed;
                        break;
                    }
                }
            }
            catch { }
            finally
            {
                activeRequest = null;
                // 地址或驱动已切换时，旧请求不得覆盖新目标的立即重试时间。
                if (expectedLease == lease)
                {
                    if (success) { failures = 0; nextAttemptUtc = DateTime.UtcNow; }
                    else
                    {
                        failures = Math.Min(failures + 1, 6);
                        nextAttemptUtc = DateTime.UtcNow.AddSeconds(Math.Min(1800, 30 * (1 << (failures - 1))));
                    }
                    SetStatus(result);
                }
                sending = false;
                StatusRevision++;
            }
        }

        [Serializable]
        private sealed class DiagnosticConfig { public string mode; public string reportUrl; }

        [Serializable]
        private sealed class GameGateConfig { public int version; public string gameGateEndpoint; }
    }
}

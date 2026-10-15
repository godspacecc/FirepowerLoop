using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;

namespace ET.Server
{
    /// <summary>诊断 HTTP 路由边界；先限定请求体，再校验匿名字段，最后委托持久仓库确认。</summary>
    [HttpHandler(SceneType.RouterManager, "/diagnostics/errors")]
    public sealed class DiagnosticReportHandler : IHttpHandler
    {
        private const int MaxBodyBytes = 16 * 1024;
        private static HashSet<string> RootFields() => new(StringComparer.Ordinal)
            { "schemaVersion", "batchId", "reports" };
        private static HashSet<string> ReportFields() => new(StringComparer.Ordinal)
            { "source", "kind", "fingerprint", "buildVersion", "platform", "eventDate", "firstUtc", "lastUtc", "count", "bootCode", "bootStage" };
        private static HashSet<string> BootCodes() => new(StringComparer.Ordinal)
            { "BootstrapConfigInvalid", "HostInitializationFailed", "PackageVersionRequestFailed", "PackageManifestUpdateFailed", "ResourceDownloadFailed", "BuiltinResourceInvalid", "RequiredResourceMissing", "AotListInvalid", "AotDllMissing", "AotMetadataRejected", "AotMetadataException", "GameDllMissing", "GameDllLoadFailed", "MainSceneLoadFailed", "MainInitializationFailed", "UnexpectedException" };
        private static HashSet<string> BootStages() => new(StringComparer.Ordinal)
            { "BootstrapConfig", "ResourceInitialization", "ResourceVersion", "ResourceManifest", "ResourceDownload", "ResourceValidation", "AotList", "AotMetadata", "GameAssembly", "MainScene", "MainInitialization", "Unexpected" };
        private static HashSet<string> Platforms() => new(StringComparer.Ordinal)
            { "WindowsPlayer", "OSXPlayer", "LinuxPlayer", "Android", "WindowsEditor", "OSXEditor", "LinuxEditor" };

        /// <summary>非 OnlineLocal 先返回 404，诊断路径异常只产生 HTTP 503，不冒泡到其它路由。</summary>
        public async ETTask Handle(Scene scene, HttpListenerContext context)
        {
            int status;
            try
            {
                if (!string.Equals(ServerConfig.Instance.Profile, "OnlineLocal", StringComparison.Ordinal))
                    status = 404;
                else if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.Ordinal))
                    status = 405;
                else if (!string.Equals(context.Request.ContentType?.Split(';')[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
                    status = 400;
                else if (context.Request.ContentLength64 > MaxBodyBytes)
                    status = 413;
                else
                {
                    byte[] body = await ReadLimitedAsync(context.Request);
                    if (body == null) status = 413;
                    else if (!TryParse(body, out DiagnosticBatch batch)) status = 400;
                    else status = DiagnosticReportStore.Shared.Accept(batch, context.Request.RemoteEndPoint?.Address,
                        context.Request.Headers["X-Real-IP"], Options.Instance.DiagnosticTrustedProxyIp, DateTimeOffset.UtcNow);
                }
            }
            catch (OperationCanceledException) { status = 503; }
            catch (Exception) { status = 503; }

            try
            {
                context.Response.StatusCode = status;
                context.Response.ContentLength64 = 0;
            }
            catch
            {
                // 断开的客户端无需产生服务端递归错误。
            }
        }

        private static async System.Threading.Tasks.Task<byte[]> ReadLimitedAsync(HttpListenerRequest request)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var memory = new MemoryStream();
            byte[] chunk = new byte[4096];
            while (true)
            {
                int length = await request.InputStream.ReadAsync(chunk.AsMemory(), timeout.Token);
                if (length == 0) break;
                if (memory.Length + length > MaxBodyBytes) return null;
                memory.Write(chunk, 0, length);
            }
            return memory.ToArray();
        }

        private static bool TryParse(byte[] body, out DiagnosticBatch batch)
        {
            batch = null;
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                JsonElement root = document.RootElement;
                if (!HasExactFields(root, RootFields())) return false;
                if (root.GetProperty("schemaVersion").GetInt32() != 1) return false;
                string id = root.GetProperty("batchId").GetString();
                if (!Guid.TryParseExact(id, "D", out _)) return false;
                JsonElement reportsElement = root.GetProperty("reports");
                if (reportsElement.ValueKind != JsonValueKind.Array || reportsElement.GetArrayLength() is < 1 or > 20) return false;
                var reports = new List<DiagnosticReport>(reportsElement.GetArrayLength());
                foreach (JsonElement element in reportsElement.EnumerateArray())
                {
                    if (!TryReport(element, out DiagnosticReport report)) return false;
                    reports.Add(report);
                }
                batch = new DiagnosticBatch { BatchId = id.ToLowerInvariant(), Reports = reports };
                return true;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException)
            {
                return false;
            }
        }

        private static bool TryReport(JsonElement element, out DiagnosticReport report)
        {
            report = null;
            if (!HasExactFields(element, ReportFields())) return false;
            string source = element.GetProperty("source").GetString();
            string kind = element.GetProperty("kind").GetString();
            string fingerprint = element.GetProperty("fingerprint").GetString();
            string version = element.GetProperty("buildVersion").GetString();
            string platform = element.GetProperty("platform").GetString();
            string day = element.GetProperty("eventDate").GetString();
            string firstText = element.GetProperty("firstUtc").GetString();
            string lastText = element.GetProperty("lastUtc").GetString();
            string bootCode = element.GetProperty("bootCode").GetString();
            string bootStage = element.GetProperty("bootStage").GetString();
            int count = element.GetProperty("count").GetInt32();

            if (fingerprint?.Length != 64 || !AllLowerHex(fingerprint) ||
                !SafeVersion(version) || !Platforms().Contains(platform ?? "") ||
                count is < 1 or > 10000 ||
                !DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !DateTimeOffset.TryParse(firstText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset first) ||
                !DateTimeOffset.TryParse(lastText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset last) ||
                first.Offset != TimeSpan.Zero || last.Offset != TimeSpan.Zero || first > last ||
                first.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != day ||
                last.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != day)
                return false;

            if (source == "Boot")
            {
                if (kind != "BootFailure" || !BootCodes().Contains(bootCode ?? "") || !BootStages().Contains(bootStage ?? "")) return false;
            }
            else if (source == "Runtime")
            {
                if (kind is not ("Error" or "Exception" or "Assert") || bootCode != "" || bootStage != "") return false;
            }
            else return false;

            report = new DiagnosticReport
            {
                Source = source, Kind = kind, Fingerprint = fingerprint, BuildVersion = version,
                Platform = platform, EventDate = day, FirstUtc = first, LastUtc = last,
                Count = count, BootCode = bootCode, BootStage = bootStage
            };
            return true;
        }

        private static bool HasExactFields(JsonElement element, HashSet<string> expected)
        {
            if (element.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!expected.Contains(property.Name) || !seen.Add(property.Name)) return false;
            }
            return seen.Count == expected.Count;
        }

        private static bool AllLowerHex(string value)
        {
            foreach (char character in value)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static bool SafeVersion(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
            foreach (char character in value)
                if (!(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '-')) return false;
            return true;
        }
    }
}

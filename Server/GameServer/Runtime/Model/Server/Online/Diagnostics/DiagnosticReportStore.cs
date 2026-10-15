using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ET.Server
{
    /// <summary>一条匿名统计；仅保存固定枚举、时间、版本和哈希，不接收原始日志。</summary>
    [EnableClass]
    public sealed class DiagnosticReport
    {
        public string Source { get; set; }
        public string Kind { get; set; }
        public string Fingerprint { get; set; }
        public string BuildVersion { get; set; }
        public string Platform { get; set; }
        public string EventDate { get; set; }
        public DateTimeOffset FirstUtc { get; set; }
        public DateTimeOffset LastUtc { get; set; }
        public int Count { get; set; }
        public string BootCode { get; set; }
        public string BootStage { get; set; }
    }

    /// <summary>客户端的不可变上传快照；BatchId 与内容哈希共同定义幂等身份。</summary>
    [EnableClass]
    public sealed class DiagnosticBatch
    {
        public string BatchId { get; set; }
        public List<DiagnosticReport> Reports { get; set; }

        public string PayloadHash()
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(Reports);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }

    [EnableClass]
    internal sealed class DiagnosticStoredBatch
    {
        public string BatchId { get; set; }
        public string PayloadHash { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
        public List<DiagnosticReport> Reports { get; set; }
    }

    /// <summary>
    /// 诊断运行数据的唯一写入权威。持久确认先于 HTTP 成功响应；与账号、房间、世界存储完全隔离。
    /// </summary>
    [EnableClass]
    public sealed class DiagnosticReportStore
    {
        private const long MaxDailyBytes = 32L * 1024 * 1024;
        private const int MaxDailyBatches = 10000;
        private const int MaxRateEntries = 4096;
        [StaticField]
        private static readonly Lazy<DiagnosticReportStore> shared = new(() => new DiagnosticReportStore(Options.Instance.DiagnosticStoreRoot));
        private readonly object gate = new();
        private readonly Dictionary<string, string> knownBatches = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> batchDays = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RateEntry> rateEntries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> dailyCounts = new(StringComparer.Ordinal);
        private readonly string root;
        private bool loaded;
        private bool unavailable;

        [EnableClass]
        private sealed class RateEntry
        {
            public DateTimeOffset WindowStartUtc;
            public DateTimeOffset LastUtc;
            public int Count;
        }

        /// <summary>空覆盖定位 Bin 的上一级 Store；测试实例必须显式提供隔离目录。</summary>
        public DiagnosticReportStore(string rootOverride = null)
        {
            root = string.IsNullOrWhiteSpace(rootOverride)
                ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Store", "Diagnostics"))
                : Path.GetFullPath(rootOverride);
        }

        public string Root => root;

        public static DiagnosticReportStore Shared => shared.Value;

        /// <summary>返回 HTTP 状态；重复确认优先于限流，失败绝不伪称已经落盘。</summary>
        public int Accept(DiagnosticBatch batch, IPAddress remoteIp, string realIpHeader, string trustedProxyIp, DateTimeOffset now)
        {
            lock (gate)
            {
                if (!EnsureLoaded(now)) return 503;
                if (!PruneOldDays(now)) return 503;
                string payloadHash = batch.PayloadHash();
                if (knownBatches.TryGetValue(batch.BatchId, out string oldHash))
                    return string.Equals(oldHash, payloadHash, StringComparison.Ordinal) ? 200 : 409;

                if (!TrySourceIp(remoteIp, realIpHeader, trustedProxyIp, out string sourceIp)) return 400;
                if (!AllowRate(sourceIp, now)) return 429;

                string day = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                int count = dailyCounts.GetValueOrDefault(day);
                if (count >= MaxDailyBatches) return 503;
                string path = Path.Combine(root, day + ".jsonl");
                var stored = new DiagnosticStoredBatch
                {
                    BatchId = batch.BatchId,
                    PayloadHash = payloadHash,
                    ReceivedUtc = now.ToUniversalTime(),
                    Reports = batch.Reports
                };
                byte[] line = JsonSerializer.SerializeToUtf8Bytes(stored);
                if (line.Length + 1 > MaxDailyBytes) return 503;
                try
                {
                    using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                    if (stream.Length + line.Length + 1 > MaxDailyBytes) return 503;
                    stream.Position = stream.Length;
                    stream.Write(line);
                    stream.WriteByte((byte)'\n');
                    stream.Flush(true);
                    knownBatches.Add(batch.BatchId, payloadHash);
                    batchDays.Add(batch.BatchId, day);
                    dailyCounts[day] = count + 1;
                    return 200;
                }
                catch
                {
                    // 可能已有部分字节落盘；拒绝后续新批次，重启恢复时只修复尾行。
                    unavailable = true;
                    return 503;
                }
            }
        }

        private bool EnsureLoaded(DateTimeOffset now)
        {
            if (unavailable) return false;
            if (loaded) return true;
            try
            {
                Directory.CreateDirectory(root);
                DateTime firstRetained = now.UtcDateTime.Date.AddDays(-29);
                foreach (string path in Directory.EnumerateFiles(root, "*.jsonl").OrderBy(p => p, StringComparer.Ordinal))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (!DateTime.TryParseExact(name, "yyyyMMdd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime day)) continue;
                    if (day < firstRetained)
                    {
                        File.Delete(path);
                        continue;
                    }
                    if (!LoadFile(path, name)) throw new InvalidDataException("Diagnostic JSONL is invalid.");
                }
                loaded = true;
                return true;
            }
            catch
            {
                unavailable = true;
                return false;
            }
        }

        /// <summary>跨 UTC 日期持续运行时也回收旧文件和去重身份，始终只保留最近 30 天。</summary>
        private bool PruneOldDays(DateTimeOffset now)
        {
            string oldest = now.UtcDateTime.Date.AddDays(-29).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            try
            {
                foreach (string day in dailyCounts.Keys.Where(day => string.CompareOrdinal(day, oldest) < 0).ToArray())
                {
                    string path = Path.Combine(root, day + ".jsonl");
                    if (File.Exists(path)) File.Delete(path);
                    dailyCounts.Remove(day);
                }
                foreach (string id in batchDays.Where(kv => string.CompareOrdinal(kv.Value, oldest) < 0)
                             .Select(kv => kv.Key).ToArray())
                {
                    batchDays.Remove(id);
                    knownBatches.Remove(id);
                }
                return true;
            }
            catch
            {
                unavailable = true;
                return false;
            }
        }

        private bool LoadFile(string path, string day)
        {
            if (new FileInfo(path).Length > MaxDailyBytes) return false;
            byte[] data = File.ReadAllBytes(path);
            int lastNewline = Array.LastIndexOf(data, (byte)'\n');
            if (lastNewline < data.Length - 1)
            {
                // 仅丢弃未完成的最后一行；持久修复后才恢复写入。
                using var repair = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                repair.SetLength(lastNewline + 1);
                repair.Flush(true);
            }
            int start = 0;
            int count = 0;
            for (int index = 0; index <= lastNewline; index++)
            {
                if (data[index] != (byte)'\n') continue;
                if (index == start) return false;
                DiagnosticStoredBatch stored;
                try { stored = JsonSerializer.Deserialize<DiagnosticStoredBatch>(data.AsSpan(start, index - start)); }
                catch { return false; }
                if (stored?.Reports == null || stored.Reports.Count is < 1 or > 20 ||
                    !Guid.TryParseExact(stored.BatchId, "D", out _) ||
                    stored.ReceivedUtc.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) != day ||
                    stored.PayloadHash?.Length != 64 ||
                    stored.Reports.Any(report => !ValidRecoveredReport(report))) return false;
                string hash = new DiagnosticBatch { Reports = stored.Reports }.PayloadHash();
                if (!string.Equals(hash, stored.PayloadHash, StringComparison.Ordinal)) return false;
                if (knownBatches.TryGetValue(stored.BatchId, out string previous) && previous != hash) return false;
                knownBatches[stored.BatchId] = hash;
                batchDays[stored.BatchId] = day;
                count++;
                start = index + 1;
            }
            dailyCounts[day] = count;
            return true;
        }

        /// <summary>重启恢复时完整行也须重新验证；损坏的已确认行不能进入去重索引。</summary>
        private static bool ValidRecoveredReport(DiagnosticReport report)
        {
            if (report == null || report.Count is < 1 or > 10000 || report.Fingerprint?.Length != 64 ||
                string.IsNullOrWhiteSpace(report.BuildVersion) || report.BuildVersion.Length > 64 ||
                string.IsNullOrWhiteSpace(report.Platform) ||
                !DateTime.TryParseExact(report.EventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out _) ||
                report.FirstUtc.Offset != TimeSpan.Zero || report.LastUtc.Offset != TimeSpan.Zero ||
                report.FirstUtc > report.LastUtc ||
                report.FirstUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != report.EventDate ||
                report.LastUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != report.EventDate)
                return false;
            foreach (char c in report.Fingerprint)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            if (report.Source == "Runtime")
                return report.Kind is "Error" or "Exception" or "Assert" &&
                    report.BootCode == "" && report.BootStage == "";
            return report.Source == "Boot" && report.Kind == "BootFailure" &&
                !string.IsNullOrEmpty(report.BootCode) && !string.IsNullOrEmpty(report.BootStage);
        }

        private static bool TrySourceIp(IPAddress remote, string header, string trustedProxyIp, out string source)
        {
            source = null;
            if (remote == null) return false;
            IPAddress normalizedRemote = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
            if (!string.IsNullOrWhiteSpace(trustedProxyIp) && !IPAddress.TryParse(trustedProxyIp, out _)) return false;
            if (!string.IsNullOrWhiteSpace(trustedProxyIp) && IPAddress.TryParse(trustedProxyIp, out IPAddress trusted) &&
                normalizedRemote.Equals(trusted.IsIPv4MappedToIPv6 ? trusted.MapToIPv4() : trusted))
            {
                if (string.IsNullOrWhiteSpace(header) || header.Contains(',') || !IPAddress.TryParse(header.Trim(), out IPAddress real))
                    return false;
                source = (real.IsIPv4MappedToIPv6 ? real.MapToIPv4() : real).ToString();
                return true;
            }
            source = normalizedRemote.ToString();
            return true;
        }

        private bool AllowRate(string source, DateTimeOffset now)
        {
            foreach (string expired in rateEntries.Where(kv => now - kv.Value.LastUtc > TimeSpan.FromMinutes(2))
                         .Select(kv => kv.Key).ToArray()) rateEntries.Remove(expired);
            if (!rateEntries.TryGetValue(source, out RateEntry entry))
            {
                if (rateEntries.Count >= MaxRateEntries) return false;
                rateEntries.Add(source, new RateEntry { WindowStartUtc = now, LastUtc = now, Count = 1 });
                return true;
            }
            entry.LastUtc = now;
            if (now - entry.WindowStartUtc >= TimeSpan.FromMinutes(1))
            {
                entry.WindowStartUtc = now;
                entry.Count = 1;
                return true;
            }
            if (entry.Count >= 60) return false;
            entry.Count++;
            return true;
        }
    }
}

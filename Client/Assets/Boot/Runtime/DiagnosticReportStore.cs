using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Boot
{
    [Serializable]
    public sealed class DiagnosticReport
    {
        public string source;
        public string kind;
        public string fingerprint;
        public string buildVersion;
        public string platform;
        public string eventDate;
        public string firstUtc;
        public string lastUtc;
        public int count;
        public string bootCode;
        public string bootStage;
    }

    [Serializable]
    public sealed class DiagnosticBatch
    {
        public int schemaVersion = 1;
        public string batchId;
        public List<DiagnosticReport> reports = new List<DiagnosticReport>();
    }

    [Serializable]
    internal sealed class DiagnosticQueueFile
    {
        public List<DiagnosticReport> reports = new List<DiagnosticReport>();
        public bool hasPending;
        public DiagnosticBatch pending;
    }

    /// <summary>
    /// Boot 与 Game 共用的匿名统计权威；只保存受限字段，不持有原始日志或网络状态。
    /// </summary>
    public sealed class DiagnosticReportStore
    {
        private const int MaxGroups = 128;
        private const int MaxBytes = 256 * 1024;
        private readonly object gate = new object();
        private readonly string path;
        private DiagnosticQueueFile state;
        private int generation;

        public DiagnosticReportStore(string path)
        {
            this.path = path;
            state = Load(path);
            lock (gate) { Expire(DateTime.UtcNow); }
        }

        public int Generation { get { lock (gate) return generation; } }
        public int PendingGroups { get { lock (gate) return state.reports.Count; } }

        public static string Fingerprint(string source, string kind, string location)
        {
            byte[] data = Encoding.UTF8.GetBytes(source + "\n" + kind + "\n" + (location ?? string.Empty));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty).ToLowerInvariant();
        }

        /// <summary>同一天同一身份合并次数；写入失败只丢弃本次新增，不影响游戏调用者。</summary>
        public bool TryAdd(string source, string kind, string fingerprint, string version, string platform,
            string bootCode = "", string bootStage = "", DateTime? utc = null)
        {
            DateTime now = (utc ?? DateTime.UtcNow).ToUniversalTime();
            string day = now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            string timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture);
            lock (gate)
            {
                Expire(now);
                DiagnosticQueueFile next = Clone(state);
                DiagnosticReport current = next.reports.FirstOrDefault(r => r.eventDate == day &&
                    r.source == source && r.kind == kind && r.fingerprint == fingerprint &&
                    r.buildVersion == version && r.platform == platform);
                if (current == null)
                {
                    if (next.reports.Count >= MaxGroups)
                    {
                        var protectedKeys = new HashSet<string>((next.hasPending ? next.pending.reports : new List<DiagnosticReport>()).Select(Key));
                        DiagnosticReport oldest = next.reports.OrderBy(r => r.firstUtc)
                            .FirstOrDefault(r => !protectedKeys.Contains(Key(r)));
                        if (oldest == null) return false;
                        next.reports.Remove(oldest);
                    }
                    next.reports.Add(new DiagnosticReport
                    {
                        source = source, kind = kind, fingerprint = fingerprint, buildVersion = version,
                        platform = platform, eventDate = day, firstUtc = timestamp, lastUtc = timestamp,
                        count = 1, bootCode = bootCode, bootStage = bootStage
                    });
                }
                else
                {
                    // 第一条批次后新事件重置剩余部分的起始时间，确认旧批次时无需猜测。
                    DiagnosticReport inFlight = next.hasPending
                        ? next.pending.reports.FirstOrDefault(r => Key(r) == Key(current)) : null;
                    if (inFlight != null && current.count == inFlight.count) current.firstUtc = timestamp;
                    current.count = current.count == int.MaxValue ? int.MaxValue : current.count + 1;
                    current.lastUtc = timestamp;
                }
                return TryCommit(next);
            }
        }

        /// <summary>快照先持久化再发送；同一批次重试始终复用 ID 与完整内容。</summary>
        public DiagnosticBatch GetOrCreateBatch()
        {
            lock (gate)
            {
                Expire(DateTime.UtcNow);
                if (state.hasPending) return Clone(state.pending);
                var next = Clone(state);
                var batch = new DiagnosticBatch { batchId = Guid.NewGuid().ToString("D") };
                foreach (DiagnosticReport item in next.reports.OrderBy(r => r.firstUtc).Take(20))
                {
                    var copy = Clone(item);
                    copy.count = Math.Min(copy.count, 10000);
                    batch.reports.Add(copy);
                    if (Encoding.UTF8.GetByteCount(JsonUtility.ToJson(batch)) > 16 * 1024)
                    {
                        batch.reports.RemoveAt(batch.reports.Count - 1);
                        break;
                    }
                }
                if (batch.reports.Count == 0) return null;
                next.pending = batch;
                next.hasPending = true;
                return TryCommit(next) ? Clone(batch) : null;
            }
        }

        /// <summary>仅当前有效批次可扣除已确认次数，保留批次形成后新增的同指纹事件。</summary>
        public bool Confirm(string batchId, int expectedGeneration)
        {
            lock (gate)
            {
                if (expectedGeneration != generation || !state.hasPending || state.pending.batchId != batchId) return false;
                var next = Clone(state);
                foreach (DiagnosticReport sent in next.pending.reports)
                {
                    DiagnosticReport live = next.reports.FirstOrDefault(r => Key(r) == Key(sent));
                    if (live == null) continue;
                    live.count -= sent.count;
                    if (live.count <= 0) next.reports.Remove(live);
                }
                next.pending = null;
                next.hasPending = false;
                return TryCommit(next);
            }
        }

        /// <summary>删除成功才递增清理代次；旧网络回调因此不能确认清理后的新记录。</summary>
        public bool Clear()
        {
            lock (gate)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    state = new DiagnosticQueueFile();
                    generation++;
                    return true;
                }
                catch { return false; }
            }
        }

        private void Expire(DateTime now)
        {
            var next = Clone(state);
            string oldestDay = now.Date.AddDays(-6).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            next.reports.RemoveAll(r => string.CompareOrdinal(r.eventDate, oldestDay) < 0);
            if (next.hasPending && next.pending.reports.Any(r => string.CompareOrdinal(r.eventDate, oldestDay) < 0))
            {
                next.pending = null;
                next.hasPending = false;
            }
            if (next.reports.Count != state.reports.Count || (state.hasPending && !next.hasPending)) TryCommit(next);
        }

        private bool TryCommit(DiagnosticQueueFile next)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(next));
                if (bytes.Length > MaxBytes || next.reports.Count > MaxGroups) return false;
                string directory = Path.GetDirectoryName(path);
                Directory.CreateDirectory(directory);
                string temporary = path + ".tmp";
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                state = next;
                return true;
            }
            catch { return false; }
        }

        private static DiagnosticQueueFile Load(string file)
        {
            try
            {
                if (!File.Exists(file)) return new DiagnosticQueueFile();
                byte[] bytes = File.ReadAllBytes(file);
                if (bytes.Length > MaxBytes) throw new InvalidDataException();
                var loaded = JsonUtility.FromJson<DiagnosticQueueFile>(Encoding.UTF8.GetString(bytes));
                if (loaded?.reports == null || loaded.reports.Count > MaxGroups ||
                    (loaded.hasPending && (loaded.pending?.reports == null || string.IsNullOrEmpty(loaded.pending.batchId))))
                    throw new InvalidDataException();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (DiagnosticReport report in loaded.reports)
                    if (!Valid(report) || !keys.Add(Key(report))) throw new InvalidDataException();
                if (loaded.hasPending)
                {
                    if (!Guid.TryParse(loaded.pending.batchId, out _) || loaded.pending.schemaVersion != 1 ||
                        loaded.pending.reports.Count is < 1 or > 20) throw new InvalidDataException();
                    foreach (DiagnosticReport sent in loaded.pending.reports)
                    {
                        if (!Valid(sent) || sent.count > 10000 ||
                            !loaded.reports.Any(live => Key(live) == Key(sent) && live.count >= sent.count))
                            throw new InvalidDataException();
                    }
                }
                return loaded;
            }
            catch
            {
                try { File.Move(file, file + ".corrupt-" + Guid.NewGuid().ToString("N")); } catch { }
                return new DiagnosticQueueFile();
            }
        }

        private static bool Valid(DiagnosticReport r)
        {
            if (r == null || r.count <= 0 || r.fingerprint?.Length != 64 ||
                string.IsNullOrEmpty(r.buildVersion) || r.buildVersion.Length > 64 ||
                string.IsNullOrEmpty(r.platform) || r.platform.Length > 32 ||
                !DateTime.TryParseExact(r.eventDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _) ||
                !DateTimeOffset.TryParse(r.firstUtc, out DateTimeOffset first) ||
                !DateTimeOffset.TryParse(r.lastUtc, out DateTimeOffset last) ||
                first.Offset != TimeSpan.Zero || last.Offset != TimeSpan.Zero || first > last ||
                first.UtcDateTime.ToString("yyyy-MM-dd") != r.eventDate ||
                last.UtcDateTime.ToString("yyyy-MM-dd") != r.eventDate) return false;
            foreach (char c in r.fingerprint)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return r.source == "Boot" ? r.kind == "BootFailure" &&
                Enum.TryParse(r.bootCode, out BootFailureCode _) && Enum.TryParse(r.bootStage, out BootFailureStage _) :
                r.source == "Runtime" && (r.kind == "Error" || r.kind == "Exception" || r.kind == "Assert") &&
                r.bootCode == "" && r.bootStage == "";
        }

        private static string Key(DiagnosticReport r) => r.eventDate + "|" + r.source + "|" + r.kind + "|" +
            r.fingerprint + "|" + r.buildVersion + "|" + r.platform;
        private static DiagnosticQueueFile Clone(DiagnosticQueueFile item) =>
            JsonUtility.FromJson<DiagnosticQueueFile>(JsonUtility.ToJson(item));
        private static DiagnosticBatch Clone(DiagnosticBatch item) =>
            JsonUtility.FromJson<DiagnosticBatch>(JsonUtility.ToJson(item));
        private static DiagnosticReport Clone(DiagnosticReport item) =>
            JsonUtility.FromJson<DiagnosticReport>(JsonUtility.ToJson(item));
    }
}

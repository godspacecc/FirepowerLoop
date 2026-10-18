#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace Game.Editor
{
    /// <summary>构建前备份限定生成输入；中断标记阻止复用，显式恢复保留被替换现状，不回退已消耗版本台账。</summary>
    public static class CiWorkspaceRecovery
    {
        private static readonly string[] OwnedPaths =
        {
            "Assets/Game/Dlls", "Assets/Game/Dlls.meta", "Assets/HybridCLRGenerate", "Assets/HybridCLRGenerate.meta",
            "Assets/StreamingAssets/yoo/DefaultPackage", "Assets/StreamingAssets/yoo/DefaultPackage.meta",
            "Assets/StreamingAssets/windows-release.json", "Assets/StreamingAssets/windows-release.json.meta",
            "ProjectSettings/ProjectSettings.asset"
        };
        [Serializable] private sealed class Journal
        {
            public int SchemaVersion = 1, ProcessId;
            public string ProjectId = "FirepowerLoop", Client, RequestId, Commit, Status = "Active", SnapshotDirectory, ManifestHash, Reason;
            public long ProcessStartedUtcTicks;
        }
        [Serializable] private sealed class Slot { public int Index; public bool Exists, Directory; }
        [Serializable] private sealed class SavedFile { public string Path, Hash; public long Size; }
        [Serializable] private sealed class Snapshot { public List<Slot> Slots = new List<Slot>(); public List<SavedFile> Files = new List<SavedFile>(); }

        /// <summary>按工作区绝对路径隔离持久恢复材料；Unity退出清理Temp不能清除唯一备份。</summary>
        public static string RecoveryRoot(string client)
        {
            string normalized = Path.GetFullPath(client).TrimEnd('\\', '/').ToLowerInvariant();
            using (var hash = SHA256.Create())
            {
                string key = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(normalized))).Replace("-", "").ToLowerInvariant().Substring(0, 16);
                return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(client).TrimEnd('\\', '/')), "Release", "CiWorkspaceRecovery", key);
            }
        }
        public static string JournalPath(string client) => Path.Combine(RecoveryRoot(client), "active.json");
        public static bool RequiresRecovery(string client) { WorkspaceBuildLock.EnsureNoLinks(JournalPath(client)); return File.Exists(JournalPath(client)); }
        public static void EnsureClean(string client)
        {
            if (RequiresRecovery(client)) throw new InvalidOperationException("工作区有未正常结束的CI，请先按原RequestId执行显式恢复：" + JournalPath(client));
        }
        public static string Begin(string client, string requestId, string commit)
        {
            using (WorkspaceBuildLock.Acquire(client))
            {
                EnsureClean(client);
                string directory = Path.Combine(RecoveryRoot(client), "snapshots", Guid.NewGuid().ToString("N"));
                WorkspaceBuildLock.EnsureNoLinks(directory); Directory.CreateDirectory(Path.Combine(directory, "backup"));
                var snapshot = new Snapshot();
                for (int i = 0; i < OwnedPaths.Length; i++)
                {
                    string source = Owned(client, i); bool exists = File.Exists(source) || Directory.Exists(source);
                    snapshot.Slots.Add(new Slot { Index = i, Exists = exists, Directory = Directory.Exists(source) });
                    if (exists) Copy(source, Path.Combine(directory, "backup", i.ToString()));
                }
                snapshot.Files = Files(Path.Combine(directory, "backup")).Select(file => new SavedFile
                { Path = Relative(directory, file), Hash = Hash(file), Size = new FileInfo(file).Length }).ToList();
                string manifest = Path.Combine(directory, "snapshot.json"); WriteJson(manifest, snapshot);
                using (var process = Process.GetCurrentProcess()) WriteJson(JournalPath(client), new Journal
                {
                    Client = Path.GetFullPath(client), RequestId = requestId, Commit = commit, SnapshotDirectory = directory,
                    ManifestHash = Hash(manifest), ProcessId = process.Id, ProcessStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks
                });
                return directory;
            }
        }
        public static void MarkInterrupted(string client, string requestId, string reason)
        {
            using (WorkspaceBuildLock.Acquire(client))
            {
                var record = Read(client, requestId);
                if (record.ProcessId != Process.GetCurrentProcess().Id && OwnerAlive(record)) throw new InvalidOperationException("不能修改仍存活的其他CI标记");
                record.Status = "Interrupted"; record.Reason = reason; WriteJson(JournalPath(client), record);
            }
        }
        internal static void Complete(string client, string requestId)
        {
            var record = Read(client, requestId);
            WriteJson(Path.Combine(record.SnapshotDirectory, "completed.json"), new { RequestId = requestId, Status = "Completed" });
            File.Delete(JournalPath(client));
        }

        /// <summary>先验证所有备份并暂存恢复候选，持锁替换；失败逆序退回，成功保留现状隔离目录。</summary>
        public static string Restore(string client, string requestId, string currentCommit)
        {
            using (WorkspaceBuildLock.Acquire(client))
            {
                var record = Read(client, requestId);
                if (!string.Equals(record.Commit, currentCommit, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("当前Commit已改变，拒绝恢复另一提交的生成输入");
                if (record.Status == "Active" && OwnerAlive(record)) throw new InvalidOperationException("原CI进程仍活跃，禁止恢复");
                var snapshot = Validate(record);
                string transaction = Path.Combine(record.SnapshotDirectory, "restore-" + Guid.NewGuid().ToString("N"));
                string staging = Path.Combine(transaction, "prepared"), quarantine = Path.Combine(transaction, "displaced");
                Directory.CreateDirectory(staging); Directory.CreateDirectory(quarantine);
                foreach (var slot in snapshot.Slots) if (slot.Exists) Copy(Path.Combine(record.SnapshotDirectory, "backup", slot.Index.ToString()), Path.Combine(staging, slot.Index.ToString()));
                var applied = new List<int>();
                try
                {
                    foreach (var slot in snapshot.Slots)
                    {
                        string target = Owned(client, slot.Index), previous = Path.Combine(quarantine, slot.Index.ToString());
                        if (File.Exists(target) || Directory.Exists(target)) Move(target, previous);
                        // 原现状成功隔离后才记入回退清单，取文件失败不能移动尚未改动的目标。
                        applied.Add(slot.Index);
                        if (slot.Exists) { Directory.CreateDirectory(Path.GetDirectoryName(target)); Move(Path.Combine(staging, slot.Index.ToString()), target); }
                    }
                }
                catch (Exception original)
                {
                    try
                    {
                        foreach (int index in applied.AsEnumerable().Reverse())
                        {
                            string target = Owned(client, index), previous = Path.Combine(quarantine, index.ToString());
                            string installed = Path.Combine(staging, "rollback-" + index);
                            if (File.Exists(target) || Directory.Exists(target)) Move(target, installed);
                            if (File.Exists(previous) || Directory.Exists(previous)) Move(previous, target);
                        }
                    }
                    catch (Exception rollback) { throw new AggregateException("恢复失败且回退失败；保留进行中标记与所有隔离文件", original, rollback); }
                    throw;
                }
                string result = JsonConvert.SerializeObject(new { Status = "Recovered", RequestId = requestId, QuarantineDirectory = quarantine }, Formatting.Indented);
                File.WriteAllText(Path.Combine(transaction, "result.json"), result);
                // 仅证明本次显式恢复保留的精确台账内容；节点另对Git基线核验身份与单调序号。
                string ledger = Path.Combine(client, "Assets/Editor/Build/windows-release-version.json");
                WorkspaceBuildLock.EnsureNoLinks(ledger);
                if (File.Exists(ledger)) WriteJson(Path.Combine(RecoveryRoot(client), "retry.json"), new
                {
                    SchemaVersion = 1, ProjectId = "FirepowerLoop", Client = Path.GetFullPath(client), Commit = currentCommit,
                    RequestId = requestId, LedgerPath = "Client/Assets/Editor/Build/windows-release-version.json", LedgerHash = Hash(ledger)
                });
                File.Delete(JournalPath(client)); return result;
            }
        }

        private static Journal Read(string client, string requestId)
        {
            string path = JournalPath(client); WorkspaceBuildLock.EnsureNoLinks(path);
            var record = JsonConvert.DeserializeObject<Journal>(File.ReadAllText(path));
            string expectedParent = Path.Combine(RecoveryRoot(client), "snapshots");
            if (record == null || record.SchemaVersion != 1 || record.ProjectId != "FirepowerLoop" || record.RequestId != requestId || !string.Equals(record.Client, Path.GetFullPath(client), StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(record.SnapshotDirectory) || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(record.SnapshotDirectory)), expectedParent, StringComparison.OrdinalIgnoreCase) ||
                (record.Status != "Active" && record.Status != "Interrupted")) throw new InvalidOperationException("CI恢复身份或路径不符");
            WorkspaceBuildLock.EnsureNoLinks(record.SnapshotDirectory); return record;
        }
        private static Snapshot Validate(Journal record)
        {
            string manifest = Path.Combine(record.SnapshotDirectory, "snapshot.json"); WorkspaceBuildLock.EnsureNoLinks(manifest);
            if (Hash(manifest) != record.ManifestHash) throw new InvalidOperationException("CI快照清单被改动");
            var value = JsonConvert.DeserializeObject<Snapshot>(File.ReadAllText(manifest));
            if (value?.Slots == null || value.Files == null || value.Slots.Count != OwnedPaths.Length || !value.Slots.Select(slot => slot.Index).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, OwnedPaths.Length))) throw new InvalidOperationException("快照范围无效");
            var actual = Files(Path.Combine(record.SnapshotDirectory, "backup")).ToList();
            if (value.Files.Count != actual.Count || value.Files.Select(file => file.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Count) throw new InvalidOperationException("快照文件增减或重复");
            var paths = new HashSet<string>(actual, StringComparer.OrdinalIgnoreCase);
            foreach (var file in value.Files)
            {
                string path = HostBaselineArchive.Resolve(record.SnapshotDirectory, file.Path);
                if (!paths.Contains(path) || new FileInfo(path).Length != file.Size || Hash(path) != file.Hash) throw new InvalidOperationException("快照文件损坏：" + file.Path);
            }
            foreach (var slot in value.Slots)
            {
                string path = Path.Combine(record.SnapshotDirectory, "backup", slot.Index.ToString());
                if (slot.Exists != (File.Exists(path) || Directory.Exists(path)) || slot.Exists && slot.Directory != Directory.Exists(path)) throw new InvalidOperationException("快照目录类型不符");
            }
            return value;
        }
        private static bool OwnerAlive(Journal record)
        {
            try { using (var process = Process.GetProcessById(record.ProcessId)) return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == record.ProcessStartedUtcTicks; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        private static string Owned(string client, int index)
        {
            if (index < 0 || index >= OwnedPaths.Length) throw new InvalidOperationException("恢复范围越界");
            string path = Path.GetFullPath(Path.Combine(client, OwnedPaths[index]));
            if (!path.StartsWith(Path.GetFullPath(client).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("恢复路径越界");
            WorkspaceBuildLock.EnsureNoLinks(path); return path;
        }
        private static void Copy(string source, string destination)
        {
            WorkspaceBuildLock.EnsureNoLinks(source); WorkspaceBuildLock.EnsureNoLinks(destination);
            if (File.Exists(source)) { Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(source, destination); }
            else { Directory.CreateDirectory(destination); foreach (var path in Directory.GetFileSystemEntries(source)) Copy(path, Path.Combine(destination, Path.GetFileName(path))); }
        }
        private static void Move(string source, string destination) { WorkspaceBuildLock.EnsureNoLinks(source); WorkspaceBuildLock.EnsureNoLinks(destination); if (Directory.Exists(source)) Directory.Move(source, destination); else File.Move(source, destination); }
        private static IEnumerable<string> Files(string root) { WorkspaceBuildLock.EnsureNoLinks(root); foreach (string file in Directory.GetFiles(root)) { WorkspaceBuildLock.EnsureNoLinks(file); yield return file; } foreach (string child in Directory.GetDirectories(root)) foreach (var file in Files(child)) yield return file; }
        private static string Relative(string root, string path) => path.Substring(root.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
        private static string Hash(string file) { using (var hash = SHA256.Create()) using (var stream = File.OpenRead(file)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static void WriteJson(string path, object value)
        {
            WorkspaceBuildLock.EnsureNoLinks(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try { File.WriteAllText(pending, JsonConvert.SerializeObject(value, Formatting.Indented)); if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path); }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
#endif

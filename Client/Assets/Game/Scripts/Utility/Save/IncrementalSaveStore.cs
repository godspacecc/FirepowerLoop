using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game
{
    /// <summary>拥有初始快照及追加事务文件；不访问 Unity/业务模型，完整记录才推进提交基准。</summary>
    internal sealed class IncrementalSaveStore
    {
        private const uint Begin = 0x31445341, End = 0x444e4541;
        /// <summary>单条事务载荷的字节上限；不限制整个游戏的变化记录数量。</summary>
        private const int MaxPayloadBytes = 16 * 1024 * 1024;
        private readonly string directory;
        private readonly Dictionary<string, State> states = new Dictionary<string, State>(StringComparer.Ordinal);
        /// <summary>测试故障点；只消费一次，不影响正式保存的业务状态。</summary>
        internal string FailNextAppendStageForTests;
        private sealed class State
        {
            internal JObject Manifest, Mutable;
            internal ValleyLayoutSaveData Layout;
            /// <summary>Sequence 从首条事务的1开始；Boundary 是已提交日志结尾的字节偏移，初始均为0。</summary>
            internal long Sequence, Boundary;
            internal string LayoutHash;
            internal HashSet<string> PointIds, ClueIds;
            internal readonly Dictionary<long, string> Transactions = new Dictionary<long, string>();
        }
        internal IncrementalSaveStore(string directory) { this.directory = directory; }
        internal string PathFor(string domain) => Path.Combine(directory, domain + ".json");
        private string Named(string name)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("存档文件身份无效");
            return Path.Combine(directory, name);
        }
        private static string Hash(byte[] bytes) { using var sha = SHA256.Create(); return Convert.ToBase64String(sha.ComputeHash(bytes)); }
        private static void WriteDurable(string path, byte[] bytes)
        { using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None); stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }

        /// <summary>首次创建发布完整快照；同身份后续只写差异。普通 Flush 不允许隐式新建存档。</summary>
        internal bool Save<T>(string domain, T data, Action<T> validate, bool allowCreate, out string error)
        {
            error = null; State state = null;
            try
            {
                Directory.CreateDirectory(directory);
                states.TryGetValue(domain, out state);
                if (File.Exists(PathFor(domain)) || File.Exists(PathFor(domain) + ".backup"))
                {
                    var current = ReadManifest(domain, out _);
                    if (domain != "base" && (string)current["OwnerBase"] != BaseGeneration()) state = null;
                    else if (state == null || (string)current["Generation"] != (string)state.Manifest["Generation"]) state = Load(domain, out _);
                }
                else state = null;
                var identity = data is ExpeditionSaveData expedition ? expedition.ExpeditionId : state?.Manifest["Identity"]?.Value<string>() ?? Guid.NewGuid().ToString("N");
                if (state == null || state.Manifest["Identity"]?.Value<string>() != identity)
                {
                    if (!allowCreate) throw new InvalidDataException("测试存档格式已更新，请新建游戏");
                    validate(data); Publish(domain, identity, data); return true;
                }
                // 另一实例追加过记录时重新读取，禁止用过期基准覆盖已提交状态。
                var journal = Named((string)state.Manifest["Journal"]);
                if (new FileInfo(journal).Length != state.Boundary) { state = Load(domain, out _); states[domain] = state; }
                validate(data);
                if (data is ExpeditionSaveData candidate) ValidateLayout(state, candidate, allowCreate);
                var mutable = IncrementalSaveCodec.Capture(data);
                var changes = IncrementalSaveCodec.Diff(state.Mutable, mutable);
                if (changes.Count == 0) return true;
                var record = new JObject { ["Generation"] = state.Manifest["Generation"], ["Domain"] = domain,
                    ["Sequence"] = state.Sequence + 1, ["Transaction"] = Guid.NewGuid().ToString("N"), ["Changes"] = changes };
                var bytes = Encoding.UTF8.GetBytes(record.ToString(Formatting.None));
                if (bytes.Length > MaxPayloadBytes) throw new InvalidDataException("单次变化记录过大");
                var transaction = (string)record["Transaction"];
                try
                {
                    using var stream = new FileStream(journal, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                    stream.SetLength(state.Boundary); stream.Position = state.Boundary;
                    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                    writer.Write(Begin); writer.Write(bytes.Length); writer.Write(~bytes.Length); writer.Write(bytes);
                    Inject("payload");
                    writer.Write(Convert.FromBase64String(Hash(bytes))); writer.Write(End); writer.Flush(); stream.Flush(true);
                    Inject("committed");
                    state.Boundary = stream.Position;
                }
                catch
                {
                    // 原子记录可能已完成；先核对实际结果，不能把成功采集报告为失败并回滚。
                    try
                    {
                        var observed = Load(domain, out _);
                        if (observed.Transactions.TryGetValue(state.Sequence + 1, out var id) && id == transaction)
                        { states[domain] = observed; return true; }
                        using var repair = new FileStream(journal, FileMode.Open, FileAccess.Write, FileShare.None);
                        repair.SetLength(state.Boundary); repair.Flush(true);
                    }
                    catch { states.Remove(domain); throw; }
                    throw;
                }
                state.Mutable = IncrementalSaveCodec.Apply(state.Mutable, changes);
                state.Sequence++; state.Transactions[state.Sequence] = transaction; states[domain] = state;
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }
        private void Inject(string stage)
        { if (FailNextAppendStageForTests == stage) { FailNextAppendStageForTests = null; throw new IOException("测试存档写入失败：" + stage); } }

        /// <summary>完整布局仅在显式 DTO 保存时核对；普通变化通过缓存 ID 校验探索身份，不扫描地形。</summary>
        private static void ValidateLayout(State state, ExpeditionSaveData candidate, bool explicitDto)
        {
            if (explicitDto)
            {
                if ((state.Layout == null) != (candidate.ValleyLayout == null) || state.Layout != null &&
                    (state.LayoutHash ??= HashLayout(state.Layout)) != HashLayout(candidate.ValleyLayout))
                    throw new InvalidDataException("初始布局不可修改，请创建新远征");
            }
            if (state.Layout == null) return;
            if (candidate.MapId != state.Layout.MapId) throw new InvalidDataException("远征布局身份不一致");
            state.PointIds ??= new HashSet<string>(state.Layout.Points.Select(p => p.Id), StringComparer.Ordinal);
            state.ClueIds ??= new HashSet<string>(state.Layout.Clues.Select(p => p.Id), StringComparer.Ordinal);
            candidate.Sections.TryGetValue(CivilisationExpeditionModel.Section, out var exploration);
            if (!new CivilisationExpeditionModel().Validate(exploration, out var error)) throw new InvalidDataException(error);
            if (exploration == null) return;
            bool Matches(string key, Func<string, bool> exists) => exploration[key] == null || exploration[key].Values<string>().All(exists);
            if (!Matches("visited", state.PointIds.Contains) || !Matches("clues", state.ClueIds.Contains) || !Matches("shortcuts", id => id == state.Layout.ShortcutId))
                throw new InvalidDataException("探索记录与初始布局身份不匹配");
        }
        private static string HashLayout(ValleyLayoutSaveData layout)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            void Position(ValleyPosition p) { writer.Write(p != null); if (p == null) return; writer.Write(p.X); writer.Write(p.Y); writer.Write(p.Z); writer.Write(p.Yaw); }
            void Path(List<ValleyPosition> path) { writer.Write(path.Count); foreach (var p in path) Position(p); }
            writer.Write(layout.GenerationVersion); writer.Write(layout.Seed); writer.Write(layout.AttemptIndex); writer.Write(layout.MapId);
            writer.Write(layout.WorldSizeMeters); writer.Write(layout.HeightSampleSpacingMeters); writer.Write(layout.NominalRouteMeters);
            foreach (var height in layout.Heights) writer.Write(height);
            Path(layout.Route); Path(layout.AlternateRoute); Path(layout.River); Path(layout.Fords); Path(layout.Shortcut);
            writer.Write(layout.Points.Count); foreach (var p in layout.Points) { writer.Write(p.Id); writer.Write(p.TemplateId); writer.Write(p.RegionId); Position(p.Pose); }
            writer.Write(layout.Clues.Count); foreach (var c in layout.Clues) { writer.Write(c.Id); writer.Write(c.Chain); writer.Write(c.Order); writer.Write(c.MessageKey); Position(c.Pose); Position(c.Target); }
            writer.Write(layout.Edges.Count); foreach (var e in layout.Edges) { writer.Write(e.Id); writer.Write(e.From); writer.Write(e.To); writer.Write(e.SafeRiverRoute); }
            Position(layout.Spawn); Position(layout.Camp); writer.Write(layout.ShortcutId); writer.Flush(); return Hash(stream.ToArray());
        }

        /// <summary>只由明确的新建游戏入口调用；发布新 generation 后旧远征身份失效。</summary>
        internal bool CreateBase(BaseSaveData data, Action<BaseSaveData> validate, out string error)
        {
            error = null;
            try { validate(data); Publish("base", Guid.NewGuid().ToString("N"), data); states.Remove("offline-expedition"); return true; }
            catch (Exception e) { error = e.Message; return false; }
        }
        private void Publish<T>(string domain, string identity, T data)
        {
            Directory.CreateDirectory(directory);
            var generation = Guid.NewGuid().ToString("N");
            var prefix = domain + "." + generation;
            var mutable = IncrementalSaveCodec.Capture(data);
            var layout = (data as ExpeditionSaveData)?.ValleyLayout?.Clone();
            var snapshot = new JObject { ["Format"] = 1, ["Generation"] = generation, ["Domain"] = domain,
                ["State"] = mutable, ["Layout"] = layout == null ? JValue.CreateNull() : JToken.FromObject(layout) };
            var bytes = Encoding.UTF8.GetBytes(snapshot.ToString(Formatting.None));
            var manifest = new JObject { ["Format"] = 1, ["Generation"] = generation, ["Domain"] = domain, ["Identity"] = identity,
                ["Snapshot"] = prefix + ".snapshot.json", ["Journal"] = prefix + ".journal", ["SnapshotHash"] = Hash(bytes),
                ["OwnerBase"] = domain == "base" ? generation : BaseGeneration() };
            WriteDurable(Named((string)manifest["Snapshot"]), bytes);
            WriteDurable(Named((string)manifest["Journal"]), Array.Empty<byte>());
            var temp = PathFor(domain) + ".tmp";
            WriteDurable(temp, Encoding.UTF8.GetBytes(manifest.ToString(Formatting.None)));
            if (File.Exists(PathFor(domain))) File.Replace(temp, PathFor(domain), PathFor(domain) + ".backup", true);
            else File.Move(temp, PathFor(domain));
            states[domain] = new State { Manifest = manifest, Mutable = (JObject)mutable.DeepClone(), Layout = layout };
        }
        private string BaseGeneration()
        {
            // 基地入口恢复使用同一备份规则，避免可读基地被误判成远征已失去归属。
            if (!File.Exists(PathFor("base")) && !File.Exists(PathFor("base") + ".backup")) return null;
            return (string)ReadManifest("base", out _)["Generation"];
        }
        private JObject ReadManifest(string domain, out bool backup)
        {
            backup = false;
            JObject Read(string path)
            {
                var m = JObject.Parse(File.ReadAllText(path));
                if ((int?)m["Format"] != 1 || (string)m["Domain"] != domain || !Guid.TryParseExact((string)m["Generation"], "N", out _) || !Guid.TryParseExact((string)m["Identity"], "N", out _))
                    throw new InvalidDataException("存档入口版本或身份无效");
                return m;
            }
            try { return Read(PathFor(domain)); }
            catch when (File.Exists(PathFor(domain) + ".backup")) { backup = true; return Read(PathFor(domain) + ".backup"); }
        }
        private State Load(string domain, out bool backup)
        {
            var manifest = ReadManifest(domain, out backup);
            if (domain != "base" && (string)manifest["OwnerBase"] != BaseGeneration()) throw new InvalidDataException("远征不属于当前基地存档");
            var bytes = File.ReadAllBytes(Named((string)manifest["Snapshot"]));
            if (Hash(bytes) != (string)manifest["SnapshotHash"]) throw new InvalidDataException("初始快照校验失败");
            var snapshot = JObject.Parse(Encoding.UTF8.GetString(bytes));
            if ((int?)snapshot["Format"] != 1 || !JToken.DeepEquals(snapshot["Generation"], manifest["Generation"]) || (string)snapshot["Domain"] != domain)
                throw new InvalidDataException("初始快照身份无效");
            var state = new State { Manifest = manifest, Mutable = snapshot["State"] as JObject ?? throw new InvalidDataException("快照状态缺失"),
                Layout = snapshot["Layout"]?.Type == JTokenType.Null ? null : snapshot["Layout"]?.ToObject<ValleyLayoutSaveData>() };
            using var stream = new FileStream(Named((string)manifest["Journal"]), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            var seen = new Dictionary<long, string>();
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 12) break;
                if (reader.ReadUInt32() != Begin) throw new InvalidDataException("变化日志标记损坏");
                var length = reader.ReadInt32();
                // 反码保护长度字段，完整记录的头损坏不能伪装成可丢弃的半写尾部。
                if (reader.ReadInt32() != ~length || length <= 0 || length > MaxPayloadBytes) throw new InvalidDataException("变化日志长度无效");
                if (stream.Length - stream.Position < length + 36L) break;
                var payload = reader.ReadBytes(length); var hash = Convert.ToBase64String(reader.ReadBytes(32));
                if (reader.ReadUInt32() != End || Hash(payload) != hash) throw new InvalidDataException("完整变化记录校验失败");
                var record = JObject.Parse(Encoding.UTF8.GetString(payload));
                if ((string)record["Generation"] != (string)manifest["Generation"] || (string)record["Domain"] != domain || !Guid.TryParseExact((string)record["Transaction"], "N", out _))
                    throw new InvalidDataException("变化事务身份无效");
                var sequence = (long)record["Sequence"];
                if (seen.TryGetValue(sequence, out var prior))
                { if (prior != hash) throw new InvalidDataException("重复序号内容冲突"); }
                else
                {
                    if (sequence != state.Sequence + 1) throw new InvalidDataException("变化事务序号不连续");
                    state.Mutable = IncrementalSaveCodec.Apply(state.Mutable, record["Changes"] as JArray ?? throw new InvalidDataException("变化列表缺失"));
                    state.Sequence = sequence; seen[sequence] = hash; state.Transactions[sequence] = (string)record["Transaction"];
                }
                state.Boundary = stream.Position;
            }
            return state;
        }
        internal SaveReadResult<T> Read<T>(string domain, Action<T> validate)
        {
            var result = new SaveReadResult<T>();
            if (!File.Exists(PathFor(domain)) && !File.Exists(PathFor(domain) + ".backup")) { result.Status = SaveFileStatus.Missing; return result; }
            try
            {
                // 新游戏保留的旧远征文件不是新游戏的待处理候选。
                var manifest = ReadManifest(domain, out _);
                if (domain != "base" && (string)manifest["OwnerBase"] != BaseGeneration()) { result.Status = SaveFileStatus.Missing; return result; }
                var state = Load(domain, out var backup);
                var data = IncrementalSaveCodec.Restore<T>(state.Mutable);
                if (data is ExpeditionSaveData expedition) expedition.ValleyLayout = state.Layout?.Clone();
                validate(data); states[domain] = state;
                result.Status = SaveFileStatus.Valid; result.Data = data; result.FromBackup = backup;
                if (new FileInfo(Named((string)state.Manifest["Journal"])).Length != state.Boundary) result.Error = "已恢复最后完整事务；末尾未完成的变化未提交";
            }
            catch (Exception e) { result.Status = SaveFileStatus.Corrupted; result.Error = e.Message; states.Remove(domain); }
            return result;
        }
        internal void DeleteExpedition()
        {
            // 文件名由本格式拥有；旧固定文件以及目录外路径不会被处理。
            if (Directory.Exists(directory)) foreach (var path in Directory.EnumerateFiles(directory, "offline-expedition.*", SearchOption.TopDirectoryOnly)) File.Delete(path);
            states.Remove("offline-expedition");
        }
    }
}

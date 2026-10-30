using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Game
{
    /// <summary>仅包含已结束幸存者局的统计；不是可恢复的运行局。</summary>
    public sealed class SurvivorResultData
    {
        /// <summary>结算唯一编号，同一局的重试必须复用。</summary>
        [JsonProperty(Required = Required.Always)] public long RunId;
        /// <summary>最终分数。</summary>
        [JsonProperty(Required = Required.Always)] public long Score;
        /// <summary>实际存活秒数，不含暂停。</summary>
        [JsonProperty(Required = Required.Always)] public double SurvivalSeconds;
        /// <summary>本局击杀数。</summary>
        [JsonProperty(Required = Required.Always)] public int Kills;
        /// <summary>结算等级。</summary>
        [JsonProperty(Required = Required.Always)] public int Level;
        /// <summary>阵亡 failed 或主动退出 abandoned。</summary>
        [JsonProperty(Required = Required.Always)] public string Outcome;
    }

    /// <summary>独立原子结果存储；历史回执防止旧局重试覆盖新局，不接触闯关文件。</summary>
    public sealed class SurvivorResultFileStore
    {
        /// <summary>串行化进程内实例的读取与提交，防止相同路径竞争。</summary>
        private static readonly object Gate = new object();
        /// <summary>当前玩家的独立结果文件。</summary>
        private readonly string path;
        /// <summary>带版本的结算回执集合；末项是最新结果。</summary>
        private sealed class Document
        {
            /// <summary>当前文件格式版本。</summary>
            [JsonProperty(Required = Required.Always)] public int Version = 1;
            /// <summary>已成功提交的结果，顺序等于首次提交顺序。</summary>
            [JsonProperty(Required = Required.Always)] public List<SurvivorResultData> Results = new List<SurvivorResultData>();
        }

        /// <summary>默认使用本应用目录，测试可传入独立临时根目录。</summary>
        public SurvivorResultFileStore(long playerId, string root = null)
        {
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            path = Path.Combine(root ?? Application.persistentDataPath, "Saves", "survivor-result-" + playerId + ".json");
        }

        /// <summary>返回最新结果；文件不存在成功返回空，损坏则明确失败。</summary>
        public bool TryRead(out SurvivorResultData result, out string error)
        {
            lock (Gate)
            {
                result = null; error = null;
                try
                {
                    var document = ReadDocument();
                    if (document.Results.Count > 0) result = document.Results[document.Results.Count - 1];
                    return true;
                }
                catch (Exception exception) { error = exception.Message; return false; }
            }
        }

        /// <summary>同局相同内容可安全重试；冲突、坏文件或提交失败保留旧文件。</summary>
        public bool TrySave(SurvivorResultData result, out string error)
        {
            lock (Gate)
            {
                error = null;
                string temporary = null;
                try
                {
                    if (!IsValid(result)) throw new InvalidDataException("幸存者结果内容无效");
                    // 固化输入，后续提交不保留调用方持有的可变对象。
                    var snapshot = JsonConvert.DeserializeObject<SurvivorResultData>(JsonConvert.SerializeObject(result));
                    if (!IsValid(snapshot)) throw new InvalidDataException("幸存者结果内容无效");
                    var document = ReadDocument();
                    var previous = document.Results.FirstOrDefault(item => item.RunId == snapshot.RunId);
                    if (previous != null)
                    {
                        if (previous.Score != snapshot.Score || previous.SurvivalSeconds != snapshot.SurvivalSeconds ||
                            previous.Kills != snapshot.Kills || previous.Level != snapshot.Level || previous.Outcome != snapshot.Outcome)
                            throw new InvalidDataException("相同幸存者局的结果发生冲突");
                        return true;
                    }
                    document.Results.Add(snapshot);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(document));
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(true);
                    }
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                    return true;
                }
                catch (Exception exception) { error = exception.Message; return false; }
                finally
                {
                    // 临时文件清理失败不改变已提交结果或原始错误。
                    try { if (temporary != null && File.Exists(temporary)) File.Delete(temporary); } catch { }
                }
            }
        }

        /// <summary>读取完整回执并验证版本、必填字段和局编号唯一性。</summary>
        private Document ReadDocument()
        {
            if (!File.Exists(path)) return new Document();
            var document = JsonConvert.DeserializeObject<Document>(File.ReadAllText(path));
            if (document == null || document.Version != 1 || document.Results == null || document.Results.Count == 0 ||
                document.Results.Any(item => !IsValid(item)) ||
                document.Results.Select(item => item.RunId).Distinct().Count() != document.Results.Count)
                throw new InvalidDataException("幸存者结果文件无效");
            return document;
        }

        /// <summary>排除负数、非有限时间以及未结束或未知结果。</summary>
        private static bool IsValid(SurvivorResultData result) => result != null && result.RunId > 0 &&
            result.Score >= 0 && result.Kills >= 0 && result.Level >= 1 && result.SurvivalSeconds >= 0 &&
            !double.IsNaN(result.SurvivalSeconds) && !double.IsInfinity(result.SurvivalSeconds) &&
            (result.Outcome == "failed" || result.Outcome == "abandoned");
    }
}

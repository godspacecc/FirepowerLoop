using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Game
{
    /// <summary>仅保存已结束双人局的只读结果回执；不承担运行局恢复或局外奖励职责。</summary>
    public sealed class OnlineArcadeResultData
    {
        public long RunId;
        public string Outcome;
        public Dictionary<string, long> Scores = new Dictionary<string, long>();
    }

    /// <summary>按玩家身份原子保存双人结果；目录属于本应用，既不扫描旧项目也不读取基地文件。</summary>
    public sealed class OnlineArcadeResultFileStore
    {
        private readonly string path;
        public OnlineArcadeResultFileStore(long playerId, string root = null)
        {
            if (playerId <= 0) throw new ArgumentOutOfRangeException(nameof(playerId));
            path = Path.Combine(root ?? Application.persistentDataPath, "Saves", "online-result-" + playerId + ".json");
        }

        public bool TryRead(out OnlineArcadeResultData result, out string error)
        {
            result = null; error = null;
            try
            {
                if (!File.Exists(path)) return true;
                result = JsonConvert.DeserializeObject<OnlineArcadeResultData>(File.ReadAllText(path));
                if (!IsValid(result))
                    throw new InvalidDataException("联机结果文件无效");
                return true;
            }
            catch (Exception e) { result = null; error = e.Message; return false; }
        }

        public bool TrySave(OnlineArcadeResultData result, out string error)
        {
            error = null;
            try
            {
                if (!IsValid(result)) throw new InvalidDataException("联机结果内容无效");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path + ".tmp", JsonConvert.SerializeObject(result));
                if (File.Exists(path)) File.Replace(path + ".tmp", path, null);
                else File.Move(path + ".tmp", path);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private static bool IsValid(OnlineArcadeResultData result) => result != null && result.RunId > 0 &&
            result.Scores != null && result.Scores.Count > 0 && result.Scores.All(pair =>
                long.TryParse(pair.Key, out var id) && id > 0 && pair.Value >= 0) &&
            (result.Outcome == "victory" || result.Outcome == "failed" || result.Outcome == "abandoned");

        public bool TryAcknowledge(out string error)
        {
            error = null;
            try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); if (File.Exists(path)) File.Delete(path); return true; }
            catch (Exception e) { error = e.Message; return false; }
        }
    }
}

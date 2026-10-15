using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>持有永久文明科技、成功带回的阅读记录和最近一次返程结果；不负责搜索、结算或生产事务。</summary>
    public sealed class CivilisationTechnologyModel : AbstractModel, IScopedSaveSectionProvider
    {
        public const string Section = "another-day.civilisation-technology";
        private JObject state = Empty();
        public string SectionId => Section;
        public SaveSectionScope Scope => SaveSectionScope.BaseOnly;
        public bool IsUnlocked(string id) => state["unlocked"].Values<string>().Contains(id);
        /// <summary>成功返程归档的正文键副本；不包含尚未带回的当次发现。</summary>
        public string[] Records => state["records"].Values<string>().ToArray();
        /// <summary>成功返程后永久保存的章节完成资格，与科技全收集无关。</summary>
        public bool IsChapterCompleted(string id) => state["completedChapters"].Values<string>().Contains(id);
        /// <summary>返回副本，展示层不能修改存档中的奖励结果。</summary>
        public JObject Receipt => state["receipt"] as JObject == null ? null : (JObject)state["receipt"].DeepClone();
        protected override void OnInit() { }
        public static JObject Empty() => new JObject { ["records"] = new JArray(), ["schemaVersion"] = 1, ["unlocked"] = new JArray(), ["completedChapters"] = new JArray() };
        public bool Validate(JToken data, out string error)
        {
            error = null;
            // 新分区的唯一兼容路径：旧存档没有该分区时迁移为空。
            if (data == null) return true;
            if (!(data is JObject) || data["schemaVersion"]?.Type != JTokenType.Integer || data["schemaVersion"].ToString() != "1" ||
                !ValidIds(data["unlocked"]) || (data["records"] != null && !ValidIds(data["records"])) || (data["completedChapters"] != null && !ValidIds(data["completedChapters"])) || (data["receipt"] != null && !(data["receipt"] is JObject)))
            { error = "文明科技存档格式或版本无效"; return false; }
            return true;
        }
        internal static bool ValidIds(JToken data)
        {
            if (!(data is JArray array)) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            return array.All(t => t.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)t) && ids.Add((string)t));
        }
        public JToken Capture() => state.DeepClone();
        public void Restore(JToken data)
        {
            if (!Validate(data, out var error)) throw new InvalidOperationException(error);
            state = data == null ? Empty() : (JObject)data.DeepClone();
            if (state["completedChapters"] == null) state["completedChapters"] = new JArray();
            if (state["records"] == null) state["records"] = new JArray();
        }
        public void Reset() => state = Empty();
    }
}

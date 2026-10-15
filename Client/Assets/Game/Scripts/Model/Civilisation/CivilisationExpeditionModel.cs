using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>完整采集状态已恢复或重置后的通知；不用于计时、任务进度或单点采集。</summary>
    public readonly struct CivilisationHarvestStateRestoredEvent { }
    /// <summary>持有当次工艺样本、调查点、阅读与免费准备记录；永久解锁及阅读归档只归基地科技分区。</summary>
    public sealed class CivilisationExpeditionModel : AbstractModel, IScopedSaveSectionProvider
    {
        public const string Section = "another-day.civilisation-expedition";
        private JObject state = Empty();
        public string SectionId => Section;
        public SaveSectionScope Scope => SaveSectionScope.ExpeditionOnly;
        public bool HasVisited(string id) => state["visited"].Values<string>().Contains(id);
        public bool HasTechnology(string id) => state["technologies"].Values<string>().Contains(id);
        /// <summary>当局已取得的剧情凭证；只有成功返程结算后才成为永久章节进度。</summary>
        public bool HasChapterEvidence(string id) => state["chapters"].Values<string>().Contains(id);
        public bool PreparationClaimed => (bool)state["preparationClaimed"];
        public bool HasReadClue(string id)=>state["clues"].Values<string>().Contains(id);
        /// <summary>当次发现的正文键副本；正文按当前语言展示，不保存已翻译文本。</summary>
        public string[] Records => state["records"].Values<string>().ToArray();
        /// <summary>仅在发现事务中调用，按正文身份去重；外层保存失败时随快照回滚。</summary>
        internal void RecordReading(string key)
        {
            if (!state["records"].Values<string>().Contains(key)) ((JArray)state["records"]).Add(key);
        }
        public bool HasShortcut(string id)=>state["shortcuts"].Values<string>().Contains(id);
        internal void ReadClue(string id){if(!HasReadClue(id))((JArray)state["clues"]).Add(id);}
        internal void DiscoverShortcut(string id){if(!HasShortcut(id))((JArray)state["shortcuts"]).Add(id);}
        /// <summary>免费份额上限，跨交换仍扣除相同数量的同类食物，阻止准备兑换套利。</summary>
        public int FreeFoodRemaining => (int)state["freeFoodRemaining"];
        protected override void OnInit() { }
        public static JObject Empty() => new JObject { ["records"] = new JArray(), ["schemaVersion"] = 2, ["technologies"] = new JArray(), ["chapters"] = new JArray(), ["visited"] = new JArray(), ["preparationClaimed"] = false, ["freeFoodRemaining"] = 0,["clues"]=new JArray(),["shortcuts"]=new JArray() };
        public bool Validate(JToken data, out string error)
        {
            error = null;
            if (data == null) return true;
            if (!(data is JObject) || data["schemaVersion"]?.Type != JTokenType.Integer || (data["schemaVersion"].ToString() != "1"&&data["schemaVersion"].ToString() != "2") ||
                !CivilisationTechnologyModel.ValidIds(data["technologies"]) ||
                (data["records"] != null && !CivilisationTechnologyModel.ValidIds(data["records"])) ||
                (data["chapters"] != null && !CivilisationTechnologyModel.ValidIds(data["chapters"])) ||
                !CivilisationTechnologyModel.ValidIds(data["visited"]) || data["preparationClaimed"]?.Type != JTokenType.Boolean ||
                data["freeFoodRemaining"]?.Type != JTokenType.Integer || (data["freeFoodRemaining"].ToString() != "0" && data["freeFoodRemaining"].ToString() != "1"))
            { error = "当次文明探索存档无效"; return false; }
            if(data["schemaVersion"].ToString()=="2"&&(!CivilisationTechnologyModel.ValidIds(data["clues"])||!CivilisationTechnologyModel.ValidIds(data["shortcuts"])||data["clues"].Count()>32||data["shortcuts"].Count()>1))
            {error="文明线索或捷径存档无效";return false;}
            return true;
        }
        /// <summary>搜索事务暂存访问、科技与剧情成果；调用者负责统一保存和失败回滚。</summary>
        internal void Visit(string id, string technology, string chapterId = null)
        {
            if (!HasVisited(id)) ((JArray)state["visited"]).Add(id);
            if (!string.IsNullOrEmpty(technology) && !HasTechnology(technology)) ((JArray)state["technologies"]).Add(technology);
            if (!string.IsNullOrEmpty(chapterId) && !HasChapterEvidence(chapterId)) ((JArray)state["chapters"]).Add(chapterId);
        }
        internal void ClaimPreparation() { state["preparationClaimed"] = true; state["freeFoodRemaining"] = 1; }
        public JToken Capture() => state.DeepClone();
        public void Restore(JToken data)
        {
            if (!Validate(data, out var error)) throw new InvalidOperationException(error);
            state = data == null ? Empty() : (JObject)data.DeepClone();
            // 旧局没有剧情成果，补空集合而不重生成旧地图或授予完成资格。
            if (state["chapters"] == null) state["chapters"] = new JArray();
            if (state["records"] == null) state["records"] = new JArray();
            if(state["schemaVersion"].ToString()=="1")
            {state["schemaVersion"]=2;state["clues"]=new JArray();state["shortcuts"]=new JArray();}
            this.SendEvent(new CivilisationHarvestStateRestoredEvent());
        }
        public void Reset()
        { state = Empty(); this.SendEvent(new CivilisationHarvestStateRestoredEvent()); }
    }
}

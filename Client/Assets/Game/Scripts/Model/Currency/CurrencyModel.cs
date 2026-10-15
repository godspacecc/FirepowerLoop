using System;
using Newtonsoft.Json.Linq;
using QFramework;

namespace Game
{
    /// <summary>持有基地专属金币，并以严格的一版 JSON 分区参与离线存档。</summary>
    public sealed class CurrencyModel : AbstractModel, IScopedSaveSectionProvider
    {
        public const string SaveSectionId = "another-day.currency";
        private const int SchemaVersion = 1;

        public string SectionId => SaveSectionId;
        public SaveSectionScope Scope => SaveSectionScope.BaseOnly;
        public long Gold { get; private set; }

        protected override void OnInit()
        {
        }

        public bool Validate(JToken data, out string error)
        {
            if (data == null || data.Type != JTokenType.Object)
            {
                error = "金币分区必须为对象";
                return false;
            }

            var version = data["schemaVersion"];
            if (version?.Type != JTokenType.Integer || !TryReadInt64(version, out var schemaVersion) || schemaVersion != SchemaVersion)
            {
                error = "金币分区 schemaVersion 必须为 1";
                return false;
            }

            var gold = data["gold"];
            if (gold?.Type != JTokenType.Integer || !TryReadInt64(gold, out var value) || value < 0)
            {
                error = "金币分区 gold 必须为非负 Int64 整数";
                return false;
            }

            error = null;
            return true;
        }

        public JToken Capture()
        {
            return new JObject
            {
                ["schemaVersion"] = SchemaVersion,
                ["gold"] = Gold
            };
        }

        public void Restore(JToken data)
        {
            // SaveSystem 已先统一校验；这里不补缺失值，避免损坏存档被默认金币掩盖。
            Gold = data["gold"].Value<long>();
        }

        public void Reset()
        {
            Gold = 0;
        }

        /// <summary>仅供商店事务提交已受检的非负金币，不保存也不发布通知。</summary>
        internal bool TrySetGold(long value)
        {
            if (value < 0) return false;
            Gold = value;
            return true;
        }

        /// <summary>恢复商店自行捕获的合法旧值；该窄入口确定性替换内存且不抛异常。</summary>
        internal void RestoreGold(long value)
        {
            Gold = value < 0 ? 0 : value;
        }

        private static bool TryReadInt64(JToken token, out long value)
        {
            try
            {
                value = token.Value<long>();
                return true;
            }
            catch (Exception)
            {
                value = 0;
                return false;
            }
        }
    }
}

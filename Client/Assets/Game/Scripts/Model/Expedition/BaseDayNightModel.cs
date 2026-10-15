using System;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 当前基地存档的昼夜时刻权威，只保存一天内分钟。
    /// 不负责远征计时、基地恢复结算或离线时间补算。
    /// </summary>
    public sealed class BaseDayNightModel : AbstractModel, IScopedSaveSectionProvider
    {
        public const string SaveSectionId = "another-day.base-day-night";
        public string SectionId => SaveSectionId;
        public SaveSectionScope Scope => SaveSectionScope.BaseOnly;
        /// <summary>游戏分钟，范围 [0, 1440)；新游戏及缺少本分区的旧档从正午开始。</summary>
        public float MinuteOfDay { get; private set; } = PlayerConditionRules.GameMinutesPerDay * 0.5f;

        protected override void OnInit() { }

        /// <summary>仅由昼夜 System 在可推进的基地运行态调用；跨午夜取模，不累计日数。</summary>
        internal void Advance(float minutes)
        {
            MinuteOfDay = Mathf.Repeat(MinuteOfDay + minutes, PlayerConditionRules.GameMinutesPerDay);
        }

        public JToken Capture() => new JObject { ["schemaVersion"] = 1, ["minuteOfDay"] = MinuteOfDay };

        public bool Validate(JToken data, out string error)
        {
            error = null;
            if (data == null || data.Type == JTokenType.Null) return true;
            try
            {
                if (data is not JObject obj || obj["schemaVersion"]?.Type != JTokenType.Integer ||
                    obj["schemaVersion"].Value<int>() != 1 ||
                    obj["minuteOfDay"]?.Type is not (JTokenType.Integer or JTokenType.Float))
                    throw new FormatException();
                var minute = obj["minuteOfDay"].Value<float>();
                if (float.IsNaN(minute) || float.IsInfinity(minute) || minute < 0f ||
                    minute >= PlayerConditionRules.GameMinutesPerDay) throw new FormatException();
                return true;
            }
            catch
            {
                error = "基地昼夜时刻分区无效";
                return false;
            }
        }

        /// <summary>读档或事务回滚时精确恢复；缺失分区按旧档兼容处理，不按现实时间推进。</summary>
        public void Restore(JToken data)
        {
            if (!Validate(data, out var error)) throw new InvalidOperationException(error);
            MinuteOfDay = data == null || data.Type == JTokenType.Null
                ? PlayerConditionRules.GameMinutesPerDay * 0.5f : data["minuteOfDay"].Value<float>();
        }

        public void Reset() => Restore(null);
    }
}

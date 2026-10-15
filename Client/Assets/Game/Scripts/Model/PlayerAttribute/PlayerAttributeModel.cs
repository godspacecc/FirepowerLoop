using System;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class PlayerAttributeModel : AbstractModel, IScopedSaveSectionProvider
    {
        public SaveSectionScope Scope => SaveSectionScope.SharedPlayer;
        private const string MaxHPKey = "maxHP";
        private const string MoveSpeedKey = "moveSpeed";

        public const string Section = "another-day.player-attribute";

        public string SectionId => Section;
        public int? MaxHPOverride { get; private set; }
        public float? MoveSpeedOverride { get; private set; }

        protected override void OnInit()
        {
        }

        public bool SetMaxHPOverride(int value)
        {
            if (MaxHPOverride.HasValue && MaxHPOverride.Value == value)
            {
                return false;
            }

            MaxHPOverride = value;
            return true;
        }

        public bool SetMoveSpeedOverride(float value)
        {
            if (MoveSpeedOverride.HasValue && Mathf.Approximately(MoveSpeedOverride.Value, value))
            {
                return false;
            }

            MoveSpeedOverride = value;
            return true;
        }

        public JToken Capture()
        {
            return new JObject
            {
                [MaxHPKey] = MaxHPOverride.HasValue ? new JValue(MaxHPOverride.Value) : JValue.CreateNull(),
                [MoveSpeedKey] = MoveSpeedOverride.HasValue ? new JValue(MoveSpeedOverride.Value) : JValue.CreateNull()
            };
        }

        public bool Validate(JToken data, out string error)
        {
            error = null;
            if (data == null)
            {
                return true;
            }

            if (data.Type != JTokenType.Object)
            {
                error = "玩家属性分区必须为对象";
                return false;
            }

            if (!ValidateNullablePositiveInt(data[MaxHPKey], MaxHPKey, out error))
            {
                return false;
            }

            return ValidateNullablePositiveFloat(data[MoveSpeedKey], MoveSpeedKey, out error);
        }

        public void Restore(JToken data)
        {
            if (!Validate(data, out var error))
            {
                throw new InvalidOperationException(error);
            }

            MaxHPOverride = ReadNullableInt(data?[MaxHPKey]);
            MoveSpeedOverride = ReadNullableFloat(data?[MoveSpeedKey]);
        }

        public void Reset()
        {
            MaxHPOverride = null;
            MoveSpeedOverride = null;
        }

        private static int? ReadNullableInt(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? null : token.Value<int>();
        }

        private static float? ReadNullableFloat(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ? null : token.Value<float>();
        }

        private static bool ValidateNullablePositiveInt(JToken token, string field, out string error)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                error = null;
                return true;
            }

            if (token.Type != JTokenType.Integer || token.Value<int>() <= 0)
            {
                error = $"{field} 必须为正整数或 null";
                return false;
            }

            error = null;
            return true;
        }

        private static bool ValidateNullablePositiveFloat(JToken token, string field, out string error)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                error = null;
                return true;
            }

            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                error = $"{field} 必须为正数或 null";
                return false;
            }

            var value = token.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                error = $"{field} 必须为正数或 null";
                return false;
            }

            error = null;
            return true;
        }
    }
}

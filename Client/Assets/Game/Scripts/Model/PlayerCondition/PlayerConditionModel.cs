using System;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class PlayerConditionModel : AbstractModel, IScopedSaveSectionProvider
    {
        public SaveSectionScope Scope => SaveSectionScope.SharedPlayer;
        private const string PowerKey = "power";
        private const string TiliKey = "tili";
        private const string WaterKey = "water";
        private const string MaxWaterKey = "maxWater";
        private const string TemperatureKey = "temperature";

        public string SectionId => "another-day.player-condition";
        public float Power { get; private set; } = PlayerConditionRules.MaxPower;
        public float MaxPower => PlayerConditionRules.MaxPower;
        public float Tili { get; private set; } = PlayerConditionRules.MaxTili;
        public float MaxTili => PlayerConditionRules.MaxTili;
        public float Water { get; private set; } = PlayerConditionRules.DefaultMaxWater;
        public float MaxWater { get; private set; } = PlayerConditionRules.DefaultMaxWater;
        public float Temperature { get; private set; } = PlayerConditionRules.NormalTemperature;
        public bool SprintReleaseRequired { get; private set; }
        internal int RuntimeResetVersion { get; private set; }

        protected override void OnInit()
        {
        }

        public JToken Capture()
        {
            return new JObject
            {
                [PowerKey] = Power,
                [TiliKey] = Tili,
                [WaterKey] = Water,
                [MaxWaterKey] = MaxWater,
                [TemperatureKey] = Temperature
            };
        }

        public bool Validate(JToken data, out string error)
        {
            if (data == null)
            {
                error = null;
                return true;
            }

            if (data.Type != JTokenType.Object)
            {
                error = "玩家状态分区必须为对象";
                return false;
            }

            if (!ValidateFiniteRange(data[PowerKey], 0f, PlayerConditionRules.MaxPower, "power", out error))
            {
                return false;
            }

            if (!ValidateFiniteRange(data[TiliKey], 0f, PlayerConditionRules.MaxTili, "tili", out error))
            {
                return false;
            }

            if (!ValidateFinitePositive(data[MaxWaterKey], "maxWater", out error))
            {
                return false;
            }

            var maxWater = data[MaxWaterKey].Value<float>();
            if (!ValidateFiniteRange(data[WaterKey], 0f, maxWater, "water", out error))
            {
                return false;
            }

            if (!ValidateFiniteRange(
                    data[TemperatureKey],
                    PlayerConditionRules.MinTemperature,
                    PlayerConditionRules.MaxTemperature,
                    "temperature",
                    out error))
            {
                return false;
            }

            error = null;
            return true;
        }

        public void Restore(JToken data)
        {
            if (!Validate(data, out var error))
            {
                throw new InvalidOperationException(error);
            }

            Power = data == null
                ? PlayerConditionRules.MaxPower
                : data[PowerKey].Value<float>();
            Tili = data == null
                ? PlayerConditionRules.MaxTili
                : data[TiliKey].Value<float>();
            MaxWater = data == null
                ? PlayerConditionRules.DefaultMaxWater
                : data[MaxWaterKey].Value<float>();
            Water = data == null
                ? PlayerConditionRules.DefaultMaxWater
                : data[WaterKey].Value<float>();
            Temperature = data == null
                ? PlayerConditionRules.NormalTemperature
                : data[TemperatureKey].Value<float>();
            SprintReleaseRequired = false;
            RuntimeResetVersion++;
        }

        public void Reset()
        {
            Power = PlayerConditionRules.MaxPower;
            Tili = PlayerConditionRules.MaxTili;
            MaxWater = PlayerConditionRules.DefaultMaxWater;
            Water = PlayerConditionRules.DefaultMaxWater;
            Temperature = PlayerConditionRules.NormalTemperature;
            SprintReleaseRequired = false;
            RuntimeResetVersion++;
        }

        internal bool SetPower(float value)
        {
            var next = Mathf.Clamp(value, 0f, PlayerConditionRules.MaxPower);
            if (Mathf.Approximately(Power, next))
            {
                return false;
            }

            Power = next;
            return true;
        }

        internal bool SetTili(float value)
        {
            var next = Mathf.Clamp(value, 0f, PlayerConditionRules.MaxTili);
            if (Mathf.Approximately(Tili, next))
            {
                return false;
            }

            Tili = next;
            return true;
        }

        internal bool SetWater(float value)
        {
            var next = Mathf.Clamp(value, 0f, MaxWater);
            if (Mathf.Approximately(Water, next))
            {
                return false;
            }

            Water = next;
            return true;
        }

        internal bool SetMaxWater(float value)
        {
            var next = Mathf.Max(0.0001f, value);
            var beforeWater = Water;
            var beforeMax = MaxWater;
            MaxWater = next;
            if (Water > MaxWater)
            {
                Water = MaxWater;
            }

            return !Mathf.Approximately(beforeMax, MaxWater) ||
                   !Mathf.Approximately(beforeWater, Water);
        }

        internal bool SetTemperature(float value)
        {
            var next = Mathf.Clamp(
                value,
                PlayerConditionRules.MinTemperature,
                PlayerConditionRules.MaxTemperature);
            if (Mathf.Approximately(Temperature, next))
            {
                return false;
            }

            Temperature = next;
            return true;
        }

        internal bool SetSprintReleaseRequired(bool required)
        {
            if (SprintReleaseRequired == required)
            {
                return false;
            }

            SprintReleaseRequired = required;
            return true;
        }

        private static bool ValidateFiniteRange(JToken token, float min, float max, string field, out string error)
        {
            if (token == null ||
                token.Type != JTokenType.Integer &&
                token.Type != JTokenType.Float)
            {
                error = $"{field} 无效";
                return false;
            }

            var value = token.Value<float>();
            if (float.IsNaN(value) ||
                float.IsInfinity(value) ||
                value < min ||
                value > max)
            {
                error = $"{field} 超出范围";
                return false;
            }

            error = null;
            return true;
        }

        private static bool ValidateFinitePositive(JToken token, string field, out string error)
        {
            if (token == null ||
                token.Type != JTokenType.Integer &&
                token.Type != JTokenType.Float)
            {
                error = $"{field} 无效";
                return false;
            }

            var value = token.Value<float>();
            if (float.IsNaN(value) ||
                float.IsInfinity(value) ||
                value <= 0f)
            {
                error = $"{field} 超出范围";
                return false;
            }

            error = null;
            return true;
        }
    }
}

using System;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public readonly struct PlayerBasePoseSnapshot
    {
        public PlayerBasePoseSnapshot(Vector3 localPosition, float localYawDegrees)
        {
            LocalPosition = localPosition;
            LocalYawDegrees = localYawDegrees;
        }
        public Vector3 LocalPosition { get; }
        public float LocalYawDegrees { get; }
    }

    /// <summary>基地玩家局部姿态的唯一持久化权威。</summary>
    public sealed class PlayerBasePoseModel : AbstractModel, IScopedSaveSectionProvider
    {
        public SaveSectionScope Scope => SaveSectionScope.BaseOnly;
        public const string SaveSectionId = "another-day.player-base-pose";
        private bool hasPose;
        private Vector3 localPosition;
        private float localYawDegrees;

        public string SectionId => SaveSectionId;
        public bool HasPose => hasPose;

        protected override void OnInit() { }

        public bool TryGet(out PlayerBasePoseSnapshot snapshot)
        {
            snapshot = new PlayerBasePoseSnapshot(localPosition, localYawDegrees);
            return hasPose;
        }

        public bool TrySet(Vector3 position, float yawDegrees, out bool changed, out string error)
        {
            changed = false;
            if (!IsFinite(position.x) || !IsFinite(position.y) || !IsFinite(position.z) || !IsFinite(yawDegrees))
            {
                error = "基地姿态包含非有限值";
                return false;
            }
            var normalizedYaw = NormalizeYaw(yawDegrees);
            if (hasPose && Vector3.Distance(localPosition, position) < 0.001f &&
                Mathf.Abs(Mathf.DeltaAngle(localYawDegrees, normalizedYaw)) < 0.1f)
            {
                error = null;
                return true;
            }
            localPosition = position;
            localYawDegrees = normalizedYaw;
            hasPose = true;
            changed = true;
            error = null;
            return true;
        }

        public bool Validate(JToken data, out string error)
        {
            error = null;
            if (data == null || data.Type == JTokenType.Null) return true;
            if (!(data is JObject obj)) { error = "基地姿态分区必须为对象"; return false; }
            foreach (var property in obj.Properties())
            {
                if (property.Name != "hasPose" && property.Name != "positionX" && property.Name != "positionY" &&
                    property.Name != "positionZ" && property.Name != "rotationY")
                {
                    error = "基地姿态分区包含未知字段";
                    return false;
                }
            }
            if (!TryGetBoolean(obj, "hasPose", out var exists, out error)) return false;
            var names = new[] { "positionX", "positionY", "positionZ", "rotationY" };
            if (!exists)
            {
                foreach (var name in names) if (obj[name] != null) { error = "hasPose 为 false 时基地姿态值必须缺失"; return false; }
                return true;
            }
            foreach (var name in names)
                if (!TryGetFiniteNumber(obj, name, out _, out error)) return false;
            TryGetFiniteNumber(obj, "rotationY", out var yaw, out _);
            if (yaw < 0f || yaw >= 360f) { error = "基地姿态 rotationY 必须位于 [0, 360)"; return false; }
            return true;
        }

        public JToken Capture()
        {
            var obj = new JObject { ["hasPose"] = hasPose };
            if (hasPose)
            {
                obj["positionX"] = localPosition.x; obj["positionY"] = localPosition.y;
                obj["positionZ"] = localPosition.z; obj["rotationY"] = localYawDegrees;
            }
            return obj;
        }

        public void Restore(JToken data)
        {
            if (data == null || data.Type == JTokenType.Null) { Reset(); return; }
            if (!Validate(data, out var error)) throw new InvalidOperationException(error);
            var obj = (JObject)data;
            if (!(bool)obj["hasPose"]) { Reset(); return; }
            TryGetFiniteNumber(obj, "positionX", out var x, out _); TryGetFiniteNumber(obj, "positionY", out var y, out _);
            TryGetFiniteNumber(obj, "positionZ", out var z, out _); TryGetFiniteNumber(obj, "rotationY", out var yaw, out _);
            localPosition = new Vector3(x, y, z); localYawDegrees = yaw; hasPose = true;
        }

        public void Reset() { hasPose = false; localPosition = Vector3.zero; localYawDegrees = 0f; }

        private static bool TryGetBoolean(JObject obj, string name, out bool value, out string error)
        {
            value = false; error = null;
            if (obj[name]?.Type != JTokenType.Boolean) { error = "基地姿态 hasPose 必须为布尔值"; return false; }
            value = obj[name].Value<bool>(); return true;
        }
        private static bool TryGetFiniteNumber(JObject obj, string name, out float value, out string error)
        {
            value = 0f; error = null; var token = obj[name];
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) { error = $"基地姿态 {name} 必须为数值"; return false; }
            try { value = token.Value<float>(); } catch { error = $"基地姿态 {name} 必须为浮点数"; return false; }
            if (!IsFinite(value)) { error = $"基地姿态 {name} 必须为有限值"; return false; }
            return true;
        }
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float NormalizeYaw(float yaw) { yaw %= 360f; return yaw < 0f ? yaw + 360f : yaw; }
    }
}

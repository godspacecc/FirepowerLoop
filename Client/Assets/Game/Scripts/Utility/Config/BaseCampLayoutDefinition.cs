using UnityEngine;

namespace Game
{
    /// <summary>配置中的初始摆件快照；不保存运行时地图身份或 Unity 对象。</summary>
    public readonly struct BaseCampLayoutDefinition
    {
        public BaseCampLayoutDefinition(string id, string buildingId, Vector3 position, float yaw, float scale)
        { Id = id; BuildingId = buildingId; Position = position; Yaw = yaw; Scale = scale; }
        /// <summary>配置行的稳定身份，用于默认布局去重。</summary>
        public string Id { get; }
        public string BuildingId { get; }
        /// <summary>基地坐标，单位米。</summary>
        public Vector3 Position { get; }
        /// <summary>绕竖直轴的朝向，单位度。</summary>
        public float Yaw { get; }
        /// <summary>相对于标准建筑 Prefab 的统一比例，必须为有限正数。</summary>
        public float Scale { get; }
    }
}

using UnityEngine;

namespace Game
{
    /// <summary>
    /// 保存一个运行时单位的公共身份、归属和空间快照，不包含生命、战斗或 Unity 组件。
    /// </summary>
    public readonly struct UnitSnapshot
    {
        public UnitSnapshot(
            int unitId,
            long mapInstanceId,
            UnitType unitType,
            UnitSource unitSource,
            string configId,
            Vector3 position,
            Quaternion rotation,
            UnitAuthorityMode authorityMode,
            int ownerUnitId,
            bool isActive)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
            UnitType = unitType;
            UnitSource = unitSource;
            ConfigId = configId;
            Position = position;
            Rotation = rotation;
            AuthorityMode = authorityMode;
            OwnerUnitId = ownerUnitId;
            IsActive = isActive;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }
        public UnitType UnitType { get; }
        public UnitSource UnitSource { get; }
        public string ConfigId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public UnitAuthorityMode AuthorityMode { get; }
        public int OwnerUnitId { get; }
        public bool IsActive { get; }

        public UnitSnapshot WithTransform(Vector3 position, Quaternion rotation)
        {
            return new UnitSnapshot(UnitId, MapInstanceId, UnitType, UnitSource, ConfigId, position, rotation, AuthorityMode, OwnerUnitId, IsActive);
        }

        public UnitSnapshot WithActive(bool active)
        {
            return new UnitSnapshot(UnitId, MapInstanceId, UnitType, UnitSource, ConfigId, Position, Rotation, AuthorityMode, OwnerUnitId, active);
        }
    }
}

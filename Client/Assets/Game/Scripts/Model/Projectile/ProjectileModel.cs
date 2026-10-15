using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 保存当前进程内投射物运行态，不加入 Unit，也不参与存档。
    /// </summary>
    public sealed class ProjectileModel : AbstractModel
    {
        private readonly Dictionary<int, ProjectileRuntimeState> projectiles =
            new Dictionary<int, ProjectileRuntimeState>();
        private int nextRuntimeId = 1;

        public IReadOnlyDictionary<int, ProjectileRuntimeState> Projectiles => projectiles;

        protected override void OnInit()
        {
        }

        internal int AllocateRuntimeId()
        {
            return nextRuntimeId++;
        }

        internal void Add(ProjectileRuntimeState state)
        {
            projectiles[state.ProjectileRuntimeId] = state;
        }

        internal bool TryGet(int projectileRuntimeId, out ProjectileRuntimeState state)
        {
            return projectiles.TryGetValue(projectileRuntimeId, out state);
        }

        internal bool TrySet(ProjectileRuntimeState state)
        {
            if (!projectiles.ContainsKey(state.ProjectileRuntimeId))
            {
                return false;
            }

            projectiles[state.ProjectileRuntimeId] = state;
            return true;
        }

        internal bool Remove(int projectileRuntimeId)
        {
            return projectiles.Remove(projectileRuntimeId);
        }

        internal void Clear()
        {
            projectiles.Clear();
        }
    }

    /// <summary>
    /// 描述一支投射物的纯运行时快照，表现层不保存这些战斗结论。
    /// </summary>
    public readonly struct ProjectileRuntimeState
    {
        public ProjectileRuntimeState(
            int projectileRuntimeId,
            int attackerUnitId,
            CombatFaction faction,
            long mapInstanceId,
            int damage,
            Vector3 position,
            Vector3 direction,
            float speed,
            float travelledDistance,
            float maxTravelDistance,
            bool ended,
            string recoveryLootConfigId = "",
            string projectileConfigId = "")
        {
            ProjectileRuntimeId = projectileRuntimeId;
            AttackerUnitId = attackerUnitId;
            Faction = faction;
            MapInstanceId = mapInstanceId;
            Damage = damage;
            Position = position;
            Direction = direction;
            Speed = speed;
            TravelledDistance = travelledDistance;
            MaxTravelDistance = maxTravelDistance;
            Ended = ended;
            RecoveryLootConfigId = recoveryLootConfigId ?? string.Empty;
            ProjectileConfigId = projectileConfigId ?? string.Empty;
        }

        public int ProjectileRuntimeId { get; }
        public int AttackerUnitId { get; }
        public CombatFaction Faction { get; }
        public long MapInstanceId { get; }
        public int Damage { get; }
        public Vector3 Position { get; }
        public Vector3 Direction { get; }
        public float Speed { get; }
        public float TravelledDistance { get; }
        public float MaxTravelDistance { get; }
        public bool Ended { get; }
        public string RecoveryLootConfigId { get; }
        /// <summary>创建时的稳定配置身份；随移动和结束快照保留，不能从持有者当前武器猜命中音色。</summary>
        public string ProjectileConfigId { get; }

        public ProjectileRuntimeState WithPosition(Vector3 position, float travelledDistance)
        {
            return new ProjectileRuntimeState(
                ProjectileRuntimeId,
                AttackerUnitId,
                Faction,
                MapInstanceId,
                Damage,
                position,
                Direction,
                Speed,
                travelledDistance,
                MaxTravelDistance,
                Ended,
                RecoveryLootConfigId, ProjectileConfigId);
        }

        public ProjectileRuntimeState AsEnded()
        {
            return new ProjectileRuntimeState(
                ProjectileRuntimeId,
                AttackerUnitId,
                Faction,
                MapInstanceId,
                Damage,
                Position,
                Direction,
                Speed,
                TravelledDistance,
                MaxTravelDistance,
                true,
                RecoveryLootConfigId, ProjectileConfigId);
        }
    }
}

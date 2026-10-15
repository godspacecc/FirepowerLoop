using System;
using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 表示投射物创建所需的战斗请求，速度和最大距离只由配置解析。
    /// </summary>
    public readonly struct ProjectileSpawnRequest
    {
        public ProjectileSpawnRequest(
            int attackerUnitId,
            CombatFaction faction,
            long mapInstanceId,
            int damage,
            Vector3 origin,
            Vector3 direction,
            string projectileConfigId)
        {
            AttackerUnitId = attackerUnitId;
            Faction = faction;
            MapInstanceId = mapInstanceId;
            Damage = damage;
            Origin = origin;
            Direction = direction;
            ProjectileConfigId = projectileConfigId;
        }

        public int AttackerUnitId { get; }
        public CombatFaction Faction { get; }
        public long MapInstanceId { get; }
        public int Damage { get; }
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public string ProjectileConfigId { get; }
    }

    /// <summary>
    /// 区分投射物结束原因，供清理流程和定向测试核对。
    /// </summary>
    public enum ProjectileEndReason
    {
        HitCombatant,
        HitObstacle,
        MaxDistance,
        MapDeactivated,
        MapReleased,
        Defeated,
        OwnerDied,
        ArchitectureReleased
    }

    public readonly struct ProjectileSpawnedEvent
    {
        public ProjectileSpawnedEvent(int projectileRuntimeId, long mapInstanceId, Vector3 position, Vector3 direction)
        {
            ProjectileRuntimeId = projectileRuntimeId;
            MapInstanceId = mapInstanceId;
            Position = position;
            Direction = direction;
        }

        public int ProjectileRuntimeId { get; }
        public long MapInstanceId { get; }
        public Vector3 Position { get; }
        public Vector3 Direction { get; }
    }

    public readonly struct ProjectileMovedEvent
    {
        public ProjectileMovedEvent(int projectileRuntimeId, Vector3 position)
        {
            ProjectileRuntimeId = projectileRuntimeId;
            Position = position;
        }

        public int ProjectileRuntimeId { get; }
        public Vector3 Position { get; }
    }

    public readonly struct ProjectileEndedEvent
    {
        public ProjectileEndedEvent(int projectileRuntimeId, ProjectileEndReason reason)
        {
            ProjectileRuntimeId = projectileRuntimeId;
            Reason = reason;
        }

        public int ProjectileRuntimeId { get; }
        public ProjectileEndReason Reason { get; }
    }

    /// <summary>
    /// 统一处理投射物创建、推进、碰撞命中和结束，控制器只负责表现回调。
    /// </summary>
    public sealed class ProjectileSystem : AbstractSystem
    {
        private const float DirectionDeadZoneSqr = 0.001f * 0.001f;
        private readonly List<ProjectileMovementHit> movementHits = new List<ProjectileMovementHit>(8);

        protected override void OnInit()
        {
            LifeCycle.RegisterUpdate(OnUpdate);
            this.RegisterEvent<CombatantDiedEvent>(OnCombatantDied);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            EndAll(ProjectileEndReason.ArchitectureReleased);
        }

        public bool TrySpawn(ProjectileSpawnRequest request, out int projectileRuntimeId, out string error)
        {
            projectileRuntimeId = 0;
            if (!ValidateSpawnRequest(request, out var direction, out var config, out error))
            {
                return false;
            }

            projectileRuntimeId = this.GetModel<ProjectileModel>().AllocateRuntimeId();
            if (!this.GetUtility<IProjectileRuntimePortProvider>().TryCreateProjectile(
                    projectileRuntimeId,
                    request.MapInstanceId,
                    request.Origin,
                    direction,
                    config.PrefabAddress,
                    out error))
            {
                projectileRuntimeId = 0;
                return false;
            }

            this.GetModel<ProjectileModel>().Add(new ProjectileRuntimeState(
                projectileRuntimeId,
                request.AttackerUnitId,
                request.Faction,
                request.MapInstanceId,
                request.Damage,
                request.Origin,
                direction,
                config.MoveSpeed,
                0f,
                config.MaxTravelDistance,
                false,
                config.RecoveryLootConfigId, request.ProjectileConfigId));
            this.SendEvent(new ProjectileSpawnedEvent(projectileRuntimeId, request.MapInstanceId, request.Origin, direction));
            error = null;
            return true;
        }

        public bool TryEnd(int projectileRuntimeId, ProjectileEndReason reason)
        {
            var model = this.GetModel<ProjectileModel>();
            if (!model.TryGet(projectileRuntimeId, out var state) || state.Ended)
            {
                return false;
            }

            model.TrySet(state.AsEnded());
            model.Remove(projectileRuntimeId);
            this.GetUtility<IProjectileRuntimePortProvider>().ReleaseProjectile(projectileRuntimeId);
            if (IsRecoverableEnd(reason) && !string.IsNullOrWhiteSpace(state.RecoveryLootConfigId))
            {
                var rotation = state.Direction.sqrMagnitude > DirectionDeadZoneSqr
                    ? Quaternion.LookRotation(state.Direction, Vector3.up)
                    : Quaternion.identity;
                this.SendEvent(new LootDropRequestedEvent(
                    state.MapInstanceId,
                    state.RecoveryLootConfigId,
                    state.Position,
                    rotation));
            }
            this.SendEvent(new ProjectileEndedEvent(projectileRuntimeId, reason));
            return true;
        }

        public void ClearForMap(long mapInstanceId)
        {
            ClearForMap(mapInstanceId, ProjectileEndReason.MapReleased);
        }

        public void ClearForMap(long mapInstanceId, ProjectileEndReason reason)
        {
            if (mapInstanceId <= 0)
            {
                return;
            }

            var ids = new List<int>();
            foreach (var pair in this.GetModel<ProjectileModel>().Projectiles)
            {
                if (pair.Value.MapInstanceId == mapInstanceId)
                {
                    ids.Add(pair.Key);
                }
            }

            for (var i = 0; i < ids.Count; i++)
            {
                TryEnd(ids[i], reason);
            }
        }

        public void HandleCollision(int projectileRuntimeId, Collider other)
        {
            if (other == null ||
                !this.GetModel<ProjectileModel>().TryGet(projectileRuntimeId, out var projectile) ||
                projectile.Ended)
            {
                return;
            }

            if (this.GetUtility<IPlayerRuntimePortProvider>().IsPlayerCollider(
                    this.GetModel<PlayerModel>().PlayerUnitId,
                    projectile.MapInstanceId,
                    other))
            {
                TryHitCombatant(projectile, this.GetModel<PlayerModel>().PlayerUnitId);
                return;
            }

            if (this.GetUtility<IEnemyColliderResolver>().TryResolveEnemyUnitId(other, out var unitId))
            {
                TryHitCombatant(projectile, unitId);
                return;
            }

            if (!other.isTrigger)
            {
                var surface = other.GetComponentInParent<CombatImpactSurface>();
                var kind = surface != null && surface.Material == CombatImpactMaterial.Metal ? CombatSoundCueKind.BulletMetal
                    : surface != null && surface.Material == CombatImpactMaterial.Ice ? CombatSoundCueKind.BulletIce
                    : (CombatSoundCueKind?)null;
                // 只有声明过材质的实体才选对应音色；未知障碍不伪装成肉体或金属。
                if (TryEnd(projectileRuntimeId, ProjectileEndReason.HitObstacle) && kind.HasValue &&
                    projectile.ProjectileConfigId == "projectile.bullet.basic")
                    this.SendEvent(new CombatSoundCueEvent(kind.Value, 0, other.ClosestPoint(projectile.Position)));
            }
        }

        private void OnUpdate()
        {
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            {
                return;
            }

            if (this.GetModel<ExpeditionModel>().Status == ExpeditionStatus.Defeated)
            {
                EndAll(ProjectileEndReason.Defeated);
                return;
            }

            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            var snapshots = new List<ProjectileRuntimeState>(this.GetModel<ProjectileModel>().Projectiles.Values);
            for (var i = 0; i < snapshots.Count; i++)
            {
                Advance(snapshots[i], deltaTime);
            }
        }

        private void Advance(ProjectileRuntimeState state, float deltaTime)
        {
            if (state.Ended ||
                !this.GetModel<ProjectileModel>().TryGet(state.ProjectileRuntimeId, out var current))
            {
                return;
            }

            var distance = current.Speed * deltaTime;
            var remaining = current.MaxTravelDistance - current.TravelledDistance;
            var step = Mathf.Min(distance, Mathf.Max(0f, remaining));
            var nextTravelled = current.TravelledDistance + step;
            var nextPosition = current.Position + current.Direction * step;
            var startPosition = current.Position;
            var startTravelled = current.TravelledDistance;
            this.GetUtility<IProjectileRuntimePortProvider>().CollectMovementHits(
                current.ProjectileRuntimeId,
                current.Position,
                nextPosition,
                movementHits);
            for (var i = 0; i < movementHits.Count; i++)
            {
                var hitDistance = Mathf.Clamp(movementHits[i].Distance, 0f, step);
                current = current.WithPosition(
                    startPosition + current.Direction * hitDistance,
                    startTravelled + hitDistance);
                this.GetModel<ProjectileModel>().TrySet(current);
                HandleCollision(current.ProjectileRuntimeId, movementHits[i].Collider);
                if (!this.GetModel<ProjectileModel>().TryGet(current.ProjectileRuntimeId, out current) || current.Ended)
                {
                    return;
                }
            }

            current = current.WithPosition(nextPosition, nextTravelled);
            this.GetModel<ProjectileModel>().TrySet(current);
            this.GetUtility<IProjectileRuntimePortProvider>().MoveProjectile(current.ProjectileRuntimeId, nextPosition, current.Direction);
            this.SendEvent(new ProjectileMovedEvent(current.ProjectileRuntimeId, nextPosition));
            if (nextTravelled >= current.MaxTravelDistance)
            {
                TryEnd(current.ProjectileRuntimeId, ProjectileEndReason.MaxDistance);
            }
        }

        private bool ValidateSpawnRequest(
            ProjectileSpawnRequest request,
            out Vector3 direction,
            out ProjectileConfigDefinition config,
            out string error)
        {
            direction = request.Direction;
            config = default;
            if (request.AttackerUnitId <= 0 ||
                request.MapInstanceId <= 0 ||
                request.Damage <= 0 ||
                !IsFinite(request.Origin) || !IsFinite(direction) ||
                direction.sqrMagnitude < DirectionDeadZoneSqr ||
                string.IsNullOrWhiteSpace(request.ProjectileConfigId))
            {
                error = "投射物创建请求无效";
                return false;
            }

            direction.Normalize();
            if (!this.GetSystem<UnitSystem>().TryGet(request.AttackerUnitId, out var attacker) ||
                !attacker.IsActive ||
                attacker.MapInstanceId != request.MapInstanceId ||
                !this.GetModel<CombatModel>().TryGetCombatant(request.AttackerUnitId, out var combatant) ||
                combatant.IsDead ||
                combatant.Faction != request.Faction)
            {
                error = "投射物攻击者无效";
                return false;
            }

            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetProjectile(request.ProjectileConfigId, out config, out error))
            {
                return false;
            }

            return true;
        }

        /// <summary>投射物保留调用方的三维方向；拒绝非有限坐标，防止非法状态进入移动与碰撞链路。</summary>
        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool IsRecoverableEnd(ProjectileEndReason reason)
        {
            return reason == ProjectileEndReason.HitObstacle || reason == ProjectileEndReason.MaxDistance;
        }

        private void TryHitCombatant(ProjectileRuntimeState projectile, int targetUnitId)
        {
            if (!this.GetSystem<UnitSystem>().TryGet(targetUnitId, out var targetUnit) ||
                targetUnit.MapInstanceId != projectile.MapInstanceId ||
                !targetUnit.IsActive ||
                !this.GetModel<CombatModel>().TryGetCombatant(targetUnitId, out var targetCombatant) ||
                targetCombatant.IsDead ||
                targetCombatant.Faction == projectile.Faction)
            {
                return;
            }

            if (this.GetSystem<CombatSystem>().TryDamage(projectile.AttackerUnitId, targetUnitId, projectile.Damage))
            {
                TryEnd(projectile.ProjectileRuntimeId, ProjectileEndReason.HitCombatant);
                if (projectile.ProjectileConfigId == "projectile.bullet.basic")
                    this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.BulletBody, 0, targetUnit.Position));
            }
        }

        private void OnCombatantDied(CombatantDiedEvent evt)
        {
            if (evt.UnitId != PlayerModel.LocalPlayerUnitId)
            {
                return;
            }

            EndAll(ProjectileEndReason.OwnerDied);
        }

        private void EndAll(ProjectileEndReason reason)
        {
            var ids = new List<int>(this.GetModel<ProjectileModel>().Projectiles.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
                TryEnd(ids[i], reason);
            }
        }
    }
}

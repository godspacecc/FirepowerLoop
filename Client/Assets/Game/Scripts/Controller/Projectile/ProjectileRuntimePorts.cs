using QFramework;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 投射物表现端口，由系统请求创建、移动和释放，不暴露战斗规则到 View。
    /// </summary>
    public interface IProjectileRuntimePortProvider : IUtility
    {
        bool TryCreateProjectile(
            int projectileRuntimeId,
            long mapInstanceId,
            Vector3 position,
            Vector3 direction,
            string prefabAddress,
            out string error);

        void MoveProjectile(int projectileRuntimeId, Vector3 position, Vector3 direction);
        void CollectMovementHits(int projectileRuntimeId, Vector3 from, Vector3 to, List<ProjectileMovementHit> results);
        void ReleaseProjectile(int projectileRuntimeId);
    }

    /// <summary>街机投射物的纯表现端口；碰撞、伤害和寿命继续由街机战斗规则裁决。</summary>
    public interface IArcadeProjectilePresentationPort : IUtility
    {
        bool TrySetProjectile(int runtimeId, Vector3 position, Vector3 direction, out string error);
        bool TrySetProjectile(int runtimeId, Vector3 position, Vector3 direction, string visualResourceName, bool hostile,
            out string error) => TrySetProjectile(runtimeId, position, direction, out error);
        void RemoveProjectile(int runtimeId);
        void ClearArcadeProjectiles();
    }

    public readonly struct ProjectileMovementHit
    {
        public ProjectileMovementHit(Collider collider, float distance)
        {
            Collider = collider;
            Distance = distance;
        }

        public Collider Collider { get; }
        public float Distance { get; }
    }

    /// <summary>
    /// 敌人碰撞体只读解析端口，供投射物系统识别命中的敌人 Unit。
    /// </summary>
    public interface IEnemyColliderResolver : IUtility
    {
        bool TryResolveEnemyUnitId(Collider collider, out int unitId);
    }
}

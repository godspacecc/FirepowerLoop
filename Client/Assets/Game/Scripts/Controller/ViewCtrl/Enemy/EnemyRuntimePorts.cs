using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 敌人移动表现端口，只服务寻路、追击和攻击距离判断。
    /// </summary>
    public interface IEnemyNavigationPort
    {
        int UnitId { get; }
        long MapInstanceId { get; }
        bool IsValid { get; }
        Vector3 Position { get; }
        Quaternion Rotation { get; }
        bool IsOnNavigableSurface(float sampleRadius);
        bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin);
        bool TrySetRetreatDestination(Vector3 requestedPosition, float sampleRadius, out Vector3 destination);
        void PlayAttackAnimation();
        void SetTelegraphVisible(bool visible);
        void SetDestination(Vector3 position);
        void Stop();
    }

    /// <summary>街机仅复用敌人 Prefab 与表现生命周期；生命、移动和死亡结论仍由 ArcadeCombatSystem 权威维护。</summary>
    public interface IArcadeEnemyPresentationPort : IUtility
    {
        bool TrySetEnemy(int runtimeId, string configId, Vector3 position, Vector3 facing,
            int currentHealth, int maximumHealth, out string error, bool telegraph = false,
            int attackSequence = 0, bool persistentHealthBar = false);
        void RemoveEnemy(int runtimeId, bool playDeath = false);
        void ClearArcadeEnemies();
    }
}

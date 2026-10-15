using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>街机投射物的纯表现端口，不拥有碰撞或伤害规则。</summary>
    public interface IArcadeProjectilePresentationPort : IUtility
    {
        bool TrySetProjectile(int runtimeId, Vector3 position, Vector3 direction, out string error);
        bool TrySetProjectile(int runtimeId, Vector3 position, Vector3 direction, string visualResourceName, bool hostile,
            out string error) => TrySetProjectile(runtimeId, position, direction, out error);
        void RemoveProjectile(int runtimeId);
        void ClearArcadeProjectiles();
    }

}

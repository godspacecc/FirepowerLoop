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
        /// <summary>传递纯视觉比例；默认实现兼容不支持缩放的表现端口。</summary>
        bool TrySetProjectile(int runtimeId, Vector3 position, Vector3 direction, string visualResourceName, bool hostile,
            float visualScale, out string error) => TrySetProjectile(runtimeId, position, direction, visualResourceName, hostile, out error);
        void RemoveProjectile(int runtimeId);
        void ClearArcadeProjectiles();
    }

}

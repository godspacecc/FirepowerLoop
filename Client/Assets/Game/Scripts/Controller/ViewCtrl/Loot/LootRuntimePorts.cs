using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>街机掉落池与正式Prefab表现端口，不裁决拾取或本局奖励。</summary>
    public interface IArcadeLootPresentationPort : IUtility
    {
        bool TrySetDrop(int runtimeId, Vector3 position, string displayName, int amount, out string error);
        /// <summary>同步街机掉落，并把配置提供的可加载 Prefab 地址交给表现控制器。</summary>
        bool TrySetDrop(
            int runtimeId,
            Vector3 position,
            string displayName,
            int amount,
            string visualPrefabAddress,
            out string error)
        {
            return TrySetDrop(runtimeId, position, displayName, amount, out error);
        }

        /// <summary>同步掉落实例的剩余寿命透明度；旧表现端口可忽略视觉渐隐。</summary>
        void SetDropOpacity(int runtimeId, float opacity) { }

        void RemoveDrop(int runtimeId);
        /// <summary>地图空投只按已同步的阶段控制箱子、名称和武器显隐。</summary>
        void SetDropDelivery(int runtimeId,bool airdrop,float remaining,float duration) { }
        void ClearArcadeDrops();
    }
}

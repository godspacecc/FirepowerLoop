using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 掉落物表现端口，供 LootCtrl 统一处理显隐、朝向和收集触发。
    /// </summary>
    public interface ILootPresentationPort
    {
        int UnitId { get; }
        bool IsVisibleTargetActive { get; }
        Vector3 Position { get; }
        void RefreshDisplay(string itemDisplayName, int amount);
        void SetNameVisible(bool visible);
        /// <summary>按相机世界前方向和上方向对齐名称；第一个参数不是相机位置。</summary>
        void FaceName(Vector3 cameraForward, Vector3 cameraUp);
        void SetCollected();
    }

    /// <summary>
    /// 街机掉落表现端口；仅负责复用正式掉落 Prefab、同步位置和对象池，不注册 Loot/Unit 领域或裁决拾取。
    /// </summary>
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

        void RemoveDrop(int runtimeId);
        void ClearArcadeDrops();
    }
}

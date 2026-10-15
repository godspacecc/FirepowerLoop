using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>同步创建归属当前地图的采集产物；不修改背包、采集记录或存档，由采集系统决定提交与回滚。</summary>
    public interface ICivilisationHarvestDropPort : IUtility
    {
        /// <summary>在资源点附近落地；只使用已准备资源，失败不留下任何领域或表现对象。</summary>
        /// <param name="mapInstanceId">当前活动地图实例，旧实例请求必须拒绝。</param>
        /// <param name="position">资源点世界坐标，米。</param>
        /// <param name="lootUnitIds">本次创建的实体身份，仅供同一事务回滚；失败为空。</param>
        bool TryCreateHarvestDrop(long mapInstanceId, Vector3 position, int itemId, int amount,
            out IReadOnlyList<int> lootUnitIds, out string error);

        /// <summary>撤销本次创建；空收据和重复回滚无副作用。</summary>
        void RollbackHarvestDrop(IReadOnlyList<int> lootUnitIds);
    }
}

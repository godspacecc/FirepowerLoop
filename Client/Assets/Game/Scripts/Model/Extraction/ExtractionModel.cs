using System.Collections.Generic;
using QFramework;

namespace Game
{
    /// <summary>
    /// 保存当前地图运行会话中的撤离点范围、可用性与返回中状态；这些状态均不进入存档。
    /// </summary>
    public sealed class ExtractionModel : AbstractModel
    {
        private readonly Dictionary<int, ExtractionSnapshot> extractions =
            new Dictionary<int, ExtractionSnapshot>();

        public IReadOnlyDictionary<int, ExtractionSnapshot> Extractions => extractions;

        protected override void OnInit()
        {
        }

        internal void Set(ExtractionSnapshot snapshot)
        {
            extractions[snapshot.UnitId] = snapshot;
        }

        internal bool Remove(int unitId)
        {
            return extractions.Remove(unitId);
        }

        public bool TryGet(int unitId, out ExtractionSnapshot snapshot)
        {
            return extractions.TryGetValue(unitId, out snapshot);
        }
    }

    /// <summary>
    /// 单个撤离点的运行会话快照。PlayerInside 与 Available、Returning 一样只在当前地图会话有效；
    /// 四参数构造仅兼容旧调用，并明确把 PlayerInside 初始化为 false。
    /// </summary>
    public readonly struct ExtractionSnapshot
    {
        public ExtractionSnapshot(int unitId, long mapInstanceId, bool available, bool returning)
            : this(unitId, mapInstanceId, available, returning, false)
        {
        }

        public ExtractionSnapshot(int unitId, long mapInstanceId, bool available, bool returning, bool playerInside)
        {
            UnitId = unitId;
            MapInstanceId = mapInstanceId;
            Available = available;
            Returning = returning;
            PlayerInside = playerInside;
        }

        public int UnitId { get; }
        public long MapInstanceId { get; }
        public bool Available { get; }
        public bool Returning { get; }
        public bool PlayerInside { get; }

        public ExtractionSnapshot WithAvailable(bool available)
        {
            return new ExtractionSnapshot(UnitId, MapInstanceId, available, Returning, PlayerInside);
        }

        public ExtractionSnapshot WithReturning(bool returning)
        {
            return new ExtractionSnapshot(UnitId, MapInstanceId, Available, returning, PlayerInside);
        }

        public ExtractionSnapshot WithPlayerInside(bool playerInside)
        {
            return new ExtractionSnapshot(UnitId, MapInstanceId, Available, Returning, playerInside);
        }
    }
}

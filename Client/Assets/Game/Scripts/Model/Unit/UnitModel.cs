using System.Collections.Generic;
using QFramework;

namespace Game
{
    /// <summary>
    /// 保存当前活动世界中的 Unit 字典，并提供只读查询入口。
    /// </summary>
    public sealed class UnitModel : AbstractModel
    {
        private readonly Dictionary<int, UnitSnapshot> units = new Dictionary<int, UnitSnapshot>();

        public int Count => units.Count;
        public IReadOnlyDictionary<int, UnitSnapshot> Units => units;

        protected override void OnInit()
        {
        }

        internal bool Contains(int unitId)
        {
            return units.ContainsKey(unitId);
        }

        internal void Set(UnitSnapshot snapshot)
        {
            units[snapshot.UnitId] = snapshot;
        }

        internal bool Remove(int unitId)
        {
            return units.Remove(unitId);
        }

        internal void Clear()
        {
            units.Clear();
        }

        public bool TryGet(int unitId, out UnitSnapshot snapshot)
        {
            return units.TryGetValue(unitId, out snapshot);
        }
    }
}

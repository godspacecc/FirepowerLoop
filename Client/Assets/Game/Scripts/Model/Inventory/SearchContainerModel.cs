using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    internal sealed class SearchContainerRecord
    {
        public SearchContainerRecord(int containerId, long mapInstanceId, int sourceEnemyUnitId, Vector3 position, ItemContainer container)
        {
            ContainerId = containerId;
            MapInstanceId = mapInstanceId;
            SourceEnemyUnitId = sourceEnemyUnitId;
            Position = position;
            Container = container;
        }

        public int ContainerId { get; }
        public long MapInstanceId { get; }
        public int SourceEnemyUnitId { get; }
        public Vector3 Position { get; }
        public ItemContainer Container { get; }
    }

    public sealed class SearchContainerModel : AbstractModel
    {
        private readonly Dictionary<int, SearchContainerRecord> records = new Dictionary<int, SearchContainerRecord>();

        protected override void OnInit() { }

        internal bool TryRegister(SearchContainerRecord record)
        {
            return record != null && record.ContainerId > 0 && !records.ContainsKey(record.ContainerId) &&
                   records.TryAdd(record.ContainerId, record);
        }

        /// <summary>原子注册一组已验证的候选容器，失败时不改动活动记录。</summary>
        internal bool TryRegisterRange(IReadOnlyList<SearchContainerRecord> candidates)
        {
            if (candidates == null) return false;
            var ids = new HashSet<int>();
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || candidate.ContainerId <= 0 || records.ContainsKey(candidate.ContainerId) || !ids.Add(candidate.ContainerId)) return false;
            }

            for (var i = 0; i < candidates.Count; i++) records.Add(candidates[i].ContainerId, candidates[i]);
            return true;
        }

        internal bool TryGet(int containerId, out SearchContainerRecord record)
        {
            return records.TryGetValue(containerId, out record);
        }

        internal IEnumerable<SearchContainerRecord> Enumerate()
        {
            return records.Values;
        }

        internal bool Remove(int containerId)
        {
            return records.Remove(containerId);
        }

        internal void Clear()
        {
            records.Clear();
        }

        internal void RemoveByMapInstance(long mapInstanceId)
        {
            var ids = new List<int>();
            foreach (var pair in records)
            {
                if (pair.Value.MapInstanceId == mapInstanceId) ids.Add(pair.Key);
            }

            foreach (var id in ids) records.Remove(id);
        }
    }
}

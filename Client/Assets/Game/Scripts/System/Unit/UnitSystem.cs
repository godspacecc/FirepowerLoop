using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 分配、注册、更新和移除当前活动世界中的 Unit 公共快照。
    /// </summary>
    public sealed class UnitSystem : AbstractSystem
    {
        private const int DynamicUnitIdStart = 1000;
        private int nextDynamicUnitId = DynamicUnitIdStart;

        protected override void OnInit()
        {
        }

        public int AllocateUnitId()
        {
            var model = this.GetModel<UnitModel>();
            while (model.Contains(nextDynamicUnitId) || nextDynamicUnitId == PlayerModel.LocalPlayerUnitId)
            {
                nextDynamicUnitId++;
            }

            return nextDynamicUnitId++;
        }

        public bool TryRegister(UnitSnapshot snapshot, out string error)
        {
            if (!ValidateSnapshot(snapshot, out error))
            {
                return false;
            }

            var model = this.GetModel<UnitModel>();
            if (model.Contains(snapshot.UnitId))
            {
                error = $"Unit 注册失败：UnitId={snapshot.UnitId} 已存在";
                return false;
            }

            model.Set(snapshot);
            this.SendEvent(new UnitAddedEvent(snapshot));
            return true;
        }

        public bool TryGet(int unitId, out UnitSnapshot snapshot)
        {
            return this.GetModel<UnitModel>().TryGet(unitId, out snapshot);
        }

        public bool TryUpdateTransform(int unitId, Vector3 position, Quaternion rotation)
        {
            var model = this.GetModel<UnitModel>();
            if (!model.TryGet(unitId, out var previous))
            {
                return false;
            }

            var mask = UnitChangeMask.None;
            if (previous.Position != position)
            {
                mask |= UnitChangeMask.Position;
            }

            if (previous.Rotation != rotation)
            {
                mask |= UnitChangeMask.Rotation;
            }

            if (mask == UnitChangeMask.None)
            {
                return false;
            }

            var current = previous.WithTransform(position, rotation);
            model.Set(current);
            this.SendEvent(new UnitChangedEvent(previous, current, mask));
            return true;
        }

        public bool TrySetActive(int unitId, bool active)
        {
            var model = this.GetModel<UnitModel>();
            if (!model.TryGet(unitId, out var previous) || previous.IsActive == active)
            {
                return false;
            }

            var current = previous.WithActive(active);
            model.Set(current);
            this.SendEvent(new UnitChangedEvent(previous, current, UnitChangeMask.Active));
            return true;
        }

        public bool Remove(int unitId, UnitRemovedReason reason)
        {
            var model = this.GetModel<UnitModel>();
            if (!model.TryGet(unitId, out var snapshot))
            {
                return false;
            }

            model.Remove(unitId);
            this.SendEvent(new UnitRemovedEvent(snapshot, reason));
            return true;
        }

        internal bool TryRestoreRemovedSnapshot(UnitSnapshot snapshot)
        {
            if (!ValidateSnapshot(snapshot, out _))
            {
                return false;
            }

            var model = this.GetModel<UnitModel>();
            if (model.Contains(snapshot.UnitId))
            {
                return false;
            }

            model.Set(snapshot);
            return true;
        }

        public int RemoveMapInstance(long MapInstanceId, UnitRemovedReason reason)
        {
            if (MapInstanceId <= 0)
            {
                return 0;
            }

            var model = this.GetModel<UnitModel>();
            var ids = new List<int>();
            foreach (var pair in model.Units)
            {
                if (pair.Value.MapInstanceId == MapInstanceId)
                {
                    ids.Add(pair.Key);
                }
            }

            for (var i = 0; i < ids.Count; i++)
            {
                Remove(ids[i], reason);
            }

            return ids.Count;
        }

        private static bool ValidateSnapshot(UnitSnapshot snapshot, out string error)
        {
            if (snapshot.UnitId <= 0)
            {
                error = "Unit 注册失败：UnitId 必须大于 0";
                return false;
            }

            if (snapshot.MapInstanceId <= 0)
            {
                error = "Unit 注册失败：MapInstanceId 必须大于 0";
                return false;
            }

            error = null;
            return true;
        }
    }
}

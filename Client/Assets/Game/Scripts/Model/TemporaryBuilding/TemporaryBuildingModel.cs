using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;

namespace Game
{
    public enum TemporaryBuildingMode
    {
        Normal,
        BuildPreview,
        BuildProgress,
        OperatingManualAttack,
        DestroyMode
    }

    public readonly struct TemporaryBuildingRuntimeState
    {
        public TemporaryBuildingRuntimeState(
            int runtimeId,
            string configId,
            long mapInstanceId,
            Vector3 position,
            Quaternion rotation, float uniformScale = 1f)
        {
            RuntimeId = runtimeId;
            ConfigId = configId;
            MapInstanceId = mapInstanceId;
            Position = position;
            Rotation = rotation;
            UniformScale = uniformScale;
        }

        /// <summary>保存的模型比例，正常新建为 1，初始布局按原场景比例恢复。</summary>
        public float UniformScale { get; }
        public int RuntimeId { get; }
        public string ConfigId { get; }
        public long MapInstanceId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
    }

    public readonly struct TemporaryManualAttackRuntimeState
    {
        public TemporaryManualAttackRuntimeState(float nextShootTime, int loadedAmmo = 0)
        {
            NextShootTime = nextShootTime;
            LoadedAmmo = Mathf.Max(0, loadedAmmo);
        }

        public float NextShootTime { get; }
        public int LoadedAmmo { get; }
        public TemporaryManualAttackRuntimeState WithNextShootTime(float value) => new TemporaryManualAttackRuntimeState(value, LoadedAmmo);
        public TemporaryManualAttackRuntimeState WithLoadedAmmo(int value) => new TemporaryManualAttackRuntimeState(NextShootTime, value);
    }

    public readonly struct TemporaryBuildingHudSnapshot
    {
        public TemporaryBuildingHudSnapshot(
            TemporaryBuildingMode mode,
            bool placementValid,
            string reason,
            float progress01,
            string selectedBuildingName,
            int arrowAmount,
            float cooldownRemaining,
            IEnumerable<TemporaryBuildingMaterialSnapshot> buildMaterials = null,
            int destroyTargetBuildingId = 0,
            bool destroyTargetTooFar = false,
            float operationRange = 0f)
        {
            Mode = mode;
            PlacementValid = placementValid;
            Reason = reason;
            Progress01 = Mathf.Clamp01(progress01);
            SelectedBuildingName = selectedBuildingName ?? string.Empty;
            ArrowAmount = arrowAmount;
            CooldownRemaining = Mathf.Max(0f, cooldownRemaining);
            BuildMaterials = (buildMaterials ?? Enumerable.Empty<TemporaryBuildingMaterialSnapshot>()).ToArray();
            DestroyTargetBuildingId = destroyTargetBuildingId;
            DestroyTargetTooFar = destroyTargetTooFar;
            OperationRange = operationRange;
        }

        public TemporaryBuildingMode Mode { get; }
        public bool PlacementValid { get; }
        public string Reason { get; }
        public float Progress01 { get; }
        public string SelectedBuildingName { get; }
        public int ArrowAmount { get; }
        public float CooldownRemaining { get; }
        public IReadOnlyList<TemporaryBuildingMaterialSnapshot> BuildMaterials { get; }
        /// <summary>当前快照中的有效拆除目标 ID；0 表示无目标，仅用于表现，不进入存档。</summary>
        public int DestroyTargetBuildingId { get; }
        /// <summary>指向当前地图物件但超出三维拆除距离；不代表有效拆除目标。</summary>
        public bool DestroyTargetTooFar { get; }
        /// <summary>表现用玩家操作半径，单位米；建造与拆除共用，建筑占地大小不改变圈的大小。</summary>
        public float OperationRange { get; }
    }

    public readonly struct TemporaryBuildingMaterialSnapshot
    {
        public TemporaryBuildingMaterialSnapshot(int itemId, string name, int ownedAmount, int requiredAmount)
        {
            ItemId = itemId;
            Name = name;
            OwnedAmount = ownedAmount;
            RequiredAmount = requiredAmount;
        }

        public int ItemId { get; }
        public string Name { get; }
        public int OwnedAmount { get; }
        public int RequiredAmount { get; }
        public bool IsEnough => OwnedAmount >= RequiredAmount;
    }

    public sealed class TemporaryBuildingModel : AbstractModel
    {
        private readonly Dictionary<int, TemporaryBuildingRuntimeState> buildings = new Dictionary<int, TemporaryBuildingRuntimeState>();
        private readonly Dictionary<int, TemporaryManualAttackRuntimeState> manualAttacks = new Dictionary<int, TemporaryManualAttackRuntimeState>();
        private int nextRuntimeId = 1;

        public IReadOnlyDictionary<int, TemporaryBuildingRuntimeState> Buildings => buildings;
        public IReadOnlyDictionary<int, TemporaryManualAttackRuntimeState> ManualAttacks => manualAttacks;
        public TemporaryBuildingMode Mode { get; internal set; } = TemporaryBuildingMode.Normal;
        public string PreviewConfigId { get; internal set; }
        public Vector3 PreviewPosition { get; internal set; }
        public Quaternion PreviewRotation { get; internal set; } = Quaternion.identity;
        public bool PreviewValid { get; internal set; }
        public string PreviewReason { get; internal set; }
        public float BuildElapsedSeconds { get; internal set; }
        public long PreviewMapInstanceId { get; internal set; }
        public int OperatingManualAttackId { get; internal set; }
        public int DestroyTargetBuildingId { get; internal set; }
        /// <summary>最近一次指向的同地图建筑 ID，含超距候选；0 表示无候选，不进入存档。</summary>
        public int DestroyCandidateBuildingId { get; internal set; }
        public bool DestroyTargetTooFar { get; internal set; }

        protected override void OnInit()
        {
        }

        internal int AllocateRuntimeId()
        {
            while (buildings.ContainsKey(nextRuntimeId))
            {
                nextRuntimeId++;
            }

            return nextRuntimeId++;
        }

        internal bool AddBuilding(TemporaryBuildingRuntimeState state, TemporaryManualAttackRuntimeState? manualAttack = null)
        {
            if (state.RuntimeId <= 0 || buildings.ContainsKey(state.RuntimeId))
            {
                return false;
            }

            buildings.Add(state.RuntimeId, state);
            if (manualAttack.HasValue) manualAttacks[state.RuntimeId] = manualAttack.Value;
            if (state.RuntimeId >= nextRuntimeId)
            {
                nextRuntimeId = state.RuntimeId + 1;
            }

            return true;
        }

        internal bool TryGetBuilding(int runtimeId, out TemporaryBuildingRuntimeState state)
        {
            return buildings.TryGetValue(runtimeId, out state);
        }

        internal bool TrySetManualAttack(int runtimeId, TemporaryManualAttackRuntimeState state)
        {
            if (!manualAttacks.ContainsKey(runtimeId)) return false;
            manualAttacks[runtimeId] = state; return true;
        }

        internal bool RemoveBuilding(int runtimeId)
        {
            manualAttacks.Remove(runtimeId);
            return buildings.Remove(runtimeId);
        }

        internal void ClearMap(long mapInstanceId)
        {
            if (mapInstanceId <= 0)
            {
                return;
            }

            var ids = new List<int>();
            foreach (var pair in buildings)
            {
                if (pair.Value.MapInstanceId == mapInstanceId)
                {
                    ids.Add(pair.Key);
                }
            }

            for (var i = 0; i < ids.Count; i++)
            {
                RemoveBuilding(ids[i]);
            }
        }

        internal void ResetMode()
        {
            Mode = TemporaryBuildingMode.Normal;
            PreviewConfigId = null;
            PreviewPosition = Vector3.zero;
            PreviewRotation = Quaternion.identity;
            PreviewValid = false;
            PreviewReason = null;
            BuildElapsedSeconds = 0f;
            PreviewMapInstanceId = 0;
            OperatingManualAttackId = 0;
            DestroyTargetBuildingId = 0;
            DestroyCandidateBuildingId = 0;
            DestroyTargetTooFar = false;
        }
    }
}

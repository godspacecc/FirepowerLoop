using UnityEngine;

namespace Game
{
    public readonly struct TemporaryBuildRequestedEvent
    {
    }

    public readonly struct TemporaryDestroyRequestedEvent
    {
    }

    public readonly struct TemporaryCancelRequestedEvent
    {
    }

    public enum TemporaryBuildingPromptReason
    {
        AmmoInsufficient,
        PressReload
    }

    public readonly struct TemporaryBuildingPromptEvent
    {
        public TemporaryBuildingPromptEvent(TemporaryBuildingPromptReason reason, int ammoItemId, int buildingRuntimeId)
        {
            Reason = reason;
            AmmoItemId = ammoItemId;
            BuildingRuntimeId = buildingRuntimeId;
        }

        public TemporaryBuildingPromptReason Reason { get; }
        public int AmmoItemId { get; }
        public int BuildingRuntimeId { get; }
    }

    public readonly struct TemporaryBuildingChangedEvent
    {
        public TemporaryBuildingChangedEvent(TemporaryBuildingHudSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public TemporaryBuildingHudSnapshot Snapshot { get; }
    }

    public readonly struct TemporaryBuildingCreatedEvent
    {
        public TemporaryBuildingCreatedEvent(int runtimeId, long mapInstanceId, string configId, Vector3 position)
        {
            RuntimeId = runtimeId;
            MapInstanceId = mapInstanceId;
            ConfigId = configId;
            Position = position;
        }

        public int RuntimeId { get; }
        public long MapInstanceId { get; }
        public string ConfigId { get; }
        public Vector3 Position { get; }
    }

    public readonly struct TemporaryBuildingDestroyedEvent
    {
        public TemporaryBuildingDestroyedEvent(int runtimeId, long mapInstanceId)
        {
            RuntimeId = runtimeId;
            MapInstanceId = mapInstanceId;
        }

        public int RuntimeId { get; }
        public long MapInstanceId { get; }
    }
}

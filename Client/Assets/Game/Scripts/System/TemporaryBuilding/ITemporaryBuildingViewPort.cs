using QFramework;
using UnityEngine;

namespace Game
{
    public interface ITemporaryBuildingViewPort : IUtility
    {
        bool TryCreatePreview(long mapInstanceId, TempBuildingDefinition definition, bool requiresManualAttack, Vector3 position, Quaternion rotation, out string error);
        void UpdatePreview(Vector3 position, Quaternion rotation, bool valid);
        void ReleasePreview();
        /// <summary>候选占地用于朝向相关的验证；旧视口没有矩形数据时继续原有规则。</summary>
        bool TryGetPlacementBox(Vector3 position, Quaternion rotation, out Vector3 center, out Vector3 halfSize)
        { center = default; halfSize = default; return false; }
        bool TryCreateBuilding(TemporaryBuildingRuntimeState state, TempBuildingDefinition definition, bool requiresManualAttack, out string error);
        bool TryGetManualAttackPose(int runtimeId, out Vector3 origin, out Vector3 forward);
        void SetManualAttackAimDirection(int runtimeId, Vector3 direction);
        void SetManualAttackHud(int runtimeId, bool operating, int ammoItemId, int loadedAmmo, int capacity, PlayerActionSnapshot? reloadSnapshot);
        void PlayManualAttackEmptyAmmoFeedback(int runtimeId);
        bool TryPickCommittedBuilding(Ray pointerRay, float maxDistance, out int runtimeId);
        void FaceManualAttackWorldHuds(Vector3 cameraPosition, Vector3 cameraUp);
        void SetOperating(int runtimeId, bool active);
        void SetDestroyHighlight(int runtimeId, bool active);
        void ReleaseBuilding(int runtimeId);
        void ReleaseMap(long mapInstanceId);
        void ReleaseAll();
    }
}

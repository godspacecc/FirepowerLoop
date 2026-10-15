using UnityEngine;

namespace Game
{
    /// <summary>从角色脚底查询最近支撑与明确地表标记；未知或结果溢出保持静音，不猜测材质。</summary>
    public sealed class FootstepGroundProbe
    {
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Collider[] regions = new Collider[16];

        public bool TryGet(Transform actor, CharacterController controller, out FootstepSurfaceKind surface)
        {
            surface = FootstepSurfaceKind.Unknown;
            if (actor == null || controller == null) return false;
            // CharacterController 在远端可能禁用，bounds 此时为空，须从序列化几何求脚底。
            Vector3 foot = actor.TransformPoint(controller.center - Vector3.up * controller.height * 0.5f);
            int count = Physics.RaycastNonAlloc(foot + Vector3.up * 0.2f, Vector3.down, hits,
                0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            Collider support = null;
            Vector3 supportPoint = foot;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(actor) || hit.normal.y < 0.5f) continue;
                if (hit.distance < nearest) { nearest = hit.distance; support = hit.collider; supportPoint = hit.point; }
            }
            if (support == null) return false;
            var marker = support.GetComponentInParent<FootstepSurface>();
            if (marker == null || !marker.isActiveAndEnabled || marker.IsRegion) return false;
            surface = marker.ResolveKind(supportPoint);
            if (surface == FootstepSurfaceKind.Unknown) return false;
            int regionCount = Physics.OverlapSphereNonAlloc(foot, 0.05f, regions,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            if (regionCount == regions.Length) { surface = FootstepSurfaceKind.Unknown; return false; }
            FootstepSurface selected = null;
            for (int i = 0; i < regionCount; i++)
            {
                var collider = regions[i];
                if (!collider.isTrigger || collider.gameObject.scene != support.gameObject.scene) continue;
                var region = collider.GetComponent<FootstepSurface>();
                if (region == null || !region.isActiveAndEnabled || !region.IsRegion || string.IsNullOrEmpty(region.RegionId)) continue;
                if (!region.Contains(foot)) continue;
                if (selected == null || region.Priority > selected.Priority ||
                    region.Priority == selected.Priority && string.CompareOrdinal(region.RegionId, selected.RegionId) < 0)
                    selected = region;
            }
            if (selected != null) surface = selected.Kind;
            return surface != FootstepSurfaceKind.Unknown;
        }
    }
}

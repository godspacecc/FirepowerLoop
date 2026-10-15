using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>把准星射线解析为范围内的可见交互对象；只查询 Unity 几何，不维护焦点或执行业务。</summary>
    public static class FirstPersonInteractionTargeting
    {
        /// <summary>以米为单位的查询上限；实际候选仍受各对象已有交互范围限制。</summary>
        private const float MaximumDistance = 6f;

        /// <summary>选择射线穿过的最近模型，排除范围触发器和墙后对象；无目标返回 null。</summary>
        public static IPlayerInteractable Resolve(Ray ray, IEnumerable<IPlayerInteractable> candidates)
        {
            IPlayerInteractable selected = null;
            Component selectedComponent = null;
            var nearest = MaximumDistance;
            foreach (var candidate in candidates)
            {
                var component = candidate as Component;
                if (component == null || !component.gameObject.activeInHierarchy ||
                    (!candidate.CanInteract && !(candidate is IPlayerSearchable searchable && searchable.CanSearch))) continue;
                if (!TryGetBounds(component, out var bounds) || !bounds.IntersectRay(ray, out var distance) ||
                    distance < 0f || distance > nearest) continue;
                nearest = distance;
                selected = candidate;
                selectedComponent = component;
            }

            if (selectedComponent == null) return null;
            // 范围 Trigger 不挡视线；玩家自身与当前目标的实体碰撞也不能误挡自己的提示。
            foreach (var hit in Physics.RaycastAll(ray, nearest + 0.01f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<PlayerView>() != null ||
                    hit.collider.transform.IsChildOf(selectedComponent.transform)) continue;
                return null;
            }
            return selected;
        }

        /// <summary>优先合并可见模型，缺少模型时使用实体碰撞；HUD 和交互范围不参与选取。</summary>
        private static bool TryGetBounds(Component component, out Bounds bounds)
        {
            bounds = default;
            var found = false;
            foreach (var renderer in component.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer ||
                    renderer.GetComponentInParent<Canvas>() != null) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (found) return true;
            // 无模型的交互标记只用实体碰撞形状，不能拿大范围 Trigger 当可看目标。
            foreach (var collider in component.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (!found) { bounds = collider.bounds; found = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            return found;
        }
    }
}

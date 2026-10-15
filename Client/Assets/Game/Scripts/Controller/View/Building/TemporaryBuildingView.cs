using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game
{
    /// <summary>管理建筑的序列化表现、预览碰撞及高亮；建造资格、材料与存档由 System 裁决。</summary>
    [ExecuteAlways]
    public sealed class TemporaryBuildingView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly Color DestroyTint = new Color32(0x71, 0x3B, 0x3B, 0xFF);

        public Collider FootprintCollider;
        /// <summary>摆件选择占地与实体分离；预览禁用全部实体，正式提交只启用模型碰撞。</summary>
        public bool SeparatePhysicalColliders;
        public NavMeshObstacle NavMeshObstacle;
        public GameObject ValidHighlight;
        public GameObject InvalidHighlight;
        public GameObject DestroyHighlight;
        public GameObject CommittedEffectRoot;
        /// <summary>仅标记表现是否已提交；供相机排除建造预览，不代表建造资格或存档权威。</summary>
        public bool IsCommitted { get; private set; }

        private readonly Dictionary<Renderer, MaterialPropertyBlock> destroyHighlightOriginalBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();
        private List<Renderer> destroyHighlightRenderers;
        private bool destroyHighlightActive;

        public bool ValidateReferences(out string error)
        {
            error = null;
            if (FootprintCollider == null)
            {
                error = "临时建筑缺少占地碰撞器";
                return false;
            }

            if (ValidHighlight == null)
            {
                error = "临时建筑缺少有效高亮";
                return false;
            }

            if (InvalidHighlight == null)
            {
                error = "临时建筑缺少无效高亮";
                return false;
            }

            if (DestroyHighlight == null)
            {
                error = "临时建筑缺少销毁高亮";
                return false;
            }

            if (CommittedEffectRoot != null)
            {
                if (!CommittedEffectRoot.transform.IsChildOf(transform) ||
                    !CommittedEffectRoot.TryGetComponent<CampfireVisualEffect>(out var effect) ||
                    !effect.ValidateReferences(out error))
                {
                    error ??= "临时建筑提交效果配置无效";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public void SetPreviewValidity(bool valid)
        {
            IsCommitted = false;
            RestoreDestroyHighlight();
            SetActive(ValidHighlight, valid);
            SetActive(InvalidHighlight, !valid);
            SetActive(DestroyHighlight, false);
            SetActive(CommittedEffectRoot, false);
            if (FootprintCollider != null)
            {
                FootprintCollider.isTrigger = true;
            }
            if (SeparatePhysicalColliders) SetPhysicalColliders(false);

            if (NavMeshObstacle != null)
            {
                NavMeshObstacle.enabled = false;
            }
        }

        public bool SetCommitted(bool blocksNavigation, out string error)
        {
            RestoreDestroyHighlight();
            SetActive(ValidHighlight, false);
            SetActive(InvalidHighlight, false);
            SetActive(DestroyHighlight, false);
            if (FootprintCollider != null)
            {
                FootprintCollider.isTrigger = SeparatePhysicalColliders || !blocksNavigation;
            }
            if (SeparatePhysicalColliders) SetPhysicalColliders(true);

            if (blocksNavigation && NavMeshObstacle == null)
            {
                error = "临时建筑阻挡导航但缺少导航网格障碍物";
                return false;
            }

            if (NavMeshObstacle != null)
            {
                NavMeshObstacle.carving = true;
                NavMeshObstacle.carveOnlyStationary = true;
                NavMeshObstacle.enabled = blocksNavigation;
            }

            SetActive(CommittedEffectRoot, true);
            IsCommitted = true;

            error = null;
            return true;
        }

        public void SetDestroyHighlight(bool active)
        {
            SetActive(DestroyHighlight, false);
            if (!active)
            {
                RestoreDestroyHighlight();
                return;
            }

            if (destroyHighlightActive)
            {
                return;
            }

            EnsureDestroyHighlightRenderers();
            destroyHighlightOriginalBlocks.Clear();
            for (var i = 0; i < destroyHighlightRenderers.Count; i++)
            {
                var renderer = destroyHighlightRenderers[i];
                if (renderer == null) continue;

                var supportsBaseColor = false;
                var supportsColor = false;
                var materials = renderer.sharedMaterials;
                for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    var material = materials[materialIndex];
                    if (material == null) continue;
                    supportsBaseColor |= material.HasProperty(BaseColorId);
                    supportsColor |= material.HasProperty(ColorId);
                }

                if (!supportsBaseColor && !supportsColor) continue;
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                destroyHighlightOriginalBlocks[renderer] = original;
                var tinted = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(tinted);
                if (supportsBaseColor) tinted.SetColor(BaseColorId, DestroyTint);
                if (supportsColor) tinted.SetColor(ColorId, DestroyTint);
                renderer.SetPropertyBlock(tinted);
            }

            destroyHighlightActive = true;
        }

        /// <summary>只控制实体子碰撞，不改变用于选择及占地的触发器。</summary>
        private void SetPhysicalColliders(bool enabled)
        {
            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (collider != FootprintCollider) collider.enabled = enabled;
        }

        private void OnDisable()
        {
            SetActive(CommittedEffectRoot, false);
            RestoreDestroyHighlight();
        }

        private void OnDestroy()
        {
            RestoreDestroyHighlight();
        }

        private void EnsureDestroyHighlightRenderers()
        {
            if (destroyHighlightRenderers != null) return;
            destroyHighlightRenderers = new List<Renderer>();
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || renderer.GetComponentInParent<Canvas>(true) != null) continue;
                if (IsInAuxiliaryHierarchy(renderer.transform, ValidHighlight) ||
                    IsInAuxiliaryHierarchy(renderer.transform, InvalidHighlight) ||
                    IsInAuxiliaryHierarchy(renderer.transform, DestroyHighlight)) continue;
                destroyHighlightRenderers.Add(renderer);
            }
        }

        private void RestoreDestroyHighlight()
        {
            if (!destroyHighlightActive && destroyHighlightOriginalBlocks.Count == 0) return;
            foreach (var pair in destroyHighlightOriginalBlocks)
            {
                if (pair.Key != null) pair.Key.SetPropertyBlock(pair.Value);
            }
            destroyHighlightOriginalBlocks.Clear();
            destroyHighlightActive = false;
        }

        private static bool IsInAuxiliaryHierarchy(Transform transform, GameObject auxiliaryRoot)
        {
            return auxiliaryRoot != null &&
                   (transform == auxiliaryRoot.transform || transform.IsChildOf(auxiliaryRoot.transform));
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}

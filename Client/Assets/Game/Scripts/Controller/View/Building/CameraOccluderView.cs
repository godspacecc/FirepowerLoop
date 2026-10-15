using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game
{
    /// <summary>只处理单个场景物件的相机遮挡表现；不改变碰撞、建筑状态或存档，没有独立 Update。</summary>
    [ExecuteAlways]
    public sealed class CameraOccluderView : MonoBehaviour
    {
        /// <summary>遮挡或进入棚内时保留 18% 不透明度，使玩家可见且建筑外形不会完全消失。</summary>
        private const float OccludedAlpha = .18f;
        /// <summary>遮挡时淡化的主体渲染器；显隐仍由物件生命周期或 LOD 管理。</summary>
        public Renderer[] BodyRenderers = Array.Empty<Renderer>();
        /// <summary>独立顶面渲染器；玩家进入室内时也只半透明，不关闭渲染。</summary>
        public Renderer[] RoofRenderers = Array.Empty<Renderer>();
        /// <summary>棚子/房间的室内范围，复用选取碰撞器；空值表示只有镜头遮挡淡化。</summary>
        public BoxCollider InteriorCollider;
        /// <summary>正式 Prefab 引用的透明 URP 变体，确保 Android 资源构建保留透明着色器路径。</summary>
        public Material TransparentMaterial;
        private readonly Dictionary<Renderer, FadeLease> leases = new();
        // 仅登记活动表现对象；相机按玩家所属 Scene 过滤，卸载/回收时自动移除。
        internal static readonly HashSet<CameraOccluderView> ActiveViews = new();
        /// <summary>活动对象变化时失效相机分区索引；静态地图对象的树冠范围在下次查询时重建。</summary>
        internal static int RegistryVersion { get; private set; }
        private float bodyAlpha = 1f;
        private float roofAlpha = 1f;
        private bool inside;
        public bool FullyVisible => !inside && bodyAlpha >= 1f && roofAlpha >= 1f;
        internal bool CanOcclude => !TryGetComponent<TemporaryBuildingView>(out var building) || building.IsCommitted;

        internal bool Obstructs(Ray sight, float distance)
        {
            return Intersects(BodyRenderers, sight, distance) || Intersects(RoofRenderers, sight, distance);
        }

        private static bool Intersects(Renderer[] renderers, Ray sight, float distance)
        {
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                var bounds = renderer.bounds;
                bounds.Expand(.5f); // 角色周围保留 0.25 米观察余量，使用树冠/屋顶而非树干实体碰撞。
                if (bounds.IntersectRay(sight, out var hitDistance) && hitDistance < distance - .05f) return true;
            }
            return false;
        }

        /// <summary>由当前玩家相机帧末调用；遮挡或进入室内时保留半透明外形，离开范围 0.3 米后恢复，使用未缩放时间淡化。</summary>
        public void Refresh(bool obstructed, Vector3 playerPosition, float deltaTime)
        {
            if (!isActiveAndEnabled || TransparentMaterial == null) { Restore(); return; }
            inside = ContainsPlayer(playerPosition, inside ? .3f : 0f);
            var step = Mathf.Max(0f, deltaTime) / .22f;
            bodyAlpha = Mathf.MoveTowards(bodyAlpha, obstructed ? OccludedAlpha : 1f, step);
            roofAlpha = Mathf.MoveTowards(roofAlpha, inside || obstructed ? OccludedAlpha : 1f, step);
            Apply(BodyRenderers, bodyAlpha);
            Apply(RoofRenderers, roofAlpha);
        }

        private bool ContainsPlayer(Vector3 point, float margin)
        {
            if (InteriorCollider == null || RoofRenderers.Length == 0) return false;
            var local = InteriorCollider.transform.InverseTransformPoint(point) - InteriorCollider.center;
            var size = InteriorCollider.size * .5f;
            var scale = InteriorCollider.transform.lossyScale;
            return Mathf.Abs(local.x) <= size.x + margin / Mathf.Max(.001f, Mathf.Abs(scale.x)) &&
                   Mathf.Abs(local.z) <= size.z + margin / Mathf.Max(.001f, Mathf.Abs(scale.z)) &&
                   Mathf.Abs(local.y) <= size.y + .4f / Mathf.Max(.001f, Mathf.Abs(scale.y));
        }

        private void Apply(Renderer[] renderers, float alpha)
        {
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (alpha >= 1f)
                {
                    if (leases.Remove(renderer, out var previous)) previous.Dispose();
                    continue;
                }
                if (!leases.TryGetValue(renderer, out var lease))
                {
                    lease = new FadeLease(renderer, TransparentMaterial);
                    leases.Add(renderer, lease);
                }
                lease.Apply(alpha);
            }
        }

        /// <summary>切视角、停用、回收或卸载时立即恢复原材质与阴影；不接管显隐，幂等释放实例材质。</summary>
        public void Restore()
        {
            foreach (var lease in leases.Values) lease.Dispose();
            leases.Clear(); inside = false; bodyAlpha = roofAlpha = 1f;
        }
        private void OnEnable() { if (ActiveViews.Add(this)) RegistryVersion++; }
        private void OnDisable() { if (ActiveViews.Remove(this)) RegistryVersion++; Restore(); }
        private void OnDestroy() { if (ActiveViews.Remove(this)) RegistryVersion++; Restore(); }

        /// <summary>静态物件改变位置或渲染器绑定后通知索引失效；不会改变对象显隐和玩法状态。</summary>
        public void InvalidateOcclusionBounds() => RegistryVersion++;

        internal Bounds GetOcclusionBounds()
        {
            var bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var renderer in BodyRenderers) if (renderer != null) bounds.Encapsulate(renderer.bounds);
            foreach (var renderer in RoofRenderers) if (renderer != null) bounds.Encapsulate(renderer.bounds);
            if (InteriorCollider != null) bounds.Encapsulate(InteriorCollider.bounds);
            bounds.Expand(1f); // 覆盖树冠检测余量和室内退出缓冲，不能只以树干中心选候选。
            return bounds;
        }

        private sealed class FadeLease : IDisposable
        {
            private readonly Renderer renderer;
            private readonly Material[] originals;
            private readonly Material[] instances;
            private readonly MaterialPropertyBlock originalBlock = new();
            private readonly MaterialPropertyBlock block = new();
            private readonly ShadowCastingMode shadows;
            private bool released;
            public FadeLease(Renderer renderer, Material template)
            {
                this.renderer = renderer; originals = renderer.sharedMaterials;
                instances = new Material[originals.Length];
                renderer.GetPropertyBlock(originalBlock);
                shadows = renderer.shadowCastingMode;
                for (var i = 0; i < instances.Length; i++)
                {
                    if (originals[i] == null) continue;
                    var material = new Material(originals[i]) { hideFlags = HideFlags.DontSave };
                    // 保留原纹理与光照参数，仅采用预先收录的透明表面状态。
                    foreach (var property in new[] { "_Surface", "_Blend", "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_ZWrite" })
                        if (material.HasProperty(property) && template.HasProperty(property)) material.SetFloat(property, template.GetFloat(property));
                    // 半透明叶片使用原纹理 Alpha 混合；裁剪会裁掉整片叶子，AlphaToMask 会过滤低透明度或将 Alpha 锐化回不透明。
                    if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                    if (material.HasProperty("_AlphaToMask")) material.SetFloat("_AlphaToMask", 0f);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    // SrcBlend=One 的模板需要预乘颜色，关键字必须与已复制的混合参数一致。
                    if (template.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")) material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    else material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = (int)RenderQueue.Transparent;
                    material.SetShaderPassEnabled("ShadowCaster", false);
                    instances[i] = material;
                }
                renderer.sharedMaterials = instances;
            }
            public void Apply(float alpha)
            {
                if (renderer == null) return;
                renderer.GetPropertyBlock(block);
                var color = block.HasColor("_BaseColor") ? block.GetColor("_BaseColor") :
                    originals.Length > 0 && originals[0] != null && originals[0].HasProperty("_BaseColor") ?
                    originals[0].GetColor("_BaseColor") : Color.white;
                color.a = alpha; block.SetColor("_BaseColor", color); renderer.SetPropertyBlock(block);
                // 淡化始终保留外形，不接管 enabled，避免恢复时覆盖 LODGroup 的网格选择。
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            public void Dispose()
            {
                if (released) return; released = true;
                if (renderer != null)
                {
                    renderer.sharedMaterials = originals;
                    // 高亮可能在淡化期间变化：保留当前 RGB 与其他属性，只恢复本适配器接管的 Alpha。
                    renderer.GetPropertyBlock(block);
                    if (block.HasColor("_BaseColor"))
                    {
                        var color = block.GetColor("_BaseColor");
                        color.a = originalBlock.HasColor("_BaseColor") ? originalBlock.GetColor("_BaseColor").a :
                            originals.Length > 0 && originals[0] != null && originals[0].HasProperty("_BaseColor") ?
                            originals[0].GetColor("_BaseColor").a : 1f;
                        block.SetColor("_BaseColor", color); renderer.SetPropertyBlock(block);
                    }
                    renderer.shadowCastingMode = shadows;
                }
                foreach (var material in instances)
                {
                    if (material == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                    else UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }
    }
}

using UnityEngine;

namespace Game
{
    /// <summary>将地图共享的视线数据绑定到墙体或遮盖室内的建筑表面。</summary>
    public sealed class SurvivorBuildingSurface : MonoBehaviour
    {
        /// <summary>由场地预制件引用，随场地一起收集的透明表面材质。</summary>
        public Material TransparentMaterial;
        /// <summary>屋顶依据其所覆盖的室内位置取样，实体墙依据自身位置取样。</summary>
        public bool RevealInterior = true;

        private const float RoofOpacity = .13f;
        private const float WallOpacity = .44f;
        private static readonly int SightId = Shader.PropertyToID("_BuildingSight");
        private static readonly int OriginId = Shader.PropertyToID("_BuildingSightOrigin");
        private static readonly int ConeId = Shader.PropertyToID("_BuildingSightCone");
        private static readonly int GroundId = Shader.PropertyToID("_BuildingSightGroundHeight");
        private static readonly int OpacityId = Shader.PropertyToID("_BuildingSightOpacity");

        private MeshRenderer surface;
        private Material original;
        private Material faded;
        private MaterialPropertyBlock block;
        private MaterialPropertyBlock originalBlock;
        private float opacity = 1f;

        internal bool IsInRange(Vector3 eye, float radius)
        {
            if (!isActiveAndEnabled || TransparentMaterial == null) return false;
            if (surface == null) surface = GetComponent<MeshRenderer>();
            if (surface == null) return false;
            var bounds = surface.bounds;
            return bounds.SqrDistance(new Vector3(eye.x, bounds.center.y, eye.z)) <= radius * radius;
        }

        /// <summary>仅根据共享场地视线改变本表面的材质表现。</summary>
        internal void ApplyVisibility(SurvivorVisibilityField sight, float seconds)
        {
            if (faded == null)
            {
                original = surface.sharedMaterial;
                faded = new Material(TransparentMaterial) { hideFlags = HideFlags.DontSave };
                faded.CopyMatchingPropertiesFromMaterial(original);
                block ??= new MaterialPropertyBlock();
                originalBlock ??= new MaterialPropertyBlock();
                surface.GetPropertyBlock(originalBlock);
                surface.sharedMaterial = faded;
            }
            opacity = Mathf.MoveTowards(opacity, RevealInterior ? RoofOpacity : WallOpacity, Mathf.Max(0f, seconds) * 4f);
            surface.GetPropertyBlock(block);
            block.SetTexture(SightId, sight.Texture);
            block.SetVector(OriginId, sight.Origin);
            block.SetVector(ConeId, sight.Cone);
            block.SetFloat(GroundId, sight.GroundHeight);
            block.SetFloat(OpacityId, opacity);
            surface.SetPropertyBlock(block);
        }

        /// <summary>还原原材质和原属性，避免卸载后残留透明设置。</summary>
        public void Restore()
        {
            if (faded == null) return;
            if (surface != null)
            {
                surface.sharedMaterial = original;
                surface.SetPropertyBlock(originalBlock);
            }
            if (Application.isPlaying) Destroy(faded);
            else DestroyImmediate(faded);
            faded = null;
            original = null;
            opacity = 1f;
        }

        private void OnDisable() => Restore();
    }
}

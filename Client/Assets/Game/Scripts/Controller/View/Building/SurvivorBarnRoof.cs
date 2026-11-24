using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    /// <summary>仅在玩家从圆形视野内能看到的位置淡化仓房屋顶。</summary>
    public sealed class SurvivorBarnRoof : MonoBehaviour
    {
        /// <summary>由农场预制件直接引用，随农场一起收集。</summary>
        public Material TransparentMaterial;

        private const int MaskSize = 128;
        private const float RevealOpacity = .10f;
        // 现有谷仓屋檐比室内墙体宽，屋外地面不应从屋檐上开洞。
        private const float EaveInset = .3f;
        private static readonly int MaskId = Shader.PropertyToID("_RoofVisibilityMask");
        private static readonly int AreaId = Shader.PropertyToID("_RoofVisibilityArea");
        private static readonly int EyeHeightId = Shader.PropertyToID("_RoofEyeHeight");
        private static readonly int OpacityId = Shader.PropertyToID("_RoofOpacity");

        private readonly List<Bounds> nearbyBlockers = new();
        private MeshRenderer roof;
        private Material original;
        private Material faded;
        private MaterialPropertyBlock block;
        private MaterialPropertyBlock originalBlock;
        private Texture2D mask;
        private byte[] pixels;
        private Vector3 lastEye;
        private float lastRadius = -1f;
        private bool anyVisible;

        /// <summary>无遮挡处的最低不透明度，屋顶未淡化时为 1。</summary>
        public float Opacity { get; private set; } = 1f;

        /// <summary>以眼高平面生成本屋的可见区域，立体障碍以实际高度参与遮挡。</summary>
        public void TickVisibility(Vector3 eye, float radius, IReadOnlyList<Bounds> blockers, float seconds)
        {
            if (!isActiveAndEnabled || TransparentMaterial == null || radius <= 0f) return;
            if (roof == null) roof = GetComponent<MeshRenderer>();
            if (roof == null) return;
            var footprint = roof.bounds;
            footprint.Expand(new Vector3(-2f * EaveInset, 0f, -2f * EaveInset));
            var min = footprint.min;
            var size = footprint.size;
            var eyeOnRoof = new Vector3(eye.x, footprint.center.y, eye.z);
            if (footprint.SqrDistance(eyeOnRoof) > radius * radius)
            {
                Restore();
                return;
            }

            // 场地阻挡在本局中固定；静止时复用当前范围，昼夜半径变化仍会重算。
            if ((lastEye - eye).sqrMagnitude >= .0004f || Mathf.Abs(lastRadius - radius) >= .02f)
            {
                lastEye = eye;
                lastRadius = radius;
                anyVisible = UpdateMask(eye, radius, blockers, min, size);
            }

            if (!anyVisible)
            {
                ReleaseMaterial();
                return;
            }

            if (faded == null)
            {
                original = roof.sharedMaterial;
                faded = new Material(TransparentMaterial) { hideFlags = HideFlags.DontSave };
                faded.CopyMatchingPropertiesFromMaterial(original);
                block ??= new MaterialPropertyBlock();
                originalBlock ??= new MaterialPropertyBlock();
                roof.GetPropertyBlock(originalBlock);
                roof.sharedMaterial = faded;
            }
            Opacity = Mathf.MoveTowards(Opacity, RevealOpacity, Mathf.Max(0f, seconds) * 4f);
            roof.GetPropertyBlock(block);
            block.SetTexture(MaskId, mask);
            block.SetVector(AreaId, new Vector4(min.x, min.z, 1f / size.x, 1f / size.z));
            block.SetFloat(EyeHeightId, eye.y);
            block.SetFloat(OpacityId, Opacity);
            roof.SetPropertyBlock(block);
        }

        /// <summary>单通道贴图把对应地面位置的可见性映射到屋顶。</summary>
        private bool UpdateMask(Vector3 eye, float radius, IReadOnlyList<Bounds> blockers, Vector3 min, Vector3 size)
        {
            nearbyBlockers.Clear();
            float radiusSquared = radius * radius;
            // 只有位于玩家与本屋之间的包围盒才可能截断这些视线。
            var lower = Vector3.Min(eye, min);
            var upper = Vector3.Max(eye, min + size);
            foreach (var bounds in blockers)
            {
                var low = bounds.min;
                var high = bounds.max;
                if (eye.y >= low.y && eye.y <= high.y && lower.x <= high.x && upper.x >= low.x &&
                    lower.z <= high.z && upper.z >= low.z && bounds.SqrDistance(eye) <= radiusSquared)
                    nearbyBlockers.Add(bounds);
            }
            pixels ??= new byte[MaskSize * MaskSize];
            bool visible = false;
            for (int z = 0; z < MaskSize; z++)
            {
                for (int x = 0; x < MaskSize; x++)
                {
                    float dx = min.x + (x + .5f) * size.x / MaskSize - eye.x;
                    float dz = min.z + (z + .5f) * size.z / MaskSize - eye.z;
                    bool seen = dx * dx + dz * dz <= radiusSquared && !Blocked(eye, dx, dz);
                    pixels[z * MaskSize + x] = seen ? byte.MaxValue : (byte)0;
                    visible |= seen;
                }
            }
            if (!visible) return false;
            mask ??= new Texture2D(MaskSize, MaskSize, TextureFormat.R8, false, true)
            {
                name = "仓房屋顶视野", wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            mask.SetPixelData(pixels, 0);
            mask.Apply(false, false);
            return true;
        }

        /// <summary>在眼高处沿视线检测实体，保留门洞及低于视线的障碍。</summary>
        private bool Blocked(Vector3 eye, float dx, float dz)
        {
            // 倒数在每条视线上只计算一次；平行轴单独判断，避免边界处产生 NaN。
            float inverseX = dx == 0 ? 0 : 1f / dx;
            float inverseZ = dz == 0 ? 0 : 1f / dz;
            foreach (var bounds in nearbyBlockers)
            {
                var min = bounds.min;
                var max = bounds.max;
                float enter = 0f, exit = 1f;
                if (ClipAxis(eye.x, inverseX, min.x, max.x, ref enter, ref exit) &&
                    ClipAxis(eye.z, inverseZ, min.z, max.z, ref enter, ref exit))
                    return true;
            }
            return false;
        }

        private static bool ClipAxis(float origin, float inverse, float min, float max, ref float enter, ref float exit)
        {
            if (inverse == 0f) return origin >= min && origin <= max;
            float a = (min - origin) * inverse;
            float b = (max - origin) * inverse;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            return enter <= exit;
        }

        /// <summary>恢复原屋顶材质，并归还场地实例持有的临时贴图。</summary>
        public void Restore()
        {
            ReleaseMaterial();
            Release(mask);
            mask = null;
            lastRadius = -1f;
            anyVisible = false;
        }

        private void ReleaseMaterial()
        {
            Opacity = 1f;
            if (faded == null) return;
            if (roof != null)
            {
                roof.sharedMaterial = original;
                roof.SetPropertyBlock(originalBlock);
            }
            Release(faded);
            faded = null;
            original = null;
        }

        private static void Release(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else DestroyImmediate(asset);
        }

        private void OnDisable() => Restore();
    }
}

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Game
{
    /// <summary>负责拾取物模型、名称及街机光柱的实例生命周期；不裁决拾取、掉落价值或强化规则。</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ResourcePickupView : MonoBehaviour, IPlayerInteractable
    {
        /// <summary>描述某个掉落模型在俯视场景中的局部展示姿态，不负责修改资源本体。</summary>
        [Serializable]
        private sealed class ArcadeDropVisualPose
        {
            [SerializeField]
            private string prefabAddress = string.Empty;

            [SerializeField]
            private Vector3 localPosition;

            [SerializeField]
            private Vector3 localEulerAngles;

            public string PrefabAddress => prefabAddress;
            public Vector3 LocalPosition => localPosition;
            public Vector3 LocalEulerAngles => localEulerAngles;
        }

        /// <summary>按资源地址选择光柱外观，未匹配的街机道具使用默认光柱。</summary>
        [Serializable]
        private sealed class ArcadeLootBeamBinding
        {
            [SerializeField] private string[] visualAddresses = Array.Empty<string>();
            [SerializeField] private GameObject beamPrefab;

            public GameObject Resolve(string address)
            {
                foreach (var candidate in visualAddresses)
                    if (string.Equals(candidate, address, StringComparison.Ordinal)) return beamPrefab;
                return null;
            }
        }

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField]
        private GameObject nameRoot;

        [SerializeField]
        private TMP_Text nameLabel;

        [SerializeField]
        private Renderer defaultRenderer;

        /// <summary>仅调整掉落实例的展示姿态，不改变资源 Prefab、拾取根节点或碰撞体。</summary>
        [SerializeField]
        private ArcadeDropVisualPose[] arcadeVisualPoses = Array.Empty<ArcadeDropVisualPose>();

        /// <summary>街机专用光柱；普通远征拾取物不会实例化，随模型替换或回收释放。</summary>
        [SerializeField] private GameObject arcadeLootBeamPrefab;
        [SerializeField] private ArcadeLootBeamBinding[] arcadeLootBeamBindings = Array.Empty<ArcadeLootBeamBinding>();

        private string currentDisplayName = string.Empty;
        private int currentDisplayAmount = 1;
        private GameObject activeArcadeVisual;
        private GameObject activeArcadeLootBeam;
        /// <summary>正式拾取模板持有箱体；所有实例、回池与地图释放继续归本 View 管理。</summary>
        [SerializeField] private ArcadeWeaponDeliveryView weaponDelivery;
        /// <summary>已确认空投展示最长边；普通击杀物资不使用此尺寸。</summary>
        [SerializeField] private float deliveredWeaponMaxSize = 1.7f;
        /// <summary>仅在开箱期间应用的展示姿态，回到普通掉落时恢复原姿态。</summary>
        [SerializeField] private ArcadeDropVisualPose[] weaponDeliveryPoses = Array.Empty<ArcadeDropVisualPose>();
        private bool deliveryActive;
        private Vector3 deliveryWeaponPosition;
        /// <summary>保存空投前的姿态，普通掉落复用时还原，避免沿用箱内展示尺寸。</summary>
        private Vector3 deliveryOriginalWeaponPosition;
        private Vector3 deliveryOriginalWeaponScale;
        private Quaternion deliveryOriginalWeaponRotation;

        /// <summary>模型与光柱各自持有渐隐材质管理器，统一随展示生命周期释放。</summary>
        private ArcadeDropOpacity visualOpacity;
        private ArcadeDropOpacity beamOpacity;
        /// <summary>保存初始名称透明度，反复同步按基准缩放而不是累乘。</summary>
        private float nameBaseAlpha = 1f;
        private bool nameAlphaCaptured;
        private float arcadeOpacity = 1f;

        public event Action<ResourcePickupView, Collider> Triggered;
        public event Action<ResourcePickupView, Collider> RangeExited;
        public event Action InteractionRequested;
        /// <summary>Controller 同步的范围/背包可操作镜像，不裁决实际拾取结果。</summary>
        private bool interactionAvailable;
        public string DisplayName => FormatDisplayName(currentDisplayName, currentDisplayAmount);
        public bool CanInteract => isActiveAndEnabled && interactionAvailable && InteractionRequested != null;
        public void SetInteractionAvailable(bool available) => interactionAvailable = available;
        public void SetFocused(bool focused) { }
        public void Interact() { if (CanInteract) InteractionRequested?.Invoke(); }
        public GameObject NameRoot => nameRoot;
        public TMP_Text NameLabel => nameLabel;
        public string ActiveArcadeVisualPrefabAddress { get; private set; }

        public static string FormatDisplayName(string name, int amount)
        {
            var safeName = string.IsNullOrWhiteSpace(name) ? string.Empty : name;
            var safeAmount = Mathf.Max(1, amount);
            return safeAmount > 1 ? $"{safeName}x{safeAmount}" : safeName;
        }

        public void RefreshDisplay(string itemDisplayName, int displayAmount)
        {
            currentDisplayName = string.IsNullOrWhiteSpace(itemDisplayName) ? string.Empty : itemDisplayName;
            currentDisplayAmount = Mathf.Max(1, displayAmount);
            RefreshNameText(currentDisplayName, currentDisplayAmount);
        }

        /// <summary>
        /// 按配置地址实例化地面掉落模型。空地址或空 Prefab 保留通用方块；相同地址重复同步保持幂等。
        /// </summary>
        /// <returns>成功启用专用模型时返回 true，否则返回 false。</returns>
        public bool SetArcadePresentation(GameObject visualPrefab, string visualPrefabAddress)
        {
            if (activeArcadeVisual != null &&
                string.Equals(ActiveArcadeVisualPrefabAddress, visualPrefabAddress, StringComparison.Ordinal))
            {
                return true;
            }

            ClearArcadePresentation();
            if (visualPrefab == null || string.IsNullOrWhiteSpace(visualPrefabAddress))
            {
                return false;
            }

            activeArcadeVisual = Instantiate(visualPrefab, transform, false);
            activeArcadeVisual.name = "ArcadeDropVisual";
            ApplyArcadeVisualPose(activeArcadeVisual.transform, visualPrefabAddress);
            ActiveArcadeVisualPrefabAddress = visualPrefabAddress;
            CreateArcadeLootBeam(visualPrefabAddress);
            if (defaultRenderer != null) defaultRenderer.enabled = false;
            return true;
        }

        /// <summary>按权威空投阶段同步武器弹出与箱体消失；完成后显示光柱，不改变拾取根位置。</summary>
        public void SetWeaponDelivery(bool airdrop,float remaining,float duration)
        {
            if(airdrop&&weaponDelivery!=null)
            {
                if(!deliveryActive&&activeArcadeVisual!=null)
                {
                    deliveryOriginalWeaponPosition=activeArcadeVisual.transform.localPosition;
                    deliveryOriginalWeaponScale=activeArcadeVisual.transform.localScale;
                    deliveryOriginalWeaponRotation=activeArcadeVisual.transform.localRotation;
                    ApplyVisualPose(weaponDeliveryPoses,activeArcadeVisual.transform,ActiveArcadeVisualPrefabAddress);
                    FitArcadeVisual(deliveredWeaponMaxSize);
                    deliveryWeaponPosition=activeArcadeVisual.transform.localPosition;
                }
                deliveryActive=true;weaponDelivery.SetProgress(remaining,duration);
                if(activeArcadeVisual!=null){activeArcadeVisual.SetActive(weaponDelivery.WeaponVisible);activeArcadeVisual.transform.localPosition=deliveryWeaponPosition+Vector3.up*((.65f+weaponDelivery.WeaponPopLift)/Mathf.Max(.001f,Mathf.Abs(transform.lossyScale.y)));}
                if(activeArcadeLootBeam!=null)activeArcadeLootBeam.SetActive(remaining<=0);
                SetNameVisible(remaining<=0&&!string.IsNullOrWhiteSpace(currentDisplayName));
                return;
            }
            if(deliveryActive&&activeArcadeVisual!=null)
            {
                activeArcadeVisual.SetActive(true);
                activeArcadeVisual.transform.localPosition=deliveryOriginalWeaponPosition;
                activeArcadeVisual.transform.localScale=deliveryOriginalWeaponScale;
                activeArcadeVisual.transform.localRotation=deliveryOriginalWeaponRotation;
            }
            if(deliveryActive&&activeArcadeLootBeam!=null)activeArcadeLootBeam.SetActive(true);
            deliveryActive=false;weaponDelivery?.ResetPresentation();
        }

        /// <summary>按展示请求校准模型实例：最长边统一、底面抬至掉落根；重复应用不会累积缩放。</summary>
        public void FitArcadeVisual(float maxSize)
        {
            if (activeArcadeVisual == null || maxSize <= 0 || float.IsNaN(maxSize) || float.IsInfinity(maxSize)) return;
            var bounds = new Bounds(); bool found = false;
            foreach (var renderer in activeArcadeVisual.GetComponentsInChildren<Renderer>())
            {
                // 枪口粒子的动态包围盒不代表武器实体尺寸。
                if (renderer is ParticleSystemRenderer) continue;
                var local = renderer.localBounds;
                var matrix = transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = matrix.MultiplyPoint3x4(corner);
                    if (found) bounds.Encapsulate(point); else { bounds = new Bounds(point, Vector3.zero); found = true; }
                }
            }
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (!found || longest < .0001f) return;
            var rootScale = transform.lossyScale;
            float rootSize = Mathf.Max(Mathf.Abs(rootScale.x), Mathf.Abs(rootScale.y), Mathf.Abs(rootScale.z));
            if (rootSize < .0001f) return;
            float scale = maxSize / (longest * rootSize);
            var visual = activeArcadeVisual.transform;
            visual.localScale *= scale;
            // 以整个包围盒底面定位，兼容源模型枢轴及已有俯视展示姿态。
            visual.localPosition = visual.localPosition * scale - new Vector3(0, bounds.min.y * scale, 0);
        }

        /// <summary>释放当前实例化视觉并恢复通用方块；可安全重复调用。</summary>
        public void ClearArcadePresentation()
        {
            deliveryActive=false;weaponDelivery?.ResetPresentation();
            ReleaseOpacityMaterials();
            ActiveArcadeVisualPrefabAddress = null;
            if (activeArcadeLootBeam != null)
            {
                activeArcadeLootBeam.SetActive(false);
                if (Application.isPlaying) Destroy(activeArcadeLootBeam); else DestroyImmediate(activeArcadeLootBeam);
                activeArcadeLootBeam = null;
            }
            if (activeArcadeVisual != null)
            {
                activeArcadeVisual.SetActive(false);
                if (Application.isPlaying) Destroy(activeArcadeVisual); else DestroyImmediate(activeArcadeVisual);
                activeArcadeVisual = null;
            }

            if (defaultRenderer != null) defaultRenderer.enabled = true;
        }

        /// <summary>对模型、名称和光柱统一渐隐，不改变 Collider 或交互状态；首次渐隐时才建立实例材质缓存。</summary>
        public void SetArcadeOpacity(float opacity)
        {
            opacity = float.IsNaN(opacity) ? 1f : Mathf.Clamp01(opacity);
            if (Mathf.Approximately(arcadeOpacity, opacity)) return;
            if (!nameAlphaCaptured && nameLabel != null)
            {
                nameBaseAlpha = nameLabel.alpha;
                nameAlphaCaptured = true;
            }

            if (visualOpacity == null && opacity < 1f)
            {
                var modelRoot = activeArcadeVisual != null ? activeArcadeVisual : defaultRenderer != null ? defaultRenderer.gameObject : null;
                if (modelRoot != null) visualOpacity = new ArcadeDropOpacity(modelRoot);
                if (activeArcadeLootBeam != null) beamOpacity = new ArcadeDropOpacity(activeArcadeLootBeam);
            }

            arcadeOpacity = opacity;
            if (nameLabel != null) nameLabel.alpha = nameBaseAlpha * opacity;
            visualOpacity?.SetOpacity(opacity);
            beamOpacity?.SetOpacity(opacity);
        }

        /// <summary>归还材质并恢复名称，支持重复清理和对象池复用。</summary>
        private void ReleaseOpacityMaterials()
        {
            visualOpacity?.Dispose();
            beamOpacity?.Dispose();
            visualOpacity = null;
            beamOpacity = null;
            arcadeOpacity = 1f;
            if (nameAlphaCaptured && nameLabel != null) nameLabel.alpha = nameBaseAlpha;
        }

        public void SetNameVisible(bool visible)
        {
            if (nameRoot != null && nameRoot.activeSelf != visible)
            {
                nameRoot.SetActive(visible);
            }
        }

        /// <summary>在相机姿态确定后使名称与屏幕平行；不改变物品模型、位置或名称显隐。</summary>
        /// <param name="cameraForward">相机世界空间前方向，不是相机位置。</param>
        /// <param name="cameraUp">相机世界空间上方向。</param>
        public void FaceName(Vector3 cameraForward, Vector3 cameraUp)
        {
            if (nameRoot == null || !nameRoot.activeSelf)
            {
                return;
            }

            if (cameraForward.sqrMagnitude <= 0.0001f || cameraUp.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            nameRoot.transform.rotation = Quaternion.LookRotation(cameraForward, cameraUp);
        }

        private void Awake()
        {
            RefreshNameText(currentDisplayName, currentDisplayAmount);
            SetNameVisible(false);
        }

        private void OnEnable()
        {
            RefreshNameText(currentDisplayName, currentDisplayAmount);
            SetNameVisible(false);
        }

        private void OnDisable()
        {
            interactionAvailable = false;
            SetNameVisible(false);
            ClearArcadePresentation();
        }

        private void OnDestroy() => ClearArcadePresentation();

        private void Reset()
        {
            var trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Triggered?.Invoke(this, other);
        }

        private void OnTriggerExit(Collider other) => RangeExited?.Invoke(this, other);

        private void RefreshNameText(string itemDisplayName, int displayAmount)
        {
            if (nameLabel != null)
            {
                nameLabel.text = FormatDisplayName(itemDisplayName, displayAmount);
            }
        }

        /// <summary>按资源地址应用俯视角展示姿态；未配置的资源保持原始朝向。</summary>
        private void ApplyArcadeVisualPose(Transform visualTransform, string visualPrefabAddress)
            => ApplyVisualPose(arcadeVisualPoses, visualTransform, visualPrefabAddress);

        /// <summary>按显式配置应用实例姿态，不从物品名字猜测展示方式。</summary>
        private static void ApplyVisualPose(ArcadeDropVisualPose[] poses,Transform visualTransform,string visualPrefabAddress)
        {
            foreach (var pose in poses)
            {
                if (pose == null ||
                    !string.Equals(pose.PrefabAddress, visualPrefabAddress, StringComparison.Ordinal))
                {
                    continue;
                }

                visualTransform.localPosition = pose.LocalPosition;
                visualTransform.localRotation = Quaternion.Euler(pose.LocalEulerAngles);
                return;
            }
        }

        /// <summary>成功启用街机模型后创建一次光柱；绑定只选择外观，不从地址推断游戏规则。</summary>
        private void CreateArcadeLootBeam(string visualAddress)
        {
            var prefab = arcadeLootBeamPrefab;
            foreach (var binding in arcadeLootBeamBindings)
            {
                var candidate = binding?.Resolve(visualAddress);
                if (candidate == null) continue;
                prefab = candidate;
                break;
            }

            if (prefab == null) return;
            activeArcadeLootBeam = Instantiate(prefab, transform, false);
            activeArcadeLootBeam.name = "ArcadeLootBeam";
        }
    }

    /// <summary>街机物资独占的渐隐材质生命周期；适用于普通掉落和宝藏物资，不持有规则或逐帧回调。</summary>
    internal sealed class ArcadeDropOpacity : IDisposable
    {
        /// <summary>每个渲染器的原始引用与渐隐副本；由表现所有者独占并随展示回收。</summary>
        private sealed class OpacityRenderer
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Instances;
            public Color[] BaseColors;
            public Color[] Colors;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<OpacityRenderer> opacityRenderers = new List<OpacityRenderer>();
        /// <summary>只扫描此物资的子层级，避免宝藏出口等邻居一起渐隐。</summary>
        private readonly GameObject root;
        /// <summary>即使没有渲染器也只初始化一次；释放后拒绝重新创建副本。</summary>
        private bool initialized;
        private bool disposed;
        /// <summary>跳过相同透明度的重复同步，避免不必要的材质写入。</summary>
        private float currentOpacity = 1f;

        /// <summary>绑定单个物资根节点，首次渐隐前保持原始共享材质。</summary>
        public ArcadeDropOpacity(GameObject root) => this.root = root;

        /// <summary>按初始颜色缩放透明度，首次扫描后不再分配或遍历层级。</summary>
        public void SetOpacity(float opacity)
        {
            opacity = float.IsNaN(opacity) ? 1f : Mathf.Clamp01(opacity);
            if (disposed || root == null || Mathf.Approximately(currentOpacity, opacity)) return;
            if (!initialized && opacity < 1f)
            {
                initialized = true;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    // 名称由 View 的 TMP alpha 管理，不复制字体材质。
                    if (renderer.GetComponent<TMP_Text>() == null) CacheOpacityRenderer(renderer);
                }
            }
            currentOpacity = opacity;
            foreach (var entry in opacityRenderers)
            {
                for (var i = 0; i < entry.Instances.Length; i++)
                {
                    var material = entry.Instances[i];
                    if (material == null) continue;
                    if (material.HasProperty(BaseColorId))
                    {
                        var color = entry.BaseColors[i];
                        color.a *= opacity;
                        material.SetColor(BaseColorId, color);
                    }
                    if (material.HasProperty(ColorId))
                    {
                        var color = entry.Colors[i];
                        color.a *= opacity;
                        material.SetColor(ColorId, color);
                    }
                }
            }
        }

        /// <summary>复制完整材质以保留贴图、色彩与粒子混合方式，绝不修改共享资源。</summary>
        private void CacheOpacityRenderer(Renderer renderer)
        {
            var originals = renderer.sharedMaterials;
            var entry = new OpacityRenderer
            {
                Renderer = renderer,
                Originals = originals,
                Instances = new Material[originals.Length],
                BaseColors = new Color[originals.Length],
                Colors = new Color[originals.Length]
            };
            for (var i = 0; i < originals.Length; i++)
            {
                var source = originals[i];
                if (source == null) continue;
                var material = new Material(source) { name = source.name + " (ArcadeDropOpacity)", hideFlags = HideFlags.DontSave };
                entry.Instances[i] = material;
                if (material.HasProperty(BaseColorId)) entry.BaseColors[i] = material.GetColor(BaseColorId);
                if (material.HasProperty(ColorId)) entry.Colors[i] = material.GetColor(ColorId);
                // 正式模型为 URP Lit 不透明材质；光柱已使用 SrcAlpha/One 加法，必须保留其现有混合。
                if (material.HasProperty("_Surface") && material.GetFloat("_Surface") < 0.5f)
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (material.HasProperty("_SrcBlendAlpha")) material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
                    if (material.HasProperty("_DstBlendAlpha")) material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetFloat("_ZWrite", 0f);
                    if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.DisableKeyword("_ALPHAMODULATE_ON");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    material.SetShaderPassEnabled("ShadowCaster", false);
                    material.SetShaderPassEnabled("DepthOnly", false);
                    material.SetShaderPassEnabled("DepthNormals", false);
                }
            }
            renderer.sharedMaterials = entry.Instances;
            opacityRenderers.Add(entry);
        }

        /// <summary>先归还原始引用再销毁独占副本，支持 Clear、禁用、销毁及对象池复用。</summary>
        public void Dispose()
        {
            foreach (var entry in opacityRenderers)
            {
                if (entry.Renderer != null) entry.Renderer.sharedMaterials = entry.Originals;
                foreach (var material in entry.Instances)
                {
                    if (material == null) continue;
                    if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material);
                }
            }
            opacityRenderers.Clear();
            disposed = true;
        }
    }
}

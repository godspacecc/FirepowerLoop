using System;
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

        /// <summary>释放当前实例化视觉并恢复通用方块；可安全重复调用。</summary>
        public void ClearArcadePresentation()
        {
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
        {
            foreach (var pose in arcadeVisualPoses)
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
}

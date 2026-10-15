using QFramework;
using UnityEngine;

namespace Game
{
    [RequireComponent(typeof(BoxCollider))]
    /// <summary>
    /// 提供基地交互物的触发范围、焦点名称显隐和屏幕朝向；焦点权威由交互 System 持有，具体动作由子类实现。
    /// </summary>
    public abstract class PlayerInteractableView : MonoBehaviour, IController, IPlayerInteractable
    {
        [SerializeField]
        private string displayName;

        [SerializeField]
        private GameObject nameRoot;

        private PlayerInteractionSystem interactionSystem;
        /// <summary>当前启用周期的相机查询入口；禁用时清空，避免帧末回调保留失效绑定。</summary>
        private PlayerCameraSystem cameraSystem;
        private bool focused;

        public virtual string DisplayName => displayName;
        public virtual bool CanInteract => isActiveAndEnabled;
        /// <summary>仅供派生 View 调整已序列化的世界提示，不暴露给领域规则。</summary>
        protected GameObject FocusedPresentationRoot => nameRoot;

        public IArchitecture GetArchitecture()
        {
            return Game.Interface;
        }

        public abstract void Interact();

        public void SetFocused(bool focused)
        {
            this.focused = focused;
            RefreshFocusedPresentation();
        }

        /// <summary>
        /// 子类只覆写焦点提示的显示条件，不改变交互系统持有的真实焦点。
        /// </summary>
        protected virtual bool ShouldShowFocusedPresentation => true;

        /// <summary>
        /// 输入模式等纯表现条件变化时，按当前真实焦点重新计算提示显隐。
        /// </summary>
        protected void RefreshFocusedPresentation()
        {
            var visible = focused && ShouldShowFocusedPresentation && interactionSystem?.UsesCrosshairTargeting != true;
            if (nameRoot != null && nameRoot.activeSelf != visible)
            {
                nameRoot.SetActive(visible);
            }
        }

        protected virtual void Reset()
        {
            var trigger = GetComponent<Collider>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }

        protected virtual void Awake()
        {
            if (nameRoot != null)
            {
                nameRoot.SetActive(false);
            }
        }

        protected virtual void OnEnable()
        {
            interactionSystem = this.GetSystem<PlayerInteractionSystem>();
            cameraSystem = this.GetSystem<PlayerCameraSystem>();
            LifeCycle.RegisterLateUpdate(QueueNameFacing);
        }

        protected virtual void OnDisable()
        {
            LifeCycle.UnregisterLateUpdate(QueueNameFacing);
            cameraSystem = null;
            SetFocused(false);
            try
            {
                interactionSystem?.Blur(this);
            }
            finally
            {
                interactionSystem = null;
            }
        }

        /// <summary>仅旋转可见名称，使其与屏幕平行；不改变交互焦点、物件模型或位置。</summary>
        public void FaceName(Vector3 cameraForward, Vector3 cameraUp)
        {
            if (nameRoot == null || !nameRoot.activeSelf ||
                cameraForward.sqrMagnitude <= 0.0001f || cameraUp.sqrMagnitude <= 0.0001f) return;
            nameRoot.transform.rotation = Quaternion.LookRotation(cameraForward, cameraUp);
        }

        /// <summary>延迟到共享帧末，确保相机及 NPC 的既有朝向更新均已完成。</summary>
        private void QueueNameFacing() => LifeCycle.ScheduleFrameEnd(this, RefreshNameFacing);

        /// <summary>禁用后到达的帧末回调直接忽略，不重新获取架构或恢复隐藏名称。</summary>
        private void RefreshNameFacing()
        {
            if (this != null) RefreshFocusedPresentation();
            if (this == null || !isActiveAndEnabled || cameraSystem == null ||
                nameRoot == null || !nameRoot.activeSelf) return;
            if (cameraSystem.TryGetScreenOrientation(out var forward, out var up)) FaceName(forward, up);
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerView>() != null)
            {
                interactionSystem?.Focus(this);
            }
        }

        protected virtual void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerView>() != null)
            {
                interactionSystem?.Blur(this);
            }
        }

        public void ConfigureForTests(GameObject configuredNameRoot, string configuredDisplayName)
        {
            nameRoot = configuredNameRoot;
            displayName = configuredDisplayName;
            focused = false;
            if (nameRoot != null)
            {
                nameRoot.SetActive(false);
            }
        }

        public void SetFocusedForTests(bool focused)
        {
            SetFocused(focused);
        }
    }
}

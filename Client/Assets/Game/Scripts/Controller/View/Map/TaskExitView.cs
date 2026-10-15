using System;
using QFramework;
using TMPro;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 只维护撤离点的焦点、提示与可交互表现，并发布原始范围和交互请求。
    /// 玩家身份、撤离资格、计时、返回及结算均由上层 Controller/System 决定。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TaskExitView : MonoBehaviour, IController, IPlayerInteractable
    {
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private TMP_Text promptText;

        private PlayerInteractionSystem interactionSystem;

        // 这些字段是 Controller/System 结果的表现镜像；CanInteract 仅用于焦点和输入门禁，并非业务权威状态。
        // 禁用时必须 Blur 并清空全部镜像，避免对象隐藏后仍残留交互焦点。
        private bool available;
        private bool playerInside;
        private bool countdownActive;
        private bool returning;
        private bool focused;

        public event Action<TaskExitView, Collider> RangeEntered;
        public event Action<TaskExitView, Collider> RangeExited;
        public event Action<TaskExitView> InteractionRequested;

        public string DisplayName => promptText != null ? promptText.text : string.Empty;
        public bool CanInteract => isActiveAndEnabled && available && playerInside && !countdownActive && !returning;
        public bool HasVisualRoot => visualRoot != null;
        public bool HasPromptRoot => promptRoot != null;
        public bool HasPromptText => promptText != null;

        public IArchitecture GetArchitecture() => Game.Interface;

        private void Reset()
        {
            var trigger = GetComponent<Collider>();
            if (trigger != null) trigger.isTrigger = true;
        }

        private void Awake()
        {
            if (promptRoot != null) promptRoot.SetActive(false);
        }

        private void OnEnable()
        {
            interactionSystem = this.GetSystem<PlayerInteractionSystem>();
            RefreshFocusRegistration();
        }

        private void OnDisable()
        {
            available = false;
            playerInside = false;
            countdownActive = false;
            returning = false;
            focused = false;
            if (promptRoot != null) promptRoot.SetActive(false);
            try { interactionSystem?.Blur(this); }
            finally { interactionSystem = null; }
        }

        private void OnTriggerEnter(Collider other) => RangeEntered?.Invoke(this, other);
        private void OnTriggerExit(Collider other) => RangeExited?.Invoke(this, other);

        public void Interact()
        {
            if (CanInteract) InteractionRequested?.Invoke(this);
        }

        public void SetFocused(bool value)
        {
            focused = value;
            RefreshPromptVisibility();
        }

        public void SetAvailable(bool value)
        {
            available = value;
            if (visualRoot != null && visualRoot.activeSelf != value) visualRoot.SetActive(value);
            RefreshFocusRegistration();
        }

        public void SetPlayerInside(bool value)
        {
            playerInside = value;
            RefreshFocusRegistration();
        }

        public void SetCountdownActive(bool value)
        {
            countdownActive = value;
            RefreshFocusRegistration();
        }

        public void SetReturning(bool value)
        {
            returning = value;
            RefreshFocusRegistration();
        }

        public void SetPromptText(string text)
        {
            if (promptText != null) promptText.text = text ?? string.Empty;
        }

        public void FacePrompt(Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (promptRoot == null || !promptRoot.activeSelf) return;
            var direction = promptRoot.transform.position - cameraPosition;
            if (direction.sqrMagnitude > 0.0001f)
                promptRoot.transform.rotation = Quaternion.LookRotation(direction, cameraUp);
        }

        public bool TryValidatePresentation(out string error)
        {
            if (visualRoot == null) { error = "TaskExit Prefab 缺少 visualRoot"; return false; }
            if (promptRoot == null) { error = "TaskExit Prefab 缺少 PromptRoot"; return false; }
            if (promptText == null || !promptText.transform.IsChildOf(promptRoot.transform))
            { error = "TaskExit Prefab 缺少 PromptRoot 下的 PromptText"; return false; }
            var canvas = promptRoot.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            { error = "TaskExit PromptRoot 必须使用 World Space Canvas"; return false; }
            error = null;
            return true;
        }

        private void RefreshFocusRegistration()
        {
            if (CanInteract) interactionSystem?.Focus(this);
            else interactionSystem?.Blur(this);
            RefreshPromptVisibility();
        }

        private void RefreshPromptVisibility()
        {
            if (promptRoot == null) return;
            var visible = countdownActive || focused && CanInteract && interactionSystem?.UsesCrosshairTargeting != true;
            if (promptRoot.activeSelf != visible) promptRoot.SetActive(visible);
        }
    }
}

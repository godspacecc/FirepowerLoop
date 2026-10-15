using System;
using QFramework;
using TMPro;
using UnityEngine;

namespace Game
{
    [RequireComponent(typeof(Collider))]
    /// <summary>
    /// 负责 NPC 世界提示和原始交互回调；手机端只展示名字，不决定交互焦点或业务执行。
    /// </summary>
    public sealed class NpcView : PlayerInteractableView
    {
        [SerializeField]
        private GameObject promptRoot;

        [SerializeField]
        private TMP_Text promptText;

        private MobileInputSystem mobileInputSystem;
        private IUnRegister mobileInputModeChangedUnregister;
        private bool mobileInputMode;
        /// <summary>由 NPC 控制器提供的本地化表现文本，随当前 View 生命周期持有，不进入存档。</summary>
        private string desktopPrompt;
        /// <summary>同一 NPC 的当前语言名字；手机提示不包含电脑按键说明。</summary>
        private string mobileName;
        public override string DisplayName => mobileName ?? base.DisplayName;
        public event Action<NpcView> InteractionRequested;

        protected override void OnEnable()
        {
            base.OnEnable();
            mobileInputSystem = this.GetSystem<MobileInputSystem>();
            mobileInputMode = mobileInputSystem?.IsMobileInputMode == true;
            mobileInputModeChangedUnregister = this.RegisterEvent<MobileInputModeChangedEvent>(
                OnMobileInputModeChanged);
            RefreshPromptText();
            RefreshFocusedPresentation();
        }

        protected override void OnDisable()
        {
            mobileInputModeChangedUnregister?.UnRegister();
            mobileInputModeChangedUnregister = null;
            base.OnDisable();
            mobileInputSystem = null;
            mobileInputMode = false;
        }

        /// <summary>手机输入模式切换只刷新世界提示，不改变 NPC 焦点。</summary>
        private void OnMobileInputModeChanged(MobileInputModeChangedEvent e)
        {
            mobileInputMode = e.Enabled;
            RefreshPromptText();
            RefreshFocusedPresentation();
        }

        public override void Interact()
        {
            InteractionRequested?.Invoke(this);
        }

        public void SetPromptText(string text)
        {
            SetPromptText(text, text);
        }

        /// <summary>创建或切换语言时接收本地化文本；手机名字独立传入，不从按键提示中解析。</summary>
        public void SetPromptText(string text, string name)
        {
            desktopPrompt = text ?? string.Empty;
            mobileName = name ?? string.Empty;
            RefreshPromptText();
        }

        private void RefreshPromptText()
        {
            if (promptText != null && desktopPrompt != null)
                promptText.text = mobileInputMode ? mobileName : desktopPrompt;
        }

        public void FacePrompt(Vector3 cameraPosition, Vector3 cameraUp)
        {
            if (promptRoot == null || !promptRoot.activeSelf)
            {
                return;
            }

            var direction = promptRoot.transform.position - cameraPosition;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            promptRoot.transform.rotation = Quaternion.LookRotation(direction, cameraUp);
        }

        public bool TryValidatePresentation(out string error)
        {
            if (promptRoot == null)
            {
                error = "NPC Prefab 缺少提示根";
                return false;
            }

            if (promptText == null)
            {
                error = "NPC Prefab 缺少提示 TMP_Text";
                return false;
            }

            if (!promptText.transform.IsChildOf(promptRoot.transform))
            {
                error = "NPC Prefab 提示 TMP_Text 必须位于提示根之下";
                return false;
            }

            error = null;
            return true;
        }
    }
}

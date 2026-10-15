using System.Collections.Generic;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    public sealed class MessageUICtrl : BaseUICtrl
    {
        private IReadOnlyList<MessageButtonConfig> buttons = System.Array.Empty<MessageButtonConfig>();
        private bool closeOnBackground;
        private MessageConfig currentConfig;

        private MessageUI UI => BaseUI as MessageUI;

        /// <summary>按按钮集合身份核对弹窗归属，异步调用者不能关闭或等待后来覆盖的消息。</summary>
        public bool IsShowingButtons(IReadOnlyList<MessageButtonConfig> expected) =>
            IsShow() && ReferenceEquals(buttons, expected);

        public override UILayer Layer()
        {
            return UILayer.Top;
        }

        protected override string Path()
        {
            return "Notify/Message";
        }

        protected override void OnInit()
        {
            UI.ButtonRoot.AddItemRefreshListener(OnButtonRefresh);
            if (!UnityObjectState.IsMissing(UI.BackgroundButton))
            {
                UI.BackgroundButton.onClick.AddListener(OnBackgroundClick);
            }

            if (!UnityObjectState.IsMissing(UI.InputField))
            {
                UI.InputField.onValueChanged.AddListener(OnInputChanged);
            }
        }

        protected override void OnShow()
        {
        }

        protected override void OnHide()
        {
            ClearControllerState();
            ClearInputState();
        }

        protected override void OnUnload()
        {
            ClearControllerState();
        }

        private void ClearControllerState()
        {
            closeOnBackground = false;
            currentConfig = null;
        }

        public void SetConfig(MessageConfig config)
        {
            currentConfig = config;
            closeOnBackground = config?.CloseOnBackground ?? false;
            if (UI == null)
            {
                return;
            }

            UI.TitleText.text = config?.Title ?? string.Empty;
            UI.ContentText.text = config?.Content ?? string.Empty;
            ApplyInputState(config);
            RebuildButtons(config?.Buttons);
        }

        public void SetTitle(string title)
        {
            if (currentConfig != null)
            {
                currentConfig = new MessageConfig
                {
                    Title = title,
                    Content = currentConfig.Content,
                    Buttons = currentConfig.Buttons,
                    CloseOnBackground = currentConfig.CloseOnBackground,
                    ShowInput = currentConfig.ShowInput,
                    InputDefaultText = currentConfig.InputDefaultText,
                    InputCharacterLimit = currentConfig.InputCharacterLimit,
                    InputSubmit = currentConfig.InputSubmit
                };
            }

            if (UI == null || UnityObjectState.IsMissing(UI.TitleText))
            {
                return;
            }

            UI.TitleText.text = title ?? string.Empty;
        }

        private void RebuildButtons(List<MessageButtonConfig> configs)
        {
            if (UnityObjectState.IsMissing(UI) || UnityObjectState.IsMissing(UI.ButtonRoot))
            {
                return;
            }

            buttons = configs == null || configs.Count == 0
                ? new List<MessageButtonConfig> { new MessageButtonConfig { ButtonText = Texts.CommonConfirm } }
                : configs;
            UI.ButtonRoot.totalCount = buttons.Count;
            UI.ButtonRoot.RefreshCells();
            LayoutRebuilder.ForceRebuildLayoutImmediate(UI.ButtonRoot.GetComponent<RectTransform>());
        }

        private void OnButtonRefresh(Transform transform, int index)
        {
            var button = transform.GetComponent<MessageButtonBoxUI>();
            if (button == null || index < 0 || index >= buttons.Count)
            {
                return;
            }

            var config = buttons[index];
            button.ButtonText.text = config.ButtonText ?? string.Empty;
            button.BtnButton.interactable = config.Interactable;
            button.BtnButton.RemoveAllEvents();
            button.BtnButton.OnClickEvent(OnMessageButtonClick, config);
        }

        private void OnMessageButtonClick(object[] args)
        {
            var buttonConfig = args.GetArg<MessageButtonConfig>(0);
            var owner = currentConfig;
            if (owner?.ShowInput == true)
            {
                if (buttonConfig.Role == MessageButtonRole.Cancel)
                {
                    HideIfCurrent(owner);
                    return;
                }

                if (buttonConfig.Role != MessageButtonRole.Confirm)
                {
                    return;
                }

                var value = UI.InputField == null ? string.Empty : UI.InputField.text.Trim();
                var localizedValidationMessage = owner.InputSubmit?.Invoke(value);
                if (!string.IsNullOrEmpty(localizedValidationMessage))
                {
                    if (ReferenceEquals(currentConfig, owner))
                    {
                        ShowInputValidationMessage(localizedValidationMessage);
                    }

                    return;
                }

                HideIfCurrent(owner);
                return;
            }

            buttonConfig.OnClick?.Invoke();
            HideIfCurrent(owner);
        }

        private void HideIfCurrent(MessageConfig owner)
        {
            if (ReferenceEquals(currentConfig, owner))
            {
                Hide();
            }
        }

        private void OnBackgroundClick()
        {
            if (closeOnBackground)
            {
                Hide();
            }
        }

        private void ApplyInputState(MessageConfig config)
        {
            var showInput = config?.ShowInput == true;
            if (!UnityObjectState.IsMissing(UI.ContentText))
            {
                UI.ContentText.gameObject.SetActive(!showInput);
            }

            if (!UnityObjectState.IsMissing(UI.InputField))
            {
                UI.InputField.gameObject.SetActive(showInput);
                if (showInput)
                {
                    UI.InputField.characterLimit = Mathf.Max(0, config.InputCharacterLimit);
                    UI.InputField.text = config.InputDefaultText ?? string.Empty;
                    UI.InputField.ActivateInputField();
                    UI.InputField.Select();
                }
            }

            if (!UnityObjectState.IsMissing(UI.InputErrorText))
            {
                UI.InputErrorText.gameObject.SetActive(showInput);
                UI.InputErrorText.text = string.Empty;
            }
        }

        private void ClearInputState()
        {
            if (!UnityObjectState.IsMissing(UI.InputField))
            {
                UI.InputField.text = string.Empty;
                UI.InputField.characterLimit = 0;
                UI.InputField.DeactivateInputField();
                UI.InputField.gameObject.SetActive(false);
            }

            if (!UnityObjectState.IsMissing(UI.InputErrorText))
            {
                UI.InputErrorText.text = string.Empty;
                UI.InputErrorText.gameObject.SetActive(false);
            }

            if (!UnityObjectState.IsMissing(UI.ContentText))
            {
                UI.ContentText.gameObject.SetActive(true);
            }
        }

        private void ShowInputValidationMessage(string localizedMessage)
        {
            if (UnityObjectState.IsMissing(UI.InputErrorText))
            {
                Notify.ShowFloatTips(localizedMessage);
                return;
            }

            UI.InputErrorText.text = localizedMessage;
            UI.InputErrorText.gameObject.SetActive(true);
        }

        private void OnInputChanged(string _)
        {
            if (!UnityObjectState.IsMissing(UI.InputErrorText))
            {
                UI.InputErrorText.text = string.Empty;
            }
        }
    }
}

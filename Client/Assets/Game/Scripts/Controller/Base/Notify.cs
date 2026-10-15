using System;
using System.Collections.Generic;

namespace Game
{
    public static class Notify
    {
        /// <summary>提交已本地化提示；指定稳定身份时，同类等待或显示中的提示最多一条，普通提示保持原行为。</summary>
        public static void ShowFloatTips(string tips, float lifetimeSeconds = FloatUICtrl.DefaultTipLifetimeSeconds, string uniqueKey = null)
        {
            if (string.IsNullOrWhiteSpace(tips))
            {
                return;
            }

            UIHelper.ShowUI<FloatUICtrl>().SetTips(tips, lifetimeSeconds, uniqueKey);
        }

        public static void ShowMessage(
            string title,
            string content,
            List<MessageButtonConfig> buttons = null,
            bool closeOnBackground = false)
        {
            UIHelper.ShowUI<MessageUICtrl>().SetConfig(new MessageConfig
            {
                Title = title,
                Content = content,
                Buttons = buttons,
                CloseOnBackground = closeOnBackground
            });
        }

        public static void ShowInput(
            string title,
            string defaultText,
            int characterLimit,
            Func<string, string> validateAndSubmit)
        {
            UIHelper.ShowUI<MessageUICtrl>().SetConfig(new MessageConfig
            {
                Title = title,
                ShowInput = true,
                InputDefaultText = defaultText,
                InputCharacterLimit = characterLimit,
                InputSubmit = validateAndSubmit,
                CloseOnBackground = false,
                Buttons = new List<MessageButtonConfig>
                {
                    new MessageButtonConfig
                    {
                        ButtonText = Texts.CommonConfirm,
                        Role = MessageButtonRole.Confirm
                    },
                    new MessageButtonConfig
                    {
                        ButtonText = Texts.CommonCancel,
                        Role = MessageButtonRole.Cancel
                    }
                }
            });
        }
    }

    public enum MessageButtonRole
    {
        Default,
        Confirm,
        Cancel
    }

    public sealed class MessageButtonConfig
    {
        public string ButtonText;
        public MessageButtonRole Role;
        public Action OnClick;
        public bool Interactable = true;
    }

    public sealed class MessageConfig
    {
        public string Title;
        public string Content;
        public List<MessageButtonConfig> Buttons;
        public bool CloseOnBackground;
        public bool ShowInput;
        public string InputDefaultText;
        public int InputCharacterLimit;
        // 返回值必须是已本地化、可直接展示给玩家的校验消息；空字符串表示提交成功。
        public Func<string, string> InputSubmit;
    }
}

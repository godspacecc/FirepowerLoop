using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>
    /// 统一指定界面的面板、操作按钮与文本样式；全屏根遮罩及背景取消按钮的外观由 Prefab 持有，
    /// 不负责界面显隐、输入阻断或业务操作。
    /// </summary>
    public static class UITheme
    {
        public static readonly Color Backdrop = Hex("#080D10D6");
        public static readonly Color Surface = Hex("#121B21F7");
        public static readonly Color SurfaceRaised = Hex("#19252DF7");
        public static readonly Color SurfaceInset = Hex("#0B1218E8");
        public static readonly Color Field = Hex("#202D35FF");
        public static readonly Color Primary = Hex("#247A6BFF");
        public static readonly Color Neutral = Hex("#30434DFF");
        public static readonly Color Danger = Hex("#713B3BFF");
        public static readonly Color TextPrimary = Hex("#F1F6F3FF");
        public static readonly Color TextSecondary = Hex("#B7C7C1FF");
        public static readonly Color TextMuted = Hex("#849791FF");

        public const float StandardControlHeight = 44f;
        public const float StandardSpacing = 8f;

        public static void Apply(GameObject root)
        {
            if (root == null || !IsThemedScreen(root.name))
            {
                return;
            }

            ApplySurfaces(root);
            ApplyButtons(root);
            ApplyFields(root);
            ApplyText(root);
            ApplyLayout(root);
        }

        /// <summary>加载界面时统一内容面板样式；全屏根保留序列化遮罩颜色，避免额外加深背景。</summary>
        private static void ApplySurfaces(GameObject root)
        {
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.GetComponent<Button>() != null ||
                    image.GetComponent<TMP_InputField>() != null ||
                    image.GetComponent<Slider>() != null ||
                    image.GetComponent<Toggle>() != null)
                {
                    continue;
                }

                string name = image.gameObject.name;
                if (name.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Dialog", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = image.transform == root.transform ? Surface : SurfaceRaised;
                }
                else if (name.IndexOf("Viewport", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    image.color = SurfaceInset;
                }
            }

            Image rootImage = root.GetComponent<Image>();
            if (rootImage != null)
            {
                RectTransform rect = root.transform as RectTransform;
                bool isOverlay = rect != null &&
                                 rect.anchorMin == Vector2.zero &&
                                 rect.anchorMax == Vector2.one;
                // 全屏根承担遮罩而非内容面板职责，透明度以设计师在 Prefab 中的配置为准。
                if (!isOverlay)
                {
                    rootImage.color = Surface;
                }
            }

            Transform background = root.transform.Find("Background");
            if (background != null && background.TryGetComponent(out Image backgroundImage))
            {
                backgroundImage.color = Backdrop;
            }
        }

        /// <summary>加载界面时应用普通操作按钮样式；保留消息背景取消按钮的序列化颜色及交互状态配色。</summary>
        private static void ApplyButtons(GameObject root)
        {
            var messageUI = root.GetComponent<MessageUI>();
            var backgroundButton = messageUI != null ? messageUI.BackgroundButton : null;
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                // 遮罩通过 Button 接收外部取消点击，但不能套用不透明的普通按钮配色。
                if (button == backgroundButton || IsSlotButton(button.name))
                {
                    continue;
                }

                Color baseColor = IsDangerAction(button.name)
                    ? Danger
                    : IsPrimaryAction(button.name) ? Primary : Neutral;

                if (button.targetGraphic != null)
                {
                    button.targetGraphic.color = baseColor;
                }

                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Hex("#D4EEE6FF");
                colors.pressedColor = Hex("#91B8ADFF");
                colors.selectedColor = Hex("#BFE1D7FF");
                colors.disabledColor = Hex("#71807B80");
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.12f;
                button.colors = colors;
            }
        }

        private static void ApplyFields(GameObject root)
        {
            foreach (TMP_InputField field in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (field.targetGraphic != null)
                {
                    field.targetGraphic.color = Field;
                }

                ColorBlock colors = field.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Hex("#D8E8E3FF");
                colors.selectedColor = Hex("#C5E5DCFF");
                colors.pressedColor = Hex("#A8C9C0FF");
                colors.disabledColor = Hex("#68736FFF");
                colors.fadeDuration = 0.12f;
                field.colors = colors;
                field.selectionColor = Hex("#4FB8A780");
                field.caretColor = TextPrimary;
            }
        }

        private static void ApplyText(GameObject root)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string name = text.gameObject.name;
                if (name.IndexOf("Placeholder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Hint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Status", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Version", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    text.color = TextMuted;
                }
                else if (name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("Desc", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    text.color = TextSecondary;
                }
                else
                {
                    text.color = TextPrimary;
                }
            }
        }

        private static void ApplyLayout(GameObject root)
        {
            foreach (VerticalLayoutGroup layout in root.GetComponentsInChildren<VerticalLayoutGroup>(true))
            {
                if (layout.gameObject.name.IndexOf("ButtonGroup", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    layout.spacing = StandardSpacing;
                    layout.childAlignment = TextAnchor.UpperCenter;
                    layout.childForceExpandWidth = false;
                    layout.childForceExpandHeight = false;
                    layout.childControlWidth = false;
                    layout.childControlHeight = false;
                }
            }

            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                if (IsSlotButton(button.name) || IsCompactButton(button.name))
                {
                    continue;
                }

                RectTransform rect = button.transform as RectTransform;
                if (rect != null && rect.sizeDelta.y >= 34f && rect.sizeDelta.y <= 56f)
                {
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, StandardControlHeight);
                }
            }
        }

        private static bool IsThemedScreen(string name)
        {
            int instanceSuffix = name.IndexOf('(');
            string screenName = instanceSuffix >= 0 ? name.Substring(0, instanceSuffix).Trim() : name;
            // 开始页、联机登录及手机大厅以正式 Prefab 的风景样式和触控尺寸为准，避免 Awake 覆盖配色与布局。
            // 设置页的街机配色和触控尺寸由正式 Prefab 持有，避免旧主题覆盖。
            return screenName == "Inventory" || screenName == "Message";
        }

        private static bool IsPrimaryAction(string name)
        {
            return ContainsAny(name, "NewGame", "Continue", "Login", "Join", "Create", "Save", "Send", "Resume", "Use", "Online");
        }

        private static bool IsDangerAction(string name)
        {
            return ContainsAny(name, "Exit", "Leave", "Logout", "Clear", "Delete", "MainMenu");
        }

        private static bool IsSlotButton(string name)
        {
            return ContainsAny(name, "Slot", "Equipment", "Template");
        }

        private static bool IsCompactButton(string name)
        {
            return ContainsAny(name, "CloseButton", "BtnClose");
        }

        private static bool ContainsAny(string value, params string[] fragments)
        {
            foreach (string fragment in fragments)
            {
                if (value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static Color Hex(string value)
        {
            return ColorUtility.TryParseHtmlString(value, out Color color) ? color : Color.magenta;
        }
    }
}

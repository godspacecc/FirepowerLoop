using QFramework;

namespace Game
{
    /// <summary>
    /// 复用库存 Box 前清除上一轮的显示和交互状态。
    /// Prefab 契约保证这些字段均已接线；View 本身不承载任何行为。
    /// </summary>
    internal static class InventorySlotBoxReuse
    {
        public static void Reset(InventorySlotBoxUI view)
        {
            view.ItemContentRoot.SetActive(false);
            view.ExpansionContentRoot.SetActive(false);
            ResetButton(view.ClickButton);
            ResetButton(view.ExpansionButton);
            view.IconImage.enabled = true;
            view.IconImage.sprite = null;
            view.IconImage.gameObject.SetActive(false);
            ResetText(view.AmountText);
            ResetText(view.ExpansionLabelText);
            view.EmptyIconRoot.SetActive(false);
            view.SelectedFrame.SetActive(false);
            view.DisabledOverlay.SetActive(false);
            view.DragFeedback.SetActive(false);
        }

        /// <summary>口袋条目复用前清除旧回调、名称及数量；只重置 View，不改变实际库存。</summary>
        public static void Reset(PocketSlotBoxUI view)
        {
            ResetButton(view.ClickButton);
            view.IconImage.enabled = true;
            view.IconImage.sprite = null;
            view.IconImage.gameObject.SetActive(false);
            ResetText(view.AmountText);
            ResetText(view.NameText);
            view.SelectedFrame.SetActive(false);
        }

        private static void ResetButton(UnityEngine.UI.Button button)
        {
            button.RemoveAllEvents();
            button.interactable = false;
        }

        private static void ResetText(TMPro.TMP_Text text)
        {
            text.text = string.Empty;
            text.gameObject.SetActive(false);
        }
    }
}

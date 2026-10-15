using UnityEngine;

namespace Game
{
    internal static class UiPopupPosition
    {
        public static void Place(RectTransform popup, Vector2 screenPosition, Vector2 offset)
        {
            if (popup == null || !(popup.parent is RectTransform parent)) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPosition, null, out var local);
            local += offset;
            var half = popup.rect.size * 0.5f;
            var bounds = parent.rect;
            local.x = Mathf.Clamp(local.x, bounds.xMin + half.x, bounds.xMax - half.x);
            local.y = Mathf.Clamp(local.y, bounds.yMin + half.y, bounds.yMax - half.y);
            popup.anchorMin = popup.anchorMax = popup.pivot = new Vector2(0.5f, 0.5f);
            popup.anchoredPosition = local;
        }
    }
}

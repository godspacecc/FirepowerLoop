using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>统一背包的四列展示布局；按可用宽度扩大点击区域，不负责槽位创建或库存状态。</summary>
    public sealed class InventoryManagementGridLayout : GridLayoutGroup
    {
        /// <summary>布局重建时重新计算列宽；保持序列化的行高与间距，兼容不同横屏比例。</summary>
        public override void CalculateLayoutInputHorizontal()
        {
            var columns = Mathf.Max(1, constraintCount);
            cellSize = new Vector2(Mathf.Max(1, (rectTransform.rect.width - padding.horizontal -
                spacing.x * (columns - 1)) / columns), cellSize.y);
            base.CalculateLayoutInputHorizontal();
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>应用生命条的显示比例；只修改图像，不读取或写入战斗生命数据。</summary>
    public static class HealthFillUtility
    {
        /// <summary>刷新生命图像；无 Sprite 时裁切水平锚点，避免只改 fillAmount 而画面不变。</summary>
        public static void Apply(Image fillImage, float fillAmount)
        {
            if (fillImage == null) return;
            var clamped = Mathf.Clamp01(fillAmount);
            fillImage.fillAmount = clamped;
            if (fillImage.sprite != null) return;
            var rect = fillImage.rectTransform;
            rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(clamped, rect.anchorMax.y);
            rect.offsetMin = new Vector2(0f, rect.offsetMin.y);
            rect.offsetMax = new Vector2(0f, rect.offsetMax.y);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>持有加载页的正式背景引用，只按控制器给出的场地身份绘图，不裁决路线。</summary>
    public sealed class ArcadeLoadingArtwork : MonoBehaviour
    {
        /// <summary>已采用图片与稳定场地配置编号的序列化绑定，独立于本局关卡序号。</summary>
        [Serializable] private struct Entry
        {
            public int ArenaConfigNumber;
            public Sprite Artwork;
        }

        [SerializeField] private Image target;
        [SerializeField] private Sprite fallback;
        /// <summary>已确认的幸存者独立加载插画，不覆盖闯关各场地图片。</summary>
        [SerializeField] private Sprite survivorArtwork;
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        /// <summary>未知场地或缺失图片立即回退默认图，防止重复打开时残留上一关背景。</summary>
        public void ShowArena(int arenaConfigNumber)
        {
            var artwork = fallback;
            foreach (var entry in entries)
            {
                if (entry.ArenaConfigNumber != arenaConfigNumber) continue;
                artwork = entry.Artwork != null ? entry.Artwork : fallback;
                break;
            }
            target.sprite = artwork;
        }
        /// <summary>准备期按明确入口身份显示；资源缺失时仍有默认背景。</summary>
        public void ShowSurvivor()=>target.sprite=survivorArtwork!=null?survivorArtwork:fallback;
    }
}

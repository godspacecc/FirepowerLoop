using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class SurvivorCombatViewController
    {
        /// <summary>当前地图的静态场地根，与动态对象根分开。</summary>
        private Transform baseArenaRoot;
        /// <summary>临时隐藏的底图渲染器原始状态，离场时逐项恢复。</summary>
        private readonly Dictionary<Renderer, bool> baseArenaVisibility = new();

        /// <summary>旧表现释放或公共资源失败后，仍由已就绪且正在显示的正式环境覆盖共享底图。</summary>
        private void ReapplyBaseArenaOverlay()
        {
            if (root == null || !root.activeSelf) return;
            bool hasContent = contentEnvironment != null && contentEnvironment.activeSelf;
            bool hasRound = roundLayoutReady && roundLayout != null &&
                roundContentRoot != null && roundContentRoot.activeSelf;
            bool hasShop = slayerMartRegionReady && slayerMartRegionRoot != null && slayerMartRegionRoot.activeSelf;
            if (hasContent || hasRound || hasShop) HideBaseArena();
        }

        /// <summary>正式环境就绪后隐藏当前场地的旧地板与四面墙；重复调用不覆盖原始状态。</summary>
        private void HideBaseArena()
        {
            if (baseArenaRoot == null || baseArenaVisibility.Count > 0) return;
            foreach (var renderer in baseArenaRoot.GetComponentsInChildren<Renderer>(true))
            {
                switch (renderer.gameObject.name)
                {
                    case "Ground":
                    case "NorthBoundary":
                    case "SouthBoundary":
                    case "EastBoundary":
                    case "WestBoundary":
                        baseArenaVisibility.Add(renderer, renderer.enabled);
                        renderer.enabled = false;
                        break;
                }
            }
        }

        /// <summary>区域切换、离场或失败回滚时恢复本次覆盖；地图已销毁的对象直接跳过。</summary>
        private void RestoreBaseArena()
        {
            foreach (var pair in baseArenaVisibility)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            baseArenaVisibility.Clear();
        }
    }
}

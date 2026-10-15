using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>当前 View 已消费的横幅；会话去重记录由战斗模型/联机表现系统持有。</summary>
        private ArcadeStageNotice stageBanner;

        /// <summary>可见 HUD 消费最新阶段一次；重复事件和 HUD 重开不会重置截止时刻。</summary>
        private void TryPresentStageNotice()
        {
            if (!IsShow()) return;
            var notices = onlineMode ? this.GetSystem<OnlineArcadePresentationSystem>().StageNotices :
                this.GetModel<ArcadeCombatModel>().StageNotices;
            double now = Time.unscaledTimeAsDouble;
            if (!notices.TryConsume(now, out var notice)) return;
            stageBanner = notice;
            RenderStageBannerText();
            UI.StageBannerRoot.SetActive(true);
            UpdateStageBanner(now);
        }

        /// <summary>每帧只更新短暂视觉透明度，不暂停战斗、不修改输入或任何阶段等待时间。</summary>
        private void UpdateStageBanner(double now)
        {
            if (stageBanner == null) return;
            float alpha = stageBanner.Opacity(now);
            UI.StageBannerOpacity.alpha = alpha;
            if (alpha <= 0f) ClearStageBanner();
        }

        /// <summary>语言变化只替换当前文字，不重新消费或延长横幅。</summary>
        private void RenderStageBannerText()
        {
            if (stageBanner == null || UI == null) return;
            UI.TextStageBanner.text = stageBanner.Kind == ArcadeStageNoticeKind.ArenaCompleted
                ? Loc.T("ui.arcade.stage.island_completed")
                : stageBanner.Kind == ArcadeStageNoticeKind.WaveCompleted
                    ? Loc.T("ui.arcade.stage.wave_completed")
                    : Loc.T("ui.arcade.stage.wave_started", stageBanner.WaveNumber);
        }

        /// <summary>隐藏、销毁或淡出结束立即清空当前 View，不清除会话的已消费记录。</summary>
        private void ClearStageBanner()
        {
            stageBanner = null;
            if (UI == null) return;
            UI.StageBannerOpacity.alpha = 0f;
            UI.StageBannerRoot.SetActive(false);
            UI.TextStageBanner.text = string.Empty;
        }
    }
}

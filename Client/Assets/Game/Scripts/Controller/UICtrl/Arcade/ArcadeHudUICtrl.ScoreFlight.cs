using System.Collections.Generic;
using QFramework;
using TMPro;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeHudUICtrl
    {
        /// <summary>财宝表现有界复用；超出上限只合并待显示增量，不丢分或增加规则奖励。</summary>
        private const int ScoreFlightLimit = 16;
        private const float ScoreFlightSeconds = .55f, ScorePulseSeconds = .3f;
        private readonly List<ScoreFlight> scoreFlights = new(ScoreFlightLimit);
        /// <summary>只缓存当前本人行的两个表现落点和原始比例，不保留玩家或地图对象。</summary>
        private TextMeshProUGUI desktopScoreTarget, mobileScoreTarget;
        private Vector3 desktopScoreScale, mobileScoreScale;
        private long authoritativeSelfScore;
        private float scorePulseAge = ScorePulseSeconds;

        /// <summary>本次拾取的视觉轨迹和尚未显示的真实增量，生命周期只属于当前 HUD。</summary>
        private sealed class ScoreFlight
        {
            public Vector2 Start, End;
            public long Score;
            public float Age;
        }

        /// <summary>成功拾取后从屏幕落点发出财宝；不提交积分，也不播放重连或读档的历史增量。</summary>
        public bool TryPlayScorePickup(long score, Vector2 screenPosition)
        {
            if (!IsShow() || UI == null || score <= 0 || float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y) ||
                float.IsInfinity(screenPosition.x) || float.IsInfinity(screenPosition.y) || ActiveScoreTarget() == null) return false;
            var rect = (RectTransform)UI.ScoreFlightList.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPosition, ScoreCanvasCamera(), out var start)) return false;
            // 镜头边缘的拾取也从可见区域出发，避免离屏飞行让延后计分看起来失效。
            start.x = Mathf.Clamp(start.x, rect.rect.xMin + 24, rect.rect.xMax - 24);
            start.y = Mathf.Clamp(start.y, rect.rect.yMin + 24, rect.rect.yMax - 24);
            if (scoreFlights.Count == ScoreFlightLimit)
            {
                var last = scoreFlights[scoreFlights.Count - 1];
                if (last.Score > long.MaxValue - score) return false;
                last.Score += score;
            }
            else scoreFlights.Add(new ScoreFlight { Start = start, End = ScoreTargetPosition(), Score = score });
            RefreshScoreFlights();
            RefreshScoreNumbers();
            return true;
        }

        /// <summary>本人行刷新只重绑真实落点；权威分数回退时取消旧局的飞行，不将动画记作存档状态。</summary>
        private void BindScoreTargets(ArcadePlayerStatusBoxUI box, long score, bool mobile)
        {
            if (!mobile && desktopScoreTarget != box.TextScore)
            {
                if (desktopScoreTarget != null) desktopScoreTarget.transform.localScale = desktopScoreScale;
                desktopScoreTarget = box.TextScore;
                desktopScoreScale = desktopScoreTarget.transform.localScale;
            }
            if (mobile && mobileScoreTarget != box.MobileScore)
            {
                if (mobileScoreTarget != null) mobileScoreTarget.transform.localScale = mobileScoreScale;
                mobileScoreTarget = box.MobileScore;
                mobileScoreScale = mobileScoreTarget.transform.localScale;
            }
            bool rolledBack = score < authoritativeSelfScore;
            authoritativeSelfScore = score;
            if (rolledBack) ClearScorePickupFeedback();
        }

        /// <summary>权威快照仍立即更新；只有正在飞行的财宝增量从可见总分中暂扣，到达后自然释放。</summary>
        private long VisibleSelfScore(long score)
        {
            long visible = score;
            foreach (var flight in scoreFlights) visible -= System.Math.Min(visible, flight.Score);
            return visible;
        }

        /// <summary>沿用 HUD 的共享生命周期，不新增 MonoBehaviour.Update，也不驱动任何游戏计时。</summary>
        private void AdvanceScorePickups(float seconds)
        {
            if (!IsShow() || UI == null) return;
            if (scoreFlights.Count == 0 && scorePulseAge >= ScorePulseSeconds) return;
            seconds = Mathf.Max(0, seconds);
            float arrivedAge = float.PositiveInfinity;
            for (int index = scoreFlights.Count - 1; index >= 0; index--)
            {
                var flight = scoreFlights[index]; flight.Age += seconds;
                if (flight.Age >= ScoreFlightSeconds)
                {
                    arrivedAge = Mathf.Min(arrivedAge, flight.Age - ScoreFlightSeconds);
                    scoreFlights.RemoveAt(index);
                }
                else flight.End = ScoreTargetPosition();
            }
            scorePulseAge = float.IsPositiveInfinity(arrivedAge) ? scorePulseAge + seconds : arrivedAge;
            RefreshScoreFlights(); RefreshScoreNumbers();
            float pulse = 1f + .22f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(scorePulseAge / ScorePulseSeconds));
            if (desktopScoreTarget != null) desktopScoreTarget.transform.localScale = desktopScoreScale * pulse;
            if (mobileScoreTarget != null) mobileScoreTarget.transform.localScale = mobileScoreScale * pulse;
        }

        /// <summary>仅由 CustomListView 创建及复用外部 Box，Controller 只提供轨迹与可见数量。</summary>
        private void RefreshScoreFlights()
        {
            UI.ScoreFlightList.totalCount = scoreFlights.Count;
            UI.ScoreFlightList.RefreshCells();
        }

        /// <summary>每次复用覆盖位置、旋转、大小和透明度；弧线抬高后吸入当前积分中心。</summary>
        private void RefreshScoreFlight(Transform transform, int index)
        {
            var box = transform.GetComponent<ArcadeScoreFlightBoxUI>();
            var flight = scoreFlights[index];
            float t = Mathf.Clamp01(flight.Age / ScoreFlightSeconds);
            float eased = t * t * (3f - 2f * t);
            var point = Vector2.Lerp(flight.Start, flight.End, eased);
            point.y += Mathf.Sin(Mathf.PI * t) * Mathf.Min(90, Vector2.Distance(flight.Start, flight.End) * .2f);
            box.Motion.anchoredPosition = point;
            box.Motion.localRotation = Quaternion.Euler(0, 0, -20 + 200 * t);
            float size = Mathf.Lerp(1.15f, .45f, t * t);
            box.Motion.localScale = new Vector3(size * (.8f + .2f * Mathf.Abs(Mathf.Cos(t * Mathf.PI * 2))), size, 1);
            box.Opacity.alpha = 1f - Mathf.Clamp01((t - .9f) * 10);
        }

        /// <summary>桌面与手机都显示同一个延后总分，不新增 +分数文字或影响队友行。</summary>
        private void RefreshScoreNumbers()
        {
            long score = VisibleSelfScore(authoritativeSelfScore);
            if (desktopScoreTarget != null) Set(desktopScoreTarget, score.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
            if (mobileScoreTarget != null) Set(mobileScoreTarget, score.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>输入模式切换后直接取当前显示的分数落点，动画不依赖固定像素位置。</summary>
        private TextMeshProUGUI ActiveScoreTarget() => this.GetSystem<MobileInputSystem>().IsMobileInputMode ? mobileScoreTarget : desktopScoreTarget;

        private Camera ScoreCanvasCamera()
        {
            var canvas = UI.ScoreFlightList.GetComponentInParent<Canvas>().rootCanvas;
            return canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        }

        private Vector2 ScoreTargetPosition()
        {
            var target = ActiveScoreTarget();
            if (target == null) return Vector2.zero;
            return UI.ScoreFlightList.transform.InverseTransformPoint(target.rectTransform.TransformPoint(target.rectTransform.rect.center));
        }

        /// <summary>隐藏、离图、区域重建和回滚立即取消待播动画，并恢复真实分数及文字比例。</summary>
        public void ClearScorePickupFeedback()
        {
            scoreFlights.Clear(); scorePulseAge = ScorePulseSeconds;
            if (UI != null) UI.ScoreFlightList.ClearCells();
            RestoreScoreScale(); RefreshScoreNumbers();
        }

        private void RestoreScoreScale()
        {
            if (desktopScoreTarget != null) desktopScoreTarget.transform.localScale = desktopScoreScale;
            if (mobileScoreTarget != null) mobileScoreTarget.transform.localScale = mobileScoreScale;
        }
    }
}

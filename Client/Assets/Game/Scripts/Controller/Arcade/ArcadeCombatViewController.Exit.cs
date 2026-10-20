using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>缓存正式模板的提示节点；只动提示，不移动出口权威落点。</summary>
        private readonly List<ExitCue> exitCues = new();
        /// <summary>奖励房入口独立持有提示，普通出口刷新不能清除其方向引导。</summary>
        private readonly List<ExitCue> treasureExitCues = new();
        /// <summary>本次出口显示周期的纯视觉秒数，重建和释放时归零。</summary>
        private float exitCueElapsed;

        /// <summary>持有当前出口实例的节点和动画基线，不保存任何玩法数据。</summary>
        private sealed class ExitCue
        {
            /// <summary>出口锚点、上方箭头、可选光圈和角色附近指路箭头；普通出口没有光圈。</summary>
            public Transform Gate, Arrow, Ring, Approach;
            /// <summary>从正式模板读取的原始位置和比例。</summary>
            public Vector3 ArrowPosition, RingScale;
        }

        /// <summary>只复制正式箭头节点，避免实例化门框、门牌、光圈和粒子；生命周期随出口列表释放。</summary>
        private GameObject CreateRouteExitGuide(Vector2 position)
        {
            var source = contentTemplates.Find("RouteGate/ExitCue");
            var arrowTemplate = source != null ? source.Find("Arrow") : null;
            var approachTemplate = source != null ? source.Find("ApproachArrow") : null;
            if (arrowTemplate == null || approachTemplate == null)
                throw new System.InvalidOperationException("正式出口缺少指路箭头模板");
            var marker = new GameObject("ArcadeRouteGuide");
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(position.x, 0, position.y);
            var cue = new GameObject("ExitCue");
            cue.transform.SetParent(marker.transform, false);
            var arrow = UnityEngine.Object.Instantiate(arrowTemplate, cue.transform, false);
            var approach = UnityEngine.Object.Instantiate(approachTemplate, cue.transform, false);
            arrow.name = "Arrow"; approach.name = "ApproachArrow";
            exitCues.Add(new ExitCue { Gate = marker.transform, Arrow = arrow, Approach = approach,
                ArrowPosition = arrow.localPosition });
            return marker;
        }

        /// <summary>模板契约在导入和定向测试中校验，运行期不临时拼装替代控件。</summary>
        private void BindExitCue(GameObject marker, List<ExitCue> target = null)
        {
            var arrow = marker.transform.Find("ExitCue/Arrow");
            var ring = marker.transform.Find("ExitCue/GroundRing");
            var approach = marker.transform.Find("ExitCue/ApproachArrow");
            if (arrow == null || ring == null || approach == null)
                throw new System.InvalidOperationException("正式出口缺少箭头或地面光圈");
            (target ?? exitCues).Add(new ExitCue { Gate = marker.transform, Arrow = arrow, Ring = ring, Approach = approach,
                ArrowPosition = arrow.localPosition, RingScale = ring.localScale });
        }

        /// <summary>出口在镜头外时，角色前方仍有地面箭头指路；接近触发区后只保留门上标记。</summary>
        private void SetExitGuidePlayer(Vector2 position, bool visible)
        {
            SetExitGuidePlayer(exitCues, position, visible);
            SetExitGuidePlayer(treasureExitCues, position, visible);
        }

        /// <summary>普通出口和奖励房入口共用近身指路计算，不改变入口权威坐标。</summary>
        private static void SetExitGuidePlayer(List<ExitCue> cues, Vector2 position, bool visible)
        {
            foreach (var cue in cues)
            {
                var gate = new Vector2(cue.Gate.localPosition.x, cue.Gate.localPosition.z);
                var direction = gate - position;
                bool show = visible && direction.sqrMagnitude > 9f;
                cue.Approach.gameObject.SetActive(show);
                if (!show) continue;
                var point = position + direction.normalized * 2.3f - gate;
                cue.Approach.localPosition = new Vector3(point.x, .28f, point.y);
                cue.Approach.localRotation = Quaternion.Euler(0, Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 0);
            }
        }

        /// <summary>沿用表现帧生命周期；浮动与呼吸无碰撞，不触发选路、保存或推进。</summary>
        private void TickExitCues(float deltaTime)
        {
            if (exitCues.Count == 0 && treasureExitCues.Count == 0) return;
            exitCueElapsed += Mathf.Max(0, deltaTime);
            float phase = Mathf.Sin(exitCueElapsed * 3f);
            TickExitCues(exitCues, phase);
            TickExitCues(treasureExitCues, phase);
        }

        /// <summary>两类入口按同一表现时钟播放箭头浮动和光圈呼吸。</summary>
        private static void TickExitCues(List<ExitCue> cues, float phase)
        {
            foreach (var cue in cues)
            {
                cue.Arrow.localPosition = cue.ArrowPosition + Vector3.up * (.25f * phase);
                if (cue.Ring != null) cue.Ring.localScale = cue.RingScale * (1f + .045f * phase);
            }
        }
    }
}

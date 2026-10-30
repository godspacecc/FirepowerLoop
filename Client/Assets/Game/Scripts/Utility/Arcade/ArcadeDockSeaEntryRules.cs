using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>右侧海中进场的瞬时权威状态，随敌人进入快照，不进入轮前节点。</summary>
    public struct ArcadeDockSeaEntryState
    {
        /// <summary>保留来源身份，完成后不让迟到快照重新开始动作。</summary>
        public bool Started;
        /// <summary>活动年龄与固定车道，不由客户端播放时钟推进。</summary>
        public float Age, Lane;
        public bool Active => Started && Age < ArcadeDockSeaEntryRules.TotalSeconds;
        /// <summary>游近及抓边攀爬时不接受攻击；双脚登台后即恢复，不额外等待站稳步进。</summary>
        public bool CanBeAttacked => !Started || Age >= ArcadeDockSeaEntryRules.ClimbSeconds;
    }
    /// <summary>已采用艺术路径；X用于战斗位置，高度和倾斜只供表现。</summary>
    public readonly struct ArcadeDockSeaEntryPose
    {
        public readonly float X, Height, Tilt;
        public ArcadeDockSeaEntryPose(float x, float height, float tilt) { X = x; Height = height; Tilt = tilt; }
    }
    /// <summary>单人与服务器共用已确认的右路、抓边/登台时间和站稳位置。</summary>
    public static class ArcadeDockSeaEntryRules
    {
        public const float ClimbSeconds = 5.75f, TotalSeconds = 6.75f;
        /// <summary>码头左桥和上方通道须走入正式主平台；与既有码头网格中心(-1,0)、尺寸26×23一致。</summary>
        public static bool TryGetDeckEntryTarget(string theme, int passage, float x, float y, float radius, out ArcadeRegionPoint target)
        {
            target = default;
            if (theme != "docks" || passage < 0 || passage > 1 || !Finite(x) || !Finite(y) || !Finite(radius) || radius <= 0) return false;
            target = new ArcadeRegionPoint(Math.Max(-14f + radius, Math.Min(12f - radius, x)),
                Math.Max(-11.5f + radius, Math.Min(11.5f - radius, y)));
            return true;
        }

        /// <summary>原第三通道预算，三条车道与采用版同源。</summary>
        public static bool TryStart(string theme, int passage, int sequence, out ArcadeDockSeaEntryState state)
        {
            state = default;
            if (theme != "docks" || passage != 2) return false;
            state = new ArcadeDockSeaEntryState { Started = true, Lane = -3f - (int)((uint)sequence % 3) * 1.1f };
            return true;
        }
        /// <summary>完成帧仍占用进场，下一活动帧才追击；无效/零时间不消耗动作。</summary>
        public static bool Advance(ref ArcadeDockSeaEntryState state, float seconds)
        {
            if (!state.Active) return false;
            if (Finite(seconds) && seconds > 0) state.Age = Math.Min(TotalSeconds, state.Age + seconds);
            return true;
        }
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Smooth(float t) { t = Math.Max(0, Math.Min(1, t)); return t * t * (3 - 2 * t); }
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        /// <summary>海面靠近、抓边、撑身、前脚登台、后脚补步，最后走入碰撞半径可站稳处。</summary>
        public static ArcadeDockSeaEntryPose Sample(float age)
        {
            float t = Finite(age) ? Math.Max(0, Math.Min(TotalSeconds, age)) : 0;
            if (t < 2.1f) return new ArcadeDockSeaEntryPose(Lerp(18.3f, 12.55f, t / 2.1f), -1.2f + .035f * (float)Math.Sin(t * 7), 12);
            if (t < 2.7f) return new ArcadeDockSeaEntryPose(12.55f, -1.2f, Lerp(12, 30, Smooth((t - 2.1f) / .6f)));
            if (t < 3.6f) { float u = Smooth((t - 2.7f) / .9f); return new ArcadeDockSeaEntryPose(Lerp(12.55f, 12.25f, u), Lerp(-1.2f, -.4f, u), Lerp(30, 55, u)); }
            if (t < 4.45f) { float u = Smooth((t - 3.6f) / .85f); return new ArcadeDockSeaEntryPose(Lerp(12.25f, 12.1f, u), Lerp(-.4f, -.25f, u), Lerp(55, 40, u)); }
            if (t < 5.15f) { float u = Smooth((t - 4.45f) / .7f); return new ArcadeDockSeaEntryPose(Lerp(12.1f, 11.85f, u), Lerp(-.25f, -.1f, u), Lerp(40, 20, u)); }
            if (t < ClimbSeconds) { float u = Smooth((t - 5.15f) / .6f); return new ArcadeDockSeaEntryPose(Lerp(11.85f, 11.8f, u), Lerp(-.1f, .04f, u), Lerp(20, 0, u)); }
            return new ArcadeDockSeaEntryPose(11.8f - (t - ClimbSeconds) * .9f, .04f * (1 - Smooth(t - ClimbSeconds)), 0);
        }
    }
}

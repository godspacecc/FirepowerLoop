using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>三方向海中进场的瞬时权威状态，随敌人进入快照，不进入轮前节点。</summary>
    public struct ArcadeDockSeaEntryState
    {
        /// <summary>保留来源身份，完成后不让迟到快照重新开始动作。</summary>
        public bool Started;
        /// <summary>0右、1左、2上；零值保留旧右路语义，快照与重连必须携带方向。</summary>
        public int Direction;
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
    /// <summary>单人与服务器共用已采用三路的抓边/登台时间和站稳位置。</summary>
    public static class ArcadeDockSeaEntryRules
    {
        public const float ClimbSeconds = 5.75f, TotalSeconds = 6.75f;
        /// <summary>将已采用的右路平面轨迹旋转到左／上岸沿，高度与骨骼动作保持同源。</summary>
        public static ArcadeRegionPoint Position(ArcadeDockSeaEntryState state)
        {
            float x = Sample(state.Age).X;
            return state.Direction == 1 ? new ArcadeRegionPoint(-2f-x,-state.Lane) :
                state.Direction == 2 ? new ArcadeRegionPoint(-state.Lane-2.2f,x-.5f) : new ArcadeRegionPoint(x,state.Lane);
        }
        /// <summary>面向对应平台内侧，返回平面单位向量供地图根投影。</summary>
        public static ArcadeRegionPoint Forward(ArcadeDockSeaEntryState state) => state.Direction == 1 ?
            new ArcadeRegionPoint(1,0) : state.Direction == 2 ? new ArcadeRegionPoint(0,-1) : new ArcadeRegionPoint(-1,0);
        /// <summary>拒绝无效方向和车道，避免错误快照把演员投影到其它岸沿。</summary>
        public static bool Valid(ArcadeDockSeaEntryState state) => state.Direction >= 0 && state.Direction <= 2 &&
            Finite(state.Age) && state.Age >= 0 && state.Age <= TotalSeconds && Finite(state.Lane) &&
            Math.Min(Math.Abs(state.Lane+3),Math.Min(Math.Abs(state.Lane+4.1f),Math.Abs(state.Lane+5.2f))) <= .001f;

        /// <summary>保留三路原出生预算，每路三条车道与采用版同源。</summary>
        public static bool TryStart(string theme, int passage, int sequence, out ArcadeDockSeaEntryState state)
        {
            state = default;
            if (theme != "docks" || (passage < 0 || passage > 2)) return false;
            state = new ArcadeDockSeaEntryState { Started = true, Direction = passage == 0 ? 1 : passage == 1 ? 2 : 0, Lane = -3f - (int)((uint)sequence % 3) * 1.1f };
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

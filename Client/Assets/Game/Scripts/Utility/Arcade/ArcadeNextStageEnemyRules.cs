using System;

namespace FirepowerLoop.Arcade
{
    /// <summary>墓穴起身、乌鸦现身及暗影换位的权威阶段；表现只按年龄采样。</summary>
    public struct ArcadeStageEnemyState
    {
        /// <summary>0追击，1越过墓沿，2乌鸦降临，3首次打倒后的蓝影换位。</summary>
        public int Phase;
        /// <summary>暗影只允许重生一次；已消费的次数随同一实体保留。</summary>
        public int RevivesRemaining;
        /// <summary>当前阶段活动秒数，以及动画起点的平面坐标。</summary>
        public float Age, OriginX, OriginY;
    }

    /// <summary>单人与服务器共用阶段时长和已确认的固定墓位，不拥有实体或伤害。</summary>
    public static class ArcadeNextStageEnemyRules
    {
        /// <summary>两类特色身份与已采用布局的固定墓位数量。</summary>
        public const int GraveKind=18, ShadowKind=19, TombCount=16;
        /// <summary>土面起点与墓沿外落脚留量，包含正式敌人的碰撞半径。</summary>
        public const float GraveOriginOffset=.35f, GraveLandingOffset=2.85f;
        /// <summary>固定墓地为四列四排；坐标对应已确认的墓碑中心。</summary>
        public static void GraveTomb(int index,out float x,out float z)
        {
            if(index<0||index>=TombCount)throw new ArgumentOutOfRangeException(nameof(index));
            x=TombAxis(index%4);z=TombAxis(index/4);
        }
        /// <summary>中央十字通路两侧的四条墓列，和正式场景同源。</summary>
        private static float TombAxis(int index)=>index==0?-13:index==1?-8:index==2?8:13;
        /// <summary>返回本帧是否被现身占用；完成帧仍不攻击，下一活动帧才开始追击。</summary>
        public static bool Advance(ref ArcadeStageEnemyState state,float seconds,ArcadeContentTuning tuning)
        {
            if(state.Phase==0)return false;
            if(!ArcadeRoundContentRules.Finite(seconds)||seconds<=0)return true;
            float duration=state.Phase==1?tuning.SkillPeriod:state.Phase==2?tuning.SkillWarning:tuning.EventWarning;
            state.Age+=seconds;
            if(state.Age>=duration){state.Phase=0;state.Age=0;}
            return true;
        }
    }
}

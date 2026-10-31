using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    /// <summary>幸存者场内爬出状态；仅由玩法活动时间推进，不进入闯关存档或联机协议。</summary>
    public struct SurvivorGroundEntryState
    {
        /// <summary>保留入场身份，迟到的表现投影不能重复开始动作。</summary>
        public bool Started;
        /// <summary>本局活动年龄、洞口位置和出生时锁定朝向。</summary>
        public float Age,StartX,StartZ,ForwardX,ForwardZ;
        public bool Active=>Started&&Age<SurvivorGroundEntryRules.TotalSeconds;
    }
    /// <summary>采用版翻土、撑地、抬膝与站稳时间轴；表现与命中使用同一采样年龄及下沉。</summary>
    public static class SurvivorGroundEntryRules
    {
        public const float WarningSeconds=.8f,TotalSeconds=5.2f;
        /// <summary>只复用码头抓边至双脚登台段，不播放海中游近。</summary>
        public static float ClipAge(float age)
        {
            if(age<.8f)return 2.1f;
            if(age<1.6f)return Mathf.Lerp(2.1f,2.9f,Mathf.SmoothStep(0,1,(age-.8f)/.8f));
            if(age<2.1f)return 2.9f+Mathf.Sin((age-1.6f)*Mathf.PI/.5f)*.035f;
            if(age<3.1f)return Mathf.Lerp(2.9f,4.05f,Mathf.SmoothStep(0,1,age-2.1f));
            if(age<3.55f)return 4.05f-Mathf.Sin((age-3.1f)*Mathf.PI/.45f)*.06f;
            return Mathf.Lerp(4.05f,5.75f,Mathf.SmoothStep(0,1,(age-3.55f)/1.65f));
        }
        /// <summary>提示期整个身体藏在土下，随后手臂与躯干随撑身逐渐露出。</summary>
        public static float ExtraDepth(float age)=>-1.1f*(1-Mathf.SmoothStep(0,1,Mathf.Clamp01((age-.6f)/1f)));
        /// <summary>采用版爬行的平面位移；刷怪时须验证整段而非只有终点。</summary>
        public static float CrawlDistance(float age)=>12.55f-ArcadeDockSeaEntryRules.Sample(ClipAge(age)).X;
        public static Vector2 Position(SurvivorGroundEntryState state)=>new Vector2(state.StartX,state.StartZ)+new Vector2(state.ForwardX,state.ForwardZ)*CrawlDistance(state.Age);
        /// <summary>完成帧仍属于登场，下一活动帧才允许追击和近战。</summary>
        public static bool Advance(ref SurvivorGroundEntryState state,float seconds)
        {
            if(!state.Active)return false;
            if(!float.IsNaN(seconds)&&!float.IsInfinity(seconds)&&seconds>0)state.Age=Mathf.Min(TotalSeconds,state.Age+seconds);
            return true;
        }
        public static bool Valid(SurvivorGroundEntryState state)=>state.Started&&ArcadeDockSeaEntryRules.Finite(state.Age)&&state.Age>=0&&state.Age<=TotalSeconds&&
            ArcadeDockSeaEntryRules.Finite(state.ForwardX)&&ArcadeDockSeaEntryRules.Finite(state.ForwardZ)&&Mathf.Abs(state.ForwardX*state.ForwardX+state.ForwardZ*state.ForwardZ-1)<.002f;
    }
}

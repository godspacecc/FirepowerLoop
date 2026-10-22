using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatViewController
    {
        /// <summary>奖励实例随现有异步视觉事务释放，不创建碰撞体或名称HUD。</summary>
        public void SetRoundReward(ArcadeRoundRewardState state,ArcadeRoundRewardTuning tuning)
        {
            const string key="round-reward";
            if(state==null||state.Phase==ArcadeRoundRewardPhase.Expired){ReleaseEffectVisual(key);return;}
            SetEffectVisual(key,"round-reward","Prefabs/SceneObjects/ArcadeRoundRewardVisual",new Vector2(state.X,state.Z),Vector3.zero);
            if(!effectVisuals.TryGetValue(key,out var entry))return;entry.RoundReward=state;entry.RewardTuning=tuning;ApplyRewardPortalAppearance(entry);
        }
        /// <summary>整关门独占一个视觉，普通轮次不展示。</summary>
        public void SetStagePortal(bool visible,Vector2 position,float seconds,float appearSeconds)
        {
            const string key="stage-portal";if(!visible){ReleaseEffectVisual(key);return;}
            SetEffectVisual(key,"stage-portal","Prefabs/SceneObjects/ArcadeStagePortalVisual",position,Vector3.zero);
            if(!effectVisuals.TryGetValue(key,out var entry))return;entry.IsStagePortal=true;entry.PortalSeconds=seconds;entry.PortalAppearSeconds=appearSeconds;ApplyRewardPortalAppearance(entry);
        }
        /// <summary>异步回调也应用最近状态，避免晚到资源重播坠落或显示已领金币。</summary>
        private static void ApplyRewardPortalAppearance(EffectVisualEntry entry)
        {
            if(entry.Instance==null)return;
            if(entry.RoundReward!=null)entry.Instance.GetComponent<ArcadeRoundRewardVisual>()?.Apply(entry.RoundReward,entry.RewardTuning);
            if(entry.IsStagePortal)entry.Instance.GetComponent<ArcadeStagePortalVisual>()?.Apply(entry.PortalSeconds,entry.PortalAppearSeconds);
        }
    }
}

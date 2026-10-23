using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>每个主线轮清场一次，以玩家清场时的位置投影附近可走地面，落下后不跟随。</summary>
        private void StartRoundReward(ArcadeCombatRuntime r)
        {
            if(r.IsChallenge||r.SlayerChallenge!=null||this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute!=true||catalog.Rule.RoundReward==null)return;
            var anchor=RewardGround(r,r.PlayerPosition.x,r.PlayerPosition.y,.65f);
            if(!ArcadeRoundRewardRules.TryStart(ref r.RoundReward,catalog.Rule.RoundReward,(r.ArenaNumber-1)*4+r.WaveIndex+1,anchor.x,anchor.y))return;
            for(int i=0;i<r.RoundReward.Items.Count;i++)
            {
                var item=r.RoundReward.Items[i];var position=RewardGround(r,item.X,item.Z,.25f);
                r.RoundReward.Items[i]=new ArcadeRoundRewardItem(item.Id,item.GoldBar,item.Value,position.x,position.y);
            }
        }
        /// <summary>特效与地面寿命只消耗活动秒；完整播完后才打开领取。</summary>
        private void TickRoundReward(ArcadeCombatRuntime r,float seconds)
        {
            ArcadeRoundRewardRules.Advance(r.RoundReward,catalog.Rule.RoundReward,seconds);
            if(!UsesStagePortal(r))return;
            if(r.StagePortal==null)
            {
                var t=catalog.Rule.RoundReward;var position=RewardGround(r,EarlyStage?.PortalX??t.PortalX,EarlyStage?.PortalY??t.PortalZ,t.PortalRadius);
                r.StagePortal=new ArcadeStagePortalState{X=position.x,Z=position.y};
            }
            ArcadeStagePortalRules.Advance(r.StagePortal,seconds);
            ArcadeStagePortalRules.TryContact(r.StagePortal,catalog.Rule.RoundReward,0,r.PlayerPosition.x,r.PlayerPosition.y,r.PlayerHealth>0&&r.RespawnRemaining<=0);
        }
        /// <summary>整关出口与普通轮通道独立，独立挑战保持原合同。</summary>
        private bool UsesStagePortal(ArcadeCombatRuntime r)=>catalog.Rule.RoundReward!=null&&!r.IsChallenge&&r.SlayerChallenge==null&&r.AwaitingExit&&r.WaveIndex>=3&&this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute==true;
        /// <summary>只接受活着的玩家在门完成出现后主动走入。</summary>
        private bool StagePortalContact(ArcadeCombatRuntime r)=>ArcadeStagePortalRules.TryContact(r.StagePortal,catalog.Rule.RoundReward,0,r.PlayerPosition.x,r.PlayerPosition.y,r.PlayerHealth>0&&r.RespawnRemaining<=0);
        /// <summary>先注销物品身份再发积分；失败回滚身份，不生成局外货币。</summary>
        private void CollectRoundReward(ArcadeCombatRuntime r)
        {
            var state=r.RoundReward;if(state==null||r.InTreasureRoom||r.RespawnRemaining>0)return;
            foreach(var item in state.Items)
            {
                if(!ArcadeRoundRewardRules.TryCollect(state,catalog.Rule.RoundReward,item.Id,r.PlayerPosition.x,r.PlayerPosition.y,r.PlayerHealth>0,out var awarded))continue;
                long before=this.GetModel<ArcadeRunModel>().Current?.Score??0;
                if(!AwardOrdinaryTreasure(awarded.Value)){state.Collected.Remove(item.Id);state.Phase=ArcadeRoundRewardPhase.Ready;continue;}
                long added=(this.GetModel<ArcadeRunModel>().Current?.Score??before)-before;
                view?.PlayTreasurePickup(added,0,new Vector2(item.X,item.Z));
            }
        }
        /// <summary>金币和门都放在可走地面，不嵌入丘陵或障碍。</summary>
        private Vector2 RewardGround(ArcadeCombatRuntime r,float x,float z,float radius)
        {
            var obstacles=ObstaclesFor(r);var island=IslandFor(r);
            var p=ArcadeRoundRewardRules.FindGround(x,z,radius,ArenaHalfWidth,ArenaHalfHeight,
                (px,pz)=>IsInsideObstacle(new Vector2(px,pz),radius,obstacles)||island!=null&&!island.ContainsCircle(px,pz,radius));
            return new Vector2(p.X,p.Y);
        }
    }
}

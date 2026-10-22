using System;
using System.Linq;
using FirepowerLoop.Arcade;
namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>主线每轮只创建一次宝箱；同轮清场重复回调不重发。</summary>
        private static void StartRoundReward(OnlineArcadeBattleRuntime b)
        {
            if(!b.IsCasualRoute||b.IsChallenge||b.SlayerChallenge!=null)return;
            var t=ServerConfig.Instance.RoundReward;var rule=ServerConfig.Instance.ArcadeRule;
            var ground=RewardGround(b,0,0,.65f);float x=ground.X,z=ground.Y;
            if(!ArcadeRoundRewardRules.TryStart(ref b.RoundReward,t,(b.ArenaNumber-1)*4+b.WaveIndex+1,x,z))return;
            for(int i=0;i<b.RoundReward.Items.Count;i++)
            {var item=b.RoundReward.Items[i];var point=RewardGround(b,item.X,item.Z,.25f);b.RoundReward.Items[i]=new ArcadeRoundRewardItem(item.Id,item.GoldBar,item.Value,point.X,point.Y);}
        }
        /// <summary>完整特效门禁与地面寿命由权威活动时钟推进。</summary>
        private static bool TickRoundReward(this OnlineArcadeRunActor self,OnlineArcadeBattleRuntime b,float seconds)
        {
            if(seconds<=0)return false;bool changed=false;
            if(b.RoundReward!=null&&b.RoundReward.Phase!=ArcadeRoundRewardPhase.Expired){ArcadeRoundRewardRules.Advance(b.RoundReward,ServerConfig.Instance.RoundReward,seconds);changed=true;}
            if(UsesStagePortal(b))
            {
                var t=ServerConfig.Instance.RoundReward;
                if(b.StagePortal==null){var ground=RewardGround(b,EarlyStage(b)?.PortalX??t.PortalX,EarlyStage(b)?.PortalY??t.PortalZ,t.PortalRadius);b.StagePortal=new ArcadeStagePortalState{X=ground.X,Z=ground.Y};}
                ArcadeStagePortalRules.Advance(b.StagePortal,seconds);
                foreach(var p in b.Players.Values)ArcadeStagePortalRules.TryContact(b.StagePortal,t,p.PlayerId,p.PositionX,p.PositionZ,p.Alive&&!p.InTreasureRoom&&p.RespawnSeconds<=0);
                changed=true;
            }
            return changed;
        }
        /// <summary>稳定玩家顺序竞争共享物品，先注销身份，发奖失败回滚，不补发遗留物。</summary>
        private static bool CollectRoundReward(OnlineArcadeRunActor self,OnlineArcadeBattleRuntime b)
        {
            var state=b.RoundReward;if(state==null||state.Phase!=ArcadeRoundRewardPhase.Ready)return false;bool changed=false;
            var rules=ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.FirstOrDefault();if(rules==null)return false;
            var parameters=ArcadeTreasureRoomParameters.FromConfig(rules);
            foreach(var p in b.Players.Values.OrderBy(p=>p.PlayerId))
            {
                if(!p.Alive||p.InTreasureRoom||p.RespawnSeconds>0)continue;
                foreach(var item in state.Items)
                {
                    if(!ArcadeRoundRewardRules.TryCollect(state,ServerConfig.Instance.RoundReward,item.Id,p.PositionX,p.PositionZ,true,out var award))continue;
                    var reward=new ArcadeTreasureRewardParameters(item.Id,FirepowerLoop.Arcade.ArcadeTreasureRewardKind.GoldBar,item.X,item.Z,award.Value,award.Value,1);
                    if(!ApplyTreasureReward(self,p.PlayerId,reward,parameters)){state.Collected.Remove(item.Id);state.Phase=ArcadeRoundRewardPhase.Ready;continue;}
                    changed=true;
                }
            }
            return changed;
        }
        /// <summary>快照只公开位置、阶段与已领身份，不公开客户端可改的奖励价值。</summary>
        private static void AppendRoundRewardSnapshot(OnlineArcadeBattleRuntime b,OnlineArcadeBattleInfo snapshot)
        {
            snapshot.StagePortalSeconds=b.StagePortal?.ElapsedSeconds??0;snapshot.StagePortalX=b.StagePortal?.X??0;snapshot.StagePortalZ=b.StagePortal?.Z??0;
            var r=b.RoundReward;if(r==null)return;var target=OnlineArcadeRoundRewardInfo.Create();snapshot.RoundReward=target;
            target.RoundIdentity=r.RoundIdentity;target.X=r.X;target.Z=r.Z;target.ElapsedSeconds=r.ElapsedSeconds;target.Phase=(int)r.Phase;
            foreach(var item in r.Items){var row=OnlineArcadeRoundRewardItemInfo.Create();row.Id=item.Id;row.X=item.X;row.Z=item.Z;row.GoldBar=item.GoldBar;row.Collected=r.Collected.Contains(item.Id);target.Items.Add(row);}
        }
        /// <summary>响应缓存深复制奖励投影，不能共用池化协议对象。</summary>
        private static void CloneRoundRewardSnapshot(OnlineArcadeBattleInfo source,OnlineArcadeBattleInfo target)
        {
            target.StagePortalSeconds=source.StagePortalSeconds;target.StagePortalX=source.StagePortalX;target.StagePortalZ=source.StagePortalZ;
            var r=source.RoundReward;if(r==null)return;var copy=OnlineArcadeRoundRewardInfo.Create();target.RoundReward=copy;
            copy.RoundIdentity=r.RoundIdentity;copy.X=r.X;copy.Z=r.Z;copy.ElapsedSeconds=r.ElapsedSeconds;copy.Phase=r.Phase;
            foreach(var item in r.Items){var row=OnlineArcadeRoundRewardItemInfo.Create();row.Id=item.Id;row.X=item.X;row.Z=item.Z;row.GoldBar=item.GoldBar;row.Collected=item.Collected;copy.Items.Add(row);}
        }
        private static bool UsesStagePortal(OnlineArcadeBattleRuntime b)=>b.IsCasualRoute&&!b.IsChallenge&&b.SlayerChallenge==null&&b.AwaitingExit&&b.WaveIndex>=3&&(!(b.RoundLayout?.BossKind>0)||b.RoundBossDefeated)&&(!b.IsFinalBoss||b.FinalBossDefeated);
        private static ArcadeRegionPoint RewardGround(OnlineArcadeBattleRuntime b,float x,float z,float radius)
        {
            var rule=ServerConfig.Instance.ArcadeRule;var obstacles=EffectiveObstacles(b);var island=IslandFor(b);
            return ArcadeRoundRewardRules.FindGround(x,z,radius,ArenaHalfWidth(b),ArenaHalfHeight(b),
                (px,pz)=>InsideObstacle(px,pz,radius,obstacles)||island!=null&&!island.ContainsCircle(px,pz,radius));
        }
    }
}

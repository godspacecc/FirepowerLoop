using System;
using System.Linq;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>胜利店的下轮加命在返回的同一候选中交付，不借原墓穴轮号，也不重复领取。</summary>
        internal static bool ApplySlayerChallengeRoundBenefits(OnlineArcadeRunRecord record,out string error)
        {
            if(!TrySlayerMartBenefits(record,record.SlayerMart,true,out var candidate,out var scores,out error))return false;
            record.SlayerMart=candidate;foreach(var player in record.Players)ApplySlayerMartScore(player,scores[player.PlayerId]);
            SyncSlayerMartRoundInventory(record);return true;
        }
        /// <summary>胜利购物继承能力与现金，另开代次且不再次发放墓穴现金箱。</summary>
        private static void BeginSlayerChallengeShop(OnlineArcadeBattleRuntime battle)
        {
            var next=ArcadeSlayerMartRules.BeginChallengeVisit(battle.SlayerMart,ServerConfig.Instance.SlayerMart);
            long cash=ArcadeSlayerChallengeRules.UnclaimedCash(battle.SlayerChallenge,ServerConfig.Instance.SlayerChallenge);
            foreach(var member in next.Players)member.Cash=checked(member.Cash+cash);
            battle.SlayerMart=next;battle.SlayerChallenge=ArcadeSlayerChallengeRules.ClaimCash(battle.SlayerChallenge);
            battle.SlayerMartReturnWave=1;battle.SlayerMartRegionPrepared=false;battle.SlayerMartDeadlineAt=battle.SlayerMartPausedAt=0;
            foreach(var player in battle.Players.Values)
            {player.Alive=true;player.CurrentHealth=player.MaxHealth;player.RespawnSeconds=0;player.MoveX=player.MoveZ=0;player.Firing=false;}
        }
        /// <summary>独立金币保证掉落，只计挑战现金；原主线金币与随机表完全隔离。</summary>
        private static bool SpawnSlayerCoin(OnlineArcadeBattleRuntime battle,OnlineArcadeEnemyRuntime enemy,bool hazardKill)
        {
            if(battle.SlayerChallenge==null)return false;
            if(!hazardKill&&battle.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Collecting&&battle.Drops.Count<ServerConfig.Instance.ArcadeRule.MaxDrops)
            {
                long id=battle.NextDropRuntimeId++;battle.Drops.Add(id,new OnlineArcadeDropRuntime{RuntimeId=id,
                    DropConfigId=ServerConfig.Instance.SlayerChallenge.CoinDropId,PositionX=enemy.OutsideEntry.Active?enemy.OutsideEntry.TargetX:enemy.PositionX,PositionZ=enemy.OutsideEntry.Active?enemy.OutsideEntry.TargetZ:enemy.PositionZ});
            }
            return true;
        }
    }
    public static partial class OnlineArcadeRunManagerSystem
    {
        /// <summary>返回只覆盖实际资源、购物及死亡清理；主线HP、命数、活动资格和目标沿已提交节点。</summary>
        private static OnlineArcadeRunRecord BuildSlayerReturnCandidate(OnlineArcadeRunActor actor)
        {
            var battle=actor.Battle;var candidate=CloneCommittedRecord(actor);
            // 返回后的普通场必须高于挑战及购物现场代次，不能接受延迟的旧输入或加载确认。
            candidate.RoundGeneration=Math.Max(candidate.RoundGeneration,battle.RoundGeneration);
            var shop=battle.SlayerMart.Clone();
            long cash=ArcadeSlayerChallengeRules.UnclaimedCash(battle.SlayerChallenge,ServerConfig.Instance.SlayerChallenge);
            shop.Ready=shop.Closing=true;foreach(var owner in shop.Players){owner.Cash=checked(owner.Cash+cash);owner.MenuOpen=false;}
            candidate.SlayerMart=shop;
            foreach(var target in candidate.Players)
            {
                var live=actor.Record.Players.Single(p=>p.PlayerId==target.PlayerId);var player=battle.Players[target.PlayerId];
                target.Bombs=live.Bombs;target.Dashes=live.Dashes;target.Score=live.Score;target.ScoreMultiplier=live.ScoreMultiplier;target.MultiplierProgress=live.MultiplierProgress;
                target.RoundPowerups=player.Powerups.Values.Where(p=>p.RemainingSeconds>0).Select(OnlineArcadeRunActorSystem.ClonePowerup).ToList();
                target.RoundTemporaryWeaponId=player.TemporaryWeaponSeconds>0?player.TemporaryWeaponId:string.Empty;
                target.RoundTemporaryWeaponSeconds=Math.Max(0,player.TemporaryWeaponSeconds);
                var owner=shop.Players.Single(p=>p.PlayerId==target.PlayerId);owner.Bombs=target.Bombs;owner.Dashes=target.Dashes;owner.Lives=target.Lives;
                owner.Chickens=owner.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)+
                    (player.Powerups.TryGetValue(ArcadePowerupBehavior.Chicken,out var chicken)&&chicken.RemainingSeconds>0?1:0);
            }
            if(!OnlineArcadeRunActorSystem.ApplySlayerChallengeRoundBenefits(candidate,out var error))throw new InvalidOperationException(error);
            candidate.ChallengeAttempted=true;candidate.ChallengeId=string.Empty;return candidate;
        }
    }
}

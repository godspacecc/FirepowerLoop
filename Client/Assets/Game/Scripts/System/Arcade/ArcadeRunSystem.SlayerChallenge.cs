using System;
using System.Linq;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using QFramework;

namespace Game
{
    public sealed partial class ArcadeRunSystem
    {
        /// <summary>只在已完成配置场序的规则1节点给出一次机会，不影响规则0或普通40轮长度。</summary>
        private void ConfigureSlayerOpportunity(ArcadeRunSaveData candidate)
        {
            if(candidate==null||candidate.Victory||!candidate.IsCasualRoute||candidate.SlayerMartRulesVersion!=1||candidate.Checkpoint==null)return;
            if(this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerChallenge(out var c,out _)&&c.Enabled&&
                candidate.CompletedArenaCount==c.AfterArena&&!candidate.Checkpoint.ChallengeAttempted)candidate.Checkpoint.ChallengeId=c.ChallengeId;
        }

        /// <summary>完整挑战返回以主线节点为身份，实际库存和购物账本为候选，写盘成功后才公开返回。</summary>
        public bool TryCompleteSlayerChallenge(ArcadeSlayerChallengeState state,ArcadeSlayerMartState shop,
            ArcadeIslandRoundCheckpoint actual,out string error)
        {
            error="Slayer返回身份无效";var model=this.GetModel<ArcadeRunModel>();var current=model.Current;
            if(model.Phase!=ArcadeRunPhase.Challenge||current?.Checkpoint==null||state==null||shop==null||actual==null||
                state.Phase!=ArcadeSlayerChallengePhase.Returning||state.RunId!=current.RunId||state.ReturnArena!=current.NextArenaNumber||
                state.ReturnMap!=current.ArenaConfigNumber||state.Members.Count!=1||state.Members[0]!=1||actual.Bombs<0||actual.Dashes<0)return false;
            var provider=this.GetUtility<IRuntimeConfigProvider>();
            if(!provider.TryGetSlayerChallenge(out var c,out error)||current.Checkpoint.ChallengeId!=c.ChallengeId||
                !provider.TryGetSlayerMart(out var shopConfig,out error)||!provider.TryGetArcadeCatalog(out var catalog,out error))return false;
            var candidate=current.Clone();candidate.Checkpoint.ChallengeAttempted=true;candidate.Checkpoint.ChallengeId=string.Empty;
            candidate.EmergencyResources[ArcadeRunDefaults.BombResourceId]=actual.Bombs;candidate.EmergencyResources[ArcadeRunDefaults.DashResourceId]=actual.Dashes;
            var nextShop=shop.Clone();nextShop.Ready=nextShop.Closing=true;
            long cash=ArcadeSlayerChallengeRules.UnclaimedCash(state,c);
            foreach(var member in nextShop.Players){member.Cash=checked(member.Cash+cash);member.MenuOpen=false;member.Bombs=actual.Bombs;member.Dashes=actual.Dashes;member.Lives=current.RemainingLives;}
            var treasure=catalog.TreasureRooms.First(p=>p.Rewards.Any(r=>r.Kind==FirepowerLoop.Arcade.ArcadeTreasureRewardKind.Gem));
            if(!ArcadeSlayerMartBenefitRules.TryDeliver(nextShop,shopConfig,treasure,new Dictionary<long,ArcadeTreasureScoreState>{{1,new(current.Score,current.ScoreMultiplier,current.MultiplierProgress,current.RemainingLives,current.ScoreLifeTier)}},
                true,out nextShop,out var scores,out error))return false;
            var score=scores[1];candidate.Score=score.Score;candidate.ScoreMultiplier=score.Multiplier;candidate.MultiplierProgress=score.Progress;candidate.RemainingLives=score.Lives;candidate.ScoreLifeTier=score.LifeTier;
            candidate.SlayerMart=nextShop;
            var entry=candidate.RoundCheckpoint?.PlayerState??candidate.ArenaEntryState;
            if(entry!=null)
            {
                entry.Bombs=actual.Bombs;entry.Dashes=actual.Dashes;entry.Powerups=actual.Powerups.Select(p=>p.Clone()).ToList();
                entry.TemporaryWeaponId=actual.TemporaryWeaponId;entry.TemporaryWeaponRemaining=actual.TemporaryWeaponRemaining;entry.FirstPersonRemaining=actual.FirstPersonRemaining;
            }
            if(candidate.RoundCheckpoint!=null)candidate.RoundCheckpoint.SlayerMart=nextShop.Clone();
            candidate.UpdatedAtUtc=clock.UtcNow.ToUniversalTime().ToString("O");
            if(!store.Save(candidate,out error))return false;
            CommitSafeNode(candidate);error=null;return true;
        }

        /// <summary>真正选择进入时先落盘尝试标记；准备或写失败不能消耗机会，也不把阶段切回主线。</summary>
        public bool TryCommitSlayerEntry(ArcadeSlayerChallengeState preparation,out string error)
        {
            var model=this.GetModel<ArcadeRunModel>();var current=model.Current;error="Slayer进入身份已失效";
            if(model.Phase!=ArcadeRunPhase.Challenge||current?.Checkpoint==null||current.Checkpoint.ChallengeAttempted||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerChallenge(out var c,out error)||!c.Enabled||
                current.SlayerMartRulesVersion!=1||current.Checkpoint.ChallengeId!=c.ChallengeId||preparation==null||
                preparation.RunId!=current.RunId||preparation.Phase!=ArcadeSlayerChallengePhase.Preparing||preparation.Generation<=0||
                preparation.ReturnArena!=current.NextArenaNumber||preparation.ReturnMap!=current.ArenaConfigNumber||
                preparation.Members.Count!=1||preparation.Members[0]!=1)return false;
            var candidate=current.Clone();candidate.Checkpoint.ChallengeAttempted=true;candidate.UpdatedAtUtc=clock.UtcNow.ToUniversalTime().ToString("O");
            if(!store.Save(candidate,out error))return false;
            model.CommitChallenge(candidate);Publish();error=null;return true;
        }
    }
}

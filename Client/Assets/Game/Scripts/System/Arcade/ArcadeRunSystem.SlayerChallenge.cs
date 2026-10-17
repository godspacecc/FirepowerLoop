using System;
using System.Linq;
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

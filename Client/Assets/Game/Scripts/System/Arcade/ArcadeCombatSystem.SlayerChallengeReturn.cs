using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>购物新代次继承个人能力和真实库存，挑战现金仅在这里领取一次。</summary>
        private void BeginSlayerChallengeShop(ArcadeCombatRuntime runtime)
        {
            var next=ArcadeSlayerMartRules.BeginChallengeVisit(runtime.SlayerMart,slayerMartConfig);
            long cash=ArcadeSlayerChallengeRules.UnclaimedCash(runtime.SlayerChallenge,slayerChallengeConfig);
            foreach(var owner in next.Players){owner.Cash=checked(owner.Cash+cash);SyncSlayerMartInventory(runtime,owner);}
            runtime.SlayerMart=next;runtime.SlayerChallenge=ArcadeSlayerChallengeRules.ClaimCash(runtime.SlayerChallenge);
            runtime.SlayerMartReturnWave=1;runtime.SlayerMartRegionPrepared=false;runtime.PlayerHealth=runtime.PlayerMaxHealth;runtime.RespawnRemaining=0;
            ClearArcadeInput(runtime);
        }
        /// <summary>挑战普通击杀保证实体现金币，不进入随机主线掉落或主线计分。</summary>
        private bool SpawnSlayerCoin(ArcadeCombatRuntime runtime,ArcadeEnemyRuntime enemy,bool hazardKill)
        {
            if(runtime.SlayerChallenge==null)return false;
            if(!hazardKill&&runtime.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Collecting&&runtime.Drops.Count<catalog.Rule.MaxDrops)
            {
                var drop=runtimePool.RentDrop();drop.Id=runtime.NextEntityId++;drop.Position=enemy.Position;
                drop.Definition=catalog.Drops.Single(p=>p.Id==slayerChallengeConfig.CoinDropId);runtime.Drops.Add(drop.Id,drop);
            }
            view?.RemoveEnemy(enemy.Id,true);runtimePool.Return(enemy);return true;
        }
        /// <summary>返回写失败保留冻结现场，重试相同资源与已领取账本，不重进收币或购物。</summary>
        private void CompleteSlayerChallenge(ArcadeCombatRuntime runtime)
        {
            if(runtime.CompletionSubmitted)return;runtime.CompletionSubmitted=true;
            if(!this.GetSystem<ArcadeRunSystem>().TryCompleteSlayerChallenge(runtime.SlayerChallenge,runtime.SlayerMart,CaptureIslandCheckpoint(runtime),out var error))
            {runtime.CompletionSubmitted=false;runtime.CompletionAwaitingRetry=true;FailCombat(error);return;}
            this.SendEvent(new ArcadeArenaCompletedEvent());
        }
        /// <summary>单人提前退出走相同领域投票；不发胜利奖金或开启商店。</summary>
        public bool TrySetSlayerExit(bool value,out string error)
        {
            var runtime=this.GetModel<ArcadeCombatModel>().Runtime;error="Slayer不在战斗阶段";
            if(runtime?.SlayerChallenge==null||runtime.SlayerChallenge.Phase!=ArcadeSlayerChallengePhase.Collecting&&runtime.SlayerChallenge.Phase!=ArcadeSlayerChallengePhase.Boss)return false;
            runtime.SlayerChallenge=ArcadeSlayerChallengeRules.SetExitVote(runtime.SlayerChallenge,1,value);error=null;return true;
        }
        /// <summary>界面只取得深复制的挑战工作快照，不持有权威对象。</summary>
        public ArcadeSlayerChallengeState GetSlayerChallengeSnapshot()=>this.GetModel<ArcadeCombatModel>().Runtime?.SlayerChallenge?.Clone();
    }
}

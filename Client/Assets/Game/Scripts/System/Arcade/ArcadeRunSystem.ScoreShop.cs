using FirepowerLoop.Arcade;
using System;
using System.Linq;
using QFramework;

namespace Game
{
    public sealed partial class ArcadeRunSystem
    {
        /// <summary>个人商店事务入口，成功保存前不公布积分或候选变化。</summary>
        public bool TryOperateScoreShop(ScoreShopAction action,string dropId,int revision,out ArcadeRunSaveData snapshot,out string error)
        {
            var model=this.GetModel<ArcadeRunModel>();var current=model.Current;snapshot=current;
            error="当前没有可操作的积分商店";
            if(model.Phase!=ArcadeRunPhase.SafeCheckpoint||current==null||current.HasResult||
                current.ScoreShopRulesVersion!=1||current.ScoreShop?.NextArena!=current.NextArenaNumber)return false;
            var provider=this.GetUtility<IRuntimeConfigProvider>();
            if(provider==null||!provider.TryGetArcadeCatalog(out var catalog,out error))return false;
            if(!ArcadeScoreShopRules.TryOperate(catalog.ScoreShop,current.ScoreShop,current.Score,revision,action,dropId,
                out var shop,out var score,out error))return false;
            var candidate=current.Clone();candidate.ScoreShop=shop;candidate.Score=score;
            candidate.ScoreLifeTier=Math.Max(current.ScoreLifeTier,current.Score/catalog.TreasureRooms.First().LifeThreshold);
            candidate.UpdatedAtUtc=clock.UtcNow.ToUniversalTime().ToString("O");
            if(!store.Save(candidate,out error))return false;
            CommitSafeNode(candidate);snapshot=candidate.Clone();return true;
        }
        /// <summary>整关结算候选内生成一次访问；终局直接清理，不在恢复或显示界面时重新抽取。</summary>
        private bool ConfigureScoreShop(ArcadeRunSaveData candidate,out string error)
        {
            error=null;candidate.ScoreShop=null;
            if(candidate.ScoreShopRulesVersion!=1||candidate.HasResult)return true;
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out var catalog,out error))return false;
            try { candidate.ScoreShop=ArcadeScoreShopRules.Open(catalog.ScoreShop,candidate.NextArenaNumber,
                candidate.Checkpoint.GenerationSeed^candidate.CompletedArenaCount*7919);return true; }
            catch(ArgumentException exception){error=exception.Message;return false;}
        }
        /// <summary>只阻止仍待选择的访问；购买后待交付不阻止下一普通关。</summary>
        private static bool ScoreShopUnfinished(ArcadeRunSaveData run)=>run?.ScoreShopRulesVersion==1&&run.ScoreShop is {Completed:false};
    }
}

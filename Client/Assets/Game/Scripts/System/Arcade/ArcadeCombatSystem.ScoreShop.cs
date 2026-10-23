using System.Linq;
using FirepowerLoop.Arcade;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>恢复上一关角色后、下一关首次轮前保存前领取一次；加载冻结和保存失败重试均不再次升档。</summary>
        private bool TryApplyScoreShopDrop(ArcadeCombatRuntime runtime,ArcadeRunSaveData run,out string error)
        {
            error=null;var shop=run?.ScoreShop;
            if(runtime.IsChallenge||runtime.ScoreShopDeliveryApplied||shop==null||shop.Delivered||shop.PurchasedDropId==null)return true;
            if(run.ScoreShopRulesVersion!=1||shop.NextArena!=runtime.ArenaNumber||!shop.Completed||
                !ArcadeScoreShopRules.IsValid(catalog.ScoreShop,shop))
            {error="商店待领取记录与下一关不匹配";return false;}
            var drop=catalog.Drops.FirstOrDefault(d=>d.Id==shop.PurchasedDropId);
            if(drop.Id==null||!CanApplyDrop(drop)||!ApplyDrop(runtime,drop))
            {error="商店空投领取失败，原存档已保留";return false;}
            runtime.ScoreShopDeliveryApplied=true;return true;
        }
    }
}

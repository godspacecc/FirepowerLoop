using System;
using System.Linq;
using QFramework;

namespace Game
{
    /// <summary>负责基地工艺生产和结果已读事务；不负责远征奖励计算或界面布局。</summary>
    public sealed class CivilisationProductionSystem : AbstractSystem
    {
        private bool busy;
        protected override void OnInit()
        {
            var save = this.GetSystem<SaveSystem>();
            if (!save.RegisterSection(this.GetModel<CivilisationTechnologyModel>(), out var error) ||
                !save.RegisterSection(this.GetModel<CivilisationExpeditionModel>(), out error)) throw new InvalidOperationException(error);
        }

        /// <summary>仅基地可生产；产物和金币一起落盘，失败恢复完整运行时快照。</summary>
        public bool TryProduce(string productionId, int amount, out string error)
        {
            error = null;
            if (busy || amount < 1 || amount > 100 || this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                this.GetModel<MapSceneModel>().CurrentMapId != MapIds.Base || this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.Base ||
                this.GetModel<SaveModel>().CurrentBase == null || this.GetModel<PlayerBagModel>().RiskState != PlayerBagRiskState.BaseSafe)
            { error = Loc.T("civilisation.error.context"); return false; }
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out error)) return false;
            var recipe = catalog.Productions.FirstOrDefault(r => r.Id == productionId);
            if (recipe == null || !this.GetModel<CivilisationTechnologyModel>().IsUnlocked(recipe.TechnologyId))
            { error = Loc.T("civilisation.error.locked"); return false; }
            var currency = this.GetModel<CurrencyModel>();
            var cost = checked((long)recipe.CostGold * amount);
            if (currency.Gold < cost) { error = Loc.T("civilisation.error.gold"); return false; }
            var inventory = this.GetSystem<InventorySystem>();
            if (!inventory.TryBuildPlayerBagAddCandidate(recipe.OutputItemId, amount, out var candidate, out error)) return false;
            var save = this.GetSystem<SaveSystem>();
            var before = save.CaptureRuntimeSnapshot();
            busy = true;
            var committed = false;
            try
            {
                if (!inventory.TryApplyPlayerBagCandidate(candidate) || !currency.TrySetGold(currency.Gold - cost))
                { error = Loc.T("civilisation.error.save"); return false; }
                save.MarkDirty("civilisation production");
                if (!save.Flush(out error)) return false;
                committed = true;
                inventory.PublishPlayerBagChanged();
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
            finally
            {
                if (!committed) save.RestoreRuntimeSnapshot(before, out _);
                busy = false;
            }
        }

        /// <summary>已读只改变结果展示状态，不重新兑换；保存失败保持未读，可再次打开。</summary>
        public bool TryAcknowledgeReceipt(out string error)
        {
            error = null;
            if (busy || this.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.Base) return false;
            var model = this.GetModel<CivilisationTechnologyModel>();
            var receipt = model.Receipt;
            if (receipt == null || receipt.Value<bool>("acknowledged")) return true;
            var before = model.Capture();
            var after = model.Capture();
            after["receipt"]["acknowledged"] = true;
            model.Restore(after);
            var save = this.GetSystem<SaveSystem>();
            save.MarkDirty("civilisation receipt acknowledged");
            if (save.Flush(out error)) return true;
            model.Restore(before);
            return false;
        }
    }
}

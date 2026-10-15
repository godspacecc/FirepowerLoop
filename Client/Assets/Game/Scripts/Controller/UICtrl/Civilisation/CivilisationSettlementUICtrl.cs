using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using QFramework;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>呈现已经落盘的返程结果；只提交已读意图，不计算或发放奖励。</summary>
    public sealed class CivilisationSettlementUICtrl : BaseUICtrl
    {
        private CivilisationSettlementUI UI => BaseUI as CivilisationSettlementUI;
        private bool allowClose;

        protected override string Path()
        {
            return "Civilisation/CivilisationSettlement";
        }

        protected override void OnInit()
        {
            UI.BtnContinue.onClick.AddListener(Continue);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if (IsShow()) Refresh(); })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            allowClose = false;
            Refresh();
            UI.BodyScroll.verticalNormalizedPosition = 1;
        }

        protected override void OnHide()
        {
            EventSystem.current?.SetSelectedGameObject(null);
        }

        protected override bool CanHide() => allowClose;

        /// <summary>成功返程或恢复基地后展示未读结果；展示失败不回滚已经完成的结算。</summary>
        public static bool TryShowPending()
        {
            var architecture = Game.Interface;
            if (architecture.GetModel<MapSceneModel>().CurrentMapId != MapIds.Base ||
                architecture.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                architecture.GetModel<SaveModel>().CurrentDomain != OfflineSaveDomain.Base) return false;
            var receipt = architecture.GetModel<CivilisationTechnologyModel>().Receipt;
            if (receipt == null || receipt.Value<bool>("acknowledged")) return false;
            try { UIHelper.ShowUI<CivilisationSettlementUICtrl>(); return true; }
            catch (Exception e) { Log.Error("[CivilisationSettlementUICtrl] 结算结果展示失败：" + e.Message); return false; }
        }

        private void Refresh()
        {
            var receipt = this.GetModel<CivilisationTechnologyModel>().Receipt;
            UI.BodyText.text = receipt == null ? Loc.T("civilisation.receipt.none") : BuildReceiptText(receipt, this.GetUtility<IRuntimeConfigProvider>());
        }

        private void Continue()
        {
            if (!IsShow()) return;
            if (!this.GetSystem<CivilisationProductionSystem>().TryAcknowledgeReceipt(out var error))
            { Notify.ShowFloatTips(string.IsNullOrEmpty(error) ? Loc.T("civilisation.error.save") : error); return; }
            allowClose = true;
            Hide();
        }

        /// <summary>将只读结算快照转为当前语言的文字；调用不修改金币、科技或已读状态。</summary>
        public static string BuildReceiptText(JObject receipt, IRuntimeConfigProvider config)
        {
            if (!config.TryGetCivilisationCatalog(out var catalog, out var error)) return error;
            var text = new StringBuilder();
            text.AppendLine(Loc.T("civilisation.receipt.gold", receipt.Value<long>("gold"))).AppendLine();
            foreach (var id in (receipt["newChapters"] as JArray ?? new JArray()).Values<string>())
                text.AppendLine(Loc.T("civilisation.receipt.chapter", Loc.T(id + ".name"))).AppendLine();
            text.AppendLine(Loc.T("civilisation.receipt.tech"));
            var technologies = receipt["newTechnologies"] as JArray ?? new JArray();
            if (technologies.Count == 0) text.AppendLine(Loc.T("civilisation.receipt.none"));
            foreach (var id in technologies.Values<string>())
            {
                var technology = catalog.Technologies.FirstOrDefault(t => t.Id == id);
                if (technology != null) text.AppendLine(Loc.T(technology.NameKey)).AppendLine(Loc.T(technology.UnlockKey));
            }
            text.AppendLine().AppendLine(Loc.T("civilisation.receipt.items"));
            var rows = receipt["rows"] as JArray ?? new JArray();
            if (rows.Count == 0) text.AppendLine(Loc.T("civilisation.receipt.none"));
            foreach (var row in rows)
            {
                if (!config.TryGetItem(row.Value<int>("itemId"), out var item, out _)) continue;
                text.Append(Loc.T("civilisation.receipt.row", Loc.T(item.NameKey), row.Value<int>("amount"), row.Value<long>("gold")));
                if (row.Value<int>("excluded") > 0) text.Append(Loc.T("civilisation.receipt.exempt", row.Value<int>("excluded")));
                text.AppendLine();
            }
            text.AppendLine().AppendLine(Loc.T("civilisation.receipt.visited"));
            var places = receipt["visited"] as JArray ?? new JArray();
            if (places.Count == 0) text.AppendLine(Loc.T("civilisation.receipt.none"));
            foreach (var id in places.Values<string>())
            {
                var point = catalog.Points.FirstOrDefault(p => p.Id == id);
                if (point != null) text.AppendLine(Loc.T(point.NameKey));
            }
            return text.ToString();
        }

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }
    }
}

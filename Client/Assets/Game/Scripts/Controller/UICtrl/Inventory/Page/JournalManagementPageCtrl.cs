using QFramework;
using System.Linq;

namespace Game
{
    /// <summary>投影已发现的探索正文；归档权威属于领域存档，不揭示未发现内容，不读取诊断日志。</summary>
    public sealed class JournalManagementPageCtrl : BasePageCtrl
    {
        private JournalManagementPageUI UI => (JournalManagementPageUI)BaseUI;
        /// <summary>分类是分页内暂存的展示选择，隐藏后下次默认进入生存日志。</summary>
        private int category;
        private IUnRegister languageSubscription;
        protected override void OnInit()
        {
            UI.DailyButton.onClick.AddListener(() => SelectCategory(0));
            UI.NotesButton.onClick.AddListener(() => SelectCategory(1));
            UI.CollectionsButton.onClick.AddListener(() => SelectCategory(2));
        }
        protected override void OnShow()
        {
            category = 0;
            languageSubscription = this.RegisterEvent<LanguageChangedEvent>(_ => Refresh());
            Refresh();
        }
        protected override void OnHide()
        {
            languageSubscription?.UnRegister(); languageSubscription = null;
        }
        private void SelectCategory(int value)
        {
            category = value; UI.DetailScroll.verticalNormalizedPosition = 1; Refresh();
        }
        /// <summary>分类、重开和语言变化时重新投影；当局记录仅在远征领域展示，避免返回后残留。</summary>
        private void Refresh()
        {
            if (!IsShow()) return;
            var key = category == 0 ? "daily" : category == 1 ? "notes" : "collections";
            UI.DailySelected.SetActive(category == 0); UI.NotesSelected.SetActive(category == 1); UI.CollectionsSelected.SetActive(category == 2);
            UI.DetailTitleText.text = Loc.T("ui.journal." + key + "_empty");
            UI.DetailBodyText.text = Loc.T("ui.journal." + key + "_hint");
            if (category != 2) return;
            var records = this.GetModel<CivilisationTechnologyModel>().Records.AsEnumerable();
            if (this.GetModel<SaveModel>().CurrentDomain == OfflineSaveDomain.Expedition)
                records = records.Concat(this.GetModel<CivilisationExpeditionModel>().Records);
            var discovered = records.Distinct().OrderBy(id => id, System.StringComparer.Ordinal).ToArray();
            if (discovered.Length == 0) return;
            UI.DetailTitleText.text = Loc.T("ui.journal.collections_title", discovered.Length);
            UI.DetailBodyText.text = Loc.T("ui.journal.records_scope") + "\n\n" + string.Join("\n\n", discovered.Select(id => Loc.T(id)));
        }
    }
}

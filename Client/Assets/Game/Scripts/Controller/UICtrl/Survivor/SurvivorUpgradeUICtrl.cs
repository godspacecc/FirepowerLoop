using QFramework;
using UnityEngine.EventSystems;
namespace Game
{
    /// <summary>显示权威三选一快照；选择和暂停规则由幸存者系统裁决。</summary>
    public sealed class SurvivorUpgradeUICtrl : BaseUICtrl
    {
        private SurvivorUpgradeUI UI => (SurvivorUpgradeUI)BaseUI;
        /// <summary>列表刷新前固定当前候选及版本，旧卡片不能提交新一轮选择。</summary>
        private int[] choices = System.Array.Empty<int>();
        private int revision;
        /// <summary>整局剩余预算的同一份展示快照，三个刷新按钮同步显示。</summary>
        private int remainingRefreshes;
        protected override string Path() => "Survivor/SurvivorUpgrade";
        public override UILayer Layer() => UILayer.Dialog;
        protected override void OnInit()
        {
            UI.Cards.AddItemRefreshListener((item, index) =>
                SurvivorUpgradeBoxCtrl.Bind(item.GetComponent<SurvivorUpgradeBoxUI>(), choices[index], revision, this.GetSystem<SurvivorSystem>(), Select,index,remainingRefreshes,this.GetSystem<SurvivorSystem>().CanRefreshUpgrade(revision,index),RefreshCard));
            this.RegisterEvent<SurvivorChangedEvent>(_ => { if (IsShow()) Refresh(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => { if (IsShow()) Refresh(); }).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow() => Refresh();
        /// <summary>按当前领域状态重画，结束或无候选时关闭遮罩。</summary>
        private void Refresh()
        {
            var model = this.GetModel<SurvivorModel>();
            choices = model.UpgradeChoices;
            revision = model.Snapshot.ChoiceRevision;
            remainingRefreshes=model.Snapshot.RemainingRefreshes;
            if (!model.Snapshot.Active || model.Snapshot.Finished || choices.Length == 0) { Hide(); return; }
            UI.Cards.totalCount = choices.Length;
            UI.Cards.RefreshCells();
        }
        /// <summary>版本由系统核验；重复点击失败后重新呈现有效候选。</summary>
        private void Select(int choiceRevision, int id)
        {
            this.GetSystem<SurvivorSystem>().Choose(choiceRevision, id);
            if (IsShow()) Refresh();
        }
        /// <summary>提交单槽刷新，失效或无替代项时只重画当前权威快照。</summary>
        private void RefreshCard(int choiceRevision,int slot)
        {
            this.GetSystem<SurvivorSystem>().RefreshUpgrade(choiceRevision,slot);
            if(IsShow())Refresh();
        }
        protected override void OnHide()
        {
            UI.Cards.ClearCells();
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(UI.transform)) EventSystem.current.SetSelectedGameObject(null);
        }
    }
}

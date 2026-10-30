using System;
namespace Game
{
    /// <summary>复用卡片完整覆盖展示和回调；不保存业务状态或创建列表项。</summary>
    public static class SurvivorUpgradeBoxCtrl
    {
        /// <summary>以稳定强化编号读取本地化文本，捕获当轮版本防止旧回调重复选择。</summary>
        public static void Bind(SurvivorUpgradeBoxUI view, int id, int revision, SurvivorSystem system, Action<int, int> choose)
        {
            view.TitleText.text = Loc.T("ui.survivor.upgrade." + id + ".title");
            bool valid=system.TryGetUpgradeDetails(id,out int level,out double current,out double next,out bool percentage);
            string Format(double value)=>percentage?(value*100).ToString("0.#")+"%":value.ToString("0.##");
            view.DetailText.text=valid?Loc.T("ui.survivor.upgrade.progress",level,level+1,Format(current),Format(next)):string.Empty;
            view.BtnSelect.interactable = valid;
            view.BtnSelect.onClick.RemoveAllListeners();
            view.BtnSelect.onClick.AddListener(() => choose(revision, id));
        }
    }
}

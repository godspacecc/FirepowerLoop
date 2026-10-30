using System;
namespace Game
{
    /// <summary>复用卡片完整覆盖展示和回调；不保存业务状态或创建列表项。</summary>
    public static class SurvivorUpgradeBoxCtrl
    {
        /// <summary>以稳定强化编号读取本地化文本，捕获当轮版本防止旧回调重复选择。</summary>
        public static void Bind(SurvivorUpgradeBoxUI view, int id, int revision, SurvivorSystem system, Action<int, int> choose)
        {
            if(system.TryGetWeaponUpgrade(id,out var kind,out int weaponLevel,out var before,out var after))
            {
                string name=Loc.T("ui.survivor.weapon."+kind);
                view.TitleText.text=Loc.T(weaponLevel==0?"ui.survivor.weapon.acquire":"ui.survivor.weapon.upgrade",name);
                view.DetailText.text=weaponLevel==0?Loc.T("ui.survivor.weapon.acquire_detail",after.Damage,after.FireIntervalSeconds.ToString("0.00")):
                    Loc.T("ui.survivor.weapon.upgrade_detail",weaponLevel,weaponLevel+1,before.Damage,after.Damage,before.FireIntervalSeconds.ToString("0.00"),after.FireIntervalSeconds.ToString("0.00"));
                view.BtnSelect.interactable=true;view.BtnSelect.onClick.RemoveAllListeners();view.BtnSelect.onClick.AddListener(()=>choose(revision,id));return;
            }
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

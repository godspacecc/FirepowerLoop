using System;
namespace Game
{
    /// <summary>复用卡片完整覆盖展示和回调；不保存业务状态或创建列表项。</summary>
    public static class SurvivorUpgradeBoxCtrl
    {
        /// <summary>以稳定强化编号读取本地化文本，捕获当轮版本防止旧回调重复选择。</summary>
        public static void Bind(SurvivorUpgradeBoxUI view, int id, int revision, SurvivorSystem system, Action<int, int> choose)
        {
            if(system.TryGetModifierUpgrade(id,out var modifier,out int modifierLevel))
            {
                int nextLevel=modifierLevel+1;string effect=modifier.ModifierKind;
                view.TitleText.text=Loc.T("ui.survivor.modifier.title",Loc.T("ui.survivor.weapon."+modifier.TargetWeapon),Loc.T("ui.survivor.modifier."+effect));
                string F(float value)=>value.ToString("0.##");
                view.DetailText.text=effect=="Multishot"?Loc.T("ui.survivor.modifier.Multishot.detail",modifierLevel,nextLevel,1+modifier.EffectCount(modifierLevel),1+modifier.EffectCount(nextLevel)):
                    effect=="Pierce"?Loc.T("ui.survivor.modifier.Pierce.detail",modifierLevel,nextLevel,modifier.EffectCount(modifierLevel),modifier.EffectCount(nextLevel)):
                    effect=="Explosion"?Loc.T("ui.survivor.modifier.Explosion.detail",modifierLevel,nextLevel,F(modifier.EffectRadius(modifierLevel)),F(modifier.EffectRadius(nextLevel)),F(modifier.EffectDamage(modifierLevel)*100),F(modifier.EffectDamage(nextLevel)*100)):
                    Loc.T("ui.survivor.modifier.Split.detail",modifierLevel,nextLevel,modifier.EffectCount(modifierLevel),modifier.EffectCount(nextLevel),F(modifier.EffectDamage(modifierLevel)*100),F(modifier.EffectDamage(nextLevel)*100));
                view.BtnSelect.interactable=true;view.BtnSelect.onClick.RemoveAllListeners();view.BtnSelect.onClick.AddListener(()=>choose(revision,id));return;
            }
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
            // 拾取显示实际半径和本次增量，避免把累计加成误读成最终距离。
            if(valid&&system.TryGetPickupUpgradeDetails(id,out var radius,out var nextRadius))
                view.DetailText.text=Loc.T("ui.survivor.upgrade.pickup_detail",Format(radius),Format(nextRadius),Format(nextRadius-radius));
            // 使用原有卡片展示新强化的实际发数/秒数，不增加战斗HUD。
            if(valid&&system.TryGetRifleUpgradeDetails(id,out bool magazine,out var rifleCurrent,out var rifleNext))
                view.DetailText.text=Loc.T(magazine?"ui.survivor.upgrade.magazine_detail":"ui.survivor.upgrade.reload_detail",Format(rifleCurrent),Format(rifleNext),Format(Math.Abs(rifleNext-rifleCurrent)));
            // 恢复条件和每秒比例来自战斗快照，沿用原卡片布局。
            if(valid&&system.TryGetRecoveryUpgradeDetails(id,out var recoveryKind,out var delay,out var recoveryCurrent,out var recoveryNext))
                view.DetailText.text=Loc.T("ui.survivor.upgrade."+recoveryKind+".detail",delay.ToString("0.#"),recoveryCurrent.ToString("0.#"),recoveryNext.ToString("0.#"));
            view.BtnSelect.interactable = valid;
            view.BtnSelect.onClick.RemoveAllListeners();
            view.BtnSelect.onClick.AddListener(() => choose(revision, id));
        }
    }
}

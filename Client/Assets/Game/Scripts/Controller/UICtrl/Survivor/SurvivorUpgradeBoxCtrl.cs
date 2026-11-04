using System;
using UnityEngine;
namespace Game
{
    /// <summary>复用卡片完整覆盖展示和回调；不保存业务状态或创建列表项。</summary>
    public static class SurvivorUpgradeBoxCtrl
    {
        /// <summary>整卡选择与独立刷新分别绑定；复用时覆盖全部装饰、计数、可用状态与回调。</summary>
        public static void Bind(SurvivorUpgradeBoxUI view,int id,int revision,SurvivorSystem system,Action<int,int> choose,int slot,int remaining,bool canRefresh,Action<int,int> refresh)
        {
            BindDetails(view,id,revision,system,choose);
            view.FireBadge.SetActive(false);view.FrostBadge.SetActive(false);view.PierceBadge.SetActive(false);
            var accent=new Color(.82f,.66f,.35f);int level=0;string category="player";
            if(system.TryGetModifierUpgrade(id,out var modifier,out level))
            {
                string kind=modifier.ModifierKind;category=kind=="Multishot"?"multishot":"modifier";
                view.FireBadge.SetActive(kind=="Fire");view.FrostBadge.SetActive(kind=="Frost");view.PierceBadge.SetActive(kind=="Pierce");
                if(kind=="Fire")accent=new Color(.94f,.46f,.22f);else if(kind=="Frost")accent=new Color(.42f,.76f,.85f);
                if(kind=="Fire"||kind=="Frost"||kind=="Pierce"||kind=="Multishot")
                {
                    string F(float v)=>v.ToString("0.##");
                    string Number(int at)=>kind=="Fire"||kind=="Frost"?F(modifier.StatusStrength(at)*100):modifier.EffectCount(at).ToString();
                    string amount=level==0?Number(1):Loc.T("ui.survivor.cards.next.stat",Number(level),Number(level+1));
                    string stat=Loc.T("ui.survivor.cards."+kind+".stat",amount);
                    string route=kind=="Multishot"?Loc.T("ui.survivor.cards.compatible"):Loc.T("ui.survivor.cards.route.RifleTip");
                    view.DetailText.text="<line-height=43>"+Loc.T("ui.survivor.cards."+kind+".trigger",F(modifier.StatusDurationSeconds))+"\n<size=26><color=#"+ColorUtility.ToHtmlStringRGB(accent)+">"+stat+"</color></size>\n<size=17><color=#91A6A8>"+Loc.T("ui.survivor.cards."+kind+".note")+"</color></size>\n<size=16><color=#7D9196>"+route+"</color></size></line-height>";
                }
            }
            else if(system.TryGetWeaponUpgrade(id,out _,out level,out _,out _))category="weapon";
            else
            {
                system.TryGetUpgradeDetails(id,out level,out _,out _,out _);
                if(system.TryGetRifleUpgradeDetails(id,out _,out _,out _))category="magazine";
                else if(system.TryGetRecoveryUpgradeDetails(id,out _,out _,out _,out _))category="recovery";
            }
            view.CategoryText.text=Loc.T("ui.survivor.cards.category."+category);
            view.LevelText.text=level==0?Loc.T("ui.survivor.cards.first"):Loc.T("ui.survivor.cards.level",level,level+1);
            view.Accent.color=new Color(accent.r,accent.g,accent.b,.72f);
            view.RefreshCountText.text=remaining.ToString();view.RefreshCountText.color=canRefresh?new Color(.94f,.89f,.78f):new Color(.39f,.47f,.49f);
            foreach(var shape in view.BtnRefresh.GetComponentsInChildren<SurvivorUpgradeShape>())
                if(shape.name=="IconStroke"||shape.name=="RefreshArrow")shape.color=canRefresh?new Color(.94f,.89f,.78f):new Color(.39f,.47f,.49f);
            view.BtnRefresh.interactable=canRefresh;view.BtnRefresh.onClick.RemoveAllListeners();view.BtnRefresh.onClick.AddListener(()=>refresh(revision,slot));
        }
        /// <summary>以稳定强化编号读取本地化文本，捕获当轮版本防止旧回调重复选择。</summary>
        private static void BindDetails(SurvivorUpgradeBoxUI view, int id, int revision, SurvivorSystem system, Action<int, int> choose)
        {
            if(system.TryGetModifierUpgrade(id,out var modifier,out int modifierLevel))
            {
                int nextLevel=modifierLevel+1;string effect=modifier.ModifierKind;
                view.TitleText.text=Loc.T("ui.survivor.modifier."+effect);
                string F(float value)=>value.ToString("0.##");
                view.DetailText.text=(effect=="Fire"||effect=="Frost")?Loc.T("ui.survivor.modifier."+effect+".detail",F(modifier.StatusDurationSeconds),F(modifier.StatusStrength(modifierLevel)*100),F(modifier.StatusStrength(nextLevel)*100)):
                    effect=="Multishot"?Loc.T("ui.survivor.modifier.Multishot.detail",modifierLevel,nextLevel,1+modifier.EffectCount(modifierLevel),1+modifier.EffectCount(nextLevel)):
                    effect=="Pierce"?Loc.T("ui.survivor.modifier.Pierce.detail",modifierLevel,nextLevel,modifier.EffectCount(modifierLevel),modifier.EffectCount(nextLevel)):
                    effect=="Explosion"?Loc.T("ui.survivor.modifier.Explosion.detail",modifierLevel,nextLevel,F(modifier.EffectRadius(modifierLevel)),F(modifier.EffectRadius(nextLevel)),F(modifier.EffectDamage(modifierLevel)*100),F(modifier.EffectDamage(nextLevel)*100)):
                    Loc.T("ui.survivor.modifier.Split.detail",modifierLevel,nextLevel,modifier.EffectCount(modifierLevel),modifier.EffectCount(nextLevel),F(modifier.EffectDamage(modifierLevel)*100),F(modifier.EffectDamage(nextLevel)*100));
                view.BtnSelect.interactable=true;view.BtnSelect.onClick.RemoveAllListeners();view.BtnSelect.onClick.AddListener(()=>choose(revision,id));return;
            }
            if(system.TryGetWeaponUpgrade(id,out var kind,out int weaponLevel,out var before,out var after))
            {
                string name=Loc.T("ui.survivor.weapon."+kind);
                view.TitleText.text=kind=="Drone"?Loc.T("ui.survivor.cards.support"):name;
                view.DetailText.text=weaponLevel==0?Loc.T("ui.survivor.weapon.acquire_detail",after.Damage,after.FireIntervalSeconds.ToString("0.00")):
                    Loc.T("ui.survivor.weapon.upgrade_detail",weaponLevel,weaponLevel+1,before.Damage,after.Damage,before.FireIntervalSeconds.ToString("0.00"),after.FireIntervalSeconds.ToString("0.00"));
                // 投掷武器展示火区每秒伤害和半径，避免误读成单颗子弹伤害或射速成长。
                if(kind=="Drone" && system.TryGetDroneFlameUpgradeDamage(id,out var currentDps,out var nextDps))
                    view.DetailText.text=Loc.T("ui.survivor.weapon.Drone.flame_upgrade_detail",currentDps.ToString("0.##"),nextDps.ToString("0.##"));
                if(kind=="Molotov")
                    view.DetailText.text=weaponLevel==0?Loc.T("ui.survivor.weapon.Molotov.acquire_detail",after.FireIntervalSeconds.ToString("0.#"),after.AreaDurationSeconds.ToString("0.#"),after.Damage,after.AreaRadius.ToString("0.##")):
                        Loc.T("ui.survivor.weapon.Molotov.upgrade_detail",before.Damage,after.Damage,before.AreaRadius.ToString("0.##"),after.AreaRadius.ToString("0.##"));
                if(kind=="RocketSupport")
                {
                    string key="ui.survivor.weapon.RocketSupport";
                    view.DetailText.text=weaponLevel==0?Loc.T(key+".acquire_detail",after.Damage,after.FireIntervalSeconds.ToString("0.#")):
                        Loc.T(key+".upgrade_detail",before.Damage,after.Damage,before.AreaRadius.ToString("0.##"),after.AreaRadius.ToString("0.##"));
                }
                if(kind=="SlimeBottle")
                {
                    system.TryGetSlimeUpgradeStrength(id,out var slow,out var nextSlow);
                    view.DetailText.text=weaponLevel==0?Loc.T("ui.survivor.weapon.SlimeBottle.acquire_detail",after.FireIntervalSeconds.ToString("0.#"),after.AreaDurationSeconds.ToString("0.#"),after.AreaRadius.ToString("0.##"),(nextSlow*100).ToString("0.#")):
                        Loc.T("ui.survivor.weapon.SlimeBottle.upgrade_detail",before.AreaRadius.ToString("0.##"),after.AreaRadius.ToString("0.##"),(slow*100).ToString("0.#"),(nextSlow*100).ToString("0.#"));
                }
                view.BtnSelect.interactable=true;view.BtnSelect.onClick.RemoveAllListeners();view.BtnSelect.onClick.AddListener(()=>choose(revision,id));return;
            }
            system.TryGetUpgradeKind(id,out var stableKind);
            view.TitleText.text = Loc.T("ui.survivor.upgrade." + stableKind + ".title");
            bool valid=system.TryGetUpgradeDetails(id,out int level,out double current,out double next,out bool percentage);
            string Format(double value)=>percentage?(value*100).ToString("0.#")+"%":value.ToString("0.##");
            view.DetailText.text=valid?Loc.T("ui.survivor.upgrade.progress",level,level+1,Format(current),Format(next)):string.Empty;
            if(valid&&(stableKind=="DroneFlame"||stableKind=="GlobalPickup"))view.DetailText.text=Loc.T("ui.survivor.upgrade."+stableKind+".detail");
            // 拾取显示实际半径和本次增量，避免把累计加成误读成最终距离。
            if(valid&&system.TryGetPickupUpgradeDetails(id,out var radius,out var nextRadius))
                view.DetailText.text=Loc.T("ui.survivor.upgrade.pickup_detail",Format(radius),Format(nextRadius),Format(nextRadius-radius));
            if(valid&&system.TryGetMoveSpeedUpgradeDetails(id,out var speed,out var nextSpeed))
                view.DetailText.text=Loc.T("ui.survivor.upgrade.move_speed_detail",speed.ToString("0.##"),nextSpeed.ToString("0.##"),(nextSpeed-speed).ToString("0.##"));
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

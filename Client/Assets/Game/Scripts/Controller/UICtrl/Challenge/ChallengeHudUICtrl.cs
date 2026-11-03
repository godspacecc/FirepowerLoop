using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>独立挑战的只读弹药HUD；不阻断输入、不推进换弹。</summary>
    public sealed class ChallengeHudUICtrl : BaseUICtrl
    {
        private ChallengeHudUI UI => (ChallengeHudUI)BaseUI;
        public override bool BlocksGame => false;
        public override UILayer Layer() => UILayer.HUD;
        protected override string Path() => "Challenge/ChallengeHud";

        /// <summary>订阅由当前View销毁时注销，重载不会累加旧界面监听。</summary>
        protected override void OnInit()
        {
            this.RegisterEvent<ChallengeChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_=>{if(IsShow())Refresh();}).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow() => Refresh();
        protected override void OnHide() { UI.Content.gameObject.SetActive(false); }
        private void Refresh() => Render(UI,this.GetModel<ChallengeModel>().Snapshot);

        /// <summary>只展示权威快照，暂停时保留冻结进度，停止时隐藏旧局内容。</summary>
        internal static void Render(ChallengeHudUI view,ChallengeSnapshot snapshot)
        {
            view.Content.gameObject.SetActive(snapshot.Active);
            if(!snapshot.Active) {view.AmmoText.text=string.Empty;view.ReloadText.text=string.Empty;return;}
            string key=snapshot.WeaponId=="challenge.rifle"?"ui.challenge.weapon.rifle":
                snapshot.WeaponId=="challenge.pistol"?"ui.challenge.weapon.pistol":string.Empty;
            string weapon=key.Length>0?Loc.T(key):snapshot.WeaponId;
            view.AmmoText.text=Loc.T("ui.challenge.hud.ammo",weapon,snapshot.Magazine,snapshot.Reserve);
            bool reloading=snapshot.ReloadRemaining>0;
            view.ReloadText.text=reloading?Loc.T("ui.challenge.hud.reload",snapshot.ReloadRemaining):Loc.T("ui.challenge.hud.keys");
            view.ReloadText.color=reloading?new Color(1,.65f,.2f):new Color(.55f,.55f,.55f);
        }
    }
}

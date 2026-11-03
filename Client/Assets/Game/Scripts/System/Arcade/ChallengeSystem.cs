using System;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>独立挑战会话与武器规则；仅接受显式射击意图，不自动攻击、不调用旧模式结算。</summary>
    public sealed class ChallengeSystem : AbstractSystem
    {
        private ChallengeModel Model => this.GetModel<ChallengeModel>();
        protected override void OnInit() => LifeCycle.RegisterUpdate(Tick);
        protected override void OnDeinit() { LifeCycle.UnregisterUpdate(Tick); Stop(); }
        private void Tick() => Advance(Time.deltaTime);

        /// <summary>供挑战场景准备成功后调用；先验证全部配置，失败不提交会话。</summary>
        public bool TryBegin(out string error)
        {
            if(Model.Active) {error="挑战会话已在进行中";return false;}
            var provider=this.GetUtility<IRuntimeConfigProvider>();
            if(provider==null) {error="挑战配置提供者缺失";return false;}
            if(!provider.TryGetChallenge(out var config,out error)) return false;
            if(config==null) {error="挑战配置为空";return false;}
            var weapons=config.CreateLoadout();
            Model.Weapons=weapons;Model.SelectedSlot=0;Model.Paused=false;Model.Active=true;
            Changed();return true;
        }

        /// <summary>退出即丢弃所有本局弹药与换弹事务，重复退出无额外通知。</summary>
        public void Stop()
        {
            var model=Model;if(model==null || !model.Active)return;
            model.Active=false;model.Paused=false;model.Weapons=Array.Empty<ChallengeWeaponState>();model.SelectedSlot=0;
            Changed();
        }

        public void SetPaused(bool paused)
        {
            if(!Model.Active || Model.Paused==paused)return;
            Model.Paused=paused;Changed();
        }

        /// <summary>后台枪只推进射击冷却，换枪时已经取消其换弹。</summary>
        public void Advance(float seconds)
        {
            if(!CanAct || seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds))return;
            bool reloading=Model.Current.ReloadRemaining>0;
            foreach(var gun in Model.Weapons) gun.Advance(seconds);
            if(reloading)Changed();
        }

        public bool TryFire()
        {
            if(!CanAct)return false;
            var gun=Model.Current;float before=gun.ReloadRemaining;
            bool fired=gun.TryFire();
            if(fired || gun.ReloadRemaining!=before)Changed();
            return fired;
        }

        public bool TryReload()
        {
            if(!CanAct || !Model.Current.TryReload())return false;
            Changed();return true;
        }

        /// <summary>无效槽位或原槽位不打断换弹；有效切枪保留两把枪的弹量。</summary>
        public bool TrySwitch(int slot)
        {
            if(!CanAct || slot<0 || slot>=Model.Weapons.Length || slot==Model.SelectedSlot)return false;
            Model.Current.CancelReload();Model.SelectedSlot=slot;Changed();return true;
        }

        /// <summary>补给点只为当前枪补充备弹，数量仍受该枪上限约束。</summary>
        public int Supply(int amount)
        {
            if(!CanAct)return 0;
            int accepted=Model.Current.Supply(amount);
            if(accepted>0)Changed();return accepted;
        }

        private bool CanAct => Model.Active && !Model.Paused;
        private void Changed() => this.SendEvent<ChallengeChangedEvent>();
    }
}

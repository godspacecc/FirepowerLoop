using System;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>容量按基础弹夹的百分比线性累加，最后向下取整并保护整数上限。</summary>
        private int CurrentRifleCapacity()=>(int)Math.Min(int.MaxValue,Math.Floor(config.RifleMagazineCapacity*(1+Bonus("Magazine"))+0.000001));
        /// <summary>换弹强化缩短秒数，始终遵守配置下限。</summary>
        private float CurrentRifleReloadSeconds()=>(float)Math.Max(config.RifleMinimumReloadSeconds,config.RifleReloadSeconds-Bonus("Reload"));
        /// <summary>新局首次初始化满弹，绝不因无目标或升级重新补弹。</summary>
        private void EnsureRifleMagazine()
        {
            if(Model.RifleCapacity>0)return;
            Model.RifleCapacity=CurrentRifleCapacity();Model.RifleRounds=Model.RifleCapacity;
            Model.RifleReloadRemaining=Model.RifleReloadDuration=0;
        }
        /// <summary>容量升级只更新容量，当前弹量和正在进行的换弹进度保持。</summary>
        private void UpdateRifleCapacity(){EnsureRifleMagazine();Model.RifleCapacity=CurrentRifleCapacity();}
        /// <summary>只在已有可射击目标且弹丸预算允许时扣一发，最后一发启动自动换弹。</summary>
        private bool TryConsumeRifleRound()
        {
            EnsureRifleMagazine();var m=Model;
            if(m.RifleReloadRemaining>0||m.RifleRounds<=0)return false;
            m.RifleRounds--;
            if(m.RifleRounds==0)m.RifleReloadRemaining=m.RifleReloadDuration=CurrentRifleReloadSeconds();
            return true;
        }
        /// <summary>只由有效战斗帧调用；暂停冻结，完成后按当前新容量装满无限备弹。</summary>
        private void AdvanceRifleReload(float seconds)
        {
            EnsureRifleMagazine();var m=Model;
            if(seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds)||m.RifleReloadRemaining<=0)return;
            m.RifleReloadRemaining=Math.Max(0,m.RifleReloadRemaining-seconds);
            if(m.RifleReloadRemaining<=0.000001f){m.RifleReloadRemaining=0;UpdateRifleCapacity();m.RifleRounds=m.RifleCapacity;}
        }
        /// <summary>强化卡读实际弹夹容量或换弹秒数，避免只显示没有单位的百分比。</summary>
        public bool TryGetRifleUpgradeDetails(int id,out bool magazine,out double current,out double next)
        {
            magazine=false;current=next=0;
            if(!TryGetUpgradeDetails(id,out _,out var before,out var after,out _))return false;
            var kind=config.Upgrades[id-1].Kind;
            if(kind=="Magazine"){magazine=true;current=CurrentRifleCapacity();next=Math.Min(int.MaxValue,Math.Floor(config.RifleMagazineCapacity*(1+Bonus("Magazine")+after-before)+0.000001));return true;}
            if(kind=="Reload"){current=CurrentRifleReloadSeconds();next=Math.Max(config.RifleMinimumReloadSeconds,current-(after-before));return true;}
            return false;
        }
    }
}

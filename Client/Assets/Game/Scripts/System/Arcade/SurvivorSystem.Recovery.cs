using System;
using QFramework;
using FirepowerLoop.Arcade;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>恢复路线仅保存本局连续条件时间与不足一滴的累计值。</summary>
        private double recoveryConditionSeconds,recoveryFraction;
        private bool recoveryDamagedThisFrame;
        /// <summary>候选与提交共用互斥校验，旧卡片请求同样不能绕过已选路线。</summary>
        private bool CanChooseRoute(SurvivorUpgradeDefinition definition)
        {
            if(string.IsNullOrEmpty(definition.ExclusiveGroup))return true;
            for(int i=0;i<config.Upgrades.Count;i++)
                if(config.Upgrades[i].Kind!=definition.Kind&&config.Upgrades[i].ExclusiveGroup==definition.ExclusiveGroup&&Model.State.UpgradeLevel(i+1)>0)return false;
            return true;
        }
        /// <summary>所有实际生命损失统一重置无伤计时；零伤害和无敌未命中不打断。</summary>
        private void DamageSurvivor(int damage)=>DamageSurvivorFrom(damage,null);
        /// <summary>实际扣血后通知本次攻击来源；持续伤害缺少来源时只显示损伤提示。</summary>
        private void DamageSurvivorFrom(int damage,UnityEngine.Vector2? source)
        {
            if(damage<=0||Model.Health<=0)return;
            Model.Health=Math.Max(0,Model.Health-damage);recoveryDamagedThisFrame=true;
            if(source.HasValue)view.PlaySurvivorPlayerHit(player-source.Value);
            this.SendEvent(new SurvivorPlayerHurtEvent());
            for(int i=0;i<config.Upgrades.Count;i++)
                if(config.Upgrades[i].Kind=="RecoverySafe"&&Model.State.UpgradeLevel(i+1)>0){recoveryConditionSeconds=recoveryFraction=0;break;}
        }
        /// <summary>停止或首次取得恢复路线时清除条件进度，不能从旧局或未取得期间积攒恢复。</summary>
        private void ResetRecovery(){recoveryConditionSeconds=recoveryFraction=0;recoveryDamagedThisFrame=false;}
        /// <summary>仅由有效战斗帧推进；按条件满足后的实际时段回血，满血丢弃余量且不能复活。</summary>
        private void AdvanceRecovery(float dt,bool moving)
        {
            if(dt<=0||Model.Health<=0)return;
            for(int i=0;i<config.Upgrades.Count;i++)
            {
                var definition=config.Upgrades[i];int level=Model.State.UpgradeLevel(i+1);
                if(!definition.IsRecovery||level<=0)continue;
                if((definition.Kind=="RecoveryStill"&&moving)||(definition.Kind=="RecoverySafe"&&recoveryDamagedThisFrame))
                {recoveryConditionSeconds=recoveryFraction=0;return;}
                double before=recoveryConditionSeconds;recoveryConditionSeconds+=dt;
                double eligible=Math.Max(0,recoveryConditionSeconds-definition.ActivationDelaySeconds)-Math.Max(0,before-definition.ActivationDelaySeconds);
                if(Model.Health>=Model.MaximumHealth){recoveryFraction=0;return;}
                recoveryFraction+=eligible*Model.MaximumHealth*definition.BonusAtLevel(level);
                int restored=(int)Math.Min(Model.MaximumHealth-Model.Health,Math.Floor(recoveryFraction+0.000001));
                Model.Health+=restored;recoveryFraction=Math.Max(0,recoveryFraction-restored);
                if(Model.Health==Model.MaximumHealth)recoveryFraction=0;
                return;
            }
        }
        /// <summary>卡片只读取恢复条件与当前／下级每秒最大生命比例。</summary>
        public bool TryGetRecoveryUpgradeDetails(int id,out string kind,out float delay,out double current,out double next)
        {
            kind=null;delay=0;current=next=0;
            if(config==null||Model.State==null||id<1||id>config.Upgrades.Count)return false;
            var definition=config.Upgrades[id-1];if(!definition.IsRecovery)return false;
            int level=Model.State.UpgradeLevel(id);kind=definition.Kind;delay=definition.ActivationDelaySeconds;
            current=definition.BonusAtLevel(level)*100;next=definition.BonusAtLevel(level+1)*100;return true;
        }
    }
}

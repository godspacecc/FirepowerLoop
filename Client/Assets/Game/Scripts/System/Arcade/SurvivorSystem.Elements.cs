using System;
using System.Collections.Generic;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>按敌人身份保存本局状态；不改基础移速，不参与局外持久化。</summary>
        private sealed class ElementStatus
        {
            /// <summary>燃烧剩余秒数、每秒伤害及尚未满一点的小数伤害。</summary>
            public double BurnRemaining,BurnDamagePerSecond,BurnFraction;
            /// <summary>冰霜剩余秒数与减速比例，不覆盖敌人基础速度。</summary>
            public float FrostRemaining,SlowFraction;
        }
        /// <summary>仅存活敌人持有元素状态，复用身份列表避免结算死亡时修改遍历集合。</summary>
        private readonly Dictionary<int,ElementStatus> elementStatuses=new();
        private readonly List<int> elementStatusIds=new();
        /// <summary>仅步枪直接命中附加快照；重复命中刷新时间，旧弱弹不覆盖新强度。</summary>
        private void ApplyElementHit(ArcadeEnemyRuntime enemy,ModifierShot shot)
        {
            if(!enemies.ContainsKey(enemy.Id)||shot.Child||(shot.FireSeconds<=0&&shot.FrostSeconds<=0))return;
            if(!elementStatuses.TryGetValue(enemy.Id,out var state))elementStatuses.Add(enemy.Id,state=new ElementStatus());
            if(shot.FireSeconds>0)
            {
                if(state.BurnRemaining<=0){state.BurnDamagePerSecond=0;state.BurnFraction=0;}
                state.BurnRemaining=shot.FireSeconds;state.BurnDamagePerSecond=Math.Max(state.BurnDamagePerSecond,shot.FireDamagePerSecond);
            }
            if(shot.FrostSeconds>0)
            {
                if(state.FrostRemaining<=0)state.SlowFraction=0;
                state.FrostRemaining=shot.FrostSeconds;state.SlowFraction=Math.Max(state.SlowFraction,shot.FrostSlowFraction);
            }
        }
        /// <summary>有效战斗帧推进燃烧，保留不足一点伤害；死亡复用唯一掉落事务。</summary>
        private void AdvanceElementDamage(float dt)
        {
            if(dt<=0)return;
            elementStatusIds.Clear();elementStatusIds.AddRange(elementStatuses.Keys);
            foreach(int id in elementStatusIds)
            {
                if(!elementStatuses.TryGetValue(id,out var state))continue;
                if(!enemies.TryGetValue(id,out var enemy)){elementStatuses.Remove(id);continue;}
                if(state.BurnRemaining>0)
                {
                    double elapsed=Math.Min(dt,state.BurnRemaining);state.BurnRemaining=Math.Max(0,state.BurnRemaining-elapsed);
                    state.BurnFraction+=elapsed*state.BurnDamagePerSecond;
                    int damage=(int)Math.Min(int.MaxValue,Math.Floor(state.BurnFraction+.000001));state.BurnFraction=Math.Max(0,state.BurnFraction-damage);
                    ApplyModifierDamage(enemy,damage);
                }
                if(state.BurnRemaining<=0&&state.FrostRemaining<=0)elementStatuses.Remove(id);
            }
        }
        /// <summary>只缩短本帧行走距离，跨越到期点时精确分段，基础速度和攻击计时不变。</summary>
        private float ElementMovementSeconds(ArcadeEnemyRuntime enemy,float dt)
        {
            if(!elementStatuses.TryGetValue(enemy.Id,out var state)||state.FrostRemaining<=0)return dt;
            float slowed=Math.Min(dt,state.FrostRemaining);state.FrostRemaining=Math.Max(0,state.FrostRemaining-dt);
            return dt-slowed*state.SlowFraction;
        }
        /// <summary>退出或重开清除状态与复用候选，不能残留到下一局相同编号的敌人。</summary>
        private void ClearElements(){elementStatuses.Clear();elementStatusIds.Clear();}
    }
}

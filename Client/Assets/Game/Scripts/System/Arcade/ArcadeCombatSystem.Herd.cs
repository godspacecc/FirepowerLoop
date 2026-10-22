using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>同一布局再次进场也使用新代次，旧扫掠不能命中新现场。</summary>
        private long herdGeneration;
        private void InitializeHerd(ArcadeCombatRuntime r)
        {
            ClearHerd(r);r.HerdMapId=checked(++herdGeneration);r.HerdBatch=0;r.HerdUntilNext=EarlyStage?.HerdFirstDelay??0;
        }
        private static void ClearHerd(ArcadeCombatRuntime r)
        {foreach(var cow in r.HerdCows)ArcadeHerdRules.Cancel(cow);r.HerdCows.Clear();r.HerdMapId=0;}
        /// <summary>活动循环生成固定通路的一排牛；不追踪玩家、不显示方向提示。</summary>
        private void TickHerd(ArcadeCombatRuntime r,float dt)
        {
            var stage=EarlyStage;if(stage==null||stage.HerdCount<=0)return;
            r.HerdUntilNext-=dt;
            if(r.HerdUntilNext<=0)
            {
                r.HerdUntilNext=stage.HerdPeriod;
                ArcadeHerdRules.StartBatch(r.HerdCows,stage,r.HerdMapId,++r.HerdBatch);
                return;
            }
            // 先推进整排，死亡或结算导致的中途冻结不能让后半排漏走本帧。
            var steps=r.HerdCows.Select(c=>(Cow:c,Sweep:ArcadeHerdRules.Step(c,stage.Herd,dt,true))).ToArray();
            foreach(var step in steps)
            {
                var cow=step.Cow;var sweep=step.Sweep;
                if(ArcadeHerdRules.TryContact(cow,sweep,r.HerdMapId,-1,r.PlayerPosition.x,r.PlayerPosition.y,catalog.Rule.PlayerRadius,r.PlayerHealth>0))
                    DamageContentPlayer(r,ArcadeRoundContentRules.Damage(r.PlayerMaxHealth,stage.HerdPlayerDamage));
                if(this.GetModel<ArcadeCombatModel>().Runtime!=r||catalog==null||!RoundContentActive(r))return;
                foreach(var enemy in r.Enemies.Values.ToArray())
                {
                    if(!r.Enemies.ContainsKey(enemy.Id)||!ArcadeHerdRules.TryContact(cow,sweep,r.HerdMapId,enemy.Id,enemy.Position.x,enemy.Position.y,catalog.Rule.EnemyContactRadius,enemy.Health>0))continue;
                    DamageEnemy(enemy,ArcadeRoundContentRules.Damage(enemy.MaxHealth,stage.HerdEnemyDamage));
                    if(enemy.Health<=0)KillEnemy(r,enemy,true);
                    if(this.GetModel<ArcadeCombatModel>().Runtime!=r||catalog==null||!RoundContentActive(r))return;
                }
                if(cow.Phase==ArcadeHerdPhase.Expired){ArcadeHerdRules.Cancel(cow);r.HerdCows.Remove(cow);}
            }
        }
    }
}

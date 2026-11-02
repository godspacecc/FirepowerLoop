using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>本局特殊敌人状态，死亡或退出即释放；朝向是规则事实而非动画结果。</summary>
        private sealed class SpecialEnemyState
        {
            public Vector2 Facing=Vector2.up,Target;
            public float Remaining,Cooldown;
            public bool Warning;
            /// <summary>本局剩余盾耐久，完整投影不得重置；零表示已破盾。</summary>
            public int ShieldRemaining;
        }
        private readonly Dictionary<int,SpecialEnemyState> specialEnemies=new();
        private readonly Dictionary<int,ArcadeProjectileRuntime> hostileProjectiles=new();
        /// <summary>自爆表现独立容量，不能挤占玩家强化爆炸的显示预算。</summary>
        private readonly List<ModifierBlast> specialBlasts=new();
        private readonly List<int> specialDeaths=new(),expiredHostileProjectiles=new();
        /// <summary>仅实体首次需要特殊规则时建状态，避免普通尸潮增加逐敌字典开销。</summary>
        private SpecialEnemyState SpecialState(ArcadeEnemyRuntime enemy)
        {
            if(!specialEnemies.TryGetValue(enemy.Id,out var state))specialEnemies.Add(enemy.Id,state=new SpecialEnemyState{ShieldRemaining=Shape(enemy.ConfigId)?.ShieldDurability??0});
            return state;
        }
        /// <summary>盾牌朝向限速旋转供近战动画消费，不再作为伤害过滤条件。</summary>
        private Vector2 SpecialFacing(ArcadeEnemyRuntime enemy)
            =>Shape(enemy.ConfigId)?.SpecialKind=="Shield"?SpecialState(enemy).Facing:(player-enemy.Position).normalized;
        /// <summary>返回true代表特殊攻击接管本帧移动与近战；爬出阶段由调用者阻断。</summary>
        private bool TickSpecialEnemy(ArcadeEnemyRuntime enemy,float dt)
        {
            var shape=Shape(enemy.ConfigId);if(shape==null||string.IsNullOrEmpty(shape.SpecialKind))return false;
            if(enemy.Health<=0||!enemies.ContainsKey(enemy.Id))return true;
            if(IsEnchantKind(shape.SpecialKind))return false;
            var state=SpecialState(enemy);var delta=player-enemy.Position;
            if(shape.SpecialKind=="Shield")
            {
                if(delta.sqrMagnitude>0.000001f)
                {
                    float angle=Vector2.SignedAngle(state.Facing,delta.normalized);
                    float turn=Mathf.Clamp(angle,-shape.TurnDegreesPerSecond*dt,shape.TurnDegreesPerSecond*dt)*Mathf.Deg2Rad;
                    state.Facing=new Vector2(state.Facing.x*Mathf.Cos(turn)-state.Facing.y*Mathf.Sin(turn),state.Facing.x*Mathf.Sin(turn)+state.Facing.y*Mathf.Cos(turn));
                }
                return false;
            }
            state.Cooldown=Mathf.Max(0,state.Cooldown-dt);
            // 施法起手与释放共用火球的精确挡墙规则；目标躲到墙后或旧落点被挡时重新追击。
            if(shape.SpecialKind=="Caster"&&(CasterShotBlocked(enemy,player)||state.Warning&&CasterShotBlocked(enemy,state.Target)))
            {state.Warning=false;state.Remaining=0;return true;}
            if(state.Warning)
            {
                state.Remaining=Mathf.Max(0,state.Remaining-dt);
                if(state.Remaining>0)return true;
                state.Warning=false;enemy.AttackSequence++;
                if(shape.SpecialKind=="Exploder")
                {
                    if(delta.sqrMagnitude<=shape.BlastRadius*shape.BlastRadius&&hurtClock<=0)
                    {DamageSurvivorFrom(shape.SpecialDamage,enemy.Position);hurtClock=catalog.Rule.ContactInvulnerabilitySeconds;}
                    if(specialBlasts.Count<config.MaxBlastEffects)
                    {var blast=new ModifierBlast{Id=++identity,Position=enemy.Position,Radius=shape.BlastRadius};specialBlasts.Add(blast);view.SetSurvivorEnemyBlast(blast.Id,blast.Position,blast.Radius,0,config.MaxBlastEffects);}
                    enemy.Health=0;specialDeaths.Add(enemy.Id);return true;
                }
                var direction=state.Target-enemy.Position;if(direction.sqrMagnitude<=.000001f)direction=state.Facing;
                if(hostileProjectiles.Count<config.MaxProjectiles)
                {var b=new ArcadeProjectileRuntime{Id=++identity,Position=enemy.Position,Velocity=direction.normalized*shape.ProjectileSpeed,Damage=shape.SpecialDamage,RemainingSeconds=shape.ProjectileLifetime};hostileProjectiles.Add(b.Id,b);}
                state.Cooldown=shape.AttackCooldownSeconds;return true;
            }
            if(delta.sqrMagnitude>shape.TriggerRange*shape.TriggerRange)
            {return true;}
            if(state.Cooldown<=0)
            {
                // 预警起手保存玩家当时位置；移动躲开不会被后续追踪修正。
                state.Warning=true;state.Remaining=shape.WindupSeconds;state.Target=player;
                if(delta.sqrMagnitude>0.000001f)state.Facing=delta.normalized;
            }
            return true;
        }
        /// <summary>与火球碰撞使用同一条连续射线，薄墙和端点遮挡也不能发起远程攻击。</summary>
        private bool CasterShotBlocked(ArcadeEnemyRuntime enemy,Vector2 target)
            =>HostileWallSweep(enemy.Position,target-enemy.Position)<=1;
        /// <summary>自毁不经过击杀、经验和金币事务，遍历结束再移除，保证一次性。</summary>
        private void FlushSpecialDeaths()
        {
            foreach(int id in specialDeaths)
            {if(enemies.Remove(id)){presentedEnemies.Remove(id);elementStatuses.Remove(id);specialEnemies.Remove(id);enchantments.Remove(id);view.RemoveEnemy(id,false);}}
            specialDeaths.Clear();
        }
        /// <summary>所有来源先消耗盾耐久；击碎当次完整吸收，不向生命溢出。</summary>
        private int ShieldDamage(ArcadeEnemyRuntime enemy,int damage,Vector2? source)
        {
            if(AbsorbEnchantShield(enemy,damage))return 0;
            if(Shape(enemy.ConfigId)?.SpecialKind!="Shield")return damage;
            var state=SpecialState(enemy);if(state.ShieldRemaining<=0)return damage;
            state.ShieldRemaining=Math.Max(0,state.ShieldRemaining-damage);
            // 立即更新近场装备；远场恢复时仍由完整权威投影覆盖。
            view.SetSurvivorEnemyShield(enemy.Id,state.ShieldRemaining>0);
            return 0;
        }
        /// <summary>线段圆首次接触，快弹不能从玩家身上跨过。</summary>
        private static float PlayerSweep(Vector2 from,Vector2 segment,Vector2 center,float radius)
        {
            var offset=from-center;float c=offset.sqrMagnitude-radius*radius;if(c<=0)return 0;
            float a=segment.sqrMagnitude;if(a<=.0000001f)return float.PositiveInfinity;
            float b=Vector2.Dot(offset,segment),disc=b*b-a*c;if(disc<0)return float.PositiveInfinity;
            float t=(-b-Mathf.Sqrt(disc))/a;return t>=0&&t<=1?t:float.PositiveInfinity;
        }
        /// <summary>精确线段与矩形扫掠，不依赖离散采样，薄墙同样会挡住火球。</summary>
        private float HostileWallSweep(Vector2 from,Vector2 segment)
        {
            float first=float.PositiveInfinity;
            foreach(var obstacle in obstacles)
            {
                if(!obstacle.BlocksProjectiles)continue;float enter=0,exit=1;bool hit=true;
                for(int axis=0;axis<2;axis++)
                {
                    float origin=from[axis],speed=segment[axis],min=obstacle.Center[axis]-obstacle.HalfExtents[axis],max=obstacle.Center[axis]+obstacle.HalfExtents[axis];
                    if(Mathf.Abs(speed)<.0000001f){if(origin<min||origin>max){hit=false;break;}continue;}
                    float a=(min-origin)/speed,b=(max-origin)/speed;if(a>b){float swap=a;a=b;b=swap;}
                    enter=Mathf.Max(enter,a);exit=Mathf.Min(exit,b);if(enter>exit){hit=false;break;}
                }
                if(hit&&enter>=0&&enter<=1)first=Mathf.Min(first,enter);
            }
            return first;
        }
        /// <summary>先比较墙与玩家的首次接触；共用玩家受击保护，命中或到期即清理。</summary>
        private void AdvanceHostileProjectiles(float dt)
        {
            for(int i=specialBlasts.Count-1;i>=0;i--)
            {
                var blast=specialBlasts[i];blast.Age+=dt;
                if(blast.Age>=ModifierBlastSeconds){view.RemoveSurvivorEnemyBlast(blast.Id);specialBlasts.RemoveAt(i);}
                else view.SetSurvivorEnemyBlast(blast.Id,blast.Position,blast.Radius,blast.Age/ModifierBlastSeconds,config.MaxBlastEffects);
            }
            expiredHostileProjectiles.Clear();
            foreach(var b in hostileProjectiles.Values)
            {
                var segment=b.Velocity*Mathf.Min(dt,Mathf.Max(0,b.RemainingSeconds));
                float wall=HostileWallSweep(b.Position,segment),hit=PlayerSweep(b.Position,segment,player,catalog.Rule.PlayerRadius);
                if(hit<wall&&hit<=1)
                {if(hurtClock<=0){DamageSurvivorFrom(b.Damage,b.Position-b.Velocity);hurtClock=catalog.Rule.ContactInvulnerabilitySeconds;}expiredHostileProjectiles.Add(b.Id);continue;}
                b.Position+=segment;b.RemainingSeconds-=dt;
                if(wall<=1||b.RemainingSeconds<=0){expiredHostileProjectiles.Add(b.Id);continue;}
                view.SetSurvivorHostileProjectile(b.Id,b.Position,b.Velocity.normalized);
            }
            foreach(int id in expiredHostileProjectiles){hostileProjectiles.Remove(id);view.RemoveSurvivorHostileProjectile(id);}
        }
        /// <summary>恢复近场显示时重新投影完整状态，暂停期间由主循环冻结。</summary>
        private void ProjectSpecialEnemy(ArcadeEnemyRuntime enemy)
        {
            ProjectEnchantment(enemy);
            var shape=Shape(enemy.ConfigId);if(shape==null||string.IsNullOrEmpty(shape.SpecialKind)||IsEnchantKind(shape.SpecialKind))return;
            var state=SpecialState(enemy);
            // 盾牌及前摇保留权威锁定方向；追击和施法冷却则朝当前目标，避免旧方向造成横向滑行。
            var direction=state.Facing;
            if(shape.SpecialKind!="Shield"&&!state.Warning&&(player-enemy.Position).sqrMagnitude>.000001f)
                direction=(player-enemy.Position).normalized;
            if(shape.SpecialKind=="Shield")view.SetSurvivorEnemyShield(enemy.Id,state.ShieldRemaining>0);
            view.SetSurvivorEnemySpecial(enemy.Id,shape.SpecialKind,direction,state.Warning,state.Warning?Mathf.Clamp01(1-state.Remaining/shape.WindupSeconds):0,state.Target,shape.BlastRadius);
        }
        /// <summary>重开与退出清空敌方弹体、预警、方向及延迟自毁，不能污染复用身份。</summary>
        private void ClearSpecialEnemies()
        {
            foreach(int id in hostileProjectiles.Keys)view?.RemoveSurvivorHostileProjectile(id);
            foreach(var blast in specialBlasts)view?.RemoveSurvivorEnemyBlast(blast.Id);specialBlasts.Clear();
            hostileProjectiles.Clear();specialEnemies.Clear();enchantments.Clear();specialDeaths.Clear();expiredHostileProjectiles.Clear();
        }
    }
}

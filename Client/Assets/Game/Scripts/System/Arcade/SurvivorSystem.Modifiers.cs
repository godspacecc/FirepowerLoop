using System;
using System.Collections.Generic;
using FirepowerLoop.Arcade;
using UnityEngine;
namespace Game
{
    public sealed partial class SurvivorSystem
    {
        /// <summary>每颗弹在发射时固定的强化快照；子弹不读后来选择的等级。</summary>
        private sealed class ModifierShot
        {
            public bool Child,Triggered;
            public int SplitCount,SplitDamage,BlastDamage;
            public float BlastRadius;
        }
        /// <summary>权威爆炸仅记录视觉寿命；伤害在触发帧已完成。</summary>
        private sealed class ModifierBlast
        {
            public int Id;
            public Vector2 Position;
            public float Radius,Age;
        }
        // 发射快照与复用查询缓存，不把临时命中候选交给表现层持有。
        private readonly Dictionary<int,ModifierShot> modifierShots=new();
        private readonly List<ArcadeProjectileRuntime> pendingSplitShots=new();
        private readonly List<(int Id,float Fraction)> modifierHits=new();
        private readonly List<int> blastCandidates=new();
        private readonly List<ModifierBlast> modifierBlasts=new();
        private int splitShotCount;
        /// <summary>表现用短冲击时长，沿用已确认的0.4秒预览。</summary>
        private const float ModifierBlastSeconds=.4f;

        /// <summary>齐射按实际弹丸逐颗扣弹；余弹和预算共同限制条数，打空后自动换弹。</summary>
        private void ShootRifleVolley(SurvivorWeaponLevelDefinition level)
        {
            int count=1;float spacing=0;
            for(int i=0;i<config.Upgrades.Count;i++){
                var upgrade=config.Upgrades[i];if(upgrade.Kind!="RifleMultishot")continue;
                count+=upgrade.EffectCount(Model.State.UpgradeLevel(i+1));spacing=upgrade.EffectAngleDegrees;break;
            }
            count=Math.Min(count,Math.Min(Model.RifleRounds,config.MaxProjectiles-bullets.Count));
            for(int i=0;i<count;i++){
                if(!TryConsumeRifleRound())break;
                float angle=(i-(count-1)*.5f)*spacing*Mathf.Deg2Rad;
                var direction=new Vector2(facing.x*Mathf.Cos(angle)-facing.y*Mathf.Sin(angle),facing.x*Mathf.Sin(angle)+facing.y*Mathf.Cos(angle));
                Shoot(player,direction,level,"Rifle");
            }
        }

        /// <summary>只允许已拥有且该效果未满级的武器进入候选或接受选择。</summary>
        private bool CanChooseModifier(SurvivorUpgradeDefinition definition,int id)
        {
            if(!definition.IsModifier)return true;
            if(definition.TargetWeapon!="Rifle"||definition.Weight<=0||Model.Loadout==null||Model.Loadout.LevelOf("Rifle")==0||Model.State.UpgradeLevel(id)>=definition.MaxLevel)return false;
            // 弹体分支只在首次选择时确定；穿透、多重不参与互斥。
            string rival=definition.ModifierKind=="Explosion"?"Split":definition.ModifierKind=="Split"?"Explosion":null;
            if(rival!=null)for(int i=0;i<config.Upgrades.Count;i++)
                if(config.Upgrades[i].TargetWeapon=="Rifle"&&config.Upgrades[i].ModifierKind==rival&&Model.State.UpgradeLevel(i+1)>0)return false;
            return true;
        }

        /// <summary>卡片只读查询所属武器与独立效果等级，不修改候选或战斗。</summary>
        public bool TryGetModifierUpgrade(int id,out SurvivorUpgradeDefinition definition,out int level)
        {
            definition=null;level=0;
            if(config==null||Model.State==null||id<1||id>config.Upgrades.Count)return false;
            var value=config.Upgrades[id-1];if(!value.IsModifier)return false;
            definition=value;level=Model.State.UpgradeLevel(id);return CanChooseModifier(value,id);
        }
        /// <summary>将所属武器的等级转换为发射时快照，之后的强化不影响在途弹。</summary>
        private void SnapshotModifiers(ArcadeProjectileRuntime bullet,string owner)
        {
            var shot=new ModifierShot();bullet.RemainingHits=1;
            for(int i=0;i<config.Upgrades.Count;i++)
            {
                var u=config.Upgrades[i];if(owner!="Rifle"||!u.IsModifier||u.Weight<=0||u.TargetWeapon!=owner)continue;
                int level=Model.State.UpgradeLevel(i+1);if(level<=0)continue;
                switch(u.ModifierKind)
                {
                    case "Pierce":bullet.RemainingHits+=u.EffectCount(level);break;
                    case "Explosion":shot.BlastRadius=u.EffectRadius(level);shot.BlastDamage=ScaledModifierDamage(bullet.Damage,u.EffectDamage(level));break;
                    case "Split":shot.SplitCount=u.EffectCount(level);shot.SplitDamage=ScaledModifierDamage(bullet.Damage,u.EffectDamage(level));break;
                }
            }
            modifierShots[bullet.Id]=shot;
        }
        /// <summary>比例伤害向下取整，并消除浮点尾差导致的整点少算。</summary>
        private static int ScaledModifierDamage(int damage,float ratio)=>(int)Math.Min(int.MaxValue,Math.Floor((double)damage*ratio+0.000001));
        /// <summary>表现查询子弹身份，避免分裂弹从玩家枪口重新出现。</summary>
        private bool IsSplitProjectile(int id)=>modifierShots.TryGetValue(id,out var shot)&&shot.Child;

        /// <summary>达到总预算时优先收回最早的分裂弹，保证主武器仍可开火。</summary>
        private bool CanFirePrimary()
        {
            if(bullets.Count<config.MaxProjectiles)return true;
            int oldest=int.MaxValue;
            foreach(var pair in modifierShots)if(pair.Value.Child&&bullets.ContainsKey(pair.Key)&&pair.Key<oldest)oldest=pair.Key;
            if(oldest==int.MaxValue)return false;
            RemoveModifierProjectile(oldest);return true;
        }
        /// <summary>删除权威弹丸时同时释放快照、分裂计数和显示实例。</summary>
        private void RemoveModifierProjectile(int id)
        {
            if(modifierShots.TryGetValue(id,out var shot)&&shot.Child)splitShotCount--;
            modifierShots.Remove(id);bullets.Remove(id);view?.RemoveProjectile(id);
        }

        /// <summary>按首次身体接触排序结算；空间索引仅给候选，穿透仍尊重实体障碍。</summary>
        private void AdvanceModifierBullets(float dt)
        {
            EnsureHordeSpatial();expiredBullets.Clear();pendingSplitShots.Clear();
            foreach(var bullet in bullets.Values)
            {
                var old=bullet.Position;
                float step=Mathf.Min(dt,Mathf.Max(0,bullet.RemainingSeconds));
                var next=old+bullet.Velocity*step;bullet.RemainingSeconds-=dt;
                modifierHits.Clear();
                enemySpatial.QuerySegment(old.x,old.y,next.x,next.y,maximumEnemyRadius,enemyCandidates);
                foreach(int id in groundEntryCandidates)if(!enemyCandidates.Contains(id))enemyCandidates.Add(id);
                var segment=next-old;float length=segment.magnitude;
                foreach(int id in enemyCandidates)
                {
                    if(bullet.HitEnemyIds.Contains(id)||!enemies.TryGetValue(id,out var enemy))continue;
                    float fraction;
                    if(enemy.GroundEntry.Active)
                    {
                        float height=bullet.SourceWeaponId=="survivor.drone"?droneVisual.VisualOffset.y:.75f;
                        if(!TryGroundBodySweep(enemy,old,next,height,out fraction))continue;
                    }
                    else
                    {
                        float projection=length<=.000001f?0:Vector2.Dot(enemy.Position-old,segment/length);
                        var closest=old+(length<=.000001f?Vector2.zero:segment/length)*Mathf.Clamp(projection,0,length);
                        float radius=EnemyRadius(enemy);
                        if((enemy.Position-closest).sqrMagnitude>radius*radius)continue;
                        float perpendicular=Mathf.Max(0,(enemy.Position-old).sqrMagnitude-projection*projection);
                        fraction=length<=.000001f?0:Mathf.Clamp01((projection-Mathf.Sqrt(Mathf.Max(0,radius*radius-perpendicular)))/length);
                    }
                    modifierHits.Add((id,fraction));
                }
                modifierHits.Sort((a,b)=>{int order=a.Fraction.CompareTo(b.Fraction);return order!=0?order:a.Id.CompareTo(b.Id);});
                bool remove=false;
                foreach(var hit in modifierHits)
                {
                    if(!enemies.TryGetValue(hit.Id,out var enemy))continue;
                    var point=Vector2.Lerp(old,next,hit.Fraction);
                    if(Blocked(old,point)){remove=true;break;}
                    bullet.HitEnemyIds.Add(hit.Id);
                    ApplyModifierDamage(enemy,bullet.Damage);
                    if(modifierShots.TryGetValue(bullet.Id,out var shot)&&!shot.Child&&!shot.Triggered)
                    {
                        shot.Triggered=true;
                        TriggerModifierBlast(point,hit.Id,shot);
                        QueueModifierChildren(bullet,point,hit.Id,shot);
                    }
                    bullet.RemainingHits=Math.Max(1,bullet.RemainingHits)-1;
                    if(bullet.RemainingHits==0){remove=true;break;}
                }
                bullet.Position=next;
                if(remove||bullet.RemainingSeconds<=0||Blocked(old,next))expiredBullets.Add(bullet.Id);
            }
            foreach(int id in expiredBullets)RemoveModifierProjectile(id);
            foreach(var child in pendingSplitShots){bullets.Add(child.Id,child);modifierShots.Add(child.Id,new ModifierShot{Child=true});splitShotCount++;}
            pendingSplitShots.Clear();spatialFrameReady=false;
        }
        /// <summary>所有伤害共用一次死亡提交，连锁命中不能重复掉落。</summary>
        private void ApplyModifierDamage(ArcadeEnemyRuntime enemy,int damage)
        {
            if(damage<=0||!enemies.ContainsKey(enemy.Id))return;
            enemy.Health=Math.Max(0,enemy.Health-damage);if(enemy.Health>0)return;
            enemies.Remove(enemy.Id);presentedEnemies.Remove(enemy.Id);view.RemoveEnemy(enemy.Id,true);Model.Kills++;TryDropGold(enemy);
            if(SurvivorCombatRules.ShouldDropExperience(config.ExperienceDropChance,Math.Min((float)experienceRandom.NextDouble(),.99999994f)))AddExperienceDrop(enemy.Position);
        }
        /// <summary>爆炸查询局部候选，排除直接命中目标；特效预算不足仍结算伤害。</summary>
        private void TriggerModifierBlast(Vector2 point,int directTarget,ModifierShot shot)
        {
            if(shot.BlastRadius<=0)return;
            enemySpatial.QueryCircle(point.x,point.y,shot.BlastRadius+maximumEnemyRadius,blastCandidates);
            foreach(int id in blastCandidates)
            {
                if(id==directTarget||!enemies.TryGetValue(id,out var enemy))continue;
                if(Vector2.Distance(point,enemy.Position)>shot.BlastRadius||Blocked(point,enemy.Position)||!CanAimGroundBody(enemy,point,.75f))continue;
                ApplyModifierDamage(enemy,shot.BlastDamage);
            }
            if(modifierBlasts.Count>=config.MaxBlastEffects)return;
            var blast=new ModifierBlast{Id=++identity,Position=point,Radius=shot.BlastRadius};
            modifierBlasts.Add(blast);view.SetSurvivorBlast(blast.Id,point,blast.Radius,0,config.MaxBlastEffects);
        }
        /// <summary>分裂在遍历后加入；不继承三强化且排除触发目标，不能递归。</summary>
        private void QueueModifierChildren(ArcadeProjectileRuntime parent,Vector2 point,int directTarget,ModifierShot shot)
        {
            if(shot.SplitCount<=0||shot.SplitDamage<=0)return;
            for(int i=0;i<shot.SplitCount;i++)
            {
                if(splitShotCount+pendingSplitShots.Count>=config.MaxSplitProjectiles||bullets.Count+pendingSplitShots.Count>=config.MaxProjectiles)break;
                float degrees=Mathf.Lerp(-30,30,shot.SplitCount==1?.5f:(float)i/(shot.SplitCount-1));
                float angle=degrees*Mathf.Deg2Rad;var d=parent.Velocity.normalized;
                var direction=new Vector2(d.x*Mathf.Cos(angle)-d.y*Mathf.Sin(angle),d.x*Mathf.Sin(angle)+d.y*Mathf.Cos(angle));
                var child=new ArcadeProjectileRuntime{Id=++identity,Position=point,Velocity=direction*parent.Velocity.magnitude,Damage=shot.SplitDamage,
                    RemainingSeconds=Mathf.Max(.01f,parent.RemainingSeconds),SourceWeaponId=parent.SourceWeaponId,RemainingHits=1};
                child.HitEnemyIds.Add(directTarget);pendingSplitShots.Add(child);
            }
        }
        /// <summary>只在有效战斗帧推进冲击表现；暂停及选择强化时保持当前进度。</summary>
        private void AdvanceModifierEffects(float dt)
        {
            for(int i=modifierBlasts.Count-1;i>=0;i--)
            {
                var blast=modifierBlasts[i];blast.Age+=dt;
                if(blast.Age>=ModifierBlastSeconds){view.RemoveSurvivorBlast(blast.Id);modifierBlasts.RemoveAt(i);}
                else view.SetSurvivorBlast(blast.Id,blast.Position,blast.Radius,blast.Age/ModifierBlastSeconds,config.MaxBlastEffects);
            }
        }
        /// <summary>退出及新局清除所有本局弹丸快照和冲击；不触及局外数据。</summary>
        private void ClearModifiers()
        {
            foreach(var blast in modifierBlasts)view?.RemoveSurvivorBlast(blast.Id);
            modifierBlasts.Clear();modifierShots.Clear();pendingSplitShots.Clear();modifierHits.Clear();blastCandidates.Clear();splitShotCount=0;
        }
    }
}

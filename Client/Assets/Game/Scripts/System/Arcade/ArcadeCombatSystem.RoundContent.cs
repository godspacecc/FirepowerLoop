using System;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>所有可调强度来自当前轮布局；不复制角色或关卡默认数值。</summary>
        private ArcadeContentTuning ContentTuning(ArcadeCombatRuntime r)
        {
            var p=RoundLayout(r);
            return p==null ? default : new ArcadeContentTuning(p.SpecialKind,p.EventKind,p.BossKind,p.SpecialRatio,
                p.EnemyHealthScale,p.EnemySpeedScale,p.SkillPeriod,p.SkillWarning,p.SkillMagnitude,p.EventPeriod,
                p.EventWarning,p.EventRadius,p.EventDamage,p.EventMoveSpeed);
        }
        /// <summary>资源等待、清场、宝藏、死亡等待均停止活动；独立Boss只例外推进其自身技能。</summary>
        private bool RoundContentActive(ArcadeCombatRuntime r) => r!=null && catalog!=null && RoundLayout(r)!=null &&
            !r.IsChallenge && !r.InTreasureRoom && !r.AwaitingExit && !r.IslandRoundAwaitingExit &&
            !r.IslandTransitionPending && !r.IslandRoomTransitionPending && !r.CompletionAwaitingRetry && !r.CompletionSubmitted &&
            r.RespawnRemaining<=0 && (!r.WaveClearAwarded || r.ThemeBossStarted && !r.ThemeBossDefeated || r.IsFinalBoss && !r.FinalBossDefeated);

        /// <summary>每张地图创建独占时钟、接触冷却、领域与冰面速度，普通切轮不继承旧危险。</summary>
        private void InitializeRoundContent(ArcadeCombatRuntime r)
        {
            var layout=RoundLayout(r);double previousElapsed=r.ContentClock.Elapsed;
            bool sameMap=layout!=null&&r.GroundBarrelMapId==layout.Id&&r.GroundBarrelLayoutVersion==layout.Version;
            if(!sameMap)r.PurpleRayArcs.Clear();
            if(!sameMap||r.HerdMapId<=0)InitializeHerd(r);
            // 同地图切入 Boss 只停止危险；已结算爆炸保留剩余寿命，并换算到重置后的活动时钟。
            r.ContentAreas.RemoveAll(a=>!sameMap||a.Kind!=ArcadeRoundContentRules.BarrelExplosionKind||a.ExpiresAt<=previousElapsed);
            foreach(var area in r.ContentAreas){area.WarningUntil=0;area.ExpiresAt-=previousElapsed;}
            r.ContentClock=new ArcadeContentClock();r.ContentAreaSnapshots.Clear();
            InitializeGroundBarrels(r);
            r.ContentVelocity=Vector2.zero;r.ContentSlowRemaining=r.ContentBumperCooldown=0;
            // 同地图进入主题 Boss 时表现仍保留去重记录，事件身份不能重用。
            r.ContentSpawnSequence=0;r.ContentNextAreaId=Math.Max(1,r.ContentNextAreaId);r.ContentEventCycle=r.ContentEventTargetCycle=-1;
            r.ContentEventAnchor=ContentPosition(r,Vector2.zero);r.ContentEventTarget=r.ContentEventAnchor;
        }

        /// <summary>稳定出生序只替换原有预算，Boss重复绑定不重复缩放或计入特殊比例。</summary>
        private void AssignRoundEnemyContent(ArcadeCombatRuntime r,ArcadeEnemyRuntime e)
        {
            var layout=RoundLayout(r);if(layout==null || r.IsChallenge)return;
            if(r.ThemeBossStarted && !r.ThemeBossDefeated && e.Id==r.ContentBossEnemyId || r.IsFinalBoss && e.Id==r.FinalBossEnemyId)
            {
                if(e.ContentBossKind==0){e.ContentBornAt=r.ContentClock.Elapsed;e.ContentSkillCycle=e.ContentTelegraphCycle=-1;}
                e.ContentBossKind=layout.BossKind;e.ContentSpecialKind=0;e.IsBossPresentation=true;return;
            }
            if(UsesContentRounds(r) && !r.ThemeBossStarted && !r.IsFinalBoss)
            { e.BossId=null;e.BossDefinition=default;e.IsBossPresentation=false;e.BossPhase=ArcadeBossPhase.None; }
            if(e.ContentAssigned)return;e.ContentAssigned=true;e.ContentBornAt=r.ContentClock.Elapsed;
            int sequence=r.ContentSpawnSequence++;
            if(layout.SpecialKind==0 || Math.Floor((sequence+1)*layout.SpecialRatio)<=Math.Floor(sequence*layout.SpecialRatio))return;
            e.ContentSpecialKind=layout.SpecialKind;e.MaxHealth=Mathf.Max(1,Mathf.CeilToInt(e.MaxHealth*layout.EnemyHealthScale));e.Health=e.MaxHealth;
            e.Speed*=layout.EnemySpeedScale;
            if(e.ContentSpecialKind==2 || e.ContentSpecialKind==6)e.Speed*=Mathf.Sqrt(layout.SkillMagnitude);
            if(e.ContentSpecialKind==13)
            { e.Position=ContentPosition(r,r.PlayerPosition+Vector2.up*layout.EventRadius); }
        }

        /// <summary>所有伤害共用潜地免伤、空降预警、护甲与人偶三段受击规则。</summary>
        private int RoundIncomingDamage(ArcadeEnemyRuntime e,int damage)
        {
            if(damage<=0)return 0;var r=this.GetModel<ArcadeCombatModel>().Runtime;
            if(r==null || e.ContentSpecialKind==0 || RoundLayout(r)==null)return damage;
            var p=ContentTuning(r);double age=r.ContentClock.Elapsed-e.ContentBornAt;
            if(e.ContentSpecialKind==3)
            {
                if(e.ContentMannequinHits>0 && r.ContentClock.Elapsed-e.ContentLastHitAt<p.SkillWarning)return 0;
                e.ContentLastHitAt=r.ContentClock.Elapsed;e.ContentMannequinHits++;
                e.Health=e.ContentMannequinHits>=3 ? 0 : Mathf.Max(1,Mathf.CeilToInt(e.MaxHealth*(3-e.ContentMannequinHits)/3f));
                return 0;
            }
            if(e.ContentSpecialKind==13 && age<p.SkillWarning)return 0;
            return Mathf.CeilToInt(damage*ArcadeRoundContentRules.IncomingDamageScale(e.ContentSpecialKind,age,p));
        }

        /// <summary>特殊敌人从出生起使用自己的移动和技能，仍经过统一障碍及岸线限制。</summary>
        private bool TickRoundEnemyContent(ArcadeCombatRuntime r,ArcadeEnemyRuntime e,Vector2 target,bool decoy,float dt)
        {
            if(RoundLayout(r)==null)return false;
            if(e.ContentStunUntil>r.ContentClock.Elapsed){CancelMeleeWindup(e);return true;}
            if(e.ContentBossKind==4)return false;
            if(e.ContentSpecialKind==0 && e.ContentBossKind==0 && e.ContentAllyBuff<=1)return false;
            if(!RoundContentActive(r)){CancelMeleeWindup(e);return true;}
            var p=ContentTuning(r);double now=r.ContentClock.Elapsed,age=now-e.ContentBornAt;
            var offset=target-e.Position;var direction=offset.sqrMagnitude>.001f ? offset.normalized : Vector2.up;
            if(now<e.ContentStunUntil)return true;
            if(e.ContentBossKind>0)return TickContentBoss(r,e,target,decoy,dt,p,age);
            int kind=e.ContentSpecialKind;int phase=ArcadeRoundContentRules.Phase(age,p.SkillPeriod,p.SkillWarning);
            int cycle=(int)(age/p.SkillPeriod);
            e.ContentWarning=phase==0 && (kind==4 || kind==5 || kind==8 || kind==10 || kind==11 || kind==13 || kind==14 || kind==16 || kind==17);
            if(phase==0 && e.ContentTelegraphCycle!=cycle)
            {
                e.ContentTelegraphCycle=cycle;e.ContentLockedDirection=direction;
                if(kind==16)AddContentArea(r,4,e.Position,e.Position+direction*p.EventRadius*p.SkillMagnitude,p.EventRadius*.12f,
                    Mathf.Max(0,p.SkillWarning-(float)(age%p.SkillPeriod)),0,0,true,false);
            }
            if(kind==16 && phase==0){CancelMeleeWindup(e);return true;}
            if(kind==3 && e.ContentMannequinHits>0 && now-e.ContentLastHitAt<p.SkillWarning)
            {CancelMeleeWindup(e);return true;}
            if(ArcadeRoundContentRules.IsUnderground(kind,age,p) || kind==13 && age<p.SkillWarning)
            {CancelMeleeWindup(e);return true;}
            if(phase==1 && e.ContentSkillCycle!=cycle)
            {
                e.ContentSkillCycle=cycle;
                if(kind==8 || kind==17 || kind==9)
                    ContentShot(r,e,direction,kind==9 ? p.SkillWarning : 0,kind==9 ? 0 : p.SkillMagnitude);
                if(kind==11)AddContentArea(r,4,e.Position,e.Position,p.EventRadius,0,p.SkillWarning,p.EventDamage,false,false);
                if(kind==13 && cycle==0)AddContentArea(r,3,e.Position,e.Position,p.EventRadius,0,p.SkillWarning,p.EventDamage,false,false);
                if(kind==16)ContentRay(r,e.Position,e.ContentLockedDirection,p,1);
                if(kind==12)
                    foreach(var ally in r.Enemies.Values)
                        if(ally!=e && !ally.ContentEvolved && Vector2.Distance(ally.Position,e.Position)<=p.EventRadius)
            { ally.ContentEvolved=true;ally.MaxHealth=Mathf.Max(ally.MaxHealth,Mathf.CeilToInt(ally.MaxHealth*p.SkillMagnitude));ally.Health=ally.MaxHealth; }
            }
            float motion=ArcadeRoundContentRules.SpecialMotion(kind,age,e.ContentLastHitAt,p);
            if(kind==5 || kind==10 || kind==14 || kind==17)
                direction=phase==1 && e.ContentLockedDirection.sqrMagnitude>.001f ? e.ContentLockedDirection : direction;
            bool ranged=kind==8 || kind==9 || kind==11 || kind==16 || kind==17 || kind==0 && e.AttackKind==global::EnemyAttackKind.Projectile;
            if(kind==0 && ranged && offset.magnitude<=e.AttackRange && e.AttackCooldown<=0)
            { ContentShot(r,e,direction,0,0);e.AttackCooldown=e.AttackInterval; }
            if(!ranged || offset.magnitude>e.AttackRange*.8f)
                MoveContentEnemy(r,e,target,direction,motion*e.ContentAllyBuff,dt,kind==2 || kind==6);
            if(!ranged && TryCompleteMeleeAttack(e, Vector2.Distance(e.Position,target)<=
                catalog.Rule.PlayerRadius+catalog.Rule.EnemyContactRadius, decoy, dt))
            {
                e.AttackSequence++;e.AttackCooldown=e.AttackInterval;
                DamageContentPlayer(r,Mathf.Max(1,Mathf.RoundToInt(e.Damage*e.ContentAllyBuff)));
            }
            return true;
        }

        /// <summary>追击仍走正式导航和碰撞；低矮爬行者与蜘蛛用较小实体半径。</summary>
        private void MoveContentEnemy(ArcadeCombatRuntime r,ArcadeEnemyRuntime e,Vector2 target,Vector2 direction,float motion,float dt,bool low)
        {
            float radius=catalog.Rule.EnemyContactRadius*(low ? .65f : 1);
            bool charging=e.ContentSpecialKind==5 || e.ContentSpecialKind==10 || e.ContentSpecialKind==14 || e.ContentBossKind>0;
            var move=charging ? direction : ResolveNavigationDirection(e.Position,target,radius,ObstaclesFor(r));
            e.Position=ResolvePosition(e.Position+move*e.Speed*motion*EnemySpeedMultiplier(r)*SlayerMartStormSpeed(r,e)*dt,radius,
                ArenaHalfWidth,ArenaHalfHeight,ObstaclesFor(r),IslandFor(r));
        }

        /// <summary>活动帧推进环境相位与独占领域；Boss阶段保留技能领域但停止主题环境。</summary>
        private void TickRoundContent(ArcadeCombatRuntime r,float dt)
        {
            if(!RoundContentActive(r) || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
            r.ContentClock.Advance(dt,false);r.ContentSlowRemaining=Mathf.Max(0,r.ContentSlowRemaining-dt);
            TickHerd(r,dt);
            if(this.GetModel<ArcadeCombatModel>().Runtime!=r||catalog==null||!RoundContentActive(r))return;
            TickVortexBarrels(r,dt);
            r.ContentBumperCooldown=Mathf.Max(0,r.ContentBumperCooldown-dt);
            var p=ContentTuning(r);r.ContentAreaSnapshots.Clear();
            foreach(var e in r.Enemies.Values)e.ContentAllyBuff=1;
            foreach(var source in r.Enemies.Values)
                if(source.ContentSpecialKind==7 || source.ContentSpecialKind==12)
                    foreach(var e in r.Enemies.Values)
                        if(e!=source)e.ContentAllyBuff=Mathf.Max(e.ContentAllyBuff,ArcadeRoundContentRules.AllyBuff(source.ContentSpecialKind,Vector2.Distance(source.Position,e.Position),p));
            if(!r.ThemeBossStarted && !r.IsFinalBoss)TickContentEvent(r,p);
            if(r.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime!=r || catalog==null)return;
            for(int i=r.ContentAreas.Count-1;i>=0;i--)
            {
                if(i>=r.ContentAreas.Count)continue;var area=r.ContentAreas[i];
                if(r.ContentClock.Elapsed>=area.ExpiresAt){r.ContentAreas.RemoveAt(i);continue;}
                bool warning=r.ContentClock.Elapsed<area.WarningUntil;
                SnapshotContentArea(r,area.Id,area.Kind,area.Position,area.End,area.Radius,warning,area.Segment);
                if(warning)continue;
                bool touches=ContentTouches(r.PlayerPosition,catalog.Rule.PlayerRadius,area.Position,area.End,area.Radius,area.Segment);
                if(area.Kind==8 && touches)r.ContentSlowRemaining=Mathf.Max(r.ContentSlowRemaining,p.SkillWarning);
                if(area.Kind!=8 && touches && r.ContentClock.TryHit(100000L+area.Id,p.SkillWarning))
                    DamageContentPlayer(r,ArcadeRoundContentRules.Damage(r.PlayerMaxHealth,area.DamageRatio));
                if(r.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime!=r || catalog==null)return;
                if(area.EnemyDamage)DamageContentEnemies(r,area.Position,area.Radius,area.DamageRatio);
            }
        }

        /// <summary>十类环境共用配置相位、真实接触和每目标冷却，预警阶段不结算伤害。</summary>
        private void TickContentEvent(ArcadeCombatRuntime r,ArcadeContentTuning p)
        {
            int kind=p.EventKind;if(kind==0)return;
            double now=r.ContentClock.Elapsed;int phase=ArcadeRoundContentRules.Phase(now,p.EventPeriod,p.EventWarning);
            int cycle=(int)(now/p.EventPeriod);bool warning=phase==0;
            if(cycle!=r.ContentEventTargetCycle){r.ContentEventTargetCycle=cycle;r.ContentEventTarget=ContentPosition(r,r.PlayerPosition);}
            ArcadeRoundContentRules.EventCenter(kind,now,r.ContentEventAnchor.x,r.ContentEventAnchor.y,p.EventRadius*2,p.EventMoveSpeed,out var x,out var y);
            var center=new Vector2(x,y);var end=center;bool line=kind==4;
            if(kind==3)center=r.ContentEventTarget;
            if(line){center=(cycle%2==0 ? Vector2.left : Vector2.down)*ArenaHalfWidth;end=-center;}
            if(kind==5)return;
            if(kind!=3 || warning || now%p.EventPeriod<p.EventWarning*2)
                SnapshotContentArea(r,-100,kind,center,end,line ? p.EventRadius*.12f : p.EventRadius,warning,line);
            if(warning)return;
            bool touching=ContentTouches(r.PlayerPosition,catalog.Rule.PlayerRadius,center,end,line ? p.EventRadius*.12f : p.EventRadius,line);
            bool firstActive=r.ContentEventCycle!=cycle;
            if(firstActive)r.ContentEventCycle=cycle;
            if(kind==8 && touching)r.ContentSlowRemaining=Mathf.Max(r.ContentSlowRemaining,p.SkillWarning);
            if(kind==9 && touching && r.ContentBumperCooldown<=0)
            {
                var landing=-r.PlayerPosition;
                if(Vector2.Distance(landing,r.PlayerPosition)<p.EventRadius*2)landing=r.ContentEventAnchor+Vector2.up*p.EventRadius*p.SkillMagnitude;
                r.PlayerPosition=ContentPosition(r,landing);r.ContentVelocity=Vector2.zero;r.ContentBumperCooldown=p.EventPeriod;
            }
            if(kind==10 && touching && r.ContentClock.TryHit(-1000,p.EventPeriod))
                DamageContentEnemies(r,center,p.EventRadius*p.SkillMagnitude,p.EventDamage);
            if(kind==2)
                foreach(var e in r.Enemies.Values)
                    if(Vector2.Distance(center,e.Position)<=p.EventRadius+catalog.Rule.EnemyContactRadius)e.ContentStunUntil=now+p.SkillWarning;
            if(kind==6)
                foreach(var drop in r.Drops.Values.ToArray())
                    if(Vector2.Distance(center,drop.Position)<=p.EventRadius)
                    { r.Drops.Remove(drop.Id);view?.RemoveDrop(drop.Id);runtimePool.Return(drop); }
            bool harmful=kind==1 || kind==2 || kind==3 || kind==4 || kind==6 || kind==7;
            if(harmful && touching && (kind!=3 || firstActive) && r.ContentClock.TryHit(-100-kind,p.SkillWarning))
                DamagePlayerCore(r,kind==2 ? r.PlayerMaxHealth : ArcadeRoundContentRules.Damage(r.PlayerMaxHealth,p.EventDamage),kind==2?SlayerMartHitKind.LethalMechanism:SlayerMartHitKind.Ordinary);
            if(kind==3 && firstActive && !r.CompletionSubmitted && this.GetModel<ArcadeCombatModel>().Runtime==r && catalog!=null)
                DamageContentExplosionEnemies(r,center,p.EventRadius,p.EventDamage);
        }

        /// <summary>零强度不会被正式伤害入口的一点下限放大。</summary>
        private void DamageContentPlayer(ArcadeCombatRuntime r,int damage)
        { if(damage>0)DamagePlayer(r,damage); }

        /// <summary>死亡爆炸不复用敌人主循环的临时列表，连锁死亡不会覆盖父循环。</summary>
        private void DamageContentEnemies(ArcadeCombatRuntime r,Vector2 center,float radius,float ratio)
            => DamageContentEnemiesInArea(r,center,radius,ratio,false);

        /// <summary>爆桶及爆炸事件保留原伤害归属，仅额外传递尸体受力来源。</summary>
        private void DamageContentExplosionEnemies(ArcadeCombatRuntime r,Vector2 center,float radius,float ratio)
            => DamageContentEnemiesInArea(r,center,radius,ratio,true);

        private void DamageContentEnemiesInArea(ArcadeCombatRuntime r,Vector2 center,float radius,float ratio,bool explosive)
        {
            foreach(var e in r.Enemies.Values.ToArray())
            {
                if(!r.Enemies.ContainsKey(e.Id) || Vector2.Distance(center,e.Position)>radius+catalog.Rule.EnemyContactRadius ||
                    !r.ContentClock.TryHit(200000L+e.Id,ContentTuning(r).SkillWarning))continue;
                DamageEnemy(e,ArcadeRoundContentRules.Damage(e.MaxHealth,ratio));if(e.Health<=0)KillEnemyWithImpact(r,e,true,explosive,center);
                if(this.GetModel<ArcadeCombatModel>().Runtime!=r || catalog==null)return;
            }
        }
        /// <summary>爆桶立刻范围爆炸，毒性敌人留下有寿命的毒云；对象已由调用方移除。</summary>
        private void OnRoundEnemyKilled(ArcadeCombatRuntime r,ArcadeEnemyRuntime e,bool hazardKill)
        {
            if(RoundLayout(r)==null || r.AwaitingExit || r.ThemeBossDefeated || r.FinalBossDefeated)return;
            var p=ContentTuning(r);float radius=ArcadeRoundContentRules.DeathAreaRadius(e.ContentSpecialKind,p);if(radius<=0)return;
            if(e.ContentSpecialKind==1)
            {
                AddContentArea(r,ArcadeRoundContentRules.BarrelExplosionKind,e.Position,e.Position,radius,0,p.SkillWarning,0,false,false);
                DamageContentExplosionEnemies(r,e.Position,radius,p.EventDamage);
            }
            else AddContentArea(r,7,e.Position,e.Position,radius,0,p.SkillPeriod,p.EventDamage,false,false);
        }
        /// <summary>追踪弹保持原速度限幅转向；寄生弹命中通过玩家正式受伤入口后附加减速。</summary>
        private void SteerRoundProjectile(ArcadeCombatRuntime r,ArcadeProjectileRuntime p,float dt)
        {
            if(!p.Hostile || p.ContentTurnRate<=0 || !RoundContentActive(r))return;
            var target=r.PlayerPosition-p.Position;
            ArcadeRoundContentRules.SteerTowards(p.Velocity.x,p.Velocity.y,target.x,target.y,p.ContentTurnRate,dt,out var x,out var y);p.Velocity=new Vector2(x,y);
        }
        /// <summary>正式受伤成功后施加寄生减速，不延长其他效果。</summary>
        private void OnRoundProjectileHit(ArcadeCombatRuntime r,ArcadeProjectileRuntime p)
        { if(r.PlayerHealth>0 && p.ContentSlowSeconds>0)r.ContentSlowRemaining=Mathf.Max(r.ContentSlowRemaining,p.ContentSlowSeconds); }
        /// <summary>冰面指数惯性和孢子/寄生减速只改变候选位移，最终位置仍由正式碰撞裁决。</summary>
        private Vector2 RoundMovementDelta(ArcadeCombatRuntime r,Vector2 input,float speed,float dt)
        {
            var layout=RoundLayout(r);if(layout==null)return input*speed*dt;
            var p=ContentTuning(r);speed*=ArcadeRoundContentRules.SporeSpeedScale(r.ContentSlowRemaining>0,p);
            if(layout.EventKind!=5 || !RoundContentActive(r)){r.ContentVelocity=Vector2.zero;return input*speed*dt;}
            ArcadeRoundContentRules.UpdateIceVelocity(r.ContentVelocity.x,r.ContentVelocity.y,input.x,input.y,speed,
                1f/Mathf.Max(1,p.SkillMagnitude),dt,!RoundContentActive(r),out var x,out var y);
            r.ContentVelocity=new Vector2(x,y);
            var requested=r.ContentVelocity*dt;var position=r.PlayerPosition;
            int steps=Mathf.Max(1,Mathf.CeilToInt(requested.magnitude/Mathf.Max(.05f,catalog.Rule.PlayerRadius*.5f)));
            for(int i=0;i<steps;i++)
            {
                var next=ResolvePosition(position+requested/steps,catalog.Rule.PlayerRadius,ArenaHalfWidth,ArenaHalfHeight,ObstaclesFor(r),IslandFor(r));
                if(Mathf.Abs(next.x-position.x-requested.x/steps)>.001f)r.ContentVelocity.x=0;
                if(Mathf.Abs(next.y-position.y-requested.y/steps)>.001f)r.ContentVelocity.y=0;
                position=next;
            }
            return position-r.PlayerPosition;
        }

        /// <summary>Guardian冲锋/扇弹、ZRex冲锋/喷发、Mega多向射线/增援均有独立预警。</summary>
        private bool TickContentBoss(ArcadeCombatRuntime r,ArcadeEnemyRuntime e,Vector2 target,bool decoy,float dt,ArcadeContentTuning p,double age)
        {
            int phase=ArcadeRoundContentRules.Phase(age,p.SkillPeriod,p.SkillWarning),cycle=(int)(age/p.SkillPeriod);
            int attack=ArcadeRoundContentRules.BossAttack(e.ContentBossKind,age,p.SkillPeriod);
            e.ContentWarning=phase==0;var offset=target-e.Position;
            if(phase==0)
            {
                if(e.ContentTelegraphCycle!=cycle)
                {
                    e.ContentTelegraphCycle=cycle;e.ContentLockedDirection=offset.sqrMagnitude>.001f ? offset.normalized : Vector2.up;e.ContentLockedTarget=ContentPosition(r,target);
                    float remaining=Mathf.Max(0,p.SkillWarning-(float)(age%p.SkillPeriod));
                    if(e.ContentBossKind==3 && attack==0)
                        for(int i=0;i<4;i++)AddContentArea(r,4,e.Position,e.Position+Rotate(e.ContentLockedDirection,i*90)*p.EventRadius*p.SkillMagnitude,p.EventRadius*.12f,remaining,0,0,true,false);
                    if(e.ContentBossKind==2 && attack>0)AddContentArea(r,attack==1 ? 1 : 8,e.ContentLockedTarget,e.ContentLockedTarget,p.EventRadius,remaining,0,0,false,false);
                }
                return true;
            }
            if(e.ContentSkillCycle!=cycle)
            {
                e.ContentSkillCycle=cycle;e.AttackSequence++;
                if(e.ContentBossKind==1 && attack==1)
                    for(int i=-2;i<=2;i++)ContentShot(r,e,Rotate(e.ContentLockedDirection,i*15),0,0);
                if(e.ContentBossKind==2 && attack>0)
                    AddContentArea(r,attack==1 ? 1 : 8,e.ContentLockedTarget,e.ContentLockedTarget,p.EventRadius,0,p.SkillPeriod-p.SkillWarning,p.EventDamage,false,false);
                if(e.ContentBossKind==3)
                {
                    if(attack==0)for(int i=0;i<4;i++)ContentRay(r,e.Position,Rotate(e.ContentLockedDirection,i*90),p,1);
                    if(attack==1)for(int i=0;i<8;i++)ContentShot(r,e,Rotate(e.ContentLockedDirection,i*45),0,p.SkillMagnitude);
                    if(attack==2)SpawnThemeReinforcements(r,Mathf.Max(1,Mathf.RoundToInt(p.SkillMagnitude)));
                }
            }
            if(attack==0 && e.ContentBossKind!=3)
            {
                var before=e.Position;MoveContentEnemy(r,e,target,e.ContentLockedDirection,p.SkillMagnitude,dt,false);
                if(!decoy && ArcadeRoundContentRules.TouchesSegment(r.PlayerPosition.x,r.PlayerPosition.y,catalog.Rule.PlayerRadius,
                    before.x,before.y,e.Position.x,e.Position.y,catalog.Rule.EnemyContactRadius) && e.AttackCooldown<=0)
                {e.AttackCooldown=e.AttackInterval;DamageContentPlayer(r,Mathf.Max(1,Mathf.RoundToInt(e.Damage*p.SkillMagnitude)));}
            }
            return true;
        }
        /// <summary>独立Boss增援按正式敌人配置创建，受全局和敌人组同时在场上限约束。</summary>
        private void SpawnThemeReinforcements(ArcadeCombatRuntime r,int count)
        {
            // 第四轮可能使用单体Boss组，增援必须另取本场正式普通组，不能修改普通轮队列。
            var source=arena.WaveGroupIds.Select(FindGroup).FirstOrDefault(candidate=>candidate!=null && candidate.MaxAlive>1 &&
                candidate.Members.Count>0 && !catalog.Bosses.Any(b=>string.Equals(b.EnemyGroupId,candidate.Id,StringComparison.Ordinal)));
            if(source==null)return;
            int cap=Mathf.Min(source.MaxAlive,catalog.Rule.MaxAliveEnemies);
            var provider=this.GetUtility<IRuntimeConfigProvider>();
            for(int i=0;i<count && r.Enemies.Count<cap;i++)
            {
                string id=source.Members[i%source.Members.Count].EnemyId;if(!provider.TryGetEnemy(id,out var d,out _))continue;
                var e=runtimePool.RentEnemy();e.Id=r.NextEntityId++;e.ConfigId=d.Id;e.AttackKind=d.AttackKind;e.Position=ContentPosition(r,RandomBorderPosition());
                e.BlocksPurpleSkullDrop=d.BlocksPurpleSkullDrop;
                e.MeleeWindupSeconds=d.TelegraphSeconds;
                e.MaxHealth=e.Health=Mathf.Max(1,Mathf.RoundToInt(d.MaxHP*source.HealthMultiplier));e.Damage=Mathf.Max(1,Mathf.RoundToInt(d.AttackDamage*source.DamageMultiplier));
                e.Speed=Mathf.Max(.1f,d.MoveSpeed*source.SpeedMultiplier);e.AttackRange=Mathf.Max(.7f,d.AttackRange);e.AttackInterval=Mathf.Max(.25f,d.AttackCooldownSeconds);
                AssignRoundEnemyContent(r,e);r.Enemies.Add(e.Id,e);
            }
        }
        /// <summary>统一弹丸预算入口成功分配实体后，才追加追踪/减速逻辑。</summary>
        private void ContentShot(ArcadeCombatRuntime r,ArcadeEnemyRuntime e,Vector2 direction,float slow,float turn)
        {
            int next=r.NextEntityId;SpawnProjectile(r,true,e.Position,direction*EnemyProjectileSpeed,Mathf.Max(1,Mathf.RoundToInt(e.Damage*e.ContentAllyBuff)),EnemyProjectileLifetime);
            if(r.Projectiles.TryGetValue(next,out var projectile)){projectile.ContentTurnRate=turn;projectile.ContentSlowSeconds=slow;}e.AttackSequence++;
        }
        /// <summary>锁向射线在预警结束后保留短活动胶囊，伤害和显示使用完全相同线段。</summary>
        private void ContentRay(ArcadeCombatRuntime r,Vector2 from,Vector2 direction,ArcadeContentTuning p,float scale)
            => AddContentArea(r,4,from,from+direction*p.EventRadius*p.SkillMagnitude,p.EventRadius*.12f,0,p.SkillWarning,p.EventDamage*scale,true,false);
        /// <summary>由地图拥有有限领域列表，预警和活动寿命均使用配置秒数。</summary>
        private void AddContentArea(ArcadeCombatRuntime r,int kind,Vector2 position,Vector2 end,float radius,float warning,float lifetime,float damage,bool segment,bool enemies)
        {
            if(r.ContentAreas.Count>=catalog.Rule.MaxAliveEnemies)return;
            r.ContentAreas.Add(new ArcadeContentAreaRuntime {Id=r.ContentNextAreaId++,Kind=kind,Position=position,End=end,Radius=radius,
                WarningUntil=r.ContentClock.Elapsed+warning,ExpiresAt=r.ContentClock.Elapsed+warning+lifetime,DamageRatio=damage,Segment=segment,EnemyDamage=enemies});
        }
        /// <summary>圆和线段共用纯接触规则，显示端不参与判定。</summary>
        private static bool ContentTouches(Vector2 target,float radius,Vector2 from,Vector2 to,float size,bool line)
            => line ? ArcadeRoundContentRules.TouchesSegment(target.x,target.y,radius,from.x,from.y,to.x,to.y,size) :
                ArcadeRoundContentRules.TouchesCircle(target.x,target.y,radius,from.x,from.y,size);
        /// <summary>跃迁与落点必须经过本轮碰撞，不能把玩家或重甲敌人送进阻挡。</summary>
        private Vector2 ContentPosition(ArcadeCombatRuntime r,Vector2 candidate)
        {
            if(catalog==null)return candidate;
            var position=ResolvePosition(candidate,catalog.Rule.PlayerRadius,ArenaHalfWidth,ArenaHalfHeight,ObstaclesFor(r),IslandFor(r));
            var layout=RoundLayout(r);
            if(layout==null || layout.IsWalkable(position.x,position.y,catalog.Rule.PlayerRadius,ArenaHalfWidth,ArenaHalfHeight))return position;
            // 加减障碍中心会产生浮点接触误差；仅失败时留一毫米余量重投影，避免合法墙边跳回出生点。
            const float placementClearance=.001f;
            position=ResolvePosition(position,catalog.Rule.PlayerRadius+placementClearance,ArenaHalfWidth,ArenaHalfHeight,ObstaclesFor(r),IslandFor(r));
            return layout.IsWalkable(position.x,position.y,catalog.Rule.PlayerRadius,ArenaHalfWidth,ArenaHalfHeight)
                ? position : new Vector2(layout.SpawnX,layout.SpawnY);
        }
        /// <summary>单条快照保留真实端点，长射线不创建大量替代圆。</summary>
        private void SnapshotContentArea(ArcadeCombatRuntime r,int id,int kind,Vector2 from,Vector2 to,float radius,bool warning,bool line)
        {
            r.ContentAreaSnapshots.Add(new ArcadeContentAreaSnapshot(id,kind,from.x,from.y,radius,warning,to.x,to.y,line));
        }
        /// <summary>表现只接收活动时钟与只读领域，不持有伤害或阶段推进职责。</summary>
        private void SyncRoundContent(ArcadeCombatRuntime r)
        {
            bool active=RoundContentActive(r);r.ContentPresentationSnapshots.Clear();
            if(active||r.SlayerGuardian!=null)
                foreach(var area in r.ContentAreaSnapshots)
                    if(area.Kind!=ArcadeRoundContentRules.BarrelExplosionKind)r.ContentPresentationSnapshots.Add(area);
            // 弹丸命中晚于领域更新；直接投影本帧爆炸，清场暂停危险也不能吞掉最后一击。
            if(!r.InTreasureRoom&&!r.IslandTransitionPending&&!r.IslandRoomTransitionPending)
                foreach(var area in r.ContentAreas)
                    if(area.Kind==ArcadeRoundContentRules.BarrelExplosionKind&&area.ExpiresAt>r.ContentClock.Elapsed)
                        r.ContentPresentationSnapshots.Add(new ArcadeContentAreaSnapshot(area.Id,area.Kind,area.Position.x,area.Position.y,area.Radius,false));
            foreach(var arc in r.PurpleRayArcs)r.ContentPresentationSnapshots.Add(arc.Snapshot);
            AppendGroundBarrelSnapshots(r);
            view?.SetRoundContent(r.ContentClock.Elapsed,r.ContentPresentationSnapshots);
            view?.SetHerds(r.HerdMapId,r.HerdCows.Select(c=>ArcadeHerdRules.Snapshot(c,EarlyStage?.Herd??default)).ToArray());
            foreach(var e in r.Enemies.Values)view?.SetEnemyContent(e.Id,e.ContentSpecialKind,e.ContentBossKind,e.ContentWarning);
        }
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
using FirepowerLoop.Arcade;

namespace ET.Server
{
    public static partial class OnlineArcadeRunManagerSystem
    {
        /// <summary>仅捕获稳定逻辑值；世界敌人、弹丸、掉落、句柄不进入轮前节点。</summary>
        private static void CaptureRoundCheckpoint(OnlineArcadeRunRecord record, OnlineArcadeBattleRuntime battle)
        {
            record.SlayerMart=battle.SlayerMart?.Clone();
            record.RoundLayoutId=battle.RoundLayout.Id;record.RoundLayoutVersion=battle.RoundLayout.Version;
            record.RoundNumber=battle.WaveIndex+1;record.RoundBossEncounter=battle.RoundBossEncounter;record.RoundGeneration=battle.RoundGeneration;
            record.DestroyedCrateIds=battle.DestroyedCrateIds.OrderBy(id=>id).ToList();
            record.CrateHealth=battle.Crates.ToDictionary(pair=>pair.Key,pair=>pair.Value.Health);
            record.DestroyedBarrelIds=battle.DestroyedBarrelIds.OrderBy(id=>id).ToList();
            foreach(var source in battle.Players.Values)
            {
                var target=record.Players.First(p=>p.PlayerId==source.PlayerId);
                target.RoundHealth=source.CurrentHealth;target.RoundAlive=source.Alive;
                target.RoundDownedSeconds=source.DownedSeconds;
                target.RoundInvulnerabilitySeconds=source.InvulnerabilitySeconds;target.RoundRespawnSeconds=source.RespawnSeconds;
                target.RoundDashSeconds=source.DashSeconds;target.RoundFireCooldownSeconds=source.FireCooldownSeconds;
                target.RoundDashDirectionX=source.DashDirectionX;target.RoundDashDirectionZ=source.DashDirectionZ;
                target.RoundTemporaryWeaponId=source.TemporaryWeaponId;
                target.RoundTemporaryWeaponSeconds=source.TemporaryWeaponSeconds;
                target.RoundPowerups=source.Powerups.Values.Where(p=>p.RemainingSeconds>0).Select(OnlineArcadeRunActorSystem.ClonePowerup).ToList();
            }
        }
        /// <summary>候选验证与全员写盘在公开新轮之前完成；失败保留旧现场冻结，可重试同一候选。</summary>
        private static bool TryCommitRoundTransition(this OnlineArcadeRunManagerComponent manager,OnlineArcadeRunActor actor)
        {
            var previous=actor.Battle;
            if(previous==null||!previous.RoundTransitionPending)return false;
            if(actor.TryBeginSlayerMart(previous))return true;
            try
            {
                var candidate=Clone(actor.Record);
                CaptureRoundCheckpoint(candidate,previous);
                candidate.RoundNumber=previous.PendingBossEncounter?4:previous.WaveIndex+2;
                candidate.RoundBossEncounter=previous.PendingBossEncounter;
                var layout=OnlineArcadeRunActorSystem.ResolveRoundLayout(previous.ArenaConfigNumber,candidate.RoundNumber);
                if(layout.Id!=previous.RoundLayout.Id||layout.Version!=previous.RoundLayout.Version)candidate.DestroyedBarrelIds.Clear();
                candidate.RoundLayoutId=layout.Id;candidate.RoundLayoutVersion=layout.Version;
                candidate.Revision=checked(candidate.Revision+1);candidate.UpdatedAtUtc=DateTime.UtcNow.ToString("O");
                if(!OnlineArcadeRunActorSystem.ApplySlayerMartRoundCandidate(candidate,out var benefitError))throw new InvalidOperationException(benefitError);
                // 新轮只补空库存一次，候选写盘失败不会触碰工作态。
                if(!candidate.RoundBossEncounter)foreach(var p in candidate.Players.Where(OnlineArcadeRunActorSystem.CanContinueCoop))
                {p.Bombs=ArcadeOpeningRules.ReplenishEmpty(p.Bombs);p.Dashes=ArcadeOpeningRules.ReplenishEmpty(p.Dashes);}
                OnlineArcadeRunActorSystem.SyncSlayerMartRoundInventory(candidate);
                // 场内切轮靠地图代次确认加载，不重复使用安全节点的开场准备位。
                var prepared=OnlineArcadeRunActorSystem.PrepareBattle(candidate,false,false);
                prepared.RoundGeneration=checked(previous.RoundGeneration+1);
                prepared.BattleRevision=checked(previous.BattleRevision+1);
                candidate.RoundGeneration=prepared.RoundGeneration;
                manager.CommitWithCandidate(candidate,candidate.RunId);
                CommitActorRecord(actor,candidate);actor.Battle=prepared;
                return true;
            }
            catch(Exception ex)
            { previous.RoundLoadError="轮前节点保存或准备失败，可重试："+ex.Message;return false; }
        }
    }

    public static partial class OnlineArcadeRunActorSystem
    {
        /// <summary>轮内容只读快照与配置均通过稳定布局身份解析，不回退通用地图。</summary>
        public static ArcadeRoundLayoutParameters ResolveRoundLayout(int arena,int round)
        {
            var row=ServerConfig.Instance.Tables.TbArcadeRoundLayout.GetOrDefault("arena."+arena+".round."+round);
            if(row==null)throw new InvalidOperationException("轮布局缺失");
            return ArcadeRoundLayoutParameters.FromConfig(row);
        }
        private static IReadOnlyList<ArcadeObstacleConfig> EffectiveObstacles(OnlineArcadeBattleRuntime battle)
            => battle.RoundLayout==null?ResolveBattleArena(battle).Obstacles:
                ServerConfig.Instance.Tables.TbArcadeRoundLayout.Get(battle.RoundLayout.Id).Obstacles;
        /// <summary>当前参与且尚有命数的成员必须确认当前代次，死亡复活等待者也不能绕过加载门禁。</summary>
        private static bool RoundPlayersReady(OnlineArcadeBattleRuntime battle)
            => battle.RoundLayout==null||battle.Players.Values.All(p=>p.ReadyRoundGeneration==battle.RoundGeneration);
        private static ArcadeContentTuning ContentTuning(OnlineArcadeBattleRuntime b)
        {
            var p=b.RoundLayout;
            return new ArcadeContentTuning(p.SpecialKind,p.EventKind,p.BossKind,p.SpecialRatio,p.EnemyHealthScale,p.EnemySpeedScale,
                p.SkillPeriod,p.SkillWarning,p.SkillMagnitude,p.EventPeriod,p.EventWarning,p.EventRadius,p.EventDamage,p.EventMoveSpeed);
        }
        /// <summary>深复制强化逻辑剩余量与冷却，不复制字典或运行时对象身份。</summary>
        public static OnlineArcadePowerupRuntime ClonePowerup(OnlineArcadePowerupRuntime p)=>new()
        {ProductId=p.ProductId,Behavior=p.Behavior,Magnitude=p.Magnitude,Damage=p.Damage,IntervalSeconds=p.IntervalSeconds,
            Radius=p.Radius,ProjectileSpeed=p.ProjectileSpeed,RemainingSeconds=p.RemainingSeconds,Charges=p.Charges,
            CooldownSeconds=p.CooldownSeconds,PositionX=p.PositionX,PositionZ=p.PositionZ,ShotDirectionX=p.ShotDirectionX,ShotDirectionZ=p.ShotDirectionZ};
        /// <summary>四向通道共用同一去向；校验接触意图，同 Tick 按稳定玩家 ID 仲裁一次。</summary>
        private static bool TickRoundExit(OnlineArcadeBattleRuntime b)
        {
            if(b.RoundTransitionPending||b.ArenaCompletionReady||b.TreasureRoom?.Members.Length>0)return false;
            var layout=b.RoundLayout;var arena=ResolveBattleArena(b);
            bool nextRound=b.WaveIndex<3&&!b.RoundBossEncounter;
            bool nextBoss=!nextRound&&!b.RoundBossEncounter&&layout.BossKind>0;
            var targets=nextRound||nextBoss?new[]{arena.ArenaNumber}:b.FinalBossDefeated?new[]{0}:arena.NextArenaNumbers;
            if(targets.Length==0)return false;
            var rule=ServerConfig.Instance.ArcadeRule;
            foreach(var p in b.Players.Values.OrderBy(p=>p.PlayerId))
            {
                if(!p.Alive||p.InTreasureRoom||p.ConfirmedExitTarget==int.MinValue)continue;
                int intent=p.ConfirmedExitTarget;p.ConfirmedExitTarget=int.MinValue;
                if(UsesStagePortal(b))
                {
                    if(intent==targets[0]&&ArcadeStagePortalRules.TryContact(b.StagePortal,ServerConfig.Instance.RoundReward,p.PlayerId,p.PositionX,p.PositionZ,p.Alive&&p.RespawnSeconds<=0))
                    {b.SelectedExitConfigNumber=intent;b.ArenaCompletionReady=true;return true;}
                    continue;
                }
                for(int i=0;i<layout.ExitCount;i++)
                {
                    var exit=layout.GetExitPosition(i,ArenaHalfWidth(b),ArenaHalfHeight(b),rule.PlayerRadius);
                    if(intent!=targets[0]||DistanceSquared(p.PositionX,p.PositionZ,exit.X,exit.Y)>2.25f)continue;
                    if(nextRound||nextBoss){b.RoundTransitionPending=true;b.PendingBossEncounter=nextBoss;}
                    else {b.SelectedExitConfigNumber=intent;b.ArenaCompletionReady=true;}
                    return true;
                }
            }
            return false;
        }
        /// <summary>专属 Boss 是四轮后的独立遭遇，不增加主线轮数；非终局 Boss 不触发总胜利。</summary>
        private static void PrepareRoundBoss(OnlineArcadeBattleRuntime b)
        {
            b.RoundBossEncounter=true;b.IsFinalBoss=b.ArenaConfigNumber==20;b.AwaitingExit=false;
            b.SpawnQueue.Clear();b.SpawnQueueIndex=0;b.SpawnedInWave=0;
            var group=ResolveBattleGroup(b,ResolveBattleArena(b),3);
            b.SpawnQueue.Add(group.Members[0].EnemyId);b.TotalInWave=1;b.MaxAlive=1;b.Hazards.Clear();b.NextSpawnAt=0;
            if(b.IsFinalBoss)b.ReinforcementCooldown=ServerConfig.Instance.Tables.TbArcadeBoss.Get(ResolveBattleArena(b).FinalBossId).ReinforcementIntervalSeconds;
        }
        private static void InitializeEnemyContent(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime enemy)
        {
            enemy.ContentBornAt=b.ContentClock.Elapsed;
            if(b.RoundLayout==null||b.SlayerChallenge!=null)return;var p=ContentTuning(b);
            if(b.RoundBossEncounter&&b.SpawnedInWave==0)
            {
                enemy.ContentBossKind=p.BossKind;
                if(p.BossKind==4)
                {var boss=ServerConfig.Instance.Tables.TbArcadeBoss.Get(ResolveBattleArena(b).FinalBossId);enemy.BossId=boss.Id;enemy.BossSkillCooldownSeconds=boss.SkillCooldownSeconds;enemy.BossRayCooldownSeconds=boss.RayIntervalSeconds;}
                else enemy.BossId=string.Empty;
                enemy.MaxHealth=Math.Max(1,(int)Math.Round(enemy.MaxHealth*p.EnemyHealthScale*p.SkillMagnitude));
            }
            else if((b.SpawnedInWave+1)*p.SpecialRatio>=Math.Floor(b.SpawnedInWave*p.SpecialRatio)+1)
            {enemy.SpecialKind=p.SpecialKind;enemy.MaxHealth=Math.Max(1,(int)Math.Round(enemy.MaxHealth*p.EnemyHealthScale));enemy.MoveSpeed*=p.EnemySpeedScale;}
            if(enemy.SpecialKind==2||enemy.SpecialKind==6)enemy.MoveSpeed*=MathF.Sqrt(p.SkillMagnitude);
            if(enemy.SpecialKind==13)
            {var target=b.Players.Values.Where(v=>v.Alive).OrderBy(v=>v.PlayerId).FirstOrDefault();if(target!=null){float x=target.PositionX,y=target.PositionZ+p.EventRadius;ResolveBattlePosition(b,ref x,ref y,target.PositionX,target.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,EffectiveObstacles(b),IslandFor(b));enemy.PositionX=x;enemy.PositionZ=y;}}
            enemy.CurrentHealth=enemy.MaxHealth;
        }
        private static float EnemyContentMotion(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime enemy,float delta)
        {
            if(b.RoundLayout==null)return 1;if(!RoundContentActive(b))return enemy.SpecialKind>0||enemy.ContentBossKind>0?0:1;enemy.ContentElapsed+=delta;var p=ContentTuning(b);
            if(enemy.ContentStunUntil>b.ContentClock.Elapsed)return 0;
            enemy.ContentWarning=enemy.ContentBossKind==4 ? enemy.BossPhase==OnlineArcadeBossPhase.Telegraph||enemy.BossPhase==OnlineArcadeBossPhase.RayTelegraph : ArcadeRoundContentRules.Phase(enemy.ContentElapsed,p.SkillPeriod,p.SkillWarning)==0;
            float speed=ArcadeRoundContentRules.SpecialMotion(enemy.SpecialKind,enemy.ContentElapsed,enemy.LastContentHitAt,p);
            float aura=1;
            foreach(var ally in b.Enemies.Values)
                if(ally.RuntimeId!=enemy.RuntimeId) aura=Math.Max(aura,ArcadeRoundContentRules.AllyBuff(ally.SpecialKind,
                    MathF.Sqrt(DistanceSquared(enemy.PositionX,enemy.PositionZ,ally.PositionX,ally.PositionZ)),p));
            return speed*aura;
        }
        /// <summary>追踪精英和重型精英在活动边缘实际发弹，不借用名称替代技能。</summary>
        private static void TickSpecialAttack(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,OnlineArcadeBattlePlayerRuntime target)
        {
            if(!RoundContentActive(b)||e.SpecialKind==0)return;
            var p=ContentTuning(b);long cycle=(long)(e.ContentElapsed/p.SkillPeriod);
            float dx=target.PositionX-e.PositionX,dy=target.PositionZ-e.PositionZ,len=Math.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));
            if(e.ContentWarning)
            {
                if(e.ContentTelegraphCycle!=cycle)
                {e.ContentTelegraphCycle=cycle;e.ContentLockedX=dx/len;e.ContentLockedZ=dy/len;
                    if(e.SpecialKind==16)AddContentZone(b,4,e.PositionX,e.PositionZ,e.PositionX+dx/len*p.EventRadius*p.SkillMagnitude,e.PositionZ+dy/len*p.EventRadius*p.SkillMagnitude,
                        p.EventRadius*.12f,p.SkillWarning,p.SkillWarning,0,true,false);}
                return;
            }
            if(e.LastContentAttackCycle==cycle)return;e.LastContentAttackCycle=cycle;
            if(e.SpecialKind==12)
                foreach(var ally in b.Enemies.Values)
                    if(ally.RuntimeId!=e.RuntimeId&&!ally.ContentEvolved&&DistanceSquared(e.PositionX,e.PositionZ,ally.PositionX,ally.PositionZ)<=p.EventRadius*p.EventRadius)
                    {ally.ContentEvolved=true;ally.MaxHealth=Math.Max(ally.MaxHealth,(int)Math.Ceiling(ally.MaxHealth*p.SkillMagnitude));ally.CurrentHealth=ally.MaxHealth;}
            if(e.SpecialKind==11)AddContentZone(b,4,e.PositionX,e.PositionZ,e.PositionX,e.PositionZ,p.EventRadius,0,p.SkillWarning,p.EventDamage,false,false);
            if(e.SpecialKind==13&&cycle==0)AddContentZone(b,3,e.PositionX,e.PositionZ,e.PositionX,e.PositionZ,p.EventRadius,0,p.SkillWarning,p.EventDamage,false,true);
            if(e.SpecialKind==16)AddContentZone(b,4,e.PositionX,e.PositionZ,e.PositionX+e.ContentLockedX*p.EventRadius*p.SkillMagnitude,e.PositionZ+e.ContentLockedZ*p.EventRadius*p.SkillMagnitude,p.EventRadius*.12f,0,p.SkillWarning,p.EventDamage,true,false);
            if(!(e.SpecialKind==8||e.SpecialKind==9||e.SpecialKind==17)||b.Projectiles.Count>=ServerConfig.Instance.ArcadeRule.MaxProjectiles)return;
            long id=b.NextProjectileRuntimeId++;
            b.Projectiles.Add(id,new OnlineArcadeProjectileRuntime{RuntimeId=id,PositionX=e.PositionX,PositionZ=e.PositionZ,
                VelocityX=dx/len*p.EventMoveSpeed,VelocityZ=dy/len*p.EventMoveSpeed,Damage=e.Damage,
                RemainingSeconds=p.SkillPeriod,RemainingHits=1,TrackingTurnRate=e.SpecialKind==9?0:p.SkillMagnitude,ContentSlowSeconds=e.SpecialKind==9?p.SkillWarning:0});
        }
        /// <summary>特殊敌人完整接管移动与攻击，冻结和预告不再落入旧普通攻击分支。</summary>
        private static bool TickSpecialEnemy(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,
            OnlineArcadeBattlePlayerRuntime target,float targetX,float targetY,bool decoy,float movementDelta)
        {
            if(e.SpecialKind==0)return false;
            if(!RoundContentActive(b))return true;
            var p=ContentTuning(b);int kind=e.SpecialKind;
            actor.TickSpecialAttack(b,e,target);
            if(e.ContentStunUntil>b.ContentClock.Elapsed||ArcadeRoundContentRules.IsUnderground(kind,e.ContentElapsed,p)||
                kind==13&&e.ContentElapsed<p.SkillWarning||kind==3&&e.LastContentHitAt>=0&&e.ContentElapsed-e.LastContentHitAt<p.SkillWarning)return true;
            bool ranged=kind==8||kind==9||kind==11||kind==16||kind==17;
            if((kind==5||kind==10||kind==14||kind==17)&&!e.ContentWarning&&(e.ContentLockedX!=0||e.ContentLockedZ!=0))
            {targetX=e.PositionX+e.ContentLockedX*p.EventRadius*p.SkillMagnitude;targetY=e.PositionZ+e.ContentLockedZ*p.EventRadius*p.SkillMagnitude;}
            float distance=DistanceSquared(e.PositionX,e.PositionZ,targetX,targetY);
            if(!ranged||distance>e.AttackRange*e.AttackRange*.64f)
                MoveEnemy(e,targetX,targetY,movementDelta,ServerConfig.Instance.ArcadeRule,EffectiveObstacles(b),IslandFor(b));
            float radius=ServerConfig.Instance.ArcadeRule.PlayerRadius+ServerConfig.Instance.ArcadeRule.EnemyContactRadius;
            if(!ranged&&!decoy&&e.AttackCooldownSeconds<=0&&DistanceSquared(e.PositionX,e.PositionZ,target.PositionX,target.PositionZ)<=radius*radius)
            {
                float aura=1;
                foreach(var ally in b.Enemies.Values)if(ally.RuntimeId!=e.RuntimeId)
                    aura=Math.Max(aura,ArcadeRoundContentRules.AllyBuff(ally.SpecialKind,MathF.Sqrt(DistanceSquared(e.PositionX,e.PositionZ,ally.PositionX,ally.PositionZ)),p));
                actor.ContentDamagePlayer(b,target,Math.Max(1,(int)Math.Round(e.Damage*aura)));e.AttackCooldownSeconds=e.AttackIntervalSeconds;
            }
            return true;
        }

        /// <summary>零伤害领域只显示，不触发正式伤害入口的一点下限。</summary>
        private static void ContentDamagePlayer(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeBattlePlayerRuntime player,int damage)
        {if(damage>0)actor.DamagePlayer(b,player,damage);}
        /// <summary>领域在本地图生成唯一身份，预警和伤害分别计时。</summary>
        private static void AddContentZone(OnlineArcadeBattleRuntime b,int kind,float x,float y,float ex,float ey,float radius,float warning,float duration,float damage,bool segment,bool enemyDamage)
        {b.DeathZones.Add(new OnlineArcadeContentZoneRuntime{Id=(int)b.NextProjectileRuntimeId++,Kind=kind,X=x,Y=y,EndX=ex,EndY=ey,Radius=radius,WarningRemaining=warning,Remaining=warning+duration,DamageRatio=damage,Segment=segment,EnemyDamage=enemyDamage});}
        private static void DamageContentEnemies(OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,float x,float y,float radius,float ratio,long ownerPlayerId=0)
            => DamageContentEnemiesInArea(actor,b,x,y,radius,ratio,ownerPlayerId,false);
        /// <summary>爆炸事件单独标记表现类别，不改变原有伤害与奖励归属。</summary>
        private static void DamageContentExplosionEnemies(OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,float x,float y,float radius,float ratio,long ownerPlayerId=0)
            => DamageContentEnemiesInArea(actor,b,x,y,radius,ratio,ownerPlayerId,true);
        private static void DamageContentEnemiesInArea(OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,float x,float y,float radius,float ratio,long ownerPlayerId,bool explosive)
        {foreach(var e in b.Enemies.Values.ToArray())if(ArcadeRoundContentRules.TouchesCircle(e.PositionX,e.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,x,y,radius))
            {if(ownerPlayerId>0)e.LastDamagePlayerId=ownerPlayerId;DamageEnemy(b,e,ArcadeRoundContentRules.Damage(e.MaxHealth,ratio));if(e.CurrentHealth<=0)actor.KillEnemyWithImpact(b,e,true,explosive,x,y);}}

        /// <summary>Guardian五弹扇面、ZRex锁位喷发/孢子、Mega四线/八弹/增援；最终Boss交还既有配置技能。</summary>
        private static bool TickContentBoss(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,OnlineArcadeBattlePlayerRuntime target,float delta)
        {
            if(e.ContentBossKind==0||e.ContentBossKind==4)return false;
            if(!RoundContentActive(b))return true;
            var p=ContentTuning(b);long cycle=(long)(e.ContentElapsed/p.SkillPeriod);
            int attack=ArcadeRoundContentRules.BossAttack(e.ContentBossKind,e.ContentElapsed,p.SkillPeriod);
            e.ContentWarning=ArcadeRoundContentRules.Phase(e.ContentElapsed,p.SkillPeriod,p.SkillWarning)==0;
            float dx=target.PositionX-e.PositionX,dy=target.PositionZ-e.PositionZ,length=Math.Max(.001f,MathF.Sqrt(dx*dx+dy*dy));
            if(e.ContentWarning)
            {
                e.BossPhase=OnlineArcadeBossPhase.Telegraph;
                if(e.ContentTelegraphCycle!=cycle)
                {
                    e.ContentTelegraphCycle=cycle;e.ContentLockedX=dx/length;e.ContentLockedZ=dy/length;
                    e.ContentLockedTargetX=target.PositionX;e.ContentLockedTargetY=target.PositionZ;
                    float remaining=Math.Max(0,p.SkillWarning-(float)(e.ContentElapsed%p.SkillPeriod));
                    if(e.ContentBossKind==3&&attack==0)
                        for(int i=0;i<4;i++){RotateContent(e.ContentLockedX,e.ContentLockedZ,i*Math.PI/2,out float x,out float y);AddContentZone(b,4,e.PositionX,e.PositionZ,e.PositionX+x*p.EventRadius*p.SkillMagnitude,e.PositionZ+y*p.EventRadius*p.SkillMagnitude,p.EventRadius*.12f,remaining,0,0,true,false);}
                    if(e.ContentBossKind==2&&attack>0)AddContentZone(b,attack==1?1:8,e.ContentLockedTargetX,e.ContentLockedTargetY,e.ContentLockedTargetX,e.ContentLockedTargetY,p.EventRadius,remaining,0,0,false,false);
                }
                return true;
            }
            if(e.ContentLockedX==0&&e.ContentLockedZ==0){e.ContentLockedX=dx/length;e.ContentLockedZ=dy/length;e.ContentLockedTargetX=target.PositionX;e.ContentLockedTargetY=target.PositionZ;}
            e.BossPhase=attack==0&&e.ContentBossKind!=3?OnlineArcadeBossPhase.Charging:OnlineArcadeBossPhase.None;
            if(e.LastContentAttackCycle!=cycle)
            {
                e.LastContentAttackCycle=cycle;
                if(e.ContentBossKind==1&&attack==1)
                    for(int i=-2;i<=2;i++){RotateContent(e.ContentLockedX,e.ContentLockedZ,i*Math.PI/12,out float x,out float y);ContentBossShot(b,e,x,y,0);}
                if(e.ContentBossKind==2&&attack>0)AddContentZone(b,attack==1?1:8,e.ContentLockedTargetX,e.ContentLockedTargetY,e.ContentLockedTargetX,e.ContentLockedTargetY,p.EventRadius,0,p.SkillPeriod-p.SkillWarning,p.EventDamage,false,false);
                if(e.ContentBossKind==3)
                {
                    if(attack==0)for(int i=0;i<4;i++){RotateContent(e.ContentLockedX,e.ContentLockedZ,i*Math.PI/2,out float x,out float y);AddContentZone(b,4,e.PositionX,e.PositionZ,e.PositionX+x*p.EventRadius*p.SkillMagnitude,e.PositionZ+y*p.EventRadius*p.SkillMagnitude,p.EventRadius*.12f,0,p.SkillWarning,p.EventDamage,true,false);}
                    if(attack==1)for(int i=0;i<8;i++){RotateContent(e.ContentLockedX,e.ContentLockedZ,i*Math.PI/4,out float x,out float y);ContentBossShot(b,e,x,y,p.SkillMagnitude);}
                    if(attack==2)SpawnContentReinforcements(b,Math.Max(1,(int)Math.Round(p.SkillMagnitude)));
                }
            }
            if(attack==0&&e.ContentBossKind!=3)
            {
                float beforeX=e.PositionX,beforeY=e.PositionZ;
                float x=e.PositionX+e.ContentLockedX*e.MoveSpeed*p.SkillMagnitude*delta,y=e.PositionZ+e.ContentLockedZ*e.MoveSpeed*p.SkillMagnitude*delta;
                ResolveBattlePosition(b,ref x,ref y,beforeX,beforeY,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,EffectiveObstacles(b),IslandFor(b));e.PositionX=x;e.PositionZ=y;
                if(e.AttackCooldownSeconds<=0&&ArcadeRoundContentRules.TouchesSegment(target.PositionX,target.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,beforeX,beforeY,x,y,ServerConfig.Instance.ArcadeRule.EnemyContactRadius))
                {actor.ContentDamagePlayer(b,target,Math.Max(1,(int)Math.Round(e.Damage*p.SkillMagnitude)));e.AttackCooldownSeconds=e.AttackIntervalSeconds;}
            }
            return true;
        }
        private static void RotateContent(float x,float y,double angle,out float rx,out float ry)
        {rx=(float)(x*Math.Cos(angle)-y*Math.Sin(angle));ry=(float)(x*Math.Sin(angle)+y*Math.Cos(angle));}
        /// <summary>内容Boss仍受全局弹丸预算，只有成功创建后才增加实体序。</summary>
        private static void ContentBossShot(OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e,float x,float y,float turn)
        {
            if(b.Projectiles.Count>=ServerConfig.Instance.ArcadeRule.MaxProjectiles)return;var p=ContentTuning(b);long id=b.NextProjectileRuntimeId++;
            b.Projectiles.Add(id,new OnlineArcadeProjectileRuntime{RuntimeId=id,PositionX=e.PositionX,PositionZ=e.PositionZ,VelocityX=x*p.EventMoveSpeed,VelocityZ=y*p.EventMoveSpeed,
                Damage=e.Damage,RemainingSeconds=p.SkillPeriod,RemainingHits=1,TrackingTurnRate=turn});
        }
        /// <summary>增援由本轮正式敌群创建，和单人一样同时受敌群及全局数量上限约束。</summary>
        private static void SpawnContentReinforcements(OnlineArcadeBattleRuntime b,int count)
        {
            // 独立Boss占用第四组的一人预算；增援使用本场首个正式普通群，不能以Boss群上限静默禁用技能。
            var arena=ResolveBattleArena(b);
            var group=arena.WaveGroupIds.Select(id=>ServerConfig.Instance.Tables.TbArcadeEnemyGroup.Get(id)).FirstOrDefault(g=>g.MaxAlive>1&&g.Members.Length>0&&!ServerConfig.Instance.Tables.TbArcadeBoss.DataList.Any(boss=>boss.EnemyGroupId==g.Id));
            if(group==null||group.Members.Length==0)return;
            int cap=Math.Min(group.MaxAlive,ServerConfig.Instance.ArcadeRule.MaxAliveEnemies);
            for(int i=0;i<count&&b.Enemies.Count<cap;i++)
            {
                var source=ServerConfig.Instance.GetEnemy(group.Members[i%group.Members.Length].EnemyId);long id=b.NextEnemyRuntimeId++;
                var entry=b.RoundLayout.Entries[i%b.RoundLayout.Entries.Count];
                var enemy=new OnlineArcadeEnemyRuntime{RuntimeId=id,EnemyConfigId=source.Id,PositionX=entry.X,PositionZ=entry.Y,
                    MaxHealth=Math.Max(1,(int)Math.Round(source.MaxHP*group.HealthMultiplier)),Damage=Math.Max(1,(int)Math.Round(source.AttackDamage*group.DamageMultiplier)),
                    MoveSpeed=Math.Max(.1f,source.MoveSpeed*group.SpeedMultiplier),AttackKind=source.AttackKind,AttackRange=Math.Max(.7f,source.AttackRange),
                    AttackIntervalSeconds=Math.Max(.25f,source.AttackCooldownSeconds),ProjectileConfigId=source.ProjectileConfigId,WaveNumber=b.WaveIndex+1};
                InitializeEnemyContent(b,enemy);b.Enemies.Add(id,enemy);
            }
        }
        private static void TickTrackingProjectile(OnlineArcadeBattleRuntime b,OnlineArcadeProjectileRuntime shot,float delta)
        {
            if(shot.TrackingTurnRate<=0)return;var target=b.Players.Values.Where(p=>p.Alive&&!p.InTreasureRoom)
                .OrderBy(p=>DistanceSquared(shot.PositionX,shot.PositionZ,p.PositionX,p.PositionZ)).ThenBy(p=>p.PlayerId).FirstOrDefault();
            if(target==null)return;
            ArcadeRoundContentRules.SteerTowards(shot.VelocityX,shot.VelocityZ,target.PositionX-shot.PositionX,target.PositionZ-shot.PositionZ,
                shot.TrackingTurnRate,delta,out shot.VelocityX,out shot.VelocityZ);
        }
        /// <summary>爆桶按各目标最大生命比例结算一次伤害；爆桶危险区使用短预警时长，毒云保留技能周期寿命。</summary>
        private static void SpawnContentDeathZone(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,OnlineArcadeEnemyRuntime e)
        {
            if(b.RoundLayout==null)return;var p=ContentTuning(b);
            if(ArcadeRoundContentRules.DeathAreaRadius(e.SpecialKind,p)<=0)return;
            if(e.SpecialKind==1)
            {
                // 使用独立身份快照，连锁死亡不会修改外层遍历；伤害不再取载体攻击力。
                float radius=p.EventRadius+ServerConfig.Instance.ArcadeRule.EnemyContactRadius;
                foreach(long targetId in b.Enemies.Keys.OrderBy(id=>id).ToArray())
                {
                    if(!b.Enemies.TryGetValue(targetId,out var target) ||
                        DistanceSquared(e.PositionX,e.PositionZ,target.PositionX,target.PositionZ)>radius*radius ||
                        !b.ContentClock.TryHit(200000L+targetId,p.SkillWarning))continue;
                    target.LastDamagePlayerId=e.LastDamagePlayerId;
                    DamageEnemy(b,target,ArcadeRoundContentRules.Damage(target.MaxHealth,p.EventDamage));
                    if(target.CurrentHealth<=0)actor.KillEnemyWithImpact(b,target,false,true,e.PositionX,e.PositionZ);
                }
            }
            b.DeathZones.Add(new OnlineArcadeContentZoneRuntime{Id=(int)b.NextProjectileRuntimeId++,Kind=e.SpecialKind==15?7:ArcadeRoundContentRules.BarrelExplosionKind,X=e.PositionX,Y=e.PositionZ,Remaining=e.SpecialKind==15?p.SkillPeriod:p.SkillWarning,DamageRatio=e.SpecialKind==1?0:p.EventDamage});
        }
        /// <summary>冰面惯性与寄生/孢子减速仍走正式碰撞入口，暂停门禁在外层统一冻结。</summary>
        private static void ContentPlayerVelocity(OnlineArcadeBattleRuntime b,OnlineArcadeBattlePlayerRuntime player,float speed,float delta,out float vx,out float vy)
        {
            // 锁定方向只覆盖冲刺期间的候选移动，仍沿正式地形和障碍碰撞链裁决。
            vx=(player.DashSeconds>0?player.DashDirectionX:player.MoveX)*speed;
            vy=(player.DashSeconds>0?player.DashDirectionZ:player.MoveZ)*speed;
            if(b.RoundLayout==null||player.InTreasureRoom)return;
            var p=ContentTuning(b);bool ice=false,slow=false;
            foreach(var area in b.ContentAreas)
                if(!area.Warning&&ArcadeRoundContentRules.TouchesCircle(player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,area.X,area.Y,area.Radius))
                {ice|=area.Kind==5;slow|=area.Kind==8;}
            foreach(var enemy in b.Enemies.Values)
                if(enemy.SpecialKind==9&&ArcadeRoundContentRules.TouchesCircle(player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,enemy.PositionX,enemy.PositionZ,p.EventRadius))slow=true;
            slow|=player.ContentSlowRemaining>0;
            if(slow){vx*=ArcadeRoundContentRules.SporeSpeedScale(true,p);vy*=ArcadeRoundContentRules.SporeSpeedScale(true,p);}
            ice|=p.EventKind==5;
            if(ice)ArcadeRoundContentRules.UpdateIceVelocity(player.IceVelocityX,player.IceVelocityZ,vx/Math.Max(speed,float.Epsilon),vy/Math.Max(speed,float.Epsilon),speed,1/Math.Max(1,p.SkillMagnitude),delta,false,out vx,out vy);
            player.IceVelocityX=vx;player.IceVelocityZ=vy;
        }

        /// <summary>轮内容活动门禁；清场、复活等待、全员宝藏、切图、失败均冻结环境与技能。</summary>
        private static bool RoundContentActive(OnlineArcadeBattleRuntime b)=>b.RoundLayout!=null&&!b.IsChallenge&&!b.AwaitingExit&&
            !b.RoundTransitionPending&&!b.ArenaCompletionReady&&!b.AllPlayersDefeated&&!b.Players.Values.Any(p=>p.RespawnSeconds>0)&&
            b.Players.Values.Any(p=>p.Alive&&!p.InTreasureRoom)&&(!b.WaveClearAwarded||b.RoundBossEncounter&&!b.RoundBossDefeated);

        /// <summary>权威事件与死亡领域只随活动时间推进，每目标每间隔至多结算一次；快照只读投影。</summary>
        private static bool TickRoundContent(this OnlineArcadeRunActor actor,OnlineArcadeBattleRuntime b,float delta)
        {
            if(!RoundContentActive(b)||delta<=0||float.IsNaN(delta)||float.IsInfinity(delta))return false;var p=ContentTuning(b);b.ContentClock.Advance(delta,false);b.ContentAreas.Clear();
            TickVortexBarrels(b,delta);
            foreach(var player in b.Players.Values){player.ContentSlowRemaining=Math.Max(0,player.ContentSlowRemaining-delta);player.ContentBumperCooldown=Math.Max(0,player.ContentBumperCooldown-delta);}
            if(p.EventKind>0&&!b.RoundBossEncounter)
            {
                double now=b.ContentClock.Elapsed;long cycle=(long)(now/p.EventPeriod);
                if(b.ContentEventTargetCycle!=cycle)
                {b.ContentEventTargetCycle=cycle;var target=b.Players.Values.Where(v=>v.Alive&&!v.InTreasureRoom).OrderBy(v=>v.PlayerId).FirstOrDefault();if(target!=null){b.ContentEventTargetX=target.PositionX;b.ContentEventTargetY=target.PositionZ;}}
                ArcadeRoundContentRules.EventCenter(p.EventKind,now,0,0,p.EventRadius*2,p.EventMoveSpeed,out float x,out float y);
                bool warning=ArcadeRoundContentRules.Phase(now,p.EventPeriod,p.EventWarning)!=1;
                bool firstActive=!warning&&b.ContentEventCycle!=cycle;if(firstActive)b.ContentEventCycle=cycle;
                if(p.EventKind==3){x=b.ContentEventTargetX;y=b.ContentEventTargetY;}
                bool segment=p.EventKind==4;float ex=x,ey=y,radius=p.EventRadius;
                if(segment){x=cycle%2==0?-ArenaHalfWidth(b):0;y=cycle%2==0?0:-ArenaHalfHeight(b);ex=-x;ey=-y;radius*=.12f;}
                if(p.EventKind!=3||warning||now%p.EventPeriod<p.EventWarning*2)
                    b.ContentAreas.Add(new ArcadeContentAreaSnapshot(-100,p.EventKind,x,y,radius,warning,ex,ey,segment));
                if(p.EventKind==6&&!warning)
                    foreach(var drop in b.Drops.Values.ToArray())
                        if(ArcadeRoundContentRules.TouchesCircle(drop.PositionX,drop.PositionZ,0,x,y,p.EventRadius))b.Drops.Remove(drop.RuntimeId);
                if(p.EventKind==2&&!warning)
                    foreach(var enemy in b.Enemies.Values)
                        if(ArcadeRoundContentRules.TouchesCircle(enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,x,y,p.EventRadius))enemy.ContentStunUntil=now+p.SkillWarning;
            if(p.EventKind==3&&firstActive)DamageContentExplosionEnemies(actor,b,x,y,p.EventRadius,p.EventDamage);
                if(p.EventKind==10)
                {
                    var members=b.Players.Values.Where(v=>v.Alive&&!v.InTreasureRoom).OrderBy(v=>v.PlayerId).ToArray();bool pressed=members.Length>0;
                    for(int i=0;i<members.Length;i++)
                    {float padX=(i-(members.Length-1)*.5f)*p.EventRadius;bool occupied=members.Any(v=>ArcadeRoundContentRules.TouchesCircle(v.PositionX,v.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,padX,0,ServerConfig.Instance.ArcadeRule.PlayerRadius));pressed&=occupied;
                        b.ContentAreas.Add(new ArcadeContentAreaSnapshot(-i-1,10,padX,0,ServerConfig.Instance.ArcadeRule.PlayerRadius,!occupied));}
                    if(pressed&&!warning&&b.ContentClock.TryHit(long.MinValue,p.EventPeriod))DamageContentEnemies(actor,b,0,0,p.EventRadius*p.SkillMagnitude,p.EventDamage);
                }
                if(!warning&&p.EventKind!=10)foreach(var player in b.Players.Values.Where(v=>v.Alive&&!v.InTreasureRoom).ToArray())
                {
                    bool contact=segment?ArcadeRoundContentRules.TouchesSegment(player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,x,y,ex,ey,radius):
                        ArcadeRoundContentRules.TouchesCircle(player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,x,y,radius);
                    if(!contact)continue;
                    if(p.EventKind==8){player.ContentSlowRemaining=Math.Max(player.ContentSlowRemaining,p.SkillWarning);continue;}
                    if(p.EventKind==5)continue;
                    if(p.EventKind==9)
                    {
                        if(player.ContentBumperCooldown>0)continue;
                        float nx=-player.PositionX,ny=-player.PositionZ;
                        if(DistanceSquared(nx,ny,player.PositionX,player.PositionZ)<p.EventRadius*p.EventRadius*4){nx=0;ny=p.EventRadius*p.SkillMagnitude;}
                        ResolveBattlePosition(b,ref nx,ref ny,player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,EffectiveObstacles(b),IslandFor(b));
                        player.PositionX=nx;player.PositionZ=ny;player.IceVelocityX=player.IceVelocityZ=0;player.ContentBumperCooldown=p.EventPeriod;
                    }
                    else if((p.EventKind!=3||firstActive)&&b.ContentClock.TryHit(player.PlayerId,p.EventWarning))
                        actor.DamagePlayerCore(b,player,p.EventKind==2?player.MaxHealth:ArcadeRoundContentRules.Damage(player.MaxHealth,p.EventDamage),p.EventKind==2?SlayerMartHitKind.LethalMechanism:SlayerMartHitKind.Ordinary);
                }
            }
            foreach(var zone in b.DeathZones.ToArray())
            {
                zone.Clock.Advance(delta,false);zone.Remaining-=delta;zone.WarningRemaining=Math.Max(0,zone.WarningRemaining-delta);
                if(zone.Remaining<=0){b.DeathZones.Remove(zone);continue;}
                float radius=zone.Radius>0?zone.Radius:p.EventRadius;
                bool warning=zone.WarningRemaining>0;
                b.ContentAreas.Add(new ArcadeContentAreaSnapshot(zone.Id,zone.Kind,zone.X,zone.Y,radius,warning,zone.EndX,zone.EndY,zone.Segment));
                if(warning)continue;
                foreach(var player in b.Players.Values.Where(v=>v.Alive&&!v.InTreasureRoom).ToArray())
                {
                    bool contact=zone.Segment?ArcadeRoundContentRules.TouchesSegment(player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,zone.X,zone.Y,zone.EndX,zone.EndY,radius):
                        ArcadeRoundContentRules.TouchesCircle(player.PositionX,player.PositionZ,ServerConfig.Instance.ArcadeRule.PlayerRadius,zone.X,zone.Y,radius);
                    if(contact&&zone.Clock.TryHit(player.PlayerId,p.EventWarning))actor.ContentDamagePlayer(b,player,ArcadeRoundContentRules.Damage(player.MaxHealth,zone.DamageRatio));
                }
                if(zone.EnemyDamage)foreach(var enemy in b.Enemies.Values.ToArray())
                    if(ArcadeRoundContentRules.TouchesCircle(enemy.PositionX,enemy.PositionZ,ServerConfig.Instance.ArcadeRule.EnemyContactRadius,zone.X,zone.Y,radius)&&zone.Clock.TryHit(-enemy.RuntimeId,p.EventWarning))
                    {DamageEnemy(b,enemy,ArcadeRoundContentRules.Damage(enemy.MaxHealth,zone.DamageRatio));if(enemy.CurrentHealth<=0)actor.KillEnemy(b,enemy,true);}
            }
            return true;
        }
    }
}

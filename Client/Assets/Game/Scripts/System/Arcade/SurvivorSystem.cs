using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>独立幸存者战斗权威；不进入闯关节点和存档事务。</summary>
    public sealed class SurvivorSystem : AbstractSystem
    {
        // 本局依赖及运行实体只由本系统创建和释放。
        private SurvivorConfiguration config;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeRoundLayoutParameters layout;
        private EnemyConfigDefinition enemyConfig;
        private ArcadeWeaponDefinition weapon;
        private ArcadeDropDefinition dropVisual;
        private ArcadeObstacleDefinition[] obstacles;
        private IArcadeCombatViewPort view;
        private PlayerInputCtrl input;
        private SurvivorResultFileStore store;
        private readonly Dictionary<int,ArcadeEnemyRuntime> enemies=new();
        private readonly Dictionary<int,ArcadeProjectileRuntime> bullets=new();
        private readonly Dictionary<int,(Vector2 Position,long Experience)> drops=new();
        private Vector2 player, facing=Vector2.up;
        private float spawnClock,fireClock,hurtClock;
        private int identity,fireSequence;
        /// <summary>全敌人共享农场距离场，入口轮流生成以覆盖各个方向。</summary>
        private SurvivorNavigation navigation;
        private int spawnIndex;
        private long runId;
        private bool ownsView,saved;
        // 菜单暂停独立于升级暂停，选择强化不能解除菜单暂停。
        private bool menuPaused;
        public string LastError {get;private set;}
        private SurvivorModel Model=>this.GetModel<SurvivorModel>();
        /// <summary>复用农场权威尺寸，不能退回旧竞技场的小边界。</summary>
        private ArcadeEarlyStageParameters Stage => catalog.EarlyStages.FirstOrDefault(x => x.ArenaConfigNumber == layout.ArenaConfigNumber);
        private float HalfWidth => Stage?.HalfWidth ?? catalog.Rule.ArenaHalfWidth;
        private float HalfHeight => Stage?.HalfHeight ?? catalog.Rule.ArenaHalfHeight;
        protected override void OnInit(){input=new PlayerInputCtrl();view=this.GetUtility<IArcadeCombatViewPort>();LifeCycle.RegisterUpdate(Tick);}
        protected override void OnDeinit(){LifeCycle.UnregisterUpdate(Tick);Stop();input?.Dispose();}
        /// <summary>准备配置和独立结果文件；错误发生时拒绝进入地图。</summary>
        public bool TryPrepare(out string error)
        {
            error=null;
            try {
                var provider=this.GetUtility<IRuntimeConfigProvider>();
                if(!provider.TryGetSurvivor(out config,out error)||!provider.TryGetArcadeCatalog(out catalog,out error)||!provider.TryGetEnemy(config.EnemyId,out enemyConfig,out error))return false;
                layout=catalog.RoundLayouts.Single(x=>x.Id==config.LayoutId);
                weapon=catalog.Weapons.Single(x=>x.Id==config.WeaponId);
                dropVisual=catalog.Drops.Single(x=>x.Id=="arcade.drop.gold.small");
                obstacles=layout.Obstacles.Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight,o.BlocksProjectiles)).ToArray();
                store=new SurvivorResultFileStore(1);
                return store.TryRead(out _,out error);
            }catch(Exception ex){error=ex.Message;return false;}
        }
        /// <summary>地图已加载后创建本局；资源准备完成之前不推进时间。</summary>
        public bool TryStart(out string error)
        {
            if(Model.Active){error="幸存者局已经开始";return false;}
            if(this.GetModel<MapSceneModel>().Status.Value!=MapLoadStatus.Loaded){error="战场尚未加载完成";return false;}
            if(!TryPrepare(out error))return false;
            Stop();var m=Model;m.State=new SurvivorRunState(config.InitialExperienceThreshold,config.ExperienceThresholdGrowth);
            m.Health=m.MaximumHealth=config.PlayerMaxHealth;m.Kills=0;m.Score=0;m.Result=null;m.Active=true;m.Paused=false;
            runId=DateTime.UtcNow.Ticks;saved=false;menuPaused=false;identity=fireSequence=0;spawnClock=fireClock=hurtClock=0;LastError=null;
            navigation=new SurvivorNavigation(HalfWidth,HalfHeight,catalog.Rule.EnemyContactRadius,obstacles);spawnIndex=0;
            player=Resolve(new Vector2(layout.SpawnX,layout.SpawnY),catalog.Rule.PlayerRadius);
            try{ownsView=true;view.Begin(layout.ArenaConfigNumber,HalfWidth,HalfHeight,obstacles);view.SetRoundLayout(layout);view.SetPlayerWeapon(weapon);input.Enable();Publish();return true;}
            catch(Exception ex){Stop();error=ex.Message;return false;}
        }
        /// <summary>释放现场，不修改闯关进度；正常退出必须先结算。</summary>
        public void Stop(){input?.Disable();if(ownsView)view?.End();ownsView=false;enemies.Clear();bullets.Clear();drops.Clear();Model.Active=false;Model.Paused=false;Model.Choices=Array.Empty<int>();}
        /// <summary>失败可以用同一结果重试，不覆盖本局统计。</summary>
        public bool TryFinish(out string error)
        {
            error=null;var m=Model;if(m.State==null){error="当前没有幸存者局";return false;}
            if(m.Result==null){m.State.Finish();m.Paused=true;input.Disable();m.Choices=Array.Empty<int>();m.Result=new SurvivorResultData{RunId=runId,Score=m.Score,SurvivalSeconds=m.State.ElapsedSeconds,Kills=m.Kills,Level=m.State.Level,Outcome=m.Health<=0?"failed":"abandoned"};}
            if(!saved)saved=store.TrySave(m.Result,out error);LastError=error;Publish();return saved;
        }
        /// <summary>返回当前等级及下一等级的累计加成，只读查询供强化卡片展示。</summary>
        public bool TryGetUpgradeDetails(int id,out int level,out double currentBonus,out double nextBonus,out bool percentage)
        {
            level=0;currentBonus=nextBonus=0;percentage=false;
            if(config==null || Model.State==null || id<1 || id>config.Upgrades.Count)return false;
            var definition=config.Upgrades[id-1];level=Model.State.UpgradeLevel(id);
            currentBonus=definition.BonusAtLevel(level);nextBonus=definition.BonusAtLevel(level+1);
            percentage=definition.Kind=="Damage" || definition.Kind=="FireRate" || definition.Kind=="MoveSpeed";
            return true;
        }
        public string GetUpgradeName(int id){if(config==null||id<1||id>config.Upgrades.Count)return "未知强化";return config.Upgrades[id-1].Kind switch{"Damage"=>"伤害提升","FireRate"=>"射速提升","Health"=>"生命提升","MoveSpeed"=>"移速提升","Pickup"=>"拾取范围提升",_=>"强化"};}
        /// <summary>菜单暂停不清空升级队列，恢复时仍先完成升级。</summary>
        public void SetPaused(bool paused){if(!Model.Active||Model.State.Finished)return;menuPaused=paused;Model.Paused=paused||Model.State.PendingChoices>0;Publish();}
        public bool Choose(int revision,int upgradeId)
        {
            var m=Model;if(!m.Active||!m.State.Choose(revision,upgradeId))return false;
            int maximum=config.PlayerMaxHealth+(int)Bonus("Health");m.Health+=maximum-m.MaximumHealth;m.MaximumHealth=maximum;Offer();Publish();return true;
        }
        private double Bonus(string kind){double value=0;for(int i=0;i<config.Upgrades.Count;i++)if(config.Upgrades[i].Kind==kind)value+=config.Upgrades[i].BonusAtLevel(Model.State.UpgradeLevel(i+1));return value;}
        private void Offer(){var m=Model;m.Choices=m.State.Offer(Enumerable.Range(1,config.Upgrades.Count).Where(id=>config.Upgrades[id-1].MaxLevel==0||m.State.UpgradeLevel(id)<config.Upgrades[id-1].MaxLevel).ToArray(),unchecked((int)runId+m.State.ChoiceRevision));m.Paused=menuPaused||m.State.PendingChoices>0;}
        private Vector2 Resolve(Vector2 position,float radius)=>ArcadeCombatSystem.ResolvePosition(position,radius,HalfWidth,HalfHeight,obstacles);
        private void Publish(){this.SendEvent<SurvivorChangedEvent>();}
        /// <summary>暂停时不推进敌人、子弹、刷怪或升级时钟。</summary>
        private void Tick()
        {
            var m=Model;if(!m.Active||m.State.Finished)return;
            if(!string.IsNullOrEmpty(view.RoundLayoutLoadError)){if(LastError!=view.RoundLayoutLoadError){LastError=view.RoundLayoutLoadError;m.Paused=true;Publish();}return;}
            if(!view.IsRoundLayoutReady||m.Paused||this.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing||UIHelper.HasUIOpen)return;
            if(Time.deltaTime<=0)return;
            float dt=Mathf.Min(Time.deltaTime,.05f);view.TickPlayerFeedback(dt);m.State.Advance(dt,false);spawnClock-=dt;fireClock-=dt;hurtClock-=dt;
            player=Resolve(player+input.MoveInput*(float)(config.PlayerMoveSpeed*(1+Bonus("MoveSpeed")))*dt,catalog.Rule.PlayerRadius);
            view.SetPlayer(player,facing,true,hurtClock>0);
            if(view.TryGetPlayerPosition(out var actualPlayer))player=Resolve(actualPlayer,catalog.Rule.PlayerRadius);
            if(spawnClock<=0){Spawn();spawnClock=SurvivorCombatRules.SpawnInterval(m.State.ElapsedSeconds,config.SpawnIntervalSeconds,config.MinimumSpawnIntervalSeconds,config.SpawnIntervalReductionPerMinute);}
            navigation.SetTarget(player);
            foreach(var e in enemies.Values){e.Position=navigation.Move(e.Position,player,e.Speed*dt);if(hurtClock<=0&&Vector2.Distance(e.Position,player)<=catalog.Rule.PlayerRadius+catalog.Rule.EnemyContactRadius){m.Health=Math.Max(0,m.Health-e.Damage);hurtClock=catalog.Rule.ContactInvulnerabilitySeconds;}}
            Fire();AdvanceBullets(dt);
            foreach(var pair in drops.ToArray())if(Vector2.Distance(player,pair.Value.Position)<=config.PickupRadius+Bonus("Pickup")){m.State.AddExperience(pair.Value.Experience);drops.Remove(pair.Key);view.RemoveDrop(pair.Key);}
            Offer();view.SetPlayer(player,facing,true,hurtClock>0);view.SetPlayerHealth(m.Health,m.MaximumHealth);
            foreach(var e in enemies.Values)view.SetEnemy(e.Id,e.ConfigId,e.Position,e.Health,e.MaxHealth);
            foreach(var b in bullets.Values)view.SetProjectile(b.Id,false,b.Position,b.Velocity.normalized,weapon.ProjectileVisualResourceName,true);
            foreach(var d in drops)view.SetDrop(d.Key,dropVisual,d.Value.Position);
            if(m.Health<=0)TryFinish(out _);else Publish();
        }
        /// <summary>按入口轮转与存活预算生成敌人，避免贴身生成。</summary>
        private void Spawn()
        {
            if(SurvivorCombatRules.SpawnBudget(enemies.Count,config.MaxAliveEnemies,1)==0)return;
            for(int attempt=0;attempt<layout.Entries.Count;attempt++){
                var entry=layout.Entries[spawnIndex];spawnIndex=(spawnIndex+1)%layout.Entries.Count;
                var position=Resolve(new Vector2(entry.X,entry.Y),catalog.Rule.EnemyContactRadius);if(Vector2.Distance(position,player)<config.MinimumSpawnDistance)continue;
                double minutes=Model.State.ElapsedSeconds/60;int health=(int)Math.Min(int.MaxValue,enemyConfig.MaxHP*(1+minutes*config.EnemyHealthGrowthPerMinute));
                var e=new ArcadeEnemyRuntime{Id=++identity,ConfigId=config.EnemyId,Position=position,Health=health,MaxHealth=health,Damage=(int)Math.Min(int.MaxValue,enemyConfig.AttackDamage*(1+minutes*config.EnemyDamageGrowthPerMinute)),Speed=enemyConfig.MoveSpeed*(float)(1+minutes*config.EnemySpeedGrowthPerMinute)};enemies.Add(e.Id,e);break;
            }
        }
        /// <summary>只对射程内无遮挡的最近目标发射，弹丸保留发射时伤害。</summary>
        private void Fire()
        {
            if(fireClock>0||bullets.Count>=config.MaxProjectiles)return;var candidates=enemies.Values.ToArray();
            long target=SurvivorCombatRules.SelectTarget(player.x,player.y,config.AimRange,candidates.Select(e=>(long)e.Id).ToArray(),candidates.Select(e=>e.Position.x).ToArray(),candidates.Select(e=>e.Position.y).ToArray(),candidates.Select(e=>!Blocked(player,e.Position)).ToArray());
            if(target==0)return;facing=(enemies[(int)target].Position-player).normalized;fireClock=weapon.FireIntervalSeconds/(float)(1+Bonus("FireRate"));
            var b=new ArcadeProjectileRuntime{Id=++identity,Position=player,Velocity=facing*weapon.ProjectileSpeed,Damage=(int)Math.Min(int.MaxValue,weapon.Damage*(1+Bonus("Damage"))),RemainingSeconds=weapon.ProjectileLifetimeSeconds};bullets.Add(b.Id,b);view.SetPlayerFireSequence(++fireSequence,fireClock);
        }
        /// <summary>步进弹丸线段检查实体阻挡，防止穿越农场围栏。</summary>
        private bool Blocked(Vector2 from,Vector2 to){int steps=Math.Max(1,Mathf.CeilToInt(Vector2.Distance(from,to)/.1f));for(int i=0;i<=steps;i++){var p=Vector2.Lerp(from,to,(float)i/steps);foreach(var o in obstacles)if(o.BlocksProjectiles&&Mathf.Abs(p.x-o.Center.x)<=o.HalfExtents.x&&Mathf.Abs(p.y-o.Center.y)<=o.HalfExtents.y)return true;}return false;}
        /// <summary>按弹道线段结算击杀，掉落满额时合并经验而不丢失。</summary>
        private void AdvanceBullets(float dt)
        {
            foreach(var b in bullets.Values.ToArray()){
                var old=b.Position;b.Position+=b.Velocity*dt;b.RemainingSeconds-=dt;bool remove=b.RemainingSeconds<=0||Blocked(old,b.Position);
                if(!remove)foreach(var e in enemies.Values.ToArray()){
                    var segment=b.Position-old;float t=segment.sqrMagnitude==0?0:Mathf.Clamp01(Vector2.Dot(e.Position-old,segment)/segment.sqrMagnitude);
                    if(Vector2.Distance(e.Position,old+segment*t)>catalog.Rule.EnemyContactRadius)continue;
                    e.Health=Math.Max(0,e.Health-b.Damage);remove=true;if(e.Health==0){enemies.Remove(e.Id);view.RemoveEnemy(e.Id,true);Model.Kills++;Model.Score=Math.Min(long.MaxValue-config.ScorePerKill,Model.Score)+config.ScorePerKill;
                        if(drops.Count<config.MaxDrops)drops.Add(++identity,(e.Position,config.ExperiencePerKill));else {int id=drops.OrderBy(x=>(x.Value.Position-e.Position).sqrMagnitude).First().Key;var d=drops[id];drops[id]=(d.Position,Math.Min(long.MaxValue-config.ExperiencePerKill,d.Experience)+config.ExperiencePerKill);}}
                    break;
                }
                if(remove){bullets.Remove(b.Id);view.RemoveProjectile(b.Id);}
            }
        }
    }
}

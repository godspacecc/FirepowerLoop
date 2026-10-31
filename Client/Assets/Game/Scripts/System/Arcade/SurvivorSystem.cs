using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;
namespace Game
{
    /// <summary>独立幸存者战斗权威；不进入闯关节点和存档事务。</summary>
    public sealed partial class SurvivorSystem : AbstractSystem
    {
        // 本局依赖及运行实体只由本系统创建和释放。
        private SurvivorConfiguration config;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeRoundLayoutParameters layout;
        private EnemyConfigDefinition enemyConfig;
        private ArcadeWeaponDefinition weapon;
        /// <summary>无人机只复用既有表现配置，计时和伤害由幸存者独立规则拥有。</summary>
        private ArcadePowerupDefinition droneVisual;
        private Vector2 dronePosition,droneFacing=Vector2.up;
        private float droneFireClock;
        /// <summary>本局经验掉落独立随机流，不影响刷怪或强化候选。</summary>
        private readonly System.Random experienceRandom=new System.Random();
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
        /// <summary>入口确认完整战场后才提交；用于区分正常等待和开局后的场地丢失。</summary>
        private bool battlePrepared;
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
                if(!PrepareEnemyRoster(out error))return false;
                layout=catalog.RoundLayouts.Single(x=>x.Id==config.LayoutId);
                weapon=catalog.Weapons.Single(x=>x.Id==config.WeaponId);
                var support=catalog.SupportVisuals.Single(x=>x.ProductId=="arcade.support.drone");
                droneVisual=new ArcadePowerupDefinition("survivor.drone",global::ArcadePowerupBehavior.Drone,support.ActiveVisualPrefabAddress,false,new Vector3(0,1.6f,0),0,0,0,0,0,0);
                // 经验模型由幸存者独立配置提供，不再借用金币掉落。
                obstacles=layout.Obstacles.Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight,o.BlocksProjectiles)).ToArray();
                // 在交接战场前验证所有入口，不允许运行中退回入口补生。
                for(int i=0;i<layout.Entries.Count;i++)if(!FirepowerLoop.Arcade.ArcadeOutsideRouteRules.TryStart(layout,i,HalfWidth,HalfHeight,catalog.Rule.EnemyContactRadius,out _))
                    throw new InvalidOperationException("幸存者入口没有合法场外通道，请检查地图配置");
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
            m.Loadout=new SurvivorLoadout(config.WeaponSlots,"Rifle");
            m.Health=m.MaximumHealth=config.PlayerMaxHealth;m.Kills=0;m.Score=0;m.Result=null;m.Active=true;m.Paused=false;
            runId=DateTime.UtcNow.Ticks;saved=false;menuPaused=false;identity=fireSequence=0;spawnClock=fireClock=hurtClock=0;LastError=null;
            navigation=new SurvivorNavigation(HalfWidth,HalfHeight,catalog.Rule.EnemyContactRadius,obstacles);PrepareSizedNavigation();spawnIndex=0;
            player=Resolve(new Vector2(layout.SpawnX,layout.SpawnY),catalog.Rule.PlayerRadius);
            dronePosition=player+new Vector2(catalog.Rule.Drone.OffsetX,catalog.Rule.Drone.OffsetZ);droneFacing=Vector2.up;droneFireClock=0;
            try{ownsView=true;view.Begin(layout.ArenaConfigNumber,HalfWidth,HalfHeight,obstacles);view.SetRoundLayout(layout);view.SetPlayerWeapon(weapon);input.Enable();Publish();return true;}
            catch(Exception ex){Stop();error=ex.Message;return false;}
        }
        /// <summary>释放现场，不修改闯关进度；正常退出必须先结算。</summary>
        public void Stop(){battlePrepared=false;input?.Disable();if(ownsView)view?.End();ownsView=false;enemies.Clear();bullets.Clear();drops.Clear();sizedNavigation.Clear();Model.Active=false;Model.Paused=false;Model.Loadout=null;Model.Choices=Array.Empty<int>();}
        /// <summary>加载页释放之前复核完整战场；未准备好的现场不得交给玩家。</summary>
        public void ConfirmBattlePrepared()
        {
            if(!Model.Active||view==null||!view.IsActive||!view.IsRoundLayoutReady||!string.IsNullOrEmpty(view.RoundLayoutLoadError))
                throw new InvalidOperationException(view?.RoundLayoutLoadError??"幸存者战场尚未准备完成");
            battlePrepared=true;
        }
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
        /// <summary>拾取强化卡读取实际半径及下一层半径，与战斗拾取判定使用同一基础值和累计加成。</summary>
        public bool TryGetPickupUpgradeDetails(int id,out double currentRadius,out double nextRadius)
        {
            currentRadius=nextRadius=0;
            if(!TryGetUpgradeDetails(id,out _,out var currentBonus,out var nextBonus,out _)||config.Upgrades[id-1].Kind!="Pickup")return false;
            currentRadius=config.PickupRadius+Bonus("Pickup");nextRadius=currentRadius+nextBonus-currentBonus;return true;
        }
        public string GetUpgradeName(int id){if(config==null||id<1||id>config.Upgrades.Count)return "未知强化";return config.Upgrades[id-1].Kind switch{"Damage"=>"伤害提升","FireRate"=>"射速提升","Health"=>"生命提升","MoveSpeed"=>"移速提升","Pickup"=>"拾取范围提升",_=>"强化"};}
        /// <summary>武器卡片读当前及下一等级，未获取时当前参数为空。</summary>
        public bool TryGetWeaponUpgrade(int id,out string kind,out int level,out SurvivorWeaponLevelDefinition current,out SurvivorWeaponLevelDefinition next)
        {
            kind=null;level=0;current=next=null;
            if(config==null||Model.Loadout==null||id<1||id>config.Upgrades.Count||!config.Upgrades[id-1].IsWeapon)return false;
            kind=config.Upgrades[id-1].Kind;level=Model.Loadout.LevelOf(kind);
            if(!Model.Loadout.CanUpgrade(kind,config.MaximumWeaponLevel(kind)))return false;
            current=level>0?config.WeaponLevel(kind,level):null;next=config.WeaponLevel(kind,level+1);return true;
        }
        /// <summary>菜单暂停不清空升级队列，恢复时仍先完成升级。</summary>
        public void SetPaused(bool paused){if(!Model.Active||Model.State.Finished)return;menuPaused=paused;Model.Paused=paused||Model.State.PendingChoices>0;Publish();}
        public bool Choose(int revision,int upgradeId)
        {
            var m=Model;if(!m.Active||config==null||upgradeId<1||upgradeId>config.Upgrades.Count)return false;
            var definition=config.Upgrades[upgradeId-1];
            if(definition.IsWeapon&&(m.Loadout==null||!m.Loadout.CanUpgrade(definition.Kind,config.MaximumWeaponLevel(definition.Kind))))return false;
            if(!m.State.Choose(revision,upgradeId))return false;
            if(definition.IsWeapon)
            {
                // 首次获取从当前玩家身边出现；升级不重置正在飞行的无人机。
                if(definition.Kind=="Drone"&&m.Loadout.LevelOf("Drone")==0){dronePosition=player+new Vector2(catalog.Rule.Drone.OffsetX,catalog.Rule.Drone.OffsetZ);droneFacing=facing;droneFireClock=0;}
                m.Loadout.TryUpgrade(definition.Kind,config.MaximumWeaponLevel(definition.Kind));
            }
            int maximum=config.PlayerMaxHealth+(int)Bonus("Health");m.Health+=maximum-m.MaximumHealth;m.MaximumHealth=maximum;Offer();Publish();return true;
        }
        private double Bonus(string kind){double value=0;for(int i=0;i<config.Upgrades.Count;i++)if(config.Upgrades[i].Kind==kind)value+=config.Upgrades[i].BonusAtLevel(Model.State.UpgradeLevel(i+1));return value;}
        /// <summary>已有武器只提供升级，满槽/满级过滤；首次选择保底无人机。</summary>
        private void Offer()
        {
            var m=Model;var ids=Enumerable.Range(1,config.Upgrades.Count).Where(id=>{
                var u=config.Upgrades[id-1];return u.IsWeapon?m.Loadout!=null&&m.Loadout.CanUpgrade(u.Kind,config.MaximumWeaponLevel(u.Kind)):u.MaxLevel==0||m.State.UpgradeLevel(id)<u.MaxLevel;
            }).ToArray();
            int preferred=m.State.ChoiceRevision==0?ids.FirstOrDefault(id=>config.Upgrades[id-1].Kind=="Drone"&&m.Loadout.LevelOf("Drone")==0):0;
            m.Choices=m.State.OfferWeighted(ids,ids.Select(id=>config.Upgrades[id-1].Weight).ToArray(),unchecked((int)runId+m.State.ChoiceRevision),preferred);
            m.Paused=menuPaused||m.State.PendingChoices>0;
        }
        private Vector2 Resolve(Vector2 position,float radius)=>ArcadeCombatSystem.ResolvePosition(position,radius,HalfWidth,HalfHeight,obstacles);
        private void Publish(){this.SendEvent<SurvivorChangedEvent>();}
        /// <summary>暂停时不推进敌人、子弹、刷怪或升级时钟。</summary>
        private void Tick()
            => TickFrame(Time.deltaTime);

        /// <summary>按本帧实际时间推进；单独接收时间以便在非运行态准确验证移动及暂停规则。</summary>
        private void TickFrame(float frameSeconds)
        {
            var m=Model;if(!m.Active||m.State.Finished)return;
            if(battlePrepared&&(!view.IsActive||!view.IsRoundLayoutReady||!string.IsNullOrEmpty(view.RoundLayoutLoadError)))
            {
                // 已交接场地失效不能永久静默等待，也不把加载故障保存为一局结算。
                if(string.IsNullOrEmpty(LastError))
                {
                    LastError=view.RoundLayoutLoadError??"幸存者战场已失效，请返回主菜单重新进入";
                    m.Paused=true;input.Disable();Publish();
                }
                return;
            }
            if(!string.IsNullOrEmpty(view.RoundLayoutLoadError)){if(LastError!=view.RoundLayoutLoadError){LastError=view.RoundLayoutLoadError;m.Paused=true;Publish();}return;}
            if(!view.IsRoundLayoutReady||m.Paused||this.GetModel<AppStateModel>().CurrentState.Value!=AppState.Playing||UIHelper.HasUIOpen)return;
            if(frameSeconds<=0)return;
            float dt=Mathf.Min(frameSeconds,.05f);view.TickPlayerFeedback(dt);m.State.Advance(dt,false);spawnClock-=dt;fireClock-=dt;hurtClock-=dt;
            player=Resolve(player+input.MoveInput*(float)(config.PlayerMoveSpeed*(1+Bonus("MoveSpeed")))*dt,catalog.Rule.PlayerRadius);
            view.SetPlayer(player,facing,true,hurtClock>0);
            if(view.TryGetPlayerPosition(out var actualPlayer))player=Resolve(actualPlayer,catalog.Rule.PlayerRadius);
            if(spawnClock<=0){Spawn();if(m.Paused)return;spawnClock=SurvivorCombatRules.SpawnInterval(m.State.ElapsedSeconds,config.SpawnIntervalSeconds,config.MinimumSpawnIntervalSeconds,config.SpawnIntervalReductionPerMinute);}
            navigation.SetTarget(player);
            TickEnemies(dt);
            Fire();TickDrone(dt);AdvanceBullets(dt);
            foreach(var pair in drops.ToArray())if(Vector2.Distance(player,pair.Value.Position)<=config.PickupRadius+Bonus("Pickup")){m.State.AddExperience(pair.Value.Experience);drops.Remove(pair.Key);view.RemoveDrop(pair.Key);}
            Offer();view.SetPlayerCombatState(facing,hurtClock>0);view.SetPlayerHealth(m.Health,m.MaximumHealth);
            foreach(var e in enemies.Values)
            {
                view.SetEnemy(e.Id,e.ConfigId,e.Position,e.Health,e.MaxHealth,false,e.AttackSequence);
                view.SetEnemyScale(e.Id,Shape(e.ConfigId)?.VisualScale??Vector3.one);
                if(FirepowerLoop.Arcade.ArcadeMeleeRules.IsZombie(e.ConfigId))
                    view.SetEnemyMelee(e.Id,e.MeleeWindupSeconds,e.MeleeWindupRemaining,e.MeleeRecoveryRemaining,(player-e.Position).normalized);
            }
            foreach(var b in bullets.Values)
                if(b.SourceWeaponId=="survivor.drone")view.SetDroneProjectile(b.Id,b.Position,b.Velocity.normalized,weapon.ProjectileVisualResourceName,droneVisual.VisualOffset.y);
                else view.SetProjectile(b.Id,false,b.Position,b.Velocity.normalized,weapon.ProjectileVisualResourceName,true);
            foreach(var d in drops)view.SetExperienceDrop(d.Key,config.ExperienceVisualPrefabAddress,d.Value.Position,d.Value.Experience);
            if(m.Health<=0)TryFinish(out _);else Publish();
        }
        /// <summary>按入口轮转持续生成敌人，不限制存活数量，避免贴身生成。</summary>
        private void Spawn()
        {
            if(enemyRoster==null&&!PrepareEnemyRoster(out var rosterError)){LastError=rosterError;Model.Paused=true;input.Disable();Publish();return;}
            var choice=enemyRoster.Select(Model.State.ElapsedSeconds,(float)Math.Min(enemyRandom.NextDouble(),.99999994));
            var selected=enemyConfigs[choice.EnemyId];
            float radius=catalog.Rule.EnemyContactRadius*choice.BodyRadiusMultiplier;
            for(int attempt=0;attempt<layout.Entries.Count;attempt++){
                spawnIndex=(spawnIndex+1)%layout.Entries.Count;
                // 幸存者使用同一配置走廊，入场完成后才交给场内距离场。
                if(!FirepowerLoop.Arcade.ArcadeOutsideRouteRules.TryStart(layout,(spawnIndex+layout.Entries.Count-1)%layout.Entries.Count,
                    HalfWidth,HalfHeight,radius,out var outsideEntry))
                {
                    // 大体型走不通时尝试其它入口，不因某条窄路冻结本局。
                    if(radius>catalog.Rule.EnemyContactRadius+.0001f)continue;
                    // 准备后配置失效也不能静默在入口补生，冻结本局并保留明确故障。
                    LastError="幸存者入口没有合法场外通道，请检查地图配置";
                    Model.Paused=true;input.Disable();Publish();return;
                }
                // 场地边界之外仍可能入镜，沿同一安全走廊后移；无不可见起点时尝试下一入口。
                if(!FirepowerLoop.Arcade.ArcadeOutsideRouteRules.TryExtendToHidden(layout,outsideEntry,radius,
                    (x,z)=>view.IsArenaSpawnHidden(new Vector2(x,z),radius),out outsideEntry))continue;
                var position=new Vector2(outsideEntry.StartX,outsideEntry.StartZ);
                if(Vector2.Distance(position,player)<config.MinimumSpawnDistance)continue;
                double minutes=Model.State.ElapsedSeconds/60;int health=(int)Math.Min(int.MaxValue,selected.MaxHP*choice.HealthMultiplier*(1+minutes*config.EnemyHealthGrowthPerMinute));
                var e=new ArcadeEnemyRuntime{Id=++identity,ConfigId=choice.EnemyId,AttackKind=selected.AttackKind,Position=position,OutsideEntry=outsideEntry,Health=health,MaxHealth=health,Damage=(int)Math.Min(int.MaxValue,selected.AttackDamage*choice.DamageMultiplier*(1+minutes*config.EnemyDamageGrowthPerMinute)),Speed=selected.MoveSpeed*choice.SpeedMultiplier*(float)(1+minutes*config.EnemySpeedGrowthPerMinute)};enemies.Add(e.Id,e);break;
            }
        }
        /// <summary>只对画面内、射程内无遮挡的最近目标发射，弹丸保留发射时伤害。</summary>
        private void Fire()
        {
            if(fireClock>0||bullets.Count>=config.MaxProjectiles)return;var candidates=enemies.Values.ToArray();
            var level=config.WeaponLevel("Rifle",Model.Loadout.LevelOf("Rifle"));
            long target=SurvivorCombatRules.SelectTarget(player.x,player.y,Math.Min(config.AimRange,level.Range),candidates.Select(e=>(long)e.Id).ToArray(),candidates.Select(e=>e.Position.x).ToArray(),candidates.Select(e=>e.Position.y).ToArray(),candidates.Select(e=>view.IsArenaTargetVisible(e.Position)&&!Blocked(player,e.Position)).ToArray());
            if(target==0)return;facing=(enemies[(int)target].Position-player).normalized;fireClock=level.FireIntervalSeconds/(float)(1+Bonus("FireRate"));
            Shoot(player,facing,level,"Rifle");view.SetPlayerFireSequence(++fireSequence,fireClock);
        }
        /// <summary>常驻无人机优先保护玩家附近目标；升级暂停和结算时此入口不执行。</summary>
        private void TickDrone(float dt)
        {
            int levelNumber=Model.Loadout?.LevelOf("Drone")??0;if(levelNumber==0)return;
            var level=config.WeaponLevel("Drone",levelNumber);droneFireClock-=dt;
            dronePosition=Vector2.Lerp(dronePosition,player+new Vector2(catalog.Rule.Drone.OffsetX,catalog.Rule.Drone.OffsetZ),1-Mathf.Exp(-12*dt));
            var target=enemies.Values.Where(e=>e.Health>0&&view.IsArenaTargetVisible(e.Position)&&Vector2.Distance(dronePosition,e.Position)<=level.Range&&!Blocked(dronePosition,e.Position)).OrderBy(e=>(e.Position-player).sqrMagnitude).ThenBy(e=>e.Id).FirstOrDefault();
            if(target!=null){droneFacing=(target.Position-dronePosition).normalized;if(droneFireClock<=0&&bullets.Count<config.MaxProjectiles){Shoot(dronePosition,droneFacing,level,"Drone");droneFireClock=level.FireIntervalSeconds/(float)(1+Bonus("FireRate"));}}
            view.SetDrone(1,droneVisual,dronePosition,droneFacing,(int)ArcadeDronePhase.Following,1);
        }
        /// <summary>全部武器共享弹丸预算及命中入口，弹丸保留发射时伤害。</summary>
        private void Shoot(Vector2 origin,Vector2 direction,SurvivorWeaponLevelDefinition level,string kind)
        {
            var b=new ArcadeProjectileRuntime{Id=++identity,Position=origin,Velocity=direction*level.ProjectileSpeed,Damage=(int)Math.Min(int.MaxValue,level.Damage*(1+Bonus("Damage"))),RemainingSeconds=level.ProjectileLifetimeSeconds,SourceWeaponId=kind=="Drone"?"survivor.drone":config.WeaponId};bullets.Add(b.Id,b);
        }
        /// <summary>步进弹丸线段检查实体阻挡，防止穿越农场围栏。</summary>
        private bool Blocked(Vector2 from,Vector2 to){int steps=Math.Max(1,Mathf.CeilToInt(Vector2.Distance(from,to)/.1f));for(int i=0;i<=steps;i++){var p=Vector2.Lerp(from,to,(float)i/steps);foreach(var o in obstacles)if(o.BlocksProjectiles&&Mathf.Abs(p.x-o.Center.x)<=o.HalfExtents.x&&Mathf.Abs(p.y-o.Center.y)<=o.HalfExtents.y)return true;}return false;}
        /// <summary>仅将完全同坐标经验累加到同一晶体；满额时沿用既有合并保全经验机制。</summary>
        private void AddExperienceDrop(Vector2 position)
        {
            int existing=0;
            // Vector2的==带距离容差，必须使用Equals，不能合并旁边的不同落点。
            foreach(var pair in drops)if(pair.Value.Position.Equals(position)){existing=pair.Key;break;}
            if(existing==0&&drops.Count<config.MaxDrops)
            {
                drops.Add(++identity,(position,config.ExperiencePerKill));return;
            }
            if(existing==0)existing=drops.OrderBy(x=>(x.Value.Position-position).sqrMagnitude).First().Key;
            var drop=drops[existing];
            drops[existing]=(drop.Position,Math.Min(long.MaxValue-config.ExperiencePerKill,drop.Experience)+config.ExperiencePerKill);
        }
        /// <summary>按弹道线段结算击杀，同落点与满额掉落合并经验而不丢失。</summary>
        private void AdvanceBullets(float dt)
        {
            foreach(var b in bullets.Values.ToArray()){
                var old=b.Position;b.Position+=b.Velocity*dt;b.RemainingSeconds-=dt;bool remove=b.RemainingSeconds<=0||Blocked(old,b.Position);
                if(!remove)foreach(var e in enemies.Values.ToArray()){
                    var segment=b.Position-old;float t=segment.sqrMagnitude==0?0:Mathf.Clamp01(Vector2.Dot(e.Position-old,segment)/segment.sqrMagnitude);
                    if(Vector2.Distance(e.Position,old+segment*t)>EnemyRadius(e))continue;
                    e.Health=Math.Max(0,e.Health-b.Damage);remove=true;if(e.Health==0){enemies.Remove(e.Id);view.RemoveEnemy(e.Id,true);Model.Kills++;Model.Score=Math.Min(long.MaxValue-config.ScorePerKill,Model.Score)+config.ScorePerKill;
                        if(SurvivorCombatRules.ShouldDropExperience(config.ExperienceDropChance,Math.Min((float)experienceRandom.NextDouble(),.99999994f)))
                            AddExperienceDrop(e.OutsideEntry.Active?new Vector2(e.OutsideEntry.TargetX,e.OutsideEntry.TargetZ):e.Position);}
                    break;
                }
                if(remove){bullets.Remove(b.Id);view.RemoveProjectile(b.Id);}
            }
        }
    }
}

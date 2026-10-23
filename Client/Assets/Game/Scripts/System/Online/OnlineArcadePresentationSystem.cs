using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 把服务器街机快照投影到正式地图表现对象；只做插值、轻量本地移动预测和权威校正，不裁决战斗结果。
    /// </summary>
    public sealed partial class OnlineArcadePresentationSystem : AbstractSystem
    {
        /// <summary>预测、边界与镜头使用当前地图的独立尺寸，旧主题回退原规则。</summary>
        private FirepowerLoop.Arcade.ArcadeEarlyStageParameters EarlyStage=>catalog?.EarlyStages.FirstOrDefault(p=>p.ArenaConfigNumber==activeArena?.ArenaNumber);
        private float ArenaHalfWidth=>EarlyStage?.HalfWidth??catalog.Rule.ArenaHalfWidth;
        private float ArenaHalfHeight=>EarlyStage?.HalfHeight??catalog.Rule.ArenaHalfHeight;
        private const float SnapDistance = 2.5f;
        private const float DashSpeedMultiplier = 2.4f;
        private readonly Dictionary<long, int> enemyIds = new();
        private readonly Dictionary<long, int> projectileIds = new();
        private readonly Dictionary<long, int> dropIds = new();
        /// <summary>上一份权威快照的护盾次数；只用来识别挡伤视觉，不参与战斗判定。</summary>
        private readonly Dictionary<long, int> shieldChargesByPlayer = new();
        /// <summary>本场已观察的成功补给实例 ID；首次快照只建基线，离场清空，区域切换保留。</summary>
        private readonly HashSet<long> observedSupplyPickupIds = new();
        /// <summary>炸弹只播放权威成功记录，双人共用表现且不从库存差推断。</summary>
        private readonly OnlineBombPresentationState bombPresentation = new();
        /// <summary>电击只读命中收据，和死亡或血量变化独立消费。</summary>
        private readonly OnlineElectricHitPresentationState electricHitPresentation = new();
        /// <summary>武器瞬时反馈只消费服务器事实，不从弹丸移除猜测命中。</summary>
        private readonly OnlineWeaponFeedbackPresentationState weaponFeedbackPresentation = new();
        private readonly HashSet<long> seenIds = new();
        private IArcadeCombatViewPort view;
        private ArcadeConfigCatalogDefinition catalog;
        /// <summary>专属风暴视觉只读正式半径，不持有客户端伤害时钟。</summary>
        private FirepowerLoop.Arcade.ArcadeSlayerMartConfiguration stormVisualConfig;
        private ArcadeArenaDefinition activeArena;
        private FirepowerLoop.Arcade.ArcadeRoundLayoutParameters activeRoundLayout;
        private int activeRoundGeneration;
        private bool roundLoadPrompt;
        private Vector2 predictedIceVelocity;
        private IReadOnlyList<ArcadeObstacleDefinition> RoundObstacles => activeRoundLayout==null?activeArena.Obstacles:
            activeRoundLayout.Obstacles.Select(o=>new ArcadeObstacleDefinition(o.X,o.Y,o.HalfWidth,o.HalfHeight)).ToArray();
        private OnlineArcadeRunClientSnapshot latest = OnlineArcadeRunClientSnapshot.Empty();
        private Vector2 predictedLocalPosition;
        private bool hasPrediction;
        private FirepowerLoop.Arcade.ArcadeIslandRegion predictionIsland;
        private int activeBattleArenaNumber;
        /// <summary>区域代次与最近已应用的权威帧；切换会清空预测，旧帧不再投影。</summary>
        private bool presentedInTreasureRoom;
        private string presentedRoomId;
        private long lastAppliedBattleRevision = -1;
        private bool active;
        private bool transitionBusy;
        /// <summary>新轮快照等待黑幕遮挡，期间保留旧世界且不投影新角色位置。</summary>
        private bool roundTransitionPending;
        /// <summary>只证明该代次已真正替换表现世界；输入泵仍须另外校验资源及相机就绪。</summary>
        public bool IsRoundPresented(int generation)=>active&&!roundTransitionPending&&activeRoundGeneration==generation;
        /// <summary>本机本场已观察的权威攻击序列；首次进入只建立基线，不补播历史动作。</summary>
        private bool hasLocalFireBaseline;
        private int lastLocalFireSequence;
        private int transitionGeneration;
        private int nextPresentationId = 1;
        /// <summary>本机联机战斗会话的阶段提示记录；区域重建不重置，退出联机战场时清理。</summary>
        internal ArcadeStageNoticeState StageNotices { get; } = new();

        protected override void OnInit()
        {
            view = this.GetUtility<IArcadeCombatViewPort>();
            this.RegisterEvent<OnlineArcadeRunChangedEvent>(OnRunChanged);
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            transitionGeneration++;
            EndPresentation();
        }

        private void OnRunChanged(OnlineArcadeRunChangedEvent evt)
        {
            if (active && latest.RunId == evt.Snapshot.RunId && latest.Battle?.ArenaNumber == evt.Snapshot.Battle?.ArenaNumber && evt.Snapshot.Battle != null &&
                (evt.Snapshot.Battle.RoundGeneration < activeRoundGeneration || evt.Snapshot.Battle.RoundGeneration == activeRoundGeneration && evt.Snapshot.Battle.BattleRevision < lastAppliedBattleRevision)) return;
            latest = evt.Snapshot;
            if (latest.Phase is OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge &&
                latest.Battle != null)
            {
                if (active && activeBattleArenaNumber != latest.Battle.ArenaNumber) EndPresentation();
                if (!active && !transitionBusy) EnterArenaAsync(++transitionGeneration, latest.RunId,
                    latest.Battle.ArenaNumber).Forget();
                else if (active) ApplyAuthoritativeSnapshot(latest.Battle);
                return;
            }
            transitionGeneration++;
            EndPresentation();
            bool transit = latest.Phase == OnlineArcadeRunPhase.SafeCheckpoint;
            if (!transit && this.GetModel<MapSceneModel>().CurrentMapId == MapIds.Arcade && !transitionBusy)
                ReturnToMainMenuAsync(transitionGeneration).Forget();
        }

        /// <summary>地图加载完成后重新校验运行局、竞技场与代次，过期加载不会启动表现。</summary>
        private async UniTaskVoid EnterArenaAsync(int generation, long runId, int arenaNumber)
        {
            transitionBusy = true;
            try
            {
                var preparation = this.GetUtility<IGameplayResourcePreparation>();
                if (preparation != null && !await preparation.PrepareAsync(MapIds.Arcade,
                    () => generation == transitionGeneration && latest.RunId == runId && latest.Battle?.ArenaNumber == arenaNumber)) return;
                ArcadeSceneFlowResult result = await this.GetSystem<ArcadeSceneFlowSystem>().EnterArenaMapAsync();
                if (!result.Success) { Log.Error($"[OnlineArcadePresentationSystem] 联机街机地图加载失败：{result.Error}"); return; }
                if (generation != transitionGeneration || latest.RunId != runId || latest.Battle?.ArenaNumber != arenaNumber) return;
                if (!this.GetUtility<IRuntimeConfigProvider>().TryGetArcadeCatalog(out catalog, out string error))
                { Log.Error($"[OnlineArcadePresentationSystem] 联机街机配置不可用：{error}"); return; }
                view.BeginRoundTransition();
                while(!view.IsRoundTransitionCovered)
                {
                    await UniTask.NextFrame();
                    if(generation!=transitionGeneration||latest.RunId!=runId||latest.Battle?.ArenaNumber!=arenaNumber)return;
                }
                // 只在进入场地时投影本局出口，显示与接触意图共用服务器确认的路线。
                activeArena = ResolveArena(latest.Battle.ArenaConfigNumber > 0 ? latest.Battle.ArenaConfigNumber : arenaNumber)
                    .WithRouteVersion(latest.CasualRouteVersion);
                view.Begin(activeArena.ArenaNumber, ArenaHalfWidth, ArenaHalfHeight,
                    activeArena.Obstacles);
                active = true;
                activeBattleArenaNumber = arenaNumber;
                ApplyAuthoritativeSnapshot(latest.Battle);
            }
            catch (Exception exception)
            {
                Log.Error($"[OnlineArcadePresentationSystem] 启动联机战斗表现失败：{exception}");
                EndPresentation();
            }
            finally { transitionBusy = false; }
        }

        private async UniTaskVoid ReturnToMainMenuAsync(int generation)
        {
            transitionBusy = true;
            try
            {
                ArcadeSceneFlowResult result = await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
                if (generation == transitionGeneration && !result.Success)
                    Log.Error($"[OnlineArcadePresentationSystem] 联机街机返回主菜单失败：{result.Error}");
            }
            finally { transitionBusy = false; }
        }

        private void OnUpdate()
        {
            // 单人与联机共用表现端口，非活动联机系统不能停止单人持有的循环声。
            if (!active) return;
            view?.SetHerdActivityPaused(latest.BattlePaused||latest.Battle?.RoundLoading==true);
            if (latest.Battle == null || catalog == null || latest.BattlePaused)
            { view?.StopPlayerFireAudio(); return; }
            if(roundTransitionPending)
            {
                ApplyAuthoritativeSnapshot(latest.Battle);
                if(roundTransitionPending){view.StopPlayerFireAudio();return;}
            }
            if(latest.Battle.SlayerMart?.Active==true){view.StopPlayerFireAudio();TickSlayerMartPresentation();return;}
            if(!string.IsNullOrEmpty(view.RoundLayoutLoadError)||!string.IsNullOrEmpty(view.IslandRoundLoadError))
            {view.StopPlayerFireAudio();ShowRoundLoadRetry();return;}
            if(!view.IsRoundLayoutReady||!view.IsIslandRoundReady||presentedInTreasureRoom&&!view.IsIslandTreasureReady||latest.Battle.RoundLoading)
            {view.StopPlayerFireAudio();return;}
            view.EndRoundTransition();
            view.TickPlayerFeedback(Time.deltaTime);
            long selfId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            OnlineArcadeBattlePlayerSnapshot self = default;
            bool found = false;
            foreach (OnlineArcadeBattlePlayerSnapshot player in latest.Battle.Players)
                if (player.PlayerId == selfId) { self = player; found = true; break; }
            if (!found || !self.Alive) return;
            if(self.HerdImpact.Active)
            {
                predictedLocalPosition=new Vector2(self.PositionX,self.PositionZ);hasPrediction=true;
                view.SetPlayer(predictedLocalPosition,new Vector2(self.AimX,self.AimZ),true,self.Invulnerable);
                view.SetPlayerHerdImpact(0,self.HerdImpact);return;
            }
            TickRoundExitContact(self);
            OnlineArcadeInputFrame input = this.GetSystem<OnlineArcadeRunSystem>().LastSampledInput;
            var moveSpeed = CalculatePredictedMoveSpeed(catalog.Rule.PlayerMoveSpeed, self.DashSeconds > 0f,
                catalog.Upgrades, FindLocalRunPlayer(selfId).UpgradeStacks);
            var island = !self.InTreasureRoom && !latest.Battle.IsChallenge && activeArena?.Opening.Enabled == true
                ? predictionIsland ??= new FirepowerLoop.Arcade.ArcadeIslandRegion(ArenaHalfWidth, ArenaHalfHeight,latest.Battle.WaveNumber) : null;
            if (island != null && island.IsShallow(predictedLocalPosition.x, predictedLocalPosition.y))
                moveSpeed *= activeArena.Opening.WaterSpeedRatio;
            predictedLocalPosition = PredictLocalPosition(predictedLocalPosition, PredictContentMove(PredictedMovementDirection(input.Move,self),moveSpeed,self), moveSpeed,
                Mathf.Min(Time.deltaTime, .05f), catalog.Rule.PlayerRadius, self.InTreasureRoom ? TreasureParameters()?.HalfWidth ?? ArenaHalfWidth : ArenaHalfWidth,
                self.InTreasureRoom ? TreasureParameters()?.HalfHeight ?? ArenaHalfHeight : ArenaHalfHeight,
                self.InTreasureRoom ? Array.Empty<ArcadeObstacleDefinition>() : RoundObstacles, island);
            Vector2 authoritative = new(self.PositionX, self.PositionZ);
            if (Vector2.Distance(predictedLocalPosition, authoritative) > SnapDistance) predictedLocalPosition = authoritative;
            Vector2 facing = input.Aim.sqrMagnitude > .01f ? input.Aim : new Vector2(self.AimX, self.AimZ);
            view.SetPlayer(predictedLocalPosition, facing, true, self.Invulnerable || self.DashSeconds > 0);
            SyncPlayerEffects(self, predictedLocalPosition);
        }

        /// <summary>冲刺只使用权威锁定方向；旧快照缺少方向时使用权威朝向补齐，不采纳后续本地输入。</summary>
        internal static Vector2 PredictedMovementDirection(Vector2 move, OnlineArcadeBattlePlayerSnapshot self)
        {
            if(self.HerdImpact.Active)return Vector2.zero;
            if (self.DashSeconds <= 0) return move;
            FirepowerLoop.Arcade.ArcadeDashRules.ResolveDirection(self.DashDirectionX, self.DashDirectionZ,
                self.AimX, self.AimZ, out var x, out var y);
            return new Vector2(x, y);
        }

        /// <summary>读取同一权威领域预测冰面与孢子；预测仅改变表现，后续仍按服务器位置校正。</summary>
        private Vector2 PredictContentMove(Vector2 move,float speed,OnlineArcadeBattlePlayerSnapshot self)
        {
            if(activeRoundLayout==null||self.InTreasureRoom||speed<=0)return move;
            bool ice=activeRoundLayout.EventKind==5,slow=false;
            foreach(var area in latest.Battle.ContentAreas)
                if(!area.Warning&&FirepowerLoop.Arcade.ArcadeRoundContentRules.TouchesCircle(predictedLocalPosition.x,predictedLocalPosition.y,catalog.Rule.PlayerRadius,area.X,area.Y,area.Radius))
                {ice|=area.Kind==5;slow|=area.Kind==8;}
            foreach(var enemy in latest.Battle.Enemies)
                if(enemy.SpecialKind==9&&FirepowerLoop.Arcade.ArcadeRoundContentRules.TouchesCircle(predictedLocalPosition.x,predictedLocalPosition.y,catalog.Rule.PlayerRadius,enemy.PositionX,enemy.PositionZ,activeRoundLayout.EventRadius))slow=true;
            if(slow)move/=Mathf.Max(1,activeRoundLayout.SkillMagnitude);
            if(ice)
            {
                FirepowerLoop.Arcade.ArcadeRoundContentRules.UpdateIceVelocity(predictedIceVelocity.x,predictedIceVelocity.y,move.x,move.y,speed,
                    1/Mathf.Max(1,activeRoundLayout.SkillMagnitude),Mathf.Min(Time.deltaTime,.05f),false,out float vx,out float vy);
                predictedIceVelocity=new Vector2(vx,vy);return predictedIceVelocity/speed;
            }
            predictedIceVelocity=move*speed;return move;
        }

        private void ApplyAuthoritativeSnapshot(OnlineArcadeBattleSnapshot battle)
        {
            if (!active || battle == null || battle.RoundGeneration<activeRoundGeneration ||
                battle.RoundGeneration==activeRoundGeneration && battle.BattleRevision < lastAppliedBattleRevision) return;
            if(battle.SlayerMart?.Active==true){ApplySlayerMartPresentation(battle);return;}
            if(activeRoundGeneration!=0&&(battle.RoundGeneration!=activeRoundGeneration||presentedSlayerMart))
            {
                view.BeginRoundTransition();
                if(!view.IsRoundTransitionCovered){roundTransitionPending=true;return;}
            }
            roundTransitionPending=false;
            if(battle.RoundGeneration!=activeRoundGeneration||presentedSlayerMart)
            {
                presentedSlayerMart=false;
                activeRoundLayout=catalog.RoundLayouts.FirstOrDefault(p=>p.Id==battle.RoundLayoutId&&p.Version==battle.RoundLayoutVersion);
                if(activeRoundLayout==null&&!battle.IsChallenge) {Log.Error("联机轮布局不可用："+battle.RoundLayoutId);return;}
                activeRoundGeneration=battle.RoundGeneration;lastAppliedBattleRevision=-1;hasPrediction=false;predictionIsland=null;predictedIceVelocity=Vector2.zero;
                roundLoadPrompt=false;
                enemyIds.Clear();projectileIds.Clear();dropIds.Clear();nextPresentationId=1;
                view.Begin(activeArena.ArenaNumber,ArenaHalfWidth,ArenaHalfHeight,RoundObstacles);
                view.SetIslandRound(activeArena.ArenaNumber==1?battle.WaveNumber:0);
                view.SetRoundLayout(activeRoundLayout);
            }
            long selfId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            var localPlayer = battle.Players.FirstOrDefault(p => p.PlayerId == selfId);
            bool inRoom = localPlayer.InTreasureRoom;
            var roomRules = TreasureParameters();
            if (inRoom != presentedInTreasureRoom || !string.Equals(presentedRoomId, battle.TreasureRoomId, StringComparison.Ordinal))
            {
                view.Begin(activeArena.ArenaNumber, inRoom ? roomRules.HalfWidth : ArenaHalfWidth,
                    inRoom ? roomRules.HalfHeight : ArenaHalfHeight,
                    inRoom ? Array.Empty<ArcadeObstacleDefinition>() : RoundObstacles);
                view.SetIslandRound(!inRoom&&activeArena.ArenaNumber==1?battle.WaveNumber:0);
                if(!inRoom)view.SetRoundLayout(activeRoundLayout);
                hasPrediction = false; hasLocalFireBaseline = false; shieldChargesByPlayer.Clear();
                enemyIds.Clear(); projectileIds.Clear(); dropIds.Clear(); nextPresentationId = 1;
                presentedInTreasureRoom = inRoom; presentedRoomId = battle.TreasureRoomId;
            }
            bool supplyBaseline = lastAppliedBattleRevision < 0;
            lastAppliedBattleRevision = battle.BattleRevision;
            // 沿用服务器已生成/待生成敌人及出口快照，不由 HUD 或动画推断清波。
            bool treasureWave = !string.IsNullOrEmpty(battle.TreasureRoomId) && battle.WaveNumber == roomRules?.WaveNumber;
            // TreasureOpen 是服务器 TreasureCleared 的投影；接触入口进入宝藏房同帧也必须显示本波完成。
            bool cleared = treasureWave ? battle.TreasureOpen || battle.TreasureExpired :
                battle.TotalInWave > 0 && battle.SpawnedInWave >= battle.TotalInWave && battle.Enemies.Count == 0;
            if (!battle.IsChallenge && !battle.IsFinalBoss && (!inRoom || treasureWave && cleared))
            {
                if (StageNotices.Observe(latest.RunId, battle.ArenaNumber, battle.WaveNumber,
                        cleared, battle.AwaitingExit, Time.unscaledTimeAsDouble))
                    this.SendEvent(new ArcadeStageNoticeChangedEvent());
            }
            else if (battle.IsChallenge || battle.IsFinalBoss) StageNotices.Clear();
            view.SetTreasureRoom(roomRules, inRoom, battle.TreasureDiscovered, battle.TreasureOpen, battle.TreasureRewardIds);
            view.SetTreasureRewardOpacity(ArcadeCombatSystem.GroundDropOpacity(battle.TreasureRewardSeconds));
            view.SetOpening(inRoom || battle.IsChallenge ? default : activeArena.Opening,
                FirepowerLoop.Arcade.ArcadeOpeningRules.CampfireActive(activeArena.Opening, battle.WaveNumber - 1,
                    battle.IsChallenge || battle.IsFinalBoss, battle.TotalInWave <= battle.SpawnedInWave && battle.Enemies.Count == 0,
                    battle.AwaitingExit, inRoom, !localPlayer.Alive || localPlayer.RespawnSeconds > 0));
            view.SetHazards(inRoom || battle.IsChallenge || battle.IsFinalBoss || battle.AwaitingExit ? Array.Empty<FirepowerLoop.Arcade.ArcadeHazardParameters>() :
                activeArena.Hazards, battle.HazardElapsedSeconds);
            view.SetRoundContent(battle.ContentElapsed,inRoom?Array.Empty<FirepowerLoop.Arcade.ArcadeContentAreaSnapshot>():battle.ContentAreas);
            view.SetHerds(battle.RoundGeneration,inRoom?Array.Empty<FirepowerLoop.Arcade.ArcadeHerdSnapshot>():battle.Herds);
            // 四向出口同去一关；负数仅表示下一轮标签，服务端仍接收正数场地身份。
            var roundTargets=battle.WaveNumber<4?new[]{-(battle.WaveNumber+1)}:activeArena.NextArenaNumbers.Take(1).ToArray();
            if(battle.WaveNumber==4&&activeRoundLayout?.BossKind>0&&!battle.RoundBossEncounter&&battle.AwaitingExit)roundTargets=new[]{activeArena.ArenaNumber};
            view.SetExits(!inRoom && battle.AwaitingExit && !battle.IsFinalBoss && !StagePortalVisible(battle) ? roundTargets : Array.Empty<int>(),
                ArenaHalfHeight, !inRoom && battle.AwaitingExit && battle.IsFinalBoss && !StagePortalVisible(battle));
            seenIds.Clear();
            view.BeginEffectSync();
            view.SetRoundReward(inRoom?null:battle.RoundReward?.ToPresentation(),catalog.Rule.RoundReward);
            view.SetStagePortal(!inRoom&&StagePortalVisible(battle),new Vector2(battle.StagePortalX,battle.StagePortalZ),battle.StagePortalSeconds,catalog.Rule.RoundReward?.PortalAppearSeconds??0);
            foreach (OnlineArcadeBattlePlayerSnapshot player in battle.Players)
            {
                if (player.InTreasureRoom != inRoom) continue;
                Vector2 position = new(player.PositionX, player.PositionZ);
                Vector2 displayPosition = position;
                Vector2 facing = new(player.AimX, player.AimZ);
                if (player.PlayerId == selfId)
                {
                    if (player.HerdImpact.Active||!hasPrediction || Vector2.Distance(predictedLocalPosition, position) > SnapDistance)
                        predictedLocalPosition = position;
                    else predictedLocalPosition = Vector2.Lerp(predictedLocalPosition, position, .35f);
                    hasPrediction = true;
                    displayPosition = predictedLocalPosition;
                    view.SetPlayer(predictedLocalPosition, facing, player.Alive, player.Invulnerable || player.DashSeconds > 0);
                    view.SetPlayerHealth(player.CurrentHealth, player.MaxHealth);
                }
                else
                {
                    seenIds.Add(player.PlayerId);
                    view.SetRemotePlayer(player.PlayerId, position, facing, player.Alive, player.Invulnerable || player.DashSeconds > 0,
                        player.CurrentHealth, player.MaxHealth);
                }
                SyncPlayerCombat(player, selfId);
                view.SetPlayerHerdImpact(player.PlayerId==selfId?0:player.PlayerId,player.Alive?player.HerdImpact:default);
                view.SetPlayerRescue(player.PlayerId==selfId?0:player.PlayerId,!player.Alive&&player.DownedSeconds>0);
                view.SetPlayerDash(player.PlayerId==selfId?0:player.PlayerId,displayPosition,
                    PredictedMovementDirection(Vector2.zero,player),player.DashSeconds,player.Alive);
                int shieldCharges = 0;
                foreach (OnlineArcadePowerupSnapshot powerup in player.Powerups)
                    if (string.Equals(powerup.ProductId, "arcade.powerup.shield", StringComparison.Ordinal))
                    {
                        shieldCharges = powerup.Charges;
                        break;
                    }
                // 联机没有单独的挡伤事件；次数下降且处于受击无敌时才推断为护盾挡伤。
                if (shieldChargesByPlayer.TryGetValue(player.PlayerId, out int previousCharges) &&
                    previousCharges > shieldCharges && player.Invulnerable)
                    view.PlayShieldBlock(player.PlayerId, displayPosition);
                if (shieldCharges > 0) shieldChargesByPlayer[player.PlayerId] = shieldCharges;
                else shieldChargesByPlayer.Remove(player.PlayerId);
                SyncPlayerEffects(player, displayPosition);
            }
            SyncWeaponEvolutionVisuals(battle,inRoom);
            view.EndEffectSync();

            foreach (OnlineArcadePlayerSnapshot player in latest.Players)
                if (player.PlayerId != selfId && !seenIds.Contains(player.PlayerId)) view.RemoveRemotePlayer(player.PlayerId);
            foreach (long playerId in shieldChargesByPlayer.Keys.ToArray())
                if (playerId != selfId && !seenIds.Contains(playerId)) shieldChargesByPlayer.Remove(playerId);

            var bossWave = activeRoundLayout != null ? battle.RoundBossEncounter : !battle.IsFinalBoss && activeArena?.IsBoss == true && battle.WaveNumber == activeArena.WaveGroupIds.Count;
            SyncEnemies(inRoom ? Array.Empty<OnlineArcadeEnemySnapshot>() : battle.Enemies, bossWave,
                battle.EnemyDeaths, battle.RoundGeneration, !inRoom && !supplyBaseline,battle.Players);
            view.SetBabylon(inRoom || battle.IsChallenge ? null : activeArena?.Babylon, battle.CrateIds);
            SyncProjectiles(inRoom ? Array.Empty<OnlineArcadeProjectileSnapshot>() : battle.Projectiles);
            SyncDrops(inRoom ? Array.Empty<OnlineArcadeDropSnapshot>() : battle.Drops);
            SyncSupplyPickups(battle.SupplyPickups, selfId, supplyBaseline);
            bombPresentation.Observe(battle.BombBlasts,supplyBaseline,battle.RoundGeneration,!inRoom,view.PlayBombExplosion);
            electricHitPresentation.Observe(battle.ElectricHits,supplyBaseline,battle.RoundGeneration,!inRoom&&!latest.BattlePaused,battle.HazardElapsedSeconds,view.PlayElectricTowerHit,battle.ServerTime,Time.realtimeSinceStartupAsDouble);
            weaponFeedbackPresentation.Observe(battle.WeaponFeedback,supplyBaseline,battle.RoundGeneration,
                !inRoom&&!latest.BattlePaused&&!battle.RoundLoading&&view.IsActive&&view.IsRoundLayoutReady&&view.IsIslandRoundReady&&
                string.IsNullOrEmpty(view.RoundLayoutLoadError)&&string.IsNullOrEmpty(view.IslandRoundLoadError),
                battle.ServerTime,Time.realtimeSinceStartupAsDouble,PlayWeaponFeedback);
            if(latest.BattlePaused)view.StopPlayerFireAudio();
        }

        /// <summary>只播放本机新增成功拾取记录；先标记身份再回调，重复或历史通知不重复反馈。</summary>
        private void SyncSupplyPickups(IReadOnlyList<OnlineArcadeDropSnapshot> receipts, long selfId, bool baseline)
        {
            foreach (var receipt in receipts)
            {
                if (!observedSupplyPickupIds.Add(receipt.RuntimeId) || baseline ||
                    receipt.CollectedByPlayerId != selfId) continue;
                view.PlayTreasurePickup(receipt.ScoreAdded, receipt.GoldAdded, new Vector2(receipt.PositionX, receipt.PositionZ));
                var definition = catalog.Drops.FirstOrDefault(d => d.Id == receipt.DropConfigId);
                if ((definition.Kind == global::ArcadeDropKind.Bomb || definition.Kind == global::ArcadeDropKind.Dash) && receipt.AddedAmount > 0)
                    view.PlaySupplyPickup(definition, receipt.AddedAmount);
                if (definition.Kind == global::ArcadeDropKind.TemporaryWeapon || definition.Kind == global::ArcadeDropKind.Powerup)
                    view.PlayProductPickup(definition.ProductId);
            }
        }

        /// <summary>临时武器覆盖已购武器；旧近战装备回退基础枪，动作只接受服务器成功攻击序列。</summary>
        private void SyncPlayerCombat(OnlineArcadeBattlePlayerSnapshot player, long selfId)
        {
            var savedWeaponId = latest.Players.FirstOrDefault(x => x.PlayerId == player.PlayerId).WeaponId;
            var weaponId = player.TemporaryWeaponSeconds > 0f ? player.TemporaryWeaponId : savedWeaponId;
            if (string.IsNullOrWhiteSpace(weaponId)) weaponId = catalog.Rule.BaseWeaponId;
            var weapon = catalog.Weapons.FirstOrDefault(x => x.Id == weaponId);
            if (weapon.Behavior == global::ArcadeWeaponBehavior.Melee)
                weapon = catalog.Weapons.FirstOrDefault(x => x.Id == catalog.Rule.BaseWeaponId);
            if (string.IsNullOrWhiteSpace(weapon.Id)) return;
            if (player.PlayerId != selfId)
            {
                view.SetRemotePlayerCombat(player.PlayerId, weapon, player.FireSequence);
                return;
            }
            view.SetPlayerWeapon(weapon);
            if (!hasLocalFireBaseline)
            {
                view.SetPlayerFireSequence(0);
                hasLocalFireBaseline = true;
            }
            else if (lastLocalFireSequence != player.FireSequence && player.Alive)
                view.SetPlayerFireSequence(player.FireSequence);
            lastLocalFireSequence = player.FireSequence;
        }

        /// <summary>把单名玩家的权威强化与支援配置映射为视觉；固定型强化使用服务器落点。</summary>
        private void SyncPlayerEffects(OnlineArcadeBattlePlayerSnapshot player, Vector2 displayPosition)
        {
            if(player.Alive&&player.RespawnSeconds<=0&&player.HasStorm&&player.StormSeconds>0&&!player.InTreasureRoom)
            {
                if(stormVisualConfig==null)this.GetUtility<IRuntimeConfigProvider>().TryGetSlayerMart(out stormVisualConfig,out _);
                if(stormVisualConfig!=null)view.SetSlayerMartStorm(player.PlayerId,displayPosition,stormVisualConfig.Parameter("storm.radius"));
            }
            foreach(var body in player.SlayerMartSupports)
            {
                var definition=catalog.Powerups.FirstOrDefault(p=>p.Id==body.ProductId);
                if(!string.IsNullOrWhiteSpace(definition.Id))view.SetSlayerMartSupport(player.PlayerId,body.InstanceId,body.BodyIndex,definition,
                    new Vector2(body.PositionX,body.PositionZ),new Vector2(body.ShotDirectionX,body.ShotDirectionZ));
            }
            foreach (OnlineArcadePowerupSnapshot value in player.Powerups)
            {
                ArcadePowerupDefinition definition = catalog.Powerups.FirstOrDefault(x =>
                    string.Equals(x.Id, value.ProductId, StringComparison.Ordinal));
                if (string.IsNullOrWhiteSpace(definition.Id) || player.InTreasureRoom && !definition.VisualFollowPlayer) continue;
                // 只有普通锯／桶已转换为真实接触体；普通鸡／球仍由原槽独立显示。
                if(definition.Behavior is global::ArcadePowerupBehavior.Saw or global::ArcadePowerupBehavior.Barrel&&
                    player.SlayerMartSupports.Any(b=>b.ProductId==value.ProductId))continue;
                Vector2 anchor = definition.VisualFollowPlayer
                    ? displayPosition
                    : new Vector2(value.PositionX, value.PositionZ);
                if(definition.Behavior==global::ArcadePowerupBehavior.Drone)
                {
                    view.SetDroneUnit(player.PlayerId,0,definition,anchor,new Vector2(value.ShotDirectionX,value.ShotDirectionZ),value.DronePhase,value.DronePowerTier);
                    if(value.HasExtraDrone)view.SetDroneUnit(player.PlayerId,1,definition,new Vector2(value.ExtraDroneX,value.ExtraDroneZ),new Vector2(value.ExtraDroneDirectionX,value.ExtraDroneDirectionZ),value.ExtraDronePhase,value.DronePowerTier);
                    continue;
                }
                view.SetPowerup(player.PlayerId, definition, anchor,
                    new Vector2(value.ShotDirectionX, value.ShotDirectionZ));
            }

            if (string.IsNullOrWhiteSpace(player.SupportId)) return;
            ArcadeSupportVisualDefinition support = catalog.SupportVisuals.FirstOrDefault(x =>
                string.Equals(x.ProductId, player.SupportId, StringComparison.Ordinal) &&
                string.Equals(x.EquipmentSlotId, ArcadeRunDefaults.SupportSlotId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(support.ProductId))
                view.SetSupport(player.PlayerId, support, displayPosition);
        }

        private void SyncEnemies(IReadOnlyList<OnlineArcadeEnemySnapshot> values, bool bossWave,
            IReadOnlyList<OnlineArcadeEnemyDeathSnapshot> deaths, int roundGeneration, bool allowDeaths,
            IReadOnlyList<OnlineArcadeBattlePlayerSnapshot> players)
        {
            seenIds.Clear();
            foreach (OnlineArcadeEnemySnapshot value in values)
            {
                seenIds.Add(value.RuntimeId);
                int id = MapId(enemyIds, value.RuntimeId);
                view.SetEnemy(id, value.EnemyConfigId, new Vector2(value.PositionX, value.PositionZ),
                    value.CurrentHealth, value.MaxHealth, value.BossPhase is ArcadeBossPhase.Telegraph or ArcadeBossPhase.RayTelegraph,
                    attackSequence: value.AttackSequence, persistentHealthBar: bossWave || !string.IsNullOrEmpty(value.BossId));
                view.SetEnemyContent(id,value.SpecialKind,value.ContentBossKind,value.ContentWarning);
                view.SetNextStageEnemyState(id,value.SpecialKind,value.StageEnemy);
                view.SetEnemyArmor(id, new Vector2(value.PositionX, value.PositionZ), value.ArmorHealth);
                view.SetEnemyBurning(id,value.FlameHitRemainingSeconds);
                view.SetEnemyHerdImpact(id,value.HerdImpact);
                if(FirepowerLoop.Arcade.ArcadeMeleeRules.IsZombie(value.EnemyConfigId))
                {
                    var target=players.FirstOrDefault(p=>p.PlayerId==value.TargetPlayerId);
                    var direction=target.PlayerId==0?Vector2.zero:new Vector2(target.PositionX-value.PositionX,target.PositionZ-value.PositionZ);
                    view.SetEnemyMelee(id,value.MeleeWindupSeconds,value.MeleeWindupRemaining,value.MeleeRecoveryRemaining,direction);
                }
            }
            // 只消费已见实体的同轮移除；重复快照与重连没有旧映射，因此不会补播尸体。
            foreach (long runtimeId in enemyIds.Keys.Where(id => !seenIds.Contains(id)).ToArray())
            {
                int id = enemyIds[runtimeId];
                bool played = false;
                if (allowDeaths && deaths != null)
                    for (int i = deaths.Count - 1; i >= 0; i--)
                    {
                        var death = deaths[i];
                        if (death.RuntimeId != runtimeId || death.RoundGeneration != roundGeneration) continue;
                        view.RemoveEnemy(id, true, new Vector2(death.DirectionX, death.DirectionZ), death.Explosive,death.FlameKill,death.ImpactKind);
                        played = true; break;
                    }
                if (!played) view.RemoveEnemy(id);
                enemyIds.Remove(runtimeId);
            }
        }

        /// <summary>复用单人正式表现端口和声音限频；权威位置直接投影，不再次结算伤害。</summary>
        private void PlayWeaponFeedback(OnlineArcadeWeaponFeedbackSnapshot hit)
        {
            var position=new Vector2(hit.X,hit.Z);
            switch(hit.Kind)
            {
                case 1:view.PlayImpactSound(position,false);break;
                case 2:view.PlayProjectileSurfaceImpact(position,new Vector2(hit.DirectionX,hit.DirectionZ));break;
                case 3:view.PlayProjectileExplosion(position,hit.Radius);break;
                case 4:view.PlayEvolvedGrenadeExplosion(position,hit.Radius,false);break;
                case 5:view.PlayEvolvedGrenadeExplosion(position,hit.Radius,true);break;
            }
        }

        private void SyncProjectiles(IReadOnlyList<OnlineArcadeProjectileSnapshot> values)
        {
            seenIds.Clear();
            foreach (OnlineArcadeProjectileSnapshot value in values)
            {
                seenIds.Add(value.RuntimeId);
                Vector2 velocity = new(value.VelocityX, value.VelocityZ);
                if(value.SourceWeaponId=="arcade.support.drone")
                {
                    var source=catalog.Weapons.First(w=>w.Id==catalog.Rule.BaseWeaponId);
                    var drone=catalog.Powerups.First(p=>p.Behavior==global::ArcadePowerupBehavior.Drone);
                    view.SetDroneProjectile(MapId(projectileIds,value.RuntimeId),new Vector2(value.PositionX,value.PositionZ),velocity,
                        ArcadeWeaponPowerVisual.WithLevel(source.ProjectileVisualResourceName,value.WeaponPowerLevel),drone.VisualOffset.y);
                    continue;
                }
                if(value.OwnerPlayerId>0&&(value.EvolutionHitRadius>0||value.SourceWeaponId=="arcade.weapon.grenade"&&value.WeaponPowerLevel==2))continue;
                // 快照携带发射时身份，不能用拥有者当前武器重染飞行中的火焰或子弹。
                var weapon = value.OwnerPlayerId > 0
                    ? catalog.Weapons.FirstOrDefault(w => w.Id == value.SourceWeaponId) : default;
                view.SetProjectile(MapId(projectileIds, value.RuntimeId), value.OwnerPlayerId <= 0,
                    new Vector2(value.PositionX, value.PositionZ), velocity.sqrMagnitude > .001f ? velocity.normalized : Vector2.up,
                    ArcadeWeaponPowerVisual.WithLevel(weapon.ProjectileVisualResourceName,value.OwnerPlayerId>0?value.WeaponPowerLevel:0));
            }
            RemoveMissing(projectileIds, seenIds, view.RemoveProjectile);
        }

        private void SyncDrops(IReadOnlyList<OnlineArcadeDropSnapshot> values)
        {
            seenIds.Clear();
            foreach (OnlineArcadeDropSnapshot value in values)
            {
                ArcadeDropDefinition definition = catalog.Drops.FirstOrDefault(x => x.Id == value.DropConfigId);
                if (string.IsNullOrEmpty(definition.Id)) continue;
                seenIds.Add(value.RuntimeId);
                view.SetDrop(MapId(dropIds, value.RuntimeId), definition, new Vector2(value.PositionX, value.PositionZ));
                view.SetDropDelivery(MapId(dropIds,value.RuntimeId),value.WeaponAirdrop,value.DeliveryRemainingSeconds,catalog.Rule.WeaponDeliverySeconds);
                view.SetDropOpacity(MapId(dropIds, value.RuntimeId), ArcadeCombatSystem.GroundDropOpacity(value.RemainingSeconds));
            }
            RemoveMissing(dropIds, seenIds, view.RemoveDrop);
        }

        private int MapId(Dictionary<long, int> map, long runtimeId)
        {
            if (!map.TryGetValue(runtimeId, out int id)) map[runtimeId] = id = nextPresentationId++;
            return id;
        }

        private static void RemoveMissing(Dictionary<long, int> map, HashSet<long> seen, Action<int> remove)
        {
            foreach (long runtimeId in map.Keys.Where(id => !seen.Contains(id)).ToArray())
            { remove(map[runtimeId]); map.Remove(runtimeId); }
        }

        private ArcadeArenaDefinition ResolveArena(int arenaNumber)
        {
            ArcadeArenaDefinition exact = catalog.Arenas.FirstOrDefault(x => x.ArenaNumber == arenaNumber);
            return exact ?? catalog.Arenas.OrderBy(x => x.ArenaNumber).ElementAt((arenaNumber - 1) % catalog.Arenas.Count);
        }

        /// <summary>读取本机安全节点数据；缺失时返回空快照，让预测退化为无强化基础移速。</summary>
        private OnlineArcadePlayerSnapshot FindLocalRunPlayer(long playerId)
        {
            foreach (OnlineArcadePlayerSnapshot player in latest.Players)
                if (player.PlayerId == playerId) return player;
            return default;
        }

        /// <summary>按服务器公式计算本机预测移速；权威结果仍由后续战斗快照校正。</summary>
        internal static float CalculatePredictedMoveSpeed(float baseSpeed, bool dashing,
            IReadOnlyList<ArcadeUpgradeDefinition> upgrades, IReadOnlyDictionary<string, int> upgradeStacks)
        {
            var bonus = 0f;
            if (upgrades != null && upgradeStacks != null)
            {
                foreach (ArcadeUpgradeDefinition upgrade in upgrades)
                {
                    if (upgrade.Kind != global::ArcadeUpgradeKind.MoveSpeedPercent ||
                        !upgradeStacks.TryGetValue(upgrade.Id, out var stacks)) continue;
                    bonus += upgrade.ValuePerStack * stacks;
                }
            }
            var speed = Mathf.Max(0f, baseSpeed) * (1f + bonus);
            return dashing ? speed * DashSpeedMultiplier : speed;
        }

        /// <summary>
        /// 镜像服务器的单位圆输入、角色半径边界和轴向障碍回退；只预测表现位置，不写权威战斗状态。
        /// </summary>
        internal static Vector2 PredictLocalPosition(Vector2 current, Vector2 move, float speed, float deltaTime,
            float radius, float arenaHalfWidth, float arenaHalfHeight,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles, FirepowerLoop.Arcade.ArcadeIslandRegion island = null)
        {
            if (!IsFinite(current) || !IsFinite(move) || !IsPositiveFinite(speed) ||
                !IsPositiveFinite(deltaTime)) return current;

            move = Vector2.ClampMagnitude(move, 1f);
            var next = current + move * speed * deltaTime;
            if (island != null)
            {
                var p = island.Move(current.x, current.y, move.x * speed * deltaTime, move.y * speed * deltaTime, radius);
                next = new Vector2(p.X, p.Y);
            }
            next.x = Mathf.Clamp(next.x, -arenaHalfWidth + radius, arenaHalfWidth - radius);
            next.y = Mathf.Clamp(next.y, -arenaHalfHeight + radius, arenaHalfHeight - radius);
            if (IsInsidePredictionObstacle(new Vector2(next.x, current.y), radius, obstacles)) next.x = current.x;
            if (IsInsidePredictionObstacle(next, radius, obstacles)) next.y = current.y;
            if (IsInsidePredictionObstacle(next, radius, obstacles)) return current;
            return next;
        }

        private static bool IsInsidePredictionObstacle(Vector2 position, float radius,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles)
        {
            if (obstacles == null) return false;
            foreach (ArcadeObstacleDefinition obstacle in obstacles)
                if (Mathf.Abs(position.x - obstacle.Center.x) <= obstacle.HalfExtents.x + radius &&
                    Mathf.Abs(position.y - obstacle.Center.y) <= obstacle.HalfExtents.y + radius)
                    return true;
            return false;
        }

        private static bool IsFinite(Vector2 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);

        private static bool IsPositiveFinite(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>权威房间是“局:代次:配置”的实例身份；不能按累计场序猜测，也不能当作裸配置ID。</summary>
        private FirepowerLoop.Arcade.ArcadeTreasureRoomParameters TreasureParameters() =>
            catalog == null ? null : catalog.TreasureRooms.FirstOrDefault(r => latest.Battle?.TreasureRoomId?.EndsWith(":"+r.Id,StringComparison.Ordinal)==true);

        /// <summary>当前代次资源失败提供可重试入口，错误期间不向服务器发送就绪。</summary>
        private void ShowRoundLoadRetry()
        {
            view.EndRoundTransition();
            if(roundLoadPrompt)return;roundLoadPrompt=true;int generation=activeRoundGeneration;
            string error=!string.IsNullOrEmpty(view.RoundLayoutLoadError)?view.RoundLayoutLoadError:view.IslandRoundLoadError;
            Notify.ShowMessage(Loc.T("ui.arcade.menu.failed"),error,new List<MessageButtonConfig>{
                new(){ButtonText=Loc.T("ui.common.confirm"),Role=MessageButtonRole.Confirm,OnClick=()=>{
                    roundLoadPrompt=false;if(active&&activeRoundGeneration==generation){view.RetryIslandRoundLoad();view.SetRoundLayout(activeRoundLayout);}}},
                new(){ButtonText=Loc.T("ui.common.cancel"),Role=MessageButtonRole.Cancel,OnClick=()=>roundLoadPrompt=false}});
        }

        /// <summary>接触通道即提交当前轮出口意图；停留期间允许重发，由服务器校验位置并推进一次。</summary>
        private bool StagePortalVisible(OnlineArcadeBattleSnapshot b)=>b.AwaitingExit&&!b.IsChallenge&&b.WaveNumber>=4&&b.RoundReward!=null;
        private void TickRoundExitContact(OnlineArcadeBattlePlayerSnapshot self)
        {
            var battle=latest.Battle;if(activeRoundLayout==null||battle==null||!battle.AwaitingExit||!self.Alive||self.InTreasureRoom)return;
            int[] targets=battle.IsFinalBoss?new[]{0}:battle.WaveNumber<4?new[]{activeArena.ArenaNumber}:
                activeRoundLayout.BossKind>0&&!battle.RoundBossEncounter?new[]{activeArena.ArenaNumber}:activeArena.NextArenaNumbers.Take(1).ToArray();
            if(targets.Length==0)return;
            if(StagePortalVisible(battle))
            {
                if(battle.StagePortalSeconds>=catalog.Rule.RoundReward.PortalAppearSeconds&&Vector2.SqrMagnitude(new Vector2(self.PositionX-battle.StagePortalX,self.PositionZ-battle.StagePortalZ))<=catalog.Rule.RoundReward.PortalRadius*catalog.Rule.RoundReward.PortalRadius)
                    this.GetSystem<OnlineArcadeRunSystem>().ConfirmRoundExit(battle.RoundGeneration,targets[0]);
                return;
            }
            int contact=-1;for(int i=0;i<activeRoundLayout.ExitCount;i++)
            {var point=activeRoundLayout.GetExitPosition(i,ArenaHalfWidth,ArenaHalfHeight,catalog.Rule.PlayerRadius);
                var p=new Vector2(point.X,point.Y);
                if(Vector2.SqrMagnitude(new Vector2(self.PositionX,self.PositionZ)-p)<=2.25f){contact=i;break;}}
            if(contact<0)return;
            this.GetSystem<OnlineArcadeRunSystem>().ConfirmRoundExit(battle.RoundGeneration,targets[0]);
        }

        private void EndPresentation()
        {
            roundTransitionPending=false;
            view?.EndRoundTransition();
            StageNotices.Clear();
            presentedSlayerMart=false;activeSlayerMartLayout=null;activeRoundLayout=null;activeRoundGeneration=0;predictedIceVelocity=Vector2.zero;
            if (active) view?.End();
            active = false; activeBattleArenaNumber = 0; hasPrediction = false; predictionIsland = null; presentedInTreasureRoom = false; presentedRoomId = null; lastAppliedBattleRevision = -1; catalog = null; activeArena = null;
            enemyIds.Clear(); projectileIds.Clear(); dropIds.Clear(); seenIds.Clear(); nextPresentationId = 1;
            shieldChargesByPlayer.Clear();
            observedSupplyPickupIds.Clear();
            bombPresentation.Clear();
            electricHitPresentation.Clear();
            weaponFeedbackPresentation.Clear();
            hasLocalFireBaseline = false;
            lastLocalFireSequence = 0;
        }
    }
}

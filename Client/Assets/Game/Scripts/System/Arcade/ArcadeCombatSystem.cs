using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>竞技场完成并已成功写入下一安全节点；界面据此继续战斗或展示结果。</summary>
    public readonly struct ArcadeArenaCompletedEvent { }

    /// <summary>街机战斗操作失败；仅用于界面提示，不代表安全节点已被修改。</summary>
    public readonly struct ArcadeCombatOperationFailedEvent
    {
        public ArcadeCombatOperationFailedEvent(string error) => Error = error;
        public string Error { get; }
    }

    /// <summary>
    /// 推进独立街机战斗、波次、命中、掉落和竞技场完成事务；不读写基地背包、正式装备或正式战斗模型。
    /// </summary>
    public sealed partial class ArcadeCombatSystem : AbstractSystem
    {
        private const float AimDeadZone = 0.15f;
        private const float ProjectileHitRadius = 0.45f;
        private const float EnemyProjectileSpeed = 7.5f;
        private const float EnemyProjectileLifetime = 4f;
        /// <summary>普通鸡与购买鸡共用既有支援弹寿命。</summary>
        private const float SupportProjectileLifetimeSeconds = 1.5f;
        private const float WaveTransitionSeconds = 1.1f;
        private const float DashSeconds = 0.28f;
        private const float DashSpeedMultiplier = 2.4f;

        private PlayerInputCtrl input;
        private IArcadeCombatViewPort view;
        /// <summary>最后一命从后台写盘到返菜单结束的单一事务；观察工具也须等待它完成。</summary>
        private bool lastDeathReturnPending;
        /// <summary>体系卸载后禁止旧死亡续体操作新界面或新地图。</summary>
        private int deathReturnGeneration;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeArenaDefinition arena;
        private ArcadeEnemyGroupDefinition group;
        private ArcadeChallengeDefinition challenge;
        private System.Random random;
        private readonly ArcadeCombatRuntimePool runtimePool = new ArcadeCombatRuntimePool();
        private readonly List<int> enemyIterationIds = new List<int>();
        private readonly List<int> projectileIterationIds = new List<int>();
        private readonly List<int> dropIterationIds = new List<int>();
        private readonly List<global::ArcadePowerupBehavior> expiredPowerups =
            new List<global::ArcadePowerupBehavior>();
        private readonly List<ArcadeEnemyRuntime> crowdingEnemies = new List<ArcadeEnemyRuntime>();
        private readonly List<Vector2> crowdingPositions = new List<Vector2>();

        /// <summary>最近一次入场失败原因；只供发起“继续”的界面在同步事件返回后显示。</summary>
        public string LastEntryError { get; private set; }

        /// <summary>
        /// 返回当前单人街机现场摘要；只读测试入口不创建战斗，也不读取联机权威状态。
        /// </summary>
        public bool TryGetTestStatus(out string summary, out string error)
        {
            summary = null;
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            var run = this.GetModel<ArcadeRunModel>().Current;
            var weaponId = TryGetWeapon(runtime, out var activeWeapon) ? activeWeapon.Id : null;
            summary =
                $"竞技场 {runtime.ArenaNumber}，波次 {runtime.WaveIndex + 1}，生命 {runtime.PlayerHealth}/{runtime.PlayerMaxHealth}，" +
                $"剩余命数 {run?.RemainingLives ?? 0}，本场金币 {runtime.ArenaGold}，炸弹 {runtime.Bombs}，冲刺 {runtime.Dashes}，" +
                $"武器 {weaponId ?? ArcadeRunDefaults.BasicWeaponId}，敌人 {runtime.Enemies.Count + runtime.SpawnQueue.Count}，掉落 {runtime.Drops.Count}";
            return true;
        }

        /// <summary>测试入口：只修改当前瞬时战斗生命，不写安全节点。</summary>
        public bool TryHealPlayerForTests(out string summary, out string error)
        {
            summary = null;
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            if (runtime.RespawnRemaining > 0f)
            {
                error = "角色正在等待复活";
                return false;
            }
            runtime.PlayerHealth = runtime.PlayerMaxHealth;
            SyncView(runtime);
            PublishHud(runtime);
            summary = $"街机生命已恢复至 {runtime.PlayerHealth}/{runtime.PlayerMaxHealth}";
            return true;
        }

        /// <summary>测试入口：通过正式受伤规则测试护盾、无敌、复活和最后一命失败。</summary>
        public bool TryDamagePlayerForTests(int damage, out string summary, out string error)
        {
            summary = null;
            if (damage <= 0)
            {
                error = "伤害必须大于零";
                return false;
            }
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            DamagePlayer(runtime, damage);
            var current = this.GetModel<ArcadeCombatModel>().Runtime;
            if (current == runtime)
            {
                SyncView(runtime);
                PublishHud(runtime);
                summary = $"已按正式规则结算 {damage} 点伤害，当前生命 {runtime.PlayerHealth}/{runtime.PlayerMaxHealth}";
            }
            else
            {
                summary = $"已按正式规则结算 {damage} 点伤害，当前街机战斗已结束";
            }
            return true;
        }

        /// <summary>测试入口：增加当前竞技场金币；金币仍只会在正式完成事务中写入安全节点。</summary>
        public bool TryAddGoldForTests(int amount, out string summary, out string error)
        {
            summary = null;
            if (amount <= 0)
            {
                error = "金币增量必须大于零";
                return false;
            }
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            try
            {
                runtime.ArenaGold = checked(runtime.ArenaGold + amount);
            }
            catch (OverflowException)
            {
                error = "局内金币数值溢出";
                return false;
            }
            PublishHud(runtime);
            summary = $"当前竞技场金币已增加 {amount}，现为 {runtime.ArenaGold}";
            return true;
        }

        /// <summary>测试入口：设置当前竞技场紧急资源，离场时仍由正式完成事务决定是否保存。</summary>
        public bool TrySetResourceForTests(bool bombs, int amount, out string summary, out string error)
        {
            summary = null;
            if (amount < 0 || amount > 9999)
            {
                error = "资源数量必须在 0 到 9999 之间";
                return false;
            }
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            if (bombs) runtime.Bombs = amount;
            else runtime.Dashes = amount;
            PublishHud(runtime);
            summary = bombs ? $"炸弹数量已设置为 {amount}" : $"冲刺数量已设置为 {amount}";
            return true;
        }

        /// <summary>测试入口：按正式掉落定义应用效果，不绕过武器或强化道具配置。</summary>
        public bool TryApplyDropForTests(string dropId, out string summary, out string error)
        {
            summary = null;
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            ArcadeDropDefinition definition = default;
            var found = false;
            foreach (var candidate in catalog.Drops)
            {
                if (!string.Equals(candidate.Id, dropId, StringComparison.Ordinal)) continue;
                definition = candidate;
                found = true;
                break;
            }
            if (!found)
            {
                error = $"街机掉落配置不存在：{dropId}";
                return false;
            }
            if (definition.Kind == global::ArcadeDropKind.FirstPerson &&
                !TryBeginFirstPerson(runtime, definition, out error)) return false;
            if(!ApplyDrop(runtime, definition)){error="当前强化容量不足或配置无效";return false;}
            SyncView(runtime);
            PublishHud(runtime);
            summary = $"已模拟拾取 {definition.Id}（{definition.Kind} / {definition.ProductId}）";
            return true;
        }

        /// <summary>测试入口：清除当前波次，并继续使用正式波次奖励与过渡逻辑。</summary>
        public bool TryClearWaveForTests(out string summary, out string error)
        {
            summary = null;
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            var removed = ClearEnemiesForTests(runtime);
            runtime.SpawnQueue.Clear();
            TickWaveTransition(runtime, 0f);
            SyncView(runtime);
            PublishHud(runtime);
            summary = $"当前波次已清除，共移除 {removed} 个已生成敌人";
            return true;
        }

        /// <summary>测试入口：把当前单人竞技场或挑战推进到正式完成事务，不伪造安全节点。</summary>
        public bool TryClearArenaForTests(out string summary, out string error)
        {
            summary = null;
            if (!TryGetActiveTestRuntime(out var runtime, out error)) return false;
            var removed = ClearEnemiesForTests(runtime);
            runtime.SpawnQueue.Clear();
            runtime.WaveIndex = runtime.IsChallenge ? 0 : arena.WaveGroupIds.Count - 1;
            TickWaveTransition(runtime, 0f);
            runtime.WaveTransitionRemaining = 0f;
            TickWaveTransition(runtime, 0f);
            if (this.GetModel<ArcadeCombatModel>().Runtime == runtime && runtime.CompletionAwaitingRetry)
            {
                error = "街机完成事务保存失败，请使用界面重试";
                return false;
            }
            summary = runtime.IsChallenge
                ? $"特殊挑战已按成功流程完成，共移除 {removed} 个已生成敌人"
                : $"竞技场已按正式流程完成，共移除 {removed} 个已生成敌人";
            return true;
        }

        protected override void OnInit()
        {
            input = new PlayerInputCtrl();
            view = this.GetUtility<IArcadeCombatViewPort>();
            this.RegisterEvent<ArcadeRunChangedEvent>(OnRunChanged);
            this.RegisterEvent<ArcadeHudActionRequestedEvent>(OnHudAction);
            this.RegisterEvent<MapLoadingStartedEvent>(_ => EndFirstPerson(this.GetModel<ArcadeCombatModel>().Runtime));
            // Editor 切换设备模式时同样释放旧触点；不能跨模式保留触屏转向或开火。
            this.RegisterEvent<MobileInputModeChangedEvent>(_ =>
            {
                var runtime = this.GetModel<ArcadeCombatModel>().Runtime;
                if (runtime != null) ClearArcadeInput(runtime);
            });
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            deathReturnGeneration++;
            LifeCycle.UnregisterUpdate(OnUpdate);
            EndCombat();
            runtimePool.Clear();
            input?.Dispose();
            input = null;
        }

        private void OnRunChanged(ArcadeRunChangedEvent evt)
        {
            if ((evt.Phase == ArcadeRunPhase.Arena || evt.Phase == ArcadeRunPhase.Challenge) && evt.Snapshot != null)
            {
                if (!this.GetModel<ArcadeCombatModel>().IsActive)
                    BeginCombat(evt.Snapshot, evt.Phase == ArcadeRunPhase.Challenge);
                return;
            }
            EndCombat();
        }

        private bool TryGetActiveTestRuntime(out ArcadeCombatRuntime runtime, out string error)
        {
            runtime = this.GetModel<ArcadeCombatModel>().Runtime;
            if (runtime != null && catalog != null && arena != null)
            {
                error = null;
                return true;
            }
            error = "当前没有进行中的本地单人街机战斗";
            return false;
        }

        private int ClearEnemiesForTests(ArcadeCombatRuntime runtime)
        {
            CopyKeys(runtime.Enemies, enemyIterationIds);
            var removed = enemyIterationIds.Count;
            for (var index = 0; index < enemyIterationIds.Count; index++)
                if (runtime.Enemies.TryGetValue(enemyIterationIds[index], out var enemy)) KillEnemy(runtime, enemy, true);
            return removed;
        }

        private static string ReadSavedWeapon(ArcadeRunSaveData run)
        {
            if (run?.EquipmentBySlot != null &&
                run.EquipmentBySlot.TryGetValue(ArcadeRunDefaults.WeaponSlotId, out var weaponId) &&
                !string.IsNullOrWhiteSpace(weaponId))
                return weaponId;
            return ArcadeRunDefaults.BasicWeaponId;
        }

        /// <summary>进入竞技场时从最后安全节点构造全新的瞬时现场；任何失败都会退回该安全节点。</summary>
        private void BeginCombat(ArcadeRunSaveData run, bool isChallenge)
        {
            LastEntryError = null;
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            string error = null;
            if (provider == null || !provider.TryGetArcadeCatalog(out catalog, out error))
            {
                RejectArenaEntry(error ?? "街机配置不可用");
                return;
            }
            if (catalog.Arenas.Count == 0 || catalog.Weapons.Count == 0 || view == null)
            {
                RejectArenaEntry("街机战斗配置或表现端口不可用");
                return;
            }
            slayerMartConfig=null;
            if(run.SlayerMartRulesVersion==1&&!provider.TryGetSlayerMart(out slayerMartConfig,out error))
            {RejectArenaEntry(error??"商店能力配置不可用");return;}
            if(run.SlayerMartRulesVersion==1&&run.SlayerMart!=null&&
                run.SlayerMart.Players.Any(p=>!ArcadeSlayerMartAbilityRules.IsValid(p,slayerMartConfig.Parameter)||!ArcadeSlayerMartSupportRules.IsValid(p,slayerMartConfig)))
            {RejectArenaEntry(error??"商店能力恢复状态无效");return;}
            runtimePool.Configure(catalog.Rule.MaxAliveEnemies, catalog.Rule.MaxProjectiles, catalog.Rule.MaxDrops);

            arena = run.IsCasualRoute
                ? catalog.Arenas.First(value => value.ArenaNumber == run.ArenaConfigNumber)
                : catalog.Arenas[ResolveConfiguredArenaIndex(run.NextArenaNumber, catalog.Arenas.Count)];
            challenge = default;
            if (isChallenge)
            {
                var found = false;
                foreach (var candidate in catalog.Challenges)
                {
                    if (!string.Equals(candidate.Id, run.Checkpoint?.ChallengeId, StringComparison.Ordinal)) continue;
                    challenge = candidate;
                    found = true;
                    break;
                }
                if (!found)
                {
                    RejectArenaEntry("特殊挑战配置不存在");
                    return;
                }
            }
            var runtime = new ArcadeCombatRuntime
            {
                SlayerMartRulesVersion=run.SlayerMartRulesVersion,
                SlayerMart=run.SlayerMart?.Clone(),
                ArenaNumber = run.NextArenaNumber,
                IsChallenge = isChallenge,
                ChallengeId = isChallenge ? challenge.Id : null,
                ChallengeSeconds = isChallenge ? challenge.TimeLimitSeconds : 0f,
                PlayerMaxHealth = ScalePlayerHealth(catalog.Rule.PlayerMaxHealth, UpgradeTotal(run, global::ArcadeUpgradeKind.MaxHealthPercent)),
                Bombs = ReadAmount(run.EmergencyResources, ArcadeRunDefaults.BombResourceId),
                Dashes = ReadAmount(run.EmergencyResources, ArcadeRunDefaults.DashResourceId),
                WaveIndex = -1
            };
            runtime.PlayerHealth = runtime.PlayerMaxHealth;
            if (!isChallenge)
                foreach (var definition in arena.Hazards)
                    runtime.Hazards.Add(new FirepowerLoop.Arcade.ArcadeHazardState(definition));
            random = new System.Random(BuildCombatSeed(run.RunId, run.NextArenaNumber));

            var map = this.GetModel<MapSceneModel>();
            if (map.CurrentMapId != MapIds.Arcade || map.CurrentMode != MapPreparationMode.ArcadePlayerOnly)
            {
                RejectArenaEntry("街机地图尚未准备完成");
                return;
            }

            try
            {
                this.GetModel<ArcadeCombatModel>().Begin(runtime);
                input.Enable();
                if(InitializeSlayerChallenge(runtime,run)) { }
                else if (UsesIslandRounds(runtime))
                    StartIslandRound(runtime, (run.IslandRoundCheckpoint?.RoundNumber ?? 1) - 1, run.IslandRoundCheckpoint);
                else if (UsesContentRounds(runtime))
                {
                    if (run.RoundCheckpoint == null && run.ArenaEntryState != null)
                        RestoreIslandCheckpoint(runtime, run.ArenaEntryState);
                    StartContentRound(runtime, (run.RoundCheckpoint?.RoundNumber ?? 1) - 1, run.RoundCheckpoint);
                }
                else
                {
                    view.Begin(arena.ArenaNumber, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, arena.Obstacles);
                    view.SetIslandRound(0);
                    if (!isChallenge && run.ArenaEntryState != null) BeginArenaEntry(runtime, run.ArenaEntryState);
                    else BeginWave(runtime, 0);
                }
                SyncView(runtime);
                PublishHud(runtime);
            }
            catch (Exception exception)
            {
                EndCombat();
                RejectArenaEntry($"创建街机战场失败：{exception.Message}");
            }
        }

        private void RejectArenaEntry(string error)
        {
            LastEntryError = string.IsNullOrWhiteSpace(error) ? "进入街机战场失败" : error;
            this.GetSystem<ArcadeRunSystem>().TryReturnToSafeNode(out _, out _);
            this.SendEvent(new ArcadeCombatOperationFailedEvent(LastEntryError));
        }

        private void EndCombat()
        {
            ClearSlayerMartClock();
            EndFirstPerson(this.GetModel<ArcadeCombatModel>()?.Runtime);
            islandRegion = null; islandMovementPath.Clear();
            islandRegionRound = islandObstacleRound = -1; islandRoundObstacles = null;
            input?.Disable();
            view?.End();
            var model = this.GetModel<ArcadeCombatModel>();
            if (model?.Runtime != null) RecycleRuntime(model.Runtime);
            model?.Clear();
            catalog = null;
            arena = null;
            group = null;
            challenge = default;
            random = null;
        }

        /// <summary>战斗结束时统一归还仍存活的规则对象；安全节点数据不在该瞬时对象图中。</summary>
        private void RecycleRuntime(ArcadeCombatRuntime runtime)
        {
            foreach (var enemy in runtime.Enemies.Values) runtimePool.Return(enemy);
            foreach (var projectile in runtime.Projectiles.Values) runtimePool.Return(projectile);
            foreach (var drop in runtime.Drops.Values) runtimePool.Return(drop);
            runtime.Enemies.Clear();
            runtime.Projectiles.Clear();
            runtime.Drops.Clear();
            runtime.WeaponSpawns = null;
            runtime.Powerups.Clear();
            runtime.Hazards.Clear();
            runtime.HazardPreviousEnemyPositions.Clear();
            runtime.SpawnQueue.Clear();
            enemyIterationIds.Clear();
            projectileIterationIds.Clear();
            dropIterationIds.Clear();
            expiredPowerups.Clear();
            crowdingEnemies.Clear();
            crowdingPositions.Clear();
        }

        private void OnHudAction(ArcadeHudActionRequestedEvent evt)
        {
            var runtime = this.GetModel<ArcadeCombatModel>().Runtime;
            if (runtime == null) return;
            if ((runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending || runtime.CompletionAwaitingRetry ||
                runtime.CompletionSubmitted) && evt.Action != ArcadeHudActionKind.RetryCompletion) return;
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing || UIHelper.HasUIOpen) return;
            // 同帧双肩技能优先于单键消耗品，未解锁时保留旧输入规则。
            if(evt.Action is ArcadeHudActionKind.Bomb or ArcadeHudActionKind.Dash&&input?.FieldUpgradePressedThisFrame==true&&
                SlayerMartAbility(runtime)?.Owned.Contains("slayermart.item.16")==true)return;
            switch (evt.Action)
            {
                case ArcadeHudActionKind.FieldUpgrade:
                    TryUseFieldUpgrade();
                    break;
                case ArcadeHudActionKind.Fire:
                    TryFire(runtime);
                    break;
                case ArcadeHudActionKind.Bomb:
                    UseBomb(runtime);
                    break;
                case ArcadeHudActionKind.Dash:
                    // 按键回调可能早于本帧战斗更新，只采样方向，不重复消费相机转向或技能输入。
                    ReadDashInput(runtime);
                    UseDash(runtime);
                    break;
                case ArcadeHudActionKind.RetryCompletion:
                    RetryCompletion(runtime);
                    break;
                case ArcadeHudActionKind.ExitFirstPerson:
                    EndFirstPerson(runtime);
                    PublishHud(runtime);
                    break;
            }
        }

        private void OnUpdate()
        {
            var runtime = this.GetModel<ArcadeCombatModel>().Runtime;
            if(runtime==null){ClearSlayerMartClock();return;}
            if(catalog==null||arena==null)return;
            view.TickPlayerFeedback(Time.deltaTime);
            TickFrame(runtime, Time.deltaTime);
        }

        /// <summary>确定性活动步进；正式Update由TickFrame独立提供实时期限。</summary>
        internal void Tick(ArcadeCombatRuntime runtime,float deltaTime)=>TickCore(runtime,deltaTime,deltaTime);
        private void TickCore(ArcadeCombatRuntime runtime,float deltaTime,float shopSeconds)
        {
            var inputApp=this.GetModel<AppStateModel>();
            if(!SlayerMartClockActive(runtime)||runtime.SlayerMartReturnWave>0||UIHelper.HasUIOpen||
                inputApp!=null&&inputApp.CurrentState.Value!=AppState.Playing)input?.ClearFieldUpgradePulse();
            if(TickSlayerMartRegionFrame(runtime,shopSeconds,deltaTime))return;
            if (runtime.IslandTransitionPending)
            { ClearArcadeInput(runtime); TryFinishIslandTransition(runtime); return; }
            if (runtime.IslandRoomTransitionPending)
            { ClearArcadeInput(runtime); TickIslandRoomTransition(runtime); return; }
            // 死亡/区域变化可以来自本帧之外；零步长及暂停也不能保留失效视角。
            if (runtime.FirstPersonRemaining > 0 && (runtime.PlayerHealth <= 0 ||
                runtime.RespawnRemaining > 0 || runtime.InTreasureRoom && !UsesRoundLayouts(runtime) ||
                !runtime.IslandFirstPersonSuspended && !view.IsFirstPersonActive))
                EndFirstPerson(runtime);
            // 暂停及阻断界面冻结两个独立时钟，清除战斗意图而不终止临时视角。
            var app = this.GetModel<AppStateModel>();
            if (app != null && app.CurrentState.Value != AppState.Playing || UIHelper.HasUIOpen)
            { ClearArcadeInput(runtime); return; }
            if (deltaTime <= 0f || runtime.CompletionSubmitted) return;
            if (runtime.CompletionAwaitingRetry) return;
            if(runtime.SlayerChallenge!=null&&!TickSlayerChallenge(runtime,(float)(slayerFrameSeconds>=0?slayerFrameSeconds:deltaTime)))
            {
                // 返回保存会同步结束或替换战场；原现场释放后不能继续渲染或发布旧HUD。
                if(this.GetModel<ArcadeCombatModel>().Runtime==runtime){SyncView(runtime);PublishHud(runtime);}
                return;
            }
            var hazardPreviousPlayer = runtime.PlayerPosition;
            runtime.HazardPreviousEnemyPositions.Clear();
            foreach (var enemy in runtime.Enemies.Values)
                runtime.HazardPreviousEnemyPositions[enemy.Id] = enemy.Position;
            // 正式 CharacterController 负责地图碰撞；下一帧把实际落点写回街机运行态，避免规则位置穿过场景障碍。
            if (runtime.RespawnRemaining <= 0f && view.TryGetPlayerPosition(out var actualPlayerPosition))
                runtime.PlayerPosition = ResolvePosition(actualPlayerPosition, catalog.Rule.PlayerRadius,
                    runtime.InTreasureRoom ? runtime.TreasureRoom.Parameters.HalfWidth : catalog.Rule.ArenaHalfWidth,
                    runtime.InTreasureRoom ? runtime.TreasureRoom.Parameters.HalfHeight : catalog.Rule.ArenaHalfHeight,
                    ObstaclesFor(runtime), IslandFor(runtime));
            ReadInput(runtime);
            TickTimers(runtime, deltaTime);
            TickTreasureRoom(runtime, deltaTime);
            if (runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending)
            { SyncView(runtime); PublishHud(runtime); return; }
            if (runtime.IsChallenge && runtime.SlayerChallenge==null)
            {
                runtime.ChallengeSeconds = Mathf.Max(0f, runtime.ChallengeSeconds - deltaTime);
                if (runtime.ChallengeSeconds <= 0f)
                {
                    CompleteChallenge(runtime, false);
                    return;
                }
            }

            if (runtime.RespawnRemaining > 0f)
            {
                SyncView(runtime);
                PublishHud(runtime);
                return;
            }

            TickPlayer(runtime, deltaTime);
            TickTreasureRoom(runtime, 0f);
            if (runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending)
            { SyncView(runtime); PublishHud(runtime); return; }
            TickSlayerMartStorm(runtime,deltaTime);
            TickSlayerMartSupports(runtime,deltaTime);
            if (runtime.InTreasureRoom) { SyncView(runtime); PublishHud(runtime); return; }
            TickOpeningFire(runtime, deltaTime);
            if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime) return;
            if (runtime.AwaitingExit)
            {
                // 清场后仍可领取地面物资；先结算拾取，再处理出口或超时离场，避免漏存同帧奖励。
                TickDrops(runtime);
                if (!UsesRoundLayouts(runtime) && UsesEarlyRest(runtime) && !runtime.CompletionAwaitingRetry)
                {
                    runtime.WaveTransitionRemaining = Mathf.Max(0, runtime.WaveTransitionRemaining - deltaTime);
                    if (runtime.WaveTransitionRemaining <= 0 && arena.NextArenaNumbers.Count > 0)
                    {
                        runtime.SelectedExitConfigNumber = arena.NextArenaNumbers[0];
                        CompleteArena(runtime);
                        return;
                    }
                }
                if (UsesContentRounds(runtime)) TickContentExit(runtime);
                else if (UsesIslandRounds(runtime)) TickIslandExit(runtime); else TickExit(runtime);
                if (this.GetModel<ArcadeCombatModel>().Runtime == runtime) { SyncView(runtime); PublishHud(runtime); }
                return;
            }
            if (runtime.IslandRoundAwaitingExit)
            {
                // 清场后的玩家射击只继续弹丸表现与寿命，敌群和机关保持结束，不冻结空中弹丸。
                TickProjectiles(runtime, deltaTime);
                TickDrops(runtime);
                if (UsesContentRounds(runtime)) TickContentExit(runtime); else TickIslandExit(runtime);
                SyncView(runtime); PublishHud(runtime);
                return;
            }
            TickWeaponPickups(runtime, deltaTime);
            TickFinalBossReinforcements(runtime, deltaTime);
            TickRoundContent(runtime, deltaTime);
            if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime) return;
            TickSpawning(runtime, deltaTime);
            TickEnemies(runtime, deltaTime);
            if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime) return;
            TickHazards(runtime, deltaTime, hazardPreviousPlayer);
            if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime) return;
            TickProjectiles(runtime, deltaTime);
            if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime) return;
            TickDrops(runtime);
            TickPowerups(runtime, deltaTime);
            TickWaveTransition(runtime, deltaTime);
            if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime) return;
            SyncView(runtime);
            PublishHud(runtime);
        }

        /// <summary>从同一 Input System 通道读取键鼠、手柄和屏幕摇杆；右摇杆超过死区即持续开火。</summary>
        private void ReadInput(ArcadeCombatRuntime runtime)
        {
            ReadFieldUpgradeInput(runtime);
            runtime.MoveDirection = input?.MoveInput ?? Vector2.zero;
            var stickAim = input?.AimInput ?? Vector2.zero;
            var stickFiring = stickAim.sqrMagnitude >= AimDeadZone * AimDeadZone;
            if (ReadFirstPersonInput(runtime, runtime.MoveDirection, stickAim, stickFiring)) return;
            ReadTopDownAim(runtime, stickAim, stickFiring);
            runtime.Firing = ReadArcadeFiring(stickFiring);
        }

        /// <summary>冲刺动作只读取当前移动和朝向；第一人称镜头增量仍由正常帧更新消费一次。</summary>
        private void ReadDashInput(ArcadeCombatRuntime runtime)
        {
            var move = input?.MoveInput ?? Vector2.zero;
            if (runtime.FirstPersonRemaining > 0 && !runtime.IslandFirstPersonSuspended &&
                view.IsFirstPersonActive && view.TryReadFirstPersonAim(out var forward))
            {
                runtime.AimDirection = forward;
                runtime.MoveDirection = RelativeFirstPersonMovement(move, forward);
                return;
            }
            runtime.MoveDirection = move;
            var stickAim = input?.AimInput ?? Vector2.zero;
            ReadTopDownAim(runtime, stickAim, stickAim.sqrMagnitude >= AimDeadZone * AimDeadZone);
        }

        /// <summary>统一俯视输入与冲刺触发时的朝向优先级，有效右摇杆优先于鼠标。</summary>
        private void ReadTopDownAim(ArcadeCombatRuntime runtime, Vector2 stickAim, bool stickFiring)
        {
            if (stickFiring)
            {
                runtime.AimDirection = stickAim.normalized;
            }
            // 有效右摇杆拥有当前瞄准；桌面鼠标仅在摇杆回到死区后接管。
            if (!stickFiring && input != null && input.TryGetPointerScreenPosition(out var screen) &&
                view.TryScreenToArena(screen, out var target))
            {
                var aim = target - runtime.PlayerPosition;
                if (aim.sqrMagnitude > 0.001f) runtime.AimDirection = aim.normalized;
            }
        }

        private void TickTimers(ArcadeCombatRuntime runtime, float deltaTime)
        {
            TickDropLifetimes(runtime, deltaTime);
            foreach(var enemy in runtime.Enemies.Values)enemy.FlameHitRemainingSeconds=Mathf.Max(0,enemy.FlameHitRemainingSeconds-deltaTime);
            if (runtime.FirstPersonRemaining > 0)
            {
                if (runtime.FirstPersonRemaining <= deltaTime ||
                    !runtime.InTreasureRoom && !runtime.IslandFirstPersonSuspended && !view.IsFirstPersonActive)
                    EndFirstPerson(runtime);
                else runtime.FirstPersonRemaining -= deltaTime;
            }
            runtime.PlayerInvulnerability = Mathf.Max(0f, runtime.PlayerInvulnerability - deltaTime);
            runtime.FireCooldown = Mathf.Max(0f, runtime.FireCooldown - deltaTime);
            runtime.DashRemaining = Mathf.Max(0f, runtime.DashRemaining - deltaTime);
            // 军械库及返回后的开战等待保留完整火力；第一场财宝规则不变。
            bool awaitingArmoryCombat = UsesContentRounds(runtime) && runtime.ArenaNumber == 2 &&
                runtime.WaveIndex == 3 && runtime.IslandWaveBudget > 0 &&
                runtime.SpawnQueue.Count == runtime.IslandWaveBudget && runtime.Enemies.Count == 0;
            bool holdArmoryWeapon = awaitingArmoryCombat || runtime.TreasureRoom?.Parameters.IsArmory == true &&
                (runtime.InTreasureRoom || runtime.TreasureRoom.Members.Length > 0 || runtime.WaveClearAwarded ||
                 runtime.Enemies.Count == 0 && runtime.SpawnQueue.Count == 0);
            if (!holdArmoryWeapon) runtime.TemporaryWeaponRemaining = Mathf.Max(0f, runtime.TemporaryWeaponRemaining - deltaTime);
            expiredPowerups.Clear();
            foreach (var pair in runtime.Powerups)
            {
                var powerup = pair.Value;
                powerup.RemainingSeconds = Mathf.Max(0f, powerup.RemainingSeconds - deltaTime);
                powerup.CooldownSeconds = Mathf.Max(0f, powerup.CooldownSeconds - deltaTime);
                if (powerup.RemainingSeconds <= 0f) expiredPowerups.Add(pair.Key);
            }
            foreach (var behavior in expiredPowerups)
            {
                if (!runtime.InTreasureRoom && behavior == global::ArcadePowerupBehavior.Decoy &&
                    runtime.Powerups.TryGetValue(behavior, out var decoy))
                    DamageEnemiesInExplosionRadius(runtime, decoy.Position, SlayerMartRadius(runtime,decoy), decoy.Definition.Damage);
                runtime.Powerups.Remove(behavior);
            }
            if (runtime.TemporaryWeaponRemaining <= 0f) runtime.TemporaryWeaponId = null;
            if (runtime.RespawnRemaining <= 0f) return;
            runtime.RespawnRemaining = Mathf.Max(0f, runtime.RespawnRemaining - deltaTime);
            if (runtime.RespawnRemaining <= 0f)
            {
                runtime.PlayerHealth = runtime.PlayerMaxHealth;
                runtime.PlayerPosition = runtime.SlayerChallenge==null?Vector2.zero:new Vector2(RoundLayout(runtime).SpawnX,RoundLayout(runtime).SpawnY);
                runtime.PlayerInvulnerability = slayerMartConfig==null?catalog.Rule.ContactInvulnerabilitySeconds*2f:
                    ArcadeSlayerMartAbilityRules.RespawnProtection(catalog.Rule.ContactInvulnerabilitySeconds,SlayerMartAbility(runtime),slayerMartConfig.Parameter);
            }
        }

        private void TickPlayer(ArcadeCombatRuntime runtime, float deltaTime)
        {
            var speedBonus = UpgradeTotal(this.GetModel<ArcadeRunModel>().Current, global::ArcadeUpgradeKind.MoveSpeedPercent);
            var bootsBonus = TryGetPowerup(runtime, global::ArcadePowerupBehavior.Boots, out var boots)
                ? boots.Definition.Magnitude
                : 0f;
            var speed = CalculatePlayerSpeed(catalog.Rule.PlayerMoveSpeed, speedBonus,
                runtime.DashRemaining > 0f, bootsBonus);
            if (!runtime.InTreasureRoom && !runtime.IsChallenge)
                speed *= IslandFor(runtime) is { } region && region.IsShallow(runtime.PlayerPosition.x, runtime.PlayerPosition.y)
                    ? arena.Opening.WaterSpeedRatio : 1f;
            var island = IslandFor(runtime);
            var move = runtime.DashRemaining > 0f ? runtime.DashDirection : runtime.MoveDirection;
            if (island != null)
            {
                runtime.PlayerPosition = MoveOnIsland(runtime, RoundMovementDelta(runtime, move, speed, deltaTime), catalog.Rule.PlayerRadius);
                KillAlongIslandDash(runtime);
            }
            else
            runtime.PlayerPosition = ResolvePosition(runtime.PlayerPosition + RoundMovementDelta(runtime, move, speed, deltaTime),
                catalog.Rule.PlayerRadius,
                runtime.InTreasureRoom ? runtime.TreasureRoom.Parameters.HalfWidth : catalog.Rule.ArenaHalfWidth,
                runtime.InTreasureRoom ? runtime.TreasureRoom.Parameters.HalfHeight : catalog.Rule.ArenaHalfHeight,
                ObstaclesFor(runtime));
            if (!runtime.InTreasureRoom && runtime.Firing) TryFire(runtime);
        }

        private void TickSpawning(ArcadeCombatRuntime runtime, float deltaTime)
        {
            if (group == null || runtime.SpawnQueue.Count == 0) return;
            if (UsesIslandRounds(runtime) && !IslandEntryIsReady(runtime, deltaTime)) return;
            if (UsesContentRounds(runtime) && !runtime.IsFinalBoss && !ContentEntryReady(runtime, deltaTime)) return;
            runtime.SpawnCooldown = Mathf.Max(0f, runtime.SpawnCooldown - deltaTime);
            var aliveCap = Mathf.Min(group.MaxAlive, catalog.Rule.MaxAliveEnemies);
            if (runtime.SpawnCooldown > 0f || runtime.Enemies.Count >= aliveCap) return;
            var enemyId = runtime.SpawnQueue.Dequeue();
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetEnemy(enemyId, out var definition, out var error))
            {
                FailCombat(error);
                return;
            }
            var healthGrowth = 1f + catalog.Rule.HealthGrowthPerArena * (runtime.ArenaNumber - 1);
            var damageGrowth = 1f + catalog.Rule.DamageGrowthPerArena * (runtime.ArenaNumber - 1);
            var speedGrowth = 1f + catalog.Rule.SpeedGrowthPerArena * (runtime.ArenaNumber - 1);
            var enemy = runtimePool.RentEnemy();
            enemy.Id = runtime.NextEntityId++;
            enemy.ConfigId = definition.Id;
            enemy.AttackKind = definition.AttackKind;
            enemy.Position = RandomBorderPosition();
            if (UsesContentRounds(runtime) && !runtime.IsFinalBoss)
            {
                var layout = RoundLayout(runtime);
                var entry = layout.Entries[runtime.IslandEntryBatch % layout.Entries.Count];
                enemy.Position = ResolvePosition(new Vector2(entry.X, entry.Y), catalog.Rule.EnemyContactRadius,
                    catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime));
            }
            if (arena.Opening.Enabled && !runtime.IsChallenge && !runtime.IsFinalBoss)
            {
                if (UsesIslandRounds(runtime))
                    IslandRoundEntry(runtime, out enemy.Position);
                else
                {
                    var region = IslandFor(runtime);
                    ArcadeRegionPoint start;
                    if (runtime.WaveIndex == 0)
                        region.EntryFromFourSides(runtime.IslandSpawnSequence++, UnityRandom(0f, 1f), catalog.Rule.EnemyContactRadius, out start);
                    else
                        region.Entry(runtime.IslandSpawnSequence++, UnityRandom(0f, 1f), catalog.Rule.EnemyContactRadius, out start);
                    enemy.Position = new Vector2(start.X, start.Y);
                }
            }
            enemy.MaxHealth = Mathf.Max(1, Mathf.RoundToInt(definition.MaxHP * group.HealthMultiplier * healthGrowth));
            enemy.Damage = Mathf.Max(1, Mathf.RoundToInt(definition.AttackDamage * group.DamageMultiplier * damageGrowth));
            enemy.Speed = Mathf.Max(0.1f, definition.MoveSpeed * group.SpeedMultiplier * speedGrowth);
            enemy.AttackRange = Mathf.Max(0.7f, definition.AttackRange);
            enemy.AttackInterval = Mathf.Max(0.25f, definition.AttackCooldownSeconds);
            enemy.AttackCooldown = UnityRandom(0f, Mathf.Max(0.1f, definition.AttackCooldownSeconds));
            enemy.Health = enemy.MaxHealth;
            AssignArmor(runtime, enemy);
            enemy.IsBossPresentation = !runtime.IsChallenge && !runtime.IsFinalBoss && arena.IsBoss &&
                                       runtime.WaveIndex == arena.WaveGroupIds.Count - 1;
            var isFinalBossEnemy = runtime.IsFinalBoss && runtime.FinalBossEnemyId == 0 &&
                                  string.Equals(group.Id, ResolveFinalBoss().EnemyGroupId, StringComparison.Ordinal);
            var boss = isFinalBossEnemy ? ResolveFinalBoss() : default;
            if (isFinalBossEnemy || !runtime.IsChallenge && TryResolveBoss(arena, group, catalog.Bosses, out boss))
            {
                enemy.BossId = boss.Id;
                enemy.BossDefinition = boss;
                enemy.BossSkillCooldown = boss.SkillCooldownSeconds;
                enemy.BossRayCooldown = boss.RayIntervalSeconds;
            }
            AssignRoundEnemyContent(runtime, enemy);
            if (isFinalBossEnemy && UsesContentRounds(runtime)) enemy.ContentBossKind = 4;
            runtime.Enemies.Add(enemy.Id, enemy);
            if (isFinalBossEnemy)
            {
                runtime.FinalBossEnemyId = enemy.Id;
                AssignRoundEnemyContent(runtime, enemy);
                enemy.IsBossPresentation = true;
                group = FindGroup(boss.ReinforcementGroupId);
            }
            runtime.SpawnCooldown = group.SpawnIntervalSeconds;
        }

        private void TickEnemies(ArcadeCombatRuntime runtime, float deltaTime)
        {
            CopyKeys(runtime.Enemies, enemyIterationIds);
            for (var index = 0; index < enemyIterationIds.Count; index++)
            {
                if (!runtime.Enemies.TryGetValue(enemyIterationIds[index], out var enemy)) continue;
                if(SlayerMartStormSpeed(runtime,enemy)==0)continue;
                enemy.AttackCooldown = Mathf.Max(0f, enemy.AttackCooldown - deltaTime);
                var targetPosition = runtime.PlayerPosition;
                var targetIsDecoy = false;
                if (TryGetPowerup(runtime, global::ArcadePowerupBehavior.Decoy, out var decoy))
                {
                    targetPosition = decoy.Position;
                    targetIsDecoy = true;
                }
                var offset = targetPosition - enemy.Position;
                var distance = offset.magnitude;
                var direction = distance > 0.001f ? offset / distance : Vector2.up;
                var movementDelta = deltaTime * EnemySpeedMultiplier(runtime);
                movementDelta *= SlayerMartStormSpeed(runtime,enemy);
                if (TickSlayerGuardian(runtime,enemy,deltaTime)||TickRoundEnemyContent(runtime, enemy, targetPosition, targetIsDecoy, deltaTime))
                {
                    if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || catalog == null) return;
                    continue;
                }
                if (TickBoss(runtime, enemy, direction, targetIsDecoy, deltaTime, movementDelta))
                {
                    if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || catalog == null) return;
                    continue;
                }
                if (enemy.AttackKind == global::EnemyAttackKind.Projectile)
                {
                    if (distance > enemy.AttackRange * 0.8f)
                        MoveEnemyTowards(runtime, enemy, targetPosition, movementDelta);
                    if (distance <= enemy.AttackRange && enemy.AttackCooldown <= 0f)
                    {
                        enemy.AttackSequence++;
                        SpawnProjectile(runtime, true, enemy.Position, direction * EnemyProjectileSpeed, enemy.Damage, EnemyProjectileLifetime);
                        enemy.AttackCooldown = enemy.AttackInterval;
                    }
                }
                else
                {
                    MoveEnemyTowards(runtime, enemy, targetPosition, movementDelta);
                    if (distance <= catalog.Rule.PlayerRadius + catalog.Rule.EnemyContactRadius && enemy.AttackCooldown <= 0f)
                    {
                        enemy.AttackSequence++;
                        if (!targetIsDecoy) DamagePlayer(runtime, enemy.Damage);
                        // 死亡会提交或挂起结果事务，本帧不得再推进敌人与已释放的配置。
                        if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || catalog == null) return;
                        enemy.AttackCooldown = enemy.AttackInterval;
                    }
                }
                // 出现后即追踪当前目标；岸线只约束可走范围，不再设置固定登陆终点。
                enemy.Position = ResolvePosition(enemy.Position, catalog.Rule.EnemyContactRadius,
                    catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), IslandFor(runtime));
            }
            ResolveEnemyCrowding(runtime);
        }

        /// <summary>普通敌人按规则层可见性绕过矩形障碍；Boss 锁向冲锋不经过此入口。</summary>
        private void MoveEnemyTowards(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy, Vector2 target, float deltaTime)
        {
            var direction = ResolveNavigationDirection(enemy.Position, target, catalog.Rule.EnemyContactRadius,
                ObstaclesFor(runtime));
            enemy.Position = ResolvePosition(enemy.Position + direction * enemy.Speed * deltaTime, catalog.Rule.EnemyContactRadius,
                catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), IslandFor(runtime));
        }

        /// <summary>推进蓄力冲锋；蓄力期间锁定方向，冲锋命中同一玩家最多结算一次。</summary>
        private bool TickBoss(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy, Vector2 targetDirection,
            bool targetIsDecoy, float deltaTime, float movementDelta)
        {
            if (string.IsNullOrEmpty(enemy.BossId)) return false;
            var boss = enemy.BossDefinition;
            if (enemy.BossPhase == ArcadeBossPhase.RayTelegraph)
            {
                enemy.BossPhaseRemaining = Mathf.Max(0f, enemy.BossPhaseRemaining - deltaTime);
                if (enemy.BossPhaseRemaining <= 0f)
                {
                    SpawnProjectile(runtime, true, enemy.Position, enemy.BossChargeDirection * boss.RaySpeed,
                        Mathf.Max(1, Mathf.RoundToInt(enemy.Damage * boss.RayDamageMultiplier)),
                        EnemyProjectileLifetime);
                    enemy.AttackSequence++;
                    enemy.BossPhase = ArcadeBossPhase.None;
                    enemy.BossRayCooldown = boss.RayIntervalSeconds;
                }
                return true;
            }
            if (enemy.BossPhase == ArcadeBossPhase.Telegraph)
            {
                enemy.BossPhaseRemaining = Mathf.Max(0f, enemy.BossPhaseRemaining - deltaTime);
                if (enemy.BossPhaseRemaining <= 0f)
                {
                    enemy.BossPhase = ArcadeBossPhase.Charging;
                    enemy.BossPhaseRemaining = boss.ChargeDurationSeconds;
                    enemy.BossImpactConsumed = false;
                }
                return true;
            }
            if (enemy.BossPhase == ArcadeBossPhase.Charging)
            {
                var previousPosition = enemy.Position;
                enemy.Position = ResolvePosition(enemy.Position + enemy.BossChargeDirection * enemy.Speed *
                    boss.ChargeSpeedMultiplier * movementDelta, catalog.Rule.EnemyContactRadius,
                    catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime));
                var hitRadius = boss.ImpactRadius + catalog.Rule.PlayerRadius;
                if (!targetIsDecoy && !enemy.BossImpactConsumed && DistanceToSegmentSquared(runtime.PlayerPosition,
                        previousPosition, enemy.Position) <= hitRadius * hitRadius)
                {
                    enemy.BossImpactConsumed = true;
                    DamagePlayer(runtime, Mathf.Max(1, Mathf.RoundToInt(enemy.Damage * boss.ImpactDamageMultiplier)));
                    if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || catalog == null) return true;
                }
                enemy.BossPhaseRemaining = Mathf.Max(0f, enemy.BossPhaseRemaining - deltaTime);
                if (enemy.BossPhaseRemaining <= 0f)
                {
                    enemy.BossPhase = ArcadeBossPhase.None;
                    enemy.BossSkillCooldown = boss.SkillCooldownSeconds;
                }
                return true;
            }

            enemy.BossRayCooldown = Mathf.Max(0f, enemy.BossRayCooldown - deltaTime);
            if (boss.RayIntervalSeconds > 0f && enemy.BossRayCooldown <= 0f)
            {
                enemy.BossPhase = ArcadeBossPhase.RayTelegraph;
                enemy.BossPhaseRemaining = boss.RayTelegraphSeconds;
                enemy.BossChargeDirection = targetDirection.sqrMagnitude > .001f ? targetDirection.normalized : Vector2.up;
                return true;
            }
            enemy.BossSkillCooldown = Mathf.Max(0f, enemy.BossSkillCooldown - deltaTime);
            if (enemy.BossSkillCooldown > 0f) return false;
            enemy.BossPhase = ArcadeBossPhase.Telegraph;
            enemy.BossPhaseRemaining = boss.TelegraphSeconds;
            enemy.BossChargeDirection = targetDirection.sqrMagnitude > 0.001f ? targetDirection.normalized : Vector2.up;
            enemy.BossImpactConsumed = false;
            return true;
        }

        private void TickProjectiles(ArcadeCombatRuntime runtime, float deltaTime)
        {
            CopyKeys(runtime.Projectiles, projectileIterationIds);
            for (var projectileIndex = 0; projectileIndex < projectileIterationIds.Count; projectileIndex++)
            {
                if (!runtime.Projectiles.TryGetValue(projectileIterationIds[projectileIndex], out var projectile)) continue;
                SteerRoundProjectile(runtime, projectile, deltaTime);
                var priorPosition = projectile.Position;
                projectile.Position += projectile.Velocity * deltaTime;
                projectile.RemainingSeconds -= deltaTime;
                var expired = projectile.RemainingSeconds <= 0f;
                if (!expired && TryHitGroundBarrel(runtime, projectile, priorPosition, projectile.Position))
                {
                    RemoveProjectile(runtime, projectile.Id);
                    if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || catalog == null) return;
                    continue;
                }
                var hitObstacle = IsInsideObstacle(projectile.Position, 0f, ObstaclesFor(runtime));
                if (UsesRoundLayouts(runtime))
                {
                    float firstHit = 1f;
                    foreach (var obstacle in ObstaclesFor(runtime))
                        if (TryGetSegmentObstacleEntry(priorPosition, projectile.Position, 0, obstacle, out var entry) && entry <= firstHit)
                        { firstHit = entry; hitObstacle = true; }
                    if (hitObstacle) projectile.Position = Vector2.Lerp(priorPosition, projectile.Position, firstHit);
                }
                var remove = expired ||
                             Mathf.Abs(projectile.Position.x) > catalog.Rule.ArenaHalfWidth ||
                             Mathf.Abs(projectile.Position.y) > catalog.Rule.ArenaHalfHeight || hitObstacle;
                // 只补已确认的普通三枪墙面接触，不把过期、敌弹或爆炸弹移除误当成命中。
                if (!expired && !projectile.Hostile && hitObstacle &&
                    (projectile.SourceWeaponId == "arcade.weapon.basic" ||
                     projectile.SourceWeaponId == "arcade.weapon.rapid" ||
                     projectile.SourceWeaponId == "arcade.weapon.spread"))
                    view.PlayProjectileSurfaceImpact(projectile.Position, projectile.Velocity);
                if (!projectile.Hostile && ShouldExplodeOnRemoval(projectile.Behavior, expired, hitObstacle))
                {
                    // 只在规则确认引爆时发出表现通知，普通移除和火箭寿命耗尽都保持静音。
                    view.PlayProjectileExplosion(projectile.Position, projectile.ExplosionRadius);
                    DamageEnemiesInExplosionRadius(runtime, projectile.Position, projectile.ExplosionRadius, projectile.Damage);
                }
                if (!remove && !projectile.Hostile && HitCrate(runtime, priorPosition, projectile.Position, projectile.Damage))
                {
                    if (IsExplosiveBehavior(projectile.Behavior))
                    {
                        view?.PlayProjectileExplosion(projectile.Position, projectile.ExplosionRadius);
                        DamageEnemiesInExplosionRadius(runtime, projectile.Position, projectile.ExplosionRadius, projectile.Damage);
                    }
                    remove = true;
                }
                if (!remove && projectile.Hostile)
                {
                    if (Vector2.Distance(projectile.Position, runtime.PlayerPosition) <= catalog.Rule.PlayerRadius + ProjectileHitRadius)
                    {
                        var healthBefore = runtime.PlayerHealth;
                        DamagePlayer(runtime, projectile.Damage);
                        if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || catalog == null) return;
                        if (runtime.PlayerHealth < healthBefore) OnRoundProjectileHit(runtime, projectile);
                        remove = true;
                    }
                }
                else if (!remove)
                {
                    CopyKeys(runtime.Enemies, enemyIterationIds);
                    for (var enemyIndex = 0; enemyIndex < enemyIterationIds.Count; enemyIndex++)
                    {
                        if (!runtime.Enemies.TryGetValue(enemyIterationIds[enemyIndex], out var enemy)) continue;
                        if (Vector2.Distance(projectile.Position, enemy.Position) > catalog.Rule.EnemyContactRadius + ProjectileHitRadius) continue;
                        if (IsExplosiveBehavior(projectile.Behavior))
                        {
                            view.PlayProjectileExplosion(projectile.Position, projectile.ExplosionRadius);
                            DamageEnemiesInExplosionRadius(runtime, projectile.Position, projectile.ExplosionRadius,
                                projectile.Damage);
                            remove = true;
                            break;
                        }
                        if (!TryRegisterProjectileHit(projectile, enemy.Id)) continue;
                        if(projectile.FromPlayerWeapon&&projectile.SourceWeaponId=="arcade.weapon.flamethrower")DamageEnemyFromFlame(enemy,projectile.Damage);
                        else DamageEnemy(enemy, projectile.Damage);
                        if (projectile.SourceWeaponId == "arcade.weapon.basic" ||
                            projectile.SourceWeaponId == "arcade.weapon.rapid" ||
                            projectile.SourceWeaponId == "arcade.weapon.spread")
                            view.PlayImpactSound(enemy.Position, false);
                        if (enemy.Health <= 0) KillEnemy(runtime, enemy);
                        remove = projectile.Behavior != global::ArcadeWeaponBehavior.Piercing ||
                                 projectile.RemainingHits <= 0;
                        if (remove) break;
                    }
                }
                if (remove) RemoveProjectile(runtime, projectile.Id);
            }
        }

        /// <summary>电球、锯片等普通圆形伤害不附带爆炸击飞。</summary>
        private void DamageEnemiesInRadius(ArcadeCombatRuntime runtime, Vector2 center, float radius, int damage)
            => DamageEnemiesInArea(runtime, center, radius, damage, false);

        /// <summary>真实爆炸来源显式选择击飞类别，伤害与目标筛选仍沿用同一结算。</summary>
        private void DamageEnemiesInExplosionRadius(ArcadeCombatRuntime runtime, Vector2 center, float radius, int damage)
            => DamageEnemiesInArea(runtime, center, radius, damage, true);

        private void DamageEnemiesInArea(ArcadeCombatRuntime runtime, Vector2 center, float radius, int damage, bool explosive)
        {
            if (radius <= 0f) return;
            DamageCratesInRadius(runtime, center, radius, damage);
            CopyKeys(runtime.Enemies, enemyIterationIds);
            for (var index = 0; index < enemyIterationIds.Count; index++)
            {
                if (!runtime.Enemies.TryGetValue(enemyIterationIds[index], out var enemy) ||
                    !IsWithinExplosion(center, enemy.Position, radius)) continue;
                DamageEnemy(enemy, damage);
                if (enemy.Health <= 0) KillEnemyWithImpact(runtime, enemy, false, explosive, center);
            }
        }

        private void DamageAndKnockbackEnemies(ArcadeCombatRuntime runtime, Vector2 center, float radius,
            int damage, float knockbackDistance)
        {
            CopyKeys(runtime.Enemies, enemyIterationIds);
            for (var index = 0; index < enemyIterationIds.Count; index++)
            {
                if (!runtime.Enemies.TryGetValue(enemyIterationIds[index], out var enemy) ||
                    !IsWithinExplosion(center, enemy.Position, radius)) continue;
                var direction = enemy.Position - center;
                if (direction.sqrMagnitude < 0.001f) direction = Vector2.up;
                var displaced = enemy.Position + direction.normalized * knockbackDistance;
                // 未上岸的敌人仍可受击，但不能因通用击退夹取瞬间跳到岸内。
                enemy.Position = ResolvePosition(displaced, catalog.Rule.EnemyContactRadius,
                    catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), IslandFor(runtime));
                DamageEnemy(enemy, damage);
                if (enemy.Health <= 0) KillEnemy(runtime, enemy);
            }
        }

        private static bool HasEnemyInRadius(ArcadeCombatRuntime runtime, Vector2 center, float radius)
        {
            var squaredRadius = radius * radius;
            foreach (var enemy in runtime.Enemies.Values)
                if ((enemy.Position - center).sqrMagnitude <= squaredRadius) return true;
            return false;
        }

        private static bool TryGetPowerup(ArcadeCombatRuntime runtime, global::ArcadePowerupBehavior behavior,
            out ArcadePowerupRuntime powerup) => runtime.Powerups.TryGetValue(behavior, out powerup) &&
                                                 powerup.RemainingSeconds > 0f;

        private static float EnemySpeedMultiplier(ArcadeCombatRuntime runtime) =>
            TryGetPowerup(runtime, global::ArcadePowerupBehavior.Clock, out var clock)
                ? Mathf.Max(0f, clock.Definition.Magnitude)
                : 1f;

        /// <summary>地面物资独立于拾取资格计时；过期先注销，避免同帧拾取补发或满额物资永久占预算。</summary>
        private void TickDropLifetimes(ArcadeCombatRuntime runtime, float seconds)
        {
            CopyKeys(runtime.Drops, dropIterationIds);
            foreach (var id in dropIterationIds)
            {
                var drop = runtime.Drops[id];
                drop.RemainingSeconds = Mathf.Max(0f, drop.RemainingSeconds - seconds);
                if (drop.RemainingSeconds > 0f) continue;
                runtime.Drops.Remove(id);
                view?.RemoveDrop(id);
                runtimePool.Return(drop);
            }
        }

        /// <summary>最后3秒按权威剩余寿命线性渐隐，表现本身不决定拾取或过期。</summary>
        internal static float GroundDropOpacity(float remainingSeconds) => Mathf.Clamp01(remainingSeconds / 3f);

        /// <summary>逐件裁决范围内掉落；满额只拒绝对应补给，成功实例先注销再发奖和反馈。</summary>
        private void TickDrops(ArcadeCombatRuntime runtime)
        {
            var pickupBonus = UpgradeTotal(this.GetModel<ArcadeRunModel>().Current, global::ArcadeUpgradeKind.PickupRadiusPercent);
            var magnetRadius = TryGetPowerup(runtime, global::ArcadePowerupBehavior.Magnet, out var magnet)
                ? SlayerMartRadius(runtime,magnet)
                : 0f;
            var pickupRadius = CalculatePickupRadius(1.1f, pickupBonus, magnetRadius);
            CopyKeys(runtime.Drops, dropIterationIds);
            for (var index = 0; index < dropIterationIds.Count; index++)
            {
                if (!runtime.Drops.TryGetValue(dropIterationIds[index], out var drop)) continue;
                if (Vector2.Distance(drop.Position, runtime.PlayerPosition) > pickupRadius) continue;
                var definition = drop.Definition;
                bool supply = definition.Kind == global::ArcadeDropKind.Bomb || definition.Kind == global::ArcadeDropKind.Dash;
                int added = supply ? SupplyAddedAmount(definition.Kind == global::ArcadeDropKind.Bomb
                    ? runtime.Bombs : runtime.Dashes, definition) : 0;
                if (supply && added <= 0) continue;
                if (!CanApplyDrop(definition)) continue;
                // 视角准备失败保留地面物品；相机提交成功后才注销并提示。
                bool firstPerson = definition.Kind == global::ArcadeDropKind.FirstPerson;
                if (firstPerson && !TryBeginFirstPerson(runtime, definition, out _)) continue;
                long scoreBefore = this.GetModel<ArcadeRunModel>().Current?.Score ?? 0;
                long goldBefore = runtime.ArenaGold;
                // 先移出权威集合，防止同步反馈或重复范围检查再次结算同一掉落。
                if (!runtime.Drops.Remove(drop.Id)) continue;
                if(runtime.SlayerChallenge!=null&&definition.Id==slayerChallengeConfig.CoinDropId)
                {runtime.SlayerChallenge=ArcadeSlayerChallengeRules.CollectCoin(runtime.SlayerChallenge,slayerChallengeConfig,drop.Id);view.RemoveDrop(drop.Id);runtimePool.Return(drop);continue;}
                if (!ApplyDrop(runtime, definition))
                {
                    runtime.Drops.Add(drop.Id, drop);
                    continue;
                }
                view.RemoveDrop(drop.Id);
                runtimePool.Return(drop);
                if (definition.Kind == global::ArcadeDropKind.RunGold)
                    view.PlayTreasurePickup((this.GetModel<ArcadeRunModel>().Current?.Score ?? scoreBefore) - scoreBefore,
                        runtime.ArenaGold - goldBefore);
                if (supply) view.PlaySupplyPickup(definition, added);
                if (firstPerson) view.PlayFirstPersonPickup();
                if (definition.Kind == global::ArcadeDropKind.TemporaryWeapon || definition.Kind == global::ArcadeDropKind.Powerup)
                    view.PlayProductPickup(definition.ProductId);
            }
        }

        /// <summary>结算全部并存强化；固定设施使用拾取点，随身效果使用玩家当前位置。</summary>
        private void TickPowerups(ArcadeCombatRuntime runtime, float deltaTime)
        {
            if(slayerMartConfig!=null)FirepowerLoop.Arcade.ArcadeSlayerMartSupportRules.AdvanceOrdinaryOrbits(SlayerMartAbility(runtime),deltaTime,slayerMartConfig.Parameter);
            if (runtime.Powerups.Count == 0 || runtime.Enemies.Count == 0) return;
            foreach (var pair in runtime.Powerups)
            {
                var powerup = pair.Value;
                if (TickSlayerMartOrdinaryOrbit(runtime,powerup)) continue;
                if (powerup.CooldownSeconds > 0f) continue;
                switch (pair.Key)
                {
                    case global::ArcadePowerupBehavior.Chicken:
                        FirePowerupProjectile(runtime, powerup, runtime.PlayerPosition, 0f);
                        break;
                    case global::ArcadePowerupBehavior.Sentry:
                        FirePowerupProjectile(runtime, powerup, powerup.Position, powerup.Definition.Radius);
                        break;
                    case global::ArcadePowerupBehavior.ElectricOrb:
                    case global::ArcadePowerupBehavior.Saw:
                        DamageEnemiesInRadius(runtime, runtime.PlayerPosition, powerup.Definition.Radius,
                            powerup.Definition.Damage);
                        powerup.CooldownSeconds = powerup.Definition.IntervalSeconds;
                        break;
                    case global::ArcadePowerupBehavior.Gloves:
                        DamageAndKnockbackEnemies(runtime, runtime.PlayerPosition, powerup.Definition.Radius,
                            powerup.Definition.Damage, powerup.Definition.Magnitude);
                        powerup.CooldownSeconds = powerup.Definition.IntervalSeconds;
                        break;
                    case global::ArcadePowerupBehavior.Barrel:
                        if (powerup.Charges > 0 && HasEnemyInRadius(runtime, runtime.PlayerPosition,
                                powerup.Definition.Magnitude + catalog.Rule.EnemyContactRadius))
                        {
                            DamageEnemiesInExplosionRadius(runtime, runtime.PlayerPosition, powerup.Definition.Radius,
                                powerup.Definition.Damage);
                            powerup.Charges--;
                            powerup.CooldownSeconds = 0.45f;
                            if (powerup.Charges <= 0) powerup.RemainingSeconds = 0f;
                        }
                        break;
                }
            }
        }

        /// <summary>周期到期时朝范围内最近敌人发射；仅有弹丸预算时记录方向，供哨塔视觉同步。</summary>
        private void FirePowerupProjectile(ArcadeCombatRuntime runtime, ArcadePowerupRuntime powerup,
            Vector2 origin, float maxRadius)
        {
            ArcadeEnemyRuntime nearest = null;
            var nearestDistance = float.MaxValue;
            foreach (var enemy in runtime.Enemies.Values)
            {
                var distance = (enemy.Position - origin).sqrMagnitude;
                if (distance >= nearestDistance || maxRadius > 0f && distance > maxRadius * maxRadius) continue;
                nearest = enemy;
                nearestDistance = distance;
            }
            if (nearest == null) return;
            var direction = (nearest.Position - origin).normalized;
            if (runtime.Projectiles.Count < catalog.Rule.MaxProjectiles)
                powerup.ShotDirection = direction;
            SpawnProjectile(runtime, false, origin, direction * powerup.Definition.ProjectileSpeed,
                SlayerMartDamage(runtime,powerup.Definition.Damage,powerup.Definition.Behavior==global::ArcadePowerupBehavior.Sentry?SlayerMartDamageSource.Sentry:
                    powerup.Definition.Behavior==global::ArcadePowerupBehavior.Chicken?SlayerMartDamageSource.Chicken:SlayerMartDamageSource.Orb), SupportProjectileLifetimeSeconds);
            powerup.CooldownSeconds = powerup.Definition.IntervalSeconds;
        }

        private void TickWaveTransition(ArcadeCombatRuntime runtime, float deltaTime)
        {
            if(runtime.SlayerChallenge!=null)
            {
                if(runtime.SlayerChallenge.Phase==ArcadeSlayerChallengePhase.Collecting&&runtime.SpawnQueue.Count==0&&runtime.Enemies.Count==0)BeginSlayerCollectionWave(runtime);
                return;
            }
            if (runtime.IsFinalBoss || runtime.AwaitingExit || runtime.InTreasureRoom ||
                runtime.ThemeBossStarted && !runtime.ThemeBossDefeated) return;
            if (runtime.SpawnQueue.Count > 0 || runtime.Enemies.Count > 0) return;
            // 等待已配置奖励房的入口接触及返回，不能提前显示普通下一轮出口。
            if (runtime.WaveIndex == 2 && (arena.Opening.Enabled || runtime.TreasureRoom != null) &&
                this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute == true && !runtime.TreasureCompleted) return;
            if (!runtime.WaveClearAwarded)
            {
                if (!runtime.IsChallenge && !UsesEarlyRounds(runtime))
                    // 清轮奖励仍来自原轮配置，不能随实际生成组回退而改价。
                    runtime.ArenaGold += ScaleGold(FindGroup(arena.WaveGroupIds[runtime.WaveIndex]).ClearGold, runtime.ArenaNumber, catalog.Rule.GoldGrowthPerArena);
                runtime.WaveClearAwarded = true;
                if (UsesContentRounds(runtime))
                {
                    runtime.WaveTransitionRemaining = 0;
                    foreach (var id in runtime.Projectiles.Keys.ToArray()) RemoveProjectile(runtime, id);
                    var layout = RoundLayout(runtime);
                    if (runtime.WaveIndex == 3 && layout.BossKind > 0)
                    {
                        if (layout.BossKind == 4) BeginFinalBoss(runtime); else BeginThemeBoss(runtime);
                    }
                    else
                    { runtime.IslandRoundAwaitingExit = runtime.WaveIndex < 3; runtime.AwaitingExit = runtime.WaveIndex == 3; }
                    return;
                }
                if (UsesIslandRounds(runtime))
                {
                    runtime.WaveTransitionRemaining = 0;
                    foreach (var projectile in runtime.Projectiles.Values.ToArray()) RemoveProjectile(runtime, projectile.Id);
                    runtime.IslandRoundAwaitingExit = runtime.WaveIndex < 3;
                    runtime.AwaitingExit = runtime.WaveIndex == 3;
                    return;
                }
                runtime.RestAttackReleased = false;
                runtime.WaveTransitionRemaining = UsesEarlyRest(runtime)
                    ? arena.Opening.RoundRestSeconds : WaveTransitionSeconds;
                // 最后一轮清场立即展示出口，五分钟时限在出口状态推进。
                if (UsesEarlyRest(runtime) && runtime.WaveIndex + 1 == arena.WaveGroupIds.Count)
                {
                    runtime.AwaitingExit = true;
                    runtime.WaveTransitionRemaining = arena.Opening.ExitRestSeconds;
                }
                return;
            }
            if (UsesRoundLayouts(runtime)) return;
            runtime.WaveTransitionRemaining -= deltaTime;
            if (!runtime.Firing) runtime.RestAttackReleased = true;
            if (UsesEarlyRest(runtime) && runtime.RestAttackReleased && runtime.Firing)
                runtime.WaveTransitionRemaining = 0f;
            if (runtime.WaveTransitionRemaining > 0f) return;
            var nextWave = runtime.WaveIndex + 1;
            var waveCount = runtime.IsChallenge ? 1 : arena.WaveGroupIds.Count;
            if (nextWave < waveCount) BeginWave(runtime, nextWave);
            else if (runtime.IsChallenge) CompleteChallenge(runtime, true);
            else if (this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute == true)
            {
                if (!string.IsNullOrEmpty(arena.FinalBossId)) BeginFinalBoss(runtime);
                else runtime.AwaitingExit = true;
            }
            else CompleteArena(runtime);
        }

        private ArcadeEnemyGroupDefinition FindGroup(string id) =>
            catalog.EnemyGroups.First(value => string.Equals(value.Id, id, StringComparison.Ordinal));

        private ArcadeBossDefinition ResolveFinalBoss() =>
            catalog.Bosses.First(value => string.Equals(value.Id, arena.FinalBossId, StringComparison.Ordinal));

        /// <summary>末场四轮之后启动独立战斗；沿用本场生命、武器与轮次，只切换敌群现场。</summary>
        private void BeginFinalBoss(ArcadeCombatRuntime runtime)
        {
            if (runtime.IsFinalBoss) return;
            runtime.IsFinalBoss = true;
            if (UsesContentRounds(runtime)) InitializeRoundContent(runtime);
            runtime.Hazards.Clear();
            runtime.SpawnQueue.Clear();
            var boss = ResolveFinalBoss();
            group = FindGroup(boss.EnemyGroupId);
            runtime.SpawnQueue.Enqueue(group.Members[0].EnemyId);
            runtime.SpawnCooldown = 0f;
            runtime.ReinforcementCooldown = boss.ReinforcementIntervalSeconds;
        }

        /// <summary>仅在 Boss 已生成且存活时补充普通敌群；不堆积未生成队列，并继续服从同屏上限。</summary>
        private void TickFinalBossReinforcements(ArcadeCombatRuntime runtime, float deltaTime)
        {
            if (!runtime.IsFinalBoss || runtime.FinalBossDefeated || runtime.FinalBossEnemyId == 0) return;
            runtime.ReinforcementCooldown = Mathf.Max(0f, runtime.ReinforcementCooldown - deltaTime);
            if (runtime.ReinforcementCooldown > 0f || runtime.SpawnQueue.Count > 0) return;
            var boss = ResolveFinalBoss();
            if (string.IsNullOrEmpty(boss.ReinforcementGroupId)) return;
            group = FindGroup(boss.ReinforcementGroupId);
            foreach (var member in group.Members)
                for (var index = 0; index < member.Count; index++) runtime.SpawnQueue.Enqueue(member.EnemyId);
            runtime.ReinforcementCooldown = boss.ReinforcementIntervalSeconds;
        }

        /// <summary>出口进入由规则位置判定，不依赖表现 Collider；失败时仍保留同一选择供重试。</summary>
        private void TickExit(ArcadeCombatRuntime runtime)
        {
            if (UsesContentRounds(runtime)) { TickContentExit(runtime); return; }
            if (runtime.TreasureRoom?.Members.Length > 0) return;
            var count = runtime.FinalBossDefeated ? 1 : arena.NextArenaNumbers.Count;
            for (var index = 0; index < count; index++)
            {
                var position = ArcadeExitPosition(index, count, catalog.Rule.ArenaHalfHeight);
                if ((runtime.PlayerPosition - position).sqrMagnitude > 1.5f * 1.5f) continue;
                runtime.SelectedExitConfigNumber = runtime.FinalBossDefeated ? 0 : arena.NextArenaNumbers[index];
                CompleteArena(runtime);
                return;
            }
        }

        internal static Vector2 ArcadeExitPosition(int index, int count, float halfHeight) =>
            new Vector2((index - (count - 1) * .5f) * 5f, halfHeight - 3f);

        /// <summary>普通轮开始时补空库存并建立完整待生成队列；总量按本局累计轮，分支不重置难度。</summary>
        private void BeginWave(ArcadeCombatRuntime runtime, int waveIndex)
        {
            runtime.IslandSpawnSequence = 0;
            InitializeCrates(runtime);
            runtime.WaveIndex = waveIndex;
            InitializeWeaponPickups(runtime);
            runtime.SpawnQueue.Clear();
            runtime.SpawnCooldown = 0f;
            runtime.WaveTransitionRemaining = 0f;
            runtime.WaveClearAwarded = false;
            runtime.RestAttackReleased = false;
            if (!runtime.IsChallenge&&!runtime.SlayerMartRoundBenefitsPending)
            {
                runtime.Bombs = FirepowerLoop.Arcade.ArcadeOpeningRules.ReplenishEmpty(runtime.Bombs);
                runtime.Dashes = FirepowerLoop.Arcade.ArcadeOpeningRules.ReplenishEmpty(runtime.Dashes);
            }
            var groupId = runtime.IsChallenge ? challenge.EnemyGroupId : arena.WaveGroupIds[waveIndex];
            group = null;
            foreach (var candidate in catalog.EnemyGroups)
            {
                if (!string.Equals(candidate.Id, groupId, StringComparison.Ordinal)) continue;
                group = candidate;
                break;
            }
            if (group == null) throw new InvalidOperationException($"街机敌人组不存在：{groupId}");
            bool casualOrdinary = this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute == true &&
                !runtime.IsChallenge && !runtime.IsFinalBoss && !runtime.ThemeBossStarted;
            if (casualOrdinary)
            {
                // 保留普通组的比例与生成预算；旧单体Boss占位只借用本场前序普通组。
                bool Ordinary(ArcadeEnemyGroupDefinition candidate) => candidate != null && candidate.MaxAlive > 1 && candidate.Members.Count > 0 &&
                    !catalog.Bosses.Any(b => string.Equals(b.EnemyGroupId, candidate.Id, StringComparison.Ordinal));
                if (!Ordinary(group))
                {
                    group = arena.WaveGroupIds.Take(waveIndex).Reverse().Select(FindGroup).FirstOrDefault(Ordinary);
                    if (group == null) throw new InvalidOperationException("本场缺少普通敌群，不能放大旧Boss组");
                }
            }
            var totalWeight = group.Members.Sum(member => member.Count);
            var total = casualOrdinary
                ? FirepowerLoop.Arcade.ArcadeOpeningRules.OrdinaryRoundTotal((runtime.ArenaNumber - 1) * 4 + waveIndex + 1, 1)
                : totalWeight;
            var previousWeight = 0;
            foreach (var member in group.Members)
            {
                var count = FirepowerLoop.Arcade.ArcadeOpeningRules.AllocateMember(total, previousWeight, member.Count, totalWeight);
                for (var index = 0; index < count; index++) runtime.SpawnQueue.Enqueue(member.EnemyId);
                previousWeight += member.Count;
            }
        }

        /// <summary>普通地图开场只建立错峰刷新计划，切轮或特殊现场取消旧计划。</summary>
        private void InitializeWeaponPickups(ArcadeCombatRuntime runtime)
        {
            runtime.WeaponSpawns=null;
            if(runtime.IsChallenge||runtime.IsFinalBoss||runtime.ThemeBossStarted||runtime.SlayerChallenge!=null||runtime.SlayerMartReturnWave>0)return;
            var layout=RoundLayout(runtime);
            if(layout==null)return;
            runtime.WeaponSpawns=layout.CreateWeaponSpawnSchedule(random);
        }

        /// <summary>仅活动战斗计时；每次最多创建一把武器，不在暂停、清场或房间内补刷。</summary>
        private void TickWeaponPickups(ArcadeCombatRuntime runtime,float seconds)
        {
            if(runtime.WeaponSpawns==null||runtime.IsChallenge||runtime.IsFinalBoss||runtime.ThemeBossStarted||
                runtime.SlayerChallenge!=null||runtime.SlayerMartReturnWave>0||runtime.PlayerHealth<=0||runtime.RespawnRemaining>0||
                runtime.InTreasureRoom||runtime.IslandTransitionPending||runtime.IslandRoomTransitionPending||runtime.WaveClearAwarded||
                runtime.IslandRoundAwaitingExit||runtime.AwaitingExit||runtime.CompletionSubmitted||runtime.CompletionAwaitingRetry)return;
            if(!runtime.WeaponSpawns.TryAdvance(seconds,runtime.Drops.Count<catalog.Rule.MaxDrops,out var point))return;
            var candidates=catalog.Drops.Where(d=>d.Kind==global::ArcadeDropKind.TemporaryWeapon&&d.Weight>0&&
                IsDropEligible(d,runtime.ArenaNumber)&&CanApplyDrop(d)).ToArray();
            int total=candidates.Sum(d=>d.Weight);
            if(total<=0)return;
            int roll=runtime.WeaponSpawns.NextWeaponRoll(total);
            foreach(var candidate in candidates)
            {
                if(roll>=candidate.Weight){roll-=candidate.Weight;continue;}
                var drop=runtimePool.RentDrop();
                drop.Id=runtime.NextEntityId++;drop.Definition=candidate;
                drop.Position=new Vector2(point.X,point.Y);
                runtime.Drops.Add(drop.Id,drop);
                break;
            }
        }

        /// <summary>攻击冷却结束且瞄准有效时，按当前临时或已购武器结算近战/发射弹丸；不消耗基地弹药。</summary>
        private void TryFire(ArcadeCombatRuntime runtime)
        {
            if(runtime.SlayerMartReturnWave>0)return;
            if (runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending || runtime.CompletionAwaitingRetry || runtime.CompletionSubmitted) return;
            if (runtime.AwaitingExit || runtime.RespawnRemaining > 0f || runtime.FireCooldown > 0f || runtime.AimDirection.sqrMagnitude < 0.001f) return;
            if (!TryGetWeapon(runtime, out var weapon)) return;
            var run = this.GetModel<ArcadeRunModel>().Current;
            var damageBonus = UpgradeTotal(run, global::ArcadeUpgradeKind.DamagePercent);
            var fireRateBonus = UpgradeTotal(run, global::ArcadeUpgradeKind.FireRatePercent);
            var damage = Mathf.Max(1, Mathf.RoundToInt(weapon.Damage * (1f + damageBonus)));
            damage=SlayerMartDamage(runtime,damage,SlayerMartDamageSource.Weapon);
            if (weapon.Behavior == global::ArcadeWeaponBehavior.Melee)
            {
                // 近战在按下攻击的当帧按面向结算，仍通过同一开火序列驱动共享攻击片段。
                CopyKeys(runtime.Enemies, enemyIterationIds);
                for (var index = 0; index < enemyIterationIds.Count; index++)
                {
                    if (!runtime.Enemies.TryGetValue(enemyIterationIds[index], out var enemy) ||
                        !IsWithinMeleeStrike(runtime.PlayerPosition, runtime.AimDirection, enemy.Position,
                            weapon.MeleeRange, weapon.MeleeArcDegrees) ||
                        SegmentCrossesObstacle(runtime.PlayerPosition, enemy.Position, 0f,
                            ObstaclesFor(runtime))) continue;
                    DamageEnemy(enemy, damage);
                    view.PlayImpactSound(enemy.Position, true);
                    if (enemy.Health <= 0) KillEnemy(runtime, enemy);
                    if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || catalog == null) return;
                }
                runtime.PlayerFireSequence++;
                runtime.FireCooldown = weapon.FireIntervalSeconds / Mathf.Max(1f, 1f + fireRateBonus);
                return;
            }
            var projectileCount = Mathf.Max(1, weapon.ProjectileCount);
            var spawned = false;
            for (var index = 0; index < projectileCount; index++)
            {
                if (runtime.Projectiles.Count >= catalog.Rule.MaxProjectiles) break;
                var angle = projectileCount == 1
                    ? 0f
                    : -weapon.SpreadDegrees * 0.5f + weapon.SpreadDegrees * index / (projectileCount - 1);
                var direction = Rotate(runtime.AimDirection.normalized, angle);
                SpawnProjectile(runtime, false, runtime.PlayerPosition + direction * 0.65f,
                    direction * weapon.ProjectileSpeed, damage, weapon.ProjectileLifetimeSeconds,
                    weapon.Behavior, weapon.MaxHits, weapon.ExplosionRadius, weapon.ProjectileVisualResourceName, true, weapon.Id);
                spawned = true;
            }
            if (!spawned) return;
            runtime.PlayerFireSequence++;
            runtime.FireCooldown = weapon.FireIntervalSeconds / Mathf.Max(1f, 1f + fireRateBonus);
        }

        /// <summary>每次攻击与表现同步时解析武器；旧局近战装备回退基础枪，不改写安全节点。</summary>
        private bool TryGetWeapon(ArcadeCombatRuntime runtime, out ArcadeWeaponDefinition weapon)
        {
            var run = this.GetModel<ArcadeRunModel>().Current;
            string savedWeapon = null;
            run?.EquipmentBySlot.TryGetValue(ArcadeRunDefaults.WeaponSlotId, out savedWeapon);
            var id = runtime.TemporaryWeaponRemaining > 0f ? runtime.TemporaryWeaponId : savedWeapon;
            if (string.IsNullOrWhiteSpace(id)) id = catalog.Rule.BaseWeaponId;
            foreach (var candidate in catalog.Weapons)
            {
                if (!string.Equals(candidate.Id, id, StringComparison.Ordinal)) continue;
                if (candidate.Behavior == global::ArcadeWeaponBehavior.Melee)
                {
                    weapon = catalog.Weapons.FirstOrDefault(value => value.Id == catalog.Rule.BaseWeaponId);
                    return !string.IsNullOrWhiteSpace(weapon.Id);
                }
                weapon = candidate;
                return true;
            }
            weapon = default;
            return false;
        }

        private void UseBomb(ArcadeCombatRuntime runtime)
        {
            if(runtime.SlayerMartReturnWave>0)return;
            if (runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending || runtime.CompletionAwaitingRetry || runtime.CompletionSubmitted) return;
            if (runtime.AwaitingExit || runtime.InTreasureRoom || runtime.Bombs <= 0 || runtime.RespawnRemaining > 0f) return;
            runtime.Bombs--;
            if (IslandFor(runtime) != null) runtime.PlayerInvulnerability = Mathf.Max(runtime.PlayerInvulnerability, 3f);
            if (runtime.InTreasureRoom) { PublishHud(runtime); return; }
            // 成功消耗后先发出声音表现，清场可能立即结束本轮并释放战场。
            view?.PlayBombExplosion(runtime.PlayerPosition);
            CopyKeys(runtime.Enemies, enemyIterationIds);
            for (var index = 0; index < enemyIterationIds.Count; index++)
                if (runtime.Enemies.TryGetValue(enemyIterationIds[index], out var enemy) &&
                    enemy.Id != runtime.FinalBossEnemyId && enemy.Id != runtime.ContentBossEnemyId)
                    KillEnemyWithImpact(runtime, enemy, false, true, runtime.PlayerPosition);
            PublishHud(runtime);
        }

        /// <summary>消耗一次冲刺并锁定方向；无移动输入时沿角色当前朝向，后续松键不取消位移。</summary>
        private void UseDash(ArcadeCombatRuntime runtime)
        {
            if(runtime.SlayerMartReturnWave>0)return;
            if (runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending || runtime.CompletionAwaitingRetry || runtime.CompletionSubmitted) return;
            if (runtime.Dashes <= 0 || runtime.RespawnRemaining > 0f || runtime.DashRemaining > 0f) return;
            ArcadeDashRules.ResolveDirection(runtime.MoveDirection.x, runtime.MoveDirection.y,
                runtime.AimDirection.x, runtime.AimDirection.y, out var dashX, out var dashY);
            runtime.DashDirection = new Vector2(dashX, dashY);
            runtime.Dashes--;
            runtime.DashRemaining = DashSeconds;
            runtime.PlayerInvulnerability = Mathf.Max(runtime.PlayerInvulnerability, DashSeconds);
            PublishHud(runtime);
        }

        /// <summary>普通战斗帧推进机关；统一受伤入口负责护盾/无敌/死亡，机关击杀仅关闭随机掉落。</summary>
        private void TickHazards(ArcadeCombatRuntime runtime, float seconds, Vector2 previousPlayer)
        {
            if (runtime.Hazards.Count == 0 || runtime.RespawnRemaining > 0f || runtime.WaveClearAwarded) return;
            runtime.HazardElapsedSeconds += seconds;
            foreach (var hazard in runtime.Hazards) hazard.Advance(seconds);
            foreach (var hazard in runtime.Hazards)
            {
                if (runtime.PlayerInvulnerability <= 0f && runtime.DashRemaining <= 0f &&
                    hazard.TryHit(-1, previousPlayer.x, previousPlayer.y, runtime.PlayerPosition.x,
                        runtime.PlayerPosition.y, catalog.Rule.PlayerRadius))
                {
                    DamagePlayerCore(runtime, FirepowerLoop.Arcade.ArcadeHazardRules.Damage(runtime.PlayerMaxHealth,
                        hazard.Parameters.PlayerDamageRatio),hazard.Parameters.PlayerDamageRatio>=1?SlayerMartHitKind.LethalMechanism:SlayerMartHitKind.Ordinary);
                    if (runtime.CompletionSubmitted || this.GetModel<ArcadeCombatModel>().Runtime != runtime || runtime.RespawnRemaining > 0f) return;
                }
                enemyIterationIds.Clear();
                foreach (var enemy in runtime.Enemies.Values) enemyIterationIds.Add(enemy.Id);
                foreach (var id in enemyIterationIds)
                {
                    if (!runtime.Enemies.TryGetValue(id, out var enemy)) continue;
                    var previous = runtime.HazardPreviousEnemyPositions.TryGetValue(id, out var value) ? value : enemy.Position;
                    if (!hazard.TryHit(id, previous.x, previous.y, enemy.Position.x, enemy.Position.y, catalog.Rule.EnemyContactRadius)) continue;
                    if (enemy.IsBossPresentation)
                        enemy.Health -= FirepowerLoop.Arcade.ArcadeHazardRules.Damage(enemy.MaxHealth, hazard.Parameters.BossDamageRatio);
                    else enemy.Health = 0;
                    if (enemy.Health <= 0) KillEnemy(runtime, enemy, true);
                }
            }
        }

        private void DamagePlayer(ArcadeCombatRuntime runtime, int damage)
            => DamagePlayerCore(runtime,damage,SlayerMartHitKind.Ordinary);

        /// <summary>正式挡伤入口；失命机关显式跳过新增护甲，旧保护与护盾优先级保留。</summary>
        private void DamagePlayerCore(ArcadeCombatRuntime runtime,int damage,SlayerMartHitKind kind)
        {
            if (lastDeathReturnPending) return;
            if(runtime.SlayerChallenge!=null&&runtime.SlayerChallenge.Phase!=ArcadeSlayerChallengePhase.Collecting&&runtime.SlayerChallenge.Phase!=ArcadeSlayerChallengePhase.Boss)return;
            if (runtime.PlayerInvulnerability > 0f || runtime.DashRemaining > 0f || runtime.RespawnRemaining > 0f) return;
            if (TryConsumeShield(runtime))
            {
                runtime.PlayerInvulnerability = catalog.Rule.ContactInvulnerabilitySeconds;
                view?.PlayShieldBlock(0, runtime.PlayerPosition);
                return;
            }
            var ability=runtime.SlayerMartRulesVersion==1?runtime.SlayerMart?.Players.FirstOrDefault(p=>p.PlayerId==1):null;
            if(ArcadeSlayerMartAbilityRules.TryBlockHit(kind,ability,false,false)==SlayerMartBlockResult.Plate)
            {
                runtime.PlayerInvulnerability=catalog.Rule.ContactInvulnerabilitySeconds;
                view?.PlayShieldBlock(0,runtime.PlayerPosition);
                return;
            }
            runtime.PlayerHealth -= Mathf.Max(1, damage);
            runtime.PlayerInvulnerability = catalog.Rule.ContactInvulnerabilitySeconds;
            if (runtime.PlayerHealth > 0) return;
            EndFirstPerson(runtime);

            if (runtime.IsChallenge)
            {
                if(runtime.SlayerChallenge!=null){DownSlayerPlayer(runtime);return;}
                ArcadeSlayerMartAbilityRules.OnDeath(ability);
                ClearSlayerMartOrdinarySupports(runtime);
                runtime.PlayerHealth = 0;
                CompleteChallenge(runtime, false);
                return;
            }

            ResetTreasureMultiplierAfterDeath();
            if (this.GetModel<ArcadeRunModel>().Current?.RemainingLives == 1)
            {
                runtime.PlayerHealth = 0; runtime.CompletionSubmitted = true;
                ClearArcadeInput(runtime); lastDeathReturnPending = true;
                FinishLastDeathAsync(runtime, deathReturnGeneration).Forget();
                return;
            }
            var result = this.GetSystem<ArcadeRunSystem>().TryHandleDeath(out _, out var error);
            if (result == ArcadeDeathResult.Respawn)
            {
                ArcadeSlayerMartAbilityRules.OnDeath(ability);
                ClearSlayerMartOrdinarySupports(runtime);
                runtime.PlayerHealth = 0;
                runtime.RespawnRemaining = catalog.Rule.RespawnDelaySeconds;
                runtime.PlayerPosition = Vector2.zero;
            }
            else if (result == ArcadeDeathResult.RunFailed)
            {
                EndCombat();
                this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync().Forget();
            }
            else
            {
                runtime.PlayerHealth = 1;
                this.SendEvent(new ArcadeCombatOperationFailedEvent(error));
            }
        }

        /// <summary>冻结原现场并等待后台原子写盘；成功才卸载，失败恢复一血及原竞技场供重试。</summary>
        private async UniTaskVoid FinishLastDeathAsync(ArcadeCombatRuntime runtime, int generation)
        {
            var app = this.GetSystem<AppStateSystem>();
            var previousState = this.GetModel<AppStateModel>().CurrentState.Value;
            LoadingUICtrl loading = null; int loadingRequest = 0;
            try
            {
                app.TryTransition(AppState.Loading);
                loading = UIHelper.ShowUI<LoadingUICtrl>();
                loadingRequest = loading.BeginEntryPreparation(MapIds.Arcade);
                var result = await this.GetSystem<ArcadeRunSystem>().HandleLastDeathAsync();
                if (generation != deathReturnGeneration) return;
                if (result.Result != ArcadeDeathResult.RunFailed)
                {
                    RestoreFailedLastDeath(runtime, previousState, result.Error);
                    return;
                }
                // 成功事件已结束战斗；先让遮罩完成一帧绘制，再启动场景卸载，不能把两段清理挤在死亡帧。
                await UniTask.NextFrame();
                if (generation != deathReturnGeneration) return;
                await this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync();
            }
            catch (Exception error)
            {
                if (generation == deathReturnGeneration) RestoreFailedLastDeath(runtime, previousState, error.Message);
            }
            finally
            {
                if (generation == deathReturnGeneration) loading?.EndEntryPreparation(loadingRequest);
                lastDeathReturnPending = false;
            }
        }

        /// <summary>仅未提交的原现场允许回退；已成功保存结果时不能恢复为可战斗状态。</summary>
        private void RestoreFailedLastDeath(ArcadeCombatRuntime runtime, AppState previousState, string error)
        {
            if (this.GetModel<ArcadeCombatModel>().Runtime == runtime && this.GetModel<ArcadeRunModel>().Phase == ArcadeRunPhase.Arena)
            {
                runtime.PlayerHealth = 1; runtime.CompletionSubmitted = false;
                this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(previousState);
            }
            this.SendEvent(new ArcadeCombatOperationFailedEvent(error));
        }

        private void KillEnemy(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy, bool hazardKill = false)
            => KillEnemyWithImpact(runtime, enemy, hazardKill, false, runtime.PlayerPosition);

        /// <summary>伤害已裁决后传递死亡方向；爆炸来源只影响骨骼表现，不改变奖励或场内预算。</summary>
        private void KillEnemyWithImpact(ArcadeCombatRuntime runtime, ArcadeEnemyRuntime enemy, bool hazardKill,
            bool explosive, Vector2 origin)
        {
            if (!runtime.Enemies.Remove(enemy.Id)) return;
            if(hazardKill||explosive)enemy.LastDamageWasFlame=false;
            if(SlayerGuardianDefeated(runtime,enemy))return;

            if(slayerMartConfig!=null)ArcadeSlayerMartAbilityRules.RegisterKill(SlayerMartAbility(runtime),!hazardKill,runtime.SlayerMartStormDamage,slayerMartConfig.Parameter);
            if(SpawnSlayerCoin(runtime,enemy,hazardKill))return;
            if (runtime.ThemeBossStarted && enemy.Id == runtime.ContentBossEnemyId)
            {
                runtime.ThemeBossDefeated = true; runtime.AwaitingExit = true;
                runtime.ContentAreas.Clear(); runtime.ContentAreaSnapshots.Clear();
                runtime.SpawnQueue.Clear();
                foreach (var other in runtime.Enemies.Values.ToArray()) KillEnemy(runtime, other, true);
                foreach (int id in runtime.Projectiles.Keys.ToArray()) RemoveProjectile(runtime, id);
            }
            OnRoundEnemyKilled(runtime, enemy, hazardKill);
            if (!hazardKill) AwardIslandKill(runtime, enemy);
            if (runtime.IsFinalBoss && enemy.Id == runtime.FinalBossEnemyId)
            {
                runtime.FinalBossDefeated = true;
                runtime.AwaitingExit = true;
                runtime.SpawnQueue.Clear();
                // 最终 Boss 死亡后停止所有危险，不等待普通敌人清空才开放胜利出口。
                foreach (var other in runtime.Enemies.Values.ToArray()) KillEnemy(runtime, other, true);
                foreach (var id in runtime.Projectiles.Keys.ToArray()) RemoveProjectile(runtime, id);
            }
            foreach (var hazard in runtime.Hazards) hazard.ForgetTarget(enemy.Id);
            view?.RemoveEnemy(enemy.Id, true, enemy.Position - origin, explosive, !hazardKill&&!explosive&&enemy.LastDamageWasFlame);
            var deathPosition = IslandPosition(runtime, enemy.Position, catalog.Rule.PlayerRadius);
            if (!hazardKill && !runtime.IsChallenge && runtime.Drops.Count < catalog.Rule.MaxDrops &&
                UnityRandom(0f, 1f) <= catalog.Rule.DropChance)
            {
                var totalWeight = 0;
                foreach (var candidate in catalog.Drops)
                {
                    if (!IsDropEligible(candidate, runtime.ArenaNumber) || candidate.Weight <= 0) continue;
                    totalWeight += candidate.Weight;
                }
                if (totalWeight > 0)
                {
                    var roll = random.Next(totalWeight);
                    foreach (var candidate in catalog.Drops)
                    {
                        if (!IsDropEligible(candidate, runtime.ArenaNumber) || candidate.Weight <= 0) continue;
                        if (roll < candidate.Weight)
                        {
                            // 武器与其他物资共用击杀掉落规则，拾取后才替换当前武器。
                            var drop = runtimePool.RentDrop();
                            drop.Id = runtime.NextEntityId++;
                            drop.Definition = candidate;
                            drop.Position = deathPosition;
                            runtime.Drops.Add(drop.Id, drop);
                            break;
                        }
                        roll -= candidate.Weight;
                    }
                }
            }
            runtimePool.Return(enemy);
        }

        /// <summary>按当前持有量计算可接收次数；满额或无有效奖励时为零，不回退超额库存。</summary>
        private static int SupplyAddedAmount(int held, ArcadeDropDefinition drop) =>
            (int)Math.Max(0L, Math.Min((long)drop.Amount, (long)drop.MaxOwned - held));

        /// <summary>拒绝无效奖励及不存在的产品；检查先于相机、库存和掉落集合变化。</summary>
        private bool CanApplyDrop(ArcadeDropDefinition drop)
        {
            switch (drop.Kind)
            {
                case global::ArcadeDropKind.RunGold:
                case global::ArcadeDropKind.Bomb:
                case global::ArcadeDropKind.Dash: return drop.Amount > 0;
                case global::ArcadeDropKind.TemporaryWeapon:
                    return drop.DurationSeconds > 0 && catalog.Weapons.Any(w => w.Id == drop.ProductId);
                case global::ArcadeDropKind.Powerup:
                    return drop.DurationSeconds > 0 && catalog.Powerups.Any(p => p.Id == drop.ProductId);
                case global::ArcadeDropKind.FirstPerson: return drop.DurationSeconds > 0;
                default: return false;
            }
        }

        /// <summary>只在奖励实际写入后返回成功，反馈消费本次实际变化。</summary>
        private bool ApplyDrop(ArcadeCombatRuntime runtime, ArcadeDropDefinition drop)
        {
            if(drop.Kind==global::ArcadeDropKind.Powerup&&runtime.SlayerMartRulesVersion==1&&slayerMartConfig!=null&&
                catalog.Powerups.Any(p=>p.Id==drop.ProductId&&p.Behavior==global::ArcadePowerupBehavior.Chicken)&&
                !TryGetPowerup(runtime,global::ArcadePowerupBehavior.Chicken,out _)&&
                (SlayerMartAbility(runtime)?.Supports.Count(p=>p.Kind==SlayerMartSupportKind.Chicken&&p.RemainingSeconds>0)??0)>=slayerMartConfig.Catalog.MaxChickens)return false;
            switch (drop.Kind)
            {
                case global::ArcadeDropKind.RunGold:
                    if (this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute == true)
                        return AwardOrdinaryTreasure(drop.Amount);
                    if (drop.Amount <= 0 || runtime.ArenaGold > long.MaxValue - drop.Amount) return false;
                    runtime.ArenaGold += drop.Amount;
                    break;
                case global::ArcadeDropKind.Bomb:
                    runtime.Bombs += SupplyAddedAmount(runtime.Bombs, drop);
                    break;
                case global::ArcadeDropKind.Dash:
                    runtime.Dashes += SupplyAddedAmount(runtime.Dashes, drop);
                    break;
                case global::ArcadeDropKind.TemporaryWeapon:
                    runtime.TemporaryWeaponId = drop.ProductId;
                    runtime.TemporaryWeaponRemaining = drop.DurationSeconds;
                    break;
                case global::ArcadeDropKind.Powerup:
                    ApplyPowerup(runtime, drop);
                    SyncSlayerMartCapacity(runtime);
                    break;
            }
            return true;
        }

        /// <summary>同类强化只刷新时长、次数和锚点；不同类型可并存，避免掉落顺序互相覆盖。</summary>
        private void ApplyPowerup(ArcadeCombatRuntime runtime, ArcadeDropDefinition drop)
        {
            ArcadePowerupDefinition definition = default;
            var found = false;
            foreach (var candidate in catalog.Powerups)
            {
                if (!string.Equals(candidate.Id, drop.ProductId, StringComparison.Ordinal)) continue;
                definition = candidate;
                found = true;
                break;
            }
            if (!found) return;
            float duration=slayerMartConfig==null?drop.DurationSeconds:ArcadeSlayerMartAbilityRules.MasteryDuration(drop.DurationSeconds,SlayerMartMastery(definition.Behavior),SlayerMartAbility(runtime),slayerMartConfig.Parameter);
            if((definition.Behavior==global::ArcadePowerupBehavior.Saw||definition.Behavior==global::ArcadePowerupBehavior.Barrel)&&
                ArcadeSlayerMartSupportRules.TryExtendOrdinaryDrop(SlayerMartAbility(runtime),definition.Behavior==global::ArcadePowerupBehavior.Saw?SlayerMartSupportKind.Saw:SlayerMartSupportKind.Barrel,duration,slayerMartConfig))return;
            if (!runtime.Powerups.TryGetValue(definition.Behavior, out var powerup))
            {
                powerup = new ArcadePowerupRuntime();
                runtime.Powerups.Add(definition.Behavior, powerup);
            }
            powerup.ProductId = definition.Id;
            powerup.Definition = definition;
            powerup.RemainingSeconds = duration;
            powerup.Charges = definition.Charges;
            powerup.CooldownSeconds = 0f;
            powerup.Position = runtime.PlayerPosition;
        }

        /// <summary>只在全部配置轮次清空后提交一次；保存失败则保持现场并允许下一帧后的人工重试。</summary>
        private void CompleteArena(ArcadeCombatRuntime runtime)
        {
            var run = this.GetModel<ArcadeRunModel>().Current;
            ArcadeIslandRoundCheckpoint carriedPlayer = null;
            if (!runtime.FinalBossDefeated && (UsesRoundLayouts(runtime) || run.ArenaEntryState != null))
            {
                carriedPlayer = CaptureIslandCheckpoint(runtime);
                carriedPlayer.RoundNumber = 1; carriedPlayer.MapId = "arena.entry"; carriedPlayer.ArenaGold = 0;
            }
            runtime.CompletionSubmitted = true;
            if (!runtime.ArenaClearAwarded)
            {
                if (!UsesEarlyRounds(runtime))
                    runtime.ArenaGold += ScaleGold(arena.ClearGold, runtime.ArenaNumber, catalog.Rule.GoldGrowthPerArena);
                runtime.ArenaClearAwarded = true;
            }
            var resources = new Dictionary<string, int>(run.EmergencyResources, StringComparer.Ordinal)
            {
                [ArcadeRunDefaults.BombResourceId] = runtime.Bombs,
                [ArcadeRunDefaults.DashResourceId] = runtime.Dashes
            };
            var nextArena = runtime.ArenaNumber + 1;
            var seed = unchecked((run.Checkpoint?.GenerationSeed ?? 0) ^ arena.EncounterSeedSalt ^ nextArena * 7919);
            var victory = run.IsCasualRoute && runtime.FinalBossDefeated;
            ArcadeCheckpointSaveData checkpoint = null;
            string error = null;
            if (!victory) checkpoint = new ArcadeCheckpointSaveData { ArenaNumber = nextArena, GenerationSeed = seed };
            var completion = new ArcadeArenaCompletion
            {
                SlayerMart=runtime.SlayerMart?.Clone(),
                ArenaNumber = runtime.ArenaNumber,
                EarnedGold = runtime.ArenaGold,
                Score = run.Score, ScoreMultiplier = run.ScoreMultiplier, MultiplierProgress = run.MultiplierProgress,
                EmergencyResources = resources,
                EquipmentBySlot = new Dictionary<string, string>(run.EquipmentBySlot, StringComparer.Ordinal),
                EarnedRewards = BuildRewards(runtime.ArenaNumber, arena.IsBoss),
                NextCheckpoint = checkpoint,
                NextArenaConfigNumber = runtime.SelectedExitConfigNumber,
                Victory = victory,
                PlayerState = carriedPlayer
            };
            if (!this.GetSystem<ArcadeRunSystem>().TryCompleteArena(completion, out _, out error))
            {
                runtime.CompletionSubmitted = false;
                runtime.CompletionAwaitingRetry = true;
                FailCombat(error);
                return;
            }
            this.SendEvent(new ArcadeArenaCompletedEvent());
        }

        /// <summary>挑战成功只提交挑战奖励，失败恢复进入前已经落盘的“已尝试”安全节点。</summary>
        private void CompleteChallenge(ArcadeCombatRuntime runtime, bool succeeded)
        {
            if(runtime.SlayerChallenge!=null){CompleteSlayerChallenge(runtime);return;}
            if (runtime.CompletionSubmitted) return;
            runtime.CompletionSubmitted = true;
            // 挑战属于刚完成竞技场的安全节点；不能用下一竞技场编号提前增长奖励。
            var rewards = succeeded
                ? BuildChallengeRewards(Mathf.Max(1, runtime.ArenaNumber - 1))
                : Array.Empty<ArcadeRewardSaveData>();
            if (!this.GetSystem<ArcadeRunSystem>().TryCompleteChallenge(succeeded, rewards, out _, out var error))
            {
                runtime.CompletionSubmitted = false;
                runtime.CompletionAwaitingRetry = true;
                FailCombat(error);
                return;
            }
            this.SendEvent(new ArcadeArenaCompletedEvent());
        }

        /// <summary>仅响应玩家显式重试；竞技场清场金币只记一次，安全节点事务仍由 ArcadeRunSystem 保证原子性。</summary>
        private void RetryCompletion(ArcadeCombatRuntime runtime)
        {
            if(runtime.SlayerMartReturnWave>0){TryRetrySlayerMartPreparation();return;}
            if ((runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending) && runtime.CompletionAwaitingRetry)
            { RetryIslandTransition(runtime); return; }
            if (!TryConsumeCompletionRetry(runtime)) return;
            if (runtime.IsChallenge)
                CompleteChallenge(runtime, runtime.PlayerHealth > 0 && runtime.ChallengeSeconds > 0f);
            else
                CompleteArena(runtime);
            if (this.GetModel<ArcadeCombatModel>().Runtime == runtime) PublishHud(runtime);
        }

        internal static bool TryConsumeCompletionRetry(ArcadeCombatRuntime runtime)
        {
            if (runtime == null || !runtime.CompletionAwaitingRetry || runtime.CompletionSubmitted) return false;
            runtime.CompletionAwaitingRetry = false;
            return true;
        }

        /// <summary>按当前街机武器配置判定前方扇形；无有效方向或范围时拒绝命中，障碍由调用方裁决。</summary>
        internal static bool IsWithinMeleeStrike(Vector2 origin, Vector2 facing, Vector2 target,
            float range, float arcDegrees)
        {
            var offset = target - origin;
            if (range <= 0f || arcDegrees <= 0f || arcDegrees > 360f ||
                offset.sqrMagnitude > range * range || facing.sqrMagnitude < 0.0001f) return false;
            return offset.sqrMagnitude < 0.0001f ||
                   Vector2.Dot(facing.normalized, offset.normalized) >= Mathf.Cos(arcDegrees * 0.5f * Mathf.Deg2Rad);
        }

        internal static float CalculatePlayerSpeed(float baseSpeed, float upgradePercent, bool dashing,
            float bootsPercent)
        {
            var speed = Mathf.Max(0f, baseSpeed) * (1f + Mathf.Max(0f, upgradePercent)) *
                        (1f + Mathf.Max(0f, bootsPercent));
            return dashing ? speed * DashSpeedMultiplier : speed;
        }

        internal static float CalculatePickupRadius(float baseRadius, float upgradePercent, float magnetRadius) =>
            Mathf.Max(Mathf.Max(0f, baseRadius) * (1f + Mathf.Max(0f, upgradePercent)),
                Mathf.Max(0f, magnetRadius));

        internal static bool TryConsumeShield(ArcadeCombatRuntime runtime)
        {
            if (runtime == null || !TryGetPowerup(runtime, global::ArcadePowerupBehavior.Shield, out var shield) ||
                shield.Charges <= 0) return false;
            shield.Charges--;
            if (shield.Charges <= 0) runtime.Powerups.Remove(global::ArcadePowerupBehavior.Shield);
            return true;
        }

        private List<ArcadeRewardSaveData> BuildRewards(int arenaNumber, bool boss)
        {
            var rewards = new List<ArcadeRewardSaveData>();
            foreach (var definition in catalog.Rewards)
            {
                var matches = definition.Trigger == global::ArcadeRewardTrigger.ArenaClear ||
                              definition.Trigger == global::ArcadeRewardTrigger.BossClear && boss;
                if (!matches || !IsRewardTriggered(arenaNumber, definition.StartArena, definition.Interval)) continue;
                var triggerIndex = (arenaNumber - definition.StartArena) / definition.Interval;
                rewards.Add(new ArcadeRewardSaveData
                {
                    RewardId = definition.RewardId,
                    Amount = definition.Amount + (long)definition.AmountPerTrigger * triggerIndex
                });
            }
            return rewards;
        }

        private IReadOnlyList<ArcadeRewardSaveData> BuildChallengeRewards(int arenaNumber)
        {
            var rewards = new List<ArcadeRewardSaveData>();
            foreach (var definition in catalog.Rewards)
            {
                if (definition.Trigger != global::ArcadeRewardTrigger.ChallengeClear ||
                    !IsRewardTriggered(arenaNumber, definition.StartArena, definition.Interval)) continue;
                var triggerIndex = (arenaNumber - definition.StartArena) / definition.Interval;
                rewards.Add(new ArcadeRewardSaveData
                {
                    RewardId = definition.RewardId,
                    Amount = definition.Amount + (long)definition.AmountPerTrigger * triggerIndex
                });
            }
            return rewards;
        }

        private void FailCombat(string error)
        {
            this.SendEvent(new ArcadeCombatOperationFailedEvent(string.IsNullOrWhiteSpace(error) ? "街机战斗操作失败" : error));
        }

        private void SpawnProjectile(ArcadeCombatRuntime runtime, bool hostile, Vector2 position, Vector2 velocity,
            int damage, float lifetime, global::ArcadeWeaponBehavior behavior = global::ArcadeWeaponBehavior.Direct,
            int maxHits = 1, float explosionRadius = 0f, string projectileVisualResourceName = null,
            bool fromPlayerWeapon = false, string sourceWeaponId = null)
        {
            if (runtime.Projectiles.Count >= catalog.Rule.MaxProjectiles) return;
            var projectile = runtimePool.RentProjectile();
            projectile.Id = runtime.NextEntityId++;
            projectile.Hostile = hostile;
            projectile.Position = position;
            projectile.Velocity = velocity;
            projectile.Damage = Mathf.Max(1, damage);
            projectile.RemainingSeconds = lifetime;
            projectile.Behavior = hostile ? global::ArcadeWeaponBehavior.Direct : behavior;
            projectile.ProjectileVisualResourceName = hostile ? null : projectileVisualResourceName;
            projectile.FromPlayerWeapon = !hostile && fromPlayerWeapon;
            projectile.SourceWeaponId = hostile ? null : sourceWeaponId;
            projectile.RemainingHits = Mathf.Max(1, maxHits);
            projectile.ExplosionRadius = Mathf.Max(0f, explosionRadius);
            runtime.Projectiles.Add(projectile.Id, projectile);
        }

        /// <summary>贯穿弹只对每个稳定敌人 ID 结算一次，并在达到配置命中数后耗尽。</summary>
        internal static bool TryRegisterProjectileHit(ArcadeProjectileRuntime projectile, int enemyId)
        {
            if (projectile == null || projectile.Hostile || projectile.RemainingHits <= 0 || enemyId <= 0 ||
                !projectile.HitEnemyIds.Add(enemyId)) return false;
            projectile.RemainingHits--;
            return true;
        }

        internal static bool IsWithinExplosion(Vector2 center, Vector2 target, float radius) =>
            radius > 0f && (target - center).sqrMagnitude <= radius * radius;

        /// <summary>普通爆炸弹只在碰撞时引爆；定时榴弹还会在飞行时间耗尽时引爆。</summary>
        internal static bool ShouldExplodeOnRemoval(global::ArcadeWeaponBehavior behavior, bool expired,
            bool hitObstacle) => IsExplosiveBehavior(behavior) &&
                                 (hitObstacle || behavior == global::ArcadeWeaponBehavior.TimedExplosive && expired);

        internal static bool IsExplosiveBehavior(global::ArcadeWeaponBehavior behavior) =>
            behavior == global::ArcadeWeaponBehavior.Explosive ||
            behavior == global::ArcadeWeaponBehavior.TimedExplosive;

        private void RemoveProjectile(ArcadeCombatRuntime runtime, int id)
        {
            if (!runtime.Projectiles.TryGetValue(id, out var projectile)) return;
            runtime.Projectiles.Remove(id);
            view.RemoveProjectile(id);
            runtimePool.Return(projectile);
        }

        private void SyncView(ArcadeCombatRuntime runtime)
        {
            SyncTreasureView(runtime);
            if (runtime.IslandRoomTransitionPending && runtime.CompletionAwaitingRetry) return;
            view.SetBabylon(runtime.InTreasureRoom || !UsesBabylon(runtime) ? null : arena.Babylon, runtime.Crates.Keys.ToArray());
            // 先同步场地切换，宝藏房提前返回前也必须释放岛岸并恢复共享场地表现。
            view.SetOpening(runtime.InTreasureRoom || runtime.IsChallenge ? default : arena.Opening,
                runtime.IslandCampfireWarningRemaining <= 0 && ArcadeOpeningRules.CampfireActive(arena.Opening, runtime.WaveIndex, runtime.IsChallenge || runtime.IsFinalBoss,
                    runtime.WaveClearAwarded, runtime.AwaitingExit, runtime.InTreasureRoom, runtime.RespawnRemaining > 0));
            view.SetIslandCampfireWarning(UsesIslandRounds(runtime) && runtime.WaveIndex == 3 &&
                !runtime.WaveClearAwarded && !runtime.InTreasureRoom && runtime.IslandCampfireWarningRemaining > 0);
            SyncIslandEntryWarning(runtime);
            view.SetPlayerRescue(0,false);
            view.SetPlayerDash(0,runtime.PlayerPosition,runtime.DashDirection,runtime.DashRemaining,runtime.RespawnRemaining<=0f);
            if (runtime.InTreasureRoom)
            {
                view.SetPlayer(runtime.PlayerPosition, runtime.AimDirection, runtime.PlayerHealth>0, runtime.PlayerInvulnerability > 0f || runtime.DashRemaining > 0f);
                view.SetPlayerHealth(runtime.PlayerHealth, runtime.PlayerMaxHealth);
                if (runtime.TreasureRoom.Parameters.IsArmory && TryGetWeapon(runtime, out var selectedWeapon))
                    view.SetPlayerWeapon(selectedWeapon);
                return;
            }
            view.SetExits(runtime.IslandRoundAwaitingExit ? new[] { -(runtime.WaveIndex + 2) } :
                runtime.AwaitingExit && !runtime.FinalBossDefeated ? arena.NextArenaNumbers.Take(1).ToArray() : Array.Empty<int>(),
                catalog.Rule.ArenaHalfHeight, runtime.AwaitingExit && runtime.FinalBossDefeated);
            view.SetHazards(runtime.IsChallenge || runtime.IsFinalBoss || runtime.AwaitingExit || runtime.IslandRoundAwaitingExit ? Array.Empty<FirepowerLoop.Arcade.ArcadeHazardParameters>() : arena.Hazards,
                runtime.HazardElapsedSeconds);
            view.SetPlayer(runtime.PlayerPosition, runtime.AimDirection, runtime.RespawnRemaining <= 0f&&runtime.PlayerHealth>0,
                runtime.PlayerInvulnerability > 0f || runtime.DashRemaining > 0f);
            if (TryGetWeapon(runtime, out var currentWeapon)) view.SetPlayerWeapon(currentWeapon);
            // 新攻击的冷却在 TryFire 中刚写入，包含攻速强化；重复序列不会重新调整动作。
            view.SetPlayerFireSequence(runtime.PlayerFireSequence, runtime.FireCooldown);
            view.SetPlayerHealth(runtime.PlayerHealth, runtime.PlayerMaxHealth);
            foreach (var enemy in runtime.Enemies.Values)
            {
                view.SetEnemy(enemy.Id, enemy.ConfigId, enemy.Position, enemy.Health, enemy.MaxHealth,
                    enemy.BossPhase is ArcadeBossPhase.Telegraph or ArcadeBossPhase.RayTelegraph, enemy.AttackSequence,
                    enemy.IsBossPresentation);
                view.SetEnemyArmor(enemy.Id, enemy.Position, enemy.ArmorHealth);
            }
            SyncRoundContent(runtime);
            foreach(var enemy in runtime.Enemies.Values)view.SetEnemyBurning(enemy.Id,enemy.FlameHitRemainingSeconds);
            foreach (var projectile in runtime.Projectiles.Values)
                view.SetProjectile(projectile.Id, projectile.Hostile, projectile.Position, projectile.Velocity,
                    projectile.ProjectileVisualResourceName, projectile.FromPlayerWeapon);
            foreach (var drop in runtime.Drops.Values)
            {
                view.SetDrop(drop.Id, drop.Definition, drop.Position);
                view.SetDropOpacity(drop.Id, GroundDropOpacity(drop.RemainingSeconds));
            }
            view.BeginEffectSync();
            foreach (ArcadePowerupRuntime powerup in runtime.Powerups.Values)
            {
                if(SyncSlayerMartOrdinaryOrbitVisual(runtime,powerup))continue;
                view.SetPowerup(powerup.Definition,
                    powerup.Definition.VisualFollowPlayer ? runtime.PlayerPosition : powerup.Position,
                    powerup.ShotDirection);
            }
            SyncSlayerMartSupportVisuals(runtime);
            SyncSlayerMartStormVisual(runtime);
            if (TryGetSupportVisual(out ArcadeSupportVisualDefinition support))
                view.SetSupport(support, runtime.PlayerPosition);
            view.EndEffectSync();
        }

        /// <summary>从安全节点装备槽解析支援表现；未装备或配置无视觉时不创建占位对象。</summary>
        private bool TryGetSupportVisual(out ArcadeSupportVisualDefinition support)
        {
            support = default;
            ArcadeRunSaveData run = this.GetModel<ArcadeRunModel>().Current;
            if (run == null || !run.EquipmentBySlot.TryGetValue(ArcadeRunDefaults.SupportSlotId,
                    out string productId) || string.IsNullOrWhiteSpace(productId)) return false;
            foreach (ArcadeSupportVisualDefinition candidate in catalog.SupportVisuals)
            {
                if (!string.Equals(candidate.ProductId, productId, StringComparison.Ordinal) ||
                    !string.Equals(candidate.EquipmentSlotId, ArcadeRunDefaults.SupportSlotId,
                        StringComparison.Ordinal) || string.IsNullOrWhiteSpace(candidate.ActiveVisualPrefabAddress))
                    continue;
                support = candidate;
                return true;
            }
            return false;
        }

        private void PublishHud(ArcadeCombatRuntime runtime)
        {
            // 阶段提示只读取已提交的波次/出口状态；不修改清波等待、射击继续或离场条件。
            var notices = this.GetModel<ArcadeCombatModel>().StageNotices;
            if (!runtime.IsChallenge && !runtime.IsFinalBoss)
            {
                // 宝藏轮清场直接进入房间、返回后开下一波，不经过普通 WaveClearAwarded 路径。
                bool treasureWaveCleared = runtime.TreasureCleared &&
                    runtime.WaveIndex + 1 == runtime.TreasureRoom?.Parameters.WaveNumber;
                if (notices.Observe(runtime, runtime.ArenaNumber, runtime.WaveIndex + 1,
                        runtime.WaveClearAwarded || treasureWaveCleared, runtime.AwaitingExit, Time.unscaledTimeAsDouble))
                    this.SendEvent(new ArcadeStageNoticeChangedEvent());
            }
            else notices.Clear();
            var temporary = FindDrop(global::ArcadeDropKind.TemporaryWeapon, runtime.TemporaryWeaponId);
            var powerups = new List<ArcadePowerupHudEntry>(runtime.Powerups.Count);
            foreach (var pair in runtime.Powerups)
                powerups.Add(new ArcadePowerupHudEntry(ProductNameKey(pair.Value.ProductId),
                    pair.Value.RemainingSeconds, pair.Value.Charges));
            this.SendEvent(new ArcadeCombatHudChangedEvent(new ArcadeCombatHudSnapshot
            {
                ArenaNumber = runtime.ArenaNumber,
                WaveNumber = runtime.WaveIndex + 1,
                TotalWaves = runtime.IsChallenge ? 1 : arena.WaveGroupIds.Count,
                RemainingEnemies = runtime.Enemies.Count + runtime.SpawnQueue.Count,
                TemporaryWeaponNameKey = runtime.TemporaryWeaponRemaining > 0f ? ProductNameKey(temporary.ProductId) : null,
                TemporaryWeaponSeconds = runtime.TemporaryWeaponRemaining,
                FirstPersonSeconds = runtime.FirstPersonRemaining,
                SlayerMartRulesVersion=runtime.SlayerMartRulesVersion,
                HasStorm=SlayerMartAbility(runtime)?.Owned.Contains("slayermart.item.16")==true,
                StormCharge=SlayerMartAbility(runtime)?.StormCharge??0,
                StormSeconds=(float)(SlayerMartAbility(runtime)?.StormRemaining??0),
                VestSlots=SlayerMartAbility(runtime)?.VestSlots??0,
                Plates=SlayerMartAbility(runtime)?.Plates??0,
                Powerups = powerups.ToArray(),
                Bombs = runtime.Bombs,
                Dashes = runtime.Dashes,
                ArenaGold = runtime.ArenaGold,
                Score = this.GetModel<ArcadeRunModel>().Current?.Score ?? 0,
                ScoreMultiplier = this.GetModel<ArcadeRunModel>().Current?.ScoreMultiplier ?? 1,
                InTreasureRoom = runtime.InTreasureRoom, TreasureSeconds = (float)(runtime.TreasureRoom?.RemainingSeconds ?? 0),
                PlayerHealth = runtime.PlayerHealth,
                PlayerMaxHealth = runtime.PlayerMaxHealth,
                RespawnSeconds = runtime.RespawnRemaining,
                CanRetryCompletion = runtime.CompletionAwaitingRetry,
                IslandPreparationError = runtime.IslandPreparationError,
                IsChallenge = runtime.IsChallenge,
                IsFinalBoss = runtime.IsFinalBoss,
                AwaitingExit = runtime.AwaitingExit,
                IslandMapNameKey = UsesIslandRounds(runtime) ? ArcadeIslandLayout.GetNameKey(runtime.WaveIndex + 1) : RoundLayout(runtime)?.NameKey,
                IslandTransitioning = runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending,
                IslandAwaitingRoundExit = runtime.IslandRoundAwaitingExit,
                Resting = !UsesRoundLayouts(runtime) && UsesEarlyRest(runtime) && runtime.WaveClearAwarded && !runtime.AwaitingExit,
                RestSeconds = runtime.WaveTransitionRemaining,
                ChallengeSeconds = runtime.ChallengeSeconds
            }));
        }

        private ArcadeDropDefinition FindDrop(global::ArcadeDropKind kind, string productId)
        {
            foreach (var candidate in catalog.Drops)
            {
                if (candidate.Kind != kind) continue;
                if (productId != null && !string.Equals(candidate.ProductId, productId, StringComparison.Ordinal)) continue;
                return candidate;
            }
            return default;
        }

        private static bool IsDropEligible(ArcadeDropDefinition definition, int arenaNumber) =>
            definition.MinArena <= arenaNumber && (definition.MaxArena <= 0 || definition.MaxArena >= arenaNumber);

        private float UpgradeTotal(ArcadeRunSaveData run, global::ArcadeUpgradeKind kind)
        {
            if (run == null) return 0f;
            var total = 0f;
            foreach (var upgrade in catalog.Upgrades)
            {
                if (upgrade.Kind != kind) continue;
                run.PurchasedUpgradeStacks.TryGetValue(upgrade.Id, out var stacks);
                total += Mathf.Min(stacks, upgrade.MaxStacks) * upgrade.ValuePerStack;
            }
            return total;
        }

        private Vector2 RandomBorderPosition()
        {
            var horizontal = random.Next(2) == 0;
            var sign = random.Next(2) == 0 ? -1f : 1f;
            return horizontal
                ? new Vector2(UnityRandom(-catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfWidth), sign * (catalog.Rule.ArenaHalfHeight - 0.5f))
                : new Vector2(sign * (catalog.Rule.ArenaHalfWidth - 0.5f), UnityRandom(-catalog.Rule.ArenaHalfHeight, catalog.Rule.ArenaHalfHeight));
        }

        private float UnityRandom(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        /// <summary>在规则层修正拥挤，避免直接移动的街机敌人彼此叠成一个模型。</summary>
        private void ResolveEnemyCrowding(ArcadeCombatRuntime runtime)
        {
            crowdingEnemies.Clear();
            foreach (var enemy in runtime.Enemies.Values)
                crowdingEnemies.Add(enemy);
            if (crowdingEnemies.Count < 2) return;
            crowdingEnemies.Sort(CompareEnemyIds);
            crowdingPositions.Clear();
            for (var index = 0; index < crowdingEnemies.Count; index++)
                crowdingPositions.Add(crowdingEnemies[index].Position);
            ResolveCrowding(crowdingPositions, catalog.Rule.EnemyContactRadius,
                catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), IslandFor(runtime));
            for (var index = 0; index < crowdingEnemies.Count; index++)
                crowdingEnemies[index].Position = crowdingPositions[index];
        }

        private static int CompareEnemyIds(ArcadeEnemyRuntime left, ArcadeEnemyRuntime right) => left.Id.CompareTo(right.Id);

        /// <summary>复用键快照，允许同一帧安全移除字典成员而不产生临时数组。</summary>
        private static void CopyKeys<T>(Dictionary<int, T> source, List<int> destination)
        {
            destination.Clear();
            foreach (var pair in source) destination.Add(pair.Key);
        }

        internal static int ResolveConfiguredArenaIndex(int arenaNumber, int configuredCount) =>
            configuredCount <= 0 ? -1 : (Mathf.Max(1, arenaNumber) - 1) % configuredCount;

        internal static int ScalePlayerHealth(int baseHealth, float percent) =>
            Mathf.Max(1, Mathf.RoundToInt(baseHealth * (1f + Mathf.Max(0f, percent))));

        internal static long ScaleGold(int baseGold, int arenaNumber, float growth) =>
            Math.Max(0L, (long)Mathf.RoundToInt(baseGold * (1f + Mathf.Max(0, arenaNumber - 1) * growth)));

        /// <summary>计算点到移动线段的平方距离，供高速冲锋做连续命中判定。</summary>
        internal static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0.000001f) return (point - start).sqrMagnitude;
            var progress = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return (point - (start + segment * progress)).sqrMagnitude;
        }

        internal static Vector2 ClampPosition(Vector2 value, float halfWidth, float halfHeight) =>
            new Vector2(Mathf.Clamp(value.x, -halfWidth, halfWidth), Mathf.Clamp(value.y, -halfHeight, halfHeight));

        /// <summary>把圆形角色限制在竞技场内并推出配置障碍；输入与输出均为竞技场局部坐标。</summary>
        internal static Vector2 ResolvePosition(Vector2 value, float radius, float arenaHalfWidth, float arenaHalfHeight,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles, ArcadeIslandRegion island = null)
        {
            if (island != null)
            {
                var p = island.Constrain(value.x, value.y, radius); value = new Vector2(p.X, p.Y);
            }
            var result = ClampPosition(value, arenaHalfWidth - radius, arenaHalfHeight - radius);
            if (obstacles == null) return result;
            for (var pass = 0; pass < 2; pass++)
            for (var index = 0; index < obstacles.Count; index++)
            {
                var obstacle = obstacles[index];
                var half = obstacle.HalfExtents + Vector2.one * radius;
                var local = result - obstacle.Center;
                if (Mathf.Abs(local.x) >= half.x || Mathf.Abs(local.y) >= half.y) continue;
                var pushX = half.x - Mathf.Abs(local.x);
                var pushY = half.y - Mathf.Abs(local.y);
                if (pushX <= pushY)
                    result.x = obstacle.Center.x + (local.x < 0f ? -half.x : half.x);
                else
                    result.y = obstacle.Center.y + (local.y < 0f ? -half.y : half.y);
                result = ClampPosition(result, arenaHalfWidth - radius, arenaHalfHeight - radius);
                if (island != null)
                { var p = island.Constrain(result.x, result.y, radius); result = new Vector2(p.X, p.Y); }
            }
            return result;
        }

        internal static bool IsInsideObstacle(Vector2 position, float radius,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles)
        {
            if (obstacles == null) return false;
            for (var index = 0; index < obstacles.Count; index++)
            {
                var local = position - obstacles[index].Center;
                var half = obstacles[index].HalfExtents + Vector2.one * radius;
                if (Mathf.Abs(local.x) < half.x && Mathf.Abs(local.y) < half.y) return true;
            }
            return false;
        }

        /// <summary>
        /// 返回朝目标的确定性基础绕障方向。直线受阻时选择膨胀障碍的可见角点，避免为瞬时敌人保存路径状态。
        /// </summary>
        internal static Vector2 ResolveNavigationDirection(Vector2 position, Vector2 target, float radius,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles)
        {
            var direct = target - position;
            if (direct.sqrMagnitude <= 0.000001f) return Vector2.zero;
            if (!TryFindBlockingObstacle(position, target, radius, obstacles, out var blocking))
                return direct.normalized;

            const float cornerClearance = 0.08f;
            var expanded = blocking.HalfExtents + Vector2.one * (radius + cornerClearance);
            var bestDirection = Vector2.zero;
            var bestScore = float.MaxValue;
            for (var cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                var corner = blocking.Center + new Vector2(
                    (cornerIndex & 1) == 0 ? -expanded.x : expanded.x,
                    (cornerIndex & 2) == 0 ? -expanded.y : expanded.y);
                var firstLeg = corner - position;
                if (firstLeg.sqrMagnitude <= 0.000001f ||
                    SegmentCrossesObstacle(position, corner, radius, obstacles)) continue;

                var score = firstLeg.magnitude + Vector2.Distance(corner, target);
                // 能从角点直达目标的路线优先；多障碍场景会在抵达角点后继续逐段重算。
                if (SegmentCrossesObstacle(corner, target, radius, obstacles)) score += 1000f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestDirection = firstLeg.normalized;
            }
            return bestDirection.sqrMagnitude > 0f ? bestDirection : direct.normalized;
        }

        private static bool TryFindBlockingObstacle(Vector2 start, Vector2 end, float radius,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles, out ArcadeObstacleDefinition blocking)
        {
            blocking = default;
            if (obstacles == null) return false;
            var nearestEntry = float.MaxValue;
            var found = false;
            for (var index = 0; index < obstacles.Count; index++)
            {
                if (!TryGetSegmentObstacleEntry(start, end, radius, obstacles[index], out var entry) ||
                    entry >= nearestEntry) continue;
                nearestEntry = entry;
                blocking = obstacles[index];
                found = true;
            }
            return found;
        }

        private static bool SegmentCrossesObstacle(Vector2 start, Vector2 end, float radius,
            IReadOnlyList<ArcadeObstacleDefinition> obstacles)
        {
            if (obstacles == null) return false;
            for (var index = 0; index < obstacles.Count; index++)
                if (TryGetSegmentObstacleEntry(start, end, radius, obstacles[index], out _)) return true;
            return false;
        }

        /// <summary>以开区间检测线段与膨胀矩形内部相交，允许路径贴着留有余量的角点通过。</summary>
        private static bool TryGetSegmentObstacleEntry(Vector2 start, Vector2 end, float radius,
            ArcadeObstacleDefinition obstacle, out float entry)
        {
            const float interiorEpsilon = 0.0001f;
            var half = obstacle.HalfExtents + Vector2.one * radius;
            var minimum = obstacle.Center - half + Vector2.one * interiorEpsilon;
            var maximum = obstacle.Center + half - Vector2.one * interiorEpsilon;
            var delta = end - start;
            var enter = 0f;
            var exit = 1f;
            if (!ClipSegmentAxis(start.x, delta.x, minimum.x, maximum.x, ref enter, ref exit) ||
                !ClipSegmentAxis(start.y, delta.y, minimum.y, maximum.y, ref enter, ref exit) ||
                enter > exit)
            {
                entry = 0f;
                return false;
            }
            entry = enter;
            return exit > interiorEpsilon && enter < 1f - interiorEpsilon;
        }

        private static bool ClipSegmentAxis(float origin, float delta, float minimum, float maximum,
            ref float enter, ref float exit)
        {
            if (Mathf.Abs(delta) <= 0.000001f) return origin > minimum && origin < maximum;
            var first = (minimum - origin) / delta;
            var second = (maximum - origin) / delta;
            if (first > second)
            {
                var swap = first;
                first = second;
                second = swap;
            }
            enter = Mathf.Max(enter, first);
            exit = Mathf.Min(exit, second);
            return enter <= exit;
        }

        /// <summary>以固定顺序做两轮成对分离，复杂度受同屏敌人上限约束。</summary>
        internal static void ResolveCrowding(IList<Vector2> positions, float radius,
            float arenaHalfWidth, float arenaHalfHeight, IReadOnlyList<ArcadeObstacleDefinition> obstacles, ArcadeIslandRegion island = null)
        {
            if (positions == null || positions.Count < 2 || radius <= 0f) return;
            var minimumDistance = radius * 1.7f;
            for (var pass = 0; pass < 2; pass++)
            for (var first = 0; first < positions.Count - 1; first++)
            for (var second = first + 1; second < positions.Count; second++)
            {
                var delta = positions[second] - positions[first];
                var distance = delta.magnitude;
                if (distance >= minimumDistance) continue;
                var direction = distance > 0.0001f ? delta / distance : ((first + second) & 1) == 0 ? Vector2.right : Vector2.up;
                var correction = direction * ((minimumDistance - distance) * 0.5f);
                positions[first] = ResolvePosition(positions[first] - correction, radius,
                    arenaHalfWidth, arenaHalfHeight, obstacles, island);
                positions[second] = ResolvePosition(positions[second] + correction, radius,
                    arenaHalfWidth, arenaHalfHeight, obstacles, island);
            }
        }

        internal static bool IsRewardTriggered(int arenaNumber, int startArena, int interval) =>
            interval > 0 && arenaNumber >= startArena && (arenaNumber - startArena) % interval == 0;

        /// <summary>Boss 只绑定配置声明的最后一轮敌群；其它 Boss 场继续沿用倍率占位。</summary>
        internal static bool TryResolveBoss(ArcadeArenaDefinition arena, ArcadeEnemyGroupDefinition group,
            IReadOnlyList<ArcadeBossDefinition> bosses, out ArcadeBossDefinition boss)
        {
            boss = default;
            if (arena == null || group == null || string.IsNullOrWhiteSpace(arena.BossId) || bosses == null) return false;
            for (var index = 0; index < bosses.Count; index++)
            {
                var candidate = bosses[index];
                if (!string.Equals(candidate.Id, arena.BossId, StringComparison.Ordinal) ||
                    !string.Equals(candidate.EnemyGroupId, group.Id, StringComparison.Ordinal)) continue;
                boss = candidate;
                return true;
            }
            return false;
        }

        internal static float[] BuildSpreadAngles(int count, float totalDegrees)
        {
            count = Mathf.Max(1, count);
            if (count == 1) return new[] { 0f };
            var result = new float[count];
            for (var index = 0; index < count; index++) result[index] = -totalDegrees * 0.5f + totalDegrees * index / (count - 1);
            return result;
        }

        private static Vector2 Rotate(Vector2 value, float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector2(value.x * cos - value.y * sin, value.x * sin + value.y * cos);
        }

        private static int ReadAmount(IReadOnlyDictionary<string, int> values, string id) =>
            values != null && values.TryGetValue(id, out var amount) ? amount : 0;

        private static int BuildCombatSeed(string runId, int arenaNumber)
        {
            unchecked
            {
                var hash = 17;
                foreach (var character in runId ?? string.Empty) hash = hash * 31 + character;
                return hash * 397 ^ arenaNumber;
            }
        }

        private static string ProductNameKey(string productId) =>
            string.IsNullOrWhiteSpace(productId) ? null : "ui.arcade.product." + productId + ".name";
    }
}

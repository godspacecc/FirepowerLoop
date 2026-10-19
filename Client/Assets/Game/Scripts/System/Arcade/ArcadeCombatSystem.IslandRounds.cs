using System;
using System.Collections.Generic;
using System.Linq;
using FirepowerLoop.Arcade;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed partial class ArcadeCombatSystem
    {
        /// <summary>四图只属于本地休闲首场；默认海岛规则继续供旧顺序局和联机使用。</summary>
        private bool UsesIslandRounds(ArcadeCombatRuntime runtime) => runtime != null && !runtime.IsChallenge &&
            !runtime.IsFinalBoss && arena?.Opening.Enabled == true &&
            this.GetModel<ArcadeRunModel>().IsCasualOpeningArena;

        /// <summary>预告采用现有接触保护时长作为初值；不重定敌人、武器、水火数值。</summary>
        private float IslandWarningSeconds => Mathf.Max(.1f, catalog.Rule.ContactInvulnerabilitySeconds);

        /// <summary>缓存本轮固定阻挡，避免每个敌人的空间查询重复分配数组。</summary>
        private int islandObstacleRound = -1;
        /// <summary>表现碰撞、移动绕行与射击遮挡共用的不可变布局。</summary>
        private IReadOnlyList<ArcadeObstacleDefinition> islandRoundObstacles;
        /// <summary>宝藏区域无战斗障碍，其余按真实地图身份选择同源布局。</summary>
        private IReadOnlyList<ArcadeObstacleDefinition> ObstaclesFor(ArcadeCombatRuntime runtime)
        {
            if (runtime.InTreasureRoom) return Array.Empty<ArcadeObstacleDefinition>();
            if (UsesContentRounds(runtime)) return ContentObstacles(runtime);
            if (!UsesIslandRounds(runtime)) return arena.Obstacles;
            int round = runtime.WaveIndex + 1;
            if (islandObstacleRound != round || islandRoundObstacles == null)
            {
                islandObstacleRound = round;
                islandRoundObstacles = ArcadeIslandLayout.GetObstacles(round, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight);
            }
            return islandRoundObstacles;
        }

        /// <summary>显式逐轮切图；保留角色及效果逻辑，销毁旧图世界实例，准备失败不重复开始轮次。</summary>
        private void StartIslandRound(ArcadeCombatRuntime runtime, int waveIndex, ArcadeIslandRoundCheckpoint restore = null)
        {
            if (waveIndex < 0 || waveIndex > 3) throw new ArgumentOutOfRangeException(nameof(waveIndex));
            var oldPosition = runtime.PlayerPosition;
            runtime.IslandTransitionPending = true;
            runtime.IslandPreparationError = null;
            runtime.IslandTransitionViewPrepared = false;
            runtime.IslandRoundAwaitingExit = false;
            runtime.AwaitingExit = false;
            runtime.CompletionAwaitingRetry = false;
            SuspendIslandFirstPerson(runtime);
            ClearArcadeInput(runtime);
            foreach (var enemy in runtime.Enemies.Values) runtimePool.Return(enemy);
            foreach (var projectile in runtime.Projectiles.Values) runtimePool.Return(projectile);
            foreach (var drop in runtime.Drops.Values) runtimePool.Return(drop);
            runtime.Enemies.Clear(); runtime.Projectiles.Clear(); runtime.Drops.Clear();
            runtime.Hazards.Clear(); runtime.HazardPreviousEnemyPositions.Clear(); runtime.Crates.Clear();
            runtime.CratesInitialized = false;
            runtime.InTreasureRoom = false;
            runtime.IslandRoomTransitionPending = false;
            runtime.PresentedRegion = 0;
            islandRegion = new ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, waveIndex + 1);
            islandRegionRound = waveIndex + 1;
            random = new System.Random(BuildCombatSeed(this.GetModel<ArcadeRunModel>().Current.RunId, runtime.ArenaNumber) ^ (waveIndex + 1) * 7919);
            BeginWave(runtime, waveIndex);
            runtime.PlayerPosition = ArcadeIslandLayout.GetSpawn(waveIndex + 1, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight);
            // 锚点随玩家起点平移再投影到新图合法位置；次数、耐久与冷却保持原值。
            foreach (var powerup in runtime.Powerups.Values)
                powerup.Position = ResolvePosition(powerup.Position - oldPosition + runtime.PlayerPosition,
                    catalog.Rule.PlayerRadius, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight,
                    ObstaclesFor(runtime), islandRegion);
            if (restore != null) RestoreIslandCheckpoint(runtime, restore);
            runtime.IslandEntryBatch = 0;
            runtime.IslandWaveBudget = runtime.SpawnQueue.Count;
            runtime.IslandEntryWarningRemaining = waveIndex == 0 ? 0f : IslandWarningSeconds;
            runtime.IslandCampfireWarningRemaining = waveIndex == 3 ? IslandWarningSeconds : 0;
            // 出口确认不经过活动帧尾部，资源异步等待前必须先公布准备状态。
            PublishHud(runtime);
            PrepareIslandRoundView(runtime);
        }

        /// <summary>替换局部地图并启动资源准备，异常转为可重试冻结状态。</summary>
        private void PrepareIslandRoundView(ArcadeCombatRuntime runtime)
        {
            try
            {
                view.Begin(arena.ArenaNumber, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime));
                view.SetIslandRound(UsesIslandRounds(runtime) ? runtime.WaveIndex + 1 : 0);
                runtime.IslandTransitionViewPrepared = true;
                SyncView(runtime);
                TryFinishIslandTransition(runtime);
            }
            catch (Exception exception) { FailIslandTransition(runtime, exception.Message); }
        }

        /// <summary>主题间先继承角色，再执行新轮原有补空规则；海岛整轮重试采用相反恢复顺序避免补发。</summary>
        private void BeginArenaEntry(ArcadeCombatRuntime runtime, ArcadeIslandRoundCheckpoint saved)
        {
            RestoreIslandCheckpoint(runtime, saved);
            BeginWave(runtime, 0);
            runtime.IslandTransitionPending = runtime.IslandTransitionViewPrepared = true;
        }

        /// <summary>地图就绪与安全节点均成功后才开始活动帧；等待和失败期间不消耗临时效果。</summary>
        private bool TryFinishIslandTransition(ArcadeCombatRuntime runtime)
        {
            if (UsesContentRounds(runtime)) return TryFinishContentTransition(runtime);
            if (!runtime.IslandTransitionPending || runtime.CompletionAwaitingRetry) return false;
            if (!string.IsNullOrEmpty(view.IslandRoundLoadError))
            { FailIslandTransition(runtime, view.IslandRoundLoadError); return false; }
            if (!runtime.IslandTransitionViewPrepared || !view.IsIslandRoundReady) return false;
            if (runtime.FirstPersonRemaining > 0 && !view.IsFirstPersonActive)
            {
                if (!view.TryBeginFirstPerson(out var cameraError) || !view.TryReadFirstPersonAim(out _))
                { view.EndFirstPerson(); FailIslandTransition(runtime, cameraError ?? "第一人称相机尚未准备完成"); return false; }
            }
            var run = this.GetModel<ArcadeRunModel>().Current;
            if (UsesIslandRounds(runtime) && run.IslandRoundCheckpoint?.RoundNumber != runtime.WaveIndex + 1 &&
                !this.GetSystem<ArcadeRunSystem>().TrySaveIslandRound(CaptureIslandCheckpoint(runtime), out var error))
            { FailIslandTransition(runtime, error); return false; }
            runtime.IslandFirstPersonSuspended = false;
            runtime.IslandTransitionPending = false;
            // 开场只提交就绪状态；全局确认音会在未射击时产生突兀的枪响感。
            PublishHud(runtime);
            return true;
        }

        /// <summary>保留目标轮候选与旧安全节点，失败不扣命也不继续计时。</summary>
        private void FailIslandTransition(ArcadeCombatRuntime runtime, string error)
        {
            runtime.CompletionAwaitingRetry = true;
            runtime.IslandPreparationError = string.IsNullOrWhiteSpace(error) ? "地图准备失败" : error;
            ClearArcadeInput(runtime);
            FailCombat(error);
            PublishHud(runtime);
        }

        /// <summary>同一轮候选重试，不重新补给或恢复满时长。</summary>
        private void RetryIslandTransition(ArcadeCombatRuntime runtime)
        {
            runtime.CompletionAwaitingRetry = false;
            runtime.IslandPreparationError = null;
            if (runtime.IslandRoomTransitionPending)
            {
                runtime.PresentedRegion = 0;
                SyncView(runtime);
                PublishHud(runtime);
                return;
            }
            SuspendIslandFirstPerson(runtime);
            if (UsesContentRounds(runtime)) PrepareContentRoundView(runtime);
            else PrepareIslandRoundView(runtime);
            PublishHud(runtime);
        }

        /// <summary>加载页只允许重试当前失败的地图准备，不绕过普通战斗或结算输入门禁。</summary>
        public bool TryRetryMapPreparation()
        {
            var runtime = this.GetModel<ArcadeCombatModel>().Runtime;
            var map = this.GetModel<MapSceneModel>();
            if (runtime == null || !runtime.CompletionAwaitingRetry ||
                !(runtime.IslandTransitionPending || runtime.IslandRoomTransitionPending) ||
                map.CurrentMapId != MapIds.Arcade || map.CurrentMode != MapPreparationMode.ArcadePlayerOnly) return false;
            RetryIslandTransition(runtime);
            return true;
        }

        /// <summary>宝藏区域资源及相机准备成功才恢复房间实际活动计时。</summary>
        private void TickIslandRoomTransition(ArcadeCombatRuntime runtime)
        {
            if (runtime.CompletionAwaitingRetry) return;
            if (!string.IsNullOrEmpty(view.IslandRoundLoadError))
            { FailIslandTransition(runtime, view.IslandRoundLoadError); return; }
            if (!view.IsIslandTreasureReady) return;
            if (runtime.FirstPersonRemaining > 0 && !view.IsFirstPersonActive &&
                (!view.TryBeginFirstPerson(out var error) || !view.TryReadFirstPersonAim(out _)))
            { view.EndFirstPerson(); FailIslandTransition(runtime, error ?? "宝藏房相机尚未准备完成"); return; }
            runtime.IslandFirstPersonSuspended = false;
            runtime.IslandRoomTransitionPending = false; PublishHud(runtime);
        }

        /// <summary>捕获稳定角色逻辑值，排除敌人、弹丸、掉落和 Unity 对象。</summary>
        private ArcadeIslandRoundCheckpoint CaptureIslandCheckpoint(ArcadeCombatRuntime runtime) => new()
        {
            RoundNumber = runtime.WaveIndex + 1, MapId = "island.round." + (runtime.WaveIndex + 1),
            PlayerHealth = runtime.PlayerHealth, PlayerMaxHealth = runtime.PlayerMaxHealth,
            Bombs = runtime.Bombs, Dashes = runtime.Dashes, ArenaGold = runtime.ArenaGold,
            TemporaryWeaponId = runtime.TemporaryWeaponRemaining > 0 ? runtime.TemporaryWeaponId : null,
            TemporaryWeaponRemaining = Mathf.Max(0, runtime.TemporaryWeaponRemaining),
            FirstPersonRemaining = Mathf.Max(0, runtime.FirstPersonRemaining),
            FireCooldown = Mathf.Max(0, runtime.FireCooldown), InvulnerabilityRemaining = Mathf.Max(0, runtime.PlayerInvulnerability),
            DashRemaining = Mathf.Max(0, runtime.DashRemaining), AimDirectionX = runtime.AimDirection.x, AimDirectionY = runtime.AimDirection.y,
            DashDirectionX = runtime.DashDirection.x, DashDirectionY = runtime.DashDirection.y,
            Powerups = runtime.Powerups.Values.Where(p => p.RemainingSeconds > 0).Select(p => new ArcadeIslandPowerupCheckpoint
            {
                ProductId = p.ProductId, RemainingSeconds = p.RemainingSeconds, Charges = p.Charges,
                CooldownSeconds = Mathf.Max(0, p.CooldownSeconds), PositionX = p.Position.x, PositionY = p.Position.y
            }).ToList()
        };

        /// <summary>按现有定义恢复剩余效果及合法锚点，不走拾取入口、不补满额度。</summary>
        private void RestoreIslandCheckpoint(ArcadeCombatRuntime runtime, ArcadeIslandRoundCheckpoint saved)
        {
            if (saved.TemporaryWeaponRemaining > 0 && !catalog.Weapons.Any(w => w.Id == saved.TemporaryWeaponId))
                throw new InvalidOperationException("轮级节点的临时武器不在当前配置中");
            runtime.PlayerHealth = saved.PlayerHealth; runtime.PlayerMaxHealth = saved.PlayerMaxHealth;
            runtime.Bombs = saved.Bombs; runtime.Dashes = saved.Dashes; runtime.ArenaGold = saved.ArenaGold;
            runtime.TemporaryWeaponId = saved.TemporaryWeaponId; runtime.TemporaryWeaponRemaining = saved.TemporaryWeaponRemaining;
            runtime.FirstPersonRemaining = saved.FirstPersonRemaining; runtime.IslandFirstPersonSuspended = saved.FirstPersonRemaining > 0;
            runtime.FireCooldown = saved.FireCooldown; runtime.PlayerInvulnerability = saved.InvulnerabilityRemaining;
            runtime.DashRemaining = saved.DashRemaining; runtime.AimDirection = new Vector2(saved.AimDirectionX, saved.AimDirectionY);
            if (runtime.AimDirection.sqrMagnitude < .001f) runtime.AimDirection = Vector2.up;
            FirepowerLoop.Arcade.ArcadeDashRules.ResolveDirection(saved.DashDirectionX, saved.DashDirectionY,
                runtime.AimDirection.x, runtime.AimDirection.y, out var dashX, out var dashY);
            runtime.DashDirection = new Vector2(dashX, dashY);
            runtime.Powerups.Clear();
            foreach (var effect in saved.Powerups)
            {
                var definition = catalog.Powerups.FirstOrDefault(p => p.Id == effect.ProductId);
                if (string.IsNullOrEmpty(definition.Id) || runtime.Powerups.ContainsKey(definition.Behavior))
                    throw new InvalidOperationException("轮级节点的强化身份无效或行为重复");
                runtime.Powerups.Add(definition.Behavior, new ArcadePowerupRuntime
                {
                    ProductId = effect.ProductId, Definition = definition, RemainingSeconds = effect.RemainingSeconds,
                    Charges = effect.Charges, CooldownSeconds = effect.CooldownSeconds,
                    Position = ResolvePosition(new Vector2(effect.PositionX, effect.PositionY), catalog.Rule.PlayerRadius,
                        catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), islandRegion)
                });
            }
            runtime.TreasureCompleted = saved.RoundNumber == 4;
        }

        /// <summary>只释放地图相机租约，转场冻结或宝藏重建仍保留逻辑剩余秒数。</summary>
        private void SuspendIslandFirstPerson(ArcadeCombatRuntime runtime)
        {
            if (runtime.FirstPersonRemaining <= 0) return;
            runtime.IslandFirstPersonSuspended = true;
            view.EndFirstPerson();
        }

        /// <summary>首关四入口持续生成；后续海岛轮保留按预算换向的方向预告。</summary>
        private bool IslandEntryIsReady(ArcadeCombatRuntime runtime, float seconds)
        {
            if (runtime.WaveIndex == 0)
            {
                runtime.IslandEntryWarningRemaining = 0f;
                return true;
            }
            int batchCount = runtime.WaveIndex == 1 ? 3 : 2;
            int spawned = runtime.IslandWaveBudget - runtime.SpawnQueue.Count;
            int desiredBatch = Mathf.Min(batchCount - 1, spawned * batchCount / Mathf.Max(1, runtime.IslandWaveBudget));
            if (desiredBatch != runtime.IslandEntryBatch)
            {
                runtime.IslandEntryBatch = desiredBatch;
                runtime.IslandEntryWarningRemaining = IslandWarningSeconds;
                // 换向沿用可见预告，不在活动战斗中额外请求全局确认音。
            }
            if (runtime.IslandEntryWarningRemaining <= 0) return true;
            runtime.IslandEntryWarningRemaining = Mathf.Max(0, runtime.IslandEntryWarningRemaining - seconds);
            return runtime.IslandEntryWarningRemaining <= 0;
        }

        /// <summary>固定岸段与车道只决定出生位置；预告查询不消耗序号，出生后立即普通追击。</summary>
        private void IslandRoundEntry(ArcadeCombatRuntime runtime, out Vector2 start, bool consume = true)
        {
            var region = IslandFor(runtime);
            if (runtime.WaveIndex == 0)
            {
                // 四入口逐个轮换，同一个入口内部的车道也轮换，预览不消耗出生序号。
                float entryLane = (runtime.IslandSpawnSequence / 4 % 5) * .25f;
                region.EntryFromFourSides(runtime.IslandSpawnSequence, entryLane, catalog.Rule.EnemyContactRadius, out var entry);
                if (consume) runtime.IslandSpawnSequence++;
                start = ResolvePosition(new Vector2(entry.X, entry.Y), catalog.Rule.EnemyContactRadius,
                    catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), islandRegion);
                return;
            }
            // 第2图左右接替，其余图先水侧再陆侧；车道按序循环，重试时一致。
            float direction = runtime.WaveIndex == 1
                ? (runtime.IslandEntryBatch % 2 == 0 ? Mathf.PI : 0)
                : (runtime.IslandEntryBatch == 0 ? -Mathf.PI * .5f : Mathf.PI * .5f);
            float lane = ((runtime.IslandSpawnSequence % 5) - 2) * .12f;
            if (consume) runtime.IslandSpawnSequence++;
            int index = (Mathf.RoundToInt((direction + lane) / (Mathf.PI * 2) * ArcadeIslandRegion.Samples) + ArcadeIslandRegion.Samples) % ArcadeIslandRegion.Samples;
            var outer = region.Outer[index];
            var p = region.Constrain(outer.X * .95f, outer.Y * .95f, catalog.Rule.EnemyContactRadius);
            start = ResolvePosition(new Vector2(p.X, p.Y), catalog.Rule.EnemyContactRadius,
                catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), islandRegion);
        }

        /// <summary>清场后接触通道立即推进；转场与结算状态防止同帧重复进入。</summary>
        private void TickIslandExit(ArcadeCombatRuntime runtime)
        {
            if (this.GetModel<ArcadeCombatModel>().Runtime != runtime || runtime.IslandTransitionPending ||
                runtime.CompletionSubmitted || runtime.CompletionAwaitingRetry || !runtime.WaveClearAwarded ||
                (!runtime.IslandRoundAwaitingExit && !runtime.AwaitingExit)) return;
            int count = ArcadeRoundLayoutParameters.ExitDirectionCount;
            if (!runtime.IslandRoundAwaitingExit && arena.NextArenaNumbers.Count == 0) return;
            int contact = -1;
            for (int i = 0; i < count; i++)
                if ((runtime.PlayerPosition - ArcadeIslandLayout.GetExitPosition(runtime.WaveIndex + 1, i, count,
                    catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, catalog.Rule.PlayerRadius)).sqrMagnitude <= 2.25f)
                { contact = i; break; }
            if (contact < 0) return;
            if (runtime.IslandRoundAwaitingExit) StartIslandRound(runtime, runtime.WaveIndex + 1);
            else
            { runtime.SelectedExitConfigNumber = arena.NextArenaNumbers[0]; CompleteArena(runtime); }
        }

        /// <summary>把实际生成岸段投影到表现，不提前修改敌群预算或入口进度。</summary>
        private void SyncIslandEntryWarning(ArcadeCombatRuntime runtime)
        {
            bool visible = UsesIslandRounds(runtime) && runtime.WaveIndex != 0 && !runtime.InTreasureRoom && !runtime.IslandTransitionPending &&
                !runtime.WaveClearAwarded && !runtime.AwaitingExit && runtime.SpawnQueue.Count > 0 && runtime.IslandEntryWarningRemaining > 0;
            var position = Vector2.zero;
            if (visible) IslandRoundEntry(runtime, out position, false);
            view.SetIslandEntryWarning(position, visible);
        }

        /// <summary>短步同时解析岛岸和障碍，真实路径用于冲刺连续击杀，不能横穿沉船。</summary>
        private Vector2 MoveOnIsland(ArcadeCombatRuntime runtime, Vector2 delta, float radius)
        {
            var region = IslandFor(runtime);
            islandMovementPath.Clear();
            var position = runtime.PlayerPosition;
            islandMovementPath.Add(new ArcadeRegionPoint(position.x, position.y));
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .2f));
            for (int i = 0; i < steps; i++)
            {
                position = ResolvePosition(position + delta / steps, radius, catalog.Rule.ArenaHalfWidth,
                    catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime), region);
                islandMovementPath.Add(new ArcadeRegionPoint(position.x, position.y));
            }
            return position;
        }
    }
}

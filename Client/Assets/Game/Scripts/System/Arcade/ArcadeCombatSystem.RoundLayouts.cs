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
        /// <summary>新休闲主线的每一轮都有配置身份；旧顺序局与挑战保留原流程。</summary>
        private bool UsesRoundLayouts(ArcadeCombatRuntime runtime) => runtime != null && !runtime.IsChallenge &&
            this.GetModel<ArcadeRunModel>().Current?.IsCasualRoute == true &&
            catalog?.RoundLayouts?.Any(p => p.ArenaConfigNumber == arena?.ArenaNumber) == true;

        /// <summary>海岛保留已有兼容节点，其余主题使用通用轮级事务。</summary>
        private bool UsesContentRounds(ArcadeCombatRuntime runtime) => UsesRoundLayouts(runtime) && arena.ArenaNumber > 1;

        private ArcadeRoundLayoutParameters RoundLayout(ArcadeCombatRuntime runtime) => runtime?.SlayerChallenge!=null ?
            catalog.RoundLayouts.Single(p=>p.ArenaConfigNumber==slayerChallengeConfig.ArenaConfigNumber&&p.RoundNumber==slayerChallengeConfig.LayoutRound) : !UsesRoundLayouts(runtime) ? null :
            catalog.RoundLayouts.FirstOrDefault(p => p.ArenaConfigNumber == arena.ArenaNumber &&
                p.RoundNumber == Mathf.Clamp(runtime.WaveIndex + 1, 1, 4));

        private string contentObstacleIdentity;
        private IReadOnlyList<ArcadeObstacleDefinition> contentRoundObstacles;
        /// <summary>实际碰撞、寻路和弹丸共同读取缓存，切图按稳定布局身份失效。</summary>
        private IReadOnlyList<ArcadeObstacleDefinition> ContentObstacles(ArcadeCombatRuntime runtime)
        {
            var layout = RoundLayout(runtime);
            if (layout == null) return arena.Obstacles;
            if (contentObstacleIdentity != layout.Id)
            {
                contentObstacleIdentity = layout.Id;
                contentRoundObstacles = layout.Obstacles.Select(o =>
                    new ArcadeObstacleDefinition(o.X, o.Y, o.HalfWidth, o.HalfHeight)).ToArray();
            }
            return contentRoundObstacles;
        }

        /// <summary>保留角色和主题内箱体账本，候选资源及轮前节点成功后才允许活动。</summary>
        private void StartContentRound(ArcadeCombatRuntime runtime, int waveIndex, ArcadeRoundCheckpoint restore = null)
        {
            if (waveIndex < 0 || waveIndex > 3) throw new ArgumentOutOfRangeException(nameof(waveIndex));
            runtime.WaveIndex = waveIndex;
            var layout = RoundLayout(runtime) ?? throw new InvalidOperationException("缺少正式轮级地图配置");
            if (restore != null && (restore.MapId != layout.Id || restore.LayoutVersion != layout.Version ||
                restore.ArenaNumber != runtime.ArenaNumber || restore.ArenaConfigNumber != arena.ArenaNumber))
                throw new InvalidOperationException("轮级节点与当前地图版本不兼容，原存档已保留");
            if (restore != null && restore.CratesInitialized)
            {
                var knownCrates = arena.Babylon?.Crates.ToDictionary(c => c.Id, c => c.Health)
                    ?? new Dictionary<int, int>();
                if (restore.RemainingCrateIds.Any(id => !knownCrates.ContainsKey(id)) ||
                    restore.CrateHealth.Any(pair => !knownCrates.TryGetValue(pair.Key, out var maximum) || pair.Value > maximum))
                    throw new InvalidOperationException("轮级节点箱体身份或耐久与当前配置不兼容，原存档已保留");
            }
            if (restore != null) ValidateGroundBarrelCheckpoint(layout, restore);
            var oldPosition = runtime.PlayerPosition;
            runtime.IslandTransitionPending = true;
            runtime.IslandPreparationError = null;
            runtime.IslandTransitionViewPrepared = false;
            runtime.IslandRoundAwaitingExit = false;
            runtime.AwaitingExit = runtime.CompletionAwaitingRetry = false;
            runtime.ThemeBossStarted = runtime.ThemeBossDefeated = false;
            runtime.ContentBossEnemyId = 0;
            SuspendIslandFirstPerson(runtime);
            ClearArcadeInput(runtime);
            foreach (var enemy in runtime.Enemies.Values) runtimePool.Return(enemy);
            foreach (var projectile in runtime.Projectiles.Values) runtimePool.Return(projectile);
            foreach (var drop in runtime.Drops.Values) runtimePool.Return(drop);
            runtime.Enemies.Clear(); runtime.Projectiles.Clear(); runtime.Drops.Clear();
            runtime.Hazards.Clear(); runtime.HazardPreviousEnemyPositions.Clear();
            foreach (var hazard in arena.Hazards) runtime.Hazards.Add(new ArcadeHazardState(hazard));
            runtime.HazardElapsedSeconds = 0;
            runtime.InTreasureRoom = runtime.IslandRoomTransitionPending = false;
            runtime.PresentedRegion = 0;
            islandRegion = null; islandRegionRound = -1;
            random = new System.Random(BuildCombatSeed(this.GetModel<ArcadeRunModel>().Current.RunId,
                runtime.ArenaNumber) ^ (waveIndex + 1) * 7919);
            BeginWave(runtime, waveIndex);
            runtime.PlayerPosition = ResolvePosition(new Vector2(layout.SpawnX, layout.SpawnY), catalog.Rule.PlayerRadius,
                catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime));
            foreach (var effect in runtime.Powerups.Values)
                effect.Position = ResolvePosition(effect.Position - oldPosition + runtime.PlayerPosition, catalog.Rule.PlayerRadius,
                    catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime));
            if (restore != null)
            {
                RestoreIslandCheckpoint(runtime, restore.PlayerState);
                runtime.TreasureCompleted = restore.TreasureCompleted;
                RestoreGroundBarrels(runtime, restore);
                if (restore.CratesInitialized)
                {
                    foreach (int id in runtime.Crates.Keys.Where(id => !restore.RemainingCrateIds.Contains(id)).ToArray())
                        runtime.Crates.Remove(id);
                    foreach (var pair in restore.CrateHealth)
                        runtime.Crates[pair.Key].Damage(runtime.Crates[pair.Key].Health - pair.Value, false);
                    runtime.CratesInitialized = true;
                }
            }
            runtime.IslandEntryBatch = 0;
            runtime.IslandWaveBudget = runtime.SpawnQueue.Count;
            runtime.IslandEntryWarningRemaining = layout.SkillWarning;
            InitializeRoundContent(runtime);
            // 黑幕完全遮挡后才替换地图，准备期间冻结玩法并保持黑屏。
            view.BeginRoundTransition();
            PublishHud(runtime);
            PrepareContentRoundView(runtime);
        }

        private void PrepareContentRoundView(ArcadeCombatRuntime runtime)
        {
            if(!view.IsRoundTransitionCovered)return;
            try
            {
                view.Begin(arena.ArenaNumber, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, ObstaclesFor(runtime));
                view.SetIslandRound(0);
                view.SetRoundLayout(RoundLayout(runtime));
                runtime.IslandTransitionViewPrepared = true;
                SyncView(runtime);
                TryFinishContentTransition(runtime);
            }
            catch (Exception exception) { FailIslandTransition(runtime, exception.Message); }
        }

        /// <summary>保存失败不公开下一轮，加载等待不扣除效果时长。</summary>
        private bool TryFinishContentTransition(ArcadeCombatRuntime runtime)
        {
            if (!runtime.IslandTransitionPending || runtime.CompletionAwaitingRetry) return false;
            if(!runtime.IslandTransitionViewPrepared)
            {
                if(view.IsRoundTransitionCovered)PrepareContentRoundView(runtime);
                return !runtime.IslandTransitionPending;
            }
            var failure = view.RoundLayoutLoadError ?? view.IslandRoundLoadError;
            if (!string.IsNullOrEmpty(failure)) { FailIslandTransition(runtime, failure); return false; }
            if (!runtime.IslandTransitionViewPrepared || !view.IsRoundLayoutReady || !view.IsIslandRoundReady) return false;
            if (runtime.FirstPersonRemaining > 0 && !view.IsFirstPersonActive &&
                (!view.TryBeginFirstPerson(out var error) || !view.TryReadFirstPersonAim(out _)))
            { view.EndFirstPerson(); FailIslandTransition(runtime, error ?? "地图相机尚未准备完成"); return false; }
            var run = this.GetModel<ArcadeRunModel>().Current;
            if(!TryApplySlayerMartRoundBenefits(runtime,out var benefitError))
            {FailIslandTransition(runtime,benefitError);return false;}
            if (run.RoundCheckpoint?.MapId != RoundLayout(runtime).Id &&
                !this.GetSystem<ArcadeRunSystem>().TrySaveRound(CaptureRoundCheckpoint(runtime), out var saveError))
            { FailIslandTransition(runtime, saveError); return false; }
            runtime.IslandFirstPersonSuspended = false;
            runtime.IslandTransitionPending = false;
            view.EndRoundTransition();
            PublishHud(runtime);
            return true;
        }

        private ArcadeRoundCheckpoint CaptureRoundCheckpoint(ArcadeCombatRuntime runtime)
        {
            var layout = RoundLayout(runtime);
            var state = CaptureIslandCheckpoint(runtime); state.MapId = layout.Id;
            return new ArcadeRoundCheckpoint { SlayerMart=runtime.SlayerMart?.Clone(), ArenaNumber = runtime.ArenaNumber, ArenaConfigNumber = arena.ArenaNumber,
                RoundNumber = runtime.WaveIndex + 1, LayoutVersion = layout.Version, MapId = layout.Id, PlayerState = state,
                CratesInitialized = runtime.CratesInitialized, RemainingCrateIds = runtime.Crates.Keys.OrderBy(x => x).ToList(),
                CrateHealth = runtime.Crates.ToDictionary(pair => pair.Key, pair => pair.Value.Health),
                DestroyedBarrelIds = runtime.DestroyedGroundBarrelIds.OrderBy(id => id).ToList(),
                TreasureCompleted = runtime.TreasureCompleted,
                EncounterSeed = BuildCombatSeed(this.GetModel<ArcadeRunModel>().Current.RunId, runtime.ArenaNumber) ^ (runtime.WaveIndex + 1) * 7919 };
        }

        /// <summary>固定入口按已生成预算切换，短预告先于真正生成。</summary>
        private bool ContentEntryReady(ArcadeCombatRuntime runtime, float seconds)
        {
            var layout = RoundLayout(runtime);
            int spawned = runtime.IslandWaveBudget - runtime.SpawnQueue.Count;
            int batch = Mathf.Min(layout.Entries.Count - 1, spawned * layout.Entries.Count / Mathf.Max(1, runtime.IslandWaveBudget));
            if (batch != runtime.IslandEntryBatch)
            { runtime.IslandEntryBatch = batch; runtime.IslandEntryWarningRemaining = layout.SkillWarning; }
            runtime.IslandEntryWarningRemaining = Mathf.Max(0, runtime.IslandEntryWarningRemaining - seconds);
            return runtime.IslandEntryWarningRemaining <= 0;
        }

        private Vector2 ContentExitPosition(ArcadeCombatRuntime runtime, int index, int count)
        {
            var layout = RoundLayout(runtime);
            var point = layout.GetExitPosition(index, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight, catalog.Rule.PlayerRadius);
            return new Vector2(point.X, point.Y);
        }

        /// <summary>清场后接触实体门直接推进，沿用轮前保存、商店及结算事务。</summary>
        private void TickContentExit(ArcadeCombatRuntime runtime)
        {
            if (this.GetModel<ArcadeCombatModel>().Runtime != runtime || runtime.IslandTransitionPending ||
                runtime.CompletionSubmitted || runtime.CompletionAwaitingRetry ||
                (!runtime.IslandRoundAwaitingExit && !runtime.AwaitingExit)) return;
            if(UsesStagePortal(runtime))
            {if(StagePortalContact(runtime)){runtime.SelectedExitConfigNumber=runtime.FinalBossDefeated?0:arena.NextArenaNumbers.FirstOrDefault();CompleteArena(runtime);}return;}
            int count = ArcadeRoundLayoutParameters.ExitDirectionCount;
            if (!runtime.IslandRoundAwaitingExit && !runtime.FinalBossDefeated && arena.NextArenaNumbers.Count == 0) return;
            int contact = -1;
            for (int i = 0; i < count; i++)
                if ((runtime.PlayerPosition - ContentExitPosition(runtime, i, count)).sqrMagnitude <= 2.25f) { contact = i; break; }
            if (contact < 0) return;
            if (runtime.IslandRoundAwaitingExit)
            {if(!TryBeginSlayerMart(runtime,runtime.WaveIndex+1))StartContentRound(runtime,runtime.WaveIndex+1);}
            else
            { runtime.SelectedExitConfigNumber = runtime.FinalBossDefeated ? 0 : arena.NextArenaNumbers[0]; CompleteArena(runtime); }
        }

        /// <summary>Slayer 节点在四轮之后进入单独 Boss 阶段，主线轮次与场序保持不变。</summary>
        private void BeginThemeBoss(ArcadeCombatRuntime runtime)
        {
            if (runtime.ThemeBossStarted) return;
            runtime.ThemeBossStarted = true;
            runtime.AwaitingExit = runtime.IslandRoundAwaitingExit = false;
            runtime.SpawnQueue.Clear(); runtime.Hazards.Clear();
            InitializeRoundContent(runtime);
            foreach (int id in runtime.Projectiles.Keys.ToArray()) RemoveProjectile(runtime, id);
            var definition = this.GetUtility<IRuntimeConfigProvider>();
            if (!definition.TryGetEnemy("enemy.arcade.brute", out var source, out var error))
            { FailIslandTransition(runtime, error); return; }
            var layout = RoundLayout(runtime);
            var boss = runtimePool.RentEnemy(); boss.Id = runtime.NextEntityId++;
            runtime.ContentBossEnemyId = boss.Id;
            boss.ConfigId = source.Id; boss.Position = ContentExitPosition(runtime, 0, 1);
            boss.MaxHealth = Mathf.Max(1, Mathf.RoundToInt(source.MaxHP * layout.EnemyHealthScale * layout.SkillMagnitude * runtime.ArenaNumber));
            boss.Health = boss.MaxHealth; boss.Damage = source.AttackDamage;
            boss.Speed = source.MoveSpeed * layout.EnemySpeedScale;
            boss.AttackRange = source.AttackRange; boss.AttackInterval = source.AttackCooldownSeconds;
            boss.IsBossPresentation = true; AssignRoundEnemyContent(runtime, boss);
            runtime.Enemies.Add(boss.Id, boss);
        }
    }
}

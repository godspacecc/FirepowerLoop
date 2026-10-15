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
    public sealed class OnlineArcadePresentationSystem : AbstractSystem
    {
        private const float SnapDistance = 2.5f;
        private const float DashSpeedMultiplier = 2.4f;
        private readonly Dictionary<long, int> enemyIds = new();
        private readonly Dictionary<long, int> projectileIds = new();
        private readonly Dictionary<long, int> dropIds = new();
        /// <summary>上一份权威快照的护盾次数；只用来识别挡伤视觉，不参与战斗判定。</summary>
        private readonly Dictionary<long, int> shieldChargesByPlayer = new();
        /// <summary>本场已观察的成功补给实例 ID；首次快照只建基线，离场清空，区域切换保留。</summary>
        private readonly HashSet<long> observedSupplyPickupIds = new();
        private readonly HashSet<long> seenIds = new();
        private IArcadeCombatViewPort view;
        private ArcadeConfigCatalogDefinition catalog;
        private ArcadeArenaDefinition activeArena;
        private OnlineArcadeRunClientSnapshot latest = OnlineArcadeRunClientSnapshot.Empty();
        private Vector2 predictedLocalPosition;
        private bool hasPrediction;
        private AnotherDay.Arcade.ArcadeIslandRegion predictionIsland;
        private int activeBattleArenaNumber;
        /// <summary>区域代次与最近已应用的权威帧；切换会清空预测，旧帧不再投影。</summary>
        private bool presentedInTreasureRoom;
        private string presentedRoomId;
        private long lastAppliedBattleRevision = -1;
        private bool active;
        private bool transitionBusy;
        /// <summary>本机本场已观察的权威攻击序列；首次进入只建立基线，不补播历史动作。</summary>
        private bool hasLocalFireBaseline;
        private int lastLocalFireSequence;
        private int transitionGeneration;
        private int nextPresentationId = 1;

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
            if (active && latest.RunId == evt.Snapshot.RunId && latest.Battle?.ArenaNumber == evt.Snapshot.Battle?.ArenaNumber &&
                evt.Snapshot.Battle != null && evt.Snapshot.Battle.BattleRevision < lastAppliedBattleRevision) return;
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
            bool transit = latest.Phase == OnlineArcadeRunPhase.SafeShop && latest.Players.All(p => p.ShopOffers.Count == 0);
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
                activeArena = ResolveArena(latest.Battle.ArenaConfigNumber > 0 ? latest.Battle.ArenaConfigNumber : arenaNumber);
                view.Begin(activeArena.ArenaNumber, catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight,
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
                    Log.Error($"[OnlineArcadePresentationSystem] 联机街机返回基地失败：{result.Error}");
            }
            finally { transitionBusy = false; }
        }

        private void OnUpdate()
        {
            if (!active || latest.Battle == null || catalog == null || latest.BattlePaused) return;
            view.TickPlayerFeedback(Time.deltaTime);
            long selfId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            OnlineArcadeBattlePlayerSnapshot self = default;
            bool found = false;
            foreach (OnlineArcadeBattlePlayerSnapshot player in latest.Battle.Players)
                if (player.PlayerId == selfId) { self = player; found = true; break; }
            if (!found || !self.Alive) return;
            OnlineArcadeInputFrame input = this.GetSystem<OnlineArcadeRunSystem>().LastSampledInput;
            var moveSpeed = CalculatePredictedMoveSpeed(catalog.Rule.PlayerMoveSpeed, self.DashSeconds > 0f,
                catalog.Upgrades, FindLocalRunPlayer(selfId).UpgradeStacks);
            var island = !self.InTreasureRoom && !latest.Battle.IsChallenge && activeArena?.Opening.Enabled == true
                ? predictionIsland ??= new AnotherDay.Arcade.ArcadeIslandRegion(catalog.Rule.ArenaHalfWidth, catalog.Rule.ArenaHalfHeight) : null;
            if (island != null && island.IsShallow(predictedLocalPosition.x, predictedLocalPosition.y))
                moveSpeed *= activeArena.Opening.WaterSpeedRatio;
            predictedLocalPosition = PredictLocalPosition(predictedLocalPosition, input.Move, moveSpeed,
                Mathf.Min(Time.deltaTime, .05f), catalog.Rule.PlayerRadius, self.InTreasureRoom ? TreasureParameters()?.HalfWidth ?? catalog.Rule.ArenaHalfWidth : catalog.Rule.ArenaHalfWidth,
                self.InTreasureRoom ? TreasureParameters()?.HalfHeight ?? catalog.Rule.ArenaHalfHeight : catalog.Rule.ArenaHalfHeight,
                self.InTreasureRoom ? Array.Empty<ArcadeObstacleDefinition>() : activeArena?.Obstacles, island);
            Vector2 authoritative = new(self.PositionX, self.PositionZ);
            if (Vector2.Distance(predictedLocalPosition, authoritative) > SnapDistance) predictedLocalPosition = authoritative;
            Vector2 facing = input.Aim.sqrMagnitude > .01f ? input.Aim : new Vector2(self.AimX, self.AimZ);
            view.SetPlayer(predictedLocalPosition, facing, true, self.Invulnerable);
            SyncPlayerEffects(self, predictedLocalPosition);
        }

        private void ApplyAuthoritativeSnapshot(OnlineArcadeBattleSnapshot battle)
        {
            if (!active || battle == null || battle.BattleRevision < lastAppliedBattleRevision) return;
            long selfId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            var localPlayer = battle.Players.FirstOrDefault(p => p.PlayerId == selfId);
            bool inRoom = localPlayer.InTreasureRoom;
            var roomRules = TreasureParameters();
            if (inRoom != presentedInTreasureRoom || !string.Equals(presentedRoomId, battle.TreasureRoomId, StringComparison.Ordinal))
            {
                view.Begin(activeArena.ArenaNumber, inRoom ? roomRules.HalfWidth : catalog.Rule.ArenaHalfWidth,
                    inRoom ? roomRules.HalfHeight : catalog.Rule.ArenaHalfHeight,
                    inRoom ? Array.Empty<ArcadeObstacleDefinition>() : activeArena.Obstacles);
                hasPrediction = false; hasLocalFireBaseline = false; shieldChargesByPlayer.Clear();
                enemyIds.Clear(); projectileIds.Clear(); dropIds.Clear(); nextPresentationId = 1;
                presentedInTreasureRoom = inRoom; presentedRoomId = battle.TreasureRoomId;
            }
            bool supplyBaseline = lastAppliedBattleRevision < 0;
            lastAppliedBattleRevision = battle.BattleRevision;
            view.SetTreasureRoom(roomRules, inRoom, battle.TreasureDiscovered, battle.TreasureOpen, battle.TreasureRewardIds);
            view.SetOpening(inRoom || battle.IsChallenge ? default : activeArena.Opening,
                AnotherDay.Arcade.ArcadeOpeningRules.CampfireActive(activeArena.Opening, battle.WaveNumber - 1,
                    battle.IsChallenge || battle.IsFinalBoss, battle.TotalInWave <= battle.SpawnedInWave && battle.Enemies.Count == 0,
                    battle.AwaitingExit, inRoom, !localPlayer.Alive || localPlayer.RespawnSeconds > 0));
            view.SetHazards(inRoom || battle.IsChallenge || battle.IsFinalBoss || battle.AwaitingExit ? Array.Empty<AnotherDay.Arcade.ArcadeHazardParameters>() :
                activeArena.Hazards, battle.HazardElapsedSeconds);
            view.SetExits(!inRoom && battle.AwaitingExit && !battle.IsFinalBoss ? activeArena.NextArenaNumbers : Array.Empty<int>(),
                catalog.Rule.ArenaHalfHeight, !inRoom && battle.AwaitingExit && battle.IsFinalBoss);
            seenIds.Clear();
            view.BeginEffectSync();
            foreach (OnlineArcadeBattlePlayerSnapshot player in battle.Players)
            {
                if (player.InTreasureRoom != inRoom) continue;
                Vector2 position = new(player.PositionX, player.PositionZ);
                Vector2 displayPosition = position;
                Vector2 facing = new(player.AimX, player.AimZ);
                if (player.PlayerId == selfId)
                {
                    if (!hasPrediction || Vector2.Distance(predictedLocalPosition, position) > SnapDistance)
                        predictedLocalPosition = position;
                    else predictedLocalPosition = Vector2.Lerp(predictedLocalPosition, position, .35f);
                    hasPrediction = true;
                    displayPosition = predictedLocalPosition;
                    view.SetPlayer(predictedLocalPosition, facing, player.Alive, player.Invulnerable);
                    view.SetPlayerHealth(player.CurrentHealth, player.MaxHealth);
                }
                else
                {
                    seenIds.Add(player.PlayerId);
                    view.SetRemotePlayer(player.PlayerId, position, facing, player.Alive, player.Invulnerable,
                        player.CurrentHealth, player.MaxHealth);
                }
                SyncPlayerCombat(player, selfId);
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
                view.SetProtection(player.PlayerId,
                    catalog.Powerups.FirstOrDefault(p => p.Behavior == global::ArcadePowerupBehavior.Shield), displayPosition,
                    player.Alive && (player.Invulnerable || player.DashSeconds > 0));
            }
            view.EndEffectSync();

            foreach (OnlineArcadePlayerSnapshot player in latest.Players)
                if (player.PlayerId != selfId && !seenIds.Contains(player.PlayerId)) view.RemoveRemotePlayer(player.PlayerId);
            foreach (long playerId in shieldChargesByPlayer.Keys.ToArray())
                if (playerId != selfId && !seenIds.Contains(playerId)) shieldChargesByPlayer.Remove(playerId);

            var bossWave = !battle.IsFinalBoss && activeArena?.IsBoss == true && battle.WaveNumber == activeArena.WaveGroupIds.Count;
            SyncEnemies(inRoom ? Array.Empty<OnlineArcadeEnemySnapshot>() : battle.Enemies, bossWave);
            view.SetBabylon(inRoom || battle.IsChallenge ? null : activeArena?.Babylon, battle.CrateIds);
            SyncProjectiles(inRoom ? Array.Empty<OnlineArcadeProjectileSnapshot>() : battle.Projectiles);
            SyncDrops(inRoom ? Array.Empty<OnlineArcadeDropSnapshot>() : battle.Drops);
            SyncSupplyPickups(battle.SupplyPickups, selfId, supplyBaseline);
        }

        /// <summary>只播放本机新增权威记录；先标记身份再回调，重复或过期通知不重复反馈。</summary>
        private void SyncSupplyPickups(IReadOnlyList<OnlineArcadeDropSnapshot> receipts, long selfId, bool baseline)
        {
            foreach (var receipt in receipts)
            {
                if (!observedSupplyPickupIds.Add(receipt.RuntimeId) || baseline ||
                    receipt.CollectedByPlayerId != selfId || receipt.AddedAmount <= 0) continue;
                var definition = catalog.Drops.FirstOrDefault(d => d.Id == receipt.DropConfigId);
                if (definition.Kind != global::ArcadeDropKind.Bomb && definition.Kind != global::ArcadeDropKind.Dash) continue;
                view.PlaySupplyPickup(definition, receipt.AddedAmount);
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
            foreach (OnlineArcadePowerupSnapshot value in player.Powerups)
            {
                ArcadePowerupDefinition definition = catalog.Powerups.FirstOrDefault(x =>
                    string.Equals(x.Id, value.ProductId, StringComparison.Ordinal));
                if (string.IsNullOrWhiteSpace(definition.Id) || player.InTreasureRoom && !definition.VisualFollowPlayer) continue;
                Vector2 anchor = definition.VisualFollowPlayer
                    ? displayPosition
                    : new Vector2(value.PositionX, value.PositionZ);
                view.SetPowerup(player.PlayerId, definition, anchor,
                    new Vector2(value.ShotDirectionX, value.ShotDirectionZ));
            }

            if (string.IsNullOrWhiteSpace(player.SupportId)) return;
            ArcadeShopItemDefinition support = catalog.ShopItems.FirstOrDefault(x =>
                string.Equals(x.ProductId, player.SupportId, StringComparison.Ordinal) &&
                string.Equals(x.EquipmentSlotId, ArcadeRunDefaults.SupportSlotId, StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(support.ProductId))
                view.SetSupport(player.PlayerId, support, displayPosition);
        }

        private void SyncEnemies(IReadOnlyList<OnlineArcadeEnemySnapshot> values, bool bossWave)
        {
            seenIds.Clear();
            foreach (OnlineArcadeEnemySnapshot value in values)
            {
                seenIds.Add(value.RuntimeId);
                int id = MapId(enemyIds, value.RuntimeId);
                view.SetEnemy(id, value.EnemyConfigId, new Vector2(value.PositionX, value.PositionZ),
                    value.CurrentHealth, value.MaxHealth, value.BossPhase is ArcadeBossPhase.Telegraph or ArcadeBossPhase.RayTelegraph,
                    persistentHealthBar: bossWave || !string.IsNullOrEmpty(value.BossId));
                view.SetEnemyArmor(id, new Vector2(value.PositionX, value.PositionZ), value.ArmorHealth);
            }
            RemoveMissing(enemyIds, seenIds, id => view.RemoveEnemy(id));
        }

        private void SyncProjectiles(IReadOnlyList<OnlineArcadeProjectileSnapshot> values)
        {
            seenIds.Clear();
            foreach (OnlineArcadeProjectileSnapshot value in values)
            {
                seenIds.Add(value.RuntimeId);
                Vector2 velocity = new(value.VelocityX, value.VelocityZ);
                view.SetProjectile(MapId(projectileIds, value.RuntimeId), value.OwnerPlayerId <= 0,
                    new Vector2(value.PositionX, value.PositionZ), velocity.sqrMagnitude > .001f ? velocity.normalized : Vector2.up);
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
            IReadOnlyList<ArcadeObstacleDefinition> obstacles, AnotherDay.Arcade.ArcadeIslandRegion island = null)
        {
            if (!IsFinite(current) || !IsFinite(move) || !IsPositiveFinite(speed) ||
                !IsPositiveFinite(deltaTime)) return current;

            move = Vector2.ClampMagnitude(move, 1f);
            if (island != null)
            {
                var p = island.Move(current.x, current.y, move.x * speed * deltaTime, move.y * speed * deltaTime, radius);
                return new Vector2(p.X, p.Y);
            }
            var next = current + move * speed * deltaTime;
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

        private AnotherDay.Arcade.ArcadeTreasureRoomParameters TreasureParameters() =>
            catalog?.TreasureRooms.FirstOrDefault(r => r.ArenaConfigNumber == activeArena?.ArenaNumber);

        private void EndPresentation()
        {
            if (active) view?.End();
            active = false; activeBattleArenaNumber = 0; hasPrediction = false; predictionIsland = null; presentedInTreasureRoom = false; presentedRoomId = null; lastAppliedBattleRevision = -1; catalog = null; activeArena = null;
            enemyIds.Clear(); projectileIds.Clear(); dropIds.Clear(); seenIds.Clear(); nextPresentationId = 1;
            shieldChargesByPlayer.Clear();
            observedSupplyPickupIds.Clear();
            hasLocalFireBaseline = false;
            lastLocalFireSequence = 0;
        }
    }
}

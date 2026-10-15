using System.Collections.Generic;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 表示敌人相对玩家距离解析出的移动或攻击意图。
    /// </summary>
    public enum EnemyIntent
    {
        Idle,
        Chase,
        Attack,
        HoldRange,
        Retreat,
        Telegraph
    }

    /// <summary>
    /// 维护活动敌人并通过玩家 Unit 快照驱动追逐、停止、攻击请求和血条朝向。
    /// </summary>
    public sealed class EnemySystem : AbstractSystem
    {
        private readonly Dictionary<int, EnemyRuntimeState> enemies = new Dictionary<int, EnemyRuntimeState>();
        private readonly List<int> enemyIds = new List<int>(16);

        protected override void OnInit()
        {
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(OnUpdate);
            enemies.Clear();
        }

        public static EnemyIntent ResolveIntent(float distance, float aggroRange, float attackRange)
        {
            if (distance <= attackRange)
            {
                return EnemyIntent.Attack;
            }

            return distance <= aggroRange ? EnemyIntent.Chase : EnemyIntent.Idle;
        }

        public static EnemyIntent ResolveProjectileIntent(
            float distance,
            float aggroRange,
            float attackRange,
            float retreatDistance)
        {
            if (distance > aggroRange)
            {
                return EnemyIntent.Idle;
            }

            if (distance > attackRange)
            {
                return EnemyIntent.Chase;
            }

            if (distance < retreatDistance)
            {
                return EnemyIntent.Retreat;
            }

            return EnemyIntent.HoldRange;
        }

        public static Vector3 CalculateRetreatRequest(Vector3 currentPosition, Vector3 offsetToPlayer, float retreatDistance, float distance)
        {
            offsetToPlayer.y = 0f;
            var away = -offsetToPlayer;
            if (away.sqrMagnitude <= 0.0001f)
            {
                return currentPosition;
            }

            away.Normalize();
            var retreatOffset = Mathf.Max(1f, retreatDistance - distance + 1f);
            return currentPosition + away * retreatOffset;
        }

        public bool TryRegisterEnemy(
            IEnemyNavigationPort port,
            EnemyConfigDefinition config,
            out string error)
        {
            if (port == null || !port.IsValid)
            {
                error = "敌人 AI 注册失败：导航端口无效";
                return false;
            }

            if (enemies.ContainsKey(port.UnitId))
            {
                error = "敌人 AI 注册失败：UnitId 已登记";
                return false;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(port.UnitId, out var unit))
            {
                error = "敌人 AI 注册失败：Unit 不存在";
                return false;
            }

            if (unit.UnitType != UnitType.Enemy || !unit.IsActive)
            {
                error = "敌人 AI 注册失败：Unit 类型或活动状态无效";
                return false;
            }

            if (unit.MapInstanceId != port.MapInstanceId)
            {
                error = "敌人 AI 注册失败：MapInstanceId 不匹配";
                return false;
            }

            if (unit.ConfigId != config.Id)
            {
                error = "敌人 AI 注册失败：ConfigId 不匹配";
                return false;
            }

            enemies[port.UnitId] = new EnemyRuntimeState
            {
                Port = port,
                AggroRange = config.AggroRange,
                AttackRange = config.AttackRange,
                AttackCooldownSeconds = config.AttackCooldownSeconds,
                AttackDamage = config.AttackDamage,
                AttackKind = config.AttackKind,
                RetreatDistance = config.RetreatDistance,
                TelegraphSeconds = config.TelegraphSeconds,
                ProjectileConfigId = config.ProjectileConfigId,
                AmmoCapacity = Mathf.Max(0, config.AmmoCapacity),
                RemainingAmmo = Mathf.Max(0, config.AmmoCapacity),
                MagazineCapacity = Mathf.Max(0, config.MagazineCapacity),
                LoadedAmmo = Mathf.Min(Mathf.Max(0, config.MagazineCapacity), Mathf.Max(0, config.AmmoCapacity)),
                ReloadSeconds = Mathf.Max(0f, config.ReloadSeconds),
                ReloadIconAddress = ResolveReloadIconAddress(config),
                FallbackMeleeAttackRange = config.FallbackMeleeAttackRange,
                AttackCooldownRemaining = 0f
            };
            error = null;
            return true;
        }

        public bool UnregisterEnemy(int unitId)
        {
            CancelTelegraph(unitId);
            return enemies.Remove(unitId);
        }

        public bool CancelTelegraph(int unitId)
        {
            if (!enemies.TryGetValue(unitId, out var state))
            {
                return false;
            }

            state.TelegraphRemainingSeconds = 0f;
            state.CapturedTargetPosition = Vector3.zero;
            state.Port?.SetTelegraphVisible(false);
            if (state.Intent == EnemyIntent.Telegraph)
            {
                state.Intent = EnemyIntent.HoldRange;
            }

            enemies[unitId] = state;
            return true;
        }

        public void CancelTelegraphForMap(long mapInstanceId)
        {
            SnapshotEnemyIds();
            for (var i = 0; i < enemyIds.Count; i++)
            {
                var id = enemyIds[i];
                if (enemies.TryGetValue(id, out var state) &&
                    state.Port != null &&
                    state.Port.MapInstanceId == mapInstanceId)
                {
                    CancelTelegraph(id);
                }
            }
        }

        public bool TryGetIntent(int unitId, out EnemyIntent intent)
        {
            if (enemies.TryGetValue(unitId, out var state))
            {
                intent = state.Intent;
                return true;
            }

            intent = EnemyIntent.Idle;
            return false;
        }

        public bool TryGetRemainingAmmo(int unitId, out int remainingAmmo)
        {
            if (enemies.TryGetValue(unitId, out var state))
            {
                remainingAmmo = state.RemainingAmmo;
                return true;
            }

            remainingAmmo = 0;
            return false;
        }

        public bool TryGetReloadProgress(int unitId, out float progress01, out string iconAddress)
        {
            progress01 = 0f;
            iconAddress = string.Empty;
            if (!enemies.TryGetValue(unitId, out var state) || !state.IsReloading || state.ReloadSeconds <= 0f)
            {
                return false;
            }

            progress01 = Mathf.Clamp01(state.ReloadElapsedSeconds / state.ReloadSeconds);
            iconAddress = state.ReloadIconAddress ?? string.Empty;
            return true;
        }

        private void OnUpdate()
        {
            if (this.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing ||
                this.GetModel<ExpeditionModel>().Status == ExpeditionStatus.Defeated ||
                !TryGetActivePlayer(out var player))
            {
                StopAll();
                return;
            }

            var deltaTime = Time.deltaTime;
            SnapshotEnemyIds();
            foreach (var id in enemyIds)
            {
                if (!enemies.TryGetValue(id, out var state) ||
                    state.Port == null ||
                    !state.Port.IsValid)
                {
                    continue;
                }

                TickEnemy(id, state, player, deltaTime);
            }
        }

        private void TickEnemy(
            int id,
            EnemyRuntimeState state,
            UnitSnapshot player,
            float deltaTime)
        {
            if (state.AttackCooldownRemaining > 0f)
            {
                state.AttackCooldownRemaining -= deltaTime;
            }

            TickReload(ref state, deltaTime);

            if (!this.GetSystem<UnitSystem>().TryGet(id, out var enemyUnit) ||
                !enemyUnit.IsActive ||
                enemyUnit.MapInstanceId != state.Port.MapInstanceId ||
                enemyUnit.MapInstanceId != player.MapInstanceId)
            {
                state.Port.Stop();
                state.Port.SetTelegraphVisible(false);
                state.Intent = EnemyIntent.Idle;
                state.TelegraphRemainingSeconds = 0f;
                ClearReload(ref state);
                enemies[id] = state;
                return;
            }

            var offset = player.Position - state.Port.Position;
            offset.y = 0f;
            var distance = offset.magnitude;
            if (state.AttackKind == global::EnemyAttackKind.Projectile)
            {
                TickProjectileEnemy(id, state, player, offset, distance, deltaTime);
                return;
            }

            var intent = ResolveIntent(distance, state.AggroRange, state.AttackRange);
            state.Intent = intent;

            switch (intent)
            {
                case EnemyIntent.Chase:
                    state.Port.SetDestination(player.Position);
                    break;
                case EnemyIntent.Attack:
                    state.Port.Stop();
                    if (state.AttackCooldownRemaining <= 0f)
                    {
                        if (this.GetSystem<CombatSystem>().TryDamage(id, player.UnitId))
                        {
                            this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.Swing, 0, state.Port.Position));
                            this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.PunchBody, 0, player.Position));
                            state.Port.PlayAttackAnimation();
                        }
                        state.AttackCooldownRemaining = state.AttackCooldownSeconds;
                    }

                    break;
                default:
                    state.Port.Stop();
                    break;
            }

            enemies[id] = state;
            this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
        }

        private void TickProjectileEnemy(
            int id,
            EnemyRuntimeState state,
            UnitSnapshot player,
            Vector3 offsetToPlayer,
            float distance,
            float deltaTime)
        {
            if (distance > state.AggroRange)
            {
                var refill = state.Intent != EnemyIntent.Idle;
                state.Port.Stop();
                state.Port.SetTelegraphVisible(false);
                state.Intent = EnemyIntent.Idle;
                state.TelegraphRemainingSeconds = 0f;
                state.CapturedTargetPosition = Vector3.zero;
                if (refill)
                {
                    state.RemainingAmmo = state.AmmoCapacity;
                    state.LoadedAmmo = Mathf.Min(state.MagazineCapacity, state.RemainingAmmo);
                    ClearReload(ref state);
                }

                enemies[id] = state;
                this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
                return;
            }

            if (state.RemainingAmmo <= 0)
            {
                TickFallbackMelee(id, state, player, distance);
                return;
            }

            if (state.IsReloading || state.LoadedAmmo <= 0)
            {
                state.Port.Stop();
                state.Port.SetTelegraphVisible(false);
                state.Intent = EnemyIntent.HoldRange;
                enemies[id] = state;
                this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
                return;
            }

            if (state.TelegraphRemainingSeconds > 0f)
            {
                TickTelegraph(id, state, deltaTime);
                return;
            }

            state.Port.SetTelegraphVisible(false);
            var intent = ResolveProjectileIntent(
                distance,
                state.AggroRange,
                state.AttackRange,
                state.RetreatDistance);
            state.Intent = intent;

            switch (intent)
            {
                case EnemyIntent.Chase:
                    state.Port.SetDestination(player.Position);
                    break;
                case EnemyIntent.Retreat:
                    TryRetreatOrTelegraph(id, state, player, offsetToPlayer, distance);
                    return;
                case EnemyIntent.HoldRange:
                    state.Port.Stop();
                    if (state.AttackCooldownRemaining <= 0f)
                    {
                        BeginTelegraph(id, state, player.Position);
                        return;
                    }

                    break;
                default:
                    state.Port.Stop();
                    break;
            }

            enemies[id] = state;
            this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
        }

        private void TickFallbackMelee(int id, EnemyRuntimeState state, UnitSnapshot player, float distance)
        {
            state.Port.SetTelegraphVisible(false);
            state.TelegraphRemainingSeconds = 0f;
            state.CapturedTargetPosition = Vector3.zero;
            var intent = ResolveIntent(distance, state.AggroRange, state.FallbackMeleeAttackRange);
            state.Intent = intent;

            switch (intent)
            {
                case EnemyIntent.Chase:
                    state.Port.SetDestination(player.Position);
                    break;
                case EnemyIntent.Attack:
                    state.Port.Stop();
                    if (state.AttackCooldownRemaining <= 0f)
                    {
                        if (this.GetSystem<CombatSystem>().TryDamage(id, player.UnitId))
                        {
                            this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.Swing, 0, state.Port.Position));
                            this.SendEvent(new CombatSoundCueEvent(CombatSoundCueKind.PunchBody, 0, player.Position));
                            state.Port.PlayAttackAnimation();
                        }
                        state.AttackCooldownRemaining = state.AttackCooldownSeconds;
                    }

                    break;
                default:
                    state.Port.Stop();
                    break;
            }

            enemies[id] = state;
            this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
        }

        private void TryRetreatOrTelegraph(
            int id,
            EnemyRuntimeState state,
            UnitSnapshot player,
            Vector3 offsetToPlayer,
            float distance)
        {
            var requested = CalculateRetreatRequest(
                state.Port.Position,
                offsetToPlayer,
                state.RetreatDistance,
                distance);
            if ((requested - state.Port.Position).sqrMagnitude > 0.0001f)
            {
                if (state.Port.TrySetRetreatDestination(requested, 1f, out var destination) &&
                    IsValidRetreatDestination(state.Port.Position, destination, offsetToPlayer))
                {
                    state.Port.SetDestination(destination);
                    enemies[id] = state;
                    this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
                    return;
                }
            }

            state.Port.Stop();
            if (state.AttackCooldownRemaining <= 0f)
            {
                BeginTelegraph(id, state, player.Position);
                return;
            }

            enemies[id] = state;
            this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
        }

        private static bool IsValidRetreatDestination(Vector3 currentPosition, Vector3 destination, Vector3 offsetToPlayer)
        {
            var move = destination - currentPosition;
            move.y = 0f;
            if (move.sqrMagnitude <= 0.01f * 0.01f)
            {
                return false;
            }

            offsetToPlayer.y = 0f;
            var away = -offsetToPlayer;
            if (away.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            return Vector3.Dot(move.normalized, away.normalized) > 0.01f;
        }

        private void BeginTelegraph(int id, EnemyRuntimeState state, Vector3 capturedPlayerPosition)
        {
            state.Port.Stop();
            state.Intent = EnemyIntent.Telegraph;
            state.TelegraphRemainingSeconds = state.TelegraphSeconds;
            state.CapturedTargetPosition = capturedPlayerPosition;
            state.Port.SetTelegraphVisible(true);
            enemies[id] = state;
            this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
        }

        private void TickTelegraph(int id, EnemyRuntimeState state, float deltaTime)
        {
            state.Port.Stop();
            state.Port.SetTelegraphVisible(true);
            state.Intent = EnemyIntent.Telegraph;
            state.TelegraphRemainingSeconds -= deltaTime;
            if (state.TelegraphRemainingSeconds > 0f)
            {
                enemies[id] = state;
                this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
                return;
            }

            state.TelegraphRemainingSeconds = 0f;
            state.Port.SetTelegraphVisible(false);
            if (FireProjectile(id, state))
            {
                state.Port.PlayAttackAnimation();
                state.RemainingAmmo = Mathf.Max(0, state.RemainingAmmo - 1);
                state.LoadedAmmo = Mathf.Max(0, state.LoadedAmmo - 1);
                if (state.RemainingAmmo > 0 && state.LoadedAmmo == 0)
                {
                    BeginReload(ref state);
                }
            }

            state.AttackCooldownRemaining = state.AttackCooldownSeconds;
            state.Intent = EnemyIntent.HoldRange;
            enemies[id] = state;
            this.GetSystem<UnitSystem>().TryUpdateTransform(id, state.Port.Position, state.Port.Rotation);
        }

        private bool FireProjectile(int id, EnemyRuntimeState state)
        {
            var rootDirection = state.CapturedTargetPosition - state.Port.Position;
            rootDirection.y = 0f;
            if (rootDirection.sqrMagnitude <= 0.0001f ||
                !state.Port.TryGetProjectileOrigin(rootDirection, out var origin))
            {
                return false;
            }

            var direction = state.CapturedTargetPosition - origin;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            return this.GetSystem<ProjectileSystem>().TrySpawn(
                new ProjectileSpawnRequest(
                    id,
                    CombatFaction.Enemy,
                    state.Port.MapInstanceId,
                    state.AttackDamage,
                    origin,
                    direction,
                    state.ProjectileConfigId),
                out _,
                out _);
        }

        private bool TryGetActivePlayer(out UnitSnapshot player)
        {
            return this.GetSystem<UnitSystem>().TryGet(this.GetModel<PlayerModel>().PlayerUnitId, out player) &&
                   player.IsActive &&
                   this.GetModel<CombatModel>().TryGetCombatant(player.UnitId, out var combatant) &&
                   !combatant.IsDead;
        }

        private void StopAll()
        {
            SnapshotEnemyIds();
            for (var i = 0; i < enemyIds.Count; i++)
            {
                var unitId = enemyIds[i];
                if (!enemies.TryGetValue(unitId, out var state))
                {
                    continue;
                }

                var port = state.Port;
                if (port != null && port.IsValid)
                {
                    port.Stop();
                    port.SetTelegraphVisible(false);
                }

                ClearReload(ref state);
                enemies[unitId] = state;
            }
        }

        private void TickReload(ref EnemyRuntimeState state, float deltaTime)
        {
            if (!state.IsReloading || deltaTime <= 0f)
            {
                return;
            }

            state.ReloadElapsedSeconds = Mathf.Min(state.ReloadSeconds, state.ReloadElapsedSeconds + deltaTime);
            if (state.ReloadElapsedSeconds + 0.0001f < state.ReloadSeconds)
            {
                return;
            }

            state.LoadedAmmo = Mathf.Min(state.MagazineCapacity, state.RemainingAmmo);
            ClearReload(ref state);
        }

        private static void BeginReload(ref EnemyRuntimeState state)
        {
            if (state.RemainingAmmo <= 0 || state.MagazineCapacity <= 0 || state.ReloadSeconds <= 0f)
            {
                return;
            }

            state.IsReloading = true;
            state.ReloadElapsedSeconds = 0f;
        }

        private static void ClearReload(ref EnemyRuntimeState state)
        {
            state.IsReloading = false;
            state.ReloadElapsedSeconds = 0f;
        }

        private string ResolveReloadIconAddress(EnemyConfigDefinition config)
        {
            if (string.IsNullOrWhiteSpace(config.GuaranteedAmmoLootConfigId) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetLoot(config.GuaranteedAmmoLootConfigId, out var loot, out _) ||
                !this.GetUtility<IRuntimeConfigProvider>().TryGetItem(loot.ItemId, out var item, out _))
            {
                return string.Empty;
            }

            return item.IconAddress ?? string.Empty;
        }

        private void SnapshotEnemyIds()
        {
            enemyIds.Clear();
            foreach (var pair in enemies)
            {
                enemyIds.Add(pair.Key);
            }
        }

        private struct EnemyRuntimeState
        {
            public IEnemyNavigationPort Port;
            public float AggroRange;
            public float AttackRange;
            public float AttackCooldownSeconds;
            public int AttackDamage;
            public global::EnemyAttackKind AttackKind;
            public float RetreatDistance;
            public float TelegraphSeconds;
            public string ProjectileConfigId;
            public int AmmoCapacity;
            public int RemainingAmmo;
            public int MagazineCapacity;
            public int LoadedAmmo;
            public float ReloadSeconds;
            public float ReloadElapsedSeconds;
            public bool IsReloading;
            public string ReloadIconAddress;
            public float FallbackMeleeAttackRange;
            public float AttackCooldownRemaining;
            public float TelegraphRemainingSeconds;
            public Vector3 CapturedTargetPosition;
            public EnemyIntent Intent;
        }
    }
}

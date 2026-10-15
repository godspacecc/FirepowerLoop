using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace Game
{
    /// <summary>
    /// 按地图生成配置动态创建敌人，并把 Unit、Combat、AI 和表现生命周期原子接入。
    /// 负责候选提交与失败回滚，不决定地图出生配置或重新生成导航数据。
    /// </summary>
    public sealed class EnemyCtrl : IController, IEnemyColliderResolver, IArcadeEnemyPresentationPort
    {
        internal const float ArcadeHealthBarRevealSeconds = 2.5f;
        private readonly IArchitecture architecture;
        private readonly List<EnemyRuntimeEntry> staged = new List<EnemyRuntimeEntry>();
        private readonly List<EnemyRuntimeEntry> active = new List<EnemyRuntimeEntry>();
        private readonly List<GameObject> unownedCandidateInstances = new List<GameObject>();
        private readonly Dictionary<Collider, int> colliderUnitIds = new Dictionary<Collider, int>();
        private readonly Dictionary<string, PrefabCacheEntry> committedPrefabs =
            new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, PrefabCacheEntry> temporaryPrefabs =
            new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, LoadingState> loadingPrefabs =
            new Dictionary<string, LoadingState>();
        private readonly Dictionary<int, ArcadeEnemyPresentationEntry> arcadeEnemies =
            new Dictionary<int, ArcadeEnemyPresentationEntry>();
        private readonly List<ArcadeEnemyPresentationEntry> dyingArcadeEnemies =
            new List<ArcadeEnemyPresentationEntry>();
        private readonly Dictionary<string, Stack<EnemyView>> arcadeEnemyPool =
            new Dictionary<string, Stack<EnemyView>>(StringComparer.Ordinal);
        private Transform arcadeCandidateRoot;
        private long arcadeCandidateMapInstanceId;
        private Transform arcadeRoot;
        private long arcadeMapInstanceId;
        private bool stagedArcade;
        private IUnRegister healthChangedUnregister;
        private IUnRegister diedUnregister;
        private IUnRegister unitHealthBarVisibilityUnregister;
        private IUnRegister damageAppliedUnregister;
        private bool released;
        private bool stagedPrepared;

        public EnemyCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            healthChangedUnregister = architecture.RegisterEvent<CombatHealthChangedEvent>(OnCombatHealthChanged);
            diedUnregister = architecture.RegisterEvent<CombatantDiedEvent>(OnCombatantDied);
            damageAppliedUnregister = architecture.RegisterEvent<CombatDamageAppliedEvent>(OnDamageApplied);
            unitHealthBarVisibilityUnregister =
                architecture.RegisterEvent<UnitHealthBarVisibilityChangedEvent>(OnUnitHealthBarVisibilityChanged);
            LifeCycle.RegisterUpdate(OnUpdate);
            LifeCycle.RegisterLateUpdate(OnLateUpdate);
        }

        public IArchitecture GetArchitecture()
        {
            return architecture;
        }

        public async UniTask<MapControllerResult> PrepareMapAsync(
            MapPreparationContext context,
            CancellationToken cancellationToken)
        {
            if (stagedPrepared)
            {
                return MapControllerResult.Fail("敌人准备失败：已有候选敌人");
            }

            if (context?.Scene == null)
            {
                return MapControllerResult.Fail("敌人准备失败：缺少 MapContext");
            }

            stagedPrepared = true;
            try
            {
                if (context.Mode == MapPreparationMode.ArcadePlayerOnly)
                    return await PrepareArcadeAsync(context, cancellationToken);

                var spawns = new List<MapSpawnDefinition>(context.Spawns);
                List<ExpeditionEnemySaveData> restoredSnapshots = null;
                // 河谷恢复的空敌人快照同样权威；不能让已经清空的旧局按新配置重新刷出敌人。
                if (context.ExpeditionSnapshot?.World?.Enemies != null && (context.ExpeditionSnapshot.World.Enemies.Count > 0 || context.ValleyLayout != null))
                {
                    restoredSnapshots = context.ExpeditionSnapshot.World.Enemies;
                    spawns = new List<MapSpawnDefinition>();
                    foreach (var saved in restoredSnapshots)
                    {
                        if (saved == null || saved.Pose == null || saved.RuntimeId <= 0 || string.IsNullOrWhiteSpace(saved.ConfigId))
                        {
                            return FailPrepared("敌人恢复失败：快照实体无效");
                        }
                        var configId = saved.ConfigId;
                        var pos = new Vector3(saved.Pose.X, saved.Pose.Y, saved.Pose.Z);
                        var rot = Quaternion.Euler(0f, saved.Pose.RotationY, 0f);
                        var local = context.Scene.DynamicObjectRoot.InverseTransformPoint(pos);
                        var localRot = Quaternion.Inverse(context.Scene.DynamicObjectRoot.rotation) * rot;
                        spawns.Add(new MapSpawnDefinition("snapshot-enemy", context.MapId, MapSpawnObjectType.Enemy, configId, local.x, local.y, local.z, localRot.eulerAngles.y));
                    }
                }
                for (var i = 0; i < spawns.Count; i++)
                {
                    var spawn = spawns[i];
                    if (spawn.ObjectType != MapSpawnObjectType.Enemy)
                    {
                        continue;
                    }

                    if (!this.GetUtility<IRuntimeConfigProvider>()
                        .TryGetEnemy(spawn.ConfigId, out var config, out var configError))
                    {
                        return FailPrepared("敌人准备失败：" + configError);
                    }

                    if (!string.IsNullOrWhiteSpace(config.LootConfigId) &&
                        !this.GetUtility<IRuntimeConfigProvider>()
                            .TryGetLoot(config.LootConfigId, out _, out var lootError))
                    {
                        return FailPrepared("敌人准备失败：" + lootError);
                    }

                    if (!ValidateEnemyLoot(config.GuaranteedWeaponLootConfigId, out lootError) ||
                        !ValidateEnemyLoot(config.GuaranteedAmmoLootConfigId, out lootError))
                    {
                        return FailPrepared("敌人准备失败：" + lootError);
                    }

                    var prefabResult = await LoadEnemyPrefabAsync(config, cancellationToken);
                    if (!prefabResult.Success)
                    {
                        return FailPrepared(prefabResult.Error);
                    }

                    if (!ValidatePrefab(prefabResult.Prefab, out var prefabView, out var prefabError))
                    {
                        return FailPrepared(prefabError);
                    }

                    var worldPosition = context.Scene.DynamicObjectRoot.TransformPoint(spawn.LocalPosition);
                    var worldRotation = context.Scene.DynamicObjectRoot.rotation * spawn.LocalRotation;
                    var view = InstantiateInactive(this.GetUtility<IViewInstanceProvider>(),
                        prefabView,
                        context.Scene.DynamicObjectRoot,
                        worldPosition,
                        worldRotation);
                    if (view == null)
                    {
                        return FailPrepared("敌人准备失败：Prefab 实例化返回空");
                    }

                    var instanceObject = view.gameObject;
                    unownedCandidateInstances.Add(instanceObject);
                    view.BeginPresentationUse();
                    if (!ValidatePresentationWeaponKind(view, config, out var presentationError))
                        return FailPrepared("敌人武器表现不可用：" + presentationError);
                    if (view.GetComponent<CharacterWeaponPresenter>() != null &&
                        !view.SetCombatWeapon(config.WeaponId))
                        return FailPrepared($"敌人武器表现不可用：{config.Id}/{config.WeaponId}");
                    view.ConfigureNavigation(config.MoveSpeed, config.AttackRange);

                    var restored = restoredSnapshots != null ? restoredSnapshots[staged.Count] : null;
                    var unitId = restored?.RuntimeId ?? this.GetSystem<UnitSystem>().AllocateUnitId();
                    var viewCtrl = new EnemyViewCtrl(
                        view,
                        unitId,
                        context.MapInstanceId,
                        this.GetUtility<ViewCtrlRegistry>(),
                        this.GetUtility<IViewInstanceProvider>());
                    unownedCandidateInstances.Remove(instanceObject);
                    var entry = new EnemyRuntimeEntry(viewCtrl, config, instanceObject.GetComponentsInChildren<Collider>(true));
                    if (restored != null)
                    {
                        entry.Corpse = restored.IsCorpse;
                        entry.RestoredHealth = Mathf.Clamp(Mathf.RoundToInt(restored.CurrentHealth), 0, config.MaxHP);
                    }
                    staged.Add(entry);
                }

                return MapControllerResult.Ok();
            }
            catch (OperationCanceledException)
            {
                return FailPrepared("敌人准备失败：请求已取消");
            }
            catch (Exception ex)
            {
                return FailPrepared("敌人准备失败：" + ex.Message);
            }
        }

        public MapControllerResult CommitPreparedMap()
        {
            if (active.Count > 0)
            {
                return MapControllerResult.Fail("敌人提交失败：已有活动敌人");
            }

            if (!stagedPrepared)
            {
                return MapControllerResult.Fail("敌人提交失败：没有候选敌人");
            }

            if (stagedArcade)
            {
                if (arcadeCandidateRoot == null || arcadeCandidateMapInstanceId <= 0)
                    return FailPrepared("敌人提交失败：街机候选上下文无效");
                ReleaseArcadePresentation();
                arcadeRoot = arcadeCandidateRoot;
                arcadeMapInstanceId = arcadeCandidateMapInstanceId;
                arcadeCandidateRoot = null;
                arcadeCandidateMapInstanceId = 0;
                stagedArcade = false;
                stagedPrepared = false;
                PromoteTemporaryPrefabs();
                return MapControllerResult.Ok();
            }

            if (!ValidateStagedNavMesh(out var navMeshError))
            {
                return FailPrepared(navMeshError);
            }

            var committedThisTurn = new List<EnemyRuntimeEntry>();
            for (var i = 0; i < staged.Count; i++)
            {
                var enemy = staged[i];
                var snapshot = new UnitSnapshot(
                    enemy.ViewCtrl.UnitId,
                    enemy.ViewCtrl.MapInstanceId,
                    UnitType.Enemy,
                    UnitSource.MapPlaced,
                    enemy.Config.Id,
                    enemy.ViewCtrl.Position,
                    enemy.ViewCtrl.Rotation,
                    UnitAuthorityMode.Local,
                    0,
                    true);

                if (!this.GetSystem<UnitSystem>().TryRegister(snapshot, out var unitError))
                {
                    RollbackCommit(committedThisTurn);
                    return FailPrepared(unitError);
                }

                if (enemy.Corpse)
                {
                    enemy.ViewCtrl.SetHealthDisplay(0, enemy.Config.MaxHP);
                    enemy.ViewCtrl.MarkDead();
                    enemy.ViewCtrl.SetVisible(true);
                    RegisterColliders(enemy);
                    active.Add(enemy);
                    committedThisTurn.Add(enemy);
                    continue;
                }

                if (!this.GetSystem<CombatSystem>()
                    .TryRegisterEnemy(enemy.ViewCtrl.UnitId, enemy.Config, out var combatError))
                {
                    this.GetSystem<UnitSystem>()
                        .Remove(enemy.ViewCtrl.UnitId, UnitRemovedReason.InvalidPresentation);
                    RollbackCommit(committedThisTurn);
                    return FailPrepared(combatError);
                }

                if (!this.GetSystem<EnemySystem>()
                    .TryRegisterEnemy(enemy.ViewCtrl, enemy.Config, out var aiError))
                {
                    this.GetSystem<CombatSystem>().TryRemoveCombatant(enemy.ViewCtrl.UnitId);
                    this.GetSystem<UnitSystem>()
                        .Remove(enemy.ViewCtrl.UnitId, UnitRemovedReason.InvalidPresentation);
                    RollbackCommit(committedThisTurn);
                    return FailPrepared(aiError);
                }

                if (enemy.RestoredHealth.HasValue &&
                    this.GetModel<CombatModel>().TrySetCombatantHealth(enemy.ViewCtrl.UnitId, enemy.RestoredHealth.Value, out var health))
                {
                    enemy.ViewCtrl.SetHealthDisplay(health.HP, health.MaxHP);
                }
                else
                {
                    enemy.ViewCtrl.SetHealthDisplay(enemy.Config.MaxHP, enemy.Config.MaxHP);
                }
                enemy.ViewCtrl.SetHealthBarVisible(GetUnitHealthBarsVisible());
                enemy.ViewCtrl.SetVisible(true);
                RegisterColliders(enemy);
                active.Add(enemy);
                committedThisTurn.Add(enemy);
            }

            staged.Clear();
            stagedPrepared = false;
            PromoteTemporaryPrefabs();
            return MapControllerResult.Ok();
        }

        public void RollbackPreparedMap()
        {
            ReleaseStaged();
            ReleaseTemporaryPrefabs();
            arcadeCandidateRoot = null;
            arcadeCandidateMapInstanceId = 0;
            stagedArcade = false;
        }

        public void SetCurrentMapActive(bool activeState)
        {
            foreach (var entry in arcadeEnemies.Values)
                if (entry.View != null && entry.View.gameObject.activeSelf != activeState)
                    entry.View.gameObject.SetActive(activeState);
            for (var i = 0; i < active.Count; i++)
            {
                var enemy = active[i];
                var visible = activeState && !enemy.ReleasePending;
                this.GetSystem<UnitSystem>().TrySetActive(enemy.ViewCtrl.UnitId, visible);
                if (!visible)
                {
                    if (!enemy.Corpse)
                    {
                        this.GetSystem<EnemySystem>().CancelTelegraph(enemy.ViewCtrl.UnitId);
                        enemy.ViewCtrl.Stop();
                    }

                    enemy.ViewCtrl.SetVisible(false);
                    continue;
                }

                if (enemy.Corpse)
                {
                    enemy.ViewCtrl.SetVisible(true);
                    enemy.ViewCtrl.MarkDead();
                }
                else
                {
                    enemy.ViewCtrl.SetHealthBarVisible(GetUnitHealthBarsVisible());
                    enemy.ViewCtrl.SetVisible(true);
                }
            }
        }

        public void CaptureExpeditionWorld(ExpeditionWorldSaveData world)
        {
            if (world == null) return;
            world.Enemies.Clear();
            for (var i = 0; i < active.Count; i++)
            {
                var entry = active[i];
                world.Enemies.Add(new ExpeditionEnemySaveData
                {
                    RuntimeId = entry.ViewCtrl.UnitId,
                    ConfigId = entry.Config.Id,
                    Pose = new ExpeditionPoseData { X = entry.ViewCtrl.Position.x, Y = entry.ViewCtrl.Position.y, Z = entry.ViewCtrl.Position.z, RotationY = entry.ViewCtrl.Rotation.eulerAngles.y },
                    CurrentHealth = this.GetModel<CombatModel>().TryGetCombatant(entry.ViewCtrl.UnitId, out var health) ? health.HP : 0f,
                    MaxHealth = this.GetModel<CombatModel>().TryGetCombatant(entry.ViewCtrl.UnitId, out health) ? health.MaxHP : entry.Config.MaxHP,
                    IsCorpse = entry.Corpse || entry.ReleasePending
                });
            }
        }

        public void ReleaseCurrentMap()
        {
            ReleaseCommitted();
            ReleaseArcadePresentation();
        }

        public void ReleaseAll()
        {
            released = true;
            healthChangedUnregister?.UnRegister();
            diedUnregister?.UnRegister();
            unitHealthBarVisibilityUnregister?.UnRegister();
            damageAppliedUnregister?.UnRegister();
            healthChangedUnregister = null;
            diedUnregister = null;
            unitHealthBarVisibilityUnregister = null;
            damageAppliedUnregister = null;
            LifeCycle.UnregisterUpdate(OnUpdate);
            LifeCycle.UnregisterLateUpdate(OnLateUpdate);
            ReleaseStaged();
            ReleaseCommitted();
            ReleaseArcadePresentation();
            ReleaseTemporaryPrefabs();
            ReleaseCommittedPrefabs();
        }

        public bool TryResolveEnemyUnitId(Collider collider, out int unitId)
        {
            unitId = 0;
            return collider != null && colliderUnitIds.TryGetValue(collider, out unitId);
        }

        /// <summary>创建或刷新真实 EnemyView；只更新表现，不注册 Unit、Combat 或 EnemySystem。</summary>
        public bool TrySetEnemy(int runtimeId, string configId, Vector3 position, Vector3 facing,
            int currentHealth, int maximumHealth, out string error, bool telegraph = false,
            int attackSequence = 0, bool persistentHealthBar = false)
        {
            error = null;
            if (runtimeId <= 0 || arcadeRoot == null || arcadeMapInstanceId <= 0)
            {
                error = "街机敌人表现不可用";
                return false;
            }

            arcadeEnemies.TryGetValue(runtimeId, out var current);
            if (current != null && !string.Equals(current.ConfigId, configId, StringComparison.Ordinal))
            {
                RecycleArcadeEnemy(current);
                arcadeEnemies.Remove(runtimeId);
                current = null;
            }

            if (current == null)
            {
                if (!committedPrefabs.TryGetValue(configId, out var prefab) || prefab.Prefab == null ||
                    !ValidatePrefab(prefab.Prefab, out var prefabView, out error))
                {
                    error = error ?? $"街机敌人 Prefab 未预加载：{configId}";
                    return false;
                }

                var view = TakeArcadeEnemy(configId);
                if (view == null)
                    view = InstantiateInactive(this.GetUtility<IViewInstanceProvider>(), prefabView, arcadeRoot, position, Quaternion.identity);
                if (view == null)
                {
                    error = "街机敌人表现创建失败";
                    return false;
                }

                try
                {
                    view.BeginPresentationUse();
                    view.ConfigureRuntime(runtimeId, arcadeMapInstanceId);
                    if (!this.GetUtility<IRuntimeConfigProvider>()
                            .TryGetEnemy(configId, out var weaponConfig, out var weaponError))
                    {
                        this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                        error = weaponError ?? $"街机敌人配置不可用：{configId}";
                        return false;
                    }
                    if (!ValidatePresentationWeaponKind(view, weaponConfig, out var presentationError) ||
                        view.GetComponent<CharacterWeaponPresenter>() != null &&
                        !view.SetCombatWeapon(weaponConfig.WeaponId))
                    {
                        this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                        error = presentationError ?? $"街机敌人武器表现不可用：{configId}/{weaponConfig.WeaponId}";
                        return false;
                    }
                    view.SetPresentationVariant(runtimeId);
                    view.SetNavigationEnabled(false);
                    view.SetHealthBarVisible(persistentHealthBar);
                    view.gameObject.SetActive(true);
                    current = new ArcadeEnemyPresentationEntry(runtimeId, configId, view, maximumHealth,
                        position, attackSequence, persistentHealthBar);
                    arcadeEnemies.Add(runtimeId, current);
                }
                catch (Exception exception)
                {
                    this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                    error = $"街机敌人表现初始化失败：{configId} / {exception.Message}";
                    return false;
                }
            }

            if (current.PersistentHealthBar != persistentHealthBar)
            {
                current.PersistentHealthBar = persistentHealthBar;
                current.View.SetHealthBarVisible(ShouldShowArcadeHealthBar(
                    current.PersistentHealthBar, current.HealthBarRevealRemaining));
            }

            var direction = facing;
            direction.y = 0f;
            current.View.transform.position = position;
            if (direction.sqrMagnitude > 0.0001f)
                current.View.transform.rotation = Quaternion.LookRotation(direction.normalized, arcadeRoot.up);
            if (currentHealth < current.LastHealth)
            {
                current.View.PlayHitFeedback();
                current.View.ShowDamage(current.LastHealth - currentHealth);
                current.HealthBarRevealRemaining = ArcadeHealthBarRevealSeconds;
                current.View.SetHealthBarVisible(true);
            }
            current.View.SetHealthDisplay(currentHealth, maximumHealth);
            current.View.SetTelegraphVisible(telegraph);
            if (attackSequence > 0 && attackSequence != current.LastAttackSequence)
                current.View.PlayAttackAnimation();
            current.LastHealth = currentHealth;
            current.LastPosition = position;
            current.LastAttackSequence = attackSequence;
            return true;
        }

        public void RemoveEnemy(int runtimeId, bool playDeath = false)
        {
            if (!arcadeEnemies.TryGetValue(runtimeId, out var entry)) return;
            arcadeEnemies.Remove(runtimeId);
            if (playDeath && entry.View != null && entry.View.TryPlayDeathAnimation(out var durationSeconds) &&
                durationSeconds > 0f)
            {
                entry.View.SetHealthBarVisible(false);
                entry.View.SetTelegraphVisible(false);
                entry.ReleaseTime = Time.time + durationSeconds;
                dyingArcadeEnemies.Add(entry);
                return;
            }
            RecycleArcadeEnemy(entry);
        }

        public void ClearArcadeEnemies()
        {
            var values = new List<ArcadeEnemyPresentationEntry>(arcadeEnemies.Values);
            arcadeEnemies.Clear();
            for (var i = 0; i < values.Count; i++) RecycleArcadeEnemy(values[i]);
            for (var i = 0; i < dyingArcadeEnemies.Count; i++) RecycleArcadeEnemy(dyingArcadeEnemies[i]);
            dyingArcadeEnemies.Clear();
        }

        /// <summary>街机地图候选阶段只预载会出现在波次中的敌人 Prefab，不创建正式战斗实体。</summary>
        private async UniTask<MapControllerResult> PrepareArcadeAsync(
            MapPreparationContext context,
            CancellationToken cancellationToken)
        {
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            string catalogError = null;
            if (provider == null || !provider.TryGetArcadeCatalog(out var catalog, out catalogError))
                return FailPrepared("敌人准备失败：" + (catalogError ?? "街机配置不可用"));

            var enemyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in catalog.EnemyGroups)
            foreach (var member in group.Members)
                if (!string.IsNullOrWhiteSpace(member.EnemyId)) enemyIds.Add(member.EnemyId);

            foreach (var enemyId in enemyIds)
            {
                if (!provider.TryGetEnemy(enemyId, out var config, out var configError))
                    return FailPrepared("敌人准备失败：" + configError);
                var prefabResult = await LoadEnemyPrefabAsync(config, cancellationToken);
                if (!prefabResult.Success) return FailPrepared(prefabResult.Error);
                if (!ValidatePrefab(prefabResult.Prefab, out _, out var prefabError))
                    return FailPrepared(prefabError);
            }

            arcadeCandidateRoot = context.Scene.DynamicObjectRoot;
            arcadeCandidateMapInstanceId = context.MapInstanceId;
            stagedArcade = true;
            return MapControllerResult.Ok();
        }

        private EnemyView TakeArcadeEnemy(string configId)
        {
            if (!arcadeEnemyPool.TryGetValue(configId, out var pool)) return null;
            while (pool.Count > 0)
            {
                var view = pool.Pop();
                if (view != null) return view;
            }
            return null;
        }

        private void RecycleArcadeEnemy(ArcadeEnemyPresentationEntry entry)
        {
            if (entry?.View == null) return;
            entry.View.SetNavigationEnabled(false);
            entry.View.SetTelegraphVisible(false);
            entry.View.gameObject.SetActive(false);
            entry.View.ResetWeaponSelection();
            if (!arcadeEnemyPool.TryGetValue(entry.ConfigId, out var pool))
            {
                pool = new Stack<EnemyView>();
                arcadeEnemyPool.Add(entry.ConfigId, pool);
            }
            pool.Push(entry.View);
        }

        private void ReleaseArcadePresentation()
        {
            foreach (var entry in arcadeEnemies.Values)
                if (entry.View != null) this.GetUtility<IViewInstanceProvider>().Release(entry.View.gameObject);
            arcadeEnemies.Clear();
            for (var i = 0; i < dyingArcadeEnemies.Count; i++)
                if (dyingArcadeEnemies[i].View != null)
                    this.GetUtility<IViewInstanceProvider>().Release(dyingArcadeEnemies[i].View.gameObject);
            dyingArcadeEnemies.Clear();
            foreach (var pool in arcadeEnemyPool.Values)
                while (pool.Count > 0)
                {
                    var view = pool.Pop();
                    if (view != null) this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
                }
            arcadeEnemyPool.Clear();
            arcadeRoot = null;
            arcadeMapInstanceId = 0;
        }

        private MapControllerResult FailPrepared(string error)
        {
            ReleaseStaged();
            ReleaseTemporaryPrefabs();
            arcadeCandidateRoot = null;
            arcadeCandidateMapInstanceId = 0;
            stagedArcade = false;
            return MapControllerResult.Fail(error);
        }

        /// <summary>
        /// 装配前核对本实例表现目录；表中 AttackKind 决定玩法，目录标志只验证表现与玩法一致。
        /// 旧非人形无共享 Presenter 时沿原路径，不在此创建实例或修改战斗状态。
        /// </summary>
        internal static bool ValidatePresentationWeaponKind(EnemyView view, EnemyConfigDefinition config,
            out string error)
        {
            error = null;
            if (view == null)
            {
                error = $"敌人 {config.Id} / 武器 {config.WeaponId}：候选实例缺失";
                return false;
            }
            var presenter = view.GetComponent<CharacterWeaponPresenter>();
            if (presenter == null) return true;

            var expected = config.AttackKind == global::EnemyAttackKind.Melee ? "近战" :
                config.AttackKind == global::EnemyAttackKind.Projectile ? "投射" :
                $"不支持的玩法类别 {config.AttackKind}";
            var prefix = $"敌人 {config.Id} / 武器 {config.WeaponId}：期望 {expected}";
            var catalog = presenter.Catalog;
            if (catalog == null)
            {
                error = prefix + "，实例缺少共享表现目录";
                return false;
            }
            if (!catalog.ValidateIdentities(out var catalogError))
            {
                error = prefix + "，目录身份校验失败：" + catalogError;
                return false;
            }
            var entry = catalog.Find(config.WeaponId);
            if (entry == null)
            {
                error = prefix + "，目录缺少该精确武器 ID";
                return false;
            }
            var actual = entry.IsProjectile ? "投射" : "近战";
            if ((config.AttackKind != global::EnemyAttackKind.Melee &&
                 config.AttackKind != global::EnemyAttackKind.Projectile) ||
                entry.IsProjectile != (config.AttackKind == global::EnemyAttackKind.Projectile))
            {
                error = prefix + "，实际目录类别 " + actual;
                return false;
            }
            return true;
        }

        private bool ValidateEnemyLoot(string lootConfigId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(lootConfigId))
            {
                return true;
            }

            return this.GetUtility<IRuntimeConfigProvider>().TryGetLoot(lootConfigId, out _, out error);
        }

        private async UniTask<PrefabLoadResult> LoadEnemyPrefabAsync(
            EnemyConfigDefinition config,
            CancellationToken cancellationToken)
        {
            if (committedPrefabs.TryGetValue(config.Id, out var committed) && committed.Prefab != null)
            {
                return PrefabLoadResult.Ok(committed.Prefab);
            }

            if (temporaryPrefabs.TryGetValue(config.Id, out var temporary) && temporary.Prefab != null)
            {
                return PrefabLoadResult.Ok(temporary.Prefab);
            }

            if (loadingPrefabs.ContainsKey(config.Id))
            {
                return PrefabLoadResult.Fail("敌人准备失败：敌人 Prefab 正在加载");
            }

            var state = new LoadingState();
            loadingPrefabs.Add(config.Id, state);
            state.Handle = ResourceLoader.LoadAssetAsync<GameObject>(config.PrefabAddress, asset =>
            {
                state.CallbackArrived = true;
                if (state.CancelledBeforeCallback || released)
                {
                    ReleaseHandleOnce(state);
                    loadingPrefabs.Remove(config.Id);
                    state.Completion.TrySetResult(null);
                    return;
                }

                state.Completion.TrySetResult(asset);
            });

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                state.CancelledBeforeCallback = !state.CallbackArrived;
                state.Completion.TrySetCanceled(cancellationToken);
            });

            GameObject prefab;
            try
            {
                prefab = await state.Completion.Task;
            }
            catch (OperationCanceledException)
            {
                if (state.CallbackArrived)
                {
                    loadingPrefabs.Remove(config.Id);
                }

                return PrefabLoadResult.Fail("敌人准备失败：请求已取消");
            }

            loadingPrefabs.Remove(config.Id);
            if (prefab == null)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("敌人准备失败：敌人 Prefab 加载失败");
            }

            if (cancellationToken.IsCancellationRequested || released)
            {
                ReleaseHandleOnce(state);
                return PrefabLoadResult.Fail("敌人准备失败：请求已取消");
            }

            temporaryPrefabs[config.Id] = new PrefabCacheEntry(prefab, state.Handle);
            return PrefabLoadResult.Ok(prefab);
        }

        private static void ReleaseHandleOnce(LoadingState state)
        {
            if (state.Handle.IsValid && !state.HandleReleased)
            {
                ResourceLoader.Release(state.Handle);
                state.HandleReleased = true;
            }
        }

        /// <summary>在禁用的临时父节点下创建实例，避免首次 OnEnable 先于武器和生命配置执行。</summary>
        private static EnemyView InstantiateInactive(IViewInstanceProvider instances, EnemyView prefab,
            Transform parent, Vector3 position, Quaternion rotation)
        {
            var staging = new GameObject("EnemyPresentationStaging");
            staging.SetActive(false);
            try
            {
                var view = instances?.Instantiate(prefab, staging.transform, position, rotation);
                if (view == null) return null;
                if (view.gameObject.activeInHierarchy)
                {
                    instances.Release(view.gameObject);
                    throw new InvalidOperationException("敌人候选实例创建时意外激活");
                }
                view.gameObject.SetActive(false);
                view.transform.SetParent(parent, true);
                return view;
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(staging);
                else UnityEngine.Object.DestroyImmediate(staging);
            }
        }

        private static bool ValidatePrefab(
            GameObject prefab,
            out EnemyView view,
            out string error)
        {
            view = null;
            if (prefab == null)
            {
                error = "敌人准备失败：Prefab 为空";
                return false;
            }

            view = prefab.GetComponent<EnemyView>();
            if (view == null)
            {
                error = "敌人准备失败：Prefab 根缺少 EnemyView";
                return false;
            }

            if (prefab.GetComponent<Collider>() == null)
            {
                error = "敌人准备失败：Prefab 根缺少 Collider";
                return false;
            }

            if (prefab.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
            {
                error = "敌人准备失败：Prefab 根缺少 NavMeshAgent";
                return false;
            }

            if (!view.ValidateDamageNumbers(out error))
            {
                error = "敌人准备失败：" + error;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>提交领域实体前校验整批出生位置；失败只记录现场，不移动候选或放宽导航条件。</summary>
        private bool ValidateStagedNavMesh(out string error)
        {
            const float SpawnSampleRadius = 1f;
            for (var i = 0; i < staged.Count; i++)
            {
                var enemy = staged[i];
                if (!enemy.ViewCtrl.IsOnNavigableSurface(SpawnSampleRadius))
                {
                    LogStagedNavMeshFailure(enemy, SpawnSampleRadius);
                    error = "敌人提交失败：出生点不在 NavMesh 中 " + enemy.Config.Id;
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>仅在出生校验失败时读取导航现场；诊断异常保留为文本，不替代原失败语义或阻断回滚。</summary>
        /// <param name="sampleRadius">原校验半径，单位米；扩大采样仅用于报告，不参与提交判定。</param>
        private static void LogStagedNavMeshFailure(EnemyRuntimeEntry enemy, float sampleRadius)
        {
            var text = new StringBuilder("[EnemyNavMeshDiagnostic] ");
            try
            {
                text.AppendLine($"enemy={enemy.Config.Id} unit={enemy.ViewCtrl.UnitId} mapInstance={enemy.ViewCtrl.MapInstanceId}");
                text.AppendLine($"platform={Application.platform} appVersion={Application.version} unityVersion={Application.unityVersion} sampleRadius={sampleRadius}");
                enemy.ViewCtrl.AppendNavigationDiagnostic(text, sampleRadius);
                AppendGlobalNavMeshDiagnostic(text);
            }
            catch (Exception exception)
            {
                text.AppendLine($"diagnosticReadFailed={exception.GetType().Name}: {exception.Message}");
            }
            Log.Warning(text.ToString());
        }

        /// <summary>隔离可选三角化 API 的解析；旧宿主缺失方法时由外层保留基础日志并继续回滚。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AppendGlobalNavMeshDiagnostic(StringBuilder text)
        {
            // 三角化统计覆盖当前全部已注册导航数据，不能单独证明候选地图的数据已加载。
            var triangulation = NavMesh.CalculateTriangulation();
            text.AppendLine($"navMeshScope=global vertices={triangulation.vertices.Length} triangles={triangulation.indices.Length / 3}");
        }

        private void PromoteTemporaryPrefabs()
        {
            foreach (var pair in temporaryPrefabs)
            {
                committedPrefabs[pair.Key] = pair.Value;
            }

            temporaryPrefabs.Clear();
        }

        private void ReleaseTemporaryPrefabs()
        {
            foreach (var pair in temporaryPrefabs)
            {
                if (pair.Value.Handle.IsValid)
                {
                    ResourceLoader.Release(pair.Value.Handle);
                }
            }

            temporaryPrefabs.Clear();
        }

        private void ReleaseCommittedPrefabs()
        {
            foreach (var pair in committedPrefabs)
            {
                if (pair.Value.Handle.IsValid)
                {
                    ResourceLoader.Release(pair.Value.Handle);
                }
            }

            committedPrefabs.Clear();
        }

        private void RollbackCommit(List<EnemyRuntimeEntry> committedThisTurn)
        {
            for (var i = committedThisTurn.Count - 1; i >= 0; i--)
            {
                UnregisterColliders(committedThisTurn[i]);
                RemoveDomains(committedThisTurn[i], UnitRemovedReason.InvalidPresentation);
                active.Remove(committedThisTurn[i]);
            }
        }

        private void ReleaseCommitted()
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                RemoveDomains(active[i], UnitRemovedReason.MapInstanceReleased);
                active[i].ViewCtrl.Dispose();
            }

            active.Clear();
            colliderUnitIds.Clear();
        }

        private void ReleaseStaged()
        {
            for (var i = staged.Count - 1; i >= 0; i--)
            {
                staged[i].ViewCtrl.Dispose();
            }

            staged.Clear();
            ReleaseUnownedCandidateInstances();
            stagedPrepared = false;
        }

        private void ReleaseUnownedCandidateInstances()
        {
            for (var i = unownedCandidateInstances.Count - 1; i >= 0; i--)
            {
                this.GetUtility<IViewInstanceProvider>().Release(unownedCandidateInstances[i]);
            }

            unownedCandidateInstances.Clear();
        }

        private void RemoveDomains(EnemyRuntimeEntry enemy, UnitRemovedReason reason)
        {
            UnregisterColliders(enemy);
            this.GetSystem<EnemySystem>().UnregisterEnemy(enemy.ViewCtrl.UnitId);
            this.GetSystem<CombatSystem>().TryRemoveCombatant(enemy.ViewCtrl.UnitId);
            this.GetSystem<UnitSystem>().Remove(enemy.ViewCtrl.UnitId, reason);
        }

        private void RegisterColliders(EnemyRuntimeEntry enemy)
        {
            for (var i = 0; i < enemy.Colliders.Length; i++)
            {
                var collider = enemy.Colliders[i];
                if (collider != null)
                {
                    colliderUnitIds[collider] = enemy.ViewCtrl.UnitId;
                }
            }
        }

        private void OnUnitHealthBarVisibilityChanged(UnitHealthBarVisibilityChangedEvent evt)
        {
            for (var i = 0; i < active.Count; i++)
            {
                if (!active[i].Corpse)
                {
                    active[i].ViewCtrl.SetHealthBarVisible(evt.Visible);
                }
            }
        }

        private bool GetUnitHealthBarsVisible()
        {
            try
            {
                return architecture.GetModel<TestCmdModel>().UnitHealthBarsVisible;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UnregisterColliders(EnemyRuntimeEntry enemy)
        {
            for (var i = 0; i < enemy.Colliders.Length; i++)
            {
                var collider = enemy.Colliders[i];
                if (!ReferenceEquals(collider, null))
                {
                    colliderUnitIds.Remove(collider);
                }
            }
        }

        private void OnUpdate()
        {
            for (var i = dyingArcadeEnemies.Count - 1; i >= 0; i--)
            {
                var entry = dyingArcadeEnemies[i];
                entry.View?.TickCombatFeedback(Time.deltaTime);
                if (entry.View != null && Time.time < entry.ReleaseTime) continue;
                RecycleArcadeEnemy(entry);
                dyingArcadeEnemies.RemoveAt(i);
            }
            foreach (var entry in arcadeEnemies.Values)
            {
                entry.View?.TickCombatFeedback(Time.deltaTime);
                if (entry.View == null || entry.PersistentHealthBar) continue;
                var before = entry.HealthBarRevealRemaining;
                entry.HealthBarRevealRemaining = AdvanceArcadeHealthBarReveal(before, Time.deltaTime);
                if (before > 0f && entry.HealthBarRevealRemaining <= 0f)
                    entry.View.SetHealthBarVisible(false);
            }
            for (var i = active.Count - 1; i >= 0; i--)
            {
                var enemy = active[i];
                enemy.ViewCtrl.TickCombatFeedback(Time.deltaTime);
                if (!enemy.Corpse && this.GetSystem<EnemySystem>().TryGetReloadProgress(enemy.ViewCtrl.UnitId, out var reloadProgress, out var reloadIcon))
                {
                    enemy.ViewCtrl.SetActionProgressVisible(true, reloadProgress, reloadIcon);
                }
                else
                {
                    enemy.ViewCtrl.SetActionProgressVisible(false);
                }
                if (!enemy.Corpse && this.GetSystem<EnemySystem>().TryGetIntent(enemy.ViewCtrl.UnitId, out var intent))
                {
                    enemy.ViewCtrl.SetAlerted(intent != EnemyIntent.Idle);
                }

                if (!enemy.ReleasePending)
                {
                    continue;
                }

                UnregisterColliders(enemy);
                this.GetSystem<EnemySystem>().UnregisterEnemy(enemy.ViewCtrl.UnitId);
                this.GetSystem<CombatSystem>().TryRemoveCombatant(enemy.ViewCtrl.UnitId);
                enemy.ReleasePending = false;
                enemy.Corpse = true;
            }
        }

        private void OnLateUpdate()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            for (var i = 0; i < active.Count; i++)
            {
                active[i].ViewCtrl.FaceWorldHuds(camera.transform.position, camera.transform.up);
            }
            foreach (var entry in arcadeEnemies.Values)
                entry.View?.FaceWorldHuds(camera.transform.position, camera.transform.up);
        }

        private void OnCombatHealthChanged(CombatHealthChangedEvent evt)
        {
            if (evt.Faction != CombatFaction.Enemy)
            {
                return;
            }

            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].ViewCtrl.UnitId == evt.UnitId)
                {
                    active[i].ViewCtrl.PlayHitFeedback();
                    active[i].ViewCtrl.SetHealthDisplay(evt.Current, evt.Maximum);
                    return;
                }
            }
        }

        private void OnDamageApplied(CombatDamageAppliedEvent evt)
        {
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].ViewCtrl.UnitId != evt.TargetUnitId) continue;
                active[i].ViewCtrl.ShowDamage(evt.Damage);
                return;
            }
        }

        private void OnCombatantDied(CombatantDiedEvent evt)
        {
            if (!this.GetModel<CombatModel>().TryGetCombatant(evt.UnitId, out var combatant) ||
                combatant.Faction != CombatFaction.Enemy)
            {
                return;
            }

            for (var i = 0; i < active.Count; i++)
            {
                if (active[i].ViewCtrl.UnitId == evt.UnitId && !active[i].ReleasePending)
                {
                    active[i].ViewCtrl.Stop();
                    active[i].ViewCtrl.SetActionProgressVisible(false);
                    active[i].ViewCtrl.MarkDead();
                    active[i].ReleasePending = true;
                    return;
                }
            }
        }

        private sealed class EnemyRuntimeEntry
        {
            public EnemyRuntimeEntry(EnemyViewCtrl viewCtrl, EnemyConfigDefinition config, Collider[] colliders)
            {
                ViewCtrl = viewCtrl;
                Config = config;
                Colliders = colliders ?? Array.Empty<Collider>();
            }

            public EnemyViewCtrl ViewCtrl { get; }
            public EnemyConfigDefinition Config { get; }
            public Collider[] Colliders { get; }
            public bool ReleasePending { get; set; }
            public bool Corpse { get; set; }
            public int? RestoredHealth { get; set; }
        }

        private sealed class ArcadeEnemyPresentationEntry
        {
            public ArcadeEnemyPresentationEntry(int runtimeId, string configId, EnemyView view, int health,
                Vector3 position, int attackSequence, bool persistentHealthBar)
            {
                RuntimeId = runtimeId;
                ConfigId = configId;
                View = view;
                LastHealth = health;
                LastPosition = position;
                LastAttackSequence = attackSequence;
                PersistentHealthBar = persistentHealthBar;
            }

            public int RuntimeId { get; }
            public string ConfigId { get; }
            public EnemyView View { get; }
            public int LastHealth { get; set; }
            public Vector3 LastPosition { get; set; }
            public int LastAttackSequence { get; set; }
            /// <summary>Boss 血条常驻；普通敌人只在受击计时窗口内显示。</summary>
            public bool PersistentHealthBar { get; set; }
            /// <summary>普通敌人血条剩余显示时间，单位秒；0 表示隐藏。</summary>
            public float HealthBarRevealRemaining { get; set; }
            /// <summary>街机规则实体已移除后，死亡表现允许保留到此游戏时间。</summary>
            public float ReleaseTime { get; set; }
        }

        internal static bool ShouldShowArcadeHealthBar(bool persistent, float revealRemainingSeconds) =>
            persistent || revealRemainingSeconds > 0f;

        internal static float AdvanceArcadeHealthBarReveal(float remainingSeconds, float deltaTime) =>
            Mathf.Max(0f, remainingSeconds - Mathf.Max(0f, deltaTime));

        private readonly struct PrefabCacheEntry
        {
            public PrefabCacheEntry(GameObject prefab, ResourceLoadHandle handle)
            {
                Prefab = prefab;
                Handle = handle;
            }

            public GameObject Prefab { get; }
            public ResourceLoadHandle Handle { get; }
        }

        private sealed class LoadingState
        {
            public readonly UniTaskCompletionSource<GameObject> Completion =
                new UniTaskCompletionSource<GameObject>();
            public ResourceLoadHandle Handle;
            public bool HandleReleased;
            public bool CallbackArrived;
            public bool CancelledBeforeCallback;
        }

        private readonly struct PrefabLoadResult
        {
            private PrefabLoadResult(bool success, GameObject prefab, string error)
            {
                Success = success;
                Prefab = prefab;
                Error = error;
            }

            public bool Success { get; }
            public GameObject Prefab { get; }
            public string Error { get; }

            public static PrefabLoadResult Ok(GameObject prefab)
            {
                return new PrefabLoadResult(true, prefab, null);
            }

            public static PrefabLoadResult Fail(string error)
            {
                return new PrefabLoadResult(false, null, error);
            }
        }
    }
}

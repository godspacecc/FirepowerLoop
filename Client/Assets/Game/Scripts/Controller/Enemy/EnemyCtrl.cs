using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 为街机敌人准备与复用表现对象，不创建旧战斗或 AI 权威状态。
    /// 负责候选提交与失败回滚，不决定地图出生配置或重新生成导航数据。
    /// </summary>
    public sealed class EnemyCtrl : IController, IArcadeEnemyPresentationPort
    {
        internal const float ArcadeHealthBarRevealSeconds = 2.5f;
        private readonly IArchitecture architecture;
        private readonly Dictionary<string, PrefabCacheEntry> committedPrefabs = new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, PrefabCacheEntry> temporaryPrefabs = new Dictionary<string, PrefabCacheEntry>();
        private readonly Dictionary<string, LoadingState> loadingPrefabs = new Dictionary<string, LoadingState>();
        private readonly Dictionary<int, ArcadeEnemyPresentationEntry> arcadeEnemies = new Dictionary<int, ArcadeEnemyPresentationEntry>();
        private readonly List<ArcadeEnemyPresentationEntry> dyingArcadeEnemies = new List<ArcadeEnemyPresentationEntry>();
        private readonly Dictionary<string, Stack<EnemyView>> arcadeEnemyPool = new Dictionary<string, Stack<EnemyView>>(StringComparer.Ordinal);
        private Transform arcadeCandidateRoot;
        private long arcadeCandidateMapInstanceId;
        private Transform arcadeRoot;
        private long arcadeMapInstanceId;
        private bool stagedArcade;
        private bool released;
        private bool stagedPrepared;
        /// <summary>手机最多44个、桌面最多88个活动尸体刚体；超额冻结最旧尸体但保留可见姿态。</summary>
        private readonly int corpseBudget = Application.isMobilePlatform ? 4 : 8;
        /// <summary>独立限制可见尸体数；冻结后不再占用物理预算，避免密集击杀时立即消失。</summary>
        private readonly int visibleCorpseBudget = Application.isMobilePlatform ? 12 : 24;
        private GameObject ragdollFloor;
        /// <summary>独立于尸体预算限制短时血雾；手机最多252粒，桌面504粒。</summary>
        private readonly int explosionDeathBudget = Application.isMobilePlatform ? 6 : 12;
        /// <summary>跟随当前地图创建、隐藏清空和释放，资源依赖借用预载敌人Prefab。</summary>
        private EnemyExplosionDeathPresentation explosionDeaths;
        public EnemyCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            LifeCycle.RegisterUpdate(OnUpdate);
            LifeCycle.RegisterLateUpdate(OnLateUpdate);
        }

        public IArchitecture GetArchitecture()
        {
            return architecture;
        }

        public async UniTask<MapControllerResult> PrepareMapAsync(MapPreparationContext context, CancellationToken cancellationToken)
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
                return await PrepareArcadeAsync(context, cancellationToken);
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
            if (!stagedPrepared || !stagedArcade)
                return MapControllerResult.Fail("没有街机候选表现");
            if (arcadeCandidateRoot == null || arcadeCandidateMapInstanceId <= 0)
                return FailPrepared("敌人提交失败：街机候选上下文无效");
            ReleaseArcadePresentation();
            arcadeRoot = arcadeCandidateRoot;
            arcadeMapInstanceId = arcadeCandidateMapInstanceId;
            CreateRagdollFloor();
            arcadeCandidateRoot = null;
            arcadeCandidateMapInstanceId = 0;
            stagedArcade = false;
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
            if (!activeState)
            {
                explosionDeaths?.Clear();
                foreach (var entry in dyingArcadeEnemies) RecycleArcadeEnemy(entry);
                dyingArcadeEnemies.Clear();
            }
            foreach (var entry in arcadeEnemies.Values)
                if (entry.View != null && entry.View.gameObject.activeSelf != activeState)
                    entry.View.gameObject.SetActive(activeState);
        }

        public void ReleaseCurrentMap()
        {
            ReleaseArcadePresentation();
        }

        public void ReleaseAll()
        {
            released = true;
            LifeCycle.UnregisterUpdate(OnUpdate);
            LifeCycle.UnregisterLateUpdate(OnLateUpdate);
            ReleaseStaged();
            ReleaseArcadePresentation();
            ReleaseTemporaryPrefabs();
            ReleaseCommittedPrefabs();
        }

        /// <summary>创建或刷新真实 EnemyView；只更新表现，不注册 Unit、Combat 或 EnemySystem。</summary>
        public bool TrySetEnemy(int runtimeId, string configId, Vector3 position, Vector3 facing, int currentHealth, int maximumHealth, out string error, bool telegraph = false, int attackSequence = 0, bool persistentHealthBar = false)
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
                if (!committedPrefabs.TryGetValue(configId, out var prefab) || prefab.Prefab == null || !ValidatePrefab(prefab.Prefab, out var prefabView, out error))
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
                    if (!this.GetUtility<IRuntimeConfigProvider>().TryGetEnemy(configId, out var weaponConfig, out var weaponError))
                    {
                        ReleaseArcadeView(view);
                        error = weaponError ?? $"街机敌人配置不可用：{configId}";
                        return false;
                    }

                    if (!ValidatePresentationWeaponKind(view, weaponConfig, out var presentationError) || view.GetComponent<CharacterWeaponPresenter>() != null && !view.SetCombatWeapon(weaponConfig.WeaponId))
                    {
                        ReleaseArcadeView(view);
                        error = presentationError ?? $"街机敌人武器表现不可用：{configId}/{weaponConfig.WeaponId}";
                        return false;
                    }

                    view.SetPresentationVariant(runtimeId);
                    view.SetNavigationEnabled(false);
                    view.SetHealthBarVisible(persistentHealthBar);
                    view.gameObject.SetActive(true);
                    current = new ArcadeEnemyPresentationEntry(runtimeId, configId, view, maximumHealth, position, attackSequence, persistentHealthBar);
                    arcadeEnemies.Add(runtimeId, current);
                }
                catch (Exception exception)
                {
                    ReleaseArcadeView(view);
                    error = $"街机敌人表现初始化失败：{configId} / {exception.Message}";
                    return false;
                }
            }

            if (current.PersistentHealthBar != persistentHealthBar)
            {
                current.PersistentHealthBar = persistentHealthBar;
                current.View.SetHealthBarVisible(ShouldShowArcadeHealthBar(current.PersistentHealthBar, current.HealthBarRevealRemaining));
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
            => RemoveEnemy(runtimeId, playDeath, Vector3.zero, false);

        public void RemoveEnemy(int runtimeId, bool playDeath, Vector3 direction, bool explosive)
            => RemoveEnemy(runtimeId,playDeath,direction,explosive,false);

        /// <summary>从已确认的命中状态刷新附着火焰，不依靠画面距离猜测碰撞。</summary>
        public void SetEnemyBurning(int runtimeId,float remainingSeconds)
        {if(arcadeEnemies.TryGetValue(runtimeId,out var entry))entry.View?.SetFlameRemaining(remainingSeconds);}

        /// <summary>只克隆已有资源中的效果组，不创建额外敌人、单位或加载句柄。</summary>
        public bool TryCreateFlameFeedback(Transform parent,out EnemyFlameFeedback feedback)
        {
            feedback=null;if(parent==null)return false;
            foreach(var entry in committedPrefabs.Values)
            {
                var template=entry.Prefab!=null?entry.Prefab.GetComponentInChildren<EnemyFlameFeedback>(true):null;
                if(template==null)continue;
                feedback=UnityEngine.Object.Instantiate(template,parent,false);
                feedback.ResetForReuse();return true;
            }
            return false;
        }

        public void RemoveEnemy(int runtimeId,bool playDeath,Vector3 direction,bool explosive,bool flameKill)
            => RemoveEnemy(runtimeId,playDeath,direction,explosive,flameKill,0);

        /// <summary>力度只影响已裁决死亡的骨骼，不改变尸体与特效预算。</summary>
        public void RemoveEnemy(int runtimeId,bool playDeath,Vector3 direction,bool explosive,bool flameKill,int impactKind)
        {
            if (!arcadeEnemies.TryGetValue(runtimeId, out var entry))
                return;
            arcadeEnemies.Remove(runtimeId);
            if (playDeath && entry.View != null)
            {
                if (explosive && arcadeRoot != null && entry.View.ExplosionDeathTemplate != null)
                {
                    if (explosionDeaths == null)
                        explosionDeaths = new EnemyExplosionDeathPresentation(arcadeRoot, entry.View.ExplosionDeathTemplate, explosionDeathBudget);
                    explosionDeaths.Play(entry.View.transform.position, direction);
                }
                entry.View.PlayFlameDeath(flameKill);
                while (dyingArcadeEnemies.Count >= visibleCorpseBudget)
                {
                    RecycleArcadeEnemy(dyingArcadeEnemies[0]);
                    dyingArcadeEnemies.RemoveAt(0);
                }
                int activePhysics = 0;
                foreach (var dying in dyingArcadeEnemies)
                    if (dying.View != null && dying.View.HasPendingDeathPhysics) activePhysics++;
                for (int i = 0; activePhysics >= corpseBudget && i < dyingArcadeEnemies.Count; i++)
                {
                    var dying = dyingArcadeEnemies[i].View;
                    if (dying == null || !dying.HasPendingDeathPhysics) continue;
                    dying.StopDeathSimulation();
                    activePhysics--;
                }
                if (entry.View.TryPlayPhysicalDeathWithImpact(direction, explosive, impactKind, out var durationSeconds) ||
                    entry.View.TryPlayDeathAnimation(out durationSeconds))
                {
                    entry.View.SetHealthBarVisible(false);
                    entry.View.SetTelegraphVisible(false);
                    entry.ReleaseTime = Time.time + durationSeconds;
                    dyingArcadeEnemies.Add(entry);
                    return;
                }
            }

            RecycleArcadeEnemy(entry);
        }

        public void ClearArcadeEnemies()
        {
            explosionDeaths?.Clear();
            var values = new List<ArcadeEnemyPresentationEntry>(arcadeEnemies.Values);
            arcadeEnemies.Clear();
            for (var i = 0; i < values.Count; i++)
                RecycleArcadeEnemy(values[i]);
            for (var i = 0; i < dyingArcadeEnemies.Count; i++)
                RecycleArcadeEnemy(dyingArcadeEnemies[i]);
            dyingArcadeEnemies.Clear();
        }

        /// <summary>街机地图候选阶段只预载会出现在波次中的敌人 Prefab，不创建正式战斗实体。</summary>
        private async UniTask<MapControllerResult> PrepareArcadeAsync(MapPreparationContext context, CancellationToken cancellationToken)
        {
            EnemyPhysicalReaction.ConfigureCollisionLayers();
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            string catalogError = null;
            if (provider == null || !provider.TryGetArcadeCatalog(out var catalog, out catalogError))
                return FailPrepared("敌人准备失败：" + (catalogError ?? "街机配置不可用"));
            var enemyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in catalog.EnemyGroups)
                foreach (var member in group.Members)
                    if (!string.IsNullOrWhiteSpace(member.EnemyId))
                        enemyIds.Add(member.EnemyId);
            foreach (var enemyId in enemyIds)
            {
                if (!provider.TryGetEnemy(enemyId, out var config, out var configError))
                    return FailPrepared("敌人准备失败：" + configError);
                var prefabResult = await LoadEnemyPrefabAsync(config, cancellationToken);
                if (!prefabResult.Success)
                    return FailPrepared(prefabResult.Error);
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
            if (!arcadeEnemyPool.TryGetValue(configId, out var pool))
                return null;
            while (pool.Count > 0)
            {
                var view = pool.Pop();
                if (view != null)
                    return view;
            }

            return null;
        }

        private void RecycleArcadeEnemy(ArcadeEnemyPresentationEntry entry)
        {
            if (entry?.View == null)
                return;
            entry.View.SetNavigationEnabled(false);
            entry.View.SetTelegraphVisible(false);
            entry.View.ResetPhysicalFeedback();
            entry.View.gameObject.SetActive(false);
            entry.View.ResetWeaponSelection();
            if (!arcadeEnemyPool.TryGetValue(entry.ConfigId, out var pool))
            {
                pool = new Stack<EnemyView>();
                arcadeEnemyPool.Add(entry.ConfigId, pool);
            }

            pool.Push(entry.View);
        }

        /// <summary>地图持有的纯表现地面，只与尸体层接触，不改变战斗几何或导航。</summary>
        private void CreateRagdollFloor()
        {
            int layer = LayerMask.NameToLayer(EnemyPhysicalReaction.FloorLayerName);
            if (arcadeRoot == null || layer < 0) return;
            ragdollFloor = new GameObject("EnemyRagdollFloor");
            ragdollFloor.layer = layer;
            ragdollFloor.transform.SetParent(arcadeRoot, false);
            var collider = ragdollFloor.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, -.05f, 0);
            collider.size = new Vector3(200, .1f, 200);
        }

        private void ReleaseArcadePresentation()
        {
            explosionDeaths?.Dispose();
            explosionDeaths = null;
            if (ragdollFloor != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(ragdollFloor);
                else UnityEngine.Object.DestroyImmediate(ragdollFloor);
                ragdollFloor = null;
            }
            foreach (var entry in arcadeEnemies.Values)
                ReleaseArcadeView(entry.View);
            arcadeEnemies.Clear();
            for (var i = 0; i < dyingArcadeEnemies.Count; i++)
                ReleaseArcadeView(dyingArcadeEnemies[i].View);
            dyingArcadeEnemies.Clear();
            foreach (var pool in arcadeEnemyPool.Values)
                while (pool.Count > 0)
                {
                    var view = pool.Pop();
                    ReleaseArcadeView(view);
                }

            arcadeEnemyPool.Clear();
            arcadeRoot = null;
            arcadeMapInstanceId = 0;
        }

        /// <summary>先释放表现实例持有的临时材质，再交给既有实例提供器销毁；支持重复释放。</summary>
        private void ReleaseArcadeView(EnemyView view)
        {
            if (view == null) return;
            view.ReleasePresentationResources();
            this.GetUtility<IViewInstanceProvider>().Release(view.gameObject);
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
        internal static bool ValidatePresentationWeaponKind(EnemyView view, EnemyConfigDefinition config, out string error)
        {
            error = null;
            if (view == null)
            {
                error = $"敌人 {config.Id} / 武器 {config.WeaponId}：候选实例缺失";
                return false;
            }

            var presenter = view.GetComponent<CharacterWeaponPresenter>();
            if (presenter == null)
                return true;
            var expected = config.AttackKind == global::EnemyAttackKind.Melee ? "近战" : config.AttackKind == global::EnemyAttackKind.Projectile ? "投射" : $"不支持的玩法类别 {config.AttackKind}";
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
            if ((config.AttackKind != global::EnemyAttackKind.Melee && config.AttackKind != global::EnemyAttackKind.Projectile) || entry.IsProjectile != (config.AttackKind == global::EnemyAttackKind.Projectile))
            {
                error = prefix + "，实际目录类别 " + actual;
                return false;
            }

            return true;
        }

        private async UniTask<PrefabLoadResult> LoadEnemyPrefabAsync(EnemyConfigDefinition config, CancellationToken cancellationToken)
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
        private static EnemyView InstantiateInactive(IViewInstanceProvider instances, EnemyView prefab, Transform parent, Vector3 position, Quaternion rotation)
        {
            var staging = new GameObject("EnemyPresentationStaging");
            staging.SetActive(false);
            try
            {
                var view = instances?.Instantiate(prefab, staging.transform, position, rotation);
                if (view == null)
                    return null;
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
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(staging);
                else
                    UnityEngine.Object.DestroyImmediate(staging);
            }
        }

        private static bool ValidatePrefab(GameObject prefab, out EnemyView view, out string error)
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

        private void ReleaseStaged()
        {
            stagedPrepared = false;
        }

        private void OnUpdate()
        {
            explosionDeaths?.Tick(Time.deltaTime);
            for (var i = dyingArcadeEnemies.Count - 1; i >= 0; i--)
            {
                var entry = dyingArcadeEnemies[i];
                entry.View?.TickCombatFeedback(Time.deltaTime);
                entry.View?.SetCorpseFade(entry.ReleaseTime - Time.time);
                if (entry.View != null && Time.time < entry.ReleaseTime)
                    continue;
                RecycleArcadeEnemy(entry);
                dyingArcadeEnemies.RemoveAt(i);
            }

            foreach (var entry in arcadeEnemies.Values)
            {
                entry.View?.TickCombatFeedback(Time.deltaTime);
                if (entry.View == null || entry.PersistentHealthBar)
                    continue;
                var before = entry.HealthBarRevealRemaining;
                entry.HealthBarRevealRemaining = AdvanceArcadeHealthBarReveal(before, Time.deltaTime);
                if (before > 0f && entry.HealthBarRevealRemaining <= 0f)
                    entry.View.SetHealthBarVisible(false);
            }
        }

        private void OnLateUpdate()
        {
            foreach (var entry in arcadeEnemies.Values) entry.View?.LateTickCombatFeedback();
            // 死亡失衡阶段仍需在Animator后交接骨骼，即使此帧没有主相机也要完成。
            foreach (var entry in dyingArcadeEnemies) entry.View?.LateTickCombatFeedback();
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            foreach (var entry in arcadeEnemies.Values)
                entry.View?.FaceWorldHuds(camera.transform.position, camera.transform.up);
        }

        private sealed class ArcadeEnemyPresentationEntry
        {
            public ArcadeEnemyPresentationEntry(int runtimeId, string configId, EnemyView view, int health, Vector3 position, int attackSequence, bool persistentHealthBar)
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

        internal static bool ShouldShowArcadeHealthBar(bool persistent, float revealRemainingSeconds) => persistent || revealRemainingSeconds > 0f;
        internal static float AdvanceArcadeHealthBarReveal(float remainingSeconds, float deltaTime) => Mathf.Max(0f, remainingSeconds - Mathf.Max(0f, deltaTime));
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
            public readonly UniTaskCompletionSource<GameObject> Completion = new UniTaskCompletionSource<GameObject>();
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

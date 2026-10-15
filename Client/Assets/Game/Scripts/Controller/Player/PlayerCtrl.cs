using System;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game
{
    public sealed class PlayerCtrl : IController, IPlayerRuntimePortProvider
    {
        private readonly IArchitecture architecture;
        private PlayerViewCtrl stagedPlayer;
        private PlayerViewCtrl activePlayer;
        private IPlayerCameraPort stagedCamera;
        private IPlayerCameraPort activeCamera;
        private MapPreparationMode stagedMode = MapPreparationMode.OfflineFull;
        private MapPreparationMode activeMode = MapPreparationMode.OfflineFull;
        private string cachedPrefabConfigId;
        private GameObject cachedPrefab;
        private ResourceLoadHandle cachedPrefabHandle;
        private bool cachedPrefabHandleReleased = true;
        private bool prefabLoadInProgress;
        private bool released;
        private bool stagedSwimming;
        private Camera cachedMainCamera;
        private IUnRegister weaponEquipmentChangedUnregister;
        private IUnRegister clothingChangedUnregister;
        private IUnRegister testAppearanceChangedUnregister;
        private IUnRegister playerActionChangedUnregister;
        private IUnRegister damageAppliedUnregister;
        private IUnRegister onlineSelfDamagedUnregister;
        private readonly WeaponAppearanceSlotState meleeAppearance = new WeaponAppearanceSlotState(WeaponSlot.Melee);
        private readonly WeaponAppearanceSlotState rangedAppearance = new WeaponAppearanceSlotState(WeaponSlot.Ranged);
        private long onlineLifecycleRevision;
        private long onlineUnitId;
        private int onlineHealth=-1;
        /// <summary>当前工具采集动作的表现身份与挥动节拍；只驱动动画，不产生战斗伤害或采集奖励。</summary>
        private long chopPresentationActionId;
        private int chopPresentationBeat = -1;

        public PlayerCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            clothingChangedUnregister = architecture.RegisterEvent<ClothingEquipmentChangedEvent>(_ => RefreshClothing());
            testAppearanceChangedUnregister = architecture.RegisterEvent<PlayerTestAppearanceChangedEvent>(_ => RefreshClothing());
            weaponEquipmentChangedUnregister =
                architecture.RegisterEvent<WeaponEquipmentChangedEvent>(_ =>
                {
                    if (activeMode != MapPreparationMode.ArcadePlayerOnly) RefreshWeaponAppearances();
                });
            playerActionChangedUnregister =
                architecture.RegisterEvent<PlayerActionChangedEvent>(OnPlayerActionChanged);
            damageAppliedUnregister = architecture.RegisterEvent<CombatDamageAppliedEvent>(OnDamageApplied);
            onlineSelfDamagedUnregister = architecture.RegisterEvent<OnlineWorldSelfDamagedEvent>(evt => activePlayer?.ShowDamage(evt.Damage));
        }

        public IArchitecture GetArchitecture()
        {
            return architecture;
        }

        public void CaptureExpeditionWorld(ExpeditionWorldSaveData world)
        {
            if (world == null || activePlayer == null || !activePlayer.IsValid) return;
            world.Players.Clear();
            world.Players.Add(new ExpeditionPlayerSaveData
            {
                RuntimeId = activePlayer.UnitId,
                ConfigId = "player",
                IsSwimming = this.GetSystem<PlayerMoveSystem>()?.IsSwimming == true,
                Pose = new ExpeditionPoseData { X = activePlayer.Position.x, Y = activePlayer.Position.y, Z = activePlayer.Position.z, RotationY = activePlayer.Rotation.eulerAngles.y }
            });
        }

        public async UniTask<MapControllerResult> PrepareMapAsync(MapPreparationContext context, CancellationToken cancellationToken)
        {
            if (stagedPlayer != null)
            {
                return MapControllerResult.Fail("玩家准备失败：已有候选玩家");
            }

            if (context?.Scene == null)
            {
                return MapControllerResult.Fail("玩家准备失败：缺少 MapContext");
            }

            var playerSpawns = context.Spawns.Where(item => item.ObjectType == MapSpawnObjectType.Player).ToList();
            if (context.Mode != MapPreparationMode.OnlinePlayersOnly && playerSpawns.Count != 1)
            {
                return MapControllerResult.Fail("玩家准备失败：地图必须恰好有一个 Player Spawn");
            }

            OnlineWorldPlayerSnapshot onlineSelf = default;
            if (context.Mode == MapPreparationMode.OnlinePlayersOnly)
            {
                onlineSelf = context.OnlineWorld.Players.FirstOrDefault(x => x.UnitId == context.OnlineWorld.SelfUnitId);
                if (onlineSelf.UnitId == 0) return MapControllerResult.Fail("玩家准备失败：World 快照缺少本地玩家");
            }
            var spawn = context.Mode != MapPreparationMode.OnlinePlayersOnly ? playerSpawns[0] : default;
            string playerConfigId = context.Mode == MapPreparationMode.OnlinePlayersOnly ? onlineSelf.PlayerConfigId : spawn.ConfigId;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetPlayer(playerConfigId, out var playerConfig, out var configError))
            {
                return MapControllerResult.Fail("玩家准备失败：" + configError);
            }

            var camera = GetMainCamera();
            if (camera == null)
            {
                return MapControllerResult.Fail("玩家准备失败：缺少 Main Camera");
            }

            var prefabResult = await LoadPlayerPrefabAsync(playerConfig, cancellationToken);
            if (!prefabResult.Success)
            {
                return prefabResult;
            }

            if (cancellationToken.IsCancellationRequested || released)
            {
                return MapControllerResult.Fail("玩家准备失败：请求已取消");
            }

            var prefabView = cachedPrefab != null ? cachedPrefab.GetComponent<PlayerView>() : null;
            if (prefabView == null)
            {
                return MapControllerResult.Fail("玩家准备失败：Prefab 根缺少 PlayerView");
            }

            var localPosition = context.Mode == MapPreparationMode.OnlinePlayersOnly ? onlineSelf.LocalPosition : spawn.LocalPosition;
            stagedSwimming=context.ExpeditionSnapshot?.World?.Players?.FirstOrDefault()?.IsSwimming == true;
            var localRotation = context.Mode == MapPreparationMode.OnlinePlayersOnly ? Quaternion.Euler(0f,onlineSelf.YawDegrees,0f) : spawn.LocalRotation;
            if (context.ExpeditionSnapshot?.World?.Players != null && context.ExpeditionSnapshot.World.Players.Count > 0)
            {
                var saved = context.ExpeditionSnapshot.World.Players[0];
                if (saved?.Pose == null) return MapControllerResult.Fail("玩家恢复失败：快照实体无效");
                var savedWorldPosition = new Vector3(saved.Pose.X, saved.Pose.Y, saved.Pose.Z);
                var savedWorldRotation = Quaternion.Euler(0f, saved.Pose.RotationY, 0f);
                localPosition = context.Scene.DynamicObjectRoot.InverseTransformPoint(savedWorldPosition);
                localRotation = Quaternion.Inverse(context.Scene.DynamicObjectRoot.rotation) * savedWorldRotation;
            }
            if (string.Equals(context.MapId, MapIds.Base, StringComparison.Ordinal) &&
                this.GetModel<PlayerBasePoseModel>().TryGet(out var savedPose))
            {
                localPosition = savedPose.LocalPosition;
                localRotation = Quaternion.Euler(0f, savedPose.LocalYawDegrees, 0f);
            }

            var worldPosition = context.Scene.DynamicObjectRoot.TransformPoint(localPosition);
            var worldRotation = context.Scene.DynamicObjectRoot.rotation * localRotation;
            var instanceObject = Object.Instantiate(cachedPrefab, worldPosition, worldRotation, context.Scene.DynamicObjectRoot);
            if (instanceObject == null)
            {
                return MapControllerResult.Fail("玩家准备失败：Prefab 实例化返回空");
            }

            var instance = instanceObject.GetComponent<PlayerView>();
            if (instance == null)
            {
                this.GetUtility<IViewInstanceProvider>().Release(instanceObject);
                return MapControllerResult.Fail("玩家准备失败：Prefab 根缺少 PlayerView");
            }

            if (!instance.Validate(out var viewError))
            {
                this.GetUtility<IViewInstanceProvider>().Release(instanceObject);
                return MapControllerResult.Fail("玩家准备失败：" + viewError);
            }

            instanceObject.SetActive(false);
            var playerId = this.GetModel<PlayerModel>().PlayerUnitId;
            stagedPlayer = new PlayerViewCtrl(
                instance,
                playerId,
                context.MapInstanceId,
                this.GetUtility<ViewCtrlRegistry>(),
                this.GetUtility<IViewInstanceProvider>());
            stagedCamera = new PlayerCameraCtrl(camera, instance.transform, instance.CameraProfile, instance.ShoulderCameraProfile);
            stagedMode = context.Mode;
            if (!stagedCamera.IsValid)
            {
                ReleaseStaged();
                return MapControllerResult.Fail("玩家准备失败：相机端口无效");
            }

            return MapControllerResult.Ok();
        }

        public MapControllerResult CommitPreparedMap()
        {
            if (activePlayer != null && !activePlayer.IsDisposed)
            {
                return MapControllerResult.Fail("玩家提交失败：已有活动玩家");
            }

            if (stagedPlayer == null || !stagedPlayer.IsValid || stagedCamera == null || !stagedCamera.IsValid)
            {
                return MapControllerResult.Fail("玩家提交失败：候选玩家无效");
            }

            var snapshot = new UnitSnapshot(
                stagedPlayer.UnitId,
                stagedPlayer.MapInstanceId,
                UnitType.Player,
                UnitSource.Player,
                cachedPrefabConfigId,
                stagedPlayer.Position,
                stagedPlayer.Rotation,
                UnitAuthorityMode.Local,
                0,
                true);

            if (!this.GetSystem<UnitSystem>().TryRegister(snapshot, out var error))
            {
                ReleaseStaged();
                return MapControllerResult.Fail(error);
            }

            stagedPlayer.SetVisible(true);
            activePlayer = stagedPlayer;
            activeCamera = stagedCamera;
            activeMode = stagedMode;
            stagedPlayer = null;
            stagedCamera = null;
            stagedMode = MapPreparationMode.OfflineFull;
            PublishReady(snapshot.UnitId, snapshot.MapInstanceId);
            this.GetSystem<PlayerMoveSystem>()?.RestoreSwimming(stagedSwimming);
            if (activeMode == MapPreparationMode.ArcadePlayerOnly) ReleaseAllWeaponAppearances();
            else RefreshWeaponAppearances();
            RefreshClothing();
            return MapControllerResult.Ok();
        }

        public void RollbackPreparedMap()
        {
            ReleaseStaged();
        }

        public void SetCurrentMapActive(bool active)
        {
            if (activePlayer == null)
            {
                return;
            }

            activePlayer.SetVisible(active);
            this.GetSystem<UnitSystem>().TrySetActive(activePlayer.UnitId, active);
            if (active)
            {
                PublishReady(activePlayer.UnitId, activePlayer.MapInstanceId);
                if (activeMode != MapPreparationMode.ArcadePlayerOnly) RefreshWeaponAppearances();
            }
        }

        public void ReleaseCurrentMap()
        {
            ReleaseAllWeaponAppearances();
            // 先释放地图镜头架并恢复玩家标签，再销毁玩家资源。
            (activeCamera as IDisposable)?.Dispose();
            if (activePlayer != null)
            {
                this.GetSystem<UnitSystem>().Remove(activePlayer.UnitId, UnitRemovedReason.MapInstanceReleased);
                activePlayer.Dispose();
            }

            activePlayer = null;
            activeCamera = null;
            activeMode = MapPreparationMode.OfflineFull;
            onlineLifecycleRevision=0;onlineUnitId=0;onlineHealth=-1;
        }

        public void ApplyOnlineSnapshot(OnlineWorldSnapshot world)
        {
            if(activePlayer==null||world==null||!world.HasWorld||this.GetModel<MapSceneModel>().CurrentMode!=MapPreparationMode.OnlinePlayersOnly)return;
            OnlineWorldPlayerSnapshot self=world.Players.FirstOrDefault(x=>x.UnitId==world.SelfUnitId);
            if(self.UnitId==0)
            {
                this.GetSystem<PlayerActionSystem>()?.CancelActive(PlayerActionCancelReason.PlayerUnavailable);
                activePlayer.SetAlivePresentation(false);
                activePlayer.SetVisible(false);
                onlineUnitId=0;onlineHealth=-1;
                return;
            }
            var clothingConfig=this.GetUtility<IRuntimeConfigProvider>();
            activePlayer.ApplyClothingItems(clothingConfig,world.SelfInventory.ClothingItems());
            bool identityChanged=onlineUnitId!=self.UnitId;
            if(identityChanged)this.GetSystem<PlayerActionSystem>()?.CancelActive(PlayerActionCancelReason.PlayerUnavailable);
            activePlayer.SetVisible(true);
            activePlayer.SetAlivePresentation(!self.IsDead);
            if(onlineHealth>=0&&self.CurrentHP<onlineHealth)activePlayer.PlayHurtFeedback();
            bool respawn=onlineLifecycleRevision>0&&self.LifecycleRevision>onlineLifecycleRevision&&!self.IsDead;
            if(identityChanged||onlineLifecycleRevision==0||respawn){activePlayer.SnapLocal(self.LocalPosition,self.YawDegrees);this.GetSystem<UnitSystem>().TryUpdateTransform(activePlayer.UnitId,activePlayer.Position,activePlayer.Rotation);}
            onlineUnitId=self.UnitId;onlineLifecycleRevision=self.LifecycleRevision;onlineHealth=self.CurrentHP;
        }

        public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
        {
            if (activePlayer != null &&
                activePlayer.UnitId == unitId &&
                activePlayer.MapInstanceId == mapInstanceId &&
                activePlayer.IsValid)
            {
                port = activePlayer;
                return true;
            }

            port = null;
            return false;
        }

        public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
        {
            if (activeCamera != null &&
                activePlayer != null &&
                activePlayer.UnitId == unitId &&
                activePlayer.MapInstanceId == mapInstanceId &&
                activeCamera.IsValid)
            {
                port = activeCamera;
                return true;
            }

            port = null;
            return false;
        }

        public bool TryGetFlashlightPort(int unitId, long mapInstanceId, out IPlayerFlashlightPort port)
        {
            if (activePlayer != null &&
                activePlayer.UnitId == unitId &&
                activePlayer.MapInstanceId == mapInstanceId &&
                activePlayer.IsValid)
            {
                port = activePlayer;
                return true;
            }

            port = null;
            return false;
        }

        public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
        {
            return activePlayer != null &&
                   activePlayer.UnitId == unitId &&
                   activePlayer.MapInstanceId == mapInstanceId &&
                   activePlayer.IsValid &&
                   activePlayer.IsPlayerCollider(collider);
        }

        public bool TryGetCurrentPlayerPose(out PlayerPoseSnapshot snapshot, out string error)
        {
            snapshot = default;
            if (activePlayer == null || !activePlayer.IsValid)
            {
                error = "当前玩家 不可用";
                return false;
            }

            if (activePlayer.MapInstanceId <= 0)
            {
                error = "当前玩家 地图实例 无效";
                return false;
            }

            var active = this.GetSystem<UnitSystem>().TryGet(activePlayer.UnitId, out var unit) &&
                         unit.MapInstanceId == activePlayer.MapInstanceId &&
                         unit.IsActive;
            if (!active)
            {
                error = "当前玩家 未激活";
                return false;
            }

            snapshot = new PlayerPoseSnapshot(
                activePlayer.UnitId,
                activePlayer.MapInstanceId,
                activePlayer.Position,
                activePlayer.Rotation,
                true);
            error = null;
            return true;
        }

        public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
        {
            snapshot = default;
            if (activePlayer == null || !activePlayer.IsValid)
            {
                error = "当前玩家 不可用";
                return false;
            }

            if (activePlayer.MapInstanceId <= 0)
            {
                error = "当前玩家 地图实例 无效";
                return false;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(activePlayer.UnitId, out var unit) ||
                unit.MapInstanceId != activePlayer.MapInstanceId || !unit.IsActive)
            {
                error = "当前玩家 未激活";
                return false;
            }

            snapshot = new PlayerMapLocalPoseSnapshot(activePlayer.UnitId, activePlayer.MapInstanceId,
                activePlayer.LocalPosition, activePlayer.LocalRotation.eulerAngles.y, true);
            error = null;
            return true;
        }

        public void ReleaseAll()
        {
            released = true;
            weaponEquipmentChangedUnregister?.UnRegister();
            weaponEquipmentChangedUnregister = null;
            clothingChangedUnregister?.UnRegister();
            clothingChangedUnregister = null;
            testAppearanceChangedUnregister?.UnRegister();
            testAppearanceChangedUnregister = null;
            playerActionChangedUnregister?.UnRegister();
            playerActionChangedUnregister = null;
            damageAppliedUnregister?.UnRegister();
            damageAppliedUnregister = null;
            onlineSelfDamagedUnregister?.UnRegister();
            onlineSelfDamagedUnregister = null;
            ReleaseStaged();
            ReleaseCurrentMap();
            ReleaseCachedPrefab();
        }

        private void OnDamageApplied(CombatDamageAppliedEvent evt)
        {
            if (activePlayer != null && evt.TargetUnitId == activePlayer.UnitId)
                activePlayer.ShowDamage(evt.Damage);
        }

        private async UniTask<MapControllerResult> LoadPlayerPrefabAsync(PlayerConfigDefinition playerConfig, CancellationToken cancellationToken)
        {
            if (cachedPrefab != null && cachedPrefabHandle.IsValid && cachedPrefabConfigId == playerConfig.Id)
            {
                return MapControllerResult.Ok();
            }

            if (prefabLoadInProgress)
            {
                return MapControllerResult.Fail("玩家准备失败：玩家 Prefab 正在加载");
            }

            ReleaseCachedPrefab();
            prefabLoadInProgress = true;
            var completion = new UniTaskCompletionSource<GameObject>();
            var lateReleaseNeeded = false;
            var returnedHandleReleasedForCancellation = false;
            var waitCancelledBeforeCallback = false;
            var loadCallbackArrived = false;
            ResourceLoadHandle returnedHandle = ResourceLoadHandle.Empty;
            returnedHandle = ResourceLoader.LoadAssetAsync<GameObject>(playerConfig.PrefabAddress, asset =>
            {
                loadCallbackArrived = true;
                if (asset != null && (released || cancellationToken.IsCancellationRequested || waitCancelledBeforeCallback))
                {
                    if (returnedHandle.IsValid && !returnedHandleReleasedForCancellation)
                    {
                        ResourceLoader.Release(returnedHandle);
                        returnedHandleReleasedForCancellation = true;
                    }
                    else
                    {
                        lateReleaseNeeded = true;
                    }
                }

                completion.TrySetResult(asset);
                if (waitCancelledBeforeCallback)
                {
                    prefabLoadInProgress = false;
                }
            });

            if (lateReleaseNeeded && returnedHandle.IsValid && !returnedHandleReleasedForCancellation)
            {
                ResourceLoader.Release(returnedHandle);
                returnedHandleReleasedForCancellation = true;
            }

            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                if (!loadCallbackArrived)
                {
                    waitCancelledBeforeCallback = true;
                }

                completion.TrySetCanceled(cancellationToken);
            });

            try
            {
                GameObject prefab;
                try
                {
                    prefab = await completion.Task;
                }
                catch (OperationCanceledException)
                {
                    waitCancelledBeforeCallback = true;
                    return MapControllerResult.Fail("玩家准备失败：请求已取消");
                }

                if (prefab == null)
                {
                    return MapControllerResult.Fail("玩家准备失败：玩家 Prefab 加载失败");
                }

                if (released || cancellationToken.IsCancellationRequested)
                {
                    if (returnedHandle.IsValid && !returnedHandleReleasedForCancellation)
                    {
                        ResourceLoader.Release(returnedHandle);
                        returnedHandleReleasedForCancellation = true;
                    }

                    return MapControllerResult.Fail("玩家准备失败：请求已取消");
                }

                cachedPrefab = prefab;
                cachedPrefabConfigId = playerConfig.Id;
                cachedPrefabHandle = returnedHandle;
                cachedPrefabHandleReleased = !returnedHandle.IsValid;
                return MapControllerResult.Ok();
            }
            finally
            {
                if (!waitCancelledBeforeCallback || loadCallbackArrived)
                {
                    prefabLoadInProgress = false;
                }
            }
        }

        private Camera GetMainCamera()
        {
            if (cachedMainCamera == null)
            {
                cachedMainCamera = Camera.main;
            }

            return cachedMainCamera;
        }

        private void ReleaseStaged()
        {
            (stagedCamera as IDisposable)?.Dispose();
            if (stagedPlayer != null)
            {
                stagedPlayer.Dispose();
            }

            stagedPlayer = null;
            stagedCamera = null;
            stagedMode = MapPreparationMode.OfflineFull;
        }

        private void ReleaseCachedPrefab()
        {
            cachedPrefab = null;
            cachedPrefabConfigId = null;
            if (cachedPrefabHandle.IsValid && !cachedPrefabHandleReleased)
            {
                ResourceLoader.Release(cachedPrefabHandle);
            }

            cachedPrefabHandle = ResourceLoadHandle.Empty;
            cachedPrefabHandleReleased = true;
        }

        /// <summary>离线出生、切图与衣物变化时应用完整快照；联机由服务器快照入口处理。</summary>
        private void RefreshClothing()
        {
            if(activePlayer==null || activeMode!=MapPreparationMode.OfflineFull)return;
            var model=this.GetModel<ClothingEquipmentModel>();var config=this.GetUtility<IRuntimeConfigProvider>();
            var items=new int[ClothingEquipmentModel.SlotCount];
            for(int i=0;i<items.Length;i++)items[i]=model.GetItemId((global::ClothingSlot)(i+1));
            activePlayer.ApplyClothingItems(config,items);
            if(!activePlayer.ApplyTestAppearance(this.GetModel<TestCmdModel>().PlayerAppearanceIndex))
                Log.Error("[PlayerCtrl] 测试造型绑定缺失，保留原人物表现。");
        }

        private void RefreshWeaponAppearances()
        {
            if (released || activeMode == MapPreparationMode.ArcadePlayerOnly || activePlayer == null || !activePlayer.IsValid)
            {
                ReleaseAllWeaponAppearances();
                return;
            }

            var equipment = this.GetModel<WeaponEquipmentModel>();
            var activeItemId = equipment.HeldItemId;
            if (activePlayer.HasSharedWeaponPresenter)
            {
                // 已迁移角色即使绑定失败也释放旧请求，不能让过期外观遮住配置错误。
                var bound = activePlayer.TrySetSharedWeaponPresentation(activeItemId);
                ReleaseAllWeaponAppearances();
                if (!bound)
                    Log.Error($"[PlayerCtrl] 共享武器表现失败：{activePlayer.SharedWeaponError}");
                return;
            }
            RefreshWeaponAppearanceSlot(meleeAppearance, equipment.MeleeWeaponItemId, equipment.ActiveSlot == WeaponSlot.Melee);
            RefreshWeaponAppearanceSlot(rangedAppearance, equipment.RangedWeaponItemId, equipment.ActiveSlot == WeaponSlot.Ranged);
        }

        private void RefreshWeaponAppearanceSlot(WeaponAppearanceSlotState state, int itemId, bool visible)
        {
            if (itemId <= 0)
            {
                ReleaseWeaponAppearanceSlot(state);
                return;
            }

            if (state.Instance != null && state.ItemId == itemId)
            {
                state.Instance.SetActive(visible);
                return;
            }

            if (state.Loading && state.ItemId == itemId)
            {
                return;
            }

            ReleaseWeaponAppearanceSlot(state);
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetPlayerWeaponAppearance(itemId, out var appearance, out var error))
            {
                Log.Error($"[PlayerCtrl] 玩家武器外观配置缺失：{error}");
                return;
            }

            if (!activePlayer.TryGetWeaponAttachRoot(appearance.AttachPoint, out var root))
            {
                Log.Error($"[PlayerCtrl] 玩家武器外观挂点缺失：{appearance.AttachPoint}");
                return;
            }

            LoadWeaponAppearanceAsync(state, itemId, activePlayer.MapInstanceId, appearance, root).Forget();
        }

        private async UniTaskVoid LoadWeaponAppearanceAsync(
            WeaponAppearanceSlotState state,
            int itemId,
            long mapInstanceId,
            PlayerWeaponAppearanceDefinition appearance,
            Transform root)
        {
            var version = ++state.Version;
            state.ItemId = itemId;
            state.Loading = true;
            var completion = new UniTaskCompletionSource<GameObject>();
            ResourceLoadHandle handle = ResourceLoadHandle.Empty;
            handle = ResourceLoader.LoadAssetAsync<GameObject>(
                appearance.PrefabAddress,
                prefab => completion.TrySetResult(prefab));

            GameObject prefab = null;
            try
            {
                prefab = await completion.Task;
            }
            catch (Exception e)
            {
                Log.Error($"[PlayerCtrl] 玩家武器外观加载异常：TbWeaponAppearance[{itemId}].PrefabAddress={appearance.PrefabAddress}，{e.Message}");
            }

            var stale = released ||
                        activePlayer == null ||
                        !activePlayer.IsValid ||
                        activePlayer.MapInstanceId != mapInstanceId ||
                        state.ItemId != itemId ||
                        state.Version != version;
            if (stale)
            {
                if (handle.IsValid)
                {
                    ResourceLoader.Release(handle);
                }

                return;
            }

            state.Loading = false;
            if (prefab == null || prefab.transform == null)
            {
                Log.Error($"[PlayerCtrl] 玩家武器外观加载失败：TbWeaponAppearance[{itemId}].PrefabAddress={appearance.PrefabAddress}");
                if (handle.IsValid)
                {
                    ResourceLoader.Release(handle);
                }

                state.ItemId = 0;
                return;
            }

            if (!activePlayer.TryGetWeaponAttachRoot(appearance.AttachPoint, out var currentRoot) ||
                currentRoot == null ||
                !ReferenceEquals(currentRoot, root))
            {
                if (handle.IsValid)
                {
                    ResourceLoader.Release(handle);
                }

                state.ItemId = 0;
                return;
            }

            var instanceTransform = this.GetUtility<IViewInstanceProvider>().Instantiate(
                prefab.transform,
                root,
                root.position,
                root.rotation);
            if (instanceTransform == null)
            {
                Log.Error($"[PlayerCtrl] 玩家武器外观实例化失败：TbWeaponAppearance[{itemId}].PrefabAddress={appearance.PrefabAddress}");
                if (handle.IsValid)
                {
                    ResourceLoader.Release(handle);
                }

                state.ItemId = 0;
                return;
            }

            PlayerView.ApplyWeaponAppearancePose(instanceTransform, appearance);
            var currentEquipment = this.GetModel<WeaponEquipmentModel>();
            var visibleNow = currentEquipment.ActiveSlot == state.Slot;
            instanceTransform.gameObject.SetActive(visibleNow);
            state.Instance = instanceTransform.gameObject;
            state.Handle = handle;
        }

        private void ReleaseAllWeaponAppearances()
        {
            ReleaseWeaponAppearanceSlot(meleeAppearance);
            ReleaseWeaponAppearanceSlot(rangedAppearance);
        }

        private void ReleaseWeaponAppearanceSlot(WeaponAppearanceSlotState state)
        {
            state.Version++;
            state.ItemId = 0;
            state.Loading = false;
            if (state.Instance != null)
            {
                this.GetUtility<IViewInstanceProvider>().Release(state.Instance);
            }

            state.Instance = null;
            if (state.Handle.IsValid)
            {
                ResourceLoader.Release(state.Handle);
            }

            state.Handle = ResourceLoadHandle.Empty;
        }

        private void PublishPlayerReadyAndHealth(int unitId, long mapInstanceId)
        {
            architecture.SendEvent(new PlayerReadyEvent(unitId, mapInstanceId));
            if (this.GetModel<CombatModel>().TryGetCombatant(unitId, out var combatant))
            {
                architecture.SendEvent(new CombatHealthChangedEvent(
                    unitId,
                    CombatFaction.Player,
                    combatant.HP,
                    combatant.MaxHP));
            }
        }

        /// <summary>街机玩家只发布专用就绪事件，避免正式战斗、装备与生存系统接管本局状态。</summary>
        private void PublishReady(int unitId, long mapInstanceId)
        {
            if (activeMode == MapPreparationMode.ArcadePlayerOnly)
            {
                architecture.SendEvent(new ArcadePlayerReadyEvent(unitId, mapInstanceId));
                return;
            }

            PublishPlayerReadyAndHealth(unitId, mapInstanceId);
        }

        /// <summary>消费动作快照驱动本地表现；第一人称换弹仅由屏幕状态栏提示，不激活头顶进度。</summary>
        private void OnPlayerActionChanged(PlayerActionChangedEvent evt)
        {
            if (released || activeMode == MapPreparationMode.ArcadePlayerOnly || activePlayer == null || !activePlayer.IsValid) return;
            var firstPersonReload = evt.Snapshot.Kind == PlayerActionKind.Reload &&
                                    this.GetModel<PlayerCameraModeModel>().Mode == PlayerCameraMode.FirstPerson;
            if (evt.HasAction && evt.Snapshot.Kind != PlayerActionKind.TemporaryBuildingReload && !firstPersonReload) activePlayer.ShowActionProgress(evt.Snapshot);
            else activePlayer.HideActionProgress();
            if (!evt.HasAction || (evt.Snapshot.Kind != PlayerActionKind.ChopTree && evt.Snapshot.Kind != PlayerActionKind.MineStone))
            { chopPresentationActionId = 0; chopPresentationBeat = -1; return; }
            var beat = Mathf.FloorToInt(evt.Snapshot.ElapsedSeconds / .7f);
            if (chopPresentationActionId == evt.Snapshot.ActionId && chopPresentationBeat == beat) return;
            chopPresentationActionId = evt.Snapshot.ActionId; chopPresentationBeat = beat;
            // 只播放现有挥动反馈，不发布攻击事件；否则 WeaponSystem 会取消持续采集。
            activePlayer.PlayWeaponSwingFeedback();
        }

        private sealed class WeaponAppearanceSlotState
        {
            public WeaponAppearanceSlotState(WeaponSlot slot)
            {
                Slot = slot;
            }

            public WeaponSlot Slot { get; }
            public int ItemId;
            public int Version;
            public bool Loading;
            public GameObject Instance;
            public ResourceLoadHandle Handle;
        }
    }
}

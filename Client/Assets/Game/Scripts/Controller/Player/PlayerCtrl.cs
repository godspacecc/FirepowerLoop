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
        private MapPreparationMode stagedMode = MapPreparationMode.ArcadePlayerOnly;
        private MapPreparationMode activeMode = MapPreparationMode.ArcadePlayerOnly;
        private string cachedPrefabConfigId;
        private GameObject cachedPrefab;
        private ResourceLoadHandle cachedPrefabHandle;
        private bool cachedPrefabHandleReleased = true;
        private bool prefabLoadInProgress;
        private bool released;
        private Camera cachedMainCamera;
        private IUnRegister testAppearanceChangedUnregister;
        /// <summary>当前玩家的幸存者造型事件订阅，玩家销毁时注销。</summary>
        private IUnRegister survivorAppearanceUnregister;
        public PlayerCtrl(IArchitecture architecture)
        {
            this.architecture = architecture;
            survivorAppearanceUnregister=architecture.RegisterEvent<SurvivorCharacterApplyEvent>(_ => {
                if(activePlayer==null||!activePlayer.ApplyTestAppearance(this.GetModel<SurvivorModel>().RunCharacter))
                    throw new InvalidOperationException("character.appearance_failed");
            });
            testAppearanceChangedUnregister = architecture.RegisterEvent<PlayerTestAppearanceChangedEvent>(_ => RefreshClothing());
        }

        public IArchitecture GetArchitecture()
        {
            return architecture;
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
            if (playerSpawns.Count != 1)
            {
                return MapControllerResult.Fail("玩家准备失败：地图必须恰好有一个 Player Spawn");
            }

            var spawn = playerSpawns[0];
            string playerConfigId = spawn.ConfigId;
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

            var localPosition = spawn.LocalPosition;
            var localRotation = spawn.LocalRotation;
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
            stagedPlayer = new PlayerViewCtrl(instance, playerId, context.MapInstanceId, this.GetUtility<ViewCtrlRegistry>(), this.GetUtility<IViewInstanceProvider>());
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

            var snapshot = new UnitSnapshot(stagedPlayer.UnitId, stagedPlayer.MapInstanceId, UnitType.Player, UnitSource.Player, cachedPrefabConfigId, stagedPlayer.Position, stagedPlayer.Rotation, UnitAuthorityMode.Local, 0, true);
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
            stagedMode = MapPreparationMode.ArcadePlayerOnly;
            PublishReady(snapshot.UnitId, snapshot.MapInstanceId);
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
            }
        }

        public void ReleaseCurrentMap()
        {
            // 先释放地图镜头架并恢复玩家标签，再销毁玩家资源。
            (activeCamera as IDisposable)?.Dispose();
            if (activePlayer != null)
            {
                this.GetSystem<UnitSystem>().Remove(activePlayer.UnitId, UnitRemovedReason.MapInstanceReleased);
                activePlayer.Dispose();
            }

            activePlayer = null;
            activeCamera = null;
            activeMode = MapPreparationMode.ArcadePlayerOnly;
        }

        public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
        {
            if (activePlayer != null && activePlayer.UnitId == unitId && activePlayer.MapInstanceId == mapInstanceId && activePlayer.IsValid)
            {
                port = activePlayer;
                return true;
            }

            port = null;
            return false;
        }

        public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
        {
            if (activeCamera != null && activePlayer != null && activePlayer.UnitId == unitId && activePlayer.MapInstanceId == mapInstanceId && activeCamera.IsValid)
            {
                port = activeCamera;
                return true;
            }

            port = null;
            return false;
        }

        public bool TryGetFlashlightPort(int unitId, long mapInstanceId, out IPlayerFlashlightPort port)
        {
            if (activePlayer != null && activePlayer.UnitId == unitId && activePlayer.MapInstanceId == mapInstanceId && activePlayer.IsValid)
            {
                port = activePlayer;
                return true;
            }

            port = null;
            return false;
        }

        public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
        {
            return activePlayer != null && activePlayer.UnitId == unitId && activePlayer.MapInstanceId == mapInstanceId && activePlayer.IsValid && activePlayer.IsPlayerCollider(collider);
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

            var active = this.GetSystem<UnitSystem>().TryGet(activePlayer.UnitId, out var unit) && unit.MapInstanceId == activePlayer.MapInstanceId && unit.IsActive;
            if (!active)
            {
                error = "当前玩家 未激活";
                return false;
            }

            snapshot = new PlayerPoseSnapshot(activePlayer.UnitId, activePlayer.MapInstanceId, activePlayer.Position, activePlayer.Rotation, true);
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

            if (!this.GetSystem<UnitSystem>().TryGet(activePlayer.UnitId, out var unit) || unit.MapInstanceId != activePlayer.MapInstanceId || !unit.IsActive)
            {
                error = "当前玩家 未激活";
                return false;
            }

            snapshot = new PlayerMapLocalPoseSnapshot(activePlayer.UnitId, activePlayer.MapInstanceId, activePlayer.LocalPosition, activePlayer.LocalRotation.eulerAngles.y, true);
            error = null;
            return true;
        }

        public void ReleaseAll()
        {
            released = true;
            survivorAppearanceUnregister?.UnRegister();
            survivorAppearanceUnregister=null;
            testAppearanceChangedUnregister?.UnRegister();
            testAppearanceChangedUnregister = null;
            ReleaseStaged();
            ReleaseCurrentMap();
            ReleaseCachedPrefab();
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
            stagedMode = MapPreparationMode.ArcadePlayerOnly;
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

        /// <summary>街机只刷新正式造型表现，不绑定旧装备库存。</summary>
        private void RefreshClothing()
        {
            if (activePlayer == null)
                return;
            if (!activePlayer.ApplyTestAppearance((this.GetModel<SurvivorModel>().Snapshot.Active?this.GetModel<SurvivorModel>().RunCharacter:this.GetModel<TestCmdModel>().PlayerAppearanceIndex)))
                Log.Error("[PlayerCtrl] 测试造型绑定缺失，保留原人物表现。");
        }

        private void PublishReady(int unitId, long mapInstanceId)
        {
            architecture.SendEvent(new ArcadePlayerReadyEvent(unitId, mapInstanceId));
        }
    }
}

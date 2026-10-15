using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using ET;
using Game.Network;
using QFramework;
using UnityEngine;

namespace Game
{
    public sealed class OnlineWorldSystem : AbstractSystem
    {
        private const int RpcTimeoutMilliseconds = 10000;
        private const long ReportIntervalMilliseconds = 100;

        private INetworkRuntime network;
        private IDisposable disconnectedSubscription;
        private IDisposable enteredSubscription;
        private IDisposable transformSubscription;
        private IDisposable leftSubscription;
        private IDisposable combatSubscription;
        private IDisposable enemyTransformSubscription;
        private IDisposable leaseSubscription;
        private IDisposable corpseSubscription;
        private IDisposable inventorySubscription;
        private IDisposable selfUnitSubscription;
        private IDisposable temporaryBuildingSubscription;
        private CancellationTokenSource operationCancellation;
        private CancellationTokenSource lifetimeCancellation;
        private CancellationTokenSource reportCancellation;
        private int generation;
        private int reportGeneration;
        private OnlineWorldSnapshot snapshot = OnlineWorldSnapshot.Empty();
        private long lastReportAt;
        private bool reportInFlight;
        private PlayerMapLocalPoseSnapshot lastReported;
        private bool hasLastReported;
        private long authenticatedAccountId;
        private long authenticatedPlayerId;
        private long authenticatedSessionEpoch;
        private bool accountCleanupInProgress;
        private bool deinitialized;
        private bool returnToBaseAfterWorld;
        private bool attackInFlight;
        private bool navigationInFlight;
        private long nextAttackSequence;
        private long nextNavigationSequence;
        private readonly OnlineWorldProjectileState projectileState=new OnlineWorldProjectileState();
        private IDisposable projectileSubscription;
        private bool convergenceInFlight;
        private bool inventoryInFlight;

        public OnlineWorldSnapshot Snapshot => snapshot;

        protected override void OnInit()
        {
            network = this.GetUtility<INetworkRuntime>();
            lifetimeCancellation = new CancellationTokenSource();
            Publish(OnlineWorldSnapshot.Empty());
            if (network != null)
            {
                disconnectedSubscription = network.RegisterDisconnectedHandler(OnDisconnected);
                enteredSubscription = network.RegisterPushHandler<G2C_WorldPlayerEntered>(OnEntered);
                transformSubscription = network.RegisterPushHandler<G2C_WorldPlayerTransformChanged>(OnTransform);
                leftSubscription = network.RegisterPushHandler<G2C_WorldPlayerLeft>(OnLeft);
                combatSubscription = network.RegisterPushHandler<G2C_WorldCombatChanged>(OnCombat);
                enemyTransformSubscription = network.RegisterPushHandler<G2C_WorldEnemiesTransformChanged>(OnEnemyTransforms);
                projectileSubscription=network.RegisterPushHandler<G2C_WorldProjectilesChanged>(OnProjectiles);
                leaseSubscription = network.RegisterPushHandler<G2C_WorldNavigationLeaseChanged>(OnLease);
                corpseSubscription = network.RegisterPushHandler<G2C_WorldCorpseChanged>(OnCorpseChanged);
                inventorySubscription = network.RegisterPushHandler<G2C_WorldSelfInventoryChanged>(OnSelfInventoryChanged);
                selfUnitSubscription = network.RegisterPushHandler<G2C_WorldSelfUnitChanged>(OnSelfUnitChanged);
                temporaryBuildingSubscription = network.RegisterPushHandler<G2C_WorldTemporaryBuildingChanged>(OnTemporaryBuildingChanged);
            }
            this.RegisterEvent<OnlineAccountChangedEvent>(OnAccountChanged);
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            deinitialized = true;
            returnToBaseAfterWorld = false;
            LifeCycle.UnregisterUpdate(OnUpdate);
            CancelOperation();
            CancelReport();
            lifetimeCancellation?.Cancel();
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = null;
            convergenceInFlight = false;
            projectileSubscription?.Dispose();
            disconnectedSubscription?.Dispose();
            enteredSubscription?.Dispose();
            transformSubscription?.Dispose();
            leftSubscription?.Dispose();
            combatSubscription?.Dispose();enemyTransformSubscription?.Dispose();leaseSubscription?.Dispose();corpseSubscription?.Dispose();inventorySubscription?.Dispose();selfUnitSubscription?.Dispose();temporaryBuildingSubscription?.Dispose();
            disconnectedSubscription = enteredSubscription = transformSubscription = leftSubscription = combatSubscription = enemyTransformSubscription = leaseSubscription = corpseSubscription = inventorySubscription = selfUnitSubscription = temporaryBuildingSubscription = null;
            inventoryInFlight = false;
        }

        public Task<OnlineWorldOperationResult> EnterWorldAsync(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId) || !MapCatalog.TryGet(mapId, out MapDefinition definition) ||
                (definition.Kind != MapKind.Survival && definition.Kind != MapKind.Task))
                return Task.FromResult(Result(OnlineWorldResultCode.OperationInvalid));
            if (snapshot.OperationState != OnlineWorldOperationState.Idle)
                return Task.FromResult(new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot));
            MapSceneModel map = this.GetModel<MapSceneModel>();
            if (!snapshot.HasWorld)
            {
                bool empty = map.Status.Value == MapLoadStatus.Idle && map.CurrentHandle == null &&
                             string.IsNullOrWhiteSpace(map.CurrentMapId);
                if (empty)
                {
                    returnToBaseAfterWorld = false;
                }
                else if (IsStableRecoverableBase(map))
                {
                    SaveSystem save = this.GetSystem<SaveSystem>();
                    if (save == null || !save.Flush(out _))
                        return Task.FromResult(Result(OnlineWorldResultCode.BaseSaveFailed));
                    returnToBaseAfterWorld = true;
                }
                else
                {
                    return Task.FromResult(Result(OnlineWorldResultCode.OperationInvalid));
                }
            }
            C2G_EnterWorld request = C2G_EnterWorld.Create();
            request.InventorySchemaVersion = 1;
            request.MapId = mapId;
            return EnterOrRestore(request, OnlineWorldOperationState.Entering, true);
        }

        public Task<OnlineWorldOperationResult> RestoreCurrentWorldAsync()
        {
            if (snapshot.OperationState != OnlineWorldOperationState.Idle)
                return Task.FromResult(new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot));
            return EnterOrRestore(C2G_GetCurrentWorld.Create(), OnlineWorldOperationState.Restoring, false);
        }

        private async Task<OnlineWorldOperationResult> EnterOrRestore(IRequest request, OnlineWorldOperationState state, bool enter)
        {
            if (network == null || network.ConnectionState != NetworkConnectionState.Connected)
                return Result(OnlineWorldResultCode.Disconnected, snapshot.HasWorld);

            int operationGeneration = StartOperation();
            CancellationToken token = operationCancellation.Token;
            Publish(snapshot.WithOperation(state, OnlineWorldResultCode.None));
            bool hasWorld;
            ET.OnlineWorldSnapshot wire;
            int error;
            try
            {
                if (enter)
                {
                    var message = (C2G_EnterWorld)request;
                    message.OperationId = Guid.NewGuid().ToString("N");
                    G2C_EnterWorld response = await network.CallAsync<G2C_EnterWorld>(message, RpcTimeoutMilliseconds, token);
                    hasWorld = response.HasWorld;
                    wire = response.World;
                    error = response.Error;
                }
                else
                {
                    G2C_GetCurrentWorld response = await network.CallAsync<G2C_GetCurrentWorld>(request, RpcTimeoutMilliseconds, token);
                    hasWorld = response.HasWorld;
                    wire = response.World;
                    error = response.Error;
                }
            }
            catch (Exception exception)
            {
                return CompleteRequestFailure(operationGeneration, exception);
            }

            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            OnlineWorldResultCode responseCode = MapError(error);
            if (responseCode != OnlineWorldResultCode.Success)
            {
                if (enter) returnToBaseAfterWorld = false;
                EndOperation(operationGeneration);
                return Result(responseCode, snapshot.HasWorld);
            }
            if (!hasWorld || wire == null)
            {
                OnlineWorldOperationResult absent = await CompleteConfirmedAbsentWorld(operationGeneration, token);
                EndOperation(operationGeneration);
                return absent;
            }

            OnlineWorldSnapshot candidate = Copy(wire, false, state, OnlineWorldResultCode.None);
            Publish(candidate);
            MapSceneResult loaded;
            try
            {
                var preparation = this.GetUtility<IGameplayResourcePreparation>();
                if (preparation != null && !await preparation.PrepareAsync(candidate.MapId, () => IsCurrent(operationGeneration), token))
                    loaded = MapSceneResult.Fail("联机地图资源未就绪", this.GetModel<MapSceneModel>().Status.Value);
                else loaded = await this.GetSystem<MapSceneSystem>().LoadOnlineWorldMapAsync(candidate, token);
            }
            catch (Exception exception)
            {
                loaded = MapSceneResult.Fail(exception.Message, this.GetModel<MapSceneModel>().Status.Value);
            }
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            if (!loaded.Success)
            {
                Log.Warning($"[OnlineWorldSystem] 联机地图加载失败：map={candidate.MapId} reason={loaded.Error}");
                OnlineWorldOperationResult failed = await ResolveFailedCandidate(operationGeneration, token, OnlineWorldResultCode.MapLoadFailed, false);
                EndOperation(operationGeneration);
                return failed;
            }

            G2C_GetCurrentWorld current;
            try
            {
                current = await network.CallAsync<G2C_GetCurrentWorld>(C2G_GetCurrentWorld.Create(), RpcTimeoutMilliseconds, token);
            }
            catch (Exception)
            {
                if (!IsCurrent(operationGeneration))
                    return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
                OnlineWorldOperationResult failed = await ResolveFailedCandidate(operationGeneration, token, OnlineWorldResultCode.StateConflict, false);
                EndOperation(operationGeneration);
                return failed;
            }
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);

            OnlineWorldResultCode currentCode = MapError(current.Error);
            bool serverAlreadyAbsent = currentCode == OnlineWorldResultCode.NotInWorld ||
                                       currentCode == OnlineWorldResultCode.Success && (!current.HasWorld || current.World == null);
            bool identityMatches = currentCode == OnlineWorldResultCode.Success && current.HasWorld && current.World != null &&
                                   current.World.WorldId == candidate.WorldId && current.World.MapId == candidate.MapId &&
                                   current.World.SelfUnitId == candidate.SelfUnitId;
            if (!identityMatches)
            {
                OnlineWorldOperationResult failed = await ResolveFailedCandidate(operationGeneration, token, OnlineWorldResultCode.StateConflict, serverAlreadyAbsent);
                EndOperation(operationGeneration);
                return failed;
            }

            Publish(Copy(current.World, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success));
            hasLastReported = false;
            this.GetSystem<MapSceneSystem>()?.OnlineWorldPlayers?.ApplySnapshot(snapshot);
            this.GetSystem<MapSceneSystem>()?.OnlineWorldCorpses?.ApplySnapshot(snapshot);
            EndOperation(operationGeneration);
            return new OnlineWorldOperationResult(OnlineWorldResultCode.Success, snapshot);
        }

        public async Task<OnlineWorldOperationResult> LeaveWorldAsync()
        {
            if (!snapshot.HasWorld) return new OnlineWorldOperationResult(OnlineWorldResultCode.Success, snapshot);
            if (snapshot.OperationState != OnlineWorldOperationState.Idle)
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            if (network == null || network.ConnectionState != NetworkConnectionState.Connected)
                return Result(OnlineWorldResultCode.Disconnected, true);

            int operationGeneration = StartOperation();
            CancellationToken token = operationCancellation.Token;
            Publish(snapshot.WithOperation(OnlineWorldOperationState.Leaving, OnlineWorldResultCode.None));
            OnlineWorldResultCode leaveCode = await RequestServerLeave(token);
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            if (leaveCode != OnlineWorldResultCode.Success && leaveCode != OnlineWorldResultCode.NotInWorld)
            {
                EndOperation(operationGeneration);
                return Result(leaveCode);
            }

            Publish(Frozen(snapshot, OnlineWorldOperationState.Leaving, OnlineWorldResultCode.None));
            MapSceneResult unloaded = await UnloadOnlineWorldMap(token);
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            if (!unloaded.Success)
            {
                EndOperation(operationGeneration);
                return Result(OnlineWorldResultCode.MapLoadFailed, true);
            }
            MapSceneResult restored = await RestoreBaseAfterConfirmedLeave(token);
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            returnToBaseAfterWorld = false;
            EndOperation(operationGeneration);
            OnlineWorldResultCode resultCode = restored.Success ? OnlineWorldResultCode.Success : OnlineWorldResultCode.MapLoadFailed;
            Publish(OnlineWorldSnapshot.Empty(resultCode));
            return new OnlineWorldOperationResult(resultCode, snapshot);
        }

        private async Task<OnlineWorldOperationResult> CompleteConfirmedAbsentWorld(int operationGeneration, CancellationToken token)
        {
            if (!snapshot.HasWorld)
            {
                MapSceneResult restored = await RestoreBaseAfterConfirmedLeave(token);
                if (!IsCurrent(operationGeneration))
                    return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
                returnToBaseAfterWorld = false;
                OnlineWorldResultCode code = restored.Success ? OnlineWorldResultCode.Success : OnlineWorldResultCode.MapLoadFailed;
                Publish(OnlineWorldSnapshot.Empty(code));
                return new OnlineWorldOperationResult(code, snapshot);
            }
            Publish(Frozen(snapshot, snapshot.OperationState, OnlineWorldResultCode.None));
            MapSceneResult unloaded = await UnloadOnlineWorldMap(token);
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            if (!unloaded.Success) return Result(OnlineWorldResultCode.MapLoadFailed, true);
            MapSceneResult restoredAfterUnload = await RestoreBaseAfterConfirmedLeave(token);
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            returnToBaseAfterWorld = false;
            OnlineWorldResultCode finalCode = restoredAfterUnload.Success ? OnlineWorldResultCode.Success : OnlineWorldResultCode.MapLoadFailed;
            Publish(OnlineWorldSnapshot.Empty(finalCode));
            return new OnlineWorldOperationResult(finalCode, snapshot);
        }

        private async Task<OnlineWorldOperationResult> ResolveFailedCandidate(int operationGeneration, CancellationToken token, OnlineWorldResultCode originalCode, bool serverAlreadyAbsent)
        {
            Publish(Frozen(snapshot, snapshot.OperationState, originalCode));
            MapSceneResult unloaded = await UnloadOnlineWorldMap(token);
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            if (!unloaded.Success) return Result(OnlineWorldResultCode.MapLoadFailed, true);
            if (!serverAlreadyAbsent)
            {
                OnlineWorldResultCode leaveCode = await RequestServerLeave(token);
                if (!IsCurrent(operationGeneration))
                    return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
                if (leaveCode != OnlineWorldResultCode.Success && leaveCode != OnlineWorldResultCode.NotInWorld)
                    return Result(leaveCode, true);
            }
            MapSceneResult restored = await RestoreBaseAfterConfirmedLeave(token);
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            returnToBaseAfterWorld = false;
            OnlineWorldResultCode code = restored.Success ? originalCode : OnlineWorldResultCode.MapLoadFailed;
            Publish(OnlineWorldSnapshot.Empty(code));
            return new OnlineWorldOperationResult(code, snapshot);
        }

        private async Task<OnlineWorldResultCode> RequestServerLeave(CancellationToken token)
        {
            try
            {
                var request = C2G_LeaveWorld.Create();
                request.OperationId = Guid.NewGuid().ToString("N");
                G2C_LeaveWorld response = await network.CallAsync<G2C_LeaveWorld>(request, RpcTimeoutMilliseconds, token);
                return MapError(response.Error);
            }
            catch (TimeoutException) { return OnlineWorldResultCode.Timeout; }
            catch (OperationCanceledException) { return OnlineWorldResultCode.OperationInProgress; }
            catch
            {
                return network?.ConnectionState == NetworkConnectionState.Connected
                    ? OnlineWorldResultCode.UnknownFailure
                    : OnlineWorldResultCode.Disconnected;
            }
        }

        private async Task<MapSceneResult> UnloadOnlineWorldMap(CancellationToken token)
        {
            try
            {
                MapSceneModel model = this.GetModel<MapSceneModel>();
                if (model == null || model.CurrentHandle == null || model.CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                    return MapSceneResult.Ok(model?.Status.Value ?? MapLoadStatus.Idle);
                string onlineMapId = model.CurrentMapId;
                IMapContentHandle onlineHandle = model.CurrentHandle;
                MapSceneSystem map = this.GetSystem<MapSceneSystem>();
                MapSceneResult result = map == null
                    ? MapSceneResult.Fail("地图系统不可用", MapLoadStatus.Failed)
                    : await map.UnloadCurrentMapAsync(token);
                if (!result.Success && model.CurrentHandle == onlineHandle && onlineHandle.IsValid &&
                    model.CurrentMode != MapPreparationMode.OnlinePlayersOnly)
                {
                    model.SetLoaded(onlineMapId, onlineHandle, MapPreparationMode.OnlinePlayersOnly);
                }
                return result;
            }
            catch (Exception exception)
            {
                return MapSceneResult.Fail(exception.Message, this.GetModel<MapSceneModel>().Status.Value);
            }
        }

        private bool IsStableRecoverableBase(MapSceneModel map)
        {
            SaveModel save = this.GetModel<SaveModel>();
            return map != null && map.Status.Value == MapLoadStatus.Loaded && map.CurrentHandle != null &&
                   map.CurrentHandle.IsValid && map.CurrentHandle.Context != null &&
                   string.Equals(map.CurrentMapId, MapIds.Base, StringComparison.Ordinal) &&
                   map.CurrentMode == MapPreparationMode.OfflineFull && save != null &&
                   save.CurrentDomain == OfflineSaveDomain.Base && save.CurrentBase != null;
        }

        private async Task<MapSceneResult> RestoreBaseAfterConfirmedLeave(CancellationToken token)
        {
            if (!returnToBaseAfterWorld || IsStableRecoverableBase(this.GetModel<MapSceneModel>()))
                return MapSceneResult.Ok(this.GetModel<MapSceneModel>()?.Status.Value ?? MapLoadStatus.Idle);
            BaseSaveData baseData = this.GetModel<SaveModel>()?.CurrentBase;
            MapSceneSystem map = this.GetSystem<MapSceneSystem>();
            if (baseData == null || map == null)
                return MapSceneResult.Fail("缺少可恢复的基地快照", this.GetModel<MapSceneModel>()?.Status.Value ?? MapLoadStatus.Failed);
            try
            {
                return await map.LoadBaseMapAsync(baseData, token);
            }
            catch (Exception exception)
            {
                return MapSceneResult.Fail(exception.Message, this.GetModel<MapSceneModel>()?.Status.Value ?? MapLoadStatus.Failed);
            }
        }

        private OnlineWorldOperationResult CompleteRequestFailure(int operationGeneration, Exception exception)
        {
            if (!IsCurrent(operationGeneration))
                return new OnlineWorldOperationResult(OnlineWorldResultCode.OperationInProgress, snapshot);
            EndOperation(operationGeneration);
            OnlineWorldResultCode code = exception is TimeoutException
                ? OnlineWorldResultCode.Timeout
                : exception is OperationCanceledException
                    ? OnlineWorldResultCode.OperationInProgress
                    : network?.ConnectionState == NetworkConnectionState.Connected
                        ? OnlineWorldResultCode.UnknownFailure
                        : OnlineWorldResultCode.Disconnected;
            return Result(code, snapshot.HasWorld && code == OnlineWorldResultCode.Disconnected);
        }

        public bool TryRequestWeaponAttack(int weaponItemId,Vector3 aimDirection)
        {
            OnlineWorldPlayerSnapshot self=SelfPlayer();if(network==null||network.ConnectionState!=NetworkConnectionState.Connected||!snapshot.HasWorld||snapshot.ConnectionLost||self.UnitId==0||self.IsDead||attackInFlight||!IsEquippedActiveWeapon(weaponItemId)||!Finite(aimDirection.x,aimDirection.z)||new Vector2(aimDirection.x,aimDirection.z).sqrMagnitude<0.000001f)return false;
            long sequence=++nextAttackSequence;SendAttack(sequence,weaponItemId,aimDirection,self.LifecycleRevision,generation).Forget();return true;
        }

        /// <summary>弓箭提交地图本地三维发射点和方向；索引未准备或与权威版本不符时不发送、不消费。</summary>
        public bool TryRequestWeaponAttack(int weaponItemId,Vector3 direction,Vector3 origin,string contentHash)
        {
            var self=SelfPlayer();
            if(network==null||network.ConnectionState!=NetworkConnectionState.Connected||!snapshot.HasWorld||snapshot.ConnectionLost||self.UnitId==0||self.IsDead||attackInFlight||!IsEquippedActiveWeapon(weaponItemId)||
                !Finite(direction.x,direction.z)||!Finite(direction.y,origin.y)||!Finite(origin.x,origin.z)||direction.sqrMagnitude<.000001f||
                !WorldProjectileCollisionCatalog.TryGet(snapshot.MapId,out var index,out _)||index.ContentHash!=contentHash||contentHash!=snapshot.CollisionContentHash)return false;
            SendAttack(++nextAttackSequence,weaponItemId,direction.normalized,self.LifecycleRevision,generation,origin,contentHash).Forget();return true;
        }

        public bool TryRequestMeleeAttack(int weaponItemId,Vector3 aimDirection)=>TryRequestWeaponAttack(weaponItemId,aimDirection);

        private bool IsEquippedActiveWeapon(int weaponItemId)
        {
            OnlineEquipmentSnapshot equipment=snapshot.SelfInventory.Equipment;
            if(equipment.ActiveSlot==(int)WeaponSlot.Ranged)return weaponItemId>0&&weaponItemId==equipment.RangedWeaponItemId&&equipment.RangedLoadedAmmo>0;
            return equipment.ActiveSlot==(int)WeaponSlot.Melee&&(weaponItemId==0||weaponItemId==equipment.MeleeWeaponItemId);
        }

        private async UniTaskVoid SendAttack(long sequence,int weaponItemId,Vector3 aim,long lifecycle,int ownerGeneration,Vector3? origin=null,string contentHash=null)
        {attackInFlight=true;try{C2G_WorldMeleeAttack q=C2G_WorldMeleeAttack.Create();q.AttackSequence=sequence;q.WeaponItemId=weaponItemId;q.AimX=aim.x;q.AimZ=aim.z;q.LifecycleRevision=lifecycle;if(origin.HasValue){q.AimY=aim.y;q.HasProjectileOrigin=true;q.OriginX=origin.Value.x;q.OriginY=origin.Value.y;q.OriginZ=origin.Value.z;q.CollisionContentHash=contentHash;}await network.CallAsync<G2C_WorldMeleeAttack>(q,RpcTimeoutMilliseconds,lifetimeCancellation.Token);}catch{}finally{if(ownerGeneration==generation)attackInFlight=false;}}

        public async Task<bool> OperateTemporaryBuildingAsync(OnlineTemporaryBuildingActionKind action,int runtimeId,string configId,Vector3 position,float yawDegrees,Vector3 aim,long expectedBuildingRevision)
        {
            OnlineWorldPlayerSnapshot self=SelfPlayer();
            if(network==null||network.ConnectionState!=NetworkConnectionState.Connected||!snapshot.HasWorld||snapshot.ConnectionLost||self.UnitId==0||self.IsDead)return false;
            int owner=generation;
            try
            {
                C2G_OperateWorldTemporaryBuilding q=C2G_OperateWorldTemporaryBuilding.Create();
                q.OperationId=Guid.NewGuid().ToString("N");q.ActionKind=(int)action;q.RuntimeId=runtimeId;q.ConfigId=configId??string.Empty;
                q.PositionX=position.x;q.PositionY=position.y;q.PositionZ=position.z;q.YawDegrees=yawDegrees;q.AimX=aim.x;q.AimZ=aim.z;
                q.ExpectedInventoryRevision=snapshot.SelfInventory.Revision;q.ExpectedBuildingRevision=expectedBuildingRevision;q.LifecycleRevision=self.LifecycleRevision;
                G2C_OperateWorldTemporaryBuilding r=await network.CallAsync<G2C_OperateWorldTemporaryBuilding>(q,RpcTimeoutMilliseconds,lifetimeCancellation.Token);
                if(owner!=generation)return false;
                if(r.SelfInventory!=null)ApplyInventoryResponse(r.SelfInventory,null);
                if(r.Error==0&&r.Accepted)ApplyTemporaryBuildingChange(r.WorldRevision,r.ServerTime,r.HasBuilding?r.Building:null,r.RemovedRuntimeId);
                else ScheduleConvergence();
                return r.Error==0&&r.Accepted;
            }
            catch{ScheduleConvergence();return false;}
        }

        public bool TryReportEnemyNavigation(long leaseRevision,IReadOnlyList<OnlineWorldEnemyNavigationSample> reports)
        {if(network==null||network.ConnectionState!=NetworkConnectionState.Connected||!snapshot.HasWorld||snapshot.ConnectionLost||navigationInFlight||leaseRevision!=snapshot.NavigationLeaseRevision||snapshot.NavigationExecutorUnitId!=snapshot.SelfUnitId)return false;long sequence=++nextNavigationSequence;SendNavigation(sequence,leaseRevision,reports??Array.Empty<OnlineWorldEnemyNavigationSample>(),generation).Forget();return true;}

        private async UniTaskVoid SendNavigation(long sequence,long lease,IReadOnlyList<OnlineWorldEnemyNavigationSample> reports,int ownerGeneration)
        {navigationInFlight=true;try{C2G_ReportWorldEnemyNavigation q=C2G_ReportWorldEnemyNavigation.Create();q.NavigationLeaseRevision=lease;q.ReportSequence=sequence;foreach(OnlineWorldEnemyNavigationSample s in reports){WorldEnemyNavigationReport r=WorldEnemyNavigationReport.Create();r.UnitId=s.UnitId;r.LifecycleRevision=s.LifecycleRevision;r.PositionX=s.LocalPosition.x;r.PositionY=s.LocalPosition.y;r.PositionZ=s.LocalPosition.z;r.YawDegrees=s.YawDegrees;q.Reports.Add(r);}await network.CallAsync<G2C_ReportWorldEnemyNavigation>(q,RpcTimeoutMilliseconds,lifetimeCancellation.Token);}catch{}finally{if(ownerGeneration==generation)navigationInFlight=false;}}

        private OnlineWorldPlayerSnapshot SelfPlayer()=>snapshot.Players.FirstOrDefault(x=>x.UnitId==snapshot.SelfUnitId);
        private static bool Finite(params float[] values){foreach(float v in values)if(float.IsNaN(v)||float.IsInfinity(v))return false;return true;}

        private void OnCombat(G2C_WorldCombatChanged m)
        {
            if(!ValidIncrement(m.WorldId,m.MapId,m.WorldRevision))return;List<OnlineWorldPlayerSnapshot> players=snapshot.Players.ToList();foreach(WorldPlayerInfo p in m.Players){int i=players.FindIndex(x=>x.UnitId==p.UnitId);if(i>=0&&(players[i].StateRevision>p.StateRevision||players[i].LifecycleRevision>p.LifecycleRevision))continue;if(i>=0)players[i]=Copy(p);else players.Add(Copy(p));}List<OnlineWorldEnemySnapshot> enemies=snapshot.Enemies.ToList();foreach(WorldEnemyInfo e in m.Enemies){int i=enemies.FindIndex(x=>x.UnitId==e.UnitId);if(i>=0&&(enemies[i].StateRevision>e.StateRevision||enemies[i].LifecycleRevision>e.LifecycleRevision))continue;if(e.IsDead){if(i>=0)enemies.RemoveAt(i);continue;}if(i>=0)enemies[i]=Copy(e);else enemies.Add(Copy(e));}players.Sort((a,b)=>a.UnitId.CompareTo(b.UnitId));enemies.Sort((a,b)=>a.UnitId.CompareTo(b.UnitId));Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId,m.WorldRevision,players,enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,m.ServerTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,snapshot.SelfInventory,snapshot.Corpses,snapshot.TemporaryBuildings));long[] targets=m.TargetUnitIds?.ToArray()??Array.Empty<long>();this.SendEvent(new OnlineWorldCombatPresentationEvent(m.CombatSequence,(OnlineWorldCombatActionKind)m.ActionKind,m.ActorUnitId,m.WeaponItemId,targets,m.Enemies?.Select(Copy).ToArray(),m.Players?.Select(Copy).ToArray()));
        }

        private void OnEnemyTransforms(G2C_WorldEnemiesTransformChanged m)
        {if(!ValidIncrement(m.WorldId,m.MapId,m.WorldRevision))return;List<OnlineWorldEnemySnapshot> enemies=snapshot.Enemies.ToList();foreach(WorldEnemyTransformInfo t in m.Enemies){int i=enemies.FindIndex(x=>x.UnitId==t.UnitId);if(i<0){ScheduleConvergence();continue;}OnlineWorldEnemySnapshot old=enemies[i];if(old.StateRevision>t.StateRevision||old.LifecycleRevision>t.LifecycleRevision)continue;if(old.LifecycleRevision<t.LifecycleRevision){ScheduleConvergence();continue;}enemies[i]=new OnlineWorldEnemySnapshot(old.UnitId,old.WorldId,old.MapId,old.EnemyConfigId,old.SpawnLocalPosition,old.SpawnYawDegrees,new Vector3(t.PositionX,t.PositionY,t.PositionZ),t.YawDegrees,old.TargetUnitId,old.CurrentHP,old.MaxHP,old.IsDead,old.RespawnAtServerTime,t.StateRevision,t.LifecycleRevision);}Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId,m.WorldRevision,snapshot.Players,enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,snapshot.ServerTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,snapshot.SelfInventory,snapshot.Corpses,snapshot.TemporaryBuildings));}

        private void OnProjectiles(G2C_WorldProjectilesChanged m)
        {
            if(!snapshot.HasWorld||snapshot.WorldId!=m.WorldId||snapshot.MapId!=m.MapId)return;
            projectileState.Apply(m);
            Publish(snapshot.WithProjectiles(projectileState.Projectiles,Math.Max(snapshot.WorldRevision,m.WorldRevision),Math.Max(snapshot.ServerTime,m.ServerTime),snapshot.CollisionContentHash));
        }

        private void OnLease(G2C_WorldNavigationLeaseChanged m)
        {if(!ValidIncrement(m.WorldId,m.MapId,m.WorldRevision)||m.NavigationLeaseRevision<snapshot.NavigationLeaseRevision)return;nextNavigationSequence=0;navigationInFlight=false;Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId,m.WorldRevision,snapshot.Players,snapshot.Enemies,m.NavigationExecutorUnitId,m.NavigationLeaseRevision,m.ServerTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,snapshot.SelfInventory,snapshot.Corpses,snapshot.TemporaryBuildings));}

        private void OnCorpseChanged(G2C_WorldCorpseChanged message)
        {
            if(!ValidIncrement(message.WorldId,message.MapId,message.WorldRevision))return;
            List<OnlineWorldCorpseSnapshot> corpses=snapshot.Corpses.Where(x=>x.CorpseId!=(message.HasCorpse?message.Corpse?.CorpseId??0:message.RemovedCorpseId)).ToList();
            if(message.HasCorpse&&message.Corpse!=null)
            {
                OnlineWorldCorpseSnapshot value=Copy(message.Corpse);
                OnlineWorldCorpseSnapshot old=snapshot.Corpses.FirstOrDefault(x=>x.CorpseId==value.CorpseId);
                if(old.CorpseId!=0&&old.StateRevision>value.StateRevision)return;
                corpses.Add(value);corpses.Sort((a,b)=>a.CorpseId.CompareTo(b.CorpseId));
            }
            Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId,message.WorldRevision,snapshot.Players,snapshot.Enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,snapshot.ServerTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,snapshot.SelfInventory,corpses,snapshot.TemporaryBuildings));
            OnlineWorldModel model=this.GetModel<OnlineWorldModel>();if(model?.OpenCorpse.Value is OnlineWorldCorpseContentsSnapshot open&&(!message.HasCorpse&&open.Corpse.CorpseId==message.RemovedCorpseId))model.PublishOpenCorpse(null);
        }

        private void OnSelfInventoryChanged(G2C_WorldSelfInventoryChanged message)
        {
            if(snapshot.ConnectionLost||!snapshot.HasWorld||message.WorldId!=snapshot.WorldId||message.MapId!=snapshot.MapId||message.SelfInventory==null)return;
            OnlineInventorySnapshot inventory=Copy(message.SelfInventory);if(inventory.Revision<snapshot.SelfInventory.Revision)return;
            Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId,snapshot.WorldRevision,snapshot.Players,snapshot.Enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,snapshot.ServerTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,inventory,snapshot.Corpses,snapshot.TemporaryBuildings));
            this.SendEvent(new OnlineWorldInventoryChangedEvent(inventory.Revision,0,0));
        }

        private void OnSelfUnitChanged(G2C_WorldSelfUnitChanged message)
        {
            if(!ValidIncrement(message.WorldId,message.MapId,message.WorldRevision)||snapshot.SelfUnitId!=message.PreviousUnitId)return;
            List<OnlineWorldPlayerSnapshot> players=snapshot.Players.Where(x=>x.UnitId!=message.PreviousUnitId).ToList();
            Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,message.SelfUnitId,message.WorldRevision,players,snapshot.Enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,snapshot.ServerTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,snapshot.SelfInventory,snapshot.Corpses,snapshot.TemporaryBuildings));
            if(message.SelfUnitId!=0)ScheduleConvergence();
        }

        private void OnTemporaryBuildingChanged(G2C_WorldTemporaryBuildingChanged message)
        {
            if(!ValidIncrement(message.WorldId,message.MapId,message.WorldRevision))return;
            ApplyTemporaryBuildingChange(message.WorldRevision,message.ServerTime,message.HasBuilding?message.Building:null,message.RemovedRuntimeId);
        }

        private void ApplyTemporaryBuildingChange(long worldRevision,long serverTime,WorldTemporaryBuildingInfo wire,int removedRuntimeId)
        {
            if(!snapshot.HasWorld||worldRevision<snapshot.WorldRevision)return;
            List<OnlineWorldTemporaryBuildingSnapshot> buildings=snapshot.TemporaryBuildings.ToList();
            if(removedRuntimeId>0)buildings.RemoveAll(x=>x.RuntimeId==removedRuntimeId);
            if(wire!=null)
            {
                OnlineWorldTemporaryBuildingSnapshot value=Copy(wire);int index=buildings.FindIndex(x=>x.RuntimeId==value.RuntimeId);
                if(index>=0&&buildings[index].StateRevision>value.StateRevision)return;
                if(index>=0)buildings[index]=value;else buildings.Add(value);
            }
            buildings.Sort((a,b)=>a.RuntimeId.CompareTo(b.RuntimeId));
            Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId,worldRevision,snapshot.Players,snapshot.Enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,serverTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,snapshot.SelfInventory,snapshot.Corpses,buildings));
        }

        public async Task<bool> OpenWorldCorpseAsync(long corpseId)
        {
            if(inventoryInFlight||network==null||network.ConnectionState!=NetworkConnectionState.Connected||!snapshot.HasWorld||snapshot.ConnectionLost||snapshot.SelfUnitId==0)return false;
            OnlineWorldPlayerSnapshot self=SelfPlayer();if(self.UnitId==0)return false;int owner=generation;inventoryInFlight=true;
            try{C2G_OpenWorldCorpse q=C2G_OpenWorldCorpse.Create();q.CorpseId=corpseId;q.LifecycleRevision=self.LifecycleRevision;G2C_OpenWorldCorpse r=await network.CallAsync<G2C_OpenWorldCorpse>(q,RpcTimeoutMilliseconds,lifetimeCancellation.Token);if(owner!=generation||r.Error!=0||!r.HasCorpse||r.CorpseContents==null)return false;ApplyInventoryResponse(r.SelfInventory,r.CorpseContents);return true;}
            catch{return false;}finally{inventoryInFlight=false;}
        }

        public async Task<bool> OperateWorldInventoryAsync(string operationId,long inventoryRevision,long corpseId,long corpseRevision,int action,int sourceKind,int sourceSlot,int targetKind,int targetSlot)
        {
            if(inventoryInFlight||network==null||network.ConnectionState!=NetworkConnectionState.Connected||!snapshot.HasWorld||snapshot.ConnectionLost||snapshot.SelfUnitId==0)return false;
            OnlineWorldPlayerSnapshot self=SelfPlayer();if(self.UnitId==0)return false;int owner=generation;inventoryInFlight=true;
            try{C2G_OperateWorldInventory q=C2G_OperateWorldInventory.Create();q.OperationId=operationId;q.ExpectedInventoryRevision=inventoryRevision;q.CorpseId=corpseId;q.ExpectedCorpseRevision=corpseRevision;q.InventorySchemaVersion=1;q.ExpectedItemId=action is 9 or 12?snapshot.SelfInventory.Slots.FirstOrDefault(x=>x.SlotIndex==sourceSlot).ItemId:action==11?snapshot.SelfInventory.Equipment.ItemId:0;q.ActionKind=action;q.SourceContainerKind=sourceKind;q.SourceSlotIndex=sourceSlot;q.TargetContainerKind=targetKind;q.TargetSlotIndex=targetSlot;q.LifecycleRevision=self.LifecycleRevision;G2C_OperateWorldInventory r=await network.CallAsync<G2C_OperateWorldInventory>(q,RpcTimeoutMilliseconds,lifetimeCancellation.Token);if(owner!=generation)return false;ApplyInventoryResponse(r.SelfInventory,r.HasCorpse?r.CorpseContents:null);if(r.Error!=0)ScheduleConvergence();return r.Error==0&&r.Accepted;}
            catch{ScheduleConvergence();return false;}finally{inventoryInFlight=false;}
        }

        private void ApplyInventoryResponse(OnlineInventoryInfo inventory,WorldCorpseContentsInfo corpse)
        {
            if(inventory!=null&&inventory.Revision>=snapshot.SelfInventory.Revision)Publish(new OnlineWorldSnapshot(true,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId,snapshot.WorldRevision,snapshot.Players,snapshot.Enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,snapshot.ServerTime,snapshot.ConnectionLost,snapshot.OperationState,snapshot.LastResultCode,Copy(inventory),snapshot.Corpses,snapshot.TemporaryBuildings));
            this.GetModel<OnlineWorldModel>()?.PublishOpenCorpse(corpse==null?null:Copy(corpse));
        }
        private bool ValidIncrement(long world,string map,long revision)
        {
            if(snapshot.ConnectionLost||!snapshot.HasWorld)return false;
            if(world!=snapshot.WorldId||map!=snapshot.MapId){ScheduleConvergence();return false;}
            return revision>=snapshot.WorldRevision;
        }

        private void ScheduleConvergence()
        {
            if(convergenceInFlight||deinitialized||snapshot.ConnectionLost||!snapshot.HasWorld||
               snapshot.OperationState!=OnlineWorldOperationState.Idle||network==null||
               network.ConnectionState!=NetworkConnectionState.Connected)return;
            convergenceInFlight=true;
            ConvergeCurrentWorld(generation,snapshot.WorldId,snapshot.MapId,snapshot.SelfUnitId).Forget();
        }

        private async UniTaskVoid ConvergeCurrentWorld(int ownerGeneration,long worldId,string mapId,long selfUnitId)
        {
            try
            {
                G2C_GetCurrentWorld response=await network.CallAsync<G2C_GetCurrentWorld>(C2G_GetCurrentWorld.Create(),RpcTimeoutMilliseconds,lifetimeCancellation.Token);
                if(ownerGeneration!=generation||deinitialized||response.Error!=0||!response.HasWorld||response.World==null)return;
                if(response.World.WorldId!=worldId||response.World.MapId!=mapId||response.World.SelfUnitId!=selfUnitId)return;
                Publish(Copy(response.World,false,OnlineWorldOperationState.Idle,OnlineWorldResultCode.Success));
            }
            catch{}
            finally{if(ownerGeneration==generation)convergenceInFlight=false;}
        }

        private void OnUpdate()
        {
            long now = (long)(Time.realtimeSinceStartup * 1000f);
            OnlineAccountModel account = this.GetModel<OnlineAccountModel>();
            if (!snapshot.HasWorld || snapshot.ConnectionLost || snapshot.OperationState != OnlineWorldOperationState.Idle ||
                account == null || !account.Snapshot.Value.IsAuthenticated || reportInFlight ||
                now - lastReportAt < ReportIntervalMilliseconds) return;
            IPlayerRuntimePortProvider ports = this.GetUtility<IPlayerRuntimePortProvider>();
            if (ports == null || !ports.TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot pose, out _) || !pose.IsActive) return;
            if (hasLastReported && (pose.LocalPosition - lastReported.LocalPosition).sqrMagnitude < 0.000001f &&
                Mathf.Abs(Mathf.DeltaAngle(pose.LocalYawDegrees, lastReported.LocalYawDegrees)) < 0.001f) return;
            Report(pose).Forget();
        }

        private async UniTask Report(PlayerMapLocalPoseSnapshot pose)
        {
            CancelReport();
            int currentReportGeneration = ++reportGeneration;
            reportCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            CancellationToken token = reportCancellation.Token;
            reportInFlight = true;
            lastReportAt = (long)(Time.realtimeSinceStartup * 1000f);
            int operationGeneration = generation;
            try
            {
                var request = C2G_ReportWorldTransform.Create();
                request.PositionX = pose.LocalPosition.x;
                request.PositionY = pose.LocalPosition.y;
                request.PositionZ = pose.LocalPosition.z;
                request.YawDegrees = pose.LocalYawDegrees;
                request.LifecycleRevision = SelfPlayer().LifecycleRevision;
                G2C_ReportWorldTransform response = await network.CallAsync<G2C_ReportWorldTransform>(request, RpcTimeoutMilliseconds, token);
                if (currentReportGeneration == reportGeneration && operationGeneration == generation && response.Error == 0)
                {
                    lastReported = pose;
                    hasLastReported = true;
                }
            }
            catch { }
            finally
            {
                if (currentReportGeneration == reportGeneration)
                {
                    reportCancellation?.Dispose();
                    reportCancellation = null;
                    reportInFlight = false;
                }
            }
        }

        private void OnEntered(G2C_WorldPlayerEntered message)
        {
            if (snapshot.ConnectionLost || !snapshot.HasWorld || message.Player == null || message.Player.UnitId == snapshot.SelfUnitId) return;
            if(message.Player.WorldId!=snapshot.WorldId||message.Player.MapId!=snapshot.MapId){ScheduleConvergence();return;}
            if(message.WorldRevision < snapshot.WorldRevision)return;
            Upsert(message.Player, message.WorldRevision);
        }

        private void OnTransform(G2C_WorldPlayerTransformChanged message)
        {
            if (snapshot.ConnectionLost || !snapshot.HasWorld || message.Player == null) return;
            if(message.WorldId!=snapshot.WorldId||message.MapId!=snapshot.MapId){ScheduleConvergence();return;}
            if(message.WorldRevision < snapshot.WorldRevision)return;
            OnlineWorldPlayerSnapshot old = snapshot.Players.FirstOrDefault(x => x.UnitId == message.Player.UnitId);
            if(old.UnitId==0){ScheduleConvergence();return;}
            if(old.StateRevision>message.Player.StateRevision||old.LifecycleRevision>message.Player.LifecycleRevision)return;
            if(old.LifecycleRevision<message.Player.LifecycleRevision){ScheduleConvergence();return;}
            Upsert(message.Player, message.WorldRevision);
        }

        private void Upsert(WorldPlayerInfo info, long revision)
        {
            var players = snapshot.Players.Where(x => x.UnitId != info.UnitId).ToList();
            players.Add(Copy(info));
            players.Sort((left, right) => left.UnitId.CompareTo(right.UnitId));
            Publish(new OnlineWorldSnapshot(true, snapshot.WorldId, snapshot.MapId, snapshot.SelfUnitId, revision,
                players,snapshot.Enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,snapshot.ServerTime, snapshot.ConnectionLost, snapshot.OperationState, snapshot.LastResultCode,snapshot.SelfInventory,snapshot.Corpses,snapshot.TemporaryBuildings));
        }

        private void OnLeft(G2C_WorldPlayerLeft message)
        {
            if (snapshot.ConnectionLost || !snapshot.HasWorld || message.WorldId != snapshot.WorldId ||
                message.MapId != snapshot.MapId || message.WorldRevision < snapshot.WorldRevision) return;
            var players = snapshot.Players.Where(x => x.UnitId != message.UnitId).ToList();
            Publish(new OnlineWorldSnapshot(true, snapshot.WorldId, snapshot.MapId, snapshot.SelfUnitId, message.WorldRevision,
                players,snapshot.Enemies,snapshot.NavigationExecutorUnitId,snapshot.NavigationLeaseRevision,snapshot.ServerTime, snapshot.ConnectionLost, snapshot.OperationState, snapshot.LastResultCode,snapshot.SelfInventory,snapshot.Corpses,snapshot.TemporaryBuildings));
        }

        private void OnDisconnected(NetworkDisconnected _)
        {
            if (deinitialized) return;
            CancelOperation();
            CancelReport();
            accountCleanupInProgress = false;
            authenticatedAccountId = authenticatedPlayerId = authenticatedSessionEpoch = 0;
            if (snapshot.HasWorld) Publish(Frozen(snapshot, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Disconnected));
        }

        private void OnAccountChanged(OnlineAccountChangedEvent evt)
        {
            if (deinitialized) return;
            OnlineAccountSnapshot account = evt.Snapshot;
            if (account.IsAuthenticated)
            {
                if (account.OperationState == OnlineOperationState.LoggingOut) return;
                bool newSession = account.AccountId != authenticatedAccountId || account.PlayerId != authenticatedPlayerId ||
                                  account.SessionEpoch != authenticatedSessionEpoch;
                authenticatedAccountId = account.AccountId;
                authenticatedPlayerId = account.PlayerId;
                authenticatedSessionEpoch = account.SessionEpoch;
                if ((newSession || snapshot.ConnectionLost) && snapshot.OperationState == OnlineWorldOperationState.Idle)
                    _ = RestoreCurrentWorldAsync();
                return;
            }

            authenticatedAccountId = authenticatedPlayerId = authenticatedSessionEpoch = 0;
            bool confirmedTermination = account.ConnectionState == OnlineConnectionState.Disconnected &&
                                        (account.LastResultCode == OnlineAccountResultCode.Success ||
                                         account.LastResultCode == OnlineAccountResultCode.SessionReplaced);
            if (confirmedTermination && accountCleanupInProgress) return;
            CancelOperation();
            CancelReport();
            if (!confirmedTermination) accountCleanupInProgress = false;
            if (!snapshot.HasWorld)
            {
                if (confirmedTermination)
                {
                    returnToBaseAfterWorld = false;
                    Publish(OnlineWorldSnapshot.Empty(OnlineWorldResultCode.Success));
                }
                return;
            }
            if (confirmedTermination)
            {
                StartConfirmedAccountCleanup();
                return;
            }
            Publish(Frozen(snapshot, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Disconnected));
        }

        private async void StartConfirmedAccountCleanup()
        {
            if (accountCleanupInProgress) return;
            accountCleanupInProgress = true;
            int operationGeneration = StartOperation();
            CancellationToken token = operationCancellation.Token;
            Publish(Frozen(snapshot, OnlineWorldOperationState.Leaving, OnlineWorldResultCode.None));
            try
            {
                MapSceneResult unloaded = await UnloadOnlineWorldMap(token);
                if (IsCurrent(operationGeneration) && !deinitialized)
                {
                    EndOperation(operationGeneration);
                    if (unloaded.Success)
                    {
                        returnToBaseAfterWorld = false;
                        Publish(OnlineWorldSnapshot.Empty(OnlineWorldResultCode.Success));
                    }
                    else Result(OnlineWorldResultCode.MapLoadFailed, true);
                }
            }
            finally
            {
                if (operationGeneration == generation || deinitialized) accountCleanupInProgress = false;
            }
            OnlineAccountModel account = this.GetModel<OnlineAccountModel>();
            if (!deinitialized && !accountCleanupInProgress && account != null && account.Snapshot.Value.IsAuthenticated &&
                snapshot.OperationState == OnlineWorldOperationState.Idle)
            {
                _ = RestoreCurrentWorldAsync();
            }
        }

        private OnlineWorldOperationResult Result(OnlineWorldResultCode code, bool freeze = false)
        {
            Publish(freeze ? Frozen(snapshot, OnlineWorldOperationState.Idle, code) : snapshot.WithOperation(OnlineWorldOperationState.Idle, code));
            return new OnlineWorldOperationResult(code, snapshot);
        }

        private void Publish(OnlineWorldSnapshot value)
        {
            if (deinitialized) return;
            projectileState.ObserveWorld(value.WorldId,value.HasWorld?value.MapId:null);
            if(value.IsProjectileFullSnapshot)projectileState.ReplaceSnapshot(value.Projectiles,value.WorldRevision);
            if(value.HasWorld)projectileState.PruneOwners(value.Players);
            string collisionHash=value.CollisionContentHash;
            if(!value.IsProjectileFullSnapshot&&value.HasWorld&&snapshot!=null&&value.WorldId==snapshot.WorldId&&value.MapId==snapshot.MapId&&string.IsNullOrEmpty(collisionHash))collisionHash=snapshot.CollisionContentHash;
            value=value.WithProjectiles(projectileState.Projectiles,value.WorldRevision,value.ServerTime,collisionHash);

            var previousSnapshot = snapshot;
            OnlineWorldPlayerSnapshot previousSelf=snapshot?.Players.FirstOrDefault(player=>player.UnitId==snapshot.SelfUnitId)??default;
            snapshot = value;
            if (previousSnapshot != null && (previousSnapshot.SelfInventory.Equipment.ItemId != value.SelfInventory.Equipment.ItemId ||
                previousSnapshot.SelfInventory.Equipment.SourceSlotIndex != value.SelfInventory.Equipment.SourceSlotIndex))
            {
                this.GetSystem<MobileInputSystem>().ClearCombatInput();
                this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.ContextInvalid);
            }
            if (value != null && value.HasWorld)
            {
                OnlineWorldPlayerSnapshot self = value.Players.FirstOrDefault(player => player.UnitId == value.SelfUnitId);
                if (self.UnitId != 0 && self.LastAcceptedAttackSequence > nextAttackSequence)
                {
                    nextAttackSequence = self.LastAcceptedAttackSequence;
                }
            }
            this.GetModel<OnlineWorldModel>()?.Publish(value);
            OnlineWorldPlayerSnapshot nextSelf=value?.Players.FirstOrDefault(player=>player.UnitId==value.SelfUnitId)??default;
            if(previousSelf.UnitId!=0&&nextSelf.UnitId==previousSelf.UnitId&&nextSelf.LifecycleRevision==previousSelf.LifecycleRevision&&nextSelf.CurrentHP<previousSelf.CurrentHP)
                this.SendEvent(new OnlineWorldSelfDamagedEvent(nextSelf.UnitId,nextSelf.LifecycleRevision,previousSelf.CurrentHP-nextSelf.CurrentHP));
            MapSceneSystem map=this.GetSystem<MapSceneSystem>();map?.OnlineWorldPlayers?.ApplySnapshot(value);map?.OnlineWorldEnemies?.ApplySnapshot(value);map?.OnlineWorldCorpses?.ApplySnapshot(value);map?.ApplyOnlineLocalPlayerSnapshot(value);
            if (TemporaryBuildingsChanged(previousSnapshot, value)) this.GetSystem<TemporaryBuildingSystem>()?.ApplyOnlineSnapshot(value);
        }

        private static bool TemporaryBuildingsChanged(OnlineWorldSnapshot before, OnlineWorldSnapshot after)
        {
            if (before == null || after == null || before.TemporaryBuildings.Count != after.TemporaryBuildings.Count) return true;
            for (var i = 0; i < before.TemporaryBuildings.Count; i++)
            {
                var left = before.TemporaryBuildings[i];var right = after.TemporaryBuildings[i];
                if (left.RuntimeId != right.RuntimeId || left.StateRevision != right.StateRevision || left.LoadedAmmo != right.LoadedAmmo || left.NextShootAtServerTime != right.NextShootAtServerTime) return true;
            }
            return false;
        }

        private int StartOperation()
        {
            CancelOperation();
            operationCancellation = new CancellationTokenSource();
            return generation;
        }

        private void EndOperation(int operationGeneration)
        {
            if (operationGeneration == generation)
            {
                operationCancellation?.Dispose();
                operationCancellation = null;
            }
        }

        private void CancelOperation()
        {
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = null;
            generation++;
            convergenceInFlight = false;
        }

        private void CancelReport()
        {
            reportGeneration++;
            reportCancellation?.Cancel();
            reportCancellation?.Dispose();
            reportCancellation = null;
            reportInFlight = false;
        }

        private bool IsCurrent(int operationGeneration) => !deinitialized && operationGeneration == generation;

        private static OnlineWorldSnapshot Frozen(OnlineWorldSnapshot source, OnlineWorldOperationState operation, OnlineWorldResultCode code)
        {
            return new OnlineWorldSnapshot(source.HasWorld, source.WorldId, source.MapId, source.SelfUnitId, source.WorldRevision,
                source.Players,source.Enemies,source.NavigationExecutorUnitId,source.NavigationLeaseRevision,source.ServerTime,true, operation, code,source.SelfInventory,source.Corpses,source.TemporaryBuildings);
        }

        internal static OnlineWorldResultCode MapError(int error) => error switch
        {
            0 => OnlineWorldResultCode.Success,
            200105 => OnlineWorldResultCode.NotLoggedIn,
            200124 => OnlineWorldResultCode.NotInWorld,
            200125 => OnlineWorldResultCode.OperationInvalid,
            200126 => OnlineWorldResultCode.StateConflict,
            200117 => OnlineWorldResultCode.RoomLeaveFailed,
            200123 => OnlineWorldResultCode.ServiceUnavailable,
            200127 => OnlineWorldResultCode.StateConflict,
            200128 => OnlineWorldResultCode.OperationInvalid,
            200129 => OnlineWorldResultCode.OperationInvalid,
            200130 => OnlineWorldResultCode.OperationInvalid,
            200131 => OnlineWorldResultCode.StateConflict,
            200132 => OnlineWorldResultCode.ServiceUnavailable,
            _ => OnlineWorldResultCode.UnknownFailure
        };

        internal static OnlineWorldSnapshot Copy(ET.OnlineWorldSnapshot wire, bool connectionLost, OnlineWorldOperationState operation, OnlineWorldResultCode code)
        {
            return new OnlineWorldSnapshot(true, wire.WorldId, wire.MapId, wire.SelfUnitId, wire.WorldRevision,
                wire.Players?.Select(Copy).ToList() ?? new List<OnlineWorldPlayerSnapshot>(),wire.Enemies?.Select(Copy).ToList()??new List<OnlineWorldEnemySnapshot>(),wire.NavigationExecutorUnitId,wire.NavigationLeaseRevision,wire.ServerTime, connectionLost, operation, code,Copy(wire.SelfInventory),wire.Corpses?.Select(Copy).ToList()??new List<OnlineWorldCorpseSnapshot>(),wire.TemporaryBuildings?.Select(Copy).ToList()??new List<OnlineWorldTemporaryBuildingSnapshot>(),wire.Projectiles?.Select(p=>new OnlineWorldProjectileSnapshot(p)).ToList(),wire.CollisionContentHash,true);
        }

        internal static OnlineWorldPlayerSnapshot Copy(WorldPlayerInfo player)
        {
            return new OnlineWorldPlayerSnapshot(player.PlayerId, player.UnitId, player.DisplayName, player.WorldId, player.MapId,
                player.PlayerConfigId, new Vector3(player.PositionX, player.PositionY, player.PositionZ), player.YawDegrees,
                player.ConnectionState == 2 ? OnlineWorldConnectionState.DisconnectedGrace : OnlineWorldConnectionState.Online,
                player.StateRevision,player.CurrentHP,player.MaxHP,player.IsDead,player.RespawnAtServerTime,player.LastAcceptedAttackSequence,player.LifecycleRevision,player.TorsoItemId,player.LegsItemId,player.HeadItemId,player.HandsItemId,player.FeetItemId,player.BackItemId,player.ArmorItemId);
        }

        internal static OnlineWorldEnemySnapshot Copy(WorldEnemyInfo e)=>new(e.UnitId,e.WorldId,e.MapId,e.EnemyConfigId,new Vector3(e.SpawnPositionX,e.SpawnPositionY,e.SpawnPositionZ),e.SpawnYawDegrees,new Vector3(e.PositionX,e.PositionY,e.PositionZ),e.YawDegrees,e.TargetUnitId,e.CurrentHP,e.MaxHP,e.IsDead,e.RespawnAtServerTime,e.StateRevision,e.LifecycleRevision);
        internal static OnlineWorldTemporaryBuildingSnapshot Copy(WorldTemporaryBuildingInfo b)=>b==null?default:new OnlineWorldTemporaryBuildingSnapshot(b.RuntimeId,b.ConfigId,b.WorldId,b.MapId,new Vector3(b.PositionX,b.PositionY,b.PositionZ),b.YawDegrees,b.LoadedAmmo,b.NextShootAtServerTime,b.StateRevision);
        internal static OnlineInventorySnapshot Copy(OnlineInventoryInfo value)=>value==null?OnlineInventorySnapshot.Empty:new OnlineInventorySnapshot(value.Capacity,value.Slots?.Select(x=>new OnlineItemSlotSnapshot(x.SlotIndex,x.ItemId,x.Amount,x.LoadedAmmo)).ToList()??new List<OnlineItemSlotSnapshot>(),value.Equipment==null?default:value.Equipment.SchemaVersion!=1?throw new InvalidOperationException("联机库存协议不兼容"):new OnlineEquipmentSnapshot(value.Equipment.SourceSlotIndex,value.Equipment.ItemId,value.Equipment.RangedLoadedAmmo,(WeaponSlot)value.Equipment.ActiveSlot),value.Revision,value.TorsoItemId,value.LegsItemId,value.HeadItemId,value.HandsItemId,value.FeetItemId,value.BackItemId,value.ArmorItemId);
        internal static OnlineWorldCorpseSnapshot Copy(WorldCorpseInfo value)=>value==null?default:new OnlineWorldCorpseSnapshot(value.CorpseId,value.SourceKind,value.SourceUnitId,value.SourcePlayerId,value.SourceConfigId,value.WorldId,value.MapId,new Vector3(value.PositionX,value.PositionY,value.PositionZ),value.YawDegrees,value.ExpiresAtServerTime,value.HasContents,value.StateRevision);
        internal static OnlineWorldCorpseContentsSnapshot Copy(WorldCorpseContentsInfo value)=>new(Copy(value.Corpse),value.Capacity,value.Slots?.Select(x=>new OnlineItemSlotSnapshot(x.SlotIndex,x.ItemId,x.Amount,x.LoadedAmmo)).ToList()??new List<OnlineItemSlotSnapshot>());
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using QFramework;
using UnityEngine;

namespace Game
{
    public readonly struct MapLoadingStartedEvent
    {
        public MapLoadingStartedEvent(string mapId) { MapId = mapId; }
        public string MapId { get; }
    }

    /// <summary>实际正在执行的地图准备步骤；阶段不由百分比反推，也不写入存档。</summary>
    public enum MapLoadingStage
    {
        Scene, Layout, Resources, Terrain, Navigation, ValidateRoute, Shortcut,
        Points, GroundCover, RestoreShortcut, Players, ReleasePrevious, Activate
    }

    /// <summary>地图事务进入某一步时发布；同阶段重新进入表示重试，表现层重新计时。</summary>
    public readonly struct MapLoadingStageEvent
    {
        public MapLoadingStageEvent(string mapId,MapLoadingStage stage){MapId=mapId;Stage=stage;}
        public string MapId {get;}
        public MapLoadingStage Stage {get;}
    }

    public readonly struct MapLoadingProgressEvent
    {
        public MapLoadingProgressEvent(string mapId, float progress)
        {
            MapId = mapId;
            Progress = UnityEngine.Mathf.Clamp01(progress);
        }

        public string MapId { get; }
        public float Progress { get; }
    }

    public readonly struct MapLoadingCompletedEvent
    {
        public MapLoadingCompletedEvent(string mapId) { MapId = mapId; }
        public string MapId { get; }
    }

    public readonly struct MapCurrentReleasedEvent
    {
        public MapCurrentReleasedEvent(string mapId, long mapInstanceId)
        {
            MapId = mapId;
            MapInstanceId = mapInstanceId;
        }

        public string MapId { get; }
        public long MapInstanceId { get; }
    }

    public readonly struct MapLoadingFailedEvent
    {
        public MapLoadingFailedEvent(string mapId, string error)
        {
            MapId = mapId;
            Error = error;
        }

        public string MapId { get; }
        public string Error { get; }
    }

    public readonly struct MapSceneResult
    {
        public MapSceneResult(bool success, string error, MapLoadStatus status)
        {
            Success = success;
            Error = error;
            Status = status;
        }

        public bool Success { get; }
        public string Error { get; }
        public MapLoadStatus Status { get; }

        public static MapSceneResult Ok(MapLoadStatus status) => new MapSceneResult(true, null, status);
        public static MapSceneResult Fail(string error, MapLoadStatus status) =>
            new MapSceneResult(false, string.IsNullOrWhiteSpace(error) ? "地图事务失败" : error, status);
    }

    public sealed class MapSceneSystem : AbstractSystem
    {
        private readonly PlayerCtrl playerCtrl;
        /// <summary>当前地图显示期间的地形兼容开关；停用、卸载和架构关闭均恢复。</summary>
        private readonly TerrainRenderingCompatibility terrainRendering = new TerrainRenderingCompatibility();
        private readonly NpcCtrl npcCtrl;
        private readonly ProjectileCtrl projectileCtrl;
        private readonly EnemyCtrl enemyCtrl;
        private readonly LootCtrl lootCtrl;
        private readonly ExtractionCtrl extractionCtrl;
        private readonly OnlineWorldPlayerCtrl onlineWorldPlayerCtrl;
        private readonly OnlineWorldEnemyCtrl onlineWorldEnemyCtrl;
        private readonly OnlineWorldCorpseCtrl onlineWorldCorpseCtrl;
        private TemporaryBuildingCtrl TemporaryBuildingCtrl => this.GetUtility<ITemporaryBuildingViewPort>() as TemporaryBuildingCtrl;
        private bool busy;
        private CancellationTokenSource cancellation;
        private int generation;
        private long nextMapInstanceId = 1;
        /// <summary>街机战斗期间仅停用当前基地表现，不卸载地图，也不改变地图身份。</summary>
        private bool arcadeSuspended;
        private readonly List<PendingCleanupOwnership> pendingCleanups = new List<PendingCleanupOwnership>();

        private sealed class PendingCleanupOwnership
        {
            public PendingCleanupOwnership(
                List<IMapContentHandle> handles,
                bool isCommitted,
                string mapId,
                long mapInstanceId)
            {
                Handles = handles;
                IsCommitted = isCommitted;
                MapId = mapId;
                MapInstanceId = mapInstanceId;
            }

            public List<IMapContentHandle> Handles { get; set; }
            public bool IsCommitted { get; }
            public string MapId { get; }
            public long MapInstanceId { get; }
        }

        private sealed class CleanupAttemptResult
        {
            public CleanupAttemptResult(string error, List<IMapContentHandle> remaining)
            {
                Error = error;
                Remaining = remaining;
            }

            public string Error { get; }
            public List<IMapContentHandle> Remaining { get; }
            public bool Success => Remaining.Count == 0;
        }

        public MapSceneSystem(ProjectileCtrl projectileCtrl, PlayerCtrl playerCtrl, NpcCtrl npcCtrl, EnemyCtrl enemyCtrl, LootCtrl lootCtrl, ExtractionCtrl extractionCtrl, OnlineWorldPlayerCtrl onlineWorldPlayerCtrl = null,OnlineWorldEnemyCtrl onlineWorldEnemyCtrl=null,OnlineWorldCorpseCtrl onlineWorldCorpseCtrl=null)
        {
            this.projectileCtrl = projectileCtrl;
            this.playerCtrl = playerCtrl;
            this.npcCtrl = npcCtrl;
            this.enemyCtrl = enemyCtrl;
            this.lootCtrl = lootCtrl;
            this.extractionCtrl = extractionCtrl;
            this.onlineWorldPlayerCtrl = onlineWorldPlayerCtrl;
            this.onlineWorldEnemyCtrl=onlineWorldEnemyCtrl;
            this.onlineWorldCorpseCtrl=onlineWorldCorpseCtrl;
        }

        public OnlineWorldPlayerCtrl OnlineWorldPlayers => onlineWorldPlayerCtrl;
        public OnlineWorldEnemyCtrl OnlineWorldEnemies=>onlineWorldEnemyCtrl;
        public OnlineWorldCorpseCtrl OnlineWorldCorpses=>onlineWorldCorpseCtrl;
        public void ApplyOnlineLocalPlayerSnapshot(OnlineWorldSnapshot snapshot)=>playerCtrl.ApplyOnlineSnapshot(snapshot);

        public string CurrentMapId => this.GetModel<MapSceneModel>().CurrentMapId;
        public long CurrentMapInstanceId => TryGetCurrentMapInstanceId();

        /// <summary>
        /// 为独立街机战场挂起或恢复当前基地。操作幂等，失败时尽量回滚到调用前状态。
        /// </summary>
        public bool TrySetArcadeSuspended(bool suspended, out string error)
        {
            error = null;
            if (arcadeSuspended == suspended) return true;

            var map = this.GetModel<MapSceneModel>();
            if (busy || map.Status.Value != MapLoadStatus.Loaded || map.CurrentHandle == null ||
                !map.CurrentHandle.IsValid || map.CurrentHandle.Context == null)
            {
                error = "当前地图尚未稳定，不能切换街机战场";
                return false;
            }
            if (map.CurrentMode == MapPreparationMode.OnlinePlayersOnly ||
                !string.Equals(map.CurrentMapId, MapIds.Base, StringComparison.Ordinal))
            {
                error = "街机战场只能从离线基地进入";
                return false;
            }

            try
            {
                if (suspended)
                {
                    SetCurrentMapActiveReverse(false);
                    SetHandleContentActive(map.CurrentHandle, false);
                }
                else
                {
                    SetHandleContentActive(map.CurrentHandle, true);
                    SetCurrentMapActiveReverse(true);
                }
                arcadeSuspended = suspended;
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    SetHandleContentActive(map.CurrentHandle, suspended);
                    SetCurrentMapActiveReverse(suspended);
                }
                catch
                {
                    // 保留原始异常，调用者会停止街机进入流程，避免继续推进不一致状态。
                }
                error = $"切换街机战场失败：{exception.Message}";
                return false;
            }
        }

        /// <summary>唯一的运行世界有效性判断：地图句柄、上下文、活动玩家 Unit 与玩家端口必须一致。</summary>
        public bool HasRecoverableCurrentWorld()
        {
            var map = this.GetModel<MapSceneModel>();
            if (string.IsNullOrWhiteSpace(map.CurrentMapId) || map.CurrentHandle == null ||
                !map.CurrentHandle.IsValid || map.CurrentHandle.Context == null)
            {
                return false;
            }

            if (!this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player) ||
                !player.IsActive || player.MapInstanceId <= 0)
            {
                return false;
            }

            var provider = this.GetUtility<IPlayerRuntimePortProvider>();
            return provider != null && provider.TryGetCurrentPlayerMapLocalPose(out var pose, out _) &&
                   pose.IsActive && pose.UnitId == player.UnitId && pose.MapInstanceId == player.MapInstanceId;
        }

        public void CaptureExpeditionWorld(ExpeditionWorldSaveData world)
        {
            if (world == null) return;
            playerCtrl.CaptureExpeditionWorld(world);
            enemyCtrl.CaptureExpeditionWorld(world);
            lootCtrl.CaptureExpeditionWorld(world);
            world.SearchContainers.Clear();
            foreach (var record in this.GetModel<SearchContainerModel>().Enumerate())
            {
                var slots = new List<ItemSlotSaveData>();
                foreach (var slot in record.Container.Slots)
                {
                    slots.Add(new ItemSlotSaveData { Index = slot.Key, ItemId = slot.Value.ItemId, Amount = slot.Value.Amount,LoadedAmmo=slot.Value.LoadedAmmo,TorchRemainingSeconds=slot.Value.TorchRemainingSeconds,OriginExpeditionId=slot.Value.OriginExpeditionId,AcquiredAmount=slot.Value.AcquiredAmount,LoadedAcquiredAmount=slot.Value.LoadedAcquiredAmount });
                }
                world.SearchContainers.Add(new ExpeditionSearchContainerSaveData
                {
                    RuntimeId = record.ContainerId,
                    SourceEnemyRuntimeId = record.SourceEnemyUnitId,
                    Pose = new ExpeditionPoseData { X = record.Position.x, Y = record.Position.y, Z = record.Position.z },
                    Capacity = record.Container.Capacity,
                    Slots = slots
                });
            }
            world.TemporaryBuildings.Clear();
            foreach (var pair in this.GetModel<TemporaryBuildingModel>().Buildings)
            {
                var state = pair.Value;
                var building = new ExpeditionTemporaryBuildingSaveData
                {
                    RuntimeId = state.RuntimeId,
                    ConfigId = state.ConfigId,
                    UniformScale = state.UniformScale,
                    Pose = new ExpeditionPoseData { X = state.Position.x, Y = state.Position.y, Z = state.Position.z, RotationY = state.Rotation.eulerAngles.y }
                };
                if (this.GetModel<TemporaryBuildingModel>().ManualAttacks.TryGetValue(state.RuntimeId, out var attack))
                {
                    building.LoadedAmmo = attack.LoadedAmmo;
                }
                world.TemporaryBuildings.Add(building);
            }
        }

        /// <summary>捕获基地可持久的世界叶子；基地不保存副本玩家、敌人或搜索容器。</summary>
        public void CaptureBaseWorld(BaseWorldSaveData world)
        {
            if (world == null) return;
            world.CampLayoutInitialized = true;
            var expeditionShape = new ExpeditionWorldSaveData();
            lootCtrl.CaptureExpeditionWorld(expeditionShape);
            world.Loot = expeditionShape.Loot;
            world.TemporaryBuildings.Clear();
            foreach (var pair in this.GetModel<TemporaryBuildingModel>().Buildings)
            {
                var state = pair.Value;
                var building = new ExpeditionTemporaryBuildingSaveData
                {
                    RuntimeId = state.RuntimeId,
                    ConfigId = state.ConfigId,
                    UniformScale = state.UniformScale,
                    Pose = new ExpeditionPoseData { X = state.Position.x, Y = state.Position.y, Z = state.Position.z, RotationY = state.Rotation.eulerAngles.y },
                    LoadedAmmo = this.GetModel<TemporaryBuildingModel>().ManualAttacks.TryGetValue(state.RuntimeId, out var attack) ? attack.LoadedAmmo : 0
                };
                world.TemporaryBuildings.Add(building);
            }
        }

        protected override void OnInit()
        {
            cancellation = new CancellationTokenSource();
        }

        protected override void OnDeinit()
        {
            terrainRendering.Restore();
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            RollbackPreparedReverse();
        }

        /// <summary>加载普通地图并通过候选控制器事务提交新世界。</summary>
        public UniTask<MapSceneResult> LoadMapAsync(string mapId, CancellationToken cancellationToken = default)
        {
            return LoadMapInternal(mapId, null, null, cancellationToken);
        }

        public UniTask<MapSceneResult> LoadMapAsync(string mapId, ExpeditionSaveData expeditionSnapshot, CancellationToken cancellationToken = default)
        {
            return LoadMapInternal(mapId, expeditionSnapshot, null, cancellationToken);
        }

        /// <summary>新河谷入口：在同一地图事务内尝试有限候选；普通 LoadMap 保留固定布局兼容语义。</summary>
        public UniTask<MapSceneResult> LoadNewValleyMapAsync(ValleyGenerationProfile profile,int seed)
        {
            if(profile==null||!profile.TryValidate(out _))
                return UniTask.FromResult(MapSceneResult.Fail("河谷生成参数无效",this.GetModel<MapSceneModel>().Status.Value));
            return LoadMapInternal(MapIds.CivilisationValley,null,null,CancellationToken.None,null,MapPreparationMode.OfflineFull,profile,seed);
        }

        public UniTask<MapSceneResult> LoadBaseMapAsync(BaseSaveData baseSnapshot, CancellationToken cancellationToken = default)
        {
            if (baseSnapshot?.World == null) return UniTask.FromResult(MapSceneResult.Fail("基地快照无效", this.GetModel<MapSceneModel>().Status.Value));
            if (!baseSnapshot.World.CampLayoutInitialized)
            {
                var config = this.GetUtility<IRuntimeConfigProvider>();
                if (!config.TryGetBaseCampLayout(out var layout, out var error) ||
                    !BaseCampLayoutInitializer.TryInitialize(baseSnapshot.World, layout,
                        id => config.TryGetTempBuilding(id, out var definition, out _) ? definition : null, out error))
                    return UniTask.FromResult(MapSceneResult.Fail(error, this.GetModel<MapSceneModel>().Status.Value));
            }
            return LoadMapInternal(MapIds.Base, null, baseSnapshot, cancellationToken);
        }

        /// <summary>加载只提交正式玩家表现的街机地图，敌人和奖励仍由街机系统独立管理。</summary>
        public UniTask<MapSceneResult> LoadArcadeMapAsync(CancellationToken cancellationToken = default)
        {
            return LoadMapInternal(MapIds.Arcade, null, null, cancellationToken, null, MapPreparationMode.ArcadePlayerOnly);
        }

        public UniTask<MapSceneResult> LoadOnlineWorldMapAsync(OnlineWorldSnapshot world, CancellationToken cancellationToken = default)
        {
            if(world==null||!world.HasWorld)return UniTask.FromResult(MapSceneResult.Fail("World 快照无效",this.GetModel<MapSceneModel>().Status.Value));
            return LoadMapInternal(world.MapId,null,null,cancellationToken,world);
        }

        /// <summary>卸载当前已提交地图，并在失败时保留稳定世界状态。</summary>
        public UniTask<MapSceneResult> UnloadCurrentMapAsync(CancellationToken cancellationToken = default)
        {
            return UnloadCurrentInternal(cancellationToken);
        }

        private async UniTask<MapSceneResult> LoadMapInternal(
            string mapId,
            ExpeditionSaveData expeditionSnapshot,
            BaseSaveData baseSnapshot,
            CancellationToken externalCancellationToken,
            OnlineWorldSnapshot onlineWorld = null,
            MapPreparationMode requestedMode = MapPreparationMode.OfflineFull,
            ValleyGenerationProfile newValleyProfile = null,
            int valleySeed = 0)
        {
            var model = this.GetModel<MapSceneModel>();
            if (busy)
            {
                return MapSceneResult.Fail("地图系统正忙", model.Status.Value);
            }

            if (pendingCleanups.Count > 0)
            {
                return MapSceneResult.Fail(
                    $"地图仍有 {pendingCleanups.Count} 项待清理场景，必须先完成卸载",
                    model.Status.Value);
            }

            if (!MapCatalog.TryGet(mapId, out _))
            {
                return MapSceneResult.Fail($"MapCatalog 不支持地图：{mapId}", model.Status.Value);
            }

            // 基础资源由 Boot 保证；可选地图必须先由入口准备，底层加载不隐式联网下载。
            var resources = this.GetSystem<OptionalGameplayResourceSystem>();
            if (mapId != MapIds.Base && mapId != MapIds.CivilisationValley && resources != null &&
                resources.QueryMap(mapId).State != OptionalGameplayResourceState.Ready)
                return MapSceneResult.Fail("地图资源尚未就绪，请先完成资源下载", model.Status.Value);

            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (config == null)
            {
                return MapSceneResult.Fail("缺少运行时配置 Provider", model.Status.Value);
            }

            if (!config.TryGetMap(mapId, out var mapAsset, out var mapError))
            {
                return MapSceneResult.Fail(mapError ?? $"TbMap 缺少地图：{mapId}", model.Status.Value);
            }

            if (!config.TryGetMapSpawns(mapId, out var spawns, out var spawnError))
            {
                return MapSceneResult.Fail(spawnError, model.Status.Value);
            }
            var valleyProfile=newValleyProfile;
            CivilisationCatalog valleyTemplates=null;
            if(expeditionSnapshot?.ValleyLayout!=null||newValleyProfile!=null)
            {
                if(mapId!=MapIds.CivilisationValley||requestedMode!=MapPreparationMode.OfflineFull||
                    (valleyProfile==null&&!config.TryGetValleyGenerationProfile(mapId,out valleyProfile,out _))||
                    !config.TryGetCivilisationCatalog(out valleyTemplates,out _))
                    return MapSceneResult.Fail("河谷生成配置或模式无效",model.Status.Value);
                if(expeditionSnapshot?.ValleyLayout!=null&&!ValleyLayoutValidator.TryValidate(expeditionSnapshot.ValleyLayout,valleyProfile,valleyTemplates,out var layoutError))
                    return MapSceneResult.Fail(layoutError,model.Status.Value);
                if(expeditionSnapshot?.ValleyLayout!=null)
                {
                    if(expeditionSnapshot.Sections==null)return MapSceneResult.Fail("副本探索分区缺失",model.Status.Value);
                    expeditionSnapshot.Sections.TryGetValue(CivilisationExpeditionModel.Section,out var savedExploration);
                    if(!ValleyLayoutValidator.TryValidateExploration(expeditionSnapshot.ValleyLayout,savedExploration,out var identityError))return MapSceneResult.Fail(identityError,model.Status.Value);
                }
            }

            busy = true;
            var requestGeneration = ++generation;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, externalCancellationToken);
            var token = linked.Token;
            var oldHandle = model.CurrentHandle;
            var oldMapId = model.CurrentMapId;
            var oldMode = model.CurrentMode;
            var oldMapInstanceId = TryGetCurrentMapInstanceId();
            var oldState = this.GetModel<AppStateModel>().CurrentState.Value;
            var canRestoreOld = oldHandle != null && oldHandle.IsValid;
            var oldReleasePublished = false;
            IMapContentHandle candidateHandle = null;
            MapPreparationContext context = null;
            var preparedCount = 0;
            var committedCount = 0;
            // 请求内单调推进，过期回调不得影响下一张地图；只有事务完成才允许100%。
            var reportedProgress = 0f;
            void ReportProgress(float value)
            {
                if (token.IsCancellationRequested || requestGeneration != generation || float.IsNaN(value) || float.IsInfinity(value)) return;
                value = Mathf.Clamp01(value);
                if (value <= reportedProgress) return;
                reportedProgress = value;
                this.SendEvent(new MapLoadingProgressEvent(mapId, value));
            }

            void ReportStage(MapLoadingStage stage)
            {
                if (!token.IsCancellationRequested && requestGeneration == generation)
                    this.SendEvent(new MapLoadingStageEvent(mapId,stage));
            }

            // 真实河谷进入同步重步骤前先让界面绘制阶段，避免卡住时仍显示上一步。
            async UniTask EnterStageAsync(MapLoadingStage stage)
            {
                ReportStage(stage);
                if(valleyProfile!=null&&this.GetUtility<IValleyMapBuilder>() is IValleyMapProgressBuilder)
                {
                    await UniTask.Yield();token.ThrowIfCancellationRequested();
                    if(requestGeneration!=generation)throw new OperationCanceledException();
                }
            }

            model.SetStatus(MapLoadStatus.Loading);
            this.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            this.SendEvent(new MapLoadingStartedEvent(mapId));

            try
            {
                ReportStage(MapLoadingStage.Scene);
                var load = await LoadConfiguredMapAsync(
                    mapId,
                    mapAsset,
                    progress => ReportProgress(Mathf.Clamp01(progress) * (valleyProfile != null ? .25f : .85f)),
                    token);
                candidateHandle = load.Handle;
                if (!load.Success || candidateHandle == null || !candidateHandle.IsValid || candidateHandle.Context == null)
                {
                    var error = load.Error ?? "候选地图加载失败";
                    error = await TryUnloadForCleanup(candidateHandle, error, "候选地图卸载失败");
                    RestoreModelAfterLoadFailure(model, oldMapId, oldHandle, oldMode, oldState, canRestoreOld);
                    this.SendEvent(new MapLoadingFailedEvent(mapId, error));
                    return MapSceneResult.Fail(error, model.Status.Value);
                }

                if(valleyProfile!=null)
                {
                    var builder=this.GetUtility<IValleyMapBuilder>();
                    if(builder==null)throw new InvalidOperationException("缺少河谷候选构建器");
                    MapContentLoadResult generated=null;
                    var savedLayout=expeditionSnapshot?.ValleyLayout;
                    for(int attempt=0;attempt<(savedLayout!=null?1:valleyProfile.MaxAttempts);attempt++)
                    {
                        token.ThrowIfCancellationRequested();
                        await EnterStageAsync(MapLoadingStage.Layout);
                        var layout=savedLayout?.Clone();
                        if(layout?.GenerationVersion==3 && !layout.EnemySpawnsPrepared)throw new InvalidOperationException("保存的河谷缺少初始敌人清单，原文件保留");
                        if(layout==null&&!ValleyLayoutGenerator.TryGenerate(valleySeed,attempt,valleyProfile,valleyTemplates,out layout,out var generateError))
                        {generated=MapContentLoadResult.Fail(generateError);continue;}
                        var candidateSpawns=ValleyChapterSpawns.Resolve(layout,spawns).ToArray();
                        generated=builder is IValleyMapProgressBuilder progressive
                            ? await progressive.BuildWithStagesAsync(candidateHandle,layout,valleyProfile,token,p=>ReportProgress(.25f+Mathf.Clamp01(p)*.6f),ReportStage)
                            : await builder.BuildAsync(candidateHandle,layout,valleyProfile,token);
                        if(token.IsCancellationRequested||requestGeneration!=generation)
                        {
                            if(generated.Handle is GeneratedValleyContentHandle expired)await expired.ReleaseGeneratedAsync();
                            throw new OperationCanceledException("河谷请求已取消或过期");
                        }
                        if(generated.Success&&generated.Handle is GeneratedValleyContentHandle valid&&valid.IsValid)
                        {
                            candidateHandle=valid;
                            valid.Points=layout.Points.Select(p=>
                            {
                                var t=valleyTemplates.Points.Single(v=>v.Id==p.TemplateId);
                                return new CivilisationSearchPointDefinition(p.Id,mapId,t.TechnologyId,t.ItemId,t.Amount,t.Seconds,p.Pose.ToVector(),t.NameKey,t.RequiredWeaponItemId,t.FellTree,t.Repeatable,t.ChapterId);
                            }).ToArray();
                            var spawn=layout.Spawn;
                            spawns=candidateSpawns.Select(s=>s.ObjectType==MapSpawnObjectType.Player?new MapSpawnDefinition(s.Id,s.MapId,s.ObjectType,s.ConfigId,spawn.X,spawn.Y,spawn.Z,spawn.Yaw):s).ToArray();
                            if(expeditionSnapshot?.World!=null)
                            {
                                foreach(var pose in expeditionSnapshot.World.Players.Select(p=>p.Pose).Concat(expeditionSnapshot.World.TemporaryBuildings.Select(b=>b.Pose)))
                                    if(pose==null||Mathf.Abs(pose.X)>512||Mathf.Abs(pose.Z)>512||Mathf.Abs(pose.Y-layout.HeightAt(pose.X,pose.Z))>4)
                                        throw new InvalidOperationException("保存的玩家或木棚位置不属于河谷可用地表");
                            }
                            break;
                        }
                        if(generated.Handle is GeneratedValleyContentHandle failed)await failed.ReleaseGeneratedAsync();
                    }
                    if(candidateHandle is not GeneratedValleyContentHandle)throw new InvalidOperationException(generated?.Error??"河谷候选全部失败");
                }

                context = onlineWorld != null
                    ? new MapPreparationContext(mapId,nextMapInstanceId++,candidateHandle.Context,spawns,onlineWorld)
                    : requestedMode == MapPreparationMode.ArcadePlayerOnly
                        ? MapPreparationContext.CreateArcade(mapId, nextMapInstanceId++, candidateHandle.Context, spawns)
                        : baseSnapshot != null
                            ? new MapPreparationContext(mapId, nextMapInstanceId++, candidateHandle.Context, spawns, baseSnapshot)
                            : new MapPreparationContext(mapId, nextMapInstanceId++, candidateHandle.Context, spawns, expeditionSnapshot);
                if(candidateHandle is GeneratedValleyContentHandle generatedCandidate)
                {
                    context.ValleyLayout=generatedCandidate.Layout;context.CivilisationPoints=generatedCandidate.Points;
                    if(expeditionSnapshot?.Sections.TryGetValue(CivilisationExpeditionModel.Section,out var exploration)==true &&
                        exploration?["shortcuts"] is Newtonsoft.Json.Linq.JArray shortcuts && shortcuts.Count>0)
                    {
                        if(shortcuts.Count!=1||(string)shortcuts[0]!=generatedCandidate.Layout.ShortcutId)
                            throw new InvalidOperationException("保存的捷径不属于当前河谷");
                        ReportStage(MapLoadingStage.RestoreShortcut);
                        var restored=await this.GetUtility<IValleyMapBuilder>().SetShortcutOpenAsync(generatedCandidate,generatedCandidate.Layout.ShortcutId,true,token);
                        if(!restored.Success)throw new InvalidOperationException(restored.Error);
                        token.ThrowIfCancellationRequested();
                        if(requestGeneration!=generation)throw new OperationCanceledException();
                    }
                }
                await EnterStageAsync(MapLoadingStage.Players);
                ReportProgress(.85f);
                var prepare = await PrepareControllers(context, token);
                preparedCount = prepare.PreparedCount;
                if (!prepare.Result.Success || token.IsCancellationRequested || requestGeneration != generation)
                {
                    var error = prepare.Result.Success ? "地图加载已取消或请求已过期" : prepare.Result.Error;
                    RollbackPreparedReverse(preparedCount, context);
                    error = await TryUnloadForCleanup(candidateHandle, error, "候选地图卸载失败");
                    RestoreModelAfterLoadFailure(model, oldMapId, oldHandle, oldMode, oldState, canRestoreOld);
                    this.SendEvent(new MapLoadingFailedEvent(mapId, error));
                    return MapSceneResult.Fail(error, model.Status.Value);
                }

                ReportStage(MapLoadingStage.ReleasePrevious);
                ReportProgress(.95f);
                if (canRestoreOld)
                {
                    if (!TryDeactivateOld(out var deactivateError))
                    {
                        var restored = TryRestoreOld(oldHandle, oldState, out var restoreError);
                        var error = AppendCleanupError(deactivateError, restoreError);
                        RollbackPreparedReverse(preparedCount, context);
                        if (!restored)
                        {
                            error = await CleanupFailed(model, oldHandle, candidateHandle, error, oldMapId, oldMapInstanceId, oldReleasePublished);
                        }
                        else
                        {
                            error = await TryUnloadForCleanup(candidateHandle, error, "候选地图卸载失败");
                            model.SetLoaded(oldMapId, oldHandle, oldMode);
                        }

                        this.SendEvent(new MapLoadingFailedEvent(mapId, error));
                        return MapSceneResult.Fail(error, model.Status.Value);
                    }

                    var unloadOld = await UnloadHandleAsync(oldHandle, token);
                    if (!unloadOld.Success)
                    {
                        if (oldHandle is MapContentGroupHandle)
                        {
                            var groupUnloadError = $"卸载旧地图失败：{unloadOld.Error}";
                            RollbackPreparedReverse(preparedCount, context);
                            groupUnloadError = await TryUnloadForCleanup(candidateHandle, groupUnloadError, "候选地图卸载失败");
                            HoldPendingCurrentMapCleanup(model, oldMapId, oldHandle, oldMapInstanceId);
                            this.SendEvent(new MapLoadingFailedEvent(mapId, groupUnloadError));
                            return MapSceneResult.Fail(groupUnloadError, model.Status.Value);
                        }

                        var restored = TryRestoreOld(oldHandle, oldState, out var restoreError);
                        var error = AppendCleanupError($"卸载旧地图失败：{unloadOld.Error}", restoreError);
                        RollbackPreparedReverse(preparedCount, context);
                        if (!restored)
                        {
                            error = await CleanupFailed(model, oldHandle, candidateHandle, error, oldMapId, oldMapInstanceId, oldReleasePublished);
                        }
                        else
                        {
                            error = await TryUnloadForCleanup(candidateHandle, error, "候选地图卸载失败");
                            model.SetLoaded(oldMapId, oldHandle, oldMode);
                        }

                        this.SendEvent(new MapLoadingFailedEvent(mapId, error));
                        return MapSceneResult.Fail(error, model.Status.Value);
                    }

                    canRestoreOld = false;
                    this.GetSystem<AudioSystem>().StopMapOwnedSounds();
                    ReleaseCurrentMapReverse(oldMapInstanceId);
                    this.SendEvent(new MapCurrentReleasedEvent(oldMapId, oldMapInstanceId));
                    oldReleasePublished = true;
                }

                await EnterStageAsync(MapLoadingStage.Activate);
                ReportProgress(.98f);
                var commit = CommitPreparedForward(context, out committedCount);
                if (!commit.Success)
                {
                    ReleaseCommittedCandidateReverse(committedCount, context?.MapInstanceId ?? 0, context);
                    RollbackPreparedReverse(preparedCount, context);
                    var error = await TryUnloadForCleanup(candidateHandle, commit.Error, "候选地图卸载失败");
                    model.Clear(MapLoadStatus.Failed);
                    this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                    this.SendEvent(new MapLoadingFailedEvent(mapId, error));
                    return MapSceneResult.Fail(error, model.Status.Value);
                }

                preparedCount = 0;
                SetHandleContentActive(candidateHandle, true);
                model.SetLoaded(mapId, candidateHandle, context.Mode);
                this.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
                this.SendEvent(new MapLoadingCompletedEvent(mapId));
                ReportProgress(1f);
                return MapSceneResult.Ok(model.Status.Value);
            }
            catch (Exception e)
            {
                var error = $"地图加载异常：{e.Message}";
                ReleaseCommittedCandidateReverse(committedCount, context?.MapInstanceId ?? 0, context);
                RollbackPreparedReverse(preparedCount, context);
                error = await TryUnloadForCleanup(candidateHandle, error, "候选地图卸载失败");
                if (canRestoreOld && TryRestoreOld(oldHandle, oldState, out var restoreError))
                {
                    error = AppendCleanupError(error, restoreError);
                    model.SetLoaded(oldMapId, oldHandle, oldMode);
                    this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(oldState);
                }
                else
                {
                    error = await CleanupFailed(model, oldHandle, null, error, oldMapId, oldMapInstanceId, oldReleasePublished);
                }

                this.SendEvent(new MapLoadingFailedEvent(mapId, error));
                return MapSceneResult.Fail(error, model.Status.Value);
            }
            finally
            {
                busy = false;
            }
        }

        private async UniTask<MapSceneResult> UnloadCurrentInternal(CancellationToken externalCancellationToken)
        {
            var model = this.GetModel<MapSceneModel>();
            if (busy)
            {
                return MapSceneResult.Fail("地图系统正忙", model.Status.Value);
            }

            if (pendingCleanups.Count > 0)
            {
                return await RetryPendingCleanupsAsync();
            }

            if (model.CurrentHandle == null)
            {
                RollbackPreparedReverse();
                model.Clear();
                return MapSceneResult.Ok(model.Status.Value);
            }

            busy = true;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, externalCancellationToken);
            var token = linked.Token;
            var oldMapId = model.CurrentMapId;
            var oldMode = model.CurrentMode;
            var oldHandle = model.CurrentHandle;
            var oldState = this.GetModel<AppStateModel>().CurrentState.Value;
            var oldMapInstanceId = TryGetCurrentMapInstanceId();
            var oldReleasePublished = false;
            model.SetStatus(MapLoadStatus.Unloading);
            this.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);

            try
            {
                TryDeactivateOld();
                var unload = await UnloadHandleAsync(oldHandle, token);
                if (!unload.Success)
                {
                    if (oldHandle is MapContentGroupHandle)
                    {
                        HoldPendingCurrentMapCleanup(model, oldMapId, oldHandle, oldMapInstanceId);
                        return MapSceneResult.Fail($"地图卸载失败：{unload.Error}", model.Status.Value);
                    }

                    var restored = TryRestoreOld(oldHandle, oldState, out var restoreError);
                    var error = AppendCleanupError($"地图卸载失败：{unload.Error}", restoreError);
                    if (restored)
                    {
                        model.SetLoaded(oldMapId, oldHandle, oldMode);
                    }
                    else
                    {
                        error = await CleanupFailed(model, oldHandle, null, error, oldMapId, oldMapInstanceId, oldReleasePublished);
                    }

                    return MapSceneResult.Fail(error, model.Status.Value);
                }

                ReleaseCurrentMapReverse(oldMapInstanceId);
                this.SendEvent(new MapCurrentReleasedEvent(oldMapId, oldMapInstanceId));
                oldReleasePublished = true;
                this.GetSystem<AudioSystem>().StopMapOwnedSounds();
                model.Clear();
                this.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
                return MapSceneResult.Ok(model.Status.Value);
            }
            catch (Exception e)
            {
                var restored = TryRestoreOld(oldHandle, oldState, out var restoreError);
                var error = AppendCleanupError($"地图卸载异常：{e.Message}", restoreError);
                if (restored)
                {
                    model.SetLoaded(oldMapId, oldHandle, oldMode);
                }
                else
                {
                    error = await CleanupFailed(model, oldHandle, null, error, oldMapId, oldMapInstanceId, oldReleasePublished);
                }

                return MapSceneResult.Fail(error, model.Status.Value);
            }
            finally
            {
                busy = false;
            }
        }

        private async UniTask<(MapControllerResult Result, int PreparedCount)> PrepareControllers(MapPreparationContext context, CancellationToken token)
        {
            if (context.Mode == MapPreparationMode.ArcadePlayerOnly)
            {
                var arcadeProjectiles = await projectileCtrl.PrepareMapAsync(context, token);
                if (!arcadeProjectiles.Success) return (arcadeProjectiles, 0);
                var arcadePlayer = await playerCtrl.PrepareMapAsync(context, token);
                if (!arcadePlayer.Success) return (arcadePlayer, 1);
                var arcadeEnemies = await enemyCtrl.PrepareMapAsync(context, token);
                if (!arcadeEnemies.Success) return (arcadeEnemies, 2);
                var arcadeLoot = await lootCtrl.PrepareMapAsync(context, token);
                return arcadeLoot.Success ? (MapControllerResult.Ok(), 4) : (arcadeLoot, 3);
            }

            if(context.Mode==MapPreparationMode.OnlinePlayersOnly)
            {
                if(onlineWorldPlayerCtrl==null||onlineWorldEnemyCtrl==null||onlineWorldCorpseCtrl==null)return(MapControllerResult.Fail("联机地图控制器不可用"),0);
                var onlinePlayer=await playerCtrl.PrepareMapAsync(context,token);if(!onlinePlayer.Success)return(onlinePlayer,0);
                var remotes=await onlineWorldPlayerCtrl.PrepareMapAsync(context,token);if(!remotes.Success)return(remotes,1);
                var enemies=await onlineWorldEnemyCtrl.PrepareMapAsync(context,token);if(!enemies.Success)return(enemies,2);
                var corpses=await onlineWorldCorpseCtrl.PrepareMapAsync(context,token);if(!corpses.Success)return(corpses,3);
                var temporary=TemporaryBuildingCtrl?.PrepareMap(context)??(context.OnlineWorld.TemporaryBuildings.Count>0?MapControllerResult.Fail("联机临时建筑控制器不可用"):MapControllerResult.Ok());if(!temporary.Success)return(temporary,4);
                if(!string.IsNullOrEmpty(context.OnlineWorld.CollisionContentHash))
                {
                    var arrows=await projectileCtrl.PrepareMapAsync(context,token);if(!arrows.Success)return(arrows,5);
                    return(MapControllerResult.Ok(),6);
                }
                return(MapControllerResult.Ok(),5);
            }
            var result = await projectileCtrl.PrepareMapAsync(context, token);
            if (!result.Success) return (result, 0);
            result = await playerCtrl.PrepareMapAsync(context, token);
            if (!result.Success) return (result, 1);
            if (context.ExpeditionSnapshot?.World?.SearchContainers != null)
            {
                var searchSystem = this.GetSystem<SearchContainerSystem>();
                foreach (var tokenSnapshot in context.ExpeditionSnapshot.World.SearchContainers)
                {
                    if (!searchSystem.TryPrepareSnapshot(tokenSnapshot, context.MapInstanceId, out var candidate, out var searchError))
                    {
                        return (MapControllerResult.Fail(searchError), 2);
                    }
                    context.SearchContainerCandidates.Add(candidate);
                }
            }
            result = await npcCtrl.PrepareMapAsync(context, token);
            if (!result.Success) return (result, 2);
            result = await enemyCtrl.PrepareMapAsync(context, token);
            if (!result.Success) return (result, 3);
            result = await lootCtrl.PrepareMapAsync(context, token);
            if (!result.Success) return (result, 4);
            result = await extractionCtrl.PrepareMapAsync(context, token);
            if (!result.Success) return (result, 5);
            var temporaryBuildingCtrl = TemporaryBuildingCtrl;
            if (temporaryBuildingCtrl == null)
            {
                if ((context.ExpeditionSnapshot?.World?.TemporaryBuildings?.Count ?? 0) > 0 ||
                    (context.BaseSnapshot?.World?.TemporaryBuildings?.Count ?? 0) > 0)
                {
                    return (MapControllerResult.Fail("临时建筑准备失败：候选控制器不可用"), 6);
                }
                return (MapControllerResult.Ok(), 6);
            }
            result = temporaryBuildingCtrl.PrepareMap(context);
            if (!result.Success) return (result, 6);
            return (MapControllerResult.Ok(), 7);
        }

        private MapControllerResult CommitPreparedForward(MapPreparationContext context, out int committedCount)
        {
            committedCount = 0;
            if (context.Mode == MapPreparationMode.ArcadePlayerOnly)
            {
                var arcadeProjectiles = projectileCtrl.CommitPreparedMap();
                if (!arcadeProjectiles.Success) return arcadeProjectiles;
                committedCount++;
                var arcadePlayer = playerCtrl.CommitPreparedMap();
                if (!arcadePlayer.Success) return arcadePlayer;
                committedCount++;
                var arcadeEnemies = enemyCtrl.CommitPreparedMap();
                if (!arcadeEnemies.Success) return arcadeEnemies;
                committedCount++;
                var arcadeLoot = lootCtrl.CommitPreparedMap();
                if (!arcadeLoot.Success) return arcadeLoot;
                committedCount++;
                return MapControllerResult.Ok();
            }

            if(context.Mode==MapPreparationMode.OnlinePlayersOnly)
            {
                var local=playerCtrl.CommitPreparedMap();if(!local.Success)return local;committedCount++;
                var remote=onlineWorldPlayerCtrl.CommitPreparedMap();if(!remote.Success)return remote;committedCount++;
                var enemies=onlineWorldEnemyCtrl.CommitPreparedMap();if(!enemies.Success)return enemies;committedCount++;
                var corpses=onlineWorldCorpseCtrl.CommitPreparedMap();if(!corpses.Success)return corpses;committedCount++;
                var temporary=TemporaryBuildingCtrl?.CommitPreparedMap()??MapControllerResult.Ok();if(!temporary.Success)return temporary;committedCount++;
                if(!string.IsNullOrEmpty(context.OnlineWorld.CollisionContentHash)){var arrows=projectileCtrl.CommitPreparedMap();if(!arrows.Success)return arrows;committedCount++;}
                return MapControllerResult.Ok();
            }
            var result = projectileCtrl.CommitPreparedMap();
            if (!result.Success) return result;
            committedCount++;
            result = playerCtrl.CommitPreparedMap();
            if (!result.Success) return result;
            committedCount++;
            result = npcCtrl.CommitPreparedMap();
            if (!result.Success) return result;
            committedCount++;
            result = enemyCtrl.CommitPreparedMap();
            if (!result.Success) return result;
            committedCount++;
            result = lootCtrl.CommitPreparedMap();
            if (!result.Success) return result;
            committedCount++;
            result = extractionCtrl.CommitPreparedMap();
            if (!result.Success) return result;
            committedCount++;
            var temporaryBuildingCtrl = TemporaryBuildingCtrl;
            if (temporaryBuildingCtrl != null)
            {
                result = temporaryBuildingCtrl.CommitPreparedMap();
                if (!result.Success) return result;
                committedCount++;
            }
            if (!this.GetSystem<SearchContainerSystem>().CommitPrepared(context.SearchContainerCandidates, out var searchError))
            {
                return MapControllerResult.Fail(searchError);
            }
            return MapControllerResult.Ok();
        }

        private void RollbackPreparedReverse(int preparedCount = 7, MapPreparationContext context = null)
        {
            if (context?.Mode == MapPreparationMode.ArcadePlayerOnly)
            {
                if (preparedCount >= 4) lootCtrl.RollbackPreparedMap();
                if (preparedCount >= 3) enemyCtrl.RollbackPreparedMap();
                if (preparedCount >= 2) playerCtrl.RollbackPreparedMap();
                if (preparedCount >= 1) projectileCtrl.RollbackPreparedMap();
                return;
            }

            if(context?.Mode==MapPreparationMode.OnlinePlayersOnly){if(preparedCount>=6)projectileCtrl.RollbackPreparedMap();if(preparedCount>=5)TemporaryBuildingCtrl?.RollbackPreparedMap();if(preparedCount>=4)onlineWorldCorpseCtrl.RollbackPreparedMap();if(preparedCount>=3)onlineWorldEnemyCtrl.RollbackPreparedMap();if(preparedCount>=2)onlineWorldPlayerCtrl.RollbackPreparedMap();if(preparedCount>=1)playerCtrl.RollbackPreparedMap();return;}
            if (preparedCount >= 7) TemporaryBuildingCtrl?.RollbackPreparedMap();
            if (preparedCount >= 6) extractionCtrl.RollbackPreparedMap();
            if (preparedCount >= 5) lootCtrl.RollbackPreparedMap();
            if (preparedCount >= 4) enemyCtrl.RollbackPreparedMap();
            if (preparedCount >= 3) npcCtrl.RollbackPreparedMap();
            if (preparedCount >= 2) playerCtrl.RollbackPreparedMap();
            if (preparedCount >= 1) projectileCtrl.RollbackPreparedMap();
        }

        private void SetCurrentMapActiveReverse(bool active)
        {
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.ArcadePlayerOnly)
            {
                lootCtrl.SetCurrentMapActive(active);
                enemyCtrl.SetCurrentMapActive(active);
                playerCtrl.SetCurrentMapActive(active);
                projectileCtrl.SetCurrentMapActive(active);
                return;
            }

            if(this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly){projectileCtrl.SetCurrentMapActive(active);onlineWorldCorpseCtrl?.SetCurrentMapActive(active);onlineWorldEnemyCtrl?.SetCurrentMapActive(active);onlineWorldPlayerCtrl?.ApplySnapshot(this.GetSystem<OnlineWorldSystem>()?.Snapshot);playerCtrl.SetCurrentMapActive(active);return;}
            extractionCtrl.SetCurrentMapActive(active);
            lootCtrl.SetCurrentMapActive(active);
            enemyCtrl.SetCurrentMapActive(active);
            npcCtrl.SetCurrentMapActive(active);
            playerCtrl.SetCurrentMapActive(active);
            projectileCtrl.SetCurrentMapActive(active);
        }

        private void ReleaseCurrentMapReverse(long mapInstanceId)
        {
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.ArcadePlayerOnly)
            {
                lootCtrl.ReleaseCurrentMap();
                enemyCtrl.ReleaseCurrentMap();
                playerCtrl.ReleaseCurrentMap();
                projectileCtrl.ReleaseCurrentMap();
                return;
            }

            if(this.GetModel<MapSceneModel>().CurrentMode==MapPreparationMode.OnlinePlayersOnly){projectileCtrl.ReleaseCurrentMap();TemporaryBuildingCtrl?.ReleaseMap(mapInstanceId);onlineWorldCorpseCtrl?.ReleaseCurrentMap();onlineWorldEnemyCtrl?.ReleaseCurrentMap();onlineWorldPlayerCtrl.ReleaseCurrentMap();playerCtrl.ReleaseCurrentMap();return;}
            TemporaryBuildingCtrl?.ReleaseMap(mapInstanceId);
            this.GetSystem<SearchContainerSystem>().ReleaseMap(mapInstanceId);
            extractionCtrl.ReleaseCurrentMap();
            lootCtrl.ReleaseCurrentMap();
            enemyCtrl.ReleaseCurrentMap();
            npcCtrl.ReleaseCurrentMap();
            playerCtrl.ReleaseCurrentMap();
            projectileCtrl.ReleaseCurrentMap();
        }

        private void ReleaseCommittedCandidateReverse(int committedCount, long mapInstanceId, MapPreparationContext context = null)
        {
            if (context?.Mode == MapPreparationMode.ArcadePlayerOnly)
            {
                if (committedCount >= 4) lootCtrl.ReleaseCurrentMap();
                if (committedCount >= 3) enemyCtrl.ReleaseCurrentMap();
                if (committedCount >= 2) playerCtrl.ReleaseCurrentMap();
                if (committedCount >= 1) projectileCtrl.ReleaseCurrentMap();
                return;
            }

            if(context?.Mode==MapPreparationMode.OnlinePlayersOnly){if(committedCount>=6)projectileCtrl.ReleaseCurrentMap();if(committedCount>=5)TemporaryBuildingCtrl?.ReleaseMap(mapInstanceId);if(committedCount>=4)onlineWorldCorpseCtrl.ReleaseCurrentMap();if(committedCount>=3)onlineWorldEnemyCtrl.ReleaseCurrentMap();if(committedCount>=2)onlineWorldPlayerCtrl.ReleaseCurrentMap();if(committedCount>=1)playerCtrl.ReleaseCurrentMap();return;}
            if (committedCount >= 7) TemporaryBuildingCtrl?.ReleaseMap(mapInstanceId);
            if (committedCount >= 6) extractionCtrl.ReleaseCurrentMap();
            if (committedCount >= 5) lootCtrl.ReleaseCurrentMap();
            if (committedCount >= 4) enemyCtrl.ReleaseCurrentMap();
            if (committedCount >= 3) npcCtrl.ReleaseCurrentMap();
            if (committedCount >= 2) playerCtrl.ReleaseCurrentMap();
            if (committedCount >= 1) projectileCtrl.ReleaseCurrentMap();
        }

        private async UniTask<MapContentLoadResult> LoadConfiguredMapAsync(
            string mapId,
            MapAssetDefinition mapAsset,
            Action<float> onProgress,
            CancellationToken token)
        {
            var addresses = mapAsset.SceneAddresses;
            if (addresses.Count == 1)
            {
                return await this.GetUtility<IMapContentLoader>()
                    .LoadAsync(mapId, addresses[0], onProgress, token);
            }

            var owned = new List<IMapContentHandle>(addresses.Count);
            try
            {
                for (var index = 0; index < addresses.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var completed = index;
                    var result = await this.GetUtility<IMapContentLoader>().LoadAsync(
                        mapId,
                        addresses[index],
                        current =>
                        {
                            var total = (completed + Mathf.Clamp01(current)) / addresses.Count;
                            onProgress?.Invoke(Mathf.Min(total, 0.999999f));
                        },
                        token);

                    if (result.Handle != null) owned.Add(result.Handle);
                    if (!result.Success || result.Handle == null || !result.Handle.IsValid || result.Handle.Context == null)
                    {
                        var error = result.Error ?? $"候选地图第 {index + 1} 个场景加载失败：{addresses[index]}";
                        error = await UnloadOwnedReverseAsync(owned, error, "部分地图清理失败");
                        return MapContentLoadResult.Fail(error);
                    }

                    if (index + 1 < addresses.Count) onProgress?.Invoke((float)(index + 1) / addresses.Count);
                }

                var group = new MapContentGroupHandle(owned);
                if (!TryValidateAndPlaceTerrainGroup(group, out var validationError))
                {
                    validationError = await UnloadOwnedReverseAsync(owned, validationError, "结构失败清理失败");
                    return MapContentLoadResult.Fail(validationError);
                }

                onProgress?.Invoke(1f);
                return MapContentLoadResult.Ok(group);
            }
            catch (Exception exception)
            {
                var error = exception is OperationCanceledException
                    ? "地图加载已取消"
                    : $"地图组加载异常：{exception.Message}";
                error = await UnloadOwnedReverseAsync(owned, error, "地图组异常清理失败");
                return MapContentLoadResult.Fail(error);
            }
        }

        private static bool TryValidateAndPlaceTerrainGroup(MapContentGroupHandle group, out string error)
        {
            var tiles = new List<GeneratedTerrainTile>(group.Children.Count);
            foreach (var child in group.Children)
            {
                var context = child.Context;
                var found = context?.MapContentRoot?.GetComponentsInChildren<GeneratedTerrainTile>(true);
                if (found == null || found.Length != 1)
                {
                    error = $"多场景地图的每个 MapContext 必须恰好包含一个 GeneratedTerrainTile：{child.MapId}";
                    return false;
                }
                tiles.Add(found[0]);
            }

            var first = tiles[0];
            if (string.IsNullOrWhiteSpace(first.MapName) || first.Row != 1 || first.Column != 1 ||
                first.TotalRows <= 0 || first.TotalColumns <= 0 || first.TileWorldSize <= 0f ||
                first.HeightmapResolution <= 0)
            {
                error = "多场景地图主 Terrain 元数据无效或首场景不是 row=1,column=1";
                return false;
            }

            if (tiles.Count != first.TotalRows * first.TotalColumns)
            {
                error = $"多场景地图块数量不完整：actual={tiles.Count} expected={first.TotalRows * first.TotalColumns}";
                return false;
            }

            var grid = new Dictionary<(int Row, int Column), GeneratedTerrainTile>();
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                if (!string.Equals(tile.MapName, first.MapName, StringComparison.Ordinal) ||
                    tile.TotalRows != first.TotalRows || tile.TotalColumns != first.TotalColumns ||
                    !Mathf.Approximately(tile.TileWorldSize, first.TileWorldSize) ||
                    tile.HeightmapResolution != first.HeightmapResolution)
                {
                    error = $"多场景地图 Terrain 元数据不一致：index={index + 1}";
                    return false;
                }
                if (tile.Row < 1 || tile.Row > first.TotalRows || tile.Column < 1 || tile.Column > first.TotalColumns)
                {
                    error = $"多场景地图 Terrain 行列越界：index={index + 1} row={tile.Row} column={tile.Column}";
                    return false;
                }
                if (tile.Terrain == null || tile.Terrain.terrainData == null)
                {
                    error = $"多场景地图 Terrain 引用无效：index={index + 1}";
                    return false;
                }
                if (!grid.TryAdd((tile.Row, tile.Column), tile))
                {
                    error = $"多场景地图 Terrain 行列重复：row={tile.Row} column={tile.Column}";
                    return false;
                }
            }

            var origin = group.Children[0].Context.MapContentRoot.transform.position;
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                group.Children[index].Context.MapContentRoot.transform.position = origin + new Vector3(
                    (tile.Column - 1) * first.TileWorldSize,
                    0f,
                    (tile.Row - 1) * first.TileWorldSize);
            }

            foreach (var pair in grid)
            {
                var row = pair.Key.Row;
                var column = pair.Key.Column;
                grid.TryGetValue((row, column - 1), out var left);
                grid.TryGetValue((row + 1, column), out var top);
                grid.TryGetValue((row, column + 1), out var right);
                grid.TryGetValue((row - 1, column), out var bottom);
                pair.Value.Terrain.SetNeighbors(left?.Terrain, top?.Terrain, right?.Terrain, bottom?.Terrain);
                pair.Value.Terrain.Flush();
            }

            error = null;
            return true;
        }

        /// <summary>显示前应用地形兼容策略，隐藏后恢复；候选准备阶段不改变旧世界的渲染开关。</summary>
        private void SetHandleContentActive(IMapContentHandle handle, bool active)
        {
            if(handle is GeneratedValleyContentHandle valley)valley.SetActive(active);
            if (active) terrainRendering.SetActive(handle, true, Application.platform);
            if (handle is MapContentGroupHandle group)
            {
                foreach (var child in group.Children) child.Context?.SetMapContentActive(active);
            }
            else handle?.Context?.SetMapContentActive(active);
            if (!active) terrainRendering.SetActive(handle, false, Application.platform);
        }

        private async UniTask<MapContentUnloadResult> UnloadHandleAsync(IMapContentHandle handle, CancellationToken token)
        {
            // 异常清理同样先隐藏并恢复开关，确保场景销毁前仍有可用对象完成恢复。
            SetHandleContentActive(handle, false);
            if (!(handle is MapContentGroupHandle group))
                return await this.GetUtility<IMapContentLoader>().UnloadAsync(handle, token);

            var cleanup = await UnloadHandlesWithFinalCleanupAsync(group.Children);
            return cleanup.Success
                ? MapContentUnloadResult.Ok()
                : MapContentUnloadResult.Fail(cleanup.Error);
        }

        private async UniTask<CleanupAttemptResult> UnloadHandlesWithFinalCleanupAsync(
            IReadOnlyList<IMapContentHandle> handles)
        {
            var snapshot = SnapshotLeafHandles(handles);
            var failed = new List<(int Index, IMapContentHandle Handle)>();
            string firstPassError = null;
            for (var index = snapshot.Count - 1; index >= 0; index--)
            {
                if (snapshot[index] == null || !snapshot[index].IsValid) continue;
                try
                {
                    var result = await this.GetUtility<IMapContentLoader>()
                        .UnloadAsync(snapshot[index], CancellationToken.None);
                    if (snapshot[index].IsValid)
                    {
                        failed.Add((index, snapshot[index]));
                        firstPassError = AppendCleanupError(
                            firstPassError,
                            result.Success
                                ? $"子场景序号 {index + 1} 首次卸载返回成功但句柄仍有效"
                                : $"子场景序号 {index + 1} 首次卸载失败：{result.Error}");
                    }
                }
                catch (Exception exception)
                {
                    if (snapshot[index].IsValid)
                    {
                        failed.Add((index, snapshot[index]));
                        firstPassError = AppendCleanupError(
                            firstPassError,
                            $"子场景序号 {index + 1} 首次卸载异常：{exception.Message}");
                    }
                }
            }

            string finalError = null;
            foreach (var item in failed)
            {
                if (!item.Handle.IsValid) continue;
                try
                {
                    var result = await this.GetUtility<IMapContentLoader>()
                        .UnloadAsync(item.Handle, CancellationToken.None);
                    if (item.Handle.IsValid)
                        finalError = AppendCleanupError(
                            finalError,
                            result.Success
                                ? $"子场景序号 {item.Index + 1} 最终清理返回成功但句柄仍有效"
                                : $"子场景序号 {item.Index + 1} 最终清理失败：{result.Error}");
                }
                catch (Exception exception)
                {
                    if (item.Handle.IsValid)
                        finalError = AppendCleanupError(
                            finalError,
                            $"子场景序号 {item.Index + 1} 最终清理异常：{exception.Message}");
                }
            }

            var remaining = new List<IMapContentHandle>();
            foreach (var handle in snapshot)
            {
                if (handle != null && handle.IsValid) remaining.Add(handle);
            }

            if (remaining.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(firstPassError))
                    Log.Warning($"[MapSceneSystem] 组地图首次卸载存在故障，最终清理已完成：{firstPassError}");
                return new CleanupAttemptResult(null, remaining);
            }

            return new CleanupAttemptResult(AppendCleanupError(firstPassError, finalError), remaining);
        }

        private void HoldPendingCurrentMapCleanup(
            MapSceneModel model,
            string mapId,
            IMapContentHandle handle,
            long mapInstanceId)
        {
            var alreadyOwned = HasPendingCommittedCleanup(mapId, mapInstanceId);
            if (!alreadyOwned)
            {
                ReleaseCurrentMapReverse(mapInstanceId);
                this.GetSystem<AudioSystem>().StopMapOwnedSounds();
            }

            RetainPendingCleanup(new[] { handle }, true, mapId, mapInstanceId);
            model.SetStatus(MapLoadStatus.Failed);
            this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
        }

        private async UniTask<MapSceneResult> RetryPendingCleanupsAsync()
        {
            var model = this.GetModel<MapSceneModel>();
            busy = true;
            var hadCommittedCleanup = HasPendingCommittedCleanup();
            if (hadCommittedCleanup)
            {
                model.SetStatus(MapLoadStatus.Unloading);
                this.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            }

            try
            {
                string error = null;
                var snapshot = new List<PendingCleanupOwnership>();
                foreach (var pending in pendingCleanups)
                {
                    if (!pending.IsCommitted) snapshot.Add(pending);
                }
                foreach (var pending in pendingCleanups)
                {
                    if (pending.IsCommitted) snapshot.Add(pending);
                }

                foreach (var pending in snapshot)
                {
                    var cleanup = await UnloadHandlesWithFinalCleanupAsync(pending.Handles);
                    if (!cleanup.Success)
                    {
                        pending.Handles = cleanup.Remaining;
                        error = AppendCleanupError(
                            error,
                            $"{(pending.IsCommitted ? "已提交地图" : "未提交候选")}残留清理失败：{cleanup.Error}");
                        continue;
                    }

                    pendingCleanups.Remove(pending);
                    if (pending.IsCommitted)
                        this.SendEvent(new MapCurrentReleasedEvent(pending.MapId, pending.MapInstanceId));
                }

                var committedCleanupRemains = HasPendingCommittedCleanup();
                if (hadCommittedCleanup && !committedCleanupRemains)
                {
                    model.Clear();
                    this.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    if (committedCleanupRemains)
                    {
                        model.SetStatus(MapLoadStatus.Failed);
                        this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                    }
                    return MapSceneResult.Fail(error, model.Status.Value);
                }

                return MapSceneResult.Ok(model.Status.Value);
            }
            catch (Exception exception)
            {
                if (HasPendingCommittedCleanup())
                {
                    model.SetStatus(MapLoadStatus.Failed);
                    this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                }
                return MapSceneResult.Fail($"地图残留清理异常：{exception.Message}", model.Status.Value);
            }
            finally
            {
                busy = false;
            }
        }

        private async UniTask<string> UnloadOwnedReverseAsync(
            IReadOnlyList<IMapContentHandle> owned,
            string error,
            string context)
        {
            var cleanup = await UnloadHandlesWithFinalCleanupAsync(owned);
            if (!cleanup.Success)
            {
                RetainPendingCleanup(cleanup.Remaining, false, null, 0);
                error = AppendCleanupError(error, $"{context}：{cleanup.Error}");
            }
            return error;
        }

        private void RetainPendingCleanup(
            IReadOnlyList<IMapContentHandle> handles,
            bool isCommitted,
            string mapId,
            long mapInstanceId)
        {
            var remaining = SnapshotLeafHandles(handles);
            remaining.RemoveAll(handle => handle == null || !handle.IsValid);
            foreach (var handle in remaining) handle.Context?.SetMapContentActive(false);

            PendingCleanupOwnership existing = null;
            if (isCommitted)
            {
                foreach (var pending in pendingCleanups)
                {
                    if (pending.IsCommitted && pending.MapId == mapId && pending.MapInstanceId == mapInstanceId)
                    {
                        existing = pending;
                        break;
                    }
                }
            }

            var newlyOwned = new List<IMapContentHandle>();
            foreach (var handle in remaining)
            {
                if (!IsHandlePending(handle)) newlyOwned.Add(handle);
            }

            if (existing != null)
            {
                existing.Handles.AddRange(newlyOwned);
                return;
            }

            if (newlyOwned.Count > 0)
                pendingCleanups.Add(new PendingCleanupOwnership(newlyOwned, isCommitted, mapId, mapInstanceId));
        }

        private bool IsHandlePending(IMapContentHandle handle)
        {
            foreach (var pending in pendingCleanups)
            foreach (var owned in pending.Handles)
            {
                if (ReferenceEquals(owned, handle)) return true;
            }
            return false;
        }

        private bool HasPendingCommittedCleanup(string mapId = null, long mapInstanceId = 0)
        {
            foreach (var pending in pendingCleanups)
            {
                if (!pending.IsCommitted) continue;
                if (mapId == null || pending.MapId == mapId && pending.MapInstanceId == mapInstanceId) return true;
            }
            return false;
        }

        private static List<IMapContentHandle> SnapshotLeafHandles(IReadOnlyList<IMapContentHandle> handles)
        {
            var snapshot = new List<IMapContentHandle>();
            if (handles == null) return snapshot;
            foreach (var handle in handles)
            {
                if (handle is MapContentGroupHandle group)
                {
                    foreach (var child in group.Children) AddUniqueHandle(snapshot, child);
                }
                else
                {
                    AddUniqueHandle(snapshot, handle);
                }
            }
            return snapshot;
        }

        private static void AddUniqueHandle(List<IMapContentHandle> handles, IMapContentHandle handle)
        {
            if (handle == null) return;
            foreach (var existing in handles)
            {
                if (ReferenceEquals(existing, handle)) return;
            }
            handles.Add(handle);
        }

        private bool TryDeactivateOld(out string error)
        {
            error = null;
            try
            {
                SetCurrentMapActiveReverse(false);
                SetHandleContentActive(this.GetModel<MapSceneModel>().CurrentHandle, false);
                return true;
            }
            catch (Exception e)
            {
                error = $"停用旧地图失败：{e.Message}";
                return false;
            }
        }

        private bool TryDeactivateOld()
        {
            return TryDeactivateOld(out _);
        }

        private bool TryRestoreOld(IMapContentHandle oldHandle, AppState oldState, out string error)
        {
            error = null;
            try
            {
                if (oldHandle == null || !oldHandle.IsValid || oldHandle.Context == null)
                {
                    error = "恢复旧地图失败：旧地图句柄无效";
                    return false;
                }

                SetHandleContentActive(oldHandle, true);
                SetCurrentMapActiveReverse(true);
                this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(oldState);
                return true;
            }
            catch (Exception e)
            {
                error = $"恢复旧地图失败：{e.Message}";
                return false;
            }
        }

        private void RestoreModelAfterLoadFailure(MapSceneModel model, string oldMapId, IMapContentHandle oldHandle,
            MapPreparationMode oldMode, AppState oldState, bool canRestoreOld)
        {
            if (canRestoreOld && oldHandle != null && oldHandle.IsValid)
            {
                model.SetLoaded(oldMapId, oldHandle, oldMode);
                this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(oldState);
                return;
            }

            model.Clear(MapLoadStatus.Failed);
            this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
        }

        private async UniTask<string> TryUnloadForCleanup(IMapContentHandle handle, string error, string context)
        {
            if (handle == null)
            {
                return error;
            }

            try
            {
                var unload = await UnloadHandleAsync(handle, CancellationToken.None);
                if (unload.Success) return error;
                RetainPendingCleanup(new[] { handle }, false, null, 0);
                return AppendCleanupError(error, $"{context}：{unload.Error}");
            }
            catch (Exception e)
            {
                RetainPendingCleanup(new[] { handle }, false, null, 0);
                return AppendCleanupError(error, $"{context}：{e.Message}");
            }
        }

        private async UniTask<string> CleanupFailed(
            MapSceneModel model,
            IMapContentHandle oldHandle,
            IMapContentHandle candidateHandle,
            string error,
            string oldMapId,
            long oldMapInstanceId,
            bool oldReleasePublished)
        {
            error = ReleaseCurrentMapForCleanup(error);
            error = await TryUnloadForCleanup(candidateHandle, error, "候选地图卸载失败");
            MapContentUnloadResult oldUnload = null;
            if (oldHandle != null)
            {
                try
                {
                    oldUnload = await UnloadHandleAsync(oldHandle, CancellationToken.None);
                    if (!oldUnload.Success)
                        error = AppendCleanupError(error, $"旧地图卸载失败：{oldUnload.Error}");
                }
                catch (Exception exception)
                {
                    oldUnload = MapContentUnloadResult.Fail(exception.Message);
                    error = AppendCleanupError(error, $"旧地图卸载失败：{exception.Message}");
                }
            }

            if (oldHandle is MapContentGroupHandle oldGroup &&
                oldUnload != null && !oldUnload.Success &&
                HasValidChildren(oldGroup))
            {
                RetainPendingCleanup(new[] { oldHandle }, true, oldMapId, oldMapInstanceId);
                this.GetSystem<AudioSystem>().StopMapOwnedSounds();
                model.SetStatus(MapLoadStatus.Failed);
                this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
                return error;
            }

            if (!oldReleasePublished && oldHandle != null && oldMapInstanceId > 0)
            {
                this.SendEvent(new MapCurrentReleasedEvent(oldMapId, oldMapInstanceId));
                oldReleasePublished = true;
            }
            model.Clear(MapLoadStatus.Failed);
            this.GetSystem<AppStateSystem>().TryRestoreStableStateAfterLoading(AppState.Paused);
            return error;
        }

        private static bool HasValidChildren(MapContentGroupHandle group)
        {
            foreach (var child in group.Children)
            {
                if (child != null && child.IsValid) return true;
            }
            return false;
        }

        private string ReleaseCurrentMapForCleanup(string error)
        {
            if(onlineWorldCorpseCtrl!=null)error = TryReleaseForCleanup(onlineWorldCorpseCtrl.ReleaseCurrentMap, "OnlineWorldCorpseCtrl", error);
            if(onlineWorldEnemyCtrl!=null)error = TryReleaseForCleanup(onlineWorldEnemyCtrl.ReleaseCurrentMap, "OnlineWorldEnemyCtrl", error);
            if(onlineWorldPlayerCtrl!=null)error = TryReleaseForCleanup(onlineWorldPlayerCtrl.ReleaseCurrentMap, "OnlineWorldPlayerCtrl", error);
            error = TryReleaseForCleanup(extractionCtrl.ReleaseCurrentMap, "ExtractionCtrl", error);
            error = TryReleaseForCleanup(lootCtrl.ReleaseCurrentMap, "LootCtrl", error);
            error = TryReleaseForCleanup(enemyCtrl.ReleaseCurrentMap, "EnemyCtrl", error);
            error = TryReleaseForCleanup(npcCtrl.ReleaseCurrentMap, "NpcCtrl", error);
            error = TryReleaseForCleanup(playerCtrl.ReleaseCurrentMap, "PlayerCtrl", error);
            return TryReleaseForCleanup(projectileCtrl.ReleaseCurrentMap, "ProjectileCtrl", error);
        }

        private long TryGetCurrentMapInstanceId()
        {
            return this.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player)
                ? player.MapInstanceId
                : 0;
        }

        private static string TryReleaseForCleanup(Action release, string owner, string error)
        {
            try
            {
                release();
                return error;
            }
            catch (Exception exception)
            {
                Log.Error($"[MapSceneSystem] 地图表现释放异常：owner={owner} exception={exception}");
                return AppendCleanupError(error, $"{owner} 释放异常：{exception.Message}");
            }
        }

        private static string AppendCleanupError(string error, string cleanupError)
        {
            if (string.IsNullOrWhiteSpace(cleanupError))
            {
                return error;
            }

            return string.IsNullOrWhiteSpace(error) ? cleanupError : $"{error}；{cleanupError}";
        }
    }
}

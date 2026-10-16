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
    /// <summary>提交联机街机创建/恢复意图并发布服务端快照；不裁决生命、金币、商店或战斗。</summary>
    public sealed class OnlineArcadeRunSystem : AbstractSystem
    {
        private const int RpcTimeoutMilliseconds = 10000;
        private const float InputSendIntervalSeconds = 0.05f;
        private INetworkRuntime network;
        private CancellationTokenSource operationCancellation;
        private CancellationTokenSource inputCancellation;
        private IDisposable runChangedSubscription;
        private int operationGeneration;
        private long lastIssuedInputSequence;
        private OnlineArcadeInputController input;
        private IArcadeCombatViewPort combatView;
        private float nextInputSendTime;
        private long pendingInputSequence;
        private OnlineArcadeInputFrame pendingInput;
        private OnlineArcadeInputFrame lastSampledInput;
        private bool inputPumpBusy;
        private OnlineArcadeResultFileStore resultStore;
        private OnlineArcadeResultData pendingResult;
        private bool presentingResult;
        private bool resultReadFailed;
        private OnlineArcadeRunClientSnapshot lastSnapshot = OnlineArcadeRunClientSnapshot.Empty();
        public OnlineArcadeRunClientSnapshot Snapshot => lastSnapshot;
        public OnlineArcadeInputFrame LastSampledInput => lastSampledInput;

        protected override void OnInit()
        {
            network = this.GetUtility<INetworkRuntime>();
            if (network != null)
                runChangedSubscription = network.RegisterPushHandler<G2C_OnlineArcadeRunChanged>(OnRunChanged);
            input = new OnlineArcadeInputController();
            combatView = this.GetUtility<IArcadeCombatViewPort>();
            Publish(OnlineArcadeRunClientSnapshot.Empty());
            this.RegisterEvent<OnlineAccountChangedEvent>(OnAccountChanged);
            this.RegisterEvent<ArcadeHudActionRequestedEvent>(OnHudAction);
            LifeCycle.RegisterUpdate(OnUpdate);
        }

        protected override void OnDeinit()
        {
            CancelOperation();
            LifeCycle.UnregisterUpdate(OnUpdate);
            input?.Dispose();
            input = null;
            runChangedSubscription?.Dispose();
            runChangedSubscription = null;
        }

        public Task<OnlineArcadeOperationResult> StartAsync()
        {
            if (resultReadFailed)
                return Task.FromResult(new OnlineArcadeOperationResult(OnlineArcadeResultCode.SaveFailed, lastSnapshot));
            if (pendingResult != null)
            {
                PresentResultAsync().Forget();
                return Task.FromResult(new OnlineArcadeOperationResult(OnlineArcadeResultCode.InvalidState, lastSnapshot));
            }
            return Run(OnlineArcadeOperationState.Starting, async token =>
            {
                C2G_StartOnlineArcadeRun request = C2G_StartOnlineArcadeRun.Create();
                request.OperationId = Guid.NewGuid().ToString("N");
                return await network.CallAsync<G2C_StartOnlineArcadeRun>(request, RpcTimeoutMilliseconds, token);
            });
        }

        public Task<OnlineArcadeOperationResult> RestoreAsync()
        {
            return Run(OnlineArcadeOperationState.Restoring, token =>
                network.CallAsync<G2C_GetCurrentOnlineArcadeRun>(C2G_GetCurrentOnlineArcadeRun.Create(),
                    RpcTimeoutMilliseconds, token));
        }

        public Task<OnlineArcadeOperationResult> PurchaseAsync(string offerId) =>
            OperateSafeShop(OnlineArcadeOperationState.Purchasing, 1, offerId, 0, false);

        public Task<OnlineArcadeOperationResult> TransferGoldAsync(int amount) =>
            OperateSafeShop(OnlineArcadeOperationState.TransferringGold, 2, string.Empty, amount, false);

        public Task<OnlineArcadeOperationResult> SetReadyAsync(bool ready) =>
            OperateSafeShop(OnlineArcadeOperationState.SettingReady, 3, string.Empty, 0, ready);

        /// <summary>设置本安全节点的挑战准备；双方都准备后服务器先落盘尝试标记，再创建挑战现场。</summary>
        public Task<OnlineArcadeOperationResult> SetChallengeReadyAsync(bool ready) =>
            OperateSafeShop(OnlineArcadeOperationState.SettingChallengeReady, 7, string.Empty, 0, ready);

        public Task<OnlineArcadeOperationResult> ConfirmSettlementAsync() =>
            OperateSafeShop(OnlineArcadeOperationState.Settling, 4, string.Empty, 0, false);

        public Task<OnlineArcadeOperationResult> ConfirmAbandonAsync() =>
            OperateSafeShop(OnlineArcadeOperationState.Abandoning, 5, string.Empty, 0, false);

        /// <summary>
        /// 上报本地玩家的移动、瞄准、持续射击及一次性消耗品意图；位置、命中及伤害始终由服务器裁决。
        /// 同一时刻只保留一个输入 RPC，失败不会回滚已经发出的单调序号。
        /// </summary>
        public async Task<OnlineArcadeInputResult> SubmitInputAsync(float moveX, float moveZ, float aimX,
            float aimZ, bool firing, bool useBomb = false, bool useDash = false)
        {
            return await SubmitInputWithSequenceAsync(++lastIssuedInputSequence, moveX, moveZ, aimX, aimZ,
                firing, useBomb, useDash);
        }

        /// <summary>允许输入发送泵在响应不确定时复用原序号；服务端会以 LastAcceptedInputSequence 幂等确认。</summary>
        private async Task<OnlineArcadeInputResult> SubmitInputWithSequenceAsync(long sequence, float moveX,
            float moveZ, float aimX, float aimZ, bool firing, bool useBomb, bool useDash)
        {
            OnlineAccountSnapshot account = this.GetModel<OnlineAccountModel>().Snapshot.Value;
            if (!account.IsAuthenticated || network == null || network.ConnectionState != NetworkConnectionState.Connected)
                return new OnlineArcadeInputResult(OnlineArcadeResultCode.NotLoggedIn, false,
                    lastIssuedInputSequence, lastSnapshot);
            if (lastSnapshot.Phase is not (OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge) ||
                lastSnapshot.Battle == null)
                return new OnlineArcadeInputResult(OnlineArcadeResultCode.InvalidState, false,
                    lastIssuedInputSequence, lastSnapshot);
            if (inputCancellation != null)
                return new OnlineArcadeInputResult(OnlineArcadeResultCode.OperationInProgress, false,
                    lastIssuedInputSequence, lastSnapshot);

            CancellationTokenSource currentInputCancellation = new();
            inputCancellation = currentInputCancellation;
            try
            {
                C2G_SubmitOnlineArcadeInput request = C2G_SubmitOnlineArcadeInput.Create();
                request.ArenaNumber = lastSnapshot.Battle.ArenaNumber;
                request.InputSequence = sequence;
                request.MoveX = moveX; request.MoveZ = moveZ;
                request.AimX = aimX; request.AimZ = aimZ;
                request.Firing = firing;
                request.UseBomb = useBomb;
                request.UseDash = useDash;
                G2C_SubmitOnlineArcadeInput response = await network.CallAsync<G2C_SubmitOnlineArcadeInput>(
                    request, RpcTimeoutMilliseconds, currentInputCancellation.Token);
                lastIssuedInputSequence = Math.Max(lastIssuedInputSequence, response.LastAcceptedInputSequence);
                OnlineArcadeResultCode code = MapServerError(response.Error);
                if (code == OnlineArcadeResultCode.Success && response.Run != null)
                    TryPublishNewerSnapshot(response.Run);
                return new OnlineArcadeInputResult(code, response.Accepted,
                    response.LastAcceptedInputSequence, lastSnapshot);
            }
            catch (OperationCanceledException)
            {
                return new OnlineArcadeInputResult(OnlineArcadeResultCode.Disconnected, false,
                    lastIssuedInputSequence, lastSnapshot);
            }
            catch (TimeoutException)
            {
                return new OnlineArcadeInputResult(OnlineArcadeResultCode.Timeout, false,
                    lastIssuedInputSequence, lastSnapshot);
            }
            catch
            {
                return new OnlineArcadeInputResult(OnlineArcadeResultCode.UnknownFailure, false,
                    lastIssuedInputSequence, lastSnapshot);
            }
            finally
            {
                currentInputCancellation.Dispose();
                if (ReferenceEquals(inputCancellation, currentInputCancellation)) inputCancellation = null;
            }
        }

        private void OnUpdate()
        {
            bool inArena = lastSnapshot.Phase is OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge &&
                           lastSnapshot.Battle != null;
            if (!inArena || lastSnapshot.BattlePaused)
            {
                if (input?.IsEnabled == true) input.Disable(true);
                pendingInputSequence = 0;
                return;
            }
            if (input?.IsEnabled == false) input.Enable();
            if (inputPumpBusy || Time.unscaledTime < nextInputSendTime) return;
            PumpInputAsync().Forget();
        }

        /// <summary>固定频率发送持续状态；不确定失败保留原序号和脉冲，避免丢失或重复执行。</summary>
        private async UniTaskVoid PumpInputAsync()
        {
            inputPumpBusy = true;
            try
            {
                if (pendingInputSequence == 0)
                {
                    Vector2 playerPosition = FindLocalBattlePlayerPosition();
                    pendingInput = input.Read(combatView, playerPosition);
                    lastSampledInput = pendingInput;
                    pendingInputSequence = ++lastIssuedInputSequence;
                }
                long sequence = pendingInputSequence;
                OnlineArcadeInputResult result = await SubmitInputWithSequenceAsync(sequence,
                    pendingInput.Move.x, pendingInput.Move.y, pendingInput.Aim.x, pendingInput.Aim.y,
                    pendingInput.Firing, pendingInput.Bomb, pendingInput.Dash);
                if (result.InputSequence >= sequence && (result.Accepted || result.Code == OnlineArcadeResultCode.Success))
                {
                    input.Acknowledge(pendingInput.Bomb, pendingInput.Dash);
                    pendingInputSequence = 0;
                }
                else if (result.Code == OnlineArcadeResultCode.InvalidState ||
                         result.Code == OnlineArcadeResultCode.NotLoggedIn)
                {
                    pendingInputSequence = 0;
                }
            }
            finally
            {
                nextInputSendTime = Time.unscaledTime + InputSendIntervalSeconds;
                inputPumpBusy = false;
            }
        }

        private Vector2 FindLocalBattlePlayerPosition()
        {
            long playerId = this.GetModel<OnlineAccountModel>().Snapshot.Value.PlayerId;
            IReadOnlyList<OnlineArcadeBattlePlayerSnapshot> players = lastSnapshot.Battle?.Players;
            if (players != null)
                for (int index = 0; index < players.Count; index++)
                    if (players[index].PlayerId == playerId)
                        return new Vector2(players[index].PositionX, players[index].PositionZ);
            return Vector2.zero;
        }

        private void OnHudAction(ArcadeHudActionRequestedEvent evt)
        {
            if (lastSnapshot.Phase is not (OnlineArcadeRunPhase.Arena or OnlineArcadeRunPhase.Challenge) ||
                lastSnapshot.BattlePaused || input == null) return;
            if (evt.Action == ArcadeHudActionKind.Bomb) input.QueueBomb();
            else if (evt.Action == ArcadeHudActionKind.Dash) input.QueueDash();
        }

        private Task<OnlineArcadeOperationResult> OperateSafeShop(OnlineArcadeOperationState operation,
            int actionKind, string offerId, int goldAmount, bool ready)
        {
            long expectedRevision = lastSnapshot.Revision;
            return Run(operation, async token =>
            {
                C2G_OperateOnlineArcadeSafeShop request = C2G_OperateOnlineArcadeSafeShop.Create();
                request.OperationId = Guid.NewGuid().ToString("N");
                request.ExpectedRevision = expectedRevision;
                request.ActionKind = actionKind;
                request.OfferId = offerId ?? string.Empty;
                request.GoldAmount = goldAmount;
                request.Ready = ready;
                return await network.CallAsync<G2C_OperateOnlineArcadeSafeShop>(request, RpcTimeoutMilliseconds, token);
            });
        }

        private async Task<OnlineArcadeOperationResult> Run<TResponse>(OnlineArcadeOperationState operation,
            Func<CancellationToken, Task<TResponse>> call) where TResponse : class, IResponse
        {
            OnlineAccountSnapshot account = this.GetModel<OnlineAccountModel>().Snapshot.Value;
            if (!account.IsAuthenticated || network == null || network.ConnectionState != NetworkConnectionState.Connected)
                return new OnlineArcadeOperationResult(OnlineArcadeResultCode.NotLoggedIn, lastSnapshot);
            if (operationCancellation != null)
                return new OnlineArcadeOperationResult(OnlineArcadeResultCode.OperationInProgress, lastSnapshot);

            int generation = ++operationGeneration;
            operationCancellation = new CancellationTokenSource();
            Publish(new OnlineArcadeRunClientSnapshot(lastSnapshot.HasRun, lastSnapshot.RunId, lastSnapshot.RoomId,
                lastSnapshot.Revision, lastSnapshot.Phase, lastSnapshot.CompletedArena, lastSnapshot.NextArena,
                lastSnapshot.ConfigFingerprint, lastSnapshot.Players, operation, OnlineArcadeResultCode.None,
                lastSnapshot.Battle, lastSnapshot.Connections, lastSnapshot.BattlePaused,
                lastSnapshot.ChallengeId, lastSnapshot.ChallengeAttempted));
            try
            {
                TResponse response = await call(operationCancellation.Token);
                if (generation != operationGeneration) return new OnlineArcadeOperationResult(OnlineArcadeResultCode.Disconnected, lastSnapshot);
                int error = response.Error;
                bool hasRun;
                ET.OnlineArcadeRunSnapshot run;
                if (response is G2C_StartOnlineArcadeRun start) { hasRun = start.HasRun; run = start.Run; }
                else if (response is G2C_GetCurrentOnlineArcadeRun restore) { hasRun = restore.HasRun; run = restore.Run; }
                else
                {
                    var operate = (G2C_OperateOnlineArcadeSafeShop)(object)response;
                    hasRun = operate.Run != null;
                    run = operate.Run;
                }
                OnlineArcadeResultCode code = MapServerError(error);
                if (code == OnlineArcadeResultCode.Success)
                {
                    OnlineArcadeRunClientSnapshot snapshot = hasRun && run != null
                        ? CopySnapshot(run, code)
                        : OnlineArcadeRunClientSnapshot.Empty(code);
                    if (snapshot.HasRun && !MatchesLocalConfig(snapshot.ConfigFingerprint))
                    {
                        code = OnlineArcadeResultCode.VersionMismatch;
                        snapshot = OnlineArcadeRunClientSnapshot.Empty(code);
                    }
                    Publish(snapshot);
                }
                else Publish(WithIdle(code));
                return new OnlineArcadeOperationResult(code, lastSnapshot);
            }
            catch (OperationCanceledException)
            {
                return new OnlineArcadeOperationResult(OnlineArcadeResultCode.Disconnected, lastSnapshot);
            }
            catch (TimeoutException)
            {
                Publish(WithIdle(OnlineArcadeResultCode.Timeout));
                return new OnlineArcadeOperationResult(OnlineArcadeResultCode.Timeout, lastSnapshot);
            }
            catch
            {
                Publish(WithIdle(OnlineArcadeResultCode.UnknownFailure));
                return new OnlineArcadeOperationResult(OnlineArcadeResultCode.UnknownFailure, lastSnapshot);
            }
            finally
            {
                if (generation == operationGeneration)
                {
                    operationCancellation?.Dispose();
                    operationCancellation = null;
                }
            }
        }

        internal static OnlineArcadeResultCode MapServerError(int error) => error switch
        {
            0 => OnlineArcadeResultCode.Success,
            200105 => OnlineArcadeResultCode.NotLoggedIn,
            200134 => OnlineArcadeResultCode.VersionMismatch,
            200135 => OnlineArcadeResultCode.InvalidState,
            200136 => OnlineArcadeResultCode.SaveFailed,
            200137 => OnlineArcadeResultCode.OperationConflict,
            _ => OnlineArcadeResultCode.UnknownFailure,
        };

        private bool MatchesLocalConfig(string fingerprint)
        {
            IRuntimeConfigProvider config = this.GetUtility<IRuntimeConfigProvider>();
            return config != null && config.TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition catalog, out _) &&
                   string.Equals(fingerprint, catalog.Rule.ConfigFingerprint, StringComparison.Ordinal);
        }

        private static OnlineArcadeRunClientSnapshot CopySnapshot(ET.OnlineArcadeRunSnapshot source, OnlineArcadeResultCode code)
        {
            List<OnlineArcadePlayerSnapshot> players = new(source.Players.Count);
            foreach (OnlineArcadePlayerInfo sourcePlayer in source.Players)
            {
                List<OnlineArcadeShopOfferSnapshot> offers = new(sourcePlayer.ShopOffers.Count);
                foreach (OnlineArcadeShopOfferInfo offer in sourcePlayer.ShopOffers)
                    offers.Add(new OnlineArcadeShopOfferSnapshot(offer.OfferId, offer.ShopItemId, offer.ProductId,
                        offer.ProductType, offer.Price, offer.Amount, offer.PurchaseCount, offer.MaxPurchases));
                Dictionary<string, int> upgrades = new(StringComparer.Ordinal);
                foreach (OnlineArcadeOwnedAmountInfo amount in sourcePlayer.UpgradeStacks)
                    upgrades[amount.Id ?? string.Empty] = amount.Amount;
                Dictionary<string, int> consumables = new(StringComparer.Ordinal);
                foreach (OnlineArcadeOwnedAmountInfo amount in sourcePlayer.Consumables)
                    consumables[amount.Id ?? string.Empty] = amount.Amount;
                Dictionary<string, string> equipment = new(StringComparer.Ordinal);
                foreach (OnlineArcadeEquipmentInfo item in sourcePlayer.Equipment)
                    equipment[item.SlotId ?? string.Empty] = item.ProductId ?? string.Empty;
                players.Add(new OnlineArcadePlayerSnapshot(sourcePlayer.PlayerId, sourcePlayer.DisplayName,
                    sourcePlayer.Lives, sourcePlayer.RunGold, sourcePlayer.Bombs, sourcePlayer.Dashes,
                    sourcePlayer.WeaponId, sourcePlayer.Ready, offers, upgrades, consumables, equipment,
                    sourcePlayer.SettlementConfirmed, sourcePlayer.AbandonConfirmed,
                    sourcePlayer.SettlementState, sourcePlayer.ChallengeReady, sourcePlayer.Score,
                    Math.Max(1, sourcePlayer.ScoreMultiplier), sourcePlayer.MultiplierProgress));
            }
            return new OnlineArcadeRunClientSnapshot(true, source.RunId, source.RoomId, source.Revision,
                (OnlineArcadeRunPhase)source.Phase, source.CompletedArena, source.NextArena,
                source.ConfigFingerprint, players, OnlineArcadeOperationState.Idle, code, CopyBattle(source.Battle),
                CopyConnections(source.Connections), source.BattlePaused, source.ChallengeId,
                source.ChallengeAttempted);
        }

        private static IReadOnlyList<OnlineArcadeConnectionSnapshot> CopyConnections(
            IReadOnlyList<OnlineArcadeConnectionInfo> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<OnlineArcadeConnectionSnapshot>();
            List<OnlineArcadeConnectionSnapshot> result = new(source.Count);
            foreach (OnlineArcadeConnectionInfo value in source)
                result.Add(new OnlineArcadeConnectionSnapshot(value.PlayerId, value.Connected,
                    value.DisconnectedAtServerTime, value.ArenaReconnectDeadlineServerTime,
                    value.SoloEndAvailableAtServerTime));
            return result;
        }

        private static OnlineArcadeBattleSnapshot CopyBattle(OnlineArcadeBattleInfo source)
        {
            if (source == null) return null;
            List<OnlineArcadeBattlePlayerSnapshot> players = new(source.Players.Count);
            foreach (OnlineArcadeBattlePlayerInfo player in source.Players)
            {
                List<OnlineArcadePowerupSnapshot> powerups = new(player.Powerups.Count);
                foreach (OnlineArcadePowerupInfo powerup in player.Powerups)
                    powerups.Add(new OnlineArcadePowerupSnapshot(powerup.ProductId,
                        powerup.RemainingSeconds, powerup.Charges, powerup.PositionX, powerup.PositionZ,
                        powerup.ShotDirectionX, powerup.ShotDirectionZ));
                players.Add(new OnlineArcadeBattlePlayerSnapshot(player.PlayerId, player.PositionX, player.PositionZ,
                    player.AimX, player.AimZ, player.CurrentHealth, player.MaxHealth, player.Alive,
                    player.Invulnerable, player.RespawnSeconds, player.TemporaryWeaponId,
                    player.TemporaryWeaponSeconds, player.SupportId, player.SupportSeconds, player.DashSeconds,
                    powerups, player.FireSequence, player.InTreasureRoom));
            }
            List<OnlineArcadeEnemySnapshot> enemies = new(source.Enemies.Count);
            foreach (OnlineArcadeEnemyInfo enemy in source.Enemies)
                enemies.Add(new OnlineArcadeEnemySnapshot(enemy.RuntimeId, enemy.EnemyConfigId, enemy.PositionX,
                    enemy.PositionZ, enemy.CurrentHealth, enemy.MaxHealth, enemy.TargetPlayerId, enemy.WaveNumber,
                    enemy.BossId, (ArcadeBossPhase)enemy.BossPhase, enemy.BossPhaseSeconds, enemy.ArmorHealth));
            List<OnlineArcadeProjectileSnapshot> projectiles = new(source.Projectiles.Count);
            foreach (OnlineArcadeProjectileInfo projectile in source.Projectiles)
                projectiles.Add(new OnlineArcadeProjectileSnapshot(projectile.RuntimeId, projectile.OwnerPlayerId,
                    projectile.PositionX, projectile.PositionZ, projectile.VelocityX, projectile.VelocityZ));
            List<OnlineArcadeDropSnapshot> drops = new(source.Drops.Count);
            foreach (OnlineArcadeDropInfo drop in source.Drops)
                drops.Add(new OnlineArcadeDropSnapshot(drop.RuntimeId, drop.DropConfigId,
                    drop.PositionX, drop.PositionZ));
            List<OnlineArcadeDropSnapshot> supplyPickups = new(source.SupplyPickups.Count);
            foreach (var receipt in source.SupplyPickups)
                supplyPickups.Add(new OnlineArcadeDropSnapshot(receipt.RuntimeId, receipt.DropConfigId,
                    receipt.PositionX, receipt.PositionZ, receipt.CollectedByPlayerId, receipt.AddedAmount,
                    receipt.ScoreAdded, receipt.GoldAdded));
            return new OnlineArcadeBattleSnapshot(source.ArenaNumber, source.WaveNumber, source.WaveCount,
                source.SpawnedInWave, source.TotalInWave, source.DifficultyPlayerCount, source.BattleRevision,
                source.ServerTime, players, enemies, projectiles, drops, source.IsChallenge,
                source.ChallengeId, source.ChallengeSeconds, source.HazardElapsedSeconds,
                source.ArenaConfigNumber, source.IsFinalBoss, source.AwaitingExit, source.TreasureRoomId,
                source.TreasureDiscovered, source.TreasureOpen, source.TreasureExpired, source.TreasureSeconds,
                source.TreasureRewardIds, source.CrateIds, supplyPickups);
        }

        private OnlineArcadeRunClientSnapshot WithIdle(OnlineArcadeResultCode code) =>
            new(lastSnapshot.HasRun, lastSnapshot.RunId, lastSnapshot.RoomId, lastSnapshot.Revision,
                lastSnapshot.Phase, lastSnapshot.CompletedArena, lastSnapshot.NextArena,
                lastSnapshot.ConfigFingerprint, lastSnapshot.Players, OnlineArcadeOperationState.Idle, code,
                lastSnapshot.Battle, lastSnapshot.Connections, lastSnapshot.BattlePaused,
                lastSnapshot.ChallengeId, lastSnapshot.ChallengeAttempted);

        private void Publish(OnlineArcadeRunClientSnapshot snapshot)
        {
            bool ended = lastSnapshot.HasRun && !snapshot.HasRun && snapshot.LastResultCode == OnlineArcadeResultCode.Success;
            if (ended)
            {
                pendingResult = new OnlineArcadeResultData { RunId = lastSnapshot.RunId,
                    Outcome = lastSnapshot.Phase == OnlineArcadeRunPhase.Victory ? "victory" :
                        lastSnapshot.OperationState == OnlineArcadeOperationState.Abandoning ? "abandoned" : "failed" };
                foreach (var player in lastSnapshot.Players)
                    pendingResult.Scores[player.PlayerId.ToString()] = player.Score;
                if (resultStore != null && !resultStore.TrySave(pendingResult, out var error)) Log.Error("联机结果保存失败：" + error);
            }
            lastSnapshot = snapshot;
            this.GetModel<OnlineArcadeRunModel>().Publish(snapshot);
            if (ended) PresentResultAsync().Forget();
        }

        private void OnRunChanged(G2C_OnlineArcadeRunChanged message)
        {
            if (message == null) return;
            if (!message.HasRun)
            {
                Publish(OnlineArcadeRunClientSnapshot.Empty(OnlineArcadeResultCode.Success));
                return;
            }
            if (message.Run == null) return;
            TryPublishNewerSnapshot(message.Run);
        }

        /// <summary>合并 RPC 与推送可能乱序到达的快照，只允许版本向前推进。</summary>
        private void TryPublishNewerSnapshot(ET.OnlineArcadeRunSnapshot run)
        {
            if (run.Revision < lastSnapshot.Revision) return;
            if (run.Revision == lastSnapshot.Revision && run.Battle != null &&
                lastSnapshot.Battle != null && run.Battle.BattleRevision < lastSnapshot.Battle.BattleRevision)
                return;
            OnlineArcadeRunClientSnapshot snapshot = CopySnapshot(run, OnlineArcadeResultCode.Success);
            if (!MatchesLocalConfig(snapshot.ConfigFingerprint))
            {
                Publish(OnlineArcadeRunClientSnapshot.Empty(OnlineArcadeResultCode.VersionMismatch));
                return;
            }
            if (operationCancellation != null)
                snapshot = new OnlineArcadeRunClientSnapshot(snapshot.HasRun, snapshot.RunId, snapshot.RoomId,
                    snapshot.Revision, snapshot.Phase, snapshot.CompletedArena, snapshot.NextArena,
                    snapshot.ConfigFingerprint, snapshot.Players, lastSnapshot.OperationState,
                    snapshot.LastResultCode, snapshot.Battle, snapshot.Connections, snapshot.BattlePaused,
                    snapshot.ChallengeId, snapshot.ChallengeAttempted);
            Publish(snapshot);
        }

        private void OnAccountChanged(OnlineAccountChangedEvent evt)
        {
            if (evt.Snapshot.IsAuthenticated)
            {
                resultStore = new OnlineArcadeResultFileStore(evt.Snapshot.PlayerId);
                resultReadFailed = !resultStore.TryRead(out pendingResult, out var resultError);
                if (resultReadFailed) { Log.Error("联机结果读取失败：" + resultError); Notify.ShowFloatTips(Texts.SaveErrorLoadFailed); }
                RestoreAndClaimPendingAsync().Forget();
                return;
            }
            CancelOperation();
            bool hadRun = lastSnapshot.HasRun;
            Publish(OnlineArcadeRunClientSnapshot.Empty());
            pendingResult = null; resultStore = null; resultReadFailed = false;
            if (hadRun) this.GetSystem<ArcadeSceneFlowSystem>().ReturnToMainMenuAsync().Forget();
        }

        /// <summary>
        /// 登录后只恢复未结束局，或显示本应用保存的结果回执；不领取局外奖励。
        /// 恢复失败保留服务器本局，不以新开局覆盖。
        /// </summary>
        private async UniTaskVoid RestoreAndClaimPendingAsync()
        {
            var result = await RestoreAsync();
            if (result.Success && !result.Snapshot.HasRun && pendingResult != null) PresentResultAsync().Forget();
        }

        /// <summary>先释放地图再呈现服务器结果回执；确认失败保留文件与内存结果供重试。</summary>
        private async UniTaskVoid PresentResultAsync()
        {
            if (presentingResult || pendingResult == null) return;
            // 场景展示依赖未就绪时保留结果回执；后续展示请求可重试，不影响权威快照发布。
            var sceneFlow = this.GetSystem<ArcadeSceneFlowSystem>();
            if (sceneFlow == null) return;
            presentingResult = true;
            var result = pendingResult;
            var ownerStore = resultStore;
            try
            {
                var menu = await sceneFlow.ReturnToMainMenuAsync();
                if (!menu.Success) { Notify.ShowFloatTips(menu.Error); return; }
                if (result != pendingResult || ownerStore != resultStore) return;
                var scores = string.Join("\n", result.Scores.Select(pair => Loc.T("ui.arcade.hud.score", pair.Value)));
                Notify.ShowMessage(Loc.T("ui.arcade.result." + result.Outcome), scores,
                    new List<MessageButtonConfig> { new MessageButtonConfig {
                        ButtonText = Texts.CommonConfirm, Role = MessageButtonRole.Confirm,
                        OnClick = () => {
                            if (result != pendingResult || ownerStore != resultStore) return;
                            if (ownerStore != null && (!ownerStore.TrySave(result, out var saveError) || !ownerStore.TryAcknowledge(out saveError)))
                            { Notify.ShowFloatTips(saveError); return; }
                            pendingResult = null;
                        } } });
            }
            finally { presentingResult = false; }
        }

        private void CancelOperation()
        {
            operationGeneration++;
            operationCancellation?.Cancel();
            operationCancellation?.Dispose();
            operationCancellation = null;
            inputCancellation?.Cancel();
            inputCancellation?.Dispose();
            inputCancellation = null;
            pendingInputSequence = 0;
            inputPumpBusy = false;
            input?.Disable(true);
        }
    }
}

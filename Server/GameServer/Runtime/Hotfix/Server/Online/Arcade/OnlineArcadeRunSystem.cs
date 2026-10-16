using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ET.Server
{
    [EntitySystemOf(typeof(OnlineArcadeRunManagerComponent))]
    [FriendOf(typeof(OnlineArcadeRunManagerComponent))]
    [FriendOf(typeof(OnlineArcadeRunActor))]
    [FriendOf(typeof(OnlineRoomActor))]
    public static partial class OnlineArcadeRunManagerSystem
    {
        private const long CompletionRetryMilliseconds = 2000;
        private const long ArenaReconnectGraceMilliseconds = 60_000;
        internal const long SoloEndDelayMilliseconds = 600_000;

        [EntitySystem]
        private static void Awake(this OnlineArcadeRunManagerComponent self)
        {
            self.RemainingFailSaveCount = Math.Max(0, Options.Instance.OnlineArcadeFailSaveCount);
        }

        /// <summary>启动时载入所有未结束安全节点；损坏、重复身份或版本不支持时拒绝启动。</summary>
        public static void LoadStore(this OnlineArcadeRunManagerComponent self, string storePath)
        {
            if (string.IsNullOrWhiteSpace(storePath))
                throw new InvalidOperationException("OnlineArcadeStore path is empty.");
            self.StorePath = Path.GetFullPath(storePath);
            string parent = Path.GetDirectoryName(self.StorePath);
            if (string.IsNullOrWhiteSpace(parent))
                throw new InvalidOperationException("OnlineArcadeStore parent directory is invalid.");
            Directory.CreateDirectory(parent);

            if (!File.Exists(self.StorePath)) return;
            string json = File.ReadAllText(self.StorePath);
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidOperationException("OnlineArcadeStore JSON is empty.");
            OnlineArcadeRunStoreSnapshot snapshot = JsonSerializer.Deserialize<OnlineArcadeRunStoreSnapshot>(json, JsonOptions()) ??
                                                    throw new InvalidOperationException("OnlineArcadeStore JSON is invalid.");
            ValidateStore(snapshot);
            foreach (OnlineArcadeRunRecord record in snapshot.Runs.OrderBy(x => x.RunId)) self.AddLoaded(record);
        }

        /// <summary>
        /// 为恰好两名在线原始成员创建首个安全节点。文件提交成功后才创建 Actor；失败不锁房间。
        /// </summary>
        public static async ETTask<OnlineArcadeRunRecord> CreateForRoom(this OnlineArcadeRunManagerComponent self,
            OnlineRoomActor room, long requestingPlayerId, string operationId)
        {
            if (room == null || room.IsDisposed || room.Purpose != OnlineRoomPurpose.Arcade || room.Closing ||
                room.OwnerPlayerId != requestingPlayerId || room.Members.Count != OnlineRoomSnapshotHelper.ArcadeCapacity ||
                room.Members.Values.Any(x => x.ConnectionState != OnlineRoomMemberConnectionState.Online) ||
                string.IsNullOrWhiteSpace(operationId) || operationId.Length > 64)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "街机房间尚不满足开局条件");

            if (self.RunIdsByRoom.TryGetValue(room.RoomId, out long existingRunId))
            {
                OnlineArcadeRunActor existingActor = self.Get(existingRunId);
                await self.BroadcastRunChanged(existingActor);
                return Clone(existingActor.Record);
            }
            foreach (long playerId in room.Members.Keys)
                if (self.RunIdsByPlayer.ContainsKey(playerId))
                    throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "成员已有未结束的街机对局");

            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            if (!string.Equals(room.ArcadeConfigFingerprint, rule.ConfigFingerprint, StringComparison.Ordinal))
                throw new RpcException(ErrorCode.ERR_OnlineRoomVersionMismatch, "街机配置版本与服务器不一致");

            long runId = IdGenerater.Instance.GenerateId();
            OnlineArcadeRunRecord candidate = BuildInitialRecord(runId, room, rule);
            try
            {
                self.CommitWithCandidate(candidate, 0);
            }
            catch (Exception exception)
            {
                Log.Error($"online arcade save failed stage=Create runId={runId} roomId={room.RoomId} exception={exception}");
                throw new RpcException(ErrorCode.ERR_OnlineArcadeSaveFailed, "街机安全节点保存失败，请重试");
            }

            self.AddLoaded(candidate);
            await room.LockForArcadeRun();
            self.TryStartTransitBattle(self.Get(runId));
            // 安全节点和房间锁都提交后再通知双方；非房主依赖该推送进入同一街机运行局。
            await self.BroadcastRunChanged(self.Get(runId));
            return Clone(candidate);
        }

        public static OnlineArcadeRunActor Get(this OnlineArcadeRunManagerComponent self, long runId) =>
            self.Runs.TryGetValue(runId, out EntityRef<OnlineArcadeRunActor> actorRef) ? actorRef : null;

        public static OnlineArcadeRunRecord GetForPlayer(this OnlineArcadeRunManagerComponent self, long playerId)
        {
            OnlineArcadeRunActor actor = self.GetActorForPlayer(playerId);
            return actor == null || actor.IsDisposed ? null : Clone(actor.Record);
        }

        public static OnlineArcadeRunActor GetActorForPlayer(this OnlineArcadeRunManagerComponent self, long playerId)
        {
            if (!self.RunIdsByPlayer.TryGetValue(playerId, out long runId)) return null;
            return self.Get(runId);
        }

        /// <summary>同步原始房间成员连接状态；竞技场断线立即暂停，安全节点仅累计单独结束资格时间。</summary>
        public static async ETTask SetPlayerConnection(this OnlineArcadeRunManagerComponent self, long roomId,
            long playerId, bool connected)
        {
            if (!self.RunIdsByRoom.TryGetValue(roomId, out long runId)) return;
            OnlineArcadeRunActor actor = self.Get(runId);
            if (actor == null || actor.IsDisposed || actor.Record.Players.All(x => x.PlayerId != playerId)) return;
            long now = TimeInfo.Instance.ServerNow();
            if (connected)
            {
                if (!actor.DisconnectedAtByPlayer.Remove(playerId)) return;
                if (actor.Battle != null && actor.DisconnectedAtByPlayer.Count == 0)
                {
                    actor.ArenaReconnectDeadline = 0;
                    actor.Battle.LastTickAt = now;
                    actor.Battle.InputsDirty = true;
                }
            }
            else
            {
                if (!actor.DisconnectedAtByPlayer.TryAdd(playerId, now)) return;
                if (actor.Battle != null)
                {
                    actor.ArenaReconnectDeadline = now + ArenaReconnectGraceMilliseconds;
                    foreach (OnlineArcadeBattlePlayerRuntime player in actor.Battle.Players.Values)
                    { player.MoveX = 0f; player.MoveZ = 0f; player.Firing = false; }
                }
            }
            if (connected) self.TryStartTransitBattle(actor);
            await self.BroadcastRunChanged(actor);
        }

        /// <summary>提交高频战斗输入；不写安全节点，旧序号只返回当前权威快照。</summary>
        public static (bool Accepted, long LastAcceptedInputSequence, OnlineArcadeRunSnapshot Snapshot) SubmitBattleInput(
            this OnlineArcadeRunManagerComponent self, long playerId, int arenaNumber, long inputSequence,
            float moveX, float moveZ, float aimX, float aimZ, bool firing, bool useBomb, bool useDash, int roundGeneration = 0, bool roundReady = false, int exitTarget = 0, bool confirmExit = false)
        {
            OnlineArcadeRunActor actor = self.GetActorForPlayer(playerId);
            if (actor == null || actor.IsDisposed || actor.Battle == null)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "当前没有可操作的竞技场现场");
            var result = actor.SubmitInput(playerId, arenaNumber, inputSequence, moveX, moveZ, aimX, aimZ, firing,
                useBomb, useDash, roundGeneration, roundReady, exitTarget, confirmExit);
            return (result.Accepted, result.LastAcceptedInputSequence, actor.BuildSnapshot());
        }

        /// <summary>
        /// 执行房主联机街机测试指令。身份、房主权限和暂停状态均在权威场景复核；成功修改后立即广播双方。
        /// </summary>
        public static async ETTask<ServerTestCmdRunResult> ExecuteTestCommand(
            this OnlineArcadeRunManagerComponent self, long playerId, string argumentText)
        {
            long now = TimeInfo.Instance.ServerNow();
            OnlineArcadeRunActor actor = self.GetActorForPlayer(playerId);
            if (actor == null || actor.IsDisposed)
                return TestFailure(ServerTestCmdResultCode.ExecutionFailed, "当前没有进行中的联机街机局", now);

            OnlineRoomActor room = self.Root().GetComponent<OnlineRoomManagerComponent>()?.Get(actor.Record.RoomId);
            if (room == null || room.IsDisposed || room.OwnerPlayerId != playerId)
                return TestFailure(ServerTestCmdResultCode.ExecutionFailed, "只有当前街机房主可以执行该指令", now);

            string[] args = (argumentText ?? string.Empty).Split(
                (char[])null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (args.Length == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
                return TestSuccess(actor.BuildTestStatus(), now);

            if (actor.Battle == null)
                return TestFailure(ServerTestCmdResultCode.ExecutionFailed, "当前不在联机街机战斗现场", now);
            if (actor.DisconnectedAtByPlayer.Count > 0)
                return TestFailure(ServerTestCmdResultCode.ExecutionFailed, "队友断线暂停期间不能修改战斗状态", now);
            if (!actor.Battle.Players.TryGetValue(playerId, out OnlineArcadeBattlePlayerRuntime player))
                return TestFailure(ServerTestCmdResultCode.ExecutionFailed, "房主不属于当前战斗现场", now);

            string summary;
            try
            {
                if (args.Length == 1 && string.Equals(args[0], "heal", StringComparison.OrdinalIgnoreCase))
                    summary = actor.HealPlayerForTests(player);
                else if (args.Length == 2 && string.Equals(args[0], "damage", StringComparison.OrdinalIgnoreCase) &&
                         TryParsePositive(args[1], out int damage))
                    summary = actor.DamagePlayerForTests(player, damage);
                else if (args.Length == 2 && string.Equals(args[0], "gold", StringComparison.OrdinalIgnoreCase) &&
                         TryParsePositive(args[1], out int gold))
                    summary = actor.AddGoldForTests(playerId, gold);
                else if (args.Length == 2 && string.Equals(args[0], "bombs", StringComparison.OrdinalIgnoreCase) &&
                         TryParseResource(args[1], out int bombs))
                    summary = actor.SetResourceForTests(playerId, true, bombs);
                else if (args.Length == 2 && string.Equals(args[0], "dashes", StringComparison.OrdinalIgnoreCase) &&
                         TryParseResource(args[1], out int dashes))
                    summary = actor.SetResourceForTests(playerId, false, dashes);
                else if (args.Length == 2 && string.Equals(args[0], "drop", StringComparison.OrdinalIgnoreCase))
                    summary = actor.ApplyDropForTests(player, args[1]);
                else if (args.Length == 2 && string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(args[1], "wave", StringComparison.OrdinalIgnoreCase))
                    summary = actor.ClearWaveForTests();
                else if (args.Length == 2 && string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(args[1], "arena", StringComparison.OrdinalIgnoreCase))
                    summary = actor.ClearArenaForTests();
                else
                    return TestFailure(ServerTestCmdResultCode.InvalidInput,
                        "用法：$arcade status|heal|damage <正整数>|gold <正整数>|bombs <0-9999>|dashes <0-9999>|drop <DropId>|clear wave|clear arena",
                        now);
            }
            catch (RpcException exception)
            {
                return TestFailure(ServerTestCmdResultCode.ExecutionFailed, exception.Message, now);
            }
            catch (OverflowException)
            {
                return TestFailure(ServerTestCmdResultCode.InvalidInput, "测试数值超出允许范围", now);
            }

            actor.Battle.BattleRevision++;
            await self.BroadcastRunChanged(actor);
            return TestSuccess(summary, TimeInfo.Instance.ServerNow());
        }

        private static bool TryParsePositive(string value, out int parsed) =>
            int.TryParse(value, out parsed) && parsed > 0;

        private static bool TryParseResource(string value, out int parsed) =>
            int.TryParse(value, out parsed) && parsed is >= 0 and <= 9999;

        private static ServerTestCmdRunResult TestSuccess(string message, long now) =>
            new(true, true, ServerTestCmdResultCode.Success, message, now);

        private static ServerTestCmdRunResult TestFailure(ServerTestCmdResultCode code, string message, long now) =>
            new(true, false, code, message, now);

        /// <summary>
        /// 串行执行安全节点写操作。候选节点和幂等记录一起原子落盘，保存失败时 Actor 内存保持原样。
        /// </summary>
        public static async ETTask<(bool Accepted, OnlineArcadeRunSnapshot Snapshot)> OperateSafeCheckpoint(
            this OnlineArcadeRunManagerComponent self, long playerId, string operationId, long expectedRevision,
            int actionKind, bool ready)
        {
            // 已撤销的购买/转账数值不得通过旧幂等记录重放。
            if (!Enum.IsDefined(typeof(OnlineArcadeSafeCheckpointActionKind), actionKind))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "安全节点操作类型无效");
            if (!self.RunIdsByPlayer.TryGetValue(playerId, out long runId))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "没有可操作的联机街机局");
            OnlineArcadeRunActor actor = self.Get(runId);
            if (actor == null || actor.IsDisposed)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "联机街机局不可用");
            if (actor.Record.Players.Any(x => x.SettlementState != 0) &&
                (OnlineArcadeSafeCheckpointActionKind)actionKind != OnlineArcadeSafeCheckpointActionKind.ConfirmSettlement)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "结算事务已开始，只能继续重试结算");
            if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 64)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeOperationConflict, "操作标识无效或冲突");

            string fingerprint = OperationFingerprint(playerId, expectedRevision, actionKind, ready);
            OnlineArcadeOperationRecord previous = actor.Record.Operations?.FirstOrDefault(x =>
                string.Equals(x.OperationId, operationId, StringComparison.Ordinal));
            if (previous != null)
            {
                if (previous.PlayerId != playerId || !string.Equals(previous.Fingerprint, fingerprint, StringComparison.Ordinal))
                    throw new RpcException(ErrorCode.ERR_OnlineArcadeOperationConflict, "操作标识已用于不同参数");
                if ((OnlineArcadeSafeCheckpointActionKind)actionKind == OnlineArcadeSafeCheckpointActionKind.ConfirmSettlement &&
                    await self.TryCompleteSettlement(actor, playerId)) return (true, null);
                if ((OnlineArcadeSafeCheckpointActionKind)actionKind == OnlineArcadeSafeCheckpointActionKind.ConfirmAbandon &&
                    actor.Record.Players.All(x => x.AbandonConfirmed) && await self.TryCommitRunFailure(actor))
                    return (true, null);
                return (true, actor.BuildSnapshot());
            }
            if (actor.Battle != null || actor.Record.Phase is not (OnlineArcadeRunPhase.SafeCheckpoint or OnlineArcadeRunPhase.Victory))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "当前不在安全节点");
            if (actor.Record.Phase == OnlineArcadeRunPhase.Victory &&
                (OnlineArcadeSafeCheckpointActionKind)actionKind != OnlineArcadeSafeCheckpointActionKind.ConfirmSettlement)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "通关节点只允许结算");
            if (expectedRevision != actor.Record.Revision)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "安全节点版本已更新，请刷新后重试");

            OnlineArcadeRunRecord candidate = Clone(actor.Record);
            EnsureCollections(candidate);
            OnlineArcadePlayerRecord player = candidate.Players.FirstOrDefault(x => x.PlayerId == playerId) ??
                                              throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "玩家不属于该街机局");
            ApplyCheckpointIntent(candidate, player, actionKind, ready);

            candidate.Revision = checked(candidate.Revision + 1);
            candidate.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            candidate.Operations.Add(new OnlineArcadeOperationRecord
            {
                OperationId = operationId,
                PlayerId = playerId,
                ActionKind = actionKind,
                Fingerprint = fingerprint,
                CompletedAtUtc = candidate.UpdatedAtUtc,
            });
            TrimOperations(candidate.Operations);
            bool startChallenge = candidate.Players.All(x => x.ChallengeReady && x.Lives > 0);
            OnlineArcadeBattleRuntime preparedBattle = (OnlineArcadeSafeCheckpointActionKind)actionKind == OnlineArcadeSafeCheckpointActionKind.SetReady &&
                candidate.Players.Any(x => x.Lives > 0) && candidate.Players.Where(x => x.Lives > 0).All(x => x.Ready)
                ? OnlineArcadeRunActorSystem.PrepareBattle(candidate, false)
                : (OnlineArcadeSafeCheckpointActionKind)actionKind == OnlineArcadeSafeCheckpointActionKind.SetChallengeReady && startChallenge
                    ? OnlineArcadeRunActorSystem.PrepareBattle(candidate, true) : null;
            if (preparedBattle?.RoundLayout != null) CaptureRoundCheckpoint(candidate, preparedBattle);
            if (startChallenge) candidate.ChallengeAttempted = true;
            try
            {
                self.CommitWithCandidate(candidate, candidate.RunId);
            }
            catch (Exception exception)
            {
                Log.Error($"online arcade save failed stage=SafeCheckpoint runId={candidate.RunId} playerId={playerId} exception={exception}");
                throw new RpcException(ErrorCode.ERR_OnlineArcadeSaveFailed, "街机安全节点保存失败，请重试");
            }

            CommitActorRecord(actor, candidate);
            if (preparedBattle != null)
            {
                actor.StartBattle(preparedBattle);
                self.StartBattleTick(actor, actor.BattleGeneration);
            }
            if ((OnlineArcadeSafeCheckpointActionKind)actionKind == OnlineArcadeSafeCheckpointActionKind.ConfirmSettlement)
            {
                bool ended = await self.TryCompleteSettlement(actor, playerId);
                if (ended) return (true, null);
            }
            else if ((OnlineArcadeSafeCheckpointActionKind)actionKind == OnlineArcadeSafeCheckpointActionKind.ConfirmAbandon &&
                     actor.Record.Players.All(x => x.AbandonConfirmed))
            {
                if (await self.TryCommitRunFailure(actor)) return (true, null);
            }
            await self.BroadcastRunChanged(actor);
            return (true, actor.BuildSnapshot());
        }

        /// <summary>按玩家顺序独立保存本局结果；任一步失败保留已成功玩家状态，后续操作可继续重试。</summary>
        /// <summary>只处理准备、挑战和结果确认意图；无交易分支，候选落盘前不改变 Actor。</summary>
        private static void ApplyCheckpointIntent(OnlineArcadeRunRecord candidate, OnlineArcadePlayerRecord player,
            int actionKind, bool ready)
        {
            switch ((OnlineArcadeSafeCheckpointActionKind)actionKind)
            {
                case OnlineArcadeSafeCheckpointActionKind.SetReady:
                    if (ready && player.Lives <= 0)
                        throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "生命为零的成员继续观战，不能准备开战");
                    player.Ready = ready;
                    player.ChallengeReady = false;
                    break;
                case OnlineArcadeSafeCheckpointActionKind.SetChallengeReady:
                    if (ready && (player.Lives <= 0 || string.IsNullOrWhiteSpace(candidate.ChallengeId) ||
                                  candidate.ChallengeAttempted))
                        throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "当前特殊挑战不可进入");
                    player.ChallengeReady = ready;
                    player.Ready = false;
                    break;
                case OnlineArcadeSafeCheckpointActionKind.ConfirmSettlement:
                    player.SettlementConfirmed = true;
                    player.AbandonConfirmed = false;
                    player.Ready = false;
                    player.ChallengeReady = false;
                    break;
                case OnlineArcadeSafeCheckpointActionKind.ConfirmAbandon:
                    player.AbandonConfirmed = true;
                    player.SettlementConfirmed = false;
                    player.Ready = false;
                    player.ChallengeReady = false;
                    break;
                default:
                    throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "安全节点操作类型无效");
            }
            if ((OnlineArcadeSafeCheckpointActionKind)actionKind is OnlineArcadeSafeCheckpointActionKind.SetReady or
                OnlineArcadeSafeCheckpointActionKind.SetChallengeReady)
                foreach (OnlineArcadePlayerRecord member in candidate.Players)
                { member.SettlementConfirmed = false; member.AbandonConfirmed = false; }

        }

        private static async ETTask<bool> TryCompleteSettlement(this OnlineArcadeRunManagerComponent self,
            OnlineArcadeRunActor actor, long requestingPlayerId)
        {
            bool allConfirmed = actor.Record.Players.All(x => x.SettlementConfirmed || x.SettlementState != 0);
            long now = TimeInfo.Instance.ServerNow();
            OnlineArcadePlayerRecord disconnected = actor.Record.Players.FirstOrDefault(x => x.PlayerId != requestingPlayerId);
            bool soloEligible = disconnected != null && actor.DisconnectedAtByPlayer.TryGetValue(disconnected.PlayerId,
                out long disconnectedAt) && now - disconnectedAt >= SoloEndDelayMilliseconds;
            if (!allConfirmed && !soloEligible) return false;

            IEnumerable<OnlineArcadePlayerRecord> order = actor.Record.Players
                .OrderByDescending(x => x.PlayerId == requestingPlayerId);
            foreach (OnlineArcadePlayerRecord source in order)
            {
                OnlineArcadePlayerRecord current = actor.Record.Players.First(x => x.PlayerId == source.PlayerId);
                if (current.SettlementState != 0) continue;
                try { self.CommitPlayerSettlement(actor, current.PlayerId); }
                catch (Exception exception)
                {
                    Log.Error($"online arcade save failed stage=PlayerSettlement runId={actor.Record.RunId} playerId={current.PlayerId} exception={exception}");
                    return false;
                }
            }
            return actor.Record.Players.All(x => x.SettlementState != 0) && await self.TryCommitRunFailure(actor);
        }

        private static void CommitPlayerSettlement(this OnlineArcadeRunManagerComponent self,
            OnlineArcadeRunActor actor, long playerId)
        {
            if (actor.Record.Players.First(x => x.PlayerId == playerId).SettlementState != 0) return;
            var candidate = Clone(actor.Record);
            var player = candidate.Players.First(x => x.PlayerId == playerId);
            player.SettlementState = 1;
            candidate.Revision = checked(candidate.Revision + 1);
            candidate.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            // 保留双人确认及断线等待规则，只提交本局结果，不发放或登记局外奖励。
            self.CommitWithCandidate(candidate, candidate.RunId);
            CommitActorRecord(actor, candidate);
        }

        /// <summary>管理器拥有战斗 Tick 与安全节点提交顺序，确保现场完成不会绕过持久化事务。</summary>
        private static void StartBattleTick(this OnlineArcadeRunManagerComponent self, OnlineArcadeRunActor actor,
            int generation)
        {
            BattleTickLoop(self, actor, generation).Coroutine();
        }

        private static async ETTask BattleTickLoop(OnlineArcadeRunManagerComponent self,
            OnlineArcadeRunActor actor, int generation)
        {
            while (!self.IsDisposed && actor != null && !actor.IsDisposed && actor.Battle != null &&
                   actor.BattleGeneration == generation)
            {
                await self.Root().GetComponent<TimerComponent>().WaitAsync(100);
                if (self.IsDisposed || actor.IsDisposed || actor.Battle == null ||
                    actor.BattleGeneration != generation) return;
                long now = TimeInfo.Instance.ServerNow();
                if (actor.DisconnectedAtByPlayer.Count > 0)
                {
                    if (actor.ArenaReconnectDeadline > 0 && now >= actor.ArenaReconnectDeadline)
                    {
                        if (self.TryRollbackDisconnectedBattle(actor))
                        { await self.BroadcastRunChanged(actor); return; }
                        actor.ArenaReconnectDeadline = now + CompletionRetryMilliseconds;
                    }
                    continue;
                }
                bool changed;
                if (actor.Battle?.RoundTransitionPending == true)
                {
                    changed = self.TryCommitRoundTransition(actor);
                    if (changed) await self.BroadcastRunChanged(actor);
                    continue;
                }
                changed = actor.TickBattle(now);
                if (changed) await self.BroadcastRunChanged(actor);

                OnlineArcadeBattleRuntime battle = actor.Battle;
                if (battle == null || now < battle.NextCompletionAttemptAt) continue;
                if (battle.AllPlayersDefeated)
                {
                    if (await self.TryCommitRunFailure(actor)) return;
                    battle.NextCompletionAttemptAt = now + CompletionRetryMilliseconds;
                }
                else if (battle.ChallengeCompletionReady)
                {
                    if (self.TryCommitChallengeCompletion(actor))
                    {
                        await self.BroadcastRunChanged(actor);
                        return;
                    }
                    battle.NextCompletionAttemptAt = now + CompletionRetryMilliseconds;
                }
                else if (battle.ArenaCompletionReady)
                {
                    if (self.TryCommitArenaCompletion(actor))
                    {
                        await self.BroadcastRunChanged(actor);
                        return;
                    }
                    battle.NextCompletionAttemptAt = now + CompletionRetryMilliseconds;
                }
            }
        }

        /// <summary>六十秒超时后只提交“取消准备”的上一安全节点；现场生命、金币和掉落全部丢弃。</summary>
        private static bool TryRollbackDisconnectedBattle(this OnlineArcadeRunManagerComponent self,
            OnlineArcadeRunActor actor)
        {
            // 现场金币、命数和消费均属未完成场事务；不能用工作记录伪装回滚。
            OnlineArcadeRunRecord candidate;
            try { candidate = CloneCommittedRecord(actor); }
            catch (Exception exception)
            {
                Log.Error($"online arcade rollback checkpoint invalid runId={actor.Record.RunId} exception={exception}");
                return false;
            }
            foreach (OnlineArcadePlayerRecord player in candidate.Players)
            {
                player.Ready = false;
                player.ChallengeReady = false;
            }
            candidate.Phase = OnlineArcadeRunPhase.SafeCheckpoint;
            candidate.Revision = checked(Math.Max(candidate.Revision, actor.Record.Revision) + 1);
            candidate.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
            try { self.CommitWithCandidate(candidate, candidate.RunId); }
            catch (Exception exception)
            {
                Log.Error($"online arcade save failed stage=DisconnectRollback runId={candidate.RunId} exception={exception}");
                return false;
            }
            CommitActorRecord(actor, candidate);
            actor.BattleGeneration++;
            actor.Battle = null;
            actor.ArenaReconnectDeadline = 0;
            return true;
        }

        /// <summary>最终轮完成时构造下一安全节点；文件成功前不切换阶段。</summary>
        private static bool TryCommitArenaCompletion(this OnlineArcadeRunManagerComponent self,
            OnlineArcadeRunActor actor)
        {
            OnlineArcadeRunRecord candidate = Clone(actor.Record);
            EnsureCollections(candidate);
            ArcadeArenaConfig arena = ResolveArenaConfig(candidate.IsCasualRoute ? candidate.ArenaConfigNumber : candidate.NextArena);
            try
            {
                // 新休闲前两场只有个人积分，清场不凭旧配置额外复制购物现金。
                int arenaGold = FirepowerLoop.Arcade.ArcadeOpeningRules.UsesEarlyRounds(candidate.IsCasualRoute, arena.ArenaNumber)
                    ? 0 : ScaleGold(arena.ClearGold, candidate.NextArena, ServerConfig.Instance.ArcadeRule.GoldGrowthPerArena);
                int completedArena = candidate.NextArena;
                int nextArena = checked(completedArena + 1);
                bool victory = candidate.IsCasualRoute && actor.Battle.FinalBossDefeated;
                if (candidate.IsCasualRoute)
                {
                    if (victory ? completedArena != 10 || string.IsNullOrEmpty(arena.FinalBossId) :
                        completedArena >= 10 || !arena.NextArenaNumbers.Contains(actor.Battle.SelectedExitConfigNumber))
                        throw new InvalidOperationException("Invalid casual route completion.");
                    if (!victory) candidate.ArenaConfigNumber = actor.Battle.SelectedExitConfigNumber;
                }
                candidate.RoundLayoutId = string.Empty; candidate.RoundNumber = 1;
                candidate.RoundLayoutVersion = 0; candidate.RoundBossEncounter = false;
                candidate.DestroyedCrateIds.Clear();candidate.CrateHealth.Clear();candidate.DestroyedBarrelIds.Clear();
                foreach (var member in candidate.Players) { member.RoundHealth=0;member.RoundTemporaryWeaponId=string.Empty;member.RoundTemporaryWeaponSeconds=0;member.RoundPowerups.Clear(); }
                candidate.CompletedArena = completedArena;
                candidate.NextArena = nextArena;
                foreach (OnlineArcadePlayerRecord player in candidate.Players)
                {
                    player.RunGold = checked(player.RunGold + arenaGold);
                    ApplyConfiguredRewards(player, completedArena, arena.IsBoss);
                    player.Ready = false;
                    player.ChallengeReady = false;
                }
                candidate.Phase = victory ? OnlineArcadeRunPhase.Victory : OnlineArcadeRunPhase.SafeCheckpoint;
                candidate.ChallengeId = string.Empty;
                candidate.ChallengeAttempted = false;
                candidate.Revision = checked(candidate.Revision + 1);
                candidate.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                self.CommitWithCandidate(candidate, candidate.RunId);
            }
            catch (Exception exception)
            {
                Log.Error($"online arcade save failed stage=ArenaComplete runId={candidate.RunId} arena={candidate.NextArena} exception={exception}");
                return false;
            }

            CommitActorRecord(actor, candidate);
            actor.BattleGeneration++;
            actor.Battle = null;
            if (candidate.Phase != OnlineArcadeRunPhase.Victory) self.TryStartTransitBattle(actor);
            return true;
        }

        /// <summary>挑战结束恢复已提交的尝试节点，只叠加成功奖励；现场消费不进入安全节点。</summary>
        private static bool TryCommitChallengeCompletion(this OnlineArcadeRunManagerComponent self,
            OnlineArcadeRunActor actor)
        {
            OnlineArcadeBattleRuntime battle = actor.Battle;
            OnlineArcadeRunRecord candidate = null;
            try
            {
                candidate = CloneCommittedRecord(actor);
                EnsureCollections(candidate);
                foreach (OnlineArcadePlayerRecord player in candidate.Players)
                {
                    player.Ready = false;
                    player.ChallengeReady = false;
                    if (battle.ChallengeSucceeded)
                        ApplyChallengeRewards(player, Math.Max(1, candidate.CompletedArena));
                }
                candidate.Phase = OnlineArcadeRunPhase.SafeCheckpoint;
                candidate.Revision = checked(Math.Max(candidate.Revision, actor.Record.Revision) + 1);
                candidate.UpdatedAtUtc = DateTime.UtcNow.ToString("O");
                self.CommitWithCandidate(candidate, candidate.RunId);
            }
            catch (Exception exception)
            {
                Log.Error($"online arcade save failed stage=ChallengeComplete runId={actor.Record.RunId} challenge={battle.ChallengeId} exception={exception}");
                return false;
            }
            CommitActorRecord(actor, candidate);
            actor.BattleGeneration++;
            actor.Battle = null;
            return true;
        }

        /// <summary>双方生命归零时先原子删除运行局，再清理索引并通知两端；保存失败保持冻结现场。</summary>
        private static async ETTask<bool> TryCommitRunFailure(this OnlineArcadeRunManagerComponent self,
            OnlineArcadeRunActor actor)
        {
            long runId = actor.Record.RunId;
            long roomId = actor.Record.RoomId;
            long[] playerIds = actor.Record.Players.Select(x => x.PlayerId).ToArray();
            try
            {
                self.CommitWithoutRun(runId);
            }
            catch (Exception exception)
            {
                Log.Error($"online arcade save failed stage=RunFailed runId={runId} exception={exception}");
                return false;
            }

            actor.BattleGeneration++;
            actor.Battle = null;
            self.Runs.Remove(runId);
            self.RunIdsByRoom.Remove(roomId);
            foreach (long playerId in playerIds) self.RunIdsByPlayer.Remove(playerId);
            await self.BroadcastRunEnded(playerIds);
            OnlineRoomActor room = self.Parent is Scene scene
                ? scene.GetComponent<OnlineRoomManagerComponent>()?.Get(roomId)
                : null;
            if (room != null && !room.IsDisposed) await room.UnlockAfterArcadeRun();
            if (!actor.IsDisposed) self.RemoveChild(runId);
            return true;
        }

        private static void ApplyConfiguredRewards(OnlineArcadePlayerRecord player, int arenaNumber, bool boss)
        {
            // 独立街机首轮仅保留分数与本局结果，不生成局外待领奖励。
        }

        private static void ApplyChallengeRewards(OnlineArcadePlayerRecord player, int arenaNumber)
        {
            // 独立街机首轮仅保留分数与本局结果，不生成局外待领奖励。
        }

        private static string ResolveChallengeId(int completedArena)
        {
            ArcadeChallengeConfig challenge = ServerConfig.Instance.Tables.TbArcadeChallenge.DataList
                .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault(x => completedArena >= x.StartArena &&
                    (completedArena - x.StartArena) % x.Interval == 0);
            return challenge?.Id ?? string.Empty;
        }

        private static int ScaleGold(int baseGold, int arenaNumber, float growth) =>
            Math.Max(0, (int)MathF.Round(baseGold * (1f + Math.Max(0, arenaNumber - 1) * growth)));

        private static ArcadeArenaConfig ResolveArenaConfig(int arenaNumber)
        {
            List<ArcadeArenaConfig> arenas = ServerConfig.Instance.Tables.TbArcadeArena.DataList;
            if (arenas.Count == 0) throw new InvalidOperationException("Arcade arena config is empty.");
            ArcadeArenaConfig exact = ServerConfig.Instance.Tables.TbArcadeArena.GetOrDefault(arenaNumber);
            return exact ?? arenas.OrderBy(x => x.ArenaNumber).ElementAt((arenaNumber - 1) % arenas.Count);
        }

        /// <summary>普通恢复点先保有提交节点，再以双方准备后的工作态构造下一场。</summary>
        private static bool TryStartTransitBattle(this OnlineArcadeRunManagerComponent self, OnlineArcadeRunActor actor)
        {
            if (actor == null || actor.IsDisposed || actor.Record == null || actor.Battle != null || actor.Record.Phase != OnlineArcadeRunPhase.SafeCheckpoint ||
                !actor.Record.IsCasualRoute || !actor.Record.Players.Any(p => p.Lives > 0) ||
                actor.Record.Players.Any(p => p.Lives > 0 && !p.Ready) ||
                actor.DisconnectedAtByPlayer.Count > 0) return false;
            try
            {
                var working = Clone(actor.Record);
                foreach (var player in working.Players) player.Ready = true;
                var battle = OnlineArcadeRunActorSystem.PrepareBattle(working, false);
                if (battle.RoundLayout != null && string.IsNullOrEmpty(working.RoundLayoutId))
                { CaptureRoundCheckpoint(working,battle);self.CommitWithCandidate(working,working.RunId);CommitActorRecord(actor,working); }
                actor.Record = working;
                actor.StartBattle(battle);
                self.StartBattleTick(actor, actor.BattleGeneration);
                return true;
            }
            catch (Exception e)
            {
                Log.Error($"online arcade transit failed runId={actor.Record.RunId}: {e}");
                return false;
            }
        }

        private static OnlineArcadeRunRecord BuildInitialRecord(long runId, OnlineRoomActor room, ArcadeRuleConfig rule)
        {
            string now = DateTime.UtcNow.ToString("O");
            OnlineArcadeRunRecord record = new()
            {
                RunId = runId,
                RoomId = room.RoomId,
                Revision = 1,
                Phase = OnlineArcadeRunPhase.SafeCheckpoint,
                CompletedArena = 0,
                NextArena = 1,
                IsCasualRoute = true,
                ArenaConfigNumber = 1,
                ClientVersion = room.ClientVersion ?? string.Empty,
                ConfigFingerprint = rule.ConfigFingerprint,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            foreach (OnlineRoomMember member in room.Members.Values.OrderBy(x => x.JoinSequence))
            {
                record.Players.Add(new OnlineArcadePlayerRecord
                {
                    PlayerId = member.PlayerId,
                    DisplayName = member.DisplayName ?? string.Empty,
                    Lives = rule.InitialLives,
                    RunGold = 0,
                    Bombs = rule.InitialBombs,
                    Dashes = rule.InitialDashes,
                    WeaponId = rule.BaseWeaponId,
                    Ready = false,
                    UpgradeStacks = new Dictionary<string, int>(StringComparer.Ordinal),
                    Consumables = new Dictionary<string, int>(StringComparer.Ordinal),
                    EquipmentBySlot = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["weapon"] = rule.BaseWeaponId,
                    },
                });
            }
            return record;
        }

        private static void AddLoaded(this OnlineArcadeRunManagerComponent self, OnlineArcadeRunRecord source)
        {
            OnlineArcadeRunRecord record = Clone(source);
            NormalizeLoadedCheckpoint(record);
            EnsureCollections(record);
            // 恢复资源与路线，已实际尝试的特殊挑战仍按原恢复规则。
            if (record.IsCasualRoute && record.Phase == OnlineArcadeRunPhase.SafeCheckpoint && !record.ChallengeAttempted)
            {
                record.ChallengeId = string.Empty;
            }
            // 服务重启不恢复瞬时战斗现场，也不沿用旧的双方准备提交。
            foreach (OnlineArcadePlayerRecord player in record.Players)
            {
                player.Ready = false;
                player.ChallengeReady = false;
            }
            OnlineArcadeRunActor actor = self.AddChildWithId<OnlineArcadeRunActor>(record.RunId);
            CommitActorRecord(actor, record);
            self.Runs.Add(record.RunId, actor);
            self.RunIdsByRoom.Add(record.RoomId, record.RunId);
            foreach (OnlineArcadePlayerRecord player in record.Players) self.RunIdsByPlayer.Add(player.PlayerId, record.RunId);
        }

        private static void CommitWithCandidate(this OnlineArcadeRunManagerComponent self, OnlineArcadeRunRecord candidate, long replacingRunId)
        {
            if (self.RemainingFailSaveCount > 0)
            {
                self.RemainingFailSaveCount--;
                throw new IOException("injected online arcade save failure");
            }
            OnlineArcadeRunStoreSnapshot snapshot = new();
            foreach (EntityRef<OnlineArcadeRunActor> actorRef in self.Runs.Values)
            {
                OnlineArcadeRunActor actor = actorRef;
                if (actor != null && !actor.IsDisposed && actor.Record.RunId != replacingRunId)
                    snapshot.Runs.Add(CloneCommittedRecord(actor));
            }
            snapshot.Runs.Add(Clone(candidate));
            WriteAtomic(self.StorePath, snapshot);
        }

        private static void CommitWithoutRun(this OnlineArcadeRunManagerComponent self, long removedRunId)
        {
            if (self.RemainingFailSaveCount > 0)
            {
                self.RemainingFailSaveCount--;
                throw new IOException("injected online arcade save failure");
            }
            OnlineArcadeRunStoreSnapshot snapshot = new();
            foreach (EntityRef<OnlineArcadeRunActor> actorRef in self.Runs.Values)
            {
                OnlineArcadeRunActor actor = actorRef;
                if (actor != null && !actor.IsDisposed && actor.Record.RunId != removedRunId)
                    snapshot.Runs.Add(CloneCommittedRecord(actor));
            }
            WriteAtomic(self.StorePath, snapshot);
        }

        private static void WriteAtomic(string path, OnlineArcadeRunStoreSnapshot snapshot)
        {
            string parent = Path.GetDirectoryName(path);
            Directory.CreateDirectory(parent!);
            string temp = Path.Combine(parent!, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions());
                using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private static void ValidateStore(OnlineArcadeRunStoreSnapshot snapshot)
        {
            if (snapshot.SchemaVersion is not (1 or 2)) throw new InvalidOperationException($"Unsupported OnlineArcadeStore schema version: {snapshot.SchemaVersion}");
            HashSet<long> runIds = new();
            HashSet<long> roomIds = new();
            HashSet<long> playerIds = new();
            foreach (OnlineArcadeRunRecord run in snapshot.Runs ?? new List<OnlineArcadeRunRecord>())
            {
                if (run == null || run.RunId <= 0 || run.RoomId <= 0 || !runIds.Add(run.RunId) || !roomIds.Add(run.RoomId) ||
                    run.Revision <= 0 || run.Phase is not (OnlineArcadeRunPhase.SafeCheckpoint or OnlineArcadeRunPhase.Victory) || run.NextArena <= 0 ||
                    string.IsNullOrWhiteSpace(run.ConfigFingerprint) || run.Players == null || run.Players.Count != 2)
                    throw new InvalidOperationException("OnlineArcadeStore contains an invalid run.");
                if (run.IsCasualRoute && (run.ArenaConfigNumber <= 0 || run.CompletedArena > 10 ||
                    (run.Phase == OnlineArcadeRunPhase.Victory) != (run.CompletedArena == 10) ||
                    run.NextArena != run.CompletedArena + 1 ||
                    ServerConfig.Instance.Tables.TbArcadeArena.GetOrDefault(run.ArenaConfigNumber) == null))
                    throw new InvalidOperationException("OnlineArcadeStore contains an invalid casual route.");
                foreach (OnlineArcadePlayerRecord player in run.Players)
                    if (player == null || player.PlayerId <= 0 || !playerIds.Add(player.PlayerId) || player.Lives < 0 ||
                        player.Score < 0 || player.ScoreMultiplier < 1 || player.MultiplierProgress < 0 ||
                        player.UpgradeStacks == null || player.Consumables == null || player.EquipmentBySlot == null)
                        throw new InvalidOperationException("OnlineArcadeStore contains an invalid player.");
                if(run.RoundNumber<1||run.RoundNumber>4||run.RoundGeneration<0||run.DestroyedCrateIds==null||run.CrateHealth==null||run.DestroyedBarrelIds==null||run.DestroyedBarrelIds.Any(id=>id<0)||run.DestroyedBarrelIds.Distinct().Count()!=run.DestroyedBarrelIds.Count||
                    run.CrateHealth.Any(p=>p.Key<=0||p.Value<=0||run.DestroyedCrateIds.Contains(p.Key))||
                    !string.IsNullOrEmpty(run.RoundLayoutId)&&(ServerConfig.Instance.Tables.TbArcadeRoundLayout.GetOrDefault(run.RoundLayoutId)?.Version!=run.RoundLayoutVersion||run.RoundLayoutId!="arena."+run.ArenaConfigNumber+".round."+run.RoundNumber))
                    throw new InvalidOperationException("OnlineArcadeStore contains an invalid round checkpoint.");
                if (run.Operations == null || run.Operations.GroupBy(x => x.OperationId, StringComparer.Ordinal).Any(x => x.Count() > 1))
                    throw new InvalidOperationException("OnlineArcadeStore contains invalid operation identities.");
            }
        }

        private static void EnsureCollections(OnlineArcadeRunRecord run)
        {
            run.ChallengeId ??= string.Empty;
            run.Operations ??= new List<OnlineArcadeOperationRecord>();
            foreach (OnlineArcadePlayerRecord player in run.Players)
            {
                player.UpgradeStacks ??= new Dictionary<string, int>(StringComparer.Ordinal);
                player.Consumables ??= new Dictionary<string, int>(StringComparer.Ordinal);
                player.EquipmentBySlot ??= new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        /// <summary>仅兼容本项目移除商店前的配置版本；不接受另一项目或任意未知版本。</summary>
        private static void NormalizeLoadedCheckpoint(OnlineArcadeRunRecord run)
        {
            const string previousVersion = "firepowerloop-20261014-v1";
            const string checkpointVersion = "firepowerloop-20261016-v2";
            if (run.ConfigFingerprint == previousVersion &&
                ServerConfig.Instance.ArcadeRule.ConfigFingerprint == checkpointVersion)
                run.ConfigFingerprint = checkpointVersion;
        }

        private static string OperationFingerprint(long playerId, long expectedRevision, int actionKind, bool ready) => $"{playerId}|{expectedRevision}|{actionKind}|{ready}";

        private static void TrimOperations(List<OnlineArcadeOperationRecord> operations)
        {
            const int maximum = 128;
            if (operations.Count <= maximum) return;
            operations.RemoveRange(0, operations.Count - maximum);
        }

        public static async ETTask BroadcastRunChanged(this OnlineArcadeRunManagerComponent self, OnlineArcadeRunActor actor)
        {
            OnlineArcadeRunRecord record = actor.Record;
            foreach (OnlineArcadePlayerRecord player in record.Players)
            {
                try
                {
                    ActorId playerActor = await self.Root().GetComponent<LocationProxyComponent>()
                        .Get(LocationType.OnlinePlayer, player.PlayerId);
                    R2P_OnlineArcadeRunChanged changed = R2P_OnlineArcadeRunChanged.Create();
                    changed.PlayerId = player.PlayerId;
                    changed.HasRun = true;
                    changed.Run = actor.BuildSnapshot();
                    self.Root().GetComponent<MessageSender>().Send(playerActor, changed);
                }
                catch (Exception exception)
                {
                    Log.Error($"online arcade push skipped playerId={player.PlayerId} runId={record.RunId} exception={exception}");
                }
            }
        }

        private static async ETTask BroadcastRunEnded(this OnlineArcadeRunManagerComponent self,
            IReadOnlyList<long> playerIds)
        {
            foreach (long playerId in playerIds)
            {
                try
                {
                    ActorId playerActor = await self.Root().GetComponent<LocationProxyComponent>()
                        .Get(LocationType.OnlinePlayer, playerId);
                    R2P_OnlineArcadeRunChanged changed = R2P_OnlineArcadeRunChanged.Create();
                    changed.PlayerId = playerId;
                    changed.HasRun = false;
                    self.Root().GetComponent<MessageSender>().Send(playerActor, changed);
                }
                catch (Exception exception)
                {
                    Log.Error($"online arcade end push skipped playerId={playerId} exception={exception}");
                }
            }
        }

        /// <summary>仅在加载或原子写盘成功后调用；工作态和提交点分别克隆，防止战斗修改污染节点。</summary>
        private static void CommitActorRecord(OnlineArcadeRunActor actor, OnlineArcadeRunRecord candidate)
        {
            OnlineArcadeRunRecord committed = Clone(candidate);
            OnlineArcadeRunRecord current = Clone(candidate);
            actor.CommittedRecord = committed;
            actor.Record = current;
        }

        /// <summary>保存其他局或回滚时读取提交点；身份缺失则拒绝，禁止退回到未完成工作态。</summary>
        private static OnlineArcadeRunRecord CloneCommittedRecord(OnlineArcadeRunActor actor)
        {
            if (actor.CommittedRecord == null || actor.Record == null ||
                actor.CommittedRecord.RunId != actor.Record.RunId)
                throw new InvalidOperationException("OnlineArcade committed checkpoint is missing or mismatched.");
            return Clone(actor.CommittedRecord);
        }

        public static OnlineArcadeRunRecord Clone(OnlineArcadeRunRecord value) => value == null
            ? null
            : JsonSerializer.Deserialize<OnlineArcadeRunRecord>(JsonSerializer.Serialize(value, JsonOptions()), JsonOptions());

        private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };
    }

    [EntitySystemOf(typeof(OnlineArcadeRunActor))]
    [FriendOf(typeof(OnlineArcadeRunActor))]
    public static partial class OnlineArcadeRunActorSystem
    {
        private const float AimDeadZoneSquared = 0.15f * 0.15f;
        private const float ProjectileHitRadius = 0.45f;
        private const float WaveTransitionDurationSeconds = 1.1f;
        private const float BasePickupRadius = 1.1f;
        private const float SupportProjectileSpeed = 19f;
        private const int SupportProjectileDamage = 12;
        private const float SupportProjectileLifetimeSeconds = 1.5f;
        private const string SupportSlotId = "support";
        private const float DashDurationSeconds = 0.28f;
        private const float DashSpeedMultiplier = 2.4f;
        private const long InputExpiryMilliseconds = 250;

        [EntitySystem]
        private static void Awake(this OnlineArcadeRunActor self) { }

        [EntitySystem]
        private static void Destroy(this OnlineArcadeRunActor self)
        {
            self.BattleGeneration++;
            self.Battle = null;
            self.CommittedRecord = null;
            self.Record = null;
        }

        public static OnlineArcadeRunSnapshot BuildSnapshot(this OnlineArcadeRunActor self)
        {
            OnlineArcadeRunSnapshot snapshot = BuildSnapshot(self.Record);
            if (snapshot == null) return null;
            long now = TimeInfo.Instance.ServerNow();
            foreach (OnlineArcadePlayerRecord player in self.Record.Players)
            {
                OnlineArcadeConnectionInfo connection = OnlineArcadeConnectionInfo.Create();
                connection.PlayerId = player.PlayerId;
                if (self.DisconnectedAtByPlayer.TryGetValue(player.PlayerId, out long disconnectedAt))
                {
                    connection.Connected = false;
                    connection.DisconnectedAtServerTime = disconnectedAt;
                    connection.ArenaReconnectDeadlineServerTime = self.Battle == null ? 0 : self.ArenaReconnectDeadline;
                    connection.SoloEndAvailableAtServerTime = disconnectedAt + OnlineArcadeRunManagerSystem.SoloEndDelayMilliseconds;
                }
                else connection.Connected = true;
                snapshot.Connections.Add(connection);
            }
            snapshot.BattlePaused = self.Battle != null && self.DisconnectedAtByPlayer.Count > 0;
            if (self.Battle != null)
            {
                snapshot.Phase = (int)(self.Battle.IsChallenge
                    ? OnlineArcadeRunPhase.Challenge
                    : OnlineArcadeRunPhase.Arena);
                snapshot.Battle = BuildBattleSnapshot(self.Battle);
                snapshot.Battle.ServerTime = now;
            }
            return snapshot;
        }

        /// <summary>只接受递增输入序号并把向量限制在单位圆内；客户端位置不会直接成为权威位置。</summary>
        public static (bool Accepted, long LastAcceptedInputSequence) SubmitInput(this OnlineArcadeRunActor self,
            long playerId, int arenaNumber, long inputSequence, float moveX, float moveZ, float aimX, float aimZ,
            bool firing, bool useBomb, bool useDash, int roundGeneration = 0, bool roundReady = false, int exitTarget = 0, bool confirmExit = false)
        {
            OnlineArcadeBattleRuntime battle = self.Battle;
            if (battle == null || battle.ArenaNumber != arenaNumber || !battle.Players.TryGetValue(playerId, out OnlineArcadeBattlePlayerRuntime player))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "竞技场身份已过期");
            if (battle.AllPlayersDefeated || battle.ArenaCompletionReady || battle.ChallengeCompletionReady)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "竞技场已停止接受战斗输入");
            if (self.DisconnectedAtByPlayer.Count > 0)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "竞技场正在等待队友重连");
            if (!Finite(moveX, moveZ, aimX, aimZ) || inputSequence <= 0)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "战斗输入无效");
            if (inputSequence <= player.LastInputSequence) return (false, player.LastInputSequence);
            if (battle.RoundLayout != null)
            {
                if (roundGeneration != battle.RoundGeneration)
                    throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "轮代次已过期");
                if (roundReady) player.ReadyRoundGeneration = roundGeneration;
                if (confirmExit && player.Alive && !player.InTreasureRoom && battle.AwaitingExit)
                    player.ConfirmedExitTarget = exitTarget;
                if (!RoundPlayersReady(battle) || battle.RoundTransitionPending)
                { player.LastInputSequence=inputSequence; battle.InputsDirty=true; return (true,inputSequence); }
            }
            Normalize(ref moveX, ref moveZ);
            Normalize(ref aimX, ref aimZ);
            player.MoveX = moveX;
            player.MoveZ = moveZ;
            if (aimX * aimX + aimZ * aimZ >= AimDeadZoneSquared)
            {
                player.AimX = aimX;
                player.AimZ = aimZ;
            }
            player.Firing = firing;
            player.LastInputSequence = inputSequence;
            player.LastInputAt = TimeInfo.Instance.ServerNow();
            if (player.Alive)
            {
                if (useBomb) self.UseBomb(battle, player);
                if (useDash) self.UseDash(player);
            }
            battle.InputsDirty = true;
            return (true, player.LastInputSequence);
        }

        /// <summary>返回双方权威状态摘要；只读，不创建战斗或推进服务器 Tick。</summary>
        public static string BuildTestStatus(this OnlineArcadeRunActor self)
        {
            string phase = self.Battle == null ? self.Record.Phase.ToString() :
                self.Battle.IsChallenge ? "Challenge" : "Arena";
            string players = string.Join("；", self.Record.Players.OrderBy(x => x.PlayerId).Select(record =>
            {
                string health = self.Battle != null && self.Battle.Players.TryGetValue(record.PlayerId, out var runtime)
                    ? $"{runtime.CurrentHealth}/{runtime.MaxHealth}"
                    : "--";
                return $"{record.DisplayName}({record.PlayerId}) 生命 {health} 命数 {record.Lives} 金币 {record.RunGold} 炸弹 {record.Bombs} 冲刺 {record.Dashes}";
            }));
            string battle = self.Battle == null
                ? "无战斗现场"
                : $"竞技场 {self.Battle.ArenaNumber} 波次 {self.Battle.WaveIndex + 1}/{self.Battle.WaveCount} 敌人 {self.Battle.Enemies.Count + self.Battle.SpawnQueue.Count - self.Battle.SpawnQueueIndex} 掉落 {self.Battle.Drops.Count}";
            return $"阶段 {phase}，{battle}；{players}";
        }

        /// <summary>测试入口：只恢复房主当前现场生命；死亡等待复活时拒绝执行。</summary>
        public static string HealPlayerForTests(this OnlineArcadeRunActor self,
            OnlineArcadeBattlePlayerRuntime player)
        {
            if (!player.Alive || player.RespawnSeconds > 0f)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "角色正在等待复活");
            player.CurrentHealth = player.MaxHealth;
            return $"房主生命已恢复至 {player.CurrentHealth}/{player.MaxHealth}";
        }

        /// <summary>测试入口：沿用正式受伤、护盾、无敌与扣命规则，不直接伪造死亡结果。</summary>
        public static string DamagePlayerForTests(this OnlineArcadeRunActor self,
            OnlineArcadeBattlePlayerRuntime player, int damage)
        {
            self.DamagePlayer(self.Battle, player, damage);
            return $"已按正式规则结算 {damage} 点伤害，房主生命 {player.CurrentHealth}/{player.MaxHealth}";
        }

        /// <summary>测试入口：增加房主局内金币；只有正式完成事务才会写入下一安全节点。</summary>
        public static string AddGoldForTests(this OnlineArcadeRunActor self, long playerId, int amount)
        {
            OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == playerId);
            record.RunGold = checked(record.RunGold + amount);
            return $"房主金币已增加 {amount}，现为 {record.RunGold}";
        }

        /// <summary>测试入口：设置房主炸弹或冲刺数量；数值仍由正式战斗消耗。</summary>
        public static string SetResourceForTests(this OnlineArcadeRunActor self, long playerId, bool bombs, int amount)
        {
            OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == playerId);
            if (bombs) record.Bombs = amount;
            else record.Dashes = amount;
            return bombs ? $"房主炸弹数量已设置为 {amount}" : $"房主冲刺数量已设置为 {amount}";
        }

        /// <summary>测试入口：按正式掉落配置直接为房主结算一次拾取效果。</summary>
        public static string ApplyDropForTests(this OnlineArcadeRunActor self,
            OnlineArcadeBattlePlayerRuntime player, string dropId)
        {
            ArcadeDropConfig drop = ServerConfig.Instance.Tables.TbArcadeDrop.GetOrDefault(dropId);
            if (drop == null)
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, $"街机掉落配置不存在：{dropId}");
            self.ApplyDrop(player, drop);
            return $"已为房主模拟拾取 {drop.Id}（{drop.Kind} / {drop.ProductId}）";
        }

        /// <summary>测试入口：清空当前波次，并继续使用正式波次奖励与过渡规则。</summary>
        public static string ClearWaveForTests(this OnlineArcadeRunActor self)
        {
            int removed = self.ClearEnemiesForTests();
            self.Battle.SpawnQueueIndex = self.Battle.SpawnQueue.Count;
            self.TryAdvanceWave(self.Battle, 0f);
            return $"当前波次已清除，共移除 {removed} 个已生成敌人";
        }

        /// <summary>测试入口：把当前竞技场或挑战推进到正式完成事务，最终安全节点仍由管理器提交。</summary>
        public static string ClearArenaForTests(this OnlineArcadeRunActor self)
        {
            OnlineArcadeBattleRuntime battle = self.Battle;
            int removed = self.ClearEnemiesForTests();
            battle.SpawnQueueIndex = battle.SpawnQueue.Count;
            battle.WaveIndex = battle.IsChallenge ? 0 : battle.WaveCount - 1;
            self.TryAdvanceWave(battle, 0f);
            battle.WaveTransitionSeconds = WaveTransitionDurationSeconds;
            self.TryAdvanceWave(battle, 0f);
            return battle.IsChallenge
                ? $"特殊挑战已按成功流程完成，共移除 {removed} 个已生成敌人"
                : $"竞技场已进入正式完成事务，共移除 {removed} 个已生成敌人";
        }

        private static int ClearEnemiesForTests(this OnlineArcadeRunActor self)
        {
            OnlineArcadeBattleRuntime battle = self.Battle;
            long[] enemyIds = battle.Enemies.Keys.OrderBy(x => x).ToArray();
            foreach (long enemyId in enemyIds)
                if (battle.Enemies.TryGetValue(enemyId, out OnlineArcadeEnemyRuntime enemy))
                    self.KillEnemy(battle, enemy, true);
            return enemyIds.Length;
        }

        /// <summary>双方准备成功后完整构造并校验不落盘的战斗现场候选。</summary>
        public static OnlineArcadeBattleRuntime PrepareBattle(OnlineArcadeRunRecord record, bool challenge)
        {
            if (record == null || record.Players.Count != 2 || record.Players.All(x => x.Lives <= 0) ||
                challenge && record.Players.Any(x => !x.ChallengeReady) ||
                !challenge && record.Players.Any(x => x.Lives > 0 && !x.Ready))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "双方尚未满足进入竞技场条件");
            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            if (!string.Equals(record.ConfigFingerprint, rule.ConfigFingerprint, StringComparison.Ordinal))
                throw new RpcException(ErrorCode.ERR_OnlineRoomVersionMismatch, "街机配置版本与服务器不一致");
            ArcadeArenaConfig arena = ResolveArena(record.IsCasualRoute ? record.ArenaConfigNumber : record.NextArena);
            ArcadeChallengeConfig challengeConfig = challenge
                ? ServerConfig.Instance.Tables.TbArcadeChallenge.GetOrDefault(record.ChallengeId)
                : null;
            if (challenge && (challengeConfig == null || record.ChallengeAttempted))
                throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid, "特殊挑战身份已失效");
            OnlineArcadeBattleRuntime battle = new()
            {
                ArenaNumber = record.NextArena,
                ArenaConfigNumber = arena.ArenaNumber,
                IsCasualRoute = record.IsCasualRoute,
                WaveCount = challenge ? 1 : arena.WaveGroupIds.Length,
                DifficultyPlayerCount = record.Players.Count,
                IsChallenge = challenge,
                ChallengeId = challengeConfig?.Id ?? string.Empty,
                ChallengeSeconds = challengeConfig?.TimeLimitSeconds ?? 0f,
            };
            int playerIndex = 0;
            foreach (OnlineArcadePlayerRecord source in record.Players)
            {
                float maxHealthBonus = 0f;
                foreach (KeyValuePair<string, int> entry in source.UpgradeStacks)
                {
                    ArcadeUpgradeConfig upgrade = ServerConfig.Instance.Tables.TbArcadeUpgrade.GetOrDefault(entry.Key);
                    if (upgrade?.Kind == ArcadeUpgradeKind.MaxHealthPercent)
                        maxHealthBonus += upgrade.ValuePerStack * entry.Value;
                }
                int maxHealth = Math.Max(1, (int)MathF.Round(rule.PlayerMaxHealth * (1f + maxHealthBonus)));
                source.EquipmentBySlot.TryGetValue(SupportSlotId, out string supportId);
                battle.Players.Add(source.PlayerId, new OnlineArcadeBattlePlayerRuntime
                {
                    PlayerId = source.PlayerId,
                    PositionX = playerIndex++ == 0 ? -2f : 2f,
                    PositionZ = 0f,
                    CurrentHealth = source.Lives > 0 ? maxHealth : 0,
                    Alive = source.Lives > 0,
                    MaxHealth = maxHealth,
                    SupportId = supportId ?? string.Empty,
                });
            }
            PrepareWave(battle, arena, challenge ? 0 : Math.Max(0, record.RoundNumber - 1));
            if (!challenge && battle.RoundLayout != null)
            {
                if (!string.IsNullOrEmpty(record.RoundLayoutId) &&
                    (record.RoundLayoutId != battle.RoundLayout.Id || record.RoundLayoutVersion != battle.RoundLayout.Version))
                    throw new RpcException(ErrorCode.ERR_OnlineRoomVersionMismatch,"轮布局版本已过期，保留原存档");
                battle.RoundGeneration = checked(Math.Max(0,record.RoundGeneration)+1);
                foreach (int id in record.DestroyedCrateIds) { battle.DestroyedCrateIds.Add(id); battle.Crates.Remove(id); }
                foreach(int index in record.DestroyedBarrelIds)
                {
                    if(index<0||index>=battle.RoundLayout.Barrels.Count||!battle.DestroyedBarrelIds.Add(index))
                        throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"轮前预置桶身份无效");
                }
                foreach (var pair in record.CrateHealth)
                {
                    if(!battle.Crates.TryGetValue(pair.Key,out var crate)||pair.Value<=0||pair.Value>crate.Definition.Health)
                        throw new RpcException(ErrorCode.ERR_OnlineArcadeStateInvalid,"轮前箱体耐久无效");
                    crate.Damage(crate.Definition.Health-pair.Value,true);
                }
                foreach (var source in record.Players)
                {
                    var player=battle.Players[source.PlayerId];
                    player.PositionX=battle.RoundLayout.SpawnX+(player.PlayerId==record.Players[0].PlayerId?-1f:1f);
                    player.PositionZ=battle.RoundLayout.SpawnY;
                    if (!string.IsNullOrEmpty(record.RoundLayoutId))
                    {
                        player.CurrentHealth=Math.Min(player.MaxHealth,Math.Max(0,source.RoundHealth));player.Alive=source.Lives>0&&source.RoundAlive;
                        player.InvulnerabilitySeconds=source.RoundInvulnerabilitySeconds;player.RespawnSeconds=source.RoundRespawnSeconds;
                        player.DashSeconds=source.RoundDashSeconds;player.FireCooldownSeconds=source.RoundFireCooldownSeconds;
                        player.TemporaryWeaponId=source.RoundTemporaryWeaponId;
                        player.TemporaryWeaponSeconds=source.RoundTemporaryWeaponSeconds;
                        foreach(var power in source.RoundPowerups) player.Powerups[power.Behavior]=ClonePowerup(power);
                    }
                }
                if(record.RoundBossEncounter) PrepareRoundBoss(battle);
            }
            if (!challenge && string.IsNullOrEmpty(record.RoundLayoutId))
                foreach (var player in record.Players.Where(p => p.Lives > 0))
                {
                    player.Bombs = FirepowerLoop.Arcade.ArcadeOpeningRules.ReplenishEmpty(player.Bombs);
                    player.Dashes = FirepowerLoop.Arcade.ArcadeOpeningRules.ReplenishEmpty(player.Dashes);
                }
            if (!challenge)
                foreach (ArcadeHazardConfig definition in arena.Hazards)
                    battle.Hazards.Add(new FirepowerLoop.Arcade.ArcadeHazardState(ServerConfig.GetHazardParameters(definition)));
            battle.LastTickAt = TimeInfo.Instance.ServerNow();
            return battle;
        }

        /// <summary>发布已经校验完成的瞬时现场；调用前安全节点必须已经落盘。</summary>
        public static void StartBattle(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle)
        {
            if (self.Battle != null) return;
            self.Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            self.BattleGeneration++;
        }

        /// <summary>按场创建时人数建立整轮队列；前期分支使用累计轮数，权重分配不改变同屏预算。</summary>
        private static void PrepareWave(OnlineArcadeBattleRuntime battle, ArcadeArenaConfig arena, int waveIndex)
        {
            InitializeCrates(battle);
            ArcadeEnemyGroupConfig group = ResolveBattleGroup(battle, arena, waveIndex);
            ArcadeBossConfig boss = battle.IsChallenge ? null : ResolveBoss(arena, group);
            battle.WaveIndex = waveIndex;
            if (!battle.IsChallenge && battle.IsCasualRoute)
            {
                var nextLayout=ResolveRoundLayout(arena.ArenaNumber,waveIndex+1);
                if(battle.RoundLayout?.Id!=nextLayout.Id||battle.RoundLayout.Version!=nextLayout.Version)battle.DestroyedBarrelIds.Clear();
                battle.RoundLayout = nextLayout;
                battle.IslandRegionResolved=false; battle.IslandRegion=null;
            }
            battle.SpawnQueue.Clear();
            battle.SpawnQueueIndex = 0;
            battle.SpawnedInWave = 0;
            int totalWeight = group.Members.Sum(member => member.Count);
            int total = UsesEarlyRounds(battle)
                ? FirepowerLoop.Arcade.ArcadeOpeningRules.OrdinaryRoundTotal((battle.ArenaNumber - 1) * 4 + waveIndex + 1, battle.DifficultyPlayerCount)
                : arena.WaterSpeedRatio > 0 && !battle.IsChallenge
                    ? FirepowerLoop.Arcade.ArcadeOpeningRules.OrdinaryRoundTotal(waveIndex + 1, battle.DifficultyPlayerCount)
                    : boss == null ? checked(totalWeight * battle.DifficultyPlayerCount) : totalWeight;
            int previousWeight = 0;
            foreach (ArcadeEnemyMemberConfig member in group.Members)
            {
                int count = FirepowerLoop.Arcade.ArcadeOpeningRules.AllocateMember(total, previousWeight, member.Count, totalWeight);
                for (int index = 0; index < count; index++) battle.SpawnQueue.Add(member.EnemyId);
                previousWeight += member.Count;
            }
            battle.TotalInWave = battle.SpawnQueue.Count;
            battle.MaxAlive = Math.Min(ServerConfig.Instance.ArcadeRule.MaxAliveEnemies,
                checked(group.MaxAlive * battle.DifficultyPlayerCount));
            battle.NextSpawnAt = TimeInfo.Instance.ServerNow();
            battle.WaveTransitionSeconds = 0f;
            battle.WaveClearAwarded = false;
            foreach (var player in battle.Players.Values) player.RestAttackReleased = false;
            battle.BattleRevision++;
        }

        /// <summary>以固定服务器 Tick 推进输入、射击、投射物命中和配置化敌人生成。</summary>
        public static bool TickBattle(this OnlineArcadeRunActor self, long now)
        {
            OnlineArcadeBattleRuntime battle = self.Battle;
            if (battle == null || battle.AllPlayersDefeated || battle.ArenaCompletionReady ||
                battle.ChallengeCompletionReady) return false;
            if (!RoundPlayersReady(battle) || battle.RoundTransitionPending || self.DisconnectedAtByPlayer.Count>0)
            { battle.LastTickAt=now; return battle.InputsDirty; }
            float deltaSeconds = Math.Clamp((now - battle.LastTickAt) / 1000f, 0f, 0.2f);
            battle.LastTickAt = now;
            bool changed = battle.InputsDirty;
            battle.InputsDirty = false;
            changed |= TickDropLifetimes(battle, deltaSeconds);
            bool hazardRespawnWaiting = battle.Players.Values.Any(player => player.RespawnSeconds > 0f);
            foreach (var player in battle.Players.Values)
            {
                player.HazardPreviousX = player.PositionX;
                player.HazardPreviousZ = player.PositionZ;
            }
            foreach (var enemy in battle.Enemies.Values)
            {
                enemy.HazardPreviousX = enemy.PositionX;
                enemy.HazardPreviousZ = enemy.PositionZ;
            }
            if (battle.IsChallenge)
            {
                float previous = battle.ChallengeSeconds;
                battle.ChallengeSeconds = Math.Max(0f, battle.ChallengeSeconds - deltaSeconds);
                changed |= MathF.Abs(previous - battle.ChallengeSeconds) > 0.0001f;
                if (battle.ChallengeSeconds <= 0f)
                {
                    battle.ChallengeCompletionReady = true;
                    battle.ChallengeSucceeded = false;
                    if (changed) battle.BattleRevision++;
                    return true;
                }
            }
            changed |= self.TickTreasureRoom(battle, deltaSeconds);
            changed |= self.TickRoundContent(battle, deltaSeconds);
            changed |= self.TickPlayers(battle, deltaSeconds, now);
            changed |= self.TickTreasureRoom(battle, 0f);
            changed |= self.TickOpeningFire(battle, deltaSeconds);
            if (battle.AwaitingExit)
            {
                // 等待出口仍按服务器权威规则拾取，并将奖励纳入本帧快照及随后离场结算。
                changed |= self.TickDrops(battle);
                changed |= TickExit(battle);
                if (changed) battle.BattleRevision++;
                return changed;
            }
            changed |= TickFinalBossReinforcements(battle, deltaSeconds, now);
            changed |= TrySpawnEnemy(battle, now);
            changed |= self.TickEnemies(battle, deltaSeconds);
            if (!hazardRespawnWaiting && !battle.AllPlayersDefeated)
                changed |= self.TickHazards(battle, deltaSeconds);
            if (!battle.AllPlayersDefeated)
            {
                changed |= self.TickProjectiles(battle, deltaSeconds);
                changed |= self.TickDrops(battle);
                changed |= self.TickPowerups(battle, deltaSeconds);
                changed |= self.TryAdvanceWave(battle, deltaSeconds);
            }
            if (changed) battle.BattleRevision++;
            return changed;
        }

        private static bool TrySpawnEnemy(OnlineArcadeBattleRuntime battle, long now)
        {
            if (battle.SpawnQueueIndex >= battle.SpawnQueue.Count || battle.Enemies.Count >= battle.MaxAlive || now < battle.NextSpawnAt)
                return false;
            ArcadeArenaConfig arena = ResolveBattleArena(battle);
            ArcadeEnemyGroupConfig group = ResolveBattleGroup(battle, arena, battle.WaveIndex);
            ArcadeBossConfig boss = battle.IsChallenge || battle.RoundLayout != null ? null : battle.IsFinalBoss && battle.FinalBossEnemyId == 0
                ? ServerConfig.Instance.Tables.TbArcadeBoss.Get(arena.FinalBossId) : ResolveBoss(arena, group);
            string enemyConfigId = battle.SpawnQueue[battle.SpawnQueueIndex++];
            EnemyConfig enemyConfig = ServerConfig.Instance.GetEnemy(enemyConfigId);
            long runtimeId = battle.NextEnemyRuntimeId++;
            float arenaGrowth = 1f + ServerConfig.Instance.ArcadeRule.HealthGrowthPerArena * Math.Max(0, battle.ArenaNumber - 1);
            float damageGrowth = 1f + ServerConfig.Instance.ArcadeRule.DamageGrowthPerArena * Math.Max(0, battle.ArenaNumber - 1);
            float speedGrowth = 1f + ServerConfig.Instance.ArcadeRule.SpeedGrowthPerArena * Math.Max(0, battle.ArenaNumber - 1);
            int maxHealth = Math.Max(1, (int)MathF.Round(enemyConfig.MaxHP * group.HealthMultiplier * arenaGrowth));
            (float x, float z) = SpawnPosition(runtimeId, ServerConfig.Instance.ArcadeRule);
            var opening = FirepowerLoop.Arcade.ArcadeOpeningParameters.FromConfig(arena);
            bool enteringIsland = opening.Enabled && !battle.IsChallenge && !battle.IsFinalBoss;
            float entryTargetZ = 0f;
            float entryTargetX = x;
            if (battle.RoundLayout != null && !enteringIsland)
            {
                var entry=battle.RoundLayout.Entries[battle.SpawnedInWave % battle.RoundLayout.Entries.Count];x=entry.X;z=entry.Y;
            }
            if (enteringIsland)
            {
                IslandFor(battle).Entry(battle.SpawnedInWave, ((runtimeId * 37) % 101) / 100f,
                    ServerConfig.Instance.ArcadeRule.EnemyContactRadius, out var start, out var destination);
                x = start.X; z = start.Y; entryTargetX = destination.X; entryTargetZ = destination.Y;
            }
            OnlineArcadeBattlePlayerRuntime target = battle.Players.Values.Where(player => player.Alive && !player.InTreasureRoom)
                .OrderBy(player => DistanceSquared(x, z, player.PositionX, player.PositionZ)).FirstOrDefault();
            var enemy = new OnlineArcadeEnemyRuntime
            {
                RuntimeId = runtimeId,
                EnemyConfigId = enemyConfigId,
                PositionX = x,
                PositionZ = z,
                EnteringIsland = enteringIsland,
                IslandEntryTargetX = entryTargetX,
                IslandEntryTargetZ = entryTargetZ,
                // 新生成目标本帧没有旧位置，避免从原点虚构一条机关扫掠路径。
                HazardPreviousX = x,
                HazardPreviousZ = z,
                CurrentHealth = maxHealth,
                MaxHealth = maxHealth,
                ArmorHealth = UsesBabylon(battle) && arena.ArmorWaveNumber == battle.WaveIndex + 1 ? arena.ArmorHealth : 0,
                Damage = Math.Max(1, (int)MathF.Round(enemyConfig.AttackDamage * group.DamageMultiplier * damageGrowth)),
                MoveSpeed = Math.Max(0.1f, enemyConfig.MoveSpeed * group.SpeedMultiplier * speedGrowth),
                AttackKind = enemyConfig.AttackKind,
                AttackRange = Math.Max(0.1f, enemyConfig.AttackRange),
                AttackIntervalSeconds = Math.Max(0.1f, enemyConfig.AttackCooldownSeconds),
                AttackCooldownSeconds = Math.Max(0.1f, enemyConfig.AttackCooldownSeconds),
                ProjectileConfigId = enemyConfig.ProjectileConfigId ?? string.Empty,
                TargetPlayerId = target?.PlayerId ?? 0,
                WaveNumber = battle.WaveIndex + 1,
                BossId = boss?.Id ?? string.Empty,
                BossSkillCooldownSeconds = boss?.SkillCooldownSeconds ?? 0f,
                BossRayCooldownSeconds = boss?.RayIntervalSeconds ?? 0f,
            };
            InitializeEnemyContent(battle,enemy);
            battle.Enemies.Add(runtimeId, enemy);
            if (battle.IsFinalBoss && battle.FinalBossEnemyId == 0) battle.FinalBossEnemyId = runtimeId;
            battle.SpawnedInWave++;
            battle.NextSpawnAt = now + Math.Max(1L, (long)MathF.Round(group.SpawnIntervalSeconds * 1000f));
            return true;
        }

        private static bool TickPlayers(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            float deltaSeconds, long now)
        {
            bool changed = false;
            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            ArcadeArenaConfig arena = ResolveBattleArena(battle);
            foreach (OnlineArcadeBattlePlayerRuntime player in battle.Players.Values)
            {
                float previousInvulnerability = player.InvulnerabilitySeconds;
                player.InvulnerabilitySeconds = Math.Max(0f, player.InvulnerabilitySeconds - deltaSeconds);
                changed |= MathF.Abs(previousInvulnerability - player.InvulnerabilitySeconds) > 0.0001f;
                float previousTemporaryWeapon = player.TemporaryWeaponSeconds;
                // 军械库内、队友未返回和开战前等待均不消耗所选武器。
                // 轮级事务会销毁房间现场，首批敌人生成前仍须保留完整武器时间。
                bool awaitingArmoryCombat = battle.IsCasualRoute && !battle.IsChallenge && !battle.IsFinalBoss &&
                    battle.ArenaNumber == 2 && battle.WaveIndex == 3 && battle.SpawnedInWave == 0;
                bool holdArmoryWeapon = awaitingArmoryCombat || battle.TreasureRoom?.Parameters.IsArmory == true &&
                    (player.InTreasureRoom || battle.RoundTransitionPending || battle.TreasureRoom.Members.Length > 0 || battle.WaveClearAwarded ||
                     battle.Enemies.Count == 0 && battle.SpawnQueueIndex >= battle.SpawnQueue.Count);
                if (!holdArmoryWeapon) player.TemporaryWeaponSeconds = Math.Max(0f, player.TemporaryWeaponSeconds - deltaSeconds);
                if (player.TemporaryWeaponSeconds <= 0f) player.TemporaryWeaponId = string.Empty;
                float previousDash = player.DashSeconds;
                player.DashSeconds = Math.Max(0f, player.DashSeconds - deltaSeconds);
                changed |= MathF.Abs(previousTemporaryWeapon - player.TemporaryWeaponSeconds) > 0.0001f ||
                           MathF.Abs(previousDash - player.DashSeconds) > 0.0001f;
                if (!player.Alive)
                {
                    player.MoveX = 0f;
                    player.MoveZ = 0f;
                    player.Firing = false;
                    if (player.RespawnSeconds <= 0f) continue;
                    float previousRespawn = player.RespawnSeconds;
                    player.RespawnSeconds = Math.Max(0f, player.RespawnSeconds - deltaSeconds);
                    changed |= MathF.Abs(previousRespawn - player.RespawnSeconds) > 0.0001f;
                    if (player.RespawnSeconds > 0f) continue;
                    OnlineArcadePlayerRecord respawnRecord = self.Record.Players.First(x => x.PlayerId == player.PlayerId);
                    if (respawnRecord.Lives <= 0) continue;
                    player.CurrentHealth = player.MaxHealth;
                    player.Alive = true;
                    player.InvulnerabilitySeconds = rule.ContactInvulnerabilitySeconds * 2f;
                    player.LastInputAt = now;
                    changed = true;
                    continue;
                }
                if (now - player.LastInputAt > InputExpiryMilliseconds)
                {
                    changed |= MathF.Abs(player.MoveX) > 0.0001f || MathF.Abs(player.MoveZ) > 0.0001f || player.Firing;
                    player.MoveX = 0f;
                    player.MoveZ = 0f;
                    player.Firing = false;
                }
                player.FireCooldownSeconds = Math.Max(0f, player.FireCooldownSeconds - deltaSeconds);
                OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == player.PlayerId);
                float moveSpeed = rule.PlayerMoveSpeed * (1f + UpgradeTotal(record, ArcadeUpgradeKind.MoveSpeedPercent));
                if (TryGetPowerup(player, ArcadePowerupBehavior.Boots, out OnlineArcadePowerupRuntime boots))
                    moveSpeed *= 1f + boots.Magnitude;
                if (player.DashSeconds > 0f) moveSpeed *= DashSpeedMultiplier;
                if (!player.InTreasureRoom && !battle.IsChallenge)
                    moveSpeed *= IslandFor(battle) is { } island && island.IsShallow(player.PositionX, player.PositionZ)
                        ? FirepowerLoop.Arcade.ArcadeOpeningParameters.FromConfig(arena).WaterSpeedRatio : 1f;
                ContentPlayerVelocity(battle,player,moveSpeed,deltaSeconds,out float velocityX,out float velocityZ);
                if (MathF.Abs(velocityX) > 0.0001f || MathF.Abs(velocityZ) > 0.0001f)
                {
                    float nextX = player.PositionX + velocityX * deltaSeconds;
                    float nextZ = player.PositionZ + velocityZ * deltaSeconds;
                    var region = player.InTreasureRoom ? null : IslandFor(battle);
                    if (region != null)
                    {
                        var position = region.Move(player.PositionX, player.PositionZ, nextX - player.PositionX,
                            nextZ - player.PositionZ, rule.PlayerRadius, battle.IslandMovementPath);
                        nextX = position.X; nextZ = position.Y;
                    }
                    ResolvePosition(ref nextX, ref nextZ, player.PositionX, player.PositionZ,
                        rule.PlayerRadius, rule, player.InTreasureRoom ? Array.Empty<ArcadeObstacleConfig>() : EffectiveObstacles(battle), region);
                    if (player.InTreasureRoom)
                    {
                        nextX = Math.Clamp(nextX, -battle.TreasureRoom.Parameters.HalfWidth + rule.PlayerRadius, battle.TreasureRoom.Parameters.HalfWidth - rule.PlayerRadius);
                        nextZ = Math.Clamp(nextZ, -battle.TreasureRoom.Parameters.HalfHeight + rule.PlayerRadius, battle.TreasureRoom.Parameters.HalfHeight - rule.PlayerRadius);
                    }
                    changed |= MathF.Abs(nextX - player.PositionX) > 0.0001f || MathF.Abs(nextZ - player.PositionZ) > 0.0001f;
                    player.PositionX = nextX;
                    player.PositionZ = nextZ;
                    if (region != null) KillAlongIslandDash(self, battle, player);
                }
                if (!player.InTreasureRoom && player.Firing && player.FireCooldownSeconds <= 0f)
                    changed |= self.Fire(battle, player, record);
            }
            return changed;
        }

        /// <summary>敌人每个 Tick 重选最近存活目标，并由服务器推进追击、近战或配置化投射攻击。</summary>
        private static bool TickEnemies(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            float deltaSeconds)
        {
            if (battle.Enemies.Count == 0) return false;
            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            ArcadeArenaConfig arena = ResolveBattleArena(battle);
            battle.EnemyIterationIds.Clear();
            battle.EnemyIterationIds.AddRange(battle.Enemies.Keys);
            battle.EnemyIterationIds.Sort();
            bool changed = false;
            foreach (long enemyId in battle.EnemyIterationIds)
            {
                if (!battle.Enemies.TryGetValue(enemyId, out OnlineArcadeEnemyRuntime enemy)) continue;
                OnlineArcadeBattlePlayerRuntime target = battle.Players.Values.Where(x => x.Alive && !x.InTreasureRoom)
                    .OrderBy(x => DistanceSquared(enemy.PositionX, enemy.PositionZ, x.PositionX, x.PositionZ))
                    .ThenBy(x => x.PlayerId).FirstOrDefault();
                if (target == null)
                {
                    battle.AllPlayersDefeated = self.Record.Players.All(x => x.Lives <= 0);
                    return changed || battle.AllPlayersDefeated;
                }

                if(enemy.ContentStunUntil>battle.ContentClock.Elapsed)continue;
                enemy.TargetPlayerId = target.PlayerId;
                enemy.AttackCooldownSeconds = Math.Max(0f, enemy.AttackCooldownSeconds - deltaSeconds);
                float targetX = target.PositionX;
                float targetZ = target.PositionZ;
                bool targetsDecoy = false;
                if (string.IsNullOrEmpty(enemy.BossId) &&
                    TryFindNearestDecoy(battle, enemy.PositionX, enemy.PositionZ,
                        out OnlineArcadePowerupRuntime decoy))
                {
                    targetX = decoy.PositionX;
                    targetZ = decoy.PositionZ;
                    enemy.TargetPlayerId = 0;
                    targetsDecoy = true;
                }
                float offsetX = targetX - enemy.PositionX;
                float offsetZ = targetZ - enemy.PositionZ;
                float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
                float distance = MathF.Sqrt(distanceSquared);
                float directionX = distance > 0.001f ? offsetX / distance : 0f;
                float directionZ = distance > 0.001f ? offsetZ / distance : 1f;
                float movementDelta = deltaSeconds * EnemySpeedMultiplier(battle);
                movementDelta *= EnemyContentMotion(battle,enemy,deltaSeconds);
                if (self.TickContentBoss(battle,enemy,target,deltaSeconds)) { changed=true;continue; }
                if(self.TickSpecialEnemy(battle,enemy,target,targetX,targetZ,targetsDecoy,movementDelta)){changed=true;if(battle.AllPlayersDefeated)return true;continue;}

                bool entering = enemy.EnteringIsland;
                if (entering)
                {
                    enemy.EnteringIsland = !FirepowerLoop.Arcade.ArcadeOpeningRules.AdvanceEntry(
                        ref enemy.PositionX, ref enemy.PositionZ, enemy.IslandEntryTargetX, enemy.IslandEntryTargetZ,
                        enemy.MoveSpeed * movementDelta);
                    changed = true;
                }

                if (!entering && self.TickBoss(battle, enemy, target, directionX, directionZ, deltaSeconds, movementDelta,
                        rule, EffectiveObstacles(battle)))
                {
                    changed = true;
                    if (battle.AllPlayersDefeated) return true;
                    continue;
                }

                if (enemy.AttackKind == EnemyAttackKind.Projectile)
                {
                    if (!entering && distance > enemy.AttackRange * 0.8f)
                        MoveEnemy(enemy, targetX, targetZ, movementDelta, rule, EffectiveObstacles(battle), IslandFor(battle));
                    if (distance <= enemy.AttackRange && enemy.AttackCooldownSeconds <= 0f &&
                        battle.Projectiles.Count < rule.MaxProjectiles)
                    {
                        SpawnEnemyProjectile(battle, enemy, directionX, directionZ);
                        enemy.AttackCooldownSeconds = enemy.AttackIntervalSeconds;
                    }
                }
                else
                {
                    if (!entering) MoveEnemy(enemy, targetX, targetZ, movementDelta, rule, EffectiveObstacles(battle), IslandFor(battle));
                    if (distance <= rule.PlayerRadius + rule.EnemyContactRadius && enemy.AttackCooldownSeconds <= 0f)
                    {
                        if (!targetsDecoy) self.DamagePlayer(battle, target, enemy.Damage);
                        enemy.AttackCooldownSeconds = enemy.AttackIntervalSeconds;
                        if (battle.AllPlayersDefeated) return true;
                    }
                }
                changed = true;
            }
            ResolveEnemyCrowding(battle, rule, EffectiveObstacles(battle));
            return changed;
        }

        /// <summary>服务器权威推进 Boss 蓄力与冲锋；每次冲锋对每名玩家最多命中一次。</summary>
        private static bool TickBoss(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            OnlineArcadeEnemyRuntime enemy, OnlineArcadeBattlePlayerRuntime target, float directionX, float directionZ,
            float deltaSeconds, float movementDelta, ArcadeRuleConfig rule,
            IReadOnlyList<ArcadeObstacleConfig> obstacles)
        {
            if (string.IsNullOrEmpty(enemy.BossId)) return false;
            ArcadeBossConfig boss = ServerConfig.Instance.Tables.TbArcadeBoss.Get(enemy.BossId);
            if (enemy.BossPhase == OnlineArcadeBossPhase.RayTelegraph)
            {
                enemy.BossPhaseSeconds = Math.Max(0f, enemy.BossPhaseSeconds - deltaSeconds);
                if (enemy.BossPhaseSeconds <= 0f)
                {
                    if (battle.Projectiles.Count < rule.MaxProjectiles)
                    {
                        long id = battle.NextProjectileRuntimeId++;
                        battle.Projectiles.Add(id, new OnlineArcadeProjectileRuntime
                        {
                            RuntimeId = id, OwnerPlayerId = 0, PositionX = enemy.PositionX, PositionZ = enemy.PositionZ,
                            VelocityX = enemy.BossChargeDirectionX * boss.RaySpeed,
                            VelocityZ = enemy.BossChargeDirectionZ * boss.RaySpeed,
                            Damage = Math.Max(1, (int)MathF.Round(enemy.Damage * boss.RayDamageMultiplier)),
                            RemainingSeconds = 4f, Behavior = ArcadeWeaponBehavior.Direct, RemainingHits = 1,
                        });
                    }
                    enemy.BossPhase = OnlineArcadeBossPhase.None;
                    enemy.BossRayCooldownSeconds = boss.RayIntervalSeconds;
                }
                return true;
            }
            if (enemy.BossPhase == OnlineArcadeBossPhase.Telegraph)
            {
                enemy.BossPhaseSeconds = Math.Max(0f, enemy.BossPhaseSeconds - deltaSeconds);
                if (enemy.BossPhaseSeconds <= 0f)
                {
                    enemy.BossPhase = OnlineArcadeBossPhase.Charging;
                    enemy.BossPhaseSeconds = boss.ChargeDurationSeconds;
                    enemy.BossHitPlayers.Clear();
                }
                return true;
            }
            if (enemy.BossPhase == OnlineArcadeBossPhase.Charging)
            {
                float previousX = enemy.PositionX;
                float previousZ = enemy.PositionZ;
                float nextX = previousX + enemy.BossChargeDirectionX * enemy.MoveSpeed * movementDelta *
                    boss.ChargeSpeedMultiplier;
                float nextZ = previousZ + enemy.BossChargeDirectionZ * enemy.MoveSpeed * movementDelta *
                    boss.ChargeSpeedMultiplier;
                ResolvePosition(ref nextX, ref nextZ, previousX, previousZ,
                    rule.EnemyContactRadius, rule, obstacles);
                enemy.PositionX = nextX;
                enemy.PositionZ = nextZ;
                float hitRadius = boss.ImpactRadius + rule.PlayerRadius;
                float hitRadiusSquared = hitRadius * hitRadius;
                foreach (OnlineArcadeBattlePlayerRuntime player in battle.Players.Values.OrderBy(x => x.PlayerId))
                {
                    if (!player.Alive || player.InTreasureRoom || enemy.BossHitPlayers.Contains(player.PlayerId) ||
                        DistanceToSegmentSquared(player.PositionX, player.PositionZ, previousX, previousZ,
                            enemy.PositionX, enemy.PositionZ) > hitRadiusSquared)
                        continue;
                    enemy.BossHitPlayers.Add(player.PlayerId);
                    self.DamagePlayer(battle, player,
                        Math.Max(1, (int)MathF.Round(enemy.Damage * boss.ImpactDamageMultiplier)));
                }
                enemy.BossPhaseSeconds = Math.Max(0f, enemy.BossPhaseSeconds - deltaSeconds);
                if (enemy.BossPhaseSeconds <= 0f)
                {
                    enemy.BossPhase = OnlineArcadeBossPhase.None;
                    enemy.BossSkillCooldownSeconds = boss.SkillCooldownSeconds;
                    enemy.BossHitPlayers.Clear();
                }
                return true;
            }

            if (boss.RayIntervalSeconds > 0f)
            {
                enemy.BossRayCooldownSeconds = Math.Max(0f, enemy.BossRayCooldownSeconds - deltaSeconds);
                if (enemy.BossRayCooldownSeconds <= 0f)
                {
                    enemy.BossPhase = OnlineArcadeBossPhase.RayTelegraph;
                    enemy.BossPhaseSeconds = boss.RayTelegraphSeconds;
                    enemy.BossChargeDirectionX = directionX;
                    enemy.BossChargeDirectionZ = directionZ;
                    return true;
                }
            }
            enemy.BossSkillCooldownSeconds = Math.Max(0f, enemy.BossSkillCooldownSeconds - deltaSeconds);
            if (enemy.BossSkillCooldownSeconds > 0f) return false;
            enemy.BossPhase = OnlineArcadeBossPhase.Telegraph;
            enemy.BossPhaseSeconds = boss.TelegraphSeconds;
            enemy.BossChargeDirectionX = directionX;
            enemy.BossChargeDirectionZ = directionZ;
            enemy.TargetPlayerId = target.PlayerId;
            enemy.BossHitPlayers.Clear();
            return true;
        }

        /// <summary>普通敌人使用服务器权威的确定性矩形绕障；Boss 锁向冲锋保持直线技能语义。</summary>
        private static void MoveEnemy(OnlineArcadeEnemyRuntime enemy, float targetX, float targetZ,
            float deltaSeconds, ArcadeRuleConfig rule, IReadOnlyList<ArcadeObstacleConfig> obstacles,
            FirepowerLoop.Arcade.ArcadeIslandRegion island = null)
        {
            float previousX = enemy.PositionX;
            float previousZ = enemy.PositionZ;
            ResolveNavigationDirection(previousX, previousZ, targetX, targetZ, rule.EnemyContactRadius, obstacles,
                out float directionX, out float directionZ);
            float nextX = previousX + directionX * enemy.MoveSpeed * deltaSeconds;
            float nextZ = previousZ + directionZ * enemy.MoveSpeed * deltaSeconds;
            ResolvePosition(ref nextX, ref nextZ, previousX, previousZ,
                rule.EnemyContactRadius, rule, obstacles, island);
            enemy.PositionX = nextX;
            enemy.PositionZ = nextZ;
        }

        /// <summary>直线受阻时选择膨胀障碍的可见角点；不保存跨 Tick 路径状态。</summary>
        private static void ResolveNavigationDirection(float positionX, float positionZ, float targetX, float targetZ,
            float radius, IReadOnlyList<ArcadeObstacleConfig> obstacles, out float directionX, out float directionZ)
        {
            float directX = targetX - positionX;
            float directZ = targetZ - positionZ;
            float directLengthSquared = directX * directX + directZ * directZ;
            if (directLengthSquared <= 0.000001f)
            {
                directionX = 0f;
                directionZ = 0f;
                return;
            }
            if (!TryFindBlockingObstacle(positionX, positionZ, targetX, targetZ, radius, obstacles,
                    out ArcadeObstacleConfig blocking))
            {
                float inverseLength = 1f / MathF.Sqrt(directLengthSquared);
                directionX = directX * inverseLength;
                directionZ = directZ * inverseLength;
                return;
            }

            const float cornerClearance = 0.08f;
            float expandedX = blocking.HalfWidth + radius + cornerClearance;
            float expandedZ = blocking.HalfHeight + radius + cornerClearance;
            float bestDirectionX = 0f;
            float bestDirectionZ = 0f;
            float bestScore = float.MaxValue;
            for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
            {
                float cornerX = blocking.CenterX + ((cornerIndex & 1) == 0 ? -expandedX : expandedX);
                float cornerZ = blocking.CenterY + ((cornerIndex & 2) == 0 ? -expandedZ : expandedZ);
                float legX = cornerX - positionX;
                float legZ = cornerZ - positionZ;
                float legLengthSquared = legX * legX + legZ * legZ;
                if (legLengthSquared <= 0.000001f ||
                    SegmentCrossesObstacle(positionX, positionZ, cornerX, cornerZ, radius, obstacles)) continue;
                float score = MathF.Sqrt(legLengthSquared) +
                              MathF.Sqrt(DistanceSquared(cornerX, cornerZ, targetX, targetZ));
                if (SegmentCrossesObstacle(cornerX, cornerZ, targetX, targetZ, radius, obstacles)) score += 1000f;
                if (score >= bestScore) continue;
                float inverseLength = 1f / MathF.Sqrt(legLengthSquared);
                bestScore = score;
                bestDirectionX = legX * inverseLength;
                bestDirectionZ = legZ * inverseLength;
            }

            if (bestScore < float.MaxValue)
            {
                directionX = bestDirectionX;
                directionZ = bestDirectionZ;
                return;
            }
            float directInverseLength = 1f / MathF.Sqrt(directLengthSquared);
            directionX = directX * directInverseLength;
            directionZ = directZ * directInverseLength;
        }

        private static bool TryFindBlockingObstacle(float startX, float startZ, float endX, float endZ, float radius,
            IReadOnlyList<ArcadeObstacleConfig> obstacles, out ArcadeObstacleConfig blocking)
        {
            blocking = null;
            if (obstacles == null) return false;
            float nearestEntry = float.MaxValue;
            foreach (ArcadeObstacleConfig obstacle in obstacles)
            {
                if (!TryGetSegmentObstacleEntry(startX, startZ, endX, endZ, radius, obstacle, out float entry) ||
                    entry >= nearestEntry) continue;
                nearestEntry = entry;
                blocking = obstacle;
            }
            return blocking != null;
        }

        private static bool SegmentCrossesObstacle(float startX, float startZ, float endX, float endZ, float radius,
            IReadOnlyList<ArcadeObstacleConfig> obstacles)
        {
            if (obstacles == null) return false;
            foreach (ArcadeObstacleConfig obstacle in obstacles)
                if (TryGetSegmentObstacleEntry(startX, startZ, endX, endZ, radius, obstacle, out _)) return true;
            return false;
        }

        private static bool TryGetSegmentObstacleEntry(float startX, float startZ, float endX, float endZ,
            float radius, ArcadeObstacleConfig obstacle, out float entry)
        {
            const float interiorEpsilon = 0.0001f;
            float minimumX = obstacle.CenterX - obstacle.HalfWidth - radius + interiorEpsilon;
            float maximumX = obstacle.CenterX + obstacle.HalfWidth + radius - interiorEpsilon;
            float minimumZ = obstacle.CenterY - obstacle.HalfHeight - radius + interiorEpsilon;
            float maximumZ = obstacle.CenterY + obstacle.HalfHeight + radius - interiorEpsilon;
            float deltaX = endX - startX;
            float deltaZ = endZ - startZ;
            float enter = 0f;
            float exit = 1f;
            if (!ClipSegmentAxis(startX, deltaX, minimumX, maximumX, ref enter, ref exit) ||
                !ClipSegmentAxis(startZ, deltaZ, minimumZ, maximumZ, ref enter, ref exit) || enter > exit)
            {
                entry = 0f;
                return false;
            }
            entry = enter;
            return exit > interiorEpsilon && enter < 1f - interiorEpsilon;
        }

        private static bool ClipSegmentAxis(float origin, float delta, float minimum, float maximum,
            ref float enter, ref float exit)
        {
            if (MathF.Abs(delta) <= 0.000001f) return origin > minimum && origin < maximum;
            float first = (minimum - origin) / delta;
            float second = (maximum - origin) / delta;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }
            enter = MathF.Max(enter, first);
            exit = MathF.Min(exit, second);
            return enter <= exit;
        }

        private static void SpawnEnemyProjectile(OnlineArcadeBattleRuntime battle, OnlineArcadeEnemyRuntime enemy,
            float directionX, float directionZ)
        {
            ProjectileConfig config = ServerConfig.Instance.GetProjectile(enemy.ProjectileConfigId);
            float speed = Math.Max(0.1f, config.MoveSpeed);
            long runtimeId = battle.NextProjectileRuntimeId++;
            battle.Projectiles.Add(runtimeId, new OnlineArcadeProjectileRuntime
            {
                RuntimeId = runtimeId,
                OwnerPlayerId = 0,
                PositionX = enemy.PositionX,
                PositionZ = enemy.PositionZ,
                VelocityX = directionX * speed,
                VelocityZ = directionZ * speed,
                Damage = enemy.Damage,
                RemainingSeconds = Math.Max(0.1f, config.MaxTravelDistance / speed),
                Behavior = ArcadeWeaponBehavior.Direct,
                RemainingHits = 1,
            });
        }

        /// <summary>由权威战斗推进调用；临时武器优先，近战直接命中，远程成功创建弹丸后才提交攻击冷却。</summary>
        private static bool Fire(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player, OnlineArcadePlayerRecord record)
        {
            float aimLengthSquared = player.AimX * player.AimX + player.AimZ * player.AimZ;
            if (battle.AwaitingExit || aimLengthSquared < AimDeadZoneSquared) return false;
            string weaponId = player.TemporaryWeaponSeconds > 0f ? player.TemporaryWeaponId : record.WeaponId;
            ArcadeWeaponConfig weapon = ServerConfig.Instance.Tables.TbArcadeWeapon.GetOrDefault(weaponId) ??
                                        ServerConfig.Instance.ArcadeRule.BaseWeaponId_Ref;
            // 旧装备仅保留存档身份兼容；街机攻击权威不再允许使用近战武器。
            if (weapon != null && weapon.Behavior == ArcadeWeaponBehavior.Melee)
                weapon = ServerConfig.Instance.ArcadeRule.BaseWeaponId_Ref;
            if (weapon == null) return false;
            float inverseAimLength = 1f / MathF.Sqrt(aimLengthSquared);
            float aimX = player.AimX * inverseAimLength;
            float aimZ = player.AimZ * inverseAimLength;
            int damage = Math.Max(1, (int)MathF.Round(weapon.Damage *
                (1f + UpgradeTotal(record, ArcadeUpgradeKind.DamagePercent))));
            if (weapon.Behavior == ArcadeWeaponBehavior.Melee)
            {
                // 近战不占投射物预算；复用身份缓冲，死亡掉落移除敌人不会使遍历失效。
                ArcadeArenaConfig arena = ResolveBattleArena(battle);
                battle.EnemyIterationIds.Clear();
                battle.EnemyIterationIds.AddRange(battle.Enemies.Keys);
                foreach (long enemyId in battle.EnemyIterationIds)
                {
                    if (!battle.Enemies.TryGetValue(enemyId, out OnlineArcadeEnemyRuntime enemy) ||
                        !IsWithinMeleeStrike(player.PositionX, player.PositionZ, aimX, aimZ,
                            enemy.PositionX, enemy.PositionZ, weapon.MeleeRange, weapon.MeleeArcDegrees) ||
                        SegmentCrossesObstacle(player.PositionX, player.PositionZ, enemy.PositionX,
                            enemy.PositionZ, 0f, EffectiveObstacles(battle))) continue;
                    enemy.LastDamagePlayerId = player.PlayerId;
                    DamageEnemy(battle, enemy, damage);
                    if (enemy.CurrentHealth <= 0) self.KillEnemy(battle, enemy);
                }
                player.FireSequence++;
                player.FireCooldownSeconds = weapon.FireIntervalSeconds /
                    Math.Max(1f, 1f + UpgradeTotal(record, ArcadeUpgradeKind.FireRatePercent));
                return true;
            }
            int created = 0;
            for (int index = 0; index < weapon.ProjectileCount &&
                 battle.Projectiles.Count < ServerConfig.Instance.ArcadeRule.MaxProjectiles; index++)
            {
                float angle = weapon.ProjectileCount == 1 ? 0f :
                    -weapon.SpreadDegrees * 0.5f + weapon.SpreadDegrees * index / (weapon.ProjectileCount - 1);
                Rotate(aimX, aimZ, angle, out float directionX, out float directionZ);
                long runtimeId = battle.NextProjectileRuntimeId++;
                battle.Projectiles.Add(runtimeId, new OnlineArcadeProjectileRuntime
                {
                    RuntimeId = runtimeId,
                    OwnerPlayerId = player.PlayerId,
                    FromPlayerWeapon = true,
                    PositionX = player.PositionX + directionX * ServerConfig.Instance.ArcadeRule.PlayerRadius,
                    PositionZ = player.PositionZ + directionZ * ServerConfig.Instance.ArcadeRule.PlayerRadius,
                    VelocityX = directionX * weapon.ProjectileSpeed,
                    VelocityZ = directionZ * weapon.ProjectileSpeed,
                    Damage = damage,
                    RemainingSeconds = weapon.ProjectileLifetimeSeconds,
                    Behavior = weapon.Behavior,
                    RemainingHits = Math.Max(1, weapon.MaxHits),
                    ExplosionRadius = Math.Max(0f, weapon.ExplosionRadius),
                });
                created++;
            }
            if (created > 0)
            {
                player.FireSequence++;
                player.FireCooldownSeconds = weapon.FireIntervalSeconds /
                                             Math.Max(1f, 1f + UpgradeTotal(record, ArcadeUpgradeKind.FireRatePercent));
            }
            return created > 0;
        }

        /// <summary>服务器按街机武器配置裁决近战扇形，距离与总角度分别使用米和度。</summary>
        internal static bool IsWithinMeleeStrike(float originX, float originZ, float aimX, float aimZ,
            float targetX, float targetZ, float range, float arcDegrees)
        {
            float offsetX = targetX - originX;
            float offsetZ = targetZ - originZ;
            float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
            float aimSquared = aimX * aimX + aimZ * aimZ;
            if (range <= 0f || arcDegrees <= 0f || arcDegrees > 360f ||
                distanceSquared > range * range || aimSquared < 0.0001f) return false;
            return distanceSquared < 0.0001f ||
                (aimX * offsetX + aimZ * offsetZ) / MathF.Sqrt(aimSquared * distanceSquared) >=
                MathF.Cos(arcDegrees * 0.5f * MathF.PI / 180f);
        }

        private static bool TickProjectiles(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            float deltaSeconds)
        {
            if (battle.Projectiles.Count == 0) return false;
            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            ArcadeArenaConfig arena = ResolveBattleArena(battle);
            battle.ProjectileIterationIds.Clear();
            battle.ProjectileIterationIds.AddRange(battle.Projectiles.Keys);
            bool changed = false;
            foreach (long projectileId in battle.ProjectileIterationIds)
            {
                if (!battle.Projectiles.TryGetValue(projectileId, out OnlineArcadeProjectileRuntime projectile)) continue;
                TickTrackingProjectile(battle,projectile,deltaSeconds);
                float priorX = projectile.PositionX, priorZ = projectile.PositionZ;
                projectile.PositionX += projectile.VelocityX * deltaSeconds;
                projectile.PositionZ += projectile.VelocityZ * deltaSeconds;
                projectile.RemainingSeconds -= deltaSeconds;
                if(self.TryHitGroundBarrel(battle,projectile,priorX,priorZ,projectile.PositionX,projectile.PositionZ))
                {
                    // 与射中箱体一致，桶爆炸不能吞掉火箭/榴弹原有武器伤害与半径。
                    if(IsExplosiveBehavior(projectile.Behavior))self.DamageEnemiesInRadius(battle,projectile.PositionX,projectile.PositionZ,
                        projectile.ExplosionRadius,projectile.Damage,projectile.OwnerPlayerId);
                    battle.Projectiles.Remove(projectileId);changed=true;continue;
                }
                bool wallCrossed=ClipPlayerShotToWall(battle,projectile,priorX,priorZ);
                bool expired = projectile.RemainingSeconds <= 0f;
                bool hitObstacle = wallCrossed || InsideObstacle(projectile.PositionX, projectile.PositionZ, 0f, EffectiveObstacles(battle));
                bool remove = expired || MathF.Abs(projectile.PositionX) > rule.ArenaHalfWidth ||
                              MathF.Abs(projectile.PositionZ) > rule.ArenaHalfHeight || hitObstacle;
                if (projectile.OwnerPlayerId != 0 &&
                    ShouldExplodeOnRemoval(projectile.Behavior, expired, hitObstacle))
                    self.DamageEnemiesInRadius(battle, projectile.PositionX, projectile.PositionZ,
                        projectile.ExplosionRadius, projectile.Damage, projectile.OwnerPlayerId);
                if (!remove && projectile.OwnerPlayerId != 0 && self.HitCrate(battle, priorX, priorZ,
                    projectile.PositionX, projectile.PositionZ, projectile.Damage))
                {
                    if (IsExplosiveBehavior(projectile.Behavior))
                        self.DamageEnemiesInRadius(battle, projectile.PositionX, projectile.PositionZ, projectile.ExplosionRadius, projectile.Damage, projectile.OwnerPlayerId);
                    remove = true;
                }
                if (!remove && projectile.OwnerPlayerId == 0)
                {
                    foreach (OnlineArcadeBattlePlayerRuntime player in battle.Players.Values)
                    {
                        if (!player.Alive || player.InTreasureRoom) continue;
                        float hitRadius = rule.PlayerRadius + ProjectileHitRadius;
                        if (DistanceSquared(projectile.PositionX, projectile.PositionZ,
                                player.PositionX, player.PositionZ) > hitRadius * hitRadius)
                            continue;
                        self.DamagePlayer(battle, player, projectile.Damage);
                        if(player.Alive&&projectile.ContentSlowSeconds>0)player.ContentSlowRemaining=Math.Max(player.ContentSlowRemaining,projectile.ContentSlowSeconds);
                        remove = true;
                        break;
                    }
                }
                else if (!remove)
                {
                    foreach (long enemyId in battle.EnemyIterationIds)
                    {
                        if (!battle.Enemies.TryGetValue(enemyId, out OnlineArcadeEnemyRuntime enemy)) continue;
                        float hitRadius = rule.EnemyContactRadius + ProjectileHitRadius;
                        if (DistanceSquared(projectile.PositionX, projectile.PositionZ, enemy.PositionX, enemy.PositionZ) > hitRadius * hitRadius)
                            continue;
                        if (IsExplosiveBehavior(projectile.Behavior))
                        {
                            self.DamageEnemiesInRadius(battle, projectile.PositionX, projectile.PositionZ,
                                projectile.ExplosionRadius, projectile.Damage, projectile.OwnerPlayerId);
                            remove = true;
                            break;
                        }
                        if (!TryRegisterProjectileHit(projectile, enemy.RuntimeId)) continue;
                        enemy.LastDamagePlayerId = projectile.OwnerPlayerId;
                        DamageEnemy(battle, enemy, projectile.Damage);
                        if (enemy.CurrentHealth <= 0) self.KillEnemy(battle, enemy);
                        remove = projectile.Behavior != ArcadeWeaponBehavior.Piercing || projectile.RemainingHits <= 0;
                        if (remove) break;
                    }
                }
                if (remove) battle.Projectiles.Remove(projectileId);
                changed = true;
            }
            return changed;
        }

        /// <summary>服务器在火箭命中帧一次性结算范围伤害，敌人顺序沿用稳定 RuntimeId 快照。</summary>
        private static void DamageEnemiesInRadius(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            float centerX, float centerZ, float radius, int damage, long ownerPlayerId = 0)
        {
            if (radius <= 0f) return;
            self.DamageCratesInRadius(battle, centerX, centerZ, radius, damage);
            float radiusSquared = radius * radius;
            foreach (long enemyId in battle.EnemyIterationIds)
            {
                if (!battle.Enemies.TryGetValue(enemyId, out OnlineArcadeEnemyRuntime enemy) ||
                    DistanceSquared(centerX, centerZ, enemy.PositionX, enemy.PositionZ) > radiusSquared) continue;
                enemy.LastDamagePlayerId = ownerPlayerId;
                DamageEnemy(battle, enemy, damage);
                if (enemy.CurrentHealth <= 0) self.KillEnemy(battle, enemy);
            }
        }

        private static void DamageAndKnockbackEnemies(this OnlineArcadeRunActor self,
            OnlineArcadeBattleRuntime battle, float centerX, float centerZ, float radius, int damage,
            float knockbackDistance, long ownerPlayerId = 0)
        {
            float radiusSquared = radius * radius;
            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            ArcadeArenaConfig arena = ResolveBattleArena(battle);
            foreach (long enemyId in battle.EnemyIterationIds)
            {
                if (!battle.Enemies.TryGetValue(enemyId, out OnlineArcadeEnemyRuntime enemy) ||
                    DistanceSquared(centerX, centerZ, enemy.PositionX, enemy.PositionZ) > radiusSquared) continue;
                float offsetX = enemy.PositionX - centerX;
                float offsetZ = enemy.PositionZ - centerZ;
                float length = MathF.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
                if (length < 0.001f) { offsetX = 0f; offsetZ = 1f; length = 1f; }
                float nextX = enemy.PositionX + offsetX / length * knockbackDistance;
                float nextZ = enemy.PositionZ + offsetZ / length * knockbackDistance;
                // 入场阶段不夹取到玩家边界，避免击退把水侧目标瞬间搬上岸。
                if (!enemy.EnteringIsland || IslandFor(battle) != null)
                    ResolvePosition(ref nextX, ref nextZ, enemy.PositionX, enemy.PositionZ,
                        rule.EnemyContactRadius, rule, EffectiveObstacles(battle), IslandFor(battle));
                enemy.PositionX = nextX;
                enemy.PositionZ = nextZ;
                enemy.LastDamagePlayerId = ownerPlayerId;
                DamageEnemy(battle, enemy, damage);
                if (enemy.CurrentHealth <= 0) self.KillEnemy(battle, enemy);
            }
        }

        private static bool TryGetPowerup(OnlineArcadeBattlePlayerRuntime player,
            ArcadePowerupBehavior behavior, out OnlineArcadePowerupRuntime powerup) =>
            player.Powerups.TryGetValue(behavior, out powerup) && powerup.RemainingSeconds > 0f;

        private static bool TryFindNearestDecoy(OnlineArcadeBattleRuntime battle, float x, float z,
            out OnlineArcadePowerupRuntime nearest)
        {
            nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (OnlineArcadeBattlePlayerRuntime player in battle.Players.Values)
            {
                if (player.InTreasureRoom || !TryGetPowerup(player, ArcadePowerupBehavior.Decoy,
                        out OnlineArcadePowerupRuntime decoy)) continue;
                float distance = DistanceSquared(x, z, decoy.PositionX, decoy.PositionZ);
                if (distance >= nearestDistance) continue;
                nearest = decoy;
                nearestDistance = distance;
            }
            return nearest != null;
        }

        private static float EnemySpeedMultiplier(OnlineArcadeBattleRuntime battle)
        {
            float multiplier = 1f;
            foreach (OnlineArcadeBattlePlayerRuntime player in battle.Players.Values)
                if (!player.InTreasureRoom && TryGetPowerup(player, ArcadePowerupBehavior.Clock,
                        out OnlineArcadePowerupRuntime clock))
                    multiplier = Math.Min(multiplier, Math.Max(0f, clock.Magnitude));
            return multiplier;
        }

        private static bool TryRegisterProjectileHit(OnlineArcadeProjectileRuntime projectile, long enemyId)
        {
            if (projectile == null || projectile.OwnerPlayerId == 0 || projectile.RemainingHits <= 0 || enemyId <= 0 ||
                !projectile.HitEnemyIds.Add(enemyId)) return false;
            projectile.RemainingHits--;
            return true;
        }

        /// <summary>普通爆炸弹只在碰撞时引爆；定时榴弹还会在飞行时间耗尽时引爆。</summary>
        private static bool ShouldExplodeOnRemoval(ArcadeWeaponBehavior behavior, bool expired, bool hitObstacle) =>
            IsExplosiveBehavior(behavior) &&
            (hitObstacle || behavior == ArcadeWeaponBehavior.TimedExplosive && expired);

        private static bool IsExplosiveBehavior(ArcadeWeaponBehavior behavior) =>
            behavior == ArcadeWeaponBehavior.Explosive || behavior == ArcadeWeaponBehavior.TimedExplosive;

        /// <summary>移除死亡敌人并用本局与敌人身份生成确定性掉落，确保重放不依赖进程随机状态。</summary>
        private static void KillEnemy(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            OnlineArcadeEnemyRuntime enemy, bool hazardKill = false)
        {
            if (!battle.Enemies.Remove(enemy.RuntimeId)) return;
            if (!hazardKill) AwardIslandKill(self, battle, enemy);
            if (!hazardKill) self.SpawnContentDeathZone(battle,enemy);
            if (enemy.ContentBossKind>0)
            {
                battle.RoundBossDefeated=true; battle.AwaitingExit=true;
                if(battle.ArenaConfigNumber==20)battle.FinalBossDefeated=true;
                battle.SpawnQueue.Clear();battle.SpawnQueueIndex=0; battle.Projectiles.Clear();
            }
            if (battle.IsFinalBoss && enemy.RuntimeId == battle.FinalBossEnemyId)
            {
                battle.FinalBossDefeated = true;
                battle.AwaitingExit = true;
                battle.SpawnQueue.Clear();
                battle.SpawnQueueIndex = 0;
                foreach (var remaining in battle.Enemies.Values.ToArray()) self.KillEnemy(battle, remaining, true);
                battle.Projectiles.Clear();
            }
            foreach (var hazard in battle.Hazards) hazard.ForgetTarget(enemy.RuntimeId);
            if (hazardKill || battle.Drops.Count >= ServerConfig.Instance.ArcadeRule.MaxDrops) return;
            uint seed = MixDropSeed(unchecked((ulong)self.Record.RunId), battle.ArenaNumber, enemy.RuntimeId);
            float chanceRoll = (seed & 0x00ffffffu) / 16777216f;
            if (chanceRoll >= ServerConfig.Instance.ArcadeRule.DropChance) return;
            List<ArcadeDropConfig> candidates = ServerConfig.Instance.Tables.TbArcadeDrop.DataList
                .Where(x => x.Weight > 0 && x.MinArena <= battle.ArenaNumber &&
                            (x.MaxArena <= 0 || x.MaxArena >= battle.ArenaNumber))
                .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            int totalWeight = candidates.Sum(x => x.Weight);
            if (totalWeight <= 0) return;
            int roll = (int)(MixDropSeed(seed, battle.ArenaNumber, enemy.RuntimeId + 7919) % (uint)totalWeight);
            ArcadeDropConfig selected = null;
            foreach (ArcadeDropConfig candidate in candidates)
            {
                if (roll < candidate.Weight) { selected = candidate; break; }
                roll -= candidate.Weight;
            }
            if (selected == null) return;
            long runtimeId = battle.NextDropRuntimeId++;
            var dropPosition = IslandFor(battle)?.Constrain(enemy.PositionX, enemy.PositionZ, ServerConfig.Instance.ArcadeRule.PlayerRadius);
            battle.Drops.Add(runtimeId, new OnlineArcadeDropRuntime
            {
                RuntimeId = runtimeId,
                DropConfigId = selected.Id,
                PositionX = dropPosition?.X ?? enemy.PositionX,
                PositionZ = dropPosition?.Y ?? enemy.PositionZ,
            });
        }

        /// <summary>消耗个人炸弹并清除当前已生成敌人；每个敌人仍走统一死亡掉落规则。</summary>
        private static void UseBomb(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player)
        {
            OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == player.PlayerId);
            if (battle.AwaitingExit || player.InTreasureRoom || record.Bombs <= 0) return;
            record.Bombs--;
            if (IslandFor(battle) != null) player.InvulnerabilitySeconds = Math.Max(player.InvulnerabilitySeconds, 3f);
            if (player.InTreasureRoom) return;
            battle.EnemyIterationIds.Clear();
            battle.EnemyIterationIds.AddRange(battle.Enemies.Keys);
            battle.EnemyIterationIds.Sort();
            foreach (long enemyId in battle.EnemyIterationIds)
                if (battle.Enemies.TryGetValue(enemyId, out OnlineArcadeEnemyRuntime enemy) && enemyId != battle.FinalBossEnemyId && enemy.ContentBossKind == 0)
                {
                    enemy.LastDamagePlayerId = player.PlayerId;
                    self.KillEnemy(battle, enemy);
                }
        }

        /// <summary>冲刺只能在存活且未冲刺时消耗；速度与无敌均由服务器计时。</summary>
        private static void UseDash(this OnlineArcadeRunActor self, OnlineArcadeBattlePlayerRuntime player)
        {
            OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == player.PlayerId);
            if (record.Dashes <= 0 || player.DashSeconds > 0f) return;
            record.Dashes--;
            player.DashSeconds = DashDurationSeconds;
            player.InvulnerabilitySeconds = Math.Max(player.InvulnerabilitySeconds, DashDurationSeconds);
        }

        /// <summary>只在可接收者中按距离、玩家 ID 决胜；满额保留，成功实例先注销再发奖。</summary>
        /// <summary>拾取前推进已有掉落寿命，等待出口期间也生效；到期不结算任何奖励。</summary>
        private static bool TickDropLifetimes(OnlineArcadeBattleRuntime battle, float deltaSeconds)
        {
            if (battle.Drops.Count == 0 || deltaSeconds <= 0f) return false;
            battle.DropIterationIds.Clear();
            battle.DropIterationIds.AddRange(battle.Drops.Keys);
            foreach (long dropId in battle.DropIterationIds)
            {
                OnlineArcadeDropRuntime drop = battle.Drops[dropId];
                drop.RemainingSeconds = Math.Max(0f, drop.RemainingSeconds - deltaSeconds);
                if (drop.RemainingSeconds <= 0f) battle.Drops.Remove(dropId);
            }
            return true;
        }

        private static bool TickDrops(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle)
        {
            if (battle.Drops.Count == 0) return false;
            battle.DropIterationIds.Clear();
            battle.DropIterationIds.AddRange(battle.Drops.Keys);
            battle.DropIterationIds.Sort();
            bool changed = false;
            foreach (long dropId in battle.DropIterationIds)
            {
                if (!battle.Drops.TryGetValue(dropId, out OnlineArcadeDropRuntime drop)) continue;
                ArcadeDropConfig definition = ServerConfig.Instance.Tables.TbArcadeDrop.Get(drop.DropConfigId);
                bool supply = definition.Kind == ArcadeDropKind.Bomb || definition.Kind == ArcadeDropKind.Dash;
                OnlineArcadeBattlePlayerRuntime winner = null;
                float winnerDistance = float.MaxValue;
                foreach (OnlineArcadeBattlePlayerRuntime player in battle.Players.Values)
                {
                    if (!player.Alive || player.InTreasureRoom) continue;
                    OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == player.PlayerId);
                    if (supply && SupplyAddedAmount(definition.Kind == ArcadeDropKind.Bomb
                            ? record.Bombs : record.Dashes, definition) <= 0) continue;
                    float pickupRadius = BasePickupRadius *
                                         (1f + UpgradeTotal(record, ArcadeUpgradeKind.PickupRadiusPercent));
                    if (TryGetPowerup(player, ArcadePowerupBehavior.Magnet, out OnlineArcadePowerupRuntime magnet))
                        pickupRadius = Math.Max(pickupRadius, magnet.Radius);
                    float distance = DistanceSquared(drop.PositionX, drop.PositionZ, player.PositionX, player.PositionZ);
                    if (distance > pickupRadius * pickupRadius || distance > winnerDistance ||
                        MathF.Abs(distance - winnerDistance) < 0.0001f && winner != null && player.PlayerId > winner.PlayerId)
                        continue;
                    winner = player;
                    winnerDistance = distance;
                }
                if (winner == null) continue;
                var winnerRecord = self.Record.Players.First(x => x.PlayerId == winner.PlayerId);
                int added = supply ? SupplyAddedAmount(definition.Kind == ArcadeDropKind.Bomb
                    ? winnerRecord.Bombs : winnerRecord.Dashes, definition) : 0;
                long beforeScore = winnerRecord.Score;
                int beforeGold = winnerRecord.RunGold;
                if (!battle.Drops.Remove(dropId)) continue;
                if (!self.ApplyDrop(winner, definition))
                {
                    battle.Drops.Add(dropId, drop);
                    continue;
                }
                drop.CollectedByPlayerId = winner.PlayerId;
                drop.AddedAmount = added;
                drop.ScoreAdded = winnerRecord.Score - beforeScore;
                drop.GoldAdded = winnerRecord.RunGold - beforeGold;
                battle.SupplyPickups.Add(drop);
                changed = true;
            }
            return changed;
        }

        /// <summary>先计算剩余容量，避免溢出；满额或无有效奖励为零，不降低超额库存。</summary>
        private static int SupplyAddedAmount(int held, ArcadeDropConfig drop) =>
            (int)Math.Max(0L, Math.Min((long)drop.Amount, (long)drop.MaxOwned - held));

        /// <summary>验证产品及可接收数量后应用奖励；失败不产生领取记录。</summary>
        private static bool ApplyDrop(this OnlineArcadeRunActor self, OnlineArcadeBattlePlayerRuntime player,
            ArcadeDropConfig drop)
        {
            OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == player.PlayerId);
            switch (drop.Kind)
            {
                case ArcadeDropKind.RunGold:
                    if (drop.Amount <= 0) return false;
                    if (self.Record.IsCasualRoute)
                    {
                        var rules = FirepowerLoop.Arcade.ArcadeTreasureRoomParameters.FromConfig(
                            ServerConfig.Instance.Tables.TbArcadeTreasureRoom.DataList.First());
                        return ApplyTreasureReward(self, player.PlayerId, new FirepowerLoop.Arcade.ArcadeTreasureRewardParameters(
                            0, FirepowerLoop.Arcade.ArcadeTreasureRewardKind.GoldBar, 0, 0, drop.Amount, drop.Amount, 1), rules, false);
                    }
                    if (record.RunGold > int.MaxValue - drop.Amount) return false;
                    record.RunGold += drop.Amount;
                    break;
                case ArcadeDropKind.Bomb:
                    if (SupplyAddedAmount(record.Bombs, drop) <= 0) return false;
                    record.Bombs += SupplyAddedAmount(record.Bombs, drop);
                    break;
                case ArcadeDropKind.Dash:
                    if (SupplyAddedAmount(record.Dashes, drop) <= 0) return false;
                    record.Dashes += SupplyAddedAmount(record.Dashes, drop);
                    break;
                case ArcadeDropKind.TemporaryWeapon:
                    if (drop.DurationSeconds <= 0 || ServerConfig.Instance.Tables.TbArcadeWeapon.GetOrDefault(drop.ProductId) == null) return false;
                    player.TemporaryWeaponId = drop.ProductId;
                    player.TemporaryWeaponSeconds = drop.DurationSeconds;
                    break;
                case ArcadeDropKind.Powerup:
                    ArcadePowerupConfig definition = ServerConfig.Instance.Tables.TbArcadePowerup.GetOrDefault(drop.ProductId);
                    if (definition == null || drop.DurationSeconds <= 0) return false;
                    player.Powerups[definition.Behavior] = new OnlineArcadePowerupRuntime
                    {
                        ProductId = definition.Id,
                        Behavior = definition.Behavior,
                        Magnitude = definition.Magnitude,
                        Damage = definition.Damage,
                        IntervalSeconds = definition.IntervalSeconds,
                        Radius = definition.Radius,
                        ProjectileSpeed = definition.ProjectileSpeed,
                        RemainingSeconds = drop.DurationSeconds,
                        Charges = definition.Charges,
                        PositionX = player.PositionX,
                        PositionZ = player.PositionZ,
                    };
                    break;
                default: return false;
            }
            return true;
        }

        /// <summary>推进并结算每名玩家的全部并存强化；状态只属于当前竞技场，永不进入安全节点。</summary>
        private static bool TickPowerups(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            float deltaSeconds)
        {
            bool changed = false;
            foreach (OnlineArcadeBattlePlayerRuntime player in battle.Players.Values.OrderBy(x => x.PlayerId))
            {
                foreach (KeyValuePair<ArcadePowerupBehavior, OnlineArcadePowerupRuntime> pair in
                         player.Powerups.ToList())
                {
                    OnlineArcadePowerupRuntime powerup = pair.Value;
                    powerup.RemainingSeconds = Math.Max(0f, powerup.RemainingSeconds - deltaSeconds);
                    powerup.CooldownSeconds = Math.Max(0f, powerup.CooldownSeconds - deltaSeconds);
                    changed = true;
                    if (powerup.RemainingSeconds <= 0f)
                    {
                        if (!player.InTreasureRoom && pair.Key == ArcadePowerupBehavior.Decoy)
                            self.DamageEnemiesInRadius(battle, powerup.PositionX, powerup.PositionZ,
                                powerup.Radius, powerup.Damage, player.PlayerId);
                        player.Powerups.Remove(pair.Key);
                        continue;
                    }
                    if (!player.Alive || player.InTreasureRoom || powerup.CooldownSeconds > 0f || battle.Enemies.Count == 0) continue;
                    switch (pair.Key)
                    {
                        case ArcadePowerupBehavior.Chicken:
                            changed |= SpawnPowerupProjectile(battle, player, powerup,
                                player.PositionX, player.PositionZ, 0f);
                            break;
                        case ArcadePowerupBehavior.Sentry:
                            changed |= SpawnPowerupProjectile(battle, player, powerup,
                                powerup.PositionX, powerup.PositionZ, powerup.Radius);
                            break;
                        case ArcadePowerupBehavior.ElectricOrb:
                        case ArcadePowerupBehavior.Saw:
                            self.DamageEnemiesInRadius(battle, player.PositionX, player.PositionZ,
                                powerup.Radius, powerup.Damage, player.PlayerId);
                            powerup.CooldownSeconds = powerup.IntervalSeconds;
                            break;
                        case ArcadePowerupBehavior.Gloves:
                            self.DamageAndKnockbackEnemies(battle, player.PositionX, player.PositionZ,
                                powerup.Radius, powerup.Damage, powerup.Magnitude, player.PlayerId);
                            powerup.CooldownSeconds = powerup.IntervalSeconds;
                            break;
                        case ArcadePowerupBehavior.Barrel:
                            if (powerup.Charges > 0 && battle.Enemies.Values.Any(enemy =>
                                    DistanceSquared(player.PositionX, player.PositionZ,
                                        enemy.PositionX, enemy.PositionZ) <= powerup.Magnitude * powerup.Magnitude))
                            {
                                self.DamageEnemiesInRadius(battle, player.PositionX, player.PositionZ,
                                    powerup.Radius, powerup.Damage, player.PlayerId);
                                powerup.Charges--;
                                powerup.CooldownSeconds = 0.45f;
                                if (powerup.Charges <= 0) powerup.RemainingSeconds = 0f;
                            }
                            break;
                    }
                }
            }
            return changed;
        }

        private static bool SpawnPowerupProjectile(OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime owner, OnlineArcadePowerupRuntime powerup,
            float originX, float originZ, float maxRadius)
        {
            if (battle.Projectiles.Count >= ServerConfig.Instance.ArcadeRule.MaxProjectiles) return false;
            OnlineArcadeEnemyRuntime target = battle.Enemies.Values
                .Where(enemy => maxRadius <= 0f || DistanceSquared(originX, originZ,
                    enemy.PositionX, enemy.PositionZ) <= maxRadius * maxRadius)
                .OrderBy(enemy => DistanceSquared(originX, originZ, enemy.PositionX, enemy.PositionZ))
                .ThenBy(enemy => enemy.RuntimeId).FirstOrDefault();
            if (target == null) return false;
            float offsetX = target.PositionX - originX;
            float offsetZ = target.PositionZ - originZ;
            float length = MathF.Sqrt(offsetX * offsetX + offsetZ * offsetZ);
            if (length < 0.001f) { offsetX = 0f; offsetZ = 1f; length = 1f; }
            long runtimeId = battle.NextProjectileRuntimeId++;
            battle.Projectiles.Add(runtimeId, new OnlineArcadeProjectileRuntime
            {
                RuntimeId = runtimeId,
                OwnerPlayerId = owner.PlayerId,
                PositionX = originX,
                PositionZ = originZ,
                VelocityX = offsetX / length * powerup.ProjectileSpeed,
                VelocityZ = offsetZ / length * powerup.ProjectileSpeed,
                Damage = powerup.Damage,
                RemainingSeconds = SupportProjectileLifetimeSeconds,
                Behavior = ArcadeWeaponBehavior.Direct,
                RemainingHits = 1,
            });
            // 只在弹丸成功创建后记录方向，双端视觉不会在预算封顶时假装开火转向。
            powerup.ShotDirectionX = offsetX / length;
            powerup.ShotDirectionZ = offsetZ / length;
            powerup.CooldownSeconds = powerup.IntervalSeconds;
            return true;
        }

        private static uint MixDropSeed(ulong seed, int arenaNumber, long runtimeId)
        {
            ulong value = seed ^ unchecked((ulong)runtimeId * 0x9E3779B97F4A7C15UL) ^
                          unchecked((ulong)(uint)arenaNumber * 0xBF58476D1CE4E5B9UL);
            value ^= value >> 30; value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27; value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return unchecked((uint)(value ^ (value >> 32)));
        }

        /// <summary>应用一次受击；死亡只消耗本局生命，复活保持死亡坐标且不写安全节点。</summary>
        private static void DamagePlayer(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            OnlineArcadeBattlePlayerRuntime player, int damage)
        {
            if (!player.Alive || player.InTreasureRoom || player.InvulnerabilitySeconds > 0f) return;
            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            if (TryGetPowerup(player, ArcadePowerupBehavior.Shield,
                    out OnlineArcadePowerupRuntime shield) && shield.Charges > 0)
            {
                shield.Charges--;
                if (shield.Charges <= 0) player.Powerups.Remove(ArcadePowerupBehavior.Shield);
                player.InvulnerabilitySeconds = rule.ContactInvulnerabilitySeconds;
                return;
            }
            player.CurrentHealth -= Math.Max(1, damage);
            player.InvulnerabilitySeconds = rule.ContactInvulnerabilitySeconds;
            if (player.CurrentHealth > 0) return;

            OnlineArcadePlayerRecord record = self.Record.Players.First(x => x.PlayerId == player.PlayerId);
            player.CurrentHealth = 0;
            player.Alive = false;
            player.MoveX = 0f;
            player.MoveZ = 0f;
            player.Firing = false;
            if (battle.IsChallenge)
            {
                player.RespawnSeconds = 0f;
                if (battle.Players.Values.All(x => !x.Alive))
                {
                    battle.ChallengeCompletionReady = true;
                    battle.ChallengeSucceeded = false;
                }
                return;
            }
            record.ScoreMultiplier = 1; record.MultiplierProgress = 0;
            record.Lives = Math.Max(0, record.Lives - 1);
            player.RespawnSeconds = record.Lives > 0 ? rule.RespawnDelaySeconds : 0f;
            battle.AllPlayersDefeated = battle.Players.Values.All(x => !x.Alive &&
                self.Record.Players.First(recordPlayer => recordPlayer.PlayerId == x.PlayerId).Lives <= 0);
        }

        /// <summary>服务端唯一机关伤害入口；客户端只同步时间。复活、清场等待与断线不推进。</summary>
        private static bool TickHazards(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle, float seconds)
        {
            if (battle.Hazards.Count == 0 || battle.WaveClearAwarded ||
                battle.Players.Values.Any(player => player.RespawnSeconds > 0f)) return false;
            battle.HazardElapsedSeconds += seconds;
            foreach (var hazard in battle.Hazards) hazard.Advance(seconds);
            ArcadeRuleConfig rule = ServerConfig.Instance.ArcadeRule;
            foreach (var hazard in battle.Hazards)
            {
                foreach (var player in battle.Players.Values)
                {
                    if (!player.Alive || player.InTreasureRoom || player.InvulnerabilitySeconds > 0f || player.DashSeconds > 0f) continue;
                    if (!hazard.TryHit(-player.PlayerId, player.HazardPreviousX, player.HazardPreviousZ,
                        player.PositionX, player.PositionZ, rule.PlayerRadius)) continue;
                    self.DamagePlayer(battle, player, FirepowerLoop.Arcade.ArcadeHazardRules.Damage(player.MaxHealth,
                        hazard.Parameters.PlayerDamageRatio));
                    if (battle.AllPlayersDefeated || player.RespawnSeconds > 0f) return true;
                }
                battle.EnemyIterationIds.Clear();
                foreach (var enemy in battle.Enemies.Values) battle.EnemyIterationIds.Add(enemy.RuntimeId);
                foreach (long id in battle.EnemyIterationIds)
                {
                    if (!battle.Enemies.TryGetValue(id, out var enemy)) continue;
                    if (!hazard.TryHit(id, enemy.HazardPreviousX, enemy.HazardPreviousZ,
                        enemy.PositionX, enemy.PositionZ, rule.EnemyContactRadius)) continue;
                    bool boss = ResolveBattleArena(battle).IsBoss && battle.WaveIndex + 1 == battle.WaveCount;
                    enemy.CurrentHealth = boss ? enemy.CurrentHealth - FirepowerLoop.Arcade.ArcadeHazardRules.Damage(
                        enemy.MaxHealth, hazard.Parameters.BossDamageRatio) : 0;
                    if (enemy.CurrentHealth <= 0) self.KillEnemy(battle, enemy, true);
                }
            }
            return true;
        }

        private static bool TryAdvanceWave(this OnlineArcadeRunActor self, OnlineArcadeBattleRuntime battle,
            float deltaSeconds)
        {
            if (battle.Players.Values.Any(p => p.Alive) && battle.Players.Values.Where(p => p.Alive).All(p => p.InTreasureRoom)) return false;
            if (battle.IsFinalBoss || battle.RoundBossEncounter || battle.AwaitingExit || battle.SpawnQueueIndex < battle.SpawnQueue.Count || battle.Enemies.Count > 0) return false;
            if (battle.WaveIndex == 2 && battle.IsCasualRoute && battle.ArenaConfigNumber == 1 && !battle.TreasureCompleted) return false;
            if (!battle.WaveClearAwarded)
            {
                ArcadeArenaConfig arena = ResolveBattleArena(battle);
                if (!battle.IsChallenge && !UsesEarlyRounds(battle))
                {
                    ArcadeEnemyGroupConfig group = ResolveBattleGroup(battle, arena, battle.WaveIndex);
                    int gold = ScaleWaveGold(group.ClearGold, battle.ArenaNumber,
                        ServerConfig.Instance.ArcadeRule.GoldGrowthPerArena);
                    foreach (OnlineArcadePlayerRecord player in self.Record.Players)
                        player.RunGold = checked(player.RunGold + gold);
                }
                battle.WaveClearAwarded = true;
                battle.WaveTransitionSeconds = 0f;
                foreach (var player in battle.Players.Values) player.RestAttackReleased = false;
                if (!battle.IsChallenge && battle.RoundLayout != null) battle.AwaitingExit = true;
                else if (UsesEarlyRest(battle) && battle.WaveIndex + 1 == battle.WaveCount) battle.AwaitingExit = true;
                return true;
            }
            battle.WaveTransitionSeconds += deltaSeconds;
            var opening = FirepowerLoop.Arcade.ArcadeOpeningParameters.FromConfig(ResolveBattleArena(battle));
            foreach (var player in battle.Players.Values)
                if (!player.Firing) player.RestAttackReleased = true;
            bool advance = UsesEarlyRest(battle) && battle.Players.Values.Any(p => p.Alive && p.RestAttackReleased && p.Firing && !p.InTreasureRoom);
            if (!advance && battle.WaveTransitionSeconds < (UsesEarlyRest(battle)
                ? opening.RoundRestSeconds : WaveTransitionDurationSeconds)) return false;
            if (battle.WaveIndex + 1 >= battle.WaveCount)
            {
                if (battle.IsChallenge)
                {
                    battle.ChallengeCompletionReady = true;
                    battle.ChallengeSucceeded = true;
                }
                else if (battle.IsCasualRoute)
                {
                    ArcadeArenaConfig arena = ResolveBattleArena(battle);
                    if (!string.IsNullOrEmpty(arena.FinalBossId)) BeginFinalBoss(battle, arena);
                    else battle.AwaitingExit = true;
                }
                else battle.ArenaCompletionReady = true;
                return true;
            }
            battle.WaveTransitionSeconds = 0f;
            PrepareWave(battle, ResolveBattleArena(battle), battle.WaveIndex + 1);
            if (!battle.IsChallenge)
                foreach (var player in self.Record.Players.Where(p => p.Lives > 0))
                {
                    player.Bombs = FirepowerLoop.Arcade.ArcadeOpeningRules.ReplenishEmpty(player.Bombs);
                    player.Dashes = FirepowerLoop.Arcade.ArcadeOpeningRules.ReplenishEmpty(player.Dashes);
                }
            return true;
        }

        /// <summary>末场四轮后只启动一次独立 Boss，不增加轮数或提前保存完成。</summary>
        private static void BeginFinalBoss(OnlineArcadeBattleRuntime battle, ArcadeArenaConfig arena)
        {
            if (battle.IsFinalBoss) return;
            ArcadeBossConfig boss = ServerConfig.Instance.Tables.TbArcadeBoss.Get(arena.FinalBossId);
            ArcadeEnemyGroupConfig group = ServerConfig.Instance.Tables.TbArcadeEnemyGroup.Get(boss.EnemyGroupId);
            battle.IsFinalBoss = true;
            battle.Hazards.Clear();
            battle.SpawnQueue.Clear();
            battle.SpawnQueueIndex = 0;
            foreach (var member in group.Members)
                for (int index = 0; index < member.Count; index++) battle.SpawnQueue.Add(member.EnemyId);
            battle.MaxAlive = 1;
            battle.NextSpawnAt = 0;
            battle.ReinforcementCooldown = boss.ReinforcementIntervalSeconds;
        }

        /// <summary>Boss 存活时按配置补充普通敌人；队列与现场数量仍受原有上限约束。</summary>
        private static bool TickFinalBossReinforcements(OnlineArcadeBattleRuntime battle, float seconds, long now)
        {
            if (!battle.IsFinalBoss || battle.FinalBossDefeated || battle.FinalBossEnemyId == 0) return false;
            var boss = ServerConfig.Instance.Tables.TbArcadeBoss.Get(ResolveBattleArena(battle).FinalBossId);
            if (string.IsNullOrEmpty(boss.ReinforcementGroupId)) return false;
            battle.ReinforcementCooldown = Math.Max(0f, battle.ReinforcementCooldown - seconds);
            if (battle.ReinforcementCooldown > 0f || battle.SpawnQueueIndex < battle.SpawnQueue.Count) return false;
            var group = ServerConfig.Instance.Tables.TbArcadeEnemyGroup.Get(boss.ReinforcementGroupId);
            battle.SpawnQueue.Clear();
            battle.SpawnQueueIndex = 0;
            foreach (var member in group.Members)
                for (int index = 0; index < member.Count * battle.DifficultyPlayerCount; index++)
                    battle.SpawnQueue.Add(member.EnemyId);
            battle.MaxAlive = Math.Min(ServerConfig.Instance.ArcadeRule.MaxAliveEnemies, group.MaxAlive * battle.DifficultyPlayerCount);
            battle.NextSpawnAt = now;
            battle.ReinforcementCooldown = boss.ReinforcementIntervalSeconds;
            return true;
        }

        /// <summary>任意存活成员接触出口即可选路；同一 Tick 按稳定玩家 ID 仲裁，只提交一次。</summary>
        private static bool TickExit(OnlineArcadeBattleRuntime battle)
        {
            if (battle.RoundLayout != null) return TickRoundExit(battle);
            if (battle.TreasureRoom?.Members.Length > 0) return false;
            var arena = ResolveBattleArena(battle);
            int count = battle.FinalBossDefeated ? 1 : arena.NextArenaNumbers.Length;
            foreach (var player in battle.Players.Values.OrderBy(value => value.PlayerId))
            {
                if (!player.Alive || player.InTreasureRoom) continue;
                for (int index = 0; index < count; index++)
                {
                    float x = (index - (count - 1) * 0.5f) * 5f;
                    float z = ServerConfig.Instance.ArcadeRule.ArenaHalfHeight - 3f;
                    if (DistanceSquared(player.PositionX, player.PositionZ, x, z) > 2.25f) continue;
                    battle.SelectedExitConfigNumber = battle.FinalBossDefeated ? 0 : arena.NextArenaNumbers[index];
                    battle.ArenaCompletionReady = true;
                    return true;
                }
            }
            return false;
        }

        private static ArcadeArenaConfig ResolveBattleArena(OnlineArcadeBattleRuntime battle) =>
            ResolveArena(battle.ArenaConfigNumber > 0 ? battle.ArenaConfigNumber : battle.ArenaNumber);

        private static int ScaleWaveGold(int baseGold, int arenaNumber, float growth) =>
            Math.Max(0, (int)MathF.Round(baseGold * (1f + Math.Max(0, arenaNumber - 1) * growth)));

        private static float UpgradeTotal(OnlineArcadePlayerRecord player, ArcadeUpgradeKind kind)
        {
            float total = 0f;
            foreach (KeyValuePair<string, int> entry in player.UpgradeStacks)
            {
                ArcadeUpgradeConfig upgrade = ServerConfig.Instance.Tables.TbArcadeUpgrade.GetOrDefault(entry.Key);
                if (upgrade?.Kind == kind) total += upgrade.ValuePerStack * entry.Value;
            }
            return total;
        }

        private static void ResolvePosition(ref float x, ref float z, float previousX, float previousZ, float radius,
            ArcadeRuleConfig rule, IReadOnlyList<ArcadeObstacleConfig> obstacles, FirepowerLoop.Arcade.ArcadeIslandRegion island = null)
        {
            if (island != null)
            {
                var p = island.Constrain(x, z, radius); x = p.X; z = p.Y;
            }
            x = Math.Clamp(x, -rule.ArenaHalfWidth + radius, rule.ArenaHalfWidth - radius);
            z = Math.Clamp(z, -rule.ArenaHalfHeight + radius, rule.ArenaHalfHeight - radius);
            if (InsideObstacle(x, previousZ, radius, obstacles)) x = previousX;
            if (InsideObstacle(x, z, radius, obstacles)) z = previousZ;
            if (InsideObstacle(x, z, radius, obstacles))
            {
                x = previousX;
                z = previousZ;
            }
        }

        /// <summary>按 RuntimeId 固定顺序做两轮成对分离，复杂度受联机敌人预算上限约束。</summary>
        private static void ResolveEnemyCrowding(OnlineArcadeBattleRuntime battle, ArcadeRuleConfig rule,
            IReadOnlyList<ArcadeObstacleConfig> obstacles)
        {
            if (battle.EnemyIterationIds.Count < 2) return;
            float minimumDistance = rule.EnemyContactRadius * 2f;
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            for (int pass = 0; pass < 2; pass++)
            for (int leftIndex = 0; leftIndex < battle.EnemyIterationIds.Count - 1; leftIndex++)
            for (int rightIndex = leftIndex + 1; rightIndex < battle.EnemyIterationIds.Count; rightIndex++)
            {
                OnlineArcadeEnemyRuntime left = battle.Enemies[battle.EnemyIterationIds[leftIndex]];
                OnlineArcadeEnemyRuntime right = battle.Enemies[battle.EnemyIterationIds[rightIndex]];
                if (left.EnteringIsland || right.EnteringIsland) continue;
                float offsetX = right.PositionX - left.PositionX;
                float offsetZ = right.PositionZ - left.PositionZ;
                float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
                if (distanceSquared >= minimumDistanceSquared) continue;
                float distance = MathF.Sqrt(distanceSquared);
                float directionX = distance > 0.0001f ? offsetX / distance : ((left.RuntimeId & 1) == 0 ? 1f : -1f);
                float directionZ = distance > 0.0001f ? offsetZ / distance : 0f;
                float push = (minimumDistance - distance) * 0.5f;
                float leftX = left.PositionX - directionX * push;
                float leftZ = left.PositionZ - directionZ * push;
                float rightX = right.PositionX + directionX * push;
                float rightZ = right.PositionZ + directionZ * push;
                ResolvePosition(ref leftX, ref leftZ, left.PositionX, left.PositionZ,
                    rule.EnemyContactRadius, rule, obstacles, IslandFor(battle));
                ResolvePosition(ref rightX, ref rightZ, right.PositionX, right.PositionZ,
                    rule.EnemyContactRadius, rule, obstacles, IslandFor(battle));
                left.PositionX = leftX; left.PositionZ = leftZ;
                right.PositionX = rightX; right.PositionZ = rightZ;
            }
        }

        private static bool InsideObstacle(float x, float z, float radius, IReadOnlyList<ArcadeObstacleConfig> obstacles)
        {
            foreach (ArcadeObstacleConfig obstacle in obstacles)
                if (MathF.Abs(x - obstacle.CenterX) <= obstacle.HalfWidth + radius &&
                    MathF.Abs(z - obstacle.CenterY) <= obstacle.HalfHeight + radius)
                    return true;
            return false;
        }

        private static void Normalize(ref float x, ref float z)
        {
            float lengthSquared = x * x + z * z;
            if (lengthSquared <= 1f) return;
            float inverseLength = 1f / MathF.Sqrt(lengthSquared);
            x *= inverseLength;
            z *= inverseLength;
        }

        private static void Rotate(float x, float z, float degrees, out float resultX, out float resultZ)
        {
            float radians = degrees * (MathF.PI / 180f);
            float cosine = MathF.Cos(radians);
            float sine = MathF.Sin(radians);
            resultX = x * cosine - z * sine;
            resultZ = x * sine + z * cosine;
        }

        private static bool Finite(float a, float b, float c, float d) =>
            !float.IsNaN(a) && !float.IsInfinity(a) && !float.IsNaN(b) && !float.IsInfinity(b) &&
            !float.IsNaN(c) && !float.IsInfinity(c) && !float.IsNaN(d) && !float.IsInfinity(d);

        private static ArcadeArenaConfig ResolveArena(int arenaNumber)
        {
            List<ArcadeArenaConfig> arenas = ServerConfig.Instance.Tables.TbArcadeArena.DataList;
            if (arenas.Count == 0) throw new InvalidOperationException("Arcade arena config is empty.");
            ArcadeArenaConfig exact = ServerConfig.Instance.Tables.TbArcadeArena.GetOrDefault(arenaNumber);
            return exact ?? arenas.OrderBy(x => x.ArenaNumber).ElementAt((arenaNumber - 1) % arenas.Count);
        }

        private static ArcadeEnemyGroupConfig ResolveBattleGroup(OnlineArcadeBattleRuntime battle,
            ArcadeArenaConfig arena, int waveIndex)
        {
            string groupId = battle.IsChallenge
                ? ServerConfig.Instance.Tables.TbArcadeChallenge.Get(battle.ChallengeId).EnemyGroupId
                : arena.WaveGroupIds[waveIndex];
            if (battle.IsFinalBoss)
            {
                ArcadeBossConfig boss = ServerConfig.Instance.Tables.TbArcadeBoss.Get(arena.FinalBossId);
                groupId = battle.FinalBossEnemyId == 0 ? boss.EnemyGroupId : boss.ReinforcementGroupId;
            }
            return ServerConfig.Instance.Tables.TbArcadeEnemyGroup.Get(groupId);
        }

        /// <summary>只有竞技场显式声明且与当前敌群匹配时才启用专用 Boss 行为。</summary>
        private static ArcadeBossConfig ResolveBoss(ArcadeArenaConfig arena, ArcadeEnemyGroupConfig group)
        {
            if (arena == null || group == null || string.IsNullOrWhiteSpace(arena.BossId)) return null;
            ArcadeBossConfig boss = ServerConfig.Instance.Tables.TbArcadeBoss.GetOrDefault(arena.BossId);
            return boss != null && string.Equals(boss.EnemyGroupId, group.Id, StringComparison.Ordinal) ? boss : null;
        }

        private static (float X, float Z) SpawnPosition(long runtimeId, ArcadeRuleConfig rule)
        {
            float lane = ((runtimeId * 37) % 101) / 50f - 1f;
            float inset = Math.Max(rule.EnemyContactRadius * 2f, 0.5f);
            return (runtimeId % 4) switch
            {
                0 => (-rule.ArenaHalfWidth + inset, lane * (rule.ArenaHalfHeight - inset)),
                1 => (rule.ArenaHalfWidth - inset, lane * (rule.ArenaHalfHeight - inset)),
                2 => (lane * (rule.ArenaHalfWidth - inset), -rule.ArenaHalfHeight + inset),
                _ => (lane * (rule.ArenaHalfWidth - inset), rule.ArenaHalfHeight - inset),
            };
        }

        private static float DistanceSquared(float ax, float az, float bx, float bz)
        {
            float x = ax - bx;
            float z = az - bz;
            return x * x + z * z;
        }

        /// <summary>计算点到移动线段的平方距离，避免权威服低帧推进时冲锋穿透玩家。</summary>
        private static float DistanceToSegmentSquared(float pointX, float pointZ, float startX, float startZ,
            float endX, float endZ)
        {
            float segmentX = endX - startX;
            float segmentZ = endZ - startZ;
            float lengthSquared = segmentX * segmentX + segmentZ * segmentZ;
            if (lengthSquared <= 0.000001f) return DistanceSquared(pointX, pointZ, startX, startZ);
            float progress = Math.Clamp(((pointX - startX) * segmentX + (pointZ - startZ) * segmentZ) /
                lengthSquared, 0f, 1f);
            return DistanceSquared(pointX, pointZ, startX + segmentX * progress, startZ + segmentZ * progress);
        }

        public static OnlineArcadeRunSnapshot BuildSnapshot(OnlineArcadeRunRecord record)
        {
            if (record == null) return null;
            OnlineArcadeRunSnapshot snapshot = OnlineArcadeRunSnapshot.Create();
            snapshot.RunId = record.RunId;
            snapshot.RoomId = record.RoomId;
            snapshot.Revision = record.Revision;
            snapshot.Phase = (int)record.Phase;
            snapshot.CompletedArena = record.CompletedArena;
            snapshot.NextArena = record.NextArena;
            snapshot.ConfigFingerprint = record.ConfigFingerprint ?? string.Empty;
            snapshot.ChallengeId = record.ChallengeId ?? string.Empty;
            snapshot.ChallengeAttempted = record.ChallengeAttempted;
            foreach (OnlineArcadePlayerRecord source in record.Players)
            {
                OnlineArcadePlayerInfo player = OnlineArcadePlayerInfo.Create();
                player.PlayerId = source.PlayerId;
                player.DisplayName = source.DisplayName ?? string.Empty;
                player.Lives = source.Lives;
                player.RunGold = source.RunGold;
                player.Score = source.Score; player.ScoreMultiplier = source.ScoreMultiplier; player.MultiplierProgress = source.MultiplierProgress;
                player.Bombs = source.Bombs;
                player.Dashes = source.Dashes;
                player.WeaponId = source.WeaponId ?? string.Empty;
                player.Ready = source.Ready;
                player.ChallengeReady = source.ChallengeReady;
                player.SettlementConfirmed = source.SettlementConfirmed;
                player.AbandonConfirmed = source.AbandonConfirmed;
                player.SettlementState = source.SettlementState;

                foreach (KeyValuePair<string, int> entry in source.UpgradeStacks.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    OnlineArcadeOwnedAmountInfo amount = OnlineArcadeOwnedAmountInfo.Create();
                    amount.Id = entry.Key;
                    amount.Amount = entry.Value;
                    player.UpgradeStacks.Add(amount);
                }
                foreach (KeyValuePair<string, int> entry in source.Consumables.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    OnlineArcadeOwnedAmountInfo amount = OnlineArcadeOwnedAmountInfo.Create();
                    amount.Id = entry.Key;
                    amount.Amount = entry.Value;
                    player.Consumables.Add(amount);
                }
                foreach (KeyValuePair<string, string> entry in source.EquipmentBySlot.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    OnlineArcadeEquipmentInfo equipment = OnlineArcadeEquipmentInfo.Create();
                    equipment.SlotId = entry.Key;
                    equipment.ProductId = entry.Value;
                    player.Equipment.Add(equipment);
                }

                snapshot.Players.Add(player);
            }
            return snapshot;
        }

        private static OnlineArcadeBattleInfo BuildBattleSnapshot(OnlineArcadeBattleRuntime source)
        {
            OnlineArcadeBattleInfo battle = OnlineArcadeBattleInfo.Create();
            battle.ArenaNumber = source.ArenaNumber;
            battle.RoundGeneration=source.RoundGeneration;
            battle.RoundBossEncounter=source.RoundBossEncounter;battle.RoundBossDefeated=source.RoundBossDefeated;
            battle.RoundLayoutId=source.RoundLayout?.Id ?? string.Empty;
            battle.RoundLayoutVersion=source.RoundLayout?.Version ?? 0;
            battle.RoundLoading=!RoundPlayersReady(source);battle.RoundLoadError=source.RoundLoadError;
            battle.ContentElapsed=(float)source.ContentClock.Elapsed;
            AppendGroundBarrelSnapshots(source,battle);
            foreach(var a in source.ContentAreas){var item=OnlineArcadeContentAreaInfo.Create();item.Id=a.Id;item.Kind=a.Kind;item.X=a.X;item.Y=a.Y;item.Radius=a.Radius;item.Warning=a.Warning;item.EndX=a.EndX;item.EndY=a.EndY;item.Segment=a.Segment;battle.ContentAreas.Add(item);}

            battle.ArenaConfigNumber = source.ArenaConfigNumber;
            battle.IsFinalBoss = source.IsFinalBoss;
            battle.AwaitingExit = source.AwaitingExit;
            battle.WaveNumber = source.WaveIndex + 1;
            battle.WaveCount = source.WaveCount;
            battle.SpawnedInWave = source.SpawnedInWave;
            battle.TotalInWave = source.TotalInWave;
            battle.DifficultyPlayerCount = source.DifficultyPlayerCount;
            battle.BattleRevision = source.BattleRevision;
            battle.ServerTime = TimeInfo.Instance.ServerNow();
            battle.CrateIds.AddRange(source.Crates.Keys.OrderBy(id => id));
            foreach (var receipt in source.SupplyPickups)
            {
                var copy = OnlineArcadeDropInfo.Create();
                copy.RuntimeId = receipt.RuntimeId; copy.DropConfigId = receipt.DropConfigId;
                copy.CollectedByPlayerId = receipt.CollectedByPlayerId; copy.AddedAmount = receipt.AddedAmount;
                copy.ScoreAdded = receipt.ScoreAdded; copy.GoldAdded = receipt.GoldAdded;
                battle.SupplyPickups.Add(copy);
            }
            battle.IsChallenge = source.IsChallenge;
            battle.ChallengeId = source.ChallengeId ?? string.Empty;
            battle.ChallengeSeconds = source.ChallengeSeconds;
            battle.HazardElapsedSeconds = (float)source.HazardElapsedSeconds;
            var room = source.TreasureRoom;
            battle.TreasureRoomId = room?.InstanceId ?? string.Empty;
            battle.TreasureDiscovered = source.TreasureDiscovered; battle.TreasureOpen = source.TreasureCleared && room?.Expired == false;
            battle.TreasureExpired = room?.Expired ?? false; battle.TreasureSeconds = (float)(room?.RemainingSeconds ?? 0);
            battle.TreasureRewardSeconds = (float)(room?.RewardRemainingSeconds ?? 0);
            if (room != null) battle.TreasureRewardIds.AddRange(room.RemainingRewards.Select(r => r.Id));
            foreach (OnlineArcadeBattlePlayerRuntime sourcePlayer in source.Players.Values.OrderBy(x => x.PlayerId))
            {
                OnlineArcadeBattlePlayerInfo player = OnlineArcadeBattlePlayerInfo.Create();
                player.PlayerId = sourcePlayer.PlayerId;
                player.PositionX = sourcePlayer.PositionX;
                player.PositionZ = sourcePlayer.PositionZ;
                player.AimX = sourcePlayer.AimX;
                player.AimZ = sourcePlayer.AimZ;
                player.CurrentHealth = sourcePlayer.CurrentHealth;
                player.MaxHealth = sourcePlayer.MaxHealth;
                player.Alive = sourcePlayer.Alive;
                player.Invulnerable = sourcePlayer.InvulnerabilitySeconds > 0f;
                player.RespawnSeconds = sourcePlayer.RespawnSeconds;
                player.TemporaryWeaponId = sourcePlayer.TemporaryWeaponId ?? string.Empty;
                player.FireSequence = sourcePlayer.FireSequence;
                player.InTreasureRoom = sourcePlayer.InTreasureRoom;
                player.TemporaryWeaponSeconds = sourcePlayer.TemporaryWeaponSeconds;
                player.SupportId = sourcePlayer.SupportId ?? string.Empty;
                player.DashSeconds = sourcePlayer.DashSeconds;
                foreach (OnlineArcadePowerupRuntime sourcePowerup in sourcePlayer.Powerups.Values
                             .OrderBy(x => x.Behavior))
                {
                    OnlineArcadePowerupInfo powerup = OnlineArcadePowerupInfo.Create();
                    powerup.ProductId = sourcePowerup.ProductId ?? string.Empty;
                    powerup.RemainingSeconds = sourcePowerup.RemainingSeconds;
                    powerup.Charges = sourcePowerup.Charges;
                    powerup.PositionX = sourcePowerup.PositionX;
                    powerup.PositionZ = sourcePowerup.PositionZ;
                    powerup.ShotDirectionX = sourcePowerup.ShotDirectionX;
                    powerup.ShotDirectionZ = sourcePowerup.ShotDirectionZ;
                    player.Powerups.Add(powerup);
                }
                battle.Players.Add(player);
            }
            foreach (OnlineArcadeEnemyRuntime sourceEnemy in source.Enemies.Values.OrderBy(x => x.RuntimeId))
            {
                OnlineArcadeEnemyInfo enemy = OnlineArcadeEnemyInfo.Create();
                enemy.RuntimeId = sourceEnemy.RuntimeId;
                enemy.EnemyConfigId = sourceEnemy.EnemyConfigId ?? string.Empty;
                enemy.PositionX = sourceEnemy.PositionX;
                enemy.PositionZ = sourceEnemy.PositionZ;
                enemy.CurrentHealth = sourceEnemy.CurrentHealth;
                enemy.MaxHealth = sourceEnemy.MaxHealth;
                enemy.TargetPlayerId = sourceEnemy.TargetPlayerId;
                enemy.WaveNumber = sourceEnemy.WaveNumber;
                enemy.BossId = sourceEnemy.BossId ?? string.Empty;
                enemy.BossPhase = (int)sourceEnemy.BossPhase;
                enemy.BossPhaseSeconds = sourceEnemy.BossPhaseSeconds;
                enemy.ArmorHealth = sourceEnemy.ArmorHealth;
                enemy.SpecialKind=sourceEnemy.SpecialKind;enemy.ContentBossKind=sourceEnemy.ContentBossKind;enemy.ContentWarning=sourceEnemy.ContentWarning;
                battle.Enemies.Add(enemy);
            }
            foreach (OnlineArcadeProjectileRuntime sourceProjectile in source.Projectiles.Values.OrderBy(x => x.RuntimeId))
            {
                OnlineArcadeProjectileInfo projectile = OnlineArcadeProjectileInfo.Create();
                projectile.RuntimeId = sourceProjectile.RuntimeId;
                projectile.OwnerPlayerId = sourceProjectile.OwnerPlayerId;
                projectile.PositionX = sourceProjectile.PositionX;
                projectile.PositionZ = sourceProjectile.PositionZ;
                projectile.VelocityX = sourceProjectile.VelocityX;
                projectile.VelocityZ = sourceProjectile.VelocityZ;
                battle.Projectiles.Add(projectile);
            }
            foreach (OnlineArcadeDropRuntime sourceDrop in source.Drops.Values.OrderBy(x => x.RuntimeId))
            {
                OnlineArcadeDropInfo drop = OnlineArcadeDropInfo.Create();
                drop.RuntimeId = sourceDrop.RuntimeId;
                drop.DropConfigId = sourceDrop.DropConfigId ?? string.Empty;
                drop.PositionX = sourceDrop.PositionX;
                drop.PositionZ = sourceDrop.PositionZ;
                drop.RemainingSeconds = sourceDrop.RemainingSeconds;
                battle.Drops.Add(drop);
            }
            return battle;
        }

        public static OnlineArcadeRunSnapshot CloneSnapshot(OnlineArcadeRunSnapshot source)
        {
            if (source == null) return null;
            OnlineArcadeRunSnapshot clone = OnlineArcadeRunSnapshot.Create();
            clone.RunId = source.RunId;
            clone.RoomId = source.RoomId;
            clone.Revision = source.Revision;
            clone.Phase = source.Phase;
            clone.CompletedArena = source.CompletedArena;
            clone.NextArena = source.NextArena;
            clone.ConfigFingerprint = source.ConfigFingerprint ?? string.Empty;
            clone.Battle = CloneBattleSnapshot(source.Battle);
            clone.BattlePaused = source.BattlePaused;
            foreach (OnlineArcadeConnectionInfo sourceConnection in source.Connections)
            {
                OnlineArcadeConnectionInfo connection = OnlineArcadeConnectionInfo.Create();
                connection.PlayerId = sourceConnection.PlayerId;
                connection.Connected = sourceConnection.Connected;
                connection.DisconnectedAtServerTime = sourceConnection.DisconnectedAtServerTime;
                connection.ArenaReconnectDeadlineServerTime = sourceConnection.ArenaReconnectDeadlineServerTime;
                connection.SoloEndAvailableAtServerTime = sourceConnection.SoloEndAvailableAtServerTime;
                clone.Connections.Add(connection);
            }
            foreach (OnlineArcadePlayerInfo sourcePlayer in source.Players)
            {
                OnlineArcadePlayerInfo player = OnlineArcadePlayerInfo.Create();
                player.PlayerId = sourcePlayer.PlayerId;
                player.DisplayName = sourcePlayer.DisplayName ?? string.Empty;
                player.Lives = sourcePlayer.Lives;
                player.RunGold = sourcePlayer.RunGold;
                player.Score = sourcePlayer.Score; player.ScoreMultiplier = sourcePlayer.ScoreMultiplier; player.MultiplierProgress = sourcePlayer.MultiplierProgress;
                player.Bombs = sourcePlayer.Bombs;
                player.Dashes = sourcePlayer.Dashes;
                player.WeaponId = sourcePlayer.WeaponId ?? string.Empty;
                player.Ready = sourcePlayer.Ready;
                player.SettlementConfirmed = sourcePlayer.SettlementConfirmed;
                player.AbandonConfirmed = sourcePlayer.AbandonConfirmed;
                player.SettlementState = sourcePlayer.SettlementState;

                foreach (OnlineArcadeOwnedAmountInfo sourceAmount in sourcePlayer.UpgradeStacks)
                {
                    OnlineArcadeOwnedAmountInfo amount = OnlineArcadeOwnedAmountInfo.Create();
                    amount.Id = sourceAmount.Id ?? string.Empty;
                    amount.Amount = sourceAmount.Amount;
                    player.UpgradeStacks.Add(amount);
                }
                foreach (OnlineArcadeOwnedAmountInfo sourceAmount in sourcePlayer.Consumables)
                {
                    OnlineArcadeOwnedAmountInfo amount = OnlineArcadeOwnedAmountInfo.Create();
                    amount.Id = sourceAmount.Id ?? string.Empty;
                    amount.Amount = sourceAmount.Amount;
                    player.Consumables.Add(amount);
                }
                foreach (OnlineArcadeEquipmentInfo sourceEquipment in sourcePlayer.Equipment)
                {
                    OnlineArcadeEquipmentInfo equipment = OnlineArcadeEquipmentInfo.Create();
                    equipment.SlotId = sourceEquipment.SlotId ?? string.Empty;
                    equipment.ProductId = sourceEquipment.ProductId ?? string.Empty;
                    player.Equipment.Add(equipment);
                }

                clone.Players.Add(player);
            }
            return clone;
        }

        private static OnlineArcadeBattleInfo CloneBattleSnapshot(OnlineArcadeBattleInfo source)
        {
            if (source == null) return null;
            OnlineArcadeBattleInfo battle = OnlineArcadeBattleInfo.Create();
            battle.ArenaNumber = source.ArenaNumber;
            battle.RoundGeneration=source.RoundGeneration;
            battle.RoundBossEncounter=source.RoundBossEncounter;battle.RoundBossDefeated=source.RoundBossDefeated;battle.RoundLayoutId=source.RoundLayoutId;battle.RoundLayoutVersion=source.RoundLayoutVersion;
            battle.RoundLoading=source.RoundLoading;battle.RoundLoadError=source.RoundLoadError;battle.ContentElapsed=source.ContentElapsed;
            foreach(var a in source.ContentAreas){var item=OnlineArcadeContentAreaInfo.Create();item.Id=a.Id;item.Kind=a.Kind;item.X=a.X;item.Y=a.Y;item.Radius=a.Radius;item.Warning=a.Warning;item.EndX=a.EndX;item.EndY=a.EndY;item.Segment=a.Segment;battle.ContentAreas.Add(item);}

            battle.WaveNumber = source.WaveNumber;
            battle.ArenaConfigNumber = source.ArenaConfigNumber;
            battle.IsFinalBoss = source.IsFinalBoss;
            battle.AwaitingExit = source.AwaitingExit;
            battle.IsChallenge = source.IsChallenge;
            battle.ChallengeId = source.ChallengeId;
            battle.ChallengeSeconds = source.ChallengeSeconds;
            battle.WaveCount = source.WaveCount;
            battle.SpawnedInWave = source.SpawnedInWave;
            battle.TotalInWave = source.TotalInWave;
            battle.DifficultyPlayerCount = source.DifficultyPlayerCount;
            battle.BattleRevision = source.BattleRevision;
            battle.ServerTime = source.ServerTime;
            battle.CrateIds.AddRange(source.CrateIds);
            foreach (var receipt in source.SupplyPickups)
            {
                var copy = OnlineArcadeDropInfo.Create();
                copy.RuntimeId = receipt.RuntimeId; copy.DropConfigId = receipt.DropConfigId;
                copy.CollectedByPlayerId = receipt.CollectedByPlayerId; copy.AddedAmount = receipt.AddedAmount;
                copy.ScoreAdded = receipt.ScoreAdded; copy.GoldAdded = receipt.GoldAdded;
                battle.SupplyPickups.Add(copy);
            }
            battle.HazardElapsedSeconds = source.HazardElapsedSeconds;
            battle.TreasureRoomId = source.TreasureRoomId; battle.TreasureDiscovered = source.TreasureDiscovered;
            battle.TreasureOpen = source.TreasureOpen; battle.TreasureExpired = source.TreasureExpired;
            battle.TreasureSeconds = source.TreasureSeconds; battle.TreasureRewardIds.AddRange(source.TreasureRewardIds);
            battle.TreasureRewardSeconds = source.TreasureRewardSeconds;
            foreach (OnlineArcadeBattlePlayerInfo sourcePlayer in source.Players)
            {
                OnlineArcadeBattlePlayerInfo player = OnlineArcadeBattlePlayerInfo.Create();
                player.PlayerId = sourcePlayer.PlayerId;
                player.PositionX = sourcePlayer.PositionX;
                player.PositionZ = sourcePlayer.PositionZ;
                player.AimX = sourcePlayer.AimX;
                player.AimZ = sourcePlayer.AimZ;
                player.CurrentHealth = sourcePlayer.CurrentHealth;
                player.MaxHealth = sourcePlayer.MaxHealth;
                player.Alive = sourcePlayer.Alive;
                player.Invulnerable = sourcePlayer.Invulnerable;
                player.RespawnSeconds = sourcePlayer.RespawnSeconds;
                player.TemporaryWeaponId = sourcePlayer.TemporaryWeaponId ?? string.Empty;
                player.FireSequence = sourcePlayer.FireSequence;
                player.InTreasureRoom = sourcePlayer.InTreasureRoom;
                player.TemporaryWeaponSeconds = sourcePlayer.TemporaryWeaponSeconds;
                player.SupportId = sourcePlayer.SupportId ?? string.Empty;
                player.SupportSeconds = sourcePlayer.SupportSeconds;
                player.DashSeconds = sourcePlayer.DashSeconds;
                foreach (OnlineArcadePowerupInfo sourcePowerup in sourcePlayer.Powerups)
                {
                    OnlineArcadePowerupInfo powerup = OnlineArcadePowerupInfo.Create();
                    powerup.ProductId = sourcePowerup.ProductId ?? string.Empty;
                    powerup.RemainingSeconds = sourcePowerup.RemainingSeconds;
                    powerup.Charges = sourcePowerup.Charges;
                    powerup.PositionX = sourcePowerup.PositionX;
                    powerup.PositionZ = sourcePowerup.PositionZ;
                    powerup.ShotDirectionX = sourcePowerup.ShotDirectionX;
                    powerup.ShotDirectionZ = sourcePowerup.ShotDirectionZ;
                    player.Powerups.Add(powerup);
                }
                battle.Players.Add(player);
            }
            foreach (OnlineArcadeEnemyInfo sourceEnemy in source.Enemies)
            {
                OnlineArcadeEnemyInfo enemy = OnlineArcadeEnemyInfo.Create();
                enemy.RuntimeId = sourceEnemy.RuntimeId;
                enemy.EnemyConfigId = sourceEnemy.EnemyConfigId ?? string.Empty;
                enemy.PositionX = sourceEnemy.PositionX;
                enemy.PositionZ = sourceEnemy.PositionZ;
                enemy.CurrentHealth = sourceEnemy.CurrentHealth;
                enemy.MaxHealth = sourceEnemy.MaxHealth;
                enemy.TargetPlayerId = sourceEnemy.TargetPlayerId;
                enemy.WaveNumber = sourceEnemy.WaveNumber;
                enemy.BossId = sourceEnemy.BossId ?? string.Empty;
                enemy.BossPhase = sourceEnemy.BossPhase;
                enemy.BossPhaseSeconds = sourceEnemy.BossPhaseSeconds;
                enemy.ArmorHealth = sourceEnemy.ArmorHealth;
                enemy.SpecialKind=sourceEnemy.SpecialKind;enemy.ContentBossKind=sourceEnemy.ContentBossKind;enemy.ContentWarning=sourceEnemy.ContentWarning;
                battle.Enemies.Add(enemy);
            }
            foreach (OnlineArcadeProjectileInfo sourceProjectile in source.Projectiles)
            {
                OnlineArcadeProjectileInfo projectile = OnlineArcadeProjectileInfo.Create();
                projectile.RuntimeId = sourceProjectile.RuntimeId;
                projectile.OwnerPlayerId = sourceProjectile.OwnerPlayerId;
                projectile.PositionX = sourceProjectile.PositionX;
                projectile.PositionZ = sourceProjectile.PositionZ;
                projectile.VelocityX = sourceProjectile.VelocityX;
                projectile.VelocityZ = sourceProjectile.VelocityZ;
                battle.Projectiles.Add(projectile);
            }
            foreach (OnlineArcadeDropInfo sourceDrop in source.Drops)
            {
                OnlineArcadeDropInfo drop = OnlineArcadeDropInfo.Create();
                drop.RuntimeId = sourceDrop.RuntimeId;
                drop.DropConfigId = sourceDrop.DropConfigId ?? string.Empty;
                drop.PositionX = sourceDrop.PositionX;
                drop.PositionZ = sourceDrop.PositionZ;
                drop.RemainingSeconds = sourceDrop.RemainingSeconds;
                battle.Drops.Add(drop);
            }
            return battle;
        }
    }
}

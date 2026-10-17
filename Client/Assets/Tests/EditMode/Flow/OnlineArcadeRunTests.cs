using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ET;
using Game.Network;
using Luban;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class OnlineArcadeRunTests
    {
        [TearDown]
        public void TearDown() => ResetArchitecture();

        [Test]
        public void ServerCreationBroadcastsRunSnapshotAfterRoomLock()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string source = File.ReadAllText(Path.Combine(repositoryRoot, "Server", "GameServer", "Runtime",
                "Hotfix", "Server", "Online", "Arcade", "OnlineArcadeRunSystem.cs"));
            int createStart = source.IndexOf("public static async ETTask<OnlineArcadeRunRecord> CreateForRoom",
                StringComparison.Ordinal);
            int createEnd = source.IndexOf("public static OnlineArcadeRunActor Get", createStart,
                StringComparison.Ordinal);

            Assert.That(createStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(createEnd, Is.GreaterThan(createStart));
            string createMethod = source.Substring(createStart, createEnd - createStart);
            Assert.That(createMethod, Does.Contain("await self.BroadcastRunChanged(existingActor);"),
                "重复开局必须补发当前运行局，避免首次推送丢失后队友永久停留在房间。");
            Assert.That(createMethod, Does.Contain("await room.LockForArcadeRun();"));
            Assert.That(createMethod, Does.Contain("await self.BroadcastRunChanged(self.Get(runId));"));
            Assert.That(createMethod.IndexOf("await self.BroadcastRunChanged(self.Get(runId));", StringComparison.Ordinal),
                Is.GreaterThan(createMethod.IndexOf("await room.LockForArcadeRun();", StringComparison.Ordinal)),
                "只有安全节点和房间锁提交完成后，才能向双方发布可进入的街机运行局。");
        }

        [Test]
        public void ServerArcadeTestCommandIsOwnerAuthorizedAndBroadcastByAuthority()
        {
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string commandSource = File.ReadAllText(Path.Combine(repositoryRoot, "Server", "GameServer", "Runtime",
                "Hotfix", "Server", "Online", "TestCmd", "ServerArcadeTestCmdHandler.cs"));
            string runSource = File.ReadAllText(Path.Combine(repositoryRoot, "Server", "GameServer", "Runtime",
                "Hotfix", "Server", "Online", "Arcade", "OnlineArcadeRunSystem.cs"));
            int executeStart = runSource.IndexOf(
                "public static async ETTask<ServerTestCmdRunResult> ExecuteTestCommand", StringComparison.Ordinal);
            int executeEnd = runSource.IndexOf("private static bool TryParsePositive", executeStart,
                StringComparison.Ordinal);

            Assert.That(commandSource, Does.Contain("proxy == null || !proxy.IsAuthenticated"),
                "网关入口必须拒绝匿名 Session。");
            Assert.That(commandSource, Does.Contain("G2R_ExecuteOnlineArcadeTestCmd"),
                "网关只能把测试意图转发到 OnlineRoom 权威场景。");
            Assert.That(executeStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(executeEnd, Is.GreaterThan(executeStart));
            string executeMethod = runSource.Substring(executeStart, executeEnd - executeStart);
            Assert.That(executeMethod, Does.Contain("room.OwnerPlayerId != playerId"),
                "普通成员不得执行联机街机测试指令。");
            Assert.That(executeMethod, Does.Contain("actor.DisconnectedAtByPlayer.Count > 0"),
                "队友断线暂停期间不得修改战斗现场。");
            Assert.That(executeMethod, Does.Contain("await self.BroadcastRunChanged(actor);"),
                "服务器修改成功后必须把同一权威快照广播给双方。");
            Assert.That(runSource, Does.Contain("self.TryAdvanceWave(battle, 0f);"),
                "清场指令必须沿用正式波次推进与完成事务。");
        }

        [Test]
        public void SnapshotCopiesCallerOwnedCollections()
        {
            var players = new List<OnlineArcadePlayerSnapshot>
            {
                new(1, "one", 3, 0, 1, 2, "weapon", false)
            };
            var snapshot = new OnlineArcadeRunClientSnapshot(true, 10, 20, 1,
                OnlineArcadeRunPhase.SafeCheckpoint, 0, 1, "fingerprint", players,
                OnlineArcadeOperationState.Idle, OnlineArcadeResultCode.Success);
            players.Clear();

            Assert.That(snapshot.Players.Count, Is.EqualTo(1));
            Assert.That(typeof(OnlineArcadePlayerSnapshot).GetProperty("ShopOffers"), Is.Null);
            Assert.That(((IList<OnlineArcadePlayerSnapshot>)snapshot.Players).IsReadOnly, Is.True);
        }

        [Test]
        public void SafeCheckpointApisCarryCurrentRevisionAndExplicitIntent()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();
            Assert.That(((C2G_StartOnlineArcadeRun)harness.Network.LastRequest).ProtocolFingerprint,Is.EqualTo(harness.Network.ConfigFingerprint+"|slayermart.v2"));

            harness.System.SetReadyAsync(true).GetAwaiter().GetResult();
            var ready = (C2G_OperateOnlineArcadeSafeCheckpoint)harness.Network.LastRequest;
            Assert.That(ready.ProtocolFingerprint,Is.EqualTo(harness.Network.ConfigFingerprint+"|slayermart.v2"));
            Assert.That(ready.ExpectedRevision, Is.EqualTo(1));
            Assert.That(ready.ActionKind, Is.EqualTo(3));
            Assert.That(ready.Ready, Is.True);
            Assert.That(harness.System.Snapshot.Revision, Is.EqualTo(2));

            harness.System.SetChallengeReadyAsync(true).GetAwaiter().GetResult();
            var challenge = (C2G_OperateOnlineArcadeSafeCheckpoint)harness.Network.LastRequest;
            Assert.That(challenge.ExpectedRevision, Is.EqualTo(2));
            Assert.That(challenge.ActionKind, Is.EqualTo(7));
            Assert.That(challenge.Ready, Is.True);
            Assert.That(harness.System.Snapshot.Revision, Is.EqualTo(3));
        }

        [Test]
        public void ServerPushReplacesSnapshotAndIgnoresOlderRevision()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();

            harness.Network.PushRun(3);
            Assert.That(harness.System.Snapshot.Revision, Is.EqualTo(3));
            harness.Network.PushRun(2);
            Assert.That(harness.System.Snapshot.Revision, Is.EqualTo(3));
        }

        [Test]
        public void RunEndedPushClearsClientSnapshot()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();

            harness.Network.PushRunEnded();

            Assert.That(harness.System.Snapshot.HasRun, Is.False);
            Assert.That(harness.System.Snapshot.LastResultCode, Is.EqualTo(OnlineArcadeResultCode.Success));
            // 无场景展示端口时仍保留结果，重复结束推送或新局请求都不能覆盖未确认回执。
            var field = typeof(OnlineArcadeRunSystem).GetField("pendingResult", BindingFlags.Instance | BindingFlags.NonPublic);
            var receipt = (OnlineArcadeResultData)field.GetValue(harness.System);
            Assert.That(receipt.RunId, Is.EqualTo(100));
            Assert.That(receipt.Scores.Keys, Is.EquivalentTo(new[] { "10", "20" }));
            var request = harness.Network.LastRequest;
            harness.Network.PushRunEnded();
            var blocked = harness.System.StartAsync().GetAwaiter().GetResult();
            Assert.That(blocked.Code, Is.EqualTo(OnlineArcadeResultCode.InvalidState));
            Assert.That(field.GetValue(harness.System), Is.SameAs(receipt));
            Assert.That(harness.Network.LastRequest, Is.SameAs(request));
        }

        [Test]
        public void BattlePushCopiesAuthoritativeSceneAndRejectsOlderBattleRevision()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();

            harness.Network.PushBattle(5, 1);
            Assert.That(harness.System.Snapshot.Phase, Is.EqualTo(OnlineArcadeRunPhase.Arena));
            Assert.That(harness.System.Snapshot.Battle.BattleRevision, Is.EqualTo(5));
            Assert.That(harness.System.Snapshot.Battle.DifficultyPlayerCount, Is.EqualTo(2));
            Assert.That(harness.System.Snapshot.Battle.Enemies.Count, Is.EqualTo(1));
            Assert.That(harness.System.Snapshot.Battle.Enemies[0].BossId, Is.EqualTo("arcade.boss.brute-charge"));
            Assert.That(harness.System.Snapshot.Battle.Enemies[0].BossPhase, Is.EqualTo(ArcadeBossPhase.Telegraph));
            Assert.That(harness.System.Snapshot.Battle.Enemies[0].BossPhaseSeconds, Is.EqualTo(.9f));
            Assert.That(harness.System.Snapshot.Battle.Projectiles.Count, Is.EqualTo(1));
            Assert.That(harness.System.Snapshot.Battle.Projectiles[0].OwnerPlayerId, Is.EqualTo(10));
            Assert.That(harness.System.Snapshot.Battle.Drops.Count, Is.EqualTo(1));
            Assert.That(harness.System.Snapshot.Battle.Drops[0].DropConfigId, Is.EqualTo("arcade.drop.gold.small"));
            Assert.That(harness.System.Snapshot.Battle.Drops[0].RemainingSeconds, Is.EqualTo(1.5f));
            Assert.That(harness.System.Snapshot.Battle.TreasureRewardSeconds, Is.EqualTo(2f));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Invulnerable, Is.True);
            Assert.That(harness.System.Snapshot.Battle.Players[0].RespawnSeconds, Is.EqualTo(.75f));
            Assert.That(harness.System.Snapshot.Battle.Players[0].TemporaryWeaponId, Is.EqualTo("arcade.weapon.rapid"));
            Assert.That(harness.System.Snapshot.Battle.Players[0].SupportId, Is.EqualTo("arcade.support.drone"));
            Assert.That(harness.System.Snapshot.Battle.Players[0].SupportSeconds, Is.EqualTo(8f));
            Assert.That(harness.System.Snapshot.Battle.Players[0].DashSeconds, Is.EqualTo(.2f));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Powerups, Has.Count.EqualTo(1));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Powerups[0].ProductId,
                Is.EqualTo("arcade.powerup.shield"));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Powerups[0].Charges, Is.EqualTo(1));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Powerups[0].PositionX, Is.EqualTo(4.5f));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Powerups[0].PositionZ, Is.EqualTo(-2.5f));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Powerups[0].ShotDirectionX, Is.EqualTo(0.6f));
            Assert.That(harness.System.Snapshot.Battle.Players[0].Powerups[0].ShotDirectionZ, Is.EqualTo(-0.8f));

            harness.Network.PushBattle(4, 2);
            Assert.That(harness.System.Snapshot.Battle.BattleRevision, Is.EqualTo(5));
            Assert.That(harness.System.Snapshot.Battle.Enemies.Count, Is.EqualTo(1));
        }

        [Test]
        public void BattlePushCopiesRouteIdentityAndFinalStages()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();
            harness.Network.PushBattle(5, 1, arenaConfig: 20, finalBoss: true);
            Assert.That(harness.System.Snapshot.Battle.ArenaNumber, Is.EqualTo(1));
            Assert.That(harness.System.Snapshot.Battle.ArenaConfigNumber, Is.EqualTo(20));
            Assert.That(harness.System.Snapshot.Battle.IsFinalBoss, Is.True);
            Assert.That(harness.System.Snapshot.Battle.AwaitingExit, Is.False);
            harness.Network.PushBattle(6, 0, arenaConfig: 20, finalBoss: true, awaitingExit: true);
            harness.Network.PushBattle(4, 1);
            Assert.That(harness.System.Snapshot.Battle.AwaitingExit, Is.True);
            Assert.That(harness.System.Snapshot.Battle.IsFinalBoss, Is.True);
            Assert.That(harness.System.Snapshot.Battle.ArenaConfigNumber, Is.EqualTo(20));
        }

        [Test]
        public void PausedBattleCopiesReconnectAndSoloEndDeadlines()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();

            harness.Network.PushBattle(5, 1, true);

            Assert.That(harness.System.Snapshot.BattlePaused, Is.True);
            Assert.That(harness.System.Snapshot.Connections.Count, Is.EqualTo(2));
            Assert.That(harness.System.Snapshot.Connections[1].Connected, Is.False);
            Assert.That(harness.System.Snapshot.Connections[1].ArenaReconnectDeadlineServerTime, Is.EqualTo(70_000));
            Assert.That(harness.System.Snapshot.Connections[1].SoloEndAvailableAtServerTime, Is.EqualTo(610_000));
        }

        [Test]
        public void BattleInputSendsOnlyIntentWithMonotonicSequence()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();
            harness.Network.PushBattle(5, 1);

            OnlineArcadeInputResult first = harness.System.SubmitInputAsync(.5f, -.25f, 1f, 0f, true, true, true)
                .GetAwaiter().GetResult();
            var firstRequest = (C2G_SubmitOnlineArcadeInput)harness.Network.LastRequest;
            Assert.That(first.Code, Is.EqualTo(OnlineArcadeResultCode.Success));
            Assert.That(first.Accepted, Is.True);
            Assert.That(firstRequest.ArenaNumber, Is.EqualTo(1));
            Assert.That(firstRequest.InputSequence, Is.EqualTo(1));
            Assert.That(firstRequest.ProtocolFingerprint,Is.EqualTo(harness.Network.ConfigFingerprint+"|slayermart.v2"));
            Assert.That(firstRequest.MoveX, Is.EqualTo(.5f));
            Assert.That(firstRequest.MoveZ, Is.EqualTo(-.25f));
            Assert.That(firstRequest.AimX, Is.EqualTo(1f));
            Assert.That(firstRequest.AimZ, Is.EqualTo(0f));
            Assert.That(firstRequest.Firing, Is.True);
            Assert.That(firstRequest.UseBomb, Is.True);
            Assert.That(firstRequest.UseDash, Is.True);

            OnlineArcadeInputResult second = harness.System.SubmitInputAsync(0f, 0f, 0f, 1f, false)
                .GetAwaiter().GetResult();
            var secondRequest = (C2G_SubmitOnlineArcadeInput)harness.Network.LastRequest;
            Assert.That(second.Accepted, Is.True);
            Assert.That(secondRequest.InputSequence, Is.EqualTo(2));
            Assert.That(secondRequest.UseBomb, Is.False);
            Assert.That(secondRequest.UseDash, Is.False);
        }

        [Test]
        public void InputControllerConsumesOnlyAcknowledgedPulseCount()
        {
            using var input = new OnlineArcadeInputController();
            input.QueueBomb();
            input.QueueBomb();
            input.QueueDash();

            input.Acknowledge(true, true);

            Assert.That(input.QueuedBombs, Is.EqualTo(1));
            Assert.That(input.QueuedDashes, Is.Zero);
            input.Acknowledge(true, true);
            Assert.That(input.QueuedBombs, Is.Zero);
            Assert.That(input.QueuedDashes, Is.Zero);
        }

        [Test]
        public void LocalPredictionClampsPlayerRadiusInsideArena()
        {
            Vector2 resolved = OnlineArcadePresentationSystem.PredictLocalPosition(
                new Vector2(19f, 0f), Vector2.right, 8f, .1f, .35f, 19.5f, 19.5f,
                Array.Empty<ArcadeObstacleDefinition>());

            Assert.That(resolved, Is.EqualTo(new Vector2(19.15f, 0f)));
        }

        [Test]
        public void LocalPredictionMirrorsServerAxisObstacleRollback()
        {
            var obstacles = new[] { new ArcadeObstacleDefinition(2f, 2f, .5f, .5f) };

            Vector2 resolved = OnlineArcadePresentationSystem.PredictLocalPosition(
                new Vector2(1f, 2f), Vector2.one, 2f, .5f, .25f, 10f, 10f, obstacles);

            Assert.That(resolved.x, Is.EqualTo(1f).Within(.001f));
            Assert.That(resolved.y, Is.EqualTo(2f + Mathf.Sqrt(.5f)).Within(.001f),
                "X 轴候选撞障碍时应回退，Z 轴仍可沿障碍边缘移动");
        }

        [Test]
        public void LocalPredictionAppliesMoveUpgradeAndDashSpeed()
        {
            var upgrades = new[]
            {
                new ArcadeUpgradeDefinition("move", global::ArcadeUpgradeKind.MoveSpeedPercent, .1f, 5),
                new ArcadeUpgradeDefinition("damage", global::ArcadeUpgradeKind.DamagePercent, .5f, 5)
            };
            var stacks = new Dictionary<string, int> { ["move"] = 2, ["damage"] = 4 };

            float speed = OnlineArcadePresentationSystem.CalculatePredictedMoveSpeed(5f, true, upgrades, stacks);

            Assert.That(speed, Is.EqualTo(14.4f).Within(.001f));
        }

        [Test]
        public void ServerErrorsMapToStableResults()
        {
            Assert.That(OnlineArcadeRunSystem.MapServerError(200134), Is.EqualTo(OnlineArcadeResultCode.VersionMismatch));
            Assert.That(OnlineArcadeRunSystem.MapServerError(200135), Is.EqualTo(OnlineArcadeResultCode.InvalidState));
            Assert.That(OnlineArcadeRunSystem.MapServerError(200136), Is.EqualTo(OnlineArcadeResultCode.SaveFailed));
            Assert.That(OnlineArcadeRunSystem.MapServerError(200137), Is.EqualTo(OnlineArcadeResultCode.OperationConflict));
        }

        [Test]
        public void ResolutionApisSubmitOnlySettlementAndAbandonIntent()
        {
            Harness harness = CreateHarness();
            harness.System.StartAsync().GetAwaiter().GetResult();

            harness.System.ConfirmSettlementAsync().GetAwaiter().GetResult();
            Assert.That(((C2G_OperateOnlineArcadeSafeCheckpoint)harness.Network.LastRequest).ActionKind, Is.EqualTo(4));
            harness.System.ConfirmAbandonAsync().GetAwaiter().GetResult();
            Assert.That(((C2G_OperateOnlineArcadeSafeCheckpoint)harness.Network.LastRequest).ActionKind, Is.EqualTo(5));
        }

        [Test]
        public void StartPublishesIndependentPlayerCheckpointSnapshots()
        {
            Harness harness = CreateHarness();
            OnlineArcadeOperationResult result = harness.System.StartAsync().GetAwaiter().GetResult();

            Assert.That(harness.Network.LastRequest, Is.TypeOf<C2G_StartOnlineArcadeRun>());
            Assert.That(((C2G_StartOnlineArcadeRun)harness.Network.LastRequest).OperationId, Has.Length.EqualTo(32));
            Assert.That(result.Code, Is.EqualTo(OnlineArcadeResultCode.Success));
            Assert.That(result.Snapshot.HasRun, Is.True);
            Assert.That(result.Snapshot.Players.Count, Is.EqualTo(2));
            Assert.That(result.Snapshot.Players[0].PlayerId, Is.Not.EqualTo(result.Snapshot.Players[1].PlayerId));
            Assert.That(result.Snapshot.Players[0].SettlementConfirmed, Is.True);
            Assert.That(result.Snapshot.Players[0].SettlementState, Is.EqualTo(1));
        }

        [Test]
        public void RestoreWithoutRunPublishesEmptySuccess()
        {
            Harness harness = CreateHarness();
            harness.Network.ReturnNoRun = true;
            OnlineArcadeOperationResult result = harness.System.RestoreAsync().GetAwaiter().GetResult();

            Assert.That(harness.Network.LastRequest, Is.TypeOf<C2G_GetCurrentOnlineArcadeRun>());
            Assert.That(result.Code, Is.EqualTo(OnlineArcadeResultCode.Success));
            Assert.That(result.Snapshot.HasRun, Is.False);
            Assert.That(result.Snapshot.LastResultCode, Is.EqualTo(OnlineArcadeResultCode.Success));
        }

        internal static Harness CreateHarness()
        {
            ResetArchitecture();
            TestArchitecture.Network = new FakeNetwork();
            IArchitecture architecture = TestArchitecture.Interface;
            Assert.That(architecture.GetUtility<IRuntimeConfigProvider>()
                .TryGetArcadeCatalog(out ArcadeConfigCatalogDefinition catalog, out string configError),
                Is.True, configError);
            TestArchitecture.Network.ConfigFingerprint = catalog.Rule.ConfigFingerprint;
            return new Harness(architecture.GetSystem<OnlineArcadeRunSystem>(), TestArchitecture.Network);
        }

        internal static void ResetArchitecture()
        {
            FieldInfo field = typeof(Architecture<TestArchitecture>).GetField("mArchitecture",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IArchitecture architecture) architecture.Deinit();
            field?.SetValue(null, null);
            TestArchitecture.Network = null;
        }

        private sealed class TestArchitecture : Architecture<TestArchitecture>
        {
            public static FakeNetwork Network;
            protected override void Init()
            {
                RegisterModel(new OnlineAccountModel());
                RegisterModel(new OnlineArcadeRunModel());
                // 本夹具验证已登录后的 RPC 与快照；先设置身份，避免触发登录自动恢复及读取真实结果文件。
                GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(OnlineConnectionState.Authenticated,
                    OnlineOperationState.Idle, OnlineAccountResultCode.Success, "one", 1, 10, 1, false, "one"));
                RegisterUtility<INetworkRuntime>(Network);
                RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() =>
                    new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine(
                        Application.dataPath, "Game", "Config", $"{name}.bytes"))))));
                RegisterSystem(new OnlineArcadeRunSystem());
            }
        }

        internal readonly struct Harness
        {
            public Harness(OnlineArcadeRunSystem system, FakeNetwork network) { System = system; Network = network; }
            public OnlineArcadeRunSystem System { get; }
            public FakeNetwork Network { get; }
        }

        internal sealed class FakeNetwork : INetworkRuntime
        {
            public bool IsInitialized => true;
            public NetworkConnectionState ConnectionState => NetworkConnectionState.Connected;
            public int ConnectionGeneration => 1;
            public IRequest LastRequest { get; private set; }
            public bool ReturnNoRun { get; set; }
            public string ConfigFingerprint { get; set; }
            private Action<G2C_OnlineArcadeRunChanged> arcadeRunChanged;
            public Func<IRequest, IResponse> OverrideResponse;
            public Action<ET.OnlineArcadeRunSnapshot> ConfigureRun;
            public void PushConfigured() => arcadeRunChanged?.Invoke(new G2C_OnlineArcadeRunChanged { HasRun=true, Run=ProtocolRun() });
            private long revision = 1;
            private OnlineArcadeBattleInfo currentBattle;
            public void Initialize() { }
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                LastRequest = request;
                IResponse response;
                if(OverrideResponse!=null) return Task.FromResult((TResponse)OverrideResponse(request));
                if (request is C2G_StartOnlineArcadeRun)
                    response = new G2C_StartOnlineArcadeRun { Error = 0, HasRun = true, Run = ProtocolRun() };
                else if (request is C2G_OperateOnlineArcadeSafeCheckpoint operate)
                {
                    revision = operate.ExpectedRevision + 1;
                    response = new G2C_OperateOnlineArcadeSafeCheckpoint { Error = 0, Accepted = true, Run = ProtocolRun() };
                }
                else if (request is C2G_SubmitOnlineArcadeInput input)
                    response = new G2C_SubmitOnlineArcadeInput
                    {
                        Error = 0,
                        Accepted = true,
                        LastAcceptedInputSequence = input.InputSequence,
                        Run = ProtocolRun(),
                    };
                else
                    response = new G2C_GetCurrentOnlineArcadeRun { Error = 0, HasRun = !ReturnNoRun, Run = ReturnNoRun ? null : ProtocolRun() };
                return Task.FromResult((TResponse)response);
            }
            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler) where TMessage : class, IMessage
            {
                if (typeof(TMessage) == typeof(G2C_OnlineArcadeRunChanged))
                    arcadeRunChanged = message => handler((TMessage)(object)message);
                return new Subscription();
            }
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) => new Subscription();
            public void Disconnect(NetworkDisconnectReason reason) { }
            public void Dispose() { }

            public void PushRun(long pushedRevision)
            {
                long previous = revision;
                revision = pushedRevision;
                arcadeRunChanged?.Invoke(new G2C_OnlineArcadeRunChanged { HasRun = true, Run = ProtocolRun() });
                revision = Math.Max(previous, pushedRevision);
            }

            public void PushBattle(long battleRevision, int enemyCount, bool paused = false,
                int arenaConfig = 0, bool finalBoss = false, bool awaitingExit = false)
            {
                ET.OnlineArcadeRunSnapshot run = ProtocolRun();
                run.Phase = 2;
                OnlineArcadeBattleInfo battle = OnlineArcadeBattleInfo.Create();
                battle.ArenaConfigNumber = arenaConfig;
                battle.IsFinalBoss = finalBoss;
                battle.AwaitingExit = awaitingExit;
                battle.ArenaNumber = 1; battle.WaveNumber = 1; battle.WaveCount = 4;
                battle.TotalInWave = 12; battle.DifficultyPlayerCount = 2; battle.BattleRevision = battleRevision;
                for (int index = 0; index < 2; index++)
                {
                    OnlineArcadeBattlePlayerInfo player = OnlineArcadeBattlePlayerInfo.Create();
                    player.PlayerId = index == 0 ? 10 : 20; player.MaxHealth = 100; player.CurrentHealth = 100; player.Alive = true;
                    player.Invulnerable = index == 0; player.RespawnSeconds = index == 0 ? .75f : 0f;
                    player.TemporaryWeaponId = index == 0 ? "arcade.weapon.rapid" : string.Empty;
                    player.TemporaryWeaponSeconds = index == 0 ? 12f : 0f;
                    player.SupportId = index == 0 ? "arcade.support.drone" : string.Empty;
                    player.SupportSeconds = index == 0 ? 8f : 0f;
                    player.DashSeconds = index == 0 ? .2f : 0f;
                    if (index == 0)
                    {
                        OnlineArcadePowerupInfo powerup = OnlineArcadePowerupInfo.Create();
                        powerup.ProductId = "arcade.powerup.shield";
                        powerup.RemainingSeconds = 25f;
                        powerup.Charges = 1;
                        powerup.PositionX = 4.5f;
                        powerup.PositionZ = -2.5f;
                        powerup.ShotDirectionX = 0.6f;
                        powerup.ShotDirectionZ = -0.8f;
                        player.Powerups.Add(powerup);
                    }
                    battle.Players.Add(player);
                }
                for (int index = 0; index < enemyCount; index++)
                {
                    OnlineArcadeEnemyInfo enemy = OnlineArcadeEnemyInfo.Create();
                    enemy.RuntimeId = index + 1; enemy.EnemyConfigId = "enemy.graybox.basic";
                    enemy.MaxHealth = 20; enemy.CurrentHealth = 20; enemy.TargetPlayerId = 10; enemy.WaveNumber = 1;
                    if (index == 0)
                    {
                        enemy.BossId = "arcade.boss.brute-charge";
                        enemy.BossPhase = (int)ArcadeBossPhase.Telegraph;
                        enemy.BossPhaseSeconds = .9f;
                    }
                    battle.Enemies.Add(enemy);
                }
                OnlineArcadeProjectileInfo projectile = OnlineArcadeProjectileInfo.Create();
                projectile.RuntimeId = 100; projectile.OwnerPlayerId = 10;
                projectile.PositionX = 1f; projectile.PositionZ = 2f;
                projectile.VelocityX = 12f; projectile.VelocityZ = 0f;
                battle.Projectiles.Add(projectile);
                OnlineArcadeDropInfo drop = OnlineArcadeDropInfo.Create();
                drop.RemainingSeconds = 1.5f;
                battle.TreasureRewardSeconds = 2f;
                drop.RuntimeId = 200; drop.DropConfigId = "arcade.drop.gold.small";
                drop.PositionX = -1f; drop.PositionZ = 3f;
                battle.Drops.Add(drop);
                battle.SpawnedInWave = enemyCount;
                currentBattle = battle;
                run.Battle = currentBattle;
                run.BattlePaused = paused;
                for (int index = 0; index < 2; index++)
                {
                    OnlineArcadeConnectionInfo connection = OnlineArcadeConnectionInfo.Create();
                    connection.PlayerId = index == 0 ? 10 : 20;
                    connection.Connected = !paused || index == 0;
                    if (!connection.Connected)
                    {
                        connection.DisconnectedAtServerTime = 10_000;
                        connection.ArenaReconnectDeadlineServerTime = 70_000;
                        connection.SoloEndAvailableAtServerTime = 610_000;
                    }
                    run.Connections.Add(connection);
                }
                arcadeRunChanged?.Invoke(new G2C_OnlineArcadeRunChanged { HasRun = true, Run = run });
            }

            public void PushRunEnded() => arcadeRunChanged?.Invoke(new G2C_OnlineArcadeRunChanged { HasRun = false });

            private ET.OnlineArcadeRunSnapshot ProtocolRun()
            {
                ET.OnlineArcadeRunSnapshot run = ET.OnlineArcadeRunSnapshot.Create();
                run.RunId = 100;
                run.RoomId = 200;
                run.Revision = revision;
                run.Phase = currentBattle == null ? 1 : 2;
                run.NextArena = 1;
                run.ConfigFingerprint = ConfigFingerprint ?? string.Empty;
                run.Battle = currentBattle;
                run.Players.Add(Player(10, "one", "offer-one"));
                run.Players.Add(Player(20, "two", "offer-two"));
                ConfigureRun?.Invoke(run);
                return run;
            }

            private static OnlineArcadePlayerInfo Player(long id, string name, string offerId)
            {
                OnlineArcadePlayerInfo player = OnlineArcadePlayerInfo.Create();
                player.PlayerId = id; player.DisplayName = name; player.Lives = 3; player.Bombs = 1;
                player.Dashes = 2; player.WeaponId = "arcade.weapon.basic";
                player.SettlementConfirmed = id == 10;
                player.SettlementState = id == 10 ? 1 : 0;
                OnlineArcadeEquipmentInfo equipment = OnlineArcadeEquipmentInfo.Create();
                equipment.SlotId = "weapon"; equipment.ProductId = player.WeaponId;
                player.Equipment.Add(equipment);
                return player;
            }
        }

        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
}

using System;
using System.Threading;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ET;
using Game.Network;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class TestCmdTests
    {
        private string tempRoot;
        private TestConfigProvider configs;
        private RoutingNetwork network;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            configs = new TestConfigProvider();
            network = new RoutingNetwork();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(configs);
                architecture.RegisterUtility<INetworkRuntime>(network);
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "FirepowerLoopTestCmdTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            TestReset.ResetArchitecture();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void TerrainInstancingProbeChangesOnlyCurrentMapAndRestoresOriginalSettings()
        {
            var root = new GameObject("terrain-probe-map");
            var outside = new GameObject("outside-map-terrain");
            var data = new TerrainData { heightmapResolution = 33 };
            var outsideData = new TerrainData { heightmapResolution = 33 };
            try
            {
                var context = root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, root);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, new DiagnosticMapHandle(context));
                var terrain = root.AddComponent<Terrain>();
                terrain.terrainData = data;
                terrain.drawInstanced = true;
                var other = outside.AddComponent<Terrain>();
                other.terrainData = outsideData;
                other.drawInstanced = true;
                var system = Game.Interface.GetSystem<TestCmdSystem>();
                var guarded = system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(guarded.Code, Is.EqualTo(TestCmdResultCode.StateRejected));
                Assert.That(terrain.drawInstanced, Is.True);
                Assert.That(other.drawInstanced, Is.True);
                UnityEngine.Object.DestroyImmediate(outside);
                var disabled = system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(disabled.Success, Is.True, disabled.Message);
                Assert.That(terrain.drawInstanced, Is.False);
                Assert.That(system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
                Assert.That(system.ExecuteAsync("#terrain instancing restore", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
                Assert.That(system.ExecuteAsync("#terrain instancing off", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
                Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.Arcade));
                Assert.That(terrain.drawInstanced, Is.True, "切图开始时应恢复对照开关");
                Assert.That(terrain.drawInstanced, Is.True, "重复关闭不能覆盖原始状态");
                Assert.That(system.ExecuteAsync("#terrain instancing restore", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(outside);
                UnityEngine.Object.DestroyImmediate(data);
                UnityEngine.Object.DestroyImmediate(outsideData);
            }
        }

        [Test]
        public void TerrainCommandReportsOnlyCurrentMapWithoutChangingItsState()
        {
            var root = new GameObject("current-map-diagnostic");
            try
            {
                var context = root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, root);
                var handle = new DiagnosticMapHandle(context);
                var map = Game.Interface.GetModel<MapSceneModel>();
                map.SetLoaded(MapIds.Arcade, handle);
                var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#terrain", CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(result.Success, Is.True);
                Assert.That(result.Message, Does.Contain("[TerrainDiagnostic]"));
                Assert.That(result.Message, Does.Contain("TerrainCount=0"));
                Assert.That(map.CurrentHandle, Is.SameAs(handle));
                Assert.That(map.Status.Value, Is.EqualTo(MapLoadStatus.Loaded));
                Assert.That(root.activeSelf, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void GeneratedCatalogIncludesTerrainDiagnosticCommand()
        {
            var table = new global::TbTestCmdEntry(new Luban.ByteBuf(File.ReadAllBytes("Assets/Game/Config/tbtestcmdentry.bytes")));
            Assert.That(table.GetOrDefault("diagnostics.terrain")?.CommandTemplate, Is.EqualTo("#terrain"));
        }

        private sealed class DiagnosticMapHandle : IMapContentHandle
        {
            public DiagnosticMapHandle(MapContext context) { Context = context; }
            public string MapId => MapIds.Arcade;
            public MapContext Context { get; }
            public bool IsValid => Context != null;
        }

        [Test]
        public void TerrainCommandRejectsExtraArgumentsAndUnavailableMap()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var invalid = system.ExecuteAsync("#terrain extra", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(invalid.Recognized, Is.True);
            Assert.That(invalid.Code, Is.EqualTo(TestCmdResultCode.UsageError));
            var unavailable = system.ExecuteAsync("#terrain", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(unavailable.Recognized, Is.True);
            Assert.That(unavailable.Success, Is.False);
            Assert.That(unavailable.Code, Is.EqualTo(TestCmdResultCode.StateRejected));
        }

        [Test]
        public void LogCommandRequestsRuntimeLogWindowAndRejectsExtraArguments()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();

            var result = system.ExecuteAsync("#log", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Recognized, Is.True);
            Assert.That(result.Success, Is.True);
            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.RuntimeLogOpenRequested));

            var invalid = system.ExecuteAsync("#log extra", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(invalid.Recognized, Is.True);
            Assert.That(invalid.Success, Is.False);
            Assert.That(invalid.Code, Is.EqualTo(TestCmdResultCode.UsageError));
        }

        [Test]
        public void UnitBarCommandIsSessionOnlyStrictAndIdempotent()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var model = Game.Interface.GetModel<TestCmdModel>();
            var eventCount = 0;
            var lastVisible = false;
            var unregister = Game.Interface.RegisterEvent<UnitHealthBarVisibilityChangedEvent>(evt =>
            {
                eventCount++;
                lastVisible = evt.Visible;
            });

            Assert.That(model.UnitHealthBarsVisible, Is.False);
            AssertResult(system.ExecuteAsync("#unitbar", CancellationToken.None).GetAwaiter().GetResult(), true, false, TestCmdResultCode.UsageError, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(eventCount, Is.EqualTo(0));

            AssertResult(system.ExecuteAsync("#UNITBAR on", CancellationToken.None).GetAwaiter().GetResult(), true, true, TestCmdResultCode.UnitHealthBarsChanged, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.True);
            Assert.That(eventCount, Is.EqualTo(1));
            Assert.That(lastVisible, Is.True);

            AssertResult(system.ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult(), true, true, TestCmdResultCode.UnitHealthBarsChanged, 0, 0);
            Assert.That(eventCount, Is.EqualTo(1));

            AssertResult(system.ExecuteAsync("#unitbar off", CancellationToken.None).GetAwaiter().GetResult(), true, true, TestCmdResultCode.UnitHealthBarsChanged, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(eventCount, Is.EqualTo(2));
            Assert.That(lastVisible, Is.False);

            AssertResult(system.ExecuteAsync("#unitbar true", CancellationToken.None).GetAwaiter().GetResult(), true, false, TestCmdResultCode.UsageError, 0, 0);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(eventCount, Is.EqualTo(2));
            unregister.UnRegister();
        }

        [Test]
        public void ArcadeCommandsUseStrictGrammarAndRejectWithoutLocalCombat()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();

            AssertResult(system.ExecuteAsync("#arcade", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade damage 0", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade bombs -1", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade clear everything", CancellationToken.None).GetAwaiter().GetResult(),
                true, false, TestCmdResultCode.UsageError, 0, 0);

            var inactive = system.ExecuteAsync("#arcade status", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(inactive, true, false, TestCmdResultCode.StateRejected, 0, 0);
            Assert.That(inactive.Message, Does.Contain("本地单人街机战斗"));
        }

        [Test]
        public void ArcadeStatusGoldAndResourcesMutateOnlyTheActiveCombatRuntime()
        {
            var combat = Game.Interface.GetSystem<ArcadeCombatSystem>();
            var arena = new ArcadeArenaDefinition(3, new[] { "test.wave" },
                Array.Empty<ArcadeObstacleDefinition>(), false, string.Empty, 0, 1);
            var catalog = new ArcadeConfigCatalogDefinition(
                default,
                Array.Empty<ArcadeWeaponDefinition>(),
                new[] { arena },
                Array.Empty<ArcadeEnemyGroupDefinition>(),
                Array.Empty<ArcadeBossDefinition>(),
                Array.Empty<ArcadeChallengeDefinition>(),
                Array.Empty<ArcadeDropDefinition>(),
                Array.Empty<ArcadeSupportVisualDefinition>(),
                Array.Empty<ArcadeUpgradeDefinition>(),
                Array.Empty<ArcadeRewardDefinition>());
            typeof(ArcadeCombatSystem).GetField("catalog", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(combat, catalog);
            typeof(ArcadeCombatSystem).GetField("arena", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(combat, arena);
            var runtime = new ArcadeCombatRuntime
            {
                ArenaNumber = 3,
                WaveIndex = 0,
                PlayerHealth = 40,
                PlayerMaxHealth = 100,
                ArenaGold = 7,
                Bombs = 1,
                Dashes = 2
            };
            Game.Interface.GetModel<ArcadeCombatModel>().Begin(runtime);
            var system = Game.Interface.GetSystem<TestCmdSystem>();

            var status = system.ExecuteAsync("#arcade status", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(status, true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);
            Assert.That(status.Message, Does.Contain("竞技场 3").And.Contain("生命 40/100"));
            AssertResult(system.ExecuteAsync("#arcade gold 25", CancellationToken.None).GetAwaiter().GetResult(),
                true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade bombs 4", CancellationToken.None).GetAwaiter().GetResult(),
                true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);
            AssertResult(system.ExecuteAsync("#arcade dashes 6", CancellationToken.None).GetAwaiter().GetResult(),
                true, true, TestCmdResultCode.ArcadeCommandComplete, 0, 0);

            Assert.That(runtime.ArenaGold, Is.EqualTo(32));
            Assert.That(runtime.Bombs, Is.EqualTo(4));
            Assert.That(runtime.Dashes, Is.EqualTo(6));
        }

        [Test]
        public void HistoryKeepsSubmittedUniqueRecentCommandsOnly()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var model = Game.Interface.GetModel<TestToolSettingsModel>();

            system.ExecuteAsync("# ", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#unknown 1", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#clear bag", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult();
            for (var i = 0; i < 35; i++)
            {
                system.ExecuteAsync($"#clone 2 {i + 1}", CancellationToken.None).GetAwaiter().GetResult();
            }
            system.ExecuteAsync("#clone 2 35", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(model.RecentCommands.Count, Is.EqualTo(TestToolSettingsModel.MaxRecentCommandCount));
            Assert.That(model.RecentCommands[0], Is.EqualTo("#clone 2 35"));
            Assert.That(model.RecentCommands.Count(item => item == "#clone 2 35"), Is.EqualTo(1));
            Assert.That(model.RecentCommands, Does.Not.Contain("#clone 1 1"));
            Assert.That(model.RecentCommands, Does.Not.Contain("#unknown 1"));
            Assert.That(Game.Interface.GetModel<TestCmdModel>().HasLastResult, Is.True);
        }

        [Test]
        public void CameraCommandUsesStrictGrammarAndRequiresActivePlayer()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var missing = system.ExecuteAsync("#camera", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(missing, true, false, TestCmdResultCode.UsageError, 0, 0);
            var extra = system.ExecuteAsync("#camera topdown extra", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(extra, true, false, TestCmdResultCode.UsageError, 0, 0);
            var unknown = system.ExecuteAsync("#camera side", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(unknown, true, false, TestCmdResultCode.UsageError, 0, 0);
            var noPlayer = system.ExecuteAsync("#CaMeRa ShOuLdEr", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(noPlayer, true, false, TestCmdResultCode.StateRejected, 0, 0);
        }

        [Test]
        public void CameraCommandSwitchesModeCaseInsensitivelyAndIsIdempotent()
        {
            const long mapInstanceId = 91;
            configs.Player = new PlayerConfigDefinition(
                "player", string.Empty, 100, 10, 1f, 1f, 3f, 360f, 5f);
            var motor = new CameraCommandMotor(mapInstanceId);
            var camera = new CameraCommandCamera();
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(new CameraCommandPorts(motor, camera));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId, UnitType.Player, UnitSource.Player,
                    "player", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var error), Is.True, error);
            Game.Interface.SendEvent(new PlayerReadyEvent(PlayerModel.LocalPlayerUnitId, mapInstanceId));

            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var shoulder = system.ExecuteAsync("#CaMeRa ShOuLdEr", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(shoulder, true, true, TestCmdResultCode.CameraModeChanged, 0, 0);
            var repeated = system.ExecuteAsync("#camera shoulder", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(repeated, true, true, TestCmdResultCode.CameraModeChanged, 0, 0);
            var topDown = system.ExecuteAsync("#camera topdown", CancellationToken.None).GetAwaiter().GetResult();
            AssertResult(topDown, true, true, TestCmdResultCode.CameraModeChanged, 0, 0);
            Assert.That(Game.Interface.GetModel<PlayerCameraModeModel>().Mode, Is.EqualTo(PlayerCameraMode.TopDown));
        }

        [Test]
        public void PrefixBoundaryRejectsWithoutHistoryDomainOrNetworkSideEffects()
        {
            TestCmdSystem system = Game.Interface.GetSystem<TestCmdSystem>();
            TestCmdModel model = Game.Interface.GetModel<TestCmdModel>();
            TestToolSettingsModel settings = Game.Interface.GetModel<TestToolSettingsModel>();

            Assert.That(system.ExecuteAsync(string.Empty, CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.EmptyInput));
            foreach (string input in new[] { "clone 1 1", "!clone 1 1", "#", "$" })
            {
                Assert.That(system.ExecuteAsync(input, CancellationToken.None).GetAwaiter().GetResult().Code,
                    Is.EqualTo(TestCmdResultCode.PrefixRequired), input);
            }

            Assert.That(settings.RecentCommands, Is.Empty);
            Assert.That(model.UnitHealthBarsVisible, Is.False);
            Assert.That(network.CallCount, Is.Zero);
        }

        [Test]
        public void BodySymbolsStayLiteralAndDollarRoutesWithCompleteHistoryForAllOutcomes()
        {
            // 服务器路由已有登录前置条件；夹具必须显式提供已认证快照。
            network.State=NetworkConnectionState.Disconnected;
            Game.Interface.GetModel<OnlineAccountModel>().Publish(new OnlineAccountSnapshot(
                OnlineConnectionState.Authenticated,OnlineOperationState.Idle,OnlineAccountResultCode.Success,
                "test-user",1,1,1,false,"test-user"));
            // 沿用 ServerTestCmdTests 的隔离方式，不让准备阶段触发房间与库存恢复请求。
            network.State=NetworkConnectionState.Connected;
            TestCmdSystem system = Game.Interface.GetSystem<TestCmdSystem>();
            TestCmdExecutionResult literal = system.ExecuteAsync("#clone #/$", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(literal.Code, Is.EqualTo(TestCmdResultCode.UnknownCommand));
            Assert.That(network.CallCount, Is.Zero);

            network.Response = new G2C_ExecuteServerTestCmd
                { Recognized = false, Success = false, ResultCode = 3, Message = "unknown" };
            Assert.That(system.ExecuteAsync("$missing #/$", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerUnknownCommand));
            Assert.That(((C2G_ExecuteServerTestCmd)network.LastRequest).CommandText, Is.EqualTo("missing #/$"));

            network.Response = new G2C_ExecuteServerTestCmd
                { Recognized = true, Success = false, ResultCode = 4, Message = "failed" };
            Assert.That(system.ExecuteAsync("$fail", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerExecutionFailed));

            network.Exception = new TimeoutException();
            Assert.That(system.ExecuteAsync("$slow", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerTimeout));

            network.State = NetworkConnectionState.Disconnected;
            Assert.That(system.ExecuteAsync("$offline", CancellationToken.None).GetAwaiter().GetResult().Code,
                Is.EqualTo(TestCmdResultCode.ServerNotConnected));

            Assert.That(network.CallCount, Is.EqualTo(3));
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands,
                Is.EqualTo(new[] { "$offline", "$slow", "$fail", "$missing #/$", "#clone #/$" }));
        }

        [Test]
        public void CommandWritesExpectedRuntimeLogSeverity()
        {
            var captured = new System.Collections.Generic.List<LogType>();
            using (Log.CaptureForTests((type, _) => captured.Add(type)))
            {
                Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#unitbar on", CancellationToken.None).GetAwaiter().GetResult();
                Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 nope", CancellationToken.None).GetAwaiter().GetResult();
                Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 2 1", CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.That(captured, Does.Contain(LogType.Log));
            Assert.That(captured, Does.Contain(LogType.Warning));
        }

        [Test]
        public void GeneratedRuntimeCatalogDiscoversCameraCategoryAndCommands()
        {
            var provider = new LubanRuntimeConfigProvider(() => new global::Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")))));

            Assert.That(provider.TryGetTestCmdCatalog(out var categories, out var entries, out var error), Is.True, error);
            Assert.That(categories.Select(value => value.Id), Does.Contain("camera-view"));
            Assert.That(entries.Select(value => value.Id), Does.Contain("camera.topdown"));
            Assert.That(entries.Select(value => value.Id), Does.Contain("camera.shoulder"));
            Assert.That(entries.Single(value => value.Id == "camera.firstperson").CommandTemplate, Is.EqualTo("#camera firstperson"));
            Assert.That(entries.Single(value => value.Id == "camera.firstperson").CategoryId, Is.EqualTo("camera-view"));
            Assert.That(entries.Single(value => value.Id == "camera.topdown").CategoryId, Is.EqualTo("camera-view"));
            Assert.That(entries.Single(value => value.Id == "camera.shoulder").CategoryId, Is.EqualTo("camera-view"));
            Assert.That(categories.Select(value => value.Id), Does.Contain("diagnostics"));
            Assert.That(entries.Single(value => value.Id == "diagnostics.runtime-log").CommandTemplate, Is.EqualTo("#log"));
            Assert.That(entries.Single(value => value.Id == "diagnostics.runtime-log").CategoryId, Is.EqualTo("diagnostics"));
        }

        private static void AssertResult(
            TestCmdExecutionResult result,
            bool recognized,
            bool success,
            TestCmdResultCode code,
            int affected,
            int rejected)
        {
            Assert.That(result.Recognized, Is.EqualTo(recognized));
            Assert.That(result.Success, Is.EqualTo(success));
            Assert.That(result.Code, Is.EqualTo(code));
            Assert.That(result.AffectedAmount, Is.EqualTo(affected));
            Assert.That(result.RejectedAmount, Is.EqualTo(rejected));
            Assert.That(result.Message, Is.Not.Empty);
        }

        private sealed class EventCounter
        {
            public int Count;
        }

        private sealed class RoutingNetwork: INetworkRuntime
        {
            public object Response;
            public Exception Exception;
            public IRequest LastRequest;
            public int CallCount;
            public NetworkConnectionState State = NetworkConnectionState.Connected;
            public bool IsInitialized => true;
            public NetworkConnectionState ConnectionState => State;
            public int ConnectionGeneration => 1;
            public void Initialize() { }
            public void Update() { }
            public Task ConnectAsync(NetworkEndpoint endpoint, int timeoutMilliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<TResponse> CallAsync<TResponse>(IRequest request, int timeoutMilliseconds, CancellationToken cancellationToken)
                where TResponse : class, IResponse
            {
                LastRequest = request;
                CallCount++;
                if (Exception != null)
                {
                    Exception value = Exception;
                    Exception = null;
                    return Task.FromException<TResponse>(value);
                }
                object valueResponse = Response;
                Response = null;
                return Task.FromResult((TResponse)valueResponse);
            }
            public IDisposable RegisterPushHandler<TMessage>(Action<TMessage> handler) where TMessage : class, IMessage => EmptyDisposable.Instance;
            public IDisposable RegisterDisconnectedHandler(Action<NetworkDisconnected> handler) => EmptyDisposable.Instance;
            public void Disconnect(NetworkDisconnectReason reason) => State = NetworkConnectionState.Disconnected;
            public void Dispose() => State = NetworkConnectionState.Disposed;
        }

        private sealed class EmptyDisposable: IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();
            public void Dispose() { }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public PlayerConfigDefinition? Player { get; set; }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out System.Collections.Generic.IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                if (Player.HasValue && string.Equals(Player.Value.Id, id, StringComparison.Ordinal))
                {
                    value = Player.Value;
                    error = null;
                    return true;
                }

                value = default;
                error = "missing player";
                return false;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "missing enemy";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "missing loot";
                return false;
            }
        }

        private sealed class CameraCommandPorts : IPlayerRuntimePortProvider
        {
            private readonly ICharacterMotorPort motor;
            private readonly IPlayerCameraPort camera;
            public CameraCommandPorts(ICharacterMotorPort motor, IPlayerCameraPort camera) { this.motor = motor; this.camera = camera; }
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port) { port = unitId == PlayerModel.LocalPlayerUnitId && mapInstanceId == motor.MapInstanceId ? motor : null; return port != null; }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port) { port = unitId == PlayerModel.LocalPlayerUnitId && mapInstanceId == motor.MapInstanceId ? camera : null; return port != null; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
        }

        private sealed class CameraCommandCamera : IPlayerCameraPort
        {
            public bool IsValid => true;
            public void ApplyProfile() { }
            public void LateUpdate() { }
            public bool SupportsMode(PlayerCameraMode mode, out string error) { error = null; return true; }
            public void ApplyMode(PlayerCameraMode mode, float yaw, float pitch) { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up) { position = Vector3.back; up = Vector3.up; return true; }
        }

        private sealed class CameraCommandMotor : ICharacterMotorPort
        {
            public CameraCommandMotor(long mapInstanceId) { MapInstanceId = mapInstanceId; }
            public int UnitId => PlayerModel.LocalPlayerUnitId;
            public long MapInstanceId { get; }
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
            public void FaceDirection(Vector3 direction) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Vector3.zero; return true; }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }

        private sealed class LocalizationTestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", path))) as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}

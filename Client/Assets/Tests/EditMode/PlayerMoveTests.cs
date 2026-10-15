using NUnit.Framework;
using QFramework;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class PlayerMoveTests
    {
        [Test]
        public void DeepWaterMovementFloatsSlowsAndRecoversActionsOnLand()
        {
            TestReset.ResetArchitecture();
            var layout=ValleyRandomSwimmingTests.Generate(37);
            var river=layout.River.Find(p=>Mathf.Abs(p.Z)<300 && layout.HeightAt(p.X,p.Z)<p.Y-2);
            var motor=new FakeMotorPort{Position=new Vector3(river.X,river.Y-ValleySwimmingRules.SurfaceFootDepth,river.Z),IsGrounded=false};
            var input=new TemporaryBuildingInputReader{MoveInput=Vector2.up,SprintHeld=true};
            var config=new PlayerConfigDefinition("player",string.Empty,100,10,1,1,1,180,5);
            PlayerInputSystem.SetInputReaderFactoryForTests(()=>input);
            Architecture<Game>.OnRegisterPatch=a=>
            {a.RegisterUtility<IPlayerRuntimePortProvider>(new FakePortProvider(motor,new FakeCameraPort()));a.RegisterUtility<IRuntimeConfigProvider>(new PlayerMoveRuntimeConfigProvider(config,new TestRuntimeConfigProvider()));};
            var root=new GameObject("SwimmingTest");GeneratedValleyContentHandle handle=null;
            try
            {
                PrepareActivePlayer(config);
                var original=new SwimmingMapHandle(root.AddComponent<MapContext>());
                handle=new GeneratedValleyContentHandle(original,ResourceLoader.Current,layout);
                typeof(GeneratedValleyContentHandle).GetField("GeneratedRoot",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(handle,root);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.CivilisationValley,handle);
                LifeCycle.Update();
                var move=Game.Interface.GetSystem<PlayerMoveSystem>();
                typeof(PlayerMoveSystem).GetMethod("AdvanceMovement",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(move,new object[]{.1f});
                Assert.That(move.IsSwimming,Is.True);
                Assert.That(new Vector2(motor.LastMove.x,motor.LastMove.z).magnitude/.1f,Is.EqualTo(.6f).Within(.01f));
                Assert.That(motor.LastMove.y,Is.EqualTo(0).Within(.01f),"深水应停留水面而非继续重力沉底");
                var actions=Game.Interface.GetSystem<PlayerActionSystem>();
                Assert.That(actions.TryBegin(new PlayerActionRequest(PlayerActionKind.Search,1,""),out _,out _),Is.False);
                Assert.That(actions.TryBegin(new PlayerActionRequest(PlayerActionKind.Reload,1,""),out _,out _),Is.True,"游泳不得扩大到未规定的动作禁令");
                motor.Position=layout.Route[3].ToVector();motor.IsGrounded=true;
                typeof(PlayerMoveSystem).GetMethod("AdvanceMovement",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(move,new object[]{.1f});Assert.That(move.IsSwimming,Is.False);
                Assert.That(actions.TryBegin(new PlayerActionRequest(PlayerActionKind.Search,1,""),out _,out _),Is.True);
            }
            finally
            {
                PlayerInputSystem.SetInputReaderFactoryForTests(null);Architecture<Game>.OnRegisterPatch=_=>{};
                if(handle!=null)handle.ReleaseGeneratedAsync().GetAwaiter().GetResult();else UnityEngine.Object.DestroyImmediate(root);
                TestReset.ResetArchitecture();
            }
        }
        private sealed class SwimmingMapHandle:IMapContentHandle
        {public SwimmingMapHandle(MapContext context){Context=context;}public string MapId=>MapIds.CivilisationValley;public MapContext Context{get;}public bool IsValid=>Context!=null;}
        [Test]
        public void NormalizeInputPreventsFasterDiagonalMovement()
        {
            var input = PlayerInputCtrl.Normalize(new Vector2(1f, 1f));
            Assert.That(input.magnitude, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void MotionUsesCameraYawOnXZPlane()
        {
            var motion = PlayerMoveSystem.CalculateHorizontalMotion(
                new Vector2(0f, 1f),
                45f,
                4f,
                0.25f);

            var expected = Quaternion.Euler(0f, 45f, 0f) * Vector3.forward;
            Assert.That(motion.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(motion.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(motion.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator RuntimeMovementUsesCameraYawIndependentlyFromPlayerFacing()
        {
            TestReset.ResetArchitecture();
            var motor = new FakeMotorPort { Rotation = Quaternion.Euler(0f, 90f, 0f) };
            var camera = new FakeCameraPort { TopDownYawDegrees = 0f };
            var input = new TemporaryBuildingInputReader { MoveInput = Vector2.up };
            var provider = new FakePortProvider(motor, camera);
            var playerConfig = new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 180f, 5f);
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<IRuntimeConfigProvider>(
                    new PlayerMoveRuntimeConfigProvider(playerConfig, new TestRuntimeConfigProvider()));
            };

            try
            {
                PrepareActivePlayer(playerConfig);
                do
                {
                    yield return null;
                }
                while (Time.deltaTime <= 0f);
                LifeCycle.Update();

                var horizontal = new Vector3(motor.LastMove.x, 0f, motor.LastMove.z);
                Assert.That(horizontal.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(Vector3.Angle(horizontal, Vector3.forward), Is.LessThan(0.01f), "俯视移动必须沿相机水平朝向");
                Assert.That(Vector3.Angle(horizontal, Vector3.right), Is.GreaterThan(89f), "玩家面向不得旋转移动坐标系");
            }
            finally
            {
                PlayerInputSystem.SetInputReaderFactoryForTests(null);
                Architecture<Game>.OnRegisterPatch = _ => { };
                TestReset.ResetArchitecture();
            }
        }

        [UnityTest]
        public IEnumerator RuntimeMovementFallsBackToConfiguredYawWhenCameraYawIsUnavailable()
        {
            TestReset.ResetArchitecture();
            var motor = new FakeMotorPort
            {
                Rotation = Quaternion.Euler(0f, 90f, 0f),
                MovementYawDegrees = 45f
            };
            var input = new TemporaryBuildingInputReader { MoveInput = Vector2.up };
            var provider = new FakePortProvider(motor, null);
            var playerConfig = new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 180f, 5f);
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<IRuntimeConfigProvider>(
                    new PlayerMoveRuntimeConfigProvider(playerConfig, new TestRuntimeConfigProvider()));
            };

            try
            {
                LogAssert.Expect(LogType.Error, "[PlayerCameraSystem] 玩家相机端口未绑定");
                PrepareActivePlayer(playerConfig);
                do
                {
                    yield return null;
                }
                while (Time.deltaTime <= 0f);
                LifeCycle.Update();

                var horizontal = new Vector3(motor.LastMove.x, 0f, motor.LastMove.z);
                var expected = Quaternion.Euler(0f, motor.MovementYawDegrees, 0f) * Vector3.forward;
                Assert.That(horizontal.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(Vector3.Angle(horizontal, expected), Is.LessThan(0.01f), "相机 Yaw 不可用时必须使用配置移动 Yaw");
                Assert.That(Vector3.Angle(horizontal, Vector3.right), Is.GreaterThan(44f), "回退方向不得来自玩家 Rotation");
            }
            finally
            {
                PlayerInputSystem.SetInputReaderFactoryForTests(null);
                Architecture<Game>.OnRegisterPatch = _ => { };
                TestReset.ResetArchitecture();
            }
        }

        [Test]
        public void ZeroDeltaProducesNoHorizontalMotion()
        {
            Assert.That(
                PlayerMoveSystem.CalculateHorizontalMotion(Vector2.one, 45f, 5f, 0f),
                Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ZeroCameraYawKeepsScreenUpOnPositiveZ()
        {
            var motion = PlayerMoveSystem.CalculateHorizontalMotion(
                Vector2.up,
                0f,
                5f,
                1f);

            Assert.That(motion, Is.EqualTo(new Vector3(0f, 0f, 5f)));
        }

        [Test]
        public void NonPlayingStateRejectsMovement()
        {
            Assert.IsFalse(PlayerMoveSystem.CanProcessMovement(AppState.Paused, true, true, true));
            Assert.IsFalse(PlayerMoveSystem.CanProcessMovement(AppState.Loading, true, true, true));
            Assert.IsFalse(PlayerMoveSystem.CanProcessMovement(AppState.Paused, true, true, true));
            Assert.IsTrue(PlayerMoveSystem.CanProcessMovement(AppState.Playing, true, true, true));
            Assert.IsFalse(PlayerMoveSystem.CanProcessMovement(AppState.Playing, false, true, true));
            Assert.IsFalse(PlayerMoveSystem.CanProcessMovement(AppState.Playing, true, false, true));
            Assert.IsFalse(PlayerMoveSystem.CanProcessMovement(AppState.Playing, true, true, false));
        }

        [Test]
        public void RuntimePortRequiresBoundActiveUnitAndPlayingState()
        {
            TestReset.ResetArchitecture();
            var motor = new FakeMotorPort { Position = new Vector3(2f, 0f, 0f) };
            var provider = new FakePortProvider(motor, new FakeCameraPort());
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestRuntimeConfigProvider());
            };

            try
            {
                var moveSystem = Game.Interface.GetSystem<PlayerMoveSystem>();
                var unitSystem = Game.Interface.GetSystem<UnitSystem>();
                Assert.IsNotNull(moveSystem);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);

                Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
                LifeCycle.Update();
                Assert.That(motor.MoveCount, Is.EqualTo(0));

                Assert.IsTrue(unitSystem.TryRegister(new UnitSnapshot(1, 9, UnitType.Player, UnitSource.Player, "player.local", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, false), out _));
                Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
                Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
                LifeCycle.Update();
                Assert.That(motor.MoveCount, Is.EqualTo(0));

                Assert.IsTrue(unitSystem.TrySetActive(1, true));
                Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
                LifeCycle.Update();

                Assert.That(motor.MoveCount, Is.EqualTo(1));
                Assert.IsTrue(unitSystem.TryGet(1, out var snapshot));
                Assert.That(snapshot.Position, Is.EqualTo(motor.Position));

                Assert.IsTrue(unitSystem.TrySetActive(1, false));
                LifeCycle.Update();
                Assert.That(motor.MoveCount, Is.EqualTo(1));
            }
            finally
            {
                Architecture<Game>.OnRegisterPatch = _ => { };
                TestReset.ResetArchitecture();
            }
        }

        [Test]
        public void RuntimeAimControlsFacingIndependentlyFromMovementAndKeepsLastValidDirection()
        {
            TestReset.ResetArchitecture();
            var motor = new FakeMotorPort();
            var input = new TemporaryBuildingInputReader();
            Vector3? aimPoint = Vector3.forward * 10f;
            var provider = new FakePortProvider(motor, new FakeCameraPort());
            var playerConfig = new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 180f, 5f);
            var runtimeConfig = new PlayerMoveRuntimeConfigProvider(playerConfig, new TestRuntimeConfigProvider());
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(_ => aimPoint);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<IRuntimeConfigProvider>(runtimeConfig);
            };

            try
            {
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(playerConfig, false);
                var app = Game.Interface.GetSystem<AppStateSystem>();
                app.TryTransition(AppState.Loading);
                app.TryTransition(AppState.Playing);
                Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                    new UnitSnapshot(1, 9, UnitType.Player, UnitSource.Player, "player.local", Vector3.zero,
                        Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _));
                Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));

                input.MoveInput = Vector2.right;
                PumpPlayerInput();
                RotateTowardAim(Game.Interface.GetSystem<PlayerMoveSystem>(), 1f);
                Assert.That(PlayerMoveSystem.CalculateHorizontalMotion(input.MoveInput, 0f, 1f, 1f), Is.EqualTo(Vector3.right));
                Assert.That(Vector3.Angle(motor.Rotation * Vector3.forward, Vector3.forward), Is.LessThan(.01f), "侧移不得把朝向改成移动方向");

                input.MoveInput = Vector2.down;
                PumpPlayerInput();
                RotateTowardAim(Game.Interface.GetSystem<PlayerMoveSystem>(), 1f);
                Assert.That(PlayerMoveSystem.CalculateHorizontalMotion(input.MoveInput, 0f, 1f, 1f), Is.EqualTo(Vector3.back));
                Assert.That(Vector3.Angle(motor.Rotation * Vector3.forward, Vector3.forward), Is.LessThan(.01f), "后退不得把朝向改成移动方向");

                input.MoveInput = Vector2.zero;
                aimPoint = Vector3.right * 10f;
                PumpPlayerInput();
                RotateTowardAim(Game.Interface.GetSystem<PlayerMoveSystem>(), 1f);
                Assert.That(Vector3.Angle(motor.Rotation * Vector3.forward, Vector3.right), Is.LessThan(.01f), "无移动输入时有效瞄准仍须驱动转向");

                var lastValidRotation = motor.Rotation;
                aimPoint = null;
                PumpPlayerInput();
                RotateTowardAim(Game.Interface.GetSystem<PlayerMoveSystem>(), 1f);
                Assert.That(Quaternion.Angle(motor.Rotation, lastValidRotation), Is.LessThan(.01f), "无效瞄准不得覆盖上一次有效方向");

                Game.Interface.GetModel<TemporaryBuildingModel>().Mode = TemporaryBuildingMode.OperatingManualAttack;
                aimPoint = Vector3.back * 10f;
                PumpPlayerInput();
                RotateTowardAim(Game.Interface.GetSystem<PlayerMoveSystem>(), 1f);
                Assert.That(Quaternion.Angle(motor.Rotation, lastValidRotation), Is.LessThan(.01f), "操作临时建筑期间普通移动系统不得接管旋转");
            }
            finally
            {
                PlayerInputSystem.SetInputReaderFactoryForTests(null);
                PlayerInputSystem.SetPointerGroundPositionResolverForTests(null);
                Architecture<Game>.OnRegisterPatch = _ => { };
                TestReset.ResetArchitecture();
            }
        }

        [Test]
        public void ActivePlayerFeedbackTicksEvenWhenMovementIsDisabled()
        {
            TestReset.ResetArchitecture();
            var motor = new FakeMotorPort { IsEnabled = false };
            var provider = new FakePortProvider(motor, new FakeCameraPort());
            var playerConfig = new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 180f, 5f);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<IRuntimeConfigProvider>(
                    new PlayerMoveRuntimeConfigProvider(playerConfig, new TestRuntimeConfigProvider()));
            };

            try
            {
                PrepareActivePlayer(playerConfig);
                var system = Game.Interface.GetSystem<PlayerMoveSystem>();
                var baseline = motor.CombatFeedbackTickCount;
                typeof(PlayerMoveSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(system, null);

                Assert.That(motor.MoveCount, Is.Zero);
                Assert.That(motor.CombatFeedbackTickCount, Is.EqualTo(baseline + 1),
                    "移动被阻断时，受击闪红的恢复计时仍需推进");
            }
            finally
            {
                Architecture<Game>.OnRegisterPatch = _ => { };
                TestReset.ResetArchitecture();
            }
        }

        [TestCase(true, PlayerCameraMode.TopDown, 0f, 1f, 0f, 0f, 1f)]
        [TestCase(true, PlayerCameraMode.TopDown, 90f, 0f, 1f, 0f, 1f)]
        [TestCase(true, PlayerCameraMode.TopDown, 0f, 0f, -1f, 0f, 1f)]
        [TestCase(false, PlayerCameraMode.TopDown, 0f, 1f, 0f, 0f, 1f)]
        [TestCase(true, PlayerCameraMode.Shoulder, 0f, 1f, 0f, 0f, 1f)]
        [TestCase(true, PlayerCameraMode.TopDown, 0f, 1f, 0f, 0f, 1f, false)]
        public void BaseMovementKeepsFacingWhileOtherModesUseAim(
            bool mobile, PlayerCameraMode mode, float yaw, float x, float y, float expectedX, float expectedZ,
            bool baseMap = true)
        {
            TestReset.ResetArchitecture();
            var motor = new FakeMotorPort();
            var input = new TemporaryBuildingInputReader { MoveInput = new Vector2(x, y) };
            var provider = new FakePortProvider(motor, new FakeCameraPort { TopDownYawDegrees = yaw });
            var config = new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 180f, 5f);
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            // 基地手机初始朝向不受移动或鼠标覆盖，其它模式继续使用瞄准目标。
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(_ => Vector3.forward * 10f);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<IRuntimeConfigProvider>(
                    new PlayerMoveRuntimeConfigProvider(config, new TestRuntimeConfigProvider()));
            };
            try
            {
                PrepareActivePlayer(config);
                if (!baseMap) Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
                Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(mobile);
                Game.Interface.GetModel<PlayerCameraModeModel>().Mode = mode;
                PumpPlayerInput();
                RotateTowardAim(Game.Interface.GetSystem<PlayerMoveSystem>(), 1f);
                var expected = new Vector3(expectedX, 0f, expectedZ);
                Assert.That(Vector3.Angle(motor.Rotation * Vector3.forward, expected), Is.LessThan(.01f));

                input.MoveInput = Vector2.zero;
                PumpPlayerInput();
                RotateTowardAim(Game.Interface.GetSystem<PlayerMoveSystem>(), 1f);
                Assert.That(Vector3.Angle(motor.Rotation * Vector3.forward, expected), Is.LessThan(.01f),
                    "松开摇杆应保持最后朝向；桌面和越肩仍沿用瞄准方向");
                Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
                input.MoveInput = Vector2.left;
                PumpPlayerInput();
                Assert.That(Game.Interface.GetSystem<PlayerInputSystem>().TryGetCurrentFacingDirection(out var pausedDirection), Is.True);
                Assert.That(Vector3.Angle(pausedDirection, expected), Is.LessThan(.01f), "暂停时不得接收新的转向意图");
            }
            finally
            {
                PlayerInputSystem.SetInputReaderFactoryForTests(null);
                PlayerInputSystem.SetPointerGroundPositionResolverForTests(null);
                Architecture<Game>.OnRegisterPatch = _ => { };
                TestReset.ResetArchitecture();
            }
        }

        private static void PumpPlayerInput()
        {
            typeof(PlayerInputSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(Game.Interface.GetSystem<PlayerInputSystem>(), null);
        }

        private static void RotateTowardAim(PlayerMoveSystem system, float deltaTime)
        {
            typeof(PlayerMoveSystem).GetMethod("RotateTowardAim", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(system, new object[] { deltaTime });
        }

        private static void PrepareActivePlayer(PlayerConfigDefinition playerConfig)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(playerConfig, false);
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(1, 9, UnitType.Player, UnitSource.Player, "player.local", Vector3.zero,
                    Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _));
            Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
        }

        private sealed class PlayerMoveRuntimeConfigProvider : IRuntimeConfigProvider
        {
            private readonly PlayerConfigDefinition player;
            private readonly IRuntimeConfigProvider fallback;

            public PlayerMoveRuntimeConfigProvider(PlayerConfigDefinition player, IRuntimeConfigProvider fallback)
            {
                this.player = player;
                this.fallback = fallback;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error) =>
                fallback.TryGetMap(id, out value, out error);

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error) =>
                fallback.TryGetMapSpawns(mapId, out values, out error);

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = player;
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error) =>
                fallback.TryGetEnemy(id, out value, out error);

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error) =>
                fallback.TryGetLoot(id, out value, out error);

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error) =>
                fallback.TryGetExtraction(id, out value, out error);
        }

        private sealed class FakePortProvider : IPlayerRuntimePortProvider
        {
            private readonly ICharacterMotorPort motor;
            private readonly IPlayerCameraPort camera;

            public FakePortProvider(ICharacterMotorPort motor, IPlayerCameraPort camera)
            {
                this.motor = motor;
                this.camera = camera;
            }

            public bool TryGetMotorPort(int unitId, long MapInstanceId, out ICharacterMotorPort port)
            {
                port = unitId == 1 && MapInstanceId == 9 && motor?.IsValid == true ? motor : null;
                return port != null;
            }

            public bool TryGetCameraPort(int unitId, long MapInstanceId, out IPlayerCameraPort port)
            {
                port = unitId == 1 && MapInstanceId == 9 && camera?.IsValid == true ? camera : null;
                return port != null;
            }

            public bool IsPlayerCollider(int unitId, long MapInstanceId, Collider collider)
            {
                return false;
            }
        }

        private sealed class FakeMotorPort : ICharacterMotorPort
        {
            public int UnitId => 1;
            public long MapInstanceId => 9;
            public bool IsValid { get; set; } = true;
            public bool IsEnabled { get; set; } = true;
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; } = Quaternion.identity;
            public float MovementYawDegrees { get; set; }
            public bool IsGrounded { get; set; } = true;
            public int MoveCount { get; private set; }
            public int CombatFeedbackTickCount { get; private set; }
            public Vector3 LastMove { get; private set; }

            public void Move(Vector3 motion)
            {
                MoveCount++;
                LastMove = motion;
                Position += motion;
            }

            public void RotateTowards(Quaternion rotation, float maxDegreesDelta)
            {
                Rotation = Quaternion.RotateTowards(Rotation, rotation, maxDegreesDelta);
            }

            public void FaceDirection(Vector3 direction) { }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public void SetWeaponPresentation(WeaponSlot activeSlot, bool showShortSword, bool showBow) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin)
            {
                origin = Position;
                return true;
            }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayBowFireFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { CombatFeedbackTickCount++; }
        }

        private sealed class FakeCameraPort : IPlayerCameraPort
        {
            public bool SupportsMode(PlayerCameraMode mode,out string error){error=null;return true;}
            public void ApplyMode(PlayerCameraMode mode,float yaw,float pitch){TopDownYawDegrees=yaw;}
            public bool IsValid => true;
            public float TopDownYawDegrees { get; set; }
            public void ApplyProfile() { }
            public void LateUpdate() { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
            {
                position = Vector3.zero;
                up = Vector3.up;
                return true;
            }
        }
    }
}

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class PlayerCameraModeIntegrationTests
    {
        private FakeMotorPort motor;
        private FakeCameraPort camera;
        private FakeCursorLockPort cursor;
        private FakePortProvider provider;
        private CameraInputReader input;
        private GameObject blockingUiObject;
        private GameObject mobileHudObject;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            motor = new FakeMotorPort();
            camera = new FakeCameraPort();
            cursor = new FakeCursorLockPort();
            input = new CameraInputReader();
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                provider = new FakePortProvider(motor, camera);
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<ICursorLockPort>(cursor);
                architecture.RegisterUtility<IRuntimeConfigProvider>(new TestRuntimeConfigProvider());
            };
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 1f, 1f, 5f), false);
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(1, 9, UnitType.Player, UnitSource.Player, "player.local", Vector3.zero, Quaternion.Euler(0f, 90f, 0f), UnitAuthorityMode.Local, 0, true), out _));
            Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
        }

        [TearDown]
        public void TearDown()
        {
            Architecture<Game>.OnRegisterPatch = _ => { };
            PlayerInputSystem.SetInputReaderFactoryForTests(null);
            if (blockingUiObject != null) Object.DestroyImmediate(blockingUiObject);
            if (mobileHudObject != null) Object.DestroyImmediate(mobileHudObject);
            UIHelper.ResetForTests();
            TestReset.ResetArchitecture();
        }

        [Test]
        public void EnteringHeJiaVillageInitializesShoulderBeforeMapModelCommit()
        {
            EnterMap(MapIds.HeJiaVillage, 2, 10);
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            var model = Game.Interface.GetModel<PlayerCameraModeModel>();
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(model.ShoulderYawDegrees, Is.EqualTo(90f).Within(.001f));
            Assert.That(model.ShoulderPitchDegrees, Is.EqualTo(15f).Within(.001f));
            Assert.That(cursor.IsLocked, Is.True);
            Assert.IsTrue(system.TryGetMovementYawDegrees(out var yaw));
            Assert.That(yaw, Is.EqualTo(90f).Within(.001f));
        }

        [Test]
        public void ShoulderAim_MouseHoldAndReleaseUseCurrentRangedWeapon()
        {
            EquipBowForShoulderAim();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            Assert.That(camera.ShoulderAiming, Is.True);
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            Assert.That(camera.ShoulderAiming, Is.False);
        }

        [Test]
        public void ShoulderAim_BlockingUiDiscardsHoldUntilRelease()
        {
            EquipBowForShoulderAim();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            var blocking = SetBlockingBagUi();
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            blocking.Hide();
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False, "关闭窗口不重放旧按住状态");
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
        }

        [Test]
        public void ShoulderAim_AmmoNotificationPreservesHoldButWeaponReplacementClearsIt()
        {
            EquipBowForShoulderAim();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            // 射击和装填复用此通知，但不会改变当前手持 Revision。
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(ReadShoulderAim(), Is.True, "弹药变化不应退出瞄准");
            Game.Interface.GetModel<WeaponEquipmentModel>().SetHeld(
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 1), ItemIds.Bow);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(ReadShoulderAim(), Is.False);
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False, "换成另一件同类武器也必须松开再按");
        }

        [Test]
        public void ShoulderAim_ModeWeaponAndMapChangesClearOldHold()
        {
            EquipBowForShoulderAim();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.That(system.TrySetMode(PlayerCameraMode.FirstPerson, out _), Is.True);
            Assert.That(ReadShoulderAim(), Is.False);
            Assert.That(system.TrySetMode(PlayerCameraMode.Shoulder, out _), Is.True);
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            Game.Interface.GetModel<WeaponEquipmentModel>().Reset();
            LifeCycle.LateUpdate();
            Assert.That(ReadShoulderAim(), Is.False);
            EquipBowForShoulderAim();
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            EnterMap(MapIds.Base, 2, 10, "player.graybox.default");
            Assert.That(ReadShoulderAim(), Is.False);
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
        }

        [Test]
        public void ShoulderAim_FocusPauseDeathAndMobileInputRejectHeldAim()
        {
            EquipBowForShoulderAim();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            LifeCycle.ApplicationFocus(false);
            Assert.That(ReadShoulderAim(), Is.False);
            LifeCycle.ApplicationFocus(true);
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Assert.That(ReadShoulderAim(), Is.False);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
        }

        [Test]
        public void ShoulderAim_OnlineUsesServerEquipmentAndClearsOnDisconnect()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null, MapPreparationMode.OnlinePlayersOnly);
            var player = new OnlineWorldPlayerSnapshot(1, 1, "self", 7, MapIds.Base, "player.local",
                Vector3.zero, 0f, OnlineWorldConnectionState.Online, 1, 100, 100, false, 0, 0, 1);
            var inventory = new OnlineInventorySnapshot(20, System.Array.Empty<OnlineItemSlotSnapshot>(),
                new OnlineEquipmentSnapshot(0, ItemIds.Bow, 0, WeaponSlot.Ranged), 1);
            var world = new OnlineWorldSnapshot(true, 7, MapIds.Base, 1, 1, new[] { player },
                System.Array.Empty<OnlineWorldEnemySnapshot>(), 0, 0, 0, false, OnlineWorldOperationState.Idle,
                OnlineWorldResultCode.Success, inventory);
            Game.Interface.GetModel<OnlineWorldModel>().Publish(world);
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True, "联机手持读取服务器快照，不读取离线空手状态");
            var replacementInventory = new OnlineInventorySnapshot(20, System.Array.Empty<OnlineItemSlotSnapshot>(),
                new OnlineEquipmentSnapshot(1, ItemIds.Bow, 0, WeaponSlot.Ranged), 2);
            var replacementWorld = new OnlineWorldSnapshot(true, 7, MapIds.Base, 1, 2, new[] { player },
                System.Array.Empty<OnlineWorldEnemySnapshot>(), 0, 0, 0, false, OnlineWorldOperationState.Idle,
                OnlineWorldResultCode.Success, replacementInventory);
            Game.Interface.GetModel<OnlineWorldModel>().Publish(replacementWorld);
            LifeCycle.LateUpdate();
            Assert.That(ReadShoulderAim(), Is.False, "换到另一库存格的同类远程武器也结束旧按压");
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.False);
            input.ShoulderAimHeld = false;
            LifeCycle.Update();
            input.ShoulderAimHeld = true;
            LifeCycle.Update();
            Assert.That(ReadShoulderAim(), Is.True);
            Game.Interface.GetModel<OnlineWorldModel>().Publish(world.WithConnectionLost(true));
            LifeCycle.LateUpdate();
            Assert.That(ReadShoulderAim(), Is.False);
        }

        private void EquipBowForShoulderAim()
        {
            var config = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(
                System.IO.File.ReadAllBytes(System.IO.Path.Combine("Assets/Game/Config", name + ".bytes")))));
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(config);
            Assert.That(config.TryGetWeapon(ItemIds.Bow, out _, out var error), Is.True, error);
            Game.Interface.GetModel<WeaponEquipmentModel>().SetHeld(
                new InventorySlotRef(InventoryContainerKind.PlayerBag, 0), ItemIds.Bow);
        }

        private bool ReadShoulderAim()
        {
            return Game.Interface.GetModel<PlayerCameraModeModel>().IsShoulderAiming;
        }

        [Test]
        public void FocusedNameUsesCurrentFrameCameraAndStopsAfterDisable()
        {
            var root = new GameObject("FocusedTerminal");
            root.SetActive(false);
            HudInteractableView view = null;
            var lifecycleStarted = false;
            try
            {
                var nameRoot = new GameObject("NameRoot");
                nameRoot.transform.SetParent(root.transform);
                view = root.AddComponent<HudInteractableView>();
                view.ConfigureForTests(nameRoot, "终端");
                root.SetActive(true);
                // EditMode 不驱动普通 MonoBehaviour 的启用回调，显式进入正式生命周期。
                typeof(PlayerInteractableView).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(view, null);
                lifecycleStarted = true;
                Game.Interface.GetSystem<PlayerInteractionSystem>().Focus(view);
                var cameraSystem = Game.Interface.GetSystem<PlayerCameraSystem>();
                Assert.That(cameraSystem.TrySetMode(PlayerCameraMode.Shoulder, out _), Is.True);
                Assert.That(cameraSystem.TryApplyLookDelta(new Vector2(10f, 0f)), Is.True);
                Assert.That(cameraSystem.TrySetMode(PlayerCameraMode.TopDown, out _), Is.True);

                LifeCycle.LateUpdate();

                Assert.That(nameRoot.activeSelf, Is.True, "俯视角保留原世界名称提示");
                // 本测试相机端口固定向下 45 度，输入将初始 90 度偏航推进到 91.2 度。
                Assert.That(Quaternion.Angle(nameRoot.transform.rotation, Quaternion.Euler(45f, 91.2f, 0f)),
                    Is.LessThan(0.01f));
                root.SetActive(false);
                typeof(PlayerInteractableView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(view, null);
                lifecycleStarted = false;
                var stoppedRotation = nameRoot.transform.rotation;
                Assert.That(cameraSystem.TrySetMode(PlayerCameraMode.Shoulder, out _), Is.True);
                Assert.That(cameraSystem.TryApplyLookDelta(new Vector2(10f, 0f)), Is.True);
                LifeCycle.LateUpdate();
                Assert.That(nameRoot.activeSelf, Is.False);
                Assert.That(Quaternion.Angle(nameRoot.transform.rotation, stoppedRotation), Is.LessThan(0.01f));
                Game.Interface.GetSystem<UnitSystem>().Remove(1, UnitRemovedReason.MapInstanceReleased);
                Assert.That(cameraSystem.TryGetScreenOrientation(out _, out _), Is.False);
            }
            finally
            {
                if (lifecycleStarted && view != null)
                    typeof(PlayerInteractableView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(view, null);
                Object.DestroyImmediate(root);
            }
        }

        private sealed class HudInteractableView : PlayerInteractableView
        {
            public int Interactions { get; private set; }
            public override void Interact() { Interactions++; }
        }

        [Test]
        public void FirstPersonHudUsesLookTargetAndInteractionRevalidatesAim() =>
            VerifyCrosshairInteraction(PlayerCameraMode.FirstPerson);

        [Test]
        public void ShoulderHudUsesLookTargetAndInteractionRevalidatesAim() =>
            VerifyCrosshairInteraction(PlayerCameraMode.Shoulder);

        private void VerifyCrosshairInteraction(PlayerCameraMode mode)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.SetActive(false);
            HudInteractableView view = null;
            try
            {
                root.transform.position = new Vector3(2f, -1f, 0f);
                view = root.AddComponent<HudInteractableView>();
                var label = new GameObject("WorldName");
                label.transform.SetParent(root.transform);
                view.ConfigureForTests(label, "仓库管理员");
                root.SetActive(true);
                typeof(PlayerInteractableView).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                var interaction = Game.Interface.GetSystem<PlayerInteractionSystem>();
                var cameraSystem = Game.Interface.GetSystem<PlayerCameraSystem>();
                Assert.That(cameraSystem.TrySetMode(mode, out _), Is.True);
                interaction.Focus(view);
                Physics.SyncTransforms();

                LifeCycle.LateUpdate();
                Assert.That(interaction.CrosshairTargetName, Is.EqualTo("仓库管理员"));
                Assert.That(label.activeSelf, Is.False, "第一和第三人称不能同时显示头顶名字");
                interaction.RequestInteract();
                Assert.That(view.Interactions, Is.EqualTo(1));

                root.transform.position = new Vector3(2f, -1f, 4f);
                Physics.SyncTransforms();
                interaction.RequestInteract();
                Assert.That(view.Interactions, Is.EqualTo(1), "移开目标后同帧按键不能操作旧目标");
                Assert.That(interaction.CrosshairTargetName, Is.Empty);
                Assert.That(cameraSystem.TrySetMode(PlayerCameraMode.TopDown, out _), Is.True);
                LifeCycle.LateUpdate();
                Assert.That(label.activeSelf, Is.True, "切回俯视角恢复范围提示");
            }
            finally
            {
                if (view != null)
                    typeof(PlayerInteractableView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(MapIds.Base)]
        [TestCase(MapIds.CivilisationValley)]
        [TestCase(MapIds.SurvivalSample)]
        [TestCase(MapIds.MissionSample)]
        public void EnteringOrdinaryMapResetsManualFirstPersonToShoulder(string mapId)
        {
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.FirstPerson, out _));
            EnterMap(mapId, 2, 10);
            Assert.That(Game.Interface.GetSystem<PlayerCameraSystem>().Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(camera.LastMode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(cursor.IsLocked, Is.True);
        }

        [Test]
        public void RepeatedReadyKeepsManualModeButReenteringVillageResetsIt()
        {
            EnterMap(MapIds.HeJiaVillage, 2, 10);
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.TopDown, out _));
            Game.Interface.SendEvent(new PlayerReadyEvent(2, 10));
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.TopDown));
            EnterMap(MapIds.HeJiaVillage, 3, 11);
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
        }

        [Test]
        public void FailedVillageEntryKeepsOldManualShoulderPose()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            Assert.IsTrue(system.TryApplyLookDelta(new Vector2(10f, -10f)));
            Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.HeJiaVillage));
            Game.Interface.SendEvent(new MapLoadingFailedEvent(MapIds.HeJiaVillage, "rollback"));
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            var model = Game.Interface.GetModel<PlayerCameraModeModel>();
            Assert.That(model.ShoulderYawDegrees, Is.EqualTo(91.2f).Within(.001f));
            Assert.That(model.ShoulderPitchDegrees, Is.EqualTo(16.2f).Within(.001f));
        }

        [Test]
        public void FirstPersonCommandUsesLookYawCenterAimAndPauseGate()
        {
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#camera firstperson", default).GetAwaiter().GetResult();
            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.CameraModeChanged));
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.FirstPerson));
            Assert.That(cursor.IsLocked, Is.True);
            Assert.IsTrue(system.TryApplyLookDelta(new Vector2(10f, 375f)));
            Assert.IsTrue(system.TryGetMovementYawDegrees(out var yaw));
            Assert.That(yaw, Is.EqualTo(91.2f).Within(.001f));
            Assert.That(Game.Interface.GetModel<PlayerCameraModeModel>().ShoulderPitchDegrees, Is.EqualTo(-45f).Within(.001f));
            Assert.IsTrue(system.TryCreateAimRay(Vector2.zero, out var ray));
            Assert.That(ray.direction.y, Is.LessThan(0f));
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            Assert.That(Game.Interface.GetSystem<MobileInputSystem>().CanAcceptShoulderControls(), Is.True);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False);
            Assert.IsFalse(system.TryApplyLookDelta(Vector2.one));
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(Game.Interface.GetModel<PlayerCameraModeModel>().ShoulderPitchDegrees, Is.EqualTo(-20f).Within(.001f));
        }

        private void EnterMap(string mapId, int unitId, long mapInstanceId, string configId = "player.local")
        {
            Game.Interface.SendEvent(new MapLoadingStartedEvent(mapId));
            provider.UnitId = motor.UnitId = unitId;
            provider.MapInstanceId = motor.MapInstanceId = mapInstanceId;
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(unitId, mapInstanceId, UnitType.Player, UnitSource.Player, configId,
                    Vector3.zero, motor.Rotation, UnitAuthorityMode.Local, 0, true), out _));
            // 与正式事务一致：玩家先提交，地图模型和完成事件随后提交。
            Game.Interface.SendEvent(new PlayerReadyEvent(unitId, mapInstanceId));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(mapId));
        }

        [Test]
        public void StartsShoulderFromPlayerYawAndCanSwitchBackFromTopDown()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(camera.LastMode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.TopDown, out _));
            Assert.That(cursor.IsLocked, Is.False);
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out var error), error);
            var model = Game.Interface.GetModel<PlayerCameraModeModel>();
            Assert.That(model.ShoulderYawDegrees, Is.EqualTo(90f).Within(0.001f));
            Assert.That(model.ShoulderPitchDegrees, Is.EqualTo(15f).Within(0.001f));
            Assert.That(camera.LastMode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(cursor.IsLocked, Is.True);
        }

        [Test]
        public void LookDeltaClampsPitchAndDoesNotAccumulateWhilePaused()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            Assert.IsTrue(system.TryApplyLookDelta(new Vector2(10f, -1000f)));
            var model = Game.Interface.GetModel<PlayerCameraModeModel>();
            Assert.That(model.ShoulderYawDegrees, Is.EqualTo(91.2f).Within(0.001f));
            Assert.That(model.ShoulderPitchDegrees, Is.EqualTo(60f).Within(0.001f));
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False);
            Assert.IsFalse(system.TryApplyLookDelta(Vector2.one));
            Assert.That(model.ShoulderYawDegrees, Is.EqualTo(91.2f).Within(0.001f));
        }

        [Test]
        public void RemovingPlayerReleasesCursorButKeepsSessionMode()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().Remove(1, UnitRemovedReason.MapInstanceReleased));
            Assert.That(cursor.IsLocked, Is.False);
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.IsFalse(system.TrySetMode(PlayerCameraMode.TopDown, out _));
        }

        [Test]
        public void AimRayUsesPointerInTopDownAndCenterInShoulder()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.TopDown, out _));
            Assert.IsTrue(system.TryCreateAimRay(new Vector2(12f, 34f), out _));
            Assert.That(camera.LastScreenPoint, Is.EqualTo(new Vector2(12f, 34f)));
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            Assert.IsTrue(system.TryCreateAimRay(new Vector2(99f, 88f), out _));
            Assert.That(camera.CenterRayCount, Is.EqualTo(1));
        }

        [Test]
        public void ArcadeReadyForcesHighTopDownProfileAndRestoresPreviousMode()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));

            Game.Interface.SendEvent(new ArcadePlayerReadyEvent(1, 9));
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.TopDown));
            Assert.That(camera.ArcadeMode, Is.True);

            Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(camera.ArcadeMode, Is.False);
        }

        [Test]
        public void NewArcadeInstanceUsesTopDownAndReturningToNewBaseUsesShoulder()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.Arcade));
            provider.UnitId = motor.UnitId = 2;
            provider.MapInstanceId = motor.MapInstanceId = 10;
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(2, 10, UnitType.Player, UnitSource.Player, "player.local",
                    Vector3.zero, motor.Rotation, UnitAuthorityMode.Local, 0, true), out _));
            Game.Interface.SendEvent(new ArcadePlayerReadyEvent(2, 10));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Arcade));
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.TopDown));
            Assert.That(camera.ArcadeMode, Is.True);
            Assert.That(cursor.IsLocked, Is.False);

            EnterMap(MapIds.Base, 3, 11);
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(camera.LastMode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(camera.ArcadeMode, Is.False);
            Assert.That(cursor.IsLocked, Is.True);
        }

        [Test]
        public void ShoulderCursor_ReleasesForBlockingStatesAndRelocksWhenGameplayRecovers()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            Assert.That(cursor.IsLocked, Is.True);

            var blocking = SetBlockingBagUi();
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False, "阻塞 UI 打开时应释放光标");
            blocking.Hide();
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.True, "UI 关闭且仍可操作时应重新锁定");

            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False, "暂停时应释放光标");
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.True);

            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False, "玩家死亡时应释放光标");
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.True);

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null, MapPreparationMode.OnlinePlayersOnly);
            var onlinePlayer = new OnlineWorldPlayerSnapshot(1, 1, "self", 7, MapIds.Base, "player.local",
                Vector3.zero, 0f, OnlineWorldConnectionState.Online, 1, 100, 100, false, 0, 0, 1);
            var connected = new OnlineWorldSnapshot(true, 7, MapIds.Base, 1, 1,
                new[] { onlinePlayer }, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);
            Game.Interface.GetModel<OnlineWorldModel>().Publish(connected.WithConnectionLost(true));
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False, "联机断开时应释放光标");
            Game.Interface.GetModel<OnlineWorldModel>().Publish(connected);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.True, "联机恢复且玩家可操作时应重新锁定");
        }

        [Test]
        public void LoadingWindowRejectsActiveOldPlayerAndAllowsNewMapInstancePlayer()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            var applyCountBeforeLoading = camera.ApplyCount;
            Game.Interface.SendEvent(new MapLoadingStartedEvent(MapIds.Base));
            Assert.That(cursor.IsLocked, Is.False, "地图加载应清理相机绑定");

            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(1, out var oldUnit), Is.True);
            Assert.That(oldUnit.IsActive, Is.True);
            Assert.That(provider.CanProvide(1, 9), Is.True, "旧 motor/camera 端口必须仍可取得");
            Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
            Assert.IsFalse(system.TryGetCameraPose(out _, out _), "迟到的旧玩家事件不得重新绑定");
            Assert.That(cursor.IsLocked, Is.False);
            Assert.That(camera.ApplyCount, Is.EqualTo(applyCountBeforeLoading), "拒绝旧事件不得重应用相机模式");

            provider.UnitId = 2;
            provider.MapInstanceId = 10;
            motor.UnitId = 2;
            motor.MapInstanceId = 10;
            Assert.IsTrue(Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(2, 10, UnitType.Player, UnitSource.Player, "player.local", Vector3.zero,
                    Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _));
            Game.Interface.SendEvent(new PlayerReadyEvent(2, 10));
            Assert.IsTrue(system.TryGetCameraPose(out _, out _), "加载窗口内的新 MapInstanceId 玩家必须正常绑定");
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(cursor.IsLocked, Is.True);
            Assert.That(camera.ApplyCount, Is.EqualTo(applyCountBeforeLoading + 1));
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Base));
        }

        [Test]
        public void LoadingFailureReplaysPendingOldPlayerAfterRecoveryEventOrder()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            var applyCountBeforeLoading = camera.ApplyCount;
            Game.Interface.SendEvent(new MapLoadingStartedEvent("candidate"));
            Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
            Assert.IsFalse(system.TryGetCameraPose(out _, out _));

            // 实际恢复顺序：恢复激活逻辑先发布 PlayerReady，随后 MapSceneSystem 发布失败事件。
            Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
            Assert.IsFalse(system.TryGetCameraPose(out _, out _), "失败事件到达前旧身份仍须隔离");
            Game.Interface.SendEvent(new MapLoadingFailedEvent("candidate", "test failure"));

            Assert.IsTrue(system.TryGetCameraPose(out _, out _), "失败事件必须重放待恢复旧身份");
            Assert.That(cursor.IsLocked, Is.True);
            Assert.That(camera.ApplyCount, Is.EqualTo(applyCountBeforeLoading + 1));
        }

        [Test]
        public void EarlyLoadingFailureRestoresValidOldPlayerWithoutIntermediateReadyEvent()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            var applyCountBeforeLoading = camera.ApplyCount;

            Game.Interface.SendEvent(new MapLoadingStartedEvent("candidate"));
            Assert.IsFalse(system.TryGetCameraPose(out _, out _));
            Assert.That(cursor.IsLocked, Is.False);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(1, out var oldUnit), Is.True);
            Assert.That(oldUnit.IsActive, Is.True);
            Assert.That(provider.CanProvide(1, 9), Is.True);

            Game.Interface.SendEvent(new MapLoadingFailedEvent("candidate", "early failure"));

            Assert.IsTrue(system.TryGetCameraPose(out _, out _), "没有中间 PlayerReady 的早期失败也必须恢复有效旧玩家");
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(cursor.IsLocked, Is.True);
            Assert.That(camera.ApplyCount, Is.EqualTo(applyCountBeforeLoading + 1));
        }

        [Test]
        public void BindingFailureClearsBindingAndDeinitRestoresFullCameraDefaults()
        {
            var system = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(system.TrySetMode(PlayerCameraMode.Shoulder, out _));
            Assert.IsTrue(system.TryApplyLookDelta(new Vector2(10f, -10f)));
            var oldRuntimeModel = Game.Interface.GetModel<PlayerCameraModeModel>();
            Assert.That(oldRuntimeModel.ShoulderYawDegrees, Is.Not.Zero);
            Assert.That(oldRuntimeModel.ShoulderPitchDegrees, Is.Not.EqualTo(15f));

            provider.AllowCamera = false;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "[PlayerCameraSystem] 玩家相机端口未绑定");
            Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
            Assert.That(cursor.IsLocked, Is.False);
            Assert.IsFalse(system.TryGetCameraPose(out _, out _), "相机端口绑定失败后不得保留旧端口");

            Game.Interface.Deinit();
            Assert.That(cursor.IsLocked, Is.False, "反初始化必须释放光标");
            Assert.That(input.WasDisposed, Is.True, "反初始化必须清理输入读取器绑定");
            Assert.That(oldRuntimeModel.Mode, Is.EqualTo(PlayerCameraMode.Shoulder), "反初始化必须把运行时相机模式恢复为规定默认状态");
            Assert.That(oldRuntimeModel.ShoulderYawDegrees, Is.Zero);
            Assert.That(oldRuntimeModel.ShoulderPitchDegrees, Is.EqualTo(15f));
        }

        [Test]
        public void PlayerInputUpdate_UsesPostLookCenterRayForFacingAndAttackInSameFrame()
        {
            var cameraSystem = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(cameraSystem.TrySetMode(PlayerCameraMode.Shoulder, out _));
            input.LookDeltaValue = new Vector2(.1f, 0f);
            input.AttackPressed = true;
            var received = false;
            var attackDirection = Vector3.zero;
            var unregister = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(evt =>
            {
                received = true;
                attackDirection = evt.AimDirection;
            });
            try
            {
                typeof(PlayerInputSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(Game.Interface.GetSystem<PlayerInputSystem>(), null);
                typeof(PlayerMoveSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(Game.Interface.GetSystem<PlayerMoveSystem>(), null);
            }
            finally
            {
                unregister.UnRegister();
            }

            Assert.That(received, Is.True);
            Assert.That(camera.LastYaw, Is.EqualTo(90.012f).Within(.001f), "同帧鼠标增量必须先更新越肩中心射线");
            var expected = Quaternion.Euler(0f, camera.LastYaw, 0f) * Vector3.forward;
            Assert.That(Vector3.Angle(attackDirection, expected), Is.LessThan(.01f), "攻击必须消费同一帧玩家朝向所用方向");
            Assert.That(motor.FaceDirectionCount, Is.EqualTo(1), "越肩身体应在移动更新中立即同步镜头水平朝向");
            Assert.That(Vector3.Angle(motor.Rotation * Vector3.forward, attackDirection), Is.LessThan(.01f), "人物实际 yaw 必须与同帧攻击方向一致");
            Assert.That(camera.CenterRayCount, Is.GreaterThan(0));
        }

        [TestCase(60f, 0f, -1f)]
        [TestCase(60f, 1f, 0f)]
        [TestCase(-20f, 0f, -1f)]
        [TestCase(0f, -1f, 0f)]
        public void ShoulderFacingFollowsCameraYawRegardlessOfPitchAndMovement(float pitch, float moveX, float moveY)
        {
            var cameraSystem = Game.Interface.GetSystem<PlayerCameraSystem>();
            Assert.IsTrue(cameraSystem.TrySetMode(PlayerCameraMode.Shoulder, out _));
            var mode = Game.Interface.GetModel<PlayerCameraModeModel>();
            mode.ShoulderPitchDegrees = pitch;
            // 从朝东转向朝南，镜头位于角色后方并带越肩横向偏移；低头射线会落在角色背后。
            input.LookDeltaValue = new Vector2(750f, 0f);
            input.MoveInputValue = new Vector2(moveX, moveY);
            camera.CenterRayOverride = new Ray(new Vector3(-.45f, 1.6f, 3f),
                Quaternion.Euler(pitch, 180f, 0f) * Vector3.forward);
            input.AttackPressed = true;
            PlayerAttackRequestedEvent request = default;
            var received = false;
            var unregister = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(evt => { request = evt; received = true; });
            try
            {
                typeof(PlayerInputSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Game.Interface.GetSystem<PlayerInputSystem>(), null);
                typeof(PlayerMoveSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(Game.Interface.GetSystem<PlayerMoveSystem>(), null);
            }
            finally { unregister.UnRegister(); }

            Assert.That(received, Is.True);
            Assert.That(Vector3.Angle(request.AimDirection, Vector3.back), Is.LessThan(.01f));
            Assert.That(request.ProjectileAimRay.HasValue, Is.True);
            Assert.That(Vector3.Angle(request.ProjectileAimRay.Value.direction, camera.CenterRayOverride.Value.direction), Is.LessThan(.01f),
                "三维射击仍保留准星射线的俯仰");
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(1, out var player), Is.True);
            Assert.That(Vector3.Angle(player.Rotation * Vector3.forward, Vector3.back), Is.LessThan(.01f),
                "人物权威朝向必须同帧同步镜头，不受移动、地面交点或配置转速影响");
        }

        [Test]
        public void MobileAttack_UsesCenterRayOnceAndCanRunWhileMovingLook()
        {
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out _));
            var count = 0;
            var direction = Vector3.zero;
            var registration = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(evt => { count++; direction = evt.AimDirection; });
            try
            {
                input.MoveInputValue = Vector2.up;
                input.MobileLookDeltaValue = new Vector2(50f, 0f);
                Assert.That(mobile.RequestShoulderAttack(), Is.True);
                LifeCycle.Update();
                Assert.That(Game.Interface.GetSystem<PlayerInputSystem>().MoveInput, Is.EqualTo(Vector2.up));
                Assert.That(Game.Interface.GetModel<PlayerCameraModeModel>().ShoulderYawDegrees, Is.EqualTo(96f).Within(.001f));
                input.MobileLookDeltaValue = Vector2.zero;
                LifeCycle.Update();
                Assert.That(count, Is.EqualTo(1), "一次点击只能消费一次，不能变为每帧攻击");
                Assert.That(Vector3.Angle(direction, new Vector3(.9945219f, 0f, -.1045285f)), Is.LessThan(.01f));
            }
            finally { registration.UnRegister(); }
        }

        [Test]
        public void MobileAttack_PauseDiscardsPendingClickBeforeResume()
        {
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out _);
            mobile.RequestShoulderAttack();
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            var count = 0;
            var registration = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => count++);
            try { LifeCycle.Update(); Assert.That(count, Is.Zero); }
            finally { registration.UnRegister(); }
        }

        [Test]
        public void MobileShoulder_DoesNotLockCursorForEditorTouchSimulation()
        {
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out _);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False);
        }

        [Test]
        public void MobileAttack_RejectsTopDownBlockingUiAndDeadPlayer()
        {
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Assert.That(mobile.RequestShoulderAttack(), Is.False);
            Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out _);
            var blocking = SetBlockingBagUi();
            Assert.That(mobile.RequestShoulderAttack(), Is.False);
            blocking.Hide();
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            Assert.That(mobile.RequestShoulderAttack(), Is.False);
        }

        [Test]
        public void MobileHud_ShoulderControlsRouteClickAndHideClearsPendingAttack()
        {
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            mobileHudObject = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"));
            var ui = mobileHudObject.GetComponent<MobileJoystickUI>();
            var ctrl = new MobileJoystickUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, ui);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, true);
            typeof(MobileJoystickUICtrl).GetMethod("OnInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);
            typeof(MobileJoystickUICtrl).GetMethod("OnShow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);
            Assert.That(ui.AttackButton.gameObject.activeSelf, Is.False);
            Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out _);
            LifeCycle.Update();
            Assert.That(ui.AttackButton.gameObject.activeInHierarchy, Is.True);
            Assert.That(ui.ReticleRoot.activeInHierarchy, Is.True);
            var count = 0;
            var registration = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => count++);
            try
            {
                ui.AttackButton.onClick.Invoke();
                LifeCycle.Update();
                Assert.That(count, Is.EqualTo(1));
                ui.AttackButton.onClick.Invoke();
                ctrl.Hide();
                LifeCycle.Update();
                Assert.That(count, Is.EqualTo(1));
                Assert.That(ui.ReticleRoot.activeSelf, Is.False);
            }
            finally { registration.UnRegister(); ctrl.Hide(); }
        }

        [TestCase(PlayerCameraMode.FirstPerson, false)]
        [TestCase(PlayerCameraMode.Shoulder, false)]
        [TestCase(PlayerCameraMode.FirstPerson, true)]
        [TestCase(PlayerCameraMode.Shoulder, true)]
        public void AimHud_ShowsReticleAcrossInputModesAndHidesWhenGameplayStops(PlayerCameraMode mode, bool mobileMode)
        {
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(mobileMode);
            mobileHudObject = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"));
            var ui = mobileHudObject.GetComponent<MobileJoystickUI>();
            var ctrl = new MobileJoystickUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, ui);
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, true);
            typeof(MobileJoystickUICtrl).GetMethod("OnInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);
            typeof(MobileJoystickUICtrl).GetMethod("OnShow", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);
            try
            {
                var cameraSystem = Game.Interface.GetSystem<PlayerCameraSystem>();
                Assert.IsTrue(cameraSystem.TrySetMode(mode, out _));
                LifeCycle.Update();
                Assert.That(ui.ReticleRoot.activeInHierarchy, Is.True, "键鼠和手机均需显示中心准星");
                Assert.That(ui.ContentVisibility.alpha, Is.EqualTo(mobileMode ? 1f : 0f));
                Assert.That(ui.AttackButton.gameObject.activeSelf, Is.EqualTo(mobileMode));

                var blocking = SetBlockingBagUi();
                LifeCycle.Update();
                Assert.That(ui.ReticleRoot.activeSelf, Is.False);
                blocking.Hide();
                LifeCycle.Update();
                Assert.That(ui.ReticleRoot.activeInHierarchy, Is.True);

                Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
                LifeCycle.Update();
                Assert.That(ui.ReticleRoot.activeSelf, Is.False);
                Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
                LifeCycle.Update();
                Assert.That(ui.ReticleRoot.activeInHierarchy, Is.True);

                Game.Interface.GetModel<CombatModel>().PlayerHP.Value = 0;
                // 只刷新 HUD，避免基地恢复系统按 Editor deltaTime 将零生命状态提前恢复。
                typeof(MobileJoystickUICtrl).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);
                Assert.That(ui.ReticleRoot.activeSelf, Is.False);
                Game.Interface.GetModel<CombatModel>().PlayerHP.Value = 100;
                Assert.IsTrue(cameraSystem.TrySetMode(PlayerCameraMode.TopDown, out _));
                LifeCycle.Update();
                Assert.That(ui.ReticleRoot.activeSelf, Is.False);
                Assert.IsTrue(cameraSystem.TrySetMode(mode, out _));
                LifeCycle.Update();
                Assert.That(ui.ReticleRoot.activeInHierarchy, Is.True);
                ctrl.Hide();
                Assert.That(ui.ReticleRoot.activeSelf, Is.False);
            }
            finally { ctrl.Hide(); }
        }

        [Test]
        public void MobileAttack_MapLoadingDiscardsPendingClick()
        {
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out _);
            Assert.That(mobile.RequestShoulderAttack(), Is.True);
            Game.Interface.SendEvent(new MapLoadingStartedEvent("candidate"));
            Game.Interface.SendEvent(new MapLoadingFailedEvent("candidate", "rollback"));
            Assert.That(mobile.CanAcceptShoulderControls(), Is.True);
            var count = 0;
            var registration = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => count++);
            try { LifeCycle.Update(); Assert.That(count, Is.Zero); }
            finally { registration.UnRegister(); }
        }

        [Test]
        public void MobileAttack_RejectsDisconnectedOnlineWorld()
        {
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.Shoulder, out _);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null, MapPreparationMode.OnlinePlayersOnly);
            var player = new OnlineWorldPlayerSnapshot(1, 1, "self", 7, MapIds.Base, "player.local",
                Vector3.zero, 0f, OnlineWorldConnectionState.Online, 1, 100, 100, false, 0, 0, 1);
            var connected = new OnlineWorldSnapshot(true, 7, MapIds.Base, 1, 1,
                new[] { player }, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success);
            Game.Interface.GetModel<OnlineWorldModel>().Publish(connected);
            Assert.That(mobile.RequestShoulderAttack(), Is.True);
            Game.Interface.GetModel<OnlineWorldModel>().Publish(connected.WithConnectionLost(true));
            Assert.That(mobile.RequestShoulderAttack(), Is.False);
        }

        [TestCase(MapIds.Base)]
        [TestCase(MapIds.CivilisationValley)]
        [TestCase(MapIds.SurvivalSample)]
        [TestCase(MapIds.MissionSample)]
        public void MobileBaseAimTurnsPlayerWithoutRotatingCameraOrMovementAxes(string mapId)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            var initialCameraYaw = camera.LastYaw;
            input.MoveInputValue = Vector2.left;
            Game.Interface.GetSystem<MobileInputSystem>().SetBaseAim(Vector2.down);
            LifeCycle.Update();
            var playerInput = Game.Interface.GetSystem<PlayerInputSystem>();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out var facing), Is.True);
            Assert.That(Vector3.Angle(facing, Vector3.back), Is.LessThan(.01f));
            Assert.That(playerInput.MoveInput, Is.EqualTo(Vector2.left));
            Assert.That(Game.Interface.GetSystem<PlayerCameraSystem>().TryGetMovementYawDegrees(out var yaw), Is.True);
            Assert.That(yaw, Is.Zero);
            Assert.That(camera.LastMode, Is.EqualTo(PlayerCameraMode.TopDown));
            Assert.That(camera.LastYaw, Is.EqualTo(initialCameraYaw), "摇杆不得改变镜头角度");

            Game.Interface.GetSystem<MobileInputSystem>().SetBaseAim(Vector2.zero);
            input.MoveInputValue = Vector2.up;
            LifeCycle.Update();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out facing), Is.True);
            Assert.That(Vector3.Angle(facing, Vector3.back), Is.LessThan(.01f), "摇杆回弹和移动不改变朝向");
            input.MobileLookDeltaValue = Vector2.zero;
            input.MoveInputValue = Vector2.zero;
            LifeCycle.Update();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out facing), Is.True);
            Assert.That(Vector3.Angle(facing, Vector3.back), Is.LessThan(.01f), "松手保持最后朝向");
        }

        [TestCase(MapIds.Base)]
        [TestCase(MapIds.CivilisationValley)]
        [TestCase(MapIds.SurvivalSample)]
        [TestCase(MapIds.MissionSample)]
        public void MobileBaseAimIsBlockedByUiPauseAndDeath(string mapId)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            LifeCycle.Update();
            var playerInput = Game.Interface.GetSystem<PlayerInputSystem>();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out var initial), Is.True);
            var blocking = SetBlockingBagUi();
            Game.Interface.GetSystem<MobileInputSystem>().SetBaseAim(Vector2.down);
            LifeCycle.Update();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out var facing), Is.True);
            Assert.That(Vector3.Angle(facing, initial), Is.LessThan(.01f));
            blocking.Hide();
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Game.Interface.GetSystem<MobileInputSystem>().SetBaseAim(Vector2.down);
            LifeCycle.Update();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out facing), Is.True);
            Assert.That(Vector3.Angle(facing, initial), Is.LessThan(.01f));
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            Game.Interface.GetSystem<MobileInputSystem>().SetBaseAim(Vector2.down);
            LifeCycle.Update();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out facing), Is.True);
            Assert.That(Vector3.Angle(facing, initial), Is.LessThan(.01f));
        }

        [Test]
        public void MobileTopDownExpeditionDoesNotConsumeBaseDrag()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(true);
            LifeCycle.Update();
            var playerInput = Game.Interface.GetSystem<PlayerInputSystem>();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out var initial), Is.True);
            input.MobileLookDeltaValue = new Vector2(750f, 400f);
            LifeCycle.Update();
            Assert.That(playerInput.TryGetCurrentFacingDirection(out var facing), Is.True);
            Assert.That(Vector3.Angle(facing, initial), Is.LessThan(.01f));
            Assert.That(camera.LastMode, Is.EqualTo(PlayerCameraMode.TopDown));
        }

        [TestCase(MapIds.Base)]
        [TestCase(MapIds.CivilisationValley)]
        [TestCase(MapIds.SurvivalSample)]
        [TestCase(MapIds.MissionSample)]
        public void MobileBaseAimDoesNotFireAndHeldFireStopsAfterRelease(string mapId)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            var count = 0;
            var last = Vector3.zero;
            var registration = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(e => { count++; last = e.AimDirection; });
            try
            {
                mobile.SetBaseAim(Vector2.left);
                LifeCycle.Update();
                Assert.That(count, Is.Zero);
                mobile.SetBaseFire(true);
                LifeCycle.Update(); LifeCycle.Update();
                Assert.That(count, Is.EqualTo(2));
                Assert.That(Vector3.Angle(last, Vector3.left), Is.LessThan(.01f));
                mobile.SetBaseFire(false);
                LifeCycle.Update();
                Assert.That(count, Is.EqualTo(2));
            }
            finally { registration.UnRegister(); }
        }

        [TestCase(MapIds.Base)]
        [TestCase(MapIds.CivilisationValley)]
        [TestCase(MapIds.SurvivalSample)]
        [TestCase(MapIds.MissionSample)]
        public void MobileBaseReloadIsOneShotAndDropsHeldAttack(string mapId)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            var reloads = 0; var attacks = 0;
            var r = Game.Interface.RegisterEvent<PlayerReloadRequestedEvent>(_ => reloads++);
            var a = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => attacks++);
            try
            {
                mobile.SetBaseFire(true);
                Assert.That(mobile.RequestBaseReload(), Is.True);
                LifeCycle.Update(); LifeCycle.Update();
                Assert.That(reloads, Is.EqualTo(1));
                Assert.That(attacks, Is.Zero);
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.HeJiaVillage, null);
                Assert.That(mobile.RequestBaseReload(), Is.False);
                mobile.SetBaseFire(true);
                LifeCycle.Update();
                Assert.That(attacks, Is.Zero);
            }
            finally { r.UnRegister(); a.UnRegister(); }
        }

        [TestCase(MapIds.Base)]
        [TestCase(MapIds.CivilisationValley)]
        [TestCase(MapIds.SurvivalSample)]
        [TestCase(MapIds.MissionSample)]
        public void MobileBaseBlockedInputDoesNotResumeHeldFire(string mapId)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            var attacks = 0;
            var a = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => attacks++);
            try
            {
                mobile.SetBaseFire(true);
                var blocking = SetBlockingBagUi();
                LifeCycle.Update();
                blocking.Hide();
                LifeCycle.Update();
                Assert.That(attacks, Is.Zero);
                mobile.SetBaseFire(true);
                LifeCycle.ApplicationFocus(false);
                LifeCycle.ApplicationFocus(true);
                LifeCycle.Update();
                Assert.That(attacks, Is.Zero);
            }
            finally { a.UnRegister(); }
        }

        [TestCase(MapIds.HeJiaVillage)]
        [TestCase(MapIds.Arcade)]
        [TestCase("map.unknown")]
        public void MobileSpecialMapsRejectGenericTopDownCombat(string mapId)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, null);
            Assert.IsTrue(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(PlayerCameraMode.TopDown, out _));
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            var attacks = 0;
            var registration = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => attacks++);
            try
            {
                Assert.That(mobile.CanAcceptBaseFacingControls(), Is.False);
                mobile.SetBaseAim(Vector2.down);
                mobile.SetBaseFire(true);
                Assert.That(mobile.RequestBaseReload(), Is.False);
                LifeCycle.Update();
                Assert.That(attacks, Is.Zero, "特殊地图不得消费通用俯视开火输入");
            }
            finally { registration.UnRegister(); }
        }

        [TestCase(PlayerCameraMode.FirstPerson)]
        [TestCase(PlayerCameraMode.Shoulder)]
        [TestCase(PlayerCameraMode.TopDown)]
        public void MobileReloadHud_ValleyShowsReloadAndRoutesOnePressAcrossCameraModes(PlayerCameraMode mode)
        {
            var config = new LubanRuntimeConfigProvider(() => new Tables(name => new Luban.ByteBuf(
                System.IO.File.ReadAllBytes(System.IO.Path.Combine("Assets/Game/Config", name + ".bytes")))));
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>(config);
            const int raygunItemId = 25;
            Assert.That(config.TryGetWeapon(raygunItemId, out var weapon, out var error), Is.True, error);
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            equipment.SetHeld(new InventorySlotRef(InventoryContainerKind.PlayerBag, 0), raygunItemId);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.CivilisationValley, null);
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            Assert.That(Game.Interface.GetSystem<PlayerCameraSystem>().TrySetMode(mode, out _), Is.True);
            mobileHudObject = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"));
            var ui = mobileHudObject.GetComponent<MobileJoystickUI>();
            var ctrl = new MobileJoystickUICtrl();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(BaseUICtrl).GetField("BaseUI", flags).SetValue(ctrl, ui);
            typeof(BaseUICtrl).GetField("_isActive", flags).SetValue(ctrl, true);
            typeof(MobileJoystickUICtrl).GetMethod("OnInit", flags).Invoke(ctrl, null);
            typeof(MobileJoystickUICtrl).GetMethod("OnShow", flags).Invoke(ctrl, null);
            var reloads = 0;
            var attacks = 0;
            var r = Game.Interface.RegisterEvent<PlayerReloadRequestedEvent>(_ => reloads++);
            var a = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => attacks++);
            try
            {
                Assert.That(ui.BaseReloadPress.gameObject.activeInHierarchy, Is.True);
                Assert.That(ui.BaseAimRoot.activeInHierarchy, Is.EqualTo(mode == PlayerCameraMode.TopDown));
                Assert.That(ui.BaseFirePress.gameObject.activeInHierarchy, Is.EqualTo(mode == PlayerCameraMode.TopDown));
                if (mode != PlayerCameraMode.TopDown) mobile.RequestShoulderAttack();
                else mobile.SetBaseFire(true);
                var pointer = new UnityEngine.EventSystems.PointerEventData(null) { pointerId = 19 };
                ui.BaseReloadPress.OnPointerDown(pointer);
                LifeCycle.Update(); LifeCycle.Update();
                Assert.That(reloads, Is.EqualTo(1), "一次按下只提交一次现有换弹请求");
                Assert.That(attacks, Is.Zero, "换弹丢弃同帧旧攻击意图");
                ui.BaseReloadPress.OnPointerUp(pointer);
                ui.BaseReloadPress.OnPointerDown(pointer);
                Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
                Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
                LifeCycle.Update();
                Assert.That(reloads, Is.EqualTo(1), "暂停前的请求不在恢复后重放");
                equipment.Reset();
                LifeCycle.Update();
                Assert.That(ui.BaseReloadPress.gameObject.activeInHierarchy, Is.False, "空手隐藏换弹按钮");
            }
            finally { r.UnRegister(); a.UnRegister(); ctrl.Hide(); }
        }

        private BaseUICtrl SetBlockingBagUi()
        {
            blockingUiObject = new GameObject("CameraBlockingBag", typeof(RectTransform), typeof(BagUI));
            // 本夹具只验证输入阻断，补齐关闭流程必需的弹窗节点，不加载完整背包资源。
            var quantityRoot = new GameObject("TransferQuantityRoot", typeof(RectTransform));
            quantityRoot.transform.SetParent(blockingUiObject.transform);
            blockingUiObject.GetComponent<BagUI>().TransferQuantityRoot = quantityRoot;
            var ctrl = new BagUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ctrl, blockingUiObject.GetComponent<BagUI>());
            typeof(BaseUICtrl).GetField("_isActive", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(ctrl, true);
            var dict = typeof(UIHelper).GetField("UICtrlDict", BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as Dictionary<string, BaseUICtrl>;
            Assert.That(dict, Is.Not.Null);
            dict[typeof(BagUICtrl).Name] = ctrl;
            return ctrl;
        }

        private sealed class FakePortProvider : IPlayerRuntimePortProvider
        {
            private readonly ICharacterMotorPort motor;
            private readonly IPlayerCameraPort camera;
            public FakePortProvider(ICharacterMotorPort motor, IPlayerCameraPort camera) { this.motor = motor; this.camera = camera; }
            public int UnitId { get; set; } = 1;
            public long MapInstanceId { get; set; } = 9;
            public bool AllowCamera { get; set; } = true;
            public bool CanProvide(int unitId, long mapInstanceId) =>
                AllowCamera && unitId == UnitId && mapInstanceId == MapInstanceId && motor?.IsValid == true && camera?.IsValid == true;
            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port) { port = unitId == UnitId && mapInstanceId == MapInstanceId ? motor : null; return port != null; }
            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port) { port = AllowCamera && unitId == UnitId && mapInstanceId == MapInstanceId ? camera : null; return port != null; }
            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider) => false;
        }

        private PlayerCameraSystem BeginTemporaryArcadeCamera()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            Game.Interface.GetModel<ArcadeCombatModel>().Begin(new ArcadeCombatRuntime
            { ArenaNumber = 1, PlayerHealth = 100, PlayerMaxHealth = 100 });
            Game.Interface.SendEvent(new ArcadePlayerReadyEvent(1, 9));
            return Game.Interface.GetSystem<PlayerCameraSystem>();
        }

        [Test]
        public void TemporaryArcadeCamera_LocksHorizontalLookAndRestoresOnce()
        {
            var system = BeginTemporaryArcadeCamera();
            Assert.That(system.TryBeginArcadeFirstPerson(9, out var error), Is.True, error);
            Assert.That(cursor.IsLocked, Is.True);
            Assert.That(system.TryApplyLookDelta(new Vector2(100, 200)), Is.True);
            Assert.That(Game.Interface.GetModel<PlayerCameraModeModel>().ShoulderPitchDegrees, Is.Zero);
            Assert.That(Game.Interface.GetModel<PlayerCameraModeModel>().ShoulderYawDegrees, Is.EqualTo(102).Within(.001));
            var count = camera.ApplyCount;
            Assert.That(system.TryBeginArcadeFirstPerson(9, out _), Is.True);
            Assert.That(camera.ApplyCount, Is.EqualTo(count));
            system.EndArcadeFirstPerson(9);
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.TopDown));
            Assert.That(cursor.IsLocked, Is.False);
            count = camera.ApplyCount;
            system.EndArcadeFirstPerson(9);
            Assert.That(camera.ApplyCount, Is.EqualTo(count));
        }

        [Test]
        public void TemporaryArcadeCamera_PauseReleasesCursorAndStaleExitCannotChangeNewMap()
        {
            var system = BeginTemporaryArcadeCamera();
            Assert.That(system.TryBeginArcadeFirstPerson(9, out _), Is.True);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.False);
            Assert.That(system.IsArcadeFirstPerson(9), Is.True);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            LifeCycle.LateUpdate();
            Assert.That(cursor.IsLocked, Is.True);
            EnterMap(MapIds.Base, 2, 10);
            var count = camera.ApplyCount;
            system.EndArcadeFirstPerson(9);
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.Shoulder));
            Assert.That(camera.ApplyCount, Is.EqualTo(count));
        }

        [Test]
        public void TemporaryArcadeCamera_UnsupportedResourceKeepsTopDownAndDoesNotGrantLease()
        {
            var system = BeginTemporaryArcadeCamera();
            camera.SupportsFirstPerson = false;
            Assert.That(system.TryBeginArcadeFirstPerson(9, out _), Is.False);
            Assert.That(system.IsArcadeFirstPerson(9), Is.False);
            Assert.That(system.Mode, Is.EqualTo(PlayerCameraMode.TopDown));
            Assert.That(cursor.IsLocked, Is.False);
        }

        private sealed class FakeCursorLockPort : ICursorLockPort
        {
            public bool IsLocked { get; private set; }
            public void SetLocked(bool locked) { IsLocked = locked; }
        }

        private sealed class FakeCameraPort : IPlayerCameraPort
        {
            public bool SupportsFirstPerson = true;
            public bool IsValid => true;
            public float TopDownYawDegrees => 0f;
            public PlayerCameraMode LastMode { get; private set; }
            public Vector2 LastScreenPoint { get; private set; }
            public int CenterRayCount { get; private set; }
            public Ray? CenterRayOverride { get; set; }
            public float LastYaw { get; private set; }
            public int ApplyCount { get; private set; }
            public bool ArcadeMode { get; private set; }
            public bool ShoulderAiming { get; private set; }
            public bool TrySetShoulderAim(bool aiming) { ShoulderAiming = aiming; return true; }
            public void SetArcadeMode(bool enabled) { ArcadeMode = enabled; }
            public void ApplyProfile() { LastMode = PlayerCameraMode.TopDown; ApplyCount++; }
            public void LateUpdate() { }
            public bool SupportsMode(PlayerCameraMode mode, out string error)
            { error = null; return mode != PlayerCameraMode.FirstPerson || SupportsFirstPerson; }
            public void ApplyMode(PlayerCameraMode mode, float yaw, float pitch) { LastMode = mode; LastYaw = yaw; ApplyCount++; }
            public void LateUpdate(PlayerCameraMode mode, float yaw, float pitch) { LastMode = mode; LastYaw = yaw; }
            public bool TryGetScreenPointRay(Vector2 point, out Ray ray) { LastScreenPoint = point; ray = new Ray(Vector3.up, Vector3.forward); return true; }
            public bool TryGetViewportCenterRay(out Ray ray) { CenterRayCount++; var flat = Quaternion.Euler(0f, LastYaw, 0f) * Vector3.forward; ray = CenterRayOverride ?? new Ray(Vector3.up, (flat - Vector3.up).normalized); return true; }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up) { position = Vector3.back; up = Vector3.up; return true; }
        }

        private sealed class CameraInputReader : PlayerInputSystem.IPlayerInputReader
        {
            public Vector2 MoveInput => MoveInputValue;
            public Vector2 MoveInputValue { get; set; }
            public bool IsEnabled { get; private set; }
            public bool AttackPressedThisFrame => AttackPressed;
            public bool AttackPressed { get; set; }
            public bool ShoulderAimHeld { get; set; }
            public bool HolsterPressedThisFrame { get; set; }
            public bool MeleeSlotPressedThisFrame => false;
            public bool RangedSlotPressedThisFrame => false;
            public bool InteractPressedThisFrame => false;
            public bool SprintHeld => false;
            public bool BuildPressedThisFrame => false;
            public bool DestroyPressedThisFrame => false;
            public bool CancelPressedThisFrame => false;
            public bool ReloadPressedThisFrame => false;
            public Vector2 LookDelta => LookDeltaValue;
            public Vector2 LookDeltaValue { get; set; }
            public Vector2 MobileLookDeltaValue { get; set; }
            public Vector2 ReadMobileLookDelta(bool enabled) => enabled ? MobileLookDeltaValue : Vector2.zero;
            public bool WasDisposed { get; private set; }
            public bool TryGetPointerScreenPosition(out Vector2 position) { position = new Vector2(12f, 34f); return true; }
            public void Enable() => IsEnabled = true;
            public void Disable() => IsEnabled = false;
            public void Dispose() { WasDisposed = true; }
        }

        private sealed class FakeMotorPort : ICharacterMotorPort
        {
            public int UnitId { get; set; } = 1;
            public long MapInstanceId { get; set; } = 9;
            public bool IsValid => true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation { get; private set; } = Quaternion.Euler(0f, 90f, 0f);
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public int RotateTowardsCount { get; private set; }
            public int FaceDirectionCount { get; private set; }
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta)
            {
                RotateTowardsCount++;
                Rotation = Quaternion.RotateTowards(Rotation, rotation, maxDegreesDelta);
            }
            public void FaceDirection(Vector3 direction) { FaceDirectionCount++; Rotation = Quaternion.LookRotation(direction, Vector3.up); }
            public void SetTiliRing(float current, float maximum, bool isSprinting, bool exhausted) { }
            public bool TryGetProjectileOrigin(Vector3 direction, out Vector3 origin) { origin = Vector3.zero; return true; }
            public void FaceWorldHud(Vector3 cameraPosition, Vector3 cameraUp) { }
            public void PlayAttackFeedback() { }
            public void PlayWeaponSwingFeedback() { }
            public void PlayHurtFeedback() { }
            public void TickCombatFeedback(float deltaTime) { }
        }
    }
}

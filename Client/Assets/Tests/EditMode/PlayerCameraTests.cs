using NUnit.Framework;
using QFramework;
using Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace Game.Tests
{
    public sealed class PlayerCameraTests
    {
        private GameObject cameraObject;
        private GameObject targetObject;
        private Camera camera;
        private TopDownCameraProfile profile;
        private ShoulderCameraProfile shoulderProfile;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("Camera");
            targetObject = new GameObject("Target");
            camera = cameraObject.AddComponent<Camera>();
            profile = ScriptableObject.CreateInstance<TopDownCameraProfile>();
            shoulderProfile = ScriptableObject.CreateInstance<ShoulderCameraProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(shoulderProfile);
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(targetObject);
        }

        [Test]
        public void DialogueHidesBodyEquipmentAndHudWithoutChangingCollisionAndRestoresOriginalValues()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(targetObject.transform, false);
            var renderer = body.GetComponent<Renderer>();
            var collider = body.GetComponent<Collider>();
            var hud = new GameObject("LocalHud").AddComponent<Canvas>();
            hud.transform.SetParent(targetObject.transform, false);
            var hiddenHud = new GameObject("HiddenHud").AddComponent<Canvas>();
            hiddenHud.transform.SetParent(targetObject.transform, false);
            hiddenHud.enabled = false;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            try
            {
                ctrl.ApplyMode(PlayerCameraMode.Shoulder, 30f, 15f);
                var position = targetObject.transform.position;
                var cameraPosition = camera.transform.position;
                ctrl.SetDialoguePresentationHidden(true);
                Assert.That(renderer.forceRenderingOff, Is.True);
                Assert.That(hud.enabled, Is.False);
                Assert.That(collider.enabled && targetObject.activeInHierarchy, Is.True);
                Assert.That(targetObject.transform.position, Is.EqualTo(position));
                Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
                var equipment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                equipment.transform.SetParent(targetObject.transform, false);
                var equipmentRenderer = equipment.GetComponent<Renderer>();
                ctrl.LateUpdate(PlayerCameraMode.Shoulder, 30f, 15f);
                Assert.That(equipmentRenderer.forceRenderingOff, Is.True);
                ctrl.ApplyMode(PlayerCameraMode.TopDown, 0f, 0f);
                Assert.That(renderer.forceRenderingOff, Is.True);
                ctrl.SetDialoguePresentationHidden(false);
                ctrl.SetDialoguePresentationHidden(false);
                Assert.That(renderer.forceRenderingOff, Is.False);
                Assert.That(equipmentRenderer.forceRenderingOff, Is.False);
                Assert.That(hud.enabled, Is.True);
                Assert.That(hiddenHud.enabled, Is.False);
                ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
                ctrl.SetDialoguePresentationHidden(true);
                ctrl.SetDialoguePresentationHidden(false);
                Assert.That(renderer.forceRenderingOff, Is.True, "结束交谈后第一人称仍应隐藏全身");
                ctrl.ApplyMode(PlayerCameraMode.Shoulder, 30f, 15f);
                ctrl.SetDialoguePresentationHidden(true);
            }
            finally { ctrl.Dispose(); }
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(hud.enabled, Is.True);
            Assert.That(hiddenHud.enabled, Is.False);
        }

        [Test]
        public void FirstPersonFollowsEyeAndRestoresLocalPresentation()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(targetObject.transform, false);
            var renderer = body.GetComponent<Renderer>();
            var hud = new GameObject("LocalHud").AddComponent<Canvas>();
            hud.transform.SetParent(targetObject.transform, false);
            var alreadyHidden = new GameObject("HiddenHud").AddComponent<Canvas>();
            alreadyHidden.transform.SetParent(targetObject.transform, false);
            alreadyHidden.enabled = false;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            try
            {
                ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 90f, 0f);
                Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0f, 1.6f, 0f)).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(renderer.forceRenderingOff, Is.True);
                Assert.That(hud.enabled, Is.False);
                var lateWeapon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lateWeapon.transform.SetParent(targetObject.transform, false);
                var lateRenderer = lateWeapon.GetComponent<Renderer>();
                targetObject.transform.position = new Vector3(3f, 2f, 5f);
                ctrl.LateUpdate(PlayerCameraMode.FirstPerson, 90f, -45f);
                Assert.That(lateRenderer.forceRenderingOff, Is.True, "模式进入后创建的武器也必须隐藏");
                Assert.That(camera.transform.position, Is.EqualTo(new Vector3(3f, 3.6f, 5f)).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(Vector3.Dot(camera.transform.forward, new Vector3(1f, 1f, 0f).normalized), Is.GreaterThan(.999f));
                ctrl.ApplyMode(PlayerCameraMode.TopDown, 0f, 0f);
                Assert.That(renderer.forceRenderingOff, Is.False);
                Assert.That(lateRenderer.forceRenderingOff, Is.False);
                Assert.That(hud.enabled, Is.True);
                Assert.That(alreadyHidden.enabled, Is.False);
                ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
            }
            finally { ctrl.Dispose(); }
            Assert.That(renderer.forceRenderingOff, Is.False);
            Assert.That(hud.enabled, Is.True);
        }

        [Test]
        public void ShoulderUsesManuallyDrivenCinemachineAndKeepsCurrentAimRay()
        {
            // Editor 的缩放窗口可能产生小数像素尺寸；固定整数视口以排除射线像素取整误差。
            camera.pixelRect = new Rect(0f, 0f, 64f, 64f);
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 15f);
            var virtualCamera = targetObject.GetComponentInChildren<CinemachineVirtualCamera>(true);
            Assert.That(virtualCamera, Is.Not.Null);
            Assert.That(virtualCamera.enabled, Is.False);
            Assert.That(camera.GetComponent<CinemachineBrain>(), Is.Null);
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 90f, 15f);
            Assert.That(ctrl.TryGetViewportCenterRay(out var ray), Is.True);
            Assert.That(Vector3.Distance(ray.direction, Quaternion.Euler(15f, 90f, 0f) * Vector3.forward), Is.LessThan(.00001f));
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 120f, 15f);
            Assert.That(ctrl.TryGetViewportCenterRay(out ray), Is.True);
            Assert.That(Vector3.Distance(ray.direction, Quaternion.Euler(15f, 120f, 0f) * Vector3.forward), Is.LessThan(.00001f));
            ctrl.ApplyProfile();
            Assert.That(ctrl.AppliedPitch, Is.EqualTo(70f));
        }

        [Test]
        public void ShoulderAimZoomKeepsCenterRayAndRestoresTravelLens()
        {
            shoulderProfile.AimTransitionSeconds = 0f;
            camera.pixelRect = new Rect(0f, 0f, 64f, 64f);
            using var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 30f, 15f);
            var travelFov = camera.fieldOfView;
            var follow = targetObject.GetComponentInChildren<CinemachineVirtualCamera>(true)
                .GetCinemachineComponent<Cinemachine3rdPersonFollow>();
            var travelDistance = follow.CameraDistance;
            var travelOffset = follow.ShoulderOffset.x;
            Assert.That(ctrl.TrySetShoulderAim(true), Is.True);
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 30f, 15f);
            Assert.That(camera.fieldOfView, Is.LessThan(travelFov));
            Assert.That(follow.CameraDistance, Is.LessThan(travelDistance));
            Assert.That(follow.ShoulderOffset.x, Is.GreaterThan(travelOffset));
            Assert.That(ctrl.TryGetViewportCenterRay(out var ray), Is.True);
            Assert.That(Vector3.Angle(ray.direction, Quaternion.Euler(15f, 30f, 0f) * Vector3.forward), Is.LessThan(.001f));
            Assert.That(ctrl.TrySetShoulderAim(false), Is.True);
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 30f, 15f);
            Assert.That(camera.fieldOfView, Is.EqualTo(travelFov));
            Assert.That(follow.CameraDistance, Is.EqualTo(travelDistance));
            Assert.That(follow.ShoulderOffset.x, Is.EqualTo(travelOffset));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ShoulderWallAppearingRetractsWithoutTopDownSmoothLag(bool aiming)
        {
            profile.FollowSmoothTime = 10f;
            shoulderProfile.ObstructionRadius = .05f;
            shoulderProfile.AimTransitionSeconds = 0f;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 0f);
            ctrl.TrySetShoulderAim(aiming);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 0f);
            var blocker = CreateObstacle("AppearingCameraWall",
                new Vector3(aiming ? shoulderProfile.AimShoulderOffset : shoulderProfile.ShoulderOffset,
                    shoulderProfile.TargetHeight, -1f), .2f);
            try
            {
                Physics.SyncTransforms();
                ctrl.LateUpdate(PlayerCameraMode.Shoulder, 0f, 0f);
                Assert.That(ctrl.AppliedDistance, Is.LessThan(1f), "新遮挡必须立即收镜，不继承俯视平滑滞后");
                var blockedPosition = camera.transform.position;
                Object.DestroyImmediate(blocker);
                Physics.SyncTransforms();
                ctrl.LateUpdate(PlayerCameraMode.Shoulder, 0f, 0f);
                Assert.That(Vector3.Distance(camera.transform.position, blockedPosition), Is.LessThan(.00001f),
                    "同帧刷新不得额外推进遮挡恢复阻尼");
            }
            finally
            {
                if (blocker != null) Object.DestroyImmediate(blocker);
                ctrl.Dispose();
            }
        }

        [Test]
        public void ShoulderAimTransitionReversesWithoutJumpOrDoubleFrameAdvance()
        {
            shoulderProfile.AimTransitionSeconds = .18f;
            using var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 15f);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var advance = typeof(PlayerCameraCtrl).GetMethod("AdvanceShoulderAim", flags);
            var lastFrame = typeof(PlayerCameraCtrl).GetField("lastShoulderFrame", flags);
            ctrl.TrySetShoulderAim(true);
            // 显式推进半程，排除 EditMode 全局时间步带来的不确定性。
            advance.Invoke(ctrl, new object[] { .09f });
            lastFrame.SetValue(ctrl, Time.frameCount);
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 0f, 15f);
            var halfwayFov = Mathf.Lerp(shoulderProfile.FieldOfView, shoulderProfile.AimFieldOfView, .5f);
            Assert.That(camera.fieldOfView, Is.EqualTo(halfwayFov).Within(.001f));
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 20f, 15f);
            Assert.That(camera.fieldOfView, Is.EqualTo(halfwayFov).Within(.001f));
            ctrl.TrySetShoulderAim(false);
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 20f, 15f);
            Assert.That(camera.fieldOfView, Is.EqualTo(halfwayFov).Within(.001f), "同帧松开只改变目标，不跳变构图");
            advance.Invoke(ctrl, new object[] { .09f });
            ctrl.LateUpdate(PlayerCameraMode.Shoulder, 20f, 15f);
            Assert.That(camera.fieldOfView, Is.EqualTo(shoulderProfile.FieldOfView).Within(.001f));
            ctrl.TrySetShoulderAim(true);
            advance.Invoke(ctrl, new object[] { .18f });
            ctrl.ApplyMode(PlayerCameraMode.FirstPerson, 0f, 0f);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 15f);
            Assert.That(camera.fieldOfView, Is.EqualTo(shoulderProfile.FieldOfView));
        }

        [Test]
        public void ShoulderRigReleaseIsIdempotentAndPreservesCamera()
        {
            targetObject.AddComponent<BoxCollider>();
            var originalTag = targetObject.tag;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 15f);
            Assert.That(ctrl, Is.InstanceOf<System.IDisposable>());
            var disposable = (System.IDisposable)(object)ctrl;
            disposable.Dispose();
            disposable.Dispose();
            Assert.That(targetObject.GetComponentInChildren<CinemachineVirtualCamera>(true), Is.Null);
            Assert.That(targetObject.tag, Is.EqualTo(originalTag));
            Assert.That(ctrl.IsValid, Is.False);
            Assert.That(camera != null, Is.True);
        }

        [Test]
        public void ApplyProfileForcesPerspectiveAndConfiguredValues()
        {
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile);

            ctrl.ApplyProfile();

            Assert.IsFalse(camera.orthographic);
            Assert.That(camera.fieldOfView, Is.EqualTo(40f).Within(0.001f));
            Assert.That(ctrl.AppliedPitch, Is.EqualTo(70f).Within(0.001f));
            Assert.That(ctrl.AppliedDistance, Is.EqualTo(14f).Within(0.001f));
        }

        [Test]
        public void ArcadeModeUsesCloserReadableUnitFraming()
        {
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile);

            ctrl.SetArcadeMode(true);
            ctrl.ApplyProfile();

            Assert.That(ctrl.AppliedPitch, Is.EqualTo(72f).Within(0.001f));
            Assert.That(ctrl.AppliedDistance, Is.EqualTo(16f).Within(0.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(45f).Within(0.001f));
        }

        [Test]
        public void DefaultProfileUsesStraightPerspectiveView()
        {
            var loadedProfile = AssetDatabase.LoadAssetAtPath<TopDownCameraProfile>(
                "Assets/Game/Config/DefaultCameraProfile.asset");

            Assert.That(loadedProfile, Is.Not.Null);
            Assert.That(loadedProfile.YawDegrees, Is.EqualTo(0f));
            Assert.That(loadedProfile.PitchDegrees, Is.EqualTo(75f));
            Assert.That(loadedProfile.FieldOfView, Is.InRange(35f, 40f));
        }

        [Test]
        public void ApplyProfileClampsUnsafeValues()
        {
            profile.PitchDegrees = 90f;
            profile.FieldOfView = 100f;
            profile.FollowDistance = 0f;
            profile.NearClipPlane = 0f;
            profile.FarClipPlane = 0f;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile);

            ctrl.ApplyProfile();

            Assert.That(ctrl.AppliedPitch, Is.EqualTo(75f).Within(0.001f));
            Assert.That(ctrl.AppliedDistance, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(camera.fieldOfView, Is.EqualTo(80f).Within(0.001f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(0.01f).Within(0.001f));
            Assert.That(camera.farClipPlane, Is.EqualTo(0.11f).Within(0.001f));
        }

        [Test]
        public void ShoulderProfileAssetUsesFixedGameplayValues()
        {
            var loaded = AssetDatabase.LoadAssetAtPath<ShoulderCameraProfile>("Assets/Game/Config/ShoulderCameraProfile.asset");
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded.TargetHeight, Is.EqualTo(1.6f));
            Assert.That(loaded.ShoulderOffset, Is.EqualTo(0.25f));
            Assert.That(loaded.FollowDistance, Is.EqualTo(3.5f));
            Assert.That(loaded.FieldOfView, Is.EqualTo(60f));
            Assert.That(loaded.AimShoulderOffset, Is.EqualTo(0.55f));
            Assert.That(loaded.AimFollowDistance, Is.EqualTo(2.2f));
            Assert.That(loaded.AimFieldOfView, Is.EqualTo(45f));
            Assert.That(loaded.AimTransitionSeconds, Is.EqualTo(.18f));
            Assert.That(loaded.DefaultPitchDegrees, Is.EqualTo(15f));
            Assert.That(loaded.MinPitchDegrees, Is.EqualTo(-20f));
            Assert.That(loaded.MaxPitchDegrees, Is.EqualTo(60f));
            Assert.That(loaded.LookSensitivity, Is.EqualTo(0.12f));
            Assert.That(loaded.FarClipPlane, Is.EqualTo(500f));
            Assert.That(loaded.MinimumDistance, Is.EqualTo(0.5f));
        }

        [Test]
        public void ShoulderDesiredPositionUsesFixedRightOffset()
        {
            var position = PlayerCameraCtrl.CalculateShoulderDesiredPosition(Vector3.zero, 1.6f, 0.55f, 3f, 0f, 0f);
            Assert.That(position, Is.EqualTo(new Vector3(0.55f, 1.6f, -3f)).Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void ArcadeModeCameraKeepsTargetCenteredForSmallMovement()
        {
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile);
            ctrl.SetArcadeMode(true);
            ctrl.ApplyProfile();
            var initialPosition = camera.transform.position;

            targetObject.transform.position = new Vector3(1f, 0f, 1f);
            ctrl.LateUpdate();

            Assert.That(camera.transform.position - initialPosition,
                Is.EqualTo(new Vector3(1f, 0f, 1f)).Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void ArcadeModeDoesNotAddSmoothLag()
        {
            profile.FollowSmoothTime = 10f;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile);
            ctrl.SetArcadeMode(true);
            ctrl.ApplyProfile();
            var initialPosition = camera.transform.position;

            targetObject.transform.position = new Vector3(6f, 0f, 0f);
            ctrl.LateUpdate();

            Assert.That(camera.transform.position.x - initialPosition.x, Is.EqualTo(6f).Within(0.001f),
                "街机镜头不得叠加平滑滞后，否则玩家会偏离屏幕中心");
        }

        [Test]
        public void ShoulderModeClampsPitchAndAppliesLens()
        {
            shoulderProfile.FarClipPlane = 500f;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 30f, 100f);
            Assert.That(ctrl.AppliedPitch, Is.EqualTo(60f));
            Assert.That(camera.fieldOfView, Is.EqualTo(60f));
            Assert.That(camera.nearClipPlane, Is.EqualTo(0.1f));
            Assert.That(camera.farClipPlane, Is.EqualTo(500f));
        }

        [Test]
        public void ShoulderObstructionUsesNearestNonPlayerHitAndRecovers()
        {
            shoulderProfile.ObstructionRadius = .05f;
            shoulderProfile.MinimumDistance = .5f;
            shoulderProfile.FollowDistance = 3f;
            var ctrl = new PlayerCameraCtrl(camera, targetObject.transform, profile, shoulderProfile);
            var targetPoint = targetObject.transform.position + Vector3.up * shoulderProfile.TargetHeight;
            var desired = PlayerCameraCtrl.CalculateShoulderDesiredPosition(
                targetObject.transform.position, shoulderProfile.TargetHeight, shoulderProfile.ShoulderOffset,
                shoulderProfile.FollowDistance, 0f, 0f);
            // Cinemachine 先处理肩部，再沿肩部到镜头的后向路径避障。
            var handPoint = targetPoint + Vector3.right * shoulderProfile.ShoulderOffset;
            var direction = Vector3.back;

            var far = CreateObstacle("FarCameraBlocker", handPoint + direction * 2f, .2f);
            var near = CreateObstacle("NearCameraBlocker", handPoint + direction * 1f, .2f);
            Physics.SyncTransforms();
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 0f);
            Assert.That(ctrl.AppliedDistance, Is.LessThan(1.1f), "多个命中必须选择最近有效遮挡");

            Object.DestroyImmediate(near);
            Object.DestroyImmediate(far);
            var self = CreateObstacle("PlayerSelfCollider", handPoint + direction, .2f);
            self.transform.SetParent(targetObject.transform, true);
            Physics.SyncTransforms();
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 0f);
            Assert.That(camera.transform.position, Is.EqualTo(desired).Using(Vector3ComparerWithEqualsOperator.Instance), "玩家自身碰撞体必须忽略");

            Object.DestroyImmediate(self);
            var close = CreateObstacle("CloseCameraBlocker", handPoint + direction * .3f, .1f);
            Physics.SyncTransforms();
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 0f);
            Assert.That(ctrl.AppliedDistance, Is.LessThan(.5f), "近距离墙体优先避障，不能被最小跟随距离推出墙外");
            Assert.That(camera.transform.position.z, Is.GreaterThan(-.25f + shoulderProfile.ObstructionRadius - .001f));

            Object.DestroyImmediate(close);
            Physics.SyncTransforms();
            ctrl.ApplyMode(PlayerCameraMode.Shoulder, 0f, 0f);
            Assert.That(camera.transform.position, Is.EqualTo(desired).Using(Vector3ComparerWithEqualsOperator.Instance), "遮挡消失后必须恢复未遮挡越肩位置");
        }

        private static GameObject CreateObstacle(string name, Vector3 position, float size)
        {
            var value = new GameObject(name);
            value.transform.position = position;
            value.AddComponent<BoxCollider>().size = Vector3.one * size;
            return value;
        }

        [Test]
        public void CameraPoseFollowsBindAndUnbindLifecycle()
        {
            TestReset.ResetArchitecture();
            var cameraPort = new FakeCameraPort();
            var motor = new FakeMotorPort();
            var provider = new FakePortProvider(motor, cameraPort);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IPlayerRuntimePortProvider>(provider);
                architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider());
            };

            try
            {
                var cameraSystem = Game.Interface.GetSystem<PlayerCameraSystem>();
                var unitSystem = Game.Interface.GetSystem<UnitSystem>();
                Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null);

                Assert.IsFalse(cameraSystem.TryGetCameraPose(out _, out _));
                Assert.IsTrue(unitSystem.TryRegister(new UnitSnapshot(1, 9, UnitType.Player, UnitSource.Player, "player.local", Vector3.zero, Quaternion.identity, UnitAuthorityMode.Local, 0, true), out _));
                Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));

                Assert.IsTrue(cameraSystem.TryGetCameraPose(out var position, out var up));
                Assert.That(position, Is.EqualTo(cameraPort.Position));
                Assert.That(up, Is.EqualTo(Vector3.up));
                Assert.That(cameraPort.ApplyCount, Is.EqualTo(1));

                Assert.IsTrue(unitSystem.TrySetActive(1, false));
                Assert.IsFalse(cameraSystem.TryGetCameraPose(out _, out _));

                Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
                Assert.IsFalse(cameraSystem.TryGetCameraPose(out _, out _));
                Assert.IsTrue(unitSystem.TrySetActive(1, true));
                Game.Interface.SendEvent(new PlayerReadyEvent(1, 9));
                Assert.IsTrue(cameraSystem.TryGetCameraPose(out _, out _));
                Assert.IsTrue(unitSystem.Remove(1, UnitRemovedReason.MapInstanceReleased));
                Assert.IsFalse(cameraSystem.TryGetCameraPose(out _, out _));
            }
            finally
            {
                Architecture<Game>.OnRegisterPatch = _ => { };
                TestReset.ResetArchitecture();
            }
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

        private sealed class FakeCameraPort : IPlayerCameraPort
        {
            public bool IsValid { get; set; } = true;
            public Vector3 Position { get; } = new Vector3(3f, 4f, 5f);
            public int ApplyCount { get; private set; }
            public bool SupportsMode(PlayerCameraMode mode, out string error) { error = null; return true; }
            public void ApplyMode(PlayerCameraMode mode, float yaw, float pitch) { ApplyCount++; }
            public void ApplyProfile() { ApplyCount++; }
            public void LateUpdate() { }
            public bool TryGetCameraPose(out Vector3 position, out Vector3 up)
            {
                position = Position;
                up = Vector3.up;
                return true;
            }
        }

        private sealed class FakeMotorPort : ICharacterMotorPort
        {
            public int UnitId => 1;
            public long MapInstanceId => 9;
            public bool IsValid { get; set; } = true;
            public bool IsEnabled => true;
            public Vector3 Position => Vector3.zero;
            public Quaternion Rotation => Quaternion.identity;
            public float MovementYawDegrees => 0f;
            public bool IsGrounded => true;
            public void Move(Vector3 motion) { }
            public void RotateTowards(Quaternion rotation, float maxDegreesDelta) { }
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
            public void TickCombatFeedback(float deltaTime) { }
        }
    }
}

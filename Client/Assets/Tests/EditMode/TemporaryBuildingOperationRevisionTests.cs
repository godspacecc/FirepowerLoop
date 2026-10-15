using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.Tests
{
    public sealed class TemporaryBuildingOperationRevisionTests
    {
        private FakeTemporaryBuildingViewPort viewPort;
        private FakeProjectilePort projectile;
        private GameObject ground;
        private GameObject uiRoot;
        private TemporaryBuildingInputReader input;
        private Vector3 pointerGround;
        private Ray pointerRay;
        private FakeMapHandle realMapHandle;
        private TemporaryBuildingCtrl realController;
        private GameObject floatPrefab;
        private readonly List<GameObject> transientObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            TemporaryBuildingBehaviorTestSupport.BeginIsolatedScene();
            ResourceLoader.SetLoaderForTests(new LocalizationAssetResourceLoader());
            LocalizationLanguages.ResetForTests();
            viewPort = new FakeTemporaryBuildingViewPort();
            projectile = new FakeProjectilePort();
            input = new TemporaryBuildingInputReader();
            PlayerInputSystem.SetInputReaderFactoryForTests(() => input);
            pointerGround = Vector3.forward * 10f;
            pointerRay = new Ray(Vector3.up, Vector3.forward);
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(_ => pointerGround);
            PlayerInputSystem.SetPointerRayResolverForTests(() => pointerRay);
            TemporaryBuildingBehaviorTestSupport.InstallArchitecture(new TestRuntimeConfigProvider(), viewPort, projectile, installPlayerInput: true);
            TemporaryBuildingBehaviorTestSupport.EnterExpedition();
            ground = TemporaryBuildingBehaviorTestSupport.CreateGround();
            uiRoot = TemporaryBuildingBehaviorTestSupport.CreateUiRoot();
            UIRoot.EnsureInstance();
            floatPrefab = CreateFloatPrefab();
            UIHelper.SetPrefabLoaderForTests(path => path == "Prefabs/UI/Notify/Float"
                ? floatPrefab
                : AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab"));
        }

        [TearDown]
        public void TearDown()
        {
            if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
            if (uiRoot != null) UnityEngine.Object.DestroyImmediate(uiRoot);
            if (floatPrefab != null) UnityEngine.Object.DestroyImmediate(floatPrefab);
            if (realMapHandle != null && realMapHandle.Root != null) UnityEngine.Object.DestroyImmediate(realMapHandle.Root);
            for (var i = transientObjects.Count - 1; i >= 0; i--)
            {
                if (transientObjects[i] != null) UnityEngine.Object.DestroyImmediate(transientObjects[i]);
            }
            transientObjects.Clear();
            PlayerInputSystem.SetInputReaderFactoryForTests(null);
            PlayerInputSystem.SetPointerGroundPositionResolverForTests(null);
            PlayerInputSystem.SetPointerRayResolverForTests(null);
            TemporaryBuildingBehaviorTestSupport.Reset();
        }

        [Test]
        public void ManualTowerOperation_SmoothlyCouplesPlayerAndStopsAtCollision()
        {
            var tower = BuildArrowTower(Vector3.right);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            system.SetFocusedBuildingForTests(tower);
            input.InteractPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.OperatingManualAttack));
            var error = (string)null;

            SetPointerGroundTarget(Vector3.forward * 10f);
            input.ClearPressed();
            PumpInput();
            system.TickForTests(1f);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var player), Is.True);
            Assert.That(player.Position, Is.EqualTo(Vector3.zero), "首次对齐阶段不能移动玩家");
            Assert.That(viewPort.ManualAimDirections[tower].x, Is.GreaterThan(0f));

            SetPointerGroundTarget(Vector3.left * 10f);
            PumpInput();
            system.TickForTests(.25f);
            SetPointerGroundTarget(Vector3.forward * 10f);
            PumpInput();
            system.TickForTests(.25f);
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out player), Is.True);
            Assert.That(player.Position.sqrMagnitude, Is.GreaterThan(0.0001f), "鼠标目标推进后应通过角色端口沿圆周移动");
            Assert.That(Vector3.Distance(Vector3.right, player.Position), Is.EqualTo(1f).Within(.001f));

            var partialStartPosition = player.Position;
            var partialStartAim = viewPort.ManualAimDirections[tower];
            SetPointerGroundTarget(Vector3.left * 10f);
            PumpInput();
            var partialTarget = (pointerGround - Vector3.right).normalized;
            var partialStep = 100f * .25f;
            var partialCandidate = Quaternion.RotateTowards(
                Quaternion.LookRotation(partialStartAim, Vector3.up),
                Quaternion.LookRotation(partialTarget, Vector3.up),
                partialStep) * Vector3.forward;
            FakeCharacterMotorPort.MotionResolverForTests = motion => motion * .5f;
            try
            {
                system.TickForTests(.25f);
                Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out player), Is.True);
                Assert.That(Vector3.Distance(partialStartPosition, player.Position), Is.GreaterThan(.0001f), "部分碰撞位移仍应移动玩家");
                var actualDirection = (Vector3.right - player.Position).normalized;
                Assert.That(Vector3.Angle(viewPort.ManualAimDirections[tower], actualDirection), Is.LessThan(.01f), "塔方向必须反算自实际玩家位置");
                Assert.That(Vector3.Distance(player.Position, Vector3.right - partialCandidate.normalized), Is.GreaterThan(.001f), "部分碰撞不得推进到完整候选角度");
            }
            finally
            {
                FakeCharacterMotorPort.MotionResolverForTests = null;
            }

            var legalDirectionBeforeBlock = viewPort.ManualAimDirections[tower];
            FakeCharacterMotorPort.MotionResolverForTests = _ => Vector3.zero;
            try
            {
                SetPointerGroundTarget(Vector3.left * 10f);
                PumpInput();
                system.TickForTests(.5f);
                Assert.That(viewPort.ManualAimDirections[tower], Is.EqualTo(legalDirectionBeforeBlock), "角色碰撞阻挡移动时箭塔不得越过最后合法角度");
            }
            finally
            {
                FakeCharacterMotorPort.MotionResolverForTests = null;
            }

            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 1);
            input.ReloadPressedThisFrame = true;
            PumpInput();
            system.TickForTests(2.1f);
            input.AttackPressedThisFrame = true;
            PumpInput();
            Assert.That(projectile.CreateCount, Is.GreaterThan(0));
            Assert.That(viewPort.ManualAimDirections[tower].sqrMagnitude, Is.GreaterThan(.99f));

            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.Normal), "玩家失效必须清理操作会话");
        }

        [Test]
        public void ManualTowerAim_TopDownSelfHitKeepsLastDirectionButShoulderDoesNotFreeze()
        {
            var tower = BuildArrowTower(Vector3.forward);
            var otherTower = BuildArrowTower(Vector3.left * 3f);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            system.SetFocusedBuildingForTests(tower);
            pointerGround = Vector3.forward * 11f;
            input.InteractPressedThisFrame = true;
            PumpInput();
            system.TickForTests(1f);
            var heldDirection = viewPort.ManualAimDirections[tower];
            Assert.That(Vector3.Angle(heldDirection, Vector3.forward), Is.LessThan(.01f));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var playerBefore), Is.True);

            viewPort.PickedRuntimeId = tower;
            pointerGround = Vector3.right * 10f;
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().TrySetManualAttack(
                tower, new TemporaryManualAttackRuntimeState(0f, 1)), Is.True);
            input.AttackPressedThisFrame = true;
            PumpInput();
            system.TickForTests(.25f);

            Assert.That(viewPort.ManualAimDirections[tower], Is.EqualTo(heldDirection), "俯视射线首先命中当前塔时应保留塔方向");
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var playerAfter), Is.True);
            Assert.That(playerAfter.Rotation, Is.EqualTo(playerBefore.Rotation), "俯视自命中时玩家应保留上一次操作方向");
            Assert.That(projectile.CreateCount, Is.EqualTo(1));
            Assert.That(Vector3.Angle(projectile.LastDirection, heldDirection), Is.LessThan(.01f), "同帧攻击必须使用保留方向");

            viewPort.PickedRuntimeId = otherTower;
            pointerRay = new Ray(pointerGround + Vector3.up * 10f, Vector3.down);
            PumpInput();
            system.TickForTests(.25f);
            Assert.That(Vector3.Angle(viewPort.ManualAimDirections[tower], heldDirection), Is.GreaterThan(.01f), "首先命中其它建筑不得误判为当前塔");

            var directionAfterOtherBuilding = viewPort.ManualAimDirections[tower];
            viewPort.PickedRuntimeId = 0;
            pointerGround = Vector3.left * 10f;
            pointerRay = new Ray(pointerGround + Vector3.up * 10f, Vector3.down);
            PumpInput();
            system.TickForTests(.25f);
            Assert.That(Vector3.Angle(viewPort.ManualAimDirections[tower], directionAfterOtherBuilding), Is.GreaterThan(.01f), "射线离开当前塔或先命中非建筑遮挡时应恢复普通旋转和移动");

            Game.Interface.GetModel<PlayerCameraModeModel>().Mode = PlayerCameraMode.Shoulder;
            var directionBeforeShoulder = viewPort.ManualAimDirections[tower];
            viewPort.PickedRuntimeId = tower;
            pointerGround = Vector3.forward * -10f;
            pointerRay = new Ray(pointerGround + Vector3.up * 10f, Vector3.down);
            PumpInput();
            system.TickForTests(.25f);
            Assert.That(Vector3.Angle(viewPort.ManualAimDirections[tower], directionBeforeShoulder), Is.GreaterThan(.01f), "越肩中心射线不得应用俯视自命中冻结规则");
        }

        [Test]
        public void ManualTowerReload_IsManualAtomicAndUsesIndependentWorldHud()
        {
            InstallRealController();
            var tower = BuildArrowTowerWithRealController(Vector3.right);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 5);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            system.SetFocusedBuildingForTests(tower);
            input.InteractPressedThisFrame = true;
            PumpInput();
            var error = (string)null;
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].LoadedAmmo, Is.Zero);
            input.ReloadPressedThisFrame = true;
            PumpInput();
            input.ReloadPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(5), "未完成装填不能扣背包");
            var reloadView = FindManualAttackView(tower);
            Assert.That(reloadView, Is.Not.Null);
            Assert.That(reloadView.MagazineRoot.activeSelf, Is.True);
            Assert.That(reloadView.ReloadProgressRoot.activeSelf, Is.True);
            Assert.That(reloadView.MagazineText.text, Does.Contain("0/5"));
            var progressBeforeTick = reloadView.ReloadProgressFill.fillAmount;

            system.TickForTests(2.1f);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].LoadedAmmo, Is.EqualTo(5));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.Zero);
            Assert.That(reloadView.ReloadProgressRoot.activeSelf, Is.False);
            Assert.That(reloadView.ReloadProgressFill.fillAmount, Is.GreaterThanOrEqualTo(progressBeforeTick));
            Assert.That(reloadView.MagazineText.text, Does.Contain("5/5"));

            var cameraPosition = reloadView.transform.position + Vector3.back * 5f + Vector3.up * 4f;
            var reloadRotationBeforeFace = reloadView.MagazineCanvas.transform.rotation;
            reloadView.FaceWorldHuds(cameraPosition, Vector3.up);
            Assert.That(reloadView.MagazineCanvas.transform.rotation, Is.Not.EqualTo(reloadRotationBeforeFace));

            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 1);
            input.ReloadPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False, "弹匣满时 R 不应创建动作");
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(1));

            input.DestroyPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.OperatingManualAttack), "操作中 Z 不得切换删除模式");

            var bagBeforeFailure = Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow);
            projectile.CreateSucceeds = false;
            var beforeFailure = Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower];
            input.AttackPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].LoadedAmmo, Is.EqualTo(5));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].NextShootTime, Is.EqualTo(beforeFailure.NextShootTime));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(bagBeforeFailure));

            projectile.CreateSucceeds = true;
            input.AttackPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].LoadedAmmo, Is.EqualTo(4));

            // 成功射击建立冷却后，R/Attack 必须先取消装弹，冷却分支不得再次结算。
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 1);
            input.ReloadPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var cooldownReload), Is.True);
            Assert.That(cooldownReload.Kind, Is.EqualTo(PlayerActionKind.TemporaryBuildingReload));
            var cooldownState = Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower];
            var cooldownBag = Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow);
            var cooldownProjectiles = projectile.CreateCount;
            Assert.That(UIHelper.HasUIOpen, Is.False, "冷却期 Attack 前不应有阻塞 UI");
            Assert.That(input.IsEnabled, Is.True);
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.OperatingManualAttack));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.GreaterThan(0));
            Assert.That(Game.Interface.GetSystem<UnitSystem>().TryGet(PlayerModel.LocalPlayerUnitId, out var cooldownPlayer), Is.True);
            Assert.That(cooldownPlayer.IsActive, Is.True);
            var attackRequests = 0;
            var attackUnregister = Game.Interface.RegisterEvent<PlayerAttackRequestedEvent>(_ => attackRequests++);
            var endedKinds = new List<PlayerActionEndedEvent>();
            var endedUnregister = Game.Interface.RegisterEvent<PlayerActionEndedEvent>(endedKinds.Add);
            input.AttackPressedThisFrame = true;
            Assert.That(input.AttackPressedThisFrame, Is.True);
            Assert.That(input.CancelPressedThisFrame, Is.False);
            PumpInput();
            attackUnregister.UnRegister();
            endedUnregister.UnRegister();
            Assert.That(attackRequests, Is.Zero, "箭塔操作 Attack 不应绕到普通武器事件");
            var actionRemains = Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var actionAfterCooldownAttack);
            Assert.That(endedKinds.Exists(evt => evt.Kind == PlayerActionKind.TemporaryBuildingReload && !evt.Completed), Is.True, "Attack 必须产生装弹取消事件");
            Assert.That(actionRemains, Is.False, "冷却期间 Attack 必须先取消装弹，残留动作=" + actionAfterCooldownAttack.Kind);
            Assert.That(projectile.CreateCount, Is.EqualTo(cooldownProjectiles));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].LoadedAmmo, Is.EqualTo(cooldownState.LoadedAmmo));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].NextShootTime, Is.EqualTo(cooldownState.NextShootTime));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(cooldownBag));
            AssertPlayerHeadProgressHiddenForReload(cooldownReload);

            // 弹匣不足但装弹中的 Attack 也只取消动作，并产生结构化 PressReload/AmmoInsufficient 原因。
            system.TickForTests(2f);
            var model = Game.Interface.GetModel<TemporaryBuildingModel>();
            Assert.That(model.TrySetManualAttack(tower, new TemporaryManualAttackRuntimeState(0f, 0)), Is.True);
            var promptReasons = new List<TemporaryBuildingPromptEvent>();
            var promptUnregister = Game.Interface.RegisterEvent<TemporaryBuildingPromptEvent>(promptReasons.Add);
            try
            {
                Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.GreaterThan(0));
                Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.OperatingManualAttack));
                Assert.That(UIHelper.HasUIOpen, Is.False);
                input.ReloadPressedThisFrame = true;
                PumpInput();
                Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.True);
                var insufficientBag = Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow);
                var insufficientProjectiles = projectile.CreateCount;
                input.AttackPressedThisFrame = true;
                PumpInput();
                Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
                Assert.That(promptReasons, Is.Not.Empty);
                Assert.That(promptReasons[promptReasons.Count - 1].Reason, Is.EqualTo(TemporaryBuildingPromptReason.PressReload).Or.EqualTo(TemporaryBuildingPromptReason.AmmoInsufficient));
                Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(insufficientBag));
                Assert.That(projectile.CreateCount, Is.EqualTo(insufficientProjectiles));
                Assert.That(model.ManualAttacks[tower].LoadedAmmo, Is.Zero);
            }
            finally
            {
                promptUnregister.UnRegister();
            }

            Assert.That(model.TrySetManualAttack(tower, new TemporaryManualAttackRuntimeState(0f, 4)), Is.True);

            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 1);
            input.ReloadPressedThisFrame = true;
            PumpInput();
            var bagBeforeCancel = Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow);
            var fixedHud = UIHelper.ShowUI<TemporaryBuildingHudUICtrl>();
            Assert.That(fixedHud.IsShow(), Is.True);
            input.CancelPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.OperatingManualAttack), "第一次 X 只取消装弹动作");
            Assert.That(UIHelper.GetUICtrl<TemporaryBuildingHudUICtrl>()?.IsShow(), Is.False, "进入箭塔操作时固定 TemporaryBuildingHud 必须隐藏");
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(bagBeforeCancel));
            input.CancelPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.Normal), "第二次 X 才退出操作");

            system.SetFocusedBuildingForTests(tower);
            input.InteractPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().ManualAttacks[tower].LoadedAmmo, Is.EqualTo(4), "退出操作后弹匣状态应保留");

            input.ReloadPressedThisFrame = true;
            PumpInput();
            var late = Game.Interface.GetSystem<PlayerActionSystem>();
            Assert.That(late.TryGetSnapshot(out var lateSnapshot), Is.True);
            var bagBeforeMapLoss = Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow);
            Game.Interface.SendEvent(new MapCurrentReleasedEvent(MapIds.SurvivalSample, TemporaryBuildingBehaviorTestSupport.MapInstanceId));
            Assert.That(late.TryGetSnapshot(out _), Is.False);
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(bagBeforeMapLoss));
            Game.Interface.SendEvent(new PlayerActionEndedEvent(lateSnapshot.ActionId, PlayerActionKind.TemporaryBuildingReload, true, PlayerActionCancelReason.ExternalInterrupted));
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(bagBeforeMapLoss), "卸图后的迟到完成不得结算");

            var releaseHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.SurvivalSample, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, releaseHandle);
            var releaseTower = BuildArrowTowerWithRealController(Vector3.right * 2f);
            system.SetFocusedBuildingForTests(releaseTower);
            input.InteractPressedThisFrame = true;
            PumpInput();
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 1);
            input.ReloadPressedThisFrame = true;
            PumpInput();
            var bagContainerBeforeDeinit = Game.Interface.GetModel<PlayerBagModel>().Container;
            var bagBeforeDeinit = bagContainerBeforeDeinit.GetTotalAmount(ItemIds.Arrow);
            var released = false;
            var releaseEvent = Game.Interface.RegisterEvent<PlayerActionEndedEvent>(evt => released |= evt.Kind == PlayerActionKind.TemporaryBuildingReload && !evt.Completed);
            Game.Interface.Deinit();
            releaseEvent.UnRegister();
            Assert.That(released, Is.True, "正式 Game.Interface.Deinit 必须取消装弹");
            Assert.That(bagContainerBeforeDeinit.GetTotalAmount(ItemIds.Arrow), Is.EqualTo(bagBeforeDeinit));
            Assert.That(UnityEngine.Object.FindObjectsOfType<TemporaryManualAttackView>(true), Is.Empty, "正式释放必须清理箭塔实例");
            if (releaseHandle.Root != null) UnityEngine.Object.DestroyImmediate(releaseHandle.Root);
        }

        [Test]
        public void ManualTowerAmmoPrompts_UseLocalizedAmmoName()
        {
            var tower = BuildArrowTower(Vector3.right);
            var prompts = new List<TemporaryBuildingPromptEvent>();
            var unregister = Game.Interface.RegisterEvent<TemporaryBuildingPromptEvent>(prompts.Add);
            var controller = new TemporaryBuildingCtrl((Game)Game.Interface);
            try
            {
                var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
                system.SetFocusedBuildingForTests(tower);
                input.InteractPressedThisFrame = true;
                PumpInput();
                input.AttackPressedThisFrame = true;
                PumpInput();
                Assert.That(prompts[0].Reason, Is.EqualTo(TemporaryBuildingPromptReason.AmmoInsufficient));
                Assert.That(prompts[0].AmmoItemId, Is.EqualTo(ItemIds.Arrow));
                var firstTip = FindVisibleFloatText();
                Assert.That(firstTip, Does.Contain(Loc.T("item.ammunition.arrow.name")));

                TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 1);
                input.AttackPressedThisFrame = true;
                PumpInput();
                Assert.That(prompts[1].Reason, Is.EqualTo(TemporaryBuildingPromptReason.PressReload));
                TimerMgr.Instance.TickForTests(.31f);
                var secondTip = FindVisibleFloatTextContaining("R");
                Assert.That(secondTip, Does.Contain("R"));
                Assert.That(Texts.CombatPressReload, Does.Contain("R"));
                Assert.That(Texts.CombatAmmoInsufficient(Loc.T("item.ammunition.arrow.name")), Does.Contain(Loc.T("item.ammunition.arrow.name")));
            }
            finally
            {
                unregister.UnRegister();
                controller.ReleaseAll();
            }
        }

        [Test]
        public void ManualTowerEmptyAmmoFeedbackRestartsEveryClickAndThrottlesMatchingTipPerTower()
        {
            InstallRealController();
            var firstTower = BuildArrowTowerWithRealController(Vector3.right);
            var secondTower = BuildArrowTowerWithRealController(Vector3.left * 3f);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var realtime = 10f;
            realController.SetPromptRealtimeProviderForTests(() => realtime);
            realController.SetPresentationUnscaledDeltaProviderForTests(() => .1f);

            system.SetFocusedBuildingForTests(firstTower);
            input.InteractPressedThisFrame = true;
            PumpInput();
            var firstView = FindManualAttackView(firstTower);
            Assert.That(firstView, Is.Not.Null);
            var baseScale = firstView.MagazineText.transform.localScale;

            Assert.That(system.RequestManualAttack(out _, out _), Is.False);
            LifeCycle.LateUpdate();
            Assert.That(firstView.MagazineText.transform.localScale, Is.EqualTo(baseScale * 1.2f));
            Assert.That(CountVisibleFloatTexts(), Is.EqualTo(1));

            Assert.That(system.RequestManualAttack(out _, out _), Is.False);
            Assert.That(firstView.MagazineText.transform.localScale, Is.EqualTo(baseScale), "每次空弹点击都必须从原始缩放重启反馈");
            TimerMgr.Instance.TickForTests(.31f);
            Assert.That(CountVisibleFloatTexts(), Is.EqualTo(1), "同塔、同原因和同弹药两秒内只显示一次提示");

            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Arrow, 1);
            Assert.That(system.RequestManualAttack(out _, out _), Is.False);
            TimerMgr.Instance.TickForTests(.31f);
            Assert.That(CountVisibleFloatTexts(), Is.EqualTo(2), "原因变化必须立即显示提示");

            system.CancelCurrentMode();
            system.SetFocusedBuildingForTests(secondTower);
            input.InteractPressedThisFrame = true;
            PumpInput();
            Assert.That(system.RequestManualAttack(out _, out _), Is.False);
            TimerMgr.Instance.TickForTests(.31f);
            Assert.That(CountVisibleFloatTexts(), Is.EqualTo(3), "切换箭塔后的第一次提示不得被前一座塔限频");

            system.CancelCurrentMode();
            system.SetFocusedBuildingForTests(firstTower);
            input.InteractPressedThisFrame = true;
            PumpInput();
            Assert.That(system.RequestManualAttack(out _, out _), Is.False);
            TimerMgr.Instance.TickForTests(.31f);
            Assert.That(CountVisibleFloatTexts(), Is.EqualTo(4), "退出操作必须清理该塔提示限频状态");

            realtime += 2f;
            Assert.That(system.RequestManualAttack(out _, out _), Is.False);
            TimerMgr.Instance.TickForTests(.31f);
            Assert.That(CountVisibleFloatTexts(), Is.EqualTo(5), "两秒后相同提示必须可再次显示");
        }

        [Test]
        public void ManualTowerWorldHudUsesReadableFacingConfirmedFontColorAndRestartableScale()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Building/GrayboxArrowTower.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(prefab);
            transientObjects.Add(instance);
            var view = instance.GetComponent<TemporaryManualAttackView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.ValidateReferences(out var error), Is.True, error);
            Assert.That(typeof(TemporaryManualAttackView).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly), Is.Null);
            Assert.That(view.MagazineText.fontSize, Is.EqualTo(18f));

            view.SetHud(true, "Arrow", 0, 5, null);
            Assert.That(view.MagazineText.color, Is.EqualTo(Color.red));
            view.SetHud(true, "Arrow", 3, 5, null);
            Assert.That(view.MagazineText.color, Is.EqualTo(Color.white));

            var cameraPosition = view.MagazineCanvas.transform.position + Vector3.back * 5f + Vector3.up;
            view.FaceWorldHuds(cameraPosition, Vector3.up);
            var expectedForward = (view.MagazineCanvas.transform.position - cameraPosition).normalized;
            Assert.That(Vector3.Angle(view.MagazineCanvas.transform.forward, expectedForward), Is.LessThan(.01f));

            var baseScale = view.MagazineText.transform.localScale;
            view.PlayEmptyAmmoFeedback();
            view.TickEmptyAmmoFeedbackForTests(0f);
            Assert.That(view.MagazineText.transform.localScale, Is.EqualTo(baseScale));
            view.TickEmptyAmmoFeedbackForTests(.1f);
            Assert.That(view.MagazineText.transform.localScale, Is.EqualTo(baseScale * 1.2f));
            view.TickEmptyAmmoFeedbackForTests(.1f);
            Assert.That(view.MagazineText.transform.localScale, Is.EqualTo(baseScale));
            view.PlayEmptyAmmoFeedback();
            view.TickEmptyAmmoFeedbackForTests(.1f);
            view.PlayEmptyAmmoFeedback();
            Assert.That(view.MagazineText.transform.localScale, Is.EqualTo(baseScale));
            view.SetHud(false, "Arrow", 0, 5, null);
            Assert.That(view.MagazineText.transform.localScale, Is.EqualTo(baseScale));
            view.SetHud(true, "Arrow", 0, 5, null);
            view.PlayEmptyAmmoFeedback();
            instance.SetActive(false);
            Assert.That(view.MagazineText.transform.localScale, Is.EqualTo(baseScale));
        }

        [Test]
        public void DestroyHighlight_TintsOnlyBuildingRenderersAndRestoresExistingPropertyBlocks()
        {
            var root = new GameObject("TemporaryBuildingViewTestRoot");
            transientObjects.Add(root);
            root.SetActive(false);
            var view = root.AddComponent<TemporaryBuildingView>();
            view.ValidHighlight = new GameObject("ValidHighlight");
            view.ValidHighlight.transform.SetParent(root.transform, false);
            view.InvalidHighlight = new GameObject("InvalidHighlight");
            view.InvalidHighlight.transform.SetParent(root.transform, false);
            view.DestroyHighlight = new GameObject("DestroyHighlight");
            view.DestroyHighlight.transform.SetParent(root.transform, false);

            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Config/GrayboxMaterial.mat");
            Assert.That(material, Is.Not.Null);
            var materialBaseColor = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : default;
            var materialColor = material.HasProperty(ColorId) ? material.GetColor(ColorId) : default;
            var first = CreateRenderer("BodyA", root.transform, material);
            var second = CreateRenderer("BodyB", root.transform, material);
            var helper = CreateRenderer("LegacyDestroyVisual", view.DestroyHighlight.transform, material);
            var canvasRoot = new GameObject("WorldHud", typeof(RectTransform), typeof(Canvas));
            canvasRoot.transform.SetParent(root.transform, false);
            var hud = CreateRenderer("HudRenderer", canvasRoot.transform, material);

            var firstOriginal = new MaterialPropertyBlock();
            firstOriginal.SetColor(ColorId, new Color(.1f, .2f, .3f, .4f));
            firstOriginal.SetFloat(GlossinessId, .17f);
            first.SetPropertyBlock(firstOriginal);
            var secondOriginal = new MaterialPropertyBlock();
            secondOriginal.SetColor(ColorId, new Color(.6f, .5f, .4f, .3f));
            secondOriginal.SetFloat(GlossinessId, .83f);
            second.SetPropertyBlock(secondOriginal);
            var helperBefore = CapturePropertyBlock(helper);
            var hudBefore = CapturePropertyBlock(hud);

            root.SetActive(true);
            view.SetDestroyHighlight(true);
            AssertRendererTinted(first);
            AssertRendererTinted(second);
            AssertPropertyBlockEquals(helperBefore, helper, "旧拆除辅助节点不得染色");
            AssertPropertyBlockEquals(hudBefore, hud, "Canvas 下 Renderer 不得染色");
            if (material.HasProperty(BaseColorId)) Assert.That(material.GetColor(BaseColorId), Is.EqualTo(materialBaseColor));
            if (material.HasProperty(ColorId)) Assert.That(material.GetColor(ColorId), Is.EqualTo(materialColor));

            view.SetDestroyHighlight(false);
            AssertPropertyBlockEquals(firstOriginal, first, "第一本体 Renderer 必须恢复完整属性块");
            AssertPropertyBlockEquals(secondOriginal, second, "第二本体 Renderer 必须恢复完整属性块");
            view.SetDestroyHighlight(false);
            view.SetDestroyHighlight(true);
            AssertRendererTinted(first);
            AssertRendererTinted(second);
            root.SetActive(false);
            AssertPropertyBlockEquals(firstOriginal, first, "禁用 View 时第一本体 Renderer 必须恢复");
            AssertPropertyBlockEquals(secondOriginal, second, "禁用 View 时第二本体 Renderer 必须恢复");
        }

        [Test]
        public void ReleaseBuildingAndMap_RestoreDestroyTintBeforeProviderRelease()
        {
            InstallRealController();
            var firstId = BuildArrowTowerWithRealController(Vector3.right);
            var secondId = BuildArrowTowerWithRealController(Vector3.left * 4f);
            var firstView = FindManualAttackView(firstId).GetComponent<TemporaryBuildingView>();
            var secondView = FindManualAttackView(secondId).GetComponent<TemporaryBuildingView>();
            var expected = new Dictionary<GameObject, Dictionary<Renderer, MaterialPropertyBlock>>
            {
                [firstView.gameObject] = CaptureRendererBlocks(GetBuildingRenderers(firstView)),
                [secondView.gameObject] = CaptureRendererBlocks(GetBuildingRenderers(secondView))
            };
            var originalProvider = Game.Interface.GetUtility<IViewInstanceProvider>();
            var trackingProvider = new RestoreCheckingViewInstanceProvider(originalProvider, expected);
            Game.Interface.RegisterUtility<IViewInstanceProvider>(trackingProvider);

            realController.SetDestroyHighlight(firstId, true);
            realController.SetDestroyHighlight(secondId, true);
            realController.ReleaseBuilding(firstId);
            realController.ReleaseMap(TemporaryBuildingBehaviorTestSupport.MapInstanceId);

            Assert.That(trackingProvider.ReleaseCount, Is.EqualTo(2));
            Assert.That(trackingProvider.AllRestored, Is.True);
        }

        [Test]
        public void DestroyMode_AllowsMovementAndTracksPointerUntilCanceled()
        {
            // 同步正式输入门禁依赖，避免夹具在读取移动输入前空引用。
            Game.Interface.RegisterSystem(new MobileInputSystem());
            Game.Interface.RegisterSystem(new AppearanceGallerySceneFlowSystem());
            InstallRealController();
            var tower = BuildArrowTowerWithRealController(Vector3.right);
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.left * 4f);
            var secondTower = BuildArrowTowerWithRealController(Vector3.left * 4f);
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.zero);
            var system = Game.Interface.GetSystem<TemporaryBuildingSystem>();
            var towerView = FindManualAttackView(tower);
            var secondView = FindManualAttackView(secondTower);
            Assert.That(towerView, Is.Not.Null);
            Assert.That(secondView, Is.Not.Null);
            var towerBuildingView = towerView.GetComponent<TemporaryBuildingView>();
            var secondBuildingView = secondView.GetComponent<TemporaryBuildingView>();
            var towerRenderers = GetBuildingRenderers(towerBuildingView);
            var secondRenderers = GetBuildingRenderers(secondBuildingView);
            Assert.That(towerRenderers.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(secondRenderers.Count, Is.GreaterThanOrEqualTo(2));
            var towerBefore = CaptureRendererBlocks(towerRenderers);
            var secondBefore = CaptureRendererBlocks(secondRenderers);
            var magazineRenderers = towerView.MagazineCanvas.GetComponentsInChildren<Renderer>(true);
            var reloadRenderers = towerView.ReloadProgressRoot.GetComponentsInChildren<Renderer>(true);
            var magazineBefore = CaptureRendererBlocks(magazineRenderers);
            var reloadBefore = CaptureRendererBlocks(reloadRenderers);
            Assert.That(Vector3.Distance(towerView.transform.position, secondView.transform.position), Is.GreaterThan(.5f));
            var firstProbe = CreatePhysicsCollider("CommittedFirstBuildingProbe", towerView.transform.position + Vector3.up * .9f, new Vector3(.6f, 1.8f, .6f));
            firstProbe.GetComponent<Collider>().isTrigger = true;
            firstProbe.transform.SetParent(towerView.transform, true);
            var secondProbe = CreatePhysicsCollider("CommittedSecondBuildingProbe", secondView.transform.position + Vector3.up * .9f, new Vector3(.6f, 1.8f, .6f));
            secondProbe.transform.SetParent(secondView.transform, true);
            pointerRay = RayToBuilding(towerView);
            input.DestroyPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.DestroyMode));
            var error = (string)null;
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.Zero);

            // 玩家 Collider 在射线前方时必须被正式 Controller 识别并忽略，仍选中建筑。
            var playerColliderObject = CreatePhysicsCollider("PlayerCollider", towerView.transform.position + Vector3.up * 4f, Vector3.one);
            FakePlayerRuntimePortProvider.PlayerCollider = playerColliderObject.GetComponent<Collider>();
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(tower));
            AssertRenderersTinted(towerRenderers);

            // 非分配缓存满时结果可能截断，必须安全失败且不得猜选目标。
            var overflowOrigin = new Vector3(50f, 50f, 50f);
            for (var i = 0; i < 32; i++)
            {
                CreatePhysicsCollider("CommittedPickOverflow" + i, overflowOrigin + Vector3.forward * (i + 1), Vector3.one * .25f);
            }
            Physics.SyncTransforms();
            Assert.That(realController.TryPickCommittedBuilding(new Ray(overflowOrigin, Vector3.forward), 100f, out var overflowPick), Is.False);
            Assert.That(overflowPick, Is.Zero);
            AssertRendererBlocksEqual(magazineBefore, "MagazineCanvas 不得被拆除色覆盖");
            AssertRendererBlocksEqual(reloadBefore, "ReloadProgressRoot 不得被拆除色覆盖");

            // 当前目标仍存在时通过正式取消输入退出，必须立即恢复本体颜色。
            input.CancelPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.Zero);
            AssertRendererBlocksEqual(towerBefore, "取消拆除模式时仍存在的当前建筑必须恢复");

            // 再次通过正式拆除输入进入，并由鼠标重新选择同一建筑。
            input.DestroyPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.DestroyMode));
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.Zero);
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(tower));
            AssertRenderersTinted(towerRenderers);

            input.MoveInput = Vector2.right;
            input.SprintHeld = true;
            PumpInput();
            Assert.That(Game.Interface.GetSystem<PlayerInputSystem>().MoveInput, Is.EqualTo(Vector2.right));
            Assert.That(Game.Interface.GetSystem<PlayerInputSystem>().SprintHeld, Is.True);
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(tower));

            // 指针候选切换必须同步旧/新高亮。
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.left * 2f);
            pointerRay = RayToBuilding(secondView);
            Assert.That(realController.TryPickCommittedBuilding(pointerRay, 1000f, out var pickedSecond), Is.True, "第二建筑射线应命中正式 Controller");
            Assert.That(pickedSecond, Is.EqualTo(secondTower));
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(secondTower));
            AssertRendererBlocksEqual(towerBefore, "切换目标后第一座建筑必须恢复");
            AssertRenderersTinted(secondRenderers);

            // 场景、非玩家、预览或无效对象的更近碰撞都必须阻挡后方建筑。
            AssertBlockedByCollider(secondView.transform.position, "SceneCollider");
            AssertBlockedByCollider(secondView.transform.position, "PreviewCollider");
            AssertBlockedByCollider(secondView.transform.position, "InvalidCollider");
            AssertRendererBlocksEqual(secondBefore, "被遮挡时当前建筑必须恢复");
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.zero);
            pointerRay = RayToBuilding(towerView);
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.EqualTo(tower));
            AssertRenderersTinted(towerRenderers);

            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.up * 0f + Vector3.left * 100f);
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.Zero, "超距候选必须清除");
            AssertRendererBlocksEqual(towerBefore, "超距时当前建筑必须恢复");

            // 无目标 Attack 无删除副作用。
            var buildingCountBeforeEmptyAttack = Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.Count;
            pointerRay = new Ray(Vector3.up * 5f + Vector3.left * 100f, Vector3.down);
            input.AttackPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.Count, Is.EqualTo(buildingCountBeforeEmptyAttack));

            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.zero);
            pointerRay = RayToBuilding(towerView);
            PumpInput();
            input.AttackPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(tower), Is.False);
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.DestroyMode));
            TemporaryBuildingBehaviorTestSupport.MovePlayer(Vector3.left * 4f);
            pointerRay = RayToBuilding(secondView);
            PumpInput();
            input.AttackPressedThisFrame = true;
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.ContainsKey(secondTower), Is.False);
            input.CancelPressedThisFrame = true;
            PumpInput();
            Assert.That(system.Mode, Is.EqualTo(TemporaryBuildingMode.Normal));
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly Color DestroyTint = new Color32(0x71, 0x3B, 0x3B, 0xFF);

        private static Renderer CreateRenderer(string name, Transform parent, Material material)
        {
            var child = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(parent, false);
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private static List<Renderer> GetBuildingRenderers(TemporaryBuildingView view)
        {
            var result = new List<Renderer>();
            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.GetComponentInParent<Canvas>(true) != null) continue;
                if (IsDescendantOf(renderer.transform, view.ValidHighlight) ||
                    IsDescendantOf(renderer.transform, view.InvalidHighlight) ||
                    IsDescendantOf(renderer.transform, view.DestroyHighlight)) continue;
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != null && (materials[i].HasProperty(BaseColorId) || materials[i].HasProperty(ColorId)))
                    {
                        result.Add(renderer);
                        break;
                    }
                }
            }
            return result;
        }

        private static bool IsDescendantOf(Transform transform, GameObject possibleAncestor)
        {
            return possibleAncestor != null && (transform == possibleAncestor.transform || transform.IsChildOf(possibleAncestor.transform));
        }

        private static MaterialPropertyBlock CapturePropertyBlock(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block;
        }

        private static Dictionary<Renderer, MaterialPropertyBlock> CaptureRendererBlocks(IEnumerable<Renderer> renderers)
        {
            var result = new Dictionary<Renderer, MaterialPropertyBlock>();
            foreach (var renderer in renderers) result[renderer] = CapturePropertyBlock(renderer);
            return result;
        }

        private static void AssertRendererTinted(Renderer renderer)
        {
            var block = CapturePropertyBlock(renderer);
            var supportsColor = false;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (material.HasProperty(BaseColorId))
                {
                    supportsColor = true;
                    AssertColorEquals(DestroyTint, block.GetColor(BaseColorId), renderer.name + " _BaseColor");
                }
                if (material.HasProperty(ColorId))
                {
                    supportsColor = true;
                    AssertColorEquals(DestroyTint, block.GetColor(ColorId), renderer.name + " _Color");
                }
            }
            Assert.That(supportsColor, Is.True, renderer.name + " 应有可覆盖颜色属性");
        }

        private static void AssertColorEquals(Color expected, Color actual, string message)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(.0001f), message + " r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(.0001f), message + " g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(.0001f), message + " b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(.0001f), message + " a");
        }

        private static void AssertRenderersTinted(IEnumerable<Renderer> renderers)
        {
            foreach (var renderer in renderers) AssertRendererTinted(renderer);
        }

        private static void AssertRendererBlocksEqual(Dictionary<Renderer, MaterialPropertyBlock> expected, string message)
        {
            foreach (var pair in expected) AssertPropertyBlockEquals(pair.Value, pair.Key, message + ": " + pair.Key.name);
        }

        private static void AssertPropertyBlockEquals(MaterialPropertyBlock expected, Renderer renderer, string message)
        {
            var actual = CapturePropertyBlock(renderer);
            Assert.That(actual.isEmpty, Is.EqualTo(expected.isEmpty), message + " empty");
            Assert.That(actual.GetColor(BaseColorId), Is.EqualTo(expected.GetColor(BaseColorId)), message + " _BaseColor");
            Assert.That(actual.GetColor(ColorId), Is.EqualTo(expected.GetColor(ColorId)), message + " _Color");
            Assert.That(actual.GetFloat(GlossinessId), Is.EqualTo(expected.GetFloat(GlossinessId)), message + " _Glossiness");
        }

        private sealed class RestoreCheckingViewInstanceProvider : IViewInstanceProvider
        {
            private readonly IViewInstanceProvider inner;
            private readonly Dictionary<GameObject, Dictionary<Renderer, MaterialPropertyBlock>> expected;

            public RestoreCheckingViewInstanceProvider(IViewInstanceProvider inner, Dictionary<GameObject, Dictionary<Renderer, MaterialPropertyBlock>> expected)
            {
                this.inner = inner;
                this.expected = expected;
            }

            public int ReleaseCount { get; private set; }
            public bool AllRestored { get; private set; } = true;

            public T Instantiate<T>(T prefab, Transform parent, Vector3 position, Quaternion rotation) where T : Component
            {
                return inner.Instantiate(prefab, parent, position, rotation);
            }

            public void Release(GameObject instance)
            {
                ReleaseCount++;
                if (!expected.TryGetValue(instance, out var rendererBlocks))
                {
                    AllRestored = false;
                }
                else
                {
                    foreach (var pair in rendererBlocks)
                    {
                        var actual = CapturePropertyBlock(pair.Key);
                        if (actual.isEmpty != pair.Value.isEmpty ||
                            actual.GetColor(BaseColorId) != pair.Value.GetColor(BaseColorId) ||
                            actual.GetColor(ColorId) != pair.Value.GetColor(ColorId) ||
                            !Mathf.Approximately(actual.GetFloat(GlossinessId), pair.Value.GetFloat(GlossinessId)))
                        {
                            AllRestored = false;
                        }
                    }
                }
                inner.Release(instance);
            }
        }

        private GameObject CreatePhysicsCollider(string name, Vector3 position, Vector3 size)
        {
            var blocker = new GameObject(name);
            blocker.transform.position = position;
            var collider = blocker.AddComponent<BoxCollider>();
            collider.size = size;
            transientObjects.Add(blocker);
            return blocker;
        }

        private void AssertBlockedByCollider(Vector3 buildingPosition, string name)
        {
            var blocker = CreatePhysicsCollider(name, buildingPosition + Vector3.up * 4f, Vector3.one);
            pointerRay = new Ray(buildingPosition + Vector3.up * 6f, Vector3.down);
            PumpInput();
            Assert.That(Game.Interface.GetModel<TemporaryBuildingModel>().DestroyTargetBuildingId, Is.Zero, name + " 应阻挡后方建筑");
            UnityEngine.Object.DestroyImmediate(blocker);
            transientObjects.Remove(blocker);
        }

        private static Ray RayToBuilding(TemporaryManualAttackView view)
        {
            var center = view.transform.position;
            return new Ray(center + Vector3.up * 10f, Vector3.down);
        }

        private void PumpInput()
        {
            var update = typeof(PlayerInputSystem).GetMethod("OnUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            update.Invoke(Game.Interface.GetSystem<PlayerInputSystem>(), null);
            input.ClearPressed();
        }

        private void SetPointerGroundTarget(Vector3 target)
        {
            pointerGround = target;
            pointerRay = new Ray(target + Vector3.up * 10f, Vector3.down);
        }

        private static string FindVisibleFloatText()
        {
            var boxes = UnityEngine.Object.FindObjectsOfType<FloatTextBoxUI>(true);
            for (var i = boxes.Length - 1; i >= 0; i--)
            {
                if (boxes[i] != null && boxes[i].gameObject.activeInHierarchy && boxes[i].TextTips != null)
                {
                    return boxes[i].TextTips.text;
                }
            }

            return string.Empty;
        }

        private static string FindVisibleFloatTextContaining(string value)
        {
            foreach (var box in UnityEngine.Object.FindObjectsOfType<FloatTextBoxUI>(true))
            {
                if (box != null && box.gameObject.activeInHierarchy && box.TextTips != null && box.TextTips.text.Contains(value))
                {
                    return box.TextTips.text;
                }
            }

            return string.Empty;
        }

        private static int CountVisibleFloatTexts()
        {
            var count = 0;
            foreach (var box in UnityEngine.Object.FindObjectsOfType<FloatTextBoxUI>(true))
            {
                if (box != null && box.gameObject.activeInHierarchy && box.TextTips != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = new GameObject("TemporaryBuildingFloatPrefab", typeof(RectTransform), typeof(FloatUI));
            var tipsRoot = new GameObject("TipsRoot", typeof(RectTransform));
            tipsRoot.transform.SetParent(root.transform, false);
            var boxRoot = new GameObject("TipTemplate", typeof(RectTransform), typeof(FloatTextBoxUI));
            boxRoot.transform.SetParent(tipsRoot.transform, false);
            var box = boxRoot.GetComponent<FloatTextBoxUI>();
            var textObject = new GameObject("TextTips", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(boxRoot.transform, false);
            box.TextTips = textObject.GetComponent<TextMeshProUGUI>();
            boxRoot.SetActive(false);
            var ui = root.GetComponent<FloatUI>();
            ui.TipsRoot = tipsRoot.GetComponent<RectTransform>();
            ui.TipTemplate = box;
            return root;
        }

        private void AssertPlayerHeadProgressHiddenForReload(PlayerActionSnapshot reloadSnapshot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Characters/GrayboxPlayer.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(prefab);
            transientObjects.Add(instance);
            var playerView = instance.GetComponent<PlayerView>();
            Assert.That(playerView, Is.Not.Null);
            Assert.That(playerView.Validate(out var viewError), Is.True, viewError);
            var registry = new ViewCtrlRegistry();
            var viewCtrl = new PlayerViewCtrl(playerView, PlayerModel.LocalPlayerUnitId, TemporaryBuildingBehaviorTestSupport.MapInstanceId, registry, new UnityViewInstanceProvider());
            var playerCtrl = new PlayerCtrl((Game)Game.Interface);
            try
            {
                SetPrivateField(playerCtrl, "activePlayer", viewCtrl);
                Game.Interface.SendEvent(new PlayerActionChangedEvent(true, reloadSnapshot));
                var actionView = typeof(PlayerView).GetField("actionProgressView", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(playerView) as UnitActionProgressView;
                var root = actionView == null ? null : typeof(UnitActionProgressView).GetField("root", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(actionView) as GameObject;
                Assert.That(root == null || !root.activeSelf, Is.True, "TemporaryBuildingReload 不得显示玩家头顶进度");
            }
            finally
            {
                SetPrivateField(playerCtrl, "activePlayer", null);
                viewCtrl.Dispose();
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }

            Assert.Fail("missing private field: " + fieldName);
        }

        private void InstallRealController()
        {
            realMapHandle = TemporaryBuildingBehaviorTestSupport.CreateMapHandle(MapIds.SurvivalSample, TemporaryBuildingBehaviorTestSupport.MapInstanceId);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, realMapHandle);
            realController = new TemporaryBuildingCtrl((Game)Game.Interface);
            Game.Interface.RegisterUtility<ITemporaryBuildingViewPort>(realController);
        }

        private TemporaryManualAttackView FindManualAttackView(int runtimeId)
        {
            var views = UnityEngine.Object.FindObjectsOfType<TemporaryManualAttackView>(true);
            if (!Game.Interface.GetModel<TemporaryBuildingModel>().Buildings.TryGetValue(runtimeId, out var state))
            {
                return null;
            }
            for (var i = 0; i < views.Length; i++)
            {
                if (views[i] != null && Vector3.Distance(views[i].transform.position, state.Position) < .01f)
                {
                    return views[i];
                }
            }

            return null;
        }

        private int BuildArrowTowerWithRealController(Vector3 position)
        {
            var definition = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(definition.TryGetTempBuilding("temp_arrow_tower", out var buildingDefinition, out var definitionError), Is.True, definitionError);
            var runtimeId = Game.Interface.GetModel<TemporaryBuildingModel>().AllocateRuntimeId();
            var state = new TemporaryBuildingRuntimeState(runtimeId, buildingDefinition.Id, TemporaryBuildingBehaviorTestSupport.MapInstanceId, position, Quaternion.identity);
            Assert.That(realController.TryCreateBuilding(state, buildingDefinition, true, out var error), Is.True, error);
            Game.Interface.GetModel<TemporaryBuildingModel>().AddBuilding(state, new TemporaryManualAttackRuntimeState(0f));
            return runtimeId;
        }

        private static int BuildArrowTower(Vector3 position)
        {
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.MetalParts, 2);
            TemporaryBuildingBehaviorTestSupport.AddBagItem(ItemIds.Wood, 3);
            return TemporaryBuildingBehaviorTestSupport.Build("temp_arrow_tower", position);
        }
    }
}

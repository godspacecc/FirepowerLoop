using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class MobileInputAndScreenAdaptationTests
    {
        private const string OfficialStickPrefabPath =
            "Assets/Samples/Input System/1.7.0/On-Screen Controls/Stick.prefab";

        [TearDown]
        public void TearDown()
        {
            LifeCycle.ClearForTests();
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
        }

        [Test]
        public void HiddenJoystickKeepsVirtualDeviceAndClearsMovementBeforeShowingAgain()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab");
            var root = Object.Instantiate(prefab);
            root.SetActive(true);
            try
            {
                var ui = root.GetComponent<MobileJoystickUI>();
                MobileJoystickPresentation.SetContentVisible(ui, true);
                // EditMode 不自动执行 OnScreenControl 的运行时 OnEnable，显式初始化正式设备接口。
                if (ui.JoystickSurface.control == null)
                    typeof(OnScreenControl).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(ui.JoystickSurface, null);
                var device = ui.JoystickSurface.control.device;
                using var move = new InputAction(binding: "<Gamepad>/leftStick");
                move.Enable();
                UnityEngine.InputSystem.LowLevel.InputState.Change(((Gamepad)device).leftStick, new Vector2(0.5f, 0.25f));
                InputSystem.Update();
                MobileJoystickPresentation.SetContentVisible(ui, false);
                InputSystem.Update();
                Assert.That(ui.ContentRoot.activeSelf, Is.True);
                Assert.That(ui.JoystickSurface.control.device, Is.SameAs(device));
                Assert.That(((Gamepad)device).leftStick.ReadValue(), Is.EqualTo(Vector2.zero));
                var group = ui.ContentRoot.GetComponent<CanvasGroup>();
                Assert.That(group.alpha, Is.Zero);
                Assert.That(group.blocksRaycasts, Is.False);
                Assert.That(group.interactable, Is.False);
                MobileJoystickPresentation.SetContentVisible(ui, true);
                Assert.That(ui.JoystickSurface.control.device, Is.SameAs(device));
                Assert.That(group.alpha, Is.EqualTo(1));
                Assert.That(group.blocksRaycasts, Is.True);
            }
            finally
            {
                var stick = root.GetComponent<MobileJoystickUI>().JoystickSurface;
                if (stick.control != null)
                    typeof(OnScreenControl).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stick, null);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PlayerSettings_AllowOnlyBothLandscapeOrientations()
        {
            Assert.That(PlayerSettings.defaultInterfaceOrientation, Is.EqualTo(UIOrientation.AutoRotation));
            Assert.That(PlayerSettings.allowedAutorotateToLandscapeLeft, Is.True);
            Assert.That(PlayerSettings.allowedAutorotateToLandscapeRight, Is.True);
            Assert.That(PlayerSettings.allowedAutorotateToPortrait, Is.False);
            Assert.That(PlayerSettings.allowedAutorotateToPortraitUpsideDown, Is.False);
        }

        [Test]
        public void SafeAreaAnchors_MapPhysicalInsetsWithoutChangingReferenceResolution()
        {
            SafeAreaFitter.CalculateAnchors(
                new Rect(100f, 20f, 2200f, 1040f),
                new Vector2(2400f, 1080f),
                out var min,
                out var max);

            Assert.That(min.x, Is.EqualTo(100f / 2400f).Within(0.0001f));
            Assert.That(min.y, Is.EqualTo(20f / 1080f).Within(0.0001f));
            Assert.That(max.x, Is.EqualTo(2300f / 2400f).Within(0.0001f));
            Assert.That(max.y, Is.EqualTo(1060f / 1080f).Within(0.0001f));

            SafeAreaFitter.MapAnchorsIntoSafeArea(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                min,
                max,
                out var centeredMin,
                out var centeredMax);
            Assert.That(centeredMin, Is.EqualTo(centeredMax));
            Assert.That(centeredMin.x, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(centeredMin.y, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void SafeAreaFitter_FullScreenUiPreservesCoverageAndInsetsOnlyContent()
        {
            var root = new GameObject("FullScreenUi", typeof(RectTransform), typeof(Image));
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            var content = new GameObject("ContentRoot", typeof(RectTransform));
            try
            {
                var rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.anchoredPosition = Vector2.zero;
                rootRect.sizeDelta = Vector2.zero;
                ConfigureFullScreenChild(backdrop.transform as RectTransform, rootRect);
                ConfigureFullScreenChild(content.transform as RectTransform, rootRect);

                var fitter = root.AddComponent<SafeAreaFitter>();
                var safeMin = new Vector2(0.05f, 0.02f);
                var safeMax = new Vector2(0.95f, 0.98f);
                fitter.ApplyAnchors(safeMin, safeMax);

                Assert.That(rootRect.anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(rootRect.anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(((RectTransform)backdrop.transform).anchorMin, Is.EqualTo(Vector2.zero));
                Assert.That(((RectTransform)backdrop.transform).anchorMax, Is.EqualTo(Vector2.one));
                Assert.That(((RectTransform)content.transform).anchorMin, Is.EqualTo(safeMin));
                Assert.That(((RectTransform)content.transform).anchorMax, Is.EqualTo(safeMax));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AndroidDisplayPolicy_SkipsPcResolutionRowsAndScreenSetResolution()
        {
            Assert.That(SettingUICtrl.ShouldShowDesktopDisplaySettings(RuntimePlatform.Android), Is.False);
            Assert.That(UnityDisplayResolutionApplier.ShouldApplyForPlatform(RuntimePlatform.Android), Is.False);
            Assert.That(SettingUICtrl.ShouldShowDesktopDisplaySettings(RuntimePlatform.WindowsPlayer), Is.True);
            Assert.That(UnityDisplayResolutionApplier.ShouldApplyForPlatform(RuntimePlatform.WindowsPlayer), Is.True);
        }

        [Test]
        public void AndroidRenderScale_IsPlatformScopedAndStartsAtPointEightFive()
        {
            Assert.That(MobileRenderScaleRuntime.AndroidInitialRenderScale, Is.EqualTo(0.85f));
            Assert.That(MobileRenderScaleRuntime.ShouldApply(RuntimePlatform.Android), Is.True);
            Assert.That(MobileRenderScaleRuntime.ShouldApply(RuntimePlatform.WindowsPlayer), Is.False);
            Assert.That(MobileRenderScaleRuntime.ShouldApply(RuntimePlatform.WindowsEditor), Is.False);
        }

        [Test]
        public void JoystickPrefab_HasGeneratedViewValidReferencesAndRaycastSurface()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab");

            Assert.That(prefab, Is.Not.Null);
            var collector = prefab.GetComponent<ReferenceCollector>();
            var ui = prefab.GetComponent<MobileJoystickUI>();
            Assert.That(collector, Is.Not.Null);
            Assert.That(ui, Is.Not.Null);
            Assert.That(ui.ContentRoot, Is.Not.Null);
            Assert.That(ui.JoystickSurface, Is.Not.Null);
            Assert.That(ui.InteractionButton, Is.Not.Null);
            var inventoryField = typeof(MobileJoystickUI).GetField("InventoryButton");
            Assert.That(inventoryField, Is.Not.Null, "手机 HUD 必须提供背包触屏入口");
            var inventoryButton = inventoryField.GetValue(ui) as Button;
            Assert.That(inventoryButton, Is.Not.Null);
            Assert.That(inventoryButton.transform.IsChildOf(ui.ContentRoot.transform), Is.True);
            Assert.That(inventoryButton.GetComponent<Image>().raycastTarget, Is.True);
            var inventoryRect = inventoryButton.GetComponent<RectTransform>();
            Assert.That(inventoryRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(inventoryRect.anchorMax, Is.EqualTo(Vector2.zero));
            Assert.That(inventoryRect.sizeDelta, Is.EqualTo(new Vector2(80, 80)));
            Assert.That(inventoryButton.GetComponentsInChildren<TMPro.TMP_Text>(true), Is.Empty);
            Assert.That(inventoryButton.transform.Find("Icon").GetComponent<Image>().raycastTarget, Is.False);
            var buildRect = ui.BuildButton.GetComponent<RectTransform>();
            Assert.That(buildRect.anchoredPosition.y, Is.EqualTo(inventoryRect.anchoredPosition.y));
            Assert.That(buildRect.anchoredPosition.x - inventoryRect.anchoredPosition.x, Is.EqualTo(96f));
            var guide = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/GameplayGuideHud.prefab");
            var pauseRect = guide.transform.Find("MobileBtnPause").GetComponent<RectTransform>();
            Assert.That(pauseRect.anchorMin, Is.EqualTo(Vector2.one));
            Assert.That(pauseRect.sizeDelta, Is.EqualTo(new Vector2(80, 80)));
            Assert.That(collector.data.Select(item => item.key),
                Is.EquivalentTo(new[] { "ContentRoot", "ContentVisibility", "InteractionButton", "JoystickSurface", "AttackButton", "ReticleRoot", "InventoryButton", "MoveJoystickRoot", "MoveOpacity", "JoystickLayoutArea", "BuildButton" }));
            var surfaceObject = ui.JoystickSurface.gameObject;
            var image = surfaceObject.GetComponent<Image>();
            Assert.That(image, Is.Not.Null);
            Assert.That(image.sprite, Is.Not.Null);
            Assert.That(image.sprite.name, Is.EqualTo("Knob"));
            Assert.That(image.raycastTarget, Is.True);
            Assert.That(surfaceObject.transform.IsChildOf(ui.ContentRoot.transform), Is.True);
            Assert.That(ui.JoystickSurface, Is.TypeOf<OnScreenStick>());
            Assert.That(ui.JoystickSurface.controlPath, Is.EqualTo("<Gamepad>/leftStick"));
            Assert.That(ui.JoystickSurface.behaviour,
                Is.EqualTo(OnScreenStick.Behaviour.RelativePositionWithStaticOrigin));
            Assert.That(((RectTransform)surfaceObject.transform).sizeDelta,
                Is.EqualTo(new Vector2(180f, 180f)),
                "通用移动摇杆应提供与街机一致的移动端拇指触控区域");
            Assert.That(ui.JoystickSurface.movementRange, Is.EqualTo(75f));
            var source = PrefabUtility.GetCorrespondingObjectFromSource(surfaceObject);
            Assert.That(AssetDatabase.GetAssetPath(source), Is.EqualTo(OfficialStickPrefabPath));
            Assert.That(surfaceObject.transform.Find("Text"), Is.Null, "摇杆不保留英文占位文字");

            var interactionButton = ui.InteractionButton;
            Assert.That(interactionButton.transform.IsChildOf(ui.ContentRoot.transform), Is.True);
            Assert.That(interactionButton.gameObject.activeSelf, Is.False);
            var interactionRect = interactionButton.GetComponent<RectTransform>();
            Assert.That(interactionRect.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(interactionRect.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(interactionRect.anchoredPosition.x, Is.LessThan(0f));
            Assert.That(interactionRect.anchoredPosition.y, Is.GreaterThan(0f));
            Assert.That(interactionButton.GetComponentsInChildren<TMPro.TMP_Text>(true), Is.Empty);
            var interactionIcon = interactionButton.transform.Find("InteractionIcon").GetComponent<Image>();
            Assert.That(interactionIcon.sprite, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(interactionIcon.sprite),
                Is.EqualTo("Assets/Game/Art/UI/UtilityButtons/Interact.png"));
            Assert.That(interactionIcon.raycastTarget, Is.False, "装饰图标不抢占按钮点击");
            Assert.That(interactionIcon.preserveAspect, Is.True);
            Assert.That(interactionRect.sizeDelta, Is.EqualTo(new Vector2(120, 120)));
            Assert.That(interactionButton.GetComponent<Image>().color,
                Is.EqualTo(new Color(.68f, .22f, .13f, .82f)));
        }

        [Test]
        public void JoystickNormalization_PreservesSmallInputAndCapsDiagonalMagnitude()
        {
            Assert.That(PlayerInputCtrl.Normalize(new Vector2(0.25f, 0.5f)),
                Is.EqualTo(new Vector2(0.25f, 0.5f)));
            Assert.That(PlayerInputCtrl.Normalize(new Vector2(1f, 1f)).magnitude,
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void PlayerInput_UsesGamepadAxesSharedByPhysicalAndOnScreenSticks()
        {
            using var input = new PlayerInputCtrl();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var move = (InputAction)typeof(PlayerInputCtrl).GetField("moveAction", flags)?.GetValue(input);
            var aim = (InputAction)typeof(PlayerInputCtrl).GetField("aimAction", flags)?.GetValue(input);
            var attack = (InputAction)typeof(PlayerInputCtrl).GetField("attackAction", flags)?.GetValue(input);
            var pointer = (InputAction)typeof(PlayerInputCtrl).GetField("pointerPositionAction", flags)?.GetValue(input);

            Assert.That(move, Is.Not.Null);
            Assert.That(aim, Is.Not.Null);
            Assert.That(attack, Is.Not.Null);
            Assert.That(pointer, Is.Not.Null);
            Assert.That(move.bindings.Any(binding => binding.path == "<Gamepad>/leftStick"), Is.True);
            Assert.That(aim.bindings.Any(binding => binding.path == "<Gamepad>/rightStick"), Is.True);
            Assert.That(attack.bindings.Any(binding => binding.path == "<Mouse>/leftButton"), Is.True);
            Assert.That(attack.bindings.Any(binding => binding.path == "<Touchscreen>/touch*/press"), Is.False);
            Assert.That(pointer.bindings.Any(binding => binding.path == "<Pointer>/position"), Is.True);
        }

        [Test]
        public void OnlineArcadeInput_UsesRightStickMouseAndPhysicalTriggerWithoutTouchFire()
        {
            using var input = new OnlineArcadeInputController();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var aim = (InputAction)typeof(OnlineArcadeInputController).GetField("aimAction", flags)?.GetValue(input);
            var fire = (InputAction)typeof(OnlineArcadeInputController).GetField("fireAction", flags)?.GetValue(input);
            var trigger = (InputAction)typeof(OnlineArcadeInputController).GetField("triggerAction", flags)?.GetValue(input);

            Assert.That(aim, Is.Not.Null);
            Assert.That(fire, Is.Not.Null);
            Assert.That(trigger, Is.Not.Null);
            Assert.That(aim.bindings.Any(binding => binding.path == "<Gamepad>/rightStick"), Is.True);
            Assert.That(fire.bindings.Any(binding => binding.path == "<Mouse>/leftButton"), Is.True);
            Assert.That(fire.bindings.Any(binding => binding.path == "<Touchscreen>/touch*/press"), Is.False);
            Assert.That(trigger.bindings.Any(binding => binding.path == "<Gamepad>/rightTrigger"), Is.True);
        }

        [Test]
        public void ApplicationFocusLifecycle_NotifiesLossAndStopsAfterUnregister()
        {
            var calls = 0;
            System.Action<bool> callback = focused =>
            {
                if (!focused)
                {
                    calls++;
                }
            };

            LifeCycle.RegisterApplicationFocus(callback);
            LifeCycle.ApplicationFocus(false);
            LifeCycle.UnregisterApplicationFocus(callback);
            LifeCycle.ApplicationFocus(false);

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void MobileInputCommand_TogglesOnlyCurrentEditorSessionAndOffClearsMode()
        {
            var architecture = new Game();
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, architecture);
            typeof(Architecture<Game>)
                .GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(architecture, true);

            architecture.RegisterModel(new TestCmdModel());
            architecture.RegisterModel(new TestToolSettingsModel());
            architecture.RegisterSystem(new SettingsSystem());
            architecture.RegisterSystem(new MobileInputSystem());
            architecture.RegisterSystem(new TestCmdSystem());

            var commandSystem = architecture.GetSystem<TestCmdSystem>();
            var mobileInput = architecture.GetSystem<MobileInputSystem>();
            var on = commandSystem.ExecuteAsync("#mobileinput on", CancellationToken.None)
                .GetAwaiter().GetResult();
            var off = commandSystem.ExecuteAsync("#mobileinput off", CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.That(on.Success, Is.True);
            Assert.That(on.Code, Is.EqualTo(TestCmdResultCode.MobileInputModeChanged));
            Assert.That(off.Success, Is.True);
            Assert.That(off.Code, Is.EqualTo(TestCmdResultCode.MobileInputModeChanged));
            Assert.That(mobileInput.IsMobileInputMode, Is.False);
        }

        private static void ConfigureFullScreenChild(RectTransform child, RectTransform parent)
        {
            child.SetParent(parent, false);
            child.anchorMin = Vector2.zero;
            child.anchorMax = Vector2.one;
            child.anchoredPosition = Vector2.zero;
            child.sizeDelta = Vector2.zero;
        }
    }
}

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
        public void ArcadeJoystickPrefab_HasGeneratedViewValidReferencesAndRaycastSurfaces()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Arcade/ArcadeHud.prefab");
            Assert.That(prefab, Is.Not.Null);
            var collector = prefab.GetComponent<ReferenceCollector>();
            var ui = prefab.GetComponent<ArcadeHudUI>();
            Assert.That(collector, Is.Not.Null); Assert.That(ui, Is.Not.Null);
            foreach (var item in collector.data)
            {
                var field = typeof(ArcadeHudUI).GetField(item.key);
                Assert.That(field, Is.Not.Null, item.key);
                Assert.That(field.GetValue(ui) as UnityEngine.Object, Is.Not.Null, item.key);
            }
            var sticks = new[] { ui.MoveJoystick, ui.AimJoystick };
            var wrappers = new[] { ui.MoveJoystickRoot, ui.AimJoystickRoot };
            var paths = new[] { "<Gamepad>/leftStick", "<Gamepad>/rightStick" };
            for (var i = 0; i < sticks.Length; i++)
            {
                var stick = sticks[i];
                Assert.That(stick.transform.parent, Is.EqualTo(wrappers[i].transform));
                Assert.That(wrappers[i].transform.IsChildOf(ui.JoystickLayoutArea), Is.True);
                Assert.That(stick.controlPath, Is.EqualTo(paths[i]));
                Assert.That(stick.behaviour, Is.EqualTo(OnScreenStick.Behaviour.RelativePositionWithStaticOrigin));
                Assert.That(stick.movementRange, Is.EqualTo(75));
                Assert.That(((RectTransform)stick.transform).rect.size, Is.EqualTo(new Vector2(180, 180)));
                Assert.That(stick.GetComponent<Image>().raycastTarget, Is.True);
                foreach (var label in stick.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    Assert.That(label.raycastTarget, Is.False, "摇杆提示文字不能截获拖动触点");
                    Assert.That(label.GetComponent<LocalizedText>()?.Key,
                        Is.EqualTo(i == 0 ? "ui.arcade.hud.move_stick" : "ui.arcade.hud.aim_stick"));
                }
            }
            foreach (var button in new[] { ui.BtnPause, ui.BtnBomb, ui.BtnDash, ui.BtnFieldUpgrade, ui.BtnDonateLife, ui.BtnExitFirstPerson })
                Assert.That(button.image.raycastTarget, Is.True, button.name);
            Assert.That(ui.FirstPersonFirePress.GetComponent<Image>().raycastTarget, Is.True);
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

using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor;

namespace Game.Tests
{
    public sealed class MobileShoulderTouchTests
    {
        private Touchscreen screen;
        private MobileLookInputCtrl reader;
        private bool uiBlocked;

        [SetUp]
        public void SetUp()
        {
            screen = InputSystem.AddDevice<Touchscreen>();
            reader = new MobileLookInputCtrl(_ => uiBlocked);
        }

        [TearDown]
        public void TearDown()
        {
            reader?.Dispose();
            if (screen != null) InputSystem.RemoveDevice(screen);
        }

        [Test]
        public void RightFingerDrag_EmitsResolutionNormalizedDelta()
        {
            Touch(1, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .75f, Screen.height * .5f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
            Touch(1, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .75f + 40f, Screen.height * .5f - 20f));
            var delta = Read(true);
            Assert.That(delta.x, Is.EqualTo(40f * 1080f / Screen.height).Within(.001f));
            Assert.That(delta.y, Is.EqualTo(-20f * 1080f / Screen.height).Within(.001f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero), "同一位置不能重复消费增量");
        }

        [Test]
        public void LeftFingerAndOtherRightFinger_DoNotStealCapturedLook()
        {
            Touch(1, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .2f, 100f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
            Touch(2, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .7f, 100f));
            Read(true);
            Touch(3, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .8f, 100f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
            Touch(2, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .7f + 25f, 100f));
            Assert.That(Read(true).x, Is.EqualTo(25f * 1080f / Screen.height).Within(.001f));
        }

        [Test]
        public void TouchStartingOnUi_CannotBecomeLookAfterLeavingUi()
        {
            uiBlocked = true;
            Touch(1, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .7f, 100f));
            Read(true);
            uiBlocked = false;
            Touch(1, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .8f, 100f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void GateClosure_DropsFingerUntilANewPress()
        {
            Touch(1, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .7f, 100f));
            Read(true);
            Read(false);
            Touch(1, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .8f, 100f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void FocusLoss_DropsFingerUntilANewPress()
        {
            Touch(1, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .7f, 100f));
            Read(true);
            LifeCycle.ApplicationFocus(false);
            Touch(1, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .8f, 100f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void DragEnteringUi_CancelsUntilNewTouch()
        {
            Touch(1, UnityEngine.InputSystem.TouchPhase.Began, new Vector2(Screen.width * .7f, 100f));
            Read(true);
            uiBlocked = true;
            Touch(1, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .8f, 100f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
            uiBlocked = false;
            Touch(1, UnityEngine.InputSystem.TouchPhase.Moved, new Vector2(Screen.width * .9f, 100f));
            Assert.That(Read(true), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void EditorMouseDrag_UsesSameCaptureAndDeltaRules()
        {
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                var start = new Vector2(Screen.width * .7f, 100f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = start, buttons = 1 });
                InputSystem.Update();
                Assert.That(Read(true), Is.EqualTo(Vector2.zero));
                InputSystem.QueueStateEvent(mouse, new MouseState { position = start + new Vector2(25f, 10f), buttons = 1 });
                InputSystem.Update();
                var delta = Read(true);
                Assert.That(delta.x, Is.EqualTo(25f * 1080f / Screen.height).Within(.001f));
                Assert.That(delta.y, Is.EqualTo(10f * 1080f / Screen.height).Within(.001f));
                Read(false);
                Assert.That(Read(true), Is.EqualTo(Vector2.zero));
            }
            finally { InputSystem.RemoveDevice(mouse); }
        }

        private Vector2 Read(bool enabled) => reader.ReadDelta(enabled);
        private void Touch(int id, UnityEngine.InputSystem.TouchPhase phase, Vector2 position)
        {
            InputSystem.QueueStateEvent(screen, new TouchState { touchId = id, phase = phase, position = position });
            InputSystem.Update();
        }
    }
}

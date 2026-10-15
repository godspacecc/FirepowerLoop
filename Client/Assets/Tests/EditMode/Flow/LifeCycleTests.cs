using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class LifeCycleTests
    {
        [TearDown]
        public void TearDown()
        {
            LifeCycle.ClearForTests();
        }

        [Test]
        public void Update_RegistersOnceAndCanUnregister()
        {
            var calls = 0;
            Action callback = () => calls++;

            LifeCycle.RegisterUpdate(callback);
            LifeCycle.RegisterUpdate(callback);
            LifeCycle.Update();
            LifeCycle.UnregisterUpdate(callback);
            LifeCycle.Update();

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Update_ContinuesAfterCallbackThrows()
        {
            var calls = 0;
            LifeCycle.RegisterUpdate(() => throw new InvalidOperationException("update boom"));
            LifeCycle.RegisterUpdate(() => calls++);

            var errors = CaptureErrors(() => Assert.DoesNotThrow(() => LifeCycle.Update()));

            AssertHasError(errors, "生命周期阶段 Update 回调异常：.*update boom");
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void UnregisterFixedAndLate_RemovesCallbacks()
        {
            var fixedCalls = 0;
            var lateCalls = 0;
            Action fixedCallback = () => fixedCalls++;
            Action lateCallback = () => lateCalls++;

            LifeCycle.RegisterFixedUpdate(fixedCallback);
            LifeCycle.RegisterLateUpdate(lateCallback);
            LifeCycle.FixedUpdate();
            LifeCycle.LateUpdate();

            LifeCycle.UnregisterFixedUpdate(fixedCallback);
            LifeCycle.UnregisterLateUpdate(lateCallback);
            LifeCycle.FixedUpdate();
            LifeCycle.LateUpdate();

            Assert.That(fixedCalls, Is.EqualTo(1));
            Assert.That(lateCalls, Is.EqualTo(1));
        }

        [Test]
        public void FixedUpdate_ContinuesAfterCallbackThrows()
        {
            var calls = 0;
            LifeCycle.RegisterFixedUpdate(() => throw new InvalidOperationException("fixed boom"));
            LifeCycle.RegisterFixedUpdate(() => calls++);

            var errors = CaptureErrors(() => Assert.DoesNotThrow(() => LifeCycle.FixedUpdate()));

            AssertHasError(errors, "生命周期阶段 FixedUpdate 回调异常：.*fixed boom");
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void LateUpdate_ContinuesAfterCallbackThrows()
        {
            var calls = 0;
            LifeCycle.RegisterLateUpdate(() => throw new InvalidOperationException("late boom"));
            LifeCycle.RegisterLateUpdate(() => calls++);

            var errors = CaptureErrors(() => Assert.DoesNotThrow(() => LifeCycle.LateUpdate()));

            AssertHasError(errors, "生命周期阶段 LateUpdate 回调异常：.*late boom");
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void FrameEnd_DeduplicatesByOwnerAndMethod()
        {
            var calls = 0;
            var owner = new object();
            void Callback()
            {
                calls++;
            }

            LifeCycle.ScheduleFrameEnd(owner, Callback);
            LifeCycle.ScheduleFrameEnd(owner, Callback);
            LifeCycle.LateUpdate();

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Quit_ContinuesAfterCallbackThrowsAndClearsCallbacks()
        {
            var updateCalls = 0;
            var fixedCalls = 0;
            var lateCalls = 0;
            var calls = 0;
            LifeCycle.RegisterUpdate(() => updateCalls++);
            LifeCycle.RegisterFixedUpdate(() => fixedCalls++);
            LifeCycle.RegisterLateUpdate(() => lateCalls++);
            LifeCycle.RegisterQuit(() => throw new InvalidOperationException("quit boom"));
            LifeCycle.RegisterQuit(() => calls++);

            var errors = CaptureErrors(() => Assert.DoesNotThrow(() => LifeCycle.Quit()));
            LifeCycle.Update();
            LifeCycle.FixedUpdate();
            LifeCycle.LateUpdate();
            LifeCycle.Quit();

            AssertHasError(errors, "生命周期阶段 Quit 回调异常：.*quit boom");
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(updateCalls, Is.EqualTo(0));
            Assert.That(fixedCalls, Is.EqualTo(0));
            Assert.That(lateCalls, Is.EqualTo(0));
        }

        [Test]
        public void Quit_ContinuesWhenCallbackUnregistersItself()
        {
            var calls = 0;
            void SelfUnregisteringCallback()
            {
                calls++;
                LifeCycle.UnregisterQuit(SelfUnregisteringCallback);
            }

            LifeCycle.RegisterQuit(SelfUnregisteringCallback);
            LifeCycle.RegisterQuit(() => calls++);

            LifeCycle.Quit();

            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void ApplicationPauseRegistersOnceUnregistersAndClears()
        {
            var calls = 0;
            bool lastPaused = false;
            Action<bool> callback = paused =>
            {
                calls++;
                lastPaused = paused;
            };

            LifeCycle.RegisterApplicationPause(callback);
            LifeCycle.RegisterApplicationPause(callback);
            LifeCycle.ApplicationPause(true);

            Assert.That(calls, Is.EqualTo(1));
            Assert.IsTrue(lastPaused);

            LifeCycle.UnregisterApplicationPause(callback);
            LifeCycle.ApplicationPause(false);
            Assert.That(calls, Is.EqualTo(1));

            LifeCycle.RegisterApplicationPause(callback);
            LifeCycle.Quit();
            LifeCycle.ApplicationPause(true);
            Assert.That(calls, Is.EqualTo(1));

            LifeCycle.RegisterApplicationPause(callback);
            LifeCycle.ClearForTests();
            LifeCycle.ApplicationPause(true);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ApplicationPauseContinuesAfterCallbackThrows()
        {
            var calls = 0;
            bool received = false;
            LifeCycle.RegisterApplicationPause(_ => throw new InvalidOperationException("pause boom"));
            LifeCycle.RegisterApplicationPause(paused =>
            {
                calls++;
                received = paused;
            });

            var errors = CaptureErrors(() => Assert.DoesNotThrow(() => LifeCycle.ApplicationPause(true)));

            AssertHasError(errors, "生命周期阶段 ApplicationPause 回调异常：.*pause boom");
            Assert.That(calls, Is.EqualTo(1));
            Assert.IsTrue(received);
        }

        [Test]
        public void GameStartForwardsApplicationPauseToLifeCycleOnly()
        {
            string source = File.ReadAllText(Path.Combine(ApplicationRoot(), "Assets/Game/Scripts/GameStart.cs"));

            StringAssert.Contains("private void OnApplicationPause(bool pauseStatus)", source);
            StringAssert.Contains("LifeCycle.ApplicationPause(pauseStatus);", source);
            string method = Regex.Match(
                source,
                @"private\s+void\s+OnApplicationPause\s*\(\s*bool\s+pauseStatus\s*\)\s*\{(?<body>.*?)\}",
                RegexOptions.Singleline).Groups["body"].Value;
            StringAssert.DoesNotContain("OnlineAccount", method);
            StringAssert.DoesNotContain("Network", method);
            StringAssert.DoesNotContain("Ping", method);
            Assert.That(Regex.Matches(method, @";").Count, Is.EqualTo(1));
        }

        private static List<string> CaptureErrors(Action action)
        {
            var errors = new List<string>();
            using (Log.CaptureForTests((type, message) =>
            {
                if (type == LogType.Error)
                {
                    errors.Add(message);
                }
            }))
            {
                action();
            }

            return errors;
        }

        private static void AssertHasError(List<string> errors, string pattern)
        {
            Assert.IsTrue(errors.Exists(message => Regex.IsMatch(message, pattern)), $"Expected log matching: {pattern}");
        }

        private static string ApplicationRoot()
        {
            return Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ?? throw new InvalidOperationException("Cannot resolve project root.");
        }
    }
}

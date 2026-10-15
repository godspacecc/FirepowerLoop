using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class TimerMgrTests
    {
        [TearDown]
        public void TearDown()
        {
            TimerMgr.Instance.ClearForTests();
            LifeCycle.ClearForTests();
        }

        [Test]
        public void Add_InvokesOneShotTimerAfterDelay()
        {
            var calls = 0;

            TimerMgr.Instance.Add(() =>
            {
                calls++;
                return false;
            }, 0f, 1f);

            TimerMgr.Instance.TickForTests(0.5f);
            Assert.That(calls, Is.EqualTo(0));

            TimerMgr.Instance.TickForTests(0.5f);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Add_RepeatsTimerWhileCallbackReturnsTrue()
        {
            var calls = 0;

            TimerMgr.Instance.Add(() =>
            {
                calls++;
                return calls < 3;
            }, 0.25f);

            TimerMgr.Instance.TickForTests(0.25f);
            TimerMgr.Instance.TickForTests(0.25f);
            TimerMgr.Instance.TickForTests(0.25f);
            TimerMgr.Instance.TickForTests(0.25f);

            Assert.That(calls, Is.EqualTo(3));
        }

        [Test]
        public void Delete_PreventsTimerCallback()
        {
            var calls = 0;
            var id = TimerMgr.Instance.Add(() =>
            {
                calls++;
                return false;
            }, 0f, 1f);

            TimerMgr.Instance.Delete(id);
            TimerMgr.Instance.TickForTests(1f);

            Assert.That(calls, Is.EqualTo(0));
        }

        [Test]
        public void Tick_OnlyUnscaledTimerAdvancesWhileGameClockIsPaused()
        {
            var scaledCalls = 0;
            var unscaledCalls = 0;
            TimerMgr.Instance.Add(() => { scaledCalls++; return false; }, delay: 1f);
            TimerMgr.Instance.Add(() => { unscaledCalls++; return false; }, delay: 1f, useUnscaledTime: true);

            TimerMgr.Instance.TickForTests(0f, 1f);
            Assert.That(scaledCalls, Is.Zero);
            Assert.That(unscaledCalls, Is.EqualTo(1));
            TimerMgr.Instance.TickForTests(1f, 0f);
            Assert.That(scaledCalls, Is.EqualTo(1));
            Assert.That(unscaledCalls, Is.EqualTo(1));
        }

        [Test]
        public void ThrowingTimer_DoesNotBlockOtherTimers()
        {
            var calls = 0;

            TimerMgr.Instance.Add(() => throw new InvalidOperationException("timer boom"));
            TimerMgr.Instance.Add(() =>
            {
                calls++;
                return false;
            });

            var errors = CaptureErrors(() => Assert.DoesNotThrow(() => TimerMgr.Instance.TickForTests(0f)));

            Assert.IsTrue(errors.Exists(message => Regex.IsMatch(message, "计时器回调异常：.*timer boom")));
            Assert.That(calls, Is.EqualTo(1));
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
    }
}

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class AppStateTests
    {
        private FakeTimeScaleController timeScale;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            timeScale = new FakeTimeScaleController { TimeScale = 1f };
            Game.Interface.GetSystem<AppStateSystem>().SetTimeScaleControllerForTests(timeScale);
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
        }

        [Test]
        public void AllowsExpectedTransitionsAndSendsEvents()
        {
            var events = new List<AppStateChangedEvent>();
            var unregister = Game.Interface.RegisterEvent<AppStateChangedEvent>(events.Add);
            var system = Game.Interface.GetSystem<AppStateSystem>();

            Assert.IsTrue(system.TryTransition(AppState.Loading));
            Assert.IsTrue(system.TryTransition(AppState.Playing));
            Assert.IsTrue(system.TryTransition(AppState.Paused));
            Assert.IsTrue(system.TryTransition(AppState.Loading));
            Assert.IsTrue(system.TryTransition(AppState.Paused));

            unregister.UnRegister();
            Assert.That(events[0].Previous, Is.EqualTo(AppState.Paused));
            Assert.That(events[0].Current, Is.EqualTo(AppState.Loading));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
        }

        [Test]
        public void RejectsInvalidDirectTransitions()
        {
            var system = Game.Interface.GetSystem<AppStateSystem>();

            Assert.IsTrue(system.TryTransition(AppState.Paused));
            Assert.IsTrue(system.TryTransition(AppState.Loading));
            Assert.IsTrue(system.TryTransition(AppState.Playing));
            Assert.IsTrue(system.TryTransition(AppState.Paused));
            Assert.IsTrue(system.TryTransition(AppState.Paused));
        }

        [Test]
        public void PauseAndResumePreserveTimeScale()
        {
            var system = Game.Interface.GetSystem<AppStateSystem>();
            timeScale.TimeScale = 0.35f;

            system.TryTransition(AppState.Loading);
            system.TryTransition(AppState.Playing);
            timeScale.TimeScale = 0.35f;
            Assert.IsTrue(system.TryPause());
            Assert.That(timeScale.TimeScale, Is.EqualTo(0f));

            Assert.IsTrue(system.TryResume());
            Assert.That(timeScale.TimeScale, Is.EqualTo(0.35f));
        }

        [Test]
        public void EnteringPlayingFromLoadingRecoversZeroTimeScale()
        {
            var system = Game.Interface.GetSystem<AppStateSystem>();
            timeScale.TimeScale = 0f;

            Assert.IsTrue(system.TryTransition(AppState.Loading));
            Assert.IsTrue(system.TryTransition(AppState.Playing));

            Assert.That(timeScale.TimeScale, Is.EqualTo(1f));
        }

        private static void ResetArchitecture()
        {
            var field = typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic);
            field?.SetValue(null, null);
        }

        private sealed class FakeTimeScaleController : ITimeScaleController
        {
            public float TimeScale { get; set; }
        }
    }
}

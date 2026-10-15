using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class ViewCtrlLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
        }

        [Test]
        public void PlayerViewCtrlRegistersVisibilityAndReleaseIdempotently()
        {
            var go = new GameObject("Player", typeof(CharacterController), typeof(PlayerView));
            var view = go.GetComponent<PlayerView>();
            var ctrl = new PlayerViewCtrl(
                view,
                1,
                9,
                Game.Interface.GetUtility<ViewCtrlRegistry>(),
                Game.Interface.GetUtility<IViewInstanceProvider>());

            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.EqualTo(1));
            ctrl.SetVisible(false);
            Assert.IsFalse(go.activeSelf);
            ctrl.Release();
            ctrl.Release();
            Assert.That(Game.Interface.GetUtility<ViewCtrlRegistry>().Count, Is.EqualTo(0));
        }

        [Test]
        public void InvalidConstructionAndDuplicateRegistryAreRejected()
        {
            var registry = Game.Interface.GetUtility<ViewCtrlRegistry>();
            var provider = Game.Interface.GetUtility<IViewInstanceProvider>();
            var go = new GameObject("Player", typeof(CharacterController), typeof(PlayerView));

            try
            {
                Assert.Throws<System.ArgumentNullException>(() => new PlayerViewCtrl(null, 1, 1, registry, provider));
                Assert.Throws<System.ArgumentOutOfRangeException>(() => new PlayerViewCtrl(go.GetComponent<PlayerView>(), 0, 1, registry, provider));
                Assert.Throws<System.ArgumentOutOfRangeException>(() => new PlayerViewCtrl(go.GetComponent<PlayerView>(), 1, 0, registry, provider));

                var ctrl = new PlayerViewCtrl(go.GetComponent<PlayerView>(), 1, 1, registry, provider);
                Assert.IsFalse(registry.TryRegister(ctrl, out _, out var error));
                Assert.That(error, Does.Contain("已注册"));
            }
            finally
            {
                registry.DisposeAll();
            }
        }

        [Test]
        public void DisposeAllClearsAllRegisteredControllers()
        {
            var registry = Game.Interface.GetUtility<ViewCtrlRegistry>();
            var provider = Game.Interface.GetUtility<IViewInstanceProvider>();
            _ = new PlayerViewCtrl(new GameObject("A", typeof(CharacterController), typeof(PlayerView)).GetComponent<PlayerView>(), 1, 1, registry, provider);
            _ = new PlayerViewCtrl(new GameObject("B", typeof(CharacterController), typeof(PlayerView)).GetComponent<PlayerView>(), 2, 1, registry, provider);

            registry.DisposeAll();

            Assert.That(registry.Count, Is.EqualTo(0));
        }
    }
}

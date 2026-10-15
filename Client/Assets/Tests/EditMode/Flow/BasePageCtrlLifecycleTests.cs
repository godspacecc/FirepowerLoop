using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class BasePageCtrlLifecycleTests
    {
        private sealed class RecordingPageCtrl : BasePageCtrl
        {
            public int InitCount { get; private set; }
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public bool Visible => IsShow();

            protected override void OnInit() => InitCount++;
            protected override void OnShow() => ShowCount++;
            protected override void OnHide() => HideCount++;
        }

        private static BaseUI CreatePage(string name)
        {
            return new GameObject(name).AddComponent<BaseUI>();
        }

        [Test]
        public void UnboundShowAndHide_DoNotInvokeCallbacks()
        {
            var ctrl = new RecordingPageCtrl();
            ctrl.Show();
            ctrl.Hide();

            Assert.That(ctrl.IsInitialized, Is.False);
            Assert.That(ctrl.Visible, Is.False);
            Assert.That(ctrl.InitCount, Is.Zero);
            Assert.That(ctrl.ShowCount, Is.Zero);
            Assert.That(ctrl.HideCount, Is.Zero);
        }

        [Test]
        public void RepeatedShowAndHide_AreIdempotent()
        {
            var ui = CreatePage("PageA");
            var ctrl = new RecordingPageCtrl();
            try
            {
                ctrl.SetUI(ui);
                Assert.That(ctrl.IsInitialized, Is.True);
                Assert.That(ctrl.InitCount, Is.Zero);

                ctrl.Show();
                ctrl.Show();
                Assert.That(ctrl.InitCount, Is.EqualTo(1));
                Assert.That(ctrl.ShowCount, Is.EqualTo(1));
                Assert.That(ctrl.Visible, Is.True);
                Assert.That(ui.gameObject.activeSelf, Is.True);

                ctrl.Hide();
                ctrl.Hide();
                Assert.That(ctrl.HideCount, Is.EqualTo(1));
                Assert.That(ctrl.Visible, Is.False);
                Assert.That(ui.gameObject.activeSelf, Is.False);

                ctrl.Show();
                Assert.That(ctrl.InitCount, Is.EqualTo(1));
                Assert.That(ctrl.ShowCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(ui.gameObject);
            }
        }

        [Test]
        public void RebindingDifferentView_HidesOldAndInitializesNewOnFirstShow()
        {
            var first = CreatePage("PageA");
            var second = CreatePage("PageB");
            var ctrl = new RecordingPageCtrl();
            try
            {
                ctrl.SetUI(first);
                ctrl.Show();
                ctrl.SetUI(second);

                Assert.That(ctrl.HideCount, Is.EqualTo(1));
                Assert.That(first.gameObject.activeSelf, Is.False);
                Assert.That(ctrl.Visible, Is.False);

                ctrl.Show();
                Assert.That(ctrl.InitCount, Is.EqualTo(2));
                Assert.That(ctrl.ShowCount, Is.EqualTo(2));

                ctrl.SetUI(null);
                Assert.That(ctrl.HideCount, Is.EqualTo(2));
                Assert.That(ctrl.IsInitialized, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(first.gameObject);
                Object.DestroyImmediate(second.gameObject);
            }
        }

        [Test]
        public void DestroyedBoundView_ResetsStateAndStopsFurtherCallbacks()
        {
            var ui = CreatePage("PageA");
            var ctrl = new RecordingPageCtrl();
            ctrl.SetUI(ui);
            ctrl.Show();

            Object.DestroyImmediate(ui.gameObject);

            Assert.That(ctrl.HideCount, Is.EqualTo(1));
            Assert.That(ctrl.IsInitialized, Is.False);
            Assert.That(ctrl.Visible, Is.False);

            ctrl.Show();
            ctrl.Hide();
            Assert.That(ctrl.InitCount, Is.EqualTo(1));
            Assert.That(ctrl.ShowCount, Is.EqualTo(1));
            Assert.That(ctrl.HideCount, Is.EqualTo(1));
        }
    }
}

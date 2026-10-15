using NUnit.Framework;

namespace Game.Tests
{
    public sealed class SaveUiTests
    {
        [Test]
        public void StartControllerUsesCurrentCollectorAddress()
        {
            Assert.That(InvokePath(new LoadingUICtrl()), Is.EqualTo("Common/Loading"));
            Assert.That(InvokePath(new StartUICtrl()), Is.EqualTo("Home/Start"));
        }

        [Test]
        public void CurrentStartAndLoadingBindingsExist()
        {
            Assert.That(typeof(LoadingUI), Is.Not.Null);
            Assert.That(typeof(StartUI), Is.Not.Null);
        }

        private static string InvokePath(BaseUICtrl ctrl)
        {
            return (string)ctrl.GetType()
                .GetMethod("Path", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(ctrl, null);
        }
    }
}

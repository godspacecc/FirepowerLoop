using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class GameFlowUiRevisionTests
    {
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [Test]
        public void StartUiShowRejectsStatesOtherThanPausedWithoutTransitioningState()
        {
            var source = ReadGameFile("Scripts/Controller/UICtrl/Home/StartUICtrl.cs");

            Assert.That(source, Does.Not.Contain("EnsureMainMenuState"));
            Assert.That(source, Does.Contain("appState != AppState.Paused"));
            Assert.That(source, Does.Contain("Start UI 只能在暂停状态显示。"));
            Assert.That(source, Does.Contain("Hide();"));
        }

        [Test]
        public void StartResumeUsesRecoverableWorldBeforeResuming()
        {
            var start = ReadGameFile("Scripts/Controller/UICtrl/Home/StartUICtrl.cs");

            Assert.That(start, Does.Contain("HasRecoverableCurrentWorld()"));
            Assert.That(start, Does.Contain("TryResume()"));
        }

        [Test]
        public void NotifyFailures_UseSingleMeaningfulSurface()
        {
            var start = ReadGameFile("Scripts/Controller/UICtrl/Home/StartUICtrl.cs");

            Assert.That(start, Does.Contain("Notify.ShowFloatTips(Texts.StartUpdateCheckFailed)"));
            Assert.That(start, Does.Contain("SetTitle(Texts.StartUpdateRestartFailed)"));
            Assert.That(start, Does.Not.Contain("Notify.ShowFloatTips(Texts.StartUpdateRestartFailed)"));
            Assert.That(start, Does.Not.Contain("Notify.ShowMessage(Texts.StartUpdateCheckFailed, Texts.StartUpdateCheckFailed)"));
            Assert.That(start, Does.Not.Contain("Notify.ShowMessage(Texts.StartUpdateRestartFailed, Texts.StartUpdateRestartFailed)"));
        }

        [Test]
        public void StartRoutesNewAndContinueToIndependentArcadeEntry()
        {
            var source = ReadGameFile("Scripts/Controller/UICtrl/Home/StartUICtrl.cs");
            Assert.That(source, Does.Contain("ArcadeResumeUICtrl.TryOpenEntry"));
            Assert.That(source, Does.Not.Contain("EnterBaseAsync"));
            Assert.That(source, Does.Not.Contain("ResumePendingExpeditionAsync"));
        }

        private static string ReadGameFile(string path)
        {
            return File.ReadAllText(Path.Combine(ProjectRoot, "Assets/Game", path.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}

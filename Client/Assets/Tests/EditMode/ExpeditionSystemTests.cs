using System;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ExpeditionSystemTests
    {
        [Test]
        public void ExpeditionModel_CapturesStableExpeditionIdentityAndGameplayState()
        {
            var model = new ExpeditionModel();
            model.BeginTask(MapIds.MissionSample, 2);
            model.SetExpeditionId("abcdef0123456789abcdef0123456789");
            model.AddTaskKill();

            var data = model.CaptureSave(model.CurrentExpeditionId, DateTime.UtcNow);

            Assert.That(data.ExpeditionId, Is.EqualTo(model.CurrentExpeditionId));
            Assert.That(data.MapId, Is.EqualTo(MapIds.MissionSample));
            Assert.That(data.TaskTargetKills, Is.EqualTo(2));
            Assert.That(data.TaskKillCount, Is.EqualTo(1));
            Assert.That(data.Version, Is.EqualTo(ExpeditionSaveData.CurrentVersion));
        }

        [Test]
        public void ExpeditionMapCatalog_StillDefinesBothOfflineExpeditionKinds()
        {
            Assert.That(MapCatalog.TryGet(MapIds.SurvivalSample, out var survival), Is.True);
            Assert.That(survival.Kind, Is.EqualTo(MapKind.Survival));
            Assert.That(MapCatalog.TryGet(MapIds.MissionSample, out var task), Is.True);
            Assert.That(task.Kind, Is.EqualTo(MapKind.Task));
        }

        [Test]
        public void TaskManagedExpeditionDoesNotUseLegacyQualification()
        {
            var model = new ExpeditionModel();
            model.BeginConfiguredExpedition(MapIds.SurvivalSample, "abcdef0123456789abcdef0123456789", true, 1f, 0);
            Assert.That(model.AdvanceGameMinutes(2f), Is.True);
            model.AddTaskKill();
            Assert.That(model.Status, Is.EqualTo(ExpeditionStatus.Playing));
            Assert.That(model.CanReturn, Is.False);
        }
    }
}

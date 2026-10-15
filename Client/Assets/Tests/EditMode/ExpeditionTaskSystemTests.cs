using System;
using System.IO;
using System.Linq;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    public sealed class ExpeditionTaskSystemTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            root = Path.Combine(Path.GetTempPath(), "AnotherDayExpeditionTaskSystemTests", Guid.NewGuid().ToString("N"));
            Architecture<Game>.OnRegisterPatch = architecture => architecture.RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes"))))));
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void SingleTaskExpeditionQualifiesOnceAndIgnoresDuplicateOrLateProgress()
        {
            var id = Guid.NewGuid().ToString("N"); var system = Game.Interface.GetSystem<ExpeditionSystem>();
            var definition = new ExpeditionTaskDefinition("task", MapIds.MissionSample, "Task", "task", TaskObjectiveType.Kill, 2f, 1);
            Assert.That(system.TryBeginConfiguredExpedition(MapIds.MissionSample, id, new[] { definition }, out var error), Is.True, error);
            system.RecordEnemyKilled(); system.RecordEnemyKilled(); system.RecordEnemyKilled();
            var task = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks[0];
            Assert.That(task.CurrentAmount, Is.EqualTo(2f)); Assert.That(task.Status, Is.EqualTo(ExpeditionTaskStatus.Completed)); Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True);
        }

        [Test]
        public void EmptyTaskExpeditionInitializesIdentityQualifiesAndPublishesOnce()
        {
            var id = Guid.NewGuid().ToString("N"); var events = 0; Game.Interface.RegisterEvent<ExpeditionChangedEvent>(_ => events++);
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(MapIds.HeJiaVillage, id, Array.Empty<ExpeditionTaskDefinition>(), out var error), Is.True, error);
            var model = Game.Interface.GetModel<ExpeditionTaskModel>();
            Assert.That(model.MapId, Is.EqualTo(MapIds.HeJiaVillage)); Assert.That(model.ExpeditionId, Is.EqualTo(id)); Assert.That(model.Tasks, Is.Empty);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True); Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void TaskSectionRoundTripsSingleOrEmptyStateAndRejectsCorruption()
        {
            var id = Guid.NewGuid().ToString("N");
            var token = JObject.Parse($"{{'schemaVersion':1,'mapId':'{MapIds.MissionSample}','expeditionId':'{id}','tasks':[{{'instanceId':'{id}:task','taskId':'task','mapId':'{MapIds.MissionSample}','expeditionId':'{id}','objectiveType':'Kill','targetAmount':2,'currentAmount':1,'order':1,'status':'InProgress'}}]}}");
            var restored = new ExpeditionTaskModel(); Assert.That(restored.Validate(token, out var error), Is.True, error); restored.Restore(token);
            Assert.That(restored.Tasks[0].CurrentAmount, Is.EqualTo(1f));
            ((JArray)token["tasks"])[0]["currentAmount"] = 3f; Assert.That(restored.Validate(token, out _), Is.False);
        }

        [Test]
        public void SurvivalProgressMarksDirtyOnlyAtWholeMinuteOrCompletion()
        {
            var id = Guid.NewGuid().ToString("N"); var events = 0;
            Game.Interface.RegisterEvent<ExpeditionChangedEvent>(_ => events++);
            var definition = new ExpeditionTaskDefinition("task", MapIds.SurvivalSample, "Task", "task", TaskObjectiveType.Survive, PlayerConditionRules.GameMinutesPerDay, 1);
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(MapIds.SurvivalSample, id, new[] { definition }, out var error), Is.True, error);
            Assert.That(events, Is.EqualTo(1));

            var save = Game.Interface.GetSystem<SaveSystem>();
            save.ConfigureForTests(root);
            Assert.That(save.CreateBase(out var baseData, out error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(baseData, out error), Is.True, error);
            var pending = Game.Interface.GetModel<ExpeditionModel>().CaptureSave(id, DateTime.UtcNow);
            Assert.That(save.CaptureExpeditionSections(pending, out error), Is.True, error);
            Assert.That(save.SaveExpedition(pending, out error), Is.True, error);
            Assert.That(save.CommitExpeditionRuntime(pending, out error), Is.True, error);

            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(1439.999f);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.False);
            Assert.That(save.Flush(out error), Is.True, error);
            var inProgress = save.InspectExpedition();
            Assert.That(inProgress.IsValid, Is.True, inProgress.Error);
            Assert.That(inProgress.Data.ElapsedGameMinutes, Is.EqualTo(1439.999f).Within(.001f));
            Assert.That(inProgress.Data.Status, Is.EqualTo(ExpeditionStatus.Playing));
            Assert.That(inProgress.Data.Sections[ExpeditionTaskModel.SaveSectionId]["tasks"]?[0]?["status"]?.Value<string>(), Is.EqualTo(ExpeditionTaskStatus.InProgress.ToString()));

            Game.Interface.GetSystem<ExpeditionSystem>().AdvanceGameMinutes(.001f);
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Tasks[0].Status, Is.EqualTo(ExpeditionTaskStatus.Completed));
            Assert.That(events, Is.EqualTo(3));
            Assert.That(save.Flush(out error), Is.True, error);
            var completed = save.InspectExpedition();
            Assert.That(completed.IsValid, Is.True, completed.Error);
            Assert.That(completed.Data.ElapsedGameMinutes, Is.EqualTo(PlayerConditionRules.GameMinutesPerDay).Within(.001f));
            Assert.That(completed.Data.Status, Is.EqualTo(ExpeditionStatus.ReturnQualified));
            Assert.That(completed.Data.Sections[ExpeditionTaskModel.SaveSectionId]["tasks"]?[0]?["status"]?.Value<string>(), Is.EqualTo(ExpeditionTaskStatus.Completed.ToString()));
        }

        [Test]
        public void InvalidConfiguredInitializationPreservesPreviousExpeditionAndTask()
        {
            var system = Game.Interface.GetSystem<ExpeditionSystem>();
            var events = 0;
            Game.Interface.RegisterEvent<ExpeditionChangedEvent>(_ => events++);
            var originalId = Guid.NewGuid().ToString("N");
            var original = new ExpeditionTaskDefinition("original", MapIds.MissionSample, "Task", "task", TaskObjectiveType.Kill, 2f, 1);
            Assert.That(system.TryBeginConfiguredExpedition(MapIds.MissionSample, originalId, new[] { original }, out var error), Is.True, error);
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.RegisterOrResetCombatant(8101, CombatFaction.Enemy, 10, 1);
            Game.Interface.SendEvent(new CombatantDiedEvent(8101, PlayerModel.LocalPlayerUnitId));
            var before = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single();
            var eventsBeforeFailure = events;

            var invalid = new[]
            {
                new ExpeditionTaskDefinition("a", MapIds.SurvivalSample, "A", "a", TaskObjectiveType.Survive, 1f, 1),
                new ExpeditionTaskDefinition("b", MapIds.SurvivalSample, "B", "b", TaskObjectiveType.Survive, 1f, 2)
            };
            Assert.That(system.TryBeginConfiguredExpedition(
                MapIds.SurvivalSample, Guid.NewGuid().ToString("N"), invalid, out _), Is.False);
            Assert.That(events, Is.EqualTo(eventsBeforeFailure));

            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            var after = Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single();
            Assert.That(expedition.CurrentExpeditionId, Is.EqualTo(originalId));
            Assert.That(expedition.Status, Is.EqualTo(ExpeditionStatus.Playing));
            Assert.That(expedition.IsTaskManaged, Is.True);
            Assert.That(after.InstanceId, Is.EqualTo(before.InstanceId));
            Assert.That(after.CurrentAmount, Is.EqualTo(1f));

            Game.Interface.SendEvent(new CombatantDiedEvent(8101, PlayerModel.LocalPlayerUnitId));
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single().CurrentAmount, Is.EqualTo(1f), "失败初始化不得清掉敌人死亡去重集合");
        }

        [Test]
        public void CombatantDeathEventsFilterPlayerUnknownAndDuplicateEnemiesBeforeCompletion()
        {
            var architecture = ExpeditionEventTestArchitecture.Interface;
            try
            {
                var system = architecture.GetSystem<ExpeditionSystem>();
                var definition = new ExpeditionTaskDefinition("kill", MapIds.MissionSample, "Task", "task", TaskObjectiveType.Kill, 2f, 1);
                Assert.That(system.TryBeginConfiguredExpedition(MapIds.MissionSample, Guid.NewGuid().ToString("N"), new[] { definition }, out var error), Is.True, error);
                var combat = architecture.GetModel<CombatModel>();
                combat.RegisterOrResetCombatant(8200, CombatFaction.Player, 10, 1);
                combat.RegisterOrResetCombatant(8201, CombatFaction.Enemy, 10, 1);
                combat.RegisterOrResetCombatant(8202, CombatFaction.Enemy, 10, 1);

                architecture.SendEvent(new CombatantDiedEvent(8200, 0));
                architecture.SendEvent(new CombatantDiedEvent(8299, 0));
                Assert.That(architecture.GetModel<ExpeditionTaskModel>().Tasks.Single().CurrentAmount, Is.Zero);
                architecture.SendEvent(new CombatantDiedEvent(8201, 0));
                architecture.SendEvent(new CombatantDiedEvent(8201, 0));
                Assert.That(architecture.GetModel<ExpeditionTaskModel>().Tasks.Single().CurrentAmount, Is.EqualTo(1f));
                Assert.That(architecture.GetModel<ExpeditionModel>().CanReturn, Is.False);
                architecture.SendEvent(new CombatantDiedEvent(8202, 0));
                Assert.That(architecture.GetModel<ExpeditionTaskModel>().Tasks.Single().CurrentAmount, Is.EqualTo(2f));
                Assert.That(architecture.GetModel<ExpeditionModel>().CanReturn, Is.True);
            }
            finally
            {
                architecture.Deinit();
            }
        }

        private sealed class ExpeditionEventTestArchitecture : Architecture<ExpeditionEventTestArchitecture>
        {
            protected override void Init()
            {
                RegisterModel(new SaveModel());
                RegisterModel(new ExpeditionModel());
                RegisterModel(new ExpeditionTaskModel());
                RegisterModel(new CombatModel());
                RegisterSystem(new SaveSystem());
                RegisterSystem(new ExpeditionTaskSystem());
                RegisterSystem(new ExpeditionSystem());
            }
        }
    }
}

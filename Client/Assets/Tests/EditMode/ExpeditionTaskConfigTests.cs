using System.IO;
using Luban;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ExpeditionTaskConfigTests
    {
        [Test]
        public void GeneratedTaskTableProvidesTwoSampleObjectivesAndHeJiaIsValidEmpty()
        {
            var provider = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")))));
            Assert.That(provider.TryGetExpeditionTasks(MapIds.SurvivalSample, out var survival, out var error), Is.True, error);
            Assert.That(survival, Has.Count.EqualTo(1));
            Assert.That(survival[0].ObjectiveType, Is.EqualTo(TaskObjectiveType.Survive));
            Assert.That(survival[0].TargetAmount, Is.EqualTo(PlayerConditionRules.GameMinutesPerDay));
            Assert.That(provider.TryGetExpeditionTasks(MapIds.MissionSample, out var mission, out error), Is.True, error);
            Assert.That(mission, Has.Count.EqualTo(1));
            Assert.That(mission[0].ObjectiveType, Is.EqualTo(TaskObjectiveType.Kill));
            Assert.That(mission[0].TargetAmount, Is.EqualTo(2f));
            Assert.That(provider.TryGetExpeditionTasks(MapIds.HeJiaVillage, out var empty, out error), Is.True, error);
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void ProviderDistinguishesValidEmptyFromInvalidTable()
        {
            var valid = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")))));
            Assert.That(valid.TryGetExpeditionTasks(MapIds.HeJiaVillage, out var empty, out var error), Is.True, error);
            Assert.That(empty, Is.Empty);
            Assert.That(valid.TryGetExpeditionTasks("missing.map", out _, out error), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void ProviderRejectsMultipleTasksForOneMapWithoutReturningPartialResults()
        {
            AssertInvalidTable(
                Row("task-a", MapIds.SurvivalSample, TaskObjectiveType.Survive, 10f),
                Row("task-b", MapIds.SurvivalSample, TaskObjectiveType.Kill, 2f));
        }

        [Test]
        public void ProviderRejectsNonFiniteAndNonPositiveTargetsWithoutReturningPartialResults()
        {
            AssertInvalidTable(Row("task-nan", MapIds.SurvivalSample, TaskObjectiveType.Survive, float.NaN));
            AssertInvalidTable(Row("task-zero", MapIds.SurvivalSample, TaskObjectiveType.Survive, 0f));
        }

        [Test]
        public void ProviderRejectsFractionalKillTargetWithoutReturningPartialResults()
        {
            AssertInvalidTable(Row("task-kill", MapIds.MissionSample, TaskObjectiveType.Kill, 1.5f));
        }

        [Test]
        public void ProviderRejectsMissingMapReferenceWithoutReturningPartialResults()
        {
            AssertInvalidTable(Row("task-missing-map", "map.missing", TaskObjectiveType.Survive, 10f));
        }

        private static void AssertInvalidTable(params TaskRow[] rows)
        {
            var provider = new LubanRuntimeConfigProvider(() => CreateTables(rows));
            Assert.That(provider.TryGetExpeditionTasks(MapIds.HeJiaVillage, out var values, out var error), Is.False);
            Assert.That(values, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        }

        private static Tables CreateTables(TaskRow[] rows)
        {
            return new Tables(name =>
            {
                if (name != "tbexpeditiontask")
                    return new ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")));

                var buffer = new ByteBuf();
                buffer.WriteSize(rows.Length);
                foreach (var row in rows)
                {
                    buffer.WriteString(row.Id);
                    buffer.WriteString(row.MapId);
                    buffer.WriteString("Task");
                    buffer.WriteString("task.test.name");
                    buffer.WriteInt((int)row.ObjectiveType);
                    buffer.WriteFloat(row.TargetAmount);
                    buffer.WriteInt(100);
                }
                return buffer;
            });
        }

        private static TaskRow Row(string id, string mapId, TaskObjectiveType objectiveType, float targetAmount) =>
            new TaskRow(id, mapId, objectiveType, targetAmount);

        private readonly struct TaskRow
        {
            public TaskRow(string id, string mapId, TaskObjectiveType objectiveType, float targetAmount)
            { Id = id; MapId = mapId; ObjectiveType = objectiveType; TargetAmount = targetAmount; }

            public string Id { get; }
            public string MapId { get; }
            public TaskObjectiveType ObjectiveType { get; }
            public float TargetAmount { get; }
        }
    }
}

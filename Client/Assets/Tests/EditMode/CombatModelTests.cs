using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;

namespace Game.Tests
{
    public sealed class CombatModelTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            ResetArchitecture();
            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayCombatTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            ResetArchitecture();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void PlayerHealthWaitsForConfigBeforeCreatingCombatant()
        {
            var combat = Game.Interface.GetModel<CombatModel>();

            Assert.That(combat.PlayerMaxHP, Is.EqualTo(0));
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(0));
            Assert.IsFalse(combat.TryGetCombatant(PlayerModel.LocalPlayerUnitId, out _));
        }

        [TestCase(-5, 0)]
        [TestCase(37, 37)]
        [TestCase(150, 100)]
        public void SetPlayerHPClampsToRange(int input, int expected)
        {
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.ApplyPlayerConfig(TestPlayerConfig(), false);

            combat.SetPlayerHP(input);

            Assert.That(combat.PlayerHP.Value, Is.EqualTo(expected));
        }

        [Test]
        public void CaptureAndRestoreUseHPField()
        {
            var combat = new CombatModel();
            combat.ApplyPlayerConfig(TestPlayerConfig(), false);
            combat.SetPlayerHP(37);

            var captured = combat.Capture();
            Assert.That(captured["hp"]?.Value<int>(), Is.EqualTo(37));

            combat.SetPlayerHP(100);
            combat.Restore(captured);
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(37));
            combat.SetPlayerHP(50);
            Assert.That(combat.Capture()["hp"]?.Value<int>(), Is.EqualTo(50));
        }

        [Test]
        public void RestoreNullUsesConfiguredFullHealthWhenConfigArrives()
        {
            var combat = new CombatModel();
            combat.SetPlayerHP(12);

            combat.Restore(null);
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(0));
            Assert.IsFalse(combat.TryGetCombatant(PlayerModel.LocalPlayerUnitId, out _));
            combat.ApplyPlayerConfig(TestPlayerConfig(), true);

            Assert.That(combat.PlayerHP.Value, Is.EqualTo(100));
        }

        [Test]
        public void ExplicitZeroRestoreSurvivesLaterConfig()
        {
            var combat = new CombatModel();

            combat.Restore(JObject.FromObject(new { hp = 0 }));
            combat.ApplyPlayerConfig(TestPlayerConfig(), true);

            Assert.That(combat.PlayerHP.Value, Is.EqualTo(0));
        }

        [Test]
        public void ValidateRejectsNonIntegerHP()
        {
            var combat = new CombatModel();

            Assert.IsFalse(combat.Validate(
                JObject.FromObject(new { hp = "bad" }),
                out _));
        }

        [Test]
        public void NewBaseBeforePlayerConfigStartsAtFullHealth()
        {
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>((IRuntimeConfigProvider)
                typeof(ItemInventoryFoundationTests).GetMethod("CreateRealConfigProvider", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null));
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var created, out var error), Is.True, error);
            Assert.That(save.CommitBaseRuntime(created, out error), Is.True, error);
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.ApplyPlayerConfig(TestPlayerConfig(), true);
            Assert.That(combat.PlayerHP.Value, Is.EqualTo(combat.PlayerMaxHP));
        }

        [Test]
        public void PlayerHealthPersistsThroughSaveSection()
        {
            Game.Interface.GetSystem<CombatSystem>();
            var saveSystem = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(saveSystem.CreateBase(out var created, out var error), Is.True, error);
            Assert.IsTrue(saveSystem.CommitBaseRuntime(created, out error), error);
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.ApplyPlayerConfig(TestPlayerConfig(), false);
            combat.SetPlayerHP(37);
            saveSystem.MarkDirty("player health test");
            Assert.IsTrue(saveSystem.Flush(out error), error);

            combat.SetPlayerHP(100);
            var prepared = saveSystem.PrepareBase();
            Assert.IsTrue(prepared.IsValid, prepared.Error);
            Assert.IsTrue(saveSystem.CommitBaseRuntime(prepared.Data, out error), error);

            Assert.That(combat.PlayerHP.Value, Is.EqualTo(37));
        }

        private static PlayerConfigDefinition TestPlayerConfig()
        {
            return new PlayerConfigDefinition(
                "player.test",
                "Prefabs/Characters/GrayboxPlayer",
                100,
                10,
                3f,
                1f,
                5f,
                360f,
                1f);
        }

        private static void ResetArchitecture()
        {
            typeof(Architecture<Game>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
        }
    }
}

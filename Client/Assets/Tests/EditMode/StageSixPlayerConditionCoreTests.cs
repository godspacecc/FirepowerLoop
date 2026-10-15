using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    public sealed class StageSixPlayerConditionCoreTests
    {
        private string tempRoot;
        private TestMapContentLoader mapLoader;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            mapLoader = new TestMapContentLoader();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
                architecture.RegisterUtility<IRuntimeConfigProvider>(CreateRuntimeConfigProvider());
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayStageSixCondition", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 100, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            _ = Game.Interface.GetSystem<CombatSystem>();
            _ = Game.Interface.GetSystem<PlayerConditionSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            TestReset.ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = _ => { };
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void RulesExposeConfirmedStageSixDefaults()
        {
            Assert.That(PlayerConditionRules.GameMinutesPerRealSecond, Is.EqualTo(1f));
            Assert.That(PlayerConditionRules.GameMinutesPerDay, Is.EqualTo(1440f));
            Assert.That(PlayerConditionRules.FullPowerDepletionGameMinutes, Is.EqualTo(2160f));
            Assert.That(PlayerConditionRules.MaxPower, Is.EqualTo(100f));
            Assert.That(PlayerConditionRules.BasePowerRecoveryPerRealSecond, Is.EqualTo(1f));
            Assert.That(PlayerConditionRules.PowerDepletionDamagePerRealSecond, Is.EqualTo(5));
            Assert.That(PlayerConditionRules.DefaultMaxWater, Is.EqualTo(100f));
            Assert.That(PlayerConditionRules.WaterDrainPerGameMinute, Is.EqualTo(100f / 1440f).Within(0.0001f));
            Assert.That(PlayerConditionRules.WaterDepletionDamagePerRealSecond, Is.EqualTo(5f));
            Assert.That(PlayerConditionRules.BaseWaterRecoveryPerRealSecond, Is.EqualTo(1f));
            Assert.That(PlayerConditionRules.MinTemperature, Is.EqualTo(30f));
            Assert.That(PlayerConditionRules.MaxTemperature, Is.EqualTo(45f));
            Assert.That(PlayerConditionRules.NormalTemperature, Is.EqualTo(37f));
            Assert.That(PlayerConditionRules.BaseTemperatureRecoveryPerRealSecond, Is.EqualTo(0.1f));
            Assert.That(PlayerConditionRules.MaxTili, Is.EqualTo(100f));
            Assert.That(PlayerConditionRules.SprintSpeedMultiplier, Is.EqualTo(1.5f));
            Assert.That(PlayerConditionRules.SprintTiliCostPerRealSecond, Is.EqualTo(20f));
            Assert.That(PlayerConditionRules.StandingTiliRecoveryPerRealSecond, Is.EqualTo(25f));
            Assert.That(PlayerConditionRules.WalkingTiliRecoveryPerRealSecond, Is.EqualTo(15f));
            Assert.That(PlayerConditionRules.SprintRestartThreshold, Is.EqualTo(20f));
            Assert.That(PlayerConditionRules.PowerDrainPerGameMinute, Is.EqualTo(100f / 2160f).Within(0.0001f));
        }

        [Test]
        public void ExpeditionClockAdvancesAllExplorationAndContinuesAfterQualification()
        {
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            BeginSurvival();
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(water: 10000f, maxWater: 10000f));

            condition.TickWorld(PlayerConditionRules.GameMinutesPerDay, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(expedition.Status, Is.EqualTo(ExpeditionStatus.ReturnQualified));
            Assert.That(expedition.ElapsedGameMinutes, Is.EqualTo(PlayerConditionRules.GameMinutesPerDay).Within(0.001f));

            condition.TickWorld(10f, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(expedition.ElapsedGameMinutes, Is.EqualTo(PlayerConditionRules.GameMinutesPerDay + 10f).Within(0.001f));

            Game.Interface.GetSystem<ExpeditionSystem>().BeginTask(MapIds.MissionSample, 2);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
            condition.TickWorld(30f, AppState.Playing, MapIds.MissionSample);
            Assert.That(expedition.ElapsedGameMinutes, Is.EqualTo(30f).Within(0.001f));
            Assert.That(expedition.CanReturn, Is.False);
        }

        [Test]
        public void ExpeditionClockStopsInBasePausedLoadingReturningAndDefeated()
        {
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            BeginSurvival();

            condition.TickWorld(5f, AppState.Playing, MapIds.SurvivalSample);
            condition.TickWorld(5f, AppState.Paused, MapIds.SurvivalSample);
            condition.TickWorld(5f, AppState.Loading, MapIds.SurvivalSample);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            condition.TickWorld(5f, AppState.Playing, MapIds.Base);
            expedition.QualifyReturn();
            expedition.BeginReturning();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            condition.TickWorld(5f, AppState.Playing, MapIds.SurvivalSample);
            expedition.MarkDefeated();
            condition.TickWorld(5f, AppState.Playing, MapIds.SurvivalSample);

            Assert.That(expedition.ElapsedGameMinutes, Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void PowerDrainsInExplorationAndRecoversOnlyInPlayingBase()
        {
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            BeginSurvival();

            condition.TickWorld(216f, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(90f).Within(0.001f));

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            condition.TickWorld(5f, AppState.Paused, MapIds.Base);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(90f).Within(0.001f));
            condition.TickWorld(5f, AppState.Playing, MapIds.Base);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(95f).Within(0.001f));
        }

        [Test]
        public void LargeAdvanceSettlesCrossingZeroWithoutFrameLoop()
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 1000, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            BeginSurvival();
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(water: 10000f, maxWater: 10000f));

            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TryAdvanceGameMinutes(
                    PlayerConditionRules.FullPowerDepletionGameMinutes + 3f,
                    out var result,
                    out var error),
                Is.True,
                error);

            Assert.That(result.AdvancedGameMinutes, Is.EqualTo(PlayerConditionRules.FullPowerDepletionGameMinutes + 3f).Within(0.001f));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(0f).Within(0.001f));
            Assert.That(result.PowerDepletionDamage, Is.EqualTo(15));
        }

        [Test]
        public void PowerDepletionDamageUsesCombatAndPublishesOnePlayerDeath()
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 10, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            var deaths = 0;
            Game.Interface.RegisterEvent<CombatantDiedEvent>(evt =>
            {
                if (evt.UnitId == PlayerModel.LocalPlayerUnitId && evt.KillerUnitId == 0)
                {
                    deaths++;
                }
            });
            BeginSurvival();
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(water: 10000f, maxWater: 10000f));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[CombatFlowSystem\\] 失败回基地未完成"));

            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TryAdvanceGameMinutes(
                    PlayerConditionRules.FullPowerDepletionGameMinutes + 10f,
                    out var result,
                    out var error),
                Is.True,
                error);
            Assert.That(result.AdvancedGameMinutes, Is.EqualTo(PlayerConditionRules.FullPowerDepletionGameMinutes + 2f).Within(0.001f));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes, Is.EqualTo(result.AdvancedGameMinutes).Within(0.001f));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(0));
            Assert.That(deaths, Is.EqualTo(1));

            Game.Interface.GetSystem<PlayerConditionSystem>().TryAdvanceGameMinutes(20f, out _, out _);
            Assert.That(deaths, Is.EqualTo(1));
        }

        [Test]
        public void DualDepletionDamageKeepsPowerPriorityAndStopsWaterAfterDeath()
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 1, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            BeginSurvival();
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 0f, water: 0f));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[CombatFlowSystem\\] 失败回基地未完成"));

            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TryAdvanceGameMinutes(
                    10f,
                    out var result,
                    out var error),
                Is.True,
                error);

            Assert.That(result.AdvancedGameMinutes, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes, Is.EqualTo(result.AdvancedGameMinutes).Within(0.001f));
            Assert.That(result.PowerDepletionDamage, Is.EqualTo(1));
            Assert.That(result.WaterDepletionDamage, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(0));
        }

        [Test]
        public void MaxWaterIncreaseDoesNotRefillWaterButMarksDirty()
        {
            CommitCleanSaveWithCondition(ConditionSection(water: 40f, maxWater: 80f));
            var events = 0;
            Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => events++);

            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TrySetMaxWater(120f, out var before, out var after, out var error), Is.True, error);

            var model = Game.Interface.GetModel<PlayerConditionModel>();
            Assert.That(before, Is.EqualTo(80f).Within(0.001f));
            Assert.That(after, Is.EqualTo(120f).Within(0.001f));
            Assert.That(model.Water, Is.EqualTo(40f).Within(0.001f));
            Assert.That(IsSaveDirty(), Is.True);
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void MaxWaterDecreaseClampsWaterPublishesOnceAndMarksDirty()
        {
            CommitCleanSaveWithCondition(ConditionSection(water: 90f, maxWater: 100f));
            var events = 0;
            Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => events++);

            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TrySetMaxWater(60f, out var before, out var after, out var error), Is.True, error);

            var model = Game.Interface.GetModel<PlayerConditionModel>();
            Assert.That(before, Is.EqualTo(100f).Within(0.001f));
            Assert.That(after, Is.EqualTo(60f).Within(0.001f));
            Assert.That(model.MaxWater, Is.EqualTo(60f).Within(0.001f));
            Assert.That(model.Water, Is.EqualTo(60f).Within(0.001f));
            Assert.That(IsSaveDirty(), Is.True);
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void WaterFractionalChangeWithinSameIntegerBucketMarksDirty()
        {
            CommitCleanSaveWithCondition(ConditionSection(water: 40.1f, maxWater: 100f));
            var events = 0;
            Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => events++);

            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TrySetWater(40.9f, out _, out _, out var error), Is.True, error);

            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Water, Is.EqualTo(40.9f).Within(0.001f));
            Assert.That(IsSaveDirty(), Is.True);
            Assert.That(events, Is.EqualTo(1));
        }

        [Test]
        public void WaterAndMaxWaterSameValueDoNotMarkDirtyOrPublish()
        {
            CommitCleanSaveWithCondition(ConditionSection(water: 40f, maxWater: 100f));
            var events = 0;
            Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => events++);

            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TrySetWater(40f, out _, out _, out var waterError), Is.True, waterError);
            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TrySetMaxWater(100f, out _, out _, out var maxWaterError), Is.True, maxWaterError);

            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(events, Is.EqualTo(0));
        }

        [Test]
        public void CombatFlowDeathHandlerReturnsUniTaskAndCallerForgets()
        {
            var source = File.ReadAllText("Assets/Game/Scripts/System/Combat/CombatFlowSystem.cs");

            Assert.That(source, Does.Contain("private async UniTask HandlePlayerDiedAsync()"));
            Assert.That(source, Does.Contain("HandlePlayerDiedAsync().Forget();"));
            Assert.That(source, Does.Not.Contain("async void HandlePlayerDied"));
        }

        [UnityTest]
        public IEnumerator CombatFlowBusinessFailureResetsGuardAndKeepsError()
        {
            BeginSurvival();
            mapLoader.FailNextLoad = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[CombatFlowSystem\\] 失败回基地未完成"));

            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            yield return WaitFrames(2);

            var flow = Game.Interface.GetSystem<CombatFlowSystem>();
            Assert.That(flow.LastFailureError, Is.EqualTo("load failed"));
            Assert.That(IsReturningAfterDeath(flow), Is.False);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.Defeated));
            Assert.That(mapLoader.LoadedMapIds, Is.EqualTo(new[] { MapIds.Base }));

            mapLoader.FailNextLoad = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[CombatFlowSystem\\] 失败回基地未完成"));
            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            yield return WaitFrames(2);

            Assert.That(IsReturningAfterDeath(flow), Is.False);
            Assert.That(mapLoader.LoadedMapIds, Is.EqualTo(new[] { MapIds.Base, MapIds.Base }));
        }

        [Test]
        public void PlayerConditionSaveRestoresPowerAndResetsTili()
        {
            var model = Game.Interface.GetModel<PlayerConditionModel>();
            BeginSurvival();
            Game.Interface.GetSystem<PlayerConditionSystem>().TickWorld(216f, AppState.Playing, MapIds.SurvivalSample);
            Game.Interface.GetSystem<PlayerConditionSystem>().TickTili(2f, true, true);
            var saved = model.Capture();

            model.Reset();
            model.Restore(saved);

            Assert.That(model.Power, Is.EqualTo(90f).Within(0.001f));
            Assert.That(model.Tili, Is.EqualTo(60f).Within(0.001f));
            Assert.That(model.Water, Is.EqualTo(85f).Within(0.001f));
            Assert.That(model.MaxWater, Is.EqualTo(PlayerConditionRules.DefaultMaxWater));
            Assert.That(model.Temperature, Is.EqualTo(PlayerConditionRules.NormalTemperature));
            Assert.That(model.SprintReleaseRequired, Is.False);
        }

        [Test]
        public void MissingConditionSectionStartsFullAndInvalidSectionIsRejected()
        {
            var model = Game.Interface.GetModel<PlayerConditionModel>();
            model.Restore(null);
            Assert.That(model.Power, Is.EqualTo(PlayerConditionRules.MaxPower));
            Assert.That(model.Tili, Is.EqualTo(PlayerConditionRules.MaxTili));
            Assert.That(model.Water, Is.EqualTo(PlayerConditionRules.DefaultMaxWater));
            Assert.That(model.MaxWater, Is.EqualTo(PlayerConditionRules.DefaultMaxWater));
            Assert.That(model.Temperature, Is.EqualTo(PlayerConditionRules.NormalTemperature));
            Assert.That(model.Validate(ConditionSection(), out _), Is.True);
            foreach (var field in new[] { "power", "tili", "water", "maxWater", "temperature" })
            {
                var section = ConditionSection();
                section.Remove(field);
                Assert.That(model.Validate(section, out _), Is.False, field);
            }

            Assert.That(model.Validate(new JObject(), out _), Is.False);
            Assert.That(model.Validate(ConditionSection(power: float.NaN), out _), Is.False);
            Assert.That(model.Validate(ConditionSection(tili: float.PositiveInfinity), out _), Is.False);
            Assert.That(model.Validate(ConditionSection(water: 101f), out _), Is.False);
            Assert.That(model.Validate(ConditionSection(water: 80f, maxWater: 60f), out _), Is.False);
            Assert.That(model.Validate(ConditionSection(maxWater: 0f), out _), Is.False);
            Assert.That(model.Validate(ConditionSection(temperature: 29.9f), out _), Is.False);
            Assert.That(model.Validate(ConditionSection(temperature: 45.1f), out _), Is.False);
        }

        [Test]
        public void WaterConsumesOnlyWhileAlivePlayingAndInActiveExploration()
        {
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            var model = Game.Interface.GetModel<PlayerConditionModel>();
            model.Restore(ConditionSection(water: 100f));

            condition.TickWorld(10f, AppState.Playing, MapIds.Base);
            Assert.That(model.Water, Is.EqualTo(100f).Within(0.001f));

            BeginSurvival();
            condition.TickWorld(10f, AppState.Paused, MapIds.SurvivalSample);
            condition.TickWorld(10f, AppState.Loading, MapIds.SurvivalSample);
            Assert.That(model.Water, Is.EqualTo(100f).Within(0.001f));

            condition.TickWorld(144f, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(model.Water, Is.EqualTo(90f).Within(0.001f));

            Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
            condition.TickWorld(144f, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(model.Water, Is.EqualTo(90f).Within(0.001f));

            BeginSurvival();
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            condition.TickWorld(144f, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(model.Water, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void BaseRecoversWaterAndReturnsTemperatureOnlyWhenAlivePlaying()
        {
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            var model = Game.Interface.GetModel<PlayerConditionModel>();
            model.Restore(ConditionSection(power: 80f, water: 40f, temperature: 30f));

            condition.TickWorld(5f, AppState.Paused, MapIds.Base);
            condition.TickWorld(5f, AppState.Loading, MapIds.Base);
            Assert.That(model.Water, Is.EqualTo(40f).Within(0.001f));
            Assert.That(model.Temperature, Is.EqualTo(30f).Within(0.001f));

            condition.TickWorld(10f, AppState.Playing, MapIds.Base);
            Assert.That(model.Water, Is.EqualTo(50f).Within(0.001f));
            Assert.That(model.Temperature, Is.EqualTo(31f).Within(0.001f));

            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            condition.TickWorld(10f, AppState.Playing, MapIds.Base);
            Assert.That(model.Water, Is.EqualTo(50f).Within(0.001f));
            Assert.That(model.Temperature, Is.EqualTo(31f).Within(0.001f));
        }

        [Test]
        public void SprintConsumesOnlyWhileMovingAndHeld()
        {
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            Assert.That(condition.TickTili(1f, false, true).IsSprinting, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(100f).Within(0.001f));

            var sprint = condition.TickTili(1f, true, true);
            Assert.That(sprint.IsSprinting, Is.True);
            Assert.That(sprint.SpeedMultiplier, Is.EqualTo(PlayerConditionRules.SprintSpeedMultiplier));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(80f).Within(0.001f));
        }

        [Test]
        public void SprintRecoversAtStandingAndWalkingRates()
        {
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            condition.TickTili(2f, true, true);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(60f).Within(0.001f));
            condition.TickTili(1f, true, false);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(75f).Within(0.001f));
            condition.TickTili(1f, false, false);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void ExhaustedSprintRequiresReleaseAndRestartThreshold()
        {
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            condition.TickTili(5f, true, true);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(0f).Within(0.001f));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().SprintReleaseRequired, Is.True);
            Assert.That(condition.TickTili(1f, true, true).IsSprinting, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(0f).Within(0.001f));

            condition.TickTili(0.1f, false, false);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().SprintReleaseRequired, Is.False);
            Assert.That(condition.TickTili(1f, true, true).IsSprinting, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(17.5f).Within(0.001f));
            Assert.That(condition.TickTili(1f, true, true).IsSprinting, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(32.5f).Within(0.001f));
            Assert.That(condition.TickTili(1f, true, true).IsSprinting, Is.False);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(47.5f).Within(0.001f));

            Assert.That(condition.TickTili(0.1f, true, false).IsSprinting, Is.False);
            Assert.That(condition.TickTili(1f, true, true).IsSprinting, Is.True);
        }

        [Test]
        public void ConditionRestoreClearsTransientRuntimeState()
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 100, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            BeginSurvival();
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(water: 10000f, maxWater: 10000f));
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            var model = Game.Interface.GetModel<PlayerConditionModel>();

            Assert.That(condition.TryAdvanceGameMinutes(
                    PlayerConditionRules.FullPowerDepletionGameMinutes,
                    out _,
                    out var error),
                Is.True,
                error);
            Assert.That(model.Power, Is.EqualTo(0f).Within(0.001f));
            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var firstFraction, out error), Is.True, error);
            Assert.That(firstFraction.PowerDepletionDamage, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(100));

            condition.TickTili(5f, true, true);
            condition.TickTili(0.1f, false, false);
            condition.TickTili(1f, true, true);
            Assert.That(model.Tili, Is.LessThan(PlayerConditionRules.SprintRestartThreshold));

            model.Restore(ConditionSection(power: 0f));
            Assert.That(model.Power, Is.EqualTo(0f).Within(0.001f));
            Assert.That(model.Tili, Is.EqualTo(PlayerConditionRules.MaxTili));
            Assert.That(model.SprintReleaseRequired, Is.False);
            Assert.That(condition.TickTili(1f, true, true).IsSprinting, Is.True);

            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var restoredFraction, out error), Is.True, error);
            Assert.That(restoredFraction.PowerDepletionDamage, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(100));
            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var restoredDamage, out error), Is.True, error);
            Assert.That(restoredDamage.PowerDepletionDamage, Is.EqualTo(1));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(99));
        }

        [Test]
        public void WaterDepletionAccumulatorClearsOnSetWaterRestoreAndReset()
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 100, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            BeginSurvival();
            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            var model = Game.Interface.GetModel<PlayerConditionModel>();
            model.Restore(ConditionSection(power: PlayerConditionRules.MaxPower, water: 0f));

            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var fraction, out var error), Is.True, error);
            Assert.That(fraction.WaterDepletionDamage, Is.EqualTo(0));
            Assert.That(condition.TrySetWater(0f, out _, out _, out error), Is.True, error);
            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var afterSet, out error), Is.True, error);
            Assert.That(afterSet.WaterDepletionDamage, Is.EqualTo(0));
            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var setDamage, out error), Is.True, error);
            Assert.That(setDamage.WaterDepletionDamage, Is.EqualTo(1));

            model.Restore(ConditionSection(power: PlayerConditionRules.MaxPower, water: 0f));
            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var afterRestore, out error), Is.True, error);
            Assert.That(afterRestore.WaterDepletionDamage, Is.EqualTo(0));

            model.Reset();
            model.Restore(ConditionSection(power: PlayerConditionRules.MaxPower, water: 0f));
            Assert.That(condition.TryAdvanceGameMinutes(0.1f, out var afterResetRestore, out error), Is.True, error);
            Assert.That(afterResetRestore.WaterDepletionDamage, Is.EqualTo(0));
        }

        [Test]
        public void LargeAdvanceMatchesEqualSplitAdvance()
        {
            var single = AdvanceScenario(30f);
            var split = AdvanceScenario(5f, 5f, 5f, 5f, 5f, 5f);

            Assert.That(single.ElapsedGameMinutes, Is.EqualTo(split.ElapsedGameMinutes).Within(0.001f));
            Assert.That(single.Power, Is.EqualTo(split.Power).Within(0.001f));
            Assert.That(single.Water, Is.EqualTo(split.Water).Within(0.001f));
            Assert.That(single.PlayerHP, Is.EqualTo(split.PlayerHP));
            Assert.That(single.PowerDepletionDamage, Is.EqualTo(split.PowerDepletionDamage));
            Assert.That(single.WaterDepletionDamage, Is.EqualTo(split.WaterDepletionDamage));
        }

        [Test]
        public void PlayerMoveUsesConditionMultiplierWithoutOwningTili()
        {
            var motion = PlayerMoveSystem.CalculateHorizontalMotion(
                Vector2.up,
                0f,
                4f * PlayerConditionRules.SprintSpeedMultiplier,
                1f);
            Assert.That(motion.z, Is.EqualTo(6f).Within(0.001f));
            var source = File.ReadAllText("Assets/Game/Scripts/System/Player/PlayerMoveSystem.cs");
            Assert.That(source, Does.Contain("TickTili"));
            Assert.That(source, Does.Contain("SpeedMultiplier"));
            Assert.That(source, Does.Not.Contain("SprintReleaseRequired"));
        }

        private static void BeginSurvival()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.GetSystem<ExpeditionSystem>().BeginSurvival(
                MapIds.SurvivalSample,
                PlayerConditionRules.GameMinutesPerDay);
        }

        private static void CommitCleanSaveWithCondition(JObject condition)
        {
            var save = Game.Interface.GetSystem<SaveSystem>();
            Assert.That(save.CreateBase(out var data, out var error), Is.True, error);
            data.Sections["another-day.player-condition"] = condition;
            Assert.That(save.CommitBaseRuntime(data, out error), Is.True, error);
            Assert.That(IsSaveDirty(), Is.False);
        }

        private static bool IsSaveDirty()
        {
            var snapshot = Game.Interface.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            return (bool)typeof(SaveRuntimeSnapshot)
                .GetProperty("Dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(snapshot);
        }

        private static JObject ConditionSection(
            float power = PlayerConditionRules.MaxPower,
            float tili = PlayerConditionRules.MaxTili,
            float water = PlayerConditionRules.DefaultMaxWater,
            float maxWater = PlayerConditionRules.DefaultMaxWater,
            float temperature = PlayerConditionRules.NormalTemperature)
        {
            return new JObject
            {
                ["power"] = power,
                ["tili"] = tili,
                ["water"] = water,
                ["maxWater"] = maxWater,
                ["temperature"] = temperature
            };
        }

        private static AdvanceSnapshot AdvanceScenario(params float[] steps)
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 1000, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(1000);
            BeginSurvival();
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 1f, water: 1f));

            var condition = Game.Interface.GetSystem<PlayerConditionSystem>();
            var powerDamage = 0;
            var waterDamage = 0;
            foreach (var minutes in steps)
            {
                Assert.That(condition.TryAdvanceGameMinutes(minutes, out var result, out var error), Is.True, error);
                powerDamage += result.PowerDepletionDamage;
                waterDamage += result.WaterDepletionDamage;
            }

            var model = Game.Interface.GetModel<PlayerConditionModel>();
            return new AdvanceSnapshot(
                Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes,
                model.Power,
                model.Water,
                Game.Interface.GetModel<CombatModel>().PlayerHP.Value,
                powerDamage,
                waterDamage);
        }

        private readonly struct AdvanceSnapshot
        {
            public AdvanceSnapshot(
                float elapsedGameMinutes,
                float power,
                float water,
                int playerHP,
                int powerDepletionDamage,
                int waterDepletionDamage)
            {
                ElapsedGameMinutes = elapsedGameMinutes;
                Power = power;
                Water = water;
                PlayerHP = playerHP;
                PowerDepletionDamage = powerDepletionDamage;
                WaterDepletionDamage = waterDepletionDamage;
            }

            public float ElapsedGameMinutes { get; }
            public float Power { get; }
            public float Water { get; }
            public int PlayerHP { get; }
            public int PowerDepletionDamage { get; }
            public int WaterDepletionDamage { get; }
        }

        private static IRuntimeConfigProvider CreateRuntimeConfigProvider()
        {
            return new LubanRuntimeConfigProvider(CreateTables);
        }

        private static global::Tables CreateTables()
        {
            return new global::Tables(name =>
                new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", $"{name}.bytes"))));
        }

        private static bool IsReturningAfterDeath(CombatFlowSystem flow)
        {
            return (bool)typeof(CombatFlowSystem)
                .GetField("returningAfterDeath", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(flow);
        }

        private static IEnumerator WaitFrames(int frameCount)
        {
            for (var i = 0; i < frameCount; i++)
            {
                yield return null;
            }
        }

        private sealed class TestMapContentLoader : IMapContentLoader
        {
            public bool FailNextLoad { get; set; }
            public List<string> LoadedMapIds { get; } = new List<string>();

            public UniTask<MapContentLoadResult> LoadAsync(
                string mapId,
                string address,
                Action<float> onProgress,
                CancellationToken cancellationToken = default)
            {
                LoadedMapIds.Add(mapId);
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    return UniTask.FromResult(MapContentLoadResult.Fail("load failed"));
                }

                return UniTask.FromResult(MapContentLoadResult.Fail("unexpected core test load"));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(
                IMapContentHandle handle,
                CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }
        }
    }
}

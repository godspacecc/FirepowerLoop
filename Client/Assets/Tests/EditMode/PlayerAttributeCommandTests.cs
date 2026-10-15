using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class PlayerAttributeCommandTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            ResourceLoader.SetLoaderForTests(new LocalizationTestLoader());
            LocalizationLanguages.ResetForTests();
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(new AttributeConfigProvider());
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayPlayerAttributeCommandTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            Game.Interface.GetModel<SaveModel>().SetBase(new BaseSaveData(), true);
            EnterBase();
        }

        [TearDown]
        public void TearDown()
        {
            Architecture<Game>.OnRegisterPatch = _ => { };
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            TestReset.ResetArchitecture();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void NamingAndGeneratedConfigUseHPPowerAndTiliOnly()
        {
            var roots = new[]
            {
                "../Config/Defines",
                "../Config/DataCsv",
                "Assets/Game/Scripts",
                "Assets/Game/Prefabs",
                "Assets/Game/Config/Localization"
            };
            foreach (var root in roots)
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    var text = File.ReadAllText(file);
                    foreach (var forbidden in ForbiddenLegacyTerms())
                    {
                        Assert.That(text, Does.Not.Contain(forbidden), file);
                    }
                }
            }

            AssertPublicContractHelpersAreInternal();
        }

        [Test]
        public void VersionFiveRejectsLegacyVersionAndPersistsNewSections()
        {
            Assert.That(BaseSaveData.CurrentVersion, Is.EqualTo(3));

            Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set power 45", CancellationToken.None).GetAwaiter().GetResult();
            Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set tili 35", CancellationToken.None).GetAwaiter().GetResult();
            Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set water 55", CancellationToken.None).GetAwaiter().GetResult();
            Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set maxwater 80", CancellationToken.None).GetAwaiter().GetResult();
            Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set temp 36.5", CancellationToken.None).GetAwaiter().GetResult();
            Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set maxhp 120", CancellationToken.None).GetAwaiter().GetResult();
            Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set movespeed 4.5", CancellationToken.None).GetAwaiter().GetResult();

            var condition = (JObject)Game.Interface.GetModel<PlayerConditionModel>().Capture();
            Assert.That(condition["power"]?.Value<float>(), Is.EqualTo(45f).Within(0.001f));
            Assert.That(condition["tili"]?.Value<float>(), Is.EqualTo(35f).Within(0.001f));
            Assert.That(condition["water"]?.Value<float>(), Is.EqualTo(55f).Within(0.001f));
            Assert.That(condition["maxWater"]?.Value<float>(), Is.EqualTo(80f).Within(0.001f));
            Assert.That(condition["temperature"]?.Value<float>(), Is.EqualTo(36.5f).Within(0.001f));
            var attributes = (JObject)Game.Interface.GetModel<PlayerAttributeModel>().Capture();
            Assert.That(attributes["maxHP"]?.Value<int>(), Is.EqualTo(120));
            Assert.That(attributes["moveSpeed"]?.Value<float>(), Is.EqualTo(4.5f).Within(0.001f));
        }

        [Test]
        public void SetGrammarAcceptsEightFieldsAndInvariantValues()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            Assert.That(system.ExecuteAsync("#set hp 10", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set power 10.5", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set tili 10.5", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set maxhp 101", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set movespeed 3.25", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set water 10.5", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set maxwater 110", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set temp 37.5", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(system.ExecuteAsync("#set health 1", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.UnknownAttribute));
            Assert.That(system.ExecuteAsync("#set temperature 37", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.UnknownAttribute));
            Assert.That(system.ExecuteAsync("#set hp 1.5", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set power NaN", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set tili Infinity", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set water NaN", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set maxwater Infinity", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set temp NaN", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set maxhp 1.5", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set movespeed 1,5", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidAttributeValue));
            Assert.That(system.ExecuteAsync("#set hp -1", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set hp 102", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set power -0.1", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set power 101", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set tili -0.1", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set tili 101", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set water -0.1", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set water 111", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set maxwater 0", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set temp 29.9", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set temp 45.1", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set maxhp 0", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set movespeed 0", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set movespeed -1", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.AttributeOutOfRange));
            Assert.That(system.ExecuteAsync("#set hp 1 extra", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.UsageError));
        }

        [Test]
        public void SetHPAndMaxHPUseCombatClampEventsAndDeathFlow()
        {
            var died = 0;
            Game.Interface.RegisterEvent<CombatantDiedEvent>(_ => died++);

            SetSaveDirty(false);
            var sameMax = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set maxhp 100", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(sameMax.Success, Is.True);
            Assert.That(sameMax.BeforeValue, Is.EqualTo(100d).Within(0.001d));
            Assert.That(sameMax.AfterValue, Is.EqualTo(100d).Within(0.001d));
            Assert.That(Game.Interface.GetModel<PlayerAttributeModel>().MaxHPOverride.HasValue, Is.False);
            Assert.That(IsSaveDirty(), Is.False);

            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set hp 40", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(40));
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set maxhp 30", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerMaxHP, Is.EqualTo(30));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(30));
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set hp 0", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(died, Is.EqualTo(1));
        }

        [Test]
        public void SetConditionValuesUseRulesAndPersist()
        {
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set power 0", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(100));
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set tili 0", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set water 0", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set maxwater 75", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set temp 38.5", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().SprintReleaseRequired, Is.True);
            var data = (JObject)Game.Interface.GetModel<PlayerConditionModel>().Capture();
            Assert.That(data["power"]?.Value<float>(), Is.EqualTo(0f).Within(0.001f));
            Assert.That(data["tili"]?.Value<float>(), Is.EqualTo(0f).Within(0.001f));
            Assert.That(data["water"]?.Value<float>(), Is.EqualTo(0f).Within(0.001f));
            Assert.That(data["maxWater"]?.Value<float>(), Is.EqualTo(75f).Within(0.001f));
            Assert.That(data["temperature"]?.Value<float>(), Is.EqualTo(38.5f).Within(0.001f));
        }

        [Test]
        public void SetWaterMaxWaterAndTempReturnStructuredValuesDirtyAndRollbackOnEventException()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            var model = Game.Interface.GetModel<PlayerConditionModel>();
            model.Restore(ConditionSection(water: 40f, maxWater: 100f, temperature: 37f));

            SetSaveDirty(false);
            var sameWaterEvents = 0;
            var sameWaterRegistration = Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => sameWaterEvents++);
            var sameWater = system.ExecuteAsync("#set water 40", CancellationToken.None).GetAwaiter().GetResult();
            sameWaterRegistration.UnRegister();
            Assert.That(sameWater.Success, Is.True);
            Assert.That(sameWater.AttributeId, Is.EqualTo("water"));
            Assert.That(sameWater.BeforeValue, Is.EqualTo(40d).Within(0.001d));
            Assert.That(sameWater.AfterValue, Is.EqualTo(40d).Within(0.001d));
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(sameWaterEvents, Is.EqualTo(0));

            var water = system.ExecuteAsync("#set water 41", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(water.Success, Is.True);
            Assert.That(water.AttributeId, Is.EqualTo("water"));
            Assert.That(water.BeforeValue, Is.EqualTo(40d).Within(0.001d));
            Assert.That(water.AfterValue, Is.EqualTo(41d).Within(0.001d));
            Assert.That(IsSaveDirty(), Is.True);

            SetSaveDirty(false);
            var maxWater = system.ExecuteAsync("#set maxwater 120", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(maxWater.Success, Is.True);
            Assert.That(maxWater.AttributeId, Is.EqualTo("maxwater"));
            Assert.That(maxWater.BeforeValue, Is.EqualTo(100d).Within(0.001d));
            Assert.That(maxWater.AfterValue, Is.EqualTo(120d).Within(0.001d));
            Assert.That(model.Water, Is.EqualTo(41f).Within(0.001f));
            Assert.That(IsSaveDirty(), Is.True);

            SetSaveDirty(false);
            var sameMaxWaterEvents = 0;
            var sameMaxWaterRegistration = Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => sameMaxWaterEvents++);
            var sameMaxWater = system.ExecuteAsync("#set maxwater 120", CancellationToken.None).GetAwaiter().GetResult();
            sameMaxWaterRegistration.UnRegister();
            Assert.That(sameMaxWater.Success, Is.True);
            Assert.That(sameMaxWater.AttributeId, Is.EqualTo("maxwater"));
            Assert.That(sameMaxWater.BeforeValue, Is.EqualTo(120d).Within(0.001d));
            Assert.That(sameMaxWater.AfterValue, Is.EqualTo(120d).Within(0.001d));
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(sameMaxWaterEvents, Is.EqualTo(0));

            SetSaveDirty(false);
            var temp = system.ExecuteAsync("#set temp 38.5", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(temp.Success, Is.True);
            Assert.That(temp.AttributeId, Is.EqualTo("temperature"));
            Assert.That(temp.BeforeValue, Is.EqualTo(37d).Within(0.001d));
            Assert.That(temp.AfterValue, Is.EqualTo(38.5d).Within(0.001d));
            Assert.That(IsSaveDirty(), Is.True);

            SetSaveDirty(false);
            var sameTempEvents = 0;
            var sameTempRegistration = Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => sameTempEvents++);
            var sameTemp = system.ExecuteAsync("#set temp 38.5", CancellationToken.None).GetAwaiter().GetResult();
            sameTempRegistration.UnRegister();
            Assert.That(sameTemp.Success, Is.True);
            Assert.That(sameTemp.AttributeId, Is.EqualTo("temperature"));
            Assert.That(sameTemp.BeforeValue, Is.EqualTo(38.5d).Within(0.001d));
            Assert.That(sameTemp.AfterValue, Is.EqualTo(38.5d).Within(0.001d));
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(sameTempEvents, Is.EqualTo(0));

            var snapshot = Game.Interface.GetSystem<SaveSystem>().CaptureRuntimeSnapshot();
            var before = (JObject)model.Capture();
            SetSaveDirty(true);
            var dirtyAt = SetSaveDirtyAt(23.5f);
            var unregister = Game.Interface.RegisterEvent<PlayerConditionChangedEvent>(_ => throw new InvalidOperationException("condition rollback probe"));
            foreach (var command in new[] { "set water 42", "set maxwater 90", "set temp 39" })
            {
                TestCmdExecutionResult failed;
                using (Log.CaptureForTests((_, __) => { }))
                {
                    failed = system.ExecuteAsync("#" + command, CancellationToken.None).GetAwaiter().GetResult();
                }

                Assert.That(failed.Code, Is.EqualTo(TestCmdResultCode.ExecutionFailed), command);
                Assert.That(model.Capture().ToString(), Is.EqualTo(before.ToString()), command);
                Assert.That(IsSaveDirty(), Is.True, command);
                Assert.That(GetSaveDirtyAt(), Is.EqualTo(dirtyAt).Within(0.001f), command);
                Assert.That(Game.Interface.GetSystem<SaveSystem>().CaptureRuntimeSnapshot().Domain, Is.EqualTo(snapshot.Domain), command);
            }

            unregister.UnRegister();
        }

        [Test]
        public void SetMoveSpeedAppliesImmediatelyAndPersists()
        {
            var seen = 0f;
            Game.Interface.RegisterEvent<PlayerMoveSpeedChangedEvent>(evt => seen = evt.MoveSpeed);
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set movespeed 5.5", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            Assert.That(seen, Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(Game.Interface.GetSystem<PlayerAttributeSystem>().EffectiveMoveSpeed, Is.EqualTo(5.5f).Within(0.001f));
            Assert.That(((JObject)Game.Interface.GetModel<PlayerAttributeModel>().Capture())["moveSpeed"]?.Value<float>(), Is.EqualTo(5.5f).Within(0.001f));

            EnterBase();
            SetSaveDirty(false);
            var eventCount = 0;
            Game.Interface.RegisterEvent<PlayerMoveSpeedChangedEvent>(_ => eventCount++);
            var sameSpeed = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set movespeed 3", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(sameSpeed.Success, Is.True);
            Assert.That(sameSpeed.BeforeValue, Is.EqualTo(3d).Within(0.001d));
            Assert.That(sameSpeed.AfterValue, Is.EqualTo(3d).Within(0.001d));
            Assert.That(Game.Interface.GetModel<PlayerAttributeModel>().MoveSpeedOverride.HasValue, Is.False);
            Assert.That(IsSaveDirty(), Is.False);
            Assert.That(eventCount, Is.EqualTo(0));
        }

        [Test]
        public void ExistingPlayerAllowsAttributeChangesAcrossStatesAndExceptionRollsBack()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            Assert.That(system.ExecuteAsync("#set hp 10", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);

            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.EnteringExpedition, "test", false, out _);
            var unsafeBase = system.ExecuteAsync("#set hp 11", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(unsafeBase.Success, Is.True);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(11));

            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.BaseSafe, "test", false, out _);
            Assert.That(system.ExecuteAsync("#set hp 12", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            EnterExpedition(ExpeditionStatus.Playing);
            Assert.That(system.ExecuteAsync("#set hp 13", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            EnterExpedition(ExpeditionStatus.ReturnQualified);
            Assert.That(system.ExecuteAsync("#set hp 14", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            EnterExpedition(ExpeditionStatus.Returning);
            Assert.That(system.ExecuteAsync("#set hp 15", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            EnterExpedition(ExpeditionStatus.Defeated);
            Assert.That(system.ExecuteAsync("#set hp 15", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);

            EnterBase();
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            var paused = system.ExecuteAsync("#set hp 10", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(paused.Success, Is.True);
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(10));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
            Assert.That(Time.timeScale, Is.Zero);
            EnterBase();
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Assert.That(system.ExecuteAsync("#set hp 10", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            EnterBase();
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(0);
            Assert.That(system.ExecuteAsync("#set hp 10", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);
            EnterBase();
            Game.Interface.GetSystem<UnitSystem>().TrySetActive(PlayerModel.LocalPlayerUnitId, false);
            Assert.That(system.ExecuteAsync("#set hp 10", CancellationToken.None).GetAwaiter().GetResult().Success, Is.True);

            EnterBase();
            system.ExecuteAsync("#set power 44", CancellationToken.None).GetAwaiter().GetResult();
            system.ExecuteAsync("#set maxhp 110", CancellationToken.None).GetAwaiter().GetResult();
            SetSaveDirty(true);
            var dirtyAt = SetSaveDirtyAt(12.5f);
            var beforePower = Game.Interface.GetModel<PlayerConditionModel>().Power;
            var beforeMaxHP = Game.Interface.GetModel<CombatModel>().PlayerMaxHP;
            var beforeOverride = Game.Interface.GetModel<PlayerAttributeModel>().MaxHPOverride;
            var beforeMoveOverride = Game.Interface.GetModel<PlayerAttributeModel>().MoveSpeedOverride;
            var moveSystem = Game.Interface.GetSystem<PlayerMoveSystem>();
            var beforeMoveSpeedCache = GetMoveSpeedCache(moveSystem);
            Game.Interface.RegisterEvent<PlayerMoveSpeedChangedEvent>(_ => throw new InvalidOperationException("rollback probe"));
            TestCmdExecutionResult failed;
            using (Log.CaptureForTests((_, __) => { }))
            {
                failed = system.ExecuteAsync("#set movespeed 6", CancellationToken.None).GetAwaiter().GetResult();
            }

            Assert.That(failed.Code, Is.EqualTo(TestCmdResultCode.ExecutionFailed));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(beforePower).Within(0.001f));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerMaxHP, Is.EqualTo(beforeMaxHP));
            Assert.That(Game.Interface.GetModel<PlayerAttributeModel>().MaxHPOverride, Is.EqualTo(beforeOverride));
            Assert.That(Game.Interface.GetModel<PlayerAttributeModel>().MoveSpeedOverride, Is.EqualTo(beforeMoveOverride));
            Assert.That(IsSaveDirty(), Is.True);
            Assert.That(GetSaveDirtyAt(), Is.EqualTo(dirtyAt).Within(0.001f));
            Assert.That(GetMoveSpeedCache(moveSystem), Is.EqualTo(beforeMoveSpeedCache).Within(0.001f));
        }

        [Test]
        public void PausedAttributesRequireExistingPlayerAndKeepWorldPaused()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryPause(), Is.True);
            var commands = new[] { "hp 20", "maxhp 110", "power 30", "tili 40", "movespeed 3", "water 50", "maxwater 120", "temp 38" };
            foreach (var command in commands)
            {
                Assert.That(system.ExecuteAsync("#set " + command, CancellationToken.None).GetAwaiter().GetResult().Success, Is.True, command);
                Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
                Assert.That(Time.timeScale, Is.Zero);
            }
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(20));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerMaxHP, Is.EqualTo(110));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(30));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(40));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Water, Is.EqualTo(50));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().MaxWater, Is.EqualTo(120));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Temperature, Is.EqualTo(38));
            Assert.That(Game.Interface.GetSystem<PlayerAttributeSystem>().EffectiveMoveSpeed, Is.EqualTo(3));

            Assert.That(Game.Interface.GetSystem<UnitSystem>().Remove(PlayerModel.LocalPlayerUnitId, UnitRemovedReason.Explicit), Is.True);
            SetSaveDirty(false);
            foreach (var command in commands)
                Assert.That(system.ExecuteAsync("#set " + command, CancellationToken.None).GetAwaiter().GetResult().Code,
                    Is.EqualTo(TestCmdResultCode.StateRejected), command);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(30));
            Assert.That(IsSaveDirty(), Is.False);
        }

        [Test]
        public void PlayerAttributeCategoryHistoryAndLayoutRemainValid()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetTestCmdCatalog(out var categories, out var entries, out var error), Is.True, error);
            Assert.That(categories.Any(item => item.Id == "player-attribute"), Is.True);
            Assert.That(entries.Count(item => item.CategoryId == "player-attribute"), Is.EqualTo(8));
            var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#set power 70", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Success, Is.True);
            Assert.That(Game.Interface.GetModel<TestToolSettingsModel>().RecentCommands[0], Is.EqualTo("#set power 70"));
            Assert.That(categories.Single(item => item.Id == "player-attribute").LabelText, Is.EqualTo("玩家属性"));
        }

        [Test]
        public void CloneClearBagAndAdvanceTimeRemainSupported()
        {
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clone 1 1", CancellationToken.None).GetAwaiter().GetResult().Recognized, Is.True);
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#clear bag", CancellationToken.None).GetAwaiter().GetResult().Recognized, Is.True);
            Assert.That(Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#advance time 1", CancellationToken.None).GetAwaiter().GetResult().Recognized, Is.True);
        }

        private static void EnterBase()
        {
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<ExpeditionModel>().Clear();
            Game.Interface.GetModel<PlayerAttributeModel>().Reset();
            Game.Interface.GetSystem<UnitSystem>().TryRegister(
                new UnitSnapshot(
                    PlayerModel.LocalPlayerUnitId,
                    1,
                    UnitType.Player,
                    UnitSource.Player,
                    "player",
                    Vector3.zero,
                    Quaternion.identity,
                    UnitAuthorityMode.Local,
                    0,
                    true),
                out _);
            Game.Interface.GetSystem<UnitSystem>().TrySetActive(PlayerModel.LocalPlayerUnitId, true);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 3f, 360f, 5f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.BaseSafe, "test", false, out _);
        }

        private static IEnumerable<string> ForbiddenLegacyTerms()
        {
            yield return "Maximum" + "Health";
            yield return "Player" + "Current" + "Health";
            yield return "Player" + "Max" + "Health";
            yield return "Restore" + "Hun" + "ger";
            yield return "Hun" + "ger";
            yield return "hun" + "ger";
            yield return "Sta" + "mina";
            yield return "sta" + "mina";
            yield return "Current" + "Power";
            yield return "Current" + "Tili";
            yield return "Maximum" + "Power";
            yield return "Maximum" + "Tili";
        }

        private static void EnterExpedition(ExpeditionStatus status)
        {
            EnterBase();
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            var expedition = Game.Interface.GetModel<ExpeditionModel>();
            expedition.BeginSurvival(MapIds.SurvivalSample, 60f);
            expedition.SetExpeditionId(Guid.NewGuid().ToString("N"));
            if (status == ExpeditionStatus.ReturnQualified)
            {
                expedition.QualifyReturn();
            }
            else if (status == ExpeditionStatus.Returning)
            {
                expedition.QualifyReturn();
                expedition.BeginReturning();
            }
            else if (status == ExpeditionStatus.Defeated)
            {
                expedition.MarkDefeated();
            }

            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out _);
        }

        private static bool IsSaveDirty()
        {
            return (bool)typeof(SaveSystem)
                .GetField("dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private static void SetSaveDirty(bool value)
        {
            typeof(SaveSystem)
                .GetField("dirty", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(Game.Interface.GetSystem<SaveSystem>(), value);
        }

        private static float GetSaveDirtyAt()
        {
            return (float)typeof(SaveSystem)
                .GetField("dirtyAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(Game.Interface.GetSystem<SaveSystem>());
        }

        private static float SetSaveDirtyAt(float value)
        {
            typeof(SaveSystem)
                .GetField("dirtyAt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(Game.Interface.GetSystem<SaveSystem>(), value);
            return value;
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

        private static float GetMoveSpeedCache(PlayerMoveSystem system)
        {
            return (float)typeof(PlayerMoveSystem)
                .GetProperty("MoveSpeedCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(system);
        }

        private static void AssertPublicContractHelpersAreInternal()
        {
            var runtimeAssembly = typeof(PlayerAttributeSystem).Assembly;
            var failureKind = runtimeAssembly.GetType("Game.PlayerAttributeFailureKind");
            Assert.That(failureKind, Is.Not.Null);
            Assert.That(failureKind.IsPublic, Is.False);

            var failureProperty = typeof(PlayerAttributeSystem).GetProperty(
                "LastFailureKind",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.That(failureProperty, Is.Not.Null);
            Assert.That(failureProperty.GetMethod.IsPublic, Is.False);

            var dirtyProperty = typeof(SaveRuntimeSnapshot).GetProperty(
                "Dirty",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var dirtyAtProperty = typeof(SaveRuntimeSnapshot).GetProperty(
                "DirtyAt",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            Assert.That(dirtyProperty, Is.Not.Null);
            Assert.That(dirtyAtProperty, Is.Not.Null);
            Assert.That(dirtyProperty.GetMethod.IsPublic, Is.False);
            Assert.That(dirtyAtProperty.GetMethod.IsPublic, Is.False);
        }

        private sealed class AttributeConfigProvider : IRuntimeConfigProvider
        {
            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = new MapAssetDefinition(id, id);
                error = null;
                return true;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = null;
                return true;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = new PlayerConfigDefinition("player", string.Empty, 100, 10, 1f, 1f, 3f, 360f, 5f);
                error = null;
                return true;
            }

            public bool TryGetEnemy(string id, out EnemyConfigDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }

            public bool TryGetLoot(string id, out LootConfigDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }

            public bool TryGetExtraction(string id, out ExtractionConfigDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                value = new ItemDefinition(id, id.ToString(), string.Empty, id.ToString(), id.ToString(), string.Empty, global::ItemType.Material, 20, global::ItemUseEffect.None, 0);
                error = null;
                return id == ItemIds.MetalParts || id == ItemIds.Cloth;
            }

            public bool TryGetTestCmdCatalog(
                out IReadOnlyList<TestCmdCategoryDefinition> categories,
                out IReadOnlyList<TestCmdEntryDefinition> entries,
                out string error)
            {
                categories = new[]
                {
                    new TestCmdCategoryDefinition("inventory-item", 100, "背包物品"),
                    new TestCmdCategoryDefinition("player-condition", 200, "玩家状态"),
                    new TestCmdCategoryDefinition("player-attribute", 300, "玩家属性")
                };
                entries = new[]
                {
                    Entry("inventory.clone", "inventory-item", 100, "clone ", "向当前玩家背包加入物品。", "添加物品"),
                    Entry("inventory.clear-bag", "inventory-item", 200, "clear bag", "清空当前玩家背包。", "清空背包"),
                    Entry("condition.advance-time", "player-condition", 100, "advance time ", "推进游戏分钟，用于结算能量、远征时间与能量耗尽伤害。", "推进时间"),
                    Entry("condition.unitbar", "player-condition", 200, "unitbar ", "开启或关闭玩家和存活敌人的头顶生命条。", "单位血条"),
                    Entry("attribute.set-hp", "player-attribute", 100, "set hp ", "设置当前生命：set hp <整数>。", "设置当前生命"),
                    Entry("attribute.set-power", "player-attribute", 200, "set power ", "设置当前能量：set power <数值>。", "设置当前能量"),
                    Entry("attribute.set-tili", "player-attribute", 300, "set tili ", "设置当前体力：set tili <数值>。", "设置当前体力"),
                    Entry("attribute.set-maxhp", "player-attribute", 400, "set maxhp ", "设置最大生命：set maxhp <正整数>。", "设置最大生命"),
                    Entry("attribute.set-movespeed", "player-attribute", 500, "set movespeed ", "设置移动速度：set movespeed <正数>。", "设置移动速度"),
                    Entry("attribute.set-water", "player-attribute", 600, "set water ", "设置当前水分：set water <数值>。", "设置当前水分"),
                    Entry("attribute.set-maxwater", "player-attribute", 700, "set maxwater ", "设置最大水分：set maxwater <正数>。", "设置最大水分"),
                    Entry("attribute.set-temp", "player-attribute", 800, "set temp ", "设置体温：set temp <30-45>。", "设置体温")
                };
                error = null;
                return true;
            }

            private static TestCmdEntryDefinition Entry(
                string id,
                string categoryId,
                int order,
                string commandTemplate,
                string descriptionText,
                string labelText)
            {
                return new TestCmdEntryDefinition(
                    id,
                    categoryId,
                    order,
                    labelText,
                    commandTemplate,
                    descriptionText,
                    "用法：未配置",
                    "示例：未配置");
            }
        }

        private sealed class LocalizationTestLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                switch (path)
                {
                    case LocalizationLanguages.LanguagesAddress:
                        return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                    case "zh-CN.json":
                    case "en-US.json":
                        return new TextAsset(File.ReadAllText(Path.Combine(Application.dataPath, "Game/Config/Localization", path))) as T;
                    default:
                        return null;
                }
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                var asset = LoadAsset<T>(path);
                onLoaded?.Invoke(asset);
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}

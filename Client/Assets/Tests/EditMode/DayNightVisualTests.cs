using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tests.EditMode
{
    public sealed class DayNightVisualTests
    {
        [TestCase(0f, 0f)]
        [TestCase(360f, 0.25f)]
        [TestCase(720f, 0.5f)]
        [TestCase(1080f, 0.75f)]
        [TestCase(1440f, 0f)]
        [TestCase(1800f, 0.25f)]
        public void CalculateDayProgress_WrapsExpeditionMinutesWithinOneDay(float minutes, float expected)
        {
            Assert.That(DayNightVisualSystem.CalculateDayProgress(minutes), Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void CalculateDayProgress_NegativeMinutesWrapToPreviousDay()
        {
            Assert.That(DayNightVisualSystem.CalculateDayProgress(-360f), Is.EqualTo(0.75f).Within(0.0001f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void CalculateDayProgress_NonFiniteInputFallsBackToMidnight(float minutes)
        {
            Assert.That(DayNightVisualSystem.CalculateDayProgress(minutes), Is.Zero);
        }

        [Test]
        public void TwilightCurve_SeparatesThreeAmDeepNightFromFiveAmPredawn()
        {
            var threeAmSolarHeight = SolarHeightAtMinutes(180f);
            var fiveAmSolarHeight = SolarHeightAtMinutes(300f);

            Assert.That(DayNightView.CalculateTwilightAmount(threeAmSolarHeight), Is.LessThan(0.05f));
            Assert.That(DayNightView.CalculateTwilightAmount(fiveAmSolarHeight), Is.GreaterThan(0.7f));
        }

        [Test]
        public void LateUpdate_PublishesFractionalExpeditionProgress()
        {
            ResetDayNightArchitecture();
            var architecture = DayNightTestArchitecture.Interface;
            var snapshots = new List<DayNightVisualSnapshot>();
            var unregister = architecture.RegisterEvent<DayNightVisualChangedEvent>(evt => snapshots.Add(evt.Snapshot));

            try
            {
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
                var expedition = architecture.GetModel<ExpeditionModel>();
                expedition.BeginSurvival(MapIds.SurvivalSample, PlayerConditionRules.GameMinutesPerDay);

                LifeCycle.LateUpdate();
                expedition.AdvanceGameMinutes(0.25f);
                LifeCycle.LateUpdate();

                Assert.That(snapshots, Has.Count.EqualTo(2));
                Assert.That(snapshots[1].FollowsExpeditionTime, Is.True);
                Assert.That(snapshots[1].DayProgress,
                    Is.EqualTo(0.25f / PlayerConditionRules.GameMinutesPerDay).Within(0.000001f));
            }
            finally
            {
                unregister.UnRegister();
                architecture.Deinit();
                ResetDayNightArchitecture();
            }
        }

        [Test]
        public void MapSkyboxView_AppliesAndRestoresReflectionIntensityWithSkyboxOwnership()
        {
            var gameObject = new GameObject("MapSkyboxViewTest");
            var view = gameObject.AddComponent<MapSkyboxView>();
            var originalSkybox = RenderSettings.skybox;
            var originalReflectionIntensity = RenderSettings.reflectionIntensity;
            const float baselineReflectionIntensity = 0.83f;

            try
            {
                RenderSettings.reflectionIntensity = baselineReflectionIntensity;
                InvokePrivate(view, "CaptureBaseline");

                view.Apply(new DayNightVisualSnapshot(0.5f, false));

                Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(0.5f).Within(0.0001f));

                InvokePrivate(view, "RestoreBaseline");
                Assert.That(RenderSettings.skybox, Is.SameAs(originalSkybox));
                Assert.That(RenderSettings.reflectionIntensity,
                    Is.EqualTo(baselineReflectionIntensity).Within(0.0001f));
            }
            finally
            {
                RenderSettings.skybox = originalSkybox;
                RenderSettings.reflectionIntensity = originalReflectionIntensity;
                Object.DestroyImmediate(gameObject);
            }
        }
        [Test]
        public void SavedDaylightOrigin_DrivesVisualClockThroughMidnight()
        {
            ResetDayNightArchitecture();var architecture=DayNightTestArchitecture.Interface;var snapshots=new List<DayNightVisualSnapshot>();
            var unregister=architecture.RegisterEvent<DayNightVisualChangedEvent>(evt=>snapshots.Add(evt.Snapshot));
            try
            {
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample,null);var expedition=architecture.GetModel<ExpeditionModel>();expedition.BeginSurvival(MapIds.SurvivalSample,1440);
                typeof(ExpeditionModel).GetMethod("InitializeClock",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(expedition,new object[]{480});
                LifeCycle.LateUpdate();Assert.That(snapshots[snapshots.Count-1].DayProgress,Is.EqualTo(480f/1440).Within(.000001f));
                expedition.AdvanceGameMinutes(120);LifeCycle.LateUpdate();Assert.That(snapshots[snapshots.Count-1].DayProgress,Is.EqualTo(600f/1440).Within(.000001f));
                expedition.AdvanceGameMinutes(840);LifeCycle.LateUpdate();Assert.That(snapshots[snapshots.Count-1].DayProgress,Is.Zero.Within(.000001f));
            }
            finally{unregister.UnRegister();architecture.Deinit();ResetDayNightArchitecture();}
        }

        [Test]
        public void BaseClock_AdvancesFromNoonAndWrapsWithoutAdvancingExpedition()
        {
            WithBaseClock(architecture =>
            {
                var system = architecture.GetSystem<DayNightVisualSystem>();
                Assert.That(system.GetSnapshot().DayProgress, Is.EqualTo(0.5f));
                TickBaseClock(system, 720f);
                Assert.That(system.GetSnapshot().DayProgress, Is.Zero.Within(0.000001f));
                TickBaseClock(system, 360f);
                Assert.That(system.GetSnapshot().DayProgress, Is.EqualTo(0.25f).Within(0.000001f));
                Assert.That(system.GetSnapshot().FollowsExpeditionTime, Is.False);
                Assert.That(architecture.GetModel<ExpeditionModel>().ElapsedGameMinutes, Is.Zero);
            });
        }

        [Test]
        public void BaseClock_PauseLoadingAndInvalidDeltaDoNotAdvance()
        {
            WithBaseClock(architecture =>
            {
                var system = architecture.GetSystem<DayNightVisualSystem>();
                var state = architecture.GetModel<AppStateModel>();
                foreach (var value in new[] { AppState.Paused, AppState.Loading })
                {
                    state.CurrentState.Value = value;
                    TickBaseClock(system, 60f);
                }
                state.CurrentState.Value = AppState.Playing;
                var map = architecture.GetModel<MapSceneModel>();
                map.SetStatus(MapLoadStatus.Loading);
                TickBaseClock(system, 60f);
                map.SetLoaded(MapIds.Base, null);
                foreach (var delta in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                    TickBaseClock(system, delta);
                Assert.That(system.GetSnapshot().DayProgress, Is.EqualTo(0.5f));
            });
        }

        [Test]
        public void BaseClock_ExpeditionRoundTripRetainsBaseTimeAndPublishesChanges()
        {
            WithBaseClock(architecture =>
            {
                var system = architecture.GetSystem<DayNightVisualSystem>();
                var snapshots = new List<DayNightVisualSnapshot>();
                var unregister = architecture.RegisterEvent<DayNightVisualChangedEvent>(evt => snapshots.Add(evt.Snapshot));
                try
                {
                    LifeCycle.LateUpdate();
                    TickBaseClock(system, 60f);
                    LifeCycle.LateUpdate();
                    Assert.That(snapshots, Has.Count.EqualTo(2));
                    var progress = system.GetSnapshot().DayProgress;
                    var map = architecture.GetModel<MapSceneModel>();
                    map.SetLoaded(MapIds.SurvivalSample, null);
                    architecture.GetModel<ExpeditionModel>().BeginSurvival(MapIds.SurvivalSample, 1440f);
                    TickBaseClock(system, 300f);
                    LifeCycle.LateUpdate();
                    Assert.That(system.GetSnapshot().FollowsExpeditionTime, Is.True);
                    map.SetLoaded(MapIds.Base, null);
                    LifeCycle.LateUpdate();
                    Assert.That(system.GetSnapshot().DayProgress, Is.EqualTo(progress));
                    Assert.That(snapshots[snapshots.Count - 1].DayProgress, Is.EqualTo(progress));
                    LifeCycle.LateUpdate();
                    Assert.That(snapshots, Has.Count.EqualTo(4));
                }
                finally { unregister.UnRegister(); }
            });
        }

        [Test]
        public void BaseClock_OnlineMapStaysAtNoonAndUnsavedNewSessionUsesDefault()
        {
            WithBaseClock(architecture =>
            {
                var system = architecture.GetSystem<DayNightVisualSystem>();
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null, MapPreparationMode.OnlinePlayersOnly);
                TickBaseClock(system, 300f);
                Assert.That(system.GetSnapshot().DayProgress, Is.EqualTo(0.5f));
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                TickBaseClock(system, 300f);
                Assert.That(system.GetSnapshot().DayProgress, Is.GreaterThan(0.5f));
            });
            WithBaseClock(architecture => Assert.That(
                architecture.GetSystem<DayNightVisualSystem>().GetSnapshot().DayProgress, Is.EqualTo(0.5f)));
        }

        private static void TickBaseClock(DayNightVisualSystem system, float seconds)
        {
            system.TickBaseClock(seconds);
        }

        [Test]
        public void BaseClock_SaveAndNewSessionRestoreLastMomentWithoutOfflineAdvance()
        {
            BaseSaveData saved = null;
            WithBaseClock(architecture =>
            {
                var save = architecture.GetSystem<SaveSystem>();
                var initial = new BaseSaveData();
                Assert.That(save.CaptureBaseSections(initial, out var error), Is.True, error);
                Assert.That(initial.Sections.ContainsKey("another-day.base-day-night"), Is.True,
                    "基地时刻必须注册为正式基地存档分区");
                Assert.That(new SaveFileStore(saveRoot).SaveBase(initial, out error), Is.True, error);
                architecture.GetModel<SaveModel>().SetBase(initial, true);
                TickBaseClock(architecture.GetSystem<DayNightVisualSystem>(), 390.5f);
                Assert.That(save.Flush(out error), Is.True, error);
                var loaded = new SaveFileStore(saveRoot).InspectBase();
                Assert.That(loaded.IsValid, Is.True, loaded.Error);
                Assert.That(loaded.Data.Sections["another-day.base-day-night"]["minuteOfDay"].Value<float>(),
                    Is.EqualTo(1110.5f).Within(0.0001f));
                // 新会话恢复磁盘重读的数据，而不是复用上个会话的内存时刻。
                saved = loaded.Data;
            });
            WithBaseClock(architecture =>
            {
                Assert.That(architecture.GetSystem<SaveSystem>().CommitBaseRuntime(saved, out var error),
                    Is.True, error);
                Assert.That(architecture.GetSystem<DayNightVisualSystem>().GetSnapshot().DayProgress,
                    Is.EqualTo(1110.5f / 1440f).Within(0.000001f));
            });
        }

        [Test]
        public void BaseClock_MissingOldSectionResetsPreviousSaveTimeToNoon()
        {
            WithBaseClock(architecture =>
            {
                var model = architecture.GetModel<BaseDayNightModel>();
                model.Restore(new JObject { ["schemaVersion"] = 1, ["minuteOfDay"] = 1250.25f });
                Assert.That(architecture.GetSystem<DayNightVisualSystem>().GetSnapshot().DayProgress,
                    Is.EqualTo(1250.25f / 1440f).Within(0.000001f));
                var oldSave = new BaseSaveData();
                var save = architecture.GetSystem<SaveSystem>();
                Assert.That(save.CaptureBaseSections(oldSave, out var error), Is.True, error);
                oldSave.Sections.Remove(BaseDayNightModel.SaveSectionId);
                Assert.That(save.CommitBaseRuntime(oldSave, out error), Is.True, error);
                Assert.That(architecture.GetSystem<DayNightVisualSystem>().GetSnapshot().DayProgress, Is.EqualTo(0.5f));
            });
        }

        [Test]
        public void BaseClock_NewGameStartsAtDefaultWithoutBorrowingPreviousSaveClock()
        {
            WithBaseClock(architecture =>
            {
                var clock = architecture.GetModel<BaseDayNightModel>();
                clock.Restore(new JObject { ["schemaVersion"] = 1, ["minuteOfDay"] = 1260f });
                var save = architecture.GetSystem<SaveSystem>();
                Assert.That(save.CreateBase(out var data, out var error), Is.True, error);
                Assert.That(save.CommitBaseRuntime(data, out error), Is.True, error);
                Assert.That(architecture.GetSystem<DayNightVisualSystem>().GetSnapshot().DayProgress, Is.EqualTo(0.5f));
            });
        }

        [TestCase(-1f)]
        [TestCase(1440f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void BaseClock_InvalidSavedMinuteIsRejectedWithoutChangingCurrentTime(float minute)
        {
            var model = new BaseDayNightModel();
            model.Restore(new JObject { ["schemaVersion"] = 1, ["minuteOfDay"] = 1080.5f });
            var invalid = new JObject { ["schemaVersion"] = 1, ["minuteOfDay"] = minute };
            Assert.That(model.Validate(invalid, out _), Is.False);
            Assert.Throws<System.InvalidOperationException>(() => model.Restore(invalid));
            Assert.That(model.MinuteOfDay, Is.EqualTo(1080.5f));
        }

        [Test]
        public void BaseClock_ApplicationPauseSavesFractionAfterPreviousFlush()
        {
            WithBaseClock(architecture =>
            {
                var save = architecture.GetSystem<SaveSystem>();
                var initial = new BaseSaveData();
                Assert.That(save.CaptureBaseSections(initial, out var error), Is.True, error);
                Assert.That(new SaveFileStore(saveRoot).SaveBase(initial, out error), Is.True, error);
                architecture.GetModel<SaveModel>().SetBase(initial, true);
                var system = architecture.GetSystem<DayNightVisualSystem>();
                TickBaseClock(system, 60f);
                Assert.That(save.Flush(out error), Is.True, error);
                TickBaseClock(system, 0.125f);
                LifeCycle.ApplicationPause(true);
                var loaded = new SaveFileStore(saveRoot).InspectBase();
                Assert.That(loaded.IsValid, Is.True, loaded.Error);
                Assert.That(loaded.Data.Sections[BaseDayNightModel.SaveSectionId]["minuteOfDay"].Value<float>(),
                    Is.EqualTo(780.125f).Within(0.0001f));
                Assert.That(system.GetSnapshot().DayProgress, Is.EqualTo(780.125f / 1440f).Within(0.000001f));
            });
        }

        [Test]
        public void BaseScene_UsesProceduralSkyAndDisablesFixedDayDome()
        {
            const string path = "Assets/Game/Scenes/BaseMap.unity";
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                MapContext context = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (context == null) context = root.GetComponentInChildren<MapContext>(true);
                Assert.That(context, Is.Not.Null);
                var sky = context.MapContentRoot.GetComponent<MapProceduralSkyView>();
                Assert.That(sky, Is.Not.Null);
                Assert.That(sky.enabled, Is.True);
                var template = new SerializedObject(sky).FindProperty("skyTemplate").objectReferenceValue as Material;
                Assert.That(template, Is.Not.Null);
                Assert.That(template.HasProperty("_SunDirection"), Is.True);
                Assert.That(template.HasProperty("_CloudTex"), Is.True);
                var foundDome = false;
                foreach (var renderer in context.MapContentRoot.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.name == "基地天空穹顶")
                    {
                        foundDome = true;
                        Assert.That(renderer.enabled, Is.False, "固定白天穹顶不能遮住昼夜天空");
                    }
                Assert.That(foundDome, Is.True);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void WithBaseClock(System.Action<IArchitecture> check)
        {
            ResetDayNightArchitecture();
            var architecture = DayNightTestArchitecture.Interface;
            try
            {
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                architecture.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
                check(architecture);
            }
            finally
            {
                architecture.Deinit();
                ResetDayNightArchitecture();
            }
        }

        private static float SolarHeightAtMinutes(float minutes)
        {
            var progress = DayNightVisualSystem.CalculateDayProgress(minutes);
            return Mathf.Sin((progress - 0.25f) * Mathf.PI * 2f);
        }

        private static void ResetDayNightArchitecture()
        {
            typeof(Architecture<DayNightTestArchitecture>)
                .GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
            LifeCycle.ClearForTests();
            if (saveRoot != null && Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
            saveRoot = null;
        }

        private static string saveRoot;

        private static void InvokePrivate(object target, string methodName)
        {
            target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }

        private sealed class DayNightTestArchitecture : Architecture<DayNightTestArchitecture>
        {
            protected override void Init()
            {
                RegisterModel(new MapSceneModel());
                RegisterModel(new ExpeditionModel());
                RegisterModel(new BaseDayNightModel());
                RegisterModel(new AppStateModel());
                RegisterModel(new SaveModel());
                RegisterModel(new CurrencyModel());
                RegisterModel(new BasePreparationModel());
                RegisterModel(new ShopUnlockModel());
                RegisterModel(new WeaponEquipmentModel());
                RegisterModel(new ClothingEquipmentModel());
                RegisterModel(new PlayerBagModel());
                RegisterUtility<IRuntimeConfigProvider>(new LubanRuntimeConfigProvider(() => new global::Tables(name =>
                    new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Application.dataPath, "Game/Config", name + ".bytes"))))));
                RegisterSystem(new SaveSystem());
                saveRoot = Path.Combine(Path.GetTempPath(), "AnotherDayBaseClock", System.Guid.NewGuid().ToString("N"));
                this.GetSystem<SaveSystem>().ConfigureForTests(saveRoot);
                Assert.That(this.GetSystem<SaveSystem>().RegisterSection(this.GetModel<PlayerBagModel>(), out var bagError),
                    Is.True, bagError);
                RegisterSystem(new WeaponEquipmentSystem());
                RegisterSystem(new ClothingEquipmentSystem());
                var seed = new BaseSaveData();
                seed.Sections[CurrencyModel.SaveSectionId] = this.GetModel<CurrencyModel>().Capture();
                Assert.That(BasePreparationPolicy.Initialize(seed, this.GetUtility<IRuntimeConfigProvider>(), true,
                    out var error), Is.True, error);
                foreach (var provider in new IScopedSaveSectionProvider[]
                {
                    this.GetModel<CurrencyModel>(), this.GetModel<BasePreparationModel>(), this.GetModel<ShopUnlockModel>()
                })
                {
                    provider.Restore(seed.Sections[provider.SectionId]);
                    Assert.That(this.GetSystem<SaveSystem>().RegisterSection(provider, out error), Is.True, error);
                }
                RegisterSystem(new DayNightVisualSystem());
            }
        }
    }
}

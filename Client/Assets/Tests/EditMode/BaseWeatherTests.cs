using Game;
using NUnit.Framework;
using System;
using System.Reflection;
using QFramework;

namespace Tests.EditMode
{
    public sealed class BaseWeatherTests
    {
        private object Create()
        {
            var type = typeof(DayNightVisualSystem).Assembly.GetType("Game.BaseWeatherTimeline");
            Assert.That(type, Is.Not.Null);
            return Activator.CreateInstance(type);
        }
        private static object Call(object timeline, string method, params object[] args) => timeline.GetType().GetMethod(method).Invoke(timeline, args);
        private static float Value(object timeline, string property) => (float)Call(timeline, "GetSnapshot").GetType().GetProperty(property).GetValue(Call(timeline, "GetSnapshot"));
        private static string Stage(object timeline) => (string)Call(timeline, "GetSnapshot").GetType().GetProperty("Stage").GetValue(Call(timeline, "GetSnapshot"));
        [Test]
        public void RainClear_RetainsWetnessThenDries()
        {
            var t = Create(); Call(t,"SetMode","rain"); Assert.That(Value(t,"RainAmount"),Is.EqualTo(1));
            Call(t,"SetMode","clear"); Assert.That(Value(t,"RainAmount"),Is.Zero); Assert.That(Value(t,"Wetness"),Is.EqualTo(1));
            Call(t,"Advance",45f); Assert.That(Value(t,"Wetness"),Is.EqualTo(.5f).Within(.001));
            Call(t,"SetMode","clear"); Call(t,"Advance",45f); Assert.That(Value(t,"Wetness"),Is.Zero);
        }
        [TestCase(30f,0f,0f)]
        [TestCase(50f,1f,1f)]
        [TestCase(110f,1f,1f)]
        [TestCase(130f,0f,1f)]
        [TestCase(175f,0f,.5f)]
        [TestCase(220f,0f,0f)]
        public void Demo_CrossesPhaseBoundaries(float elapsed,float rain,float wet)
        {
            var t=Create(); Call(t,"SetMode","demo"); Call(t,"Advance",elapsed);
            Assert.That(Value(t,"RainAmount"),Is.EqualTo(rain).Within(.001));
            Assert.That(Value(t,"Wetness"),Is.EqualTo(wet).Within(.001));
        }
        [Test]
        public void InvalidMode_DoesNotCancelDemo()
        {
            var t=Create();Call(t,"SetMode","demo");Call(t,"Advance",40f);
            Assert.That(Call(t,"SetMode","invalid"),Is.EqualTo(false));Call(t,"Advance",10f);
            Assert.That(Value(t,"RainAmount"),Is.EqualTo(1));
        }
        [Test]
        public void Dry_CancelsRainAndDemo()
        {
            var t=Create();Call(t,"SetMode","rain");Call(t,"SetMode","dry");Call(t,"Advance",10000f);
            Assert.That(Value(t,"Wetness"),Is.Zero);Assert.That(Value(t,"RainAmount"),Is.Zero);
        }
        [Test]
        public void NonFiniteOrNegativeTime_DoesNotAdvance()
        {
            var t=Create();Call(t,"SetMode","demo");foreach(var delta in new[]{float.NaN,float.PositiveInfinity,-1f})Call(t,"Advance",delta);
            Assert.That(Stage(t),Is.EqualTo("晴天"));Assert.That(Value(t,"Wetness"),Is.Zero);
        }
        [Test]
        public void WeatherSystem_HasExplicitDomainOwner()
        {
            Assert.That(typeof(DayNightVisualSystem).Assembly.GetType("Game.BaseWeatherSystem"), Is.Not.Null,
                "基地天气应由领域 System 持有，不由雨粒子推进状态");
        }
        private static void WithDomain(Action<IArchitecture> check)
        {
            typeof(Architecture<WeatherArchitecture>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);
            LifeCycle.ClearForTests();var architecture=WeatherArchitecture.Interface;
            try
            {
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base,null);
                architecture.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;check(architecture);
            }
            finally
            {
                architecture.Deinit();typeof(Architecture<WeatherArchitecture>).GetField("mArchitecture",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,null);LifeCycle.ClearForTests();
            }
        }
        [Test]
        public void Domain_PauseFreezesDryingAndResumeContinues()
        {
            WithDomain(a=>{var w=a.GetSystem<BaseWeatherSystem>();w.TrySetMode("rain",out _);w.TrySetMode("clear",out _);
                a.GetModel<AppStateModel>().CurrentState.Value=AppState.Paused;w.Tick(45);Assert.That(w.GetSnapshot().Wetness,Is.EqualTo(1));
                a.GetModel<AppStateModel>().CurrentState.Value=AppState.Playing;w.Tick(45);Assert.That(w.GetSnapshot().Wetness,Is.EqualTo(.5f).Within(.001));});
        }
        [Test]
        public void Domain_NonBaseAndOnlineRejectMutation()
        {
            WithDomain(a=>{var w=a.GetSystem<BaseWeatherSystem>();a.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample,null);
                Assert.That(w.TrySetMode("rain",out _),Is.False);Assert.That(w.GetSnapshot().IsActive,Is.False);
                a.GetModel<MapSceneModel>().SetLoaded(MapIds.Base,null,MapPreparationMode.OnlinePlayersOnly);Assert.That(w.TrySetMode("rain",out _),Is.False);});
        }
        [Test]
        public void Domain_MapLoadingResetsWeather()
        {
            WithDomain(a=>{var w=a.GetSystem<BaseWeatherSystem>();w.TrySetMode("rain",out _);a.SendEvent(new MapLoadingStartedEvent(MapIds.SurvivalSample));
                Assert.That(w.GetSnapshot().RainAmount,Is.Zero);Assert.That(w.GetSnapshot().Wetness,Is.Zero);});
        }
        [Test]
        public void Command_ActualParserRoutesWeatherAndRejectsInvalidSyntax()
        {
            WithDomain(a=>{var cmd=a.GetSystem<TestCmdSystem>();var method=typeof(TestCmdSystem).GetMethod("ExecuteClientBody",BindingFlags.Instance|BindingFlags.NonPublic);
                var rain=(TestCmdExecutionResult)method.Invoke(cmd,new object[]{"weather rain"});Assert.That(rain.Success,Is.True);
                var bad=(TestCmdExecutionResult)method.Invoke(cmd,new object[]{"weather dry extra"});Assert.That(bad.Success,Is.False);
                Assert.That(a.GetSystem<BaseWeatherSystem>().GetSnapshot().RainAmount,Is.EqualTo(1));
                var status=(TestCmdExecutionResult)method.Invoke(cmd,new object[]{"weather status"});Assert.That(status.Success,Is.True);Assert.That(status.Message,Does.Contain("湿润度"));
                method.Invoke(cmd,new object[]{"weather clear"});Assert.That(a.GetSystem<BaseWeatherSystem>().GetSnapshot().Wetness,Is.EqualTo(1));});
        }
        private sealed class WeatherArchitecture:Architecture<WeatherArchitecture>
        {
            protected override void Init(){RegisterModel(new MapSceneModel());RegisterModel(new AppStateModel());RegisterSystem(new BaseWeatherSystem());RegisterSystem(new TestCmdSystem());}
        }
    }
}

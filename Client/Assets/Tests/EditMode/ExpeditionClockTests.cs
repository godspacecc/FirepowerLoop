using System;
using System.Reflection;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ExpeditionClockTests
    {
        private const string Id = "abcdef0123456789abcdef0123456789";
        private static void Start(ExpeditionModel model, int minute)
        {
            var method = typeof(ExpeditionModel).GetMethod("InitializeClock", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "新远征应提供独立钟表起点");
            method.Invoke(model, new object[] { minute });
        }
        private static float Clock(ExpeditionModel model)
        {
            var property = typeof(ExpeditionModel).GetProperty("ClockGameMinutes");
            Assert.That(property, Is.Not.Null, "钟表时间不能直接使用累计耗时");
            return (float)property.GetValue(model);
        }
        private static ExpeditionModel NewModel()
        {
            var model = new ExpeditionModel();
            model.BeginConfiguredExpedition(MapIds.SurvivalSample, Id, false, 1440, 0);
            return model;
        }
        [Test] public void RandomStart_IsDaytimeAndCanSelectBothEndpoints()
        {
            var type = typeof(ExpeditionModel).Assembly.GetType("Game.ExpeditionClockRules");
            Assert.That(type, Is.Not.Null);
            var method = type.GetMethod("DrawStartMinuteOfDay");
            Assert.That((int)method.Invoke(null, new object[] { new EndpointRandom(false) }), Is.EqualTo(480));
            Assert.That((int)method.Invoke(null, new object[] { new EndpointRandom(true) }), Is.EqualTo(720));
            var random = new Random(37);
            for (int i = 0; i < 100; i++) Assert.That((int)method.Invoke(null, new object[] { random }), Is.InRange(480, 720));
        }
        [Test] public void EightAm_StartHasZeroElapsed()
        { var model = NewModel(); Start(model, 480); Assert.That(Clock(model), Is.EqualTo(480)); Assert.That(model.ElapsedGameMinutes, Is.Zero); }
        [Test] public void Noon_StartHasZeroElapsed()
        { var model = NewModel(); Start(model, 720); Assert.That(Clock(model), Is.EqualTo(720)); Assert.That(model.ElapsedGameMinutes, Is.Zero); }
        [Test] public void TwoHours_FromEightAmShowsTenAm()
        { var model = NewModel(); Start(model, 480); model.AdvanceGameMinutes(120); Assert.That(Clock(model), Is.EqualTo(600)); }
        [Test] public void SixteenHours_FromEightAmCrossesMidnight()
        { var model = NewModel(); Start(model, 480); model.AdvanceGameMinutes(960); Assert.That(Clock(model), Is.EqualTo(1440)); }
        [Test] public void LegacySave_RestoresZeroStart()
        {
            var source = JsonConvert.DeserializeObject<ExpeditionSaveData>("{\"Version\":3,\"ExpeditionId\":\"" + Id + "\",\"MapId\":\"" + MapIds.SurvivalSample + "\",\"ElapsedGameMinutes\":120}");
            var model = new ExpeditionModel(); Assert.That(model.TryRestoreSave(source, out var error), Is.True, error); Assert.That(Clock(model), Is.EqualTo(120));
        }
        [Test] public void SaveRestore_PreservesDrawnStartWithoutAddingItAgain()
        {
            var model = NewModel(); Start(model, 615); model.AdvanceGameMinutes(125);
            var saved = JsonConvert.DeserializeObject<ExpeditionSaveData>(JsonConvert.SerializeObject(model.CaptureSave(Id, DateTime.UtcNow)));
            var restored = new ExpeditionModel(); Assert.That(restored.TryRestoreSave(saved, out var error), Is.True, error);
            Assert.That(Clock(restored), Is.EqualTo(740)); Assert.That(restored.ElapsedGameMinutes, Is.EqualTo(125));
            Assert.That(restored.TryRestoreSave(saved, out error), Is.True, error); Assert.That(Clock(restored), Is.EqualTo(740));
        }
        [Test] public void Clone_PreservesClockAndDoesNotShareChanges()
        {
            var model = NewModel(); Start(model, 615); var source = model.CaptureSave(Id, DateTime.UtcNow); var clone = source.Clone();
            var property = typeof(ExpeditionSaveData).GetProperty("StartMinuteOfDay"); Assert.That(property, Is.Not.Null);
            property.SetValue(source, 720f); Assert.That((float)property.GetValue(clone), Is.EqualTo(615));
        }
        [Test] public void InvalidClock_RejectsWithoutMutatingModel()
        {
            var model = NewModel(); Start(model, 600); var saved = model.CaptureSave(Id, DateTime.UtcNow);
            var version = typeof(ExpeditionSaveData).GetProperty("ClockSchemaVersion"); var start = typeof(ExpeditionSaveData).GetProperty("StartMinuteOfDay");
            Assert.That(version, Is.Not.Null); Assert.That(start, Is.Not.Null);
            version.SetValue(saved, 9); Assert.That(model.TryRestoreSave(saved, out _), Is.False); Assert.That(Clock(model), Is.EqualTo(600));
            version.SetValue(saved, 1);
            foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -1f, 1440f })
            { start.SetValue(saved, invalid); Assert.That(model.TryRestoreSave(saved, out _), Is.False); Assert.That(Clock(model), Is.EqualTo(600)); }
        }
        [Test] public void SurvivalQualification_UsesElapsedRatherThanClock()
        {
            var model = NewModel(); Start(model, 720); model.AdvanceGameMinutes(720);
            Assert.That(Clock(model), Is.EqualTo(1440)); Assert.That(model.CanReturn, Is.False);
            model.AdvanceGameMinutes(720); Assert.That(model.CanReturn, Is.True); Assert.That(model.ElapsedGameMinutes, Is.EqualTo(1440));
        }
        private sealed class EndpointRandom : Random
        {
            private readonly bool upper;
            public EndpointRandom(bool upper) { this.upper = upper; }
            public override int Next(int minValue, int maxValue) => upper ? maxValue - 1 : minValue;
        }
    }
}

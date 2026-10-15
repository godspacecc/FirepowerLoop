using System;
using System.IO;
using System.Linq;
using Luban;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests
{
    /// <summary>验证可重复取水的背包保存事务及河岸场景契约；测试不修复正式资源。</summary>
    public sealed class CivilisationWaterTests
    {
        private CivilisationHarvestTests support;
        private const string WaterPoint = "water.valley.west";
        private static readonly Vector3 Position = new Vector3(-14, 0, -9);
        [SetUp] public void SetUp()
        {
            support = new CivilisationHarvestTests(); support.SetUp();
            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, Position, Quaternion.identity);
            var architecture = (Game)Game.Interface;
            architecture.RegisterModel(new PlayerConditionModel());
            architecture.RegisterSystem(new PlayerConditionSystem());
            var condition = architecture.GetModel<PlayerConditionModel>(); var state = condition.Capture();
            state["water"] = 60; condition.Restore(state);
        }
        [TearDown] public void TearDown() => support?.TearDown();
        private static bool Begin(out string error) => Game.Interface.GetSystem<CivilisationSearchSystem>().TryBeginSearch(1, WaterPoint, out error);
        private static void Complete() => Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(3.1f);
        private static int Water => Game.Interface.GetModel<PlayerBagModel>().Container.GetTotalAmount(8);
        [Test] public void RepeatedCollectionPersistsWaterWithoutExhaustingSourceOrDrinking()
        {
            Assert.That(Begin(out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action), Is.True);
            Assert.That(action.Kind.ToString(), Is.EqualTo("CollectWater")); Complete();
            Assert.That(Water, Is.EqualTo(1)); Assert.That(Begin(out error), Is.True, error); Complete();
            Assert.That(Water, Is.EqualTo(2)); Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Water, Is.EqualTo(60));
            var saved = Game.Interface.GetSystem<SaveSystem>().InspectExpedition();
            Assert.That(saved.Status, Is.EqualTo(SaveFileStatus.Valid), saved.Error);
            Assert.That(saved.Data.Sections["another-day.player-bag"]["slots"].Children().Sum(t => t["itemId"].Value<int>() == 8 ? t["amount"].Value<int>() : 0), Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(WaterPoint), Is.False);
            Game.Interface.GetModel<CivilisationExpeditionModel>().Restore(saved.Data.Sections[CivilisationExpeditionModel.Section]);
            Assert.That(Begin(out error), Is.True, error);
        }
        [Test] public void LegacyVisitedRecordDoesNotDisableRepeatableWaterSource()
        {
            Game.Interface.GetModel<CivilisationExpeditionModel>().Visit(WaterPoint, null);
            Assert.That(Begin(out var error), Is.True, error); Complete(); Assert.That(Water, Is.EqualTo(1));
        }
        [Test] public void FullBagCannotGrantWaterOrExhaustSource()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(5, bag.Capacity, 1, out _, out _), Is.True);
            Assert.That(Begin(out var error), Is.True, error); Complete();
            Assert.That(Water, Is.Zero); Assert.That(Game.Interface.GetModel<CivilisationExpeditionModel>().HasVisited(WaterPoint), Is.False);
        }
        [Test] public void LeavingRiverBankDoesNotGrantWater()
        {
            Assert.That(Begin(out var error), Is.True, error);
            Game.Interface.GetSystem<UnitSystem>().TryUpdateTransform(1, Vector3.zero, Quaternion.identity); Complete(); Assert.That(Water, Is.Zero);
        }
        [TestCase(PlayerActionCancelReason.Damaged)] [TestCase(PlayerActionCancelReason.Attack)]
        public void InterruptedCollectionCanRetryWithoutPartialReward(PlayerActionCancelReason reason)
        {
            Assert.That(Begin(out var error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().CancelActive(reason); Complete(); Assert.That(Water, Is.Zero);
            Assert.That(Begin(out error), Is.True, error); Complete(); Assert.That(Water, Is.EqualTo(1));
        }
        [Test] public void DestroyModeCancelsCollectionAndBlocksRestart()
        {
            Assert.That(Begin(out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<TemporaryBuildingSystem>().BeginDestroyMode(out error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<PlayerActionSystem>().TryGetSnapshot(out _), Is.False);
            Assert.That(Begin(out _), Is.False); Complete(); Assert.That(Water, Is.Zero);
        }
        [Test] public void SaveFailureRollsBackWaterAndAllowsRetry()
        {
            var root = Path.Combine(Path.GetTempPath(), "AnotherDayWaterFailure", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); var blocked = Path.Combine(root, "blocked"); File.WriteAllText(blocked, "block");
            try
            {
                Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(blocked, "child"));
                Assert.That(Begin(out var error), Is.True, error); Complete(); Assert.That(Water, Is.Zero);
                Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(Path.Combine(root, "valid"));
                Assert.That(Begin(out error), Is.True, error); Complete(); Assert.That(Water, Is.EqualTo(1));
            }
            finally { Directory.Delete(root, true); }
        }
        [Test] public void CollectedWaterUsesExistingDrinkActionAndRestoresFortyWater()
        {
            Assert.That(Begin(out var error), Is.True, error); Complete();
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var slot = bag.Slots.Single(p => p.Value.ItemId == 8).Key;
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(slot, out error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1.1f);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Water, Is.EqualTo(100)); Assert.That(Water, Is.Zero);
        }
        [Test] public void FormalRiverHasThreeReadableWaterPointsWithoutConsumableRiverBinding()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Game/Scenes/CivilisationValleyMap.unity", OpenSceneMode.Additive);
            try
            {
                var config = new LubanRuntimeConfigProvider(() => new Tables(name => new ByteBuf(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Game/Config/" + name + ".bytes").bytes)));
                Assert.That(config.TryGetCivilisationCatalog(out var catalog, out var error), Is.True, error);
                var points = catalog.Points.Where(p => p.Id.StartsWith("water.valley.")).ToArray(); Assert.That(points.Length, Is.EqualTo(3));
                var views = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CivilisationPointView>(true)).ToArray();
                foreach (var point in points)
                {
                    Assert.That(point.ItemId, Is.EqualTo(8)); Assert.That(point.Amount, Is.EqualTo(1)); Assert.That(point.Seconds, Is.EqualTo(3));
                    var view = views.Single(v => v.PointId == point.Id); var data = new SerializedObject(view);
                    foreach (var other in views.Where(v => v != view))
                        Assert.That(Vector3.Distance(view.transform.position, other.transform.position), Is.GreaterThan(4.5f), "河岸取水与其他交互点不能争抢焦点：" + other.PointId);
                    Assert.That(data.FindProperty("harvestedVisualRoot").objectReferenceValue, Is.Null, "取水不能停用河流");
                    var prompt = (GameObject)data.FindProperty("nameRoot").objectReferenceValue;
                    Assert.That(prompt.GetComponentInChildren<TMP_Text>(true).text, Does.Contain("取水"));
                }
                Assert.That(scene.isDirty, Is.False);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}

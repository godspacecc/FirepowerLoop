using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Tests
{
    public sealed class MapItemAndIndependentMapTests
    {
        private static LubanRuntimeConfigProvider Provider() => new LubanRuntimeConfigProvider(() =>
            new Tables(name => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine("Assets/Game/Config", name + ".bytes")))));

        [Test]
        public void ValleyMapConfigDefinesToolDisplayAndPickup()
        {
            var provider = Provider();
            Assert.That(provider.TryGetItem(47, out var item, out var error), Is.True, error);
            Assert.That(item.ItemType, Is.EqualTo(ItemType.Tool));
            Assert.That(item.ActionKind, Is.EqualTo(ItemActionKind.OpenMap));
            Assert.That(item.ActionTarget, Is.EqualTo(MapIds.CivilisationValley));
            Assert.That(item.MaxStackAmount, Is.EqualTo(1));
            Assert.That(item.UseEffect, Is.EqualTo(ItemUseEffect.None));
            Assert.That(provider.TryGetMapDisplay(MapIds.CivilisationValley, out var display, out error), Is.True, error);
            Assert.That((display.CenterX, display.CenterZ, display.Width, display.Height), Is.EqualTo((0f, 0f, 1024f, 1024f)));
            Assert.That(display.ImageAddress, Is.EqualTo("Art/UI/Map/MapCivilisationValley"));
            Assert.That(provider.TryGetLoot("loot.map.civilisation-valley", out var loot, out error), Is.True, error);
            Assert.That((loot.ItemId, loot.Amount), Is.EqualTo((47, 1)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValleyMapOpensFromEitherCarriedContainer(bool pocketSource)
        {
            var loader = PrepareUiArchitecture();
            try
            {
                loader.Enqueue(LoadBehavior.SyncSuccess);
                var model = Game.Interface.GetModel<PlayerBagModel>();
                var source = pocketSource ? model.Pocket : model.Container;
                Add(source, 47);
                model.SetRiskState(PlayerBagRiskState.InExpedition);
                var combat = Game.Interface.GetModel<CombatModel>();
                combat.ApplyPlayerMaxHP(100);
                combat.SetPlayerHP(100);
                Game.Interface.GetModel<ExpeditionModel>().BeginSurvival(MapIds.CivilisationValley, 60f);
                Game.Interface.GetModel<SaveModel>().SetExpedition(new ExpeditionSaveData
                    { MapId = MapIds.CivilisationValley, Status = ExpeditionStatus.Playing });
                SetPlayingMap(MapIds.CivilisationValley);
                Assert.That(BagUICtrl.TryShowMapForItem(47, out var error), Is.True, error);
                var host = UIHelper.GetUICtrl<BagUICtrl>();
                Assert.That(host.CurrentPage, Is.EqualTo(BagManagementPage.Map));
                var page = (MapManagementPageCtrl)typeof(BagUICtrl).GetField("mapPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                Assert.That(page.SelectedMapId, Is.EqualTo(MapIds.CivilisationValley));
                Assert.That(source.GetTotalAmount(47), Is.EqualTo(1));
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void GeneratedConfigDefinesMapToolsWithoutConsumableSemantics()
        {
            var provider = Provider();
            var expected = new[]
            {
                (ItemIds.BaseMap, MapIds.Base), (ItemIds.SurvivalSampleMap, MapIds.SurvivalSample),
                (ItemIds.MissionSampleMap, MapIds.MissionSample), (ItemIds.HeJiaVillageMap, MapIds.HeJiaVillage),
                (ItemIds.HeJiaVillageLocalMap, MapIds.HeJiaVillage),
                (ItemIds.CivilisationValleyMap, MapIds.CivilisationValley)
            };
            foreach (var pair in expected)
            {
                Assert.That(provider.TryGetItem(pair.Item1, out var item, out var error), Is.True, error);
                Assert.That(item.ItemType, Is.EqualTo(ItemType.Tool));
                Assert.That(item.ActionKind, Is.EqualTo(ItemActionKind.OpenMap));
                Assert.That(item.ActionTarget, Is.EqualTo(pair.Item2));
                Assert.That(item.MaxStackAmount, Is.EqualTo(1));
                Assert.That(item.UseEffect, Is.EqualTo(ItemUseEffect.None));
                Assert.That(item.UseValue, Is.Zero);
                Assert.That(item.UseDurationSeconds, Is.Zero);
            }
        }

        [Test]
        public void GeneratedMapDisplayRowsMatchWorldBounds()
        {
            var provider = Provider();
            var expected = new[]
            {
                (MapIds.Base, "Art/UI/Map/MapBase", 0f, 0f, 30f, 30f, 1, 1),
                (MapIds.SurvivalSample, "Art/UI/Map/MapSurvivalSample", 0f, 0f, 40f, 40f, 1, 1),
                (MapIds.MissionSample, "Art/UI/Map/MapMissionSample", 0f, 0f, 40f, 40f, 1, 1),
                (MapIds.HeJiaVillage, "Art/UI/Map/MapHeJiaVillageLocal", 384f, 384f, 768f, 768f, 1, 1),
                (MapIds.CivilisationValley, "Art/UI/Map/MapCivilisationValley", 0f, 0f, 1024f, 1024f, 1, 1)
            };
            foreach (var row in expected)
            {
                Assert.That(provider.TryGetMapDisplay(row.Item1, out var value, out var error), Is.True, error);
                Assert.That((value.ImageAddress, value.CenterX, value.CenterZ, value.Width, value.Height,
                        value.ViewportColumns, value.ViewportRows),
                    Is.EqualTo((row.Item2, row.Item3, row.Item4, row.Item5, row.Item6, row.Item7, row.Item8)));
            }
        }

        [Test]
        public void ProjectionMapsNorthUpAndRejectsInvalidOrOutsideCoordinates()
        {
            var display = new MapDisplayDefinition("map", "address", 0, 0, 20, 10);
            Assert.That(MapDisplayProjection.TryWorldToNormalized(display, new Vector3(-10, 99, -5), out var southWest), Is.True);
            Assert.That(southWest, Is.EqualTo(Vector2.zero));
            Assert.That(MapDisplayProjection.TryWorldToNormalized(display, new Vector3(10, 0, 5), out var northEast), Is.True);
            Assert.That(northEast, Is.EqualTo(Vector2.one));
            Assert.That(MapDisplayProjection.TryWorldToNormalized(display, new Vector3(0, 0, 5.01f), out _), Is.False);
            Assert.That(MapDisplayProjection.TryWorldToNormalized(display, new Vector3(float.NaN, 0, 0), out _), Is.False);
        }

        [Test]
        public void ProjectionSelectsConfiguredViewportAndHidesMarkersOutsideIt()
        {
            var display = new MapDisplayDefinition("map", "address", 1024, 1024, 2048, 2048, 2, 2);
            Assert.That(MapDisplayProjection.TryResolveViewport(display, new Vector3(1536, 0, 512),
                out var viewport, out var playerPoint), Is.True);
            Assert.That(viewport, Is.EqualTo(new Rect(0.5f, 0f, 0.5f, 0.5f)));
            Assert.That(playerPoint, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(MapDisplayProjection.TryWorldToViewportNormalized(display,
                new Vector3(1024, 0, 0), viewport, out var southWest), Is.True);
            Assert.That(southWest, Is.EqualTo(Vector2.zero));
            Assert.That(MapDisplayProjection.TryWorldToViewportNormalized(display,
                new Vector3(512, 0, 512), viewport, out _), Is.False);
        }

        [Test]
        public void CarriedMapResolutionRejectsWarehouseAndNeverChoosesAnotherMapForShortcut()
        {
            var architecture = PrepareArchitecture();
            try
            {
                Add(architecture.GetModel<PlayerBagModel>().Container, ItemIds.BaseMap);
                Add(architecture.GetModel<WarehouseModel>().Container, ItemIds.SurvivalSampleMap);
                var system = architecture.GetSystem<MapDisplaySystem>();
                Assert.That(system.TryResolveCarriedItem(ItemIds.BaseMap, out var carried, out var code, out var error), Is.True, error);
                Assert.That((carried.MapItemId, carried.MapId), Is.EqualTo((ItemIds.BaseMap, MapIds.Base)));
                Assert.That(system.TryResolveCarriedItem(ItemIds.SurvivalSampleMap, out _, out code, out _), Is.False);
                Assert.That(code, Is.EqualTo(MapOpenFailureCode.NotCarried));

                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
                Assert.That(system.TryResolveCurrentMap(out _, out code, out _), Is.False);
                Assert.That(code, Is.EqualTo(MapOpenFailureCode.NoCurrentMapItem));
                Add(architecture.GetModel<PlayerBagModel>().Container, ItemIds.MissionSampleMap);
                Assert.That(system.TryResolveCurrentMap(out var current, out code, out error), Is.True, error);
                Assert.That((current.MapItemId, current.MapId), Is.EqualTo((ItemIds.MissionSampleMap, MapIds.MissionSample)));
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void PocketMapRemainsCarriedAcrossTransferAndStopsAfterRemoval()
        {
            var architecture = PrepareArchitecture();
            try
            {
                var bag = architecture.GetModel<PlayerBagModel>();
                Add(bag.Pocket, ItemIds.BaseMap);
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                var system = architecture.GetSystem<MapDisplaySystem>();
                Assert.That(system.TryResolveCarriedItem(ItemIds.BaseMap, out var request, out _, out var error), Is.True, error);
                Assert.That(system.TryResolveCurrentMap(out var current, out _, out error), Is.True, error);
                Assert.That(current.MapItemId, Is.EqualTo(ItemIds.BaseMap));
                Assert.That(system.TryBuildSnapshot(request, out _, out _, out error), Is.True, error);
                Assert.That(bag.Pocket.TryRemove(0, 1, out _, out error), Is.True, error);
                Add(bag.Container, ItemIds.BaseMap);
                Assert.That(system.IsStillCarried(request), Is.True);
                Assert.That(bag.Container.TryRemove(0, 1, out _, out error), Is.True, error);
                Assert.That(system.IsStillCarried(request), Is.False);
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void ViewingAnotherMapUsesOnlyItsConfiguredExtractionData()
        {
            var architecture = PrepareArchitecture();
            try
            {
                Add(architecture.GetModel<PlayerBagModel>().Container, ItemIds.MissionSampleMap);
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
                var request = new MapOpenRequest(ItemIds.MissionSampleMap, MapIds.MissionSample);
                Assert.That(architecture.GetSystem<MapDisplaySystem>().TryBuildSnapshot(
                    request, out var snapshot, out _, out var error), Is.True, error);
                Assert.That(snapshot.Player.HasValue, Is.False);
                Assert.That(snapshot.Extractions.Select(value => (value.Id, value.WorldPosition, value.IsLive)),
                    Is.EqualTo(new[] { ("extraction.task.main", new Vector3(0f, 0.6f, 12f), false) }));
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void CurrentMapUsesLocalPlayerAndAvailableExtractionFromMatchingInstance()
        {
            var architecture = PrepareArchitecture();
            try
            {
                Add(architecture.GetModel<PlayerBagModel>().Container, ItemIds.MissionSampleMap);
                architecture.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
                var units = architecture.GetSystem<UnitSystem>();
                Register(units, PlayerModel.LocalPlayerUnitId, 77, UnitType.Player, "player", new Vector3(2, 0, 3), 35, true);
                Register(units, 5001, 77, UnitType.ExtractionPoint, "exit.live", new Vector3(4, 0, 5), 0, true);
                Register(units, 5002, 76, UnitType.ExtractionPoint, "exit.stale", new Vector3(6, 0, 7), 0, true);
                Register(units, 5003, 77, UnitType.ExtractionPoint, "exit.unavailable", new Vector3(8, 0, 9), 0, true);
                var extractions = architecture.GetModel<ExtractionModel>();
                extractions.Set(new ExtractionSnapshot(5001, 77, true, false));
                extractions.Set(new ExtractionSnapshot(5002, 76, true, false));
                extractions.Set(new ExtractionSnapshot(5003, 77, false, false));

                var request = new MapOpenRequest(ItemIds.MissionSampleMap, MapIds.MissionSample);
                Assert.That(architecture.GetSystem<MapDisplaySystem>().TryBuildSnapshot(
                    request, out var snapshot, out _, out var error), Is.True, error);
                Assert.That(snapshot.Player.Value.WorldPosition, Is.EqualTo(new Vector3(2, 0, 3)));
                Assert.That(snapshot.Player.Value.Yaw, Is.EqualTo(35f).Within(0.001f));
                Assert.That(snapshot.Extractions.Select(value => value.Id), Is.EqualTo(new[] { "exit.live" }));
                Assert.That(snapshot.Extractions[0].IsLive, Is.True);
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void ValleyMapStartsWithNeutralSourceAndLocalizedItemNames()
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.That(texture.LoadImage(File.ReadAllBytes("Assets/Game/Art/UI/Map/MapCivilisationValley.png")), Is.True);
                Assert.That((texture.width, texture.height), Is.EqualTo((1024, 1024)));
                Assert.That(texture.GetPixels32().All(p => p.r < 60 && p.g < 60 && p.b < 60), Is.True,
                    "随机河谷的资料底图不能预载旧固定场景地形");
                foreach (var language in new[] { "zh-CN", "en-US" })
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText($"Assets/Game/Config/Localization/{language}.json"));
                    Assert.That((string)json["item.map.civilisation_valley.name"], Is.Not.Null.And.Not.Empty);
                    Assert.That((string)json["item.map.civilisation_valley.desc"], Is.Not.Null.And.Not.Empty);
                }
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void FormalMapImagesAndItemIconsHaveRequiredImportSettings()
        {
            var maps = new[] { "MapBase", "MapSurvivalSample", "MapMissionSample", "MapHeJiaVillageLocal", "MapCivilisationValley" };
            var icons = new[] { "IconMapBase", "IconMapSurvivalSample", "IconMapMissionSample", "IconMapHeJiaVillageLocal", "IconMapCivilisationValley" };
            foreach (var name in maps) AssertTexture($"Assets/Game/Art/UI/Map/{name}.png", 1024, TextureImporterType.Default);
            foreach (var name in icons) AssertTexture($"Assets/Game/Art/UI/Items/{name}.png", 256, TextureImporterType.Sprite);
        }

        [Test]
        public void EscapeRouteClosesMapWithoutChangingStateThenPreservesPauseRoute()
        {
            var loader = PrepareUiArchitecture();
            try
            {
                loader.Enqueue(LoadBehavior.SyncSuccess);
                Add(Game.Interface.GetModel<PlayerBagModel>().Container, ItemIds.BaseMap);
                SetPlayingMap(MapIds.Base);
                var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
                Game.Interface.GetModel<HotKeyModel>().TryGet(HotKeyActionIds.ToggleMap, out var mapBinding);
                Game.Interface.GetModel<HotKeyModel>().TryGet(HotKeyActionIds.TogglePause, out var pauseBinding);
                Assert.That(hotKeys.ExecuteBinding(mapBinding.BindingPath), Is.True);
                var beforeState = Game.Interface.GetModel<AppStateModel>().CurrentState.Value;
                var beforeScale = Time.timeScale;
                Assert.That(hotKeys.ExecuteBinding(pauseBinding.BindingPath), Is.True);
                Assert.That(UIHelper.GetUICtrl<BagUICtrl>().IsShow(), Is.False);
                Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(beforeState));
                Assert.That(Time.timeScale, Is.EqualTo(beforeScale));

                Assert.That(hotKeys.ExecuteBinding(pauseBinding.BindingPath), Is.True);
                Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Paused));
                Assert.That(Time.timeScale, Is.Zero);
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void MapShortcutOpensManagementMapAndSecondPressClosesHost()
        {
            var loader = PrepareUiArchitecture();
            try
            {
                Add(Game.Interface.GetModel<PlayerBagModel>().Container, ItemIds.BaseMap);
                SetPlayingMap(MapIds.Base);
                Game.Interface.GetModel<HotKeyModel>().TryGet(HotKeyActionIds.ToggleMap, out var binding);
                var hotKeys = Game.Interface.GetSystem<HotKeySystem>();
                Assert.That(hotKeys.ExecuteBinding(binding.BindingPath), Is.True);
                var bag = UIHelper.GetUICtrl<BagUICtrl>();
                Assert.That(bag, Is.Not.Null);
                Assert.That(bag.CurrentPage, Is.EqualTo(BagManagementPage.Map));
                Assert.That(UIHelper.HasUIOpen, Is.True);
                Assert.That(hotKeys.ExecuteBinding(binding.BindingPath), Is.True);
                Assert.That(bag.IsShow(), Is.False);
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void InvalidMapDisplayFailsLocallyWithoutBreakingOtherConfig()
        {
            var inner = Provider();
            var provider = DispatchProxy.Create<IRuntimeConfigProvider, MapDisplayFailureProxy>();
            ((MapDisplayFailureProxy)(object)provider).Inner = inner;
            ((MapDisplayFailureProxy)(object)provider).BlockedMapId = MapIds.Base;
            var architecture = PrepareArchitecture(provider);
            try
            {
                Add(architecture.GetModel<PlayerBagModel>().Container, ItemIds.BaseMap);
                Assert.That(architecture.GetSystem<MapDisplaySystem>().TryResolveCarriedItem(
                    ItemIds.BaseMap, out _, out var code, out var error), Is.False);
                Assert.That(code, Is.EqualTo(MapOpenFailureCode.DisplayUnavailable));
                Assert.That(error, Does.Contain(MapIds.Base));
                Assert.That(provider.TryGetItem(1, out var ordinary, out error), Is.True, error);
                Assert.That(ordinary.Id, Is.EqualTo(1));
                Assert.That(provider.TryGetMapDisplay(MapIds.MissionSample, out _, out error), Is.True, error);
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void ShortcutSwitchesExistingHostAndShowsMissingMapPlaceholder()
        {
            PrepareUiArchitecture();
            try
            {
                SetPlayingMap(MapIds.Base);
                Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
                var host = UIHelper.GetUICtrl<BagUICtrl>();
                host.SelectPage(BagManagementPage.Equipment);
                Game.Interface.GetModel<HotKeyModel>().TryGet(HotKeyActionIds.ToggleMap, out var binding);
                Assert.That(Game.Interface.GetSystem<HotKeySystem>().ExecuteBinding(binding.BindingPath), Is.True);
                Assert.That(UIHelper.GetUICtrl<BagUICtrl>(), Is.SameAs(host));
                Assert.That(host.CurrentPage, Is.EqualTo(BagManagementPage.Map));
                var view = Object.FindObjectsOfType<MapManagementPageUI>(true).Single();
                Assert.That(view.MessageText.text, Is.EqualTo(Loc.T("ui.map.error.no_current_map_item")));
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void MapShortcutDoesNotCrossPauseWindow()
        {
            PrepareUiArchitecture();
            try
            {
                SetPlayingMap(MapIds.Base);
                Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryPause(), Is.True);
                UIHelper.ShowUI<PauseUICtrl>();
                Game.Interface.GetModel<HotKeyModel>().TryGet(HotKeyActionIds.ToggleMap, out var binding);
                Assert.That(Game.Interface.GetSystem<HotKeySystem>().ExecuteBinding(binding.BindingPath), Is.False);
                Assert.That(UIHelper.GetUICtrl<BagUICtrl>(), Is.Null);
                Assert.That(UIHelper.GetUICtrl<PauseUICtrl>().IsShow(), Is.True);
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void MapItemOnlyOpensCurrentRegionWithoutConsumption()
        {
            PrepareUiArchitecture();
            try
            {
                var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
                Add(bag, ItemIds.BaseMap);
                Add(bag, ItemIds.MissionSampleMap);
                Add(Game.Interface.GetModel<WarehouseModel>().Container, ItemIds.SurvivalSampleMap);
                SetPlayingMap(MapIds.Base);
                Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
                var host = UIHelper.GetUICtrl<BagUICtrl>();
                host.SelectPage(BagManagementPage.Map);
                var page = (MapManagementPageCtrl)typeof(BagUICtrl).GetField("mapPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                Assert.That(page.SelectedMapId, Is.EqualTo(MapIds.Base));
                Assert.That(BagUICtrl.TryShowMapForItem(ItemIds.MissionSampleMap, out _), Is.False);
                Assert.That(page.SelectedMapId, Is.EqualTo(MapIds.Base));
                Assert.That(BagUICtrl.TryShowMapForItem(ItemIds.BaseMap, out error), Is.True, error);
                Assert.That(bag.Slots.Values.Sum(item => item.Amount), Is.EqualTo(2));
                Assert.That(BagUICtrl.TryShowMapForItem(ItemIds.SurvivalSampleMap, out _), Is.False);
                Assert.That(page.SelectedMapId, Is.EqualTo(MapIds.Base));
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void PocketMapOpensManagementPageWithoutConsumption()
        {
            var loader = PrepareUiArchitecture();
            try
            {
                loader.Enqueue(LoadBehavior.SyncSuccess);
                var pocket = Game.Interface.GetModel<PlayerBagModel>().Pocket;
                Add(pocket, ItemIds.BaseMap);
                SetPlayingMap(MapIds.Base);
                Assert.That(BagUICtrl.TryShowMapForItem(ItemIds.BaseMap, out var error), Is.True, error);
                var host = UIHelper.GetUICtrl<BagUICtrl>();
                Assert.That(host.CurrentPage, Is.EqualTo(BagManagementPage.Map));
                var page = (MapManagementPageCtrl)typeof(BagUICtrl).GetField("mapPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                Assert.That(page.SelectedMapId, Is.EqualTo(MapIds.Base));
                Assert.That(pocket.GetTotalAmount(ItemIds.BaseMap), Is.EqualTo(1));
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void WheelZoomPreservesPointAndResetRestoresWholeMap()
        {
            var loader = PrepareUiArchitecture();
            try
            {
                loader.Enqueue(LoadBehavior.SyncSuccess);
                Add(Game.Interface.GetModel<PlayerBagModel>().Container, ItemIds.BaseMap);
                SetPlayingMap(MapIds.Base);
                Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
                var host = UIHelper.GetUICtrl<BagUICtrl>();
                host.SelectPage(BagManagementPage.Map);
                var page = (MapManagementPageCtrl)typeof(BagUICtrl).GetField("mapPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
                var ui = Object.FindObjectsOfType<MapManagementPageUI>(true).Single();
                ui.MapScroll.viewport.anchorMin = ui.MapScroll.viewport.anchorMax = Vector2.one * .5f;
                ui.MapScroll.viewport.sizeDelta = new Vector2(800, 600);
                typeof(MapManagementPageCtrl).GetMethod("RefreshLayout", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(page, null);
                var initialSize = ui.MapContentRect.rect.size;
                var position = RectTransformUtility.WorldToScreenPoint(null, ui.MapScroll.viewport.TransformPoint(new Vector3(100, 50)));
                RectTransformUtility.ScreenPointToLocalPointInRectangle(ui.MapContentRect, position, null, out var before);
                ui.MapScroll.OnScroll(new PointerEventData(EventSystem.current)
                    { position = position, scrollDelta = new Vector2(0, Mathf.Log(2) / Mathf.Log(1.2f)) });
                Assert.That(ui.MapContentRect.rect.width, Is.EqualTo(initialSize.x * 2).Within(.1f));
                RectTransformUtility.ScreenPointToLocalPointInRectangle(ui.MapContentRect, position, null, out var after);
                Assert.That(after.x / ui.MapContentRect.rect.width, Is.EqualTo(before.x / initialSize.x).Within(.001f));
                Assert.That(after.y / ui.MapContentRect.rect.height, Is.EqualTo(before.y / initialSize.y).Within(.001f));
                ui.ResetButton.onClick.Invoke();
                Assert.That(ui.MapContentRect.rect.width, Is.EqualTo(initialSize.x).Within(.1f));
                Assert.That(ui.MapContentRect.anchoredPosition, Is.EqualTo(Vector2.zero));
            }
            finally { ResetArchitecture(); }
        }

        [Test]
        public void LegacyWindowAssetsAreRemovedAndSharedMarkerRemains()
        {
            Assert.That(File.Exists("Assets/Game/Prefabs/UI/Map/Map.prefab"), Is.False);
            Assert.That(File.Exists("Assets/Game/Scripts/Controller/UI/Map/MapUI.cs"), Is.False);
            Assert.That(File.Exists("Assets/Game/Scripts/Controller/UICtrl/Map/MapUICtrl.cs"), Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Map/Box/MapMarkerBox.prefab").GetComponent<MapMarkerBoxUI>(), Is.Not.Null);
        }

        private static void AssertTexture(string path, int size, TextureImporterType type)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); Assert.NotNull(texture, path);
            Assert.That(texture.width, Is.EqualTo(size)); Assert.That(texture.height, Is.EqualTo(size));
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.textureType, Is.EqualTo(type)); Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear)); Assert.That(importer.mipmapEnabled, Is.False);
            Assert.That(importer.sRGBTexture, Is.True); Assert.That(importer.maxTextureSize, Is.EqualTo(size));
        }

        private static IArchitecture PrepareArchitecture(IRuntimeConfigProvider provider = null)
        {
            ResetArchitecture();
            Architecture<Game>.OnRegisterPatch = architecture =>
                architecture.RegisterUtility<IRuntimeConfigProvider>(provider ?? Provider());
            return Game.Interface;
        }

        private static CountingResourceLoader PrepareUiArchitecture()
        {
            PrepareArchitecture();
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(LoadUiPrefab);
            var loader = new CountingResourceLoader();
            loader.IsTextureCleared = () =>
            {
                var ui = Object.FindObjectsOfType<MapManagementPageUI>(true).FirstOrDefault();
                return ui == null || ui.MapImage == null || ui.MapImage.texture == null;
            };
            ResourceLoader.SetLoaderForTests(loader);
            var localization = Game.Interface.GetUtility<LocalizationUtility>();
            localization.LoadFromJson("zh-CN", File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json"));
            localization.LoadFromJson("en-US", File.ReadAllText("Assets/Game/Config/Localization/en-US.json"));
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("zh-CN");
            return loader;
        }

        private static void ResetArchitecture()
        {
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            Time.timeScale = 1f;
            foreach (var obj in Object.FindObjectsOfType<GameObject>(true))
                if (obj.scene.IsValid() && obj.scene.isLoaded && string.IsNullOrEmpty(obj.scene.path))
                    Object.DestroyImmediate(obj);
        }

        private static void Add(ItemContainer container, int itemId)
        {
            Assert.That(container.TryAdd(itemId, 1, 1, out var accepted, out var error), Is.True, error);
            Assert.That(accepted, Is.EqualTo(1));
        }

        private static void Register(UnitSystem units, int id, long instanceId, UnitType type,
            string configId, Vector3 position, float yaw, bool active)
        {
            Assert.That(units.TryRegister(new UnitSnapshot(id, instanceId, type,
                type == UnitType.Player ? UnitSource.Player : UnitSource.MapPlaced,
                configId, position, Quaternion.Euler(0, yaw, 0), UnitAuthorityMode.Local, 0, active),
                out var error), Is.True, error);
        }

        private static void SetPlayingMap(string mapId)
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(mapId, new TestMapContentHandle(mapId));
            Register(Game.Interface.GetSystem<UnitSystem>(), PlayerModel.LocalPlayerUnitId, 77,
                UnitType.Player, "player", Vector3.zero, 0, true);
            Game.Interface.RegisterUtility<IPlayerRuntimePortProvider>(new ActiveWorldPlayerPortProvider
            { Current = new PlayerMapLocalPoseSnapshot(PlayerModel.LocalPlayerUnitId, 77, Vector3.zero, 0, true) });
            Assert.That(Game.Interface.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld(), Is.True);
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryResume(), Is.True);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(AppState.Playing));
        }


        private static List<string> VisibleTips() => Object.FindObjectsOfType<FloatUI>(true)
            .SelectMany(item => item.TipsRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
            .Where(item => item.gameObject.activeInHierarchy)
            .Select(item => item.text).ToList();

        private static void AssertVisibleTip(string expected) =>
            Assert.That(VisibleTips().Count(value => value == expected), Is.EqualTo(1));

        private static GameObject LoadUiPrefab(string address)
        {
            switch (address)
            {
                case "Prefabs/UI/Inventory/Bag":
                    return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab");
                case "Prefabs/UI/Home/Start":
                    return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Start.prefab");
                case "Prefabs/UI/Home/Pause":
                    return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Home/Pause.prefab");
                case "Prefabs/UI/Notify/Float":
                    return CreateFloatPrefab();
                default:
                    return null;
            }
        }

        private static GameObject CreateFloatPrefab()
        {
            var root = new GameObject("Float", typeof(RectTransform), typeof(FloatUI));
            var ui = root.GetComponent<FloatUI>();
            var tipsRoot = new GameObject("TipsRoot", typeof(RectTransform));
            tipsRoot.transform.SetParent(root.transform, false);
            ui.TipsRoot = tipsRoot.GetComponent<RectTransform>();
            var tip = new GameObject("TipTemplate", typeof(RectTransform), typeof(FloatTextBoxUI));
            tip.transform.SetParent(ui.TipsRoot, false);
            var text = new GameObject("TextTips", typeof(RectTransform), typeof(TextMeshProUGUI));
            text.transform.SetParent(tip.transform, false);
            tip.SetActive(false);
            ui.TipTemplate = tip.GetComponent<FloatTextBoxUI>();
            ui.TipTemplate.TextTips = text.GetComponent<TextMeshProUGUI>();
            return root;
        }

        private static void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
            }
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            var inputModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            Assert.That(inputModule, Is.Not.Null);
            eventSystem.AddComponent(inputModule);
            eventSystem.transform.SetParent(root.transform, false);
            new GameObject("PoolRoot");
        }

        private enum LoadBehavior { SyncSuccess, DelayedSuccess, SyncNull, Throw }

        private sealed class CountingResourceLoader : IResourceLoader
        {
            private readonly Queue<LoadBehavior> behaviors = new Queue<LoadBehavior>();
            public readonly List<Request> Requests = new List<Request>();
            public readonly Dictionary<object, int> ReleaseCounts = new Dictionary<object, int>();
            public readonly Dictionary<object, bool> ClearedBeforeRelease = new Dictionary<object, bool>();
            public Func<bool> IsTextureCleared { get; set; }

            public void Enqueue(LoadBehavior behavior) => behaviors.Enqueue(behavior);
            public T LoadAsset<T>(string path) where T : Object => null;

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                var behavior = behaviors.Count > 0 ? behaviors.Dequeue() : LoadBehavior.DelayedSuccess;
                if (behavior == LoadBehavior.Throw) throw new InvalidOperationException("controlled map load failure");
                var request = new Request(path, value => onLoaded?.Invoke(value as T));
                Requests.Add(request);
                if (behavior == LoadBehavior.SyncSuccess) request.Callback(new Texture2D(2, 2));
                else if (behavior == LoadBehavior.SyncNull) request.Callback(null);
                return new ResourceLoadHandle(request.Token);
            }

            public void Complete(int index, Texture2D texture) => Requests[index].Callback(texture);

            public void Release(ResourceLoadHandle handle)
            {
                var token = handle.RawHandle;
                ReleaseCounts[token] = ReleaseCounts.TryGetValue(token, out var count) ? count + 1 : 1;
                ClearedBeforeRelease[token] = IsTextureCleared?.Invoke() != false;
            }

            public sealed class Request
            {
                public Request(string address, Action<Object> callback)
                { Address = address; Callback = callback; Token = new object(); }
                public string Address { get; }
                public Action<Object> Callback { get; }
                public object Token { get; }
            }
        }

        private sealed class TestMapContentHandle : IMapContentHandle
        {
            private readonly GameObject _root;

            public TestMapContentHandle(string mapId)
            {
                MapId = mapId;
                _root = new GameObject($"InventoryIndependentUiTests_Map_{mapId}");
                var presentation = new GameObject("Presentation");
                presentation.transform.SetParent(_root.transform, false);
                var runtimeUnits = new GameObject("RuntimeUnits");
                runtimeUnits.transform.SetParent(presentation.transform, false);

                Context = _root.AddComponent<MapContext>();
                typeof(MapContext).GetField("mapContentRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(Context, presentation);
                typeof(MapContext).GetField("dynamicObjectRoot", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(Context, runtimeUnits.transform);
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool IsValid => _root != null && Context != null && Context.Validate(out _);

            public void Destroy()
            {
                if (_root != null)
                {
                    Object.DestroyImmediate(_root);
                }
            }
        }

        private sealed class ActiveWorldPlayerPortProvider : IPlayerRuntimePortProvider
        {
            public PlayerMapLocalPoseSnapshot Current;

            public bool TryGetMotorPort(int unitId, long mapInstanceId, out ICharacterMotorPort port)
            {
                port = null;
                return false;
            }

            public bool TryGetCameraPort(int unitId, long mapInstanceId, out IPlayerCameraPort port)
            {
                port = null;
                return false;
            }

            public bool IsPlayerCollider(int unitId, long mapInstanceId, Collider collider)
            {
                return false;
            }

            public bool TryGetCurrentPlayerMapLocalPose(out PlayerMapLocalPoseSnapshot snapshot, out string error)
            {
                snapshot = Current;
                error = snapshot.IsActive ? null : "test player inactive";
                return snapshot.IsActive;
            }
        }

        public class MapDisplayFailureProxy : DispatchProxy
        {
            public IRuntimeConfigProvider Inner { get; set; }
            public string BlockedMapId { get; set; }

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                if (targetMethod.Name == nameof(IRuntimeConfigProvider.TryGetMapDisplay) &&
                    string.Equals((string)args[0], BlockedMapId, StringComparison.Ordinal))
                {
                    args[1] = default(MapDisplayDefinition);
                    args[2] = $"TbMapDisplay[{BlockedMapId}] 测试无效";
                    return false;
                }
                return targetMethod.Invoke(Inner, args);
            }
        }
    }
}

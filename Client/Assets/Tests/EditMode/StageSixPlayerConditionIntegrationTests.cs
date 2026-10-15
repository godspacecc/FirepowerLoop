using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using QFramework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class StageSixPlayerConditionIntegrationTests
    {
        private string tempRoot;
        private TestMapContentLoader mapLoader;
        private readonly List<GameObject> createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new TestResourceLoader());
            mapLoader = new TestMapContentLoader(createdObjects);
            Architecture<Game>.OnRegisterPatch = architecture =>
            {
                architecture.RegisterUtility<IRuntimeConfigProvider>(CreateRuntimeConfigProvider());
                architecture.RegisterUtility<IMapContentLoader>(mapLoader);
            };

            tempRoot = Path.Combine(Path.GetTempPath(), "AnotherDayStageSixPlan2", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            Game.Interface.GetSystem<SaveSystem>().ConfigureForTests(tempRoot);
            EnterBase();
            _ = Game.Interface.GetSystem<CombatSystem>();
            _ = Game.Interface.GetSystem<InventorySystem>();
            _ = Game.Interface.GetSystem<PlayerConditionSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            Architecture<Game>.OnRegisterPatch = _ => { };
            TestReset.ResetArchitecture();
            for (var i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
                }
            }

            createdObjects.Clear();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void ConsumableConfigAndBothExplorationMapsProvideStaticSources()
        {
            var provider = CreateRuntimeConfigProvider();
            Assert.That(provider.TryGetItem(ItemIds.Ration, out var ration, out var error), Is.True, error);
            Assert.That(ration.ItemType, Is.EqualTo(global::ItemType.Consumable));
            Assert.That(ration.MaxStackAmount, Is.EqualTo(5));
            Assert.That(ration.UseEffect, Is.EqualTo(global::ItemUseEffect.RestorePower));
            Assert.That(ration.UseValue, Is.EqualTo(40));
            Assert.That(provider.TryGetItem(ItemIds.BottledWater, out var water, out error), Is.True, error);
            Assert.That(water.ItemType, Is.EqualTo(global::ItemType.Consumable));
            Assert.That(water.MaxStackAmount, Is.EqualTo(5));
            Assert.That(water.UseEffect, Is.EqualTo(global::ItemUseEffect.RestoreWater));
            Assert.That(water.UseValue, Is.EqualTo(40));

            Assert.That(provider.TryGetLoot("loot.ration", out var loot, out error), Is.True, error);
            Assert.That(loot.ItemId, Is.EqualTo(ItemIds.Ration));
            Assert.That(loot.Amount, Is.EqualTo(1));
            Assert.That(provider.TryGetLoot("loot.bottled-water", out var waterLoot, out error), Is.True, error);
            Assert.That(waterLoot.ItemId, Is.EqualTo(ItemIds.BottledWater));
            Assert.That(waterLoot.Amount, Is.EqualTo(1));

            Assert.That(provider.TryGetMapSpawns(MapIds.SurvivalSample, out var survival, out error), Is.True, error);
            Assert.That(provider.TryGetMapSpawns(MapIds.MissionSample, out var task, out error), Is.True, error);
            Assert.That(survival.Count(item => item.ObjectType == MapSpawnObjectType.Loot && item.ConfigId == "loot.ration"), Is.EqualTo(1));
            Assert.That(task.Count(item => item.ObjectType == MapSpawnObjectType.Loot && item.ConfigId == "loot.ration"), Is.EqualTo(1));
            Assert.That(survival.Count(item => item.ObjectType == MapSpawnObjectType.Loot && item.ConfigId == "loot.bottled-water"), Is.EqualTo(1));
            Assert.That(task.Count(item => item.ObjectType == MapSpawnObjectType.Loot && item.ConfigId == "loot.bottled-water"), Is.EqualTo(1));
            Assert.That(survival.Count(item => item.ObjectType == MapSpawnObjectType.Player), Is.EqualTo(1));
            Assert.That(task.Count(item => item.ObjectType == MapSpawnObjectType.Player), Is.EqualTo(1));

            var tables = CreateTables();
            Assert.That(tables.TbEnemy.DataList.Any(item => string.Equals(item.LootConfigId, "loot.ration", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void RationUseRestoresPowerAndConsumesExactlyOne()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Ration, 2, 5, out _, out var error), Is.True, error);
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 60f));

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Assert.That(bag.GetTotalAmount(ItemIds.Ration), Is.EqualTo(2));
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(100f).Within(0.001f));
            Assert.That(bag.GetTotalAmount(ItemIds.Ration), Is.EqualTo(1));
        }

        [Test]
        public void BottledWaterUseRestoresWaterAndConsumesExactlyOne()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.BottledWater, 2, 5, out _, out var error), Is.True, error);
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(water: 60f));

            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Assert.That(bag.GetTotalAmount(ItemIds.BottledWater), Is.EqualTo(2));
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Water, Is.EqualTo(100f).Within(0.001f));
            Assert.That(bag.GetTotalAmount(ItemIds.BottledWater), Is.EqualTo(1));
        }

        [Test]
        public void RationUseAtFullOrUnsafeStateRollsBackBag()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Ration, 1, 5, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.UserCanceled);
            Assert.That(bag.GetTotalAmount(ItemIds.Ration), Is.EqualTo(1));

            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 50f));
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out _), Is.False);
            Assert.That(bag.GetTotalAmount(ItemIds.Ration), Is.EqualTo(1));

            EnterExpedition(ExpeditionStatus.Returning, 100);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out _), Is.False);
            Assert.That(bag.GetTotalAmount(ItemIds.Ration), Is.EqualTo(1));
        }

        [Test]
        public void MedkitUseRulesRemainExpeditionOnly()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Medkit, 1, 5, out _, out var error), Is.True, error);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(80);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out _), Is.False);
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));

            EnterExpedition(ExpeditionStatus.Playing, 80);
            Assert.That(Game.Interface.GetSystem<InventorySystem>().TryBeginUsePlayerBagItem(0, out error), Is.True, error);
            Game.Interface.GetSystem<PlayerActionSystem>().TickForTests(1f);
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(0));
        }

        [Test]
        public void MobileSprintToggle_UsesUnifiedInputAndResetsAfterExhaustionAndFocusLoss()
        {
            var mobile = Game.Interface.GetSystem<MobileInputSystem>();
            mobile.TrySetEditorSimulation(true);
            var input = Game.Interface.GetSystem<PlayerInputSystem>();
            var toggle = typeof(MobileInputSystem).GetMethod("TryToggleSprint");
            Assert.That(toggle, Is.Not.Null, "手机加速切换入口必须存在");
            Assert.That(toggle.Invoke(mobile, null), Is.EqualTo(true));
            Assert.That(input.SprintHeld, Is.True);
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            var ctrl = UIHelper.ShowUI<MobileJoystickUICtrl>();
            var ui = GetUi<MobileJoystickUI>(ctrl);
            Assert.That(input.SprintHeld, Is.True, "刷新普通移动 HUD 不清除加速");
            Assert.That(ui.SprintIcon.color, Is.EqualTo(new Color(.95f, .76f, .24f, 1f)));
            ui.SprintButton.onClick.Invoke();
            Assert.That(input.SprintHeld, Is.False);
            toggle.Invoke(mobile, null);
            Game.Interface.GetSystem<PlayerConditionSystem>().TickTili(100f, true, input.SprintHeld);
            Assert.That(input.SprintHeld, Is.False, "体力耗尽释放加速意图");
            Game.Interface.GetSystem<PlayerConditionSystem>().TickTili(.1f, false, input.SprintHeld);
            Assert.That(toggle.Invoke(mobile, null), Is.EqualTo(false), "恢复阈值前拒绝重新开启，避免保持无效高亮");
            Game.Interface.GetSystem<PlayerConditionSystem>().TickTili(10f, false, input.SprintHeld);
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.GreaterThan(0));
            toggle.Invoke(mobile, null);
            Assert.That(input.SprintHeld, Is.True);
            typeof(MobileInputSystem).GetMethod("OnFocus", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(mobile, new object[] { false });
            Assert.That(input.SprintHeld, Is.False);
            typeof(MobileInputSystem).GetMethod("OnFocus", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(mobile, new object[] { true });
            Assert.That(input.SprintHeld, Is.False, "恢复焦点不能恢复旧加速");
            toggle.Invoke(mobile, null);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Assert.That(input.SprintHeld, Is.False, "切图不能保留加速意图");
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Assert.That(input.SprintHeld, Is.False);
        }

        [Test]
        public void ConditionHud_WeaponRowRefreshesAmmoAndClearsItForMelee()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            var equipment = Game.Interface.GetModel<WeaponEquipmentModel>();
            equipment.RestoreSnapshot(new WeaponEquipmentSnapshot(ItemIds.ShortSword, ItemIds.Bow, WeaponSlot.Ranged, 1));
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Arrow, 5, 60, out _, out var error), Is.True, error);
            var ctrl = UIHelper.ShowUI<PlayerConditionHudUICtrl>();
            var ui = GetUi<PlayerConditionHudUI>(ctrl);
            Assert.That(ui.AmmoFill.fillAmount, Is.EqualTo(1f));
            equipment.RestoreSnapshot(new WeaponEquipmentSnapshot(ItemIds.ShortSword, 20, WeaponSlot.Ranged, 12));
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(ui.AmmoFill.fillAmount, Is.EqualTo(.4f).Within(.001f), "步枪按 30 发弹匣显示进度");
            equipment.RestoreSnapshot(new WeaponEquipmentSnapshot(ItemIds.ShortSword, ItemIds.Bow, WeaponSlot.Ranged, 1));
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            equipment.TrySetRangedLoadedAmmo(0, out _);
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(ui.AmmoFill.fillAmount, Is.Zero);
            bag.TryAdd(ItemIds.Arrow, 3, 60, out _, out _);
            Game.Interface.SendEvent(new InventoryChangedEvent());
            Assert.That(ui.AmmoFill.fillAmount, Is.Zero, "备用弹药不计入弹匣进度");
            foreach (var stack in bag.Slots.ToArray()) bag.TryRemove(stack.Key, stack.Value.Amount, out _, out _);
            Game.Interface.SendEvent(new InventoryChangedEvent());
            Assert.That(ui.AmmoFill.fillAmount, Is.Zero);
            Assert.That(ui.AmmoBackground.gameObject.activeSelf, Is.True, "缺弹仍保留底环");
            equipment.RestoreSnapshot(new WeaponEquipmentSnapshot(ItemIds.ShortSword, ItemIds.Bow, WeaponSlot.Melee, 0));
            Game.Interface.SendEvent(new WeaponEquipmentChangedEvent());
            Assert.That(ui.AmmoFill.gameObject.activeSelf, Is.False);
            Assert.That(ui.AmmoBackground.gameObject.activeSelf, Is.False);
            Assert.That(ui.WeaponInfoRoot.activeSelf, Is.True, "近战保留武器图标区域");
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null, MapPreparationMode.OnlinePlayersOnly);
            Game.Interface.GetModel<OnlineWorldModel>().Publish(new OnlineWorldSnapshot(
                true, 1, MapIds.Base, 1, 1, Array.Empty<OnlineWorldPlayerSnapshot>(), Array.Empty<OnlineWorldEnemySnapshot>(),
                0, 0, 0, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success,
                new OnlineInventorySnapshot(12, new[] { new OnlineItemSlotSnapshot(0, ItemIds.Arrow, 9) },
                    new OnlineEquipmentSnapshot(ItemIds.ShortSword, ItemIds.Bow, (int)WeaponSlot.Ranged, 1), 1)));
            Assert.That(ui.AmmoFill.fillAmount, Is.EqualTo(1f), "联机只读取服务器装填快照");
            Game.Interface.GetModel<OnlineWorldModel>().Publish(new OnlineWorldSnapshot(
                true, 1, MapIds.Base, 1, 2, Array.Empty<OnlineWorldPlayerSnapshot>(), Array.Empty<OnlineWorldEnemySnapshot>(),
                0, 0, 0, false, OnlineWorldOperationState.Idle, OnlineWorldResultCode.Success,
                new OnlineInventorySnapshot(12, Array.Empty<OnlineItemSlotSnapshot>(),
                    new OnlineEquipmentSnapshot(ItemIds.ShortSword, 20, (int)WeaponSlot.Ranged, 15), 2)));
            Assert.That(ui.AmmoFill.fillAmount, Is.EqualTo(.5f).Within(.001f));
        }

        [Test]
        public void ConditionHud_CustomRowsApplyImmediatelyAndSurviveStateRefresh()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            var ctrl = UIHelper.ShowUI<PlayerConditionHudUICtrl>();
            var ui = GetUi<PlayerConditionHudUI>(ctrl);
            Canvas.ForceUpdateCanvases();
            var data = new MobileControlLayoutData();
            data.Status.X = .2f; data.Status.Y = .7f; data.Status.Scale = 1.2f;
            data.Health.X = .5f; data.Health.Y = .4f; data.Health.Scale = 1.4f;
            data.Weapon.X = .75f; data.Weapon.Y = .2f; data.Weapon.Scale = .8f;
            Game.Interface.GetModel<MobileControlLayoutModel>().Apply(data);
            Game.Interface.SendEvent(new MobileControlLayoutChangedEvent());
            void Verify()
            {
                var area = (RectTransform)ui.transform;
                var rows = new[] { ui.MobileStatusRow, ui.MobileHealthRoot, ui.WeaponInfoRoot };
                for (var i = 0; i < rows.Length; i++)
                {
                    var settings = data.Hud((MobileLayoutHud)i);
                    var bounds = MobileControlLayoutGeometry.Bounds(area, (RectTransform)rows[i].transform);
                    Assert.That(bounds.center.x / area.rect.width, Is.EqualTo(settings.X).Within(.001f));
                    Assert.That(bounds.center.y / area.rect.height, Is.EqualTo(settings.Y).Within(.001f));
                    Assert.That(rows[i].transform.localScale.x, Is.EqualTo(settings.Scale));
                }
            }
            Verify();
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Verify();
            ctrl.Hide(); ctrl.Show(); Verify();
        }

        [Test]
        public void PlayerConditionHudPrefabHasSharedBottomCenterContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab");
            Assert.That(prefab, Is.Not.Null);
            var ui = prefab.GetComponent<PlayerConditionHudUI>();
            Assert.That(ui, Is.Not.Null);
            Assert.That(prefab.GetComponents<ReferenceCollector>(), Has.Length.EqualTo(1));
            foreach (var field in typeof(PlayerConditionHudUI).GetFields()
                         .Where(field => typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)))
                Assert.That((UnityEngine.Object)field.GetValue(ui), Is.Not.Null, field.Name);
            var rect = (RectTransform)ui.MobileContentRoot.transform;
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(.5f, 0f)));
            Assert.That(rect.anchorMax, Is.EqualTo(new Vector2(.5f, 0f)));
            Assert.That(rect.pivot, Is.EqualTo(new Vector2(.5f, 0f)));
            Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(0f, 28f)));
            Assert.That(ui.MobileHealthRoot.transform.IsChildOf(rect), Is.True);
            Assert.That(ui.MobileStatusRow.transform.IsChildOf(rect), Is.True);
            Assert.That(ui.MobileContentRoot.GetComponentsInChildren<TMP_Text>(true), Has.Length.EqualTo(3));
            foreach (var fill in new[] { ui.MobilePowerFill, ui.MobileWaterFill, ui.MobileTiliFill })
            {
                Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
                Assert.That(fill.sprite, Is.Not.Null);
                Assert.That(fill.raycastTarget, Is.False);
            }
            Assert.That(prefab.GetComponent<GraphicRaycaster>(), Is.Null);
        }

        [Test]
        public void ConditionHud_SharesCenteredLayoutAcrossInputModesAndKeepsLiveStatus()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            var model = Game.Interface.GetModel<PlayerConditionModel>();
            model.Restore(ConditionSection(power: 80f, water: 40f, temperature: 37f, tili: 60f));
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(50);
            var ctrl = UIHelper.ShowUI<PlayerConditionHudUICtrl>();
            var ui = GetUi<PlayerConditionHudUI>(ctrl);
            var input = Game.Interface.GetSystem<MobileInputSystem>();
            input.TrySetEditorSimulation(true);
            Assert.That(ui.ContentRoot.activeSelf, Is.False);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(.5f).Within(.001f));
            Assert.That(ui.MobileHealthRoot.activeSelf, Is.True);
            Assert.That(ui.MobilePowerRoot.activeSelf, Is.True);
            Assert.That(ui.MobileWaterRoot.activeSelf, Is.True);
            Assert.That(ui.MobileTiliRoot.activeSelf, Is.True);
            Assert.That(ui.MobileTemperatureRoot.activeSelf, Is.False);
            Assert.That(ui.MobileContentRoot.GetComponentsInChildren<TMP_Text>(true), Has.Length.EqualTo(3), "血条加号、武器名称和弹药；生存圆环不显示详细数值");
            foreach (var fill in new[] { ui.MobilePowerFill, ui.MobileWaterFill, ui.MobileTiliFill })
                Assert.That(fill.fillMethod, Is.EqualTo(Image.FillMethod.Radial360));
            Assert.That(ui.MobileWaterFill.fillAmount, Is.EqualTo(.4f).Within(.001f));
            var row = (RectTransform)ui.MobilePowerRoot.transform.parent;
            LayoutRebuilder.ForceRebuildLayoutImmediate(row);
            var powerX = ui.MobilePowerRoot.transform.position.x;
            var waterX = ui.MobileWaterRoot.transform.position.x;
            var tiliX = ui.MobileTiliRoot.transform.position.x;
            Assert.That(waterX - powerX, Is.EqualTo(52f).Within(.01f));
            Assert.That(tiliX - waterX, Is.EqualTo(52f).Within(.01f));
            Assert.That(powerX, Is.EqualTo(row.position.x + 22f).Within(.01f));
            model.Restore(ConditionSection(temperature: 35f));
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Assert.That(ui.MobileTemperatureRoot.activeSelf, Is.True);
            Assert.That(ui.MobileTemperatureStem.fillAmount, Is.EqualTo(1f / 3f).Within(.001f));
            var coolColor = ui.MobileTemperatureStem.color;
            Assert.That(coolColor.b, Is.GreaterThan(coolColor.r));
            Assert.That(ui.MobileTemperatureBulb.color, Is.EqualTo(coolColor));
            model.Restore(ConditionSection(temperature: 39f));
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            var warmColor = ui.MobileTemperatureStem.color;
            Assert.That(warmColor.r, Is.GreaterThan(warmColor.b));
            Assert.That(ui.MobileTemperatureStem.fillAmount, Is.EqualTo(.6f).Within(.001f));
            model.Restore(ConditionSection(temperature: 42f, power: 10f));
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Assert.That(ui.MobileTemperatureStem.fillAmount, Is.EqualTo(.8f).Within(.001f));
            Assert.That(ui.MobileTemperatureStem.color.b, Is.LessThan(warmColor.b));
            Assert.That(ui.MobilePowerFill.color.r, Is.GreaterThan(ui.MobilePowerFill.color.g));
            model.Restore(ConditionSection(temperature: 32f));
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Assert.That(ui.MobileTemperatureStem.color.r, Is.LessThan(coolColor.r));
            foreach (var temperature in new[] { 36f, 37f, 38f })
            {
                model.Restore(ConditionSection(power: model.MaxPower, water: model.MaxWater, temperature: temperature, tili: model.MaxTili));
                Game.Interface.SendEvent(new PlayerConditionChangedEvent());
                Assert.That(ui.MobilePowerRoot.activeSelf, Is.False);
                Assert.That(ui.MobileWaterRoot.activeSelf, Is.False);
                Assert.That(ui.MobileTiliRoot.activeSelf, Is.False);
                Assert.That(ui.MobileTemperatureRoot.activeSelf, Is.False);
                Assert.That(ui.MobileHealthRoot.activeInHierarchy, Is.True);
            }
            model.Restore(ConditionSection(power: model.MaxPower, water: 99.9f, temperature: 37f, tili: model.MaxTili));
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Assert.That(ui.MobileWaterRoot.activeSelf, Is.True, "未满值必须显示，不能按百分比舍入隐藏");
            LayoutRebuilder.ForceRebuildLayoutImmediate(row);
            Assert.That(ui.MobileWaterRoot.transform.position.x, Is.EqualTo(row.position.x + 22f).Within(.01f));
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(25);
            Game.Interface.SendEvent(new CombatHealthChangedEvent(PlayerModel.LocalPlayerUnitId, CombatFaction.Player, 25, 100));
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(.25f).Within(.001f));
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(ui.MobileWaterRoot.activeSelf, Is.True);
            Game.Interface.GetSystem<AppStateSystem>().TryPause();
            Assert.That(ui.MobileContentRoot.activeSelf, Is.False);
            Game.Interface.GetSystem<AppStateSystem>().TryResume();
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);
            input.TrySetEditorSimulation(false);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);
            Assert.That(ui.ContentRoot.activeSelf, Is.False);
            Assert.That(ui.MobileWaterFill.fillAmount, Is.EqualTo(.999f).Within(.001f));
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(.25f).Within(.001f));
        }
        [Test]
        public void MobileJoystick_CustomPositionAvoidsStatusStripWithoutChangingSavedLayout()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            var condition = GetUi<PlayerConditionHudUI>(UIHelper.ShowUI<PlayerConditionHudUICtrl>());
            var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Player/MobileJoystick.prefab"));
            createdObjects.Add(instance);
            var ui = instance.GetComponent<MobileJoystickUI>();
            var area = ui.JoystickLayoutArea;
            area.anchorMin = area.anchorMax = Vector2.zero;
            area.pivot = Vector2.zero;
            area.sizeDelta = new Vector2(1920, 1080);
            condition.MobileContentRoot.transform.SetParent(area, false);
            var layout = new MobileControlLayoutData();
            layout.Move.X = .48f;
            layout.Move.Y = 0f;
            Game.Interface.GetModel<MobileControlLayoutModel>().Apply(layout);
            var ctrl = new MobileJoystickUICtrl();
            typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ctrl, ui);
            typeof(MobileJoystickUICtrl).GetMethod("RefreshLayout", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ctrl, null);
            var stick = MobileControlLayoutGeometry.Bounds(area, (RectTransform)ui.MoveJoystickRoot.transform);
            foreach (var row in new[] { condition.MobileStatusRow, condition.MobileHealthRoot, condition.WeaponInfoRoot })
                Assert.That(stick.Overlaps(MobileControlLayoutGeometry.Bounds(area, (RectTransform)row.transform)), Is.False,
                    "自定义摇杆不能遮住实际信息栏：" + row.name);
            Assert.That(stick.Overlaps(MobileControlLayoutGeometry.Bounds(area, (RectTransform)ui.InventoryButton.transform)), Is.False);
            Assert.That(Game.Interface.GetModel<MobileControlLayoutModel>().ToData().Move.X, Is.EqualTo(.48f));
            Assert.That(Game.Interface.GetModel<MobileControlLayoutModel>().ToData().Move.Y, Is.EqualTo(0f));
        }

        [Test]
        public void PlayerConditionHudShowsOnlyInPlayingAndReadsModel()
        {
            CreateUIRoot();
            var loadCount = 0;
            UIHelper.SetPrefabLoaderForTests(path =>
            {
                if (path == "Prefabs/UI/Player/PlayerConditionHud")
                {
                    loadCount++;
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab");
            });
            Assert.That(UIHelper.GetUICtrl<PlayerConditionHudUICtrl>(), Is.Null);
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 80f, water: 40f, temperature: 30f, tili: 60f));
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(50);
            var ctrl = UIHelper.ShowUI<PlayerConditionHudUICtrl>();
            var ui = GetUi<PlayerConditionHudUI>(ctrl);
            Assert.That(UIHelper.HasUIOpen, Is.False);
            Assert.That(loadCount, Is.EqualTo(1));
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);
            Assert.That(ui.ContentRoot.activeSelf, Is.False);
            Assert.That(ui.MobilePowerFill.fillAmount, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(ui.MobileWaterFill.fillAmount, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(ui.MobileTemperatureStem.fillAmount, Is.EqualTo(0f).Within(0.001f));
            Assert.That(ui.MobileTemperatureRoot.activeSelf, Is.True);
            Assert.That(ui.MobileTiliFill.fillAmount, Is.EqualTo(0.6f).Within(0.001f));
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));

            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 0f, water: 50f, temperature: 37f, tili: 25f));
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Assert.That(ui.MobilePowerFill.fillAmount, Is.EqualTo(0f).Within(0.001f));
            Assert.That(ui.MobileWaterFill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(ui.MobileTemperatureStem.fillAmount, Is.EqualTo((37f - PlayerConditionRules.MinTemperature) / (PlayerConditionRules.MaxTemperature - PlayerConditionRules.MinTemperature)).Within(0.001f));
            Assert.That(ui.MobileTemperatureRoot.activeSelf, Is.False);
            Assert.That(ui.MobileTiliFill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));

            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 50f, water: 25f, temperature: 45f, tili: 100f));
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Assert.That(ui.MobilePowerFill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(ui.MobileWaterFill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));
            Assert.That(ui.MobileTemperatureStem.fillAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(ui.MobileTemperatureRoot.activeSelf, Is.True);
            Assert.That(ui.MobileTiliFill.fillAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(InvokePowerFillAmount(150f, 100f), Is.EqualTo(1f).Within(0.001f));
            Assert.That(InvokePowerFillAmount(10f, 0f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(InvokePowerFillAmount(10f, -1f), Is.EqualTo(0f).Within(0.001f));

            Game.Interface.GetModel<CombatModel>().SetPlayerHP(25);
            Game.Interface.SendEvent(new CombatHealthChangedEvent(200, CombatFaction.Enemy, 1, 10));
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(0.5f).Within(0.001f));
            Game.Interface.SendEvent(new CombatHealthChangedEvent(PlayerModel.LocalPlayerUnitId, CombatFaction.Player, 25, 100));
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(0.25f).Within(0.001f));

            Assert.That(ui.MobileHealthIconText.text, Is.EqualTo("＋"));
            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(ui.MobileHealthIconText.text, Is.EqualTo("+"));

            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.False);
            Assert.That(ctrl.IsShow(), Is.True);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.False);
            Assert.That(ctrl.IsShow(), Is.True);
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 20f, water: 10f, temperature: 37f, tili: 30f));
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(75);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);
            Assert.That(ui.ContentRoot.activeSelf, Is.False);
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(0.75f).Within(0.001f));
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Paused);
            Assert.That(ctrl.IsShow(), Is.True, "暂停仅隐藏内容，保留 HUD 实例以便恢复");
            Assert.That(ui.MobileContentRoot.activeSelf, Is.False);

            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading), Is.True);
            Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing), Is.True);
            var reused = UIHelper.ShowUI<PlayerConditionHudUICtrl>();
            Assert.That(reused, Is.SameAs(ctrl));
            Assert.That(GetUi<PlayerConditionHudUI>(reused), Is.SameAs(ui));
            Assert.That(loadCount, Is.EqualTo(1));
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 33f, water: 44f, temperature: 39f, tili: 55f));
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(66);
            Game.Interface.SendEvent(new PlayerConditionChangedEvent());
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);
            Assert.That(ui.ContentRoot.activeSelf, Is.False);
            Assert.That(ui.MobileHealthFill.fillAmount, Is.EqualTo(0.66f).Within(0.001f));
        }

        [Test]
        public void PlayerConditionHud_HidesInArcadeAndRestoresInBase()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(_ =>
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Player/PlayerConditionHud.prefab"));
            var ctrl = UIHelper.ShowUI<PlayerConditionHudUICtrl>();
            var ui = GetUi<PlayerConditionHudUI>(ctrl);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(ui.gameObject.activeSelf, Is.True);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Arcade, null, MapPreparationMode.ArcadePlayerOnly);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Arcade));
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(ui.gameObject.activeSelf, Is.False);
            Assert.That(ui.MobileContentRoot.activeInHierarchy, Is.False);

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.SendEvent(new MapLoadingCompletedEvent(MapIds.Base));
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(ui.gameObject.activeSelf, Is.True);
            Assert.That(ui.MobileContentRoot.activeSelf, Is.True);
        }

        [Test]
        public void ExpeditionHudFormatsDayClockAndKeepsUpdatingAfterQualification()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab"));
            BeginSurvival();
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(water: 10000f, maxWater: 10000f));
            var ctrl = UIHelper.ShowUI<ExpeditionHudUICtrl>();
            var ui = GetUi<ExpeditionHudUI>(ctrl);
            Assert.That(ui.TimerText.text, Does.Not.Contain(MapIds.SurvivalSample));
            Assert.That(ui.TimerText.text, Does.Contain("00:00"));

            Game.Interface.GetSystem<PlayerConditionSystem>().TickWorld(PlayerConditionRules.GameMinutesPerDay, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.ReturnQualified));
            Game.Interface.GetSystem<PlayerConditionSystem>().TickWorld(60f, AppState.Playing, MapIds.SurvivalSample);
            Assert.That(ui.TimerText.text, Does.Contain("01:00"));

            BeginConfigured(MapIds.MissionSample, TaskObjectiveType.Kill, 2f);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
            Assert.That(ui.TimerText.text, Does.Contain("0/2"));
        }

        [Test]
        public void HeJiaVillageHudShowsClockAndImmediateReturn()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab"));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.HeJiaVillage, null);
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(MapIds.HeJiaVillage, Guid.NewGuid().ToString("N"), Array.Empty<ExpeditionTaskDefinition>(), out var emptyError), Is.True, emptyError);

            var ctrl = UIHelper.ShowUI<ExpeditionHudUICtrl>();
            var ui = GetUi<ExpeditionHudUI>(ctrl);
            Assert.That(ui.TimerText.text, Is.EqualTo(Texts.ExpeditionDayClock(1, "00:00")));
            Assert.That(ui.TimerText.text, Does.Not.Contain("0/0"));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().CanReturn, Is.True);
            Assert.That(ui.BtnReturn.interactable, Is.True);
            Assert.That(ui.StatusText.text, Is.EqualTo(Texts.ExpeditionStatusReturnOpen));

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            BeginConfigured(MapIds.SurvivalSample, TaskObjectiveType.Survive, 60f);
            Assert.That(ui.TimerText.text, Does.Contain("00:00"));

            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
            BeginConfigured(MapIds.MissionSample, TaskObjectiveType.Kill, 2f);
            Assert.That(ui.TimerText.text, Does.Contain("0/2"));
        }

        [Test]
        public void ExpeditionHudBlocksDirectReturnWhenCurrentMapHasEntityExtraction() => VerifyExtractionHud(false);

        [Test]
        public void MobileExpeditionHud_BlocksDirectReturnAtEntityExtraction() => VerifyExtractionHud(true);

        private void VerifyExtractionHud(bool mobile)
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab"));
            EnterExpedition(ExpeditionStatus.ReturnQualified, 100);
            const long mapInstanceId = 900;
            var units = Game.Interface.GetSystem<UnitSystem>();
            Assert.That(units.TryRegister(new UnitSnapshot(PlayerModel.LocalPlayerUnitId, mapInstanceId,
                UnitType.Player, UnitSource.Player, "player.local", Vector3.zero, Quaternion.identity,
                UnitAuthorityMode.Local, 0, true), out var playerError), Is.True, playerError);
            const int extractionUnitId = 901;
            Assert.That(units.TryRegister(new UnitSnapshot(extractionUnitId, mapInstanceId,
                UnitType.ExtractionPoint, UnitSource.MapPlaced, "extraction.task.main", Vector3.zero,
                Quaternion.identity, UnitAuthorityMode.Local, 0, true), out var exitError), Is.True, exitError);
            Assert.That(Game.Interface.GetSystem<ExtractionSystem>().TryRegisterExtraction(
                extractionUnitId, true, out var extractionError), Is.True, extractionError);

            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(mobile);
            var ctrl = UIHelper.ShowUI<ExpeditionHudUICtrl>();
            var ui = GetUi<ExpeditionHudUI>(ctrl);
            var button = mobile ? ui.MobileBtnReturn : ui.BtnReturn;
            var status = mobile ? ui.MobileStatusText : ui.StatusText;
            var label = mobile ? ui.MobileReturnButtonText : ui.ReturnButtonText;
            Assert.That(button.gameObject.activeSelf, Is.False);
            Assert.That(status.text, Is.EqualTo(Texts.ExpeditionStatusGoToExtraction));
            button.onClick.Invoke();
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status,
                Is.EqualTo(ExpeditionStatus.ReturnQualified), "隐藏状态之外，点击处理也必须阻断直接返回");

            Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
            Game.Interface.SendEvent(new ExpeditionChangedEvent());
            Assert.That(button.gameObject.activeSelf, Is.True);
            Assert.That(label.text, Is.EqualTo(Texts.ExpeditionRetryReturn));
        }

        [Test]
        public void ExpeditionHudReadsSingleTaskSnapshotWithoutChangingExistingView() => VerifyTaskHud(false);

        [Test]
        public void MobileExpeditionHud_TracksTaskProgressAndLanguage() => VerifyTaskHud(true);

        private void VerifyTaskHud(bool mobile)
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab"));
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.MissionSample, null);
            BeginConfigured(MapIds.MissionSample, TaskObjectiveType.Kill, 2f);

            Game.Interface.GetSystem<MobileInputSystem>().TrySetEditorSimulation(mobile);
            var ctrl = UIHelper.ShowUI<ExpeditionHudUICtrl>();
            var ui = GetUi<ExpeditionHudUI>(ctrl);
            var timer = mobile ? ui.MobileTimerText : ui.TimerText;
            var status = mobile ? ui.MobileStatusText : ui.StatusText;
            var resource = mobile ? ui.MobileResourceText : ui.ResourceText;
            var button = mobile ? ui.MobileBtnReturn : ui.BtnReturn;
            Assert.That(ui.MobileHudPanel.activeSelf, Is.EqualTo(mobile));
            Assert.That(ui.DesktopHudPanel.activeSelf, Is.EqualTo(!mobile));
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Tasks, Has.Count.EqualTo(1));
            Assert.That(timer.text, Does.Contain("0/2"));
            Assert.That(status.text, Is.EqualTo(Texts.ExpeditionStatusContinueGoal));
            Assert.That(resource.text, Is.Empty);
            Assert.That(button.interactable, Is.False);

            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(7101, CombatFaction.Enemy, 10, 1);
            Game.Interface.SendEvent(new CombatantDiedEvent(7101, PlayerModel.LocalPlayerUnitId));
            Assert.That(timer.text, Does.Contain("1/2"));
            Assert.That(button.interactable, Is.False);

            Game.Interface.SendCommand(new SetLanguageCommand { LanguageCode = "en-US" });
            Assert.That(timer.text, Does.Contain("1/2"));
            Assert.That(status.text, Is.EqualTo(Texts.ExpeditionStatusContinueGoal));
            Assert.That(resource.text, Is.Empty);

            Game.Interface.GetModel<CombatModel>().RegisterOrResetCombatant(7102, CombatFaction.Enemy, 10, 1);
            Game.Interface.SendEvent(new CombatantDiedEvent(7102, PlayerModel.LocalPlayerUnitId));
            Assert.That(timer.text, Does.Contain("2/2"));
            Assert.That(status.text, Is.EqualTo(Texts.ExpeditionStatusReturnOpen));
            Assert.That(button.interactable, Is.True);
            Assert.That(Game.Interface.GetModel<ExpeditionTaskModel>().Tasks.Single().Status,
                Is.EqualTo(ExpeditionTaskStatus.Completed));
        }

        [Test]
        public void AdvanceTimeRejectsInvalidSyntaxAmountAndState()
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            Assert.That(system.ExecuteAsync("#advance time", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.UsageError));
            Assert.That(system.ExecuteAsync("#advance time 0", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidGameMinutes));
            Assert.That(system.ExecuteAsync("#advance time 1.5", CancellationToken.None).GetAwaiter().GetResult().Code, Is.EqualTo(TestCmdResultCode.InvalidGameMinutes));
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Loading);
            var loading = system.ExecuteAsync("#advance time 5", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(loading.Code, Is.EqualTo(TestCmdResultCode.StateRejected));
            Assert.That(loading.Success, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AdvanceTimeUsesDomainSettlementInExplorationAndBase(bool paused)
        {
            var system = Game.Interface.GetSystem<TestCmdSystem>();
            if (paused) Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryPause(), Is.True);
            var expectedState = paused ? AppState.Paused : AppState.Playing;
            var timeScaleBefore = Time.timeScale;
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 80f, water: 80f));
            var baseResult = system.ExecuteAsync("#advance time 10", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(baseResult.Code, Is.EqualTo(TestCmdResultCode.AdvanceTimeComplete));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.EqualTo(90f).Within(0.001f));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(expectedState));
            Assert.That(Time.timeScale, Is.EqualTo(timeScaleBefore));

            BeginSurvival();
            // 测试地图切换只替换领域身份，沿用基地阶段的暂停状态。
            timeScaleBefore = Time.timeScale;
            var exploration = system.ExecuteAsync("#advance time 60", CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(exploration.Success, Is.True);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes, Is.EqualTo(60f).Within(0.001f));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Power, Is.LessThan(90f));
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(expectedState));
            Assert.That(Time.timeScale, Is.EqualTo(timeScaleBefore));
            var elapsedBefore = Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes;
            Game.Interface.GetSystem<PlayerConditionSystem>().TickWorld(5f, expectedState, MapIds.SurvivalSample);
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes,
                Is.EqualTo(paused ? elapsedBefore : elapsedBefore + 5f * PlayerConditionRules.GameMinutesPerRealSecond).Within(0.001f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AdvanceTimeStopsAtPowerDepletionDeathAndDoesNotChangeTili(bool paused)
        {
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 10, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(10);
            Game.Interface.GetModel<PlayerConditionModel>().Restore(ConditionSection(power: 0f));
            Game.Interface.GetSystem<PlayerConditionSystem>().TickTili(1f, true, true);
            var tiliBefore = Game.Interface.GetModel<PlayerConditionModel>().Tili;
            BeginSurvival();
            if (paused) Assert.That(Game.Interface.GetSystem<AppStateSystem>().TryPause(), Is.True);
            mapLoader.FailNextLoad = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[CombatFlowSystem\\] 失败回基地未完成"));

            var result = Game.Interface.GetSystem<TestCmdSystem>().ExecuteAsync("#advance time 100", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Code, Is.EqualTo(TestCmdResultCode.AdvanceTimeComplete));
            Assert.That(result.AffectedAmount, Is.EqualTo(2));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().ElapsedGameMinutes, Is.EqualTo(result.AffectedAmount).Within(0.001f));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.Defeated));
            Assert.That(Game.Interface.GetModel<CombatModel>().PlayerHP.Value, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<PlayerConditionModel>().Tili, Is.EqualTo(tiliBefore).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator CombatFlowNotificationExceptionStillStartsFailureReturn()
        {
            BeginSurvival();
            mapLoader.FailNextLoad = true;
            IUnRegister unregister = null;
            unregister = Game.Interface.RegisterEvent<ExpeditionChangedEvent>(_ => throw new InvalidOperationException("death notify failed"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("ExpeditionChangedEvent after player death failed: System.InvalidOperationException: death notify failed"));
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[CombatFlowSystem\\] 失败回基地未完成"));

            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            yield return WaitFrames(2);
            unregister.UnRegister();

            Assert.That(mapLoader.LoadedMapIds, Is.EqualTo(new[] { MapIds.Base }));
            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.Defeated));
            Assert.That(IsReturningAfterDeath(Game.Interface.GetSystem<CombatFlowSystem>()), Is.False);
        }

        [UnityTest]
        public IEnumerator CombatFlowUnexpectedReturnExceptionResetsGuardAndRecordsDiagnostic()
        {
            BeginSurvival();
            IUnRegister unregister = null;
            unregister = Game.Interface.RegisterEvent<ExpeditionChangedEvent>(_ =>
            {
                if (!Game.Interface.GetModel<ExpeditionModel>().IsActive)
                {
                    throw new InvalidOperationException("return notify failed");
                }
            });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("player death return failed unexpectedly: System.InvalidOperationException: return notify failed"));

            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            yield return WaitFrames(2);
            unregister.UnRegister();

            var flow = Game.Interface.GetSystem<CombatFlowSystem>();
            Assert.That(IsReturningAfterDeath(flow), Is.False);
            Assert.That(flow.LastFailureError, Does.Contain("InvalidOperationException"));
            Assert.That(flow.LastFailureError, Does.Contain("return notify failed"));
        }

        [UnityTest]
        public IEnumerator CombatFlowUnexpectedExceptionDoesNotRepeatDomainRollback()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.ShortSword, 1, 1, out _, out var error), Is.True, error);
            Assert.That(Game.Interface.GetSystem<WeaponEquipmentSystem>().TryEquipFromPlayerBag(0, out error), Is.True, error);
            Assert.That(bag.TryAdd(ItemIds.MetalParts, 2, 20, out _, out error), Is.True, error);
            BeginSurvival();
            IUnRegister unregister = null;
            unregister = Game.Interface.RegisterEvent<ExpeditionChangedEvent>(_ =>
            {
                if (!Game.Interface.GetModel<ExpeditionModel>().IsActive)
                {
                    throw new InvalidOperationException("return notify failed");
                }
            });
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("player death return failed unexpectedly: System.InvalidOperationException: return notify failed"));

            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            yield return WaitFrames(2);
            unregister.UnRegister();

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId, Is.EqualTo(0));
            Assert.That(mapLoader.LoadedMapIds, Is.EqualTo(new[] { MapIds.Base }));

            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            yield return WaitFrames(2);

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.Slots.Count, Is.EqualTo(0));
            Assert.That(Game.Interface.GetModel<WeaponEquipmentModel>().MeleeWeaponItemId, Is.EqualTo(0));
            Assert.That(mapLoader.LoadedMapIds, Is.EqualTo(new[] { MapIds.Base }));
        }

        [UnityTest]
        public IEnumerator DefeatedHudRetryRemainsAvailableAfterDeathReturnFailure()
        {
            CreateUIRoot();
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Expedition/ExpeditionHud.prefab"));
            BeginSurvival();
            var ctrl = UIHelper.ShowUI<ExpeditionHudUICtrl>();
            var ui = GetUi<ExpeditionHudUI>(ctrl);
            mapLoader.FailNextLoad = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[CombatFlowSystem\\] 失败回基地未完成"));

            Game.Interface.SendEvent(new CombatantDiedEvent(PlayerModel.LocalPlayerUnitId, 0));
            yield return WaitFrames(2);

            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.Defeated));
            Assert.That(ui.BtnReturn.interactable, Is.True);

            mapLoader.FailNextLoad = true;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("\\[ExpeditionHudUICtrl\\] 返回基地失败：load failed"));
            ui.BtnReturn.onClick.Invoke();
            yield return WaitFrames(2);

            Assert.That(Game.Interface.GetModel<ExpeditionModel>().Status, Is.EqualTo(ExpeditionStatus.Defeated));
            Assert.That(ui.BtnReturn.interactable, Is.True);
        }

        [Test]
        public void TestCmdPlayerConditionCategoryAndLocalizationAreComplete()
        {
            var provider = Game.Interface.GetUtility<IRuntimeConfigProvider>();
            Assert.That(provider.TryGetTestCmdCatalog(out var categories, out var entries, out var error), Is.True, error);
            var playerCondition = categories.SingleOrDefault(item => item.Id == "player-condition");
            Assert.That(playerCondition.Id, Is.EqualTo("player-condition"));
            Assert.That(playerCondition.LabelText, Is.EqualTo("玩家状态"));
            var playerConditionEntries = entries
                .Where(item => item.CategoryId == "player-condition")
                .OrderBy(item => item.Order)
                .ToArray();
            Assert.That(playerConditionEntries.Select(item => item.CommandTemplate), Is.EqualTo(new[] { "#advance time ", "#unitbar " }));

            var advanceTime = playerConditionEntries.Single(item => item.Id == "condition.advance-time");
            Assert.That(advanceTime.LabelText, Is.EqualTo("推进时间"));
            Assert.That(advanceTime.DescriptionText, Is.EqualTo("推进游戏分钟，用于结算能量、远征时间与能量耗尽伤害。"));
            Assert.That(advanceTime.UsageText, Is.EqualTo("语法：#advance time <游戏分钟>"));
            Assert.That(advanceTime.ExampleText, Is.EqualTo("示例：#advance time 60"));

            var unitbar = playerConditionEntries.Single(item => item.Id == "condition.unitbar");
            Assert.That(unitbar.LabelText, Is.EqualTo("单位血条"));
            Assert.That(unitbar.DescriptionText, Is.EqualTo("开启或关闭玩家和存活敌人的头顶生命条。"));
            Assert.That(unitbar.UsageText, Is.EqualTo("语法：#unitbar on / #unitbar off"));
            Assert.That(unitbar.ExampleText, Is.EqualTo("示例：#unitbar on"));

            var zh = File.ReadAllText("Assets/Game/Config/Localization/zh-CN.json");
            var en = File.ReadAllText("Assets/Game/Config/Localization/en-US.json");
            Assert.That(RuntimeJson.TryParseStringTable(zh, out var zhTable), Is.True);
            Assert.That(RuntimeJson.TryParseStringTable(en, out var enTable), Is.True);
            foreach (var key in new[]
                     {
                         "item.ration.name",
                         "item.ration.desc",
                         "item.bottled_water.name",
                         "item.bottled_water.desc",
                         "ui.player_condition.power_icon",
                         "ui.player_condition.water_icon",
                         "ui.player_condition.temperature_icon",
                         "ui.player_condition.tili_icon",
                         "ui.player_condition.health_icon",
                         "ui.inventory.use_applied_water",
                         "ui.expedition.day_clock",
                         "ui.extraction.prompt.start",
                         "ui.extraction.prompt.countdown",
                         "ui.extraction.cancel.user",
                         "ui.extraction.cancel.attack",
                         "ui.extraction.cancel.damaged",
                         "ui.extraction.cancel.left_range",
                         "ui.extraction.return_failed",
                         "ui.expedition.status.go_to_extraction"
                     })
            {
                Assert.That(zhTable.ContainsKey(key), Is.True, key);
                Assert.That(enTable.ContainsKey(key), Is.True, key);
            }

            Assert.That(zhTable["ui.player_condition.power_icon"], Is.EqualTo("能"));
            Assert.That(enTable["ui.player_condition.power_icon"], Is.EqualTo("P"));
            Assert.That(zhTable["ui.player_condition.water_icon"], Is.EqualTo("水"));
            Assert.That(enTable["ui.player_condition.water_icon"], Is.EqualTo("W"));
            Assert.That(string.Format(zhTable["ui.extraction.prompt.countdown"], 5), Is.EqualTo("撤离中：5 秒"));
            Assert.That(string.Format(enTable["ui.extraction.prompt.countdown"], 5), Is.EqualTo("Extracting: 5s"));
        }

        private static IRuntimeConfigProvider CreateRuntimeConfigProvider()
        {
            return new LubanRuntimeConfigProvider(CreateTables);
        }

        private static global::Tables CreateTables()
        {
            return new global::Tables(name => new Luban.ByteBuf(File.ReadAllBytes($"Assets/Game/Config/{name}.bytes")));
        }

        private static void EnterBase()
        {
            var app = Game.Interface.GetSystem<AppStateSystem>();
            app.TryTransition(AppState.Loading);
            app.TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.BaseSafe, "test", false, out _);
            Game.Interface.GetModel<ExpeditionModel>().Clear();
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(
                new PlayerConfigDefinition("player.local", string.Empty, 100, 10, 4f, 1f, 1f, 1f, 1f),
                false);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(100);
        }

        private static void EnterExpedition(ExpeditionStatus status, int health)
        {
            Game.Interface.GetSystem<AppStateSystem>().TryTransition(AppState.Playing);
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.GetSystem<ExpeditionSystem>().BeginSurvival(MapIds.SurvivalSample, PlayerConditionRules.GameMinutesPerDay);
            if (status == ExpeditionStatus.ReturnQualified)
            {
                Game.Interface.GetModel<ExpeditionModel>().QualifyReturn();
            }
            else if (status == ExpeditionStatus.Returning)
            {
                Game.Interface.GetModel<ExpeditionModel>().QualifyReturn();
                Game.Interface.GetModel<ExpeditionModel>().BeginReturning();
            }
            else if (status == ExpeditionStatus.Defeated)
            {
                Game.Interface.GetModel<ExpeditionModel>().MarkDefeated();
            }

            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out _);
            Game.Interface.GetModel<CombatModel>().SetPlayerHP(health);
        }

        private static void BeginSurvival()
        {
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.SurvivalSample, null);
            Game.Interface.GetSystem<InventorySystem>().TrySetPlayerBagRiskState(PlayerBagRiskState.InExpedition, "test", false, out _);
            BeginConfigured(MapIds.SurvivalSample, TaskObjectiveType.Survive, PlayerConditionRules.GameMinutesPerDay);
        }

        private static void BeginConfigured(string mapId, TaskObjectiveType type, float target)
        {
            var taskId = type == TaskObjectiveType.Survive ? "expedition.survival-sample.survive" : "expedition.mission-sample.kill";
            var definition = new ExpeditionTaskDefinition(taskId, mapId, taskId, taskId, type, target, 100);
            Assert.That(Game.Interface.GetSystem<ExpeditionSystem>().TryBeginConfiguredExpedition(mapId, Guid.NewGuid().ToString("N"), new[] { definition }, out var error), Is.True, error);
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

        private static MapContext CreateMapContext(string mapId)
        {
            var root = new GameObject("map-root-" + mapId);
            var runtime = new GameObject("runtime-root").transform;
            runtime.SetParent(root.transform, false);
            var contextObject = new GameObject("map-context-" + mapId);
            var context = contextObject.AddComponent<MapContext>();
            SetPrivateField(context, "mapContentRoot", root);
            SetPrivateField(context, "dynamicObjectRoot", runtime);
            return context;
        }

        private static void SetPrivateField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
        {
            typeof(TTarget)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private void CreateUIRoot()
        {
            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UIRoot));
            createdObjects.Add(root);
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            foreach (var layer in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var child = new GameObject(layer, typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
                var rect = child.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), inputModuleType);
            createdObjects.Add(eventSystem);
            eventSystem.transform.SetParent(root.transform, false);
        }

        private static T GetUi<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            var field = typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(ctrl);
        }

        private static float InvokePowerFillAmount(float currentPower, float maxPower)
        {
            var method = typeof(PlayerConditionHudUICtrl).GetMethod(
                "CalculatePowerFillAmount",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (float)method.Invoke(null, new object[] { currentPower, maxPower });
        }

        private static Color InvokeTemperatureColor(float temperature)
        {
            var method = typeof(PlayerConditionHudUICtrl).GetMethod(
                "GetTemperatureColor",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (Color)method.Invoke(null, new object[] { temperature });
        }

        private sealed class TestMapContentLoader : IMapContentLoader
        {
            private readonly List<GameObject> objects;

            public TestMapContentLoader(List<GameObject> objects)
            {
                this.objects = objects;
            }

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

                var context = CreateMapContext(mapId);
                objects.Add(context.gameObject);
                objects.Add(context.MapContentRoot);
                objects.Add(context.DynamicObjectRoot.gameObject);
                return UniTask.FromResult(MapContentLoadResult.Ok(new TestMapHandle(mapId, context)));
            }

            public UniTask<MapContentUnloadResult> UnloadAsync(
                IMapContentHandle handle,
                CancellationToken cancellationToken = default)
            {
                if (handle is TestMapHandle testHandle)
                {
                    testHandle.Valid = false;
                }

                return UniTask.FromResult(MapContentUnloadResult.Ok());
            }
        }

        private sealed class TestMapHandle : IMapContentHandle
        {
            public TestMapHandle(string mapId, MapContext context)
            {
                MapId = mapId;
                Context = context;
            }

            public string MapId { get; }
            public MapContext Context { get; }
            public bool Valid { get; set; } = true;
            public bool IsValid => Valid;
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : UnityEngine.Object
            {
                if (typeof(T) == typeof(TextAsset))
                {
                    var file = Path.Combine("Assets/Game/Config/Localization", path);
                    return new TextAsset(File.ReadAllText(file)) as T;
                }

                return AssetDatabase.LoadAssetAtPath<T>(path.StartsWith("Assets/", StringComparison.Ordinal)
                    ? path
                    : $"Assets/Game/{path}.prefab");
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : UnityEngine.Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }
    }
}

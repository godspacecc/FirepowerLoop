using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using QFramework;
using System.Reflection;

namespace Game.Tests
{
    /// <summary>检查正式状态分页资产与权威数据展示，不生成或修补产品 Prefab。</summary>
    public sealed class StatusManagementUiTests
    {
        private InventoryIndependentUiTests environment;
        private GameObject poolRoot;
        private BagUI bag;
        private BagUICtrl host;

        [SetUp] public void SetUp()
        {
            environment = new InventoryIndependentUiTests();
            environment.SetUp();
            // 共用夹具只创建旧层；在测试实例中补齐当前枚举声明的层，不修改正式场景。
            var root = Object.FindObjectOfType<UIRoot>();
            foreach (var name in System.Enum.GetNames(typeof(UILayer)))
            {
                if (root.transform.Find(name) != null) continue;
                var layer = new GameObject(name, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
                var rect = (RectTransform)layer.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            }
            poolRoot = new GameObject("PoolRoot");
            var localization = Game.Interface.GetUtility<LocalizationUtility>();
            localization.InitializeConfiguredTables();
            foreach (var language in new[] { "zh-CN", "en-US" })
                localization.LoadFromJson(language, System.IO.File.ReadAllText(System.IO.Path.Combine(
                    Application.dataPath, "Game/Config/Localization/" + language + ".json")));
            UIHelper.SetPrefabLoaderForTests(path => AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/" + path + ".prefab"));
            Game.Interface.RegisterUtility<IRuntimeConfigProvider>((IRuntimeConfigProvider)
                typeof(ItemInventoryFoundationTests).GetMethod("CreateRealConfigProvider", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null));
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            host = UIHelper.GetUICtrl<BagUICtrl>();
            bag = (BagUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
        }
        [TearDown] public void TearDown()
        {
            environment.TearDown();
            Object.DestroyImmediate(poolRoot);
        }

        [Test] public void PowerAndWaterValuesUseOneDecimalWithoutChangingModels()
        {
            var condition = Game.Interface.GetModel<PlayerConditionModel>();
            condition.SetPower(72.345f);
            condition.SetWater(12.345f);
            bag.BtnPageStatus.onClick.Invoke();
            var rows = bag.StatusPage.StatusList.GetDraggableItems();
            rows[3].GetComponent<StatusItemBoxUI>().ClickButton.onClick.Invoke();
            Assert.That(bag.StatusPage.DetailValueText.text, Is.EqualTo("72.3 / 100.0"));
            rows[2].GetComponent<StatusItemBoxUI>().ClickButton.onClick.Invoke();
            Assert.That(bag.StatusPage.DetailValueText.text, Is.EqualTo("12.3 / 100.0"));
            Assert.That(condition.Power, Is.EqualTo(72.345f));
            Assert.That(condition.Water, Is.EqualTo(12.345f));
        }

        [Test] public void StatusTabRendersAuthoritativeValuesAndPlaceholderBoundaries()
        {
            bag.BtnPageStatus.onClick.Invoke();
            Assert.That(bag.PlaceholderRoot.activeSelf, Is.False);
            var list = bag.StatusPage.StatusList;
            Assert.That(list.totalCount, Is.EqualTo(4));
            var rows = list.GetDraggableItems();
            Assert.That(rows[0].GetComponent<StatusItemBoxUI>().NameText.text, Is.EqualTo("体温"));
            Assert.That(rows[1].GetComponent<StatusItemBoxUI>().NameText.text, Is.EqualTo("体力"));
            Assert.That(bag.StatusPage.HealthValueText.text, Is.EqualTo("100 / —"));
            var afflictions = (CustomListView)bag.StatusPage.GetType().GetField("AfflictionList").GetValue(bag.StatusPage);
            Assert.That(afflictions.totalCount, Is.EqualTo(2));
            foreach (var item in rows)
            {
                var row = item.GetComponent<StatusItemBoxUI>();
                Assert.That(row.Icon.sprite, Is.Not.Null);
                Assert.That(row.MeterRoot.activeSelf, Is.True);
                Assert.That(row.MeterFill.fillMethod, Is.EqualTo(UnityEngine.UI.Image.FillMethod.Radial360));
                Assert.That(row.ValueText.gameObject.activeSelf, Is.False);
                Assert.That(row.ClickButton.image.color.a, Is.Zero);
            }
            afflictions.GetDraggableItems()[0].GetComponent<StatusAfflictionBoxUI>().ClickButton.onClick.Invoke();
            Assert.That(afflictions.GetDraggableItems()[0].GetComponent<StatusItemBoxUI>(), Is.Null);
            Assert.That(rows[0].GetComponent<StatusItemBoxUI>().Selected.activeSelf, Is.False);
            Assert.That(bag.StatusPage.DetailValueText.text, Is.EqualTo("—"));
            Assert.That(bag.StatusPage.DetailDescriptionText.text, Does.Contain("尚未实现"));
            afflictions.GetDraggableItems()[1].GetComponent<StatusAfflictionBoxUI>().ClickButton.onClick.Invoke();
            Assert.That(bag.StatusPage.DetailDescriptionText.text, Does.Contain("没有疾病状态数据"));
            Assert.That(UIHelper.HasUIOpen, Is.True);
        }
        [Test]
        public void StatusPageIsAFormalNestedPrefabWithGeneratedReferences()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Page/StatusManagement.prefab");
            Assert.That(page, Is.Not.Null, "状态分页必须有正式 Prefab");
            Assert.That(page.GetComponent<ReferenceCollector>(), Is.Not.Null);
            var body = page.transform.Find("BodyPanel/BodyFigure/BodySilhouette").GetComponent<UnityEngine.UI.Image>();
            Assert.That(body.sprite, Is.Not.Null);
            Assert.That(body.raycastTarget, Is.False);
            Assert.That(page.GetComponent<StatusManagementPageUI>().DetailDescriptionText.enableWordWrapping, Is.True);
            var view = page.GetComponent<BaseUI>();
            Assert.That(view, Is.Not.Null);
            foreach (var field in view.GetType().GetFields())
                if (typeof(Object).IsAssignableFrom(field.FieldType))
                    Assert.That((Object)field.GetValue(view), Is.Not.Null, field.Name);
            var list = page.GetComponentInChildren<CustomListView>(true);
            Assert.That(list, Is.Not.Null);
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(list.itemPrefab), Is.True);
            Assert.That(list.itemPrefab.GetComponent<BaseBoxUI>(), Is.Not.Null);
            var row = list.itemPrefab.GetComponent<BaseBoxUI>();
            Assert.That(list.itemPrefab.GetComponent<ReferenceCollector>(), Is.Not.Null);
            foreach (var field in row.GetType().GetFields())
                if (typeof(Object).IsAssignableFrom(field.FieldType))
                    Assert.That((Object)field.GetValue(row), Is.Not.Null, field.Name);
            var affliction = page.GetComponent<StatusManagementPageUI>().AfflictionList.itemPrefab;
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(affliction), Is.True);
            Assert.That(affliction.GetComponent<ReferenceCollector>(), Is.Not.Null);
            var afflictionView = affliction.GetComponent<StatusAfflictionBoxUI>();
            Assert.That(afflictionView, Is.Not.Null);
            foreach (var field in afflictionView.GetType().GetFields())
                if (typeof(Object).IsAssignableFrom(field.FieldType))
                    Assert.That((Object)field.GetValue(afflictionView), Is.Not.Null, field.Name);
            var bag = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Inventory/Bag.prefab").GetComponent<BagUI>();
            var status = bag.GetType().GetField("StatusPage");
            Assert.That(status, Is.Not.Null);
            Assert.That((Object)status.GetValue(bag), Is.Not.Null);
        }
        [Test] public void ConditionEventsRefreshSelectedDetailAndReuseRows()
        {
            bag.BtnPageStatus.onClick.Invoke();
            var page = bag.StatusPage;
            var rows = page.StatusList.GetDraggableItems();
            var water = rows[2].GetComponent<StatusItemBoxUI>();
            water.ClickButton.onClick.Invoke();
            var system = Game.Interface.GetSystem<PlayerConditionSystem>();
            Assert.That(system.TrySetWater(12.5f, out _, out _, out _), Is.True);
            Assert.That(page.DetailValueText.text, Is.EqualTo("12.5 / 100.0"));
            Assert.That(page.DetailStateText.text, Is.EqualTo("低于 20%"));
            Assert.That(water.StateText.text, Is.EqualTo(page.DetailStateText.text));
            Assert.That(water.MeterFill.fillAmount, Is.EqualTo(.125f).Within(.0001f));
            Assert.That(page.StatusList.GetDraggableItems()[2], Is.SameAs(rows[2]));
            Assert.That(system.TrySetMaxWater(150f, out _, out _, out _), Is.True);
            Assert.That(page.DetailValueText.text, Is.EqualTo("12.5 / 150.0"));
            Assert.That(water.MeterFill.fillAmount, Is.EqualTo(12.5f / 150).Within(.0001f));
            Assert.That(system.TrySetWater(150f, out _, out _, out _), Is.True);
            Assert.That(page.DetailStateText.text, Is.EqualTo("当前可用"));
            rows[0].GetComponent<StatusItemBoxUI>().ClickButton.onClick.Invoke();
            Assert.That(system.TrySetTemperature(39f, out _, out _, out _), Is.True);
            Assert.That(page.DetailValueText.text, Is.EqualTo("39 °C"));
            Assert.That(page.DetailStateText.text, Is.EqualTo("体温偏高"));
            Assert.That(rows[0].GetComponent<StatusItemBoxUI>().MeterFill.fillAmount, Is.EqualTo(1));
            Assert.That(rows[0].GetComponent<StatusItemBoxUI>().Icon.color.r, Is.GreaterThan(rows[0].GetComponent<StatusItemBoxUI>().Icon.color.g));
            Assert.That(page.DetailDescriptionText.text, Does.Contain("尚未实现"));
        }

        [Test] public void HealthEventsRefreshCurrentHealthAndConfiguredRecovery()
        {
            var units = Game.Interface.GetSystem<UnitSystem>();
            Assert.That(units.TryGet(PlayerModel.LocalPlayerUnitId, out var before), Is.True);
            units.Remove(before.UnitId, UnitRemovedReason.Explicit);
            Assert.That(units.TryRegister(new UnitSnapshot(before.UnitId, before.MapInstanceId, before.UnitType,
                before.UnitSource, "player.graybox.default", before.Position, before.Rotation,
                before.AuthorityMode, before.OwnerUnitId, before.IsActive), out var error), Is.True, error);
            Assert.That(Game.Interface.GetUtility<IRuntimeConfigProvider>().TryGetPlayer("player.graybox.default", out var config, out _), Is.True);
            Game.Interface.GetModel<CombatModel>().ApplyPlayerConfig(config, false);
            bag.BtnPageStatus.onClick.Invoke();
            var combat = Game.Interface.GetModel<CombatModel>();
            combat.PlayerHP.Value = 23;
            Game.Interface.SendEvent(new CombatHealthChangedEvent(PlayerModel.LocalPlayerUnitId, CombatFaction.Player, 23, combat.PlayerMaxHP));
            Assert.That(bag.StatusPage.DetailValueText.text, Is.EqualTo("23 / " + combat.PlayerMaxHP));
            Assert.That(bag.StatusPage.HealthValueText.text, Is.EqualTo(bag.StatusPage.DetailValueText.text));
            Assert.That(bag.StatusPage.HealthFill.fillAmount, Is.EqualTo(23f / combat.PlayerMaxHP).Within(.0001f));
            bag.StatusPage.StatusList.GetDraggableItems()[2].GetComponent<StatusItemBoxUI>().ClickButton.onClick.Invoke();
            bag.StatusPage.HealthButton.onClick.Invoke();
            Assert.That(bag.StatusPage.DetailNameText.text, Is.EqualTo("生命"));
            Assert.That(bag.StatusPage.DetailDescriptionText.text, Is.EqualTo(Loc.T("ui.status.health_detail",
                config.BaseRecoveryPerSecond.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
        }

        [Test] public void MissingHealthConfigurationDoesNotInventRecoveryRate()
        {
            bag.BtnPageStatus.onClick.Invoke();
            Assert.That(bag.StatusPage.DetailDescriptionText.text, Is.EqualTo(Loc.T("ui.status.health_config_unavailable")));
            Assert.That(bag.StatusPage.DetailValueText.text, Is.EqualTo("100 / —"));
            Assert.That(bag.StatusPage.DetailStateText.text, Is.EqualTo(Loc.T("ui.status.config_unavailable")));
        }

        [Test] public void UnimplementedTabsRemainPlaceholdersAndHideStatusSubscriptions()
        {
            bag.BtnPageStatus.onClick.Invoke();
            foreach (var page in new[] { BagManagementPage.Map, BagManagementPage.Tasks, BagManagementPage.Journal })
            {
                host.SelectPage(page);
                Assert.That(bag.PlaceholderRoot.activeSelf, Is.True);
                Assert.That(bag.StatusPage.gameObject.activeSelf, Is.False);
                Assert.That(bag.EquipmentPage.gameObject.activeSelf, Is.False);
            }
            var events = Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            // EditMode 不自动调用 EventSystem.OnEnable，显式登记并成对释放，验证真实关闭逻辑。
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnEnable", flags).Invoke(events, null);
            try
            {
                UnityEngine.EventSystems.EventSystem.current = events;
                Assert.That(UnityEngine.EventSystems.EventSystem.current, Is.SameAs(events));
                events.SetSelectedGameObject(bag.BtnPageStatus.gameObject);
                host.Hide();
                Assert.That(events.currentSelectedGameObject, Is.Null);
            }
            finally { typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnDisable", flags).Invoke(events, null); }
        }

        [Test] public void StatusLocalizationExistsInBothLanguagesWithoutDynamicOwnershipConflicts()
        {
            foreach (var language in new[] { "zh-CN", "en-US" })
            {
                var table = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(
                    Application.dataPath, "Game/Config/Localization/" + language + ".json")));
                foreach (var key in new[] { "health", "power", "water", "temperature", "tili", "injury", "disease" })
                {
                    Assert.That(table["ui.status." + key], Is.Not.Null);
                    Assert.That(table["ui.status." + key + "_detail"], Is.Not.Null);
                }
                foreach (var key in new[] { "attributes", "body", "afflictions" })
                    Assert.That(table["ui.status." + key], Is.Not.Null);
            }
            foreach (var path in new[] { "Inventory/Page/StatusManagement", "Inventory/Box/StatusItem", "Inventory/Box/StatusAffliction" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/" + path + ".prefab");
                foreach (var text in prefab.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    var localized = text.GetComponent<LocalizedText>();
                    if (localized != null)
                        Assert.That(new[] { "ui.status.attributes", "ui.status.body", "ui.status.afflictions", "ui.status.health" }, Does.Contain(localized.Key));
                    Assert.That(text.text, Does.Not.StartWith("ui."));
                }
            }
        }

        [Test] public void LanguageChangeKeepsSelectionAndHiddenPageCatchesUpOnShow()
        {
            bag.BtnPageStatus.onClick.Invoke();
            var page = bag.StatusPage;
            page.StatusList.GetDraggableItems()[1].GetComponent<StatusItemBoxUI>().ClickButton.onClick.Invoke();
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(page.DetailNameText.text, Is.EqualTo("Stamina"));
            var oldValue = page.DetailValueText.text;
            bag.BtnPageBackpack.onClick.Invoke();
            Assert.That(Game.Interface.GetSystem<PlayerConditionSystem>().TrySetTili(42f, out _, out _, out _), Is.True);
            Assert.That(page.DetailValueText.text, Is.EqualTo(oldValue));
            bag.BtnPageStatus.onClick.Invoke();
            Assert.That(page.DetailValueText.text, Is.EqualTo("42 / 100"));
            Assert.That(page.DetailNameText.text, Is.EqualTo("Stamina"));
            Assert.That(page.StatusList.GetDraggableItems().Count, Is.EqualTo(4));
        }

        [Test] public void HideAndDestroyReleaseSubscriptionsAndReopenRebindsPage()
        {
            bag.BtnPageStatus.onClick.Invoke();
            var ctrl = (StatusManagementPageCtrl)typeof(BagUICtrl).GetField("statusPage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            var handles = (System.Collections.ICollection)typeof(StatusManagementPageCtrl).GetField("subscriptions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ctrl);
            Assert.That(handles.Count, Is.EqualTo(4));
            bag.BtnPageStatus.onClick.Invoke();
            Assert.That(handles.Count, Is.EqualTo(4));
            host.Hide();
            Assert.That(handles.Count, Is.Zero);
            Assert.That(BagUICtrl.TryShow(out _), Is.True);
            bag.BtnPageStatus.onClick.Invoke();
            Assert.That(handles.Count, Is.EqualTo(4));
            Object.DestroyImmediate(bag.gameObject);
            Assert.That(handles.Count, Is.Zero);
            Assert.DoesNotThrow(() => Game.Interface.SendEvent(new PlayerConditionChangedEvent()));
            Assert.That(BagUICtrl.TryShow(out var error), Is.True, error);
            bag = (BagUI)typeof(BaseUICtrl).GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            bag.BtnPageStatus.onClick.Invoke();
            Assert.That(bag.StatusPage.DetailNameText.text, Is.EqualTo("生命"));
            Assert.That(handles.Count, Is.EqualTo(4));
        }
    }
}

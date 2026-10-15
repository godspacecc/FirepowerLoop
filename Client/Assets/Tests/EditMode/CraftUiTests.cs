using System;
using System.Collections.Generic;
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
    public sealed class CraftUiTests
    {
        private GameObject floatPrefab;
        private TestConfigProvider configProvider;

        [SetUp]
        public void SetUp()
        {
            TestReset.ResetArchitecture();
            LocalizationLanguages.ResetForTests();
            UIHelper.ResetForTests();
            ResourceLoader.SetLoaderForTests(new TestResourceLoader());
            CreateUIRoot();
            configProvider = new TestConfigProvider();
            InstallArchitecture();
            InstallLocalization();
            SetBaseState();
            floatPrefab = CreateFloatPrefab();
            UIHelper.SetPrefabLoaderForTests(path =>
            {
                if (path == "Prefabs/UI/Craft/Craft")
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Craft/Craft.prefab");
                }

                if (path == "Prefabs/UI/Notify/Float")
                {
                    return floatPrefab;
                }

                return AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/{path}.prefab");
            });
        }

        [TearDown]
        public void TearDown()
        {
            UIHelper.ResetForTests();
            ResourceLoader.ResetForTests();
            LocalizationLanguages.ResetForTests();
            TestReset.ResetArchitecture();
            DestroySceneObjects();
            Object.DestroyImmediate(floatPrefab);
        }

        [Test]
        public void CraftPrefabsKeepCollectorAndGeneratedViewContractLegacyCoverage()
        {
            CraftPrefabsKeepCollectorAndGeneratedViewContract();
        }

        [Test]
        public void CraftItemIconsLoadAsSprites()
        {
            foreach (var path in new[]
                     {
                         "Assets/Game/Art/UI/Items/IconMetalParts.png",
                         "Assets/Game/Art/UI/Items/IconCloth.png",
                         "Assets/Game/Art/UI/Items/IconMedkit.png",
                         "Assets/Game/Art/UI/Items/IconArrow.png"
                     })
            {
                Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(path), Is.Not.Null, path);
            }
        }

        [Test]
        public void CraftUiShowHideRefreshesAndRealButtonCraftsOneBatch()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out var error), Is.True, error);

            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<CraftUICtrl>();
            var ui = GetBaseUI<CraftUI>(ctrl);
            Assert.That(ctrl.IsShow(), Is.True);
            Assert.That(UIHelper.HasUIOpen, Is.True);
            Assert.That(ui.RecipeList.transform.Cast<Transform>().Count(child => child.gameObject.activeSelf), Is.EqualTo(2));
            var row = ui.RecipeList.transform.Cast<Transform>()
                .Select(child => child.GetComponent<CraftRecipeBoxUI>())
                .First(box => box != null && box.TextName.text.Contains("急救包"));
            Assert.That(row.TextOutputAmount.text, Is.EqualTo("×1"));
            row.BtnSelect.onClick.Invoke();
            Assert.That(ui.MaterialList.GetComponentsInChildren<CraftMaterialBoxUI>(true).Single().TextOwnedRequired.text, Is.EqualTo("2 / 2"));
            Assert.That(ui.BtnCraft.interactable, Is.True);

            ui.BtnCraft.onClick.Invoke();
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
            Assert.That(ui.BtnCraft.interactable, Is.False);
            Assert.That(row.TextReason.text, Is.EqualTo("材料不足"));
            var floatUi = GetBaseUI<FloatUI>(UIHelper.GetUICtrl<FloatUICtrl>());
            var visibleTip = floatUi.TipsRoot.GetComponentsInChildren<FloatTextBoxUI>(false).Single();
            Assert.That(visibleTip.TextTips.text, Is.EqualTo("已制作 急救包 ×1"));

            var stateBeforeClose = Game.Interface.GetModel<AppStateModel>().CurrentState.Value;
            ui.BtnClose.onClick.Invoke();
            Assert.That(ctrl.IsShow(), Is.False);
            Assert.That(UIHelper.HasUIOpen, Is.False);
            Assert.That(Game.Interface.GetModel<AppStateModel>().CurrentState.Value, Is.EqualTo(stateBeforeClose));
            Assert.That(row.BtnSelect.onClick.GetPersistentEventCount(), Is.EqualTo(0));

            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out error), Is.True, error);
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            row = ui.RecipeList.transform.Cast<Transform>()
                .Select(child => child.GetComponent<CraftRecipeBoxUI>())
                .First(box => box != null && box.TextName.text.Contains("急救包"));
            row.BtnSelect.onClick.Invoke();
            Assert.That(ui.BtnCraft.interactable, Is.True);
            Assert.That(ui.MaterialList.GetComponentsInChildren<CraftMaterialBoxUI>(true).Single().TextOwnedRequired.text, Is.EqualTo("2 / 2"));
        }

        [Test]
        public void CraftUiReusesListAndRespondsToInventoryAndLanguageEvents()
        {
            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<CraftUICtrl>();
            var ui = GetBaseUI<CraftUI>(ctrl);
            Assert.That(ui.RecipeList.transform.Cast<Transform>().Count(child => child.gameObject.activeSelf), Is.EqualTo(2));
            var medkit = ui.RecipeList.transform.Cast<Transform>()
                .Select(child => child.GetComponent<CraftRecipeBoxUI>())
                .First(box => box != null && box.TextName.text.Contains("急救包"));
            medkit.BtnSelect.onClick.Invoke();
            Assert.That(ui.BtnCraft.interactable, Is.False);

            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.Cloth, 2, 20, out _, out error), Is.True, error);
            Game.Interface.SendEvent(new InventoryChangedEvent());
            Assert.That(ui.BtnCraft.interactable, Is.True);

            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(medkit.TextName.text, Does.Contain("Medkit"));

            var reused = UIHelper.ShowUI<CraftUICtrl>();
            Assert.That(reused, Is.SameAs(ctrl));
            var previousUi = GetBaseUI<CraftUI>(ctrl);
            Object.DestroyImmediate(previousUi.gameObject);
            var reloaded = UIHelper.ShowUI<CraftUICtrl>();
            Assert.That(reloaded, Is.SameAs(ctrl));
            Assert.That(GetBaseUI<CraftUI>(reloaded), Is.Not.Null);
            Assert.That(GetBaseUI<CraftUI>(reloaded), Is.Not.SameAs(previousUi));
        }

        [Test]
        public void CraftMaterialRows_UseExplicitEnoughAndInsufficientColorsAcrossReuse()
        {
            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            var row = ui.RecipeList.transform.Cast<Transform>()
                .Select(child => child.GetComponent<CraftRecipeBoxUI>())
                .First(box => box != null && box.TextName.text.Contains("急救包"));
            row.BtnSelect.onClick.Invoke();

            var material = ui.MaterialList.GetComponentsInChildren<CraftMaterialBoxUI>(true).Single();
            Assert.That(material.TextOwnedRequired.color, Is.EqualTo(Color.red));
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 2, 20, out _, out error), Is.True, error);
            Game.Interface.SendEvent(new InventoryChangedEvent());
            Assert.That(material.TextOwnedRequired.color, Is.EqualTo(Color.white));

            ui.BtnBatchPlus.onClick.Invoke();
            Assert.That(material.TextOwnedRequired.color, Is.EqualTo(Color.red));
        }

        [Test]
        public void TryShowRejectsUnsafeBagWithoutLoadingThenOpensWhenBaseSafe()
        {
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.InExpedition);

            Assert.That(CraftUICtrl.TryShow(out var error), Is.False);
            Assert.That(error, Does.Contain("玩家背包处于安全状态"));
            Assert.That(UIHelper.GetUICtrl<CraftUICtrl>(), Is.Null);
            Assert.That(UIHelper.HasUIOpen, Is.False);

            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            Assert.That(UIHelper.GetUICtrl<CraftUICtrl>().IsShow(), Is.True);
        }

        [Test]
        public void CraftUiBatchControlsClampAndPrimaryButtonUsesCurrentBatch()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 4, 20, out _, out var error), Is.True, error);
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());

            Assert.That(ui.TextBatchCount.text, Is.EqualTo("批数 1"));
            Assert.That(ui.BtnBatchMinus.interactable, Is.False);
            ui.BtnBatchMinus.onClick.Invoke();
            Assert.That(ui.TextBatchCount.text, Is.EqualTo("批数 1"));

            for (var i = 0; i < 98; i++)
            {
                ui.BtnBatchPlus.onClick.Invoke();
            }

            Assert.That(ui.TextBatchCount.text, Is.EqualTo("批数 99"));
            Assert.That(ui.BtnBatchPlus.interactable, Is.False);
            ui.BtnBatchPlus.onClick.Invoke();
            Assert.That(ui.TextBatchCount.text, Is.EqualTo("批数 99"));

            for (var i = 0; i < 98; i++)
            {
                ui.BtnBatchMinus.onClick.Invoke();
            }

            Assert.That(ui.TextBatchCount.text, Is.EqualTo("批数 1"));
            ui.BtnBatchPlus.onClick.Invoke();
            Assert.That(ui.TextBatchCount.text, Is.EqualTo("批数 2"));
            Assert.That(ui.TextOutputTotal.text, Is.EqualTo("总产出 ×2"));
            var material = ui.MaterialList.transform.Cast<Transform>()
                .Where(child => child.gameObject.activeSelf)
                .Select(child => child.GetComponent<CraftMaterialBoxUI>())
                .Single();
            Assert.That(material.TextOwnedRequired.text, Is.EqualTo("4 / 4"));
            Assert.That(ui.BtnCraft.interactable, Is.True);

            ui.BtnCraft.onClick.Invoke();
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));
        }

        [Test]
        public void CraftUiCategoryKeepsUncraftableRecipeSelectable()
        {
            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            var ammunitionCategory = ui.CategoryList.transform.Cast<Transform>()
                .Where(child => child.gameObject.activeSelf)
                .Select(child => child.GetComponent<CraftCategoryBoxUI>())
                .Single(box => box.TextName.text == "弹药");

            ammunitionCategory.BtnSelect.onClick.Invoke();

            var rows = ui.RecipeList.transform.Cast<Transform>().Where(child => child.gameObject.activeSelf).ToArray();
            Assert.That(rows.Length, Is.EqualTo(1));
            var arrow = rows[0].GetComponent<CraftRecipeBoxUI>();
            Assert.That(arrow.TextName.text, Does.Contain("箭矢"));
            Assert.That(arrow.BtnSelect.interactable, Is.True);
            arrow.BtnSelect.onClick.Invoke();
            Assert.That(ui.TextDetailName.text, Is.EqualTo("箭矢"));
            Assert.That(ui.BtnCraft.interactable, Is.False);
        }

        [Test]
        public void CraftCategoryButtonOverlayStaysTransparentAndTextRemainsVisible()
        {
            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            var categories = ActiveBoxes<CraftCategoryBoxUI>(ui.CategoryList).ToArray();
            Assert.That(categories.Length, Is.GreaterThan(1));
            AssertCategoryOverlayState(categories);

            var target = categories[1];
            target.BtnSelect.onClick.Invoke();

            categories = ActiveBoxes<CraftCategoryBoxUI>(ui.CategoryList).ToArray();
            AssertCategoryOverlayState(categories);
            Assert.That(categories[1].TextName.text, Is.Not.Empty);
            Assert.That(categories[1].SelectedRoot.activeSelf, Is.True);
            Assert.That(categories[0].SelectedRoot.activeSelf, Is.False);
        }

        [Test]
        public void RepeatedTryShowAndDestroyedReloadCraftOnlyOneBatchPerRealClick()
        {
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 4, 20, out _, out var error), Is.True, error);
            var completedEvents = 0;
            Game.Interface.RegisterEvent<CraftCompletedEvent>(_ => completedEvents++);

            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            var ctrl = UIHelper.GetUICtrl<CraftUICtrl>();
            var ui = GetBaseUI<CraftUI>(ctrl);
            SelectAndCraft(ui, GetRowByOutput(ui, "急救包"));
            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(1));
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(2));
            Assert.That(completedEvents, Is.EqualTo(1));

            var oldUi = ui;
            Object.DestroyImmediate(oldUi.gameObject);
            Game.Interface.SendEvent(new InventoryChangedEvent());
            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            var newUi = GetBaseUI<CraftUI>(ctrl);
            Assert.That(newUi, Is.Not.SameAs(oldUi));
            SelectAndCraft(newUi, GetRowByOutput(newUi, "Medkit"));

            Assert.That(bag.GetTotalAmount(ItemIds.Medkit), Is.EqualTo(2));
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(0));
            Assert.That(completedEvents, Is.EqualTo(2));
        }

        [Test]
        public void ControlledProviderShrinkExpandOverwritesReusedRowsAndClickIntentions()
        {
            configProvider.SetRecipes(
                TestConfigProvider.DefaultMedkitRecipe,
                TestConfigProvider.DefaultArrowRecipe,
                new CraftRecipeDefinition(
                    "craft.water.test",
                    ItemIds.BottledWater,
                    1,
                    new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) },
                    300));
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 4, 20, out _, out var error), Is.True, error);
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            AssertActiveRows(ui, 3);
            AssertRow(ui, 0, "急救包", "×1", false, false, string.Empty);
            AssertRow(ui, 1, "箭矢", "×5", false, true, "材料不足");
            AssertRow(ui, 2, "瓶装水", "×1", true, false, string.Empty);

            configProvider.SetRecipes(TestConfigProvider.DefaultArrowRecipe);
            Game.Interface.SendEvent(new InventoryChangedEvent());
            AssertActiveRows(ui, 1);
            AssertRow(ui, 0, "箭矢", "×5", false, true, "材料不足");

            configProvider.SetRecipes(
                TestConfigProvider.DefaultMedkitRecipe,
                TestConfigProvider.DefaultArrowRecipe,
                new CraftRecipeDefinition(
                    "craft.water.test",
                    ItemIds.BottledWater,
                    1,
                    new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) },
                    300));
            Game.Interface.SendEvent(new InventoryChangedEvent());
            AssertActiveRows(ui, 3);
            var water = GetRowByOutput(ui, "瓶装水");
            SelectAndCraft(ui, water);
            Assert.That(bag.GetTotalAmount(ItemIds.BottledWater), Is.EqualTo(1));
            Assert.That(bag.GetTotalAmount(ItemIds.Cloth), Is.EqualTo(3));
        }

        [Test]
        public void DisabledReasonsAndLanguageSwitchRefreshAllCraftTexts()
        {
            configProvider.SetRecipes(
                TestConfigProvider.DefaultMedkitRecipe,
                new CraftRecipeDefinition(
                    "craft.capacity.test",
                    ItemIds.Medkit,
                    500,
                    new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) },
                    200));
            var bag = Game.Interface.GetModel<PlayerBagModel>().Container;
            var warehouse = Game.Interface.GetModel<WarehouseModel>().Container;
            Assert.That(bag.TryAdd(ItemIds.Cloth, 1, 20, out _, out var error), Is.True, error);
            for (var i = 1; i < bag.Capacity; i++)
            {
                Assert.That(bag.TryAdd(ItemIds.Ration, 1, 1, out _, out error), Is.True, error);
            }

            for (var i = 0; i < warehouse.Capacity; i++)
            {
                Assert.That(warehouse.TryAdd(ItemIds.Ration, 1, 1, out _, out error), Is.True, error);
            }

            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            var normalRow = GetRowByOutput(ui, "急救包", "×1");
            var capacityRowZh = GetRowByOutput(ui, "急救包", "×500");
            Assert.That(normalRow.TextReason.text, Is.EqualTo("材料不足"));
            Assert.That(capacityRowZh.TextReason.text, Is.Empty);
            capacityRowZh.BtnSelect.onClick.Invoke();
            Assert.That(ui.BtnCraft.interactable, Is.True);

            Game.Interface.GetModel<LocalizationModel>().SetLanguageCode("en-US");
            Game.Interface.SendEvent(new LanguageChangedEvent("zh-CN", "en-US"));
            Assert.That(GetLocalizedText(ui.gameObject, "ui.craft.title").text, Is.EqualTo("Craft"));
            Assert.That(GetLocalizedText(ui.gameObject, "ui.craft.output_flow", "OutputFlow").text, Is.EqualTo("Output goes to the bag first, then base storage."));
            var capacityRow = GetRowByOutput(ui, "Medkit", "x500");
            Assert.That(capacityRow.TextOutputAmount.text, Is.EqualTo("x500"));
            capacityRow.BtnSelect.onClick.Invoke();
            Assert.That(ui.MaterialList.GetComponentsInChildren<CraftMaterialBoxUI>(true).Single().TextOwnedRequired.text, Is.EqualTo("1 / 1"));
        }

        [Test]
        public void CraftVisibleLabelsAndIconsKeepDistinctReadableRegions()
        {
            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            AssertCraftReadableRegions(ui);

            var categories = ActiveBoxes<CraftCategoryBoxUI>(ui.CategoryList).ToArray();
            categories.Last().BtnSelect.onClick.Invoke();
            var recipe = ActiveBoxes<CraftRecipeBoxUI>(ui.RecipeList).Single();
            recipe.BtnSelect.onClick.Invoke();
            AssertCraftReadableRegions(ui);
        }

        [Test]
        public void CraftPrefabsKeepCollectorAndGeneratedViewContract()
        {
            AssertPrefabContract<CraftUI>("Assets/Game/Prefabs/UI/Craft/Craft.prefab", new[] { "BtnClose", "CategoryList", "CategoryGrid", "RecipeList", "DetailIcon", "TextDetailName", "TextDetailDesc", "TextSingleOutput", "TextBatchCount", "BtnBatchMinus", "BtnBatchPlus", "TextOutputTotal", "MaterialList", "ReasonRoot", "TextReason", "BtnCraft" });
            AssertPrefabContract<CraftCategoryBoxUI>("Assets/Game/Prefabs/UI/Craft/Box/CraftCategoryBox.prefab", new[] { "TextName", "SelectedRoot", "BtnSelect" });
            AssertPrefabContract<CraftRecipeBoxUI>("Assets/Game/Prefabs/UI/Craft/Box/CraftRecipeBox.prefab", new[] { "Icon", "TextName", "TextOutputAmount", "SelectedRoot", "DisabledRoot", "TextReason", "BtnSelect" });
            AssertPrefabContract<CraftMaterialBoxUI>("Assets/Game/Prefabs/UI/Craft/Box/CraftMaterialBox.prefab", new[] { "Icon", "TextName", "TextOwnedRequired" });
            var materialPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Craft/Box/CraftMaterialBox.prefab");
            Assert.That(materialPrefab.transform.Find("InsufficientRoot"), Is.Null);
            var craft = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Craft/Craft.prefab").GetComponent<CraftUI>();
            Assert.That(craft.CategoryList.itemPrefab.GetComponent<CraftCategoryBoxUI>(), Is.Not.Null);
            Assert.That(craft.RecipeList.itemPrefab.GetComponent<CraftRecipeBoxUI>(), Is.Not.Null);
            Assert.That(craft.MaterialList.itemPrefab.GetComponent<CraftMaterialBoxUI>(), Is.Not.Null);
            Assert.That(((RectTransform)craft.MaterialList.transform).rect.width, Is.EqualTo(340f).Within(.01f));
            Assert.That(craft.MaterialList.itemPrefab.GetComponent<RectTransform>().rect.width, Is.EqualTo(340f).Within(.01f));
        }

        [Test]
        public void CraftListsUseSerializedLayoutWithoutRuntimeRectRewrite()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Craft/Craft.prefab");
            AssertGrid(prefab.transform.Find("CategoryList").GetComponent<GridLayoutGroup>(), new Vector2(150, 42), new Vector2(8, 0), GridLayoutGroup.Constraint.FixedRowCount);
            AssertGrid(prefab.transform.Find("RecipeList").GetComponent<GridLayoutGroup>(), new Vector2(260, 76), new Vector2(0, 8), GridLayoutGroup.Constraint.FixedColumnCount);
            AssertGrid(prefab.transform.Find("MaterialList").GetComponent<GridLayoutGroup>(), new Vector2(340, 44), new Vector2(0, 6), GridLayoutGroup.Constraint.FixedColumnCount);

            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            var recipeBefore = ActiveBoxes<CraftRecipeBoxUI>(ui.RecipeList).Select(RectState).ToArray();
            var materialBefore = ActiveBoxes<CraftMaterialBoxUI>(ui.MaterialList).Select(RectState).ToArray();
            ui.BtnBatchPlus.onClick.Invoke();
            Assert.That(ActiveBoxes<CraftRecipeBoxUI>(ui.RecipeList).Select(RectState), Is.EqualTo(recipeBefore));
            Assert.That(ActiveBoxes<CraftMaterialBoxUI>(ui.MaterialList).Select(RectState), Is.EqualTo(materialBefore));
        }

        [Test]
        public void CraftCategoryListWidthMatchesActiveCategoryCount()
        {
            AssertCategoryLayout(Array.Empty<CraftRecipeDefinition>(), 1, 150f, 150f);
            AssertCategoryLayout(new[] { TestConfigProvider.DefaultMedkitRecipe, TestConfigProvider.DefaultArrowRecipe }, 3, 466f, 150f);

            configProvider.SetRecipes(TestConfigProvider.DefaultMedkitRecipe, TestConfigProvider.DefaultArrowRecipe,
                TestConfigProvider.DefaultMaterialRecipe, TestConfigProvider.DefaultWeaponRecipe);
            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            AssertCategoryGeometry(ui, 5, 680f, 129.6f);
        }

        [Test]
        public void CraftListItemVisibleGraphicsStayInsideTheirCells()
        {
            Assert.That(Game.Interface.GetModel<PlayerBagModel>().Container.TryAdd(ItemIds.Cloth, 2, 20, out _, out var error), Is.True, error);
            Assert.That(CraftUICtrl.TryShow(out error), Is.True, error);
            var ui = GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>());
            Canvas.ForceUpdateCanvases();
            foreach (var box in ActiveBoxes<CraftCategoryBoxUI>(ui.CategoryList).Cast<Component>()
                         .Concat(ActiveBoxes<CraftRecipeBoxUI>(ui.RecipeList))
                         .Concat(ActiveBoxes<CraftMaterialBoxUI>(ui.MaterialList)))
                AssertVisibleGraphicsInside((RectTransform)box.transform, box.name);
        }

        private static void AssertPrefabContract<T>(string path, string[] keys) where T : Component
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var collector = prefab.GetComponent<ReferenceCollector>();
            Assert.That(prefab.GetComponents<T>().Length, Is.EqualTo(1));
            Assert.That(collector.data.Select(x => x.key), Is.EquivalentTo(keys));
            foreach (var key in keys)
                Assert.That(typeof(T).GetField(key)?.GetValue(prefab.GetComponent<T>()), Is.SameAs(collector.GetObject(key)), key);
        }

        private static void AssertGrid(GridLayoutGroup grid, Vector2 cellSize, Vector2 spacing, GridLayoutGroup.Constraint constraint)
        {
            Assert.That(grid, Is.Not.Null);
            Assert.That(grid.startCorner, Is.EqualTo(GridLayoutGroup.Corner.UpperLeft));
            Assert.That(grid.startAxis, Is.EqualTo(GridLayoutGroup.Axis.Horizontal));
            Assert.That(grid.constraint, Is.EqualTo(constraint));
            Assert.That(grid.constraintCount, Is.EqualTo(1));
            Assert.That(grid.cellSize, Is.EqualTo(cellSize));
            Assert.That(grid.spacing, Is.EqualTo(spacing));
            Assert.That(grid.childAlignment, Is.EqualTo(TextAnchor.UpperLeft));
        }

        private void AssertCategoryLayout(CraftRecipeDefinition[] recipes, int count, float width, float cellWidth)
        {
            configProvider.SetRecipes(recipes);
            Assert.That(CraftUICtrl.TryShow(out var error), Is.True, error);
            AssertCategoryGeometry(GetBaseUI<CraftUI>(UIHelper.GetUICtrl<CraftUICtrl>()), count, width, cellWidth);
            UIHelper.GetUICtrl<CraftUICtrl>().Hide();
        }

        private static void AssertCategoryGeometry(CraftUI ui, int count, float width, float cellWidth)
        {
            Canvas.ForceUpdateCanvases();
            var list = (RectTransform)ui.CategoryList.transform;
            var boxes = ActiveBoxes<CraftCategoryBoxUI>(ui.CategoryList).ToArray();
            Assert.That(boxes.Length, Is.EqualTo(count));
            Assert.That(list.rect.width, Is.EqualTo(width).Within(0.01f));
            Assert.That(ui.CategoryGrid.cellSize.x, Is.EqualTo(cellWidth).Within(0.01f));
            for (var index = 0; index < boxes.Length; index++)
            {
                var rect = (RectTransform)boxes[index].transform;
                Assert.That(rect.anchoredPosition.x, Is.EqualTo(cellWidth * .5f + index * (cellWidth + 8f)).Within(0.5f));
                Assert.That(rect.anchoredPosition.y, Is.EqualTo(-21f).Within(0.5f));
            }
        }

        private static IEnumerable<T> ActiveBoxes<T>(CustomListView list) where T : Component
        {
            return list.transform.Cast<Transform>().Where(child => child.gameObject.activeSelf).Select(child => child.GetComponent<T>());
        }

        private static void AssertCategoryOverlayState(IEnumerable<CraftCategoryBoxUI> categories)
        {
            foreach (var category in categories)
            {
                Assert.That(category.TextName.text, Is.Not.Empty);
                Assert.That(category.TextName.color.a, Is.GreaterThan(0f));
                Assert.That(category.BtnSelect.targetGraphic, Is.Not.Null);
                Assert.That(category.BtnSelect.targetGraphic.color.a, Is.EqualTo(0f).Within(0.001f));
            }
        }

        private static void AssertCraftReadableRegions(CraftUI ui)
        {
            Canvas.ForceUpdateCanvases();
            AssertCategoryOverlayState(ActiveBoxes<CraftCategoryBoxUI>(ui.CategoryList));
            foreach (var recipe in ActiveBoxes<CraftRecipeBoxUI>(ui.RecipeList))
            {
                var root = (RectTransform)recipe.transform;
                var name = RectInRoot(recipe.TextName.rectTransform, root);
                var output = RectInRoot(recipe.TextOutputAmount.rectTransform, root);
                AssertInside(name, root, "recipe name");
                AssertInside(output, root, "recipe output");
                Assert.That(name.Overlaps(output), Is.False, "recipe texts overlap");
                if (recipe.Icon.enabled && recipe.Icon.sprite != null)
                {
                    var icon = RectInRoot(recipe.Icon.rectTransform, root);
                    AssertInside(icon, root, "recipe icon");
                    Assert.That(icon.Overlaps(name), Is.False, "recipe icon/name overlap");
                    Assert.That(icon.Overlaps(output), Is.False, "recipe icon/output overlap");
                }
            }

            foreach (var material in ActiveBoxes<CraftMaterialBoxUI>(ui.MaterialList))
            {
                var root = (RectTransform)material.transform;
                var icon = RectInRoot(material.Icon.rectTransform, root);
                var name = RectInRoot(material.TextName.rectTransform, root);
                var owned = RectInRoot(material.TextOwnedRequired.rectTransform, root);
                AssertInside(icon, root, "material icon");
                AssertInside(name, root, "material name");
                AssertInside(owned, root, "material owned/required");
                Assert.That(icon.Overlaps(name), Is.False, "material icon/name overlap");
                Assert.That(icon.Overlaps(owned), Is.False, "material icon/owned overlap");
                Assert.That(name.Overlaps(owned), Is.False, "material name/owned overlap");
            }

            var craftRoot = (RectTransform)ui.transform;
            var detailName = ui.TextDetailName;
            Assert.That(detailName.text, Is.Not.Empty);
            Assert.That(detailName.gameObject.activeInHierarchy, Is.True);
            Assert.That(detailName.enabled, Is.True);
            Assert.That(detailName.color.a, Is.GreaterThan(0f));
            var selectedRecipe = ActiveBoxes<CraftRecipeBoxUI>(ui.RecipeList).Single(recipe => recipe.SelectedRoot.activeSelf);
            Assert.That(detailName.text, Is.EqualTo(selectedRecipe.TextName.text));
            var nameRect = RectInRoot(detailName.rectTransform, craftRoot);
            var descRect = RectInRoot(ui.TextDetailDesc.rectTransform, craftRoot);
            var outputRect = RectInRoot(ui.TextSingleOutput.rectTransform, craftRoot);
            Assert.That(nameRect.yMin, Is.GreaterThanOrEqualTo(descRect.yMax + 4f - .01f));
            Assert.That(descRect.yMin, Is.GreaterThanOrEqualTo(outputRect.yMax + 5f - .01f));
        }

        private static Rect RectInRoot(RectTransform target, RectTransform root)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);
            var min = root.InverseTransformPoint(corners[0]);
            var max = root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static void AssertInside(Rect rect, RectTransform root, string label)
        {
            var bounds = root.rect;
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(bounds.xMin - .01f), label + " left");
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(bounds.xMax + .01f), label + " right");
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(bounds.yMin - .01f), label + " bottom");
            Assert.That(rect.yMax, Is.LessThanOrEqualTo(bounds.yMax + .01f), label + " top");
        }

        private static Vector4 RectState(Component component)
        {
            var rect = (RectTransform)component.transform;
            return new Vector4(rect.sizeDelta.x, rect.sizeDelta.y, rect.pivot.x, rect.pivot.y);
        }

        private static void AssertVisibleGraphicsInside(RectTransform box, string boxPath)
        {
            var corners = new Vector3[4];
            box.GetWorldCorners(corners);
            var min = box.InverseTransformPoint(corners[0]);
            var max = box.InverseTransformPoint(corners[2]);
            foreach (var graphic in box.GetComponentsInChildren<Graphic>(true).Where(item => item.gameObject.activeInHierarchy && item.enabled))
            {
                var rect = graphic.rectTransform;
                rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    var local = box.InverseTransformPoint(corner);
                    Assert.That(local.x, Is.InRange(min.x - .5f, max.x + .5f), $"{boxPath}/{graphic.name} x");
                    Assert.That(local.y, Is.InRange(min.y - .5f, max.y + .5f), $"{boxPath}/{graphic.name} y");
                }
            }
        }

        private static void AssertLocalizedPreview(GameObject root, string name, string key)
        {
            var text = root.GetComponentsInChildren<TextMeshProUGUI>(true).Single(item => item.name == name);
            var localizedText = text.GetComponent<LocalizedText>();
            Assert.That(localizedText, Is.Not.Null);
            Assert.That(localizedText.Key, Is.EqualTo(key));
            Assert.That(localizedText.Texts, Is.Empty);
        }

        private void InstallArchitecture()
        {
            var architecture = new Game();
            typeof(Architecture<Game>).GetField("mArchitecture", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, architecture);
            typeof(Architecture<Game>).GetField("mInited", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(architecture, true);
            architecture.RegisterModel(new SaveModel());
            architecture.RegisterModel(new AppStateModel());
            architecture.RegisterModel(new MapSceneModel());
            architecture.RegisterModel(new PlayerBagModel());
            architecture.RegisterModel(new WarehouseModel());
            architecture.RegisterModel(new LocalizationModel());
            architecture.RegisterUtility<IRuntimeConfigProvider>(configProvider);
            architecture.RegisterUtility(new LocalizationUtility(loadConfiguredTables: false));
            architecture.RegisterSystem(new SaveSystem());
            architecture.RegisterSystem(new InventorySystem());
            architecture.RegisterSystem(new CraftSystem());
        }

        private static void InstallLocalization()
        {
            var utility = Game.Interface.GetUtility<LocalizationUtility>();
            utility.LoadFromJson(
                "zh-CN",
                "{\"item.medkit.name\":\"急救包\",\"item.material.cloth.name\":\"布料\",\"item.material.metal_parts.name\":\"金属零件\",\"item.ammunition.arrow.name\":\"箭矢\",\"item.bottled_water.name\":\"瓶装水\",\"ui.craft.title\":\"打造\",\"ui.craft.output_flow\":\"产物优先放入背包，剩余进入仓库。\",\"ui.craft.make_one_batch\":\"制作一批\",\"ui.craft.category.all\":\"全部\",\"ui.craft.category.ammunition\":\"弹药\",\"ui.craft.category.consumable\":\"消耗品\",\"ui.craft.category.material\":\"材料\",\"ui.craft.category.weapon\":\"武器\",\"ui.craft.batch_count\":\"批数 {0}\",\"ui.craft.output_total\":\"总产出 ×{0}\",\"ui.craft.output_amount\":\"{0} ×{1}\",\"ui.craft.material_amount\":\"{0}：背包 {1} + 仓库 {2} / 需要 {3}\",\"ui.craft.material_owned_required\":\"{0} / {1}\",\"ui.craft.reason.materials\":\"材料不足\",\"ui.craft.reason.space\":\"背包与仓库空间不足\",\"ui.craft.reason.state\":\"当前只能在基地安全状态制作\",\"ui.craft.reason.config\":\"配方配置不可用\",\"ui.craft.reason.batch\":\"批数无效\",\"ui.craft.reason.drop\":\"掉落创建失败\",\"ui.craft.failed\":\"制作失败\",\"ui.craft.success\":\"已制作 {0} ×{1}\",\"ui.craft.success_with_drop\":\"已制作 {0} ×{1}，部分产物已掉落在脚边\"}");
            utility.LoadFromJson(
                "en-US",
                "{\"item.medkit.name\":\"Medkit\",\"item.material.cloth.name\":\"Cloth\",\"item.material.metal_parts.name\":\"Metal Parts\",\"item.ammunition.arrow.name\":\"Arrow\",\"item.bottled_water.name\":\"Bottled Water\",\"ui.craft.title\":\"Craft\",\"ui.craft.output_flow\":\"Output goes to the bag first, then base storage.\",\"ui.craft.make_one_batch\":\"Craft One Batch\",\"ui.craft.category.all\":\"All\",\"ui.craft.category.ammunition\":\"Ammunition\",\"ui.craft.category.consumable\":\"Consumables\",\"ui.craft.category.material\":\"Materials\",\"ui.craft.category.weapon\":\"Weapons\",\"ui.craft.batch_count\":\"Batch {0}\",\"ui.craft.output_total\":\"Total x{0}\",\"ui.craft.output_amount\":\"{0} x{1}\",\"ui.craft.material_amount\":\"{0}: Bag {1} + Storage {2} / Need {3}\",\"ui.craft.material_owned_required\":\"{0} / {1}\",\"ui.craft.reason.materials\":\"Not enough materials\",\"ui.craft.reason.space\":\"Not enough bag and storage space\",\"ui.craft.reason.state\":\"Crafting requires a safe base state\",\"ui.craft.reason.config\":\"Crafting recipe configuration is unavailable\",\"ui.craft.reason.batch\":\"Invalid batch count\",\"ui.craft.reason.drop\":\"Drop creation failed\",\"ui.craft.failed\":\"Crafting failed\",\"ui.craft.success\":\"Crafted {0} x{1}\",\"ui.craft.success_with_drop\":\"Crafted {0} x{1}; overflow dropped nearby\"}");
        }

        private static void SetBaseState()
        {
            Game.Interface.GetModel<AppStateModel>().CurrentState.Value = AppState.Playing;
            Game.Interface.GetModel<MapSceneModel>().SetLoaded(MapIds.Base, null);
            Game.Interface.GetModel<PlayerBagModel>().SetRiskState(PlayerBagRiskState.BaseSafe);
        }

        private static void CreateUIRoot()
        {
            foreach (var rootName in new[] { "MapRoot", "UnitRoot", "HudRoot", "PoolRoot" })
            {
                new GameObject(rootName);
            }

            var root = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UIRoot));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920f, 1080f);
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            foreach (var layerName in new[] { "HUD", "Dialog", "Popup", "Tips", "Top" })
            {
                var layer = new GameObject(layerName, typeof(RectTransform));
                layer.transform.SetParent(root.transform, false);
                var layerRect = layer.GetComponent<RectTransform>();
                layerRect.anchorMin = Vector2.zero;
                layerRect.anchorMax = Vector2.one;
                layerRect.anchoredPosition = Vector2.zero;
                layerRect.sizeDelta = Vector2.zero;
            }

            var inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            var eventSystem = inputModuleType == null
                ? new GameObject("EventSystem", typeof(EventSystem))
                : new GameObject("EventSystem", typeof(EventSystem), inputModuleType);
            eventSystem.transform.SetParent(root.transform, false);
        }

        private static void DestroySceneObjects()
        {
            foreach (var rootName in new[] { "MapRoot", "UnitRoot", "HudRoot", "PoolRoot", "UIRoot", "EventSystem" })
            {
                var obj = GameObject.Find(rootName);
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
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

        private static void AssertActiveRows(CraftUI ui, int count)
        {
            Assert.That(ui.RecipeList.transform.Cast<Transform>().Count(child => child.gameObject.activeSelf), Is.EqualTo(count));
        }

        private static void AssertRow(
            CraftUI ui,
            int index,
            string output,
            string outputAmount,
            bool iconEnabled,
            bool reasonVisible,
            string reason)
        {
            var row = ui.RecipeList.transform.Cast<Transform>()
                .Where(child => child.gameObject.activeSelf)
                .ElementAt(index)
                .GetComponent<CraftRecipeBoxUI>();
            Assert.That(row.TextName.text, Is.EqualTo(output));
            Assert.That(row.TextOutputAmount.text, Is.EqualTo(outputAmount));
            Assert.That(row.Icon.enabled, Is.EqualTo(iconEnabled));
            Assert.That(row.DisabledRoot.activeSelf, Is.EqualTo(reasonVisible));
            Assert.That(row.TextReason.text, Is.EqualTo(reason));
            Assert.That(row.BtnSelect.interactable, Is.True);
        }

        private static CraftRecipeBoxUI GetRowByOutput(CraftUI ui, string outputFragment)
        {
            return ui.RecipeList.transform.Cast<Transform>()
                .Where(child => child.gameObject.activeSelf)
                .Select(child => child.GetComponent<CraftRecipeBoxUI>())
                .First(box => box != null && box.TextName.text.Contains(outputFragment));
        }

        private static CraftRecipeBoxUI GetRowByOutput(CraftUI ui, string outputName, string outputAmount)
        {
            return ui.RecipeList.transform.Cast<Transform>()
                .Where(child => child.gameObject.activeSelf)
                .Select(child => child.GetComponent<CraftRecipeBoxUI>())
                .First(box => box != null && box.TextName.text.Contains(outputName) && box.TextOutputAmount.text == outputAmount);
        }

        private static void SelectAndCraft(CraftUI ui, CraftRecipeBoxUI row)
        {
            row.BtnSelect.onClick.Invoke();
            ui.BtnCraft.onClick.Invoke();
        }

        private static TextMeshProUGUI GetLocalizedText(GameObject root, string key, string objectName = null)
        {
            return root.GetComponentsInChildren<LocalizedText>(true)
                .First(text => text.Key == key && (objectName == null || text.gameObject.name == objectName))
                .GetComponent<TextMeshProUGUI>();
        }

        private sealed class TestResourceLoader : IResourceLoader
        {
            public T LoadAsset<T>(string path) where T : Object
            {
                if (path == LocalizationLanguages.LanguagesAddress)
                {
                    return new TextAsset("{\"default\":\"zh-CN\",\"languages\":[{\"code\":\"zh-CN\",\"label\":\"简体中文\"},{\"code\":\"en-US\",\"label\":\"English\"}]}") as T;
                }

                if (path == TestConfigProvider.BottledWaterIconAddress && typeof(T) == typeof(Sprite))
                {
                    var texture = new Texture2D(1, 1);
                    return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.zero) as T;
                }

                return null;
            }

            public ResourceLoadHandle LoadAssetAsync<T>(string path, Action<T> onLoaded) where T : Object
            {
                onLoaded?.Invoke(LoadAsset<T>(path));
                return ResourceLoadHandle.Empty;
            }

            public void Release(ResourceLoadHandle handle)
            {
            }
        }

        private sealed class TestConfigProvider : IRuntimeConfigProvider
        {
            public const string BottledWaterIconAddress = "test/icon/bottled-water";
            public const int ShortSwordItemId = 990001;
            public static readonly CraftRecipeDefinition DefaultMedkitRecipe = new CraftRecipeDefinition(
                "craft.medkit.basic",
                ItemIds.Medkit,
                1,
                new[] { new CraftMaterialDefinition(ItemIds.Cloth, 2) },
                100);
            public static readonly CraftRecipeDefinition DefaultArrowRecipe = new CraftRecipeDefinition(
                "craft.arrow.basic",
                ItemIds.Arrow,
                5,
                new[]
                {
                    new CraftMaterialDefinition(ItemIds.MetalParts, 1),
                    new CraftMaterialDefinition(ItemIds.Cloth, 1)
                },
                200);
            public static readonly CraftRecipeDefinition DefaultMaterialRecipe = new CraftRecipeDefinition(
                "craft.material.test", ItemIds.MetalParts, 1,
                new[] { new CraftMaterialDefinition(ItemIds.Cloth, 1) }, 250);
            public static readonly CraftRecipeDefinition DefaultWeaponRecipe = new CraftRecipeDefinition(
                "craft.short_sword.test", ShortSwordItemId, 1,
                new[] { new CraftMaterialDefinition(ItemIds.MetalParts, 1) }, 300);

            private IReadOnlyList<CraftRecipeDefinition> recipes = new[] { DefaultMedkitRecipe, DefaultArrowRecipe };
            private readonly Dictionary<int, ItemDefinition> items = new Dictionary<int, ItemDefinition>
            {
                [ItemIds.MetalParts] = new ItemDefinition(ItemIds.MetalParts, "Metal Parts", string.Empty, "item.material.metal_parts.name", string.Empty, string.Empty, global::ItemType.Material, 20, global::ItemUseEffect.None, 0),
                [ItemIds.Cloth] = new ItemDefinition(ItemIds.Cloth, "Cloth", string.Empty, "item.material.cloth.name", string.Empty, string.Empty, global::ItemType.Material, 20, global::ItemUseEffect.None, 0),
                [ItemIds.Medkit] = new ItemDefinition(ItemIds.Medkit, "Medkit", string.Empty, "item.medkit.name", string.Empty, string.Empty, global::ItemType.Consumable, 5, global::ItemUseEffect.RestoreHealth, 30),
                [ItemIds.Ration] = new ItemDefinition(ItemIds.Ration, "Ration", string.Empty, "item.ration.name", string.Empty, string.Empty, global::ItemType.Consumable, 1, global::ItemUseEffect.RestorePower, 40),
                [ItemIds.Arrow] = new ItemDefinition(ItemIds.Arrow, "Arrow", string.Empty, "item.ammunition.arrow.name", string.Empty, string.Empty, global::ItemType.Ammunition, 20, global::ItemUseEffect.None, 0),
                [ItemIds.BottledWater] = new ItemDefinition(ItemIds.BottledWater, "Bottled Water", string.Empty, "item.bottled_water.name", string.Empty, BottledWaterIconAddress, global::ItemType.Consumable, 5, global::ItemUseEffect.RestoreWater, 40)
                , [ShortSwordItemId] = new ItemDefinition(ShortSwordItemId, "Short Sword", string.Empty, string.Empty, string.Empty, string.Empty, global::ItemType.Weapon, 1, global::ItemUseEffect.None, 0)
            };

            public void SetRecipes(params CraftRecipeDefinition[] values)
            {
                recipes = values;
            }

            public bool TryGetItem(int id, out ItemDefinition value, out string error)
            {
                if (items.TryGetValue(id, out value))
                {
                    error = null;
                    return true;
                }

                value = default;
                error = $"TbItem[{id}].Id 不存在";
                return false;
            }

            public bool TryGetCraftRecipe(string id, out CraftRecipeDefinition value, out string error)
            {
                value = recipes.FirstOrDefault(recipe => string.Equals(recipe.Id, id, StringComparison.Ordinal));
                error = value == null ? $"TbCraftRecipe[{id}].Id 不存在" : null;
                return value != null;
            }

            public bool TryGetCraftRecipes(out IReadOnlyList<CraftRecipeDefinition> values, out string error)
            {
                values = recipes;
                error = null;
                return true;
            }

            public bool TryGetMap(string id, out MapAssetDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
            }

            public bool TryGetMapSpawns(string mapId, out IReadOnlyList<MapSpawnDefinition> values, out string error)
            {
                values = Array.Empty<MapSpawnDefinition>();
                error = "not used";
                return false;
            }

            public bool TryGetPlayer(string id, out PlayerConfigDefinition value, out string error)
            {
                value = default;
                error = "not used";
                return false;
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
        }

        private static T GetBaseUI<T>(BaseUICtrl ctrl) where T : BaseUI
        {
            return (T)typeof(BaseUICtrl)
                .GetField("BaseUI", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(ctrl);
        }
    }
}

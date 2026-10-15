using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>展示制作配方并提交制作意图；物品消耗和产出由 CraftSystem 决定。</summary>
    public sealed class CraftUICtrl : BaseUICtrl
    {
        private const int MinBatchCount = 1;
        private const int MaxBatchCount = 99;
        private const string AllCategoryId = "__all";
        private const float CategoryNaturalWidth = 150f;
        private const float CategorySpacing = 8f;
        private const float CategoryMaxWidth = 680f;

        private readonly List<CraftCategoryViewModel> categories = new List<CraftCategoryViewModel>();
        private readonly List<CraftQuote> allQuotes = new List<CraftQuote>();
        private readonly List<CraftQuote> visibleQuotes = new List<CraftQuote>();
        private readonly List<CraftMaterialQuote> selectedMaterials = new List<CraftMaterialQuote>();
        private string selectedCategoryId = AllCategoryId;
        private string selectedRecipeId;
        private int batchCount = MinBatchCount;

        private CraftUI UI => BaseUI as CraftUI;

        protected override string Path()
        {
            return "Craft/Craft";
        }

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }

        public override bool BlocksGame => true;

        /// <summary>校验基地状态并确认界面实际显示；资源缺失时返回失败。</summary>
        public static bool TryShow(out string error)
        {
            if (!CanOpen(out error))
            {
                return false;
            }

            var ctrl = UIHelper.ShowUI<CraftUICtrl>();
            if (ctrl == null || !ctrl.IsShow())
            {
                error = "制作界面加载失败";
                return false;
            }
            ctrl.RefreshIfVisible();
            return true;
        }

        protected override void OnInit()
        {
            UI.BtnClose.onClick.AddListener(Hide);
            UI.CategoryList?.AddItemRefreshListener(RefreshCategoryRow);
            UI.RecipeList?.AddItemRefreshListener(RefreshRecipeRow);
            UI.MaterialList?.AddItemRefreshListener(RefreshMaterialRow);
            UI.BtnBatchMinus?.onClick.AddListener(() => ChangeBatch(-1));
            UI.BtnBatchPlus?.onClick.AddListener(() => ChangeBatch(1));
            UI.BtnCraft?.onClick.AddListener(OnCraftClicked);
            this.RegisterEvent<InventoryChangedEvent>(_ => RefreshIfVisible())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ =>
                {
                    RefreshLocalizedTexts();
                    RefreshIfVisible();
                })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            RefreshAll(keepSelection: true);
        }

        protected override void OnHide()
        {
            ClearRows();
        }

        protected override void OnUnload()
        {
            ClearRows();
            categories.Clear();
            allQuotes.Clear();
            visibleQuotes.Clear();
            selectedMaterials.Clear();
        }

        private void RefreshIfVisible()
        {
            if (IsShow())
            {
                RefreshAll(keepSelection: true);
            }
        }

        private void RefreshLocalizedTexts()
        {
            if (UI == null)
            {
                return;
            }

            foreach (var localizedText in UI.GetComponentsInChildren<LocalizedText>(true))
            {
                localizedText.Refresh();
            }
        }

        private void RefreshAll(bool keepSelection)
        {
            if (UI == null)
            {
                return;
            }

            var previousRecipeId = keepSelection ? selectedRecipeId : null;
            allQuotes.Clear();
            if (!this.GetSystem<CraftSystem>().TryGetQuotes(batchCount, out var latestQuotes, out var error))
            {
                Log.Error($"[CraftUICtrl] 刷新打造报价失败：{error}");
            }
            else
            {
                allQuotes.AddRange(latestQuotes);
            }

            RebuildCategories();
            RebuildVisibleQuotes();
            SelectRecipeAfterRefresh(previousRecipeId);
            RefreshLists();
            RefreshDetail();
        }

        private void RebuildCategories()
        {
            categories.Clear();
            categories.Add(new CraftCategoryViewModel(AllCategoryId, Loc.T("ui.craft.category.all"), null));
            var types = allQuotes
                .Select(quote => TryGetItem(quote.Recipe.OutputItemId, out var item) ? item.ItemType : (global::ItemType?)null)
                .Where(type => type.HasValue)
                .Select(type => type.Value)
                .Distinct()
                .OrderBy(type => (int)type);
            foreach (var type in types)
            {
                categories.Add(new CraftCategoryViewModel(GetCategoryId(type), FormatCategoryName(type), type));
            }

            if (!categories.Any(category => category.Id == selectedCategoryId))
            {
                selectedCategoryId = AllCategoryId;
            }
        }

        private void RebuildVisibleQuotes()
        {
            visibleQuotes.Clear();
            var selectedType = categories.FirstOrDefault(category => category.Id == selectedCategoryId).ItemType;
            visibleQuotes.AddRange(selectedType.HasValue
                ? allQuotes.Where(quote => TryGetItem(quote.Recipe.OutputItemId, out var item) && item.ItemType == selectedType.Value)
                : allQuotes);
        }

        private void SelectRecipeAfterRefresh(string previousRecipeId)
        {
            if (!string.IsNullOrWhiteSpace(previousRecipeId) &&
                visibleQuotes.Any(quote => string.Equals(quote.Recipe.Id, previousRecipeId, StringComparison.Ordinal)))
            {
                selectedRecipeId = previousRecipeId;
                return;
            }

            var firstCraftable = visibleQuotes.FirstOrDefault(quote => quote.Inventory.CanCraft);
            selectedRecipeId = firstCraftable.Recipe != null
                ? firstCraftable.Recipe.Id
                : visibleQuotes.FirstOrDefault().Recipe?.Id;
            batchCount = MinBatchCount;
        }

        private void RefreshLists()
        {
            if (UI.CategoryList != null)
            {
                RefreshCategoryLayout();
                UI.CategoryList.totalCount = categories.Count;
                UI.CategoryList.RefreshCells();
            }

            if (UI.RecipeList != null)
            {
                UI.RecipeList.totalCount = visibleQuotes.Count;
                UI.RecipeList.RefreshCells();
            }
        }

        private void RefreshDetail()
        {
            selectedMaterials.Clear();
            var selected = SelectedQuote();
            var hasSelection = selected.Recipe != null;
            SetGameObjectActive(UI.DetailIcon, hasSelection);
            SetGameObjectActive(UI.ReasonRoot, hasSelection && !selected.Inventory.CanCraft);
            if (!hasSelection)
            {
                SetText(UI.TextDetailName, string.Empty);
                SetText(UI.TextDetailDesc, string.Empty);
                SetText(UI.TextSingleOutput, string.Empty);
                SetText(UI.TextBatchCount, string.Empty);
                SetText(UI.TextOutputTotal, string.Empty);
                SetText(UI.TextReason, string.Empty);
                SetButtonInteractable(UI.BtnCraft, false);
                RefreshMaterialList();
                return;
            }

            TryGetItem(selected.Recipe.OutputItemId, out var output);
            var outputName = ResolveItemName(output, selected.Recipe.OutputItemId);
            if (UI.DetailIcon != null)
            {
                UI.DetailIcon.sprite = ResolveIcon(selected.Recipe.OutputItemId);
                UI.DetailIcon.enabled = UI.DetailIcon.sprite != null;
            }

            SetText(UI.TextDetailName, outputName);
            SetText(UI.TextDetailDesc, ResolveItemDesc(output));
            SetText(UI.TextSingleOutput, Loc.T("ui.craft.output_amount", outputName, selected.Recipe.OutputAmount));
            SetText(UI.TextBatchCount, Loc.T("ui.craft.batch_count", batchCount));
            SetText(UI.TextOutputTotal, Loc.T("ui.craft.output_total", selected.Inventory.OutputAmount));
            SetText(UI.TextReason, selected.Inventory.CanCraft ? string.Empty : FormatReason(selected.Inventory.FailureReason));
            SetButtonInteractable(UI.BtnBatchMinus, batchCount > MinBatchCount);
            SetButtonInteractable(UI.BtnBatchPlus, batchCount < MaxBatchCount);
            SetButtonInteractable(UI.BtnCraft, selected.Inventory.CanCraft);
            SetButtonColor(UI.BtnBatchMinus, new Color(0.22f, 0.28f, 0.30f, 1f));
            SetButtonColor(UI.BtnBatchPlus, new Color(0.22f, 0.28f, 0.30f, 1f));
            SetButtonColor(UI.BtnCraft, selected.Inventory.CanCraft
                ? new Color(0.32f, 0.46f, 0.50f, 1f)
                : new Color(0.22f, 0.24f, 0.24f, 1f));
            selectedMaterials.AddRange(selected.Inventory.Materials);
            RefreshMaterialList();
        }

        private void RefreshMaterialList()
        {
            if (UI.MaterialList == null)
            {
                return;
            }

            UI.MaterialList.totalCount = selectedMaterials.Count;
            UI.MaterialList.RefreshCells();
        }

        private void RefreshCategoryLayout()
        {
            var count = categories.Count;
            var width = count == 0 ? 0f : count * CategoryNaturalWidth + (count - 1) * CategorySpacing;
            var cellWidth = CategoryNaturalWidth;
            if (width > CategoryMaxWidth)
            {
                width = CategoryMaxWidth;
                cellWidth = (CategoryMaxWidth - (count - 1) * CategorySpacing) / count;
            }

            if (UI.CategoryList.transform is RectTransform listRect)
            {
                listRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }

            if (UI.CategoryGrid != null)
            {
                UI.CategoryGrid.cellSize = new Vector2(cellWidth, UI.CategoryGrid.cellSize.y);
            }
        }

        private void RefreshCategoryRow(Transform item, int index)
        {
            var box = item.GetComponent<CraftCategoryBoxUI>();
            if (box == null || index < 0 || index >= categories.Count)
            {
                return;
            }

            var category = categories[index];
            SetText(box.TextName, category.DisplayName);
            SetGameObjectActive(box.SelectedRoot, string.Equals(category.Id, selectedCategoryId, StringComparison.Ordinal));
            if (box.BtnSelect != null)
            {
                box.BtnSelect.onClick.RemoveAllListeners();
                box.BtnSelect.onClick.AddListener(() => SelectCategory(category.Id));
            }
        }

        private void RefreshRecipeRow(Transform item, int index)
        {
            var box = item.GetComponent<CraftRecipeBoxUI>();
            if (box == null || index < 0 || index >= visibleQuotes.Count)
            {
                return;
            }

            var quote = visibleQuotes[index];
            TryGetItem(quote.Recipe.OutputItemId, out var output);
            if (box.Icon != null)
            {
                box.Icon.sprite = ResolveIcon(quote.Recipe.OutputItemId);
                box.Icon.enabled = box.Icon.sprite != null;
            }

            var outputName = ResolveItemName(output, quote.Recipe.OutputItemId);
            SetText(box.TextName, outputName);
            SetText(box.TextOutputAmount, Loc.T("ui.craft.output_amount", string.Empty, quote.Recipe.OutputAmount).Trim());
            SetGameObjectActive(box.SelectedRoot, string.Equals(quote.Recipe.Id, selectedRecipeId, StringComparison.Ordinal));
            SetGameObjectActive(box.DisabledRoot, !quote.Inventory.CanCraft);
            SetText(box.TextReason, quote.Inventory.CanCraft ? string.Empty : FormatReason(quote.Inventory.FailureReason));

            if (box.BtnSelect != null)
            {
                box.BtnSelect.interactable = true;
                box.BtnSelect.onClick.RemoveAllListeners();
                box.BtnSelect.onClick.AddListener(() => SelectRecipe(quote.Recipe.Id));
            }
        }

        private void RefreshMaterialRow(Transform item, int index)
        {
            var box = item.GetComponent<CraftMaterialBoxUI>();
            if (box == null || index < 0 || index >= selectedMaterials.Count)
            {
                return;
            }

            var material = selectedMaterials[index];
            TryGetItem(material.ItemId, out var itemDefinition);
            if (box.Icon != null)
            {
                box.Icon.sprite = ResolveIcon(material.ItemId);
                box.Icon.enabled = box.Icon.sprite != null;
            }

            SetText(box.TextName, ResolveItemName(itemDefinition, material.ItemId));
            SetText(box.TextOwnedRequired, Loc.T(
                "ui.craft.material_owned_required",
                material.TotalOwnedAmount,
                material.RequiredAmount));
            if (box.TextOwnedRequired != null)
            {
                box.TextOwnedRequired.color = material.HasEnough ? Color.white : Color.red;
            }
        }

        private void SelectCategory(string categoryId)
        {
            var previousRecipeId = selectedRecipeId;
            selectedCategoryId = categoryId;
            RebuildVisibleQuotes();
            SelectRecipeAfterRefresh(previousRecipeId);
            RefreshLists();
            RefreshDetail();
        }

        private void SelectRecipe(string recipeId)
        {
            if (string.Equals(selectedRecipeId, recipeId, StringComparison.Ordinal))
            {
                return;
            }

            selectedRecipeId = recipeId;
            batchCount = MinBatchCount;
            RefreshAll(keepSelection: true);
        }

        private void ChangeBatch(int delta)
        {
            var next = Mathf.Clamp(batchCount + delta, MinBatchCount, MaxBatchCount);
            if (next == batchCount)
            {
                return;
            }

            batchCount = next;
            RefreshAll(keepSelection: true);
        }

        private void OnCraftClicked()
        {
            var recipeId = selectedRecipeId;
            if (string.IsNullOrWhiteSpace(recipeId))
            {
                return;
            }

            if (this.GetSystem<CraftSystem>().TryCraft(recipeId, batchCount, out var result, out _, out var error))
            {
                if (TryGetItem(result.OutputItemId, out var output))
                {
                    var key = result.DroppedAmount > 0 ? "ui.craft.success_with_drop" : "ui.craft.success";
                    Notify.ShowFloatTips(Loc.T(key, ResolveItemName(output, result.OutputItemId), result.OutputAmount));
                }

                RefreshAll(keepSelection: true);
                return;
            }

            Log.Error($"[CraftUICtrl] 制作失败：{recipeId} {error}");
            Notify.ShowFloatTips(Loc.T("ui.craft.failed"));
            RefreshAll(keepSelection: true);
        }

        private CraftQuote SelectedQuote()
        {
            return allQuotes.FirstOrDefault(quote => string.Equals(quote.Recipe.Id, selectedRecipeId, StringComparison.Ordinal));
        }

        private static string FormatReason(CraftFailureReason reason)
        {
            switch (reason)
            {
                case CraftFailureReason.StateNotAllowed:
                    return Loc.T("ui.craft.reason.state");
                case CraftFailureReason.ConfigUnavailable:
                    return Loc.T("ui.craft.reason.config");
                case CraftFailureReason.MaterialsInsufficient:
                    return Loc.T("ui.craft.reason.materials");
                case CraftFailureReason.DropCreationFailed:
                    return Loc.T("ui.craft.reason.drop");
                case CraftFailureReason.InvalidBatchCount:
                    return Loc.T("ui.craft.reason.batch");
                default:
                    return Loc.T("ui.craft.failed");
            }
        }

        private static string GetCategoryId(global::ItemType itemType)
        {
            return "item-type-" + itemType;
        }

        private static string FormatCategoryName(global::ItemType itemType)
        {
            switch (itemType)
            {
                case global::ItemType.Consumable:
                    return Loc.T("ui.craft.category.consumable");
                case global::ItemType.Ammunition:
                    return Loc.T("ui.craft.category.ammunition");
                case global::ItemType.Material:
                    return Loc.T("ui.craft.category.material");
                case global::ItemType.Weapon:
                    return Loc.T("ui.craft.category.weapon");
                default:
                    return itemType.ToString();
            }
        }

        private Sprite ResolveIcon(int itemId)
        {
            if (!TryGetItem(itemId, out var item) ||
                string.IsNullOrWhiteSpace(item.IconAddress))
            {
                return null;
            }

            return ResourceLoader.LoadAsset<Sprite>(item.IconAddress);
        }

        private bool TryGetItem(int itemId, out ItemDefinition item)
        {
            return this.GetUtility<IRuntimeConfigProvider>().TryGetItem(itemId, out item, out _);
        }

        private static string ResolveItemName(ItemDefinition item, int fallbackItemId)
        {
            if (!string.IsNullOrWhiteSpace(item.NameKey))
            {
                var localized = Loc.T(item.NameKey);
                if (!string.IsNullOrWhiteSpace(localized) &&
                    !string.Equals(localized, item.NameKey, StringComparison.Ordinal))
                {
                    return localized;
                }
            }

            return string.IsNullOrWhiteSpace(item.Name) ? fallbackItemId.ToString() : item.Name;
        }

        private static string ResolveItemDesc(ItemDefinition item)
        {
            if (!string.IsNullOrWhiteSpace(item.DescKey))
            {
                var localized = Loc.T(item.DescKey);
                if (!string.IsNullOrWhiteSpace(localized) &&
                    !string.Equals(localized, item.DescKey, StringComparison.Ordinal))
                {
                    return localized;
                }
            }

            return item.Desc ?? string.Empty;
        }

        private void ClearRows()
        {
            ClearListButtons(UI?.CategoryList, typeof(CraftCategoryBoxUI));
            ClearListButtons(UI?.RecipeList, typeof(CraftRecipeBoxUI));
            ClearListButtons(UI?.MaterialList, typeof(CraftMaterialBoxUI));
        }

        private static void ClearListButtons(CustomListView list, Type boxType)
        {
            if (list == null)
            {
                return;
            }

            foreach (Transform child in list.transform)
            {
                if (boxType == typeof(CraftCategoryBoxUI) && child.TryGetComponent<CraftCategoryBoxUI>(out var category))
                {
                    category.BtnSelect?.onClick.RemoveAllListeners();
                }
                else if (boxType == typeof(CraftRecipeBoxUI) && child.TryGetComponent<CraftRecipeBoxUI>(out var recipe))
                {
                    recipe.BtnSelect?.onClick.RemoveAllListeners();
                }
            }
        }

        private static void SetText(TMPro.TextMeshProUGUI text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private static void SetButtonInteractable(UnityEngine.UI.Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private static void SetButtonColor(UnityEngine.UI.Button button, Color color)
        {
            if (button?.targetGraphic != null)
            {
                button.targetGraphic.color = color;
            }
        }

        private static void SetGameObjectActive(Component component, bool active)
        {
            if (component != null)
            {
                component.gameObject.SetActive(active);
            }
        }

        private static void SetGameObjectActive(GameObject gameObject, bool active)
        {
            if (gameObject != null)
            {
                gameObject.SetActive(active);
            }
        }

        private static bool CanOpen(out string error)
        {
            var architecture = Game.Interface;
            if (architecture.GetModel<AppStateModel>().CurrentState.Value != AppState.Playing)
            {
                error = "制作需要处于游戏中状态";
                return false;
            }

            if (!string.Equals(architecture.GetModel<MapSceneModel>().CurrentMapId, MapIds.Base, StringComparison.Ordinal))
            {
                error = "制作需要位于基地地图";
                return false;
            }

            if (architecture.GetModel<PlayerBagModel>().RiskState != PlayerBagRiskState.BaseSafe)
            {
                error = "制作需要玩家背包处于安全状态";
                return false;
            }

            error = null;
            return true;
        }

        private readonly struct CraftCategoryViewModel
        {
            public CraftCategoryViewModel(string id, string displayName, global::ItemType? itemType)
            {
                Id = id;
                DisplayName = displayName;
                ItemType = itemType;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public global::ItemType? ItemType { get; }
        }
    }
}

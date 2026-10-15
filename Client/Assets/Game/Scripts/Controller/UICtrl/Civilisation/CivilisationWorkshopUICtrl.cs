using System.Linq;
using QFramework;
using UnityEngine.EventSystems;

namespace Game
{
    /// <summary>呈现已发现工艺、当前配方和加工费用；仅提交生产意图，金币与物品事务归生产 System。</summary>
    public sealed class CivilisationWorkshopUICtrl : BaseUICtrl
    {
        private CivilisationWorkshopUI UI => BaseUI as CivilisationWorkshopUI;
        /// <summary>本窗口当前配方索引，跨语言刷新保留；不代表科技解锁或库存状态。</summary>
        private int recipeIndex;

        protected override string Path()
        {
            return "Civilisation/CivilisationWorkshop";
        }

        protected override void OnInit()
        {
            UI.BtnContinue.onClick.AddListener(Hide);
            UI.BtnProduce.onClick.AddListener(Produce);
            UI.BtnPrevious.onClick.AddListener(() => SelectRecipe(-1));
            UI.BtnNext.onClick.AddListener(() => SelectRecipe(1));
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshIfVisible()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<InventoryChangedEvent>(_ => RefreshIfVisible()).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            Refresh();
            UI.BodyScroll.verticalNormalizedPosition = 1;
        }

        protected override void OnHide()
        {
            EventSystem.current?.SetSelectedGameObject(null);
        }

        private void RefreshIfVisible() { if (IsShow()) Refresh(); }

        /// <summary>翻页只改变展示索引，不消耗物资；空目录保持不可制作。</summary>
        private void SelectRecipe(int direction)
        {
            if (!IsShow() || !this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out _) || catalog.Productions.Count == 0) return;
            recipeIndex = (recipeIndex + direction + catalog.Productions.Count) % catalog.Productions.Count;
            Refresh();
            UI.BodyScroll.verticalNormalizedPosition = 1;
        }

        private void Refresh()
        {
            UI.BtnProduce.interactable = false;
            UI.BtnPrevious.interactable = UI.BtnNext.interactable = false;
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out var error))
            { UI.BodyText.text = error; return; }
            recipeIndex = UnityEngine.Mathf.Clamp(recipeIndex, 0, System.Math.Max(0, catalog.Productions.Count - 1));
            var recipe = catalog.Productions.ElementAtOrDefault(recipeIndex);
            if (recipe == null) { UI.BodyText.text = Loc.T("civilisation.error.locked"); return; }
            var unlocked = this.GetModel<CivilisationTechnologyModel>().IsUnlocked(recipe.TechnologyId);
            var gold = this.GetModel<CurrencyModel>().Gold;
            var config = this.GetUtility<IRuntimeConfigProvider>();
            if (!config.TryGetItem(recipe.OutputItemId, out var item, out error)) { UI.BodyText.text = error; return; }
            var technology = catalog.Technologies.Single(t => t.Id == recipe.TechnologyId);
            UI.BodyText.text = Loc.T("civilisation.workshop.recipe", recipeIndex + 1, catalog.Productions.Count,
                Loc.T(item.NameKey), Loc.T(item.DescKey), Loc.T(technology.NameKey),
                Loc.T(unlocked ? "civilisation.workshop.unlocked" : "civilisation.error.locked"), recipe.CostGold, gold);
            UI.BodyText.text += "\n\n" + Loc.T(this.GetModel<CivilisationTechnologyModel>().IsChapterCompleted("chapter.han-transport") ?
                "civilisation.workshop.completed" : "civilisation.workshop.ongoing");
            UI.BtnPrevious.interactable = UI.BtnNext.interactable = catalog.Productions.Count > 1;
            UI.BtnProduce.interactable = unlocked && gold >= recipe.CostGold && this.GetModel<MapSceneModel>().CurrentMapId == MapIds.Base;
        }

        private void Produce()
        {
            if (!IsShow() || !this.GetUtility<IRuntimeConfigProvider>().TryGetCivilisationCatalog(out var catalog, out _)) return;
            var recipe = catalog.Productions.ElementAtOrDefault(recipeIndex);
            if (recipe == null) return;
            var success = this.GetSystem<CivilisationProductionSystem>().TryProduce(recipe.Id, 1, out var error);
            Refresh();
            Notify.ShowFloatTips(success ? Loc.T("civilisation.workshop.success") : error ?? Loc.T("civilisation.error.save"));
        }

        public override UILayer Layer()
        {
            return UILayer.Dialog;
        }
    }
}

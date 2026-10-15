using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>按当前地图资格呈现配置目录并提交建造或拆除意图；使用既有列表与弹窗生命周期。</summary>
    public sealed class TemporaryBuildingSelectUICtrl : BaseUICtrl
    {
        private readonly List<TempBuildingDefinition> buildings = new List<TempBuildingDefinition>();
        private TemporaryBuildingSelectUI UI => BaseUI as TemporaryBuildingSelectUI;

        public override UILayer Layer() => UILayer.Dialog;
        // 建筑目录允许玩家走近放置点；攻击、交互等意图由输入系统单独拦截。
        public override bool BlocksGame => false;

        protected override string Path() => "TemporaryBuilding/TemporaryBuildingSelect";

        public static bool TryShow(out string error)
        {
            var architecture = Game.Interface;
            if (architecture == null)
            {
                error = "游戏架构不可用";
                return false;
            }

            if (!architecture.GetSystem<TemporaryBuildingSystem>().CanOpenBuildSelection(out error))
            {
                return false;
            }

            var ctrl = UIHelper.ShowUI<TemporaryBuildingSelectUICtrl>();
            ctrl.Refresh();
            error = null;
            return true;
        }

        protected override void OnInit()
        {
            UI.BtnClose?.onClick.AddListener(Hide);
            UI.BtnDestroy?.onClick.AddListener(() => {
                Hide();
                this.GetSystem<TemporaryBuildingSystem>().BeginDestroyMode(out _);
            });
            UI.BuildingList?.AddItemRefreshListener(RefreshRow);
            this.RegisterEvent<InventoryChangedEvent>(_ => RefreshIfVisible())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldInventoryChangedEvent>(_ => RefreshIfVisible())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshIfVisible())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            Refresh();
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)UI.BuildingList.transform);
            UI.BuildingScroll.StopMovement();
            // 每次打开从顶部开始；库存和语言的可见刷新保留当前浏览位置。
            UI.BuildingScroll.verticalNormalizedPosition = 1f;
        }

        protected override void OnHide()
        {
            ClearRowListeners();
            buildings.Clear();
        }

        protected override void OnUnload()
        {
            ClearRowListeners();
            buildings.Clear();
        }

        private void RefreshIfVisible()
        {
            if (IsShow())
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            if (UI == null)
            {
                return;
            }

            buildings.Clear();
            if (!this.GetUtility<IRuntimeConfigProvider>().TryGetTempBuildings(out var definitions, out var error))
            {
                Log.Error($"[TemporaryBuildingSelectUICtrl] 建筑列表加载失败：{error}");
            }
            else
            {
                buildings.AddRange(definitions.Where(definition => this.GetSystem<TemporaryBuildingSystem>().CanBuildDefinition(definition)));
            }

            if (UI.BuildingList != null)
            {
                UI.BuildingList.totalCount = buildings.Count;
                UI.BuildingList.RefreshCells();
            }
        }

        private void RefreshRow(Transform item, int index)
        {
            var box = item.GetComponent<TemporaryBuildingOptionBoxUI>();
            if (box == null || index < 0 || index >= buildings.Count)
            {
                return;
            }

            var definition = buildings[index];
            SetText(box.TextName, ResolveBuildingName(definition));
            SetText(box.TextMaterials, FormatMaterials(definition));
            if (box.Icon != null)
            {
                box.Icon.sprite = string.IsNullOrWhiteSpace(definition.IconAddress)
                    ? null
                    : ResourceLoader.LoadAsset<Sprite>(definition.IconAddress);
                box.Icon.gameObject.SetActive(box.Icon.sprite != null);
            }

            if (box.BtnSelect != null)
            {
                box.BtnSelect.onClick.RemoveAllListeners();
                box.BtnSelect.onClick.AddListener(() => Select(definition.Id));
            }
        }

        private void Select(string configId)
        {
            if (this.GetSystem<TemporaryBuildingSystem>().BeginBuildPreview(configId, out var error))
            {
                Hide();
                return;
            }

            Log.Warning($"[TemporaryBuildingSelectUICtrl] 选择建筑失败：{configId} {error}");
        }

        private string FormatMaterials(TempBuildingDefinition definition)
        {
            var bag = this.GetModel<PlayerBagModel>().Container;
            return string.Join("  ", definition.Materials.Select(material =>
            {
                var name = material.ItemId.ToString();
                if (this.GetUtility<IRuntimeConfigProvider>().TryGetItem(material.ItemId, out var item, out _))
                {
                    var localized = Loc.T(item.NameKey);
                    name = string.IsNullOrWhiteSpace(localized) || string.Equals(localized, item.NameKey, StringComparison.Ordinal)
                        ? item.Name
                        : localized;
                }

                return $"{name} {bag.GetTotalAmount(material.ItemId)}/{material.Amount}";
            }));
        }

        private static string ResolveBuildingName(TempBuildingDefinition definition)
        {
            var localized = Loc.T(definition.NameKey);
            return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, definition.NameKey, StringComparison.Ordinal)
                ? definition.Name
                : localized;
        }

        private void ClearRowListeners()
        {
            if (UI?.BuildingList == null)
            {
                return;
            }

            foreach (Transform child in UI.BuildingList.transform)
            {
                child.GetComponent<TemporaryBuildingOptionBoxUI>()?.BtnSelect?.onClick.RemoveAllListeners();
            }
        }

        private static void SetText(TextMeshProUGUI text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }
    }
}

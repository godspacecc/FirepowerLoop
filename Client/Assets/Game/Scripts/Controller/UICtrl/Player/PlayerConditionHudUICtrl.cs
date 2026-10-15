using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 电脑与手机共用状态、生命和武器图标；只读取玩家快照，不执行武器选择或库存操作。
    /// </summary>
    public sealed class PlayerConditionHudUICtrl : BaseUICtrl
    {
        private static readonly Color TemperatureNormalColor = Color.white;
        private static readonly Color TemperatureLowColor = new Color(0.25f, 0.62f, 1f, 0.95f);
        private static readonly Color TemperatureHighColor = new Color(1f, 0.34f, 0.18f, 0.95f);
        // 两端圆环保留各状态原色，低于 20% 时统一显示警示色。
        private static readonly Color PowerColor = new Color(.95f, .76f, .24f, .9f);
        private static readonly Color WaterColor = new Color(.25f, .7f, 1f, .9f);
        private static readonly Color TiliColor = new Color(.25f, .95f, .45f, .9f);
        private static readonly Color ReloadColor = new Color(.25f, .7f, 1f, .95f);
        private PlayerConditionHudUI UI => BaseUI as PlayerConditionHudUI;
        /// <summary>当前 View 的弹药余量原色；换弹结束后恢复，重新加载时从 Prefab 重新读取。</summary>
        private Color ammoNormalColor;
        /// <summary>当前 View 已加载图标的物品身份；重新加载 View 时复位，弹药刷新不重复加载资源。</summary>
        private int displayedWeaponItemId = -1;
        private Vector2 layoutSize;
        private bool layoutUpdateRegistered;
        /// <summary>当前实例的手机状态区；供触控布局避让，不暴露或修改玩家属性。</summary>
        internal RectTransform MobileStatusArea => UnityObjectState.IsMissing(UI)
            ? null : (RectTransform)UI.MobileContentRoot.transform;

        /// <summary>触控控件逐块避让实际信息栏；拖开后的空白区域不占用触控空间。</summary>
        internal Rect[] MobileInfoBounds(RectTransform area) => UnityObjectState.IsMissing(UI)
            ? System.Array.Empty<Rect>() : new[]
            {
                MobileControlLayoutGeometry.Bounds(area, (RectTransform)UI.MobileStatusRow.transform),
                MobileControlLayoutGeometry.Bounds(area, (RectTransform)UI.MobileHealthRoot.transform),
                MobileControlLayoutGeometry.Bounds(area, (RectTransform)UI.WeaponInfoRoot.transform)
            };

        public override UILayer Layer()
        {
            return UILayer.HUD;
        }

        public override bool BlocksGame => false;

        protected override string Path()
        {
            return "Player/PlayerConditionHud";
        }

        protected override void OnInit()
        {
            displayedWeaponItemId = -1;
            ammoNormalColor = UI.AmmoFill.color;
            this.RegisterEvent<PlayerActionChangedEvent>(OnPlayerActionChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileControlLayoutChangedEvent>(_ => RefreshLayout()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<WeaponEquipmentChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<InventoryChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldInventoryChangedEvent>(_ => Refresh()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileInputModeChangedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<PlayerConditionChangedEvent>(OnPlayerConditionChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<CombatHealthChangedEvent>(OnCombatHealthChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<AppStateChangedEvent>(OnAppStateChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(OnMapLoadingStarted)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingCompletedEvent>(OnMapLoadingCompleted)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapCurrentReleasedEvent>(OnMapCurrentReleased)
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            Refresh();
            if (IsShow() && !layoutUpdateRegistered) { LifeCycle.RegisterUpdate(RefreshLayoutOnResize); layoutUpdateRegistered = true; }
        }

        protected override void OnHide()
        {
            ReleaseLayoutUpdate();
        }

        protected override void OnUnload() => ReleaseLayoutUpdate();
        private void ReleaseLayoutUpdate()
        { if (layoutUpdateRegistered) { LifeCycle.UnregisterUpdate(RefreshLayoutOnResize); layoutUpdateRegistered = false; } }
        private void RefreshLayoutOnResize()
        { if (!UnityObjectState.IsMissing(UI) && UI.HudLayoutArea.rect.size != layoutSize) RefreshLayout(); }

        /// <summary>显示、保存设置或分辨率变化后应用独立位置和缩放；不改写已保存的比例。</summary>
        private void RefreshLayout()
        {
            if (UnityObjectState.IsMissing(UI)) return;
            var area = UI.HudLayoutArea;
            layoutSize = area.rect.size;
            if (layoutSize.x <= 0 || layoutSize.y <= 0) return;
            var data = this.GetModel<MobileControlLayoutModel>().ToData();
            MobileControlLayoutGeometry.RenderHud(area, (RectTransform)UI.MobileStatusRow.transform, data.HudForDisplay(MobileLayoutHud.Status, layoutSize));
            MobileControlLayoutGeometry.RenderHud(area, (RectTransform)UI.MobileHealthRoot.transform, data.HudForDisplay(MobileLayoutHud.Health, layoutSize));
            MobileControlLayoutGeometry.RenderHud(area, (RectTransform)UI.WeaponInfoRoot.transform, data.HudForDisplay(MobileLayoutHud.Weapon, layoutSize));
        }

        /// <summary>显示或收到权威状态事件时刷新共用状态区；非游玩状态只隐藏内容，保留实例。</summary>
        private void Refresh()
        {
            if (UnityObjectState.IsMissing(UI))
            {
                return;
            }

            var mapId = this.GetModel<MapSceneModel>().CurrentMapId;
            var state = this.GetModel<AppStateModel>().CurrentState.Value;
            if (string.IsNullOrWhiteSpace(mapId) || ShouldSuppressForMap(mapId))
            {
                Hide();
                return;
            }

            if (!IsShow())
            {
                return;
            }

            var contentVisible = state == AppState.Playing;
            // 两端共用三行信息，输入模式切换不改变玩家保存的位置。
            UI.ContentRoot.SetActive(false);
            UI.MobileContentRoot.SetActive(contentVisible);
            if (!contentVisible)
            {
                return;
            }

            var model = this.GetModel<PlayerConditionModel>();
            var combat = this.GetModel<CombatModel>();
            SetIndicator(UI.MobilePowerFill, CalculateFillAmount(model.Power, model.MaxPower));
            SetIndicator(UI.MobileWaterFill, CalculateFillAmount(model.Water, model.MaxWater));
            SetIndicator(UI.MobileTiliFill, CalculateFillAmount(model.Tili, model.MaxTili));
            UI.MobileHealthFill.fillAmount = CalculateFillAmount(combat.PlayerHP.Value, combat.PlayerMaxHP);
            UI.MobileHealthIconText.text = Loc.T("ui.player_condition.health_icon");
            // 液柱表示摄氏温度在生存范围内的位置，不表示可补满的生存属性。
            var temperatureColor = GetMobileTemperatureColor(model.Temperature);
            UI.MobileTemperatureStem.fillAmount = CalculateTemperatureFillAmount(model.Temperature);
            UI.MobileTemperatureStem.color = temperatureColor;
            UI.MobileTemperatureBulb.color = temperatureColor;
            SetMobileStatusColor(UI.MobilePowerFill, UI.MobilePowerIcon, PowerColor);
            SetMobileStatusColor(UI.MobileWaterFill, UI.MobileWaterIcon, WaterColor);
            SetMobileStatusColor(UI.MobileTiliFill, UI.MobileTiliIcon, TiliColor);
            RefreshMobileStatusVisibility(model);
            RefreshWeaponInfo();
            RefreshLayout();
        }

        /// <summary>换弹时圆环显示蓝色动作进度，结束后恢复弹匣余量；近战隐藏圆环，空弹匣保留底环。</summary>
        private void RefreshWeaponInfo()
        {
            var online = this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly;
            var local = this.GetModel<WeaponEquipmentModel>();
            var inventory = this.GetModel<OnlineWorldModel>().Snapshot.Value.SelfInventory;
            var slot = online ? (WeaponSlot)inventory.Equipment.ActiveSlot : local.ActiveSlot;
            var itemId = online
                ? (slot == WeaponSlot.Ranged ? inventory.Equipment.RangedWeaponItemId : inventory.Equipment.MeleeWeaponItemId)
                : local.GetSlotItem(slot);
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            var hasItem = itemId > 0 && provider.TryGetItem(itemId, out _, out _);
            if (displayedWeaponItemId != itemId)
            {
                displayedWeaponItemId = itemId;
                UI.WeaponIcon.sprite = hasItem && provider.TryGetItem(itemId, out var iconItem, out _) &&
                    !string.IsNullOrWhiteSpace(iconItem.IconAddress)
                    ? ResourceLoader.LoadAsset<Sprite>(iconItem.IconAddress) : null;
            }
            UI.WeaponIcon.gameObject.SetActive(UI.WeaponIcon.sprite != null);
            UI.WeaponInfoRoot.SetActive(hasItem);
            var ranged = slot == WeaponSlot.Ranged && itemId > 0 &&
                provider.TryGetWeapon(itemId, out var weapon, out _) && weapon.WeaponKind == global::WeaponKind.Projectile;
            UI.AmmoFill.gameObject.SetActive(ranged);
            UI.AmmoBackground.gameObject.SetActive(ranged);
            UI.AmmoFill.fillAmount = 0f;
            UI.AmmoFill.color = ammoNormalColor;
            if (!ranged || !provider.TryGetWeapon(itemId, out var rangedWeapon, out _)) return;
            if (this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action) && action.Kind == PlayerActionKind.Reload)
            {
                UI.AmmoFill.color = ReloadColor;
                UI.AmmoFill.fillAmount = Mathf.Clamp01(action.Progress01);
                return;
            }
            var loaded = online ? inventory.Equipment.RangedLoadedAmmo : local.RangedLoadedAmmo;
            // 背包备用弹药不计入弹匣进度；两种模式使用相同的配置容量。
            UI.AmmoFill.fillAmount = CalculateFillAmount(loaded, rangedWeapon.MagazineCapacity);
        }

        /// <summary>动作开始、推进、完成或取消后读取最新快照；隐藏期间不刷新，重显由 OnShow 补齐。</summary>
        private void OnPlayerActionChanged(PlayerActionChangedEvent evt)
        {
            if (UnityObjectState.IsMissing(UI) || !IsShow() || !UI.MobileContentRoot.activeSelf) return;
            RefreshWeaponInfo();
        }

        private void OnPlayerConditionChanged(PlayerConditionChangedEvent evt)
        {
            Refresh();
        }

        private void OnAppStateChanged(AppStateChangedEvent evt)
        {
            Refresh();
        }

        private void OnCombatHealthChanged(CombatHealthChangedEvent evt)
        {
            if (evt.Faction == CombatFaction.Player)
            {
                Refresh();
            }
        }

        private void OnLanguageChanged(LanguageChangedEvent evt)
        {
            Refresh();
        }

        private void OnMapLoadingStarted(MapLoadingStartedEvent evt)
        {
            Hide();
        }

        /// <summary>地图完成后仅在常规地图恢复整张玩家状态界面。</summary>
        private void OnMapLoadingCompleted(MapLoadingCompletedEvent evt)
        {
            var mapId = this.GetModel<MapSceneModel>().CurrentMapId;
            if (string.IsNullOrWhiteSpace(mapId) || ShouldSuppressForMap(mapId))
            {
                Hide();
                return;
            }

            if (!IsShow()) Show();
            else Refresh();
        }

        private void OnMapCurrentReleased(MapCurrentReleasedEvent evt)
        {
            Hide();
        }

        /// <summary>
        /// 街机使用独立 HUD，因此不显示搜打撤玩家状态界面。
        /// </summary>
        private static bool ShouldSuppressForMap(string mapId)
        {
            return mapId == MapIds.Arcade;
        }

        internal static float CalculatePowerFillAmount(float currentPower, float maxPower)
        {
            return CalculateFillAmount(currentPower, maxPower);
        }

        internal static float CalculateFillAmount(float current, float maximum)
        {
            return maximum > 0f && !float.IsNaN(maximum) && !float.IsInfinity(maximum)
                ? Mathf.Clamp01(current / maximum)
                : 0f;
        }

        internal static float CalculateTemperatureFillAmount(float temperature)
        {
            return Mathf.Clamp01((temperature - PlayerConditionRules.MinTemperature) /
                                (PlayerConditionRules.MaxTemperature - PlayerConditionRules.MinTemperature));
        }

        internal static Color GetTemperatureColor(float temperature)
        {
            if (temperature < 36f)
            {
                return TemperatureLowColor;
            }

            if (temperature > 38f)
            {
                return TemperatureHighColor;
            }

            return TemperatureNormalColor;
        }

        /// <summary>正常体温区间为白色，向生存温度上下限连续渐变红蓝；两端共用相同表现。</summary>
        internal static Color GetMobileTemperatureColor(float temperature)
        {
            if (temperature < 36f)
                return Color.Lerp(TemperatureNormalColor, TemperatureLowColor,
                    Mathf.InverseLerp(36f, PlayerConditionRules.MinTemperature, temperature));
            if (temperature > 38f)
                return Color.Lerp(TemperatureNormalColor, TemperatureHighColor,
                    Mathf.InverseLerp(38f, PlayerConditionRules.MaxTemperature, temperature));
            return TemperatureNormalColor;
        }

        /// <summary>只显示未满状态与异常体温；隐藏项由 Prefab 布局自动移除空位，血条不受影响。</summary>
        private void RefreshMobileStatusVisibility(PlayerConditionModel model)
        {
            UI.MobilePowerRoot.SetActive(model.Power < model.MaxPower);
            UI.MobileWaterRoot.SetActive(model.Water < model.MaxWater);
            UI.MobileTiliRoot.SetActive(model.Tili < model.MaxTili);
            UI.MobileTemperatureRoot.SetActive(model.Temperature < 36f || model.Temperature > 38f);
            // 刷新后立即收拢，避免隐藏或恢复状态时出现一帧空位。
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)UI.MobileStatusRow.transform);
        }

        /// <summary>每次刷新同时恢复原色，避免低状态恢复后残留警示色。</summary>
        private static void SetMobileStatusColor(UnityEngine.UI.Image fill, UnityEngine.UI.Image icon, Color normal)
        {
            var low = fill.fillAmount < .2f;
            fill.color = low ? TemperatureHighColor : normal;
            icon.color = low ? TemperatureHighColor : Color.white;
        }

        /// <summary>手机与电脑均保留原圆环，只渲染权威快照中的状态比例。</summary>
        private static void SetIndicator(UnityEngine.UI.Image image, float fillAmount)
        {
            image.type = UnityEngine.UI.Image.Type.Filled;
            image.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
            image.fillClockwise = true;
            image.fillAmount = fillAmount;
        }

    }
}

using QFramework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// 管理手机操控、背包与口袋快捷入口及中心准星；只提交意图，不裁决库存、手持或战斗状态。
    /// </summary>
    public sealed class MobileJoystickUICtrl : BaseUICtrl
    {
        private MobileJoystickUI UI => BaseUI as MobileJoystickUI;
        private bool lifecycleRegistered;
        private bool blockingSubscribed;
        /// <summary>当前安全区尺寸，单位为 Canvas；布局只在设置、尺寸或显示状态变化时重算。</summary>
        private Vector2 layoutAreaSize;
        /// <summary>状态区在当前安全区中的边界；HUD 加载或位置变化时重新避让。</summary>
        private Rect conditionAreaBounds;
        private Rect pauseAreaBounds;
        private bool layoutDirty = true;
        private bool baseLayout;
        /// <summary>加速入口的默认比例来自 Prefab 锚点；布局适配不产生第二份静态坐标。</summary>
        private MobileButtonLayoutData sprintPosition;
        /// <summary>口袋开关位置来自 Prefab；列表收起时仍参与操控避让。</summary>
        private MobileButtonLayoutData pocketTogglePosition;
        /// <summary>仅属于当前 HUD 实例的展开选择；不写存档，使用物品不收起，隐藏或重载时归零。</summary>
        private bool pocketExpanded;

        public override UILayer Layer() => UILayer.HUD;
        public override bool BlocksGame => false;
        protected override string Path() => "Player/MobileJoystick";

        protected override void OnInit()
        {
            var sprintAnchor = UI.SprintButton.GetComponent<RectTransform>().anchorMin;
            sprintPosition = new MobileButtonLayoutData { X = sprintAnchor.x, Y = sprintAnchor.y };
            var pocketAnchor = UI.PocketToggleButton.GetComponent<RectTransform>().anchorMin;
            pocketTogglePosition = new MobileButtonLayoutData { X = pocketAnchor.x, Y = pocketAnchor.y };
            pocketExpanded = false;
            UI.PocketPanel.SetActive(false);
            UI.PocketExpandedMark.SetActive(false);
            UI.PocketToggleButton.onClick.AddListener(OnPocketToggleClick);
            UI.PocketCloseButton.onClick.AddListener(OnPocketCloseClick);
            UI.InteractionButton.onClick.AddListener(OnInteractionButtonClick);
            UI.SprintButton.onClick.AddListener(OnSprintButtonClick);
            UI.AttackButton.onClick.AddListener(OnAttackButtonClick);
            UI.InventoryButton.onClick.AddListener(OnInventoryButtonClick);
            UI.BuildButton?.onClick.AddListener(OnBuildButtonClick);
            this.RegisterEvent<InventoryChangedEvent>(_ => RefreshPocket()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<WeaponEquipmentChangedEvent>(_ => RefreshPocket()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<OnlineWorldInventoryChangedEvent>(_ => RefreshPocket()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(_ => RefreshPocket()).UnRegisterWhenGameObjectDestroyed(BaseUI);
            UI.BaseAimSurface.DirectionChanged += OnBaseAim;
            UI.BaseFirePress.Pressed += OnBaseFireDown;
            UI.BaseFirePress.Released += OnBaseFireUp;
            UI.BaseReloadPress.Pressed += OnBaseReload;
            this.RegisterEvent<MobileControlLayoutChangedEvent>(_ =>
            {
                layoutDirty = true;
                if (IsShow()) Refresh();
            }).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MobileInputModeChangedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<DialogueChangedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<AppStateChangedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingStartedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapLoadingCompletedEvent>(_ => { Refresh(); RefreshPocket(); })
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<MapCurrentReleasedEvent>(_ => Refresh())
                .UnRegisterWhenGameObjectDestroyed(BaseUI);
        }

        protected override void OnShow()
        {
            layoutDirty = true;
            Canvas.ForceUpdateCanvases();
            if (!blockingSubscribed)
            {
                UIHelper.BlockingUIChanged += OnBlockingUiChanged;
                blockingSubscribed = true;
            }

            if (!lifecycleRegistered)
            {
                LifeCycle.RegisterUpdate(Refresh);
                lifecycleRegistered = true;
            }

            Refresh();
            RefreshPocket();
        }

        protected override void OnHide()
        {
            ReleaseTransientState();
        }

        protected override void OnUnload()
        {
            ReleaseTransientState();
        }

        private void Refresh()
        {
            if (UnityObjectState.IsMissing(UI) || UnityObjectState.IsMissing(UI.ContentRoot))
            {
                return;
            }

            var mobileInput = this.GetSystem<MobileInputSystem>();
            // 背包等窗口通过 Dialog 层遮挡入口；输入是否可用仍由 MobileInputSystem 判定。
            // 对话拥有当前操作区；隐藏全部不可用入口并归零摇杆，保留 HUD 和虚拟设备供结束后恢复。
            var inDialogue = this.GetSystem<DialogueSystem>()?.Current != null;
            var visible = mobileInput.ShouldShowJoystick() && !inDialogue;
            SetControlVisible(UI.SprintButton.gameObject, visible);
            // 高亮只表示本次加速意图；体力耗尽或门禁变化后会自动复位。
            UI.SprintIcon.color = mobileInput.SprintRequested ? new Color(.95f, .76f, .24f, 1f) : Color.white;
            var shoulderVisible = visible && mobileInput.CanAcceptShoulderControls();
            var baseVisible = visible && mobileInput.CanAcceptBaseFacingControls();
            if (baseLayout != baseVisible) { baseLayout = baseVisible; layoutDirty = true; }
            // 容器内的装填键供三种视角复用；方向摇杆与持续开火仍只属于俯视操作。
            UI.BaseCombatRoot.SetActive(baseVisible || shoulderVisible);
            SetControlVisible(UI.BaseAimRoot, baseVisible);
            SetControlVisible(UI.BaseFirePress.gameObject, baseVisible);
            if (!baseVisible)
            {
                UI.BaseAimSurface.ResetCapture();
                UI.BaseFirePress.ResetCapture();
            }
            var reloadVisible = visible && mobileInput.CanAcceptReloadControls() && SupportsCurrentWeaponReload();
            if (UI.BaseReloadPress.gameObject.activeSelf != reloadVisible)
            {
                UI.BaseReloadPress.ResetCapture();
                UI.BaseReloadPress.gameObject.SetActive(reloadVisible);
                layoutDirty = true;
            }
            var reloading = this.GetSystem<PlayerActionSystem>().TryGetSnapshot(out var action) &&
                            action.Kind == PlayerActionKind.Reload;
            UI.BaseReloadProgress.fillAmount = reloadVisible && reloading ? action.Progress01 : 0f;
            SetControlVisible(UI.InventoryButton.gameObject, visible && InventoryOpenRules.CanOpenPlayerBag(out _));
            // 联机口袋尚未开放，不把离线容器作为联机备用数据。
            SetControlVisible(UI.PocketToggleButton.gameObject, visible && !IsOnlineMap);
            SetControlVisible(UI.PocketPanel, visible && !IsOnlineMap && pocketExpanded);
            UI.PocketToggleButton.interactable = !UIHelper.HasUIOpen && InventoryOpenRules.CanOpenPlayerBag(out _);
            UI.PocketInputGroup.interactable = !UIHelper.HasUIOpen && InventoryOpenRules.CanOpenPlayerBag(out _);
            if (UI.BuildButton != null)
                SetControlVisible(UI.BuildButton.gameObject, visible && this.GetSystem<TemporaryBuildingSystem>().CanOpenBuildSelection(out _));
            SetControlVisible(UI.AttackButton.gameObject, shoulderVisible);
            // 准星独立于手机控件显隐，复用屏幕中央的 Prefab 节点，不随安全区或摇杆开关偏移。
            UI.ReticleRoot.SetActive(mobileInput.ShouldShowReticle() && !inDialogue);
            if (!shoulderVisible && !baseVisible) mobileInput.ClearCombatInput();
            var interactionVisible = visible &&
                                     this.GetSystem<PlayerInteractionSystem>().CanInteractWithFocusedTarget;
            if (UI.InteractionButton.gameObject.activeSelf != interactionVisible)
            {
                UI.InteractionButton.gameObject.SetActive(interactionVisible);
                layoutDirty = true;
            }
            RefreshLayout();

            // 保留 HUD 内的虚拟 Gamepad，避免在 UI 回调内删建设备并重建进行中的绑定。
            UI.SetContentVisible(visible);
        }

        private void SetControlVisible(GameObject control, bool visible)
        {
            if (control.activeSelf == visible) return;
            control.SetActive(visible);
            layoutDirty = true;
        }

        /// <summary>复用双摇杆的左侧设备设置；移动外层容器，内层官方摇杆始终以零点回弹。</summary>
        private void RefreshLayout()
        {
            var area = UI.JoystickLayoutArea;
            var size = area.rect.size;
            var statusArea = UIHelper.GetUICtrl<PlayerConditionHudUICtrl>()?.MobileStatusArea;
            var statusBounds = statusArea != null ? MobileControlLayoutGeometry.Bounds(area, statusArea) : default;
            var infoBounds = UIHelper.GetUICtrl<PlayerConditionHudUICtrl>()?.MobileInfoBounds(area) ?? System.Array.Empty<Rect>();
            // 包围盒只用于检测布局变化，实际避让使用每块信息栏的边界。
            foreach (var bounds in infoBounds)
                statusBounds = Rect.MinMaxRect(Mathf.Min(statusBounds.xMin, bounds.xMin), Mathf.Min(statusBounds.yMin, bounds.yMin),
                    Mathf.Max(statusBounds.xMax, bounds.xMax), Mathf.Max(statusBounds.yMax, bounds.yMax));
            var pauseBounds = UIHelper.GetUICtrl<GameplayGuideHudUICtrl>()?.MobilePauseBounds(area) ?? default;
            if (size.x <= 0 || size.y <= 0 || (!layoutDirty && size == layoutAreaSize && statusBounds == conditionAreaBounds && pauseBounds == pauseAreaBounds)) return;
            layoutAreaSize = size;
            conditionAreaBounds = statusBounds;
            pauseAreaBounds = pauseBounds;
            layoutDirty = false;
            var data = this.GetModel<MobileControlLayoutModel>().ToData();
            var obstacles = new System.Collections.Generic.List<Rect>(infoBounds) { pauseBounds };
            if (UI.PocketPanel.activeSelf)
                obstacles.Add(MobileControlLayoutGeometry.Bounds(area, (RectTransform)UI.PocketPanel.transform));
            // 加速属于左手区域，先固定入口，移动摇杆继续复用既有触控范围避让。
            RenderButton(UI.SprintButton.transform, sprintPosition, obstacles);
            if (UI.InteractionButton.gameObject.activeSelf)
                obstacles.Add(MobileControlLayoutGeometry.Bounds(area, (RectTransform)UI.InteractionButton.transform));
            RenderButton(UI.InventoryButton.transform, data.Inventory, obstacles);
            RenderButton(UI.PocketToggleButton.transform, pocketTogglePosition, obstacles);
            if (UI.BuildButton != null) RenderButton(UI.BuildButton.transform, data.Build, obstacles);
            RenderButton(baseLayout ? UI.BaseFirePress.transform : UI.AttackButton.transform, data.Attack, obstacles);
            if (UI.BaseReloadPress.gameObject.activeSelf) RenderButton(UI.BaseReloadPress.transform, data.Reload, obstacles);
            if (baseLayout)
            {
                data.Move = data.OrdinaryStick(true);
                data.Aim = data.OrdinaryStick(false);
                MobileControlLayoutGeometry.Render(area, (RectTransform)UI.BaseAimRoot.transform,
                    UI.BaseAimOpacity, data.Aim, false, obstacles.ToArray());
            }
            MobileControlLayoutGeometry.Render(area, (RectTransform)UI.MoveJoystickRoot.transform,
                UI.MoveOpacity, data.Move, true, obstacles.ToArray());
        }

        /// <summary>顺序绘制固定按钮并避让先前入口；隐藏按钮不占触控区域，保存位置不被显示适配覆写。</summary>
        private void RenderButton(Transform button, MobileButtonLayoutData position, System.Collections.Generic.List<Rect> obstacles)
        {
            var rect = (RectTransform)button;
            MobileControlLayoutGeometry.RenderButton(UI.JoystickLayoutArea, rect, position, obstacles.ToArray());
            if (button.gameObject.activeSelf) obstacles.Add(MobileControlLayoutGeometry.Bounds(UI.JoystickLayoutArea, rect));
        }
        /// <summary>UI 点击只提交一次攻击意图，统一输入下一帧按中央射线发布正式攻击。</summary>
        private void OnAttackButtonClick()
        {
            this.GetSystem<MobileInputSystem>().RequestShoulderAttack();
        }

        /// <summary>点击仅切换手机加速意图；不在 UI 中修改速度或体力。</summary>
        private void OnSprintButtonClick()
        {
            this.GetSystem<MobileInputSystem>().TryToggleSprint();
            Refresh();
        }

        private void OnBaseAim(Vector2 direction) => this.GetSystem<MobileInputSystem>().SetBaseAim(direction);
        private void OnBaseFireDown()
        {
            var mobile = this.GetSystem<MobileInputSystem>();
            if (!mobile.CanAcceptBaseFacingControls()) { UI.BaseFirePress.ResetCapture(); return; }
            mobile.SetBaseFire(true);
        }
        private void OnBaseFireUp() => this.GetSystem<MobileInputSystem>().SetBaseFire(false);
        /// <summary>装填按下时重验武器能力；释放旧攻击触点，防止持续按压取消装填。</summary>
        private void OnBaseReload()
        {
            if (!SupportsCurrentWeaponReload()) { UI.BaseReloadPress.ResetCapture(); return; }
            UI.BaseFirePress.ResetCapture();
            this.GetSystem<MobileInputSystem>().RequestBaseReload();
        }

        /// <summary>
        /// 只判断当前武器是否具有装填机制；弹药满或背包无弹药不隐藏入口，执行条件仍由武器系统裁决。
        /// 联机读取服务器装备快照，单机读取本地装备模型，不维护第二份装备状态。
        /// </summary>
        private bool SupportsCurrentWeaponReload()
        {
            WeaponSlot activeSlot;
            int weaponItemId;
            if (this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly)
            {
                var equipment = this.GetModel<OnlineWorldModel>().Snapshot.Value.SelfInventory.Equipment;
                activeSlot = (WeaponSlot)equipment.ActiveSlot;
                weaponItemId = equipment.RangedWeaponItemId;
            }
            else
            {
                var equipment = this.GetModel<WeaponEquipmentModel>();
                if (equipment.IsHoldingTorch) return true;
                activeSlot = equipment.ActiveSlot;
                weaponItemId = equipment.RangedWeaponItemId;
            }

            return activeSlot == WeaponSlot.Ranged && weaponItemId > 0 &&
                   this.GetUtility<IRuntimeConfigProvider>().TryGetWeapon(weaponItemId, out var weapon, out _) &&
                   weapon.WeaponKind == global::WeaponKind.Projectile && weapon.MagazineCapacity > 0 &&
                   weapon.ReloadSeconds > 0f && weapon.AmmoItemId > 0;
        }
        private void ResetBaseTouches()
        {
            UI.BaseAimSurface.ResetCapture();
            UI.BaseFirePress.ResetCapture();
            UI.BaseReloadPress.ResetCapture();
        }

        /// <summary>手机点击复用背包热键事务；回调时重验门禁，加载或阻断期间的旧点击不打开背包。</summary>
        private void OnInventoryButtonClick()
        {
            if (!this.GetSystem<MobileInputSystem>().CanAcceptMovement() ||
                !InventoryOpenRules.CanOpenPlayerBag(out _))
            {
                Refresh();
                return;
            }

            this.GetSystem<HotKeySystem>().TryExecuteAction(HotKeyActionIds.ToggleInventory);
            Refresh();
        }

        /// <summary>触控入口复用键盘建造意图，回调时重验玩法与建筑门禁。</summary>
        private void OnBuildButtonClick()
        {
            if (!this.GetSystem<MobileInputSystem>().CanAcceptMovement() ||
                !this.GetSystem<TemporaryBuildingSystem>().CanOpenBuildSelection(out _)) return;
            Game.Interface.SendEvent(new TemporaryBuildRequestedEvent());
            Refresh();
        }

        /// <summary>显示或收到库存事件时读取口袋快照；列表组件负责创建和复用，UI 不保存第二份库存。</summary>
        private void RefreshPocket()
        {
            if (UnityObjectState.IsMissing(UI) || !IsShow()) return;
            if (IsOnlineMap)
            {
                UI.PocketList.ClearCells();
                UI.PocketPanel.SetActive(false);
                UI.PocketExpandedMark.SetActive(false);
                layoutDirty = true;
                return;
            }
            SetControlVisible(UI.PocketPanel, pocketExpanded);
            UI.PocketExpandedMark.SetActive(pocketExpanded);
            var transfer = this.GetUtility<IItemTransferPort>();
            UI.PocketList.AddItemRefreshListener(RenderPocketCell);
            UI.PocketList.totalCount = transfer.TryGetContainerView(InventoryContainerKind.Pocket, 0, out var pocket)
                ? pocket.Capacity : 0;
            UI.PocketList.RefreshCells();
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)UI.PocketList.transform);
            layoutDirty = true;
        }

        /// <summary>点击仅切换本实例显隐；阻断、隐藏或联机期间拒绝旧回调，展开时补读当前口袋。</summary>
        private void OnPocketToggleClick()
        {
            if (!IsShow() || UIHelper.HasUIOpen || IsOnlineMap ||
                !this.GetSystem<InventorySystem>().CanModifyPlayerBag(out _)) return;
            pocketExpanded = !pocketExpanded;
            RefreshPocket();
            if (pocketExpanded)
            {
                // 与建筑选择一致：打开时从顶部浏览，库存刷新不改变滚动位置。
                UI.PocketScroll.StopMovement();
                UI.PocketScroll.verticalNormalizedPosition = 1f;
            }
            RefreshLayout();
        }

        /// <summary>关闭按钮只收起当前展开面板；迟到或重复关闭不会把口袋重新打开。</summary>
        private void OnPocketCloseClick()
        {
            if (pocketExpanded) OnPocketToggleClick();
        }

        /// <summary>完整重置复用格，并捕获实际物品身份；旧点击不能操作同格后来放入的物品。</summary>
        private void RenderPocketCell(Transform item, int index)
        {
            var view = item.GetComponent<PocketSlotBoxUI>();
            InventorySlotBoxReuse.Reset(view);
            if (IsOnlineMap) return;
            var source = new InventorySlotRef(InventoryContainerKind.Pocket, index);
            var transfer = this.GetUtility<IItemTransferPort>();
            if (!transfer.TryGetStack(source, out var stack)) return;
            var provider = this.GetUtility<IRuntimeConfigProvider>();
            if (!provider.TryGetItem(stack.ItemId, out var config, out _)) return;
            var localizedName = Loc.T(config.NameKey);
            view.NameText.text = string.IsNullOrWhiteSpace(localizedName) || localizedName == config.NameKey
                ? config.Name : localizedName;
            view.NameText.gameObject.SetActive(true);
            view.IconImage.sprite = string.IsNullOrWhiteSpace(config.IconAddress) ? null : ResourceLoader.LoadAsset<Sprite>(config.IconAddress);
            view.IconImage.gameObject.SetActive(view.IconImage.sprite != null);
            view.AmountText.text = stack.Amount.ToString();
            view.AmountText.gameObject.SetActive(true);
            view.SelectedFrame.SetActive(this.GetModel<WeaponEquipmentModel>().SourceIsCurrent(source));
            var usable = WeaponEquipmentModel.ValidateHeldWeapon(stack.ItemId, provider, out _) ||
                config.ActionKind == global::ItemActionKind.OpenMap ||
                config.ItemType == global::ItemType.Consumable && config.UseValue > 0 && config.UseDurationSeconds > 0;
            view.ClickButton.interactable = usable;
            var bound = BaseUI;
            var itemId = stack.ItemId;
            var identity = stack.RuntimeIdentity;
            view.ClickButton.onClick.AddListener(() =>
            {
                if (!usable || !pocketExpanded || !IsShow() || !ReferenceEquals(bound, BaseUI) || UIHelper.HasUIOpen ||
                    IsOnlineMap || !this.GetSystem<InventorySystem>().CanModifyPlayerBag(out _)) return;
                if (!transfer.TryGetStack(source, out var current) || current.ItemId != itemId || current.RuntimeIdentity != identity)
                { RefreshPocket(); return; }
                UsePocketItem(source, itemId, config.ActionKind);
                RefreshPocket();
            });
        }

        /// <summary>以地图模式为隔离边界；服务器快照尚未到达也不能回退读取离线口袋。</summary>
        private bool IsOnlineMap => this.GetModel<MapSceneModel>().CurrentMode == MapPreparationMode.OnlinePlayersOnly;

        /// <summary>点击当前手持来源时收起，点击其他武器时拿出；状态由装备系统修改，消耗品保持原使用时长。</summary>
        private void UsePocketItem(InventorySlotRef source, int itemId, global::ItemActionKind actionKind)
        {
            if (WeaponEquipmentModel.ValidateHeldWeapon(itemId, this.GetUtility<IRuntimeConfigProvider>(), out _))
            {
                var equipment = this.GetSystem<WeaponEquipmentSystem>();
                var success = this.GetModel<WeaponEquipmentModel>().SourceIsCurrent(source)
                    ? equipment.TryHolster(out _)
                    : equipment.TryHold(source, itemId, out _);
                if (!success)
                    Notify.ShowFloatTips(Texts.InventoryOperationFailed);
                return;
            }
            if (actionKind == global::ItemActionKind.OpenMap)
            {
                if (!BagUICtrl.TryShowMapForItem(itemId, out _)) Notify.ShowFloatTips(Loc.T("ui.map.error.unavailable"));
                return;
            }
            // 正式动作 HUD 显示使用进度，快捷入口只在拒绝操作时提示，避免每次使用叠加浮层。
            if (!this.GetSystem<InventorySystem>().TryBeginUseCarriedItem(source, out _))
                Notify.ShowFloatTips(Texts.InventoryOperationFailed);
        }

        /// <summary>
        /// 手机按钮提交当前有效物件的交互意图；点击与状态变化竞态时重新校验全部玩法门禁。
        /// </summary>
        private void OnInteractionButtonClick()
        {
            var mobileInput = this.GetSystem<MobileInputSystem>();
            var interaction = this.GetSystem<PlayerInteractionSystem>();
            if (!mobileInput.CanAcceptMovement() || !interaction.CanInteractWithFocusedTarget)
            {
                Refresh();
                return;
            }

            this.GetSystem<PlayerActionSystem>().CancelActive(PlayerActionCancelReason.Interaction);
            interaction.RequestInteract();
            Refresh();
        }

        /// <summary>窗口阻断时释放已按住的移动输入，保留 HUD 显示，由层级完成遮挡。</summary>
        private void OnBlockingUiChanged(bool blocked)
        {
            if (blocked && !UnityObjectState.IsMissing(UI))
                UI.JoystickSurface.OnPointerUp(null);
            Refresh();
        }

        private void ReleaseTransientState()
        {
            pocketExpanded = false;
            this.GetSystem<MobileInputSystem>().ClearShoulderInput();
            this.GetSystem<PlayerInputSystem>().ResetMobileLook();
            if (blockingSubscribed)
            {
                UIHelper.BlockingUIChanged -= OnBlockingUiChanged;
                blockingSubscribed = false;
            }

            if (lifecycleRegistered)
            {
                LifeCycle.UnregisterUpdate(Refresh);
                lifecycleRegistered = false;
            }

            if (!UnityObjectState.IsMissing(UI) && !UnityObjectState.IsMissing(UI.ContentRoot))
            {
                ResetBaseTouches();
                UI.BaseCombatRoot.SetActive(false);
                UI.InteractionButton.gameObject.SetActive(false);
                UI.SprintButton.gameObject.SetActive(false);
                UI.AttackButton.gameObject.SetActive(false);
                UI.InventoryButton.gameObject.SetActive(false);
                UI.PocketList.ClearCells();
                UI.PocketPanel.SetActive(false);
                UI.PocketToggleButton.gameObject.SetActive(false);
                UI.PocketExpandedMark.SetActive(false);
                if (UI.BuildButton != null) UI.BuildButton.gameObject.SetActive(false);
                UI.ReticleRoot.SetActive(false);
                UI.SetContentVisible(false);
            }
        }
    }
}

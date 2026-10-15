using System.Collections.Generic;
using System.Linq;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game
{
    /// <summary>
    /// 表示热键重绑操作结果，成功返回绑定路径，失败返回可记录的中文原因。
    /// </summary>
    public readonly struct HotKeyRebindResult
    {
        public HotKeyRebindResult(bool success, string error, bool needsConfirmation, string conflictingActionId)
        {
            Success = success;
            Error = error ?? string.Empty;
            NeedsConfirmation = needsConfirmation;
            ConflictingActionId = conflictingActionId ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }
        public bool NeedsConfirmation { get; }
        public string ConflictingActionId { get; }

        public static HotKeyRebindResult Ok()
        {
            return new HotKeyRebindResult(true, string.Empty, false, string.Empty);
        }

        public static HotKeyRebindResult Fail(string error)
        {
            return new HotKeyRebindResult(false, error, false, string.Empty);
        }

        public static HotKeyRebindResult Conflict(string actionId)
        {
            return new HotKeyRebindResult(false, string.Empty, true, actionId);
        }
    }

    /// <summary>
    /// 交互式重绑捕获到新路径后的通知，供设置界面刷新并触发保存。
    /// </summary>
    public readonly struct HotKeyRebindCapturedEvent
    {
        public HotKeyRebindCapturedEvent(HotKeyRebindResult result)
        {
            Result = result;
        }

        public HotKeyRebindResult Result { get; }
    }

    public readonly struct HotKeyBindingsChangedEvent { }

    /// <summary>
    /// 维护默认动作、上下文仲裁和交互式重绑，包含诊断阶段始终可用的 F1 RuntimeLog 动作。
    /// </summary>
    public sealed class HotKeySystem : AbstractSystem
    {
        private readonly Dictionary<string, InputAction> actionsByPath = new Dictionary<string, InputAction>();
        private HotKeyBinding pendingBinding;
        private bool isCapturing;

        protected override void OnInit()
        {
            RegisterDefaultActions();
            RebuildActions();
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(Update);
            foreach (var action in actionsByPath.Values)
            {
                action.Disable();
                action.Dispose();
            }

            actionsByPath.Clear();
            CancelRebind();
        }

        public void RebuildActions()
        {
            foreach (var action in actionsByPath.Values)
            {
                action.Disable();
                action.Dispose();
            }

            actionsByPath.Clear();
            foreach (var path in this.GetModel<HotKeyModel>().Bindings.Select(item => item.BindingPath).Distinct())
            {
                var inputAction = new InputAction(
                    $"HotKey:{path}",
                    InputActionType.Button,
                    binding: path);
                inputAction.Enable();
                actionsByPath[path] = inputAction;
            }
        }

        public bool IsTextInputFocused()
        {
            return EventSystem.current != null &&
                   EventSystem.current.currentSelectedGameObject != null &&
                   EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }

        public bool IsCapturingRebind => isCapturing;
        public string CapturingActionId => pendingBinding?.ActionId;

        public HotKeyRebindResult BeginRebind(string actionId)
        {
            if (!this.GetModel<HotKeyModel>().TryGet(actionId, out var binding))
            {
                return HotKeyRebindResult.Fail("未知热键动作");
            }

            pendingBinding = binding.Clone();
            isCapturing = true;
            return HotKeyRebindResult.Ok();
        }

        public HotKeyRebindResult ReceiveBindingPath(string bindingPath)
        {
            if (!isCapturing || pendingBinding == null)
            {
                return HotKeyRebindResult.Fail("热键捕获未开始");
            }

            if (string.IsNullOrWhiteSpace(bindingPath))
            {
                return HotKeyRebindResult.Fail("热键绑定路径为空");
            }

            if (bindingPath == HotKeyDefaults.Escape)
            {
                CancelRebind();
                return HotKeyRebindResult.Fail("热键捕获已取消");
            }

            pendingBinding.BindingPath = bindingPath.Trim();
            if (this.GetModel<HotKeyModel>().TryGetConflict(pendingBinding, out var conflict))
            {
                return HotKeyRebindResult.Conflict(conflict.ActionId);
            }

            return CommitPendingBinding(false);
        }

        public HotKeyRebindResult ConfirmPendingBinding(bool replaceConflict)
        {
            if (!isCapturing || pendingBinding == null)
            {
                return HotKeyRebindResult.Fail("热键捕获未开始");
            }

            return CommitPendingBinding(replaceConflict);
        }

        public void CancelRebind()
        {
            pendingBinding = null;
            isCapturing = false;
        }

        private void Update()
        {
            // 造型库是附加预览场景，不能让基地快捷键在隐藏 HUD 后继续触发正式玩法。
            if (this.GetSystem<AppearanceGallerySceneFlowSystem>().IsOpen) return;
            if (IsTextInputFocused())
            {
                return;
            }

            if (isCapturing)
            {
                CapturePressedKey();
                return;
            }

            foreach (var pair in actionsByPath)
            {
                if (!pair.Value.WasPressedThisFrame())
                {
                    continue;
                }

                ExecuteBinding(pair.Key);
            }
        }

        private void CapturePressedKey()
        {
            var keyboard = global::UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            foreach (KeyControl key in keyboard.allKeys)
            {
                if (!key.wasPressedThisFrame)
                {
                    continue;
                }

                var result = ReceiveBindingPath($"<Keyboard>/{key.name}");
                this.SendEvent(new HotKeyRebindCapturedEvent(result));
                return;
            }
        }

        private HotKeyRebindResult CommitPendingBinding(bool replaceConflict)
        {
            var model = this.GetModel<HotKeyModel>();
            if (!model.TryReplace(pendingBinding, replaceConflict))
            {
                if (model.TryGetConflict(pendingBinding, out var conflict))
                {
                    return HotKeyRebindResult.Conflict(conflict.ActionId);
                }

                return HotKeyRebindResult.Fail("替换热键绑定失败");
            }

            this.GetSystem<SettingsSystem>().MarkDirty();
            RebuildActions();
            this.SendEvent(new HotKeyBindingsChangedEvent());
            CancelRebind();
            return HotKeyRebindResult.Ok();
        }

        public bool ExecuteBinding(string bindingPath)
        {
            var candidates = this.GetModel<HotKeyModel>().Bindings
                .Where(item => item.BindingPath == bindingPath)
                .Select(item => item.ActionId);
            return this.GetUtility<HotKeyActionRegistry>().TryExecute(candidates);
        }

        /// <summary>由触屏按钮等非物理按键入口执行同一动作仲裁，避免复制暂停、窗口关闭等路由规则。</summary>
        public bool TryExecuteAction(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) &&
                   this.GetUtility<HotKeyActionRegistry>().TryExecute(new[] { actionId });
        }

        private void RegisterDefaultActions()
        {
            var registry = this.GetUtility<HotKeyActionRegistry>();
            registry.Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.ToggleGameplayGuide,
                Context = HotKeyContext.Gameplay,
                Priority = 40,
                CanExecute = () => UIHelper.GetUICtrl<GameplayGuideHudUICtrl>()?.CanToggleDesktopGuide() == true,
                Execute = () => UIHelper.GetUICtrl<GameplayGuideHudUICtrl>().ToggleDesktopGuide()
            });
            registry.Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.TogglePause,
                Context = HotKeyContext.Gameplay,
                Priority = 10,
                CanExecute = () => ResolveTogglePauseRoute().CanExecute,
                Execute = () =>
                {
                    ExecuteTogglePauseRoute(ResolveTogglePauseRoute());
                }
            });
            registry.Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.ToggleInventory,
                Context = HotKeyContext.Gameplay,
                Priority = 20,
                CanExecute = () =>
                {
                    if (IsShown<BagUICtrl>())
                    {
                        return true;
                    }

                    return InventoryOpenRules.CanOpenPlayerBag(out _);
                },
                Execute = () =>
                {
                    if (IsShown<BagUICtrl>())
                    {
                        UIHelper.HideUI<BagUICtrl>();
                        return;
                    }

                    CleanupInvalidInventoryContexts();
                    var warehouseOpen = IsShown<WarehouseUICtrl>();
                    var searchOpen = IsShown<SearchContainerUICtrl>();
                    if (warehouseOpen && searchOpen)
                    {
                        return;
                    }

                    BagUICtrl.TryShow(out _);
                }
            });
            registry.Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.ToggleMap,
                Context = HotKeyContext.Gameplay,
                Priority = 30,
                CanExecute = BagUICtrl.CanToggleMap,
                Execute = BagUICtrl.ToggleMap
            });
            registry.Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.ToggleRuntimeLog,
                Context = HotKeyContext.Global,
                Priority = 1,
                CanExecute = () => true,
                Execute = () =>
                {
                    var ctrl = UIHelper.GetUICtrl<RuntimeLogUICtrl>();
                    if (ctrl != null && ctrl.IsShow())
                    {
                        UIHelper.HideUI<RuntimeLogUICtrl>();
                    }
                    else
                    {
                        UIHelper.ShowUI<RuntimeLogUICtrl>();
                    }
                }
            });
            registry.Register(new HotKeyAction
            {
                ActionId = HotKeyActionIds.ToggleFlashlight,
                Context = HotKeyContext.Gameplay,
                Priority = 35,
                CanExecute = () => UIHelper.GetTopmostBlockingUI() == null &&
                                   this.GetSystem<PlayerFlashlightSystem>().CanToggle(out _),
                Execute = () => this.GetSystem<PlayerFlashlightSystem>().TryToggle(out _)
            });
        }

        private TogglePauseRoute ResolveTogglePauseRoute()
        {
            var topmostBlockingUI = UIHelper.GetTopmostBlockingUI();
            if (topmostBlockingUI != null)
            {
                // 对话中的 Esc 与手机暂停入口使用同一路由，保留会话而不执行关闭。
                if (topmostBlockingUI is DialogueUICtrl)
                    return this.GetModel<AppStateModel>().CurrentState.Value == AppState.Playing
                        ? TogglePauseRoute.Pause() : TogglePauseRoute.Blocked();
                if (IsEscapeClosable(topmostBlockingUI))
                {
                    return TogglePauseRoute.Close(topmostBlockingUI);
                }

                if ((topmostBlockingUI is PauseUICtrl || topmostBlockingUI is StartUICtrl) &&
                    this.GetModel<AppStateModel>().CurrentState.Value == AppState.Paused)
                {
                    return TogglePauseRoute.Resume();
                }

                return TogglePauseRoute.Blocked();
            }

            // 建筑目录允许移动，不再计入阻断窗口，但 Escape 仍应先关闭目录再处理暂停。
            var buildingSelection = UIHelper.GetUICtrl<TemporaryBuildingSelectUICtrl>();
            if (buildingSelection != null && buildingSelection.IsShow())
            {
                return TogglePauseRoute.Close(buildingSelection);
            }

            var state = this.GetModel<AppStateModel>().CurrentState.Value;
            if (state == AppState.Playing)
            {
                return TogglePauseRoute.Pause();
            }

            return state == AppState.Paused
                ? TogglePauseRoute.Resume()
                : TogglePauseRoute.Blocked();
        }

        private void ExecuteTogglePauseRoute(TogglePauseRoute route)
        {
            switch (route.Action)
            {
                case TogglePauseRouteAction.CloseUI:
                    route.Target?.Hide();
                    return;
                case TogglePauseRouteAction.Pause:
                    if (this.GetSystem<AppStateSystem>().TryPause())
                    {
                        UIHelper.ShowUI<PauseUICtrl>();
                    }

                    return;
                case TogglePauseRouteAction.Resume:
                    if (this.GetSystem<MapSceneSystem>().HasRecoverableCurrentWorld() &&
                        this.GetSystem<AppStateSystem>().TryResume())
                    {
                        UIHelper.HideUI<PauseUICtrl>();
                    }

                    return;
            }
        }

        private static bool IsEscapeClosable(BaseUICtrl ctrl)
        {
            return ctrl is BagUICtrl ||
                   ctrl is SettingUICtrl ||
                   ctrl is WarehouseUICtrl ||
                   ctrl is SearchContainerUICtrl ||
                   ctrl is RuntimeLogUICtrl ||
                   ctrl is CraftUICtrl ||
                   ctrl is ShopUICtrl ||
                   ctrl is TemporaryBuildingSelectUICtrl ||
                   ctrl is TestCmdUICtrl;
        }

        private static bool IsShown<T>() where T : BaseUICtrl
        {
            var ctrl = UIHelper.GetUICtrl<T>();
            return ctrl != null && ctrl.IsShow();
        }

        private static void CleanupInvalidInventoryContexts()
        {
            var warehouse = UIHelper.GetUICtrl<WarehouseUICtrl>();
            if (warehouse != null && warehouse.IsShow() && !InventoryOpenRules.CanOpenWarehouse(out _))
            {
                warehouse.Hide();
            }

            var search = UIHelper.GetUICtrl<SearchContainerUICtrl>();
            if (search != null && search.IsShow() &&
                !InventoryOpenRules.CanOpenSearchContainer(search.CurrentContainerId, out _))
            {
                search.Hide();
            }
        }

        private enum TogglePauseRouteAction
        {
            None,
            CloseUI,
            Pause,
            Resume
        }

        private readonly struct TogglePauseRoute
        {
            private TogglePauseRoute(TogglePauseRouteAction action, BaseUICtrl target)
            {
                Action = action;
                Target = target;
            }

            public TogglePauseRouteAction Action { get; }
            public BaseUICtrl Target { get; }
            public bool CanExecute => Action != TogglePauseRouteAction.None;

            public static TogglePauseRoute Blocked()
            {
                return new TogglePauseRoute(TogglePauseRouteAction.None, null);
            }

            public static TogglePauseRoute Close(BaseUICtrl target)
            {
                return new TogglePauseRoute(TogglePauseRouteAction.CloseUI, target);
            }

            public static TogglePauseRoute Pause()
            {
                return new TogglePauseRoute(TogglePauseRouteAction.Pause, null);
            }

            public static TogglePauseRoute Resume()
            {
                return new TogglePauseRoute(TogglePauseRouteAction.Resume, null);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>管理热键列表、重绑定冲突确认与当前页默认值恢复。</summary>
    public sealed class SettingControlsPageCtrl : BasePageCtrl
    {
        private IReadOnlyList<HotKeyBinding> bindings = Array.Empty<HotKeyBinding>();
        private SettingControlsPageUI UI => BaseUI as SettingControlsPageUI;

        protected override void OnInit()
        {
            UI.HotKeyList.AddItemRefreshListener(OnRefreshItem);
            UI.ResetButton.onClick.AddListener(ResetPage);
            this.RegisterEvent<HotKeyRebindCapturedEvent>(OnCaptured).UnRegisterWhenGameObjectDestroyed(BaseUI);
            this.RegisterEvent<LanguageChangedEvent>(OnLanguageChanged).UnRegisterWhenGameObjectDestroyed(BaseUI);
        }
        protected override void OnShow() => Refresh();
        protected override void OnHide() => this.GetSystem<HotKeySystem>().CancelRebind();

        private void Refresh()
        {
            bindings = this.GetModel<HotKeyModel>().Bindings.ToArray();
            UI.HotKeyList.totalCount = bindings.Count;
            UI.HotKeyList.RefreshCells();
        }

        private void OnRefreshItem(Transform transform, int index)
        {
            var row = transform.GetComponent<HotKeyItemBoxUI>();
            if (row == null || index < 0 || index >= bindings.Count) return;
            var binding = bindings[index];
            row.ActionNameText.text = FormatAction(binding.ActionId);
            row.BindingText.text = HotKeyDisplayFormatter.FormatPath(binding.BindingPath);
            var label = row.RebindButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = Texts.SettingsRebind;
            row.RebindButton.RemoveAllEvents();
            row.RebindButton.OnClickEvent(_ =>
            {
                this.GetSystem<HotKeySystem>().BeginRebind(binding.ActionId);
                Notify.ShowFloatTips(Texts.SettingsPressHotkey);
            });
        }

        private void OnCaptured(HotKeyRebindCapturedEvent evt)
        {
            if (evt.Result.Success) { Refresh(); return; }
            if (evt.Result.NeedsConfirmation)
            {
                Notify.ShowMessage(Texts.CommonConfirm, Texts.SettingsHotKeyConflict(FormatAction(evt.Result.ConflictingActionId)),
                    new List<MessageButtonConfig>
                    {
                        new MessageButtonConfig { ButtonText = Texts.CommonSwap, Role = MessageButtonRole.Confirm, OnClick = () => { if (this.GetSystem<HotKeySystem>().ConfirmPendingBinding(true).Success) Refresh(); } },
                        new MessageButtonConfig { ButtonText = Texts.CommonCancel, Role = MessageButtonRole.Cancel, OnClick = () => this.GetSystem<HotKeySystem>().CancelRebind() }
                    });
                return;
            }
            if (!string.IsNullOrEmpty(evt.Result.Error)) Notify.ShowFloatTips(Texts.SettingsErrorRebindFailed);
        }

        private void ResetPage()
        {
            this.GetSystem<HotKeySystem>().CancelRebind();
            if (!this.GetSystem<SettingsSystem>().ResetHotKeysToDefault()) Notify.ShowFloatTips(Texts.SettingsErrorSaveFailed);
            Refresh();
        }

        private void OnLanguageChanged(LanguageChangedEvent _)
        {
            Refresh();
        }

        private static string FormatAction(string actionId)
        {
            switch (actionId)
            {
                case HotKeyActionIds.DonateLife: return Loc.T("ui.arcade.coop.donate");
                case HotKeyActionIds.UseFieldUpgrade: return Loc.T("ui.arcade.slayermart.hotkey");
                case HotKeyActionIds.TogglePause: return Texts.HotkeyTogglePause;
                case HotKeyActionIds.ToggleRuntimeLog: return Texts.HotkeyToggleRuntimeLog;
                default: return Texts.HotkeyUnknown;
            }
        }
    }
}
